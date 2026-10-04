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
}
