using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using UserControl = System.Windows.Controls.UserControl;

namespace Turn_One_Link.Views;

public partial class LoginView : UserControl
{
    public event Action<string, string>? LoginRequested;

    public LoginView()
    {
        InitializeComponent();
        SignUpLink.RequestNavigate += (s, e) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        };
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    public void ClearError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }

    public void SetLoading(bool loading)
    {
        LoginButton.IsEnabled = !loading;
        LoginButton.Content = loading ? "Signing in…" : "Sign In";
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Please enter your email and password.");
            return;
        }

        LoginRequested?.Invoke(email, password);
    }
}
