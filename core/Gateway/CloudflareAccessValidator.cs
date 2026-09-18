using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly OAuthConfig _config;
    private readonly HttpClient _http;

    /*
     * Cached JWKS keys, keyed by key ID (kid).
     * Refreshed when an unknown kid is encountered.
     */
    private readonly ConcurrentDictionary<string, SecurityKey> _keys = new();

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public CloudflareAccessValidator(OAuthConfig config)
        : this(config, TimeSpan.FromHours(1))
    {
    }

    /*
     * Lets tests shrink the refresh throttle so a rotated key
     * (a kid the cache has never seen) is picked up immediately
     * instead of waiting out the production interval.
     */
    internal CloudflareAccessValidator(OAuthConfig config, TimeSpan refreshInterval)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _refreshInterval = refreshInterval;
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

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config.Issuer,

                ValidateAudience = true,
                ValidAudience = _config.Audience,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),

                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                    GetSigningKeysAsync(kid).GetAwaiter().GetResult()
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
        string kid)
    {
        // Return cached key if available
        if (_keys.TryGetValue(kid, out var cached))
        {
            return [cached];
        }

        // Refresh the JWKS cache
        await RefreshKeysAsync();

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
    private async Task RefreshKeysAsync()
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

            var response = await _http.GetAsync(_config.JwksUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var jwks = JsonSerializer.Deserialize<JsonElement>(json);

            var keys = jwks.GetProperty("keys");

            foreach (var keyElement in keys.EnumerateArray())
            {
                /*
                 * One oddly-shaped key (a bad kid, a non-RSA entry,
                 * unparsable base64) must not cost us every other key
                 * in the same JWKS response.
                 */
                try
                {
                    if (!keyElement.TryGetProperty("kid", out var kidElement))
                    {
                        continue;
                    }

                    var kid = kidElement.GetString();

                    if (string.IsNullOrEmpty(kid))
                    {
                        continue;
                    }

                    if (!keyElement.TryGetProperty("n", out var nElement) ||
                        !keyElement.TryGetProperty("e", out var eElement))
                    {
                        continue;
                    }

                    var n = nElement.GetString();
                    var e = eElement.GetString();

                    if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(e))
                    {
                        continue;
                    }

                    var rsa = RSA.Create();
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = Base64UrlEncoder.DecodeBytes(n),
                        Exponent = Base64UrlEncoder.DecodeBytes(e)
                    });

                    _keys[kid] = new RsaSecurityKey(rsa) { KeyId = kid };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Cloudflare Access JWKS key entry skipped: {ex.Message}");
                }
            }

            _lastRefresh = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Keep using cached keys, but don't hide the failure.
            Console.Error.WriteLine(
                $"Cloudflare Access JWKS refresh failed ({_config.JwksUrl}): {ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
