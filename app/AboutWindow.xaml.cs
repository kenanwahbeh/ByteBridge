using System;
using System.Diagnostics;
using System.Windows;
using ByteBridge.Data;
using ByteBridge.Gateway;
using ByteBridge.Localization;

namespace ByteBridge;

public partial class AboutWindow : Window
{
    private readonly SqliteDatabase _database;

    public AboutWindow(SqliteDatabase database)
    {
        InitializeComponent();

        FlowDirection = Strings.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        _database = database;

        var version =
            System.Reflection.Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString(3)
            ?? "1.0.0";

        Title = Strings.Get("AboutTitle");
        AppTitleTextBlock.Text = Strings.Get("AppTitle");
        VersionTextBlock.Text = Strings.Format("AboutVersion", version);
        DescriptionTextBlock.Text = Strings.Get("AboutDescription");
        MadeByTextBlock.Text = Strings.Get("AboutMadeBy");
        OpenLogsButton.Content = Strings.Get("AboutOpenLogs");
        CloseButton.Content = Strings.Get("AboutClose");
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_database.LogDirectory);

            Process.Start(new ProcessStartInfo(_database.LogDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void MakerLink_RequestNavigate(
        object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                Strings.Get("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
