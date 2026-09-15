using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ByteBridge.Configuration;
using ByteBridge.Data;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * Add and Edit share this window. One field at a time across four
 * steps reads far better than the old single long form, especially in
 * Arabic where the labels run longer -- and it gives Test a step of
 * its own instead of burying it at the bottom of a scrollable page.
 */
public partial class AddDatabaseWizardWindow : Window
{
    private const int FirstStep = 1;

    private const int LastStep = 4;

    private readonly DatabaseConfig? _existing;

    public DatabaseConfig? Result { get; private set; }

    private bool _lastTestSuccessful;

    private int _step = FirstStep;

    public AddDatabaseWizardWindow()
    {
        InitializeComponent();

        FlowDirection = Strings.CurrentLanguage == "ar"
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _lastTestSuccessful = false;

        ApplyLocalization();

        UpdateStepUi();
    }

    public AddDatabaseWizardWindow(DatabaseConfig existing)
        : this()
    {
        _existing = existing;

        Title = Strings.Get("WizardEditTitle");

        NameTextBox.Text = existing.Name;
        ServerTextBox.Text = existing.Server;
        PortTextBox.Text = existing.Port.ToString();
        UsernameTextBox.Text = existing.Username;
        PasswordBox.Password = existing.Password;
        DatabaseTextBox.Text = existing.Database;

        _lastTestSuccessful = existing.LastTestSuccessful;

        UpdateStepUi();
    }

    private void ApplyLocalization()
    {
        var isEdit = _existing != null;

        Title = Strings.Get(isEdit ? "WizardEditTitle" : "WizardAddTitle");

        Step1Title.Text = Strings.Get("WizardStep1Title");
        Step1Hint.Text = Strings.Get("WizardStep1Hint");
        Step1Label.Text = Strings.Get("WizardStep1Title");
        ConnectionNameLabel.Text = Strings.Get("WizardConnectionName");

        Step2Title.Text = Strings.Get("WizardStep2Title");
        Step2Label.Text = Strings.Get("WizardStep2Title");
        ServerLabel.Text = Strings.Get("WizardServer");
        PortLabel.Text = Strings.Get("WizardPort");
        DatabaseLabel.Text = Strings.Get("WizardDatabase");
        DatabaseHint.Text = Strings.Get("WizardDatabaseHint");

        Step3Title.Text = Strings.Get("WizardStep3Title");
        Step3Label.Text = Strings.Get("WizardStep3Title");
        UsernameLabel.Text = Strings.Get("WizardUsername");
        PasswordLabel.Text = Strings.Get("WizardPassword");
        PasswordHint.Text = Strings.Get("WizardStep3Hint");

        Step4Title.Text = Strings.Get("WizardStep4Title");
        Step4Label.Text = Strings.Get("WizardStep4Title");
        Step4Hint.Text = Strings.Get("WizardStep4Hint");
        TestButton.Content = Strings.Get("WizardTestConnection");

        CancelButton.Content = Strings.Get("Cancel");
        BackButton.Content = Strings.Get("WizardBack");
        NextButton.Content = Strings.Get("WizardNext");
        SaveButton.Content = Strings.Get(isEdit ? "WizardSave" : "WizardFinish");
    }

    private void TextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            Dispatcher.BeginInvoke(new Action(textBox.SelectAll));
        }
    }

    // ---- Step navigation ----------------------------------------------

    private void UpdateStepUi()
    {
        Step1Content.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Content.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Content.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Content.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;

        SetPill(Step1Pill, Step1Number, 1);
        SetPill(Step2Pill, Step2Number, 2);
        SetPill(Step3Pill, Step3Number, 3);
        SetPill(Step4Pill, Step4Number, 4);

        BackButton.Visibility = _step == FirstStep ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = _step == LastStep ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = _step == LastStep ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPill(Border pill, TextBlock number, int step)
    {
        if (step == _step)
        {
            pill.Background = Brushes.RoyalBlue;
            number.Foreground = Brushes.White;
        }
        else if (step < _step)
        {
            pill.Background = new SolidColorBrush(Color.FromRgb(190, 210, 245));
            number.Foreground = Brushes.RoyalBlue;
        }
        else
        {
            pill.Background = new SolidColorBrush(Color.FromRgb(230, 230, 230));
            number.Foreground = Brushes.Gray;
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep(_step))
        {
            return;
        }

        _step = Math.Min(_step + 1, LastStep);

        UpdateStepUi();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _step = Math.Max(_step - 1, FirstStep);

        UpdateStepUi();
    }

    private bool ValidateStep(int step)
    {
        switch (step)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(NameTextBox.Text))
                {
                    NameTextBox.Focus();
                    return false;
                }

                return true;

            case 2:
                if (string.IsNullOrWhiteSpace(ServerTextBox.Text))
                {
                    ServerTextBox.Focus();
                    return false;
                }

                if (!int.TryParse(PortTextBox.Text.Trim(), out var port) || port < 1 || port > 65535)
                {
                    PortTextBox.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(DatabaseTextBox.Text))
                {
                    DatabaseTextBox.Focus();
                    return false;
                }

                return true;

            case 3:
                if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    UsernameTextBox.Focus();
                    return false;
                }

                return true;

            default:
                return true;
        }
    }

    // ---- Test & save ---------------------------------------------------

    private bool TryBuildConfiguration(out DatabaseConfig config)
    {
        config = new DatabaseConfig
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString(),
            Name = NameTextBox.Text.Trim(),
            Server = ServerTextBox.Text.Trim(),
            Port = int.TryParse(PortTextBox.Text.Trim(), out var port) ? port : 3050,
            Username = UsernameTextBox.Text.Trim(),
            Password = PasswordBox.Password,
            Database = DatabaseTextBox.Text.Trim(),

            // New/Edit keeps the existing enabled state.
            Enabled = _existing?.Enabled ?? true,

            LastTestSuccessful = _lastTestSuccessful,

            LastTestedAt =
                _lastTestSuccessful
                    ? (_existing?.LastTestedAt ?? DateTime.Now)
                    : _existing?.LastTestedAt
        };

        return true;
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep(1) || !ValidateStep(2) || !ValidateStep(3))
        {
            return;
        }

        TryBuildConfiguration(out var config);

        TestButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        SaveButton.IsEnabled = false;

        TestResultBorder.Visibility = Visibility.Visible;
        TestResultTextBlock.Text = "…";
        TestResultTextBlock.Foreground = Brushes.Gray;

        var (succeeded, error) = await FirebirdConnectionTester.TestAsync(config);

        _lastTestSuccessful = succeeded;

        if (succeeded)
        {
            TestResultTextBlock.Text = "✓ " + Strings.Get("WizardTestSucceeded");
            TestResultTextBlock.Foreground = Brushes.Green;
        }
        else
        {
            TestResultTextBlock.Text = "✗ " + Strings.Format("WizardTestFailed", error ?? string.Empty);
            TestResultTextBlock.Foreground = Brushes.Red;
        }

        TestButton.IsEnabled = true;
        BackButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep(1) || !ValidateStep(2) || !ValidateStep(3))
        {
            return;
        }

        TryBuildConfiguration(out var config);

        Result = config;

        DialogResult = true;

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;

        Close();
    }
}
