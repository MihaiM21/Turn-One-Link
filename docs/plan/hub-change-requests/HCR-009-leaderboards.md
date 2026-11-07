# HCR-009 — Lap validation + per-track/car leaderboards

| | |
|---|---|
| **Status** | on dev (2026-11-07) |
| **Requested by** | turn-one-link — HF-03, HF-04, HF-05, HF-07 (decisions D6, D12) |
| **Needed by** | Phase 1 (closed beta) |
| **Breaking?** | **No — additive only.** The existing lifetime-totals `GET /api/telemetry/leaderboards` stays. |

## Why the app needs this
The only sim "leaderboard" today is lifetime totals per user (distance, laps, top speed). Drivers
want verified lap-time rankings per sim × track layout × car, with fair splits.

## What exists today (verified 2026-11-04)
- `TelemetryLap`: `LapTimeMs`, `Sector1-3Ms`, `IsValid`, stats, `ProcessingStatus`.
- `GET /api/telemetry/leaderboards` (anonymous, totals; exposes user ids — fixed by H-14).
- `GET tracks/{profileId}/laps/me?includePublic=true` is the closest per-track list.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/simracing/leaderboards` | none | `?source&layout&car|class&conditions&platform&gameVersion&period=all|month|week&page&size` | `{ total, entries: [{ rank, username, avatarUrl, lapId, lapTimeMs, sectorsMs[3], gapMs, setDate, hasSetup, verified }] , me?: {rank, ...} }` | 400 |
| `GET /api/simracing/laps/{lapId}/validation` | none if public lap, else owner | — | `{ eligible, simValid, serverValid, reasons[] }` | 404 |
| `GET /api/simracing/drivers/{username}/pbs` | none | — | `[{ source, layout, car, lapId, lapTimeMs, rank, total }]` | 404 |
| `PUT /api/simracing/me/leaderboard-visibility` | user | `{ mode: public|anonymous|hidden }` | `204` | 400 |
| `POST /api/admin/simracing/laps/{lapId}/exclude` | admin | `{ reason }` | `204` | 404 |

### Data model / migration
`LapValidation`, `LapConditions`, `LeaderboardEntry` (materialised, one row per user per board key —
their best eligible lap), `SimUser.LeaderboardVisibility` (default `public`, D12).

### Behaviour
1. After `LapProcessor` finishes, run validators ([07](../07-data-model-and-contracts.md) §3) → `LapValidation`.
2. If eligible and better than the user's entry for that board key → upsert entry, recompute ranks for the board (batched).
3. Board key = (source, layoutId, carId, conditionKey); class boards aggregate car boards.
4. `anonymous` shows "Anonymous driver #<stable hash>"; `hidden` removes the user from public boards (still sees own rank).
5. Raise a domain event `LeaderboardRecord { boardKey, userId, lapId, rank, previousHolder }` for HCR-012/013.

## Compatibility — why the website is unaffected
New routes only; existing leaderboard page keeps working until the new UI replaces it.

## Security
Public responses never include user ids or emails. Rate limit 60/min per IP. Excluded laps logged with admin id.

## Acceptance criteria
- [ ] Validators: each rule has a positive and negative test using recorded laps.
- [ ] New PB moves the user up; slower lap doesn't change the entry.
- [ ] Hidden user absent from public board, present in `me`.
- [ ] Ranks correct under concurrent inserts (property test).
- [ ] Board query p95 < 150 ms with 100 k entries.

## Rollout / rollback
Backfill validations for existing laps; ship UI behind a flag. Rollback = hide UI.

## Implementation notes
- Merged to `dev` via MihaiM21/Turn-One#144/#145: `LapValidator` v1, `LapValidation` per lap (board key + eligibility), `SimLeaderboardService`, `SimLeaderboardsController`. Deviation: no materialised `LeaderboardEntry` — boards are best-per-driver queries over `LapValidation`, which also gives week/month boards. Tick-level checks and condition splits are for a later validator version.
- After deploy: `POST /api/simracing/admin/leaderboards/backfill` once.
