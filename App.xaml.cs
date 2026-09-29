using System.ComponentModel;
using System.Diagnostics;
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

    private const string ShownAckName = @"Local\ByteBridge.ControlPanel.Shown";

    private Mutex? _instance;

    private EventWaitHandle? _showSignal;

    private EventWaitHandle? _shownAck;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool first;

        try
        {
            _instance = new Mutex(true, InstanceName, out first);
        }
        catch (UnauthorizedAccessException)
        {
            // Held by something that will not share it, so not a ByteBridge.
            _instance = null;
            first = false;
        }

        /*
         * Holding the names proves nothing about who holds them: any
         * program in this session can create a mutex and an event under
         * these names before ByteBridge starts, and would otherwise be
         * able to stop the app from ever opening. So a copy only gives
         * way to a ByteBridge that is really running here; a name held
         * by anything else is ignored and this copy opens as usual.
         */
        if (!first && AnotherCopyIsRunning())
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
                 * A ByteBridge is running but is not answering, so
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

        try
        {
            _showSignal = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ShowSignalName);

            _shownAck = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ShownAckName);

            var listener = new Thread(WaitForSecondCopy)
            {
                IsBackground = true
            };

            listener.Start();
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException
            or WaitHandleCannotBeOpenedException)
        {
            /*
             * Another program holds one of the names and will not share
             * it. This copy still opens; it just cannot be brought
             * forward by a later launch, which then says so.
             */
            _showSignal?.Dispose();
            _showSignal = null;
            _shownAck = null;
        }

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
     * True when another copy of this same program is running in this
     * Windows session: the same executable, not just the same name.
     */
    private static bool AnotherCopyIsRunning()
    {
        var path = Environment.ProcessPath;

        if (path == null)
        {
            return false;
        }

        try
        {
            using var me = Process.GetCurrentProcess();

            foreach (var other in Process.GetProcessesByName(me.ProcessName))
            {
                using (other)
                {
                    if (other.Id == me.Id || other.SessionId != me.SessionId)
                    {
                        continue;
                    }

                    try
                    {
                        if (string.Equals(
                                other.MainModule?.FileName,
                                path,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (Win32Exception)
                    {
                        // Its path cannot be read, so it cannot be vouched for.
                    }
                    catch (InvalidOperationException)
                    {
                        // It exited while it was being looked at.
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Processes could not be listed; carry on as the only copy.
        }

        return false;
    }

    /*
     * Knocks, and waits to be answered. Setting an event proves nothing
     * on its own: it may be one another program created, or the window it
     * belongs to may be stuck. The running copy sets the second event
     * only after it has asked its window to come forward, so an answer
     * means it was reached, and no answer is reported instead of being
     * mistaken for success.
     *
     * The first copy creates its events a moment after taking the mutex,
     * so a second one started in that gap is given a little while.
     */
    private static bool SignalRunningCopy()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                using var answer = EventWaitHandle.OpenExisting(ShownAckName);

                // Whatever answered an earlier knock is not an answer to this one.
                answer.Reset();

                signal.Set();

                return answer.WaitOne(TimeSpan.FromSeconds(5));
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
        var signal = _showSignal!;
        var answer = _shownAck!;

        try
        {
            while (signal.WaitOne())
            {
                // Synchronous on purpose: the answer goes out only once the
                // window has really been asked, and never if it is stuck.
                Dispatcher.Invoke(() => (MainWindow as MainWindow)?.BringToFront());

                answer.Set();
            }
        }
        catch (ObjectDisposedException)
        {
            // The app is closing.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        _shownAck?.Dispose();

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
