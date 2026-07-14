# Telemetry Overview

All messages shared by the Turn One Link desktop app over the WebSocket share the same top-level JSON envelope.

## JSON Message Schema

| Field | Type | Description |
|---|---|---|
| `type` | `string` | The message type (e.g. `"physics"`, `"graphics"`, `"static"`, `"session_start"`, `"session_end"`) |
| `timestamp` | `int64` | UTC Unix timestamp in **milliseconds** |
| `sessionId` | `string` | 32-char lowercase hex UUID for the current active session. May be `null` if no session is active. |
| `data` | `object` | The payload object — schema varies by `type` |

## Key Implementation Notes for the Frontend

### Coordinate Systems
- All `float[4]` arrays in physics data are in **[FL, FR, RL, RR]** order (Front Left, Front Right, Rear Left, Rear Right).
- `float[3]` velocity/acceleration arrays are **[X, Y, Z]** in world space.
- `carCoordinates[60]` in graphics data — only the first `activeCars` entries are populated (the rest are `{0,0,0}`).

### Timestamp
- `timestamp` is **Unix epoch in milliseconds** (UTC). You can easily parse this in JS with `new Date(timestamp)`.

### JSON Serialisation Notes
- Enum values are serialised as **strings** (`"AC_LIVE"`, `"AC_PRACTICE"`, `"AC_NO_FLAG"`, etc.) thanks to `JsonStringEnumConverter`.
- Property names are **camelCase** (`packetId`, `speedKmh`, not `PacketId`).

## Suggested Data to Display Prominently

A frontend consuming this data should typically highlight:
- **Speed** (`physics.speedKmh`) — big number display
- **Gear** (`physics.gear`) — 0=R, 1=N, 2=1st…
- **RPM** (`physics.rpms`) — RPM bar / shift light
- **Throttle / Brake / Clutch** — pedal input trace (0.0–1.0)
- **Tyre temps** (`physics.tyreCoreTemperature[4]`) — FL/FR/RL/RR per corner
- **Tyre pressures** (`physics.wheelsPressure[4]`) — PSI per corner
- **Lap time** (`graphics.currentTime`, `graphics.iCurrentTime` ms)
- **Delta** (`graphics.iDeltaLapTime` ms, positive=slower)
- **Position** (`graphics.position`)
- **Fuel left** (`graphics.fuelEstimatedLaps` laps, `physics.fuel` litres)
- **Track/Car/Driver** from the `static` message
