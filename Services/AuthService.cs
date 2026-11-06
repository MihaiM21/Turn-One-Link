using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Turn_One_Link.Services;

public record UserSession(string Email, string DisplayName);
internal sealed record StoredAuthState(string Email, string DisplayName, string AccessToken, DateTimeOffset? ExpiresAtUtc, string? DeviceToken = null);

/// <summary>A sign-in code from <c>POST /api/devices/pair/start</c> (HCR-006), shown to the user while Link polls.</summary>
public sealed record PairingCode(string DeviceCode, string UserCode, string VerifyUrl, TimeSpan Interval, DateTimeOffset ExpiresAt);

public enum PairingPollStatus { Pending, Approved, Expired, Failed }

public class AuthService
{
    private readonly HttpClient HttpClient;

    // Persistence of the encrypted sign-in state; tests swap these so they never touch the real file.
    internal Func<StoredAuthState, Task> SaveState { get; init; } = SecureTokenStore.SaveAsync;
    internal Action ClearState { get; init; } = SecureTokenStore.Clear;

    private readonly string? _apiBaseUrl;
    private UserSession? _session;
    private string? _accessToken;
    private DateTimeOffset? _accessTokenExpiresAt;
    // Rotating device token from pairing; lets Link renew its 15-minute access token on its own.
    private string? _deviceToken;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthService(string? apiBaseUrl = null) : this(apiBaseUrl, handler: null) { }

    internal AuthService(string? apiBaseUrl, HttpMessageHandler? handler)
    {
        HttpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        HttpClient.Timeout = TimeSpan.FromSeconds(15);

        if (Environment.GetEnvironmentVariable("USE_LOCAL_API") == "true")
        {
            Console.WriteLine("Using local API endpoint.");
            _apiBaseUrl = NormalizeBaseUrl(apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL_LOCAL"))
                          ?? "http://localhost:5271/api";
        }
        else
        {
            Console.WriteLine("Using production API endpoint.");
            _apiBaseUrl = NormalizeBaseUrl(apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL"))
                          ?? "https://backend.t1f1.com/api";
        }
        
        Console.WriteLine($"Using API endpoint {_apiBaseUrl}");
    }

    public UserSession? CurrentSession => _session;
    public string? AccessToken => _accessToken;
    public string? ApiBaseUrl => _apiBaseUrl;
    public bool IsAuthenticated => _session is not null && !string.IsNullOrWhiteSpace(_accessToken);
    public DateTimeOffset? AccessTokenExpiresAt => _accessTokenExpiresAt;

    /// <summary>True if no token, expired, or expiring within <paramref name="toleranceSeconds"/>.</summary>
    public bool IsTokenExpired(int toleranceSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(_accessToken)) return true;
        if (_accessTokenExpiresAt is not { } expiresAt) return false; // unknown expiry — assume valid
        return expiresAt <= DateTimeOffset.UtcNow.AddSeconds(toleranceSeconds);
    }

    public async Task<(bool Success, string? Error)> TryRestoreSessionAsync()
    {
        try
        {
            var stored = await SecureTokenStore.LoadAsync();
            if (stored is null)
                return (false, null);

            _session = new UserSession(stored.Email, stored.DisplayName);
            _accessToken = stored.AccessToken;
            _accessTokenExpiresAt = stored.ExpiresAtUtc;
            _deviceToken = stored.DeviceToken;
            ApplyAuthorizationHeader(_accessToken);

            // A paired device renews its access token; a password login's token simply expires.
            if (!await EnsureFreshTokenAsync())
            {
                Logout();
                return (false, "Session expired. Please sign in again.");
            }

            // The token may have been revoked (password change, signing-key rotation) before it
            // expired. A definite 401 sends the user to sign in; being offline keeps the session.
            if (await IsTokenRejectedByServerAsync())
            {
                Logout();
                return (false, "Your session has ended. Please sign in again.");
            }
            return (true, null);
        }
        catch
        {
            // If restore fails (corrupt/tampered data), clear local state and fall back to login.
            Logout();
            return (false, "Couldn't restore your previous session. Please sign in again.");
        }
    }

    public async Task<(bool Success, string? Error)> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return (false, "Invalid credentials.");
        
        if (email.IndexOf('@') <= 0 || email.EndsWith("@") || email.Contains(" ") || !email.Contains(".") || !email.Contains("@"))
            return (false, "Please enter a valid email address.");

        if (string.IsNullOrWhiteSpace(_apiBaseUrl))
            return (false, "API_URL is not configured.");

        var loginUrl = $"{_apiBaseUrl}/auth/login";
        var requestBody = new
        {
            email,
            password
        };

        try
        {
            using var response = await HttpClient.PostAsJsonAsync(loginUrl, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return (false, "Invalid credentials.");

                var serverError = await TryReadServerMessageAsync(response.Content);
                return (false, serverError ?? $"Login failed ({(int)response.StatusCode}).");
            }

            var payload = await ParseAuthPayloadAsync(response.Content, email.Trim());
            if (payload is not { } parsedPayload)
                return (false, "Login succeeded but the server response was missing the access token.");

            _accessToken = parsedPayload.AccessToken;
            _accessTokenExpiresAt = parsedPayload.ExpiresAtUtc;
            ApplyAuthorizationHeader(_accessToken);

            var session = await TryFetchCurrentUserAsync(parsedPayload.Session.Email, parsedPayload.Session.DisplayName)
                ?? parsedPayload.Session;

            _session = session;

            // Persist an encrypted session so the user stays signed in between app launches.
            await SaveState(new StoredAuthState(
                session.Email,
                session.DisplayName,
                parsedPayload.AccessToken,
                parsedPayload.ExpiresAtUtc));

            return (true, null);
        }
        catch (TaskCanceledException)
        {
            return (false, "Login timed out. Please try again.");
        }
        catch (HttpRequestException)
        {
            return (false, "Could not reach the server. Please check your connection.");
        }
        catch (JsonException)
        {
            return (false, "Received an invalid response from the server.");
        }
    }

    // ── Device pairing (HCR-006) ─────────────────────────────────────────────────

    /// <summary>Asks the server for a sign-in code to show; the user approves it on turnonehub.com/link.</summary>
    public async Task<(PairingCode? Code, string? Error)> StartPairingAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiBaseUrl)) return (null, "API_URL is not configured.");
        try
        {
            using var response = await HttpClient.PostAsJsonAsync($"{_apiBaseUrl}/devices/pair/start", new
            {
                deviceName = Environment.MachineName,
                platform = "windows",
                appVersion = AppInfo.Version,
            });
            if (!response.IsSuccessStatusCode)
                return (null, (int)response.StatusCode == 429
                    ? "Too many sign-in attempts. Try again in a few minutes."
                    : $"Couldn't start sign-in ({(int)response.StatusCode}).");

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement;
            return (new PairingCode(
                DeviceCode: root.GetProperty("deviceCode").GetString()!,
                UserCode: root.GetProperty("userCode").GetString()!,
                VerifyUrl: root.GetProperty("verifyUrlComplete").GetString()!,
                Interval: TimeSpan.FromSeconds(Math.Max(1, root.GetProperty("interval").GetInt32())),
                ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expiresIn").GetInt32())), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (null, "Could not reach the server. Please check your connection.");
        }
    }

    /// <summary>One poll. On approval, stores the device's tokens and signs in.</summary>
    public async Task<PairingPollStatus> PollPairingAsync(PairingCode code)
    {
        if (DateTimeOffset.UtcNow >= code.ExpiresAt) return PairingPollStatus.Expired;
        try
        {
            using var response = await HttpClient.PostAsJsonAsync($"{_apiBaseUrl}/devices/pair/poll", new { deviceCode = code.DeviceCode });
            if ((int)response.StatusCode is 428 or 429) return PairingPollStatus.Pending; // pending / slow_down
            if (response.StatusCode == HttpStatusCode.BadRequest) return PairingPollStatus.Expired;
            if (!response.IsSuccessStatusCode) return PairingPollStatus.Pending;

            if (!await ApplyDeviceTokensAsync(response.Content)) return PairingPollStatus.Failed;

            var session = await TryFetchCurrentUserAsync("", "Driver") ?? new UserSession("", "Driver");
            _session = session;
            await PersistAsync();
            return PairingPollStatus.Approved;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return PairingPollStatus.Pending; // transient; keep polling until the code expires
        }
        catch (JsonException)
        {
            return PairingPollStatus.Failed;
        }
    }

    /// <summary>
    /// Makes sure <see cref="AccessToken"/> is usable for at least two more minutes, renewing it with
    /// the device token when needed. False means the user must sign in again.
    /// </summary>
    public async Task<bool> EnsureFreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_accessToken)) return false;
        if (!IsTokenExpired(toleranceSeconds: 120)) return true;
        if (string.IsNullOrWhiteSpace(_deviceToken)) return !IsTokenExpired(toleranceSeconds: 0);

        await _refreshLock.WaitAsync();
        try
        {
            if (!IsTokenExpired(toleranceSeconds: 120)) return true; // another caller just renewed it

            using var response = await HttpClient.PostAsJsonAsync($"{_apiBaseUrl}/devices/token/refresh", new { deviceToken = _deviceToken });
            if (response.StatusCode == HttpStatusCode.Unauthorized) return false; // revoked on the website
            if (!response.IsSuccessStatusCode) return !IsTokenExpired(toleranceSeconds: 0);

            if (!await ApplyDeviceTokensAsync(response.Content)) return !IsTokenExpired(toleranceSeconds: 0);
            await PersistAsync();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return !IsTokenExpired(toleranceSeconds: 0); // offline: keep going while the current token lasts
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<bool> ApplyDeviceTokensAsync(HttpContent content)
    {
        using var json = await JsonDocument.ParseAsync(await content.ReadAsStreamAsync());
        var root = json.RootElement;
        var access = TryGetString(root, "accessToken");
        var device = TryGetString(root, "deviceToken");
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(device)) return false;

        _accessToken = access;
        _deviceToken = device;
        _accessTokenExpiresAt = root.TryGetProperty("accessTokenExpiresAt", out var exp) && exp.TryGetDateTimeOffset(out var at)
            ? at
            : DateTimeOffset.UtcNow.AddMinutes(15);
        ApplyAuthorizationHeader(_accessToken);
        return true;
    }

    private Task PersistAsync() => SaveState(new StoredAuthState(
        _session?.Email ?? "", _session?.DisplayName ?? "Driver", _accessToken!, _accessTokenExpiresAt, _deviceToken));

    public void Logout()
    {
        _session = null;
        _accessToken = null;
        _accessTokenExpiresAt = null;
        _deviceToken = null;
        ApplyAuthorizationHeader(null);
        ClearState();
    }

    private async Task<bool> IsTokenRejectedByServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiBaseUrl)) return false;
        try
        {
            using var response = await HttpClient.GetAsync($"{_apiBaseUrl}/auth/me");
            return response.StatusCode == HttpStatusCode.Unauthorized;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private async Task<UserSession?> TryFetchCurrentUserAsync(string fallbackEmail, string fallbackDisplayName)
    {
        if (string.IsNullOrWhiteSpace(_apiBaseUrl) || string.IsNullOrWhiteSpace(_accessToken))
            return null;

        try
        {
            using var response = await HttpClient.GetAsync($"{_apiBaseUrl}/auth/me");
            if (!response.IsSuccessStatusCode)
                return null;

            var profile = await ParseCurrentUserAsync(response.Content, fallbackEmail, fallbackDisplayName);
            return profile;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(UserSession Session, string AccessToken, DateTimeOffset? ExpiresAtUtc)?> ParseAuthPayloadAsync(HttpContent content, string fallbackEmail)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var resolvedEmail =
            TryGetString(root, "email") ??
            TryGetNestedString(root, "user", "email") ??
            fallbackEmail;

        if (string.IsNullOrWhiteSpace(resolvedEmail))
            return null;

        var displayName =
            TryGetString(root, "displayName") ??
            TryGetNestedString(root, "user", "displayName") ??
            resolvedEmail.Split('@')[0];

        var rawToken =
            TryGetString(root, "accessToken") ??
            TryGetString(root, "token") ??
            TryGetString(root, "jwt") ??
            TryGetNestedString(root, "data", "accessToken") ??
            TryGetNestedString(root, "data", "token") ??
            TryGetNestedString(root, "user", "token");

        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var accessToken = rawToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? rawToken["Bearer ".Length..].Trim()
            : rawToken.Trim();

        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var expiresAtUtc =
            TryParseDateTimeOffset(root, "expiresAt") ??
            TryParseDateTimeOffset(root, "expires_at") ??
            TryParseNestedDateTimeOffset(root, "data", "expiresAt") ??
            TryParseNestedDateTimeOffset(root, "data", "expires_at") ??
            TryGetExpiresIn(root, "expiresIn") ??
            TryGetExpiresIn(root, "expires_in") ??
            TryGetNestedExpiresIn(root, "data", "expiresIn") ??
            TryGetNestedExpiresIn(root, "data", "expires_in") ??
            TryGetJwtExpiryUtc(accessToken);

        return (new UserSession(resolvedEmail, displayName), accessToken, expiresAtUtc);
    }

    private static async Task<UserSession?> ParseCurrentUserAsync(HttpContent content, string fallbackEmail, string fallbackDisplayName)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        var email =
            TryGetString(root, "email") ??
            TryGetNestedString(root, "user", "email") ??
            fallbackEmail;

        var displayName =
            TryGetString(root, "username") ??
            TryGetString(root, "displayName") ??
            TryGetString(root, "name") ??
            TryGetString(root, "fullName") ??
            TryGetString(root, "preferred_username") ??
            TryGetNestedString(root, "user", "username") ??
            TryGetNestedString(root, "user", "displayName") ??
            TryGetNestedString(root, "user", "name") ??
            TryGetNestedString(root, "user", "fullName") ??
            fallbackDisplayName;

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(displayName))
            return null;

        email = string.IsNullOrWhiteSpace(email) ? fallbackEmail : email;
        displayName = string.IsNullOrWhiteSpace(displayName) ? fallbackDisplayName : displayName;

        return new UserSession(email, displayName);
    }

    private static DateTimeOffset? TryGetExpiresIn(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value))
            return null;

        return TryMapExpiresInValue(value);
    }

    private static DateTimeOffset? TryGetNestedExpiresIn(JsonElement root, string parent, string key)
    {
        if (!root.TryGetProperty(parent, out var parentElement) || parentElement.ValueKind != JsonValueKind.Object)
            return null;

        if (!parentElement.TryGetProperty(key, out var value))
            return null;

        return TryMapExpiresInValue(value);
    }

    private static DateTimeOffset? TryMapExpiresInValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
            return DateTimeOffset.UtcNow.AddSeconds(seconds);

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsedSeconds))
            return DateTimeOffset.UtcNow.AddSeconds(parsedSeconds);

        return null;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(JsonElement root, string key)
    {
        var value = TryGetString(root, key);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? TryParseNestedDateTimeOffset(JsonElement root, string parent, string key)
    {
        var value = TryGetNestedString(root, parent, key);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? TryGetJwtExpiryUtc(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
            return null;

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        while (payload.Length % 4 != 0)
            payload += "=";

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            if (TryGetLong(json.RootElement, "exp") is { } unixSeconds)
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task<string?> TryReadServerMessageAsync(HttpContent content)
    {
        var payload = await content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;

            return
                TryGetString(root, "message") ??
                TryGetString(root, "error") ??
                TryGetString(root, "detail") ??
                TryGetNestedString(root, "error", "message");
        }
        catch (JsonException)
        {
            return payload.Length <= 200 ? payload.Trim() : null;
        }
    }

    private static string? TryGetNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out var next))
                return null;

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? TryGetString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetLong(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private void ApplyAuthorizationHeader(string? accessToken)
    {
        HttpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string? NormalizeBaseUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
}
