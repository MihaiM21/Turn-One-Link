# 00 — Vision and scope

## In one paragraph

**Turn One Link makes you faster.** It is a small Windows app that sits in the tray, notices when
you start ACC, iRacing or EA F1 25/26 (PC or console), records every lap locally and uploads it to
your Turn One account. On **turnonehub.com** you see where you lose time corner by corner — against
your own best, against the leaderboard top, against the lap that came with a setup. Leaderboards,
setups, track guides and a weekly hotlap challenge tied to the real F1 calendar give a reason to
come back. Link stays a thin, reliable bridge; all analysis lives on the website (D4).

## Primary user (D1)

The **sim racer who wants to get faster**: drives 3–10 hours a week, knows what a braking point is,
has never opened MoTeC or gave up on it. Plays ACC, iRacing or F1 25/26; many F1 players are on
PS5/Xbox (D15). Probably already watches F1 content — which is where Turn One's YouTube audience
comes in.

Secondary users, served by features built for the primary one:

| User | What they get | When |
|---|---|---|
| Leaderboard hunter | Verified per-track/car leaderboards, Weekly Hotlap | Beta / launch |
| Setup tinkerer | Setups with a proven lap time, one-click install | Launch |
| Streamer | Existing OBS overlays + spectate | Already built, hardened in Phase 0 |
| League / team | Teams with shared laps, coach role | Phase 3 (D16) |

## Jobs to be done

1. *"Show me where I'm losing time."* → lap compare, corner analysis, rule-based coach (D8).
2. *"How do I compare to people like me?"* → leaderboards with fair splits (D6).
3. *"Give me a setup that is actually fast."* → setups tied to the lap they produced (D7).
4. *"Don't make me think about it."* → auto-detect, auto-record, auto-upload, never lose a lap (D3).
5. *"Give me a reason to drive tonight."* → Weekly Hotlap on this weekend's GP track, XP, Discord (D13).

## Competitive map

| Product | Strength | Where we differ |
|---|---|---|
| Garage 61 | Free, unlimited lap storage, iRacing-first, team sharing | Multi-sim incl. F1 console; corner-level coach; F1 brand + YouTube |
| Track Titan | AI coaching, polished | Generous free tier, setups tied to proof laps |
| VRS | Pro reference laps + setups, iRacing only | Community setups, multi-sim, free core |
| Coach Dave Delta | Paid setups + delta app | Free setups, analysis included |
| SimHub / RaceLab | In-game overlays and dashes | We don't compete on HUD (D14); we may export to SimHub later |

**Our wedge:** analysis-first on the web + **EA F1 including console** (underserved by all of the
above) + the Turn One F1 audience + a weekly challenge that follows the real F1 calendar.

## v1 scope (public launch, March 2027)

**In:**
- Link: ACC, F1 25/26 (PC + console via UDP), iRacing — all three working correctly (D2).
- Device login that stays signed in; local recording; reliable upload; session list with status.
- LAN phone dashboard via QR (D10).
- Auto-update and a signed installer.
- Hub: per-track/car leaderboards with verification; setups library; track pages with comments;
  Weekly Hotlap; Discord linking + record bot; rule-based coach v2; public driver profile.
- Freemium as in D5; data never auto-deleted.

**Out of v1 (explicitly):**
- In-game overlays (D14) · generic forum (D9) · paid setup marketplace (D7) · LLM / own AI model (D8)
  · Teams (D16) · AC original, LMU, rF2 (D2) · a separate desktop analysis app (D4) · macOS/Linux.

## What success looks like

| Metric | Beta (Jan 2027) | Launch + 3 months (Jun 2027) |
|---|---|---|
| Active drivers (uploaded a lap in last 30 days) | 30 | 1,000 |
| Laps uploaded / week | 5,000 | 150,000 |
| Upload success rate (sessions fully received / sessions recorded) | ≥ 98 % | ≥ 99.5 % |
| Crash-free Link sessions | ≥ 98 % | ≥ 99.5 % |
| Free → paid conversion | — | 3–5 % |
| Weekly Hotlap entrants | — | 200 / week |

These are targets to argue with, not commitments; revisit at the end of the beta.
