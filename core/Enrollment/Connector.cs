using System.Diagnostics;

namespace ByteBridge.Enrollment;

public enum ConnectorState
{
    /* cloudflared is not on this machine. */
    NotInstalled,

    /* Installed, but no Windows service runs a tunnel. */
    NoService,

    Stopped,

    Running
}

public interface IConnector
{
    ConnectorState Inspect();

    /*
     * Throws, changing nothing, when InstallAsync would refuse: no
     * cloudflared, or a connector service that may not be replaced. Asked
     * first, so nothing else is changed for an install that cannot happen.
     */
    Task EnsureCanInstallAsync(
        bool replaceExisting,
        CancellationToken cancellationToken);

    Task InstallAsync(
        string tunnelToken,
        bool replaceExisting,
        CancellationToken cancellationToken);

    Task UninstallAsync(CancellationToken cancellationToken);
}

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    public async Task<ProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        /*
         * ArgumentList, never one joined string, so a value is passed as
         * one argument whatever characters it holds.
         */
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new EnrollmentException($"Could not start {file}.");

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        limit.CancelAfter(Limit);

        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new EnrollmentException(
                $"{Path.GetFileName(file)} did not finish within "
                + $"{(int)Limit.TotalSeconds} seconds.");
        }

        return new ProcessResult(
            process.ExitCode,
            (await output + await error).Trim());
    }
}

/*
 * Hands the tunnel token to cloudflared, the way Cloudflare documents it
 * for a remotely managed tunnel: `cloudflared service install <token>`
 * makes a Windows service that starts with the machine and needs no
 * config file, which is what a gateway that runs with nobody logged in
 * needs.
 *
 * It refuses to replace a connector that is already there. A machine
 * that had a tunnel set up by hand would lose it silently otherwise, and
 * that tunnel may be the one somebody's storefront depends on today.
 */
public sealed class CloudflaredConnector : IConnector
{
    /*
     * The name cloudflared registers itself under.
     */
    private const string ServiceName = "Cloudflared";

    private readonly IProcessRunner _runner;
    private readonly Func<string?> _locate;
    private readonly IClock _clock;

    public CloudflaredConnector(
        IProcessRunner runner,
        IClock clock,
        Func<string?>? locate = null)
    {
        _runner = runner;
        _clock = clock;
        _locate = locate ?? Locate;
    }

    public ConnectorState Inspect()
    {
        if (_locate() == null)
        {
            return ConnectorState.NotInstalled;
        }

        return QueryService(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task EnsureCanInstallAsync(
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var exe = Executable();

        _ = exe;

        if (!replaceExisting
            && await QueryService(cancellationToken) != ConnectorState.NoService)
        {
            throw ExistingConnector();
        }
    }

    public async Task InstallAsync(
        string tunnelToken,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var exe = Executable();

        var state = await QueryService(cancellationToken);

        if (state != ConnectorState.NoService)
        {
            if (!replaceExisting)
            {
                throw ExistingConnector();
            }

            var removal = await _runner.RunAsync(
                exe,
                ["service", "uninstall"],
                cancellationToken);

            if (removal.ExitCode != 0)
            {
                throw Failure("remove the existing connector", removal, tunnelToken);
            }
        }

        var install = await _runner.RunAsync(
            exe,
            ["service", "install", tunnelToken],
            cancellationToken);

        if (install.ExitCode != 0)
        {
            throw Failure("install the connector", install, tunnelToken);
        }

        /*
         * The installer returning is not the tunnel being up. A service
         * that installed but did not start is the most common failure,
         * and reporting success for it would leave the customer with a
         * gateway nobody can reach.
         */
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (await QueryService(cancellationToken) == ConnectorState.Running)
            {
                return;
            }

            await _clock.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new EnrollmentException(
            "The connector was installed but its service is not running. "
            + "Check the Cloudflared service in Windows Services and the "
            + "Application event log.");
    }

    public async Task UninstallAsync(CancellationToken cancellationToken)
    {
        var exe = _locate();

        if (exe == null
            || await QueryService(cancellationToken) == ConnectorState.NoService)
        {
            return;
        }

        var result = await _runner.RunAsync(
            exe,
            ["service", "uninstall"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw Failure("remove the connector", result, string.Empty);
        }
    }

    private string Executable() =>
        _locate()
            ?? throw new EnrollmentException(
                "cloudflared is not installed. Install it from "
                + "https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/downloads/ "
                + "(ByteBridge's own installer offers it), then run this again.");

    private static EnrollmentException ExistingConnector() =>
        new(
            "A Cloudflare connector service already exists on this "
            + "machine and may be carrying another tunnel. Nothing was "
            + "changed. If it is safe to replace, run this again with "
            + "--replace-connector.",
            connectorConflict: true);

    private async Task<ConnectorState> QueryService(
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "sc.exe",
            ["query", ServiceName],
            cancellationToken);

        /*
         * 1060 is "the specified service does not exist as an installed
         * service", i.e. a machine with no connector service yet.
         */
        if (result.ExitCode == 1060)
        {
            return ConnectorState.NoService;
        }

        if (result.ExitCode != 0)
        {
            throw new EnrollmentException(
                $"Could not query the Cloudflared service: {result.Output}");
        }

        return result.Output.Contains("RUNNING", StringComparison.Ordinal)
            ? ConnectorState.Running
            : ConnectorState.Stopped;
    }

    /*
     * The token is a credential for the tunnel. cloudflared echoes its
     * arguments in some errors, so it is removed before the text goes
     * anywhere a person or a log can see it.
     */
    private static EnrollmentException Failure(
        string action,
        ProcessResult result,
        string token)
    {
        var output = token.Length > 0
            ? result.Output.Replace(token, "***")
            : result.Output;

        var hint = output.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? " Run this from an elevated (administrator) terminal."
            : string.Empty;

        return new EnrollmentException(
            $"cloudflared could not {action} (exit {result.ExitCode}): "
            + output
            + hint);
    }

    private static string? Locate()
    {
        var name = OperatingSystem.IsWindows()
            ? "cloudflared.exe"
            : "cloudflared";

        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "cloudflared"))
            .Append(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "cloudflared"));

        return folders
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
    }
}
