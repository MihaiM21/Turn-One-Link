# HCR-007 — Lap / session upload API

| | |
|---|---|
| **Status** | on dev (2026-11-07) |
| **Requested by** | turn-one-link — LF-11, LF-12, HF-02 (decision D3) |
| **Needed by** | Phase 1 (closed beta) |
| **Breaking?** | **No — additive only.** Live WSS ingestion keeps working unchanged. |

## Why the app needs this
Today the only data path is live WSS; a dropped connection loses the lap and `session_end` is
routinely lost. D3 makes Link local-first: record, then upload each closed lap over HTTP with
retry, idempotently.

## What exists today (verified 2026-11-04)
- `API/Middleware/TelemetryWebSocketMiddleware.cs` + `API/Services/TelemetryIngestionService.cs:93-235` — v2 message handling.
- `Application/Telemetry/LapCutRule.cs` cuts laps from ticks; `LapProcessingWorker` → `LapProcessor` (2 m grid, Brotli, corners).
- Session row `TelemetrySession` keyed by client-chosen 32-hex `Id`.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `POST /api/simracing/uploads/laps` | `scope=link` or user | NDJSON (Brotli) of v2 frames: `session_start`, the lap's `tick`s, `lap_complete`, optional `setup_ref`; header `Idempotency-Key: <sessionId>:<lapNumber>:<sha256>` | `202 { lapId, status: "queued" }`; repeat key → `200` same body | 400 malformed, 409 session owned by another user, 413 > 8 MB, 422 no `lap_complete`, 429 |
| `POST /api/simracing/uploads/sessions/{sessionId}/end` | same | `{ end: <session_end frame>, laps: [{ lapNumber, sha256 }] }` | `{ missing: [lapNumber…] }` | 404, 409 |
| `GET /api/simracing/uploads/sessions/{sessionId}` | same | — | `{ sessionId, laps: [{ lapNumber, status: queued/processed/invalid, reasons[] }] }` | 404, 403 |

### Data model / migration
`UploadBatch` ([07](../07-data-model-and-contracts.md) §1); `TelemetrySessions.DeviceId`, `Platform`, `GameVersion` nullable columns.

### Behaviour
1. Parse frames with the same validators as WSS; create the session on first sight, bound to the caller (`UserId`); foreign owner → 409.
2. Feed ticks into the existing lap cut/process pipeline as if they arrived live, with `Mode=Upload`; no SignalR fan-out, no Influx write.
3. If the same lap already exists from WSS, keep the version with more ticks.
4. `end` closes the session (`EndedAt`, `IsActive=false`) and returns missing laps.
5. Processing stays async; `GET` shows progress.

### Configuration
`Uploads:MaxLapBytes` (8 MB), `Uploads:LapsPerHour` (120).

## Compatibility — why the website is unaffected
- WSS ingestion: untouched. Session/lap read endpoints return uploaded data like live data.
- Plan limits: uploads respect D5 retention (H-13), never delete.

## Security
Ownership check on every request; per-user rate limit; body size limit before decompression
(zip-bomb guard: abort at 64 MB decompressed). No raw telemetry in logs.

## Acceptance criteria
- [ ] Uploading a recorded ACC lap produces the same `TelemetryLap` + `LapTelemetry` + `LapCorners` as streaming it (golden test).
- [ ] Same Idempotency-Key twice → one lap, second call 200.
- [ ] Another user's sessionId → 409; nothing written.
- [ ] `end` returns missing laps correctly.
- [ ] 9 MB body → 413; decompression bomb → 400.

## Rollout / rollback
Deploy; Link enables upload in a beta build while still streaming live (both paths merge). Rollback =
Link reverts to WSS-only by config.

## Implementation notes
- Merged to `dev` via MihaiM21/Turn-One#143/#145: `UploadsController`, `LapUploadService`, `LapUploadParser`, `LapUploads` table. Uploaded laps go through the live `LapProcessor`; a lap already processed from the live stream is kept (`duplicate`). Builds below `TurnOneLink:MinUploadVersion` get `426` (follow-up PR).
- Link: `LapRecorder` + `UploadQueue` (MihaiM21/Turn-One-Link#6/#7).
