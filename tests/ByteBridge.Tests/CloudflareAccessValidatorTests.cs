using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * CloudflareAccessValidator used to parse each JWKS key entry as an
 * x5c certificate. Cloudflare Access's real JWKS endpoint returns
 * plain RSA JWKs (n/e), so every key failed to parse, the refresh
 * failure was swallowed silently, and every token was rejected.
 * These tests pin the fixed parsing, multi-key/kid selection,
 * tolerance of a malformed entry, picking up a rotated key, and
 * surfacing a refresh failure instead of hiding it.
 *
 * The last test redirects Console.Error, a process-wide static, so
 * it shares CliTests' "Console redirection" collection to keep the
 * two from racing each other.
 */
[Collection("Console redirection")]
public class CloudflareAccessValidatorTests
{
    private const string TeamDomain = "test-team.cloudflareaccess.com";
    private const string Audience = "test-audience";

    [Fact]
    public async Task Validates_a_token_signed_with_a_standard_jwk_n_e_key()
    {
        using var jwks = new FakeJwksServer();
        using var keyA = new TestSigningKey("key-a");
        jwks.SetKeys(keyA);

        using var validator = new CloudflareAccessValidator(MakeConfig(jwks.Url));

        var token = IssueToken(keyA, "user-a@example.com");

        var principal = await validator.ValidateTokenAsync(token);

        Assert.NotNull(principal);
        Assert.Equal("user-a@example.com", CloudflareAccessValidator.GetEmail(principal!));
    }

    [Fact]
    public async Task Selects_the_matching_key_by_kid_when_the_jwks_has_several()
    {
        using var jwks = new FakeJwksServer();
        using var keyA = new TestSigningKey("key-a");
        using var keyB = new TestSigningKey("key-b");
        jwks.SetKeys(keyA, keyB);

        using var validator = new CloudflareAccessValidator(MakeConfig(jwks.Url));

        var tokenB = IssueToken(keyB, "user-b@example.com");

        var principal = await validator.ValidateTokenAsync(tokenB);

        Assert.NotNull(principal);
        Assert.Equal("user-b@example.com", CloudflareAccessValidator.GetEmail(principal!));
    }

    [Fact]
    public async Task Skips_a_malformed_key_entry_without_losing_the_valid_ones()
    {
        using var jwks = new FakeJwksServer();
        using var keyA = new TestSigningKey("key-a");

        jwks.SetRawJson(new JsonObject
        {
            ["keys"] = new JsonArray(
                new JsonObject { ["kid"] = "broken-no-n-e", ["kty"] = "RSA" },
                new JsonObject { ["kty"] = "RSA", ["n"] = "abc", ["e"] = "AQAB" },
                JwkNodeFor(keyA))
        }.ToJsonString());

        using var validator = new CloudflareAccessValidator(MakeConfig(jwks.Url));

        var token = IssueToken(keyA, "user-a@example.com");

        var principal = await validator.ValidateTokenAsync(token);

        Assert.NotNull(principal);
        Assert.Equal("user-a@example.com", CloudflareAccessValidator.GetEmail(principal!));
    }

    [Fact]
    public async Task Picks_up_a_rotated_key_it_has_never_cached()
    {
        using var jwks = new FakeJwksServer();
        using var keyA = new TestSigningKey("key-a");
        jwks.SetKeys(keyA);

        using var validator = new CloudflareAccessValidator(MakeConfig(jwks.Url), TimeSpan.Zero);

        var tokenA = IssueToken(keyA, "user-a@example.com");
        Assert.NotNull(await validator.ValidateTokenAsync(tokenA));

        // Cloudflare rotates in a new key, keeping the old one live for overlap.
        using var keyB = new TestSigningKey("key-b");
        jwks.SetKeys(keyA, keyB);

        var tokenB = IssueToken(keyB, "user-b@example.com");
        var principal = await validator.ValidateTokenAsync(tokenB);

        Assert.NotNull(principal);
        Assert.Equal("user-b@example.com", CloudflareAccessValidator.GetEmail(principal!));
    }

    [Fact]
    public async Task A_jwks_fetch_failure_is_reported_instead_of_silently_swallowed()
    {
        using var jwks = new FakeJwksServer();
        jwks.FailAllRequests();

        using var validator = new CloudflareAccessValidator(MakeConfig(jwks.Url));

        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);

        ClaimsPrincipal? principal;

        try
        {
            using var keyA = new TestSigningKey("key-a");
            var token = IssueToken(keyA, "user-a@example.com");

            principal = await validator.ValidateTokenAsync(token);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Null(principal);
        Assert.Contains("JWKS refresh failed", captured.ToString());
    }

    private static OAuthConfig MakeConfig(string jwksUrl) => new()
    {
        Enabled = true,
        TeamDomain = TeamDomain,
        Audience = Audience,
        JwksUri = jwksUrl
    };

    private static string IssueToken(TestSigningKey key, string subject)
    {
        var handler = new JwtSecurityTokenHandler();

        var credentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: $"https://{TeamDomain}",
            audience: Audience,
            claims: [new Claim("email", subject)],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return handler.WriteToken(token);
    }

    private static JsonObject JwkNodeFor(TestSigningKey key)
    {
        var parameters = key.Rsa.ExportParameters(false);

        return new JsonObject
        {
            ["kid"] = key.Kid,
            ["kty"] = "RSA",
            ["alg"] = "RS256",
            ["use"] = "sig",
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    private sealed class TestSigningKey : IDisposable
    {
        public string Kid { get; }

        public RSA Rsa { get; }

        public RsaSecurityKey SecurityKey { get; }

        public TestSigningKey(string kid)
        {
            Kid = kid;
            Rsa = RSA.Create(2048);
            SecurityKey = new RsaSecurityKey(Rsa) { KeyId = kid };
        }

        public void Dispose() => Rsa.Dispose();
    }

    /*
     * A minimal stand-in for Cloudflare's JWKS endpoint, so tests can
     * change the served key set (or fail the request) without touching
     * a real network.
     */
    private sealed class FakeJwksServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        private volatile string _body = """{"keys":[]}""";
        private volatile bool _failAllRequests;

        public string Url { get; }

        public FakeJwksServer()
        {
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/cdn-cgi/access/certs";

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void SetKeys(params TestSigningKey[] keys)
        {
            var array = new JsonArray();

            foreach (var key in keys)
            {
                array.Add(JwkNodeFor(key));
            }

            _body = new JsonObject { ["keys"] = array }.ToJsonString();
        }

        public void SetRawJson(string json) => _body = json;

        public void FailAllRequests() => _failAllRequests = true;

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = HandleAsync(context);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                if (_failAllRequests)
                {
                    context.Response.StatusCode = 500;
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(_body);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch
            {
                // Connection dropped mid-response; nothing a test needs to see.
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);

            probe.Start();

            var port = ((IPEndPoint)probe.LocalEndpoint).Port;

            probe.Stop();

            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
        }
    }
}
