using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Turn_One_Link.Services;
using Turn_One_Link.Services.Telemetry;
using Turn_One_Link.Services.Upload;
using Xunit;

namespace Turn_One_Link.Tests.Upload;

/// <summary>LF-11 / LF-12: laps written to the outbox in the HCR-007 format and uploaded reliably.</summary>
public sealed class LocalFirstUploadTests : IDisposable
{
    private readonly string _outbox = Path.Combine(Path.GetTempPath(), "t1-outbox-tests", Guid.NewGuid().ToString("N"));
    private const string SessionId = "0123456789abcdef0123456789abcdef";

    public void Dispose()
    {
        try { Directory.Delete(_outbox, recursive: true); } catch (IOException) { }
    }

    private static T1SessionInfo Info() => new(
        Source: "acc", SimVersion: null, TrackId: "spa", TrackName: "Spa", TrackLengthM: 7004, SectorCount: 3,
        SectorBoundariesM: null, CarId: "ferrari_296_gt3", CarName: "Ferrari 296 GT3", CarClass: null,
        SessionType: T1SessionType.Practice, SessionTypeRaw: "AC_PRACTICE", Driver: "Test Driver", TickRateHz: 20);

    private static T1TickFrame Tick(int lap, int lapTimeMs) => new(
        Lap: lap, LapTimeMs: lapTimeMs, LapDistM: lapTimeMs / 20f, Sector: 0, Speed: 200, Rpm: 7000, Gear: 4,
        Throttle: 1, Brake: 0, Clutch: 0, Steer: 0, SteerDeg: null, GLat: 0, GLong: 0, GVert: 0,
        PosX: null, PosY: null, PosZ: null, Heading: 0, Valid: true, Pit: 0, DriverStatus: null, Fuel: 50,
        Tc: null, Abs: null, BrakeBias: null, Drs: null, Ers: null, ErsMode: null, TyreCompound: null, TyreAgeLaps: null,
        TyreTemp: null, TyrePress: null, TyreWear: null, BrakeTemp: null, Slip: null, Frame: lapTimeMs);

    private static T1LapComplete Complete(int lap) => new(
        Lap: lap, LapTimeMs: 139_512, SectorsMs: new[] { 42_000, 51_000, 46_512 }, Valid: true, Pit: false,
        LapDistM: 7004, FuelUsedL: 2.1f, TyreCompound: null);

    private LapRecorder RecordLap(int lap)
    {
        var recorder = new LapRecorder(_outbox);
        recorder.OnSessionStarted(SessionId);
        recorder.OnSessionInfo(Info());
        for (int t = 0; t < 30; t++) recorder.OnTick(Tick(lap - 1, 130_000 + t * 50));
        for (int t = 0; t < 30; t++) recorder.OnTick(Tick(lap, t * 50));
        recorder.OnTick(Tick(lap + 1, 10));
        recorder.OnLapComplete(Complete(lap));
        return recorder;
    }

    [Fact]
    public void Recorder_Writes_The_Lap_In_The_Upload_Format()
    {
        RecordLap(3);

        var file = Path.Combine(_outbox, SessionId, "lap-0003.ndjson");
        File.Exists(file).Should().BeTrue();
        var frames = File.ReadAllLines(file).Select(l => JsonDocument.Parse(l).RootElement).ToList();

        frames.First().GetProperty("type").GetString().Should().Be("session_start");
        frames.Last().GetProperty("type").GetString().Should().Be("lap_complete");
        frames.Should().OnlyContain(f => f.GetProperty("v").GetInt32() == 2 && f.GetProperty("sessionId").GetString() == SessionId);
        frames.Count(f => f.GetProperty("type").GetString() == "tick" && f.GetProperty("data").GetProperty("lap").GetInt32() == 3)
            .Should().Be(30);
        frames.First().GetProperty("data").GetProperty("trackId").GetString().Should().Be("spa");
    }

    [Fact]
    public void Recorder_Writes_Each_Lap_Once_And_The_Session_End()
    {
        var recorder = RecordLap(3);
        recorder.OnLapComplete(Complete(3));
        recorder.OnSessionEnded(SessionId, DateTimeOffset.UtcNow, 3, 139_512);

        Directory.GetFiles(Path.Combine(_outbox, SessionId)).Select(Path.GetFileName)
            .Should().BeEquivalentTo("lap-0003.ndjson", "end.json");
    }

    // ── Queue ────────────────────────────────────────────────────────────────

    private sealed class FakeHub : HttpMessageHandler
    {
        public Func<HttpRequestMessage, byte[], HttpResponseMessage> Respond { get; set; } = (_, _) => new(HttpStatusCode.Accepted);
        public List<(string Path, string? Key, byte[] Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var raw = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct);
            if (request.Content?.Headers.ContentEncoding.Contains("br") == true)
            {
                using var br = new BrotliStream(new MemoryStream(raw), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                await br.CopyToAsync(ms, ct);
                raw = ms.ToArray();
            }
            request.Headers.TryGetValues("Idempotency-Key", out var keys);
            Requests.Add((request.RequestUri!.AbsolutePath, keys?.FirstOrDefault(), raw));
            return Respond(request, raw);
        }
    }

    private static async Task<AuthService> SignedInAuthAsync()
    {
        var server = new PairingServer();
        var auth = new AuthService("https://api.test/api", server) { SaveState = _ => Task.CompletedTask, ClearState = () => { } };
        var code = new PairingCode("dev", "K7QF-2M9D", "https://x", TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow.AddMinutes(5));
        (await auth.PollPairingAsync(code)).Should().Be(PairingPollStatus.Approved);
        return auth;
    }

    private sealed class PairingServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/poll")
                    ? $"{{\"accessToken\":\"a\",\"deviceToken\":\"d\",\"accessTokenExpiresAt\":\"{DateTimeOffset.UtcNow.AddMinutes(15):O}\"}}"
                    : "{\"username\":\"driver\"}", Encoding.UTF8, "application/json"),
            });
    }

    [Fact]
    public async Task Queue_Uploads_Laps_Then_Closes_The_Session_And_Clears_The_Outbox()
    {
        var recorder = RecordLap(3);
        recorder.OnSessionEnded(SessionId, DateTimeOffset.UtcNow, 3, 139_512);
        var hub = new FakeHub();
        hub.Respond = (r, _) => r.RequestUri!.AbsolutePath.EndsWith("/end")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"missing\":[]}") }
            : new HttpResponseMessage(HttpStatusCode.Accepted);
        using var queue = new UploadQueue(_outbox, await SignedInAuthAsync(), handler: hub);

        (await queue.DrainOnceAsync()).Should().BeTrue();

        hub.Requests.Select(r => r.Path).Should().Equal(
            "/api/simracing/uploads/laps", $"/api/simracing/uploads/sessions/{SessionId}/end");
        hub.Requests[0].Key.Should().StartWith($"{SessionId}:3:");
        Encoding.UTF8.GetString(hub.Requests[0].Body).Should().Contain("\"lap_complete\"");
        Encoding.UTF8.GetString(hub.Requests[1].Body).Should().Contain("\"lapNumber\":3");
        Directory.Exists(Path.Combine(_outbox, SessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task Offline_Keeps_The_Lap_For_Later()
    {
        RecordLap(3);
        var hub = new FakeHub { Respond = (_, _) => throw new HttpRequestException("offline") };
        using var queue = new UploadQueue(_outbox, await SignedInAuthAsync(), handler: hub);

        (await queue.DrainOnceAsync()).Should().BeFalse();

        File.Exists(Path.Combine(_outbox, SessionId, "lap-0003.ndjson")).Should().BeTrue();
    }

    [Fact]
    public async Task Rejected_Lap_Is_Set_Aside_And_Not_Retried()
    {
        RecordLap(3);
        var hub = new FakeHub { Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.Conflict) };
        using var queue = new UploadQueue(_outbox, await SignedInAuthAsync(), handler: hub);

        await queue.DrainOnceAsync();
        await queue.DrainOnceAsync();

        hub.Requests.Should().ContainSingle();
        File.Exists(Path.Combine(_outbox, SessionId, "rejected", "lap-0003.ndjson")).Should().BeTrue();
    }

    [Fact]
    public async Task Laps_The_Server_Reports_Missing_Are_Sent_Again()
    {
        var recorder = RecordLap(3);
        recorder.OnSessionEnded(SessionId, DateTimeOffset.UtcNow, 3, 139_512);
        var endCalls = 0;
        var hub = new FakeHub();
        hub.Respond = (r, _) => r.RequestUri!.AbsolutePath.EndsWith("/end")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(++endCalls == 1 ? "{\"missing\":[3]}" : "{\"missing\":[]}") }
            : new HttpResponseMessage(HttpStatusCode.Accepted);
        using var queue = new UploadQueue(_outbox, await SignedInAuthAsync(), handler: hub);

        (await queue.DrainOnceAsync()).Should().BeFalse();  // lap 3 re-queued
        (await queue.DrainOnceAsync()).Should().BeTrue();

        hub.Requests.Count(r => r.Path.EndsWith("/laps")).Should().Be(2);
        Directory.Exists(Path.Combine(_outbox, SessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_Is_Sent_While_Uploads_Are_Paused_For_An_Update()
    {
        RecordLap(3);
        var hub = new FakeHub();
        using var queue = new UploadQueue(_outbox, await SignedInAuthAsync(), uploadsAllowed: () => false, handler: hub);

        await queue.DrainOnceAsync();

        hub.Requests.Should().BeEmpty();
    }
}
