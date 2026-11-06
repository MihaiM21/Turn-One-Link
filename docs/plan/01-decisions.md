# 01 — Decisions

Agreed with the owner on **2026-11-04**. Each decision has the reasoning and what was rejected.
To change one, add a new decision that supersedes it (as turn-one-mobile does) — don't edit history.

---

### D1 — Primary user: the driver who wants to get faster
Analysis and coaching are the core; community, setups and streaming support that.
**Why:** the Hub already has the hard part — a 2 m-grid lap processor, corner detection, track
profiles, compare endpoints (`LapProcessor`, `LapCutRule`, `LapCorner`). That is the
differentiator; leaderboards alone are a commodity.
**Rejected:** community-first, streamer-first, F1-game-only.

### D2 — v1 sims: ACC + EA F1 25/26 + iRacing
AC original, Le Mans Ultimate and rFactor 2 follow in v1.1+ (LMU and rF2 share the rF2
shared-memory plugin, so they come together).
**Why:** ACC and F1 have real logs and the most mature adapters; iRacing is the largest paying
audience in sim racing and its adapter already exists — it just never runs (see [03](03-link-bugs.md) L-P0-04).
**Rejected:** ACC + F1 only; everything at once.

### D3 — Local-first data path
Link **always records locally**, then uploads completed laps/sessions over HTTP with retry; the
server de-duplicates idempotently. Live WSS streaming stays only for spectate, OBS overlays and
live features.
**Why:** today a dropped connection loses the lap, and `session_end` is routinely lost. Local-first
is robust offline, cheaper (no per-tick Influx write for every user) and allows importing old sessions.
**Rejected:** live-only (as today, just fixed); live + recovery upload as equals.

### D4 — Analysis lives on the website only
Link is a thin, stable bridge: status, live HUD, session list with upload status, "Open on web".
No separate desktop analysis app.
**Why:** one analysis codebase, shareable links, the subscription is sold on the web, and it is how
Garage 61 / Track Titan / VRS work.
**Rejected:** offline analysis inside Link; a third MoTeC-style app.

### D5 — Generous freemium; data is never auto-deleted
- **Free:** every lap kept (full telemetry for each best lap per sim × track × car, metadata for the
  rest), leaderboards, setups, analysis of own laps.
- **Paid:** compare against other people's laps, corner coach, full-resolution history of every lap,
  spectate/overlays, future AI.

Replaces today's cap (BASIC 1 / PRO 5 / ELITE 15 stored sessions, oldest **deleted** —
`TelemetrySessionService.cs:42-60, 112-129`, `Domain/PlanFeatures.cs`).
**Why:** deleting data kills leaderboards and history and is the first thing a reviewer will mention.
Pay for insight, not storage.
**Rejected:** keep current caps; everything free at launch.

### D6 — Strict, verified leaderboards
Keyed by sim × track layout × car, with a class rollup (GT3, F1…). Only laps the sim marks valid,
plus server checks: sector sum = lap time, distance ≈ track length, plausible speed, no position
jumps. Condition splits where they matter (F1: assists; ACC: wet/dry), game version, platform as a
filter. Top-N laps have public telemetry; a report button exists.
**Why:** transparency is the best anti-cheat a client-side tool can have.
**Rejected:** simple best-lap lists; leagues/events (partly covered by D13).

### D7 — Setups tied to laps, free
Link captures the active setup automatically (ACC JSON files, F1 "Car Setups" UDP packet; iRacing
manual `.sto` upload) and attaches it to the lap. The website shows a library filterable by
sim/track/car/conditions with the proven lap time. "Install" sends the setup to Link, which writes
it into the game's folder where the game allows.
**Rejected:** plain file-upload library; paid marketplace (payments, VAT, rights — not now).

### D8 — AI: own model later; rule-based coach + dataset now
No LLM at launch. Improve the deterministic coach (corner vs own PB and vs leaderboard top:
*"you brake 12 m earlier into T1"*). From day one collect, **with opt-in consent**, the dataset a
future model needs: laps, generated insights, 👍/👎 feedback on each tip.
**Why:** owner wants an own model eventually; zero per-user cost and explainable tips meanwhile.
**Rejected:** Claude-API agent now as a bridge; no AI at all.

### D9 — Community: Discord + contextual comments
Discord for chat (bot posts new records; account linking). On the site, comments attached to
setups, tracks (track guides) and public laps. No generic forum at launch.
**Rejected:** own forum; hosted Discourse.

### D10 — Phone dashboard: LAN web page now, mobile app tab later
Link serves a web dashboard on the LAN; the user scans a QR code and opens it in the phone's
browser (delta, fuel, tyres, sectors). A "Sim" tab in turn-one-mobile comes after mobile v1,
through that repo's own process.
**Rejected:** native-only; dropping LAN from v1.

### D11 — Timeline
- **Phase 0, Nov–Dec 2026:** Hub security, Link P0 bugs, installer — before any external user.
- **Closed beta, Jan 2027:** 20–50 drivers from the YouTube/Discord audience.
- **Public launch, Mar 2027:** with leaderboards and setups, ahead of the 2027 F1 season.

### D12 — Default visibility
Best-lap times appear on leaderboards by default (opt-out, or anonymous). Telemetry of leaderboard
laps is visible (free: simple trace overlay; paid: full compare + download). Whole sessions stay
private until shared. Using data for AI training is a **separate GDPR consent**.

### D13 — Gamification: server-controlled XP + Weekly Hotlap Challenge
XP for valid laps and personal bests; coins for a **Weekly Hotlap Challenge** — a fixed
sim + track + car each week, tied to the real F1 GP of that weekend when the game has the track.
Announced on Discord/YouTube. All awards computed server-side (today's
`POST /api/levelsystem/add-experience` is client-callable — see [05](05-hub-security-and-fixes.md) H-06).

### D14 — No in-game overlays in v1
Keep the existing OBS browser overlays. Maybe a delta-vs-PB/leaderboard widget or a SimHub
export in Phase 3.
**Why:** SimHub/RaceLab own this; DirectX/VR overlays are a large, separate product.

### D15 — F1 console players in v1
A "Listen for UDP" mode that doesn't require the game process on the PC, plus a step-by-step guide
showing the PC's IP. Platform (PC/PS/Xbox) is a leaderboard filter.
**Rejected:** PC only; cloud UDP relay (needs port forwarding, complex).

### D16 — Teams in Phase 3 (after launch)
Groups with shared laps/setups, a read-only coach role, paid per team. Not in the beta.

### D17 — Where this plan lives
`Turn One Link/docs/plan/`, in English, mirroring turn-one-mobile's doc pattern. Hub additions are
HCRs numbered **HCR-006+**; HCR-003 (refresh tokens) and HCR-005 (no `User` entity in responses)
from turn-one-mobile are reused, not duplicated.

---

## Technical defaults (not interview questions; change by adding a decision)

| # | Default | Why |
|---|---|---|
| T1 | **Device pairing** login: Link shows a code, the user approves it at `turnonehub.com/link`; Link receives a long-lived, revocable device token scoped to telemetry/setups (HCR-006, built on HCR-003). | No password stored in Link; "stays signed in" becomes true; a leaked token can't change the account. |
| T2 | **Velopack** for install + delta auto-update. | Replaces Inno Setup's manual flow; built for .NET. |
| T3 | **Azure Trusted Signing** for code signing (~$10/month). | Avoids SmartScreen "unknown publisher" for a tool that reads game memory. |
| T4 | **Sentry** for Link crash reports (opt-out in settings). | Already in turn-one-mobile's stack. |
| T5 | One version source: `<Version>` in the csproj, read at runtime. | Today three places disagree (`1.1.0`, `v0.0.1`, installer `1.1.0`). |
| T6 | Keep .NET 9 + WPF. | Shared memory, IRSDKSharper and UDP all work; a rewrite buys nothing. |
