# Backend Contract

The Turn One Link client sends a rich, sessionised telemetry stream over the `wss://backend.t1f1.com/api/ws/telemetry` endpoint.

## 1. Message Envelope

Every message has a `sessionId` field on the envelope.

```json
{
  "type": "physics",
  "timestamp": 1776764355947,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": { ... }
}
```

- Format: 32-char lowercase hex (`Guid.NewGuid().ToString("N")`).
- May be `null` for `client_heartbeat` sent before any session begins, or for `static` frames received before the first session.
- For all other types, it will be populated whenever a session is active.
- Use `sessionId` as the grouping key for storage / fan-out.

## 2. Session Management Messages

### 2.1 `session_start`

Emitted when a new session is detected (game enters `AC_LIVE` for the first time, or track / car / session-index changes mid-stream).

```json
{
  "type": "session_start",
  "timestamp": 1776764350000,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
    "sessionType": "AC_PRACTICE",
    "track": "monza",
    "carModel": "ferrari_488_gt3",
    "driver": "Mihai Marinescu",
    "startedAt": 1776764350000
  }
}
```

Backend should: create / upsert a session record keyed by `sessionId`, store metadata, mark as `active`.

### 2.2 `session_end`

Emitted when the game returns to `AC_OFF`, the sim disconnects, or the user starts a different session.

```json
{
  "type": "session_end",
  "timestamp": 1776764999000,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
    "endedAt": 1776764999000,
    "completedLaps": 14,
    "bestLapMs": 105234
  }
}
```

Backend should: mark the session record as `ended`, persist final stats, stop fan-out for that session.

### 2.3 `session_pause` / `session_resume`

Emitted on `AC_LIVE ↔ AC_PAUSE` transitions.

```json
{
  "type": "session_pause",
  "timestamp": 1776764500000,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
    "at": 1776764500000
  }
}
```

While paused:
- The client **stops** sending `physics` frames.
- The client **continues** sending `graphics` frames so the backend can show "Paused" state.
- A `session_resume` event with an identical payload shape arrives when the game returns to `AC_LIVE`.

### 2.4 `client_heartbeat`

Sent every ~15 seconds whenever the WebSocket is connected, regardless of whether a session is active.

```json
{
  "type": "client_heartbeat",
  "timestamp": 1776764500000,
  "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
  "data": {
    "sessionId": "9f4a2c1e7b3d4f80a1c2e5d8f9a0b1c2",
    "clientVersion": "0.0.1.0",
    "uptimeMs": 184302
  }
}
```

Backend should: update a `last_seen_at` timestamp per connection / user. Use this for "client online" indicators.

## 3. Connection Lifecycle Expectations

The client reconnects automatically and persists across short outages.
- **Auto-reconnect with exponential backoff**. After a network blip, expect a fresh upgrade request.
- **Same `sessionId` may appear across multiple WebSocket connections.** The backend must not start a new session record on reconnect — only `session_start` / `session_end` events open and close session records.
- **Multiple `session_start` events can arrive on a single WebSocket connection.**
- **Order guarantee within a connection only.**
- **Bounded backpressure.** The client buffers up to 1000 frames in memory; if the network is slow, oldest frames are dropped.

## 4. Suggested DB Schema

```sql
CREATE TABLE telemetry_sessions (
  session_id        CHAR(32) PRIMARY KEY,
  user_id           BIGINT NOT NULL,         -- from JWT subject
  session_type      VARCHAR(32),
  track             VARCHAR(64),
  car_model         VARCHAR(64),
  driver            VARCHAR(128),
  started_at        TIMESTAMPTZ NOT NULL,
  ended_at          TIMESTAMPTZ,
  completed_laps    INT,
  best_lap_ms       INT,
  client_version    VARCHAR(32),
  last_seen_at      TIMESTAMPTZ,            -- updated on heartbeat
  status            VARCHAR(16) DEFAULT 'active'  -- active | paused | ended
);

CREATE INDEX ix_sessions_user_started ON telemetry_sessions (user_id, started_at DESC);
CREATE INDEX ix_sessions_status      ON telemetry_sessions (status) WHERE status <> 'ended';
```

## 5. Live Frontend Re-broadcast

To let the website watch live telemetry, the backend needs a **read** WebSocket endpoint:
```
GET wss://backend.t1f1.com/api/ws/telemetry/subscribe?sessionId=<id>
```

On connect, send the most recent `session_start` + `static` so late joiners see metadata. Then forward every subsequent frame with that `sessionId` until `session_end`.
