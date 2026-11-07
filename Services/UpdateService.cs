using System.Net.Http;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Turn_One_Link.Services;

/// <summary>
/// Checks GitHub Releases for a newer build of the installed channel (stable / beta), downloads it in
/// the background and applies it after Turn One Link exits — never in the middle of a session.
/// Does nothing when running from a dev build (not installed by Velopack).
/// Also asks the Hub whether this build is still supported (HCR-014 <c>compat</c>) — that check runs
/// for every build, installed or not.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string RepoUrl = "https://github.com/MihaiM21/Turn-One-Link";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    // prerelease: true so beta installs see beta releases; Velopack only offers packages of the
    // channel the app was installed from, so stable installs still only get stable builds.
    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, prerelease: true));
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Action<string> _log;
    private readonly string? _apiBaseUrl;
    private System.Threading.Timer? _timer;
    private bool _staged;

    public UpdateService(Action<string> log, string? apiBaseUrl = null)
    {
        _log = log;
        _apiBaseUrl = apiBaseUrl;
    }

    /// <summary>False once the Hub says this version may no longer upload laps (until updated).</summary>
    public bool UploadsAccepted { get; private set; } = true;

    /// <summary>The Hub's "please update" message, if any — shown again when the dashboard opens.</summary>
    public string? CompatMessage { get; private set; }

    public void Start()
    {
        _ = CheckCompatAsync();
        if (!_manager.IsInstalled) return;
        _timer = new System.Threading.Timer(_ => _ = CheckAsync(), null, TimeSpan.FromSeconds(30), CheckInterval);
    }

    private async Task CheckAsync()
    {
        if (_staged) return;
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update == null) return;

            var version = update.TargetFullRelease.Version.ToString();
            _log($"[UPDATE] Downloading version {version}...");
            await _manager.DownloadUpdatesAsync(update);

            // Applied silently once the app exits; the next launch runs the new version.
            _manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
            _staged = true;
            _log($"[UPDATE] Version {version} is ready — it installs the next time you close Turn One Link.");
        }
        catch (Exception ex)
        {
            _log($"[UPDATE] Update check failed: {ex.Message}");
        }
    }

    private async Task CheckCompatAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiBaseUrl)) return;
        try
        {
            var url = $"{_apiBaseUrl}/simracing/link/compat?version={Uri.EscapeDataString(AppInfo.Version)}";
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return; // older Hub without the endpoint: assume fine

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement;
            UploadsAccepted = !root.TryGetProperty("uploadsAccepted", out var u) || u.GetBoolean();
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                CompatMessage = m.GetString();
                _log($"[UPDATE] {CompatMessage}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Offline or unexpected body: never block the app on this check.
        }
    }

    public void Dispose() => _timer?.Dispose();
}
