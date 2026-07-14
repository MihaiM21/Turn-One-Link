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
      "estimatedLaps": 8.2
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
      }
    },
    "session": {
      "type": "AC_RACE",
      "track": "monza",
      "car": "ferrari_488_gt3",
      "driver": "Mihai Marinescu",
      "status": "AC_LIVE"
    }
  }
}
```

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
| `estimatedLaps` | `float` | Estimated laps remaining on current fuel |

### `tyres`
All tyre arrays use **[FL, FR, RL, RR]** corner order.

| Field | Type | Description |
|---|---|---|
| `temps.fl/fr/rl/rr` | `float` | Core tyre temperature in °C |
| `pressures.fl/fr/rl/rr` | `float` | Tyre pressure in PSI |
| `brakeTemps.fl/fr/rl/rr` | `float` | Brake disc temperature in °C |

### `session`
| Field | Type | Description |
|---|---|---|
| `type` | `string` | Session type enum (e.g. `"AC_RACE"`, `"AC_PRACTICE"`, `"AC_QUALIFY"`) |
| `track` | `string` | Track identifier (e.g. `"monza"`) |
| `car` | `string` | Car model identifier |
| `driver` | `string` | Driver's full name |
| `status` | `string` | Game status enum (e.g. `"AC_LIVE"`, `"AC_PAUSE"`, `"AC_OFF"`) |

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
| `"ACC"` | Assetto Corsa Competizione (current) |
| `"AC"` | Assetto Corsa (future) |
| `"iRacing"` | iRacing (Phase 4) |
