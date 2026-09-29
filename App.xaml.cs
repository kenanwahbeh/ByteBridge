using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace ByteBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /*
     * One copy per Windows session, not per machine. Each signed-in user
     * has their own desktop, so a copy running in someone else's session
     * is no use to them; a machine-wide name would have left the second
     * user with no window at all. It also keeps another user's program
     * from creating the name first and stopping the app from opening.
     */
    private const string InstanceName = @"Local\ByteBridge.ControlPanel";

    private const string ShowSignalName = @"Local\ByteBridge.ControlPanel.Show";

    private Mutex? _instance;

    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, InstanceName, out var first);

        if (!first)
        {
            /*
             * Tell the copy that is already running to come forward,
             * then leave without ever creating a window. There is no
             * StartupUri to build one behind our back: WPF opens that
             * after OnStartup returns even when Shutdown has been asked
             * for, so the window is created below, by hand, and only by
             * the first copy.
             */
            if (!SignalRunningCopy())
            {
                /*
                 * Something holds the name but is not answering, so
                 * leaving without a word would look like the app refusing
                 * to open.
                 */
                MessageBox.Show(
                    "ByteBridge is already running in this Windows session, "
                    + "but its window could not be reached.\n\n"
                    + "If you cannot see it, end ByteBridge in Task Manager "
                    + "and start it again.",
                    "ByteBridge",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();

            return;
        }

        _showSignal = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowSignalName);

        var listener = new Thread(WaitForSecondCopy)
        {
            IsBackground = true
        };

        listener.Start();

        base.OnStartup(e);

        /*
         * Matches whatever Windows itself is set to (light or dark) at
         * launch. SystemThemeWatcher.Watch, called on each FluentWindow
         * as it opens, keeps it in sync if the user flips the OS theme
         * afterwards.
         */
        ApplicationThemeManager.ApplySystemTheme();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow();

        MainWindow = window;

        window.Show();
    }

    /*
     * The first copy creates its event a moment after taking the mutex,
     * so a second one started in that gap is given a little while.
     */
    private static bool SignalRunningCopy()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);

                signal.Set();

                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    private void WaitForSecondCopy()
    {
        while (_showSignal!.WaitOne())
        {
            Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.BringToFront());
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();

        try
        {
            _instance?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned: this was the second copy, which never took it.
        }

        _instance?.Dispose();

        base.OnExit(e);
    }

    /*
     * Without this, an unhandled exception on the UI thread closes the
     * process with nothing on screen -- no crash dialog, no taskbar
     * change, nothing. That reads as "the program is frozen" or
     * "nothing happened" to whoever is looking at it, which is a much
     * harder problem to report than an error message would be. This
     * still lets the app go down afterward (e.Handled stays false);
     * it only makes sure the reason is visible first.
     */
    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"ByteBridge hit an unexpected error and needs to close.\n\n{e.Exception.Message}",
            "ByteBridge",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
