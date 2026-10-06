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

        // Attached here, after the box has its first value, so filling it
        // in is not taken for the person choosing another engine.
        TypeComboBox.SelectedIndex = 0;
        TypeComboBox.SelectionChanged += TypeComboBox_SelectionChanged;

        UpdateStepUi();
    }

    private DatabaseType _appliedType = DatabaseType.Firebird;

    /*
     * Set by TryBuildConfiguration when the row being edited names an
     * engine this build cannot serve, so the wizard can say why it will
     * not save rather than appearing to ignore the button.
     */
    private bool EngineNeedsRepair { get; set; }

    /*
     * Read by index, because the box is filled in the order below and
     * an engine with no item here falls back to Firebird rather than
     * leaving the connection without one.
     */
    /*
     * Read by index, because the box is filled in the order below and
     * an engine with no item here falls back to Firebird rather than
     * leaving the connection without one.
     *
     * Falling back is safe only because nothing saves while a row needs
     * its engine repaired: TryBuildConfiguration refuses that first, so
     * this value is never written for a row that had no engine item.
     * Changing that order would make an unrelated edit silently turn
     * such a row into a working Firebird connection.
     */
    private DatabaseType SelectedType => TypeComboBox.SelectedIndex switch
    {
        1 => DatabaseType.PostgreSql,
        _ => DatabaseType.Firebird
    };

    private static int IndexOf(DatabaseType type) => type switch
    {
        DatabaseType.PostgreSql => 1,
        _ => 0
    };

    private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyType(userChoice: true);
    }

    /*
     * Words the Database box for the engine, and moves the port and user
     * to that engine's usual ones, but only while they still hold the
     * previous engine's: a port someone typed is theirs.
     */
    private void ApplyType(bool userChoice)
    {
        var type = SelectedType;

        if (userChoice)
        {
            if (PortTextBox.Text.Trim() == _appliedType.DefaultPort().ToString()
                || string.IsNullOrWhiteSpace(PortTextBox.Text))
            {
                PortTextBox.Text = type.DefaultPort().ToString();
            }

            if (string.Equals(
                    UsernameTextBox.Text.Trim(),
                    _appliedType.DefaultUser(),
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                UsernameTextBox.Text = type.DefaultUser();
            }

            // What was tested was another engine.
            _lastTestSuccessful = false;
        }

        var file = type == DatabaseType.Firebird;

        DatabaseLabel.Text = Strings.Get(file ? "WizardDatabase" : "WizardDatabaseName");
        DatabaseHint.Text = Strings.Get(file ? "WizardDatabaseHint" : "WizardDatabaseNameHint");

        _appliedType = type;
    }

    public AddDatabaseWizardWindow(DatabaseConfig existing)
        : this()
    {
        _existing = existing;

        Title = Strings.Get("WizardEditTitle");

        NameTextBox.Text = existing.Name;

        _appliedType = existing.Type;
        TypeComboBox.SelectedIndex = IndexOf(existing.Type);

        /*
         * The combo has no item for an engine this build cannot serve,
         * so it would show Firebird -- which reads as "this is a
         * Firebird connection" and would be saved as one. The empty
         * selection is honest: nothing is chosen yet, and the save is
         * refused until something is.
         */
        if (existing.EngineIsSupported)
        {
            ApplyType(userChoice: false);
        }
        else
        {
            TypeComboBox.SelectedIndex = -1;
            TypeComboBox.IsEnabled = false;
        }

        ServerTextBox.Text = existing.Server;
        PortTextBox.Text = existing.Port.ToString();
        UsernameTextBox.Text = existing.Username;
        PasswordBox.Password = existing.Password;
        DatabaseTextBox.Text = existing.Database;

        _lastTestSuccessful = existing.LastTestSuccessful;

        UpdateStepUi();
    }

    /*
     * The engine a connection came in with is not one this build has a
     * provider for. Saving would have to pick a different one, and
     * doing that silently would turn the row into a working connection
     * to an endpoint it does not talk to -- which is worse than the
     * state it started in, because it would then look healthy.
     *
     * So the box is disabled and nothing is selected, and this is what
     * the person pressing Save gets told.
     */
    private void ReportEngineNeedsRepair()
    {
        var text = Strings.Get("WizardEngineUnsupported");

        if (!string.IsNullOrWhiteSpace(text))
        {
            text = "This connection names a database engine this version " +
                   "of ByteBridge cannot serve. Choose Firebird or " +
                   "PostgreSQL before saving.";
        }

        TestResultBorder.Visibility = Visibility.Visible;
        TestResultTextBlock.Text = text;
        TestResultTextBlock.Foreground = Brushes.IndianRed;
    }

    private void ApplyLocalization()
    {
        var isEdit = _existing != null;

        Title = Strings.Get(isEdit ? "WizardEditTitle" : "WizardAddTitle");

        Step1Title.Text = Strings.Get("WizardStep1Title");
        Step1Hint.Text = Strings.Get("WizardStep1Hint");
        Step1Label.Text = Strings.Get("WizardStep1Title");
        ConnectionNameLabel.Text = Strings.Get("WizardConnectionName");
        EngineLabel.Text = Strings.Get("WizardEngine");

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
        /*
         * A row whose stored engine this build cannot serve cannot be
         * saved by editing an unrelated field. The combo holds no item
         * for it, so reading the box would hand back Firebird and the
         * save would quietly turn the row into a working Firebird
         * connection against an endpoint that is not one -- which is
         * worse than the state it started in, because it now looks
         * healthy.
         *
         * So the administrator has to choose. Refused here, and the
         * engine box says why.
         */
        if (_existing != null && !_existing.EngineIsSupported)
        {
            config = new DatabaseConfig();

            EngineNeedsRepair = true;

            return false;
        }

        EngineNeedsRepair = false;

        config = new DatabaseConfig
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString(),
            Name = NameTextBox.Text.Trim(),
            Type = SelectedType,
            EngineIsSupported = true,
            Server = ServerTextBox.Text.Trim(),
            Port = int.TryParse(PortTextBox.Text.Trim(), out var port) ? port : SelectedType.DefaultPort(),
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

        if (!TryBuildConfiguration(out var config))
        {
            return;
        }

        TestButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        SaveButton.IsEnabled = false;

        TestResultBorder.Visibility = Visibility.Visible;
        TestResultTextBlock.Text = "…";
        TestResultTextBlock.Foreground = Brushes.Gray;

        var (succeeded, error) = await DatabaseConnectionTester.TestAsync(config);

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

        if (!TryBuildConfiguration(out var config))
        {
            ReportEngineNeedsRepair();

            return;
        }

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
