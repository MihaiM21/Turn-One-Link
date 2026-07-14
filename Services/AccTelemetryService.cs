using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Turn_One_Link.Services.AccTelemetry;

namespace Turn_One_Link.Services;

public class AccTelemetryService : IDisposable
{
    private MemoryMappedFile? _physicsFile;
    private MemoryMappedFile? _graphicsFile;
    private MemoryMappedFile? _staticFile;

    private MemoryMappedViewAccessor? _physicsView;
    private MemoryMappedViewAccessor? _graphicsView;
    private MemoryMappedViewAccessor? _staticView;

    private CancellationTokenSource _cts = new();
    private Task? _pollTask;

    public event Action<SPageFilePhysics>? PhysicsUpdated;
    public event Action<SPageFileGraphic>? GraphicsUpdated;
    public event Action<SPageFileStatic>? StaticInfoUpdated;
    public event Action<Exception>? OnError;

    private int _lastPhysicsPacketId = -1;
    private int _lastGraphicsPacketId = -1;
    private volatile bool _staticRefreshRequested;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(5);

    public void Start()
    {
        if (_pollTask is { IsCompleted: false }) return;

        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
    }

    public void RequestStaticRefresh() => _staticRefreshRequested = true;

    private async Task PollLoopAsync(CancellationToken token)
    {
        bool staticRead = false;
        bool hasLoggedError = false;

        using var timer = new PeriodicTimer(PollInterval);

        while (!token.IsCancellationRequested)
        {
            try
            {
                EnsureMappedFiles();

                if ((_staticRefreshRequested || !staticRead) && _staticView != null)
                {
                    var staticInfo = ReadStruct<SPageFileStatic>(_staticView);
                    if (staticInfo.HasValue)
                    {
                        StaticInfoUpdated?.Invoke(staticInfo.Value);
                        staticRead = true;
                        _staticRefreshRequested = false;
                    }
                }

                if (_physicsView != null)
                {
                    var physics = ReadStruct<SPageFilePhysics>(_physicsView);
                    if (physics.HasValue && physics.Value.PacketId != _lastPhysicsPacketId)
                    {
                        _lastPhysicsPacketId = physics.Value.PacketId;
                        PhysicsUpdated?.Invoke(physics.Value);
                    }
                }

                if (_graphicsView != null)
                {
                    var graphics = ReadStruct<SPageFileGraphic>(_graphicsView);
                    if (graphics.HasValue && graphics.Value.PacketId != _lastGraphicsPacketId)
                    {
                        _lastGraphicsPacketId = graphics.Value.PacketId;
                        GraphicsUpdated?.Invoke(graphics.Value);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ResetMappedFiles();
                if (!hasLoggedError)
                {
                    OnError?.Invoke(ex);
                    hasLoggedError = true;
                }
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void EnsureMappedFiles()
    {
        if (_physicsFile == null)
        {
            _physicsFile = MemoryMappedFile.OpenExisting(@"Local\acpmf_physics");
            _physicsView = _physicsFile.CreateViewAccessor(0, Marshal.SizeOf<SPageFilePhysics>());
        }
        if (_graphicsFile == null)
        {
            _graphicsFile = MemoryMappedFile.OpenExisting(@"Local\acpmf_graphics");
            _graphicsView = _graphicsFile.CreateViewAccessor(0, Marshal.SizeOf<SPageFileGraphic>());
        }
        if (_staticFile == null)
        {
            _staticFile = MemoryMappedFile.OpenExisting(@"Local\acpmf_static");
            _staticView = _staticFile.CreateViewAccessor(0, Marshal.SizeOf<SPageFileStatic>());
        }
    }

    private void ResetMappedFiles()
    {
        _physicsView?.Dispose(); _physicsView = null;
        _graphicsView?.Dispose(); _graphicsView = null;
        _staticView?.Dispose(); _staticView = null;
        _physicsFile?.Dispose(); _physicsFile = null;
        _graphicsFile?.Dispose(); _graphicsFile = null;
        _staticFile?.Dispose(); _staticFile = null;
        _lastPhysicsPacketId = -1;
        _lastGraphicsPacketId = -1;
    }

    private static T? ReadStruct<T>(MemoryMappedViewAccessor accessor) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        accessor.ReadArray(0, buffer, 0, size);

        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        Stop();
        try { _pollTask?.Wait(500); } catch { }
        _cts.Dispose();
        ResetMappedFiles();
    }
}
