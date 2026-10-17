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
    /// <summary>Fired whenever the visible panel set changes so the host window can resize.</summary>
    public event Action<double>? DesiredHeightChanged;

    private readonly SolidColorBrush _connectedBrush    = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private readonly SolidColorBrush _disconnectedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private readonly SolidColorBrush _connectingBrush   = new(Color.FromRgb(0xFF, 0xC1, 0x4B));
    private readonly SolidColorBrush _deviceBrush       = new(Color.FromRgb(0x60, 0xA5, 0xFA));

    private AppSettings _settings = AppSettings.Load();
    private bool _simIsStreaming;

    public DashboardView()
    {
        InitializeComponent();
        ApplySimStatus(SimStatus.Disconnected, null);
        ApplyServerStatus(false);
        ApplyLocalDeviceCount(0);

        // Initialise toggles from persisted settings (suppress Changed events while setting)
        ShowTelemetryToggle.Checked         -= SettingsToggle_Changed;
        ShowConsoleToggle.Checked           -= SettingsToggle_Changed;
        ShowLocalWsToggle.Checked           -= SettingsToggle_Changed;
        EnableSessionLoggingToggle.Checked   -= SettingsToggle_Changed;
        ShowTelemetryToggle.Unchecked       -= SettingsToggle_Changed;
        ShowConsoleToggle.Unchecked         -= SettingsToggle_Changed;
        ShowLocalWsToggle.Unchecked         -= SettingsToggle_Changed;
        EnableSessionLoggingToggle.Unchecked -= SettingsToggle_Changed;

        ShowTelemetryToggle.IsChecked        = _settings.ShowLiveTelemetry;
        ShowConsoleToggle.IsChecked          = _settings.ShowConsole;
        ShowLocalWsToggle.IsChecked          = _settings.ShowLocalWsInfo;
        EnableSessionLoggingToggle.IsChecked = _settings.EnableSessionLogging;

        ShowTelemetryToggle.Checked         += SettingsToggle_Changed;
        ShowConsoleToggle.Checked           += SettingsToggle_Changed;
        ShowLocalWsToggle.Checked           += SettingsToggle_Changed;
        EnableSessionLoggingToggle.Checked   += SettingsToggle_Changed;
        ShowTelemetryToggle.Unchecked       += SettingsToggle_Changed;
        ShowConsoleToggle.Unchecked         += SettingsToggle_Changed;
        ShowLocalWsToggle.Unchecked         += SettingsToggle_Changed;
        EnableSessionLoggingToggle.Unchecked += SettingsToggle_Changed;

        ApplySettings();
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

    // ── Live Telemetry HUD ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the live telemetry HUD with the latest physics data.
    /// Panel is only shown when both the setting is enabled AND the sim is streaming.
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateLiveTelemetry(float speedKmh, int gear, int rpm, int maxRpm, float throttle, float brake)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _simIsStreaming = true;

            // Honour the user's setting — only reveal if enabled
            if (_settings.ShowLiveTelemetry)
                LiveTelemetryPanel.Visibility = Visibility.Visible;

            // Speed
            LiveSpeedText.Text = ((int)speedKmh).ToString();

            // Gear: raw ACC value → display string (0=R, 1=N, 2+=1st, 2nd, …)
            LiveGearText.Text = gear switch
            {
                <= 0 => "R",
                1    => "N",
                _    => (gear - 1).ToString()
            };

            // RPM
            LiveRpmText.Text = rpm.ToString("N0");

            // Bars — width proportional to container ActualWidth
            double containerWidth = RpmBarContainer.ActualWidth;
            if (containerWidth > 0)
            {
                double rpmFraction      = maxRpm > 0 ? Math.Clamp((double)rpm / maxRpm, 0, 1) : 0;
                double throttleFraction = Math.Clamp(throttle, 0f, 1f);
                double brakeFraction    = Math.Clamp(brake,    0f, 1f);

                RpmBar.Width      = containerWidth * rpmFraction;
                ThrottleBar.Width = ThrottleBarContainer.ActualWidth * throttleFraction;
                BrakeBar.Width    = BrakeBarContainer.ActualWidth    * brakeFraction;
            }
        });
    }

    /// <summary>
    /// Hides the live telemetry panel when the sim disconnects.
    /// </summary>
    public void HideLiveTelemetry()
    {
        Dispatcher.InvokeAsync(() =>
        {
            _simIsStreaming = false;
            LiveTelemetryPanel.Visibility = Visibility.Collapsed;
            DesiredHeightChanged?.Invoke(CalculateDesiredHeight());
        });
    }

    // ── Settings overlay ─────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Visible;

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void SettingsBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        => SettingsOverlay.Visibility = Visibility.Collapsed;

    private void SettingsToggle_Changed(object sender, RoutedEventArgs e)
    {
        _settings.ShowLiveTelemetry     = ShowTelemetryToggle.IsChecked        == true;
        _settings.ShowConsole           = ShowConsoleToggle.IsChecked          == true;
        _settings.ShowLocalWsInfo       = ShowLocalWsToggle.IsChecked          == true;
        _settings.EnableSessionLogging  = EnableSessionLoggingToggle.IsChecked == true;
        _settings.Save();
        ApplySettings();
    }

    private void OpenSessionsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TurnOneLink", "sessions");
        try
        {
            Directory.CreateDirectory(sessionsDir);
            System.Diagnostics.Process.Start("explorer.exe", sessionsDir);
        }
        catch { }
    }

    private void ApplySettings()
    {
        // Live telemetry: only show when the setting is on AND the sim is actively streaming
        LiveTelemetryPanel.Visibility =
            _settings.ShowLiveTelemetry && _simIsStreaming
                ? Visibility.Visible
                : Visibility.Collapsed;

        // Console log
        ConsoleSection.Visibility =
            _settings.ShowConsole ? Visibility.Visible : Visibility.Collapsed;

        // Local WS info card
        LocalDevicesCard.Visibility =
            _settings.ShowLocalWsInfo ? Visibility.Visible : Visibility.Collapsed;

        DesiredHeightChanged?.Invoke(CalculateDesiredHeight());
    }

    /// <summary>
    /// Calculates the content height the host window should adopt based on visible panels.
    /// Chrome overhead (shadow margins + titlebar) is added by the caller.
    /// </summary>
    private double CalculateDesiredHeight()
    {
        // Base: greeting row + spacers + Sim card + Server card + padding + version label
        // (always visible regardless of settings)
        const double Base = 300;

        double h = Base;
        if (_settings.ShowLocalWsInfo)                          h += 118; // Local WS card
        if (_settings.ShowLiveTelemetry && _simIsStreaming)     h += 156; // Live Telemetry panel
        if (_settings.ShowConsole)                              h += 172; // Console (MinHeight=160 + margins)

        return h;
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
