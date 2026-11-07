namespace Turn_One_Link.Services;

/// <summary>
/// LF-16 — "the game is running but nothing arrives": a short, game-specific hint for the sim card.
/// Pure, so the wording per case is unit-tested.
/// </summary>
public static class TelemetryHealth
{
    /// <summary>How long a connected sim may stay silent before the hint appears.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(8);

    /// <param name="gameName">Name from <see cref="ConnectionService"/>, or null when no sim is detected.</param>
    /// <param name="connectedFor">Time since the sim was detected.</param>
    /// <param name="sinceLastTick">Time since the last telemetry tick, or null if none arrived this connection.</param>
    public static string? Hint(string? gameName, TimeSpan connectedFor, TimeSpan? sinceLastTick, int f1Port, string pcAddress)
    {
        if (gameName == null) return null;
        if (sinceLastTick is { } quiet && quiet < Grace) return null; // data is flowing
        if (sinceLastTick == null && connectedFor < Grace) return null; // give the game a moment

        if (gameName == ConnectionService.F1ConsoleGameName)
            return $"Waiting for F1 data from your console. In F1: Settings → Telemetry → UDP Telemetry On, " +
                   $"UDP Broadcast Off, IP address {pcAddress}, port {f1Port}, format 2025.";

        if (gameName is "F1 24" or "F1 23")
            return "F1 24 and older aren't supported yet — Turn One Link reads F1 25 and F1 26.";

        if (gameName is "F1 25" or "F1 26")
            return $"No F1 data yet. In F1: Settings → Telemetry → UDP Telemetry On, port {f1Port}, format 2025.";

        if (gameName == "iRacing")
            return "Waiting for iRacing data — it starts once you're in the car.";

        if (gameName is "Assetto Corsa Competizione" or "acs")
            return "Waiting for data — it starts once you're in the car on track.";

        return $"{gameName} isn't supported yet — nothing will be recorded.";
    }
}
