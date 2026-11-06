# HCR-010 — Setups linked to laps + install requests

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — LF-20, LF-21, HF-10 (decision D7) |
| **Needed by** | Phase 2 (public launch) |
| **Breaking?** | **No — additive only.** |

## Why the app needs this
Setups are one of the top reasons sim racers install companion apps. D7: a setup is shown with the
lap time it actually produced, and can be installed in one click through Link.

## What exists today (verified 2026-11-04)
No setup entity or route. Media upload infrastructure exists (`Media` table) — reuse its storage, not its table.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `POST /api/setups/files` | `scope=link` or user | multipart: file + `{ source, simTrackId, simCarId }` | `{ setupFileId, hash, parsed }` (dedup by hash per user) | 400 unknown format, 413 > 256 KB |
| `POST /api/setups` | user / link | `{ setupFileId, name, conditions, visibility, proofLapId? }` | `{ setupId }` | 403 proof lap not owned |
| `GET /api/setups` | none | `?source&layout&car&conditions&sort=fastest|newest|downloads&page` | `[{ id, name, author, lapTimeMs?, proofLapId?, conditions, gameVersion, downloads, comments }]` | — |
| `GET /api/setups/{id}` | none (public) | — | detail + `parsedValues` | 404 |
| `GET /api/setups/{id}/download` | user | — | file | 404 |
| `GET /api/setups/{a}/diff/{b}` | none | — | `[{ key, a, b }]` | 404 |
| `POST /api/setups/{id}/install` | user | `{ deviceId? }` | `{ requestId }` | 404 |
| `GET /api/setups/pending-installs` | `scope=link` | — | `[{ requestId, setupId, format, fileUrl, targetPathHint }]` | — |
| `POST /api/setups/pending-installs/{requestId}/ack` | `scope=link` | `{ ok, error? }` | `204` | 404 |

Upload frame (HCR-007): `{"type":"setup_ref","data":{"hash":"…"}}` links the lap to an uploaded file.

### Data model / migration
`Setup`, `SetupFile`, `LapSetupLink`, `SetupInstallRequest`; nullable `TelemetryLaps.SetupId`.

### Behaviour
1. Parsers: ACC JSON → `parsedValues`; iRacing `.sto` stored opaque (values parsed later if feasible); F1 → values from the UDP Car Setups packet (no file).
2. A setup's displayed time = best eligible lap (HCR-009) linked to it.
3. Install requests expire after 24 h.

## Compatibility — why the website is unaffected
New routes and pages only.

## Security
File type/size checks; files served with `Content-Disposition: attachment`; never executed or rendered as HTML. Private setups visible only to owner (later team, HCR-016).

## Acceptance criteria
- [ ] ACC setup round-trip: upload → list → download identical bytes.
- [ ] Lap with `setup_ref` shows on setup detail with its time.
- [ ] Install request picked up by `pending-installs` and acked once.
- [ ] Diff of two ACC setups lists changed keys only.

## Rollout / rollback
Feature flag on the setups UI. Rollback = hide UI; data retained.

## Implementation notes
_(Hub-side session fills this in.)_
