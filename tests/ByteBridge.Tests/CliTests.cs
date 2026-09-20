using Xunit;
using ByteBridge.Admin;
using ByteBridge.Configuration;
using ByteBridge.Data;

namespace ByteBridge.Tests;

/*
 * Console.Out/Error are process-wide statics. Any test class that
 * swaps them (this one, and CloudflareAccessValidatorTests) has to
 * run in the same xUnit collection, or two of them redirecting at
 * once on different threads stomp on each other's capture.
 */
[CollectionDefinition("Console redirection", DisableParallelization = true)]
public class ConsoleRedirectionCollection
{
}

/*
 * On Windows Server Core there is no desktop, so the WPF control panel
 * cannot run and these commands are the only way to configure the
 * gateway. If they are broken, the service installs, starts, and is
 * unreachable forever, which is worse than not shipping them.
 */
[Collection("Console redirection")]
public class CliTests
{
    private sealed class Captured : IDisposable
    {
        private readonly TextWriter _out;
        private readonly TextWriter _error;
        private readonly StringWriter _capturedOut = new();
        private readonly StringWriter _capturedError = new();

        public Captured()
        {
            _out = Console.Out;
            _error = Console.Error;

            Console.SetOut(_capturedOut);
            Console.SetError(_capturedError);
        }

        public string Out => _capturedOut.ToString();

        public string Error => _capturedError.ToString();

        public void Dispose()
        {
            Console.SetOut(_out);
            Console.SetError(_error);
        }
    }

    private static (int Code, string Out, string Error) Run(
        SqliteDatabase database,
        params string[] args)
    {
        using var captured = new Captured();

        var code = Cli.Run(args, database);

        return (code ?? -1, captured.Out, captured.Error);
    }

    [Fact]
    public void No_arguments_means_run_as_a_service()
    {
        using var root = new TempDataRoot();

        Assert.Null(Cli.Run([], root.OpenDatabase()));
    }

    [Fact]
    public void An_unknown_command_fails_and_prints_the_usage()
    {
        using var root = new TempDataRoot();

        var (code, _, error) = Run(root.OpenDatabase(), "frobnicate");

        Assert.Equal(1, code);
        Assert.Contains("unknown command", error);
        Assert.Contains("db add", error);
    }

    [Fact]
    public void Status_reports_an_empty_install_without_failing()
    {
        using var root = new TempDataRoot();

        var (code, output, _) = Run(root.OpenDatabase(), "status");

        Assert.Equal(0, code);
        Assert.Contains("No databases configured", output);
    }

    [Fact]
    public void Off_and_on_drive_whether_the_gateway_listens()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(database, "off").Code);
        Assert.False(database.GetGatewayConfig().AutoStart);

        Assert.Equal(0, Run(database, "on").Code);
        Assert.True(database.GetGatewayConfig().AutoStart);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("http")]
    [InlineData(null)]
    public void A_port_outside_the_valid_range_is_refused(string? port)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var before = database.GetGatewayConfig().Port;

        var args = port == null
            ? new[] { "port" }
            : ["port", port];

        var (code, _, error) = Run(database, args);

        Assert.Equal(1, code);
        Assert.Contains("1 to 65535", error);
        Assert.Equal(before, database.GetGatewayConfig().Port);
    }

    [Fact]
    public void Port_changes_the_address_the_gateway_will_bind()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, output, _) = Run(database, "port", "9090");

        Assert.Equal(0, code);
        Assert.Contains("127.0.0.1:9090", output);
        Assert.Equal(9090, database.GetGatewayConfig().Port);
    }

    [Fact]
    public void Key_show_prints_the_key_and_key_new_replaces_it()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var original = database.GetGatewayConfig().ApiKey;

        Assert.False(string.IsNullOrEmpty(original));

        var shown = Run(database, "key", "show");

        Assert.Equal(0, shown.Code);
        Assert.Equal(original, shown.Out.Trim());

        var rotated = Run(database, "key", "new");

        Assert.Equal(0, rotated.Code);
        Assert.NotEqual(original, rotated.Out.Trim());
        Assert.Equal(rotated.Out.Trim(), database.GetGatewayConfig().ApiKey);
    }

    [Fact]
    public void The_key_is_never_printed_by_status()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var key = database.GetGatewayConfig().ApiKey;

        var (_, output, _) = Run(database, "status");

        Assert.DoesNotContain(key, output);
    }

    [Fact]
    public void Db_add_requires_every_field_it_cannot_invent()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(
            database, "db", "add", "--name", "Sales", "--server", "127.0.0.1");

        Assert.Equal(1, code);
        Assert.Contains("--path", error);
        Assert.Empty(database.GetConnections());
    }

    [Fact]
    public void Db_add_then_disable_enable_and_remove()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(
            database, "db", "add",
            "--name", "Sales",
            "--server", "127.0.0.1",
            "--path", "/data/sales.fdb",
            "--user", "SYSDBA",
            "--password", "a pass with spaces").Code);

        var added = Assert.Single(database.GetConnections());

        Assert.Equal("Sales", added.Name);
        Assert.Equal(3050, added.Port);
        Assert.Equal("a pass with spaces", added.Password);
        Assert.True(added.Enabled);

        Assert.Equal(0, Run(database, "db", "disable", "Sales").Code);
        Assert.False(database.GetConnections().Single().Enabled);

        Assert.Equal(0, Run(database, "db", "enable", "Sales").Code);
        Assert.True(database.GetConnections().Single().Enabled);

        Assert.Equal(0, Run(database, "db", "remove", "Sales").Code);
        Assert.Empty(database.GetConnections());
    }

    [Fact]
    public void Db_add_takes_a_custom_firebird_port()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(
            database, "db", "add",
            "--name", "Alt",
            "--server", "db.internal",
            "--path", "/data/alt.fdb",
            "--user", "SYSDBA",
            "--password", "secret",
            "--port", "3051").Code);

        Assert.Equal(3051, database.GetConnections().Single().Port);
    }

    [Fact]
    public void Naming_a_database_that_does_not_exist_fails()
    {
        using var root = new TempDataRoot();

        var (code, _, error) = Run(root.OpenDatabase(), "db", "enable", "Ghost");

        Assert.Equal(1, code);
        Assert.Contains("Ghost", error);
    }

    [Fact]
    public void Help_prints_the_usage_and_succeeds()
    {
        using var root = new TempDataRoot();

        foreach (var flag in new[] { "--help", "-h", "help", "/?" })
        {
            var (code, output, _) = Run(root.OpenDatabase(), flag);

            Assert.Equal(0, code);
            Assert.Contains("db add", output);
        }
    }

    [Fact]
    public void Db_add_help_explains_how_to_add_one()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        // What status suggests when nothing is configured.
        var (code, output, _) = Run(database, "db", "add", "--help");

        Assert.Equal(0, code);
        Assert.Contains("--password", output);
        Assert.Empty(database.GetConnections());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    public void Db_add_refuses_a_firebird_port_outside_the_valid_range(string port)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(
            database, "db", "add",
            "--name", "Sales",
            "--server", "127.0.0.1",
            "--path", "/data/sales.fdb",
            "--user", "SYSDBA",
            "--password", "secret",
            "--port", port);

        Assert.Equal(1, code);
        Assert.Contains("1 to 65535", error);
        Assert.Empty(database.GetConnections());
    }

    [Fact]
    public void Db_add_refuses_a_name_already_in_use()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        string[] Add(string path) =>
        [
            "db", "add",
            "--name", "Sales",
            "--server", "127.0.0.1",
            "--path", path,
            "--user", "SYSDBA",
            "--password", "secret"
        ];

        Assert.Equal(0, Run(database, Add("/data/sales.fdb")).Code);

        var (code, _, error) = Run(database, Add("/data/other.fdb"));

        Assert.Equal(1, code);
        Assert.Contains("already named", error);
        Assert.Single(database.GetConnections());
    }

    // ---- oauth -------------------------------------------------------

    private static string[] OAuthSet(
        string teamDomain = "my-team.cloudflareaccess.com",
        string audience = "abc123",
        string publicHostname = "api.example.com") =>
    [
        "oauth", "set",
        "--team-domain", teamDomain,
        "--audience", audience,
        "--public-hostname", publicHostname
    ];

    [Fact]
    public void Oauth_set_stores_the_team_domain_audience_and_derived_urls()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, _) = Run(database, OAuthSet());

        var config = database.GetOAuthConfig();

        Assert.Equal(0, code);
        Assert.Equal("my-team.cloudflareaccess.com", config.TeamDomain);
        Assert.Equal("abc123", config.Audience);
        Assert.Equal(
            "https://my-team.cloudflareaccess.com/cdn-cgi/access/certs",
            config.JwksUrl);
        Assert.Equal("https://api.example.com/auth/callback", config.RedirectUri);
        Assert.False(config.Enabled);
    }

    [Theory]
    [InlineData("https://my-team.cloudflareaccess.com")]
    [InlineData("my-team.cloudflareaccess.com/path")]
    [InlineData("has space.example.com")]
    public void Oauth_set_refuses_a_team_domain_that_is_not_a_bare_hostname(string teamDomain)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, OAuthSet(teamDomain: teamDomain));

        Assert.Equal(1, code);
        Assert.Contains("bare hostname", error);
        Assert.Equal(string.Empty, database.GetOAuthConfig().TeamDomain);
    }

    [Fact]
    public void Oauth_set_needs_every_field()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(
            database, "oauth", "set", "--team-domain", "my-team.cloudflareaccess.com");

        Assert.Equal(1, code);
        Assert.Contains("--audience", error);
    }

    [Fact]
    public void Oauth_on_before_the_settings_exist_is_refused()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, "oauth", "on");

        Assert.Equal(1, code);
        Assert.Contains("oauth set", error);
        Assert.False(database.GetOAuthConfig().Enabled);
    }

    [Fact]
    public void Oauth_on_and_off_toggle_login_without_losing_the_settings()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Run(database, OAuthSet());

        Assert.Equal(0, Run(database, "oauth", "on").Code);
        Assert.True(database.GetOAuthConfig().Enabled);

        Assert.Equal(0, Run(database, "oauth", "off").Code);

        var config = database.GetOAuthConfig();

        Assert.False(config.Enabled);
        Assert.Equal("abc123", config.Audience);
    }

    [Fact]
    public void Oauth_show_reports_what_is_configured()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Run(database, OAuthSet());
        Run(database, "oauth", "on");

        var (code, output, _) = Run(database, "oauth", "show");

        Assert.Equal(0, code);
        Assert.Contains("on", output);
        Assert.Contains("my-team.cloudflareaccess.com", output);
        Assert.Contains("abc123", output);
    }
}
