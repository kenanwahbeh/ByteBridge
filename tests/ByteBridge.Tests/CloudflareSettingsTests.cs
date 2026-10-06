using ByteBridge.Configuration;
using Xunit;

namespace ByteBridge.Tests;

/*
 * The Cloudflare Access settings, asked of one place.
 *
 * The CLI validated these three hostnames and refused anything that was
 * not a bare hostname -- precisely so that nobody ends up with
 * https://https://my-team.cloudflareaccess.com/cdn-cgi/access/certs as
 * the JWKS URI. The Cloudflare dialog in the control panel asked only
 * whether the boxes were empty.
 *
 * So an admin who typed the whole URL, as one does, got it accepted and
 * login turned on, and every request after that was refused: a JWKS URI
 * that cannot resolve means no token can ever be checked, so the gateway
 * asks for a token it can never validate. The dialog had no way back
 * from that except finding the setting and turning it off by hand.
 *
 * The same gap in the other direction: the CLI refuses to turn login on
 * unless the team domain, the audience and the redirect URI are all on
 * file, and the dialog checked the first two and not the third -- the
 * public hostname being the field furthest from the team domain in the
 * window, and the easiest to leave empty.
 *
 * None of it was reachable by a test: the dialog is a WPF window, and the
 * two rules were written separately in two projects.
 */
public class CloudflareSettingsTests
{
    /*
     * What these two settings get spliced into.
     *
     * A scheme typed into a hostname field is the whole bug: it is not a
     * hostname, and the result is a URL with two schemes in it, which
     * resolves to nothing.
     */
    [Theory]
    [InlineData("my-team.cloudflareaccess.com")]
    [InlineData("my-team.cloudflareaccess.com.")]
    [InlineData("tunnel.example.com")]
    public void A_bare_hostname_is_accepted(string host)
    {
        Assert.True(OAuthConfig.IsBareHostname(host));
    }

    [Theory]
    [InlineData("https://my-team.cloudflareaccess.com")]
    [InlineData("http://my-team.cloudflareaccess.com")]
    [InlineData("my-team.cloudflareaccess.com/cdn-cgi")]
    [InlineData("my team.cloudflareaccess.com")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_is_not_a_bare_hostname_is_refused(string? host)
    {
        Assert.False(OAuthConfig.IsBareHostname(host));
    }

    /*
     * The two URLs, built once.
     *
     * They were built in three places -- the CLI, the dialog and the
     * enrolment settings -- and were byte-identical strings, which is
     * exactly why a change to one of them would have been a change to
     * only one of them.
     */
    [Fact]
    public void The_urls_are_built_from_the_two_hostnames()
    {
        Assert.Equal(
            "https://my-team.cloudflareaccess.com/cdn-cgi/access/certs",
            OAuthConfig.JwksUriFor("my-team.cloudflareaccess.com"));

        Assert.Equal(
            "https://tunnel.example.com/auth/callback",
            OAuthConfig.RedirectUriFor("tunnel.example.com"));
    }

    /*
     * What the dialog shows the operator as the hostname it is using,
     * taken out of a saved URL rather than a box.
     */
    [Theory]
    [InlineData("https://tunnel.example.com/auth/callback", "tunnel.example.com")]
    [InlineData("", "")]
    [InlineData("not a url", "")]
    [InlineData(null, "")]
    public void The_hostname_is_read_back_out_of_a_saved_redirect(
        string? redirectUri,
        string expected)
    {
        Assert.Equal(expected, OAuthConfig.HostFromRedirectUri(redirectUri));
    }

    /*
     * Whether login can be turned on at all.
     *
     * All three are needed. The redirect URI is the one the CLI already
     * required and the dialog did not: turning login on without it means
     * a browser is sent to a callback that does not exist, so no session
     * can ever be established and the gateway asks for a token no one can
     * obtain.
     */
    [Fact]
    public void Login_needs_a_team_an_audience_and_a_callback()
    {
        Assert.True(Complete().CanEnable);

        // Each one on its own is enough to refuse.
        foreach (var missing in new Action<OAuthConfig>[]
                 {
                     config => config.TeamDomain = string.Empty,
                     config => config.Audience = string.Empty,
                     config => config.RedirectUri = string.Empty
                 })
        {
            var config = Complete();

            missing(config);

            Assert.False(config.CanEnable);
        }
    }

    /*
     * Blank is not the same as absent here: a space typed into the box is
     * not a hostname, and treating it as one writes a URL with a space in
     * it.
     */
    [Fact]
    public void Whitespace_is_not_a_value()
    {
        var config = Complete();

        config.TeamDomain = "   ";

        Assert.False(config.CanEnable);
        Assert.False(config.CanRequireEdgeAccess);
    }

    /*
     * Requiring an Access token needs only the team and the audience --
     * no login flow, and therefore no callback. It is a requirement on top
     * of the API key rather than an alternative to it, so it does not
     * depend on Enabled.
     */
    [Fact]
    public void Requiring_edge_access_needs_only_a_team_and_an_audience()
    {
        var config = new OAuthConfig
        {
            TeamDomain = "my-team.cloudflareaccess.com",
            Audience = "audience-tag"
        };

        Assert.True(config.CanRequireEdgeAccess);
        Assert.False(config.CanEnable);

        config.RedirectUri = OAuthConfig.RedirectUriFor("tunnel.example.com");

        Assert.True(config.CanEnable);
    }

    private static OAuthConfig Complete() =>
        new()
        {
            TeamDomain = "my-team.cloudflareaccess.com",
            Audience = "audience-tag",
            RedirectUri = OAuthConfig.RedirectUriFor("tunnel.example.com")
        };
}
