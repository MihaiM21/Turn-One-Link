# 10 — Roadmap

Dates follow D11. Each phase has **exit criteria**; a phase doesn't end because the calendar says so.
IDs: `L-` Link bugs ([03](03-link-bugs.md)), `LF-` Link features ([04](04-link-features.md)),
`H-` Hub fixes ([05](05-hub-security-and-fixes.md)), `HF-` Hub features ([06](06-hub-features.md)),
`HCR-` ([hub-change-requests](hub-change-requests/README.md)).

## Progress — 2026-11-07

Code for Phase 0 and Phase 1 is merged (Hub `dev` PRs #139–#147, Link `master` PRs #3–#10, plus LF-18).
Nothing new is on prod yet. Still open before the beta:

- **Deploy** Hub `dev` → prod; then run `POST /api/simracing/admin/leaderboards/backfill` once and set
  `TurnOneLink:DownloadUrl`, `TurnOneLink:MinUploadVersion` and both Turnstile keys (or neither).
- **Secrets the CI needs:** code signing (LF-02) and the Sentry DSN (LF-03).
- **H-01 history purge** (not run) · H-14 · H-15 · H-18 · L-P1-03 rest · L-P1-07…13.
- **Tests only a person can do:** clean Windows 11 VM install + N-1 → N update; 2 h each in ACC, F1 25 and
  iRacing; F1 on a console; the phone dashboard on a real phone; the leaderboard query on Postgres.

## Phase 0 — Make it safe and shippable · 4 Nov – 20 Dec 2026

**Hub (security first — nothing else starts on the Hub before H-01…H-05):**
- H-01 key rotation + history purge · H-02 scope enforcement · H-03 session ownership ·
  H-04 / HCR-005 entity leaks · H-05 admin seed · H-06 XP lock · H-07 rate limiting ·
  H-08 T1API proxy allowlist · H-09 spectate check · H-10 admin gate.
- H-11 403s · H-13 stop auto-deleting sessions · H-16 tests gate CI.

**Link:**
- L-P0-01…13 (installer, iRacing detection, ACC lap timing, session lifecycle, queue, LAN server).
- LF-01 Velopack · LF-02 signing · LF-03 Sentry · LF-04 compiled config.
- CI for Link ([11](11-quality-gates-and-release.md) §1); test project in the repo; replay tests from the ACC logs.

**HCRs written and approved** (implementation can start late in Phase 0): HCR-006, 007, 008, 009, 014.

**Exit criteria**
- [ ] A security re-check of H-01…H-10 passes (tests for H-02, H-03, H-04, H-09 in CI).
- [ ] Signed installer installs and runs on a clean Windows 11 VM; auto-update from N-1 → N works.
- [ ] 2 h of driving each in ACC, F1 25 and iRacing: every lap arrives once, with correct time and sectors, and one `session_start`/`session_end` per session.

## Phase 1 — Closed beta · 6 Jan – 28 Feb 2027

**Hub:** HCR-003 (shared with mobile), HCR-006 pairing, HCR-007 upload, HCR-008 catalog,
HCR-009 leaderboards + validation, HCR-014 release feed, HCR-015 consent (consent screen only).
HF-01…HF-07. H-12 plan from DB. H-17 Turnstile.

**Link:** LF-10 pairing · LF-11 recorder · LF-12 upload queue · LF-13 session list ·
LF-14 live toggle · LF-15 F1 console · LF-16 health check · LF-17 LAN dash · LF-18 report a problem.
L-P1-01…05 (lifecycle, F1 fixes).

**Beta:** 20–50 drivers invited from YouTube/Discord, mix: ≥ 10 ACC, ≥ 10 F1 (≥ 5 console), ≥ 5 iRacing.

**Exit criteria**
- [ ] Upload success ≥ 98 % of recorded sessions over the last 2 weeks; zero lost laps in bug reports.
- [ ] Crash-free sessions ≥ 98 %.
- [ ] Leaderboards for the 10 most-driven combos look right to the beta drivers (no obviously invalid laps in top 10).
- [ ] Median time from install to first uploaded lap < 10 min (console F1 < 15 min).

## Phase 2 — Public launch · March 2027 (before the 2027 F1 season opener)

**Hub:** HCR-010 setups · HCR-011 comments/reports · HCR-012 Weekly Hotlap + XP · HCR-013 Discord ·
HCR-015 coach feedback. HF-10…HF-19. Landing page fixes, sitemap, SEO pages.

**Link:** LF-20 setup capture · LF-21 install from web · LF-22 start with Windows · LF-23 notifications ·
LF-24 onboarding · LF-25 localisation. L-P1-06…13 and the P2 items in touched areas.

**Launch:** YouTube video + Short, Discord announcement, first Weekly Hotlap on the season-opener track.

**Exit criteria**
- [ ] First 4 Weekly Hotlaps run with automatic results and payouts.
- [ ] Coach tips 👍 rate measured (target ≥ 60 % at launch, ≥ 70 % by June).
- [ ] Paid plan purchasable end-to-end with D5 gating.

## Phase 3 — Grow · Apr – Sep 2027

- **Teams** (D16): HCR-016, HF-30, LF-35.
- **More sims:** LF-30 LMU + rF2, LF-31 AC original.
- **Mobile Sim tab** in turn-one-mobile ([09](09-mobile-and-ecosystem.md)).
- LF-32 delta widget / LF-33 SimHub export / LF-34 reference-lap download (D14).
- HF-31 series events, HF-32 driver rating, HF-33 3D ghost replay.
- Evaluation set for the AI curated ([08](08-ai-roadmap.md)).

## Phase 4 — Own AI model · from Q4 2027, gated

Only after the go/no-go gate in [08-ai-roadmap.md](08-ai-roadmap.md) Stage C.

## Dependency graph (critical path)

```
H-01..H-05 ──► beta invites
HCR-003 ──► HCR-006 ──► LF-10 ─┐
HCR-008 ──► HCR-009 ──► HF-03  ├─► Phase 1 exit
HCR-007 ──► LF-12 ─────────────┘
HCR-009 ──► HCR-010 (proof laps), HCR-012 (challenges), HCR-013 (record posts)
HCR-014 ──► LF-01 channels, minimum-version rule in lap validation
```
