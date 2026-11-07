using System.IO;
using System.Text;
using System.Text.Json;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services.Upload;

/// <summary>
/// Local-first recording (D3, LF-11): writes every completed lap of the active session to the outbox
/// as an HCR-007 upload body — <c>session_start</c>, the lap's ticks plus ~2 s either side,
/// <c>lap_complete</c> — and the session's end as <c>end.json</c>. <see cref="UploadQueue"/> sends them.
/// Files are written atomically (temp + rename), so a crash never leaves half a lap in the outbox.
/// </summary>
public sealed class LapRecorder
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(2);

    private readonly string _outboxDir;
    private readonly Action<string>? _log;
    private readonly object _lock = new();

    private string? _sessionId;
    private DateTimeOffset _sessionStartedAt;
    private string? _sessionStartLine;
    private readonly HashSet<int> _lapsWritten = new();

    // (timestamp, lap, json) of recent ticks: the lap in progress and the one before it.
    private readonly List<(long Ts, int Lap, string Json)> _ticks = new();
    private int _lastLap = -1;

    /// <summary>Raised with the session id after a lap or end file lands in the outbox.</summary>
    public event Action<string>? OutboxChanged;

    public LapRecorder(string outboxDir, Action<string>? log = null)
    {
        _outboxDir = outboxDir;
        _log = log;
    }

    public static string DefaultOutboxDir => Path.Combine(AppPaths.DataDir, "outbox");

    public void OnSessionStarted(string sessionId)
    {
        lock (_lock)
        {
            _sessionId = sessionId;
            _sessionStartedAt = DateTimeOffset.UtcNow;
            _sessionStartLine = null;
            _lapsWritten.Clear();
            _ticks.Clear();
            _lastLap = -1;
        }
    }

    public void OnSessionInfo(T1SessionInfo info)
    {
        lock (_lock)
        {
            if (_sessionId == null) return;
            _sessionStartLine = V2Envelope.Serialize("session_start", V2Envelope.SessionStartData(info, _sessionStartedAt),
                _sessionId, _sessionStartedAt.ToUnixTimeMilliseconds());
        }
    }

    public void OnTick(T1TickFrame tick)
    {
        lock (_lock)
        {
            if (_sessionId == null) return;
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _ticks.Add((ts, tick.Lap, V2Envelope.Serialize("tick", tick, _sessionId, ts)));

            // Keep the lap in progress and the previous one (its lap_complete may still be on the way);
            // anything older can't be uploaded any more. Trimmed only when the lap changes.
            if (tick.Lap != _lastLap)
            {
                _lastLap = tick.Lap;
                _ticks.RemoveAll(t => t.Lap < tick.Lap - 1);
            }
        }
    }

    public void OnLapComplete(T1LapComplete lap)
    {
        string? sessionId;
        string body;
        lock (_lock)
        {
            sessionId = _sessionId;
            if (sessionId == null || _sessionStartLine == null) return;
            if (!_lapsWritten.Add(lap.Lap)) return;

            var lapTicks = _ticks.Where(t => t.Lap == lap.Lap).ToList();
            if (lapTicks.Count < 2) return;
            var from = lapTicks[0].Ts - (long)Margin.TotalMilliseconds;

            var sb = new StringBuilder(lapTicks.Count * 400);
            sb.Append(_sessionStartLine).Append('\n');
            foreach (var t in _ticks.Where(t => t.Ts >= from)) sb.Append(t.Json).Append('\n');
            sb.Append(V2Envelope.Serialize("lap_complete", lap, sessionId)).Append('\n');
            body = sb.ToString();
        }

        WriteAtomic(sessionId, $"lap-{lap.Lap:D4}.ndjson", body);
        _log?.Invoke($"[UPLOAD] Lap {lap.Lap} saved for upload.");
    }

    public void OnSessionEnded(string sessionId, DateTimeOffset endedAt, int lapsCompleted, int bestLapMs)
    {
        lock (_lock)
        {
            if (sessionId != _sessionId) return;
            _sessionId = null;
            _ticks.Clear();
        }

        var end = JsonSerializer.Serialize(new
        {
            end = new { endedAt = endedAt.ToUnixTimeMilliseconds(), lapsCompleted, bestLapMs },
        }, V2Envelope.JsonOptions);
        WriteAtomic(sessionId, "end.json", end);
    }

    private void WriteAtomic(string sessionId, string fileName, string content)
    {
        try
        {
            var dir = Path.Combine(_outboxDir, sessionId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            OutboxChanged?.Invoke(sessionId);
        }
        catch (IOException ex)
        {
            _log?.Invoke($"[UPLOAD] Couldn't save {fileName}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _log?.Invoke($"[UPLOAD] Couldn't save {fileName}: {ex.Message}");
        }
    }
}
