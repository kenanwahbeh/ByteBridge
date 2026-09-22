using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ByteBridge.Configuration;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

public class CloudflareAccessValidatorTests
{
    [Fact]
    public void Cloudflare_rsa_jwks_modulus_and_exponent_are_parsed()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);

        var json = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kid = "cloudflare-test-key",
                    kty = "RSA",
                    alg = "RS256",
                    use = "sig",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!)
                }
            }
        });

        var key = Assert.Single(
            CloudflareAccessValidator.ParseSigningKeys(json));

        var rsaKey = Assert.IsType<RsaSecurityKey>(key);

        Assert.Equal("cloudflare-test-key", rsaKey.KeyId);
    }

    [Fact]
    public async Task A_correctly_signed_access_token_is_accepted()
    {
        using var rsa = RSA.Create(2048);
        var jwks = CreateJwks(rsa, "key-one");
        var responses = new JwksHandler(jwks);
        using var validator = new CloudflareAccessValidator(
            Config(),
            responses);

        var principal = await validator.ValidateTokenAsync(
            CreateToken(rsa, "key-one"));

        Assert.NotNull(principal);
        Assert.Equal(1, responses.RequestCount);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJzdWIiOiJhdHRhY2tlciJ9.")]
    public async Task Malformed_or_unsigned_tokens_are_rejected(
        string token)
    {
        var responses = new JwksHandler("{\"keys\":[]}");
        using var validator = new CloudflareAccessValidator(
            Config(),
            responses);

        Assert.Null(await validator.ValidateTokenAsync(token));
        Assert.Equal(0, responses.RequestCount);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var responses = new JwksHandler(CreateJwks(rsa, "key-one"));
        using var validator = new CloudflareAccessValidator(
            Config(),
            responses);

        var principal = await validator.ValidateTokenAsync(
            CreateToken(
                rsa,
                "key-one",
                audience: "another-application"));

        Assert.Null(principal);
    }

    [Fact]
    public async Task A_new_signing_key_is_loaded_without_waiting_for_cache_expiry()
    {
        using var first = RSA.Create(2048);
        using var second = RSA.Create(2048);
        var responses = new JwksHandler(CreateJwks(first, "key-one"));
        using var validator = new CloudflareAccessValidator(
            Config(),
            responses);

        Assert.NotNull(await validator.ValidateTokenAsync(
            CreateToken(first, "key-one")));

        responses.Json = CreateJwks(second, "key-two");

        Assert.NotNull(await validator.ValidateTokenAsync(
            CreateToken(second, "key-two")));
        Assert.Equal(2, responses.RequestCount);
    }

    [Fact]
    public async Task Random_unknown_key_ids_cannot_force_repeated_jwks_requests()
    {
        using var trusted = RSA.Create(2048);
        using var attackerOne = RSA.Create(2048);
        using var attackerTwo = RSA.Create(2048);
        var responses = new JwksHandler(CreateJwks(trusted, "trusted"));
        using var validator = new CloudflareAccessValidator(
            Config(),
            responses);

        Assert.NotNull(await validator.ValidateTokenAsync(
            CreateToken(trusted, "trusted")));
        Assert.Null(await validator.ValidateTokenAsync(
            CreateToken(attackerOne, "random-one")));
        Assert.Null(await validator.ValidateTokenAsync(
            CreateToken(attackerTwo, "random-two")));

        Assert.Equal(2, responses.RequestCount);
    }

    private static OAuthConfig Config() =>
        new()
        {
            Enabled = true,
            TeamDomain = "team.cloudflareaccess.com",
            Audience = "expected-audience",
            JwksUri = "https://team.cloudflareaccess.com/cdn-cgi/access/certs"
        };

    private static string CreateToken(
        RSA rsa,
        string kid,
        string audience = "expected-audience")
    {
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: "https://team.cloudflareaccess.com",
            audience: audience,
            claims: [new Claim("email", "person@example.com")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateJwks(RSA rsa, string kid)
    {
        var parameters = rsa.ExportParameters(false);

        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kid,
                    kty = "RSA",
                    alg = "RS256",
                    use = "sig",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!)
                }
            }
        });
    }

    private sealed class JwksHandler : HttpMessageHandler
    {
        public string Json { get; set; }

        public int RequestCount { get; private set; }

        public JwksHandler(string json)
        {
            Json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Json,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
