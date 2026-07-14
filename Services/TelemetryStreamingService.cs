using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Turn_One_Link.Services.AccTelemetry;

namespace Turn_One_Link.Services;

public enum TelemetryConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public class TelemetryStreamingService : IDisposable
{
    private const int ChannelCapacity = 1000;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly string _wsUrl;
    private readonly AuthService _auth;
    private readonly Action<string>? _onLog;
    private readonly Channel<OutgoingMessage> _channel;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _clientVersion;

    private Task? _supervisorTask;
    private ClientWebSocket? _webSocket;
    private TelemetryConnectionState _state = TelemetryConnectionState.Disconnected;
    private StreamWriter? _devFileLogger;

    private string? _activeSessionId;
    private bool _streamPhysicsEnabled = true;
    private bool _streamGraphicsEnabled = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public event Action<TelemetryConnectionState>? ConnectionStateChanged;
    public event Action? ReauthRequired;

    public TelemetryConnectionState State => _state;

    public TelemetryStreamingService(string? apiBaseUrl, AuthService auth, Action<string>? onLog = null)
    {
        _auth = auth;
        _onLog = onLog;
        _clientVersion = typeof(TelemetryStreamingService).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        var baseUrl = apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL") ?? "https://backend.t1f1.com/api";
        _wsUrl = baseUrl.Replace("https://", "wss://").Replace("http://", "ws://").TrimEnd('/') + "/ws/telemetry";

        _channel = Channel.CreateBounded<OutgoingMessage>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

#if DEBUG
        try
        {
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev_logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, $"telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
            _devFileLogger = new StreamWriter(logPath, append: true) { AutoFlush = true };
            _onLog?.Invoke($"[DEV] Telemetry logging enabled: {logPath}");
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"[DEV] Failed to initialize file logger: {ex.Message}");
        }
#endif
    }

    public Task StartAsync()
    {
        if (_supervisorTask != null) return Task.CompletedTask;
        _supervisorTask = Task.Run(() => SupervisorLoopAsync(_lifetimeCts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try { _lifetimeCts.Cancel(); } catch { }
        _channel.Writer.TryComplete();
        try
        {
            var ws = _webSocket;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopped", cts.Token);
            }
        }
        catch { }
        if (_supervisorTask != null)
        {
            try { await _supervisorTask.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        SetState(TelemetryConnectionState.Disconnected);
    }

    // ---- Session control ----------------------------------------------------

    public void SetActiveSession(string? sessionId) => _activeSessionId = sessionId;

    public void SetStreamingFilters(bool physicsEnabled, bool graphicsEnabled)
    {
        _streamPhysicsEnabled = physicsEnabled;
        _streamGraphicsEnabled = graphicsEnabled;
    }

    public void EmitSessionStart(SessionInfo info)
    {
        Enqueue("session_start", new
        {
            sessionId = info.SessionId,
            sessionType = info.SessionType.ToString(),
            track = info.Track,
            carModel = info.CarModel,
            driver = info.DriverName,
            startedAt = info.StartedAt.ToUnixTimeMilliseconds()
        }, info.SessionId);
    }

    public void EmitSessionEnd(SessionEndInfo info)
    {
        Enqueue("session_end", new
        {
            sessionId = info.SessionId,
            endedAt = info.EndedAt.ToUnixTimeMilliseconds(),
            completedLaps = info.CompletedLaps,
            bestLapMs = info.BestLapMs
        }, info.SessionId);
    }

    public void EmitSessionPause(string sessionId)
        => Enqueue("session_pause", new { sessionId, at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, sessionId);

    public void EmitSessionResume(string sessionId)
        => Enqueue("session_resume", new { sessionId, at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, sessionId);

    // ---- Telemetry input ----------------------------------------------------

    public void OnPhysicsUpdated(SPageFilePhysics data)
    {
        if (!_streamPhysicsEnabled) return;
        Enqueue("physics", data, _activeSessionId);
    }

    public void OnGraphicsUpdated(SPageFileGraphic data)
    {
        if (!_streamGraphicsEnabled) return;
        Enqueue("graphics", data, _activeSessionId);
    }

    public void OnStaticUpdated(SPageFileStatic data) => Enqueue("static", data, _activeSessionId);

    private void Enqueue<T>(string type, T data, string? sessionId)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                type,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId,
                data
            }, JsonOptions);

#if DEBUG
            if (_devFileLogger != null)
            {
                lock (_devFileLogger)
                {
                    _devFileLogger.WriteLine(json);
                }
            }
#endif

            _channel.Writer.TryWrite(new OutgoingMessage(json));
        }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Telemetry serialize error: {ex.Message}");
        }
    }

    // ---- Supervisor ---------------------------------------------------------

    private async Task SupervisorLoopAsync(CancellationToken token)
    {
        var backoff = MinBackoff;
        DateTime lastHeartbeat = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            // Refresh / validate token before each connect attempt
            if (_auth.IsTokenExpired(toleranceSeconds: 60))
            {
                _onLog?.Invoke("Telemetry: access token expired, requesting re-auth.");
                ReauthRequired?.Invoke();
                if (!await DelayAsync(backoff, token)) return;
                backoff = NextBackoff(backoff);
                continue;
            }

            SetState(_state == TelemetryConnectionState.Disconnected
                ? TelemetryConnectionState.Connecting
                : TelemetryConnectionState.Reconnecting);

            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = KeepAliveInterval;
                if (!string.IsNullOrEmpty(_auth.AccessToken))
                {
                    ws.Options.SetRequestHeader("Authorization", $"Bearer {_auth.AccessToken}");
                }

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await ws.ConnectAsync(new Uri(_wsUrl), connectCts.Token);
                }

                _webSocket = ws;
                SetState(TelemetryConnectionState.Connected);
                _onLog?.Invoke("Telemetry WebSocket connected.");
                backoff = MinBackoff;
                lastHeartbeat = DateTime.UtcNow;

                // Start a receive loop so server-initiated closes are detected immediately.
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var recvTask = ReceiveLoopAsync(ws, sessionCts.Token);

                try
                {
                    await PumpSendsAsync(ws, lastHeartbeat, sessionCts.Token);
                }
                finally
                {
                    sessionCts.Cancel();
                    try { await recvTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Telemetry connection error: {ex.Message}");
            }
            finally
            {
                try { ws?.Dispose(); } catch { }
                _webSocket = null;
            }

            if (token.IsCancellationRequested) break;

            SetState(TelemetryConnectionState.Reconnecting);
            _onLog?.Invoke($"Telemetry reconnecting in {backoff.TotalSeconds:F0}s...");
            if (!await DelayAsync(backoff, token)) break;
            backoff = NextBackoff(backoff);
        }

        SetState(TelemetryConnectionState.Disconnected);
    }

    private async Task PumpSendsAsync(ClientWebSocket ws, DateTime lastHeartbeat, CancellationToken token)
    {
        var reader = _channel.Reader;
        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            // Heartbeat watchdog — if we've been idle, send a heartbeat
            var sinceHeartbeat = DateTime.UtcNow - lastHeartbeat;
            if (sinceHeartbeat >= HeartbeatInterval)
            {
                lastHeartbeat = DateTime.UtcNow;
                Enqueue("client_heartbeat", new
                {
                    sessionId = _activeSessionId,
                    clientVersion = _clientVersion,
                    uptimeMs = (long)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds
                }, _activeSessionId);
            }

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            waitCts.CancelAfter(HeartbeatInterval);

            OutgoingMessage msg;
            try
            {
                msg = await reader.ReadAsync(waitCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                continue; // heartbeat tick
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(msg.Json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token);
            }
            catch (Exception ex)
            {
                _onLog?.Invoke($"Telemetry send failed, will reconnect: {ex.Message}");
                throw; // bubble up to supervisor for reconnect
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[2048];
        try
        {
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _onLog?.Invoke($"Telemetry server closed connection: {result.CloseStatus} {result.CloseStatusDescription}");
                    return;
                }
                // Server-side messages currently ignored; channel is client→server.
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _onLog?.Invoke($"Telemetry receive loop ended: {ex.Message}");
        }
    }

    private static TimeSpan NextBackoff(TimeSpan current)
    {
        var next = TimeSpan.FromSeconds(Math.Min(current.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        return next < MinBackoff ? MinBackoff : next;
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private void SetState(TelemetryConnectionState state)
    {
        if (_state == state) return;
        _state = state;
        try { ConnectionStateChanged?.Invoke(state); } catch { }
    }

    public void Dispose()
    {
        _devFileLogger?.Dispose();
        _devFileLogger = null;
        _ = StopAsync();
        try { _lifetimeCts.Dispose(); } catch { }
    }

    private readonly record struct OutgoingMessage(string Json);
}
