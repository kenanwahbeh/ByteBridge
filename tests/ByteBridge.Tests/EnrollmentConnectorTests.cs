using Xunit;
using ByteBridge.Enrollment;
using static ByteBridge.Tests.EnrollmentFakes;

namespace ByteBridge.Tests;

public class EnrollmentConnectorTests
{
    private const string Token = "secret-tunnel-token";
    private const string Exe = @"C:\Program Files (x86)\cloudflared\cloudflared.exe";

    private readonly Runner _runner = new();
    private readonly Clock _clock = new();

    private CloudflaredConnector Connector(string? exe = Exe) =>
        new(_runner, _clock, () => exe, windows: true);

    private CloudflaredConnector LinuxConnector(string? exe = LinuxExe) =>
        new(_runner, _clock, () => exe, windows: false);

    private const string LinuxExe = "/usr/local/bin/cloudflared";

    private static ProcessResult UnitActive() =>
        new(0, "LoadState=loaded\nActiveState=active");

    private static ProcessResult UnitInactive() =>
        new(0, "LoadState=loaded\nActiveState=inactive");

    private static ProcessResult UnitAbsent() =>
        new(0, "LoadState=not-found\nActiveState=inactive");

    private static ProcessResult Running() =>
        new(0, "SERVICE_NAME: Cloudflared\r\n        STATE              : 4  RUNNING");

    private static ProcessResult Stopped() =>
        new(0, "SERVICE_NAME: Cloudflared\r\n        STATE              : 1  STOPPED");

    private static ProcessResult Absent() =>
        new(1060, "The specified service does not exist as an installed service.");

    [Fact]
    public async Task Installs_with_the_token_as_one_argument_and_waits_for_the_service()
    {
        var installed = false;

        _runner.Handler = (_, args) =>
        {
            if (args[0] == "query")
            {
                return installed ? Running() : Absent();
            }

            installed = true;
            return new ProcessResult(0, string.Empty);
        };

        await Connector().InstallAsync(Token, replaceExisting: false, default);

        var install = _runner.Calls.Single(c => c.Arguments[0] == "service");

        Assert.Equal(Exe, install.File);
        Assert.Equal(["service", "install", Token], install.Arguments);
    }

    [Fact]
    public async Task Refuses_to_replace_a_connector_that_is_already_there()
    {
        _runner.Handler = (_, args) => args[0] == "query" ? Running() : new ProcessResult(0, "");

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Connector().InstallAsync(Token, replaceExisting: false, default));

        Assert.Contains("--replace-connector", error.Message);
        Assert.DoesNotContain(_runner.Calls, c => c.Arguments[0] == "service");
    }

    [Fact]
    public async Task Replaces_it_when_told_to_uninstalling_first()
    {
        var installed = true;

        _runner.Handler = (_, args) =>
        {
            if (args[0] == "query")
            {
                return installed ? Running() : Absent();
            }

            installed = args[1] == "install";
            return new ProcessResult(0, "");
        };

        await Connector().InstallAsync(Token, replaceExisting: true, default);

        var verbs = _runner.Calls
            .Where(c => c.Arguments[0] == "service")
            .Select(c => c.Arguments[1])
            .ToArray();

        Assert.Equal(["uninstall", "install"], verbs);
    }

    [Fact]
    public async Task A_failed_install_never_shows_the_token()
    {
        _runner.Handler = (_, args) => args[0] == "query"
            ? Absent()
            : new ProcessResult(1, $"failed: cloudflared service install {Token}: Access is denied.");

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Connector().InstallAsync(Token, false, default));

        Assert.DoesNotContain(Token, error.Message);
        Assert.Contains("***", error.Message);
        Assert.Contains("elevated", error.Message);
    }

    [Fact]
    public async Task Reports_a_service_that_installed_but_did_not_start()
    {
        var installed = false;

        _runner.Handler = (_, args) =>
        {
            if (args[0] == "query")
            {
                return installed ? Stopped() : Absent();
            }

            installed = true;
            return new ProcessResult(0, "");
        };

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Connector().InstallAsync(Token, false, default));

        Assert.Contains("not running", error.Message);
        Assert.Equal(10, _clock.Delays);
    }

    [Fact]
    public async Task Says_where_to_get_cloudflared_when_it_is_missing()
    {
        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Connector(exe: null).InstallAsync(Token, false, default));

        Assert.Contains("not installed", error.Message);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Inspect_tells_the_four_states_apart()
    {
        Assert.Equal(ConnectorState.NotInstalled, Connector(exe: null).Inspect());

        _runner.Handler = (_, _) => Absent();
        Assert.Equal(ConnectorState.NoService, Connector().Inspect());

        _runner.Handler = (_, _) => Stopped();
        Assert.Equal(ConnectorState.Stopped, Connector().Inspect());

        _runner.Handler = (_, _) => Running();
        Assert.Equal(ConnectorState.Running, Connector().Inspect());
    }

    [Fact]
    public void A_service_query_that_fails_for_another_reason_is_an_error()
    {
        _runner.Handler = (_, _) => new ProcessResult(5, "Access is denied.");

        Assert.Throws<EnrollmentException>(() => Connector().Inspect());
    }

    [Fact]
    public async Task Uninstall_removes_the_service_when_there_is_one()
    {
        _runner.Handler = (_, args) => args[0] == "query" ? Running() : new ProcessResult(0, "");

        await Connector().UninstallAsync(default);

        Assert.Contains(_runner.Calls, c => c.Arguments is ["service", "uninstall"]);
    }

    [Fact]
    public async Task Uninstall_with_no_service_or_no_cloudflared_does_nothing()
    {
        _runner.Handler = (_, _) => Absent();

        await Connector().UninstallAsync(default);
        await Connector(exe: null).UninstallAsync(default);

        Assert.DoesNotContain(_runner.Calls, c => c.Arguments[0] == "service");
    }

    [Fact]
    public async Task A_failed_uninstall_is_reported()
    {
        _runner.Handler = (_, args) => args[0] == "query"
            ? Running()
            : new ProcessResult(1, "Access is denied.");

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => Connector().UninstallAsync(default));

        Assert.Contains("elevated", error.Message);
    }

    /* ---- Linux: the same flow, asked of systemd instead of sc.exe ---- */

    [Fact]
    public void Linux_inspect_reads_the_systemd_unit()
    {
        _runner.Handler = (_, _) => UnitAbsent();
        Assert.Equal(ConnectorState.NoService, LinuxConnector().Inspect());

        _runner.Handler = (_, _) => UnitInactive();
        Assert.Equal(ConnectorState.Stopped, LinuxConnector().Inspect());

        _runner.Handler = (_, _) => UnitActive();
        Assert.Equal(ConnectorState.Running, LinuxConnector().Inspect());

        Assert.All(_runner.Calls, c =>
        {
            Assert.Equal("systemctl", c.File);
            Assert.Equal("show", c.Arguments[0]);
            Assert.Equal("cloudflared.service", c.Arguments[1]);
        });
    }

    [Fact]
    public void Linux_without_cloudflared_is_not_installed_and_asks_nothing()
    {
        Assert.Equal(ConnectorState.NotInstalled, LinuxConnector(exe: null).Inspect());
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Linux_a_systemctl_failure_is_an_error_not_an_absent_unit()
    {
        _runner.Handler = (_, _) => new ProcessResult(1, "Failed to connect to bus");

        Assert.Throws<EnrollmentException>(() => LinuxConnector().Inspect());
    }

    [Fact]
    public async Task Linux_installs_with_the_token_as_one_argument_and_waits_for_the_unit()
    {
        var installed = false;

        _runner.Handler = (file, args) =>
        {
            if (file == "systemctl")
            {
                return installed ? UnitActive() : UnitAbsent();
            }

            installed = true;
            return new ProcessResult(0, string.Empty);
        };

        await LinuxConnector().InstallAsync(Token, replaceExisting: false, default);

        var install = _runner.Calls.Single(c => c.File == LinuxExe);

        Assert.Equal(["service", "install", Token], install.Arguments);
        Assert.DoesNotContain(_runner.Calls, c => c.File == "sc.exe");
    }

    [Fact]
    public async Task Linux_refuses_to_replace_a_connector_that_is_already_there()
    {
        _runner.Handler = (file, _) =>
            file == "systemctl" ? UnitActive() : new ProcessResult(0, "");

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => LinuxConnector().InstallAsync(Token, replaceExisting: false, default));

        Assert.Contains("--replace-connector", error.Message);
        Assert.DoesNotContain(_runner.Calls, c => c.File == LinuxExe);
    }

    [Fact]
    public async Task Linux_replaces_it_when_told_to_uninstalling_first()
    {
        var installed = true;

        _runner.Handler = (file, args) =>
        {
            if (file == "systemctl")
            {
                return installed ? UnitActive() : UnitAbsent();
            }

            installed = args[1] == "install";
            return new ProcessResult(0, "");
        };

        await LinuxConnector().InstallAsync(Token, replaceExisting: true, default);

        var verbs = _runner.Calls
            .Where(c => c.File == LinuxExe)
            .Select(c => c.Arguments[1])
            .ToArray();

        Assert.Equal(["uninstall", "install"], verbs);
    }

    [Fact]
    public async Task Linux_reports_a_unit_that_installed_but_did_not_start_and_where_to_look()
    {
        var installed = false;

        _runner.Handler = (file, _) =>
        {
            if (file == "systemctl")
            {
                return installed ? UnitInactive() : UnitAbsent();
            }

            installed = true;
            return new ProcessResult(0, "");
        };

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => LinuxConnector().InstallAsync(Token, false, default));

        Assert.Contains("journalctl -u cloudflared", error.Message);
        Assert.DoesNotContain("Windows", error.Message);
    }

    [Fact]
    public async Task Linux_a_failed_install_never_shows_the_token()
    {
        _runner.Handler = (file, _) => file == "systemctl"
            ? UnitAbsent()
            : new ProcessResult(1, $"failed: cloudflared service install {Token}");

        var error = await Assert.ThrowsAsync<EnrollmentException>(
            () => LinuxConnector().InstallAsync(Token, false, default));

        Assert.DoesNotContain(Token, error.Message);
        Assert.Contains("***", error.Message);
    }

    [Fact]
    public async Task Linux_uninstall_removes_the_unit_when_there_is_one_and_does_nothing_otherwise()
    {
        _runner.Handler = (file, _) =>
            file == "systemctl" ? UnitActive() : new ProcessResult(0, "");

        await LinuxConnector().UninstallAsync(default);

        Assert.Contains(_runner.Calls, c => c.File == LinuxExe && c.Arguments is ["service", "uninstall"]);

        _runner.Calls.Clear();
        _runner.Handler = (_, _) => UnitAbsent();

        await LinuxConnector().UninstallAsync(default);

        Assert.DoesNotContain(_runner.Calls, c => c.File == LinuxExe);
    }
}
