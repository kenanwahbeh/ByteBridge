using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using ByteBridge.Configuration;

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
    private volatile OAuthConfig _config;
    private readonly HttpClient _http;

    /*
     * Cached JWKS keys, keyed by key ID (kid).
     * Refreshed when an unknown kid is encountered.
     */
    private volatile IReadOnlyDictionary<string, SecurityKey> _keys =
        new Dictionary<string, SecurityKey>(StringComparer.Ordinal);

    private DateTime _lastRefresh = DateTime.MinValue;

    private DateTime _lastUnknownKidRefresh = DateTime.MinValue;

    private static readonly TimeSpan RefreshInterval =
        TimeSpan.FromHours(1);

    private static readonly TimeSpan UnknownKidRefreshInterval =
        TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public CloudflareAccessValidator(OAuthConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    internal CloudflareAccessValidator(
        OAuthConfig config,
        HttpMessageHandler handler)
    {
        _config = config;
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void UpdateConfig(OAuthConfig config)
    {
        _refreshLock.Wait();

        try
        {
            _config = config;
            _keys = new Dictionary<string, SecurityKey>(
                StringComparer.Ordinal);
            _lastRefresh = DateTime.MinValue;
            _lastUnknownKidRefresh = DateTime.MinValue;
        }
        finally
        {
            _refreshLock.Release();
        }
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
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
            {
                return null;
            }

            var unvalidated = handler.ReadJwtToken(token);

            if (!string.Equals(
                    unvalidated.Header.Alg,
                    SecurityAlgorithms.RsaSha256,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(unvalidated.Header.Kid))
            {
                return null;
            }

            var signingKeys = await GetSigningKeysAsync(
                unvalidated.Header.Kid);

            if (signingKeys is null)
            {
                return null;
            }

            var config = _config;

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,

                ValidateAudience = true,
                ValidAudience = config.Audience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),

                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                IssuerSigningKeys = signingKeys.Values
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

    private async Task<IReadOnlyDictionary<string, SecurityKey>?>
        GetSigningKeysAsync(
        string kid)
    {
        var snapshot = _keys;
        var keyIsCached = snapshot.ContainsKey(kid);

        if (keyIsCached &&
            DateTime.UtcNow - _lastRefresh < RefreshInterval)
        {
            return snapshot;
        }

        await RefreshKeysAsync(kid);

        snapshot = _keys;

        return snapshot.ContainsKey(kid)
            ? snapshot
            : null;
    }

    /*
     * Fetches the JWKS from Cloudflare and caches the keys.
     */
    private async Task RefreshKeysAsync(string requestedKid)
    {
        await _refreshLock.WaitAsync();

        try
        {
            var now = DateTime.UtcNow;
            var snapshot = _keys;
            var keyIsCached = snapshot.ContainsKey(requestedKid);

            if (keyIsCached &&
                now - _lastRefresh < RefreshInterval)
            {
                return;
            }

            /*
             * A random kid must not turn token validation into an
             * unbounded HTTP client. The first fetch and a genuine new
             * key are immediate; subsequent unknown kids share a short
             * refresh cooldown.
             */
            if (!keyIsCached)
            {
                if (now - _lastUnknownKidRefresh <
                    UnknownKidRefreshInterval)
                {
                    return;
                }

                _lastUnknownKidRefresh = now;
            }

            var config = _config;

            using var response = await _http.GetAsync(config.JwksUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var nextKeys = ParseSigningKeys(json)
                .Where(key => key is RsaSecurityKey)
                .Where(key => !string.IsNullOrWhiteSpace(key.KeyId))
                .ToDictionary(
                    key => key.KeyId,
                    StringComparer.Ordinal);

            if (nextKeys.Count == 0)
            {
                return;
            }

            _keys = nextKeys;
            _lastRefresh = now;

            if (nextKeys.ContainsKey(requestedKid))
            {
                // A legitimate bootstrap/rotation must not consume the
                // cooldown reserved for unknown attacker-controlled kids.
                _lastUnknownKidRefresh = DateTime.MinValue;
            }
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
     * Cloudflare publishes ordinary RSA JWKs containing modulus (n) and
     * exponent (e). JsonWebKeySet handles that standard representation as
     * well as certificate-backed keys, instead of assuming x5c exists.
     */
    internal static IReadOnlyList<SecurityKey> ParseSigningKeys(string json)
    {
        return new JsonWebKeySet(json)
            .GetSigningKeys()
            .ToList();
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
