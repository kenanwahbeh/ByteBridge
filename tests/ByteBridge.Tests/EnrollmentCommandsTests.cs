using Xunit;
using ByteBridge.Admin;
using ByteBridge.Data;
using ByteBridge.Enrollment;
using static ByteBridge.Tests.EnrollmentFakes;

namespace ByteBridge.Tests;

public class EnrollmentDeviceIdentityTests
{
    [Fact]
    public void The_device_key_is_stable_for_a_machine_and_fits_the_control_plane()
    {
        var key = DeviceIdentity.DeviceKey("abc");

        Assert.Equal(key, DeviceIdentity.DeviceKey("abc"));
        Assert.Equal(key, DeviceIdentity.DeviceKey("  abc  "));
        Assert.NotEqual(key, DeviceIdentity.DeviceKey("abd"));
        Assert.InRange(key.Length, 8, 128);
        Assert.StartsWith("bb-", key);
    }

    [Fact]
    public void The_device_key_does_not_contain_the_machine_id()
    {
        Assert.DoesNotContain("11111111-2222", DeviceIdentity.DeviceKey("11111111-2222-3333"));
    }

    [Fact]
    public void An_empty_machine_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => DeviceIdentity.DeviceKey("  "));
    }

    [Fact]
    public void Secrets_are_random_and_url_safe()
    {
        var a = DeviceIdentity.NewClaimSecret();
        var b = DeviceIdentity.NewClaimSecret();

        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
    }

    [Fact]
    public void The_hash_is_a_lower_case_sha256_hex_digest()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            DeviceIdentity.Sha256Hex("abc"));
    }
}

public class EnrollmentOptionsTests
{
    private static EnrollOptions Parse(params string[] args) =>
        EnrollmentCommands.ParseEnroll(
            EnrollmentCommands.ParseOptions(["enroll", .. args]),
            new Machine());

    [Fact]
    public void Defaults_to_the_production_server_and_the_machine_name()
    {
        Environment.SetEnvironmentVariable("BYTEBALANCE_URL", null);

        var options = Parse("--email", "owner@example.com");

        Assert.Equal("https://bytebalancetech.com", options.Server);
        Assert.Equal(Environment.MachineName, options.Name);
        Assert.True(options.Wait);
        Assert.Equal(TimeSpan.FromMinutes(30), options.Timeout);
        Assert.False(options.ReplaceConnector);
    }

    [Fact]
    public void Reads_every_option()
    {
        var options = Parse(
            "--email", "owner@example.com", "--name", "Acme Sales",
            "--server", "https://x.example.com", "--timeout", "5",
            "--no-wait", "--replace-connector");

        Assert.Equal("Acme Sales", options.Name);
        Assert.Equal("https://x.example.com", options.Server);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
        Assert.False(options.Wait);
        Assert.True(options.ReplaceConnector);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@b.com, c@d.com")]
    [InlineData("a b@c.com")]
    [InlineData("<a@b.com>")]
    public void A_missing_or_bad_email_is_refused_before_anything_is_sent(string email)
    {
        Assert.Throws<EnrollmentException>(() => Parse("--email", email));
    }

    [Fact]
    public void No_email_at_all_is_refused()
    {
        Assert.Throws<EnrollmentException>(() => Parse("--name", "x"));
    }

    [Theory]
    [InlineData("http://bytebalancetech.com")]
    [InlineData("ftp://x.com")]
    [InlineData("bytebalancetech.com")]
    [InlineData("https://")]
    public void Only_https_servers_are_accepted(string server)
    {
        Assert.Throws<EnrollmentException>(() => EnrollmentCommands.ValidateServer(server));
    }

    [Fact]
    public void Plain_http_is_allowed_only_to_this_machine()
    {
        Assert.Equal("http://127.0.0.1:8787", EnrollmentCommands.ValidateServer("http://127.0.0.1:8787"));
        Assert.Equal("http://localhost:8787", EnrollmentCommands.ValidateServer("http://localhost:8787/x"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1441")]
    [InlineData("soon")]
    public void A_bad_timeout_is_refused(string timeout)
    {
        Assert.Throws<EnrollmentException>(() =>
            EnrollmentCommands.Timeout(EnrollmentCommands.ParseOptions(["claim", "--timeout", timeout])));
    }

    [Fact]
    public void A_name_over_80_characters_is_refused()
    {
        Assert.Throws<EnrollmentException>(() =>
            Parse("--email", "owner@example.com", "--name", new string('x', 81)));
    }
}

/*
 * The state and the gateway settings are saved in the real settings
 * file, so these run against a throwaway one.
 */
public class EnrollmentSettingsTests
{
    private static EnrollmentState Sample() => new(
        "https://bytebalancetech.com", "bb-1", "secret", "Acme", "owner@example.com",
        Phase.Connected, new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero),
        "a.techn0.dpdns.org",
        new DateTimeOffset(2026, 9, 22, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 22, 2, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Nothing_saved_loads_as_null()
    {
        using var root = new TempDataRoot();

        Assert.Null(new SettingsEnrollmentStore(root.OpenDatabase()).Load());
    }

    [Fact]
    public void Round_trips_every_field()
    {
        using var root = new TempDataRoot();
        var store = new SettingsEnrollmentStore(root.OpenDatabase());

        store.Create(Sample());

        Assert.Equal(Sample(), store.Load());
    }

    [Fact]
    public void Optional_fields_survive_being_empty()
    {
        using var root = new TempDataRoot();
        var store = new SettingsEnrollmentStore(root.OpenDatabase());
        var pending = Sample() with
        {
            Phase = Phase.Requested,
            Hostname = null,
            ConnectedAt = null,
            KeySharedAt = null
        };

        store.Create(pending);

        Assert.Equal(pending, store.Load());
    }

    [Fact]
    public void A_second_process_sees_what_the_first_saved()
    {
        using var root = new TempDataRoot();

        new SettingsEnrollmentStore(root.OpenDatabase()).Create(Sample());

        Assert.Equal(Sample(), new SettingsEnrollmentStore(root.OpenDatabase()).Load());
    }

    [Fact]
    public void Clear_forgets_the_enrolment_and_only_the_enrolment()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var store = new SettingsEnrollmentStore(database);

        store.Create(Sample());
        var key = database.GetGatewayConfig().ApiKey;

        store.Clear();

        Assert.Null(store.Load());
        Assert.Equal(key, database.GetGatewayConfig().ApiKey);
    }

    [Fact]
    public void A_half_written_set_is_an_error_never_a_fresh_start()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.SetSetting("Enrollment.Server", "https://bytebalancetech.com");

        var error = Assert.Throws<EnrollmentException>(
            () => new SettingsEnrollmentStore(database).Load());

        Assert.Contains("incomplete", error.Message);
    }

    [Fact]
    public void Delete_settings_takes_a_prefix_literally()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.SetSetting("A_.one", "1");
        database.SetSetting("AXX.two", "2");

        database.DeleteSettings("A_.");

        Assert.Null(database.GetSetting("A_.one"));
        Assert.Equal("2", database.GetSetting("AXX.two"));
    }

    private static EnrollmentState SampleState() =>
        new("https://x.test", "bb-1", "secret", "Acme", "o@example.com",
            Phase.Requested, DateTimeOffset.UtcNow);

    [Fact]
    public void A_late_save_cannot_bring_back_an_enrolment_that_was_removed()
    {
        using var root = new TempDataRoot();
        var store = new SettingsEnrollmentStore(root.OpenDatabase());

        store.Create(SampleState());
        store.Clear();

        Assert.Throws<EnrollmentException>(
            () => store.Save(SampleState() with { Phase = Phase.Connected }));

        Assert.Null(store.Load());
    }

    [Fact]
    public void A_second_enrolment_cannot_overwrite_the_first()
    {
        using var root = new TempDataRoot();
        var store = new SettingsEnrollmentStore(root.OpenDatabase());

        store.Create(SampleState());

        Assert.Throws<EnrollmentException>(
            () => store.Create(SampleState() with { ClaimSecret = "other" }));

        Assert.Equal("secret", store.Load()!.ClaimSecret);
    }

    [Fact]
    public void Saving_an_enrolment_keeps_every_field_together()
    {
        using var root = new TempDataRoot();
        var store = new SettingsEnrollmentStore(root.OpenDatabase());

        store.Create(SampleState());
        store.Save(SampleState() with
        {
            Phase = Phase.Connected,
            Hostname = "acme.example.org",
            EdgeAccessOwned = true
        });

        var loaded = store.Load()!;

        Assert.Equal(Phase.Connected, loaded.Phase);
        Assert.Equal("acme.example.org", loaded.Hostname);
        Assert.True(loaded.EdgeAccessOwned);
    }

    [Fact]
    public void Requiring_edge_access_saves_the_team_and_audience_and_leaves_login_alone()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var settings = new DatabaseGatewaySettings(database);

        Assert.Equal(EdgeAccessResult.Applied, settings.RequireEdgeAccess("team.cloudflareaccess.com", "aud-1"));

        var config = database.GetOAuthConfig();

        Assert.True(config.RequireEdgeAccess);
        Assert.False(config.Enabled);
        Assert.Equal("team.cloudflareaccess.com", config.TeamDomain);
        Assert.Equal("aud-1", config.Audience);
        Assert.Equal("https://team.cloudflareaccess.com/cdn-cgi/access/certs", config.JwksUri);
    }

    [Fact]
    public void Login_for_another_application_is_never_overwritten()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var existing = database.GetOAuthConfig();
        existing.Enabled = true;
        existing.TeamDomain = "mine.cloudflareaccess.com";
        existing.Audience = "my-own-app";
        existing.RedirectUri = "https://api.example.com/auth/callback";
        database.SaveOAuthConfig(existing);

        Assert.Equal(EdgeAccessResult.OtherApplication, new DatabaseGatewaySettings(database)
            .RequireEdgeAccess("team.cloudflareaccess.com", "aud-1"));

        var after = database.GetOAuthConfig();

        Assert.False(after.RequireEdgeAccess);
        Assert.Equal("my-own-app", after.Audience);
    }

    [Fact]
    public void Login_for_the_same_application_can_share_the_requirement()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var existing = database.GetOAuthConfig();
        existing.Enabled = true;
        existing.TeamDomain = "Team.CloudflareAccess.com";
        existing.Audience = "aud-1";
        database.SaveOAuthConfig(existing);

        Assert.Equal(EdgeAccessResult.Applied, new DatabaseGatewaySettings(database)
            .RequireEdgeAccess("team.cloudflareaccess.com", "aud-1"));
    }

    [Fact]
    public void Clearing_the_requirement_keeps_login_settings_that_login_still_uses()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var settings = new DatabaseGatewaySettings(database);

        var config = database.GetOAuthConfig();
        config.Enabled = true;
        config.TeamDomain = "team.cloudflareaccess.com";
        config.Audience = "aud-1";
        database.SaveOAuthConfig(config);

        settings.RequireEdgeAccess("team.cloudflareaccess.com", "aud-1");
        settings.ClearEdgeAccess();

        var after = database.GetOAuthConfig();

        Assert.False(after.RequireEdgeAccess);
        Assert.Equal("aud-1", after.Audience);
    }

    [Fact]
    public void Clearing_the_requirement_when_login_is_off_forgets_the_tunnels_application()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var settings = new DatabaseGatewaySettings(database);

        settings.RequireEdgeAccess("team.cloudflareaccess.com", "aud-1");
        settings.ClearEdgeAccess();

        var after = database.GetOAuthConfig();

        Assert.False(after.RequireEdgeAccess);
        Assert.Equal(string.Empty, after.Audience);
        Assert.Equal(string.Empty, after.TeamDomain);
    }

    [Fact]
    public void The_gateway_settings_reflect_the_real_configuration()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();
        var settings = new DatabaseGatewaySettings(database);
        var config = database.GetGatewayConfig();

        Assert.Equal(config.ApiKey, settings.ApiKey);
        Assert.Equal(config.BaseUrl, settings.Address);
        Assert.True(settings.Listening);
    }
}

[Collection("Console redirection")]
public class EnrollmentCliTests
{
    private readonly Api _api = new();
    private readonly Connector _connector = new();
    private readonly Clock _clock = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    private EnrollmentServices Services(bool elevated = true) =>
        EnrollmentFakes.Services(_api, _connector, _clock, _out, _err, elevated);

    /* Console output for the commands that write to it directly. */
    private static (int? Code, string Out, string Error) Run(
        SqliteDatabase database,
        EnrollmentServices services,
        params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var capturedOut = new StringWriter();
        var capturedError = new StringWriter();

        Console.SetOut(capturedOut);
        Console.SetError(capturedError);

        try
        {
            var code = Cli.Run(args, database, services);

            return (code, capturedOut.ToString(), capturedError.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Enroll_needs_an_elevated_terminal_and_sends_nothing_without_it()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (code, _, _) = Run(database, Services(elevated: false), "enroll", "--email", "owner@example.com");

        Assert.Equal(1, code);
        Assert.Contains("administrator", _err.ToString());
        Assert.Empty(_api.EnrollCalls);
        Assert.Null(new SettingsEnrollmentStore(database).Load());
    }

    [Fact]
    public void A_bad_option_is_an_error_line_and_exit_1()
    {
        using var root = new TempDataRoot();

        var (code, _, _) = Run(root.OpenDatabase(), Services(), "enroll", "--email", "nope");

        Assert.Equal(1, code);
        Assert.StartsWith("error:", _err.ToString());
    }

    [Fact]
    public void Enroll_runs_end_to_end_and_saves_into_the_real_settings_file()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        _api.Enrolls.Enqueue(new EnrollResult(EnrollOutcome.Requested));
        _api.Claims.Enqueue(new ClaimResult(
            ClaimStatus.Approved, "a.techn0.dpdns.org", "tok",
            "team.cloudflareaccess.com", "aud-1"));

        var (code, _, _) = Run(database, Services(), "enroll", "--email", "owner@example.com", "--name", "Acme");

        var state = new SettingsEnrollmentStore(database).Load()!;
        var oauth = database.GetOAuthConfig();

        Assert.Equal(0, code);
        Assert.Contains("Connected: a.techn0.dpdns.org", _out.ToString());
        Assert.Equal(Phase.Connected, state.Phase);
        Assert.True(oauth.RequireEdgeAccess);
        Assert.Equal("aud-1", oauth.Audience);
        Assert.Equal(database.GetGatewayConfig().ApiKey, _api.UploadCalls.Single().Key);
    }

    [Fact]
    public void Status_shows_the_enrolment_in_one_line_without_touching_windows()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (_, before, _) = Run(database, Services(), "status");

        new SettingsEnrollmentStore(database).Create(new EnrollmentState(
            "https://bytebalancetech.com", "bb-1", "s", "n", "owner@example.com",
            Phase.Connected, DateTimeOffset.UtcNow, "a.techn0.dpdns.org"));

        var (_, after, _) = Run(database, Services(), "status");

        Assert.Contains("not enrolled with ByteBalance", before);
        Assert.Contains("connected as a.techn0.dpdns.org", after);
    }

    [Fact]
    public void The_enrollment_verb_shows_the_detail()
    {
        using var root = new TempDataRoot();

        var (code, _, _) = Run(root.OpenDatabase(), Services(elevated: false), "enrollment");

        Assert.Equal(0, code);
        Assert.Contains("enrolment", _out.ToString());
    }

    [Fact]
    public void Rotating_the_key_on_a_connected_machine_sends_the_new_one()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        new SettingsEnrollmentStore(database).Create(new EnrollmentState(
            "https://bytebalancetech.com", "bb-1", "s", "n", "owner@example.com",
            Phase.Connected, DateTimeOffset.UtcNow, "a.techn0.dpdns.org"));

        var (code, newKey, _) = Run(database, Services(), "key", "new");

        Assert.Equal(0, code);
        Assert.Equal(newKey.Trim(), _api.UploadCalls.Single().Key);
    }

    [Fact]
    public void Rotating_the_key_on_a_machine_that_is_not_enrolled_contacts_nobody()
    {
        using var root = new TempDataRoot();

        var (code, _, _) = Run(root.OpenDatabase(), Services(), "key", "new");

        Assert.Equal(0, code);
        Assert.Empty(_api.UploadCalls);
    }

    [Fact]
    public void The_usage_lists_the_new_commands()
    {
        Assert.Contains("enroll --email", Cli.Usage);
        Assert.Contains("unenroll", Cli.Usage);
        Assert.Contains("sync-key", Cli.Usage);
        Assert.Contains("oauth edge on | off", Cli.Usage);
    }

    [Fact]
    public void Oauth_edge_turns_the_requirement_on_and_off_and_needs_a_team_first()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var (refused, _, refusal) = Run(database, Services(), "oauth", "edge", "on");

        Assert.Equal(1, refused);
        Assert.Contains("team domain", refusal);

        var config = database.GetOAuthConfig();
        config.TeamDomain = "team.cloudflareaccess.com";
        config.Audience = "aud-1";
        database.SaveOAuthConfig(config);

        Assert.Equal(0, Run(database, Services(), "oauth", "edge", "on").Code);
        Assert.True(database.GetOAuthConfig().RequireEdgeAccess);
        Assert.Contains("required", Run(database, Services(), "oauth", "show").Out);

        Assert.Equal(0, Run(database, Services(), "oauth", "edge", "off").Code);
        Assert.False(database.GetOAuthConfig().RequireEdgeAccess);

        Assert.Equal(1, Run(database, Services(), "oauth", "edge", "maybe").Code);
    }
}
