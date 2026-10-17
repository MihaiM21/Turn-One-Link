# T1 Protocol — Unified Telemetry Schema

The **T1 Protocol** is the game-agnostic JSON format that Turn One Link broadcasts to all local mobile clients over the WebSocket server on port 8080.

Unlike the cloud backend stream (which passes raw ACC structs), the T1 Protocol normalises data across all supported games so a single mobile app can connect regardless of which sim is running.

---

## Connection Flow

### 1. Connect
```
ws://<host-ip>:8080/
```

### 2. Receive auth challenge
```json
{ "type": "auth_required", "pinLength": 6 }
```

### 3. Send PIN
```json
{ "pin": "482910" }
```

The PIN is displayed in the Turn One Link desktop app dashboard. It regenerates each time the app starts.

### 4. Receive auth result
```json
{ "type": "auth_ok", "clientId": "a3f7b2c1" }
```
or on failure:
```json
{ "type": "auth_failed" }
```

### 5. Receive T1 telemetry frames
After authentication, the server broadcasts `t1_telemetry` frames and `ping` messages.

Respond to pings with a pong to avoid being disconnected:
```json
{ "type": "pong" }
```

---

## T1 Telemetry Frame

```json
{
  "type": "t1_telemetry",
  "timestamp": 1776764355947,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "source": "ACC",
  "data": {
    "speed": {
      "kmh": 245.2,
      "mph": 152.3
    },
    "engine": {
      "rpm": 8200,
      "maxRpm": 9200,
      "gear": 5,
      "throttle": 0.92,
      "brake": 0.0,
      "clutch": 0.0,
      "tc": false,
      "abs": false
    },
    "lap": {
      "current": "1:42.341",
      "currentMs": 102341,
      "best": "1:41.112",
      "bestMs": 101112,
      "deltaMs": 1229,
      "number": 3,
      "position": 1
    },
    "fuel": {
      "liters": 42.5,
      "estimatedLaps": 8.2,
      "perLapNative": 3.1,
      "lastLapLiters": 3.05,
      "avgPerLapLiters": 3.08,
      "estimatedStintLaps": 13.8,
      "tankCapacity": 125.0
    },
    "tyres": {
      "temps": {
        "fl": 87.2,
        "fr": 88.1,
        "rl": 85.5,
        "rr": 86.0
      },
      "pressures": {
        "fl": 27.5,
        "fr": 27.4,
        "rl": 27.1,
        "rr": 27.2
      },
      "brakeTemps": {
        "fl": 312.0,
        "fr": 308.5,
        "rl": 290.1,
        "rr": 285.4
      },
      "surfaceTemps": {
        "fl": { "inner": 89.1, "middle": 87.6, "outer": 84.9 },
        "fr": { "inner": 85.2, "middle": 88.0, "outer": 90.3 },
        "rl": { "inner": 87.4, "middle": 85.9, "outer": 83.2 },
        "rr": { "inner": 83.8, "middle": 86.1, "outer": 88.7 }
      }
    },
    "session": {
      "type": "AC_RACE",
      "track": "monza",
      "car": "ferrari_488_gt3",
      "driver": "Mihai Marinescu",
      "status": "AC_LIVE"
    },
    "delta": {
      "ms": -412,
      "seconds": -0.412,
      "source": "trace",
      "hasBestTrace": true
    }
  }
}
```

> **Optional fields:** `delta`, `tyres.surfaceTemps`, and the extended `fuel` fields (`perLapNative`, `lastLapLiters`, `avgPerLapLiters`, `estimatedStintLaps`, `tankCapacity`) are **omitted from the JSON** until data is available (e.g. computed fuel metrics require at least one completed clean lap). Clients must treat them as optional.

---

## Field Reference

### `speed`
| Field | Type | Description |
|---|---|---|
| `kmh` | `float` | Speed in km/h |
| `mph` | `float` | Speed in mph |

### `engine`
| Field | Type | Description |
|---|---|---|
| `rpm` | `int` | Current engine RPM |
| `maxRpm` | `int` | Engine's max RPM (for shift-light % calculation) |
| `gear` | `int` | 0 = Reverse, 1 = Neutral, 2 = 1st gear … |
| `throttle` | `float` | Throttle input 0.0–1.0 |
| `brake` | `float` | Brake input 0.0–1.0 |
| `clutch` | `float` | Clutch input 0.0–1.0 |
| `tc` | `bool` | Traction control currently cutting |
| `abs` | `bool` | ABS currently active |

### `lap`
| Field | Type | Description |
|---|---|---|
| `current` | `string` | Current lap time formatted as `"M:SS.mmm"` |
| `currentMs` | `int` | Current lap time in milliseconds |
| `best` | `string` | Best lap time formatted as `"M:SS.mmm"` |
| `bestMs` | `int` | Best lap time in milliseconds |
| `deltaMs` | `int` | Delta vs best lap in ms. Positive = slower, Negative = faster |
| `number` | `int` | Current lap number (1-indexed) |
| `position` | `int` | Race position (1 = P1) |

### `fuel`
| Field | Type | Description |
|---|---|---|
| `liters` | `float` | Fuel remaining in litres |
| `estimatedLaps` | `float` | Estimated laps remaining on current fuel (sim-native) |
| `perLapNative` | `float?` | Sim-native fuel consumption per lap in litres |
| `lastLapLiters` | `float?` | Computed consumption of the last clean lap in litres |
| `avgPerLapLiters` | `float?` | Computed rolling average over the last 5 clean laps in litres |
| `estimatedStintLaps` | `float?` | Computed laps remaining: `liters / avgPerLapLiters` |
| `tankCapacity` | `float?` | Fuel tank capacity in litres |

Computed fields exclude laps that touched the pit lane or involved a refuel, so out/in-laps don't skew the average. They are absent until the first clean flying lap completes.

### `tyres`
All tyre arrays use **[FL, FR, RL, RR]** corner order.

| Field | Type | Description |
|---|---|---|
| `temps.fl/fr/rl/rr` | `float` | Core tyre temperature in °C |
| `pressures.fl/fr/rl/rr` | `float` | Tyre pressure in PSI |
| `brakeTemps.fl/fr/rl/rr` | `float` | Brake disc temperature in °C. **iRacing:** always `0` — the base iRacing telemetry SDK does not expose brake disc temperature (only brake pressure), so this is a permanent, sim-dependent gap, not a bug. |
| `surfaceTemps.<corner>.inner/middle/outer` | `float?` | Tyre surface temperature across the contact patch in °C (inner = towards car centreline) |

### `delta`
Real-time comparison against the session-best lap. Absent while no delta is available (menu/idle).

| Field | Type | Description |
|---|---|---|
| `ms` | `int` | Delta vs session best in ms. Positive = slower, negative = faster |
| `seconds` | `float` | Same delta in seconds |
| `source` | `string` | `"trace"` = computed by Turn One Link's position-based delta engine from your session-best valid lap; `"native"` = the sim's own delta (fallback until a best-lap trace exists) |
| `hasBestTrace` | `bool` | `true` once a full valid lap has been recorded as the reference |

### `session`
| Field | Type | Description |
|---|---|---|
| `type` | `string` | Session type enum (`"AC_RACE"`, `"AC_PRACTICE"`, `"AC_QUALIFY"`, `"AC_HOTLAP"`, `"AC_TIME_ATTACK"`, `"AC_DRIFT"`, `"AC_DRAG"`, `"AC_UNKNOWN"`) |
| `track` | `string` | Track identifier (e.g. `"monza"`) |
| `car` | `string` | Car model identifier |
| `driver` | `string` | Driver's full name |
| `status` | `string` | Game status enum (`"AC_LIVE"`, `"AC_PAUSE"`, `"AC_REPLAY"`, `"AC_OFF"`) |

> **Note:** The `AC_`-prefixed enum vocabulary above is a legacy naming artifact from when this protocol only supported ACC. It is reused as-is for every source (including `"iRacing"`) so the wire vocabulary for `session.type`/`session.status` doesn't fork per-sim — clients should treat these as fixed protocol-level enums, not ACC-specific. The `source` field on the outer frame is what discriminates which sim produced a given frame.

---

## Heartbeat / Keepalive

The server sends a `ping` every **5 seconds**. Clients must respond with a `pong` within **10 seconds** or they will be disconnected.

```json
// Server → Client
{ "type": "ping", "at": 1776764500000 }

// Client → Server
{ "type": "pong" }
```

---

## Source Values

| `source` | Description |
|---|---|
| `"ACC"` | Assetto Corsa Competizione |
| `"AC"` | Assetto Corsa (future) |
| `"iRacing"` | iRacing, via the `IRSDKSharper` SDK wrapper (Phase 4) |

---

## iRacing Mapping Caveats

iRacing's telemetry variable set differs from ACC's in shape and coverage. Notable differences, implemented
in `Services/IRacingTelemetryService.cs`:

- **`engine.tc`** — iRacing does not expose a single universal "traction control active" telemetry variable
  the way ACC does; the exact variable (and whether it's even present) can vary per car. This is currently
  hardcoded to `false` pending confirmation against a live session — treat `tc` as unreliable for iRacing
  until this is resolved.
- **`tyres.brakeTemps`** — always `0` for iRacing (see `tyres` field reference above); the base SDK only
  exposes brake *pressure*, not disc temperature.
- **`lap.current` / `lap.best`** — iRacing gives raw lap times as floating-point seconds, not pre-formatted
  strings like ACC. `T1TelemetryMapper` formats these from milliseconds (`"M:SS.mmm"`) instead of passing
  through a sim-native string.
- **Lap validity** — iRacing's base SDK has no direct "is this lap valid" boolean. `session`/lap-tracking
  logic (`DeltaEngine`, `FuelManager`) falls back to `OnPitRoad`-based exclusion, which is a weaker signal
  than ACC's explicit flag.
- **`session.status` (Live/Paused/Replay/Off) mapping** — derived from iRacing's `SessionState` variable
  (`GetInCar`/`Warmup`/`ParadeLaps`/`Racing`/`Checkered`/`CoolDown`) plus `IsReplayPlaying`. This mapping is
  a best-effort guess pending confirmation against real Live/Paused/Replay transitions in a live session.
- **`tyres.temps`/`tyres.surfaceTemps`** — iRacing exposes three temperature zones per tyre natively
  (`LFtempCL/CM/CR`, etc. — Left/Center/Right across the contact patch), which maps more directly onto
  `surfaceTemps` than ACC's separate inner/mid/outer arrays do. The center reading is also used for the
  single-value `temps` (core temperature) field for consistency with ACC's shape.

These caveats will be resolved (and this section updated) once verified against a live iRacing session.
