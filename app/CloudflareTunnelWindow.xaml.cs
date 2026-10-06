using System;
using System.Windows;
using System.Windows.Media;
using ByteBridge.Data;
using ByteBridge.Configuration;
using ByteBridge.Gateway;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * The Cloudflare Access (OAuth) card, moved off the main window into
 * its own dialog behind the Cloudflare Tunnel menu item.
 */
public partial class CloudflareTunnelWindow : Window
{
    private readonly SqliteDatabase _database;

    public CloudflareTunnelWindow(SqliteDatabase database)
    {
        InitializeComponent();

        FlowDirection = Strings.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _database = database;

        ApplyLocalization();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadOAuthConfig();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("CloudflareTunnelWindowTitle");
        TeamDomainTextBlock.Text = Strings.Get("TeamDomain");
        AudienceTextBlock.Text = Strings.Get("Audience");
        PublicHostnameTextBlock.Text = Strings.Get("PublicHostname");
        CloseButton.Content = Strings.Get("Done");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadOAuthConfig()
    {
        var oauthConfig = _database.GetOAuthConfig();

        TeamDomainTextBox.Text = oauthConfig.TeamDomain;
        AudienceTextBox.Text = oauthConfig.Audience;
        PublicHostnameTextBox.Text =
            OAuthConfig.HostFromRedirectUri(oauthConfig.RedirectUri);

        if (oauthConfig.Enabled)
        {
            OAuthStatusTextBlock.Text = Strings.Get("Enabled");
            OAuthStatusTextBlock.Foreground = Brushes.Green;
            OAuthToggleButton.Content = Strings.Get("Disable");
            TeamDomainTextBox.IsEnabled = false;
            AudienceTextBox.IsEnabled = false;
            PublicHostnameTextBox.IsEnabled = false;
        }
        else
        {
            OAuthStatusTextBlock.Text = Strings.Get("NotConfigured");
            OAuthStatusTextBlock.Foreground = Brushes.Gray;
            OAuthToggleButton.Content = Strings.Get("Enable");
            TeamDomainTextBox.IsEnabled = true;
            AudienceTextBox.IsEnabled = true;
            PublicHostnameTextBox.IsEnabled = true;
        }
    }

    /*
     * RedirectUri is stored as the full callback URL. The field only
     * asks for the hostname, since that's the one piece an admin
     * actually has to look up (the tunnel's public hostname); the
     * scheme and path are always the same.
     */
    private void OAuthToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _database.GetOAuthConfig();

        if (config.Enabled)
        {
            config.Enabled = false;
            _database.SaveOAuthConfig(config);

            MessageBox.Show(
                Strings.Get("OAuthDisabled"),
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            var teamDomain = TeamDomainTextBox.Text.Trim();
            var audience = AudienceTextBox.Text.Trim();
            var publicHostname = PublicHostnameTextBox.Text.Trim();

            /*
             * A hostname, not a URL. These two go into strings that
             * already carry the scheme, and the command line has always
             * refused anything else so that nobody ends up with
             * https://https://my-team.cloudflareaccess.com/cdn-cgi/...
             * as the JWKS URI -- which resolves to nothing, and so means
             * no token can ever be checked: login on, every request
             * afterwards refused, and no way back out of this dialog.
             *
             * Asked here as well as in the CLI because an admin types the
             * whole URL, and this is the only place they can.
             */
            foreach (var (host, example) in new[]
                     {
                         (teamDomain, "my-team.cloudflareaccess.com"),
                         (publicHostname, "tunnel.example.com")
                     })
            {
                if (!OAuthConfig.IsBareHostname(host))
                {
                    MessageBox.Show(
                        Strings.Format("OAuthHostnameRequired", example),
                        Strings.Get("AppTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            if (string.IsNullOrEmpty(audience))
            {
                MessageBox.Show(
                    Strings.Get("OAuthAudienceRequired"),
                    Strings.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            /*
             * The redirect URI is now built from the checked hostname,
             * so all three are present before Enabled is set: without it
             * the browser is sent to a callback that does not exist and
             * no session can be established at all.
             */
            config.Enabled = true;
            config.TeamDomain = teamDomain;
            config.Audience = audience;
            config.JwksUri = OAuthConfig.JwksUriFor(teamDomain);
            config.RedirectUri = OAuthConfig.RedirectUriFor(publicHostname);

            _database.SaveOAuthConfig(config);

            MessageBox.Show(
                Strings.Get("OAuthEnabled"),
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        LoadOAuthConfig();
    }
}
