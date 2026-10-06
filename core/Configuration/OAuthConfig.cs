namespace ByteBridge.Configuration;

/*
 * Configuration for Cloudflare Access OAuth integration.
 *
 * The gateway validates JWT tokens issued by Cloudflare Access
 * when users authenticate through the configured identity
 * providers (GitHub, Google, One-time PIN, etc.).
 *
 * Cloudflare handles the identity provider logic; the gateway
 * only validates the JWT and extracts user information.
 */
public class OAuthConfig
{
    /*
     * When enabled, the gateway requires a valid Cloudflare
     * Access session for all endpoints except /health and
     * /auth/*.
     */
    public bool Enabled { get; set; }

    /*
     * The Cloudflare Access team domain, e.g.,
     * "my-team.cloudflareaccess.com".
     *
     * Used to construct the issuer claim for JWT validation.
     */
    public string TeamDomain { get; set; } = string.Empty;

    /*
     * The audience tag from the Access application.
     *
     * Found in Zero Trust → Access → Applications →
     * Settings → Application Audience (AUD) tag.
     */
    public string Audience { get; set; } = string.Empty;

    /*
     * Cloudflare's JWKS endpoint for signature verification.
     *
     * Typically:
     * "https://<team-domain>/cdn-cgi/access/certs"
     */
    public string JwksUri { get; set; } = string.Empty;

    /*
     * How long a session remains valid after login.
     */
    public int SessionTimeoutMinutes { get; set; } = 60;

    /*
     * The OAuth redirect URI for the login callback.
     *
     * This must be registered in the Access application's
     * allowed redirect URIs.
     */
    public string RedirectUri { get; set; } = string.Empty;

    /*
     * When on, a request that arrives through Cloudflare must also carry
     * a valid Access token (Cf-Access-Jwt-Assertion) for this team and
     * audience, on top of the API key. It is what makes "only the owner
     * can reach this hostname" hold even for a request that somehow got
     * past the edge.
     *
     * It is a requirement, never an alternative: it does not let anyone
     * in without the key, and it does not depend on Enabled (the login
     * flow). Requests that did not come through Cloudflare at all, such
     * as a local tool calling 127.0.0.1, are not asked for a token.
     */
    public bool RequireEdgeAccess { get; set; }

    public string Issuer =>
        $"https://{TeamDomain}";

    public string JwksUrl =>
        string.IsNullOrWhiteSpace(JwksUri)
            ? $"https://{TeamDomain}/cdn-cgi/access/certs"
            : JwksUri;

    /*
     * Whether this text is a bare hostname, which is what these two
     * settings splice into a URL.
     *
     * The CLI asked this and refused anything else, precisely so that
     * nobody produces https://https://... . The Cloudflare dialog did not
     * ask it: an admin who typed the whole URL got it accepted, the
     * JWKS URI written as "https://https://team/cdn-cgi/access/certs",
     * login turned on -- and every request afterwards refused, because a
     * JWKS URI that cannot resolve means no token can ever be checked.
     * The dialog offered no way back from that but finding the setting
     * and turning it off again by hand.
     *
     * One rule now, asked by both surfaces.
     */
    public static bool IsBareHostname(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown;

    public static string JwksUriFor(string teamDomain) =>
        $"https://{teamDomain}/cdn-cgi/access/certs";

    public static string RedirectUriFor(string publicHostname) =>
        $"https://{publicHostname}/auth/callback";

    /*
     * The hostname a saved redirect URI belongs to, for the dialog that
     * has to show the operator which one it is without asking them to
     * read a URL.
     */
    public static string HostFromRedirectUri(string? redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;

    /*
     * Whether login can be turned on with what is on file.
     *
     * The CLI refused unless all three are present, and its reason is the
     * one above: half a configuration is a gateway that demands a token
     * nobody can get. The dialog had its own three emptiness checks and
     * missed the redirect URI entirely -- the public hostname is the
     * field furthest from the team domain in the window, and the one that
     * was easy to leave blank.
     */
    public bool CanEnable =>
        !string.IsNullOrWhiteSpace(TeamDomain)
        && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(RedirectUri);

    public bool CanRequireEdgeAccess =>
        !string.IsNullOrWhiteSpace(TeamDomain)
        && !string.IsNullOrWhiteSpace(Audience);

    /*
     * Whether the running gateway's OAuth settings are the ones on file.
     *
     * Another field list, and this one is on the security type: leave a
     * field off it and a change to it never reaches the running gateway,
     * and the log line written beside the call claims it was applied.
     */
    public bool Matches(OAuthConfig other) =>
        Enabled == other.Enabled
        && TeamDomain == other.TeamDomain
        && Audience == other.Audience
        && JwksUri == other.JwksUri
        && RedirectUri == other.RedirectUri
        && SessionTimeoutMinutes == other.SessionTimeoutMinutes
        && RequireEdgeAccess == other.RequireEdgeAccess;

    public static string[] ComparedSettings =>
    [
        nameof(Enabled),
        nameof(TeamDomain),
        nameof(Audience),
        nameof(JwksUri),
        nameof(RedirectUri),
        nameof(SessionTimeoutMinutes),
        nameof(RequireEdgeAccess)
    ];
}
