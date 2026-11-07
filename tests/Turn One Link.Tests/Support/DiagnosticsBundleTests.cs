using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Turn_One_Link.Services;
using Xunit;

namespace Turn_One_Link.Tests.Support;

/// <summary>LF-18: the "Report a problem" zip — what goes in, and what must never go in.</summary>
public sealed class DiagnosticsBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "t1-report-tests", Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    private string Out => Path.Combine(_root, "out");

    public DiagnosticsBundleTests()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{"LiveStreaming":false}""");
        File.WriteAllText(Path.Combine(Data, "sessions.json"), "[]");
        File.WriteAllText(Path.Combine(Data, "authstate.bin"), "secret-auth");
        File.WriteAllText(Path.Combine(Data, "overrides.env"), "API_BASE_URL=http://x");
        File.WriteAllText(Path.Combine(Data, "turnone_debug.log"),
            "[LOCAL] Server listening on 192.168.1.5:8080 | PIN: 790513\n" +
            "Authorization: Bearer abc.def-123\n" +
            "token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl here\n" +
            "dash http://192.168.1.5:8080/#pin=790513\n" +
            "[ACC] lap 3 completed\n");

        // Three sessions; only the two newest may be included.
        for (var i = 0; i < 3; i++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(Data, "outbox", $"session-{i}"));
            File.WriteAllText(Path.Combine(dir.FullName, "lap-001.ndjson"), "{}");
            Directory.SetLastWriteTimeUtc(dir.FullName, DateTime.UtcNow.AddMinutes(i - 10));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static Dictionary<string, string> Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public void Never_Includes_Sign_In_State_Or_Overrides()
    {
        var entries = Read(DiagnosticsBundle.Create(Data, Out, includeTelemetry: true));

        entries.Keys.Should().Contain(new[] { "diagnostics.txt", "turnone_debug.log", "settings.json", "sessions.json" });
        entries.Keys.Should().NotContain(k => k.Contains("authstate") || k.Contains("overrides"));
        entries.Values.Should().NotContain(v => v.Contains("secret-auth"));
    }

    [Fact]
    public void Redacts_Tokens_And_The_Lan_Pin_From_The_Log()
    {
        var log = Read(DiagnosticsBundle.Create(Data, Out, includeTelemetry: false))["turnone_debug.log"];

        log.Should().NotContain("790513").And.NotContain("abc.def-123").And.NotContain("eyJhbGci");
        log.Should().Contain("PIN: [redacted]").And.Contain("Bearer [redacted]").And.Contain("[ACC] lap 3 completed");
    }

    [Fact]
    public void Laps_Only_When_Allowed_And_Only_The_Newest_Sessions()
    {
        Read(DiagnosticsBundle.Create(Data, Out, includeTelemetry: false)).Keys
            .Should().NotContain(k => k.StartsWith("outbox/"));

        Thread.Sleep(1100); // zip names are per-second
        var withLaps = Read(DiagnosticsBundle.Create(Data, Out, includeTelemetry: true, extraInfo: "Sim: ACC"));

        withLaps.Keys.Where(k => k.StartsWith("outbox/")).Should().BeEquivalentTo(
            "outbox/session-2/lap-001.ndjson", "outbox/session-1/lap-001.ndjson");
        withLaps["diagnostics.txt"].Should().Contain("Turn One Link").And.Contain("Telemetry included: yes").And.Contain("Sim: ACC");
    }

    [Fact]
    public void Keeps_Only_The_End_Of_A_Huge_Log()
    {
        var log = Path.Combine(Data, "turnone_debug.log");
        using (var w = new StreamWriter(log))
        {
            var line = new string('x', 1000);
            for (var i = 0; i < DiagnosticsBundle.MaxLogBytes / 1000 + 500; i++) w.WriteLine(line);
            w.WriteLine("LAST LINE");
        }

        var text = Read(DiagnosticsBundle.Create(Data, Out, includeTelemetry: false))["turnone_debug.log"];
        text.Should().StartWith("[log truncated").And.Contain("LAST LINE");
        text.Length.Should().BeLessThanOrEqualTo(DiagnosticsBundle.MaxLogBytes + 200);
    }
}
