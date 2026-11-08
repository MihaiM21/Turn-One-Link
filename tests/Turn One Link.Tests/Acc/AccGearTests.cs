using FluentAssertions;
using Turn_One_Link.Services;
using Turn_One_Link.Services.AccTelemetry;
using Xunit;

namespace Turn_One_Link.Tests.Acc;

/// <summary>
/// ACC's shared memory reports Gear as 0 = R, 1 = N, 2 = 1st — the same convention the HUD, the phone
/// dash and the local stream use, so the physics frame must pass it through unchanged.
/// </summary>
public class AccGearTests
{
    [Theory]
    [InlineData(0, 0)] // reverse
    [InlineData(1, 1)] // neutral
    [InlineData(2, 2)] // 1st
    [InlineData(4, 4)] // 3rd
    [InlineData(-1, 0)] // garbage clamps to reverse
    public void Physics_Frame_Keeps_Accs_Gear(int raw, int expected)
    {
        var frame = AccTelemetrySourceAdapter.MapPhysics(new SPageFilePhysics { Gear = raw });
        frame.Gear.Should().Be(expected);
    }
}
