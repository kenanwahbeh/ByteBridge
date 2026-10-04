using ByteBridge.Data;

namespace ByteBridge.Enrollment;

/*
 * The parts of this gateway's own configuration that enrolment reads or
 * changes, behind an interface so the flow can be tested without a
 * settings file.
 */
public interface IGatewaySettings
{
    string ApiKey { get; }

    bool Listening { get; }

    string Address { get; }

    /*
     * Makes the gateway require an Access token for this team and
     * audience on every request that came through Cloudflare. Returns
     * false, changing nothing, when Cloudflare Access login is already
     * set up for a different application: the two share one
     * team-and-audience setting, and overwriting it would break the login
     * somebody configured on purpose.
     */
    bool TryRequireEdgeAccess(string teamDomain, string audience);

    void ClearEdgeAccess();
}

public sealed class DatabaseGatewaySettings : IGatewaySettings
{
    private readonly SqliteDatabase _database;

    public DatabaseGatewaySettings(SqliteDatabase database)
    {
        _database = database;
    }

    public string ApiKey => _database.GetGatewayConfig().ApiKey;

    public bool Listening => _database.GetGatewayConfig().AutoStart;

    public string Address => _database.GetGatewayConfig().BaseUrl;

    public bool TryRequireEdgeAccess(string teamDomain, string audience)
    {
        var config = _database.GetOAuthConfig();

        var otherApplication =
            config.Enabled
            && (!string.Equals(config.TeamDomain, teamDomain, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(config.Audience, audience, StringComparison.Ordinal));

        if (otherApplication)
        {
            return false;
        }

        config.TeamDomain = teamDomain;
        config.Audience = audience;
        config.JwksUri = $"https://{teamDomain}/cdn-cgi/access/certs";
        config.RequireEdgeAccess = true;

        _database.SaveOAuthConfig(config);

        return true;
    }

    public void ClearEdgeAccess()
    {
        var config = _database.GetOAuthConfig();

        if (!config.RequireEdgeAccess)
        {
            return;
        }

        config.RequireEdgeAccess = false;

        /*
         * The team and audience are the tunnel's only when login is not
         * using them for something of its own.
         */
        if (!config.Enabled)
        {
            config.TeamDomain = string.Empty;
            config.Audience = string.Empty;
            config.JwksUri = string.Empty;
        }

        _database.SaveOAuthConfig(config);
    }
}
