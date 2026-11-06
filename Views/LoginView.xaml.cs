using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using UserControl = System.Windows.Controls.UserControl;

namespace Turn_One_Link.Views;

public partial class LoginView : UserControl
{
    public event Action<string, string>? LoginRequested;

    /// <summary>"Sign in with your browser" — start device pairing (HCR-006).</summary>
    public event Action? PairRequested;
    public event Action? PairCancelRequested;
    public event Action? OpenBrowserRequested;

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

    // ── Pairing ──────────────────────────────────────────────────────────────

    public void SetPairingStarting(bool starting)
    {
        PairButton.IsEnabled = !starting;
        PairButton.Content = starting ? "Getting a code…" : "Sign in with your browser";
    }

    public void ShowPairingCode(string userCode)
    {
        ClearError();
        PairCodeText.Text = userCode;
        PairStatusText.Text = "Waiting for approval…";
        PairStartPanel.Visibility = Visibility.Collapsed;
        EmailForm.Visibility = Visibility.Collapsed;
        EmailToggleText.Visibility = Visibility.Collapsed;
        PairCodePanel.Visibility = Visibility.Visible;
    }

    public void SetPairingStatus(string status) => PairStatusText.Text = status;

    public void ShowPairingStart()
    {
        SetPairingStarting(false);
        PairCodePanel.Visibility = Visibility.Collapsed;
        PairStartPanel.Visibility = Visibility.Visible;
        EmailToggleText.Visibility = Visibility.Visible;
    }

    private void PairButton_Click(object sender, RoutedEventArgs e)
    {
        ClearError();
        PairRequested?.Invoke();
    }

    private void OpenBrowserAgain_Click(object sender, RoutedEventArgs e) => OpenBrowserRequested?.Invoke();

    private void CancelPairing_Click(object sender, RoutedEventArgs e) => PairCancelRequested?.Invoke();

    private void EmailToggle_Click(object sender, RoutedEventArgs e)
    {
        bool showEmail = EmailForm.Visibility != Visibility.Visible;
        EmailForm.Visibility = showEmail ? Visibility.Visible : Visibility.Collapsed;
        PairStartPanel.Visibility = showEmail ? Visibility.Collapsed : Visibility.Visible;
        EmailToggleRun.Text = showEmail ? "Sign in with your browser instead" : "Sign in with email instead";
        ClearError();
    }

    // ── Email fallback ───────────────────────────────────────────────────────

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
