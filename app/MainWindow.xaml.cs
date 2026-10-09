using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Gateway;
using ByteBridge.Localization;
using ByteBridge.Updates;

namespace ByteBridge;

public partial class MainWindow : Window
{
    private readonly SqliteDatabase _database;

    private readonly GatewayServiceControl _service = new();

    private readonly GatewaySwitch _switch;

    // True while a start or stop is waiting on Windows, so the refresh
    // tick does not hand the button back before it is finished.
    private bool _switching;

    /*
     * The window no longer holds the gateway; the service does. This
     * refreshes what the window shows about it, because the service
     * reacts to a settings change on its own schedule rather than when
     * a button is clicked.
     */
    private readonly DispatcherTimer _refresh = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private List<DatabaseConfig> _connections = new();

    private DatabaseConfig? _selectedConnection;

    /*
     * How many requests the gateway has answered for each database,
     * keyed by connection id, which is what /stats reports and, unlike
     * a name, belongs to one connection. Refreshed on the same tick as
     * the status line; empty whenever the gateway is not answering.
     */
    private Dictionary<string, long> _requestCounts = new();

    /*
     * The request-count label for each connection card, kept around so
     * the 2-second refresh can update just that text run instead of
     * tearing down and rebuilding the whole list -- which would lose
     * the selection highlight and flicker for no reason.
     */
    private readonly Dictionary<string, TextBlock> _requestCountLabels = new();

    private bool _isClosing = false;

    private readonly AppUpdates _updates;

    /*
     * Asks again every few hours, because this window can stay open (in
     * the tray) for weeks. UpdateService decides whether GitHub is really
     * asked: once a day, and not at all if checks are turned off.
     */
    private readonly DispatcherTimer _updateTimer = new()
    {
        Interval = TimeSpan.FromHours(6)
    };

    // The release "Later" was pressed for; quiet until a newer one.
    private ReleaseVersion? _dismissedUpdate;

    // True while the installer is downloading, so nothing starts twice.
    private bool _updating;

    /*
     * Created once, at launch, and left in the notification area for as
     * long as the app runs, so the app can be found there whether its
     * window is open or hidden. It is not recreated on each toggle:
     * NotifyIcon holds a live shell notification-area slot, and
     * disposing and recreating it is what makes tray icons flicker or
     * land in the wrong spot.
     */
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private System.Windows.Forms.ToolStripItem? _trayOpenItem;

    private System.Windows.Forms.ToolStripItem? _trayExitItem;

    public MainWindow()
    {
        InitializeComponent();

        _database = new SqliteDatabase();

        _switch = new GatewaySwitch(_database, _service);

        _updates = new AppUpdates(_database);

        // Load saved language
        var savedLanguage = _database.GetSetting("App.Language");
        if (!string.IsNullOrEmpty(savedLanguage))
        {
            Strings.SetLanguage(savedLanguage);
        }

        ApplyLocalization();

        // In the notification area from launch, not only after a minimize.
        ShowTrayIcon();

        _refresh.Tick += async (_, _) => await UpdateStatusAsync();

        LoadConnections();

        _ = UpdateStatusAsync();

        _refresh.Start();

        _updateTimer.Tick += async (_, _) => await RefreshUpdatesAsync(false);
        _updateTimer.Start();
    }

    /*
     * The gateway starts with the machine now, so there is nothing to
     * start here. What is worth doing is telling someone when the
     * service that should be hosting it is not installed or not
     * running, since the window otherwise looks the same either way.
     */
    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await UpdateStatusAsync();

        await CheckServiceAtLaunchAsync();

        // What an earlier check (the service's, this window's) already
        // found shows at once; the network comes after.
        ShowUpdateBanner(_updates.Service.Cached());

        await RefreshUpdatesAsync(false);
    }

    /*
     * The window hosts nothing itself, so all that opening it can do is
     * notice when the service that does is not there, and say so. Only
     * when the gateway is meant to be on: someone who turned it off on
     * purpose is not asked about it again on every launch, the status
     * line already says so and the button is one click.
     */
    private async Task CheckServiceAtLaunchAsync()
    {
        var state = _service.State();

        switch (ServiceLaunch.For(
            state,
            _database.GetGatewayConfig().AutoStart))
        {
            case LaunchAdvice.ServiceMissing:
                MessageBox.Show(
                    this,
                    Strings.Get("ServiceNotInstalledMessage"),
                    Strings.Get("ServiceTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;

            /*
             * Asked of the rule rather than of two negations here: a
             * gateway somebody deliberately turned off is not something
             * to ask about on every single launch.
             */
            case LaunchAdvice.OfferToStart:
                var answer = MessageBox.Show(
                    this,
                    Strings.Get("ServiceStoppedPrompt"),
                    Strings.Get("ServiceTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes)
                {
                    await SwitchGatewayAsync(on: true);
                }

                return;

            default:
                return;
        }
    }

    private async void ServiceToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_service.State() == ServiceState.Running)
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

            await SwitchGatewayAsync(on: false);

            return;
        }

        await SwitchGatewayAsync(on: true);
    }

    private async Task SwitchGatewayAsync(bool on)
    {
        _switching = true;
        ServiceToggleButton.IsEnabled = false;

        try
        {
            var problem = on
                ? await _switch.TurnOnAsync()
                : await _switch.TurnOffAsync();

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

        await UpdateStatusAsync();
    }

    /*
     * Brought forward by a second copy of the app that was started and
     * then closed itself, so there is only ever one.
     */
    public void BringToFront()
    {
        RestoreFromTray();

        // Topmost flips once so Windows lets it past other windows.
        Topmost = true;
        Topmost = false;

        Focus();
    }

    private void Window_Closing(
        object sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        // A remembered choice skips the dialog; Settings can undo it.
        var remembered = _database.GetSetting("App.CloseAction");
        var result = remembered switch
        {
            "tray" => CloseDialogResult.MinimizeToTray,
            "exit" => CloseDialogResult.Exit,
            _ => (CloseDialogResult?)null
        };

        if (result is null)
        {
            var dialog = new CloseDialogWindow
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                // User cancelled
                e.Cancel = true;
                return;
            }

            result = dialog.Result;

            if (dialog.DontAskAgain)
            {
                _database.SetSetting(
                    "App.CloseAction",
                    result == CloseDialogResult.Exit ? "exit" : "tray");
            }
        }

        switch (result)
        {
            case CloseDialogResult.MinimizeToTray:
                // Minimize to tray instead of closing
                e.Cancel = true;
                MinimizeToTray();
                break;

            case CloseDialogResult.Exit:
                // Actually close
                _isClosing = true;
                _refresh.Stop();
        _updateTimer.Stop();
                _service.Dispose();
                break;

            case CloseDialogResult.Cancel:
                e.Cancel = true;
                break;
        }
    }

    private void Window_Closed(
        object sender,
        EventArgs e)
    {
        /*
         * Deliberately does not stop the gateway. Closing this window
         * used to kill it, which is the whole reason a tunnel went dead
         * when someone tidied up their desktop.
         */
        _refresh.Stop();
        _updateTimer.Stop();

        _service.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    /*
     * Hides the window entirely -- not just off the taskbar. The
     * notification-area icon is already there; it is what brings the
     * window back. The previous version set WindowState.Minimized with
     * ShowInTaskbar false and nothing else: no taskbar entry and no
     * tray icon either, so the window was simply gone until relaunched
     * from the Start menu.
     */
    private void MinimizeToTray()
    {
        ShowTrayIcon();

        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null)
        {
            return;
        }

        var exePath =
            Environment.ProcessPath
            ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

        /*
         * Asked for at the size the notification area really draws, so
         * the picture made for that size is used rather than a larger
         * one squeezed down. Falls back to the ordinary lookup.
         */
        System.Drawing.Icon? icon = null;

        try
        {
            icon = System.Drawing.Icon.ExtractIcon(
                exePath,
                0,
                System.Windows.Forms.SystemInformation.SmallIconSize.Width);
        }
        catch (Exception)
        {
            // Falls through to the plain lookup below.
        }

        icon ??= System.Drawing.Icon.ExtractAssociatedIcon(exePath);

        var menu = new System.Windows.Forms.ContextMenuStrip();

        _trayOpenItem = menu.Items.Add(Strings.Get("TrayOpen"));
        _trayOpenItem.Click += (_, _) => RestoreFromTray();

        _trayExitItem = menu.Items.Add(Strings.Get("ExitApp"));
        _trayExitItem.Click += (_, _) => ExitFromTray();

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = Strings.Get("AppTitle"),
            ContextMenuStrip = menu
        };

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void ShowTrayIcon()
    {
        EnsureTrayIcon();

        _trayIcon!.Visible = true;
    }

    // The language can change while the app is running.
    private void UpdateTrayText()
    {
        if (_trayIcon == null)
        {
            return;
        }

        _trayIcon.Text = Strings.Get("AppTitle");

        if (_trayOpenItem != null)
        {
            _trayOpenItem.Text = Strings.Get("TrayOpen");
        }

        if (_trayExitItem != null)
        {
            _trayExitItem.Text = Strings.Get("ExitApp");
        }
    }

    private void ExitFromTray()
    {
        _isClosing = true;

        Close();
    }

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(
            _database,
            () =>
            {
                ApplyLocalization();
                LoadConnections();
            })
        {
            Owner = this
        };

        settingsWindow.ShowDialog();

        // Refresh UI after settings change
        _ = UpdateStatusAsync();

        // "Check now" there may have found something.
        ShowUpdateBanner(_updates.Service.Cached());
    }

    // ---- Menu bar --------------------------------------------------

    private void NewDatabaseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddDatabaseWizardWindow
        {
            Owner = this
        };

        if (window.ShowDialog() != true || window.Result == null)
        {
            return;
        }

        try
        {
            _database.AddConnection(window.Result);

            LoadConnections();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "ByteBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Trigger the closing event which shows the close dialog
        Close();
    }

    private void EditSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConnection != null)
        {
            EditConnection(_selectedConnection);
        }
    }

    private void DeleteSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConnection != null)
        {
            DeleteConnection(_selectedConnection);
        }
    }

    private void WebServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new WebServerWindow(_database, _service)
        {
            Owner = this
        };

        window.ShowDialog();

        _ = UpdateStatusAsync();
    }

    private void CloudflareTunnelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new CloudflareTunnelWindow(_database)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void ConnectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new ConnectWindow(_database)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    private void OptionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow(_database)
        {
            Owner = this
        };

        window.ShowDialog();
    }

    /*
     * The plain-language guide, in the language the window is showing.
     *
     * These point at the docs folder on GitHub, which renders the same
     * Markdown the GitBook site is built from. Once that site is
     * published, swap these two for its pages; nothing else changes.
     */
    private const string UserGuideUrlEnglish =
        "https://github.com/kenanwahbeh/ByteBridge/blob/main/docs/guide/README.md";

    private const string UserGuideUrlArabic =
        "https://github.com/kenanwahbeh/ByteBridge/blob/main/docs/ar/README.md";

    // The maker's site, the same one About links to.
    private const string WebsiteUrl = "https://bytebalancetech.com";

    private void UserGuideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenUserGuide();
    }

    private void WebsiteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(WebsiteUrl);
    }

    // ---- Updates ---------------------------------------------------

    /*
     * Checks (or, unless forced, does what is due) and shows the result.
     * Never throws: an update check is a convenience, and a window that
     * failed to open over one would be a worse trade than a missing
     * banner.
     */
    private async Task<UpdateStatus?> RefreshUpdatesAsync(bool force)
    {
        try
        {
            var status = await _updates.Service.CheckAsync(force);

            ShowUpdateBanner(status);

            return status;
        }
        catch (Exception error)
        {
            // Nothing is shown for a background check; a forced one is a
            // person waiting for an answer, and gets the reason.
            return _updates.Service.Cached(error.Message);
        }
    }

    private void ShowUpdateBanner(UpdateStatus status)
    {
        if (_updating)
        {
            return;
        }

        if (!status.Available || status.Latest == _dismissedUpdate)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateBannerTextBlock.Text = Strings.Format(
            "UpdateAvailable",
            status.Latest!.Value,
            status.Current);

        UpdateNowButton.IsEnabled = true;
        UpdateLaterButton.IsEnabled = true;
        UpdateNotesButton.IsEnabled = true;

        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void UpdatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _dismissedUpdate = null;

        var status = await RefreshUpdatesAsync(true);

        if (status == null)
        {
            return;
        }

        if (status.Error != null)
        {
            MessageBox.Show(
                this,
                Strings.Format("UpdateCheckFailed", status.Error),
                Strings.Get("UpdatesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else if (status.Available)
        {
            await StartUpdateAsync();
        }
        else
        {
            MessageBox.Show(
                this,
                Strings.Format("UpdateUpToDate", status.Current),
                Strings.Get("UpdatesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        await StartUpdateAsync();
    }

    private void UpdateNotesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenInBrowser(ReleasePage());
    }

    private void UpdateLaterButton_Click(object sender, RoutedEventArgs e)
    {
        _dismissedUpdate = _updates.Service.Cached().Latest;

        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private string ReleasePage() =>
        _updates.Service.Cached().Url
        ?? UpdateChecker.RepositoryUrl + "releases/latest";

    /*
     * Asks first, because it ends with this window closing and an
     * installer asking for administrator rights, and neither should be a
     * surprise. Nothing is run unless the download matched its published
     * checksum; AppUpdates.DownloadAsync does not return one that did
     * not.
     */
    private async Task StartUpdateAsync()
    {
        var latest = _updates.Service.Cached().Latest;

        if (_updating || latest is not { } version)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            Strings.Format("UpdateConfirm", version),
            Strings.Get("UpdatesTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _updating = true;

        UpdateBanner.Visibility = Visibility.Visible;
        UpdateNowButton.IsEnabled = false;
        UpdateLaterButton.IsEnabled = false;
        UpdateNotesButton.IsEnabled = false;

        UpdateBannerTextBlock.Text =
            Strings.Format("UpdateDownloading", version, 0);

        var progress = new Progress<double>(fraction =>
            UpdateBannerTextBlock.Text = Strings.Format(
                "UpdateDownloading",
                version,
                (int)(fraction * 100)));

        string? installer;

        try
        {
            installer = await _updates.DownloadAsync(progress);
        }
        catch (Exception error)
        {
            _updating = false;
            ShowUpdateBanner(_updates.Service.Cached());

            MessageBox.Show(
                this,
                Strings.Format("UpdateDownloadFailed", error.Message),
                Strings.Get("UpdatesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _updating = false;
        ShowUpdateBanner(_updates.Service.Cached());

        if (installer == null)
        {
            MessageBox.Show(
                this,
                Strings.Get("UpdateNoInstaller"),
                Strings.Get("UpdatesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            OpenInBrowser(ReleasePage());

            return;
        }

        try
        {
            AppUpdates.Start(installer);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                Strings.Format("UpdateStartFailed", error.Message),
                Strings.Get("UpdatesTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        // Out of the installer's way; it replaces this program's files.
        ExitFromTray();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            OpenUserGuide();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OpenUserGuide()
    {
        OpenInBrowser(
            Strings.IsRightToLeft
                ? UserGuideUrlArabic
                : UserGuideUrlEnglish);
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ---- Gateway status ----------------------------------------------

    /*
     * One line: whether the gateway is answering and whether the
     * service is running. The controls that used to sit beside this
     * (port, keys, start/stop) now live in the Web Server dialog; this
     * is only what belongs on the page someone glances at every time.
     */
    private async Task UpdateStatusAsync()
    {
        var config = _database.GetGatewayConfig();
        var state = _service.State();

        var answering =
            state == ServiceState.Running
            && await _service.IsAnsweringAsync(config.BaseUrl);

        var status = GatewayStatus.For(config, state, answering);

        StatusTextBlock.Text =
            $"{Strings.Format(status.TextKey, config.BaseUrl)}    ·    "
            + $"{Strings.Get(ServiceStatus.KeyFor(state))}";

        /*
         * Keyed on the four outcome names the shared rule returns, which
         * is what the tests pin: a key renamed here falls through to red
         * rather than failing the build, and the tests are what say so.
         */
        StatusTextBlock.Foreground = status.TextKey switch
        {
            "Answering" => Brushes.Green,
            "TurnedOff" => Brushes.Gray,
            "Starting" => Brushes.DarkOrange,
            _ => Brushes.Red
        };

        ServiceToggleButton.Visibility =
            state == ServiceState.NotInstalled
                ? Visibility.Collapsed
                : Visibility.Visible;

        /*
         * The service, because this is the button that stops and starts
         * it -- ServiceToggleButton_Click branches on the service state
         * and nothing else, so a label derived from anything else claims
         * one thing and does another. With AutoStart off and the service
         * up, this reads Stop, and pressing it stops the service, which
         * is what a service that is running should be able to answer to.
         *
         * GatewayIsOn is the Web Server dialog's question, not this one:
         * that button calls TurnOnAsync, which writes AutoStart, so it
         * has to mean the setting as well as the state. Two buttons, two
         * questions -- the earlier attempt to make them agree made this
         * one lie.
         */
        ServiceToggleButton.Content =
            state == ServiceState.Running
                ? Strings.Get("StopService")
                : Strings.Get("StartService");

        ServiceToggleButton.IsEnabled =
            !_switching && state != ServiceState.Pending;

        _requestCounts =
            answering
                ? await _service.GetStatsAsync(config.BaseUrl, config.ApiKey)
                : new Dictionary<string, long>();

        RefreshRequestCountLabels();
    }

    private void RefreshRequestCountLabels()
    {
        foreach (var connection in _connections)
        {
            if (!_requestCountLabels.TryGetValue(connection.Id, out var label))
            {
                continue;
            }

            label.Text =
                _requestCounts.TryGetValue(connection.Id, out var count)
                    ? Strings.Format("RequestCount", count)
                    : Strings.Get("RequestCountUnknown");
        }
    }

    // ---- Connections ---------------------------------------------------

    private void LoadConnections()
    {
        _connections =
            _database.GetConnections();

        if (_selectedConnection != null &&
            !_connections.Exists(c => c.Id == _selectedConnection.Id))
        {
            _selectedConnection = null;
        }

        RenderConnections();

        UpdateEditMenuState();

        RefreshRequestCountLabels();
    }

    private void UpdateEditMenuState()
    {
        var hasSelection = _selectedConnection != null;

        EditSelectedMenuItem.IsEnabled = hasSelection;
        DeleteSelectedMenuItem.IsEnabled = hasSelection;
    }

    private void SelectConnection(DatabaseConfig connection)
    {
        _selectedConnection =
            _selectedConnection?.Id == connection.Id
                ? null
                : connection;

        RenderConnections();

        UpdateEditMenuState();
    }

    private void RenderConnections()
    {
        ConnectionsPanel.Children.Clear();

        _requestCountLabels.Clear();

        if (_connections.Count == 0)
        {
            ConnectionsPanel.Children.Add(
                new TextBlock
                {
                    Text = Strings.Get("NoDatabases"),

                    FontSize = 16,

                    Foreground = Brushes.Gray,

                    TextAlignment =
                        TextAlignment.Center,

                    Margin =
                        new Thickness(20)
                });

            return;
        }

        foreach (var connection in _connections)
        {
            ConnectionsPanel.Children.Add(
                CreateConnectionCard(connection));
        }
    }

    private Border CreateConnectionCard(
        DatabaseConfig connection)
    {
        var isSelected = _selectedConnection?.Id == connection.Id;

        var border = new Border
        {
            BorderBrush =
                isSelected
                    ? Brushes.RoyalBlue
                    : new SolidColorBrush(
                        Color.FromRgb(220, 220, 220)),

            BorderThickness =
                new Thickness(isSelected ? 2 : 1),

            CornerRadius =
                new CornerRadius(8),

            Padding =
                new Thickness(16),

            Margin =
                new Thickness(0, 0, 0, 12)
        };

        var grid = new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var left = new StackPanel
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        left.MouseLeftButtonUp +=
            (_, _) => SelectConnection(connection);

        left.Children.Add(
            new TextBlock
            {
                Text = connection.Name,

                FontSize = 18,

                FontWeight =
                    FontWeights.SemiBold
            });

        var status = new TextBlock
        {
            Margin =
                new Thickness(0, 6, 0, 0),

            FontSize = 14
        };

        /*
         * Status logic. Which branch runs is a presentation choice, but
         * the online answer itself comes from IsOnline and nowhere else:
         * it is the same property /health and /databases use, and
         * deriving it here as well is how the two drifted apart before.
         *
         * An engine this build cannot serve gets its own line rather
         * than Offline. It is not offline: the connection is enabled and
         * the server may well be fine. It is not online either --
         * requests for it are refused. "Offline" would send someone to
         * the server instead of to the engine.
         */

        if (!connection.EngineIsSupported)
        {
            status.Text = Strings.Get("EngineUnsupported");
            status.Foreground = Brushes.DarkOrange;
        }
        else if (!connection.Enabled)
        {
            status.Text = Strings.Get("Offline");
            status.Foreground = Brushes.Gray;
        }
        else if (connection.IsOnline)
        {
            status.Text = Strings.Get("Online");
            status.Foreground = Brushes.Green;
        }
        else
        {
            status.Text = Strings.Get("Offline");
            status.Foreground = Brushes.Red;
        }

        left.Children.Add(status);

        var requestCountLabel = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12,
            Foreground = Brushes.Gray,
            Text =
                _requestCounts.TryGetValue(connection.Id, out var count)
                    ? Strings.Format("RequestCount", count)
                    : Strings.Get("RequestCountUnknown")
        };

        left.Children.Add(requestCountLabel);

        _requestCountLabels[connection.Id] = requestCountLabel;

        Grid.SetColumn(left, 0);

        grid.Children.Add(left);

        var buttons = new StackPanel
        {
            Orientation =
                Orientation.Horizontal,

            VerticalAlignment =
                VerticalAlignment.Center
        };

        var toggleButton = new Button
        {
            Content =
                connection.Enabled
                    ? Strings.Get("OfflineButton")
                    : Strings.Get("OnlineButton"),

            Padding =
                new Thickness(12, 7, 12, 7),

            Margin =
                new Thickness(5, 0, 0, 0)
        };

        toggleButton.Click +=
            async (_, _) =>
                await ToggleConnectionAsync(connection);

        var editButton = new Button
        {
            Content = Strings.Get("Edit"),

            Padding =
                new Thickness(12, 7, 12, 7),

            Margin =
                new Thickness(5, 0, 0, 0)
        };

        editButton.Click +=
            (_, _) =>
                EditConnection(connection);

        var deleteButton = new Button
        {
            Content = Strings.Get("Delete"),

            Padding =
                new Thickness(12, 7, 12, 7),

            Margin =
                new Thickness(5, 0, 0, 0)
        };

        deleteButton.Click +=
            (_, _) =>
                DeleteConnection(connection);

        buttons.Children.Add(toggleButton);
        buttons.Children.Add(editButton);
        buttons.Children.Add(deleteButton);

        Grid.SetColumn(buttons, 1);

        grid.Children.Add(buttons);

        border.Child = grid;

        return border;
    }

    private void EditConnection(
        DatabaseConfig connection)
    {
        var window =
            new AddDatabaseWizardWindow(connection)
            {
                Owner = this
            };

        if (window.ShowDialog() != true ||
            window.Result == null)
        {
            return;
        }

        try
        {
            _database.UpdateConnection(
                window.Result);

            LoadConnections();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "ByteBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeleteConnection(
        DatabaseConfig connection)
    {
        var result =
            MessageBox.Show(
                Strings.Format("DeleteConfirm", connection.Name),

                Strings.Get("DeleteTitle"),

                MessageBoxButton.YesNo,

                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _database.DeleteConnection(
            connection.Id);

        if (_selectedConnection?.Id == connection.Id)
        {
            _selectedConnection = null;
        }

        LoadConnections();
    }

    private async Task ToggleConnectionAsync(
        DatabaseConfig connection)
    {
        /*
         * Going Online
         */
        if (!connection.Enabled)
        {
            var confirm =
                MessageBox.Show(
                    Strings.Format("TurnOnlineConfirm", connection.Name),

                    Strings.Get("TurnOnlineTitle"),

                    MessageBoxButton.YesNo,

                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var (succeeded, error) =
                await DatabaseConnectionTester.TestAsync(connection);

            if (!succeeded)
            {
                MessageBox.Show(
                    error == null
                        ? Strings.Get("ConnectionFailedMessage")
                        : Strings.Format("ConnectionFailedError", error),

                    Strings.Get("ConnectionFailed"),

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning);

                _database.SetTestResult(
                    connection.Id,
                    false);

                LoadConnections();

                return;
            }

            _database.SetTestResult(
                connection.Id,
                true);

            _database.SetEnabled(
                connection.Id,
                true);

            LoadConnections();

            return;
        }

        /*
         * Going Offline
         */
        var offlineConfirm =
            MessageBox.Show(
                Strings.Format("TurnOfflineConfirm", connection.Name),

                Strings.Get("TurnOfflineTitle"),

                MessageBoxButton.YesNo,

                MessageBoxImage.Question);

        if (offlineConfirm != MessageBoxResult.Yes)
        {
            return;
        }

        _database.SetEnabled(
            connection.Id,
            false);

        LoadConnections();
    }

    /*
     * Applies localized strings to all UI elements.
     */
    private void ApplyLocalization()
    {
        Title = Strings.Get("AppTitle");

        FileMenuItem.Header = Strings.Get("MenuFile");
        NewDatabaseMenuItem.Header = Strings.Get("MenuFileNew");
        ExitMenuItem.Header = Strings.Get("MenuFileExit");

        EditMenuItem.Header = Strings.Get("MenuEdit");
        EditSelectedMenuItem.Header = Strings.Get("MenuEditEdit");
        DeleteSelectedMenuItem.Header = Strings.Get("MenuEditDelete");

        WebServerMenuItem.Header = Strings.Get("MenuWebServer");
        CloudflareTunnelMenuItem.Header = Strings.Get("MenuCloudflareTunnel");
        ConnectMenuItem.Header = Strings.Get("MenuConnect");
        OptionsMenuItem.Header = Strings.Get("MenuOptions");

        HelpMenuItem.Header = Strings.Get("MenuHelp");
        UserGuideMenuItem.Header = Strings.Get("MenuHelpGuide");
        WebsiteMenuItem.Header = Strings.Get("MenuHelpWebsite");
        UpdatesMenuItem.Header = Strings.Get("MenuHelpUpdates");
        AboutMenuItem.Header = Strings.Get("MenuHelpAbout");

        FlowDirection =
            Strings.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

        UpdateNowButton.Content = Strings.Get("UpdateNow");
        UpdateNotesButton.Content = Strings.Get("UpdateNotes");
        UpdateLaterButton.Content = Strings.Get("UpdateLater");

        ShowUpdateBanner(_updates.Service.Cached());

        UpdateTrayText();

        // Refresh dynamic text
        _ = UpdateStatusAsync();
    }
}
