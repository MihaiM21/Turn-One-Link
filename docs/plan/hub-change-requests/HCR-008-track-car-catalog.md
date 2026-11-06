# HCR-008 — Canonical track & car catalog with per-sim aliases

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — HF-03, HF-11 (decision D6) |
| **Needed by** | Phase 1 (closed beta) |
| **Breaking?** | **No — additive only.** |

## Why the app needs this
Leaderboards, setups, track pages and challenges need one id for "Spa, GP layout" and "Ferrari
296 GT3" regardless of how each sim names them (`spa`, F1 id `10`, iRacing `trackId` + config).

## What exists today (verified 2026-11-04)
- `TrackProfile` keyed by `(Source, TrackId)`, with `DisplayName`, `LengthM`, `SectorBoundariesM`, `ReferenceCorners`, `Status` (Provisional/Stable/Curated), `Centerline`.
- `TelemetrySession.CarModel`/`CarId` are free strings.
- Link's `Services/Telemetry/AccTrackLengths.cs` (25 ACC tracks) can seed data.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/simracing/catalog/tracks` | none | `?source=` | `[{ slug, name, country, layouts: [{ slug, name, lengthM, sources: [..] }] }]` | — |
| `GET /api/simracing/catalog/cars` | none | `?source=&class=` | `[{ slug, name, class, sources: [..] }]` | — |
| `POST /api/admin/simracing/catalog/aliases` | admin | `{ kind: track/car, source, simId, simLayout?, targetSlug }` | `201` | 409 |
| `GET /api/admin/simracing/catalog/unmapped` | admin | — | provisional ids seen with counts | — |

### Data model / migration
`Track`, `TrackLayout` (FK to existing `TrackProfile`), `TrackAlias`, `Car`, `CarClass`, `CarAlias`;
nullable `TelemetrySessions.TrackLayoutId`, `CarRefId`. Seed migration for ACC, F1 25/26 and the
most common iRacing tracks/cars.

### Behaviour
1. On session start (WSS or upload) resolve aliases; unknown → create provisional entry + alias, never fail.
2. Admin merges provisional entries into canonical ones; leaderboards re-key on merge.

## Compatibility — why the website is unaffected
New tables/columns only; existing pages keep using `Track`/`CarModel` strings until migrated.

## Security
Admin endpoints `ADMIN` role, server-checked.

## Acceptance criteria
- [ ] ACC `spa`, F1 track `10` and iRacing Spa GP resolve to the same layout.
- [ ] Unknown id creates one provisional entry (idempotent under concurrency).
- [ ] Merge moves sessions and leaderboard entries.

## Rollout / rollback
Seed + backfill job for existing sessions. Rollback = ignore new columns.

## Implementation notes
_(Hub-side session fills this in.)_
