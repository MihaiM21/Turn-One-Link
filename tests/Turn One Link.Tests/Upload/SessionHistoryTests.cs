using System.IO;
using FluentAssertions;
using Turn_One_Link.Services.Upload;
using Xunit;

namespace Turn_One_Link.Tests.Upload;

/// <summary>LF-13: the recent-sessions list and its upload state, kept across restarts.</summary>
public sealed class SessionHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "t1-history-tests", Guid.NewGuid().ToString("N"), "sessions.json");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Tracks_Laps_Best_Valid_Lap_And_Upload_State()
    {
        var history = new SessionHistory(_path);
        history.Started("s1", "ACC", "spa", "ferrari_296_gt3");
        history.LapCompleted("s1", 1, 140_000, valid: true);
        history.LapCompleted("s1", 2, 135_000, valid: false); // faster but invalid: not the best
        history.LapCompleted("s1", 3, 138_500, valid: true);
        history.Ended("s1");
        history.UploadChanged("s1", UploadState.Uploaded);

        var entry = history.Entries.Single();
        entry.Laps.Should().Be(3);
        entry.BestLapMs.Should().Be(138_500);
        entry.Ended.Should().BeTrue();
        entry.Upload.Should().Be(UploadState.Uploaded);
    }

    [Fact]
    public void Survives_A_Restart_Newest_First()
    {
        var history = new SessionHistory(_path);
        history.Started("old", "F1", "monza", "f1_team_0");
        history.Started("new", "ACC", "spa", "bmw_m4_gt3");
        history.UploadChanged("old", UploadState.Failed);

        var reloaded = new SessionHistory(_path);

        reloaded.Entries.Select(e => e.SessionId).Should().Equal("new", "old");
        reloaded.Entries.Last().Upload.Should().Be(UploadState.Failed);
    }

    [Fact]
    public void Keeps_Only_The_Latest_Sessions_And_Ignores_Unknown_Ones()
    {
        var history = new SessionHistory(_path);
        for (int i = 0; i < SessionHistory.MaxEntries + 5; i++) history.Started($"s{i}", "ACC", "spa", "car");
        history.UploadChanged("never-started", UploadState.Uploaded);

        history.Entries.Should().HaveCount(SessionHistory.MaxEntries);
        history.Entries.First().SessionId.Should().Be($"s{SessionHistory.MaxEntries + 4}");
        history.Entries.Should().NotContain(e => e.SessionId == "never-started");
    }

    [Fact]
    public void A_Corrupt_File_Starts_An_Empty_List()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json");

        new SessionHistory(_path).Entries.Should().BeEmpty();
    }
}
