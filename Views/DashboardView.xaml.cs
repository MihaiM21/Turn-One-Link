using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Turn_One_Link.Services;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Key = System.Windows.Input.Key;

namespace Turn_One_Link.Views;

public partial class DashboardView : UserControl
{
    public event Action? SignOutRequested;
    /// <summary>Fired when the user successfully saves a new port and approves a restart.</summary>
    public event Action<int>? PortChangeRequested;

    private readonly SolidColorBrush _connectedBrush    = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private readonly SolidColorBrush _disconnectedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private readonly SolidColorBrush _connectingBrush   = new(Color.FromRgb(0xFF, 0xC1, 0x4B));
    private readonly SolidColorBrush _deviceBrush       = new(Color.FromRgb(0x60, 0xA5, 0xFA)); // blue for devices

    public DashboardView()
    {
        InitializeComponent();
        ApplySimStatus(SimStatus.Disconnected, null);
        ApplyServerStatus(false);
        ApplyLocalDeviceCount(0);
    }

    public void SetUser(string displayName)
    {
        UserNameText.Text = displayName;
    }

    // ── Sim Status ────────────────────────────────────────────────────────────

    public void ApplySimStatus(SimStatus status, string? gameName)
    {
        bool connected = status == SimStatus.Connected;
        var brush = connected ? _connectedBrush : _disconnectedBrush;

        SimStatusDot.Fill   = brush;
        SimGlowDot.Fill     = brush;
        SimStatusLabel.Text = connected ? "Connected" : "Disconnected";
        SimGameName.Text    = connected && gameName != null ? gameName : "No game detected";
    }

    // ── Server Status ─────────────────────────────────────────────────────────

    public void ApplyServerStatus(bool connected)
    {
        var brush = connected ? _connectedBrush : _disconnectedBrush;

        ServerStatusDot.Fill   = brush;
        ServerGlowDot.Fill     = brush;
        ServerStatusLabel.Text = connected ? "Online" : "Offline";
        ServerStatusText.Text  = connected ? "Connected" : "Disconnected";
    }

    public void ApplyServerState(TelemetryConnectionState state)
    {
        SolidColorBrush brush;
        string label, text;
        switch (state)
        {
            case TelemetryConnectionState.Connected:
                brush = _connectedBrush;   label = "Online";       text = "Connected";      break;
            case TelemetryConnectionState.Connecting:
                brush = _connectingBrush;  label = "Connecting";   text = "Connecting...";  break;
            case TelemetryConnectionState.Reconnecting:
                brush = _connectingBrush;  label = "Reconnecting"; text = "Reconnecting..."; break;
            default:
                brush = _disconnectedBrush; label = "Offline";     text = "Disconnected";   break;
        }
        ServerStatusDot.Fill   = brush;
        ServerGlowDot.Fill     = brush;
        ServerStatusLabel.Text = label;
        ServerStatusText.Text  = text;
    }

    // ── Local Server ──────────────────────────────────────────────────────────

    /// <summary>Sets the local server address and PIN displayed in the card.</summary>
    public void SetLocalServerInfo(string address, string pin, int port)
    {
        LocalAddressText.Text = address;
        LocalPinText.Text     = pin;
        PortConflictBanner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Updates the connected device count and status dot colour.</summary>
    public void ApplyLocalDeviceCount(int count)
    {
        var brush = count > 0 ? _deviceBrush : _disconnectedBrush;
        LocalStatusDot.Fill  = brush;
        LocalGlowDot.Fill    = brush;
        LocalDeviceCountLabel.Text = count == 1 ? "1 connected" : $"{count} connected";
    }

    /// <summary>Shows the port-conflict warning banner with a custom message.</summary>
    public void ShowPortConflict(int port)
    {
        PortConflictText.Text =
            $"Port {port} is already in use by another application. " +
            $"Set LOCAL_WS_PORT={port + 1} (or another free port) in your .env file and restart.";
        PortConflictBanner.Visibility = Visibility.Visible;

        // Grey-out the local card to indicate it's non-functional
        LocalAddressText.Text     = $"ws://—:{port}";
        LocalPinText.Text         = "—";
        LocalStatusDot.Fill       = _disconnectedBrush;
        LocalGlowDot.Fill         = _disconnectedBrush;
        LocalDeviceCountLabel.Text = "Unavailable";
    }

    // ── Console Log ───────────────────────────────────────────────────────────

    private int _logLines = 0;
    public void AddConsoleLog(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_logLines > 200)
            {
                var text = ConsoleOutput.Text;
                int firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0)
                    ConsoleOutput.Text = text.Substring(firstNewline + 1);
                else
                    _logLines = 0;
            }
            else
            {
                _logLines++;
            }

            ConsoleOutput.Text += $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
            ConsoleScroll.ScrollToEnd();
        });
    }

    // ── Port editor ───────────────────────────────────────────────────────────

    private void EditPortButton_Click(object sender, RoutedEventArgs e)
    {
        // Seed the textbox with the current port
        var current = LocalAddressText.Text;
        var colonIdx = current.LastIndexOf(':');
        PortInputBox.Text = colonIdx >= 0 ? current[(colonIdx + 1)..] : "8080";

        PortEditPanel.Visibility = Visibility.Visible;
        PortInputBox.Focus();
        PortInputBox.SelectAll();
    }

    private void CancelPortButton_Click(object sender, RoutedEventArgs e)
        => PortEditPanel.Visibility = Visibility.Collapsed;

    private void PortInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryApplyPort();
        else if (e.Key == Key.Escape) PortEditPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyPortButton_Click(object sender, RoutedEventArgs e)
        => TryApplyPort();

    private void TryApplyPort()
    {
        if (!int.TryParse(PortInputBox.Text.Trim(), out int port) || port < 1024 || port > 65535)
        {
            PortInputBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x17, 0x3A));
            PortInputBox.ToolTip = "Enter a valid port between 1024 and 65535";
            return;
        }

        // Reset validation style
        PortInputBox.ClearValue(System.Windows.Controls.TextBox.BorderBrushProperty);
        PortInputBox.ToolTip = null;
        PortEditPanel.Visibility = Visibility.Collapsed;

        // Persist the new port to .env.local
        SavePortToEnvLocal(port);

        // Ask user to restart
        var result = System.Windows.MessageBox.Show(
            $"Port changed to {port}.\n\nTurn One Link needs to restart to apply this change.\n\nRestart now?",
            "Restart Required",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            PortChangeRequested?.Invoke(port);
    }

    private static void SavePortToEnvLocal(int port)
    {
        try
        {
            var envLocalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env.local");

            // Read existing lines (or start fresh)
            var lines = File.Exists(envLocalPath)
                ? new System.Collections.Generic.List<string>(File.ReadAllLines(envLocalPath))
                : new System.Collections.Generic.List<string>();

            // Replace or append LOCAL_WS_PORT
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("LOCAL_WS_PORT=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"LOCAL_WS_PORT={port}";
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add($"LOCAL_WS_PORT={port}");

            File.WriteAllLines(envLocalPath, lines);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not save port setting: {ex.Message}\n\nPlease set LOCAL_WS_PORT={port} manually in .env.local.",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
        => SignOutRequested?.Invoke();
}
