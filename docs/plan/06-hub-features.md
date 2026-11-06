# 06 — Hub (website) features

What turnonehub.com gains so that Link is worth installing. Existing sim-racing routes live under
`turn-one-client/app/(dashboard)/simracing/` (index, analysis, coach, download, leaderboards,
live, sessions, `sessions/[id]`, `sessions/[id]/compare`, spectate, streamer) and
`app/overlay/[token]/…`. Styling follows turn-one-media tokens (`/t1-style`).

Plan gating follows D5: **free** = own data, leaderboards, setups, own-lap analysis;
**paid (PRO/ELITE)** = compare vs others, corner coach, full-res history, spectate/overlays, AI later.

## Phase 1 — beta

| ID | Feature | What the user sees | Backend |
|---|---|---|---|
| HF-01 | **/link pairing page** | "Approve Turn One Link on *DESKTOP-ABC*?" with the code; Settings → Devices lists and revokes devices. | HCR-006 |
| HF-02 | **Sessions & laps, upload-aware** | Existing session pages show upload state, lap validity reasons, "processing…". | HCR-007 |
| HF-03 | **Leaderboards v1** | `/simracing/leaderboards/[sim]/[track]/[car]` — filters: layout, car/class, conditions (wet/dry, F1 assists), game version, platform, period (all-time / month / week), friends. Row: rank, driver, time, gap, sectors, date, setup badge, "verified" tick. | HCR-008, HCR-009 |
| HF-04 | **Lap page** | `/simracing/laps/[id]`: time, sectors, validity, setup, trace overlay (free: speed/throttle/brake vs your PB; paid: vs any lap, all channels, corners). Shareable when public. | existing lap endpoints + HCR-009 |
| HF-05 | **Driver profile** | `/drivers/[username]`: PBs per sim × track × car with ranks, sims played, hours, hardware (optional free text: wheel/pedals), badges. | SimUser + HCR-009 |
| HF-06 | **Download page** | Real download, version, changelog, system requirements, F1 console guide. | HCR-014 |
| HF-07 | **Privacy controls** | Settings → Sim racing: leaderboard visibility (public / anonymous / hidden), default session visibility, AI dataset consent (D12). | HCR-009, HCR-015 |

## Phase 2 — public launch

| ID | Feature | What the user sees | Backend |
|---|---|---|---|
| HF-10 | **Setups library** | `/simracing/setups?sim&track&car&conditions` — sorted by best proven lap; card: author, lap time + link, conditions, game version, downloads, comments. Detail page: values table, **diff vs another setup**, "Download" and "Install in Link". Upload `.json` / `.sto` manually too (no proof lap → "unverified"). | HCR-010 |
| HF-11 | **Track pages** | `/simracing/tracks/[slug]`: map from `TrackProfile.Centerline`, corners with names, layouts, leaderboards per sim, top setups, **track guide** (admin-curated text + community comments), linked Turn One YouTube videos. Good SEO surface. | HCR-008, HCR-011 |
| HF-12 | **Comments + reports** | On setups, track guides, public laps. Markdown-lite, edit 15 min, report → admin queue. Report on leaderboard rows too. | HCR-011 |
| HF-13 | **Weekly Hotlap Challenge** | `/simracing/hotlap`: this week's sim + track + car (the real GP's track when the game has it), countdown, live standings, last week's winners, coin prizes, Discord post. Entries are normal uploaded laps that match the combo during the window. | HCR-012 |
| HF-14 | **XP for driving** | XP for valid laps (capped per day), PBs, challenge placement, first lap on a new track. Shown in the existing level system. | HCR-012 |
| HF-15 | **Discord** | "Connect Discord" in settings; bot posts new #1s, challenge results, a weekly digest; role sync for PRO/ELITE. | HCR-013 |
| HF-16 | **Coach v2 (rule-based)** | On a lap: per-corner cards — "Brake 12 m later into T1 (your PB brakes at 98 m, top-10 average 86 m)", "Min speed 6 km/h lower in T5", "You lift in T9 where the top 10 stay flat". 👍/👎 on each tip. Free: vs own PB; paid: vs leaderboard. Replaces the stub chat. | `CoachingController` + HCR-015 |
| HF-17 | **Compare vs leaderboard** (paid) | One click on any leaderboard row: overlay their lap on yours, delta trace, corner table. | existing `laps/overlay`, `laps/corners/compare` |
| HF-18 | **Upsell surfaces** | 403 from H-11 carries `upgradeRequired`; locked panels show what you'd see, blurred, with plan CTA. | H-11 |
| HF-19 | **Onboarding funnel** | Landing → "Get faster in ACC / F1 / iRacing" → download → pair → first lap → "here's your first insight" email. | HF-06, HF-16 |

## Phase 3 — after launch

| ID | Feature | Notes |
|---|---|---|
| HF-30 | **Teams** (D16) | Create team, invite, shared laps/setups, coach role (read-only on members' sessions), team leaderboards, billing per team. HCR-016. |
| HF-31 | **Events beyond Weekly Hotlap** | Monthly "series" (4 weeks, points), private league challenges for teams. |
| HF-32 | **Driver rating** | Elo-like rating per sim from leaderboard percentiles; shown on profile and rows. |
| HF-33 | **Ghost replay (3D)** | three.js/react-three-fiber (already in FE) replay of two laps on the centerline. |
| HF-34 | **Spectate polish** | Public live page with multiple drivers per event. |

## SEO note

Leaderboard, track and setup pages are the indexable long tail ("ACC Spa GT3 setup", "F1 25 Monza
leaderboard"). Server-render them with cached data (the `/f1/...` pages' revalidate pattern in
`docs/f1-sessions-pipeline.md` is the model), add to the sitemap, OG images via the existing
`/f1/og` approach.
