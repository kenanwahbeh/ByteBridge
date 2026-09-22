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

        var state = ReadQueryParameter(location, "state");

        Assert.NotEmpty(state);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.Contains($"efs_oauth_state={state}")
                && value.Contains("Secure"));
    }

    [Fact]
    public async Task Callback_rejects_a_token_without_the_browser_login_state()
    {
        using var harness = StartWithOAuth(
            redirectUri: "https://api.example.com/auth/callback");

        using var response = await harness.Client.GetAsync(
            "/auth/callback?cf_clearance_jwt=not-a-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("OAuth state", body);
    }

    private static string ReadQueryParameter(
        string url,
        string name)
    {
        var query = new Uri(url).Query.TrimStart('?').Split('&');

        foreach (var part in query)
        {
            var pair = part.Split('=', 2);

            if (pair.Length == 2 && pair[0] == name)
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return string.Empty;
    }

    private static OAuthHarness StartWithOAuth(string redirectUri)
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
