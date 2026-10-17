using System;

namespace Turn_One_Link.Services.Telemetry;

/// <summary>
/// Sim-agnostic telemetry source. Implementations translate a specific sim's
/// native telemetry (shared memory, SDK events, etc.) into unified T1 frames.
/// </summary>
public interface ITelemetrySource : IDisposable
{
    event Action<T1PhysicsFrame>? PhysicsUpdated;
    event Action<T1GraphicsFrame>? GraphicsUpdated;
    event Action<T1StaticFrame>? StaticInfoUpdated;
    event Action<Exception>? OnError;

    // Cloud protocol v2 (docs/telemetry/backend_contract_v2.md). Raised alongside the
    // Physics/Graphics/Static events above, which remain the local-mobile/v1-cloud shape.
    event Action<T1TickFrame>? TickUpdated;
    event Action<T1LapComplete>? LapCompleted;
    event Action<T1SessionInfo>? SessionInfoUpdated;

    /// <summary>v2 `source` wire value for this telemetry source ("acc", "ac", "iracing", ...).</summary>
    string SourceName { get; }

    void Start();
    void Stop();
    void RequestStaticRefresh();
}
