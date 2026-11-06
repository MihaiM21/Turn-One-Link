using System.Diagnostics;
using System.IO;

namespace Turn_One_Link.Services;

public enum SimStatus { Disconnected, Connected }

public record SimConnectionState(SimStatus Status, string? GameName);

/// <summary>
/// Polls for a running sim every 500 ms. The game process is the source of truth: shared-memory
/// blocks (ACC's acpmf_*, iRacing's IRSDK map) outlive the game while our reader holds them open,
/// so they can't tell us the game has quit.
/// </summary>
public class ConnectionService : IDisposable
{
    /// <summary>A sim and the process names that mean it is running. Order matters: first match wins.</summary>
    private sealed record SimProcess(string GameName, string[] ProcessNames);

    private static readonly SimProcess[] KnownSims =
    [
        new("Assetto Corsa Competizione", ["AC2-Win64-Shipping", "AC2"]),
        new("acs", ["acs"]),
        // iRacingUI / iRacingService run without a car on track; only the sim process carries telemetry.
        new("iRacing", ["iRacingSim64DX11", "iRacingSim64DX12", "iRacingSim64"]),
        // Named by process, not window title ("EA SPORTS F1® 25" doesn't start with "F1").
        new("F1 25", ["F1_25"]),
        new("F1 26", ["F1_26", "F126"]),
        // Detected so the UI can say so, but no telemetry adapter yet (see MainWindow.CreateSourceFor).
        new("rFactor 2", ["rFactor2"]),
        new("RaceRoom", ["RRRE", "RRRE64"]),
        new("Le Mans Ultimate", ["Le Mans Ultimate", "LMU"]),
        new("F1 24", ["F1_24", "F12024"]),
        new("F1 23", ["F12023", "F1_23"]),
    ];

    private static readonly string DebugLogPath = Path.Combine(AppPaths.DataDir, "turnone_debug.log");

    private readonly System.Timers.Timer _timer;
    private SimConnectionState _lastSim = new(SimStatus.Disconnected, null);

    public event Action<SimConnectionState>? SimStatusChanged;

    public ConnectionService()
    {
        // AutoReset off + restart after each poll: a slow process scan can't overlap the next one.
        _timer = new System.Timers.Timer(500) { AutoReset = false };
        _timer.Elapsed += (_, _) =>
        {
            try { Poll(); }
            finally { if (_running) _timer.Start(); }
        };
    }

    private volatile bool _running;

    public void Start()
    {
        _running = true;
        _timer.Start();
    }

    /// <summary>Stops polling and forgets the last state, so a later Start reports a running sim again.</summary>
    public void Stop()
    {
        _running = false;
        _timer.Stop();
        _lastSim = new(SimStatus.Disconnected, null);
    }

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
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch (IOException) { /* best-effort diagnostics only */ }
        catch (UnauthorizedAccessException) { }
    }

    private static SimConnectionState DetectSim()
    {
        foreach (var sim in KnownSims)
        {
            foreach (var processName in sim.ProcessNames)
            {
                if (IsRunning(processName))
                    return new SimConnectionState(SimStatus.Connected, sim.GameName);
            }
        }
        return new SimConnectionState(SimStatus.Disconnected, null);
    }

    private static bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Any(p => !HasExitedSafe(p));
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
        // Access denied (e.g. elevated game process): it was listed, so treat it as running.
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public void Dispose()
    {
        _running = false;
        _timer.Dispose();
    }
}
