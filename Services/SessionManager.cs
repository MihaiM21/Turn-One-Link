using System;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

public enum SessionPhase
{
    Idle,
    Live,
    Paused,
    Replay
}

public sealed record SessionInfo(
    string SessionId,
    T1SessionType SessionType,
    string Track,
    string CarModel,
    string DriverName,
    DateTimeOffset StartedAt);

public sealed record SessionEndInfo(
    string SessionId,
    DateTimeOffset EndedAt,
    int CompletedLaps,
    int BestLapMs);

/// <summary>
/// Translates raw ACC graphics/static frames into semantic session lifecycle events.
/// Owns the active sessionId so all outgoing telemetry frames can be tagged consistently.
/// </summary>
public class SessionManager
{
    private readonly Func<DateTimeOffset> _now;

    private SessionPhase _phase = SessionPhase.Idle;
    private string? _activeSessionId;
    private T1SessionType _activeSessionType = T1SessionType.Unknown;
    private int _activeSessionIndex = -1;
    private string _activeTrack = "";
    private string _activeCarModel = "";
    private string _activeDriver = "";
    private int _completedLaps;
    private int _bestLapMs;

    private T1StaticFrame? _latestStatic;
    private bool _staticPendingForNewSession;

    // Cloud protocol v2 identity rule (docs/telemetry/backend_contract_v2.md §1): a new
    // sessionId opens whenever source/track/car/session-type changes, on top of the v1
    // track/car/session-index checks in HandleLive below (which already also catch the
    // "status passes through Off" case via EndIfActive on T1SimStatus.Off).
    private string? _activeV2IdentityKey;

    public event Action<SessionInfo>? SessionStarted;
    public event Action<SessionEndInfo>? SessionEnded;
    public event Action<string>? SessionPaused;   // sessionId
    public event Action<string>? SessionResumed;  // sessionId
    public event Action? StaticRefreshRequested;

    /// <summary>Fired once per active session with its v2 <see cref="T1SessionInfo"/>, whenever
    /// that identity is new for the session (first sight, or a forced mid-session re-key).</summary>
    public event Action<T1SessionInfo>? SessionInfoReadyV2;

    public SessionManager(Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string? ActiveSessionId => _activeSessionId;
    public SessionPhase Phase => _phase;

    /// <summary>True if the current phase should produce physics frames.</summary>
    public bool ShouldStreamPhysics => _phase == SessionPhase.Live;

    /// <summary>True if the current phase should produce graphics frames (still useful while paused).</summary>
    public bool ShouldStreamGraphics => _phase == SessionPhase.Live || _phase == SessionPhase.Paused;

    public void OnGraphics(T1GraphicsFrame g)
    {
        // Track lap stats for session_end payload
        if (g.CompletedLaps > _completedLaps) _completedLaps = g.CompletedLaps;
        if (g.BestLapMs > 0 && g.BestLapMs != int.MaxValue &&
            (_bestLapMs == 0 || g.BestLapMs < _bestLapMs))
        {
            _bestLapMs = g.BestLapMs;
        }

        switch (g.Status)
        {
            case T1SimStatus.Live:
                HandleLive(g);
                break;
            case T1SimStatus.Paused:
                HandlePause();
                break;
            case T1SimStatus.Replay:
                EndIfActive();
                _phase = SessionPhase.Replay;
                break;
            case T1SimStatus.Off:
            default:
                EndIfActive();
                _phase = SessionPhase.Idle;
                break;
        }
    }

    public void OnStatic(T1StaticFrame s)
    {
        _latestStatic = s;

        // If a session was waiting on a fresh static read, finalize start now
        if (_staticPendingForNewSession && _phase == SessionPhase.Live && _activeSessionId != null)
        {
            _staticPendingForNewSession = false;
            _activeTrack = s.Track ?? "";
            _activeCarModel = s.CarModel ?? "";
            _activeDriver = $"{s.PlayerName} {s.PlayerSurname}".Trim();

            SessionStarted?.Invoke(new SessionInfo(
                _activeSessionId,
                _activeSessionType,
                _activeTrack,
                _activeCarModel,
                _activeDriver,
                _now()));
        }
    }

    /// <summary>Sim disconnected entirely — close any active session.</summary>
    public void OnSimDisconnected()
    {
        EndIfActive();
        _phase = SessionPhase.Idle;
        _latestStatic = null;
    }

    /// <summary>
    /// v2 identity rule entry point: called from each <see cref="ITelemetrySource.SessionInfoUpdated"/>.
    /// If a session is already active and this identity (source/track/car/sessionType) differs
    /// from the one that session was opened with, that's a mid-session sim/track/car swap the v1
    /// graphics-based check might not catch (e.g. iRacing session-to-session with no v1 status
    /// blip) — end the old session and start a new one so `(sessionId, lap)` stays unambiguous.
    /// Otherwise, once a session is active, raises <see cref="SessionInfoReadyV2"/> exactly once
    /// per identity so the v2 `session_start` can be built from richer per-sim data than the v1
    /// static/graphics frames provide.
    /// </summary>
    public void OnSessionInfoV2(T1SessionInfo info)
    {
        var key = $"{info.Source}|{info.TrackId}|{info.CarId}|{info.SessionType}";

        if (_activeSessionId != null && _activeV2IdentityKey != null && _activeV2IdentityKey != key)
        {
            EndIfActive();
            _activeV2IdentityKey = null;
        }

        if (_activeSessionId != null && _activeV2IdentityKey != key)
        {
            _activeV2IdentityKey = key;
            SessionInfoReadyV2?.Invoke(info);
        }
    }

    private void HandleLive(T1GraphicsFrame g)
    {
        bool sessionChanged =
            _activeSessionId == null ||
            g.SessionType != _activeSessionType ||
            g.SessionIndex != _activeSessionIndex ||
            (_latestStatic.HasValue && (
                (_latestStatic.Value.Track ?? "") != _activeTrack && _activeTrack.Length > 0 ||
                (_latestStatic.Value.CarModel ?? "") != _activeCarModel && _activeCarModel.Length > 0));

        if (sessionChanged)
        {
            EndIfActive();
            BeginSession(g);
        }
        else if (_phase == SessionPhase.Paused)
        {
            _phase = SessionPhase.Live;
            if (_activeSessionId != null)
                SessionResumed?.Invoke(_activeSessionId);
        }
        else
        {
            _phase = SessionPhase.Live;
        }
    }

    private void HandlePause()
    {
        if (_phase == SessionPhase.Live && _activeSessionId != null)
        {
            _phase = SessionPhase.Paused;
            SessionPaused?.Invoke(_activeSessionId);
        }
        else if (_phase == SessionPhase.Idle)
        {
            // Paused on the menu — treat as idle, no session
            _phase = SessionPhase.Idle;
        }
    }

    private void BeginSession(T1GraphicsFrame g)
    {
        _activeSessionId = Guid.NewGuid().ToString("N");
        _activeSessionType = g.SessionType;
        _activeSessionIndex = g.SessionIndex;
        _completedLaps = g.CompletedLaps;
        _bestLapMs = 0;
        _phase = SessionPhase.Live;

        // Force a fresh static read; emit session_start once we have it.
        _staticPendingForNewSession = true;
        StaticRefreshRequested?.Invoke();

        // If we already have a static block cached, emit immediately so the backend
        // doesn't have to wait. The pending flag will clear on the next OnStatic call.
        if (_latestStatic.HasValue)
        {
            var s = _latestStatic.Value;
            _activeTrack = s.Track ?? "";
            _activeCarModel = s.CarModel ?? "";
            _activeDriver = $"{s.PlayerName} {s.PlayerSurname}".Trim();
            SessionStarted?.Invoke(new SessionInfo(
                _activeSessionId,
                _activeSessionType,
                _activeTrack,
                _activeCarModel,
                _activeDriver,
                _now()));
        }
    }

    private void EndIfActive()
    {
        if (_activeSessionId == null) return;

        SessionEnded?.Invoke(new SessionEndInfo(
            _activeSessionId,
            _now(),
            _completedLaps,
            _bestLapMs));

        _activeSessionId = null;
        _activeSessionType = T1SessionType.Unknown;
        _activeSessionIndex = -1;
        _activeTrack = "";
        _activeCarModel = "";
        _activeDriver = "";
        _completedLaps = 0;
        _bestLapMs = 0;
        _staticPendingForNewSession = false;
        _activeV2IdentityKey = null;
    }
}
