using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * The limiter on its own, with a clock the test moves, so none of this
 * waits on real time.
 */
public class AuthFailureLimiterTests
{
    private static readonly DateTime Start =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Clock
    {
        public DateTime Now { get; set; } = Start;

        public void Advance(int seconds) =>
            Now = Now.AddSeconds(seconds);
    }

    private static AuthFailureLimiter Make(
        Clock clock,
        int maxTracked = 4096) =>
        new(
            maxFailures: 3,
            window: TimeSpan.FromSeconds(60),
            blockFor: TimeSpan.FromSeconds(30),
            maxTracked: maxTracked,
            now: () => clock.Now);

    [Fact]
    public void A_caller_is_blocked_once_it_reaches_the_limit()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        Assert.False(limiter.IsBlocked("a", out _));

        limiter.RecordFailure("a");

        Assert.True(limiter.IsBlocked("a", out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(30), retryAfter);
    }

    [Fact]
    public void The_block_ends_on_its_own()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        for (var i = 0; i < 3; i++)
        {
            limiter.RecordFailure("a");
        }

        clock.Advance(29);
        Assert.True(limiter.IsBlocked("a", out _));

        clock.Advance(2);
        Assert.False(limiter.IsBlocked("a", out _));
    }

    [Fact]
    public void After_a_block_the_count_starts_again_from_nothing()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        for (var i = 0; i < 3; i++)
        {
            limiter.RecordFailure("a");
        }

        clock.Advance(31);

        limiter.RecordFailure("a");
        limiter.RecordFailure("a");
        Assert.False(limiter.IsBlocked("a", out _));

        limiter.RecordFailure("a");
        Assert.True(limiter.IsBlocked("a", out _));
    }

    [Fact]
    public void Failures_older_than_the_window_are_forgotten()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        clock.Advance(61);

        limiter.RecordFailure("a");

        Assert.False(limiter.IsBlocked("a", out _));
    }

    [Fact]
    public void A_correct_key_clears_the_count()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        limiter.RecordFailure("a");
        limiter.RecordFailure("a");
        limiter.RecordSuccess("a");
        limiter.RecordFailure("a");
        limiter.RecordFailure("a");

        Assert.False(limiter.IsBlocked("a", out _));
    }

    [Fact]
    public void One_caller_being_blocked_does_not_block_another()
    {
        var clock = new Clock();
        var limiter = Make(clock);

        for (var i = 0; i < 3; i++)
        {
            limiter.RecordFailure("a");
        }

        Assert.True(limiter.IsBlocked("a", out _));
        Assert.False(limiter.IsBlocked("b", out _));
    }

    [Fact]
    public void The_table_never_grows_past_its_bound()
    {
        var clock = new Clock();
        var limiter = Make(clock, maxTracked: 5);

        for (var i = 0; i < 200; i++)
        {
            limiter.RecordFailure($"address-{i}");
            clock.Advance(1);
        }

        Assert.True(limiter.TrackedCount <= 5, $"tracked {limiter.TrackedCount}");
    }
}

/*
 * The limiter as the gateway applies it: what counts as a failure, what
 * does not, and that the person can turn it off.
 */
public class GatewayLockoutTests
{
    [Fact]
    public async Task Too_many_wrong_keys_get_a_429_with_a_retry_after()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 3);

        for (var i = 0; i < 3; i++)
        {
            var (status, _) = await GatewayHarness.Read(
                gateway.Get("/databases", key: "wrong"));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        // Even the right key waits: the block is on the caller.
        using var blocked = await gateway.Get("/databases");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter?.Delta);
        Assert.True(blocked.Headers.RetryAfter!.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task Health_stays_reachable_while_a_caller_is_blocked()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 2);

        for (var i = 0; i < 2; i++)
        {
            await GatewayHarness.Read(gateway.Get("/databases", key: "wrong"));
        }

        var (status, _) = await GatewayHarness.Read(gateway.Get("/health", key: ""));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Turning_the_limit_off_never_blocks()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 0);

        for (var i = 0; i < 25; i++)
        {
            var (status, _) = await GatewayHarness.Read(
                gateway.Get("/databases", key: "wrong"));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        var (right, _) = await GatewayHarness.Read(gateway.Get("/databases"));

        Assert.Equal(HttpStatusCode.OK, right);
    }

    [Fact]
    public async Task A_request_with_no_key_at_all_does_not_count()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 3);

        for (var i = 0; i < 8; i++)
        {
            var (status, _) = await GatewayHarness.Read(
                gateway.Get("/databases", key: ""));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        var (right, _) = await GatewayHarness.Read(gateway.Get("/databases"));

        Assert.Equal(HttpStatusCode.OK, right);
    }

    [Fact]
    public async Task A_correct_key_clears_the_wrong_ones_before_it()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 3);

        for (var round = 0; round < 3; round++)
        {
            await GatewayHarness.Read(gateway.Get("/databases", key: "wrong"));
            await GatewayHarness.Read(gateway.Get("/databases", key: "wrong"));

            var (status, _) = await GatewayHarness.Read(gateway.Get("/databases"));

            Assert.Equal(HttpStatusCode.OK, status);
        }
    }

    [Fact]
    public async Task A_caller_is_told_apart_by_the_address_cloudflare_reports()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 2);

        HttpRequestMessage From(string address, string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/databases");
            request.Headers.Add("X-API-Key", key);
            request.Headers.Add("CF-Connecting-IP", address);
            return request;
        }

        for (var i = 0; i < 2; i++)
        {
            using var _ = await gateway.Client.SendAsync(From("203.0.113.1", "wrong"));
        }

        using var blocked = await gateway.Client.SendAsync(From("203.0.113.1", gateway.ApiKey));
        using var other = await gateway.Client.SendAsync(From("203.0.113.2", gateway.ApiKey));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    /*
     * /auth/me answers 200 for a right key, so it tests keys like any
     * other endpoint. It used to be dispatched ahead of the lockout,
     * which made it a place to guess keys without limit.
     */
    [Fact]
    public async Task Wrong_keys_sent_to_auth_me_count_toward_the_lockout()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 3);

        for (var i = 0; i < 3; i++)
        {
            var (status, _) = await GatewayHarness.Read(
                gateway.Get("/auth/me", key: "wrong"));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        // Blocked on every endpoint, not only the one that was guessed at.
        using var elsewhere = await gateway.Get("/databases");
        using var again = await gateway.Get("/auth/me");

        Assert.Equal(HttpStatusCode.TooManyRequests, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
    }

    [Fact]
    public async Task A_caller_blocked_elsewhere_cannot_probe_keys_through_auth_me()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 2);

        for (var i = 0; i < 2; i++)
        {
            await GatewayHarness.Read(gateway.Get("/databases", key: "wrong"));
        }

        using var response = await gateway.Get("/auth/me");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Auth_me_without_credentials_does_not_count()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 3);

        for (var i = 0; i < 8; i++)
        {
            var (status, _) = await GatewayHarness.Read(
                gateway.Get("/auth/me", key: ""));

            Assert.Equal(HttpStatusCode.Unauthorized, status);
        }

        var (right, _) = await GatewayHarness.Read(gateway.Get("/databases"));

        Assert.Equal(HttpStatusCode.OK, right);
    }

    [Fact]
    public async Task Auth_me_still_answers_a_right_key()
    {
        using var gateway = new GatewayHarness();

        var (status, body) = await GatewayHarness.Read(gateway.Get("/auth/me"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("api-key", body);
    }

    /*
     * X-Forwarded-For's first entry is whatever the caller wrote, so
     * rotating it must not buy a fresh allowance.
     */
    [Fact]
    public async Task X_forwarded_for_cannot_be_used_to_dodge_the_limit()
    {
        using var gateway = GatewayHarness.With(c => c.AuthMaxFailures = 2);

        for (var i = 0; i < 2; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/databases");
            request.Headers.Add("X-API-Key", "wrong");
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i}");

            using var _ = await gateway.Client.SendAsync(request);
        }

        var next = new HttpRequestMessage(HttpMethod.Get, "/databases");
        next.Headers.Add("X-API-Key", gateway.ApiKey);
        next.Headers.Add("X-Forwarded-For", "198.51.100.99");

        using var response = await gateway.Client.SendAsync(next);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}

/*
 * The limit is the person's to set, so it has to survive a save and a
 * reload, and a value that got into the file badly must not turn it off
 * or lock everyone out.
 */
public class LockoutSettingsTests
{
    [Fact]
    public void The_defaults_are_ten_wrong_keys_in_a_minute_for_a_minute()
    {
        using var root = new TempDataRoot();
        var config = root.OpenDatabase().GetGatewayConfig();

        Assert.Equal(10, config.AuthMaxFailures);
        Assert.Equal(60, config.AuthWindowSeconds);
        Assert.Equal(60, config.AuthBlockSeconds);
    }

    [Fact]
    public void The_settings_survive_a_save_and_a_reload()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var config = database.GetGatewayConfig();
        config.AuthMaxFailures = 5;
        config.AuthWindowSeconds = 30;
        config.AuthBlockSeconds = 900;
        database.SaveGatewayConfig(config);

        var reloaded = database.GetGatewayConfig();

        Assert.Equal(5, reloaded.AuthMaxFailures);
        Assert.Equal(30, reloaded.AuthWindowSeconds);
        Assert.Equal(900, reloaded.AuthBlockSeconds);
    }

    [Fact]
    public void Zero_is_a_real_value_and_means_off()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var config = database.GetGatewayConfig();
        config.AuthMaxFailures = 0;
        database.SaveGatewayConfig(config);

        Assert.Equal(0, database.GetGatewayConfig().AuthMaxFailures);
    }

    [Theory]
    [InlineData("Gateway.AuthMaxFailures", "-1", 10)]
    [InlineData("Gateway.AuthMaxFailures", "many", 10)]
    [InlineData("Gateway.AuthWindowSeconds", "0", 60)]
    [InlineData("Gateway.AuthBlockSeconds", "-30", 60)]
    public void A_bad_value_in_the_file_falls_back_to_the_default(
        string key,
        string value,
        int expected)
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        database.SetSetting(key, value);

        var config = database.GetGatewayConfig();

        var actual = key switch
        {
            "Gateway.AuthMaxFailures" => config.AuthMaxFailures,
            "Gateway.AuthWindowSeconds" => config.AuthWindowSeconds,
            _ => config.AuthBlockSeconds
        };

        Assert.Equal(expected, actual);
    }
}

/*
 * A browser sends the session cookie with whatever a page asks it to, so
 * a request that arrives on the cookie alone must come from a page of
 * ours: JSON, and no foreign Origin. The API key needs none of that, so
 * it is left as it was.
 *
 * "no-such-connection" is the probe: a request that gets past the check
 * ends in 404 for that name, one that does not ends in 403.
 */
public class SessionCsrfTests
{
    private const string PublicOrigin = "https://api.example.com";

    private sealed class SessionHarness : IDisposable
    {
        private readonly TempDataRoot _root;

        private readonly GatewayServer _server;

        public HttpClient Client { get; }

        public string ApiKey { get; }

        public string SessionCookie { get; }

        public string GatewayOrigin { get; }

        public SessionHarness()
        {
            _root = new TempDataRoot();

            var database = _root.OpenDatabase();

            var gatewayConfig = database.GetGatewayConfig();
            gatewayConfig.Port = FreePort();
            database.SaveGatewayConfig(gatewayConfig);

            ApiKey = gatewayConfig.ApiKey;

            var oauth = database.GetOAuthConfig();
            oauth.Enabled = true;
            oauth.TeamDomain = "test-team.cloudflareaccess.com";
            oauth.Audience = "test-audience";
            oauth.RedirectUri = $"{PublicOrigin}/auth/callback";
            database.SaveOAuthConfig(oauth);

            var sessions = new OAuthSessionManager(oauth, database);
            var (token, _) = sessions.CreateSession("person@example.com");

            SessionCookie = $"efs_session={token}";

            var log = new RequestLog(Path.Combine(_root.Path, "logs"));

            _server = new GatewayServer(database, log, null, sessions);
            _server.Start(gatewayConfig);

            GatewayOrigin = $"http://127.0.0.1:{gatewayConfig.Port}";

            Client = new HttpClient
            {
                BaseAddress = new Uri(gatewayConfig.BaseUrl)
            };
        }

        public async Task<HttpStatusCode> PostQuery(
            string contentType,
            string? origin = null,
            bool withCookie = true,
            bool withKey = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/query")
            {
                Content = new StringContent(
                    """{"database":"no-such-connection","sql":"SELECT 1 FROM RDB$DATABASE"}""",
                    Encoding.UTF8)
            };

            request.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse(contentType);

            if (withCookie)
            {
                request.Headers.Add("Cookie", SessionCookie);
            }

            if (withKey)
            {
                request.Headers.Add("X-API-Key", ApiKey);
            }

            if (origin != null)
            {
                request.Headers.Add("Origin", origin);
            }

            using var response = await Client.SendAsync(request);

            return response.StatusCode;
        }

        public async Task<HttpStatusCode> Get(
            string path,
            bool withCookie,
            bool withKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);

            if (withCookie)
            {
                request.Headers.Add("Cookie", SessionCookie);
            }

            if (withKey)
            {
                request.Headers.Add("X-API-Key", ApiKey);
            }

            using var response = await Client.SendAsync(request);

            return response.StatusCode;
        }

        public void Dispose()
        {
            Client.Dispose();
            _server.Dispose();
            _root.Dispose();
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);

            probe.Start();

            var port = ((IPEndPoint)probe.LocalEndpoint).Port;

            probe.Stop();

            return port;
        }
    }

    [Fact]
    public async Task A_json_request_with_the_cookie_and_no_origin_is_accepted()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery("application/json"));
    }

    [Fact]
    public async Task A_cookie_request_that_is_not_json_is_refused()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await harness.PostQuery("text/plain"));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await harness.PostQuery("application/x-www-form-urlencoded"));
    }

    [Fact]
    public async Task A_cookie_request_from_a_foreign_origin_is_refused()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await harness.PostQuery("application/json", "https://evil.example"));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await harness.PostQuery("application/json", "null"));
    }

    [Fact]
    public async Task The_public_hostname_and_the_hosts_own_origin_are_accepted()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery("application/json", PublicOrigin));

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery("application/json", harness.GatewayOrigin));
    }

    [Fact]
    public async Task The_json_type_may_carry_a_charset()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery("application/json; charset=utf-8"));
    }

    /*
     * A browser that holds the session cookie and also sends the key must
     * not have the key refused because the cookie checks ran first.
     */
    /*
     * /stats is the app's own business and is documented as needing the
     * key. A signed-in session can use the rest of the API, but not this.
     */
    [Fact]
    public async Task A_signed_in_session_alone_cannot_read_stats()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await harness.Get("/stats", withCookie: true, withKey: false));
    }

    [Fact]
    public async Task Stats_answers_the_key_even_when_the_session_cookie_is_sent_too()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.OK,
            await harness.Get("/stats", withCookie: true, withKey: true));
    }

    [Fact]
    public async Task A_valid_key_is_honoured_even_when_the_session_cookie_is_sent_too()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery(
                "text/plain",
                "https://evil.example",
                withCookie: true,
                withKey: true));
    }

    /*
     * The key rides in a header a foreign page cannot add, so a request
     * that carries it is not what this check is for -- and a caller
     * that sends text/plain with the key has always been served.
     */
    [Fact]
    public async Task A_request_on_the_api_key_is_not_subject_to_the_check()
    {
        using var harness = new SessionHarness();

        Assert.Equal(
            HttpStatusCode.NotFound,
            await harness.PostQuery(
                "text/plain",
                "https://evil.example",
                withCookie: false,
                withKey: true));
    }
}

/*
 * An unexpected failure -- not a Firebird error about the caller's
 * statement -- used to send its exception message straight back over the
 * tunnel. The message can name paths, tables and hosts. The caller now
 * gets a fixed line, and the detail goes where the operator reads it.
 *
 * The failure is made by dropping the table the connections live in, so
 * /databases fails inside the gateway's own storage.
 */
public class UnexpectedFailureTests
{
    [Fact]
    public async Task An_unexpected_failure_tells_the_caller_nothing_but_the_log_keeps_the_detail()
    {
        using var gateway = new GatewayHarness();

        using (var connection = new SqliteConnection(
                   $"Data Source={gateway.SettingsDatabasePath}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE Databases";
            command.ExecuteNonQuery();
        }

        var (status, body) = await GatewayHarness.Read(gateway.Get("/databases"));

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Contains("request log", body);
        Assert.DoesNotContain("no such table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Databases", body);

        string? logged = null;

        for (var attempt = 0; attempt < 40 && logged == null; attempt++)
        {
            logged = gateway.Log.Tail(50)
                .Select(line => JsonDocument.Parse(line).RootElement)
                .Where(entry => entry.GetProperty("path").GetString() == "/databases"
                                && entry.TryGetProperty("error", out _))
                .Select(entry => entry.GetProperty("error").GetString())
                .FirstOrDefault();

            if (logged == null)
            {
                await Task.Delay(50);
            }
        }

        Assert.NotNull(logged);
        Assert.Contains("no such table", logged, StringComparison.OrdinalIgnoreCase);
    }
}
