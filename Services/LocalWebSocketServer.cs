using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Turn_One_Link.Services;

/// <summary>
/// A local WebSocket broadcast server built on <see cref="TcpListener"/> — no admin rights required.
/// Uses a manual HTTP WebSocket upgrade handshake, then hands off to
/// <see cref="WebSocket.CreateFromStream"/> for framing.
/// Features:
///   - PIN-based pairing (clients authenticate before receiving data).
///   - Heartbeat ping every 5 s; clients that do not pong within 10 s are dropped.
///   - Fan-out: every T1 frame is broadcast to all authenticated clients simultaneously.
///   - Port-conflict detection: raises <see cref="PortConflict"/> if the port is in use.
/// </summary>
public sealed class LocalWebSocketServer : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PongTimeout  = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AuthTimeout  = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly string _pin;
    private readonly int _port;
    private readonly Action<string>? _onLog;
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly CancellationTokenSource _cts = new();

    private TcpListener? _listener;
    private Task? _acceptTask;
    private Task? _pingTask;

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<int>? ClientCountChanged;
    public event Action? PortConflict;

    public int ClientCount => _clients.Count;
    public int Port => _port;
    public string Pin => _pin;

    public LocalWebSocketServer(int port, string pin, Action<string>? onLog = null)
    {
        _port  = port;
        _pin   = pin;
        _onLog = onLog;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public bool TryStart()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            _onLog?.Invoke($"[LOCAL] Could not bind to port {_port}: {ex.Message}");
            PortConflict?.Invoke();
            return false;
        }

        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _pingTask   = Task.Run(() => PingLoopAsync(_cts.Token));

        _onLog?.Invoke($"[LOCAL] Server started on port {_port}. PIN: {_pin}");
        return true;
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
        foreach (var (_, c) in _clients) c.Dispose();
        _clients.Clear();
        try { _listener?.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    // ── Broadcast ─────────────────────────────────────────────────────────────

    public void Broadcast(string json)
    {
        if (_clients.IsEmpty) return;
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
        foreach (var (id, client) in _clients)
            _ = SendToClientAsync(id, client, bytes);
    }

    // ── Accept loop ───────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener!.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _onLog?.Invoke($"[LOCAL] Accept error: {ex.Message}");
                break;
            }

            _ = Task.Run(() => HandleTcpClientAsync(tcp, token), token);
        }
    }

    // ── Per-connection handler ────────────────────────────────────────────────

    private async Task HandleTcpClientAsync(TcpClient tcp, CancellationToken token)
    {
        tcp.NoDelay = true;
        using var _ = tcp; // ensure disposal

        NetworkStream stream;
        try { stream = tcp.GetStream(); }
        catch { return; }

        // ── Step 1: Read HTTP upgrade request ─────────────────────────────────
        string? wsKey = null;
        bool isUpgrade = false;
        try
        {
            var headers = await ReadHttpHeadersAsync(stream, token);
            headers.TryGetValue("Sec-WebSocket-Key", out wsKey);
            headers.TryGetValue("Upgrade", out var upgradeVal);
            isUpgrade = string.Equals(upgradeVal, "websocket", StringComparison.OrdinalIgnoreCase);
        }
        catch { return; }

        if (!isUpgrade || wsKey == null)
        {
            // Plain browser health-check
            var body = "{\"service\":\"Turn One Link\",\"status\":\"online\"}"u8.ToArray();
            var resp = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            try { await stream.WriteAsync(resp, token); await stream.WriteAsync(body, token); } catch { }
            return;
        }

        // ── Step 2: Send 101 Switching Protocols ──────────────────────────────
        var acceptKey = ComputeWsAccept(wsKey);
        var handshake = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n");
        try { await stream.WriteAsync(handshake, token); }
        catch { return; }

        // ── Step 3: Create managed WebSocket ─────────────────────────────────
        WebSocket ws;
        try
        {
            ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer          = true,
                KeepAliveInterval = TimeSpan.Zero   // we handle pings ourselves
            });
        }
        catch { return; }

        using var wsDispose = ws;

        // ── Step 4: PIN authentication ────────────────────────────────────────
        var clientId = Guid.NewGuid().ToString("N")[..8];
        try
        {
            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            authCts.CancelAfter(AuthTimeout);

            await SendJsonAsync(ws, new { type = "auth_required", pinLength = _pin.Length }, authCts.Token);

            var msg = await ReceiveTextAsync(ws, authCts.Token);
            string? receivedPin = null;
            if (msg != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(msg);
                    if (doc.RootElement.TryGetProperty("pin", out var el))
                        receivedPin = el.GetString();
                }
                catch { }
            }

            if (receivedPin != _pin)
            {
                await SendJsonAsync(ws, new { type = "auth_failed" }, CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Wrong PIN", CancellationToken.None);
                _onLog?.Invoke($"[LOCAL] Client {clientId} failed PIN auth.");
                return;
            }

            await SendJsonAsync(ws, new { type = "auth_ok", clientId }, authCts.Token);
        }
        catch (OperationCanceledException)
        {
            _onLog?.Invoke($"[LOCAL] Client {clientId} auth timed out.");
            try { await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth timeout", CancellationToken.None); } catch { }
            return;
        }

        // ── Step 5: Register and receive ─────────────────────────────────────
        var client = new ConnectedClient(clientId, ws);
        _clients[clientId] = client;
        _onLog?.Invoke($"[LOCAL] Client {clientId} authenticated. Total: {_clients.Count}");
        ClientCountChanged?.Invoke(_clients.Count);

        try { await ReceiveLoopAsync(clientId, client, token); }
        finally { RemoveClient(clientId); }
    }

    private async Task ReceiveLoopAsync(string clientId, ConnectedClient client, CancellationToken token)
    {
        var buffer = new byte[512];
        try
        {
            while (!token.IsCancellationRequested && client.WebSocket.State == WebSocketState.Open)
            {
                var result = await client.WebSocket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _onLog?.Invoke($"[LOCAL] Client {clientId} disconnected gracefully.");
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (text.Contains("\"pong\"") || text.Contains("\"heartbeat_ack\""))
                        client.LastPong = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _onLog?.Invoke($"[LOCAL] Client {clientId} error: {ex.Message}"); }
    }

    // ── Ping loop ─────────────────────────────────────────────────────────────

    private async Task PingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(PingInterval, token); }
            catch (OperationCanceledException) { break; }

            var now     = DateTime.UtcNow;
            var pingMsg = new ArraySegment<byte>(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = "ping", at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() })));

            foreach (var (id, client) in _clients)
            {
                if (now - client.LastPong > PongTimeout)
                {
                    _onLog?.Invoke($"[LOCAL] Client {id} timed out.");
                    RemoveClient(id);
                    continue;
                }
                try
                {
                    if (client.WebSocket.State == WebSocketState.Open)
                        await client.WebSocket.SendAsync(pingMsg, WebSocketMessageType.Text, true, token);
                }
                catch { RemoveClient(id); }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendToClientAsync(string clientId, ConnectedClient client, ArraySegment<byte> bytes)
    {
        if (client.WebSocket.State != WebSocketState.Open) return;
        try
        {
            await client.SendLock.WaitAsync();
            await client.WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { RemoveClient(clientId); }
        finally { client.SendLock.Release(); }
    }

    private static async Task SendJsonAsync(WebSocket ws, object payload, CancellationToken token)
    {
        var bytes = new ArraySegment<byte>(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonCamel)));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken token)
    {
        var buffer = new byte[1024];
        try
        {
            var result = await ws.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Text)
                return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }
        catch { }
        return null;
    }

    private void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            client.Dispose();
            _onLog?.Invoke($"[LOCAL] Client {clientId} removed. Total: {_clients.Count}");
            ClientCountChanged?.Invoke(_clients.Count);
        }
    }

    /// <summary>
    /// Reads HTTP request headers from a raw TCP stream into a dictionary.
    /// Stops at the blank line that terminates the header block.
    /// </summary>
    private static async Task<Dictionary<string, string>> ReadHttpHeadersAsync(
        NetworkStream stream, CancellationToken token)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb      = new StringBuilder();
        var buf     = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(buf.AsMemory(0, 1), token);
            if (read == 0) break;

            char c = (char)buf[0];
            sb.Append(c);

            // Detect end of headers (\r\n\r\n)
            if (sb.Length >= 4 &&
                sb[^4] == '\r' && sb[^3] == '\n' &&
                sb[^2] == '\r' && sb[^1] == '\n')
                break;
        }

        // Parse header lines (skip the first request-line)
        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx < 0) continue;
            var name  = lines[i][..colonIdx].Trim();
            var value = lines[i][(colonIdx + 1)..].Trim();
            headers[name] = value;
        }

        return headers;
    }

    /// <summary>Computes the Sec-WebSocket-Accept header value per RFC 6455.</summary>
    private static string ComputeWsAccept(string clientKey)
    {
        const string magic  = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var combined = Encoding.UTF8.GetBytes(clientKey + magic);
        return Convert.ToBase64String(SHA1.HashData(combined));
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class ConnectedClient : IDisposable
    {
        public string     Id        { get; }
        public WebSocket  WebSocket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public DateTime   LastPong  { get; set; } = DateTime.UtcNow;

        public ConnectedClient(string id, WebSocket ws) { Id = id; WebSocket = ws; }

        public void Dispose()
        {
            SendLock.Dispose();
            try { WebSocket.Dispose(); } catch { }
        }
    }
}
