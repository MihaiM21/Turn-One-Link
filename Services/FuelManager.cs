using System.Collections.Generic;
using System.Linq;
using Turn_One_Link.Services.Telemetry;

namespace Turn_One_Link.Services;

/// <summary>
/// Computed fuel metrics. All fields are null until the first clean flying lap
/// has been completed.
/// </summary>
public sealed record FuelSnapshot(
    float? LastLapLiters,
    float? AvgPerLapLiters,
    float? EstimatedStintLaps);

/// <summary>
/// Tracks fuel consumption per lap by snapshotting the fuel level at lap
/// boundaries, keeping a rolling average over recent clean laps and estimating
/// how many laps the current fuel load will last.
///
/// Fuel level arrives via physics updates (~5 ms cadence) and lap boundaries via
/// graphics updates; because physics is so frequent, the cached fuel value is at
/// most a few milliseconds stale at any boundary — no timestamp correlation needed.
/// Laps that touch the pit lane or involve a refuel are excluded so out/in-laps
/// don't skew the average.
/// </summary>
public sealed class FuelManager
{
    private const int WindowSize = 5;

    // Consumption below this is a reset/glitch, not a driven lap.
    private const float MinValidPerLapLiters = 0.1f;

    // A fuel increase above this between physics frames means a refuel.
    private const float RefuelThresholdLiters = 0.5f;

    private readonly Queue<float> _lapWindow = new();
    private float _latestFuel = -1f;
    private float _fuelAtLapStart = -1f;
    private int _lastCompletedLaps = -1;
    private bool _excludeCurrentLap;
    private float? _lastLapLiters;

    /// <summary>Caches the latest fuel level. Call from the physics update handler.</summary>
    public void OnPhysics(float fuelLiters)
    {
        if (_latestFuel >= 0 && fuelLiters > _latestFuel + RefuelThresholdLiters)
        {
            _fuelAtLapStart = fuelLiters;
            _excludeCurrentLap = true;
        }
        _latestFuel = fuelLiters;
    }

    /// <summary>Detects lap boundaries and pit visits. Call from the graphics update handler.</summary>
    public void OnGraphics(T1GraphicsFrame g, SessionPhase phase)
    {
        if (phase != SessionPhase.Live || _latestFuel < 0) return;

        if (_lastCompletedLaps < 0)
        {
            // First live sample: baseline mid-lap; this partial lap is unusable.
            _lastCompletedLaps = g.CompletedLaps;
            _fuelAtLapStart = _latestFuel;
            _excludeCurrentLap = true;
            return;
        }

        if (g.CompletedLaps > _lastCompletedLaps)
        {
            float perLap = _fuelAtLapStart - _latestFuel;
            bool singleLap = g.CompletedLaps == _lastCompletedLaps + 1;

            if (!_excludeCurrentLap && singleLap && perLap > MinValidPerLapLiters)
            {
                _lastLapLiters = perLap;
                _lapWindow.Enqueue(perLap);
                while (_lapWindow.Count > WindowSize) _lapWindow.Dequeue();
            }

            _fuelAtLapStart = _latestFuel;
            _excludeCurrentLap = false;
            _lastCompletedLaps = g.CompletedLaps;
        }
        else if (g.CompletedLaps < _lastCompletedLaps)
        {
            // Lap counter went backwards — session restart. Re-baseline.
            _lastCompletedLaps = g.CompletedLaps;
            _fuelAtLapStart = _latestFuel;
            _excludeCurrentLap = true;
        }

        // Marked after boundary handling so a pit entry exactly on the line
        // taints the new lap, not the one just completed.
        if (g.IsInPitLane)
            _excludeCurrentLap = true;
    }

    public FuelSnapshot? GetSnapshot()
    {
        if (_lapWindow.Count == 0) return null;

        float avg = _lapWindow.Average();
        float? stintLaps = _latestFuel >= 0 && avg > 0f
            ? _latestFuel / avg
            : null;

        return new FuelSnapshot(_lastLapLiters, avg, stintLaps);
    }

    /// <summary>Clears all history. Call on session start and end.</summary>
    public void Reset()
    {
        _lapWindow.Clear();
        _latestFuel = -1f;
        _fuelAtLapStart = -1f;
        _lastCompletedLaps = -1;
        _excludeCurrentLap = false;
        _lastLapLiters = null;
    }
}
