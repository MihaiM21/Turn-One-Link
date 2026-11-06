# 09 — Mobile and the wider Turn One ecosystem

## LAN phone dashboard (Phase 1, D10)

Served by Link itself — no app, no account on the phone.

- `LocalWebSocketServer` gets a tiny HTTP handler: `GET /` → static dashboard (HTML/JS bundled as
  embedded resources), `GET /ws` → the existing T1 frame stream (`docs/telemetry/t1_protocol.md`).
- Link shows a **QR** with `http://<lan-ip>:<port>/#pin=<pin>`; the page reads the PIN from the
  fragment (never sent in a request line or logs) and authenticates over the WS.
- Layouts: *Delta* (big delta bar + lap/sector), *Engineer* (fuel laps left, tyre temps/pressures,
  last 5 laps), *Minimal* (gear, speed, shift lights). Landscape-first; keep-awake via the Wake Lock API.
- Hardening is L-P0-11; throttling is L-P2-03.
- Styled from turn-one-media tokens (`tokens/turnone.tokens.json` → generated CSS; never hardcode
  colours — see the workspace rule).

## turn-one-mobile "Sim" tab (Phase 3)

turn-one-mobile is F1-only today and its v1 makes **no Hub changes except HCR-001** (its D26).
The Sim tab is therefore planned **after mobile v1** and goes through that repo's own process:
the mobile side writes its work packages; Link and the Hub provide what it needs.

| Mobile needs | Provided by |
|---|---|
| Staying logged in | HCR-003 (already mobile's) |
| My sessions, laps, PBs, leaderboard ranks | Existing endpoints + HCR-009 |
| Push: "New PB", "You lost #1", "Weekly Hotlap results" | HCR-001's push fan-out, new topics (small follow-up HCR then) |
| Live dash over LAN (optional native version of the web dash) | T1 protocol, unchanged |
| Pairing a phone to Link without a PIN | Later: Link registers as a `Device`; the phone sees "your PC is online" via the Hub |

Write-up for the mobile side when the time comes: a short brief in turn-one-mobile's
`docs/work-packages/` style, referencing this file.

## turn-one-media (YouTube / Shorts)

- **Lap-comparison clips:** a Remotion template in `turn-one-media/apps/remotion` that takes two
  laps (JSON from `GET laps/overlay`) and renders the delta trace + track map — for "Weekly Hotlap
  winner" Shorts and video segments. Follows the WORLD/SCREEN/CHROME rules and three-part clip
  structure of the media pipeline.
- **Weekly Hotlap promo** each week tied to the GP video schedule (D13).
- Link and website visuals use the same tokens; no new colours.

## T1API

- Weekly Hotlap picks its track from the real F1 calendar (T1API `/v2/...` schedule endpoints,
  already used by the site).
- Track pages can show the real F1 pole time / lap record next to sim leaderboards (fun, sticky).
- Pit Wall (Stage C AI) is built over T1API.

## Discord (D9)

- Bot (HCR-013): new #1 per board, Weekly Hotlap start/standings/results, weekly digest of most
  improved drivers; role sync for plan tiers; `/pb <track>` command reading the Hub API.
- Discord is also the beta's feedback channel ([11](11-quality-gates-and-release.md) §4).
