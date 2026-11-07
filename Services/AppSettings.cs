using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Turn_One_Link.Services;

/// <summary>
/// Persists user-configurable dashboard preferences to settings.json in
/// <see cref="AppPaths.DataDir"/> (the install folder is replaced on every update).
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(AppPaths.DataDir, "settings.json");

    // Builds before 1.2 kept settings next to the exe; read them once if nothing newer exists.
    private static readonly string LegacySettingsPath = Path.Combine(
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

    /// <summary>F1 25/26 on PlayStation / Xbox: listen for its UDP telemetry without a game on this PC. Default: off.</summary>
    [JsonPropertyName("f1ConsoleMode")]
    public bool F1ConsoleMode { get; set; } = false;

    /// <summary>Send anonymous crash reports (no telemetry, no account data). Default: on.</summary>
    [JsonPropertyName("sendCrashReports")]
    public bool SendCrashReports { get; set; } = true;

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads settings from disk. Returns defaults if the file is missing or corrupt.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
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
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }
}
