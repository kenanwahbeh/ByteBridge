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

    private readonly GatewaySwitch _switch;

    // True while a start or stop is waiting on Windows.
    private bool _switching;

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
        _switch = new GatewaySwitch(database, service);

        ApplyLocalization();

        var config = _database.GetGatewayConfig();

        GatewayPortTextBox.Text = config.Port.ToString();
        ShowLockout(config);

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
        LockoutAttemptsTextBlock.Text = Strings.Get("LockoutAttempts");
        LockoutMinutesTextBlock.Text = Strings.Get("LockoutMinutes");
        CopyKeyButton.Content = Strings.Get("CopyApiKey");
        RegenerateKeyButton.Content = Strings.Get("NewKey");
        CloseButton.Content = Strings.Get("Done");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /*
     * The block is stored in seconds, so the CLI can set any length; the
     * box is in minutes, rounded up, and only written back when the
     * person actually changes it.
     */
    private void ShowLockout(GatewayConfig config)
    {
        LockoutAttemptsTextBox.Text = config.AuthMaxFailures.ToString();

        // The window is settable from the command line, so it is read, not assumed.
        LockoutHintTextBlock.Text =
            Strings.Format("LockoutHint", config.AuthWindowSeconds);

        LockoutMinutesTextBox.Text =
            ((config.AuthBlockSeconds + 59) / 60).ToString();

        LockoutMinutesTextBox.IsEnabled = config.AuthMaxFailures > 0;
    }

    private void LockoutAttemptsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var config = _database.GetGatewayConfig();

        if (!int.TryParse(LockoutAttemptsTextBox.Text.Trim(), out var attempts)
            || attempts < 0
            || attempts > 10000)
        {
            RejectLockoutInput(config);
            return;
        }

        if (attempts != config.AuthMaxFailures)
        {
            config.AuthMaxFailures = attempts;

            _database.SaveGatewayConfig(config);
        }

        ShowLockout(config);
    }

    private void LockoutMinutesTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var config = _database.GetGatewayConfig();

        if (!int.TryParse(LockoutMinutesTextBox.Text.Trim(), out var minutes)
            || minutes < 1
            || minutes > 1440)
        {
            RejectLockoutInput(config);
            return;
        }

        if (minutes != (config.AuthBlockSeconds + 59) / 60)
        {
            config.AuthBlockSeconds = minutes * 60;

            _database.SaveGatewayConfig(config);
        }

        ShowLockout(config);
    }

    private void RejectLockoutInput(GatewayConfig config)
    {
        MessageBox.Show(
            Strings.Get("InvalidLockout"),
            Strings.Get("AppTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        ShowLockout(config);
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

        /*
         * On means the gateway is meant to listen and the service that
         * hosts it is up. A service that is down while the setting says
         * on reads as off here, because turning it on is exactly what
         * brings it back.
         */
        var on = config.AutoStart && state == ServiceState.Running;

        GatewayToggleButton.Content =
            on ? Strings.Get("TurnOff") : Strings.Get("TurnOn");

        GatewayToggleButton.IsEnabled =
            !_switching && state != ServiceState.Pending;

        GatewayPortTextBox.IsEnabled = !on;
    }

    private async void GatewayToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var config = _database.GetGatewayConfig();

        var turningOff =
            config.AutoStart && _service.State() == ServiceState.Running;

        int? port = null;

        if (turningOff)
        {
            var confirm = MessageBox.Show(
                this,
                Strings.Get("StopServiceConfirm"),
                Strings.Get("ServiceTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }
        else
        {
            var portText = GatewayPortTextBox.Text.Trim();

            if (!int.TryParse(portText, out var chosen) || chosen < 1 || chosen > 65535)
            {
                MessageBox.Show(
                    Strings.Get("InvalidPort"),
                    Strings.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            port = chosen;
        }

        _switching = true;
        GatewayToggleButton.IsEnabled = false;

        try
        {
            var problem = turningOff
                ? await _switch.TurnOffAsync()
                : await _switch.TurnOnAsync(port);

            if (problem != null)
            {
                MessageBox.Show(
                    this,
                    problem,
                    Strings.Get("ServiceTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _switching = false;
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
