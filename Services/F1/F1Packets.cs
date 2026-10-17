using System;
using System.Buffers.Binary;

namespace Turn_One_Link.Services.F1;

/// <summary>
/// Offset-based readers for the EA SPORTS F1 25 / F1 26 UDP packets we consume. All values are
/// little-endian and packed with no padding, so every field is read by absolute byte offset from
/// the spec rather than through a struct — that keeps the parser tolerant of packet growth (new
/// fields are appended) and lets us assert on packet size per format instead of silently misreading.
/// Spec: "Data Output from F1 25" v3 (EA forums) — sizes below are for packetFormat 2025.
/// </summary>
public static class F1Packets
{
    public const int HeaderSize = 29;
    public const int MaxCars = 22;

    public const ushort Format2025 = 2025;
    public const ushort Format2026 = 2026;

    public enum PacketId : byte
    {
        Motion = 0,
        Session = 1,
        LapData = 2,
        Event = 3,
        Participants = 4,
        CarSetups = 5,
        CarTelemetry = 6,
        CarStatus = 7,
        FinalClassification = 8,
        LobbyInfo = 9,
        CarDamage = 10,
        SessionHistory = 11,
        TyreSets = 12,
        MotionEx = 13,
        TimeTrial = 14,
        LapPositions = 15,
    }

    /// <summary>Expected total packet sizes for packetFormat 2025. 2026 sizes are checked against these at runtime and logged when they differ.</summary>
    public static readonly (PacketId id, int size)[] Sizes2025 =
    {
        (PacketId.Motion, 1349), (PacketId.Session, 753), (PacketId.LapData, 1285), (PacketId.Event, 45),
        (PacketId.Participants, 1284), (PacketId.CarSetups, 1133), (PacketId.CarTelemetry, 1352), (PacketId.CarStatus, 1239),
        (PacketId.FinalClassification, 1042), (PacketId.LobbyInfo, 954), (PacketId.CarDamage, 1041), (PacketId.SessionHistory, 1460),
        (PacketId.TyreSets, 231), (PacketId.MotionEx, 273), (PacketId.TimeTrial, 101), (PacketId.LapPositions, 1131),
    };

    public static int ExpectedSize(PacketId id)
    {
        foreach (var (pid, size) in Sizes2025) if (pid == id) return size;
        return -1;
    }

    // ---------------------------------------------------------------- header

    public readonly record struct Header(
        ushort PacketFormat, byte GameYear, byte MajorVersion, byte MinorVersion, byte PacketVersion,
        PacketId Id, ulong SessionUid, float SessionTime, uint FrameIdentifier, uint OverallFrameIdentifier,
        byte PlayerCarIndex, byte SecondaryPlayerCarIndex);

    public static bool TryReadHeader(ReadOnlySpan<byte> p, out Header h)
    {
        h = default;
        if (p.Length < HeaderSize) return false;
        h = new Header(
            PacketFormat: U16(p, 0),
            GameYear: p[2],
            MajorVersion: p[3],
            MinorVersion: p[4],
            PacketVersion: p[5],
            Id: (PacketId)p[6],
            SessionUid: U64(p, 7),
            SessionTime: F32(p, 15),
            FrameIdentifier: U32(p, 19),
            OverallFrameIdentifier: U32(p, 23),
            PlayerCarIndex: p[27],
            SecondaryPlayerCarIndex: p[28]);
        return true;
    }

    // ---------------------------------------------------------------- motion (0) — 22 × 60 B

    public const int CarMotionSize = 60;

    public readonly record struct CarMotion(
        float WorldX, float WorldY, float WorldZ,
        float VelX, float VelY, float VelZ,
        float GLat, float GLong, float GVert,
        float Yaw, float Pitch, float Roll);

    public static CarMotion ReadCarMotion(ReadOnlySpan<byte> p, int carIndex)
    {
        var o = HeaderSize + carIndex * CarMotionSize;
        return new CarMotion(
            WorldX: F32(p, o + 0), WorldY: F32(p, o + 4), WorldZ: F32(p, o + 8),
            VelX: F32(p, o + 12), VelY: F32(p, o + 16), VelZ: F32(p, o + 20),
            // +24..+35: forward/right direction int16 ×6 (skipped)
            GLat: F32(p, o + 36), GLong: F32(p, o + 40), GVert: F32(p, o + 44),
            Yaw: F32(p, o + 48), Pitch: F32(p, o + 52), Roll: F32(p, o + 56));
    }

    // ---------------------------------------------------------------- session (1) — first fields only

    public readonly record struct F1Session(
        byte Weather, sbyte TrackTemperature, sbyte AirTemperature, byte TotalLaps, ushort TrackLengthM,
        byte SessionType, sbyte TrackId, byte Formula, ushort SessionTimeLeft, ushort SessionDuration);

    public static F1Session ReadSession(ReadOnlySpan<byte> p)
    {
        var o = HeaderSize;
        return new F1Session(
            Weather: p[o + 0], TrackTemperature: (sbyte)p[o + 1], AirTemperature: (sbyte)p[o + 2], TotalLaps: p[o + 3],
            TrackLengthM: U16(p, o + 4), SessionType: p[o + 6], TrackId: (sbyte)p[o + 7], Formula: p[o + 8],
            SessionTimeLeft: U16(p, o + 9), SessionDuration: U16(p, o + 11));
    }

    // ---------------------------------------------------------------- lap data (2) — 22 × 57 B

    public const int LapDataSize = 57;

    public readonly record struct LapData(
        uint LastLapTimeMs, uint CurrentLapTimeMs,
        ushort Sector1MsPart, byte Sector1Minutes, ushort Sector2MsPart, byte Sector2Minutes,
        float LapDistance, float TotalDistance,
        byte CarPosition, byte CurrentLapNum, byte PitStatus, byte NumPitStops, byte Sector,
        byte CurrentLapInvalid, byte Penalties, byte DriverStatus, byte ResultStatus)
    {
        public int Sector1Ms => Sector1Minutes * 60_000 + Sector1MsPart;
        public int Sector2Ms => Sector2Minutes * 60_000 + Sector2MsPart;
    }

    public static LapData ReadLapData(ReadOnlySpan<byte> p, int carIndex)
    {
        var o = HeaderSize + carIndex * LapDataSize;
        return new LapData(
            LastLapTimeMs: U32(p, o + 0),
            CurrentLapTimeMs: U32(p, o + 4),
            Sector1MsPart: U16(p, o + 8), Sector1Minutes: p[o + 10],
            Sector2MsPart: U16(p, o + 11), Sector2Minutes: p[o + 13],
            // +14..+19: delta to car in front / race leader (skipped)
            LapDistance: F32(p, o + 20),
            TotalDistance: F32(p, o + 24),
            // +28: safety car delta
            CarPosition: p[o + 32],
            CurrentLapNum: p[o + 33],
            PitStatus: p[o + 34],
            NumPitStops: p[o + 35],
            Sector: p[o + 36],
            CurrentLapInvalid: p[o + 37],
            Penalties: p[o + 38],
            // +39 totalWarnings, +40 cornerCuttingWarnings, +41 numUnservedDriveThroughPens, +42 numUnservedStopGoPens, +43 gridPosition
            DriverStatus: p[o + 44],
            ResultStatus: p[o + 45]);
    }

    // ---------------------------------------------------------------- event (3)

    public static string ReadEventCode(ReadOnlySpan<byte> p) =>
        p.Length >= HeaderSize + 4 ? System.Text.Encoding.ASCII.GetString(p.Slice(HeaderSize, 4)) : "";

    /// <summary>FLBK payload: the frame the game rewound to.</summary>
    public static (uint frameIdentifier, float sessionTime) ReadFlashback(ReadOnlySpan<byte> p) =>
        (U32(p, HeaderSize + 4), F32(p, HeaderSize + 8));

    // ---------------------------------------------------------------- car telemetry (6) — 22 × 60 B

    public const int CarTelemetrySize = 60;

    public readonly record struct CarTelemetry(
        ushort SpeedKmh, float Throttle, float Steer, float Brake, byte Clutch, sbyte Gear, ushort EngineRpm, byte Drs,
        ushort BrakeTempRl, ushort BrakeTempRr, ushort BrakeTempFl, ushort BrakeTempFr,
        byte TyreSurfaceRl, byte TyreSurfaceRr, byte TyreSurfaceFl, byte TyreSurfaceFr,
        byte TyreInnerRl, byte TyreInnerRr, byte TyreInnerFl, byte TyreInnerFr,
        ushort EngineTemperature,
        float TyrePressureRl, float TyrePressureRr, float TyrePressureFl, float TyrePressureFr,
        byte SurfaceRl, byte SurfaceRr, byte SurfaceFl, byte SurfaceFr);

    /// <summary>NOTE: the game's wheel arrays are ordered [RL, RR, FL, FR]; the T1 protocol wants [FL, FR, RL, RR].</summary>
    public static CarTelemetry ReadCarTelemetry(ReadOnlySpan<byte> p, int carIndex)
    {
        var o = HeaderSize + carIndex * CarTelemetrySize;
        return new CarTelemetry(
            SpeedKmh: U16(p, o + 0),
            Throttle: F32(p, o + 2),
            Steer: F32(p, o + 6),
            Brake: F32(p, o + 10),
            Clutch: p[o + 14],
            Gear: (sbyte)p[o + 15],
            EngineRpm: U16(p, o + 16),
            Drs: p[o + 18],
            // +19 revLightsPercent, +20..21 revLightsBitValue
            BrakeTempRl: U16(p, o + 22), BrakeTempRr: U16(p, o + 24), BrakeTempFl: U16(p, o + 26), BrakeTempFr: U16(p, o + 28),
            TyreSurfaceRl: p[o + 30], TyreSurfaceRr: p[o + 31], TyreSurfaceFl: p[o + 32], TyreSurfaceFr: p[o + 33],
            TyreInnerRl: p[o + 34], TyreInnerRr: p[o + 35], TyreInnerFl: p[o + 36], TyreInnerFr: p[o + 37],
            EngineTemperature: U16(p, o + 38),
            TyrePressureRl: F32(p, o + 40), TyrePressureRr: F32(p, o + 44), TyrePressureFl: F32(p, o + 48), TyrePressureFr: F32(p, o + 52),
            SurfaceRl: p[o + 56], SurfaceRr: p[o + 57], SurfaceFl: p[o + 58], SurfaceFr: p[o + 59]);
    }

    // ---------------------------------------------------------------- car status (7) — 22 × 55 B

    public const int CarStatusSize = 55;

    public readonly record struct CarStatus(
        byte TractionControl, byte AntiLockBrakes, byte FuelMix, byte FrontBrakeBias, byte PitLimiter,
        float FuelInTank, float FuelCapacity, float FuelRemainingLaps, ushort MaxRpm, ushort IdleRpm, byte MaxGears,
        byte DrsAllowed, ushort DrsActivationDistance, byte ActualTyreCompound, byte VisualTyreCompound, byte TyresAgeLaps,
        float ErsStoreEnergy, byte ErsDeployMode);

    public static CarStatus ReadCarStatus(ReadOnlySpan<byte> p, int carIndex)
    {
        var o = HeaderSize + carIndex * CarStatusSize;
        return new CarStatus(
            TractionControl: p[o + 0], AntiLockBrakes: p[o + 1], FuelMix: p[o + 2], FrontBrakeBias: p[o + 3], PitLimiter: p[o + 4],
            FuelInTank: F32(p, o + 5), FuelCapacity: F32(p, o + 9), FuelRemainingLaps: F32(p, o + 13),
            MaxRpm: U16(p, o + 17), IdleRpm: U16(p, o + 19), MaxGears: p[o + 21],
            DrsAllowed: p[o + 22], DrsActivationDistance: U16(p, o + 23),
            ActualTyreCompound: p[o + 25], VisualTyreCompound: p[o + 26], TyresAgeLaps: p[o + 27],
            // +28 vehicleFiaFlags, +29 enginePowerICE, +33 enginePowerMGUK
            ErsStoreEnergy: F32(p, o + 37),
            ErsDeployMode: p[o + 41]);
    }

    // ---------------------------------------------------------------- motion ex (13) — player car only

    /// <summary>Wheel slip ratio [RL, RR, FL, FR] — body offset 64 (after suspension pos/vel/acc and wheel speed).</summary>
    public static (float rl, float rr, float fl, float fr) ReadWheelSlipRatio(ReadOnlySpan<byte> p)
    {
        var o = HeaderSize + 64;
        return (F32(p, o), F32(p, o + 4), F32(p, o + 8), F32(p, o + 12));
    }

    // ---------------------------------------------------------------- session history (11)

    public const int LapHistorySize = 14;
    public const int MaxLapHistory = 100;

    public readonly record struct LapHistory(uint LapTimeMs, int Sector1Ms, int Sector2Ms, int Sector3Ms, byte ValidBitFlags)
    {
        /// <summary>Bit 0 = lap valid, bits 1–3 = sectors 1–3 valid.</summary>
        public bool LapValid => (ValidBitFlags & 0x01) != 0;
    }

    public static byte ReadSessionHistoryCarIndex(ReadOnlySpan<byte> p) => p[HeaderSize];
    public static byte ReadSessionHistoryNumLaps(ReadOnlySpan<byte> p) => p[HeaderSize + 1];

    /// <param name="lapIndex">0-based (lap number − 1).</param>
    public static LapHistory ReadLapHistory(ReadOnlySpan<byte> p, int lapIndex)
    {
        var o = HeaderSize + 7 + lapIndex * LapHistorySize;
        return new LapHistory(
            LapTimeMs: U32(p, o),
            Sector1Ms: p[o + 6] * 60_000 + U16(p, o + 4),
            Sector2Ms: p[o + 9] * 60_000 + U16(p, o + 7),
            Sector3Ms: p[o + 12] * 60_000 + U16(p, o + 10),
            ValidBitFlags: p[o + 13]);
    }

    // ---------------------------------------------------------------- tables

    /// <summary>Track id → (slug, display name). Unknown ids fall back to <c>track_{id}</c>.</summary>
    public static (string id, string name) Track(sbyte trackId) => trackId switch
    {
        0 => ("melbourne", "Melbourne"),
        1 => ("paul_ricard", "Paul Ricard"),
        2 => ("shanghai", "Shanghai"),
        3 => ("sakhir", "Bahrain (Sakhir)"),
        4 => ("catalunya", "Barcelona-Catalunya"),
        5 => ("monaco", "Monaco"),
        6 => ("montreal", "Montreal"),
        7 => ("silverstone", "Silverstone"),
        8 => ("hockenheim", "Hockenheim"),
        9 => ("hungaroring", "Hungaroring"),
        10 => ("spa", "Spa-Francorchamps"),
        11 => ("monza", "Monza"),
        12 => ("singapore", "Singapore"),
        13 => ("suzuka", "Suzuka"),
        14 => ("abu_dhabi", "Abu Dhabi"),
        15 => ("texas", "Austin (COTA)"),
        16 => ("brazil", "Interlagos"),
        17 => ("austria", "Red Bull Ring"),
        18 => ("sochi", "Sochi"),
        19 => ("mexico", "Mexico City"),
        20 => ("baku", "Baku"),
        21 => ("sakhir_short", "Bahrain Short"),
        22 => ("silverstone_short", "Silverstone Short"),
        23 => ("texas_short", "Austin Short"),
        24 => ("suzuka_short", "Suzuka Short"),
        25 => ("hanoi", "Hanoi"),
        26 => ("zandvoort", "Zandvoort"),
        27 => ("imola", "Imola"),
        28 => ("portimao", "Portimão"),
        29 => ("jeddah", "Jeddah"),
        30 => ("miami", "Miami"),
        31 => ("las_vegas", "Las Vegas"),
        32 => ("losail", "Lusail"),
        33 => ("silverstone_reverse", "Silverstone Reverse"),
        34 => ("austria_reverse", "Red Bull Ring Reverse"),
        35 => ("zandvoort_reverse", "Zandvoort Reverse"),
        _ => ($"track_{trackId}", $"Track {trackId}")
    };

    public static (Telemetry.T1SessionType type, string raw) MapSessionType(byte code) => code switch
    {
        1 or 2 or 3 or 4 => (Telemetry.T1SessionType.Practice, $"F1_P{code}"),
        5 or 6 or 7 or 8 or 9 => (Telemetry.T1SessionType.Qualify, $"F1_Q{code}"),
        10 or 11 or 12 or 13 or 14 => (Telemetry.T1SessionType.Qualify, $"F1_SPRINT_SHOOTOUT_{code}"),
        15 or 16 or 17 => (Telemetry.T1SessionType.Race, $"F1_RACE_{code}"),
        18 => (Telemetry.T1SessionType.Hotlap, "F1_TIME_TRIAL"),
        _ => (Telemetry.T1SessionType.Unknown, $"F1_SESSION_{code}")
    };

    // ---------------------------------------------------------------- primitives

    private static ushort U16(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(o, 2));
    private static uint U32(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(o, 4));
    private static ulong U64(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(o, 8));
    private static float F32(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadSingleLittleEndian(p.Slice(o, 4));
}
