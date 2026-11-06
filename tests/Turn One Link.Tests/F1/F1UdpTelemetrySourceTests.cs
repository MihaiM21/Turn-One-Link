using Xunit;
using FluentAssertions;
using Turn_One_Link.Services.F1;
using Turn_One_Link.Services.Telemetry;
using static Turn_One_Link.Services.F1.F1Packets;
using static Turn_One_Link.Tests.F1.F1PacketBuilder;

namespace Turn_One_Link.Tests.F1;

public class F1PacketsTests
{
    [Fact]
    public void Header_Reads_Format_Id_And_Player_Index()
    {
        var p = Packet(PacketId.CarTelemetry, sessionUid: 99, overallFrame: 555, playerCar: 7);
        TryReadHeader(p, out var h).Should().BeTrue();
        h.PacketFormat.Should().Be(2025);
        h.Id.Should().Be(PacketId.CarTelemetry);
        h.SessionUid.Should().Be(99);
        h.OverallFrameIdentifier.Should().Be(555);
        h.PlayerCarIndex.Should().Be(7);
        p.Length.Should().Be(1352);
    }

    [Fact]
    public void Expected_Sizes_Match_Spec()
    {
        ExpectedSize(PacketId.Motion).Should().Be(1349);
        ExpectedSize(PacketId.LapData).Should().Be(1285);
        ExpectedSize(PacketId.CarStatus).Should().Be(1239);
        ExpectedSize(PacketId.MotionEx).Should().Be(273);
        ExpectedSize(PacketId.SessionHistory).Should().Be(1460);
        // The per-car struct sizes must tile the packet exactly.
        (HeaderSize + MaxCars * CarMotionSize).Should().Be(1349);
        (HeaderSize + MaxCars * LapDataSize + 2).Should().Be(1285);
        (HeaderSize + MaxCars * CarTelemetrySize + 3).Should().Be(1352);
        (HeaderSize + MaxCars * CarStatusSize).Should().Be(1239);
        (HeaderSize + 7 + 100 * LapHistorySize + 24).Should().Be(1460);
    }

    [Fact]
    public void LapData_And_Telemetry_Round_Trip_For_The_Player_Car()
    {
        var lap = ReadLapData(LapData(car: 3, lapNum: 4, lapDistance: 1234.5f, currentLapMs: 45_678, sector: 1, invalid: 1, pit: 2), 3);
        lap.CurrentLapNum.Should().Be(4);
        lap.LapDistance.Should().Be(1234.5f);
        lap.CurrentLapTimeMs.Should().Be(45_678);
        lap.Sector.Should().Be(1);
        lap.CurrentLapInvalid.Should().Be(1);
        lap.PitStatus.Should().Be(2);
        lap.Sector1Ms.Should().Be(31_500);
        lap.Sector2Ms.Should().Be(62_100);

        var t = ReadCarTelemetry(CarTelemetry(car: 3, speed: 301, throttle: 0.75f, brake: 0.2f, steer: -0.3f, gear: -1, rpm: 12_345, drs: 1), 3);
        t.SpeedKmh.Should().Be(301);
        t.Throttle.Should().Be(0.75f);
        t.Brake.Should().Be(0.2f);
        t.Steer.Should().Be(-0.3f);
        t.Gear.Should().Be(-1);
        t.EngineRpm.Should().Be(12_345);
        t.Drs.Should().Be(1);
        t.BrakeTempFl.Should().Be(520, "the game orders wheels RL, RR, FL, FR");
        t.TyrePressureFr.Should().Be(23.6f);
    }

    [Fact]
    public void Session_History_Reads_Lap_Timing()
    {
        var p = SessionHistory(3, 2, new[] { (90_000u, 30_000, 31_000, 29_000, true), (91_234u, 30_100, 31_050, 30_084, false) });
        ReadSessionHistoryCarIndex(p).Should().Be(3);
        ReadSessionHistoryNumLaps(p).Should().Be(2);
        var l2 = ReadLapHistory(p, 1);
        l2.LapTimeMs.Should().Be(91_234);
        l2.Sector2Ms.Should().Be(31_050);
        l2.LapValid.Should().BeFalse();
        ReadLapHistory(p, 0).LapValid.Should().BeTrue();
    }

    [Fact]
    public void Track_And_Session_Type_Tables()
    {
        Track(10).id.Should().Be("spa");
        Track(-1).id.Should().Be("track_-1");
        MapSessionType(15).type.Should().Be(T1SessionType.Race);
        MapSessionType(18).type.Should().Be(T1SessionType.Hotlap);
        MapSessionType(6).raw.Should().Be("F1_Q6");
    }
}

public class F1UdpTelemetrySourceTests
{
    private static F1UdpTelemetrySource NewSource() => new();

    [Fact]
    public void Emits_Session_Info_And_Ticks_From_The_Player_Car()
    {
        var src = NewSource();
        var infos = new List<T1SessionInfo>();
        var ticks = new List<T1TickFrame>();
        src.SessionInfoUpdated += infos.Add;
        src.TickUpdated += ticks.Add;

        src.HandlePacket(Session(trackId: 11, trackLengthM: 5793, sessionType: 15));
        src.HandlePacket(CarStatus(3));
        src.HandlePacket(Motion(3, x: 10f, y: 2f, z: 30f, gLat: 2.5f, gLong: -1.2f, yaw: 0.7f));
        src.HandlePacket(LapData(3, lapNum: 2, lapDistance: 800f, currentLapMs: 12_000, sector: 0));
        src.HandlePacket(CarTelemetry(3, speed: 250, throttle: 1f, brake: 0f, steer: 0.1f, gear: 7, rpm: 11_000, drs: 1));

        src.SourceName.Should().Be("f1_25");
        infos.Should().ContainSingle();
        infos[0].TrackId.Should().Be("monza");
        infos[0].TrackLengthM.Should().Be(5793);
        infos[0].SessionType.Should().Be(T1SessionType.Race);
        infos[0].Source.Should().Be("f1_25");

        ticks.Should().ContainSingle();
        var t = ticks[0];
        t.Lap.Should().Be(2);
        t.LapDistM.Should().Be(800f);
        t.Speed.Should().Be(250);
        t.Gear.Should().Be(7);
        t.GLat.Should().Be(2.5f);
        t.GLong.Should().Be(-1.2f);
        t.PosX.Should().Be(10f);
        t.PosY.Should().Be(30f, "the game's Z is the ground-plane axis");
        t.PosZ.Should().Be(2f);
        t.Heading.Should().Be(0.7f);
        t.Drs.Should().Be(1);
        t.Fuel.Should().Be(55.5f);
        t.Ers.Should().Be(3_500_000f);
        t.TyreCompound.Should().Be(16);
        t.Valid.Should().BeTrue();
        t.TyreTemp!.Value.Fl.Should().Be(100);
        t.BrakeTemp!.Value.Rr.Should().Be(410);
    }

    [Fact]
    public void Ignores_Packets_From_Other_Cars_And_Bad_Sizes()
    {
        var src = NewSource();
        var ticks = new List<T1TickFrame>();
        src.TickUpdated += ticks.Add;

        src.HandlePacket(LapData(3, 1, 10f, 100));
        var wrongSize = CarTelemetry(3)[..1000];
        src.HandlePacket(wrongSize);
        ticks.Should().BeEmpty();

        src.HandlePacket(CarTelemetry(3));
        ticks.Should().ContainSingle();
    }

    [Fact]
    public void Lap_Complete_Comes_From_Session_History_After_The_Lap_Counter_Increments()
    {
        var src = NewSource();
        var completed = new List<T1LapComplete>();
        src.LapCompleted += completed.Add;

        src.HandlePacket(Session(trackId: 10, trackLengthM: 7004));
        src.HandlePacket(CarStatus(3, fuel: 60f));
        src.HandlePacket(LapData(3, lapNum: 3, lapDistance: 6990f, currentLapMs: 137_000));
        src.HandlePacket(CarStatus(3, fuel: 57.5f));
        src.HandlePacket(LapData(3, lapNum: 4, lapDistance: 12f, currentLapMs: 300));
        completed.Should().BeEmpty("timing is not known until the history packet arrives");

        // History for another car must be ignored; then the player's arrives with lap 3 published.
        src.HandlePacket(SessionHistory(9, 3, new[] { (1u, 1, 1, 1, true) }));
        src.HandlePacket(SessionHistory(3, 3, new[] { (0u, 0, 0, 0, true), (0u, 0, 0, 0, true), (138_421u, 45_210, 52_111, 41_100, true) }));

        completed.Should().ContainSingle();
        var c = completed[0];
        c.Lap.Should().Be(3);
        c.LapTimeMs.Should().Be(138_421);
        c.SectorsMs.Should().Equal(45_210, 52_111, 41_100);
        c.Valid.Should().BeTrue();
        c.LapDistM.Should().Be(7004f);
        c.FuelUsedL.Should().BeApproximately(2.5f, 0.01f);
        c.TyreCompound.Should().Be(16);
    }

    [Fact]
    public void Flashback_Invalidates_The_Current_Lap()
    {
        var src = NewSource();
        var ticks = new List<T1TickFrame>();
        src.TickUpdated += ticks.Add;
        src.HandlePacket(Session());
        src.HandlePacket(LapData(3, lapNum: 2, lapDistance: 3000f, currentLapMs: 60_000));
        src.HandlePacket(Event("FLBK"));
        src.HandlePacket(LapData(3, lapNum: 2, lapDistance: 2500f, currentLapMs: 50_000));
        src.HandlePacket(CarTelemetry(3));

        ticks.Should().ContainSingle();
        ticks[0].Valid.Should().BeFalse();
    }

    [Fact]
    public void New_Session_Uid_Re_Emits_Session_Info_And_Format_2026_Sets_Source()
    {
        var src = NewSource();
        var infos = new List<T1SessionInfo>();
        src.SessionInfoUpdated += infos.Add;

        src.HandlePacket(Session(uid: 1));
        src.HandlePacket(Session(uid: 1));
        infos.Should().ContainSingle("re-sends of the same identity are deduplicated");

        var p2026 = Session(uid: 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(p2026.AsSpan(0), Format2026);
        src.HandlePacket(p2026);
        infos.Should().HaveCount(2);
        src.SourceName.Should().Be("f1_26");
        infos[1].Source.Should().Be("f1_26");
    }
}
