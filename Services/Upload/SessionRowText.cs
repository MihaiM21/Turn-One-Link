using System.Globalization;

namespace Turn_One_Link.Services.Upload;

/// <summary>What a recent-session row says (LF-13): readable track, short detail line, upload state.</summary>
public static class SessionRowText
{
    public enum Tone { Done, Busy, Problem, Muted, Live }

    /// <summary>"Paul Ricard" from "Paul_Ricard"; the sim name when the track isn't known yet.</summary>
    public static string Title(SessionHistoryEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.Track)) return string.IsNullOrWhiteSpace(e.Sim) ? "Session" : e.Sim;
        var track = e.Track.Replace('_', ' ').Trim();
        // Sim ids are often lower-case ("monza", "spa"); keep names that already have capitals ("Paul Ricard").
        return track == track.ToLowerInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(track)
            : track;
    }

    /// <summary>"ACC · 3 laps · best 2:05.570".</summary>
    public static string Detail(SessionHistoryEntry e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Track) && !string.IsNullOrWhiteSpace(e.Sim)) parts.Add(e.Sim);
        parts.Add(e.Laps == 1 ? "1 lap" : $"{e.Laps} laps");
        if (e.BestLapMs is { } ms and > 0) parts.Add($"best {LapTime(ms)}");
        return string.Join(" · ", parts);
    }

    public static (string Text, Tone Tone) State(SessionHistoryEntry e)
    {
        if (!e.Ended) return ("Recording", Tone.Live);
        // A session without a single completed lap has nothing to upload; "Waiting" would never end.
        if (e.Laps == 0) return ("No laps", Tone.Muted);
        return e.Upload switch
        {
            UploadState.Uploaded => ("Uploaded", Tone.Done),
            UploadState.Uploading => ("Uploading…", Tone.Busy),
            UploadState.Failed => ("Retrying", Tone.Problem),
            _ => ("Queued", Tone.Busy),
        };
    }

    /// <summary>Only sessions that reached the Hub have a page to open.</summary>
    public static bool HasWebPage(SessionHistoryEntry e) => e.Ended && e.Laps > 0 && e.Upload == UploadState.Uploaded;

    public static string LapTime(int ms) => $"{ms / 60000}:{ms % 60000 / 1000:00}.{ms % 1000:000}";
}
