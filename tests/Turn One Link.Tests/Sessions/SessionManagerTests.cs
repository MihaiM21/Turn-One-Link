using FluentAssertions;
using Turn_One_Link.Services;
using Turn_One_Link.Services.Telemetry;
using Xunit;

namespace Turn_One_Link.Tests.Sessions;

/// <summary>L-P0-07 / L-P0-08: one session_start and one session_end per session.</summary>
public class SessionManagerTests
{
    private static T1GraphicsFrame Graphics(T1SimStatus status, int sessionIndex = 0, int completedLaps = 0) => new(
        PacketId: 1, Status: status, SessionType: T1SessionType.Practice, SessionIndex: sessionIndex,
        CurrentTimeText: null, LastTimeText: null, BestTimeText: null, CompletedLaps: completedLaps,
        Position: 1, CurrentLapMs: 0, LastLapMs: 0, BestLapMs: 0, NativeDeltaMs: 0,
        FuelEstimatedLaps: null, FuelPerLapNative: null, NormalizedCarPosition: 0,
        IsValidLap: true, IsInPitLane: false);

    private static readonly T1StaticFrame Static = new("paul_ricard", "ferrari_296_gt3", "Test", "Driver", 8000, 120);

    [Fact]
    public void Session_Start_Fires_Once_When_Static_Data_Is_Already_Cached()
    {
        var sm = new SessionManager();
        var starts = 0;
        sm.SessionStarted += _ => starts++;
        sm.StaticRefreshRequested += () => sm.OnStatic(Static); // the adapter re-reads static on request

        sm.OnStatic(Static);
        sm.OnGraphics(Graphics(T1SimStatus.Live));
        sm.OnStatic(Static);
        sm.OnGraphics(Graphics(T1SimStatus.Live));

        starts.Should().Be(1);
    }

    [Fact]
    public void Session_Start_Waits_For_Static_Data_When_None_Is_Cached()
    {
        var sm = new SessionManager();
        var starts = new List<SessionInfo>();
        sm.SessionStarted += starts.Add;

        sm.OnGraphics(Graphics(T1SimStatus.Live));
        starts.Should().BeEmpty();

        sm.OnStatic(Static);
        sm.OnStatic(Static);

        starts.Should().ContainSingle().Which.Track.Should().Be("paul_ricard");
    }

    [Fact]
    public void Disconnect_Ends_The_Open_Session_Once()
    {
        var sm = new SessionManager();
        var ends = new List<SessionEndInfo>();
        sm.SessionEnded += ends.Add;

        sm.OnStatic(Static);
        sm.OnGraphics(Graphics(T1SimStatus.Live, completedLaps: 3));
        sm.OnSimDisconnected();
        sm.OnSimDisconnected();

        ends.Should().ContainSingle();
        sm.ActiveSessionId.Should().BeNull();
    }

    [Fact]
    public void New_Session_Index_Ends_The_Old_Session_And_Starts_A_New_One()
    {
        var sm = new SessionManager();
        var ids = new List<string>();
        var ends = 0;
        sm.SessionStarted += s => ids.Add(s.SessionId);
        sm.SessionEnded += _ => ends++;

        sm.OnStatic(Static);
        sm.OnGraphics(Graphics(T1SimStatus.Live, sessionIndex: 0));
        sm.OnGraphics(Graphics(T1SimStatus.Live, sessionIndex: 1));

        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        ends.Should().Be(1);
    }
}
