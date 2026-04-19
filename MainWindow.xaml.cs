using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Turn_One_Link.Services;
using Turn_One_Link.Views;
using Application = System.Windows.Application;

namespace Turn_One_Link;

public partial class MainWindow : Window
{
    private readonly AuthService _auth = new();
    private readonly ConnectionService _connection = new();
    private LoginView? _loginView;
    private DashboardView? _dashboardView;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        ShowLogin();
    }

    public void PrepareExit()
    {
        _isExiting = true;
        _connection.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        var dialog = new CloseDialog { Owner = this };
        dialog.ShowDialog();

        switch (dialog.Result)
        {
            case CloseDialogResult.MinimizeToTray:
                ((App)Application.Current).MinimizeToTray();
                break;
            case CloseDialogResult.CloseApp:
                ((App)Application.Current).ExitApp();
                break;
        }
    }

    private void ShowLogin()
    {
        _connection.Stop();
        Height = 480;
        MinHeight = 480;
        _loginView = new LoginView();
        _loginView.LoginRequested += OnLoginRequested;
        MainContent.Content = _loginView;
    }

    private void ShowDashboard()
    {
        _dashboardView = new DashboardView();
        _dashboardView.SetUser(_auth.CurrentSession!.DisplayName);
        _dashboardView.SignOutRequested += OnSignOut;

        _connection.SimStatusChanged += state =>
            Dispatcher.Invoke(() =>
            {
                _dashboardView?.ApplySimStatus(state.Status, state.GameName);
            });

        _connection.ServerStatusChanged += connected =>
            Dispatcher.Invoke(() =>
            {
                _dashboardView?.ApplyServerStatus(connected);
            });

        MainContent.Content = _dashboardView;
        _connection.Start();

        Dispatcher.BeginInvoke(() =>
        {
            MinHeight = 350;
            Height = 350;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private async void OnLoginRequested(string email, string password)
    {
        _loginView!.SetLoading(true);
        var (success, error) = await _auth.LoginAsync(email, password);
        _loginView.SetLoading(false);

        if (success)
            ShowDashboard();
        else
            _loginView.ShowError(error ?? "Login failed.");
    }

    private void OnSignOut()
    {
        _auth.Logout();
        _connection.Stop();
        ShowLogin();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
