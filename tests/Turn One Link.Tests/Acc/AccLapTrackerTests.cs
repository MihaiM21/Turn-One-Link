using System.IO;
using FluentAssertions;
using Turn_One_Link.Services;
using Xunit;

namespace Turn_One_Link.Tests.Acc;

/// <summary>L-P0-05: ACC lap timing — laps cut on completedLaps / lap clock, not on sector changes.</summary>
public class AccLapTrackerTests
{
    private const int NoTime = AccLapTracker.NoTime;

    private static IEnumerable<AccLapSample> LoadReplay(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split(',');
            yield return new AccLapSample(
                CompletedLaps: int.Parse(f[0]),
                CurrentLapMs: int.Parse(f[1]),
                CurrentSectorIndex: int.Parse(f[2]),
                FrameInvalid: f[3] == "0",
                InPit: f[4] == "1",
                LastLapMs: int.Parse(f[5]),
                Fuel: null);
        }
    }

    private static List<(int Index, AccCompletedLap Lap)> Run(IEnumerable<AccLapSample> frames)
    {
        var tracker = new AccLapTracker();
        var laps = new List<(int, AccCompletedLap)>();
        int i = 0;
        foreach (var frame in frames)
        {
            if (tracker.Update(frame) is { } lap) laps.Add((i, lap));
            i++;
        }
        return laps;
    }

    [Fact]
    public void Replay_Paul_Ricard_Emits_Each_Lap_On_The_Line_With_Consistent_Sectors()
    {
        var frames = LoadReplay("acc_paul_ricard_2026-09-19.csv").ToList();
        var laps = Run(frames);

        laps.Select(l => l.Lap.Lap).Should().Equal(1, 2, 3, 4);
        laps.Select(l => l.Lap.LapTimeMs).Should().Equal(126397, 126220, 126310, 125612);

        foreach (var (index, lap) in laps)
        {
            // Emitted on the frame the lap counter moved, not ~35 s later at the next sector change.
            frames[index].CompletedLaps.Should().Be(lap.Lap);
            frames[index - 1].CompletedLaps.Should().Be(lap.Lap - 1);

            lap.SectorsMs.Should().HaveCount(3);
            lap.SectorsMs.Sum().Should().Be(lap.LapTimeMs);
            lap.SectorsMs.Should().OnlyContain(s => s > 30_000 && s < 60_000);
        }

        // ACC flagged track limits ~80–89 s into laps 1–3 of the recording; lap 4 was clean.
        // The pit out-lap is not lap 1, so no lap carries the pit flag.
        laps.Select(l => l.Lap.Valid).Should().Equal(false, false, false, true);
        laps.Should().OnlyContain(l => !l.Lap.Pit);
    }

    private static AccLapSample F(int completed, int sector, int currentMs, int lastMs = NoTime, bool invalid = false, bool pit = false)
        => new(completed, sector, currentMs, lastMs, invalid, pit, Fuel: null);

    [Fact]
    public void Sector_Wrap_A_Frame_Before_The_Counter_Is_Not_A_Split()
    {
        var t = new AccLapTracker();
        t.Update(F(0, 2, 90_000));          // joined in sector 3 of an untimed lap
        t.Update(F(0, 0, 100));             // clock restarts: lap 1 starts
        t.Update(F(0, 1, 30_000));
        t.Update(F(0, 2, 61_000));
        t.Update(F(0, 0, 90_500));          // ACC quirk: sector wraps before completedLaps moves
        var lap = t.Update(F(1, 0, 20, lastMs: 90_400));

        lap.Should().NotBeNull();
        lap!.Value.LapTimeMs.Should().Be(90_400);
        lap.Value.SectorsMs.Should().Equal(30_000, 31_000, 29_400);
        lap.Value.Valid.Should().BeTrue();
    }

    [Fact]
    public void Lap_Time_That_Arrives_A_Few_Frames_Late_Is_Waited_For()
    {
        var t = new AccLapTracker();
        t.Update(F(0, 2, 80_000));
        t.Update(F(0, 0, 50));
        t.Update(F(0, 1, 30_000));
        t.Update(F(0, 2, 60_000));
        t.Update(F(1, 0, 20, lastMs: NoTime)).Should().BeNull();
        t.Update(F(1, 0, 40, lastMs: NoTime)).Should().BeNull();
        var lap = t.Update(F(1, 0, 60, lastMs: 90_000));

        lap!.Value.LapTimeMs.Should().Be(90_000);
        lap.Value.SectorsMs.Should().Equal(30_000, 30_000, 30_000);
    }

    [Fact]
    public void Lap_Joined_Midway_Is_Reported_Without_Sectors_And_Not_Valid()
    {
        var t = new AccLapTracker();
        t.Update(F(3, 1, 40_000, lastMs: 91_000));
        t.Update(F(3, 2, 61_000, lastMs: 91_000));
        var lap = t.Update(F(4, 0, 10, lastMs: 90_800));

        lap!.Value.Lap.Should().Be(4);
        lap.Value.SectorsMs.Should().BeEmpty();
        lap.Value.Valid.Should().BeFalse();
    }

    [Fact]
    public void Track_Limits_Anywhere_In_The_Lap_Invalidate_It_But_Not_The_Next()
    {
        var t = new AccLapTracker();
        t.Update(F(0, 0, 50));
        t.Update(F(0, 1, 30_000, invalid: true));
        t.Update(F(0, 2, 60_000));
        var first = t.Update(F(1, 0, 20, lastMs: 90_000, invalid: true)); // flag still set on the crossing frame
        t.Update(F(1, 1, 30_000));
        t.Update(F(1, 2, 60_000));
        var second = t.Update(F(2, 0, 20, lastMs: 89_000));

        first!.Value.Valid.Should().BeFalse();
        second!.Value.Valid.Should().BeTrue();
    }

    [Fact]
    public void Session_Restart_Drops_The_Lap_In_Progress()
    {
        var t = new AccLapTracker();
        t.Update(F(2, 0, 50, lastMs: 90_000));
        t.Update(F(2, 1, 30_000, lastMs: 90_000));
        t.Update(F(0, 0, 0)).Should().BeNull();    // restarted
        t.Update(F(0, 1, 30_000)).Should().BeNull();
        t.Update(F(0, 2, 60_000)).Should().BeNull();
        var lap = t.Update(F(1, 0, 20, lastMs: 91_000));

        lap!.Value.Lap.Should().Be(1);
        lap.Value.LapTimeMs.Should().Be(91_000);
    }
}
