using System;
using System.Linq;
using IRSDKSharper;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

/// <summary>
/// Wraps IRSDKSharper's <see cref="IRacingSdk"/> and translates iRacing's named telemetry
/// variables and YAML session info into sim-neutral T1 frames — both the legacy v1
/// Physics/Graphics/Static shape (local mobile protocol, unchanged) and the cloud protocol v2
/// tick/lap_complete/session_start shape (docs/telemetry/backend_contract_v2.md).
///
/// A few mappings are flagged below as needing live confirmation against a running iRacing
/// session (traction-control-active variable name, exact SessionState-to-status mapping,
/// lap-validity proxy) since iRacing's variable set can vary slightly per car/car class.
/// </summary>
public sealed class IRacingTelemetryService : ITelemetrySource
{
    private readonly IRacingSdk _sdk = new();

    public string SourceName => "iracing";

    public event Action<T1PhysicsFrame>? PhysicsUpdated;
    public event Action<T1GraphicsFrame>? GraphicsUpdated;
    public event Action<T1StaticFrame>? StaticInfoUpdated;
    public event Action<Exception>? OnError;

    public event Action<T1TickFrame>? TickUpdated;
    public event Action<T1LapComplete>? LapCompleted;
    public event Action<T1SessionInfo>? SessionInfoUpdated;

    private T1SessionType _lastSessionType = T1SessionType.Unknown;
    private bool _staticRefreshRequested = true;

    // ---- v2 state: tick rate limiting + position origin ----
    private DateTime _lastTickEmittedAt = DateTime.MinValue;
    private double? _originLatDeg;
    private double? _originLonDeg;
    private float? _originAltM;

    // ---- v2 state: session identity + geometry cached from the YAML session info ----
    private string? _lastSessionInfoKey;
    private float _cachedTrackLengthM;
    private float[] _cachedSectorStartPct = Array.Empty<float>(); // ascending, index 0 = 0.0

    // ---- v2 state: in-progress lap sector/pit tracking + pending lap_complete ----
    private bool _lapTrackingInitialized;
    private int _lastLapSeen;
    private int _lastSectorIndex;
    private DateTime _lastSectorTransitionAt;
    private readonly System.Collections.Generic.List<int> _sectorTimesBuffer = new();
    private bool _lapPitLatch;
    private float? _lapStartFuel;

    private int? _pendingLapNumber;
    private int[]? _pendingSectorsMs;
    private bool _pendingPit;
    private float _pendingBaselineLapLastLapTime;
    private int _pendingTicksWaited;
    private float? _pendingFuelStart;
    private const int MaxPendingLapTicks = 10;

    public IRacingTelemetryService()
    {
        _sdk.OnTelemetryData += OnTelemetryData;
        _sdk.OnSessionInfo   += OnSessionInfo;
        _sdk.OnException     += ex => OnError?.Invoke(ex);
    }

    public void Start()
    {
        try { _sdk.Start(); }
        catch (Exception ex) { OnError?.Invoke(ex); } // never throw synchronously, matches AccTelemetryService
    }

    public void Stop()
    {
        try { _sdk.Stop(); } catch { }
    }

    public void RequestStaticRefresh() => _staticRefreshRequested = true;

    public void Dispose() => Stop();

    private void OnTelemetryData()
    {
        try
        {
            var d = _sdk.Data;

            PhysicsUpdated?.Invoke(new T1PhysicsFrame(
                PacketId: d.TickCount,
                Gas: d.GetFloat("Throttle"),
                Brake: d.GetFloat("Brake"),
                // iRacing: 1.0 = fully released, 0.0 = fully depressed — inverted vs ACC's 0=released.
                Clutch: 1f - d.GetFloat("Clutch"),
                Fuel: d.GetFloat("FuelLevel"),
                Gear: NormalizeGear(d.GetInt("Gear")),
                Rpm: (int)d.GetFloat("RPM"),
                // iRacing gives m/s, T1 wants km/h.
                SpeedKmh: d.GetFloat("Speed") * 3.6f,
                // TODO(no-live-iracing): confirm the exact traction-control-active variable
                // name against a live session (iRacing does not expose a single universal
                // "TC active" bool the way ACC does; varies somewhat per car).
                TcActive: false,
                AbsActive: HasVar(d, "BrakeABSactive") && d.GetBool("BrakeABSactive"),
                TyreCoreTemp: new T1Corners(
                    Fl: d.GetFloat("LFtempCM"), Fr: d.GetFloat("RFtempCM"),
                    Rl: d.GetFloat("LRtempCM"), Rr: d.GetFloat("RRtempCM")),
                TyrePressure: new T1Corners(
                    Fl: d.GetFloat("LFpress"), Fr: d.GetFloat("RFpress"),
                    Rl: d.GetFloat("LRpress"), Rr: d.GetFloat("RRpress")),
                // iRacing's base telemetry doesn't expose brake disc temperature (only
                // brake pressure) — permanent sim-dependent gap, documented in t1_protocol.md.
                BrakeTemp: default,
                TyreSurfaceTemp: new T1TyreSurfaceSet(
                    Fl: new Telemetry.T1TyreSurface(d.GetFloat("LFtempCL"), d.GetFloat("LFtempCM"), d.GetFloat("LFtempCR")),
                    Fr: new Telemetry.T1TyreSurface(d.GetFloat("RFtempCL"), d.GetFloat("RFtempCM"), d.GetFloat("RFtempCR")),
                    Rl: new Telemetry.T1TyreSurface(d.GetFloat("LRtempCL"), d.GetFloat("LRtempCM"), d.GetFloat("LRtempCR")),
                    Rr: new Telemetry.T1TyreSurface(d.GetFloat("RRtempCL"), d.GetFloat("RRtempCM"), d.GetFloat("RRtempCR")))));

            GraphicsUpdated?.Invoke(new T1GraphicsFrame(
                PacketId: d.TickCount,
                Status: MapStatus(d),
                SessionType: _lastSessionType,
                SessionIndex: d.GetInt("SessionNum"),
                CurrentTimeText: null,
                LastTimeText: null,
                BestTimeText: null,
                CompletedLaps: d.GetInt("Lap"),
                Position: d.GetInt("PlayerCarPosition"),
                CurrentLapMs: (int)(d.GetFloat("LapCurrentLapTime") * 1000),
                LastLapMs: (int)(d.GetFloat("LapLastLapTime") * 1000),
                BestLapMs: (int)(d.GetFloat("LapBestLapTime") * 1000),
                NativeDeltaMs: (int)(d.GetFloat("LapDeltaToSessionBestLap") * 1000),
                FuelEstimatedLaps: null,
                FuelPerLapNative: null,
                NormalizedCarPosition: d.GetFloat("LapDistPct"),
                // TODO(no-live-iracing): iRacing has no direct "is this lap valid" telemetry
                // bool in the base SDK. Defaulting true; DeltaEngine/FuelManager's own
                // pit-lane exclusion (OnPitRoad) provides a weaker but reasonable proxy.
                IsValidLap: true,
                IsInPitLane: d.GetBool("OnPitRoad")));

            if (_staticRefreshRequested)
                EmitStaticFromSessionInfo();

            // v2: sector/lap tracking runs on every telemetry callback (independent of the
            // 20Hz tick rate limiter below) so a sector crossing between throttled ticks is
            // never missed — mirrors AccTelemetrySourceAdapter's decoupling.
            TrackSectorsAndLaps(d);
            TryEmitTickV2(d);
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    // ── v2: tick emission (rate-limited to STREAM_HZ, default 20, hard max 60) ─────────────

    private void TryEmitTickV2(IRacingSdkData d)
    {
        var now = DateTime.UtcNow;
        if (now - _lastTickEmittedAt < TelemetryEnv.TickInterval) return;
        _lastTickEmittedAt = now;

        TickUpdated?.Invoke(BuildTickV2(d));
    }

    private T1TickFrame BuildTickV2(IRacingSdkData d)
    {
        float lapDistPct = d.GetFloat("LapDistPct");
        int sector = DeriveSector(lapDistPct);

        float? steer = null, steerDeg = null;
        if (HasVar(d, "SteeringWheelAngle") && HasVar(d, "SteeringWheelAngleMax"))
        {
            var max = d.GetFloat("SteeringWheelAngleMax");
            var angle = d.GetFloat("SteeringWheelAngle");
            steer = max != 0 ? angle / max : 0f;
            steerDeg = angle * 57.2958f;
        }

        var (posX, posY, posZ) = DeriveLocalPosition(d);

        int? tc = null; // TODO(no-live-iracing): see PhysicsUpdated's TcActive comment above.
        int? abs = HasVar(d, "BrakeABSactive") ? (d.GetBool("BrakeABSactive") ? 1 : 0) : null;
        float? brakeBias = HasVar(d, "dcBrakeBias") ? d.GetFloat("dcBrakeBias") : null;

        return new T1TickFrame(
            Lap: d.GetInt("Lap"),
            LapTimeMs: (int)(d.GetFloat("LapCurrentLapTime") * 1000),
            // iRacing exposes actual S/F distance directly, unlike ACC's normalized position.
            LapDistM: d.GetFloat("LapDist"),
            Sector: sector,
            Speed: d.GetFloat("Speed") * 3.6f,
            Rpm: (int)d.GetFloat("RPM"),
            // iRacing's Gear var is already -1=R,0=N,1..n — no normalization needed for v2
            // (unlike the v1 PhysicsUpdated frame above, which uses T1's own 0=R,1=N convention).
            Gear: d.GetInt("Gear"),
            Throttle: d.GetFloat("Throttle"),
            Brake: d.GetFloat("Brake"),
            Clutch: 1f - d.GetFloat("Clutch"),
            Steer: steer ?? 0f,
            SteerDeg: steerDeg,
            GLat: d.GetFloat("LatAccel") / 9.81f,
            GLong: d.GetFloat("LongAccel") / 9.81f,
            GVert: d.GetFloat("VertAccel") / 9.81f,
            PosX: posX,
            PosY: posY,
            PosZ: posZ,
            Heading: d.GetFloat("Yaw"),
            // iRacing has no native "is this lap valid" flag — omit per the v2 contract.
            Valid: null,
            Pit: d.GetBool("PlayerCarInPitStall") ? 2 : (d.GetBool("OnPitRoad") ? 1 : 0),
            // No direct flying/out-lap/in-lap/garage enum in the base SDK.
            DriverStatus: null,
            Fuel: d.GetFloat("FuelLevel"),
            Tc: tc,
            Abs: abs,
            BrakeBias: brakeBias,
            // DRS/ERS/tyre-compound-id/tyre-age are F1-only per the v2 cheatsheet.
            Drs: null,
            Ers: null,
            ErsMode: null,
            TyreCompound: null,
            TyreAgeLaps: null,
            TyreTemp: new T1Corners(
                Fl: d.GetFloat("LFtempCM"), Fr: d.GetFloat("RFtempCM"),
                Rl: d.GetFloat("LRtempCM"), Rr: d.GetFloat("RRtempCM")),
            TyrePress: new T1Corners(
                Fl: d.GetFloat("LFpress"), Fr: d.GetFloat("RFpress"),
                Rl: d.GetFloat("LRpress"), Rr: d.GetFloat("RRpress")),
            // iRacing's base telemetry doesn't expose brake disc temperature, per-corner slip
            // ratio, or tyre wear (only tread remaining % per-tyre-not-per-corner on some
            // cars) — permanent sim-dependent gaps, BrakeTemp already documented on the v1
            // PhysicsUpdated frame above.
            TyreWear: null,
            BrakeTemp: null,
            Slip: null,
            Frame: HasVar(d, "SessionTick") ? d.GetInt("SessionTick") : d.TickCount);
    }

    private int DeriveSector(float lapDistPct)
    {
        if (_cachedSectorStartPct.Length == 0) return 0;
        int sector = 0;
        for (int i = 0; i < _cachedSectorStartPct.Length; i++)
        {
            if (lapDistPct >= _cachedSectorStartPct[i]) sector = i;
        }
        return sector;
    }

    private (float? X, float? Y, float? Z) DeriveLocalPosition(IRacingSdkData d)
    {
        if (!HasVar(d, "Lat") || !HasVar(d, "Lon")) return (null, null, null);

        double latDeg = d.GetDouble("Lat") * 180.0 / Math.PI;
        double lonDeg = d.GetDouble("Lon") * 180.0 / Math.PI;
        float altM = HasVar(d, "Alt") ? d.GetFloat("Alt") : 0f;

        if (_originLatDeg is null)
        {
            _originLatDeg = latDeg;
            _originLonDeg = lonDeg;
            _originAltM = altM;
        }

        // Equirectangular approximation, origin = first sample — adequate for a single track's
        // extent (a few km) per backend_contract_v2.md's guidance.
        const double earthRadiusM = 6371000.0;
        double dLatRad = (latDeg - _originLatDeg.Value) * Math.PI / 180.0;
        double dLonRad = (lonDeg - _originLonDeg!.Value) * Math.PI / 180.0;
        double east = dLonRad * Math.Cos(_originLatDeg.Value * Math.PI / 180.0) * earthRadiusM;
        double north = dLatRad * earthRadiusM;

        return ((float)east, (float)north, altM - (_originAltM ?? 0f));
    }

    // ── v2: lap_complete (own sector timing + pit latching; validity omitted) ──────────────

    private void TrackSectorsAndLaps(IRacingSdkData d)
    {
        int lap = d.GetInt("Lap");
        int sector = DeriveSector(d.GetFloat("LapDistPct"));
        var now = DateTime.UtcNow;

        if (!_lapTrackingInitialized)
        {
            _lapTrackingInitialized = true;
            _lastLapSeen = lap;
            _lastSectorIndex = sector;
            _lastSectorTransitionAt = now;
            _lapStartFuel = d.GetFloat("FuelLevel");
            return;
        }

        if (d.GetBool("OnPitRoad") || d.GetBool("PlayerCarInPitStall")) _lapPitLatch = true;

        if (sector != _lastSectorIndex)
        {
            _sectorTimesBuffer.Add((int)(now - _lastSectorTransitionAt).TotalMilliseconds);
            _lastSectorTransitionAt = now;
            _lastSectorIndex = sector;
        }

        if (lap > _lastLapSeen)
        {
            _pendingLapNumber = _lastLapSeen;
            _pendingSectorsMs = _sectorTimesBuffer.ToArray();
            _pendingPit = _lapPitLatch;
            _pendingBaselineLapLastLapTime = d.GetFloat("LapLastLapTime");
            _pendingTicksWaited = 0;
            _pendingFuelStart = _lapStartFuel;

            _lastLapSeen = lap;
            _sectorTimesBuffer.Clear();
            _lapPitLatch = false;
            _lapStartFuel = d.GetFloat("FuelLevel");
        }

        if (_pendingLapNumber is { } pendingLap)
        {
            _pendingTicksWaited++;
            var currentLapLastLapTime = d.GetFloat("LapLastLapTime");
            bool changed = Math.Abs(currentLapLastLapTime - _pendingBaselineLapLastLapTime) > 0.0001f;
            if (changed || _pendingTicksWaited >= MaxPendingLapTicks)
            {
                float? fuelUsed = null;
                if (_pendingFuelStart is { } startFuel)
                {
                    var used = startFuel - d.GetFloat("FuelLevel");
                    if (used >= 0) fuelUsed = used;
                }

                LapCompleted?.Invoke(new T1LapComplete(
                    Lap: pendingLap,
                    LapTimeMs: (int)(currentLapLastLapTime * 1000),
                    SectorsMs: _pendingSectorsMs ?? Array.Empty<int>(),
                    Valid: null, // iRacing has no native lap-validity flag.
                    Pit: _pendingPit,
                    LapDistM: _cachedTrackLengthM,
                    FuelUsedL: fuelUsed,
                    TyreCompound: null));

                _pendingLapNumber = null;
                _pendingSectorsMs = null;
            }
        }
    }

    private void OnSessionInfo()
    {
        try
        {
            var info = _sdk.Data.SessionInfo;
            var sessions = info?.SessionInfo?.Sessions;
            var currentNum = info?.SessionInfo?.CurrentSessionNum ?? 0;
            var current = sessions?.FirstOrDefault(s => s.SessionNum == currentNum);
            _lastSessionType = MapSessionType(current?.SessionType);

            EmitStaticFromSessionInfo();
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex);
        }
    }

    private void EmitStaticFromSessionInfo()
    {
        var info = _sdk.Data.SessionInfo;
        if (info == null) return;

        var weekend = info.WeekendInfo;
        var driverInfo = info.DriverInfo;
        var driver = driverInfo?.Drivers?.FirstOrDefault(dr => dr.CarIdx == driverInfo.DriverCarIdx);

        string? firstName = null, lastName = null;
        if (!string.IsNullOrWhiteSpace(driver?.UserName))
        {
            var parts = driver.UserName.Split(' ', 2);
            firstName = parts[0];
            lastName = parts.Length > 1 ? parts[1] : "";
        }

        StaticInfoUpdated?.Invoke(new T1StaticFrame(
            Track: weekend?.TrackDisplayName,
            CarModel: driver?.CarScreenName,
            PlayerName: firstName,
            PlayerSurname: lastName,
            MaxRpm: (int)(driverInfo?.DriverCarRedLine ?? 0f),
            MaxFuel: driverInfo?.DriverCarFuelMaxLtr ?? 0f));

        _staticRefreshRequested = false;

        var currentSessionNum = info.SessionInfo?.CurrentSessionNum ?? 0;
        var current = info.SessionInfo?.Sessions?.FirstOrDefault(s => s.SessionNum == currentSessionNum);
        EmitSessionInfoV2(info, weekend, driver, current);
    }

    // ── v2: session_start ────────────────────────────────────────────────────────────────

    private void EmitSessionInfoV2(
        IRacingSdkSessionInfo info,
        IRacingSdkSessionInfo.WeekendInfoModel? weekend,
        IRacingSdkSessionInfo.DriverInfoModel.DriverModel? driver,
        IRacingSdkSessionInfo.SessionInfoModel.SessionModel? current)
    {
        if (weekend == null) return;

        _cachedTrackLengthM = ParseTrackLengthMeters(weekend.TrackLength);

        var sectors = info.SplitTimeInfo?.Sectors?.OrderBy(s => s.SectorNum).ToList();
        _cachedSectorStartPct = sectors?.Select(s => s.SectorStartPct).ToArray() ?? Array.Empty<float>();
        float[]? boundariesM = sectors is { Count: > 1 }
            ? sectors.Skip(1).Select(s => s.SectorStartPct * _cachedTrackLengthM).ToArray()
            : null;

        var trackId = $"{weekend.TrackID}:{weekend.TrackConfigName}";
        var sessionType = MapSessionType(current?.SessionType);
        var key = $"{trackId}|{driver?.CarID}|{current?.SessionType}";
        if (key == _lastSessionInfoKey) return;
        _lastSessionInfoKey = key;

        SessionInfoUpdated?.Invoke(new T1SessionInfo(
            Source: T1Wire.SourceName("iracing"),
            SimVersion: weekend.BuildVersion,
            TrackId: trackId,
            TrackName: weekend.TrackDisplayName ?? weekend.TrackName ?? trackId,
            TrackLengthM: _cachedTrackLengthM,
            SectorCount: sectors?.Count is > 0 ? sectors!.Count : 3,
            SectorBoundariesM: boundariesM,
            CarId: driver?.CarID.ToString() ?? "",
            CarName: driver?.CarScreenName ?? "",
            CarClass: driver?.CarClassShortName,
            SessionType: sessionType,
            SessionTypeRaw: current?.SessionType,
            Driver: driver?.UserName ?? "",
            TickRateHz: TelemetryEnv.StreamHz));
    }

    /// <summary>Parses iRacing's "3.60 km" / "2.5 mi" style track length string to metres.</summary>
    private static float ParseTrackLengthMeters(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0f;
        var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return 0f;

        var unit = parts.Length > 1 ? parts[1].ToLowerInvariant() : "km";
        return unit switch
        {
            "km" => value * 1000f,
            "mi" => value * 1609.34f,
            "m" => value,
            _ => value * 1000f
        };
    }

    private static bool HasVar(IRacingSdkData d, string name) => d.TelemetryDataProperties.ContainsKey(name);

    private static int NormalizeGear(int raw) => raw < 0 ? 0 : raw + 1;

    // TODO(no-live-iracing): confirm this Live/Paused/Off/Replay mapping against real
    // Live/Paused/Replay transitions in an actual session.
    private static T1SimStatus MapStatus(IRacingSdkData d)
    {
        if (HasVar(d, "IsReplayPlaying") && d.GetBool("IsReplayPlaying"))
            return T1SimStatus.Replay;

        var state = (IRacingSdkEnum.SessionState)d.GetInt("SessionState");
        return state switch
        {
            IRacingSdkEnum.SessionState.Racing => T1SimStatus.Live,
            IRacingSdkEnum.SessionState.Warmup => T1SimStatus.Live,
            IRacingSdkEnum.SessionState.ParadeLaps => T1SimStatus.Live,
            IRacingSdkEnum.SessionState.Checkered => T1SimStatus.Live,
            IRacingSdkEnum.SessionState.CoolDown => T1SimStatus.Live,
            IRacingSdkEnum.SessionState.GetInCar => T1SimStatus.Paused,
            IRacingSdkEnum.SessionState.Invalid => T1SimStatus.Off,
            _ => T1SimStatus.Off
        };
    }

    private static T1SessionType MapSessionType(string? iracingType)
    {
        if (string.IsNullOrEmpty(iracingType)) return T1SessionType.Unknown;
        if (iracingType.Contains("Race", StringComparison.OrdinalIgnoreCase)) return T1SessionType.Race;
        if (iracingType.Contains("Qualify", StringComparison.OrdinalIgnoreCase)) return T1SessionType.Qualify;
        if (iracingType.Contains("Practice", StringComparison.OrdinalIgnoreCase) ||
            iracingType.Contains("Warmup", StringComparison.OrdinalIgnoreCase) ||
            iracingType.Contains("Testing", StringComparison.OrdinalIgnoreCase))
            return T1SessionType.Practice;
        return T1SessionType.Unknown;
    }
}
