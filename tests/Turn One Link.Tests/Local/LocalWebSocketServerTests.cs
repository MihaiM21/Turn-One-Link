using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Turn_One_Link.Services;
using Xunit;

namespace Turn_One_Link.Tests.Local;

/// <summary>L-P0-11: LAN server pairing, lockout, origin check and single-sender broadcast.</summary>
public class LocalWebSocketServerTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await ws.ReceiveAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task<(ClientWebSocket Ws, string Reply)> ConnectWithPinAsync(int port, string pin)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        (await ReceiveAsync(ws)).Should().Contain("auth_required");
        await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { pin })), WebSocketMessageType.Text, true, CancellationToken.None);
        return (ws, await ReceiveAsync(ws));
    }

    [Fact]
    public async Task Correct_Pin_Pairs_And_Receives_Broadcasts()
    {
        var port = FreePort();
        using var server = new LocalWebSocketServer(port, "123456");
        server.TryStart().Should().BeTrue();

        var (ws, reply) = await ConnectWithPinAsync(port, "123456");
        reply.Should().Contain("auth_ok");

        // Many broadcasts at once must not overlap sends on the socket (that used to drop the client).
        for (int i = 0; i < 200; i++) server.Broadcast($"{{\"type\":\"t1_telemetry\",\"n\":{i}}}");
        (await ReceiveAsync(ws)).Should().Contain("t1_telemetry");
        server.ClientCount.Should().Be(1);
        ws.Dispose();
    }

    [Fact]
    public async Task Five_Wrong_Pins_Lock_The_Address_Out()
    {
        var port = FreePort();
        using var server = new LocalWebSocketServer(port, "123456");
        server.TryStart();

        for (int i = 0; i < 5; i++)
        {
            var (ws, reply) = await ConnectWithPinAsync(port, "000000");
            reply.Should().Contain("auth_failed");
            ws.Dispose();
        }

        // Even the right PIN is refused while locked out: the connection is dropped before the handshake.
        var locked = new ClientWebSocket();
        var connect = () => locked.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connect.Should().ThrowAsync<WebSocketException>();
    }

    [Fact]
    public async Task Browser_Page_From_Another_Site_Is_Refused()
    {
        var port = FreePort();
        using var server = new LocalWebSocketServer(port, "123456");
        server.TryStart();

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://evil.example");
        var connect = () => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await connect.Should().ThrowAsync<WebSocketException>();
    }

    [Fact]
    public async Task Oversized_Handshake_Is_Dropped()
    {
        var port = FreePort();
        using var server = new LocalWebSocketServer(port, "123456");
        server.TryStart();

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var junk = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nX-Junk: " + new string('a', 20_000));
        try { await stream.WriteAsync(junk); } catch (IOException) { }

        var buffer = new byte[16];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int read;
        try { read = await stream.ReadAsync(buffer, cts.Token); }
        catch (IOException) { read = 0; }
        read.Should().Be(0, "the server closes the connection instead of buffering forever");
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("172.32.1.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void Only_Local_Network_Peers_Are_Served(string ip, bool expected)
    {
        LocalWebSocketServer.IsLocalNetwork(IPAddress.Parse(ip)).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://192.168.1.20:8080", "192.168.1.20:8080", true)]
    [InlineData("https://evil.example", "192.168.1.20:8080", false)]
    [InlineData("null", "192.168.1.20:8080", false)]
    public void Origin_Must_Be_The_Server_Itself(string origin, string host, bool expected)
    {
        LocalWebSocketServer.IsSameHostOrigin(origin, host).Should().Be(expected);
    }

    [Fact]
    public void Pins_Are_Six_Digits()
    {
        Enumerable.Range(0, 100).Select(_ => LocalWebSocketServer.GeneratePin())
            .Should().OnlyContain(p => p.Length == 6 && p.All(char.IsDigit));
    }
}
