using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Turn_One_Link.Services;
using Turn_One_Link.Views;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Turn_One_Link;

public partial class MainWindow : Window
{
    private readonly AuthService _auth = new();
    private readonly ConnectionService _connection = new();
    private readonly AccTelemetryService _telemetry = new();
    private readonly SessionManager _sessionManager = new();
    private TelemetryStreamingService? _streaming;
    private LocalWebSocketServer? _localServer;
    private LoginView? _loginView;
    private DashboardView? _dashboardView;
    private bool _isExiting;
    private bool _simConnected;
    private bool _serverConnected;
    private bool _telemetryActive;
    private DateTime _lastConsoleLog = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        var (restored, error) = await _auth.TryRestoreSessionAsync();
        if (restored)
            ShowDashboard();
        else
            ShowLogin(error);
    }

    public void PrepareExit()
    {
        _isExiting = true;
        _connection.Dispose();
        _telemetry.Dispose();
        _streaming?.Dispose();
        _localServer?.Dispose();
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

    private void UpdateAccentMark()
    {
        int count = (_simConnected ? 1 : 0) + (_serverConnected ? 1 : 0);

        Color top, bottom, glow;
        if (count == 2)
        {
            top    = Color.FromRgb(0x4A, 0xDE, 0x80);
            bottom = Color.FromRgb(0x22, 0xC5, 0x5E);
            glow   = Color.FromRgb(0x22, 0xC5, 0x5E);
        }
        else if (count == 1)
        {
            top    = Color.FromRgb(0xFF, 0xC1, 0x4B);
            bottom = Color.FromRgb(0xFF, 0x8C, 0x00);
            glow   = Color.FromRgb(0xFF, 0x8C, 0x00);
        }
        else
        {
            top    = Color.FromRgb(0xFF, 0x4D, 0x6A);
            bottom = Color.FromRgb(0xE8, 0x17, 0x3A);
            glow   = Color.FromRgb(0xE8, 0x17, 0x3A);
        }

        AccentMark.Background = new LinearGradientBrush(
            new GradientStopCollection { new(top, 0), new(bottom, 1) },
            new Point(0, 0), new Point(0, 1));

        AccentMark.Effect = new DropShadowEffect
        {
            Color = glow, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9
        };
    }

    private void ShowLogin(string? error = null)
    {
        _simConnected   = false;
        _serverConnected = false;
        UpdateAccentMark();
        _connection.Stop();
        StopTelemetry();
        _localServer?.Stop();

        Height    = 480;
        MinHeight = 480;
        _loginView = new LoginView();
        _loginView.LoginRequested += OnLoginRequested;
        MainContent.Content = _loginView;

        if (!string.IsNullOrWhiteSpace(error))
            _loginView.ShowError(error);
    }

    private void ShowDashboard()
    {
        _dashboardView = new DashboardView();
        _dashboardView.SetUser(_auth.CurrentSession!.DisplayName);
        _dashboardView.SignOutRequested += OnSignOut;
        _dashboardView.PortChangeRequested += OnPortChangeRequested;

        // Start local WebSocket server
        StartLocalServer();

        _connection.SimStatusChanged += state =>
            Dispatcher.Invoke(() =>
            {
                _simConnected = state.Status == SimStatus.Connected;
                UpdateAccentMark();
                _dashboardView?.ApplySimStatus(state.Status, state.GameName);

                bool isAcc = state.GameName != null && (
                    state.GameName.Contains("Assetto Corsa") ||
                    state.GameName.Contains("AC2") ||
                    state.GameName == "acs");

                if (_simConnected && isAcc)
                {
                    StartTelemetryAsync();
                }
                else if (!_simConnected)
                {
                    _sessionManager.OnSimDisconnected();
                    StopTelemetry();
                }
            });

        MainContent.Content = _dashboardView;
        _connection.Start();

        Dispatcher.BeginInvoke(() =>
        {
            MinHeight = 640;
            Height    = 640;
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    // ── Local WebSocket Server ────────────────────────────────────────────────

    private void StartLocalServer()
    {
        int port = GetLocalPort();
        string pin  = GeneratePin();

        _localServer?.Dispose();
        _localServer = new LocalWebSocketServer(port, pin,
            msg => _dashboardView?.AddConsoleLog(msg));

        _localServer.ClientCountChanged += count =>
            Dispatcher.Invoke(() => _dashboardView?.ApplyLocalDeviceCount(count));

        _localServer.PortConflict += () =>
            Dispatcher.Invoke(() => _dashboardView?.ShowPortConflict(port));

        bool started = _localServer.TryStart();
        if (started)
        {
            var localIp = GetLocalIpAddress();
            var address = $"ws://{localIp}:{port}";
            _dashboardView?.SetLocalServerInfo(address, pin, port);
            _dashboardView?.AddConsoleLog($"[LOCAL] Server listening on {address} | PIN: {pin}");
        }
    }

    private static int GetLocalPort()
    {
        var envPort = Environment.GetEnvironmentVariable("LOCAL_WS_PORT")
                   ?? Environment.GetEnvironmentVariable("WEBSOCKET_PORT");
        return int.TryParse(envPort, out int p) ? p : 8080;
    }

    private static string GeneratePin()
    {
        // 6-digit PIN, regenerated each app launch for security
        return Random.Shared.Next(100_000, 999_999).ToString();
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            return "localhost";
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

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
        StopTelemetry();
        ShowLogin();
    }

    private void OnPortChangeRequested(int newPort)
    {
        // Gracefully shut down all services, then relaunch the current executable.
        PrepareExit();
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exe))
            System.Diagnostics.Process.Start(exe);
        ((App)Application.Current).ExitApp();
    }

    // ── Telemetry ─────────────────────────────────────────────────────────────

    private void StartTelemetryAsync()
    {
        if (_telemetryActive) return;
        _telemetryActive = true;

        _streaming?.Dispose();
        _streaming = new TelemetryStreamingService(
            _auth.ApiBaseUrl,
            _auth,
            msg => _dashboardView?.AddConsoleLog($"[SYS] {msg}")
        );

        WireSessionManager(_streaming);
        WireStreamingState(_streaming);
        WireTelemetryEvents(_streaming);

        _ = _streaming.StartAsync();
        _telemetry.Start();
    }

    private void WireSessionManager(TelemetryStreamingService streaming)
    {
        // Detach any prior subscriptions defensively
        _sessionManager.SessionStarted       -= OnSessionStarted;
        _sessionManager.SessionEnded         -= OnSessionEnded;
        _sessionManager.SessionPaused        -= OnSessionPaused;
        _sessionManager.SessionResumed       -= OnSessionResumed;
        _sessionManager.StaticRefreshRequested -= OnStaticRefreshRequested;

        _sessionManager.SessionStarted       += OnSessionStarted;
        _sessionManager.SessionEnded         += OnSessionEnded;
        _sessionManager.SessionPaused        += OnSessionPaused;
        _sessionManager.SessionResumed       += OnSessionResumed;
        _sessionManager.StaticRefreshRequested += OnStaticRefreshRequested;

        void OnSessionStarted(SessionInfo info)
        {
            streaming.SetActiveSession(info.SessionId);
            streaming.EmitSessionStart(info);
            _dashboardView?.AddConsoleLog($"[SESSION] Start {info.SessionType} on {info.Track} ({info.CarModel})");
        }
        void OnSessionEnded(SessionEndInfo info)
        {
            streaming.EmitSessionEnd(info);
            streaming.SetActiveSession(null);
            _dashboardView?.AddConsoleLog($"[SESSION] End — laps:{info.CompletedLaps} best:{info.BestLapMs}ms");
        }
        void OnSessionPaused(string id)
        {
            streaming.EmitSessionPause(id);
            streaming.SetStreamingFilters(physicsEnabled: false, graphicsEnabled: true);
            _dashboardView?.AddConsoleLog("[SESSION] Paused");
        }
        void OnSessionResumed(string id)
        {
            streaming.EmitSessionResume(id);
            streaming.SetStreamingFilters(physicsEnabled: true, graphicsEnabled: true);
            _dashboardView?.AddConsoleLog("[SESSION] Resumed");
        }
        void OnStaticRefreshRequested() => _telemetry.RequestStaticRefresh();
    }

    private void WireStreamingState(TelemetryStreamingService streaming)
    {
        streaming.ConnectionStateChanged -= OnConnectionStateChanged;
        streaming.ConnectionStateChanged += OnConnectionStateChanged;
        streaming.ReauthRequired         -= OnReauthRequired;
        streaming.ReauthRequired         += OnReauthRequired;

        void OnConnectionStateChanged(TelemetryConnectionState s) =>
            Dispatcher.Invoke(() =>
            {
                _serverConnected = s == TelemetryConnectionState.Connected;
                UpdateAccentMark();
                _dashboardView?.ApplyServerState(s);
            });

        void OnReauthRequired() =>
            Dispatcher.Invoke(() =>
            {
                _dashboardView?.AddConsoleLog("[AUTH] Session expired, please sign in again.");
                OnSignOut();
            });
    }

    private void WireTelemetryEvents(TelemetryStreamingService streaming)
    {
        _telemetry.PhysicsUpdated    -= streaming.OnPhysicsUpdated;
        _telemetry.GraphicsUpdated   -= streaming.OnGraphicsUpdated;
        _telemetry.StaticInfoUpdated -= streaming.OnStaticUpdated;
        _telemetry.OnError           -= Telemetry_OnError;
        _telemetry.GraphicsUpdated   -= _sessionManager.OnGraphics;
        _telemetry.StaticInfoUpdated -= _sessionManager.OnStatic;

        // Session manager runs first so sessionId is current before frames are enqueued.
        _telemetry.GraphicsUpdated   += _sessionManager.OnGraphics;
        _telemetry.StaticInfoUpdated += _sessionManager.OnStatic;

        _telemetry.PhysicsUpdated    += streaming.OnPhysicsUpdated;
        _telemetry.GraphicsUpdated   += streaming.OnGraphicsUpdated;
        _telemetry.StaticInfoUpdated += streaming.OnStaticUpdated;
        _telemetry.OnError           += Telemetry_OnError;

        // ── T1 Protocol: update mapper + broadcast to local clients ──────────
        _telemetry.PhysicsUpdated += p =>
        {
            T1TelemetryMapper.UpdatePhysics(p);
            BroadcastT1Frame();

            if (_dashboardView != null && DateTime.UtcNow - _lastConsoleLog > TimeSpan.FromMilliseconds(250))
            {
                _lastConsoleLog = DateTime.UtcNow;
                _dashboardView.AddConsoleLog($"[PHYSICS] Spd:{p.SpeedKmh:F1} | RPM:{p.Rpms} | Gear:{p.Gear} | Gas:{p.Gas:F2} | Brake:{p.Brake:F2}");
            }
        };

        _telemetry.GraphicsUpdated += g =>
        {
            T1TelemetryMapper.UpdateGraphics(g);
            BroadcastT1Frame();

            if (_dashboardView != null && DateTime.UtcNow - _lastConsoleLog > TimeSpan.FromMilliseconds(250))
            {
                _lastConsoleLog = DateTime.UtcNow;
                _dashboardView.AddConsoleLog($"[GRAPHICS] Lap:{g.CompletedLaps} | Pos:{g.Position} | Status:{g.Status} | Time:{g.CurrentTime}");
            }
        };

        _telemetry.StaticInfoUpdated += s =>
        {
            T1TelemetryMapper.UpdateStatic(s);
            _dashboardView?.AddConsoleLog($"[STATIC] Track:{s.Track} | Car:{s.CarModel} | Driver:{s.PlayerName} {s.PlayerSurname}");
        };
    }

    private void BroadcastT1Frame()
    {
        if (_localServer == null || _localServer.ClientCount == 0) return;
        var frame = T1TelemetryMapper.BuildFrame(_sessionManager.ActiveSessionId);
        if (frame != null)
            _localServer.Broadcast(T1TelemetryMapper.Serialize(frame));
    }

    private void Telemetry_OnError(Exception ex)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _dashboardView?.AddConsoleLog($"[TELEMETRY ERROR] {ex.Message}");
            if (ex.InnerException != null)
                _dashboardView?.AddConsoleLog($"[TELEMETRY INNER] {ex.InnerException.Message}");
        });
    }

    private void StopTelemetry()
    {
        if (!_telemetryActive) return;
        _telemetryActive = false;

        _telemetry.Stop();
        _ = _streaming?.StopAsync();
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

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
