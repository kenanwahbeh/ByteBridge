using System;
using System.Windows;
using System.Windows.Media;
using ByteBridge.Data;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * The Cloudflare Access (OAuth) card, moved off the main window into
 * its own dialog behind the Cloudflare Tunnel menu item.
 */
public partial class CloudflareTunnelWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SqliteDatabase _database;

    public CloudflareTunnelWindow(SqliteDatabase database)
    {
        InitializeComponent();

        FlowDirection = Strings.CurrentLanguage == "ar"
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
    }

    private void LoadOAuthConfig()
    {
        var oauthConfig = _database.GetOAuthConfig();

        TeamDomainTextBox.Text = oauthConfig.TeamDomain;
        AudienceTextBox.Text = oauthConfig.Audience;
        PublicHostnameTextBox.Text = ExtractHostname(oauthConfig.RedirectUri);

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
    private static string ExtractHostname(string redirectUri)
    {
        return Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;
    }

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

            if (string.IsNullOrEmpty(teamDomain))
            {
                MessageBox.Show(
                    Strings.Get("OAuthTeamDomainRequired"),
                    Strings.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
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

            var publicHostname = PublicHostnameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(publicHostname))
            {
                MessageBox.Show(
                    Strings.Get("OAuthPublicHostnameRequired"),
                    Strings.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            config.Enabled = true;
            config.TeamDomain = teamDomain;
            config.Audience = audience;
            config.JwksUri = $"https://{teamDomain}/cdn-cgi/access/certs";
            config.RedirectUri = $"https://{publicHostname}/auth/callback";

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
