using System;
using System.Collections.Generic;
using Turn_One_Link.Services.AccTelemetry;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

/// <summary>
/// Wraps <see cref="AccTelemetryService"/> and translates its ACC-specific shared-memory
/// structs into sim-neutral T1 frames — both the legacy v1 Physics/Graphics/Static shape
/// (local mobile protocol, t1_protocol.md, unchanged) and the cloud protocol v2
/// tick/lap_complete/session_start shape (docs/telemetry/backend_contract_v2.md).
/// <see cref="AccTelemetryService"/> itself is untouched so the proven ACC read path
/// carries zero risk from this adapter.
///
/// The same shared-memory layout family is used by original Assetto Corsa (AC), so this one
/// adapter also serves AC — <see cref="SourceName"/> is set at construction from the detected
/// game process (see <c>MainWindow.CreateSourceFor</c>) and only changes the v2 `source` wire
/// value and a couple of AC-specific validity fallbacks; the shared-memory read path is identical.
/// </summary>
public sealed class AccTelemetrySourceAdapter : ITelemetrySource
{
    private readonly AccTelemetryService _inner = new();
    private readonly string _sourceName;

    /// <summary>Exposes the raw ACC service so callers that still need ACC-shaped structs
    /// (e.g. cloud streaming's v1 wire format, behind PROTOCOL_V1) can subscribe directly.</summary>
    public AccTelemetryService Inner => _inner;

    public string SourceName => _sourceName;

    public event Action<T1PhysicsFrame>? PhysicsUpdated;
    public event Action<T1GraphicsFrame>? GraphicsUpdated;
    public event Action<T1StaticFrame>? StaticInfoUpdated;
    public event Action<Exception>? OnError;

    public event Action<T1TickFrame>? TickUpdated;
    public event Action<T1LapComplete>? LapCompleted;
    public event Action<T1SessionInfo>? SessionInfoUpdated;

    // ---- v2 state: latest raw structs, cached so tick-building can merge physics+graphics ----
    private SPageFilePhysics? _lastPhysics;
    private SPageFileGraphic? _lastGraphics;
    private SPageFileStatic? _lastStatic;
    private DateTime _lastTickEmittedAt = DateTime.MinValue;
    private string? _lastSessionInfoKey;

    // ---- v2 state: in-progress lap sector/validity/pit tracking ----
    private int _lastSectorIndex = -1;
    private int _lapAtSectorTrackingStart = -1;
    private readonly List<int> _sectorTimesBuffer = new();
    private bool _lapValidLatch = true;
    private bool _lapPitLatch;
    private float? _lapStartFuel;

    private const int AccNoTimeSentinel = 2147483647; // ACC's "no time recorded" value for iLastTime etc.

    public AccTelemetrySourceAdapter(string sourceName = "acc")
    {
        _sourceName = sourceName;

        _inner.PhysicsUpdated    += OnRawPhysics;
        _inner.GraphicsUpdated   += OnRawGraphics;
        _inner.StaticInfoUpdated += OnRawStatic;
        _inner.OnError           += ex => OnError?.Invoke(ex);
    }

    public void Start() => _inner.Start();
    public void Stop() => _inner.Stop();
    public void RequestStaticRefresh() => _inner.RequestStaticRefresh();
    public void Dispose() => _inner.Dispose();

    // ── Raw shared-memory handlers ──────────────────────────────────────────────

    private void OnRawPhysics(SPageFilePhysics p)
    {
        _lastPhysics = p;
        PhysicsUpdated?.Invoke(MapPhysics(p));
        // Tick building needs both physics and graphics; graphics carries lap/sector/position,
        // so v2 emission is driven from OnRawGraphics once both are available.
    }

    private void OnRawGraphics(SPageFileGraphic g)
    {
        _lastGraphics = g;
        GraphicsUpdated?.Invoke(MapGraphics(g));

        // Sector/lap tracking runs on every graphics frame (ACC delivers these at ~100Hz)
        // independent of the 20Hz tick rate limiter below, so a sector transition is never
        // missed just because we dropped a tick for rate limiting.
        TrackSectorsAndLaps(g);

        TryEmitSessionInfo();
        TryEmitTick(g);
    }

    private void OnRawStatic(SPageFileStatic s)
    {
        _lastStatic = s;
        StaticInfoUpdated?.Invoke(MapStatic(s));
        TryEmitSessionInfo();
    }

    // ── v2: tick emission (rate-limited to STREAM_HZ, default 20, hard max 60) ─────────────

    private void TryEmitTick(SPageFileGraphic g)
    {
        if (_lastPhysics is not { } p) return;

        var now = DateTime.UtcNow;
        if (now - _lastTickEmittedAt < TelemetryEnv.TickInterval) return;
        _lastTickEmittedAt = now;

        TickUpdated?.Invoke(BuildTick(p, g));
    }

    private T1TickFrame BuildTick(SPageFilePhysics p, SPageFileGraphic g)
    {
        float trackLengthM = LookupTrackLength();
        var playerCoords = SafeCarCoordinates(g);

        return new T1TickFrame(
            Lap: g.CompletedLaps + 1,
            LapTimeMs: g.iCurrentTime,
            // Unknown track length -> omit rather than send 0 (the backend integrates speed instead).
            LapDistM: trackLengthM > 0 ? g.NormalizedCarPosition * trackLengthM : null,
            Sector: g.CurrentSectorIndex,
            Speed: p.SpeedKmh,
            Rpm: p.Rpms,
            // ACC raw: 0=R, 1=N, 2=1st... -> v2: -1=R, 0=N, 1..n (no clamping, unlike the v1 mapping).
            Gear: p.Gear - 1,
            Throttle: p.Gas,
            Brake: p.Brake,
            Clutch: 1f - p.Clutch,
            Steer: p.SteerAngle,
            // ACC shared memory has no wheel-lock angle to convert steer to degrees at the wheel.
            SteerDeg: null,
            GLat: p.AccG is { Length: > 0 } ? p.AccG[0] : 0f,
            GLong: p.AccG is { Length: > 2 } ? p.AccG[2] : 0f,
            GVert: p.AccG is { Length: > 1 } ? p.AccG[1] : 0f,
            PosX: playerCoords?.X,
            PosY: playerCoords?.Z,
            PosZ: playerCoords?.Y,
            Heading: p.Heading,
            Valid: g.IsValidLap != 0,
            Pit: g.IsInPit != 0 ? 2 : (g.IsInPitLane != 0 ? 1 : 0),
            // ACC doesn't expose a flying/out-lap/in-lap/garage enum directly.
            DriverStatus: null,
            Fuel: p.Fuel,
            Tc: g.Tc,
            Abs: g.Abs,
            // ACC's BrakeBias is a 0..1 fraction to front.
            BrakeBias: p.BrakeBias * 100f,
            // DRS/ERS/tyre-compound-id/tyre-age are F1-only per the v2 cheatsheet; ACC/AC have
            // no equivalent numeric ids (ACC's tyre compound is a display string, not an id).
            Drs: null,
            Ers: null,
            ErsMode: null,
            TyreCompound: null,
            TyreAgeLaps: null,
            TyreTemp: Corners(p.TyreCoreTemperature),
            TyrePress: Corners(p.WheelsPressure),
            TyreWear: Corners(p.TyreWear),
            BrakeTemp: Corners(p.BrakeTemp),
            Slip: Corners(p.WheelSlip),
            Frame: p.PacketId);
    }

    // ── v2: lap_complete (sector accumulation + validity/pit latching) ─────────────────────

    private void TrackSectorsAndLaps(SPageFileGraphic g)
    {
        // AC lacks isValidLap; fall back to "4+ wheels off track this lap" as a latch, per
        // backend_contract_v2.md guidance for sims without a native validity flag.
        bool tickInvalid = _sourceName == "ac"
            ? (_lastPhysics?.NumberOfTyresOut ?? 0) >= 4
            : g.IsValidLap == 0;
        if (tickInvalid) _lapValidLatch = false;
        if (g.IsInPit != 0 || g.IsInPitLane != 0) _lapPitLatch = true;

        if (_lastSectorIndex < 0)
        {
            // First graphics frame seen — initialize tracking without emitting anything.
            _lastSectorIndex = g.CurrentSectorIndex;
            _lapAtSectorTrackingStart = g.CompletedLaps;
            _lapStartFuel = _lastPhysics?.Fuel;
            return;
        }

        if (g.CurrentSectorIndex == _lastSectorIndex) return;

        // Sector boundary crossed — ACC's LastSectorTime is the time of the sector that just ended.
        _sectorTimesBuffer.Add(g.LastSectorTime);
        _lastSectorIndex = g.CurrentSectorIndex;

        bool lapJustCompleted = g.CompletedLaps > _lapAtSectorTrackingStart;
        if (!lapJustCompleted) return;

        EmitLapComplete(g, completedLapNumber: g.CompletedLaps);

        // Reset for the new in-progress lap.
        _sectorTimesBuffer.Clear();
        _lapValidLatch = true;
        _lapPitLatch = false;
        _lapAtSectorTrackingStart = g.CompletedLaps;
        _lapStartFuel = _lastPhysics?.Fuel;
    }

    private void EmitLapComplete(SPageFileGraphic g, int completedLapNumber)
    {
        if (g.iLastTime == AccNoTimeSentinel) return; // ACC has no timing for this lap; skip.

        float? fuelUsed = null;
        if (_lapStartFuel is { } startFuel && _lastPhysics is { } p)
        {
            var used = startFuel - p.Fuel;
            if (used >= 0) fuelUsed = used;
        }

        LapCompleted?.Invoke(new T1LapComplete(
            Lap: completedLapNumber,
            LapTimeMs: g.iLastTime,
            SectorsMs: _sectorTimesBuffer.ToArray(),
            Valid: _lapValidLatch,
            Pit: _lapPitLatch,
            LapDistM: LookupTrackLength(),
            FuelUsedL: fuelUsed,
            TyreCompound: null));
    }

    // ── v2: session_start ────────────────────────────────────────────────────────────────

    private void TryEmitSessionInfo()
    {
        if (_lastGraphics is not { } g || _lastStatic is not { } s) return;

        // ACC reports ids like "Paul_Ricard"; lower-case so the (source, trackId) profile key is stable.
        var trackId = (s.Track ?? "").Trim().ToLowerInvariant();
        var carId = s.CarModel ?? "";
        var sessionType = MapSessionType(g.Session);
        var key = $"{_sourceName}|{trackId}|{carId}|{sessionType}";
        if (key == _lastSessionInfoKey) return;
        _lastSessionInfoKey = key;

        SessionInfoUpdated?.Invoke(new T1SessionInfo(
            Source: T1Wire.SourceName(_sourceName),
            SimVersion: string.IsNullOrWhiteSpace(s.SMVersion) ? null : s.SMVersion,
            TrackId: trackId,
            TrackName: PrettifyTrackName(trackId),
            TrackLengthM: LookupTrackLength(),
            SectorCount: s.SectorCount > 0 ? s.SectorCount : 3,
            SectorBoundariesM: null, // ACC/AC don't expose sector boundary distances natively.
            CarId: carId,
            CarName: carId,
            CarClass: null, // ACC/AC shared memory has no car-class string.
            SessionType: sessionType,
            SessionTypeRaw: g.Session.ToString(),
            Driver: $"{s.PlayerName} {s.PlayerSurname}".Trim(),
            TickRateHz: TelemetryEnv.StreamHz));
    }

    private float LookupTrackLength()
    {
        // AC exposes a real track length; ACC's is usually 0 (known SDK gap), so fall back
        // to the static lookup table, then let the backend self-calibrate if still unknown.
        if (_lastStatic is { TrackSPlineLength: > 0 } s) return s.TrackSPlineLength;
        return AccTrackLengths.Lookup(_lastStatic?.Track);
    }

    private static Coordinates? SafeCarCoordinates(SPageFileGraphic g)
    {
        if (g.CarCoordinates is not { } coords) return null;
        if (g.PlayerCarID < 0 || g.PlayerCarID >= coords.Length) return null;
        return coords[g.PlayerCarID];
    }

    private static string PrettifyTrackName(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return trackId;
        return trackId switch
        {
            "cota" => "Circuit of the Americas",
            "nurburgring_24h" => "Nürburgring Nordschleife 24h",
            "nurburgring" => "Nürburgring",
            "mount_panorama" => "Mount Panorama (Bathurst)",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo
                .ToTitleCase(trackId.Replace('_', ' '))
        };
    }

    // ── v1 mapping (unchanged) ───────────────────────────────────────────────────────────

    private static T1PhysicsFrame MapPhysics(SPageFilePhysics p)
    {
        // ACC raw Gear convention normalized to T1's 0=R,1=N,2=1st...
        // (verbatim from the previous inline logic in T1TelemetryMapper.BuildFrame)
        int gear = p.Gear - 1;
        if (gear < 0) gear = 0;

        return new T1PhysicsFrame(
            PacketId: p.PacketId,
            Gas: p.Gas,
            Brake: p.Brake,
            Clutch: p.Clutch,
            Fuel: p.Fuel,
            Gear: gear,
            Rpm: p.Rpms,
            SpeedKmh: p.SpeedKmh,
            TcActive: p.TC > 0,
            AbsActive: p.Abs > 0,
            TyreCoreTemp: Corners(p.TyreCoreTemperature),
            TyrePressure: Corners(p.WheelsPressure),
            BrakeTemp: Corners(p.BrakeTemp),
            TyreSurfaceTemp: SurfaceSet(p));
    }

    private static T1GraphicsFrame MapGraphics(SPageFileGraphic g) => new(
        PacketId: g.PacketId,
        Status: MapStatus(g.Status),
        SessionType: MapSessionType(g.Session),
        SessionIndex: g.SessionIndex,
        CurrentTimeText: g.CurrentTime,
        LastTimeText: g.LastTime,
        BestTimeText: g.BestTime,
        CompletedLaps: g.CompletedLaps,
        Position: g.Position,
        CurrentLapMs: g.iCurrentTime,
        LastLapMs: g.iLastTime,
        BestLapMs: g.iBestTime,
        NativeDeltaMs: g.IDeltaLapTime,
        FuelEstimatedLaps: g.FuelEstimatedLaps,
        FuelPerLapNative: g.FuelXLap > 0 ? g.FuelXLap : null,
        NormalizedCarPosition: g.NormalizedCarPosition,
        IsValidLap: g.IsValidLap != 0,
        IsInPitLane: g.IsInPitLane != 0);

    private static T1StaticFrame MapStatic(SPageFileStatic s) => new(
        Track: s.Track,
        CarModel: s.CarModel,
        PlayerName: s.PlayerName,
        PlayerSurname: s.PlayerSurname,
        MaxRpm: s.MaxRpm,
        MaxFuel: s.MaxFuel);

    private static T1Corners Corners(float[]? values) => new(
        Fl: values is { Length: > 0 } ? values[0] : 0,
        Fr: values is { Length: > 1 } ? values[1] : 0,
        Rl: values is { Length: > 2 } ? values[2] : 0,
        Rr: values is { Length: > 3 } ? values[3] : 0);

    private static T1TyreSurfaceSet? SurfaceSet(SPageFilePhysics p)
    {
        if (p.TyreTempI is not { Length: >= 4 } ||
            p.TyreTempM is not { Length: >= 4 } ||
            p.TyreTempO is not { Length: >= 4 })
            return null;

        return new T1TyreSurfaceSet(
            Fl: new Telemetry.T1TyreSurface(p.TyreTempI[0], p.TyreTempM[0], p.TyreTempO[0]),
            Fr: new Telemetry.T1TyreSurface(p.TyreTempI[1], p.TyreTempM[1], p.TyreTempO[1]),
            Rl: new Telemetry.T1TyreSurface(p.TyreTempI[2], p.TyreTempM[2], p.TyreTempO[2]),
            Rr: new Telemetry.T1TyreSurface(p.TyreTempI[3], p.TyreTempM[3], p.TyreTempO[3]));
    }

    private static T1SimStatus MapStatus(AC_STATUS status) => status switch
    {
        AC_STATUS.AC_OFF => T1SimStatus.Off,
        AC_STATUS.AC_REPLAY => T1SimStatus.Replay,
        AC_STATUS.AC_LIVE => T1SimStatus.Live,
        AC_STATUS.AC_PAUSE => T1SimStatus.Paused,
        _ => T1SimStatus.Off
    };

    private static T1SessionType MapSessionType(AC_SESSION_TYPE type) => type switch
    {
        AC_SESSION_TYPE.AC_PRACTICE => T1SessionType.Practice,
        AC_SESSION_TYPE.AC_QUALIFY => T1SessionType.Qualify,
        AC_SESSION_TYPE.AC_RACE => T1SessionType.Race,
        AC_SESSION_TYPE.AC_HOTLAP => T1SessionType.Hotlap,
        AC_SESSION_TYPE.AC_TIME_ATTACK => T1SessionType.TimeAttack,
        AC_SESSION_TYPE.AC_DRIFT => T1SessionType.Drift,
        AC_SESSION_TYPE.AC_DRAG => T1SessionType.Drag,
        _ => T1SessionType.Unknown
    };
}
