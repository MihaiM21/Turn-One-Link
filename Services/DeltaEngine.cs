using System.Collections.Generic;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

/// <summary>
/// Live delta vs. session best, with provenance.
/// Source is "trace" when computed from our own best-lap position trace,
/// or "native" when falling back to the sim's built-in delta.
/// </summary>
public sealed record DeltaSnapshot(
    int DeltaMs,
    float DeltaSeconds,
    string Source,
    bool HasBestTrace);

/// <summary>
/// Position-based delta engine. Records (normalized position, lap time) samples
/// for the session-best valid lap, then computes the live delta by interpolating
/// the best-lap time at the car's current track position.
///
/// The core API (<see cref="OnSample"/>) is sim-agnostic; <see cref="OnGraphics"/>
/// is the T1-frame adapter fed by any <see cref="ITelemetrySource"/> (ACC, iRacing, ...).
/// </summary>
public sealed class DeltaEngine
{
    // Minimum position advance between recorded samples. Caps a trace at
    // ~2000 points/lap while keeping interpolation error well under a frame.
    private const float MinPosStep = 0.0005f;

    // Absolute memory bound per trace, regardless of sim behaviour.
    private const int MaxTracePoints = 4000;

    // A backwards position jump larger than this without a lap increment means
    // the car was teleported (reset to pits / session skip) — the lap is void.
    private const float BackwardJumpThreshold = 0.05f;

    // A promotable trace must start near the finish line; traces that begin
    // mid-lap (out-laps, joining a session mid-lap) are incomplete.
    private const float CompleteTraceStartPos = 0.05f;

    private readonly List<(float Pos, int TimeMs)> _currentTrace = new(2048);
    private List<(float Pos, int TimeMs)>? _bestTrace;
    private int _bestTraceLapMs;

    private float _lastRecordedPos = -1f;
    private int _lastCompletedLaps = -1;
    private bool _currentLapDirty;

    private DeltaSnapshot? _snapshot;

    /// <summary>T1-frame adapter — call from the graphics update handler.</summary>
    public void OnGraphics(T1GraphicsFrame g, SessionPhase phase)
    {
        bool live = phase == SessionPhase.Live && g.Status == T1SimStatus.Live;
        OnSample(
            normalizedPos: g.NormalizedCarPosition,
            currentLapMs: g.CurrentLapMs,
            completedLaps: g.CompletedLaps,
            lastLapMs: g.LastLapMs,
            currentLapValid: g.IsValidLap,
            inPitLane: g.IsInPitLane,
            nativeDeltaMs: g.NativeDeltaMs,
            live: live);
    }

    /// <summary>Sim-neutral core. Feed one sample per graphics-rate update.</summary>
    public void OnSample(
        float normalizedPos,
        int currentLapMs,
        int completedLaps,
        int lastLapMs,
        bool currentLapValid,
        bool inPitLane,
        int nativeDeltaMs,
        bool live)
    {
        if (!live) return; // paused/replay/menu — keep the last snapshot frozen

        if (_lastCompletedLaps < 0)
        {
            // First live sample of the session: we're joining mid-lap, so the
            // in-progress lap can never become a complete best trace.
            _lastCompletedLaps = completedLaps;
            _currentLapDirty = normalizedPos >= CompleteTraceStartPos;
        }
        else if (completedLaps > _lastCompletedLaps)
        {
            // Lap boundary. Dirty-marking for the *new* lap happens below, so
            // the flags evaluated here still describe the lap just completed.
            CompleteLap(lastLapMs, lapCountDelta: completedLaps - _lastCompletedLaps);
            _lastCompletedLaps = completedLaps;
        }
        else if (completedLaps < _lastCompletedLaps)
        {
            // Lap counter went backwards — session restart. Void the lap.
            StartNewLap(dirty: true);
            _lastCompletedLaps = completedLaps;
        }

        if (!currentLapValid || inPitLane)
            _currentLapDirty = true;

        // Teleport guard: position jumped backwards without a lap increment.
        if (_lastRecordedPos >= 0 && normalizedPos < _lastRecordedPos - BackwardJumpThreshold)
        {
            _currentLapDirty = true;
            _lastRecordedPos = normalizedPos;
        }

        if (!_currentLapDirty &&
            _currentTrace.Count < MaxTracePoints &&
            (_lastRecordedPos < 0 || normalizedPos >= _lastRecordedPos + MinPosStep))
        {
            _currentTrace.Add((normalizedPos, currentLapMs));
            _lastRecordedPos = normalizedPos;
        }

        if (_bestTrace is { Count: > 1 })
        {
            int bestTimeAtPos = InterpolateBestTime(normalizedPos);
            int deltaMs = currentLapMs - bestTimeAtPos;
            _snapshot = new DeltaSnapshot(deltaMs, deltaMs / 1000f, "trace", HasBestTrace: true);
        }
        else
        {
            _snapshot = new DeltaSnapshot(nativeDeltaMs, nativeDeltaMs / 1000f, "native", HasBestTrace: false);
        }
    }

    public DeltaSnapshot? GetSnapshot() => _snapshot;

    /// <summary>Clears all traces and state. Call on session start and end.</summary>
    public void Reset()
    {
        _currentTrace.Clear();
        _bestTrace = null;
        _bestTraceLapMs = 0;
        _lastRecordedPos = -1f;
        _lastCompletedLaps = -1;
        _currentLapDirty = false;
        _snapshot = null;
    }

    private void CompleteLap(int lastLapMs, int lapCountDelta)
    {
        bool traceComplete =
            _currentTrace.Count > 1 &&
            _currentTrace[0].Pos < CompleteTraceStartPos;

        bool promote =
            !_currentLapDirty &&
            lapCountDelta == 1 &&      // multi-lap jumps mean we missed data
            traceComplete &&
            lastLapMs > 0 &&
            (_bestTraceLapMs == 0 || lastLapMs < _bestTraceLapMs);

        if (promote)
        {
            var trace = new List<(float Pos, int TimeMs)>(_currentTrace)
            {
                (1.0f, lastLapMs)
            };
            _bestTrace = trace;
            _bestTraceLapMs = lastLapMs;
        }

        StartNewLap(dirty: false);
    }

    private void StartNewLap(bool dirty)
    {
        _currentTrace.Clear();
        _lastRecordedPos = -1f;
        _currentLapDirty = dirty;
    }

    /// <summary>
    /// Best-lap time at the given position via binary search + linear interpolation,
    /// clamped to the trace's first/last samples.
    /// </summary>
    private int InterpolateBestTime(float pos)
    {
        var trace = _bestTrace!;

        if (pos <= trace[0].Pos) return trace[0].TimeMs;
        if (pos >= trace[^1].Pos) return trace[^1].TimeMs;

        int lo = 0, hi = trace.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (trace[mid].Pos <= pos) lo = mid;
            else hi = mid;
        }

        var (p0, t0) = trace[lo];
        var (p1, t1) = trace[hi];
        float span = p1 - p0;
        if (span <= 0f) return t0;

        float frac = (pos - p0) / span;
        return t0 + (int)((t1 - t0) * frac);
    }
}
