using Sentry;
using Velopack;

namespace Turn_One_Link;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles Velopack's install / update / uninstall hooks and may exit the process.
        VelopackApp.Build().Run();

        using var _ = CrashReporting.Init();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
