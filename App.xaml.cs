using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace ByteBridge;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
