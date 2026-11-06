using Sentry;

namespace Turn_One_Link;

/// <summary>
/// Sentry crash reports. Off when the build has no DSN or the user turned them off in settings.
/// Events never carry telemetry, tokens or e-mail addresses.
/// </summary>
public static class CrashReporting
{
    public static bool Enabled { get; private set; }

    public static IDisposable Init()
    {
        var dsn = Services.AppInfo.SentryDsn;
        if (string.IsNullOrWhiteSpace(dsn) || !Services.AppSettings.Load().SendCrashReports)
            return new NoOp();

        Enabled = true;
        return SentrySdk.Init(o =>
        {
            o.Dsn = dsn;
            o.Release = $"turn-one-link@{Services.AppInfo.Version}";
            o.IsGlobalModeEnabled = true;
            o.AutoSessionTracking = true;
            o.SendDefaultPii = false;
            o.MaxBreadcrumbs = 50;
        });
    }

    public static void Capture(Exception ex)
    {
        if (Enabled) SentrySdk.CaptureException(ex);
    }

    private sealed class NoOp : IDisposable
    {
        public void Dispose() { }
    }
}
