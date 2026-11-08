using FluentAssertions;
using Turn_One_Link.Services.Upload;
using Xunit;

namespace Turn_One_Link.Tests.Upload;

/// <summary>LF-13: what a recent-session row says.</summary>
public class SessionRowTextTests
{
    private static SessionHistoryEntry Entry(string track = "Paul_Ricard", int laps = 3, bool ended = true,
        UploadState upload = UploadState.Uploaded, int? best = 125570) => new()
    {
        SessionId = "s1", Sim = "ACC", Track = track, Laps = laps, Ended = ended, Upload = upload, BestLapMs = best,
    };

    [Theory]
    [InlineData("Paul_Ricard", "Paul Ricard")]
    [InlineData("monza", "Monza")]
    [InlineData("red_bull_ring", "Red Bull Ring")]
    [InlineData("", "ACC")]
    public void Title_Is_A_Readable_Track_Or_The_Sim(string track, string expected) =>
        SessionRowText.Title(Entry(track)).Should().Be(expected);

    [Fact]
    public void Detail_Has_Sim_Laps_And_Best()
    {
        SessionRowText.Detail(Entry()).Should().Be("ACC · 3 laps · best 2:05.570");
        SessionRowText.Detail(Entry(laps: 1, best: null)).Should().Be("ACC · 1 lap");
        SessionRowText.Detail(Entry(track: "", laps: 0, best: null)).Should().Be("0 laps");
    }

    [Fact]
    public void A_Finished_Session_Without_Laps_Says_So_Instead_Of_Waiting_Forever()
    {
        SessionRowText.State(Entry(laps: 0, upload: UploadState.Waiting)).Text.Should().Be("No laps");
        SessionRowText.HasWebPage(Entry(laps: 0, upload: UploadState.Waiting)).Should().BeFalse();
    }

    [Fact]
    public void States()
    {
        SessionRowText.State(Entry(ended: false, laps: 0)).Text.Should().Be("Recording");
        SessionRowText.State(Entry(upload: UploadState.Waiting)).Text.Should().Be("Queued");
        SessionRowText.State(Entry(upload: UploadState.Failed)).Text.Should().Be("Retrying");
        SessionRowText.State(Entry()).Text.Should().Be("Uploaded");
        SessionRowText.HasWebPage(Entry()).Should().BeTrue();
        SessionRowText.HasWebPage(Entry(upload: UploadState.Uploading)).Should().BeFalse();
    }
}
