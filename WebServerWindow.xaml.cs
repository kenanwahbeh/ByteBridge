using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * Everything about the gateway that used to sit open at the top of the
 * main window, moved behind Web Server on the menu bar instead. Same
 * fields, same handlers -- just no longer competing with the
 * connections list for space on every launch.
 */
public partial class WebServerWindow : Window
{
    private readonly SqliteDatabase _database;

    private readonly GatewayServiceControl _service;

    private readonly DispatcherTimer _refresh = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    public WebServerWindow(SqliteDatabase database, GatewayServiceControl service)
    {
        InitializeComponent();

        FlowDirection = Strings.CurrentLanguage == "ar"
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _database = database;
        _service = service;

        ApplyLocalization();

        GatewayPortTextBox.Text = _database.GetGatewayConfig().Port.ToString();

        _refresh.Tick += async (_, _) => await UpdateGatewayUiAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await UpdateGatewayUiAsync();

        _refresh.Start();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _refresh.Stop();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("WebServerWindowTitle");
        PortTextBlock.Text = Strings.Get("Port");
        CopyKeyButton.Content = Strings.Get("CopyApiKey");
        RegenerateKeyButton.Content = Strings.Get("NewKey");
        StartServiceButton.Content = Strings.Get("StartService");
        CloseButton.Content = Strings.Get("Done");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task UpdateGatewayUiAsync()
    {
        var config = _database.GetGatewayConfig();
        var state = _service.State();

        RenderServiceState(state);

        var answering =
            state == ServiceState.Running
            && await _service.IsAnsweringAsync(config.BaseUrl);

        RenderGatewayState(config, state, answering);
    }

    private void RenderServiceState(ServiceState state)
    {
        StartServiceButton.Visibility =
            state == ServiceState.Stopped
                ? Visibility.Visible
                : Visibility.Collapsed;

        ServiceStatusTextBlock.Text = state switch
        {
            ServiceState.Running => Strings.Get("ServiceRunning"),
            ServiceState.Stopped => Strings.Get("ServiceStopped"),
            ServiceState.Pending => Strings.Get("ServicePending"),
            _ => Strings.Get("ServiceNotInstalled")
        };
    }

    private void RenderGatewayState(
        GatewayConfig config,
        ServiceState state,
        bool answering)
    {
        if (answering)
        {
            GatewayStatusTextBlock.Text = Strings.Format("Answering", config.BaseUrl);
            GatewayStatusTextBlock.Foreground = Brushes.Green;
            GatewayHintTextBlock.Text = Strings.Format("TunnelHint", config.BaseUrl);
        }
        else if (!config.AutoStart)
        {
            GatewayStatusTextBlock.Text = Strings.Get("TurnedOff");
            GatewayStatusTextBlock.Foreground = Brushes.Gray;
            GatewayHintTextBlock.Text = Strings.Get("TurnedOffHint");
        }
        else if (state == ServiceState.Running)
        {
            GatewayStatusTextBlock.Text = Strings.Get("Starting");
            GatewayStatusTextBlock.Foreground = Brushes.DarkOrange;
            GatewayHintTextBlock.Text = Strings.Format("StartingHint", config.BaseUrl);
        }
        else
        {
            GatewayStatusTextBlock.Text = Strings.Get("NotRunning");
            GatewayStatusTextBlock.Foreground = Brushes.Red;
            GatewayHintTextBlock.Text = Strings.Get("NotRunningHint");
        }

        GatewayToggleButton.Content =
            config.AutoStart ? Strings.Get("TurnOff") : Strings.Get("TurnOn");

        GatewayPortTextBox.IsEnabled = !config.AutoStart;
    }

    private async void GatewayToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _database.GetGatewayConfig();

        if (config.AutoStart)
        {
            config.AutoStart = false;

            _database.SaveGatewayConfig(config);

            await UpdateGatewayUiAsync();

            return;
        }

        var portText = GatewayPortTextBox.Text.Trim();

        if (!int.TryParse(portText, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show(
                Strings.Get("InvalidPort"),
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        config.Port = port;
        config.AutoStart = true;

        _database.SaveGatewayConfig(config);

        await UpdateGatewayUiAsync();
    }

    private async void StartServiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.Format("ServiceError", ex.Message),
                Strings.Get("ServiceTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        await UpdateGatewayUiAsync();
    }

    private void CopyKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_database.GetGatewayConfig().ApiKey);

            MessageBox.Show(
                Strings.Get("CopyKeyMessage"),
                Strings.Get("CopyKeyTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.Format("CopyKeyError", ex.Message),
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RegenerateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            Strings.Get("NewKeyConfirm"),
            Strings.Get("NewKeyTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _database.RegenerateApiKey();

        MessageBox.Show(
            Strings.Get("NewKeyMessage"),
            Strings.Get("AppTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
