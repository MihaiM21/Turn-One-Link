using System.Windows;

namespace Turn_One_Link.Views;

public enum CloseDialogResult { MinimizeToTray, CloseApp, Cancel }

public partial class CloseDialog : Window
{
    public CloseDialogResult Result { get; private set; } = CloseDialogResult.Cancel;

    public CloseDialog()
    {
        InitializeComponent();
    }

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Result = CloseDialogResult.MinimizeToTray;
        Close();
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        Result = CloseDialogResult.CloseApp;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = CloseDialogResult.Cancel;
        Close();
    }
}
