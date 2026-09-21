using System;
using System.Security.Cryptography;
using System.Text;
using ByteBridge.Configuration;

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

    private readonly OAuthConfig _config;
    private readonly Data.SqliteDatabase _database;

    public OAuthSessionManager(
        OAuthConfig config,
        Data.SqliteDatabase database)
    {
        _config = config;
        _database = database;
    }

    public bool Enabled => _config.Enabled;

    /*
     * Creates a new session for an authenticated user and returns the
     * session token together with the expiry it was stored with, so
     * the cookie can expire at that same instant instead of one
     * recomputed from "now" a second time.
     */
    public (string Token, DateTime ExpiresAt) CreateSession(string userEmail)
    {
        var token = GenerateSessionToken();

        var expiresAt = DateTime.UtcNow.AddMinutes(
            _config.SessionTimeoutMinutes);

        _database.CreateSession(token, userEmail, expiresAt);

        return (token, expiresAt);
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
     * Every cookie carries Secure: the gateway is reached from a
     * browser over the HTTPS hostname a Cloudflare Tunnel exposes, so
     * there is no legitimate plain-HTTP request for one to leak onto.
     * Max-Age is sent beside Expires so a browser with a skewed clock
     * still ages the cookie out on time.
     */

    /*
     * The session cookie, expiring at the exact instant the stored
     * session does.
     */
    public static string FormatCookie(
        string token,
        DateTime expiresAt)
    {
        var maxAgeSeconds = Math.Max(
            0,
            (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalSeconds));

        return BuildCookie(
            SessionCookieName,
            token,
            "/",
            maxAgeSeconds,
            expiresAt);
    }

    public static string FormatCookie(
        string name,
        string value,
        string path,
        int maxAgeSeconds) =>
        BuildCookie(
            name,
            value,
            path,
            maxAgeSeconds,
            DateTime.UtcNow.AddSeconds(maxAgeSeconds));

    private static string BuildCookie(
        string name,
        string value,
        string path,
        int maxAgeSeconds,
        DateTime expiresAt) =>
        $"{name}={value}; " +
        $"Path={path}; " +
        "HttpOnly; " +
        "Secure; " +
        "SameSite=Lax; " +
        $"Max-Age={maxAgeSeconds}; " +
        $"Expires={expiresAt.ToUniversalTime().ToString("R")}";

    public static string ExpireCookie(string name, string path) =>
        $"{name}=; " +
        $"Path={path}; " +
        "HttpOnly; " +
        "Secure; " +
        "SameSite=Lax; " +
        "Max-Age=0; " +
        "Expires=Thu, 01 Jan 1970 00:00:00 GMT";

    public static string ClearCookie() =>
        ExpireCookie(SessionCookieName, "/");

    public static string? ExtractTokenFromCookie(
        string? cookieHeader) =>
        ExtractCookie(cookieHeader, SessionCookieName);

    /*
     * Cookie names are case-sensitive, so this matches them exactly.
     */
    public static string? ExtractCookie(
        string? cookieHeader,
        string name)
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
                    StringComparison.Ordinal))
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
