namespace Turn_One_Link.Services.Telemetry;

/// <summary>Sim-neutral session type. Each telemetry source maps its own native enum onto this.</summary>
public enum T1SessionType
{
    Unknown,
    Practice,
    Qualify,
    Race,
    Hotlap,
    TimeAttack,
    Drift,
    Drag
}

/// <summary>Sim-neutral session/sim status.</summary>
public enum T1SimStatus
{
    Off,
    Replay,
    Live,
    Paused
}

public readonly record struct T1Corners(float Fl, float Fr, float Rl, float Rr);

public readonly record struct T1TyreSurface(float Inner, float Middle, float Outer);

public readonly record struct T1TyreSurfaceSet(T1TyreSurface Fl, T1TyreSurface Fr, T1TyreSurface Rl, T1TyreSurface Rr);

/// <summary>
/// Sim-neutral physics ingestion frame. Each ITelemetrySource adapter is responsible for
/// normalizing its own sim's raw conventions (gear zero-point, clutch direction, units, etc.)
/// into this fixed shape before raising <see cref="ITelemetrySource.PhysicsUpdated"/>.
/// </summary>
public readonly record struct T1PhysicsFrame(
    int PacketId,
    float Gas,
    float Brake,
    float Clutch,
    float Fuel,
    int Gear,          // normalized: 0=R, 1=N, 2=1st...
    int Rpm,
    float SpeedKmh,
    bool TcActive,
    bool AbsActive,
    T1Corners TyreCoreTemp,
    T1Corners TyrePressure,
    T1Corners BrakeTemp,
    T1TyreSurfaceSet? TyreSurfaceTemp);

/// <summary>Sim-neutral graphics/session-state ingestion frame.</summary>
public readonly record struct T1GraphicsFrame(
    int PacketId,
    T1SimStatus Status,
    T1SessionType SessionType,
    int SessionIndex,
    string? CurrentTimeText,
    string? LastTimeText,
    string? BestTimeText,
    int CompletedLaps,
    int Position,
    int CurrentLapMs,
    int LastLapMs,
    int BestLapMs,
    int NativeDeltaMs,
    float? FuelEstimatedLaps,
    float? FuelPerLapNative,
    float NormalizedCarPosition,
    bool IsValidLap,
    bool IsInPitLane);

/// <summary>Sim-neutral one-time session/static info ingestion frame.</summary>
public readonly record struct T1StaticFrame(
    string? Track,
    string? CarModel,
    string? PlayerName,
    string? PlayerSurname,
    int MaxRpm,
    float MaxFuel);

// ─────────────────────────────────────────────────────────────────────────────
// Cloud protocol v2 (docs/telemetry/backend_contract_v2.md): sim-neutral
// tick/lap_complete/session_start frames streamed to the backend. Distinct from
// the T1Physics/Graphics/Static frames above, which remain the local mobile
// (t1_protocol.md) shape and the v1 cloud fallback (PROTOCOL_V1=true).
// Reuses <see cref="T1Corners"/> above ([fl, fr, rl, rr] on the wire).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Sim-neutral v2 "tick" frame — one fixed-rate sample of live telemetry. Fields
/// follow the units/conventions in the v2 contract's tick table (gear: -1=R,
/// 0=N, 1..n; angles in radians unless suffixed "Deg"; g in g's). Optional
/// fields are null when the source sim doesn't expose them — never send 0 to
/// mean "unknown" for a nullable numeric field.
/// </summary>
public readonly record struct T1TickFrame(
    int Lap,
    int LapTimeMs,
    /// <summary>Metres from the S/F line, null when the sim gives no distance and the track length is unknown (the backend then integrates speed).</summary>
    float? LapDistM,
    int Sector,
    float Speed,
    int Rpm,
    int Gear,
    float Throttle,
    float Brake,
    float? Clutch,
    float Steer,
    float? SteerDeg,
    float GLat,
    float GLong,
    float? GVert,
    float? PosX,
    float? PosY,
    float? PosZ,
    float? Heading,
    bool? Valid,
    int Pit,
    int? DriverStatus,
    float? Fuel,
    int? Tc,
    int? Abs,
    float? BrakeBias,
    int? Drs,
    float? Ers,
    int? ErsMode,
    int? TyreCompound,
    int? TyreAgeLaps,
    T1Corners? TyreTemp,
    T1Corners? TyrePress,
    T1Corners? TyreWear,
    T1Corners? BrakeTemp,
    T1Corners? Slip,
    long? Frame);

/// <summary>Sim-neutral v2 "session_start" info — one-off, re-sent (upsert) whenever identity changes.</summary>
public sealed record T1SessionInfo(
    string Source,
    string? SimVersion,
    string TrackId,
    string TrackName,
    float TrackLengthM,
    int SectorCount,
    float[]? SectorBoundariesM,
    string CarId,
    string CarName,
    string? CarClass,
    T1SessionType SessionType,
    string? SessionTypeRaw,
    string Driver,
    int TickRateHz);

/// <summary>Sim-neutral v2 "lap_complete" — sent once per S/F crossing with sim-authoritative timing.</summary>
public readonly record struct T1LapComplete(
    int Lap,
    int LapTimeMs,
    int[] SectorsMs,
    bool? Valid,
    bool Pit,
    float LapDistM,
    float? FuelUsedL,
    int? TyreCompound);

/// <summary>Wire-name helpers shared by every v2 adapter, so `source`/`sessionType` strings
/// match the backend contract (docs/telemetry/backend_contract_v2.md) exactly.</summary>
public static class T1Wire
{
    public static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "acc" => "acc",
        "ac" => "ac",
        "iracing" => "iracing",
        "f1_25" => "f1_25",
        "f1_26" => "f1_26",
        _ => source.ToLowerInvariant()
    };

    public static string SessionTypeName(T1SessionType type) => type switch
    {
        T1SessionType.Practice => "practice",
        T1SessionType.Qualify => "qualifying",
        T1SessionType.Race => "race",
        T1SessionType.Hotlap => "hotlap",
        T1SessionType.TimeAttack => "timeattack",
        // Drift/Drag/Unknown have no direct v2 slot — fall back to "other".
        _ => "other"
    };
}
