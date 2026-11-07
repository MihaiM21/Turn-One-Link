using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Turn_One_Link.Services;

/// <summary>
/// A local WebSocket broadcast server built on <see cref="TcpListener"/> — no admin rights required.
/// Uses a manual HTTP WebSocket upgrade handshake, then hands off to
/// <see cref="WebSocket.CreateFromStream"/> for framing.
/// Features:
///   - PIN-based pairing (clients authenticate before receiving data), with a lockout after
///     repeated wrong PINs from the same address.
///   - Only private-network and loopback peers are served; browser connections from another
///     site's page (a different Origin) are refused.
///   - Heartbeat ping every 5 s; clients that do not pong within 10 s are dropped.
///   - Fan-out: every T1 frame is queued to each authenticated client; each client has exactly one
///     sender, so frames never overlap on a socket, and a slow client drops stale frames.
///   - Port-conflict detection: raises <see cref="PortConflict"/> if the port is in use.
/// </summary>
public sealed class LocalWebSocketServer : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PongTimeout  = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AuthTimeout  = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private const int MaxHandshakeBytes = 8 * 1024;
    private const int MaxFailedPins = 5;
    private static readonly TimeSpan PinLockout = TimeSpan.FromSeconds(60);
    private const int ClientQueueLength = 4; // telemetry frames; older ones are stale anyway

    /// <summary>Pseudo-header carrying the request path out of <see cref="ReadHttpHeadersAsync"/>.</summary>
    private const string RequestPathKey = ":path";

    /// <summary>The phone dashboard (Content/phone-dash.html), embedded in the exe.</summary>
    private static readonly Lazy<byte[]> PhoneDashboardHtml = new(() =>
    {
        using var stream = typeof(LocalWebSocketServer).Assembly.GetManifestResourceStream("Turn_One_Link.Content.phone-dash.html");
        if (stream == null) return "Dashboard not available in this build."u8.ToArray();
        using var ms = new System.IO.MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    /// <summary>The address a phone opens (the PIN rides in the fragment, never sent over the network).</summary>
    public static string DashboardUrl(string host, int port, string pin) => $"http://{host}:{port}/#pin={pin}";

    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly string _pin;
    private readonly int _port;
    private readonly Action<string>? _onLog;
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private readonly ConcurrentDictionary<IPAddress, (int Failures, DateTime LockedUntil)> _pinFailures = new();
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

    /// <summary>A 6-digit pairing PIN from a cryptographic RNG, new every launch.</summary>
    public static string GeneratePin() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

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

        _onLog?.Invoke($"[LOCAL] Server started on port {_port}.");
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
        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var (_, client) in _clients)
            client.Enqueue(bytes);
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

        var remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
        if (remote == null || !IsLocalNetwork(remote))
        {
            _onLog?.Invoke($"[LOCAL] Refused connection from non-local address {remote}.");
            return;
        }
        if (IsLockedOut(remote)) return;

        NetworkStream stream;
        try { stream = tcp.GetStream(); }
        catch { return; }

        // ── Step 1: Read HTTP upgrade request (bounded in size and time) ──────
        Dictionary<string, string> headers;
        try
        {
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshakeCts.CancelAfter(HandshakeTimeout);
            headers = await ReadHttpHeadersAsync(stream, handshakeCts.Token);
        }
        catch { return; }

        headers.TryGetValue("Sec-WebSocket-Key", out var wsKey);
        headers.TryGetValue("Upgrade", out var upgradeVal);
        bool isUpgrade = string.Equals(upgradeVal, "websocket", StringComparison.OrdinalIgnoreCase);

        if (!isUpgrade || wsKey == null)
        {
            // Plain HTTP: the phone dashboard page (LF-17) at "/", a health check at "/health".
            var path = headers.GetValueOrDefault(RequestPathKey, "/").Split('?')[0];
            var (status, contentType, body) = path switch
            {
                "/" or "/index.html" => ("200 OK", "text/html; charset=utf-8", PhoneDashboardHtml.Value),
                "/health" => ("200 OK", "application/json", "{\"service\":\"Turn One Link\",\"status\":\"online\"}"u8.ToArray()),
                _ => ("404 Not Found", "text/plain", "Not found"u8.ToArray()),
            };
            var resp = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            try { await stream.WriteAsync(resp, token); await stream.WriteAsync(body, token); } catch { }
            return;
        }

        // A web page on another site could otherwise open ws://<lan-ip>:port and guess PINs.
        if (headers.TryGetValue("Origin", out var origin) && !IsSameHostOrigin(origin, headers.GetValueOrDefault("Host")))
        {
            var forbidden = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            try { await stream.WriteAsync(forbidden, token); } catch { }
            _onLog?.Invoke($"[LOCAL] Refused browser connection from origin {origin}.");
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

            if (!PinMatches(receivedPin))
            {
                RecordPinFailure(remote);
                await SendJsonAsync(ws, new { type = "auth_failed" }, CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Wrong PIN", CancellationToken.None);
                _onLog?.Invoke($"[LOCAL] Client {clientId} ({remote}) failed PIN auth.");
                return;
            }

            _pinFailures.TryRemove(remote, out var _);
        }
        catch (OperationCanceledException)
        {
            _onLog?.Invoke($"[LOCAL] Client {clientId} auth timed out.");
            try { await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth timeout", CancellationToken.None); } catch { }
            return;
        }

        // ── Step 5: Register, send and receive ────────────────────────────────
        // Registered before auth_ok goes out, so frames broadcast right after pairing are queued, not lost.
        var client = new ConnectedClient(clientId, ws);
        _clients[clientId] = client;
        try { await SendJsonAsync(ws, new { type = "auth_ok", clientId }, token); }
        catch
        {
            RemoveClient(clientId);
            return;
        }
        _onLog?.Invoke($"[LOCAL] Client {clientId} authenticated. Total: {_clients.Count}");
        ClientCountChanged?.Invoke(_clients.Count);

        // Started only after auth_ok, so it remains the single sender from here on.
        var sendTask = client.RunSenderAsync(token);
        try { await ReceiveLoopAsync(clientId, client, token); }
        finally
        {
            RemoveClient(clientId);
            try { await sendTask; } catch { }
        }
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
            var pingMsg = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { type = "ping", at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

            foreach (var (id, client) in _clients)
            {
                if (now - client.LastPong > PongTimeout)
                {
                    _onLog?.Invoke($"[LOCAL] Client {id} timed out.");
                    RemoveClient(id);
                    continue;
                }
                // Through the client's queue like every other frame: one sender per socket.
                client.Enqueue(pingMsg);
            }
        }
    }

    // ── Security helpers ──────────────────────────────────────────────────────

    private bool PinMatches(string? candidate) =>
        candidate != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(_pin));

    private bool IsLockedOut(IPAddress address) =>
        _pinFailures.TryGetValue(address, out var f) && f.LockedUntil > DateTime.UtcNow;

    private void RecordPinFailure(IPAddress address)
    {
        _pinFailures.AddOrUpdate(address,
            _ => (1, DateTime.MinValue),
            (_, f) =>
            {
                var failures = f.LockedUntil > DateTime.MinValue && f.LockedUntil <= DateTime.UtcNow ? 1 : f.Failures + 1;
                return failures >= MaxFailedPins
                    ? (failures, DateTime.UtcNow + PinLockout)
                    : (failures, DateTime.MinValue);
            });
    }

    /// <summary>Loopback, RFC 1918, link-local and IPv6 unique-local addresses only.</summary>
    public static bool IsLocalNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal;
    }

    /// <summary>True when a browser's Origin is the server itself (a page Link serves), not another site.</summary>
    public static bool IsSameHostOrigin(string origin, string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)) return false;
        return string.Equals(o.Authority, host, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
    /// Stops at the blank line that terminates the header block; refuses anything over
    /// <see cref="MaxHandshakeBytes"/>.
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
            if (sb.Length > MaxHandshakeBytes)
                throw new InvalidOperationException("Handshake too large.");

            // Detect end of headers (\r\n\r\n)
            if (sb.Length >= 4 &&
                sb[^4] == '\r' && sb[^3] == '\n' &&
                sb[^2] == '\r' && sb[^1] == '\n')
                break;
        }

        // Request line ("GET /path HTTP/1.1") → path; then the header lines.
        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].Split(' ') is { Length: >= 2 } requestLine)
            headers[RequestPathKey] = requestLine[1];
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
        private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(ClientQueueLength)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

        public string     Id        { get; }
        public WebSocket  WebSocket { get; }
        public DateTime   LastPong  { get; set; } = DateTime.UtcNow;

        public ConnectedClient(string id, WebSocket ws) { Id = id; WebSocket = ws; }

        public void Enqueue(byte[] message) => _outbox.Writer.TryWrite(message);

        /// <summary>The only code that sends on this socket.</summary>
        public async Task RunSenderAsync(CancellationToken token)
        {
            try
            {
                await foreach (var message in _outbox.Reader.ReadAllAsync(token))
                {
                    if (WebSocket.State != WebSocketState.Open) break;
                    await WebSocket.SendAsync(message, WebSocketMessageType.Text, true, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _outbox.Writer.TryComplete();
            try { WebSocket.Abort(); } catch { }
            try { WebSocket.Dispose(); } catch { }
        }
    }
}
