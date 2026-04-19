using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Turn_One_Link.Services;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;

namespace Turn_One_Link.Views;

public partial class DashboardView : UserControl
{
    public event Action? SignOutRequested;

    private readonly SolidColorBrush _connectedBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private readonly SolidColorBrush _disconnectedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    public DashboardView()
    {
        InitializeComponent();
        ApplySimStatus(SimStatus.Disconnected, null);
        ApplyServerStatus(false);
    }

    public void SetUser(string displayName)
    {
        UserNameText.Text = displayName;
    }

    public void ApplySimStatus(SimStatus status, string? gameName)
    {
        bool connected = status == SimStatus.Connected;
        var brush = connected ? _connectedBrush : _disconnectedBrush;

        SimStatusDot.Fill = brush;
        SimGlowDot.Fill = brush;
        SimStatusLabel.Text = connected ? "Connected" : "Disconnected";
        SimGameName.Text = connected && gameName != null ? gameName : "No game detected";
    }

    public void ApplyServerStatus(bool connected)
    {
        var brush = connected ? _connectedBrush : _disconnectedBrush;

        ServerStatusDot.Fill = brush;
        ServerGlowDot.Fill = brush;
        ServerStatusLabel.Text = connected ? "Online" : "Offline";
        ServerStatusText.Text = connected ? "Connected" : "Disconnected";
    }
    

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        SignOutRequested?.Invoke();
    }
}
