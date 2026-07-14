# Architecture & Data Flow

## Application Architecture

Turn One Link is a Windows desktop application acting as a real-time data bridge between Assetto Corsa Competizione (ACC) and the Turn One cloud backend.

```text
┌─────────────────────────────────────────────────────┐
│           Turn One Link (Windows Desktop App)        │
│                                                     │
│  ┌────────────────────┐   ┌──────────────────────┐  │
│  │  AccTelemetryService│──▶│TelemetryStreamingService│
│  │  (shared memory    │   │  (WebSocket client)   │  │
│  │   reader, ~100Hz)  │   │                       │  │
│  └────────────────────┘   └──────────┬────────────┘  │
│                                      │               │
│  ┌────────────────────┐              │               │
│  │    AuthService      │──── JWT ────►               │
│  │  (login / session) │    Bearer    │               │
│  └────────────────────┘              │               │
└─────────────────────────────────────┼───────────────┘
                                       │ WebSocket (WSS)
                                       ▼
                        ┌─────────────────────────────┐
                        │  Turn One Backend            │
                        │  https://backend.t1f1.com   │
                        │                             │
                        │  REST: /api/auth/login      │
                        │  REST: /api/auth/me         │
                        │  WSS:  /api/ws/telemetry    │
                        └─────────────────────────────┘
                                       │
                                       ▼
                              Frontend Website / Mobile App
```

---

## Step-by-Step Data Flow

### 1. Authentication (REST)

Before any telemetry flows, the desktop app logs in via HTTP POST:

```http
POST https://backend.t1f1.com/api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "..."
}
```

The backend returns a **JWT access token** (looked up in `accessToken`, `token`, or `jwt` fields, nested under `data` if needed). This token is stored securely and used for:
- Setting the `Authorization: Bearer <token>` header on the WebSocket upgrade request.
- Optionally refreshing via `GET /api/auth/me`.

### 2. Shared Memory Reading (AccTelemetryService)

The app opens three **Windows named shared memory files** created by ACC while running:

| Shared Memory Name | Content |
|---|---|
| `Local\acpmf_physics` | Real-time physics (speed, RPM, tyres, brakes…) |
| `Local\acpmf_graphics` | Session/race state (lap, position, flags, fuel…) |
| `Local\acpmf_static` | One-time static info (car model, track, driver name…) |

The poll loop runs every **10ms** (~100Hz), de-duplicating by `PacketId` so unchanged frames are not transmitted. The `static` block is only read once per session since it doesn't change.

### 3. WebSocket Streaming (TelemetryStreamingService)

After login succeeds and ACC is detected running, the app opens a **WebSocket connection**:

```http
WSS: wss://backend.t1f1.com/api/ws/telemetry
Headers:
  Authorization: Bearer <access_token>
```

For every new telemetry frame, the app serialises and sends a **text WebSocket message** as JSON.

Messages are sent with **no artificial delay** — as fast as the shared memory updates (~100 fps for physics/graphics).

In **DEBUG builds**, every message is also written line-by-line to a `.jsonl` file under `bin/Debug/.../dev_logs/`. This is what `example.json` contains.
