# Real-Time Car Physics (`type: "physics"`)

This message type is sent at a high frequency (~100Hz) when the game is running. It contains all real-time car inputs, tire data, and physical states.

## Example Message

```json
{
  "type": "physics",
  "timestamp": 1776764355947,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "packetId": 6158,
    "gas": 0.0,
    "brake": 0.0,
    "gear": 1,
    "rpms": 0,
    "speedKmh": 0.0016,
    "tyreCoreTemperature": [69.96, 69.97, 69.98, 69.98],
    "wheelsPressure": [26.50, 26.50, 25.30, 25.30],
    "brakeTemp": [27.0, 27.0, 27.1, 27.2],
    "waterTemp": 55.36,
    "airTemp": 27.04,
    "roadTemp": 27.64,
    "pitLimiterOn": 1,
    "brakeBias": 0.79
  }
}
```

## Schema Reference

| Field | Type | Description |
|---|---|---|
| `packetId` | int | Monotonically increasing frame counter |
| `gas` | float | Throttle position 0.0–1.0 |
| `brake` | float | Brake position 0.0–1.0 |
| `clutch` | float | Clutch position 0.0–1.0 |
| `gear` | int | Current gear (0=R, 1=N, 2=1st…) |
| `rpms` | int | Engine RPM |
| `steerAngle` | float | Steering wheel angle (normalised) |
| `speedKmh` | float | Speed in km/h |
| `velocity` | float[3] | World-space velocity vector [x, y, z] |
| `accG` | float[3] | G-force vector [lateral, vertical, longitudinal] |
| `wheelSlip` | float[4] | Slip per wheel [FL, FR, RL, RR] |
| `wheelsPressure` | float[4] | Tyre pressure in PSI [FL, FR, RL, RR] |
| `wheelAngularSpeed` | float[4] | Angular speed per wheel |
| `tyreCoreTemperature` | float[4] | Core tyre temp °C [FL, FR, RL, RR] |
| `tyreTemp` | float[4] | Surface tyre temp °C [FL, FR, RL, RR] |
| `tyreTempI` | float[4] | Inner tyre temp °C |
| `tyreTempM` | float[4] | Middle tyre temp °C |
| `tyreTempO` | float[4] | Outer tyre temp °C |
| `tyreWear` | float[4] | Tyre wear 0.0–1.0 |
| `tyreDirtyLevel` | float[4] | Dirt on tyre surface |
| `suspensionTravel` | float[4] | Suspension compression [FL, FR, RL, RR] |
| `suspensionDamage` | float[4] | Suspension damage 0.0–1.0 |
| `rideHeight` | float[2] | Ride height [front, rear] |
| `heading` | float | Car heading in radians |
| `pitch` | float | Car pitch in radians |
| `roll` | float | Car roll in radians |
| `cgHeight` | float | Centre of gravity height |
| `carDamage` | float[5] | Damage per zone [front, rear, left, right, centre] |
| `brakeTemp` | float[4] | Brake disc temperature °C [FL, FR, RL, RR] |
| `brakePressure` | float[4] | Brake hydraulic pressure |
| `brakeBias` | float | Front brake bias (0.0–1.0) |
| `padLife` | float[4] | Brake pad life remaining mm |
| `discLife` | float[4] | Brake disc life remaining mm |
| `frontBrakeCompound` | int | Front brake compound type |
| `rearBrakeCompound` | int | Rear brake compound type |
| `airTemp` | float | Ambient air temperature °C |
| `roadTemp` | float | Track surface temperature °C |
| `airDensity` | float | Air density kg/m³ |
| `turboBoost` | float | Turbo boost pressure |
| `waterTemp` | float | Engine water temperature °C |
| `drs` | float | DRS activation state |
| `drsAvailable` | int | DRS availability flag |
| `drsEnabled` | int | DRS currently enabled flag |
| `tc` | float | Traction control intervention (0/1) |
| `tcInAction` | int | TC intervening this frame |
| `abs` | float | ABS state |
| `absInAction` | int | ABS intervening this frame |
| `pitLimiterOn` | int | Pit limiter active |
| `autoShifterOn` | int | Auto-shifter active |
| `isEngineRunning` | int | Engine running flag |
| `ignitionOn` | int | Ignition state |
| `starterEngineOn` | int | Starter running |
| `kersCharge` | float | KERS/ERS charge level |
| `kersInput` | float | KERS deployment |
| `kersCurrentKJ` | float | Current KERS energy kJ |
| `ersRecoveryLevel` | int | ERS recovery setting |
| `ersPowerLevel` | int | ERS power deployment level |
| `ersHeatCharging` | int | ERS heat charging flag |
| `ersisCharging` | int | ERS charging flag |
| `ballast` | float | Car ballast kg |
| `isAIControlled` | int | AI control flag |
| `P2PActivation` | int | Push-to-Pass activation (ACC) |
| `P2PStatus` | int | Push-to-Pass status (ACC) |
| `currentMaxRpm` | float | Current max RPM ceiling |
| `slipRatio` | float[4] | Tyre slip ratio [FL, FR, RL, RR] |
| `slipAngle` | float[4] | Tyre slip angle [FL, FR, RL, RR] |
| `mz` | float[4] | Self-aligning torque per tyre |
| `fx` | float[4] | Longitudinal tyre force |
| `fy` | float[4] | Lateral tyre force |
| `tyreContactPoint` | Coords[4] | World position of tyre contact patches |
| `tyreContactNormal` | Coords[4] | Normal vector at tyre contact |
| `tyreContactHeading` | Coords[4] | Heading vector at tyre contact |
| `localVelocity` | float[3] | Car-local velocity vector |
| `localAngularVelocity` | float[3] | Car-local angular velocity |
| `finalFF` | float | Final force feedback output |
| `performanceMeter` | float | Lap performance delta |
| `engineBrake` | int | Engine brake setting |
| `kerbVibration` | float | Kerb vibration feedback |
| `slipVibrations` | float | Slip vibration feedback |
| `gVibrations` | float | G-force vibration feedback |
| `absVibrations` | float | ABS vibration feedback |
