using System.IO;
using System.Text;
using System.Windows;
using ByteBridge.Data;
using ByteBridge.Enrollment;
using ByteBridge.Localization;

namespace ByteBridge;

/*
 * Connects this machine to ByteBalance from the control panel: the same
 * enrolment the `enroll`, `claim`, `sync-key` and `unenroll` commands
 * run, driven by the same code, with its progress shown in the window.
 *
 * The panel already runs as an administrator, which installing the
 * connector needs. Nothing here does its own work: it builds the flow
 * and shows what the flow says.
 */
public partial class ConnectWindow : Window
{
    /*
     * How long "Check approval" waits before giving up. Approval is a
     * person clicking an email, so this is a look, not a long wait; the
     * window can be closed and reopened as often as needed.
     */
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    private readonly SqliteDatabase _database;
    private readonly CancellationTokenSource _cancellation = new();

    private bool _busy;

    public ConnectWindow(SqliteDatabase database)
    {
        InitializeComponent();

        FlowDirection = Strings.CurrentLanguage == "ar"
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _database = database;

        Title = Strings.Get("ConnectWindowTitle");
        IntroTextBlock.Text = Strings.Get("ConnectIntro");
        EmailTextBlock.Text = Strings.Get("ConnectEmail");
        CloseButton.Content = Strings.Get("Done");
        DisconnectButton.Content = Strings.Get("ConnectDisconnect");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void Window_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        _cancellation.Cancel();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /*
     * Reads the saved enrolment and sets the window to match: what it
     * says, what the email box holds and which buttons make sense.
     */
    private void Refresh()
    {
        EnrollmentState? state;

        try
        {
            state = new SettingsEnrollmentStore(_database).Load();
        }
        catch (EnrollmentException error)
        {
            StateTextBlock.Text = error.Message;
            ActionButton.IsEnabled = false;
            return;
        }

        switch (state)
        {
            case null:
                StateTextBlock.Text = Strings.Get("ConnectStateNone");
                ActionButton.Content = Strings.Get("ConnectAction");
                EmailTextBox.IsEnabled = !_busy;
                DisconnectButton.Visibility = Visibility.Collapsed;
                break;

            case { Phase: Phase.Connected }:
                StateTextBlock.Text = Strings.Format(
                    "ConnectStateConnected",
                    state.Hostname ?? string.Empty);
                ActionButton.Content = Strings.Get("ConnectSyncKey");
                EmailTextBox.Text = state.Email;
                EmailTextBox.IsEnabled = false;
                DisconnectButton.Visibility = Visibility.Visible;
                break;

            default:
                StateTextBlock.Text = Strings.Get("ConnectStateRequested");
                ActionButton.Content = Strings.Get("ConnectCheck");
                EmailTextBox.Text = state.Email;
                EmailTextBox.IsEnabled = false;
                DisconnectButton.Visibility = Visibility.Visible;
                break;
        }

        ActionButton.IsEnabled = !_busy;
        DisconnectButton.IsEnabled = !_busy;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        EnrollmentState? state;

        try
        {
            state = new SettingsEnrollmentStore(_database).Load();
        }
        catch (EnrollmentException error)
        {
            ShowError(error.Message);
            return;
        }

        if (state == null)
        {
            var email = EmailTextBox.Text.Trim();

            if (email.Length == 0 || !email.Contains('@'))
            {
                MessageBox.Show(
                    Strings.Get("ConnectEmailRequired"),
                    Strings.Get("AppTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var failure = await RunAsync(flow => flow.EnrollAsync(
                EnrollmentCommands.EnrollOptionsFor(email, CheckTimeout),
                _cancellation.Token));

            await OfferReplaceAsync(failure);

            return;
        }

        if (state.Phase == Phase.Connected)
        {
            await RunAsync(async flow => await flow.SyncKeyAsync(
                _cancellation.Token));

            return;
        }

        var claimFailure = await RunAsync(flow => flow.ClaimAsync(
            new ClaimOptions(CheckTimeout, ReplaceConnector: false),
            _cancellation.Token));

        await OfferReplaceAsync(claimFailure);
    }

    /*
     * An existing Cloudflare connector may be carrying somebody's other
     * tunnel, so it is replaced only when the person says so. By then the
     * enrolment is saved, so this continues it as `claim` does.
     */
    private async Task OfferReplaceAsync(EnrollmentException? failure)
    {
        if (failure is not { ConnectorConflict: true }
            || _cancellation.IsCancellationRequested)
        {
            return;
        }

        var answer = MessageBox.Show(
            Strings.Get("ConnectReplaceConnector"),
            Strings.Get("AppTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            await RunAsync(flow => flow.ClaimAsync(
                new ClaimOptions(CheckTimeout, ReplaceConnector: true),
                _cancellation.Token));
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            Strings.Get("ConnectDisconnectConfirm"),
            Strings.Get("AppTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAsync(flow => flow.UnenrollAsync(_cancellation.Token));

        try
        {
            if (new SettingsEnrollmentStore(_database).Load() == null)
            {
                EmailTextBox.Text = string.Empty;
            }
        }
        catch (EnrollmentException)
        {
            // Still damaged: leave the box as it is.
        }
    }

    /*
     * Runs one flow operation off the UI thread, streaming what it
     * prints into the log box, and turns its failures into a line in
     * that box rather than a crash.
     */
    private async Task<EnrollmentException?> RunAsync(
        Func<EnrollmentFlow, Task<int>> operation)
    {
        if (_busy)
        {
            return null;
        }

        EnrollmentException? failure = null;

        _busy = true;
        ActionButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        EmailTextBox.IsEnabled = false;

        LogTextBox.Text = string.Empty;
        LogTextBox.Visibility = Visibility.Visible;

        var writer = new LogWriter(LogTextBox);

        try
        {
            var services = EnrollmentServices.Create(writer, writer);
            var flow = services.CreateFlow(_database);

            await Task.Run(() => operation(flow));
        }
        catch (EnrollmentException error)
        {
            writer.WriteLine("error: " + error.Message);
            failure = error;
        }
        catch (OperationCanceledException)
        {
            // The window is closing; there is nothing left to show.
        }
        finally
        {
            _busy = false;

            if (!_cancellation.IsCancellationRequested)
            {
                Refresh();
            }
        }

        return failure;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(
            message,
            Strings.Get("AppTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /*
     * A TextWriter into a TextBox. The flow writes from a worker thread,
     * so every line is marshalled to the UI thread.
     */
    private sealed class LogWriter : TextWriter
    {
        private readonly System.Windows.Controls.TextBox _box;
        private readonly StringBuilder _line = new();

        public LogWriter(System.Windows.Controls.TextBox box)
        {
            _box = box;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                Flush();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var character in value ?? string.Empty)
            {
                Write(character);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write('\n');
        }

        public override void Flush()
        {
            var text = _line.ToString();

            _line.Clear();

            _box.Dispatcher.BeginInvoke(() =>
            {
                _box.AppendText(text + Environment.NewLine);
                _box.ScrollToEnd();
            });
        }
    }
}
