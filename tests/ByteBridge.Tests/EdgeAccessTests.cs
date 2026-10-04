using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;
using static ByteBridge.Tests.CloudflareAccessValidatorTests;

namespace ByteBridge.Tests;

/*
 * "Only requests that came through Cloudflare Access." A tunnel puts the
 * gateway on the public internet, Access sits in front of the hostname,
 * and RequireEdgeAccess makes the gateway itself refuse a request that
 * arrived through Cloudflare without proof it passed Access.
 *
 * Cloudflare puts Cf-Ray on everything it forwards, so the tests add it
 * where they mean "through the tunnel" and leave it off for "a tool on
 * this machine".
 */
public class EdgeAccessTests
{
    private const string TeamDomain = "test-team.cloudflareaccess.com";
    private const string Audience = "test-audience";

    private static OAuthConfig Required(string jwksUrl, bool enabled = false) => new()
    {
        Enabled = enabled,
        RequireEdgeAccess = true,
        TeamDomain = TeamDomain,
        Audience = Audience,
        JwksUri = jwksUrl
    };

    private static Task<HttpResponseMessage> Call(
        GatewayHarness harness,
        string path,
        bool throughCloudflare,
        string? accessToken = null,
        string? apiKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);

        var key = apiKey ?? harness.ApiKey;

        if (key.Length > 0)
        {
            request.Headers.Add("X-API-Key", key);
        }

        if (throughCloudflare)
        {
            request.Headers.Add("Cf-Ray", "8a1b2c3d4e5f6789-FRA");
        }

        if (accessToken != null)
        {
            request.Headers.Add("Cf-Access-Jwt-Assertion", accessToken);
        }

        return harness.Client.SendAsync(request);
    }

    /* What Access issues for a service token: no email, a common_name. */
    private static string IssueServiceToken(
        TestSigningKey key,
        string audience = Audience)
    {
        var token = new JwtSecurityToken(
            issuer: $"https://{TeamDomain}",
            audience: audience,
            claims: [new Claim("common_name", "abc123.access")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                key.SecurityKey,
                SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Nothing_changes_when_it_is_not_required()
    {
        using var harness = new GatewayHarness();

        using var response = await Call(harness, "/databases", throughCloudflare: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_local_tool_is_not_asked_for_a_token_but_still_needs_the_key()
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var withKey = await Call(harness, "/databases", throughCloudflare: false);
        using var withoutKey = await Call(harness, "/databases", throughCloudflare: false, apiKey: "");

        Assert.Equal(HttpStatusCode.OK, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutKey.StatusCode);
    }

    [Fact]
    public async Task A_request_through_cloudflare_without_a_token_is_refused_even_with_the_key()
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(harness, "/databases", throughCloudflare: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Cloudflare Access", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_valid_owner_token_plus_the_key_is_let_through()
    {
        using var jwks = new FakeJwksServer();
        using var signing = new TestSigningKey("key-a");
        jwks.SetKeys(signing);
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(
            harness, "/databases", true, IssueToken(signing, "owner@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_service_token_has_no_email_and_is_let_through_too()
    {
        using var jwks = new FakeJwksServer();
        using var signing = new TestSigningKey("key-a");
        jwks.SetKeys(signing);
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(
            harness, "/databases", true, IssueServiceToken(signing));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_token_never_replaces_the_key()
    {
        using var jwks = new FakeJwksServer();
        using var signing = new TestSigningKey("key-a");
        jwks.SetKeys(signing);
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(
            harness, "/databases", true, IssueToken(signing, "owner@example.com"), apiKey: "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_application_is_refused()
    {
        using var jwks = new FakeJwksServer();
        using var signing = new TestSigningKey("key-a");
        jwks.SetKeys(signing);
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(
            harness, "/databases", true, IssueServiceToken(signing, audience: "someone-elses-app"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_by_a_stranger_is_refused()
    {
        using var jwks = new FakeJwksServer();
        using var real = new TestSigningKey("key-a");
        using var stranger = new TestSigningKey("key-a");
        jwks.SetKeys(real);
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(
            harness, "/databases", true, IssueServiceToken(stranger));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task Rubbish_in_place_of_a_token_is_refused(string token)
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var response = await Call(harness, "/databases", true, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_stats_stay_open_because_they_carry_no_data()
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var health = await Call(harness, "/health", true, apiKey: "");
        using var stats = await Call(harness, "/stats", true, apiKey: "");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
    }

    [Fact]
    public async Task Requiring_edge_access_does_not_switch_the_login_flow_on()
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url, enabled: false));

        using var response = await Call(harness, "/auth/login", throughCloudflare: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Turning_it_off_again_takes_effect_without_a_restart()
    {
        using var jwks = new FakeJwksServer();
        using var harness = new GatewayHarness();

        harness.Server.UpdateOAuth(Required(jwks.Url));

        using var refused = await Call(harness, "/databases", true);

        harness.Server.UpdateOAuth(new OAuthConfig());

        using var allowed = await Call(harness, "/databases", true);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Before_the_service_has_applied_its_settings_a_request_from_cloudflare_is_refused()
    {
        /*
         * The service starts listening, then applies the Access settings
         * a moment later. In that gap the stored setting already says
         * "required" but no validator exists yet; that must not be a
         * hole.
         */
        using var harness = new GatewayHarness();

        var stored = harness.Database.GetOAuthConfig();
        stored.RequireEdgeAccess = true;
        stored.TeamDomain = TeamDomain;
        stored.Audience = Audience;
        harness.Database.SaveOAuthConfig(stored);

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var config = harness.Database.GetGatewayConfig();
        config.Port = port;

        using var late = new GatewayServer(harness.Database);
        late.Start(config);

        using var client = new HttpClient { BaseAddress = new Uri(config.BaseUrl) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/databases");
        request.Headers.Add("X-API-Key", config.ApiKey);
        request.Headers.Add("Cf-Ray", "8a1b2c3d4e5f6789-FRA");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void The_setting_is_off_by_default_and_survives_a_round_trip()
    {
        using var root = new TempDataRoot();
        var database = root.OpenDatabase();

        Assert.False(database.GetOAuthConfig().RequireEdgeAccess);

        var config = database.GetOAuthConfig();
        config.RequireEdgeAccess = true;
        database.SaveOAuthConfig(config);

        Assert.True(root.OpenDatabase().GetOAuthConfig().RequireEdgeAccess);
    }
}
