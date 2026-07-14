using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Turn_One_Link.Services;

public record UserSession(string Email, string DisplayName);
internal sealed record StoredAuthState(string Email, string DisplayName, string AccessToken, DateTimeOffset? ExpiresAtUtc);

public class AuthService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly string? _apiBaseUrl;
    private UserSession? _session;
    private string? _accessToken;
    private DateTimeOffset? _accessTokenExpiresAt;

    public AuthService(string? apiBaseUrl = null)
    {
        if (Environment.GetEnvironmentVariable("USE_LOCAL_API") == "true")
        {
            Console.WriteLine("Using local API endpoint.");
            _apiBaseUrl = NormalizeBaseUrl(apiBaseUrl ?? Environment.GetEnvironmentVariable("API_URL_LOCAL"))
                          ?? "http://localhost:5271/api";
        }
        else
        {
            Console.WriteLine("Using DEV API endpoint.");
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

            if (stored.ExpiresAtUtc is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                Logout();
                return (false, "Session expired. Please sign in again.");
            }

            _session = new UserSession(stored.Email, stored.DisplayName);
            _accessToken = stored.AccessToken;
            _accessTokenExpiresAt = stored.ExpiresAtUtc;
            ApplyAuthorizationHeader(_accessToken);
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
            await SecureTokenStore.SaveAsync(new StoredAuthState(
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

    public void Logout()
    {
        _session = null;
        _accessToken = null;
        _accessTokenExpiresAt = null;
        ApplyAuthorizationHeader(null);
        SecureTokenStore.Clear();
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

    private static void ApplyAuthorizationHeader(string? accessToken)
    {
        HttpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string? NormalizeBaseUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
}
