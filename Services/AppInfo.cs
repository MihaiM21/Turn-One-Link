using System.Reflection;

namespace Turn_One_Link.Services;

/// <summary>Build facts read from the assembly — the csproj <c>&lt;Version&gt;</c> is the single source.</summary>
public static class AppInfo
{
    /// <summary>e.g. "1.2.0" (the "+commit" suffix the SDK appends is dropped).</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>Sentry DSN baked in at build time (<c>-p:SentryDsn=...</c>); empty in local builds.</summary>
    public static string SentryDsn { get; } =
        Environment.GetEnvironmentVariable("SENTRY_DSN")
        ?? typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SentryDsn")?.Value
        ?? "";
}
