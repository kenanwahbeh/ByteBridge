using System;
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
     * The JWKS URL and the keys fetched from it, published as one
     * immutable object. A single volatile reference read gives a
     * caller a self-consistent (url, keys) pair with no lock: there is
     * no way to observe the url from one publish and the keys from
     * another, which is what made the previous url-field-plus-
     * dictionary design racy under a concurrent URL change.
     *
     * Reads never take _refreshLock at all -- only a fetch in
     * RefreshKeysAsync does, to serialize concurrent refreshes. A
     * cache hit must never wait behind another request's slow (or
     * hung) network call to Cloudflare.
     */
    private volatile JwksSnapshot? _snapshot;

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private sealed record JwksSnapshot(
        string JwksUrl,
        IReadOnlyDictionary<string, SecurityKey> Keys);

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
        if (TryGetCachedKey(jwksUrl, kid) is { } cached)
        {
            return [cached];
        }

        await RefreshKeysAsync(jwksUrl);

        return TryGetCachedKey(jwksUrl, kid) is { } afterRefresh
            ? [afterRefresh]
            : [];
    }

    /*
     * Lock-free: a single read of the volatile _snapshot reference
     * gives a self-consistent url/keys pair, so there is nothing here
     * that a concurrent refresh (which publishes a brand new
     * JwksSnapshot rather than mutating one in place) could tear.
     */
    private SecurityKey? TryGetCachedKey(string jwksUrl, string kid)
    {
        var snapshot = _snapshot;

        return snapshot != null &&
            snapshot.JwksUrl == jwksUrl &&
            snapshot.Keys.TryGetValue(kid, out var key)
                ? key
                : null;
    }

    /*
     * Fetches the JWKS from Cloudflare and publishes a new snapshot,
     * unless jwksUrl was already fetched within the throttle window.
     *
     * _refreshLock only ever serializes concurrent fetches against
     * each other; it is never held while a reader is looking up a
     * key, so a request whose key is already cached is never made to
     * wait behind another request's slow (or hung) call to Cloudflare.
     */
    private async Task RefreshKeysAsync(string jwksUrl)
    {
        var before = _snapshot;

        if (before != null &&
            before.JwksUrl == jwksUrl &&
            DateTime.UtcNow - _lastRefresh < _refreshInterval)
        {
            // Avoid a thundering herd: this exact URL was already
            // refreshed recently.
            return;
        }

        await _refreshLock.WaitAsync();

        try
        {
            // Re-check after acquiring the lock: another caller may
            // have already refreshed this exact URL while this one
            // was waiting.
            before = _snapshot;

            if (before != null &&
                before.JwksUrl == jwksUrl &&
                DateTime.UtcNow - _lastRefresh < _refreshInterval)
            {
                return;
            }

            var response = await _http.GetAsync(jwksUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var jwks = JsonSerializer.Deserialize<JsonElement>(json);

            var keys = jwks.GetProperty("keys");

            /*
             * Starts from the previous snapshot's keys when the URL
             * has not changed, so a kid Cloudflare stops listing
             * mid-rotation is not dropped by one fetch that happens
             * to land during the overlap -- matching the accumulate-
             * until-the-URL-changes behavior this cache always had.
             * A URL change starts from nothing, same as before.
             */
            var newKeys = before != null && before.JwksUrl == jwksUrl
                ? new Dictionary<string, SecurityKey>(before.Keys)
                : [];

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
                        newKeys[kid] = securityKey;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Wrong JSON type for a string field (kid/n/e/x5c[0]).
                    // Skip this key and keep processing the rest.
                }
            }

            _snapshot = new JwksSnapshot(jwksUrl, newKeys);
            _lastRefresh = DateTime.UtcNow;
        }
        catch
        {
            // If refresh fails, keep using whatever snapshot is
            // already published.
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
