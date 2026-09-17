using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using ByteBridge.Data;

namespace ByteBridge.Gateway;

/*
 * Validates JWT tokens issued by Cloudflare Access.
 *
 * Cloudflare Access issues tokens signed with RSA keys that
 * rotate periodically. This validator fetches the public keys
 * from Cloudflare's JWKS endpoint and caches them, refreshing
 * when a new key ID is encountered.
 *
 * The validator checks:
 * - Signature validity against Cloudflare's public keys
 * - Issuer matches the team domain
 * - Audience matches the Access application tag
 * - Token has not expired
 */
public sealed class CloudflareAccessValidator : IDisposable
{
    private readonly SqliteDatabase _database;
    private readonly HttpClient _http;

    /*
     * Cached JWKS keys, keyed by key ID (kid).
     * Refreshed when an unknown kid is encountered.
     */
    private readonly ConcurrentDictionary<string, SecurityKey> _keys = new();

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public CloudflareAccessValidator(SqliteDatabase database)
    {
        _database = database;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /*
     * Validates a JWT token string and returns the claims
     * principal if valid, or null if invalid.
     */
    public async Task<ClaimsPrincipal?> ValidateTokenAsync(
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            /*
             * Read fresh rather than captured at construction: the
             * gateway runs for as long as the Windows service does, so
             * a team domain or audience changed in Settings needs to
             * take effect without a restart.
             */
            var config = _database.GetOAuthConfig();

            var handler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,

                ValidateAudience = true,
                ValidAudience = config.Audience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),

                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (_, _, kid, _) =>
                    GetSigningKeysAsync(kid, config.JwksUrl).GetAwaiter().GetResult()
            };

            var principal = handler.ValidateToken(
                token,
                validationParameters,
                out _);

            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /*
     * Extracts the user email from a validated JWT.
     */
    public static string? GetEmail(ClaimsPrincipal principal)
    {
        return principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst(ClaimTypes.Email)?.Value
            ?? principal?.FindFirst("email")?.Value;
    }

    /*
     * Returns signing keys for the given key ID, fetching
     * from the JWKS endpoint if needed.
     */
    private async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(
        string kid,
        string jwksUrl)
    {
        // Return cached key if available
        if (_keys.TryGetValue(kid, out var cached))
        {
            return [cached];
        }

        // Refresh the JWKS cache
        await RefreshKeysAsync(jwksUrl);

        if (_keys.TryGetValue(kid, out var afterRefresh))
        {
            return [afterRefresh];
        }

        // Key still not found after refresh
        return [];
    }

    /*
     * Fetches the JWKS from Cloudflare and caches the keys.
     */
    private async Task RefreshKeysAsync(string jwksUrl)
    {
        // Avoid thundering herd
        if (DateTime.UtcNow - _lastRefresh < _refreshInterval)
        {
            return;
        }

        await _refreshLock.WaitAsync();

        try
        {
            // Double-check after acquiring the lock
            if (DateTime.UtcNow - _lastRefresh < _refreshInterval)
            {
                return;
            }

            var response = await _http.GetAsync(jwksUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var jwks = JsonSerializer.Deserialize<JsonElement>(json);

            var keys = jwks.GetProperty("keys");

            foreach (var keyElement in keys.EnumerateArray())
            {
                if (!keyElement.TryGetProperty("kid", out var kidProperty))
                {
                    continue;
                }

                var kid = kidProperty.GetString();

                if (string.IsNullOrEmpty(kid))
                {
                    continue;
                }

                var securityKey = BuildSecurityKey(keyElement);

                if (securityKey != null)
                {
                    _keys[kid] = securityKey;
                }
            }

            _lastRefresh = DateTime.UtcNow;
        }
        catch
        {
            // If refresh fails, keep using cached keys
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /*
     * Cloudflare Access's own JWKS (https://<team>/cdn-cgi/access/certs)
     * sends plain RSA keys as "n" (modulus) and "e" (exponent), base64url
     * encoded per RFC 7518 -- not an "x5c" certificate chain. The x5c
     * form is still accepted, for any JWKS that does send one, since
     * it's a normal and valid part of the JWK spec; it just isn't what
     * Cloudflare actually sends, which is why relying on it exclusively
     * left every token unverifiable.
     */
    internal static SecurityKey? BuildSecurityKey(JsonElement keyElement)
    {
        if (keyElement.TryGetProperty("n", out var nProperty) &&
            keyElement.TryGetProperty("e", out var eProperty))
        {
            var modulus = nProperty.GetString();
            var exponent = eProperty.GetString();

            if (!string.IsNullOrEmpty(modulus) && !string.IsNullOrEmpty(exponent))
            {
                try
                {
                    var parameters = new RSAParameters
                    {
                        Modulus = Base64UrlEncoder.DecodeBytes(modulus),
                        Exponent = Base64UrlEncoder.DecodeBytes(exponent)
                    };

                    return new RsaSecurityKey(RSA.Create(parameters));
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (CryptographicException)
                {
                    return null;
                }
            }
        }

        if (keyElement.TryGetProperty("x5c", out var x5cProperty) &&
            x5cProperty.ValueKind == JsonValueKind.Array &&
            x5cProperty.GetArrayLength() > 0)
        {
            var certBase64 = x5cProperty[0].GetString();

            if (!string.IsNullOrEmpty(certBase64))
            {
                try
                {
                    var certBytes = Convert.FromBase64String(certBase64);
#pragma warning disable SYSLIB0057
                    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certBytes);
#pragma warning restore SYSLIB0057
                    return new X509SecurityKey(cert);
                }
                catch (FormatException)
                {
                    return null;
                }
                catch (CryptographicException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
