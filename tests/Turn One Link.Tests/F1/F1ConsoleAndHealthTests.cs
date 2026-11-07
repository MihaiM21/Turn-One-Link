using FluentAssertions;
using Turn_One_Link.Services;
using Turn_One_Link.Services.F1;
using Xunit;

namespace Turn_One_Link.Tests.F1;

/// <summary>L-P1-03 driver status mapping, LF-15 console mode and LF-16 health hints.</summary>
public class F1ConsoleAndHealthTests
{
    [Theory]
    [InlineData(0, 3)] // garage
    [InlineData(1, 0)] // flying lap
    [InlineData(2, 2)] // in lap
    [InlineData(3, 1)] // out lap
    [InlineData(4, 0)] // on track, not timed → flying
    public void F1_Driver_Status_Is_Mapped_To_The_Contract(byte f1, int expected) =>
        F1UdpTelemetrySource.MapDriverStatus(f1).Should().Be(expected);

    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(2);

    [Fact]
    public void No_Hint_While_Data_Flows_Or_During_The_Grace_Period()
    {
        TelemetryHealth.Hint("Assetto Corsa Competizione", Long, Short, 20777, "192.168.1.20").Should().BeNull();
        TelemetryHealth.Hint("F1 25", Short, null, 20777, "192.168.1.20").Should().BeNull();
        TelemetryHealth.Hint(null, Long, null, 20777, "192.168.1.20").Should().BeNull();
    }

    [Fact]
    public void Console_Hint_Tells_Which_Ip_And_Port_To_Enter()
    {
        var hint = TelemetryHealth.Hint(ConnectionService.F1ConsoleGameName, Long, null, 20777, "192.168.1.20");
        hint.Should().Contain("192.168.1.20").And.Contain("20777").And.Contain("UDP Telemetry On");
    }

    [Theory]
    [InlineData("F1 25", "UDP Telemetry On")]
    [InlineData("F1 24", "aren't supported")]
    [InlineData("iRacing", "in the car")]
    [InlineData("RaceRoom", "isn't supported")]
    public void Each_Game_Gets_Its_Own_Hint(string game, string expected) =>
        TelemetryHealth.Hint(game, Long, Long, 20777, "10.0.0.2").Should().Contain(expected);

    [Fact]
    public void Busy_Udp_Port_Is_Reported_Not_Thrown()
    {
        using var blocker = new System.Net.Sockets.UdpClient(0);
        var port = ((System.Net.IPEndPoint)blocker.Client.LocalEndPoint!).Port;
        var messages = new List<string>();
        using var source = new F1UdpTelemetrySource(messages.Add, port);
        Exception? error = null;
        source.OnError += e => error = e;

        var start = () => source.Start();

        start.Should().NotThrow();
        error.Should().NotBeNull();
        messages.Should().Contain(m => m.Contains($"port {port}"));
    }
}
