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

namespace ByteBridge;

public partial class MainWindow : Window
{
    private readonly SqliteDatabase _database;

    private readonly GatewayServiceControl _service = new();

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
     * keyed by connection name -- the same identifier a client sends
     * in "database". Refreshed on the same tick as the status line;
     * empty whenever the gateway is not answering.
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

    /*
     * Created lazily on the first minimize-to-tray, then just shown
     * and hidden from there on rather than recreated each time --
     * NotifyIcon holds a live shell notification-area slot, and
     * disposing and recreating it on every toggle is what makes tray
     * icons flicker or land in the wrong spot.
     */
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        _database = new SqliteDatabase();

        // Load saved language
        var savedLanguage = _database.GetSetting("App.Language");
        if (!string.IsNullOrEmpty(savedLanguage))
        {
            Strings.SetLanguage(savedLanguage);
        }

        ApplyLocalization();

        _refresh.Tick += async (_, _) => await UpdateStatusAsync();

        LoadConnections();

        _ = UpdateStatusAsync();

        _refresh.Start();
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
    }

    private void Window_Closing(
        object sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        // Show the close dialog
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

        switch (dialog.Result)
        {
            case CloseDialogResult.MinimizeToTray:
                // Minimize to tray instead of closing
                e.Cancel = true;
                MinimizeToTray();
                break;

            case CloseDialogResult.Settings:
                // Open settings window
                e.Cancel = true;
                OpenSettings();
                break;

            case CloseDialogResult.Exit:
                // Actually close
                _isClosing = true;
                _refresh.Stop();
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

        _service.Dispose();

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    /*
     * Hides the window entirely -- not just off the taskbar -- and
     * shows a notification-area icon in its place. The previous
     * version set WindowState.Minimized with ShowInTaskbar false and
     * nothing else: no taskbar entry and no tray icon either, so the
     * window was simply gone until relaunched from the Start menu.
     */
    private void MinimizeToTray()
    {
        EnsureTrayIcon();

        Hide();

        _trayIcon!.Visible = true;
    }

    private void RestoreFromTray()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }

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

        var icon =
            System.Drawing.Icon.ExtractAssociatedIcon(exePath);

        var menu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = menu.Items.Add(Strings.Get("TrayOpen"));
        openItem.Click += (_, _) => RestoreFromTray();

        var exitItem = menu.Items.Add(Strings.Get("ExitApp"));
        exitItem.Click += (_, _) => ExitFromTray();

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

        string gatewayText;
        Brush color;

        if (answering)
        {
            gatewayText = Strings.Format("Answering", config.BaseUrl);
            color = Brushes.Green;
        }
        else if (!config.AutoStart)
        {
            gatewayText = Strings.Get("TurnedOff");
            color = Brushes.Gray;
        }
        else if (state == ServiceState.Running)
        {
            gatewayText = Strings.Get("Starting");
            color = Brushes.DarkOrange;
        }
        else
        {
            gatewayText = Strings.Get("NotRunning");
            color = Brushes.Red;
        }

        var serviceKey = state switch
        {
            ServiceState.Running => "ServiceRunning",
            ServiceState.Stopped => "ServiceStopped",
            ServiceState.Pending => "ServicePending",
            _ => "ServiceNotInstalled"
        };

        StatusTextBlock.Text = $"{gatewayText}    ·    {Strings.Get(serviceKey)}";
        StatusTextBlock.Foreground = color;

        _requestCounts =
            answering
                ? await _service.GetStatsAsync(config.BaseUrl)
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
                _requestCounts.TryGetValue(connection.Name, out var count)
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
         * Status logic:
         *
         * Enabled + successful test = Online / green
         * Disabled = Offline / gray
         * Enabled + failed test = Offline / red
         */

        if (!connection.Enabled)
        {
            status.Text = Strings.Get("Offline");
            status.Foreground = Brushes.Gray;
        }
        else if (connection.LastTestSuccessful)
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
                _requestCounts.TryGetValue(connection.Name, out var count)
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
                await FirebirdConnectionTester.TestAsync(connection);

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
        OptionsMenuItem.Header = Strings.Get("MenuOptions");

        HelpMenuItem.Header = Strings.Get("MenuHelp");
        AboutMenuItem.Header = Strings.Get("MenuHelpAbout");

        FlowDirection =
            Strings.CurrentLanguage == "ar"
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

        // Refresh dynamic text
        _ = UpdateStatusAsync();
    }
}
