using System.IO;

namespace Turn_One_Link.Services;

/// <summary>
/// Where Turn One Link keeps per-user state. Everything lives under
/// <c>%LOCALAPPDATA%\TurnOneLink</c>, never next to the exe (the install folder is replaced on update).
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TurnOneLink");

    /// <summary>
    /// Optional KEY=VALUE overrides (e.g. <c>LOCAL_WS_PORT</c>, <c>F1_UDP_PORT</c>). Release builds read
    /// only this file; production defaults are compiled in, so no .env ships with the installer.
    /// </summary>
    public static string OverridesEnv => Path.Combine(DataDir, "overrides.env");
}
