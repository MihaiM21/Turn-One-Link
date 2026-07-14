using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace Turn_One_Link.Services;

public enum SimStatus { Disconnected, Connected }

public record SimConnectionState(SimStatus Status, string? GameName);

public class ConnectionService : IDisposable
{
    private static readonly string[] OtherSimProcessNames =
    [
        "iRacing",
        "rFactor2",
        "RRRE",
        "F12023",
        "F12024",
        "F1_24",
        "F1_25",
        "Le Mans Ultimate",
    ];

    private static readonly string[] AccProcessNames =
    [
        "AC2",
        "AC2-Win64-Shipping",
        "acs",
    ];

    private readonly System.Timers.Timer _timer;
    private SimConnectionState _lastSim = new(SimStatus.Disconnected, null);

    public event Action<SimConnectionState>? SimStatusChanged;

    public ConnectionService()
    {
        _timer = new System.Timers.Timer(500);
        _timer.Elapsed += (_, _) => Poll();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Poll()
    {
        var sim = DetectSim();
        if (sim != _lastSim)
        {
            if (sim.Status == SimStatus.Connected) Log($"Status changed: Connected to {sim.GameName}");
            else Log("Status changed: Disconnected");

            _lastSim = sim;
            SimStatusChanged?.Invoke(sim);
        }
    }

    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText("turnone_debug.log", $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private static SimConnectionState DetectSim()
    {
        // Primary signal for ACC: shared memory exists the moment the game initialises
        // its physics block. This is faster and more reliable than the process-name scan.
        if (TryOpenAccSharedMemory())
        {
            var name = ResolveAccProcessTitle() ?? "Assetto Corsa Competizione";
            return new SimConnectionState(SimStatus.Connected, name);
        }

        // Fallback: process-name match for other supported sims
        foreach (var name in OtherSimProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                var gameName = procs[0].MainWindowTitle.Length > 0 ? procs[0].MainWindowTitle : name;
                return new SimConnectionState(SimStatus.Connected, gameName);
            }
        }

        return new SimConnectionState(SimStatus.Disconnected, null);
    }

    private static bool TryOpenAccSharedMemory()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(@"Local\acpmf_static");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveAccProcessTitle()
    {
        foreach (var name in AccProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0) continue;
            var title = procs[0].MainWindowTitle;
            return string.IsNullOrEmpty(title) ? name : title;
        }
        return null;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
