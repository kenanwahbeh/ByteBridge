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

    /*
     * The JWKS URL the cache above was built from. Settings can change
     * the team domain (and so the JWKS URL) without restarting the
     * service, and the cached keys and the refresh throttle below both
     * belong to whichever URL was fetched last -- without tracking it,
     * a domain change would either return a stale key for a matching
     * kid, or have the previous domain's throttle block a fetch from
     * the new one for up to an hour.
     */
    private volatile string? _cachedJwksUrl;

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
        // Fast path, lock-free: the common case once a URL's keys are
        // already cached. _cachedJwksUrl is volatile, so seeing it
        // equal jwksUrl here also guarantees seeing whatever _keys
        // held at the moment it was last set (see RefreshKeysAsync).
        if (jwksUrl == _cachedJwksUrl && _keys.TryGetValue(kid, out var cached))
        {
            return [cached];
        }

        await RefreshKeysAsync(jwksUrl);

        /*
         * Read under the same lock RefreshKeysAsync uses to reset the
         * cache on a URL change. Without this, a concurrent request
         * validating a token against a *different*, just-changed JWKS
         * URL could reset and repopulate the cache in the instant
         * between RefreshKeysAsync returning above and a lock-free
         * read here, handing this call back a key that belongs to the
         * other request's domain instead of its own.
         */
        await _refreshLock.WaitAsync();

        try
        {
            return jwksUrl == _cachedJwksUrl &&
                _keys.TryGetValue(kid, out var afterRefresh)
                    ? [afterRefresh]
                    : [];
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /*
     * Fetches the JWKS from Cloudflare and caches the keys, unless
     * jwksUrl was already fetched within the throttle window.
     *
     * Always taking the lock first, rather than checking the throttle
     * before acquiring it, means the URL-change check and the throttle
     * check both happen inside one critical section -- there is no
     * gap between them for another call to observe a half-updated
     * cache.
     */
    private async Task RefreshKeysAsync(string jwksUrl)
    {
        await _refreshLock.WaitAsync();

        try
        {
            if (jwksUrl != _cachedJwksUrl)
            {
                /*
                 * The configured JWKS URL changed (e.g. Settings
                 * updated the team domain): the cached keys and the
                 * refresh throttle both belong to the previous
                 * domain and must not be reused for this one.
                 */
                _keys.Clear();
                _lastRefresh = DateTime.MinValue;
                _cachedJwksUrl = jwksUrl;
            }
            else if (DateTime.UtcNow - _lastRefresh < _refreshInterval)
            {
                // Avoid a thundering herd: this exact URL was already
                // refreshed recently.
                return;
            }

            var response = await _http.GetAsync(jwksUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var jwks = JsonSerializer.Deserialize<JsonElement>(json);

            var keys = jwks.GetProperty("keys");

            foreach (var keyElement in keys.EnumerateArray())
            {
                /*
                 * Each key is parsed independently: GetString() throws
                 * for any JSON type other than string or null, and one
                 * oddly-shaped key from Cloudflare must not stop every
                 * key after it in the array from being cached.
                 */
                try
                {
                    if (!keyElement.TryGetProperty("kid", out var kidProperty) ||
                        kidProperty.ValueKind != JsonValueKind.String)
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
                catch (InvalidOperationException)
                {
                    // Wrong JSON type for a string field (kid/n/e/x5c[0]).
                    // Skip this key and keep processing the rest.
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
