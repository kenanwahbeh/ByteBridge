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
    public void Lockout_show_reports_the_defaults()
    {
        using var root = new TempDataRoot();

        var (code, output, _) = Run(root.OpenDatabase(), "lockout");

        Assert.Equal(0, code);
        Assert.Contains("10 wrong keys in 60s", output);
        Assert.Contains("blocks a caller for 60s", output);
    }

    [Fact]
    public void Lockout_set_changes_only_what_it_is_given()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, _) = Run(database, "lockout", "set", "--attempts", "5", "--block", "900");

        Assert.Equal(0, code);

        var config = database.GetGatewayConfig();

        Assert.Equal(5, config.AuthMaxFailures);
        Assert.Equal(60, config.AuthWindowSeconds);
        Assert.Equal(900, config.AuthBlockSeconds);
    }

    [Fact]
    public void Lockout_off_and_on_switch_the_limit_and_on_restores_the_default()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(database, "lockout", "off").Code);
        Assert.Equal(0, database.GetGatewayConfig().AuthMaxFailures);

        var (_, shown, _) = Run(database, "lockout", "show");
        Assert.Equal("off", shown.Trim());

        Assert.Equal(0, Run(database, "lockout", "on").Code);
        Assert.Equal(10, database.GetGatewayConfig().AuthMaxFailures);
    }

    [Fact]
    public void Lockout_on_keeps_a_limit_that_was_already_chosen()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Run(database, "lockout", "set", "--attempts", "4");
        Run(database, "lockout", "on");

        Assert.Equal(4, database.GetGatewayConfig().AuthMaxFailures);
    }

    [Theory]
    [InlineData("--attempts", "0")]
    [InlineData("--attempts", "10001")]
    [InlineData("--attempts", "lots")]
    [InlineData("--window", "0")]
    [InlineData("--block", "86401")]
    [InlineData("--nonsense", "1")]
    public void Lockout_set_refuses_values_out_of_range_and_saves_nothing(
        string option,
        string value)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, "lockout", "set", option, value);

        Assert.Equal(1, code);
        Assert.Contains("error:", error);

        var config = database.GetGatewayConfig();

        Assert.Equal(10, config.AuthMaxFailures);
        Assert.Equal(60, config.AuthWindowSeconds);
        Assert.Equal(60, config.AuthBlockSeconds);
    }

    [Theory]
    [InlineData("--attempts", "5", "garbage")]
    [InlineData("garbage", "--attempts", "5")]
    [InlineData("--attempts", "5", "6")]
    public void Lockout_set_refuses_an_argument_it_does_not_understand_and_saves_nothing(
        string first,
        string second,
        string third)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, "lockout", "set", first, second, third);

        Assert.Equal(1, code);
        Assert.Contains("does not understand", error);
        Assert.Equal(10, database.GetGatewayConfig().AuthMaxFailures);
    }

    [Fact]
    public void Lockout_set_with_nothing_to_set_is_an_error()
    {
        using var root = new TempDataRoot();

        var (code, _, error) = Run(root.OpenDatabase(), "lockout", "set");

        Assert.Equal(1, code);
        Assert.Contains("at least one", error);
    }

    [Fact]
    public void Status_shows_the_lockout()
    {
        using var root = new TempDataRoot();

        var (_, output, _) = Run(root.OpenDatabase(), "status");

        Assert.Contains("lockout", output);
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
    public void Db_add_with_a_type_uses_that_engines_port_and_keeps_the_type()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(
            database, "db", "add",
            "--name", "Shop",
            "--type", "postgresql",
            "--server", "10.0.0.5",
            "--database", "shop",
            "--user", "app",
            "--password", "secret").Code);

        var added = Assert.Single(database.GetConnections());

        Assert.Equal(DatabaseType.PostgreSql, added.Type);
        Assert.Equal(5432, added.Port);
        Assert.Equal("shop", added.Database);

        var (_, output, _) = Run(database, "db", "list");

        Assert.Contains("PostgreSQL", output);
    }

    [Fact]
    public void Db_add_for_sql_server_defaults_to_1433()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.Equal(0, Run(
            database, "db", "add",
            "--name", "Erp", "--type", "sqlserver", "--server", "erp",
            "--path", "erp", "--user", "reader", "--password", "x").Code);

        Assert.Equal(1433, database.GetConnections().Single().Port);
    }

    [Fact]
    public void Db_add_refuses_an_engine_it_does_not_know()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(
            database, "db", "add",
            "--name", "X", "--type", "oracle", "--server", "h",
            "--path", "p", "--user", "u", "--password", "x");

        Assert.Equal(1, code);
        Assert.Contains("--type", error);
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

    // ---- The writes switch --------------------------------------------

    [Fact]
    public void Writing_is_off_until_someone_turns_it_on()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.False(database.GetAllowWrites());

        var (code, output, _) = Run(database, "writes");

        Assert.Equal(0, code);
        Assert.Equal("off", output.Trim());
    }

    [Fact]
    public void Writes_on_and_off_move_the_switch_and_say_so()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, output, _) = Run(database, "writes", "on");

        Assert.Equal(0, code);
        Assert.True(database.GetAllowWrites());
        Assert.Contains("Writing is ON", output);
        Assert.Equal("on", Run(database, "writes", "show").Out.Trim());

        Assert.Equal(0, Run(database, "writes", "off").Code);
        Assert.False(database.GetAllowWrites());
    }

    [Fact]
    public void Writes_refuses_a_word_it_does_not_know_and_leaves_the_switch_alone()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, "writes", "maybe");

        Assert.Equal(1, code);
        Assert.Contains("'show', 'on' or 'off'", error);
        Assert.False(database.GetAllowWrites());
    }

    [Fact]
    public void Writes_refuses_extra_words_rather_than_ignoring_them()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, error) = Run(database, "writes", "on", "please");

        Assert.Equal(1, code);
        Assert.Contains("please", error);
        Assert.False(database.GetAllowWrites());
    }

    [Fact]
    public void Status_mentions_writing_only_while_it_is_on()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.DoesNotContain("writing", Run(database, "status").Out);

        database.SetAllowWrites(true);

        Assert.Contains("writing", Run(database, "status").Out);
    }

    [Fact]
    public void The_usage_does_not_advertise_the_switch()
    {
        Assert.DoesNotContain("writes", Cli.Usage);
        Assert.DoesNotContain("writing", Cli.Usage);
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
