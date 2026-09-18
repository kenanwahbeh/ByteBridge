using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ByteBridge.Gateway;

namespace ByteBridge.Tests;

/*
 * Cloudflare Access's own JWKS (https://<team>/cdn-cgi/access/certs)
 * sends plain RSA keys as base64url "n" and "e", not an "x5c"
 * certificate chain. Reading "x5c" from a key that only has "n"/"e"
 * either throws (caught and swallowed) or silently finds nothing,
 * either way leaving every token unverifiable -- login "worked" in
 * the sense that it redirected and came back, and then refused every
 * single token with no clue why.
 */
public class CloudflareAccessValidatorTests
{
    [Fact]
    public void An_n_and_e_key_builds_a_usable_RSA_signing_key()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);

        var json = JsonSerializer.Serialize(new
        {
            kty = "RSA",
            kid = "test-key",
            n = Base64UrlEncoder.Encode(parameters.Modulus),
            e = Base64UrlEncoder.Encode(parameters.Exponent)
        });

        var element = JsonDocument.Parse(json).RootElement;

        var key = CloudflareAccessValidator.BuildSecurityKey(element);

        var rsaKey = Assert.IsType<RsaSecurityKey>(key);

        // Round-trips a real signature, not just "some object came back".
        var data = "sign me"u8.ToArray();
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.True(rsaKey.Rsa.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void A_key_with_neither_n_e_nor_x5c_is_rejected_rather_than_throwing()
    {
        var element = JsonDocument.Parse("""{"kty":"RSA","kid":"test-key"}""").RootElement;

        Assert.Null(CloudflareAccessValidator.BuildSecurityKey(element));
    }

    [Fact]
    public void An_x5c_certificate_chain_is_still_accepted()
    {
        using var rsa = RSA.Create(2048);

        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var json = JsonSerializer.Serialize(new
        {
            kty = "RSA",
            kid = "test-key",
            x5c = new[] { Convert.ToBase64String(cert.RawData) }
        });

        var element = JsonDocument.Parse(json).RootElement;

        var key = CloudflareAccessValidator.BuildSecurityKey(element);

        Assert.IsType<X509SecurityKey>(key);
    }
}
