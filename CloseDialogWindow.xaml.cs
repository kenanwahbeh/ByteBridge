using System;
using System.Windows;
using ByteBridge.Localization;

namespace ByteBridge;

public partial class CloseDialogWindow : Window
{
    public CloseDialogResult Result { get; private set; } = CloseDialogResult.Cancel;

    public bool DontAskAgain => DontAskCheckBox.IsChecked == true;

    public CloseDialogWindow()
    {
        InitializeComponent();

        /*
         * Arabic labels run noticeably longer than their English
         * counterparts (this dialog once crammed three buttons into one
         * fixed-width row, which made it look broken), so this
         * also needs to read right-to-left rather than force Arabic
         * text through a left-to-right layout.
         */
        FlowDirection = Strings.CurrentLanguage == "ar"
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("CloseTitle");
        MessageTextBlock.Text = Strings.Get("CloseMessage");
        TrayButton.Content = Strings.Get("MinimizeToTray");
        ExitButton.Content = Strings.Get("ExitApp");
        DontAskCheckBox.Content = Strings.Get("DontAskAgain");
    }

    private void TrayButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CloseDialogResult.MinimizeToTray;
        DialogResult = true;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CloseDialogResult.Exit;
        DialogResult = true;
    }
}

public enum CloseDialogResult
{
    MinimizeToTray,
    Exit,
    Cancel
}
