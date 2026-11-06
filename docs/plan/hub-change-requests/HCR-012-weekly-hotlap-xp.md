# HCR-012 — Weekly Hotlap Challenge + server-side sim XP

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — HF-13, HF-14, LF-23 (decision D13) |
| **Needed by** | Phase 2 (public launch) |
| **Breaking?** | **No — additive only.** (Locking the client-callable `add-experience` is H-06 in 05, not here.) |

## Why the app needs this
A reason to drive every week, tied to the real F1 calendar and the channel. XP must come from
verified server events, not the client.

## What exists today (verified 2026-11-04)
- Level system: `Users.Level`, `Experience`; `LevelSystemController` (`POST add-experience`, line 53).
- Coins: `CoinTransactions`, `Users.Coins`.
- F1 calendar via T1API (used by `/f1` pages).

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/simracing/challenges/current` | none | — | `{ id, source, layout, car, startsAt, endsAt, f1Event?, prizes, standings: [top 50], me? }` | 404 none active |
| `GET /api/simracing/challenges` | none | `?status=past&page` | list with winners | — |
| `POST /api/admin/simracing/challenges` | admin | `{ source, layoutSlug, carSlug, startsAt, endsAt, f1EventRef?, prizes }` | `201` | 400 overlap |
| `GET /api/simracing/me/xp` | user | — | `{ today, cap, events: [...] }` | — |

### Data model / migration
`Challenge`, `ChallengeEntry`, `XpEvent` (unique `(UserId, Kind, RefId)`).

### Behaviour
1. On `LapValidation` eligible: if a challenge is active and the lap matches its combo and was **received** in the window → upsert entry.
2. At `endsAt` a job ranks entries, writes `CoinTransactions` for prizes, emits `ChallengeClosed`.
3. XP events: valid lap (+5, cap 200/day), PB on a board (+50), first lap on a new layout (+25), challenge placement (top 10 / 3 / 1). Idempotent by `RefId`.
4. Admin UI suggests next week's combo from the F1 calendar (track the game has) — admin confirms.

## Compatibility — why the website is unaffected
Uses existing coin/XP tables through their services; new routes only.

## Security
All awards server-side; challenge window uses server receive time; admin creation audited.

## Acceptance criteria
- [ ] Lap outside the window or wrong car doesn't enter.
- [ ] Close job pays prizes exactly once (re-run safe).
- [ ] XP daily cap enforced; replaying the same lap upload gives no extra XP.

## Rollout / rollback
First challenge manually created. Rollback = no active challenge.

## Implementation notes
_(Hub-side session fills this in.)_
