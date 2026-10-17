using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Timers;
using Turn_One_Link.Services.Telemetry;
using Timer = System.Timers.Timer;

namespace Turn_One_Link.Services;

/// <summary>
/// Logs telemetry to a per-session JSONL file under %LOCALAPPDATA%\TurnOneLink\sessions\ for
/// post-race analysis. Writes are buffered (no per-line flush) since physics arrives at full
/// sim rate; a periodic timer plus explicit flushes at lap boundaries, session end, and app
/// shutdown bound how much data a crash could lose.
///
/// Applies a retention policy at each session start: delete sessions older than 30 days, then
/// trim to the newest 100 if still over that count.
/// </summary>
public sealed class SessionLoggingService : IDisposable
{
    private const int RetentionDays = 30;
    private const int RetentionMaxSessions = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TurnOneLink", "sessions");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Timer _flushTimer;
    private readonly object _writerLock = new();

    private StreamWriter? _writer;
    private string? _sessionId;
    private int _lastLapForSnapshot = -1;
    private SessionInfo? _activeSessionInfo;
    private int _completedLaps;
    private int _bestLapMs;

    private static bool Enabled => AppSettings.Load().EnableSessionLogging;

    public SessionLoggingService()
    {
        _flushTimer = new Timer(FlushInterval.TotalMilliseconds) { AutoReset = true };
        _flushTimer.Elapsed += (_, _) => FlushSafe();
    }

    public void OnSessionStarted(SessionInfo info)
    {
        if (!Enabled) return;

        RunRetentionCleanup();

        try
        {
            Directory.CreateDirectory(SessionsDir);
            lock (_writerLock)
            {
                _writer?.Dispose();
                _writer = new StreamWriter(Path.Combine(SessionsDir, $"{info.SessionId}.jsonl"), append: true);
            }
            _sessionId = info.SessionId;
            _activeSessionInfo = info;
            _completedLaps = 0;
            _bestLapMs = 0;
            _lastLapForSnapshot = -1;

            WriteLine("session_start", info);
            _flushTimer.Start();
        }
        catch { /* never let a logging failure break telemetry */ }
    }

    public void OnSessionEnded(SessionEndInfo info)
    {
        if (_writer == null) return;

        _completedLaps = info.CompletedLaps;
        _bestLapMs = info.BestLapMs;
        WriteLine("session_end", info);

        _flushTimer.Stop();
        WriteSummary(info);
        FlushAndClose();

        _sessionId = null;
        _activeSessionInfo = null;
    }

    public void OnGraphics(T1GraphicsFrame g, DeltaSnapshot? delta, FuelSnapshot? fuel)
    {
        if (_writer == null) return;

        WriteLine("graphics", g);
        if (g.CompletedLaps != _lastLapForSnapshot)
        {
            _lastLapForSnapshot = g.CompletedLaps;
            _completedLaps = g.CompletedLaps;
            if (g.BestLapMs > 0 && (_bestLapMs == 0 || g.BestLapMs < _bestLapMs))
                _bestLapMs = g.BestLapMs;

            if (delta != null) WriteLine("delta_snapshot", delta);
            if (fuel != null) WriteLine("fuel_snapshot", fuel);
            FlushSafe(); // lap-boundary flush
        }
    }

    public void OnPhysics(T1PhysicsFrame p)
    {
        if (_writer == null) return;
        WriteLine("physics", p); // full rate, no throttling — disk space is not a constraint
    }

    /// <summary>Best-effort flush on app shutdown so a crash loses at most a fraction of a
    /// second of buffered data.</summary>
    public void FlushOnShutdown() => FlushSafe();

    private void WriteLine<T>(string type, T data)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                type,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId = _sessionId,
                data
            }, JsonOptions);

            lock (_writerLock)
            {
                _writer?.WriteLine(json);
            }
        }
        catch { /* never let a logging failure break telemetry */ }
    }

    private void WriteSummary(SessionEndInfo info)
    {
        if (_activeSessionInfo == null) return;
        try
        {
            var summaryPath = Path.Combine(SessionsDir, $"{info.SessionId}.summary.json");
            var summary = new
            {
                sessionId = info.SessionId,
                sessionType = T1TelemetryMapper.SessionTypeWireName(_activeSessionInfo.SessionType),
                track = _activeSessionInfo.Track,
                carModel = _activeSessionInfo.CarModel,
                driver = _activeSessionInfo.DriverName,
                startedAt = _activeSessionInfo.StartedAt.ToUnixTimeMilliseconds(),
                endedAt = info.EndedAt.ToUnixTimeMilliseconds(),
                completedLaps = _completedLaps,
                bestLapMs = _bestLapMs
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, JsonOptions));
        }
        catch { }
    }

    private void FlushSafe()
    {
        try
        {
            lock (_writerLock)
            {
                _writer?.Flush();
            }
        }
        catch { }
    }

    private void FlushAndClose()
    {
        try
        {
            lock (_writerLock)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }
        catch { }
    }

    private static void RunRetentionCleanup()
    {
        try
        {
            if (!Directory.Exists(SessionsDir)) return;

            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var files = new DirectoryInfo(SessionsDir).GetFiles("*.jsonl")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            var stale = files.Where(f => f.LastWriteTimeUtc < cutoff).ToList();
            foreach (var f in stale) TryDeleteSessionFiles(f);

            var remaining = files.Except(stale).ToList();
            foreach (var f in remaining.Skip(RetentionMaxSessions)) TryDeleteSessionFiles(f);
        }
        catch { /* never block session start over cleanup failure */ }
    }

    private static void TryDeleteSessionFiles(FileInfo jsonl)
    {
        try
        {
            jsonl.Delete();
            var summaryPath = Path.Combine(
                jsonl.DirectoryName!,
                Path.GetFileNameWithoutExtension(jsonl.Name) + ".summary.json");
            if (File.Exists(summaryPath)) File.Delete(summaryPath);
        }
        catch { }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushAndClose();
    }
}
