using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turn_One_Link.Services.Upload;

/// <summary>One recorded session as the app lists it (LF-13).</summary>
public sealed class SessionHistoryEntry
{
    public string SessionId { get; set; } = "";
    public string Sim { get; set; } = "";
    public string Track { get; set; } = "";
    public string Car { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public int Laps { get; set; }
    public int? BestLapMs { get; set; }
    public bool Ended { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UploadState Upload { get; set; } = UploadState.Waiting;
}

/// <summary>
/// The last sessions Link recorded and where their upload stands, kept in
/// <c>%LOCALAPPDATA%\TurnOneLink\sessions.json</c> so the list survives restarts. Thread-safe;
/// <see cref="Changed"/> fires after every update.
/// </summary>
public sealed class SessionHistory
{
    public const int MaxEntries = 20;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly List<SessionHistoryEntry> _entries;

    public event Action? Changed;

    public SessionHistory(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public static string DefaultPath => Path.Combine(AppPaths.DataDir, "sessions.json");

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SessionHistoryEntry> Entries
    {
        get { lock (_lock) return _entries.Select(Clone).ToList(); }
    }

    public void Started(string sessionId, string sim, string track, string car) => Update(sessionId, e =>
    {
        e.Sim = sim;
        e.Track = track;
        e.Car = car;
    }, create: true);

    public void LapCompleted(string sessionId, int lap, int lapTimeMs, bool valid) => Update(sessionId, e =>
    {
        e.Laps = Math.Max(e.Laps, lap);
        if (valid && lapTimeMs > 0 && (e.BestLapMs is not { } best || lapTimeMs < best)) e.BestLapMs = lapTimeMs;
    });

    public void Ended(string sessionId) => Update(sessionId, e => e.Ended = true);

    public void UploadChanged(string sessionId, UploadState state) => Update(sessionId, e => e.Upload = state);

    private void Update(string sessionId, Action<SessionHistoryEntry> change, bool create = false)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.SessionId == sessionId);
            if (entry == null)
            {
                if (!create) return;
                entry = new SessionHistoryEntry { SessionId = sessionId, StartedAt = DateTimeOffset.UtcNow };
                _entries.Insert(0, entry);
                if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }
            change(entry);
            Save();
        }
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException) { /* the list is a convenience; never break recording over it */ }
        catch (UnauthorizedAccessException) { }
    }

    private static List<SessionHistoryEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<SessionHistoryEntry>>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    private static SessionHistoryEntry Clone(SessionHistoryEntry e) => new()
    {
        SessionId = e.SessionId, Sim = e.Sim, Track = e.Track, Car = e.Car, StartedAt = e.StartedAt,
        Laps = e.Laps, BestLapMs = e.BestLapMs, Ended = e.Ended, Upload = e.Upload,
    };
}
