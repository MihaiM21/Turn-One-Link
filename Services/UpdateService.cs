using Velopack;
using Velopack.Sources;

namespace Turn_One_Link.Services;

/// <summary>
/// Checks GitHub Releases for a newer build of the installed channel (stable / beta), downloads it in
/// the background and applies it after Turn One Link exits — never in the middle of a session.
/// Does nothing when running from a dev build (not installed by Velopack).
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string RepoUrl = "https://github.com/MihaiM21/Turn-One-Link";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    // prerelease: true so beta installs see beta releases; Velopack only offers packages of the
    // channel the app was installed from, so stable installs still only get stable builds.
    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, prerelease: true));
    private readonly Action<string> _log;
    private System.Threading.Timer? _timer;
    private bool _staged;

    public UpdateService(Action<string> log)
    {
        _log = log;
    }

    public void Start()
    {
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

    public void Dispose() => _timer?.Dispose();
}
