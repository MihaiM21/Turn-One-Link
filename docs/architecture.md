# Architecture & Data Flow

## Application Architecture

Turn One Link is a Windows desktop application acting as a real-time data bridge between sim racing titles
(Assetto Corsa Competizione, iRacing) and the Turn One cloud backend and local mobile clients.

Telemetry sources are unified behind an `ITelemetrySource` abstraction (`Services/Telemetry/ITelemetrySource.cs`):
`AccTelemetrySourceAdapter` wraps ACC's shared-memory reader, and `IRacingTelemetryService` wraps the
`IRSDKSharper` SDK wrapper for iRacing. Both translate their sim's native telemetry into sim-neutral T1 frames,
so every downstream consumer (session tracking, delta/fuel engines, cloud streaming, local broadcast) works
the same regardless of which sim is running.

```text
┌───────────────────────────────────────────────────────────┐
│                Turn One Link (Windows Desktop App)          │
│                                                             │
│  ┌───────────────────────┐                                 │
│  │ ITelemetrySource       │                                 │
│  │ (AccTelemetrySourceAdapter                               │
│  │  or IRacingTelemetryService,                             │
│  │  unified T1 frames)    │                                 │
│  └───────────┬────────────┘                                 │
│              │                                              │
│      ┌───────┴────────┐                                     │
│      ▼                ▼                                     │
│  ┌─────────────┐  ┌───────────────────────┐                 │
│  │LocalWebSocket│  │TelemetryStreamingService│                │
│  │Server        │  │(WebSocket client to    │                │
│  │(broadcasts to│  │ cloud backend — v2     │                │
│  │ N paired     │  │ sim-neutral tick/      │                │
│  │ mobile       │  │ lap_complete/          │                │
│  │ clients, T1  │  │ session_start for every│                │
│  │ local proto) │  │ source; PROTOCOL_V1=true│               │
│  └─────────────┘  │ falls back to ACC's raw │                │
│                    │ v1 struct wire format) │                │
│                    └──────────┬──────────────┘                │
│                               │                               │
│  ┌────────────────────┐      │                               │
│  │    AuthService      │──── JWT ──►                          │
│  │  (login / session) │    Bearer                             │
│  └────────────────────┘                                       │
└──────────────────────────────┼───────────────────────────────┘
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

`AccTelemetrySourceAdapter` wraps this service and translates its ACC-specific structs into sim-neutral T1
frames (see `Services/Telemetry/`). iRacing telemetry is read differently — via `IRacingTelemetryService`,
which wraps the `IRSDKSharper` SDK wrapper (named telemetry variables + YAML session info, not raw shared
memory scanning) — but produces the same unified frame shape, so everything downstream of the adapter layer
is sim-agnostic.

### 3. WebSocket Streaming (TelemetryStreamingService)

After login succeeds and a supported sim is detected running, the app opens a **WebSocket connection**:

```http
WSS: wss://backend.t1f1.com/api/ws/telemetry
Headers:
  Authorization: Bearer <access_token>
```

By default the app speaks **cloud protocol v2** (see `docs/telemetry/backend_contract_v2.md`): each
`ITelemetrySource` adapter raises sim-neutral `TickUpdated`/`LapCompleted`/`SessionInfoUpdated` events
(`Services/Telemetry/T1TelemetryFrames.cs`'s `T1TickFrame`/`T1LapComplete`/`T1SessionInfo`), rate-limited to
`STREAM_HZ` (default 20Hz, hard-capped at 60), and `TelemetryStreamingService.OnTick`/`OnLapComplete`/
`OnSessionInfo` wrap them in the `{ v: 2, type, timestamp, sessionId, data }` envelope. `SessionManager`
implements the v2 session-identity rule (`OnSessionInfoV2`): a new `sessionId` opens whenever a
`SessionInfoUpdated` reports a different source/track/car/session-type, on top of its existing v1
track/car/session-index/status-through-`Off` checks.

Setting `PROTOCOL_V1=true` falls back to the legacy v1 stream — only ACC/AC's raw shared-memory structs
(`physics`/`graphics`/`static`) go out, unversioned, and iRacing (which never spoke v1) stops streaming to
the cloud entirely. The local mobile broadcast (`LocalWebSocketServer`, `t1_protocol.md`) is unaffected by
this flag either way — it always uses the v1 `T1Physics/Graphics/StaticFrame` shape via `T1TelemetryMapper`.

Messages are sent with **no artificial delay** for the local protocol — as fast as the shared memory
updates (~100fps for physics/graphics); the cloud v2 stream is deliberately rate-limited per `STREAM_HZ`.

In **DEBUG builds**, every message is also written line-by-line to a `.jsonl` file under `bin/Debug/.../dev_logs/`. This is what `example.json` contains.
