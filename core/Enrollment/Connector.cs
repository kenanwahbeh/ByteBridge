using System.Diagnostics;

namespace ByteBridge.Enrollment;

public enum ConnectorState
{
    /* cloudflared is not on this machine. */
    NotInstalled,

    /* Installed, but no service (Windows service, systemd unit) runs a tunnel. */
    NoService,

    Stopped,

    Running
}

public interface IConnector
{
    ConnectorState Inspect();

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
 * makes a service that starts with the machine and needs no config
 * file, which is what a gateway that runs with nobody logged in needs.
 * On Windows that is a Windows service; on Linux it is the systemd unit
 * cloudflared writes itself, so the same command serves both and only
 * the question "is it there, is it running" is asked differently.
 *
 * It refuses to replace a connector that is already there. A machine
 * that had a tunnel set up by hand would lose it silently otherwise, and
 * that tunnel may be the one somebody's storefront depends on today.
 */
public sealed class CloudflaredConnector : IConnector
{
    /*
     * The names cloudflared registers itself under: the Windows service,
     * and the systemd unit.
     */
    private const string ServiceName = "Cloudflared";
    private const string UnitName = "cloudflared.service";

    private readonly IProcessRunner _runner;
    private readonly Func<string?> _locate;
    private readonly IClock _clock;
    private readonly bool _windows;

    public CloudflaredConnector(
        IProcessRunner runner,
        IClock clock,
        Func<string?>? locate = null,
        bool? windows = null)
    {
        _runner = runner;
        _clock = clock;
        _locate = locate ?? Locate;
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    public ConnectorState Inspect()
    {
        if (_locate() == null)
        {
            return ConnectorState.NotInstalled;
        }

        return QueryService(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task InstallAsync(
        string tunnelToken,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var exe = _locate()
            ?? throw new EnrollmentException(
                "cloudflared is not installed. Install it from "
                + "https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/downloads/ "
                + "(ByteBridge's own installer offers it), then run this again.");

        var state = await QueryService(cancellationToken);

        if (state != ConnectorState.NoService)
        {
            if (!replaceExisting)
            {
                throw new EnrollmentException(
                    "A Cloudflare connector service already exists on this "
                    + "machine and may be carrying another tunnel. Nothing was "
                    + "changed. If it is safe to replace, run this again with "
                    + "--replace-connector.");
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
            + (_windows
                ? "Check the Cloudflared service in Windows Services and the "
                    + "Application event log."
                : "See why with: journalctl -u cloudflared -n 50"));
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

    private Task<ConnectorState> QueryService(
        CancellationToken cancellationToken) =>
        _windows
            ? QueryWindowsService(cancellationToken)
            : QuerySystemdUnit(cancellationToken);

    private async Task<ConnectorState> QueryWindowsService(
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
     * `systemctl show` exits 0 for a unit that does not exist and says so
     * in LoadState, which is what tells "no connector yet" apart from a
     * failure to ask. It needs no privileges.
     */
    private async Task<ConnectorState> QuerySystemdUnit(
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "systemctl",
            ["show", UnitName, "--property=LoadState", "--property=ActiveState"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new EnrollmentException(
                $"Could not query the cloudflared service: {result.Output}");
        }

        var properties = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

        if (!properties.TryGetValue("LoadState", out var load)
            || load == "not-found")
        {
            return ConnectorState.NoService;
        }

        return properties.GetValueOrDefault("ActiveState") == "active"
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
                "cloudflared"))
            .Append("/usr/local/bin")
            .Append("/usr/bin");

        /*
         * Absolute folders only. Where a Program Files folder does not
         * exist the combine above leaves a bare "cloudflared", which
         * would be looked up relative to wherever this runs.
         */
        return folders
            .Where(Path.IsPathRooted)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
    }
}
