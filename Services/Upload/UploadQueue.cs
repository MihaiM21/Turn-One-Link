using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Turn_One_Link.Services.Upload;

public enum UploadState { Waiting, Uploading, Uploaded, Failed }

/// <summary>
/// Sends the outbox to the Hub (HCR-007, LF-12): each lap file as soon as it lands, then the session's
/// <c>end.json</c>. Survives restarts (the outbox is on disk), retries with back-off while offline,
/// and treats every response deliberately:
/// 2xx → done · 409/422 → rejected (moved aside, never retried) · 401 → wait for sign-in ·
/// anything else → retry later.
/// </summary>
public sealed class UploadQueue : IDisposable
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly string _outboxDir;
    private readonly AuthService _auth;
    private readonly HttpClient _http;
    private readonly Func<bool> _uploadsAllowed;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private TimeSpan _backoff = MinBackoff;

    /// <summary>(sessionId, state, laps sent, laps waiting) whenever a session's upload state changes.</summary>
    public event Action<string, UploadState, int, int>? SessionStateChanged;

    public UploadQueue(string outboxDir, AuthService auth, Func<bool>? uploadsAllowed = null,
        Action<string>? log = null, HttpMessageHandler? handler = null)
    {
        _outboxDir = outboxDir;
        _auth = auth;
        _uploadsAllowed = uploadsAllowed ?? (() => true);
        _log = log;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Start() => _loop ??= Task.Run(() => LoopAsync(_cts.Token));

    /// <summary>Something new is in the outbox (or sign-in changed): upload now rather than at the next retry.</summary>
    public void Kick()
    {
        _backoff = MinBackoff;
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool idle;
            try
            {
                idle = await DrainOnceAsync(token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log?.Invoke($"[UPLOAD] {ex.Message}");
                idle = false;
            }

            if (!idle) _backoff = TimeSpan.FromSeconds(Math.Min(_backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
            try { await _wake.WaitAsync(idle ? Timeout.InfiniteTimeSpan : _backoff, token); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One pass over the outbox. True when everything that could be sent was sent.</summary>
    public async Task<bool> DrainOnceAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(_outboxDir)) return true;
        if (!_uploadsAllowed()) return true; // the Hub asked for an update first; keep the files
        if (!_auth.IsAuthenticated || !await _auth.EnsureFreshTokenAsync()) return true; // resumes on Kick after sign-in

        bool allDone = true;
        foreach (var dir in Directory.GetDirectories(_outboxDir).OrderBy(Directory.GetCreationTimeUtc))
        {
            token.ThrowIfCancellationRequested();
            var result = await DrainSessionAsync(dir, token);
            if (result == DrainResult.Stop) return false;
            if (result == DrainResult.Pending) allDone = false;
        }
        return allDone;
    }

    private enum DrainResult { Done, Pending, Stop }

    private async Task<DrainResult> DrainSessionAsync(string dir, CancellationToken token)
    {
        var sessionId = Path.GetFileName(dir);
        var sentDir = Path.Combine(dir, "sent");
        var laps = Directory.GetFiles(dir, "lap-*.ndjson").OrderBy(f => f).ToList();
        int sent() => Directory.Exists(sentDir) ? Directory.GetFiles(sentDir, "lap-*.ndjson").Length : 0;

        if (laps.Count > 0) SessionStateChanged?.Invoke(sessionId, UploadState.Uploading, sent(), laps.Count);

        foreach (var lapFile in laps)
        {
            var status = await PostLapAsync(lapFile, token);
            switch (status)
            {
                case >= 200 and < 300:
                    MoveInto(lapFile, sentDir);
                    break;
                case 409 or 422 or 413 or 400:
                    MoveInto(lapFile, Path.Combine(dir, "rejected"));
                    _log?.Invoke($"[UPLOAD] {Path.GetFileName(lapFile)} of session {sessionId[..8]} was rejected ({status}).");
                    break;
                case 401:
                    return DrainResult.Stop;
                case 426:
                    // The Hub refuses this build's laps (known bug fixed in a newer version): keep them for after the update.
                    _log?.Invoke("[UPLOAD] Update Turn One Link to keep uploading — your laps are kept until then.");
                    return DrainResult.Stop;
                default:
                    SessionStateChanged?.Invoke(sessionId, UploadState.Failed, sent(), laps.Count);
                    return DrainResult.Stop; // offline / server trouble: retry the whole pass later
            }
        }

        var endFile = Path.Combine(dir, "end.json");
        if (!File.Exists(endFile))
        {
            if (laps.Count > 0) SessionStateChanged?.Invoke(sessionId, UploadState.Waiting, sent(), 0);
            return DrainResult.Done; // session still running; more laps will come
        }

        var (endStatus, missing) = await PostEndAsync(sessionId, endFile, sentDir, token);
        if (endStatus is >= 200 and < 300)
        {
            // Laps the server reports missing go back into the outbox for the next pass.
            var requeued = 0;
            foreach (var lap in missing)
            {
                var name = $"lap-{lap:D4}.ndjson";
                var inSent = Path.Combine(sentDir, name);
                if (File.Exists(inSent)) { File.Move(inSent, Path.Combine(dir, name), overwrite: true); requeued++; }
            }
            if (requeued > 0) return DrainResult.Pending;

            SessionStateChanged?.Invoke(sessionId, UploadState.Uploaded, sent(), 0);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            return DrainResult.Done;
        }
        if (endStatus is 404 or 409)
        {
            // Unknown or foreign session: nothing more the server will take; drop it.
            _log?.Invoke($"[UPLOAD] Session {sessionId[..8]} can't be closed on the server ({endStatus}); discarding.");
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            return DrainResult.Done;
        }
        return endStatus == 401 ? DrainResult.Stop : DrainResult.Pending;
    }

    private async Task<int> PostLapAsync(string path, CancellationToken token)
    {
        var body = await File.ReadAllBytesAsync(path, token);
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var lapNumber = int.TryParse(Path.GetFileNameWithoutExtension(path).Split('-')[1], out var n) ? n : 0;
        var sessionId = Path.GetFileName(Path.GetDirectoryName(path)!);

        using var compressed = new MemoryStream();
        await using (var br = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            await br.WriteAsync(body, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_auth.ApiBaseUrl}/simracing/uploads/laps")
        {
            Content = new ByteArrayContent(compressed.ToArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        request.Content.Headers.ContentEncoding.Add("br");
        request.Headers.Add("Idempotency-Key", $"{sessionId}:{lapNumber}:{hash}");
        request.Headers.Add("X-Client-Version", AppInfo.Version);
        return await SendAsync(request, token);
    }

    private async Task<(int Status, List<int> Missing)> PostEndAsync(string sessionId, string endFile, string sentDir, CancellationToken token)
    {
        using var end = JsonDocument.Parse(await File.ReadAllTextAsync(endFile, token));
        var laps = Directory.Exists(sentDir)
            ? Directory.GetFiles(sentDir, "lap-*.ndjson")
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f).Split('-')[1], out var n) ? n : 0)
                .Where(n => n > 0).Select(n => new { lapNumber = n }).ToList<object>()
            : new List<object>();

        var payload = JsonSerializer.Serialize(new { end = end.RootElement.GetProperty("end"), laps });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_auth.ApiBaseUrl}/simracing/uploads/sessions/{sessionId}/end")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        var missing = new List<int>();
        var status = await SendAsync(request, token, async response =>
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (json.RootElement.TryGetProperty("missing", out var m) && m.ValueKind == JsonValueKind.Array)
                missing.AddRange(m.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number).Select(e => e.GetInt32()));
        });
        return (status, missing);
    }

    private async Task<int> SendAsync(HttpRequestMessage request, CancellationToken token, Func<HttpResponseMessage, Task>? onSuccess = null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AccessToken);
        try
        {
            using var response = await _http.SendAsync(request, token);
            if (response.IsSuccessStatusCode && onSuccess != null) await onSuccess(response);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException) { return 0; }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { return 0; } // timeout
    }

    private static void MoveInto(string file, string dir)
    {
        Directory.CreateDirectory(dir);
        File.Move(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _http.Dispose();
    }
}
