using System;
using System.Security.Cryptography;
using ByteBridge.Configuration;
using ByteBridge.Data;

namespace ByteBridge.Gateway;

/*
 * Manages OAuth sessions for the gateway.
 *
 * After a user authenticates via Cloudflare Access, a session
 * is created and stored in the SQLite database. The session
 * is identified by a random token sent as a cookie.
 *
 * Sessions expire after the configured timeout and are
 * cleaned up on access.
 */
public sealed class OAuthSessionManager
{
    private const string SessionCookieName = "efs_session";

    /*
     * Enabled is read on every single request the gateway answers
     * (it gates whether the session cookie is even worth checking), so
     * a straight database read there would trade a stale-config bug
     * for a SQLite round-trip on every request instead. A couple of
     * seconds of staleness is unnoticeable for a human flipping a
     * switch in Settings; a query per request is not.
     */
    private static readonly TimeSpan ConfigCacheDuration = TimeSpan.FromSeconds(2);

    private readonly SqliteDatabase _database;
    private readonly object _configLock = new();
    private OAuthConfig? _cachedConfig;
    private DateTime _cacheExpiresAt;

    public OAuthSessionManager(SqliteDatabase database)
    {
        _database = database;
    }

    public bool Enabled => GetConfig().Enabled;

    private OAuthConfig GetConfig()
    {
        lock (_configLock)
        {
            if (_cachedConfig == null || DateTime.UtcNow >= _cacheExpiresAt)
            {
                _cachedConfig = _database.GetOAuthConfig();
                _cacheExpiresAt = DateTime.UtcNow + ConfigCacheDuration;
            }

            return _cachedConfig;
        }
    }

    /*
     * Creates a new session for an authenticated user and
     * returns the session token.
     */
    public string CreateSession(string userEmail)
    {
        var token = GenerateSessionToken();

        var expiresAt = DateTime.UtcNow.AddMinutes(
            GetConfig().SessionTimeoutMinutes);

        _database.CreateSession(token, userEmail, expiresAt);

        return token;
    }

    /*
     * Validates a session token and returns the user email
     * if valid, or null if expired/invalid.
     */
    public string? ValidateSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var session = _database.GetSession(token);

        if (session == null)
        {
            return null;
        }

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _database.DeleteSession(token);
            return null;
        }

        return session.UserEmail;
    }

    /*
     * Revokes a session (logout).
     */
    public void RevokeSession(string token)
    {
        _database.DeleteSession(token);
    }

    /*
     * Generates a cryptographically random session token.
     */
    private static string GenerateSessionToken()
    {
        return Convert
            .ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
    }

    /*
     * Cookie helpers.
     *
     * Every cookie this gateway sets carries Secure: it is only ever
     * reached over the HTTPS hostname a Cloudflare Tunnel exposes, so
     * there is no legitimate plain-HTTP request for a cookie to leak
     * onto in the first place.
     */

    public static string FormatCookie(
        string token,
        int timeoutMinutes) =>
        FormatCookie(SessionCookieName, token, "/", timeoutMinutes * 60);

    public static string ClearCookie() =>
        ExpireCookie(SessionCookieName, "/");

    public static string? ExtractTokenFromCookie(string? cookieHeader) =>
        ExtractCookie(cookieHeader, SessionCookieName);

    public static string FormatCookie(
        string name,
        string value,
        string path,
        int maxAgeSeconds)
    {
        var expires = DateTime.UtcNow
            .AddSeconds(maxAgeSeconds)
            .ToString("R");

        return $"{name}={value}; " +
            $"Path={path}; " +
            $"HttpOnly; " +
            $"Secure; " +
            $"SameSite=Lax; " +
            $"Max-Age={maxAgeSeconds}; " +
            $"Expires={expires}";
    }

    public static string ExpireCookie(string name, string path) =>
        $"{name}=; " +
        $"Path={path}; " +
        "HttpOnly; " +
        "Secure; " +
        "SameSite=Lax; " +
        "Max-Age=0; " +
        "Expires=Thu, 01 Jan 1970 00:00:00 GMT";

    public static string? ExtractCookie(string? cookieHeader, string name)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        var cookies = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var cookie in cookies)
        {
            var trimmed = cookie.Trim();

            if (trimmed.StartsWith(
                    name + "=",
                    StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(name.Length + 1)..].Trim();
            }
        }

        return null;
    }
}

/*
 * Represents a stored session.
 */
public class Session
{
    public string Token { get; set; } = string.Empty;

    public string UserEmail { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
