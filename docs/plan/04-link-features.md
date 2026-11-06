# 04 — Link features

Link stays a thin bridge (D4). Everything here is about **capturing reliably, uploading reliably,
and getting out of the way**. IDs are referenced from [10-roadmap.md](10-roadmap.md).

## Phase 0 — make it shippable

| ID | Feature | Notes | Depends on |
|---|---|---|---|
| LF-01 | **Velopack installer + auto-update** | Per-user install, delta updates, `beta`/`stable` channels; update check on start and every 6 h; "Restart to update" when no sim is running, never mid-session. | HCR-014 for the feed (GitHub Releases as fallback) |
| LF-02 | **Code signing** | Azure Trusted Signing in CI (T3); sign exe + installer. | CI ([11](11-quality-gates-and-release.md)) |
| LF-03 | **Crash reporting** | Sentry, opt-out toggle, no telemetry payloads in events (T4). | — |
| LF-04 | **Production config compiled in** | No `.env` shipped; `%LOCALAPPDATA%\TurnOneLink\overrides.env` for power users. | L-P0-01 |

## Phase 1 — closed beta

| ID | Feature | Notes | Depends on |
|---|---|---|---|
| LF-10 | **Device pairing login** | "Sign in" opens the browser to `turnonehub.com/link?code=…`; Link polls; token in DPAPI store; "Signed in as X · Sign out · Manage devices". No password field in Link. | HCR-006 |
| LF-11 | **Local recorder** | v2 session file per session ([07](07-data-model-and-contracts.md) §2); crash-safe; replaces `SessionLoggingService`. | L-P0-05…09 |
| LF-12 | **Upload queue** | Outbox folder; uploads each lap when it closes, finalises at `session_end`; back-off; resumes after restart; per-session state *Recording · Uploading · Uploaded · Failed (retry)*. | HCR-007 |
| LF-13 | **Session list** | Last 50 sessions: sim, track, car, laps, best, upload state, "Open on web". Import old JSONL sessions from the previous logger (one-time). | LF-11, LF-12 |
| LF-14 | **Live toggle** | "Live" switch (off by default) for spectate/overlay; shows viewer count. Uses existing WSS. | H-03 ownership fix |
| LF-15 | **F1 console mode** | "I play on PS5/Xbox" → listens for UDP without process detection; shows PC IP + port and a 4-step guide with screenshots (Settings → Telemetry → UDP On, IP, port 20777, format 2025). Firewall rule added by the installer. | L-P1-05 |
| LF-16 | **Per-game health check** | Per sim: "Game detected ✓ · Data arriving ✓ 20 Hz · Lap timing ✓ · Uploading ✓". Explains the fix when a step fails (e.g. F1 UDP off, wrong format, port busy). | — |
| LF-17 | **LAN phone dashboard** | Link serves a static web page; QR code encodes `http://<lan-ip>:<port>/#<pin>`; screens: delta bar, lap/sector times, fuel laps left, tyre temps/pressures, gear/RPM. Dark theme from turn-one-media tokens. | L-P0-11, L-P2-03 |
| LF-18 | **Report a problem** | Zips last 2 session files (opt-in), logs, settings (no token), app version → uploads to Hub support endpoint or opens an email. | — |

## Phase 2 — public launch

| ID | Feature | Notes | Depends on |
|---|---|---|---|
| LF-20 | **Setup capture** | ACC: watch the setups folder, hash the file loaded at session start (ACC exposes no "active setup" — use most recently modified file for car+track, confirm in UI). F1: Car Setups packet. iRacing: "Attach setup" picks a `.sto`. Setup uploaded once (dedup by hash) and linked to laps. | HCR-010 |
| LF-21 | **Install setup from web** | Website "Install in Link" → Link polls `GET /api/setups/pending-installs` (or WSS push) → writes into `Setups\<car>\<track>\TurnOne - <name>.json` (ACC) / iRacing setups folder; F1 can't be written — show values to copy. | HCR-010 |
| LF-22 | **Start with Windows + tray-first** | Optional; starts minimised; tray shows state colour. | — |
| LF-23 | **Notifications** | Toasts: "New PB at Spa — 2:17.412 (#14 on leaderboard)", "Weekly Hotlap: you're #3". | HCR-009, HCR-012 |
| LF-24 | **First-run onboarding** | 3 screens: sign in → pick your games → health check. | LF-10, LF-16 |
| LF-25 | **Localisation** | English first; Romanian second; strings in resources. | — |

## Phase 3 — after launch

| ID | Feature | Notes |
|---|---|---|
| LF-30 | **LMU + rFactor 2 adapter** | rF2 shared-memory plugin (`rFactor2SharedMemoryMapPlugin64.dll`); Link offers to install the plugin. |
| LF-31 | **AC original adapter** | Proper AC structs (not ACC's); `trackId/config`; steer ÷ lock. |
| LF-32 | **Delta widget** (optional, D14) | One small always-on-top window: delta vs PB / vs leaderboard #1 / vs challenge target. No VR. |
| LF-33 | **SimHub export** | Expose Turn One delta targets as SimHub properties (plugin or UDP). |
| LF-34 | **Reference-lap download** | Pull a leaderboard lap as the delta reference for LF-17/LF-32. |
| LF-35 | **Team sync** | Team setups appear in the install list (D16, HCR-016). |

## Explicitly not in Link

Lap analysis, charts, compare, coach, leaderboards UI — all on the website (D4). In-game overlays
beyond LF-32 (D14).
