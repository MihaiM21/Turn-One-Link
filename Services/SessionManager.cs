using System;
using Turn_One_Link.Services.AccTelemetry;

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
    AC_SESSION_TYPE SessionType,
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
    private AC_SESSION_TYPE _activeSessionType = AC_SESSION_TYPE.AC_UNKNOWN;
    private int _activeSessionIndex = -1;
    private string _activeTrack = "";
    private string _activeCarModel = "";
    private string _activeDriver = "";
    private int _completedLaps;
    private int _bestLapMs;

    private SPageFileStatic? _latestStatic;
    private bool _staticPendingForNewSession;

    public event Action<SessionInfo>? SessionStarted;
    public event Action<SessionEndInfo>? SessionEnded;
    public event Action<string>? SessionPaused;   // sessionId
    public event Action<string>? SessionResumed;  // sessionId
    public event Action? StaticRefreshRequested;

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

    public void OnGraphics(SPageFileGraphic g)
    {
        // Track lap stats for session_end payload
        if (g.CompletedLaps > _completedLaps) _completedLaps = g.CompletedLaps;
        if (g.iBestTime > 0 && g.iBestTime != int.MaxValue &&
            (_bestLapMs == 0 || g.iBestTime < _bestLapMs))
        {
            _bestLapMs = g.iBestTime;
        }

        switch (g.Status)
        {
            case AC_STATUS.AC_LIVE:
                HandleLive(g);
                break;
            case AC_STATUS.AC_PAUSE:
                HandlePause();
                break;
            case AC_STATUS.AC_REPLAY:
                EndIfActive();
                _phase = SessionPhase.Replay;
                break;
            case AC_STATUS.AC_OFF:
            default:
                EndIfActive();
                _phase = SessionPhase.Idle;
                break;
        }
    }

    public void OnStatic(SPageFileStatic s)
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

    private void HandleLive(SPageFileGraphic g)
    {
        bool sessionChanged =
            _activeSessionId == null ||
            g.Session != _activeSessionType ||
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

    private void BeginSession(SPageFileGraphic g)
    {
        _activeSessionId = Guid.NewGuid().ToString("N");
        _activeSessionType = g.Session;
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
        _activeSessionType = AC_SESSION_TYPE.AC_UNKNOWN;
        _activeSessionIndex = -1;
        _activeTrack = "";
        _activeCarModel = "";
        _activeDriver = "";
        _completedLaps = 0;
        _bestLapMs = 0;
        _staticPendingForNewSession = false;
    }
}
