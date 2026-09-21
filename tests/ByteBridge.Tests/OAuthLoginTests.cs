using System.Net;
using System.Net.Sockets;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * /auth/login used to fall back to {gateway base url}/auth/callback
 * (http://127.0.0.1:<port>/auth/callback) whenever no public hostname
 * was configured. That address only resolves on the machine running
 * the gateway, so Cloudflare Access would send a real visitor's
 * browser, arriving through the tunnel, somewhere it can never reach.
 * These tests pin the fixed behaviour: a clear error instead of a
 * redirect nobody outside the machine can follow, and the configured
 * hostname showing up untouched once it exists.
 */
public class OAuthLoginTests
{
    [Fact]
    public async Task Login_without_a_public_hostname_fails_clearly_instead_of_redirecting_to_loopback()
    {
        using var harness = StartWithOAuth(redirectUri: "");

        using var response = await harness.Client.GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("127.0.0.1", body);
    }

    [Fact]
    public async Task Login_redirects_to_the_configured_public_hostname()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var response = await harness.Client.GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";

        Assert.Contains(
            Uri.EscapeDataString("https://api.example.com/auth/callback"),
            location);

        Assert.DoesNotContain("127.0.0.1", location);
    }

    /*
     * /auth/login used to hand out a "state" value that /auth/callback
     * never checked, which is what makes login CSRF possible: an
     * attacker completes their own login, captures the callback URL
     * Cloudflare sends back, and gets a victim to open it, logging the
     * victim's browser into the attacker's account. Binding state to a
     * cookie the callback must echo closes that -- a URL alone is no
     * longer enough to complete someone else's login.
     */
    [Fact]
    public async Task Login_sets_a_state_cookie_matching_the_redirect()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var response = await harness.Client.GetAsync("/auth/login");

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.Contains("cf_oauth_state=", setCookie);
        Assert.Contains("HttpOnly", setCookie);
        Assert.Contains("Secure", setCookie);
        Assert.Contains("SameSite=Lax", setCookie);

        var cookieState = ExtractCookieValue(setCookie, "cf_oauth_state");
        var location = response.Headers.Location?.ToString() ?? "";

        Assert.Contains($"state={cookieState}", location);
    }

    [Fact]
    public async Task Callback_without_a_state_cookie_is_rejected()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var response = await harness.Client.GetAsync(
            "/auth/callback?state=whatever&cf_clearance_jwt=irrelevant");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Callback_with_a_mismatched_state_is_rejected()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/callback?state=wrong-value&cf_clearance_jwt=irrelevant");

        request.Headers.Add("Cookie", "cf_oauth_state=right-value");

        using var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /*
     * The state check has to run before token validation, or the two
     * failures become indistinguishable and a mismatched-state error
     * would look exactly like a garbage-token error.
     */
    [Fact]
    public async Task A_rejected_state_never_reports_a_token_problem()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var response = await harness.Client.GetAsync("/auth/callback");

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("authentication token", body);
    }

    /*
     * The callback answers with two cookies at once: one that spends
     * the state cookie and the session itself. If the listener folded
     * them into one header, or the second replaced the first, the
     * browser would end up with no session and login would loop, and
     * the rejection tests above would never notice.
     */
    [Fact]
    public async Task A_completed_login_sets_the_session_and_spends_the_state_cookie()
    {
        using var jwks = new CloudflareAccessValidatorTests.FakeJwksServer();
        using var key = new CloudflareAccessValidatorTests.TestSigningKey("key-a");
        jwks.SetKeys(key);

        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback",
            jwksUri: jwks.Url);

        var token = CloudflareAccessValidatorTests.IssueToken(key, "user-a@example.com");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/auth/callback?state=abc123&cf_clearance_jwt={token}");

        request.Headers.Add("Cookie", "cf_oauth_state=abc123");

        using var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();

        Assert.Equal(2, cookies.Count);

        var stateCookie = Assert.Single(cookies, c => c.StartsWith("cf_oauth_state=;"));
        Assert.Contains("Max-Age=0", stateCookie);

        var sessionCookie = Assert.Single(cookies, c => c.StartsWith("efs_session="));
        Assert.Contains("HttpOnly", sessionCookie);
        Assert.Contains("Secure", sessionCookie);
        Assert.Contains("SameSite=Lax", sessionCookie);
        Assert.Contains("Max-Age=", sessionCookie);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/auth/me");

        me.Headers.Add(
            "Cookie",
            $"efs_session={ExtractCookieValue(sessionCookie, "efs_session")}");

        using var meResponse = await harness.Client.SendAsync(me);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Contains("user-a@example.com", await meResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_state_cookie_is_spent_even_when_the_token_is_bad()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/callback?state=abc123&cf_clearance_jwt=not-a-jwt");

        request.Headers.Add("Cookie", "cf_oauth_state=abc123");

        using var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var stateCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        Assert.StartsWith("cf_oauth_state=;", stateCookie);
        Assert.Contains("Max-Age=0", stateCookie);
    }

    private static string ExtractCookieValue(string setCookie, string name)
    {
        var prefix = name + "=";
        var start = setCookie.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = setCookie.IndexOf(';', start);

        return setCookie[start..end];
    }

    private static OAuthHarness StartWithOAuth(
        string redirectUri,
        string? jwksUri = null)
    {
        var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var gatewayConfig = database.GetGatewayConfig();
        gatewayConfig.Port = FreePort();
        database.SaveGatewayConfig(gatewayConfig);

        var oauthConfig = database.GetOAuthConfig();
        oauthConfig.Enabled = true;
        oauthConfig.TeamDomain = "test-team.cloudflareaccess.com";
        oauthConfig.Audience = "test-audience";
        oauthConfig.RedirectUri = redirectUri;

        if (jwksUri != null)
        {
            oauthConfig.JwksUri = jwksUri;
        }

        database.SaveOAuthConfig(oauthConfig);

        var log = new RequestLog(Path.Combine(root.Path, "logs"));
        var validator = new CloudflareAccessValidator(oauthConfig);
        var sessionManager = new OAuthSessionManager(oauthConfig, database);

        var server = new GatewayServer(database, log, validator, sessionManager);
        server.Start(gatewayConfig);

        var handler = new HttpClientHandler { AllowAutoRedirect = false };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(gatewayConfig.BaseUrl)
        };

        return new OAuthHarness(root, server, validator, client);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private sealed class OAuthHarness : IDisposable
    {
        private readonly TempDataRoot _root;
        private readonly GatewayServer _server;
        private readonly CloudflareAccessValidator _validator;

        public HttpClient Client { get; }

        public OAuthHarness(
            TempDataRoot root,
            GatewayServer server,
            CloudflareAccessValidator validator,
            HttpClient client)
        {
            _root = root;
            _server = server;
            _validator = validator;
            Client = client;
        }

        public void Dispose()
        {
            Client.Dispose();
            _server.Dispose();
            _validator.Dispose();
            _root.Dispose();
        }
    }
}
