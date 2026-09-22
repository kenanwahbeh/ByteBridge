using System;
using System.Collections.Generic;
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
    private const string LoginStateCookieName = "efs_oauth_state";

    private static readonly TimeSpan LoginStateLifetime =
        TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LoginStateCleanupInterval =
        TimeSpan.FromMinutes(1);

    private const int MaxPendingLoginStates = 1024;

    private const int MaxPendingLoginStatesPerClient = 8;

    private volatile OAuthConfig _config;
    private readonly Data.SqliteDatabase _database;

    private readonly object _loginStateSync = new();

    private readonly Dictionary<string, PendingLoginState> _loginStates =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _clientLoginStateCounts =
        new(StringComparer.Ordinal);

    private DateTime _nextLoginStateCleanup = DateTime.MinValue;

    public OAuthSessionManager(
        OAuthConfig config,
        Data.SqliteDatabase database)
    {
        _config = config;
        _database = database;
    }

    public bool Enabled => _config.Enabled;

    public void UpdateConfig(OAuthConfig config)
    {
        _config = config;
    }

    /*
     * Creates a new session for an authenticated user and
     * returns the session token.
     */
    public string CreateSession(string userEmail)
    {
        var token = GenerateSessionToken();

        var expiresAt = DateTime.UtcNow.AddMinutes(
            _config.SessionTimeoutMinutes);

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

    public string? CreateLoginState(string clientKey)
    {
        var now = DateTime.UtcNow;

        lock (_loginStateSync)
        {
            if (now >= _nextLoginStateCleanup)
            {
                RemoveExpiredLoginStates(now);

                _nextLoginStateCleanup =
                    now.Add(LoginStateCleanupInterval);
            }

            _clientLoginStateCounts.TryGetValue(
                clientKey,
                out var clientCount);

            if (_loginStates.Count >= MaxPendingLoginStates ||
                clientCount >= MaxPendingLoginStatesPerClient)
            {
                return null;
            }

            var value = GenerateSessionToken();

            _loginStates[value] = new PendingLoginState(
                now.Add(LoginStateLifetime),
                clientKey);

            _clientLoginStateCounts[clientKey] = clientCount + 1;

            return value;
        }
    }

    public bool ConsumeLoginState(
        string? returnedState,
        string? cookieState)
    {
        if (string.IsNullOrEmpty(returnedState) ||
            string.IsNullOrEmpty(cookieState))
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(returnedState),
                Encoding.UTF8.GetBytes(cookieState)))
        {
            return false;
        }

        lock (_loginStateSync)
        {
            if (!_loginStates.Remove(
                    returnedState,
                    out var pending))
            {
                return false;
            }

            DecrementClientCount(pending.ClientKey);

            return pending.ExpiresAt >= DateTime.UtcNow;
        }
    }

    private void RemoveExpiredLoginStates(DateTime now)
    {
        var expired = new List<string>();

        foreach (var state in _loginStates)
        {
            if (state.Value.ExpiresAt < now)
            {
                expired.Add(state.Key);
            }
        }

        foreach (var state in expired)
        {
            if (_loginStates.Remove(state, out var pending))
            {
                DecrementClientCount(pending.ClientKey);
            }
        }
    }

    private void DecrementClientCount(string clientKey)
    {
        if (!_clientLoginStateCounts.TryGetValue(
                clientKey,
                out var count))
        {
            return;
        }

        if (count <= 1)
        {
            _clientLoginStateCounts.Remove(clientKey);
        }
        else
        {
            _clientLoginStateCounts[clientKey] = count - 1;
        }
    }

    private readonly record struct PendingLoginState(
        DateTime ExpiresAt,
        string ClientKey);

    /*
     * Cookie helpers.
     */

    public static string FormatCookie(
        string token,
        int timeoutMinutes)
    {
        var expires = DateTime.UtcNow
            .AddMinutes(timeoutMinutes)
            .ToString("R");

        return $"{SessionCookieName}={token}; " +
            $"Path=/; " +
            $"HttpOnly; " +
            $"Secure; " +
            $"SameSite=Lax; " +
            $"Expires={expires}";
    }

    public static string FormatLoginStateCookie(string state)
    {
        var expires = DateTime.UtcNow
            .Add(LoginStateLifetime)
            .ToString("R");

        return $"{LoginStateCookieName}={state}; " +
            $"Path=/auth/; " +
            $"HttpOnly; " +
            $"Secure; " +
            $"SameSite=Lax; " +
            $"Expires={expires}";
    }

    public static string ClearCookie()
    {
        return $"{SessionCookieName}=; " +
            "Path=/; " +
            "HttpOnly; " +
            "Secure; " +
            "SameSite=Lax; " +
            "Expires=Thu, 01 Jan 1970 00:00:00 GMT";
    }

    public static string? ExtractTokenFromCookie(
        string? cookieHeader)
    {
        return ExtractCookie(cookieHeader, SessionCookieName);
    }

    public static string? ExtractLoginStateFromCookie(
        string? cookieHeader)
    {
        return ExtractCookie(cookieHeader, LoginStateCookieName);
    }

    public static string? ExtractCloudflareAccessTokenFromCookie(
        string? cookieHeader)
    {
        return ExtractCookie(cookieHeader, "CF_Authorization");
    }

    private static string? ExtractCookie(
        string? cookieHeader,
        string cookieName)
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
                    cookieName + "=",
                    StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(cookieName.Length + 1)..].Trim();
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
