using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Turn_One_Link.Services.AccTelemetry;

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
    T1Session Session);

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
    float EstimatedLaps);

public sealed record T1Tyres(
    T1TyreCorners Temps,
    T1TyreCorners Pressures,
    T1TyreCorners BrakeTemps);

public sealed record T1TyreCorners(
    float Fl,
    float Fr,
    float Rl,
    float Rr);

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
    private static SPageFilePhysics _lastPhysics;
    private static SPageFileGraphic _lastGraphics;
    private static SPageFileStatic _lastStatic;
    private static bool _hasPhysics;
    private static bool _hasGraphics;
    private static bool _hasStatic;

    public static void UpdatePhysics(SPageFilePhysics p)
    {
        _lastPhysics = p;
        _hasPhysics = true;
    }

    public static void UpdateGraphics(SPageFileGraphic g)
    {
        _lastGraphics = g;
        _hasGraphics = true;
    }

    public static void UpdateStatic(SPageFileStatic s)
    {
        _lastStatic = s;
        _hasStatic = true;
    }

    /// <summary>
    /// Builds a T1Frame from the most recent cached ACC data.
    /// Returns null if not enough data has been received yet.
    /// </summary>
    public static T1Frame? BuildFrame(string? sessionId)
    {
        if (!_hasPhysics || !_hasGraphics) return null;

        var p = _lastPhysics;
        var g = _lastGraphics;
        var s = _hasStatic ? _lastStatic : default;

        var gear = p.Gear - 1; // ACC: 0=R, 1=N, 2=1st… → T1: 0=R, 1=N, 2=1st
        if (gear < 0) gear = 0;

        return new T1Frame(
            Type: "t1_telemetry",
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SessionId: sessionId,
            Source: "ACC",
            Data: new T1Data(
                Speed: new T1Speed(
                    Kmh: p.SpeedKmh,
                    Mph: p.SpeedKmh * 0.621371f),
                Engine: new T1Engine(
                    Rpm: p.Rpms,
                    MaxRpm: _hasStatic ? s.MaxRpm : 9000,
                    Gear: gear,
                    Throttle: p.Gas,
                    Brake: p.Brake,
                    Clutch: p.Clutch,
                    Tc: p.TC > 0,
                    Abs: p.Abs > 0),
                Lap: new T1Lap(
                    Current: g.CurrentTime ?? "--:--.---",
                    CurrentMs: g.iCurrentTime,
                    Best: g.BestTime ?? "--:--.---",
                    BestMs: g.iBestTime,
                    DeltaMs: g.IDeltaLapTime,
                    Number: g.CompletedLaps + 1,
                    Position: g.Position),
                Fuel: new T1Fuel(
                    Liters: p.Fuel,
                    EstimatedLaps: g.FuelEstimatedLaps),
                Tyres: new T1Tyres(
                    Temps: new T1TyreCorners(
                        Fl: p.TyreCoreTemperature.Length > 0 ? p.TyreCoreTemperature[0] : 0,
                        Fr: p.TyreCoreTemperature.Length > 1 ? p.TyreCoreTemperature[1] : 0,
                        Rl: p.TyreCoreTemperature.Length > 2 ? p.TyreCoreTemperature[2] : 0,
                        Rr: p.TyreCoreTemperature.Length > 3 ? p.TyreCoreTemperature[3] : 0),
                    Pressures: new T1TyreCorners(
                        Fl: p.WheelsPressure.Length > 0 ? p.WheelsPressure[0] : 0,
                        Fr: p.WheelsPressure.Length > 1 ? p.WheelsPressure[1] : 0,
                        Rl: p.WheelsPressure.Length > 2 ? p.WheelsPressure[2] : 0,
                        Rr: p.WheelsPressure.Length > 3 ? p.WheelsPressure[3] : 0),
                    BrakeTemps: new T1TyreCorners(
                        Fl: p.BrakeTemp.Length > 0 ? p.BrakeTemp[0] : 0,
                        Fr: p.BrakeTemp.Length > 1 ? p.BrakeTemp[1] : 0,
                        Rl: p.BrakeTemp.Length > 2 ? p.BrakeTemp[2] : 0,
                        Rr: p.BrakeTemp.Length > 3 ? p.BrakeTemp[3] : 0)),
                Session: new T1Session(
                    Type: g.Session.ToString(),
                    Track: s.Track ?? "Unknown",
                    Car: s.CarModel ?? "Unknown",
                    Driver: $"{s.PlayerName} {s.PlayerSurname}".Trim(),
                    Status: g.Status.ToString())));
    }

    public static string Serialize(T1Frame frame)
        => JsonSerializer.Serialize(frame, JsonOptions);
}
