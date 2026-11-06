namespace Turn_One_Link.Services;

/// <summary>One graphics frame's worth of the ACC / AC fields lap timing needs.</summary>
public readonly record struct AccLapSample(
    int CompletedLaps,
    int CurrentSectorIndex,
    int CurrentLapMs,
    int LastLapMs,
    bool FrameInvalid,
    bool InPit,
    float? Fuel);

/// <summary>A finished lap as ACC reported it.</summary>
public readonly record struct AccCompletedLap(
    int Lap,
    int LapTimeMs,
    int[] SectorsMs,
    bool Valid,
    bool Pit,
    float? FuelUsedL);

/// <summary>
/// Turns ACC/AC graphics frames into completed laps. Pure state machine (no shared memory), so it is
/// unit-tested with recorded and synthetic frames.
///
/// <list type="bullet">
/// <item>A lap ends when <c>completedLaps</c> increments — not on a sector change: ACC moves the
/// sector index 2→0 a frame <i>before</i> the lap counter, so the old sector-driven logic only saw the
/// lap at the next 0→1 change, ~35 s late.</item>
/// <item>A lap <i>starts</i> when the lap clock (<c>iCurrentTime</c>) restarts. ACC restarts it at the
/// line after a pit out-lap without counting that out-lap, so the clock — not the counter — marks the
/// start of the first timed lap.</item>
/// <item>The lap time is <c>iLastTime</c>; if it isn't refreshed on the increment frame the tracker
/// waits a few frames for it.</item>
/// <item>Sector splits come from <c>iCurrentTime</c> at each in-lap sector boundary (ACC's
/// <c>lastSectorTime</c> is not a per-sector time). The final sector is lap time minus the others.</item>
/// <item>A lap is only valid if the tracker saw it from the line.</item>
/// </list>
/// </summary>
public sealed class AccLapTracker
{
    public const int NoTime = 2147483647; // ACC's "no time recorded" value

    /// <summary>Frames to wait for iLastTime to refresh after the lap counter increments.</summary>
    private const int MaxWaitFramesForLapTime = 60;

    private readonly int _sectorCount;

    private bool _initialized;
    private int _completedLaps;
    private int _sectorIndex;
    private int _lastCurrentLapMs;

    private LapInProgress _lap = new(SawStart: false, StartFuel: null);
    private sealed class LapInProgress(bool SawStart, float? StartFuel)
    {
        public bool SawStart { get; } = SawStart;
        public float? StartFuel { get; } = StartFuel;
        public bool Valid { get; set; } = true;
        public bool Pit { get; set; }
        public List<int> Splits { get; } = new(); // cumulative ms at each in-lap sector boundary
    }

    // The lap that ended at the most recent clock restart, kept until the counter catches up.
    private LapInProgress? _endedAtClockRestart;

    private sealed record PendingLap(int Lap, int PreviousLastLapMs, LapInProgress Data)
    {
        public int FramesWaited { get; set; }
    }
    private PendingLap? _pending;
    private int _lastLapMsSeen = NoTime;

    public AccLapTracker(int sectorCount = 3)
    {
        _sectorCount = Math.Max(1, sectorCount);
    }

    /// <summary>Feeds one graphics frame. Returns a lap when one has just been completed.</summary>
    public AccCompletedLap? Update(AccLapSample s)
    {
        var result = UpdateCore(s);
        _lastLapMsSeen = s.LastLapMs; // the previous frame's value is what "fresh" is judged against
        return result;
    }

    private AccCompletedLap? UpdateCore(AccLapSample s)
    {
        if (!_initialized)
        {
            _initialized = true;
            _completedLaps = s.CompletedLaps;
            _sectorIndex = s.CurrentSectorIndex;
            _lastCurrentLapMs = s.CurrentLapMs;
            // Joined mid-lap: the lap's time is still reported, but sectors and validity can't be vouched for.
            _lap = new LapInProgress(SawStart: false, StartFuel: s.Fuel);
            return null;
        }

        AccCompletedLap? result = TryResolvePending(s);

        if (s.CompletedLaps < _completedLaps)
        {
            // Session restart: drop everything in progress.
            _completedLaps = s.CompletedLaps;
            _pending = null;
            _endedAtClockRestart = null;
            StartLap(s, sawStart: false);
            return result;
        }

        bool clockRestarted = s.CurrentLapMs + 5_000 < _lastCurrentLapMs && s.CurrentLapMs < 5_000;
        _lastCurrentLapMs = s.CurrentLapMs;

        if (s.CompletedLaps > _completedLaps)
        {
            _completedLaps = s.CompletedLaps;

            // If the clock restarted moments ago (and not again on this frame), the lap ended there
            // and the lap in progress is already the next one.
            LapInProgress ended;
            if (!clockRestarted && _endedAtClockRestart is { } e && s.CurrentLapMs < 10_000)
            {
                ended = e;
            }
            else
            {
                ended = _lap;
                StartLap(s, sawStart: true);
            }
            _endedAtClockRestart = null;
            _pending = new PendingLap(s.CompletedLaps, _lastLapMsSeen, ended);

            result ??= TryResolvePending(s);
            return result;
        }

        if (clockRestarted)
        {
            // Crossed the line (or left the pits onto a timed lap) without the counter moving yet.
            _endedAtClockRestart = _lap;
            StartLap(s, sawStart: true);
            return result;
        }

        // Same lap: latch validity / pit, record in-lap sector boundaries.
        if (s.FrameInvalid) _lap.Valid = false;
        if (s.InPit) _lap.Pit = true;

        if (s.CurrentSectorIndex != _sectorIndex)
        {
            // Forward boundary inside the lap (0→1, 1→2). The wrap to 0 belongs to the line crossing.
            if (s.CurrentSectorIndex == _sectorIndex + 1 && s.CurrentSectorIndex < _sectorCount)
                _lap.Splits.Add(s.CurrentLapMs);
            _sectorIndex = s.CurrentSectorIndex;
        }

        return result;
    }

    private void StartLap(AccLapSample s, bool sawStart)
    {
        // The flags on the crossing frame still describe the lap that ended; start clean.
        _lap = new LapInProgress(sawStart, s.Fuel) { Pit = s.InPit };
        _sectorIndex = s.CurrentSectorIndex;
    }

    private AccCompletedLap? TryResolvePending(AccLapSample s)
    {
        if (_pending is not { } p) return null;

        bool fresh = s.LastLapMs != p.PreviousLastLapMs;
        bool usable = s.LastLapMs > 0 && s.LastLapMs != NoTime;
        p.FramesWaited++;

        if (!(usable && (fresh || p.FramesWaited > MaxWaitFramesForLapTime)))
        {
            if (p.FramesWaited > MaxWaitFramesForLapTime) _pending = null; // ACC never timed this lap
            return null;
        }

        _pending = null;
        var lap = p.Data;

        int[] sectors = Array.Empty<int>();
        if (lap.SawStart && lap.Splits.Count == _sectorCount - 1)
        {
            sectors = new int[_sectorCount];
            int previous = 0;
            for (int i = 0; i < lap.Splits.Count; i++)
            {
                sectors[i] = lap.Splits[i] - previous;
                previous = lap.Splits[i];
            }
            sectors[^1] = s.LastLapMs - previous;
            if (sectors.Any(x => x <= 0)) sectors = Array.Empty<int>(); // inconsistent; don't send garbage
        }

        float? fuelUsed = lap.StartFuel is { } start && s.Fuel is { } now && start - now >= 0 ? start - now : null;

        return new AccCompletedLap(p.Lap, s.LastLapMs, sectors, lap.SawStart && lap.Valid, lap.Pit, fuelUsed);
    }
}
