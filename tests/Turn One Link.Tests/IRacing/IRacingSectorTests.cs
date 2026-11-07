using FluentAssertions;
using Turn_One_Link.Services;
using Xunit;

namespace Turn_One_Link.Tests.IRacing;

/// <summary>L-P1-06: iRacing sector times always add up to the lap time, or aren't sent.</summary>
public class IRacingSectorTests
{
    [Fact]
    public void Last_Sector_Is_Lap_Time_Minus_The_Others()
    {
        IRacingTelemetryService.FinalizeSectors(new[] { 30_100, 31_000, 29_850 }, 90_960)
            .Should().Equal(30_100, 31_000, 29_860);
    }

    [Theory]
    [InlineData(new[] { 30_000, 31_000 }, 90_000)]          // a sector was missed
    [InlineData(new[] { 30_000, 31_000, 29_000, 1 }, 90_000)]
    [InlineData(new[] { 30_000, 31_000, 10_000 }, 90_000)]  // measured last sector far off: a reset mid-lap
    [InlineData(new[] { 30_000, 31_000, 29_000 }, 0)]       // untimed lap
    [InlineData(new[] { 60_000, 40_000, 29_000 }, 90_000)]  // would make the last sector negative
    public void Inconsistent_Sectors_Are_Dropped(int[] raw, int lapTimeMs)
    {
        IRacingTelemetryService.FinalizeSectors(raw, lapTimeMs).Should().BeEmpty();
    }

    [Fact]
    public void No_Sectors_Without_Measurements()
    {
        IRacingTelemetryService.FinalizeSectors(null, 90_000).Should().BeEmpty();
    }
}
