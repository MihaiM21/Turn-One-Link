using System;

namespace Turn_One_Link.Services.Telemetry;

/// <summary>
/// Reads the small set of environment variables that gate the cloud telemetry protocol
/// (see docs/setup/environment.md). Uses <see cref="Environment.GetEnvironmentVariable"/>
/// directly, same pattern as <c>AuthService</c>'s USE_LOCAL_API/API_URL — by the time any
/// adapter runs, App.xaml.cs has already loaded .env / .env.local into the process env.
/// </summary>
public static class TelemetryEnv
{
    /// <summary>
    /// When true, the legacy v1 ACC-shaped physics/graphics/static cloud stream
    /// (<see cref="TelemetryStreamingService.OnPhysicsUpdated"/> etc.) is wired up alongside
    /// v2. Default false — v2 tick/lap_complete/session_start is the only cloud stream.
    /// </summary>
    public static bool ProtocolV1Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("PROTOCOL_V1"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>v2 tick rate in Hz. Default 20, hard-capped at 60 per the backend contract.</summary>
    public static int StreamHz
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("STREAM_HZ");
            if (!int.TryParse(raw, out var hz) || hz <= 0) hz = 20;
            return Math.Min(hz, 60);
        }
    }

    /// <summary>Minimum spacing between emitted ticks for <see cref="StreamHz"/>.</summary>
    public static TimeSpan TickInterval => TimeSpan.FromSeconds(1.0 / StreamHz);
}
