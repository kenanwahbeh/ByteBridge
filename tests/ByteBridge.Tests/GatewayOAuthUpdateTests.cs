using System.Net;
using System.Net.Sockets;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * The service builds its GatewayServer once and outlives the control
 * panel, so Cloudflare Access settings saved later have to reach the
 * running listener. These tests pin that: login is refused while the
 * feature is off, offered the moment it is switched on, and refused
 * again when it is switched off, all on the same listener.
 */
public class GatewayOAuthUpdateTests
{
    [Fact]
    public async Task Login_is_not_offered_until_oauth_is_applied()
    {
        using var harness = Start();

        using var response = await harness.Client.GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Applying_oauth_settings_turns_login_on_without_a_restart()
    {
        using var harness = Start();

        harness.Server.UpdateOAuth(EnabledConfig());

        using var response = await harness.Client.GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";

        Assert.StartsWith("https://my-team.cloudflareaccess.com/", location);
        Assert.Contains(
            Uri.EscapeDataString("https://api.example.com/auth/callback"),
            location);
    }

    [Fact]
    public async Task Applying_disabled_oauth_settings_turns_login_back_off()
    {
        using var harness = Start();

        harness.Server.UpdateOAuth(EnabledConfig());
        harness.Server.UpdateOAuth(new OAuthConfig { Enabled = false });

        using var response = await harness.Client.GetAsync("/auth/login");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_api_key_keeps_working_while_oauth_is_on()
    {
        using var harness = Start();

        harness.Server.UpdateOAuth(EnabledConfig());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/databases");
        request.Headers.Add("X-API-Key", harness.ApiKey);

        using var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void The_jwks_url_is_derived_from_the_team_domain_when_none_is_stored()
    {
        var config = new OAuthConfig { TeamDomain = "my-team.cloudflareaccess.com" };

        Assert.Equal(
            "https://my-team.cloudflareaccess.com/cdn-cgi/access/certs",
            config.JwksUrl);
    }

    [Fact]
    public void An_explicit_jwks_uri_wins_over_the_derived_one()
    {
        var config = new OAuthConfig
        {
            TeamDomain = "my-team.cloudflareaccess.com",
            JwksUri = "https://keys.example.com/certs"
        };

        Assert.Equal("https://keys.example.com/certs", config.JwksUrl);
    }

    private static OAuthConfig EnabledConfig() => new()
    {
        Enabled = true,
        TeamDomain = "my-team.cloudflareaccess.com",
        Audience = "abc123",
        RedirectUri = "https://api.example.com/auth/callback"
    };

    private static Harness Start()
    {
        var root = new TempDataRoot();
        var database = root.OpenDatabase();

        var config = database.GetGatewayConfig();
        config.Port = FreePort();
        database.SaveGatewayConfig(config);

        var server = new GatewayServer(
            database,
            new RequestLog(Path.Combine(root.Path, "logs")));

        server.Start(config);

        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(config.BaseUrl)
        };

        return new Harness(root, server, client, config.ApiKey);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDataRoot _root;

        public GatewayServer Server { get; }

        public HttpClient Client { get; }

        public string ApiKey { get; }

        public Harness(TempDataRoot root, GatewayServer server, HttpClient client, string apiKey)
        {
            _root = root;
            Server = server;
            Client = client;
            ApiKey = apiKey;
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
            _root.Dispose();
        }
    }
}
