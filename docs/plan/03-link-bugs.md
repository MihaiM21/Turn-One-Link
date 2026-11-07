# 03 — Link bugs and hardening

Found in the 2026-11-04 investigation of `master` @ `324ffd4` and confirmed against the ACC logs in
`bin\Debug\net9.0-windows\dev_logs\telemetry_20260918/19*.jsonl`. Paths are relative to the repo root.
Line numbers are as of that commit — re-check before fixing.

**Priorities:** **P0** blocks the closed beta (Phase 0) · **P1** fix before public launch (Phase 2) ·
**P2** quality/perf, fix when touching the area.

Every fix lands with a test. Most sim bugs are testable without the game by **replaying recorded
frames** through the adapter (see [11](11-quality-gates-and-release.md) §2).

---

## P0 — before any external user

| ID | Area | Symptom | Cause | Fix | Status |
|---|---|---|---|---|---|
| L-P0-01 | Installer | A rebuild today ships an app that talks to `localhost:5271`. | `installer.iss:44` copies `.env.local`; the current one has `USE_LOCAL_API=true`. | Never ship `.env.local`. Production defaults compiled in; `.env` only for overrides in `%LOCALAPPDATA%`. | done (PR #3) |
| L-P0-02 | Installer | Installed app likely fails to start on a clean PC. | `installer.iss:39-40` copies only the exe and pdb; WPF native DLLs (`wpfgfx_cor3.dll`, `PresentationNative_cor3.dll`, `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `vcruntime140_cor3.dll`) stay behind; csproj lacks `IncludeNativeLibrariesForSelfExtract`. | Set `IncludeNativeLibrariesForSelfExtract=true` or package the whole publish folder; move to Velopack (T2); verify on a clean VM. Stop shipping the PDB. | done (PR #3) |
| L-P0-03 | Repo | Installer can't be rebuilt from a clean clone; tests aren't versioned. | `installer.iss`, `build_installer.ps1` gitignored (`.gitignore:46-51`); `Turn One Link.Tests` lives outside the repo. | Commit both scripts; move the test project into the repo (`tests/`); fix the `.sln` path. | done (PR #3) |
| L-P0-04 | iRacing | iRacing is never detected, so its adapter never runs. | `Process.GetProcessesByName("iRacing")` (`Services/ConnectionService.cs:14`) — real processes are `iRacingSim64DX11`, `iRacingUI`, `iRacingService`. | Detect via IRSDKSharper (`IsConnected` / memory-mapped `Local\IRSDKMemMapFileName`). | done (PR #3) |
| L-P0-05 | ACC | `lap_complete` arrives ~32 s late, sectors wrong, `valid` always false. | Laps only detected on a sector-index change (`Services/AccTelemetrySourceAdapter.cs:190-199`); ACC flips sector 2→0 one frame **before** `CompletedLaps` increments, so the lap is caught at the next 0→1. `LastSectorTime` treated as per-sector though it behaves cumulatively/stale (logs: `[59666,93656,0,35045,…]`). | Detect lap on `CompletedLaps` increment; take `iLastTime` for the lap; derive sectors from split times (`iSplit`/current-time at boundaries); validity from `isValidLap` latched over the lap. Replay test from the 09-18/19 logs. | done (PR #3) |
| L-P0-06 | Streaming | Ticks sent from menus with no `sessionId` and all zeros. | `OnTick` (`Services/TelemetryStreamingService.cs:210-214`) and `TryEmitTick` (`AccTelemetrySourceAdapter.cs:106-115`) don't require an active Live session. | Drop ticks unless a session is active and status is Live. | done (PR #3) |
| L-P0-07 | Sessions | Two `session_start` per session; delta/fuel engines reset. | `BeginSession` emits immediately *and* sets `_staticPendingForNewSession` so the next `OnStatic` emits again (`Services/SessionManager.cs:114-128, 217-235`). | One emit path; unit test on `SessionManager`. | done (PR #3) |
| L-P0-08 | Sessions | `session_end` lost when the sim exits; never sent when the app quits. | `OnSimDisconnected()` queues it, then `StopTelemetry()` cancels and closes the socket (`MainWindow.xaml.cs:172-173`, `TelemetryStreamingService.cs:113-123`); `PrepareExit` (`MainWindow.xaml.cs:56-65`) sends nothing. | With local-first (D3) the recorder closes the session file and the upload queue delivers it. For live: flush control messages with a 2 s timeout before closing. | done (PR #3) |
| L-P0-09 | Sessions | Restarting the same track/car/type creates a new session with no `session_start`. | `_lastSessionInfoKey` dedup (`AccTelemetrySourceAdapter.cs:241-243`; iRacing `IRacingTelemetryService.cs:425-427`) isn't reset by `RequestStaticRefresh`. | Reset the key on every new session (as `F1UdpTelemetrySource.cs:93` does). | done (PR #3) |
| L-P0-10 | Streaming | Important messages lost under load or on a failed send. | Queue of 1000 drops oldest regardless of type (`TelemetryStreamingService.cs:83-88`); failed sends discarded (`:436-445`); no send timeout; `session_start` not re-sent after reconnect. | Separate control queue (never dropped); re-send current `session_start` on reconnect; send timeout. Superseded in part by the upload queue. | done (PR #3) |
| L-P0-11 | LAN server | Phones get dropped; server is attackable on the LAN. | Ping loop sends without `SendLock` while broadcasts use it (`Services/LocalWebSocketServer.cs:285-305`); semaphore disposed under a pending `Release` (`:303-304, 398`); handshake read byte-by-byte with no size limit/timeout (`:341-361`); PIN from `Random.Shared` (`MainWindow.xaml.cs:228`); no attempt limit; no Origin check. | One send loop per client (channel); 8 KB / 5 s handshake limit; `RandomNumberGenerator` PIN; 5 attempts then 60 s lockout per IP; bind to the LAN interface, not `0.0.0.0`. | done (PR #3) |
| L-P0-12 | ACC | App stays "Connected" after the game closes. | Poller keeps MMF handles open (`Services/AccTelemetryService.cs:115-132`), so `TryOpenAccSharedMemory` (`ConnectionService.cs:93-104`) keeps succeeding (debug log: 56 connects / 9 disconnects). | Disconnect when `PacketId` stops advancing for 3 s *and* the ACC process is gone. | done (PR #3) |
| L-P0-13 | Auth | A restored token isn't checked; expiry signs the user out. | No refresh, no startup validation (`MainWindow.xaml.cs:436-441`). | Device pairing (T1, HCR-006). | done — token checked at startup (PR #3), browser pairing with rotating device token (PR #4) |

## P1 — before public launch

| ID | Area | Problem | Where | Fix | Status |
|---|---|---|---|---|---|
| L-P1-01 | Lifecycle | Sign-out/in stacks `SimStatusChanged` handlers; telemetry never restarts if the game is already running. | `MainWindow.xaml.cs:154`; `ConnectionService.cs:34,45` (`_lastSim` not reset on `Stop`) | Subscribe once; reset `_lastSim` on `Stop`. | done — subscribed once, `_lastSim` reset on `Stop` |
| L-P1-02 | F1 | Busy UDP port fails silently; F1 warnings never shown. | `Services/F1/F1UdpTelemetrySource.cs:76-78` throws in a timer handler; created without log callback (`MainWindow.xaml.cs:316`) | Surface "port 20777 in use" in the UI; pass the logger. | done (PR #8) — busy port reported in the console, logger passed |
| L-P1-03 | F1 | Contract mismatches. | `driverStatus` sent raw (`F1UdpTelemetrySource.cs:351`); `trackId` a slug instead of numeric string; reverse layouts mapped 33/34/35 (`F1Packets.cs:299-301`) — verify vs F1 25 spec (believed 39/40/41); sizes checked against 2025 only (`F1Packets.cs:41-54`); status always Live (`:386`); spectator `PlayerCarIndex=255` → car 0 (`:157`) | Map per contract; per-`packetFormat` size tables (2025, 2026); pause/end from SSTA/SEND + Session packet; ignore spectating. | partly (PR #8) — `driverStatus` per contract, spectator packets ignored; `trackId`, reverse layouts, 2026 size table and SSTA/SEND status still todo |
| L-P1-04 | F1 | Driver "Player", generic car, no tyre wear, no setup. | Participants, CarDamage, Car Setups, TyreSets not parsed | Parse them (Car Setups feeds D7). | todo (with LF-20) |
| L-P1-05 | F1 | Detection fails for console and for titles like "EA SPORTS F1…". | Process-name detection; `StartsWith("F1")` | "Listen for UDP" mode (D15): source = F1 when valid F1 packets arrive. | done (PR #8, LF-15) — not yet tested on a real console |
| L-P1-06 | iRacing | Bad lap data. | `LapLastLapTime=-1` → `lapTimeMs:-1000` (`IRacingTelemetryService.cs:328-341`); sectors by PC clock (`:305`); `CompletedLaps` fed from `Lap` (`:131`); Lat/Lon as radians (`:261`); garage counted as Live (`:471-488`); TC hardcoded off | Use `SessionTime`, `LapCompleted`, `LapLastLapTime>0`, `IsOnTrack`; test live (4 `TODO(no-live-iracing)`). | done (PR #10) — not yet tested live in iRacing |
| L-P1-07 | ACC | Car position wrong online. | `coords[g.PlayerCarID]` (`AccTelemetrySourceAdapter.cs:270-275`) | Index `i` where `CarIDs[i]==PlayerCarID`. | todo |
| L-P1-08 | ACC | `tyreWear` always zeros; fuel looked constant in logs. | `AccTelemetrySourceAdapter.cs:163` | Omit fields the sim doesn't fill; verify fuel with a replay. | todo |
| L-P1-09 | Mapping | Clutch inverted for some sources; previous sim's data leaks. | ACC raw (`AccTelemetrySourceAdapter.cs:304`) vs `1-x` (`F1UdpTelemetrySource.cs:373`, `IRacingTelemetryService.cs:97`); static state in `T1TelemetryMapper.cs:110-115` | One convention (0 = released) documented in the protocol; instance mapper reset per session. | todo |
| L-P1-10 | App | Port-change restart disposes services twice. | `MainWindow.xaml.cs:270` and `App.xaml.cs:86` | Single shutdown path. | todo |
| L-P1-11 | App | Three version strings disagree. | `TelemetryStreamingService.cs:78`, `Views/DashboardView.xaml:451`, `installer.iss:6` | T5 — csproj `<Version>`. | done (PR #3) — csproj `<Version>` only |
| L-P1-12 | Network | LAN IP picks the VPN adapter. | "connect" to `8.8.8.8` (`MainWindow.xaml.cs:236`) | Enumerate up, non-virtual IPv4 interfaces; let the user pick. | todo |
| L-P1-13 | Streaming | `Dispose` races `StopAsync`. | `TelemetryStreamingService.cs:491-497` | Await stop, then dispose. | todo |

AC original (ACC layout used for AC — tick `Valid` always false, steer not divided by lock,
`trackId` missing `/config`, fields past AC's struct are garbage) is **deferred to v1.1** with
the AC adapter rewrite (D2); until then, AC is labelled "experimental" in the UI and its laps
are excluded from leaderboards.

## P2 — quality and performance

| ID | Problem | Where | Fix |
|---|---|---|---|
| L-P2-01 | HUD dispatches to the UI thread on every physics frame (~200 Hz). | `MainWindow.xaml.cs:477`, `Views/DashboardView.xaml.cs:184` | 30 Hz UI timer reading the latest frame. |
| L-P2-02 | Console log appends to a growing string. | `DashboardView.xaml.cs:170` | Ring buffer + `ItemsControl` virtualisation. |
| L-P2-03 | LAN broadcast serialises a full frame on every physics and graphics event (~400/s/phone). | `LocalWebSocketServer.cs` | 20–30 Hz broadcast of the latest frame; drop when a client is behind. |
| L-P2-04 | `ReadStruct` allocates per read. | `AccTelemetryService.cs:146-161` | Reuse buffers; `MemoryMarshal`. |
| L-P2-05 | Session logs at full physics rate (hundreds of MB/h); `dev_logs` at 1.1 GB. | `SessionLoggingService.cs:116` | Replaced by the recorder at `STREAM_HZ`; dev logs capped. |
| L-P2-06 | `timeBeginPeriod(1)` for the whole app lifetime. | `App.xaml.cs` | Only while a sim is connected. |
| L-P2-07 | `Process` objects never disposed; timer can re-enter. | `ConnectionService.cs:82,110` | `using`; non-reentrant timer. |
| L-P2-08 | `AccTelemetryService.Dispose` blocks the UI for up to 500 ms. | `AccTelemetryService.cs:166` | Async stop. |
| L-P2-09 | `catch { }` everywhere. | `AppSettings`, `SessionLoggingService`, `App.LoadTrayIcon`, `ConnectionService.Log`, `LocalWebSocketServer` | Log at least once per type; Sentry breadcrumb. |
| L-P2-10 | `turnone_debug.log` written to the current working directory; `settings.json` next to the exe. | `ConnectionService.cs:64`, `AppSettings.cs` | Both under `%LOCALAPPDATA%\TurnOneLink\`. |
| L-P2-11 | Documented env vars never read (`LOG_LEVEL`, `DEBUG_MODE`, `CONNECTION_*`, `TELEMETRY_*`, `SECURE_TOKEN_STORE_ENABLED`). | `docs/setup/environment.md` | Delete from docs or implement. |
| L-P2-12 | "Using DEV API endpoint" logged when using production. | `AuthService.cs:35` | Fix message. |
| L-P2-13 | Docs drift: `.env` "committed" (it's ignored), poll 10 ms (it's 5 ms), AC "future", F1 missing from `t1_protocol.md`, `CLAUDE.md` "Phase 2 in progress", `environment.md` says local mode reads `API_URL`. | `CLAUDE.md`, `docs/architecture.md`, `docs/telemetry/t1_protocol.md`, `docs/setup/environment.md` | Update with the Phase 0 PR. |
| L-P2-14 | Uninstall leaves `%LOCALAPPDATA%\TurnOneLink` (token, sessions). | installer | Ask on uninstall; always remove the token. |
