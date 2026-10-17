using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Turn_One_Link.Services;
using Turn_One_Link.Services.Telemetry;
using Turn_One_Link.Views;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Turn_One_Link;

public partial class MainWindow : Window
{
    private readonly AuthService _auth = new();
    private readonly ConnectionService _connection = new();
    private ITelemetrySource? _telemetry;
    private string _activeSourceName = "ACC";
    private readonly SessionManager _sessionManager = new();
    private readonly DeltaEngine _deltaEngine = new();
    private readonly FuelManager _fuelManager = new();
    private readonly SessionLoggingService _sessionLogger = new();
    private TelemetryStreamingService? _streaming;
    private LocalWebSocketServer? _localServer;
    private LoginView? _loginView;
    private DashboardView? _dashboardView;
    private bool _isExiting;
    private bool _simConnected;
    private bool _serverConnected;
    private bool _telemetryActive;
    private int _lastMaxRpm = 9000;   // updated from static data
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
        _telemetry?.Dispose();
        _streaming?.Dispose();
        _localServer?.Dispose();
        _sessionLogger.FlushOnShutdown();
        _sessionLogger.Dispose();
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

                if (_simConnected)
                {
                    if (!_telemetryActive)
                    {
                        var source = CreateSourceFor(state.GameName, out var sourceName);
                        if (source != null)
                            StartTelemetryAsync(source, sourceName);
                    }
                }
                else
                {
                    _sessionManager.OnSimDisconnected();
                    StopTelemetry();
                    _dashboardView?.HideLiveTelemetry();
                }
            });

        _dashboardView.DesiredHeightChanged += contentHeight =>
            Dispatcher.Invoke(() => AnimateWindowHeight(contentHeight + ChromeOverhead));

        MainContent.Content = _dashboardView;
        _connection.Start();

        Dispatcher.BeginInvoke(() =>
        {
            MinHeight = 340;
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

    /// <summary>Resolves which telemetry source to use for a detected sim process name.
    /// Returns null for sims Turn One Link doesn't have telemetry support for yet.</summary>
    private static ITelemetrySource? CreateSourceFor(string? gameName, out string sourceName)
    {
        sourceName = "ACC";
        if (gameName == null) return null;

        // "Assetto Corsa Competizione" and original "Assetto Corsa" (acs.exe) share the same
        // shared-memory struct family and one adapter class, but need distinct v2 `source`
        // wire values (docs/telemetry/backend_contract_v2.md §2: "acc" vs "ac").
        bool isCompetizione = gameName.Contains("Competizione", StringComparison.OrdinalIgnoreCase) ||
                              gameName.Contains("AC2");
        bool isOriginalAc = gameName == "acs" ||
                             (gameName.Contains("Assetto Corsa", StringComparison.OrdinalIgnoreCase) && !isCompetizione);

        if (isCompetizione)
        {
            sourceName = "ACC";
            return new AccTelemetrySourceAdapter("acc");
        }
        if (isOriginalAc)
        {
            sourceName = "AC";
            return new AccTelemetrySourceAdapter("ac");
        }

        if (gameName.Contains("iRacing", StringComparison.OrdinalIgnoreCase))
        {
            sourceName = "iRacing";
            return new IRacingTelemetryService();
        }

        // EA F1 25/26 broadcast telemetry over UDP; the exact title is resolved from the packet
        // header's packetFormat once the first packet arrives (Services/F1/F1UdpTelemetrySource.cs).
        if (gameName.StartsWith("F1", StringComparison.OrdinalIgnoreCase))
        {
            sourceName = "F1";
            return new Turn_One_Link.Services.F1.F1UdpTelemetrySource();
        }

        return null; // other sims in ConnectionService.OtherSimProcessNames stay undetected for telemetry
    }

    private void StartTelemetryAsync(ITelemetrySource source, string sourceName)
    {
        if (_telemetryActive) return;
        _telemetryActive = true;
        _telemetry = source;
        _activeSourceName = sourceName;

        _streaming?.Dispose();
        _streaming = new TelemetryStreamingService(
            _auth.ApiBaseUrl,
            _auth,
            msg => _dashboardView?.AddConsoleLog($"[SYS] {msg}")
        );

        WireSessionManager(_streaming, source);
        WireStreamingState(_streaming);
        WireTelemetryEvents(_streaming, source);

        _ = _streaming.StartAsync();
        source.Start();
    }

    private void WireSessionManager(TelemetryStreamingService streaming, ITelemetrySource telemetry)
    {
        // Detach any prior subscriptions defensively
        _sessionManager.SessionStarted       -= OnSessionStarted;
        _sessionManager.SessionEnded         -= OnSessionEnded;
        _sessionManager.SessionPaused        -= OnSessionPaused;
        _sessionManager.SessionResumed       -= OnSessionResumed;
        _sessionManager.StaticRefreshRequested -= OnStaticRefreshRequested;
        _sessionManager.SessionInfoReadyV2   -= OnSessionInfoReadyV2;

        _sessionManager.SessionStarted       += OnSessionStarted;
        _sessionManager.SessionEnded         += OnSessionEnded;
        _sessionManager.SessionPaused        += OnSessionPaused;
        _sessionManager.SessionResumed       += OnSessionResumed;
        _sessionManager.StaticRefreshRequested += OnStaticRefreshRequested;
        _sessionManager.SessionInfoReadyV2   += OnSessionInfoReadyV2;

        void OnSessionStarted(SessionInfo info)
        {
            _deltaEngine.Reset();
            _fuelManager.Reset();
            _sessionLogger.OnSessionStarted(info);
            streaming.SetActiveSession(info.SessionId);
            // v1 session_start (ACC-shaped) only goes out under PROTOCOL_V1; the v2 shape is
            // emitted separately from SessionInfoReadyV2 once the richer T1SessionInfo arrives.
            if (TelemetryEnv.ProtocolV1Enabled) streaming.EmitSessionStart(info);
            _dashboardView?.AddConsoleLog($"[SESSION] Start {info.SessionType} on {info.Track} ({info.CarModel})");
        }
        void OnSessionEnded(SessionEndInfo info)
        {
            _deltaEngine.Reset();
            _fuelManager.Reset();
            _sessionLogger.OnSessionEnded(info);
            if (TelemetryEnv.ProtocolV1Enabled)
                streaming.EmitSessionEnd(info);
            else
                streaming.EmitSessionEndV2(info.SessionId, info.EndedAt, info.CompletedLaps, info.BestLapMs);
            streaming.SetActiveSession(null);
            _dashboardView?.AddConsoleLog($"[SESSION] End — laps:{info.CompletedLaps} best:{info.BestLapMs}ms");
        }
        void OnSessionPaused(string id)
        {
            if (TelemetryEnv.ProtocolV1Enabled)
            {
                streaming.EmitSessionPause(id);
                streaming.SetStreamingFilters(physicsEnabled: false, graphicsEnabled: true);
            }
            else
            {
                streaming.EmitSessionPauseV2(id);
                streaming.SetTickStreamingEnabled(false);
            }
            _dashboardView?.AddConsoleLog("[SESSION] Paused");
        }
        void OnSessionResumed(string id)
        {
            if (TelemetryEnv.ProtocolV1Enabled)
            {
                streaming.EmitSessionResume(id);
                streaming.SetStreamingFilters(physicsEnabled: true, graphicsEnabled: true);
            }
            else
            {
                streaming.EmitSessionResumeV2(id);
                streaming.SetTickStreamingEnabled(true);
            }
            _dashboardView?.AddConsoleLog("[SESSION] Resumed");
        }
        void OnStaticRefreshRequested() => telemetry.RequestStaticRefresh();
        void OnSessionInfoReadyV2(T1SessionInfo info)
        {
            if (TelemetryEnv.ProtocolV1Enabled) return;
            streaming.OnSessionInfo(info);
            _dashboardView?.AddConsoleLog($"[SESSION] v2 info: {info.Source} {info.TrackId} ({info.CarId}) {info.SessionType}");
        }
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

    private void WireTelemetryEvents(TelemetryStreamingService streaming, ITelemetrySource telemetry)
    {
        telemetry.OnError           += Telemetry_OnError;

        // Session manager runs first so sessionId is current before frames are enqueued.
        telemetry.GraphicsUpdated     += _sessionManager.OnGraphics;
        telemetry.StaticInfoUpdated   += _sessionManager.OnStatic;
        telemetry.SessionInfoUpdated  += _sessionManager.OnSessionInfoV2;

        // Cloud protocol v2 (docs/telemetry/backend_contract_v2.md): every source's
        // tick/lap_complete stream to the backend the same way, sim-neutral.
        telemetry.TickUpdated    += streaming.OnTick;
        telemetry.LapCompleted   += streaming.OnLapComplete;

        // Legacy v1 cloud stream speaks ACC's raw shared-memory struct shape (see
        // docs/telemetry/backend_contract.md). Only wired when PROTOCOL_V1=true; v2 is the
        // default for every source, ACC/AC included.
        if (TelemetryEnv.ProtocolV1Enabled && telemetry is AccTelemetrySourceAdapter accAdapter)
        {
            accAdapter.Inner.PhysicsUpdated    += streaming.OnPhysicsUpdated;
            accAdapter.Inner.GraphicsUpdated   += streaming.OnGraphicsUpdated;
            accAdapter.Inner.StaticInfoUpdated += streaming.OnStaticUpdated;
        }

        // ── T1 Protocol: update mapper + broadcast to local clients ──────────
        telemetry.PhysicsUpdated += p =>
        {
            _fuelManager.OnPhysics(p.Fuel);
            T1TelemetryMapper.UpdatePhysics(p);
            BroadcastT1Frame();
            _sessionLogger.OnPhysics(p);

            // Drive the live telemetry HUD
            _dashboardView?.UpdateLiveTelemetry(
                p.SpeedKmh, p.Gear, p.Rpm, _lastMaxRpm, p.Gas, p.Brake);

            if (_dashboardView != null && DateTime.UtcNow - _lastConsoleLog > TimeSpan.FromMilliseconds(250))
            {
                _lastConsoleLog = DateTime.UtcNow;
                _dashboardView.AddConsoleLog($"[PHYSICS] Spd:{p.SpeedKmh:F1} | RPM:{p.Rpm} | Gear:{p.Gear} | Gas:{p.Gas:F2} | Brake:{p.Brake:F2}");
            }
        };

        telemetry.GraphicsUpdated += g =>
        {
            // SessionManager.OnGraphics is subscribed first, so Phase is current here.
            _deltaEngine.OnGraphics(g, _sessionManager.Phase);
            _fuelManager.OnGraphics(g, _sessionManager.Phase);
            T1TelemetryMapper.UpdateProMetrics(_deltaEngine.GetSnapshot(), _fuelManager.GetSnapshot());
            T1TelemetryMapper.UpdateGraphics(g);
            BroadcastT1Frame();
            _sessionLogger.OnGraphics(g, _deltaEngine.GetSnapshot(), _fuelManager.GetSnapshot());

            if (_dashboardView != null && DateTime.UtcNow - _lastConsoleLog > TimeSpan.FromMilliseconds(250))
            {
                _lastConsoleLog = DateTime.UtcNow;
                _dashboardView.AddConsoleLog($"[GRAPHICS] Lap:{g.CompletedLaps} | Pos:{g.Position} | Status:{g.Status} | Time:{g.CurrentTimeText}");
            }
        };

        telemetry.StaticInfoUpdated += s =>
        {
            T1TelemetryMapper.UpdateStatic(s);
            if (s.MaxRpm > 0) _lastMaxRpm = s.MaxRpm;
            _dashboardView?.AddConsoleLog($"[STATIC] Track:{s.Track} | Car:{s.CarModel} | Driver:{s.PlayerName} {s.PlayerSurname}");
        };
    }

    private void BroadcastT1Frame()
    {
        if (_localServer == null || _localServer.ClientCount == 0) return;
        var frame = T1TelemetryMapper.BuildFrame(_sessionManager.ActiveSessionId, _activeSourceName);
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

        _telemetry?.Stop();
        _telemetry?.Dispose();
        _telemetry = null;
        _ = _streaming?.StopAsync();
    }

    // ── Window sizing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Total non-content pixels the window adds on top of the dashboard's own height.
    /// = shadow margin (20×2) + title bar (48) + dashboard grid margin (12+16).
    /// </summary>
    private const double ChromeOverhead = 116;

    /// <summary>
    /// Smoothly resizes the window to <paramref name="targetHeight"/> in 200 ms.
    /// </summary>
    private void AnimateWindowHeight(double targetHeight)
    {
        var animation = new DoubleAnimation(
            targetHeight,
            new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        MinHeight = Math.Min(MinHeight, targetHeight); // shrink fence first
        BeginAnimation(HeightProperty, animation);
        // Update MinHeight after the animation settles so the user can't drag below content
        animation.Completed += (_, _) => MinHeight = targetHeight;
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
