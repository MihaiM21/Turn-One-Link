using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turn_One_Link.Services;

/// <summary>
/// Persists user-configurable dashboard preferences to settings.json
/// in the application base directory.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Defaults ──────────────────────────────────────────────────────────────

    /// <summary>Live telemetry HUD (speed, gear, RPM, pedals). Default: off.</summary>
    [JsonPropertyName("showLiveTelemetry")]
    public bool ShowLiveTelemetry { get; set; } = false;

    /// <summary>Console / terminal log panel. Default: on.</summary>
    [JsonPropertyName("showConsole")]
    public bool ShowConsole { get; set; } = true;

    /// <summary>Local WebSocket server info card (address, PIN, devices). Default: on.</summary>
    [JsonPropertyName("showLocalWsInfo")]
    public bool ShowLocalWsInfo { get; set; } = true;

    /// <summary>Persist telemetry to local session log files for post-race analysis. Default: on.</summary>
    [JsonPropertyName("enableSessionLogging")]
    public bool EnableSessionLogging { get; set; } = true;

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads settings from disk. Returns defaults if the file is missing or corrupt.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch { /* silently use defaults */ }

        return new AppSettings();
    }

    /// <summary>
    /// Saves current settings to disk. Silently ignores IO errors.
    /// </summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }
}
