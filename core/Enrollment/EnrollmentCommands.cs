using System.Text.RegularExpressions;
using ByteBridge.Data;

namespace ByteBridge.Enrollment;

/*
 * Everything the enrolment needs from the outside world, gathered in one
 * place so the command line, the control panel and the tests each build
 * the flow from the same parts.
 */
public sealed class EnrollmentServices
{
    public required IEnrollmentApi Api { get; init; }

    public required IConnector Connector { get; init; }

    public required IClock Clock { get; init; }

    public required IMachineId Machine { get; init; }

    public required Func<bool> IsElevated { get; init; }

    public required TextWriter Out { get; init; }

    public required TextWriter Error { get; init; }

    public static EnrollmentServices Create(
        TextWriter? output = null,
        TextWriter? error = null)
    {
        var clock = new SystemClock();

        return new EnrollmentServices
        {
            Api = new HttpEnrollmentApi(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            }),
            Connector = new CloudflaredConnector(
                new SystemProcessRunner(),
                clock),
            Clock = clock,
            Machine = new SystemMachineId(),
            IsElevated = () => Environment.IsPrivilegedProcess,
            Out = output ?? Console.Out,
            Error = error ?? Console.Error
        };
    }

    public EnrollmentFlow CreateFlow(SqliteDatabase database) =>
        new(
            Api,
            new SettingsEnrollmentStore(database),
            Connector,
            new DatabaseGatewaySettings(database),
            Machine,
            Clock,
            Out);
}

/*
 * The enrolment verbs of the service command line, kept out of Cli.cs so
 * that file stays a list of small commands. They follow its conventions:
 * "--key value" options, "error:" on stderr, exit code 0 or 1 (2 for
 * "still waiting for approval").
 */
public static partial class EnrollmentCommands
{
    public const string DefaultServer = "https://bytebalancetech.com";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public static bool Handles(string verb) =>
        verb is "enroll" or "claim" or "enrollment" or "sync-key" or "unenroll";

    public static int Run(
        string[] args,
        SqliteDatabase database,
        EnrollmentServices? services = null)
    {
        services ??= EnrollmentServices.Create();

        using var cancellation = new CancellationTokenSource();

        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            return RunAsync(args, database, services, cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (EnrollmentException failure)
        {
            services.Error.WriteLine("error: " + failure.Message);
            return ExitCode.Error;
        }
        catch (OperationCanceledException)
        {
            services.Error.WriteLine(
                "Stopped. Nothing is lost: run `claim` to continue.");

            return ExitCode.Error;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    private static async Task<int> RunAsync(
        string[] args,
        SqliteDatabase database,
        EnrollmentServices services,
        CancellationToken cancellationToken)
    {
        var verb = args[0].ToLowerInvariant();
        var options = ParseOptions(args);

        var flow = services.CreateFlow(database);

        switch (verb)
        {
            case "enroll":
                RequireElevation(services);

                return await flow.EnrollAsync(
                    ParseEnroll(options, services.Machine),
                    cancellationToken);

            case "claim":
                RequireElevation(services);

                return await flow.ClaimAsync(
                    new ClaimOptions(
                        Timeout(options),
                        options.ContainsKey("replace-connector")),
                    cancellationToken);

            case "unenroll":
                RequireElevation(services);

                return await flow.UnenrollAsync(cancellationToken);

            case "sync-key":
                return await flow.SyncKeyAsync(cancellationToken);

            default:
                return await flow.StatusAsync(cancellationToken);
        }
    }

    /*
     * After `key new`: if this machine is connected, ByteBalance still
     * holds the old key and the storefront is locked out, so the new one
     * is sent straight away. Says nothing on a machine that is not
     * enrolled, and never fails the rotation.
     */
    public static void AfterKeyRotation(
        SqliteDatabase database,
        EnrollmentServices? services = null)
    {
        if (new SettingsEnrollmentStore(database).Load() is not { Phase: Phase.Connected })
        {
            return;
        }

        services ??= EnrollmentServices.Create();

        services.CreateFlow(database)
            .TrySyncKeyAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    /*
     * The one line `status` shows. It reads only the settings file, never
     * asks Windows about services, so `status` stays as safe to run
     * anywhere as it always was.
     */
    public static string Summary(SqliteDatabase database)
    {
        var state = new SettingsEnrollmentStore(database).Load();

        return state switch
        {
            null => "not enrolled with ByteBalance (enroll --help)",
            { Phase: Phase.Connected } => $"connected as {state.Hostname}",
            _ => "waiting for ByteBalance to approve (run: claim)"
        };
    }

    /*
     * Failing here, before anything is sent, beats failing at the last
     * step: an unelevated `enroll` would otherwise spend the approver's
     * time on a request that cannot finish.
     */
    private static void RequireElevation(EnrollmentServices services)
    {
        if (!services.IsElevated())
        {
            throw new EnrollmentException(
                "This needs administrator rights (it installs a Windows "
                + "service). Open an elevated terminal and run it again.");
        }
    }

    /*
     * For the control panel, which has an email box and nothing else: the
     * same checks and defaults as `enroll --email`.
     */
    public static EnrollOptions EnrollOptionsFor(string email, TimeSpan timeout) =>
        ParseEnroll(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = email,
                ["timeout"] = ((int)timeout.TotalMinutes).ToString()
            },
            new SystemMachineId());

    internal static EnrollOptions ParseEnroll(
        IReadOnlyDictionary<string, string> options,
        IMachineId machine)
    {
        var email = options.GetValueOrDefault("email", string.Empty).Trim();

        if (!IsAcceptableEmail(email))
        {
            throw new EnrollmentException(
                "enroll needs --email <address>: the one person who will be "
                + "let through to this machine's tunnel.");
        }

        var name = options.GetValueOrDefault("name", string.Empty).Trim();

        if (name.Length == 0)
        {
            name = Environment.MachineName;
        }

        if (name.Length > 80)
        {
            throw new EnrollmentException("--name is at most 80 characters.");
        }

        var server = options.GetValueOrDefault("server", string.Empty).Trim();

        if (server.Length == 0)
        {
            server = Environment.GetEnvironmentVariable("BYTEBALANCE_URL")?.Trim()
                     ?? string.Empty;
        }

        if (server.Length == 0)
        {
            server = DefaultServer;
        }

        return new EnrollOptions(
            Server: ValidateServer(server),
            Email: email,
            Name: name,
            Wait: !options.ContainsKey("no-wait"),
            Timeout: Timeout(options),
            ReplaceConnector: options.ContainsKey("replace-connector"));
    }

    /*
     * The claim secret, the tunnel token and the API key all cross this
     * connection, so plain http is only accepted to a loopback address,
     * for development against a local server.
     */
    internal static string ValidateServer(string server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new EnrollmentException(
                "--server must be an https:// address (http:// only for this machine).");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    internal static TimeSpan Timeout(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("timeout", out var text))
        {
            return DefaultTimeout;
        }

        if (!int.TryParse(text, out var minutes) || minutes is < 1 or > 1440)
        {
            throw new EnrollmentException(
                "--timeout takes a number of minutes from 1 to 1440.");
        }

        return TimeSpan.FromMinutes(minutes);
    }

    /*
     * Reads --key value pairs, and bare --flags, after the verb.
     */
    internal static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];

            if (i + 1 < args.Length
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[++i];
            }
            else
            {
                options[key] = string.Empty;
            }
        }

        return options;
    }

    /*
     * A quick check to fail before the network. The control plane
     * validates strictly and has the last word.
     */
    [GeneratedRegex(@"^[^\s@<>()\[\]\\,;:""]+@[^\s@<>()\[\]\\,;:""]+\.[^\s@<>()\[\]\\,;:"".]{2,}$")]
    private static partial Regex Address();

    /*
     * Whether this is an address worth attempting, asked before anything
     * is built rather than thrown afterwards.
     *
     * The control panel asked a different question -- Contains('@') --
     * and "a@" passes it, so EnrollOptionsFor threw EnrollmentException
     * from inside an async void handler, outside the try that catches
     * it. That is an unhandled exception on the dispatcher, and the app's
     * own handler for those says ByteBridge hit an unexpected error and
     * needs to close. Typing an incomplete address and pressing the
     * button closed the control panel.
     *
     * Same rule as ParseEnroll, so the panel and the CLI cannot disagree
     * about what an address is.
     */
    public static bool IsAcceptableEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && Address().IsMatch(email.Trim());
}
