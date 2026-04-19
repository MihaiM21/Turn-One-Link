using System.Net.Http;
using System.Net.NetworkInformation;
using System.Diagnostics;

namespace Turn_One_Link.Services;

public enum SimStatus { Disconnected, Connected }

public record SimConnectionState(SimStatus Status, string? GameName);

public class ConnectionService : IDisposable
{
    private static readonly string[] SimProcessNames =
    [
        "iRacing",
        "acs",          // Assetto Corsa
        "AC2",          // Assetto Corsa Competizione
        "rFactor2",
        "RRRE",         // RaceRoom
        "F12023",
        "F12024",
        "F1_24",
        "F1_25",
        "Le Mans Ultimate",
    ];

    private readonly System.Timers.Timer _timer;
    private SimConnectionState _lastSim = new(SimStatus.Disconnected, null);
    private bool _lastServer = false;

    public event Action<SimConnectionState>? SimStatusChanged;
    public event Action<bool>? ServerStatusChanged;

    public ConnectionService()
    {
        _timer = new System.Timers.Timer(3000);
        _timer.Elapsed += async (_, _) => await PollAsync();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private async Task PollAsync()
    {
        var sim = DetectSim();
        if (sim != _lastSim)
        {
            _lastSim = sim;
            SimStatusChanged?.Invoke(sim);
        }

        var server = await PingServerAsync();
        if (server != _lastServer)
        {
            _lastServer = server;
            ServerStatusChanged?.Invoke(server);
        }
    }

    private static SimConnectionState DetectSim()
    {
        foreach (var name in SimProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
                return new SimConnectionState(SimStatus.Connected, procs[0].MainWindowTitle.Length > 0
                    ? procs[0].MainWindowTitle
                    : name);
        }
        return new SimConnectionState(SimStatus.Disconnected, null);
    }

    private static async Task<bool> PingServerAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("turnonehub.com", 2000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
