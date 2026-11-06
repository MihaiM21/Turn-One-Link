# 02 — Target architecture

## The picture

```
 ┌──────────────── PC (or PC next to a PS5/Xbox) ─────────────────────────────────────────┐
 │                                                                                          │
 │  ACC / AC ── shared memory ─┐                                                            │
 │  iRacing ─── IRSDKSharper ──┼─► Sim adapters ─► normalised v2 frames ─┬─► Local recorder │
 │  F1 25/26 ── UDP :20777 ────┘   (ITelemetrySource)  (tick, lap,       │   (session file) │
 │  (console sends UDP here)                            session_*)        │        │         │
 │                                                                        │        ▼         │
 │                                   Setup watcher (ACC files, F1 pkt) ──►│   Upload queue ──┼──► HTTPS  POST /api/simracing/uploads   (HCR-007)
 │                                                                        │   (resumable,    │
 │                                                                        │    idempotent)   │
 │                                                                        ├─► Live streamer ─┼──► WSS    /api/ws/telemetry          (existing,
 │                                                                        │   (opt-in: spectate,         only when live is on)
 │                                                                        │    overlay, live coach)
 │                                                                        └─► LAN server ────┼──► phone browser  http://<pc-ip>:<port>/  (QR)
 │                                                                            (web dash + ws)
 └──────────────────────────────────────────────────────────────────────────────────────────┘

 ┌──────────────── Hub (backend.t1f1.com) ─────────────────────────────────────────────────┐
 │  Upload API ─► lap store ─► LapProcessingWorker / LapProcessor (existing, 2 m grid,      │
 │  (HCR-007)     (Postgres)   corners, track profiles) ─► Lap validation (HCR-009)         │
 │                                                     ─► Leaderboard materialiser (HCR-009)│
 │                                                     ─► XP / challenge awards (HCR-012)   │
 │                                                     ─► Discord webhook (HCR-013)         │
 │  WSS ingestion (existing) ─► SignalR SimTelemetryHub ─► spectate / OBS overlays          │
 │  Track & car catalog (HCR-008)   Setups (HCR-010)   Comments (HCR-011)                   │
 │  Device tokens (HCR-006)   Release feed (HCR-014)   AI consent + coach feedback (HCR-015)│
 └──────────────────────────────────────────────────────────────────────────────────────────┘
                     ▲
 turnonehub.com (Next.js 16) — sessions, laps, compare, coach, leaderboards, setups, tracks,
 profiles, Weekly Hotlap, /link pairing page, /download
```

## Link internals (target)

| Component | Exists today | Change |
|---|---|---|
| `ConnectionService` | Polls processes every 500 ms | Use each SDK's own "is running" check (ACC MMF + PacketId advancing, IRSDKSharper `IsConnected`, F1 = UDP packets arriving). Add a **"Listen for UDP"** mode independent of processes (D15). |
| Sim adapters (`AccTelemetrySourceAdapter`, `IRacingTelemetryService`, `F1UdpTelemetrySource`) | Emit legacy + v2 events | Fix the bugs in [03](03-link-bugs.md); emit only v2 frames; legacy events derived from v2 for the HUD/LAN. |
| `SessionManager` | GUID sessions, start/end/pause/resume | Single source of truth for session lifecycle; one `session_start` per session; guaranteed `session_end`. |
| **Local recorder** (new, replaces `SessionLoggingService` role) | JSONL physics at full rate | Write the **v2 session file** ([07](07-data-model-and-contracts.md) §2) at `STREAM_HZ`, append-only, crash-safe (flush per lap). Full-rate physics only in a debug option. |
| **Upload queue** (new) | — | Persistent queue in `%LOCALAPPDATA%\TurnOneLink\outbox\`; uploads finished laps as soon as they close and the session file at `session_end`; exponential back-off; survives restarts. |
| `TelemetryStreamingService` (live) | Always on, drops messages | Opt-in "Live" toggle (needed for spectate/overlay). Priority for control messages; re-send `session_start` after reconnect. Never the only path for data. |
| `LocalWebSocketServer` | `ws://` + PIN, no client | Serves a static web dashboard + the T1 frame stream; QR with `http://ip:port/#pin`; throttled to 20–30 Hz; hardened (see L-P0-11). |
| `AuthService` + `SecureTokenStore` | Email/password → 7-day JWT | Device pairing (T1, HCR-006); DPAPI store kept. |
| Setup watcher (new) | — | ACC: `Documents\Assetto Corsa Competizione\Setups\<car>\<track>\*.json` file watcher; F1: Car Setups packet; iRacing: manual pick of `.sto`. |
| Updater (new) | — | Velopack, channel from the release feed (HCR-014). |

## Auth flows

**Device pairing (HCR-006).**
1. Link `POST /api/devices/pair/start` → `{ deviceCode, userCode: "K7QF-2M9D", verifyUrl, interval }`.
2. Link shows the code and opens `turnonehub.com/link?code=K7QF-2M9D`; the logged-in user approves.
3. Link polls `POST /api/devices/pair/poll` → `{ accessToken (15 min), deviceToken (refresh, 180 d, rotating) }`.
4. Access tokens carry `scope=link` → allowed: uploads, live WSS, setups read/write, own sessions read.
   Not allowed: profile, password, billing, admin.
5. Devices are listed and revocable under Settings → Devices on the website.

**Live WSS** keeps `Authorization: Bearer` with the `scope=link` access token; the Hub checks
session ownership on every message ([05](05-hub-security-and-fixes.md) H-03).

## Data lifetime

| Data | Where | Kept |
|---|---|---|
| Session file (v2 frames at 20 Hz) | Link outbox | Until uploaded + 7 days, then deleted locally (setting: keep forever) |
| Raw ticks | Influx | Only for live sessions; 7 days |
| Processed lap (2 m grid, Brotli) | Postgres `LapTelemetries` | Forever (D5); free tier keeps full-res for each best lap per sim × track × car, downsampled for the rest |
| Lap metadata (times, sectors, validity, setup link) | Postgres | Forever |
| Leaderboard entries | Postgres (materialised) | Recomputed on new lap / validation change |

## Canonical identity across sims (HCR-008)

Every sim names tracks and cars differently (`Paul_Ricard`, F1 track id `7`, iRacing `trackId`
+ config). A catalog maps `(source, simTrackId[, layout])` → `TrackLayout` and
`(source, simCarId)` → `Car` + `CarClass`. Leaderboards, setups, track pages and challenges key
on canonical ids; unknown ids create a *provisional* entry an admin curates (the existing
`TrackProfile.Status` Provisional/Stable/Curated fits this).
