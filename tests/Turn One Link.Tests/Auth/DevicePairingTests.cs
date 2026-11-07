using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Turn_One_Link.Services;
using Xunit;

namespace Turn_One_Link.Tests.Auth;

/// <summary>LF-10 / HCR-006: sign in by approving a code, then renew the access token by itself.</summary>
public class DevicePairingTests
{
    private sealed class FakeServer : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handle { get; set; } = _ => new(HttpStatusCode.NotFound);
        public List<string> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(Handle(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Tokens(string access, string device, DateTimeOffset expires) =>
        $"{{\"deviceId\":\"{Guid.NewGuid()}\",\"accessToken\":\"{access}\",\"accessTokenExpiresAt\":\"{expires:O}\",\"deviceToken\":\"{device}\"}}";

    private static (AuthService auth, FakeServer server, List<StoredAuthState> saved) Create()
    {
        var server = new FakeServer();
        var saved = new List<StoredAuthState>();
        var auth = new AuthService("https://api.test/api", server)
        {
            SaveState = s => { saved.Add(s); return Task.CompletedTask; },
            ClearState = () => { },
        };
        return (auth, server, saved);
    }

    [Fact]
    public async Task Pairing_Signs_In_Once_The_Code_Is_Approved()
    {
        var (auth, server, saved) = Create();
        var approved = false;
        server.Handle = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/devices/pair/start" => Json(HttpStatusCode.OK,
                "{\"deviceCode\":\"dev-secret\",\"userCode\":\"K7QF-2M9D\",\"verifyUrlComplete\":\"https://turnonehub.com/link?code=K7QF-2M9D\",\"expiresIn\":600,\"interval\":5}"),
            "/api/devices/pair/poll" => approved
                ? Json(HttpStatusCode.OK, Tokens("access-1", "device-1", DateTimeOffset.UtcNow.AddMinutes(15)))
                : Json((HttpStatusCode)428, "{\"status\":\"pending\"}"),
            "/api/auth/me" => Json(HttpStatusCode.OK, "{\"email\":\"d@t.t\",\"username\":\"driver\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        var (code, error) = await auth.StartPairingAsync();
        error.Should().BeNull();
        code!.UserCode.Should().Be("K7QF-2M9D");
        code.VerifyUrl.Should().Contain("code=K7QF-2M9D");

        (await auth.PollPairingAsync(code)).Should().Be(PairingPollStatus.Pending);
        auth.IsAuthenticated.Should().BeFalse();

        approved = true;
        (await auth.PollPairingAsync(code)).Should().Be(PairingPollStatus.Approved);

        auth.IsAuthenticated.Should().BeTrue();
        auth.AccessToken.Should().Be("access-1");
        auth.CurrentSession!.DisplayName.Should().Be("driver");
        saved.Should().ContainSingle().Which.DeviceToken.Should().Be("device-1");
    }

    [Fact]
    public async Task Expired_Code_Is_Reported()
    {
        var (auth, server, _) = Create();
        server.Handle = _ => Json(HttpStatusCode.BadRequest, "{\"status\":\"expired\"}");
        var code = new PairingCode("dev", "K7QF-2M9D", "https://x", TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow.AddMinutes(5));

        (await auth.PollPairingAsync(code)).Should().Be(PairingPollStatus.Expired);
    }

    private static async Task<(AuthService auth, FakeServer server, List<StoredAuthState> saved)> PairedAsync(DateTimeOffset accessExpires)
    {
        var (auth, server, saved) = Create();
        server.Handle = r => r.RequestUri!.AbsolutePath == "/api/devices/pair/poll"
            ? Json(HttpStatusCode.OK, Tokens("access-1", "device-1", accessExpires))
            : Json(HttpStatusCode.OK, "{\"username\":\"driver\"}");
        var code = new PairingCode("dev", "K7QF-2M9D", "https://x", TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow.AddMinutes(5));
        (await auth.PollPairingAsync(code)).Should().Be(PairingPollStatus.Approved);
        server.Calls.Clear();
        saved.Clear();
        return (auth, server, saved);
    }

    [Fact]
    public async Task Fresh_Token_Is_Not_Renewed()
    {
        var (auth, server, _) = await PairedAsync(DateTimeOffset.UtcNow.AddMinutes(14));

        (await auth.EnsureFreshTokenAsync()).Should().BeTrue();
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Expiring_Token_Is_Renewed_With_The_Device_Token()
    {
        var (auth, server, saved) = await PairedAsync(DateTimeOffset.UtcNow.AddSeconds(30));
        server.Handle = r => r.RequestUri!.AbsolutePath == "/api/devices/token/refresh"
            ? Json(HttpStatusCode.OK, Tokens("access-2", "device-2", DateTimeOffset.UtcNow.AddMinutes(15)))
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        (await auth.EnsureFreshTokenAsync()).Should().BeTrue();

        auth.AccessToken.Should().Be("access-2");
        saved.Should().ContainSingle().Which.DeviceToken.Should().Be("device-2");
        server.Calls.Should().Equal("POST /api/devices/token/refresh");
    }

    [Fact]
    public async Task Device_Revoked_On_The_Website_Means_Sign_In_Again()
    {
        var (auth, server, _) = await PairedAsync(DateTimeOffset.UtcNow.AddSeconds(30));
        server.Handle = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        (await auth.EnsureFreshTokenAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Offline_Keeps_A_Token_That_Has_Not_Expired_Yet()
    {
        var (auth, server, _) = await PairedAsync(DateTimeOffset.UtcNow.AddSeconds(30));
        server.Handle = _ => throw new HttpRequestException("offline");

        (await auth.EnsureFreshTokenAsync()).Should().BeTrue();
        auth.AccessToken.Should().Be("access-1");
    }
}
