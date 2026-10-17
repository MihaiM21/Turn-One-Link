using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

/// <summary>
/// The T1 Protocol — a unified, game-agnostic telemetry frame.
/// All mobile clients receive this format regardless of the source sim.
/// </summary>
public sealed record T1Frame(
    string Type,
    long Timestamp,
    string? SessionId,
    string Source,
    T1Data Data);

public sealed record T1Data(
    T1Speed Speed,
    T1Engine Engine,
    T1Lap Lap,
    T1Fuel Fuel,
    T1Tyres Tyres,
    T1Session Session,
    T1Delta? Delta = null);

public sealed record T1Speed(
    float Kmh,
    float Mph);

public sealed record T1Engine(
    int Rpm,
    int MaxRpm,
    int Gear,         // 0 = R, 1 = N, 2 = 1st …
    float Throttle,   // 0.0 – 1.0
    float Brake,      // 0.0 – 1.0
    float Clutch,     // 0.0 – 1.0
    bool Tc,          // Traction control active
    bool Abs);        // ABS active

public sealed record T1Lap(
    string Current,
    int CurrentMs,
    string Best,
    int BestMs,
    int DeltaMs,
    int Number,
    int Position);

public sealed record T1Fuel(
    float Liters,
    float EstimatedLaps,             // sim-native estimate
    float? PerLapNative = null,      // sim-native fuel/lap
    float? LastLapLiters = null,     // computed: last clean lap
    float? AvgPerLapLiters = null,   // computed: rolling avg over recent clean laps
    float? EstimatedStintLaps = null,// computed: liters / avg
    float? TankCapacity = null);

public sealed record T1Tyres(
    T1TyreCorners Temps,             // core temps °C
    T1TyreCorners Pressures,
    T1TyreCorners BrakeTemps,
    T1TyreSurfaces? SurfaceTemps = null);

public sealed record T1TyreCorners(
    float Fl,
    float Fr,
    float Rl,
    float Rr);

public sealed record T1TyreSurface(
    float Inner,
    float Middle,
    float Outer);

public sealed record T1TyreSurfaces(
    T1TyreSurface Fl,
    T1TyreSurface Fr,
    T1TyreSurface Rl,
    T1TyreSurface Rr);

public sealed record T1Delta(
    int Ms,           // vs session best; positive = slower
    float Seconds,
    string Source,    // "trace" (computed) | "native" (sim-provided)
    bool HasBestTrace);

public sealed record T1Session(
    string Type,
    string Track,
    string Car,
    string Driver,
    string Status);

/// <summary>
/// Maps raw ACC telemetry structs into unified T1Frames.
/// When future games are added, only this class needs a new mapping method.
/// </summary>
public static class T1TelemetryMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Cached last-known values so physics/graphics frames can be merged
    // even when only one type arrives at a time.
    private static T1PhysicsFrame _lastPhysics;
    private static T1GraphicsFrame _lastGraphics;
    private static T1StaticFrame _lastStatic;
    private static bool _hasPhysics;
    private static bool _hasGraphics;
    private static bool _hasStatic;

    public static void UpdatePhysics(T1PhysicsFrame p)
    {
        _lastPhysics = p;
        _hasPhysics = true;
    }

    public static void UpdateGraphics(T1GraphicsFrame g)
    {
        _lastGraphics = g;
        _hasGraphics = true;
    }

    public static void UpdateStatic(T1StaticFrame s)
    {
        _lastStatic = s;
        _hasStatic = true;
    }

    // Pro metrics computed outside the raw sim structs (DeltaEngine / FuelManager).
    private static DeltaSnapshot? _lastDelta;
    private static FuelSnapshot? _lastFuel;

    public static void UpdateProMetrics(DeltaSnapshot? delta, FuelSnapshot? fuel)
    {
        _lastDelta = delta;
        _lastFuel = fuel;
    }

    /// <summary>
    /// Builds a T1Frame from the most recent cached telemetry data for the given source
    /// (e.g. "ACC", "iRacing"). Returns null if not enough data has been received yet.
    /// </summary>
    public static T1Frame? BuildFrame(string? sessionId, string source)
    {
        if (!_hasPhysics || !_hasGraphics) return null;

        var p = _lastPhysics;
        var g = _lastGraphics;
        var s = _hasStatic ? _lastStatic : default;

        return new T1Frame(
            Type: "t1_telemetry",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SessionId: sessionId,
            Source: source,
            Data: new T1Data(
                Speed: new T1Speed(
                    Kmh: p.SpeedKmh,
                    Mph: p.SpeedKmh * 0.621371f),
                Engine: new T1Engine(
                    Rpm: p.Rpm,
                    MaxRpm: _hasStatic ? s.MaxRpm : 9000,
                    Gear: p.Gear,
                    Throttle: p.Gas,
                    Brake: p.Brake,
                    Clutch: p.Clutch,
                    Tc: p.TcActive,
                    Abs: p.AbsActive),
                Lap: new T1Lap(
                    Current: FormatLapTime(g.CurrentLapMs, g.CurrentTimeText),
                    CurrentMs: g.CurrentLapMs,
                    Best: FormatLapTime(g.BestLapMs, g.BestTimeText),
                    BestMs: g.BestLapMs,
                    DeltaMs: g.NativeDeltaMs,
                    Number: g.CompletedLaps + 1,
                    Position: g.Position),
                Fuel: new T1Fuel(
                    Liters: p.Fuel,
                    EstimatedLaps: g.FuelEstimatedLaps ?? 0,
                    PerLapNative: g.FuelPerLapNative,
                    LastLapLiters: _lastFuel?.LastLapLiters,
                    AvgPerLapLiters: _lastFuel?.AvgPerLapLiters,
                    EstimatedStintLaps: _lastFuel?.EstimatedStintLaps,
                    TankCapacity: _hasStatic && s.MaxFuel > 0 ? s.MaxFuel : null),
                Tyres: new T1Tyres(
                    Temps: new T1TyreCorners(p.TyreCoreTemp.Fl, p.TyreCoreTemp.Fr, p.TyreCoreTemp.Rl, p.TyreCoreTemp.Rr),
                    Pressures: new T1TyreCorners(p.TyrePressure.Fl, p.TyrePressure.Fr, p.TyrePressure.Rl, p.TyrePressure.Rr),
                    BrakeTemps: new T1TyreCorners(p.BrakeTemp.Fl, p.BrakeTemp.Fr, p.BrakeTemp.Rl, p.BrakeTemp.Rr),
                    SurfaceTemps: BuildSurfaceTemps(p.TyreSurfaceTemp)),
                Session: new T1Session(
                    Type: SessionTypeWireName(g.SessionType),
                    Track: s.Track ?? "Unknown",
                    Car: s.CarModel ?? "Unknown",
                    Driver: $"{s.PlayerName} {s.PlayerSurname}".Trim(),
                    Status: SimStatusWireName(g.Status)),
                Delta: _lastDelta is { } d
                    ? new T1Delta(d.DeltaMs, d.DeltaSeconds, d.Source, d.HasBestTrace)
                    : null));
    }

    private static T1TyreSurfaces? BuildSurfaceTemps(T1TyreSurfaceSet? set)
    {
        if (set is not { } surf) return null;

        return new T1TyreSurfaces(
            Fl: new T1TyreSurface(surf.Fl.Inner, surf.Fl.Middle, surf.Fl.Outer),
            Fr: new T1TyreSurface(surf.Fr.Inner, surf.Fr.Middle, surf.Fr.Outer),
            Rl: new T1TyreSurface(surf.Rl.Inner, surf.Rl.Middle, surf.Rl.Outer),
            Rr: new T1TyreSurface(surf.Rr.Inner, surf.Rr.Middle, surf.Rr.Outer));
    }

    /// <summary>Formats a lap time from milliseconds when the source doesn't provide
    /// a pre-formatted string (e.g. iRacing gives raw float seconds, not text).</summary>
    private static string FormatLapTime(int ms, string? nativeText)
    {
        // Only null (not empty) triggers the ms-based fallback, preserving ACC's
        // exact prior behavior of `g.CurrentTime ?? "--:--.---"`.
        if (nativeText != null) return nativeText;
        if (ms <= 0) return "--:--.---";
        var span = TimeSpan.FromMilliseconds(ms);
        return $"{(int)span.TotalMinutes}:{span.Seconds:D2}.{span.Milliseconds:D3}";
    }

    // Wire-format labels are kept as the original ACC enum names for backward
    // compatibility with the T1 protocol (see docs/telemetry/t1_protocol.md) and
    // the cloud backend contract — reused across sources so the local/mobile
    // protocol vocabulary doesn't fork per-sim; the "source" field on T1Frame
    // already discriminates ACC vs. iRacing for anything that needs it.
    public static string SessionTypeWireName(T1SessionType type) => type switch
    {
        T1SessionType.Practice => "AC_PRACTICE",
        T1SessionType.Qualify => "AC_QUALIFY",
        T1SessionType.Race => "AC_RACE",
        T1SessionType.Hotlap => "AC_HOTLAP",
        T1SessionType.TimeAttack => "AC_TIME_ATTACK",
        T1SessionType.Drift => "AC_DRIFT",
        T1SessionType.Drag => "AC_DRAG",
        _ => "AC_UNKNOWN"
    };

    private static string SimStatusWireName(T1SimStatus status) => status switch
    {
        T1SimStatus.Off => "AC_OFF",
        T1SimStatus.Replay => "AC_REPLAY",
        T1SimStatus.Live => "AC_LIVE",
        T1SimStatus.Paused => "AC_PAUSE",
        _ => "AC_OFF"
    };

    public static string Serialize(T1Frame frame)
        => JsonSerializer.Serialize(frame, JsonOptions);
}
