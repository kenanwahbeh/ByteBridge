using System.Windows;
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
    }
}
