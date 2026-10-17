using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Turn_One_Link.Services.Telemetry;
using static Turn_One_Link.Services.F1.F1Packets;

namespace Turn_One_Link.Services.F1;

/// <summary>
/// EA SPORTS F1 25 / F1 26 telemetry over UDP (game setting: UDP Telemetry = On, format 2025/2026,
/// port 20777, rate 20–60 Hz). Listens on <c>F1_UDP_PORT</c>, parses the player car out of the
/// Motion / LapData / CarTelemetry / CarStatus / MotionEx / Session packets and merges them into
/// protocol-v2 ticks. Lap completion comes from <c>currentLapNum</c> changes, with timing taken from the
/// Session History packet once the game publishes the finished lap (a second or so later).
///
/// The legacy Physics/Graphics/Static events are also raised with a best-effort mapping so the local
/// mobile dashboard keeps working; the cloud stream uses the v2 events only.
/// </summary>
public sealed class F1UdpTelemetrySource : ITelemetrySource
{
    public const int DefaultPort = 20777;

    public event Action<T1PhysicsFrame>? PhysicsUpdated;
    public event Action<T1GraphicsFrame>? GraphicsUpdated;
    public event Action<T1StaticFrame>? StaticInfoUpdated;
    public event Action<Exception>? OnError;
    public event Action<T1TickFrame>? TickUpdated;
    public event Action<T1LapComplete>? LapCompleted;
    public event Action<T1SessionInfo>? SessionInfoUpdated;

    /// <summary>Resolved from the packet header's <c>packetFormat</c> (2025 → f1_25, 2026 → f1_26). "f1_25" until the first packet.</summary>
    public string SourceName { get; private set; } = "f1_25";

    private readonly int _port;
    private readonly Action<string>? _log;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    // Latest per-packet state for the player car.
    private CarMotion? _motion;
    private LapData? _lap;
    private CarTelemetry? _telemetry;
    private CarStatus? _status;
    private (float rl, float rr, float fl, float fr)? _slip;
    private F1Session? _session;
    private ulong _sessionUid;
    private ushort _format;
    private uint _lastOverallFrame;

    // Lap tracking.
    private int _currentLap = -1;
    private int _pendingHistoryLap = -1;     // lap number whose lap_complete is waiting for Session History
    private float _pendingLapDist;
    private bool _pendingPit;
    private float _fuelAtLapStart = float.NaN;
    private bool _lapPitLatch;
    private int _flashbackLap = -1;          // lap invalidated by a flashback

    private DateTime _lastTickAt = DateTime.MinValue;
    private string _lastIdentity = "";
    private readonly bool[] _sizeWarned = new bool[16];

    public F1UdpTelemetrySource(Action<string>? log = null)
    {
        _log = log;
        var raw = Environment.GetEnvironmentVariable("F1_UDP_PORT");
        _port = int.TryParse(raw, out var p) && p is > 0 and < 65536 ? p : DefaultPort;
    }

    public void Start()
    {
        if (_udp != null) return;
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
        _log?.Invoke($"F1 UDP: listening on port {_port}");
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
        _cts = null;
    }

    public void RequestStaticRefresh()
    {
        _lastIdentity = "";
        EmitSessionInfoIfReady();
    }

    public void Dispose() => Stop();

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var udp = _udp;
        if (udp == null) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                HandlePacket(result.Buffer);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }
    }

    internal void HandlePacket(ReadOnlySpan<byte> p)
    {
        if (!TryReadHeader(p, out var h)) return;
        if (h.PacketFormat != Format2025 && h.PacketFormat != Format2026)
        {
            if (!_sizeWarned[15]) { _sizeWarned[15] = true; _log?.Invoke($"F1 UDP: unsupported packetFormat {h.PacketFormat} — set the game's UDP format to 2025 or 2026"); }
            return;
        }

        if (_format != h.PacketFormat)
        {
            _format = h.PacketFormat;
            SourceName = h.PacketFormat == Format2026 ? "f1_26" : "f1_25";
        }

        // Packet sizes are asserted per format; a mismatch means the layout moved and we would misread fields.
        var expected = ExpectedSize(h.Id);
        if (expected > 0 && p.Length != expected)
        {
            var idx = (int)h.Id & 15;
            if (!_sizeWarned[idx])
            {
                _sizeWarned[idx] = true;
                _log?.Invoke($"F1 UDP: {h.Id} packet is {p.Length} B, expected {expected} B for format {h.PacketFormat} — skipping this packet type");
            }
            return;
        }

        if (_sessionUid != h.SessionUid)
        {
            // New sim session: reset lap tracking, force a new session_start.
            _sessionUid = h.SessionUid;
            _currentLap = -1;
            _pendingHistoryLap = -1;
            _flashbackLap = -1;
            _lastIdentity = "";
        }

        var car = h.PlayerCarIndex < MaxCars ? h.PlayerCarIndex : 0;

        switch (h.Id)
        {
            case PacketId.Motion:
                _motion = ReadCarMotion(p, car);
                break;
            case PacketId.Session:
                _session = ReadSession(p);
                EmitSessionInfoIfReady();
                break;
            case PacketId.LapData:
                var lap = ReadLapData(p, car);
                OnLapData(lap, h);
                _lap = lap;
                break;
            case PacketId.Event:
                OnEvent(p);
                break;
            case PacketId.CarTelemetry:
                _telemetry = ReadCarTelemetry(p, car);
                _lastOverallFrame = h.OverallFrameIdentifier;
                MaybeEmitTick(h);
                break;
            case PacketId.CarStatus:
                _status = ReadCarStatus(p, car);
                break;
            case PacketId.MotionEx:
                _slip = ReadWheelSlipRatio(p);
                break;
            case PacketId.SessionHistory:
                if (ReadSessionHistoryCarIndex(p) == car) OnSessionHistory(p);
                break;
        }
    }

    // ------------------------------------------------------------------ lap lifecycle

    private void OnLapData(in LapData lap, in Header h)
    {
        var lapNum = lap.CurrentLapNum;
        if (_currentLap < 0)
        {
            _currentLap = lapNum;
            _fuelAtLapStart = _status?.FuelInTank ?? float.NaN;
            _lapPitLatch = lap.PitStatus != 0;
            return;
        }

        if (lapNum > _currentLap)
        {
            // Lap finished: queue lap_complete until Session History carries its timing.
            _pendingHistoryLap = _currentLap;
            _pendingLapDist = _session?.TrackLengthM ?? 0;
            _pendingPit = _lapPitLatch || lap.PitStatus != 0;
            _currentLap = lapNum;
            _lapPitLatch = lap.PitStatus != 0;
            var fuelNow = _status?.FuelInTank ?? float.NaN;
            _pendingFuelUsed = !float.IsNaN(_fuelAtLapStart) && !float.IsNaN(fuelNow) ? MathF.Max(0, _fuelAtLapStart - fuelNow) : (float?)null;
            _fuelAtLapStart = fuelNow;
        }
        else if (lapNum < _currentLap)
        {
            // Flashback across the line or session restart — the backend discards the buffer on lap decrement.
            _currentLap = lapNum;
            _pendingHistoryLap = -1;
            _lapPitLatch = lap.PitStatus != 0;
        }
        else if (lap.PitStatus != 0)
        {
            _lapPitLatch = true;
        }
    }

    private float? _pendingFuelUsed;

    private void OnSessionHistory(ReadOnlySpan<byte> p)
    {
        if (_pendingHistoryLap <= 0) return;
        var numLaps = ReadSessionHistoryNumLaps(p);
        if (numLaps < _pendingHistoryLap || _pendingHistoryLap > MaxLapHistory) return;

        var hist = ReadLapHistory(p, _pendingHistoryLap - 1);
        if (hist.LapTimeMs == 0) return; // not published yet

        var s1 = hist.Sector1Ms; var s2 = hist.Sector2Ms; var s3 = hist.Sector3Ms;
        if (s3 == 0 && hist.LapTimeMs > (uint)(s1 + s2)) s3 = (int)hist.LapTimeMs - s1 - s2;

        var valid = hist.LapValid && _flashbackLap != _pendingHistoryLap;
        LapCompleted?.Invoke(new T1LapComplete(
            Lap: _pendingHistoryLap,
            LapTimeMs: (int)hist.LapTimeMs,
            SectorsMs: new[] { s1, s2, s3 },
            Valid: valid,
            Pit: _pendingPit,
            LapDistM: _pendingLapDist,
            FuelUsedL: _pendingFuelUsed,
            TyreCompound: _status?.ActualTyreCompound));

        _pendingHistoryLap = -1;
        _pendingFuelUsed = null;
    }

    private void OnEvent(ReadOnlySpan<byte> p)
    {
        var code = ReadEventCode(p);
        switch (code)
        {
            case "FLBK":
                // The game rewound; whatever lap we are on is no longer a clean lap. The backend also
                // detects the distance going backwards and aborts the buffered lap.
                _flashbackLap = _currentLap;
                _log?.Invoke("F1 UDP: flashback detected — current lap marked invalid");
                break;
            case "SSTA":
                _lastIdentity = "";
                EmitSessionInfoIfReady();
                break;
            case "SEND":
                _currentLap = -1;
                break;
        }
    }

    // ------------------------------------------------------------------ session info

    private void EmitSessionInfoIfReady()
    {
        if (_session is not { } s) return;
        var (trackId, trackName) = Track(s.TrackId);
        var (sessionType, raw) = MapSessionType(s.SessionType);
        var identity = $"{SourceName}|{trackId}|{raw}|{_sessionUid}";
        if (identity == _lastIdentity) return;
        _lastIdentity = identity;

        SessionInfoUpdated?.Invoke(new T1SessionInfo(
            Source: SourceName,
            SimVersion: null,
            TrackId: trackId,
            TrackName: trackName,
            TrackLengthM: s.TrackLengthM,
            SectorCount: 3,
            SectorBoundariesM: null,
            CarId: $"f1_team_{_carTeamId}",
            CarName: SourceName == "f1_26" ? "F1 26 car" : "F1 25 car",
            CarClass: s.Formula == 0 ? "F1" : $"formula_{s.Formula}",
            SessionType: sessionType,
            SessionTypeRaw: raw,
            Driver: "Player",
            TickRateHz: TelemetryEnv.StreamHz));

        StaticInfoUpdated?.Invoke(new T1StaticFrame(
            Track: trackName, CarModel: SourceName == "f1_26" ? "F1 26 car" : "F1 25 car",
            PlayerName: "Player", PlayerSurname: "", MaxRpm: _status?.MaxRpm ?? 13000, MaxFuel: _status?.FuelCapacity ?? 110f));
    }

    // Team id / driver name live in the Participants packet, which we don't parse yet — kept generic.
    private const int _carTeamId = 0;

    // ------------------------------------------------------------------ ticks

    private void MaybeEmitTick(in Header h)
    {
        if (_telemetry is not { } t || _lap is not { } l) return;
        var now = DateTime.UtcNow;
        if (now - _lastTickAt < TelemetryEnv.TickInterval) return;
        _lastTickAt = now;

        var m = _motion;
        var st = _status;

        var tick = new T1TickFrame(
            Lap: l.CurrentLapNum,
            LapTimeMs: (int)l.CurrentLapTimeMs,
            LapDistM: l.LapDistance,
            Sector: l.Sector,
            Speed: t.SpeedKmh,
            Rpm: t.EngineRpm,
            Gear: t.Gear,                     // F1 already uses −1 = R, 0 = N, 1..8
            Throttle: t.Throttle,
            Brake: t.Brake,
            Clutch: t.Clutch / 100f,
            Steer: t.Steer,                   // −1..1, + = right
            SteerDeg: null,
            GLat: m?.GLat ?? 0f,
            GLong: m?.GLong ?? 0f,
            GVert: m?.GVert,
            // Game world: X/Z is the ground plane, Y is up → T1 wants X/Y ground, Z up.
            PosX: m?.WorldX,
            PosY: m?.WorldZ,
            PosZ: m?.WorldY,
            Heading: m?.Yaw,
            Valid: l.CurrentLapInvalid == 0 && _flashbackLap != l.CurrentLapNum,
            Pit: l.PitStatus,                 // 0 none, 1 pitting, 2 in pit area — same scale as the contract
            DriverStatus: l.DriverStatus,     // 0 garage, 1 flying, 2 in lap, 3 out lap, 4 on track
            Fuel: st?.FuelInTank,
            Tc: st?.TractionControl,
            Abs: st?.AntiLockBrakes,
            BrakeBias: st?.FrontBrakeBias,
            Drs: t.Drs,
            Ers: st?.ErsStoreEnergy,
            ErsMode: st?.ErsDeployMode,
            TyreCompound: st?.ActualTyreCompound,
            TyreAgeLaps: st?.TyresAgeLaps,
            TyreTemp: new T1Corners(t.TyreInnerFl, t.TyreInnerFr, t.TyreInnerRl, t.TyreInnerRr),
            TyrePress: new T1Corners(t.TyrePressureFl, t.TyrePressureFr, t.TyrePressureRl, t.TyrePressureRr),
            TyreWear: null,                   // lives in the Car Damage packet (not parsed yet)
            BrakeTemp: new T1Corners(t.BrakeTempFl, t.BrakeTempFr, t.BrakeTempRl, t.BrakeTempRr),
            Slip: _slip is { } sl ? new T1Corners(sl.fl, sl.fr, sl.rl, sl.rr) : null,
            Frame: h.OverallFrameIdentifier);

        TickUpdated?.Invoke(tick);

        // Legacy shapes for the local mobile dashboard.
        PhysicsUpdated?.Invoke(new T1PhysicsFrame(
            PacketId: (int)h.OverallFrameIdentifier,
            Gas: t.Throttle, Brake: t.Brake, Clutch: 1f - t.Clutch / 100f, Fuel: st?.FuelInTank ?? 0f,
            Gear: t.Gear + 1,                 // T1 mobile convention: 0 = R, 1 = N, 2 = 1st
            Rpm: t.EngineRpm, SpeedKmh: t.SpeedKmh,
            TcActive: (st?.TractionControl ?? 0) > 0, AbsActive: (st?.AntiLockBrakes ?? 0) > 0,
            TyreCoreTemp: new T1Corners(t.TyreInnerFl, t.TyreInnerFr, t.TyreInnerRl, t.TyreInnerRr),
            TyrePressure: new T1Corners(t.TyrePressureFl, t.TyrePressureFr, t.TyrePressureRl, t.TyrePressureRr),
            BrakeTemp: new T1Corners(t.BrakeTempFl, t.BrakeTempFr, t.BrakeTempRl, t.BrakeTempRr),
            TyreSurfaceTemp: null));

        var (sessionType, _) = MapSessionType(_session?.SessionType ?? 0);
        var trackLen = _session?.TrackLengthM ?? 0;
        GraphicsUpdated?.Invoke(new T1GraphicsFrame(
            PacketId: (int)h.OverallFrameIdentifier,
            Status: T1SimStatus.Live,
            SessionType: sessionType,
            SessionIndex: 0,
            CurrentTimeText: null, LastTimeText: null, BestTimeText: null,
            CompletedLaps: Math.Max(0, l.CurrentLapNum - 1),
            Position: l.CarPosition,
            CurrentLapMs: (int)l.CurrentLapTimeMs,
            LastLapMs: (int)l.LastLapTimeMs,
            BestLapMs: 0,
            NativeDeltaMs: 0,
            FuelEstimatedLaps: st?.FuelRemainingLaps,
            FuelPerLapNative: null,
            NormalizedCarPosition: trackLen > 0 ? Math.Clamp(l.LapDistance / trackLen, 0f, 1f) : 0f,
            IsValidLap: l.CurrentLapInvalid == 0,
            IsInPitLane: l.PitStatus != 0));
    }
}
