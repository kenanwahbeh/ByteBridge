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
     * Machine-wide, not per session: the app controls one Windows
     * service, so two copies -- even one per signed-in user -- would be
     * two people driving the same switch without seeing each other.
     */
    private const string InstanceName = @"Global\ByteBridge.ControlPanel";

    private const string ShowSignalName = @"Global\ByteBridge.ControlPanel.Show";

    private Mutex? _instance;

    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, InstanceName, out var first);

        if (!first)
        {
            /*
             * Tell the copy that is already running to come forward,
             * then leave without ever creating a window. This runs
             * before base.OnStartup because that is what opens the
             * StartupUri window.
             */
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The first copy is still starting; it will be in front anyway.
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
