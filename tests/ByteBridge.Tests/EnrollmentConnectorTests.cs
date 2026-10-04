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
        new(_runner, _clock, () => exe);

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
}
