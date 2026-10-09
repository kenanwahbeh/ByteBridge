using System;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Gateway;
using ByteBridge.Localization;
using ByteBridge.Updates;

namespace ByteBridge;

public partial class SettingsWindow : Window
{
    private readonly SqliteDatabase _database;
    private readonly Action _onLanguageChanged;
    private bool _loadingSettings;

    private const string AutoStartRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private const string AutoStartRegistryValueName =
        "ByteBridge";

    public SettingsWindow(
        SqliteDatabase database,
        Action onLanguageChanged)
    {
        InitializeComponent();

        FlowDirection = Strings.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _database = database;
        _onLanguageChanged = onLanguageChanged;

        LoadSettings();
        ApplyLocalization();
    }

    private void LoadSettings()
    {
        // Load current language
        var currentLang = Strings.CurrentLanguage;
        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if (item.Tag?.ToString() == currentLang)
            {
                item.IsSelected = true;
                break;
            }
        }

        // Load auto-start
        AutoStartCheckBox.IsChecked = IsAutoStartEnabled();

        // Ask before closing is on until a choice has been remembered
        _loadingSettings = true;
        AskCloseCheckBox.IsChecked =
            string.IsNullOrEmpty(_database.GetSetting("App.CloseAction"));
        _loadingSettings = false;

        /*
         * Writing is off until an administrator turns it on. The app is
         * built to run elevated, so this is normally always true; the
         * check is for the day someone launches it some other way, when
         * the box is shown as it stands but cannot be changed.
         */
        _loadingSettings = true;
        AllowWritingCheckBox.IsChecked = _database.GetAllowWrites();
        AllowWritingCheckBox.IsEnabled = IsAdministrator();
        _loadingSettings = false;

        _loadingSettings = true;
        UpdatesCheckBox.IsChecked = UpdateService.IsEnabled(_database);
        _loadingSettings = false;
    }

    private void UpdatesCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _database.SetSetting(
            "Updates.Check",
            UpdatesCheckBox.IsChecked == true ? "1" : "0");
    }

    /*
     * Asked for, so it goes whatever the box above says: someone who
     * pressed the button has said what they want. The banner on the main
     * window picks the answer up when this one closes.
     */
    private async void UpdatesCheckNowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdatesCheckNowButton.IsEnabled = false;
        UpdatesResultTextBlock.Text = Strings.Get("UpdatesChecking");

        try
        {
            var status = await new AppUpdates(_database)
                .Service
                .CheckAsync(force: true);

            UpdatesResultTextBlock.Text =
                status.Error != null
                    ? Strings.Format("UpdateCheckFailed", status.Error)
                    : status.Available
                        ? Strings.Format(
                            "UpdateAvailable",
                            status.Latest!.Value,
                            status.Current)
                        : Strings.Format("UpdateUpToDate", status.Current);
        }
        catch (Exception error)
        {
            UpdatesResultTextBlock.Text =
                Strings.Format("UpdateCheckFailed", error.Message);
        }
        finally
        {
            UpdatesCheckNowButton.IsEnabled = true;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void AllowWritingCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        var wanted = AllowWritingCheckBox.IsChecked == true;

        if (!IsAdministrator())
        {
            RestoreAllowWritingBox(!wanted);

            MessageBox.Show(
                this,
                Strings.Get("AllowWritingNeedsAdmin"),
                Strings.Get("Settings"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK,
                ReadingOptions());

            return;
        }

        // Turning writing on is the risky direction, so it asks first.
        if (wanted)
        {
            var answer = MessageBox.Show(
                this,
                Strings.Get("AllowWritingConfirm"),
                Strings.Get("AllowWriting"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                ReadingOptions());

            if (answer != MessageBoxResult.Yes)
            {
                RestoreAllowWritingBox(false);

                return;
            }
        }

        try
        {
            _database.SetAllowWrites(wanted);
        }
        catch (Exception error)
        {
            RestoreAllowWritingBox(!wanted);

            MessageBox.Show(
                this,
                Strings.Format("AllowWritingError", error.Message),
                Strings.Get("Settings"),
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                MessageBoxResult.OK,
                ReadingOptions());
        }
    }

    private static MessageBoxOptions ReadingOptions() =>
        Strings.IsRightToLeft
            ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading
            : MessageBoxOptions.None;

    private void RestoreAllowWritingBox(bool value)
    {
        _loadingSettings = true;
        AllowWritingCheckBox.IsChecked = value;
        _loadingSettings = false;
    }

    private void AskCloseCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        // Turning it back on clears the remembered choice
        if (AskCloseCheckBox.IsChecked == true)
        {
            _database.SetSetting("App.CloseAction", "");
        }
        else
        {
            /*
             * Unticking has no choice to remember yet, so it falls back
             * to the safe one -- minimize to tray -- until the dialog
             * is answered again.
             */
            _database.SetSetting("App.CloseAction", "tray");
        }
    }

    private void ApplyLocalization()
    {
        FlowDirection = Strings.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Title = Strings.Get("Settings");
        SettingsTitleTextBlock.Text = Strings.Get("Settings");
        LanguageTextBlock.Text = Strings.Get("Language");
        LanguageHintTextBlock.Text = Strings.Get("LanguageHint");
        AutoStartTextBlock.Text = Strings.Get("AutoStart");
        AutoStartHintTextBlock.Text = Strings.Get("AutoStartHint");
        AskCloseTextBlock.Text = Strings.Get("AskBeforeClosing");
        AskCloseHintTextBlock.Text = Strings.Get("AskBeforeClosingHint");
        AllowWritingTextBlock.Text = Strings.Get("AllowWriting");
        AllowWritingHintTextBlock.Text = Strings.Get("AllowWritingHint");
        UpdatesTextBlock.Text = Strings.Get("UpdatesCheck");
        UpdatesHintTextBlock.Text = Strings.Get("UpdatesCheckHint");
        UpdatesCheckNowButton.Content = Strings.Get("UpdatesCheckNow");
        DoneButton.Content = Strings.Get("Done");
    }

    private void LanguageComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            var language = item.Tag?.ToString() ?? "en";
            Strings.SetLanguage(language);

            _database.SetSetting("App.Language", language);

            ApplyLocalization();

            _onLanguageChanged?.Invoke();
        }
    }

    private void AutoStartCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (AutoStartCheckBox.IsChecked == true)
        {
            EnableAutoStart();
        }
        else
        {
            DisableAutoStart();
        }
    }

    private void DoneButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    /*
     * Auto-start management using Windows Registry.
     *
     * Adds/removes an entry in:
     * HKCU\Software\Microsoft\Windows\CurrentVersion\Run
     *
     * This makes the app start automatically when the user
     * logs in to Windows.
     */
    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                AutoStartRegistryPath,
                false);

            return key?.GetValue(AutoStartRegistryValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void EnableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                AutoStartRegistryPath,
                true);

            if (key == null)
            {
                return;
            }

            // Get the path to the current executable
            var exePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            key.SetValue(
                AutoStartRegistryValueName,
                $"\"{exePath}\"");
        }
        catch
        {
            // Silently fail if registry access is denied
        }
    }

    private static void DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                AutoStartRegistryPath,
                true);

            if (key == null)
            {
                return;
            }

            key.DeleteValue(AutoStartRegistryValueName, false);
        }
        catch
        {
            // Silently fail if registry access is denied
        }
    }
}
