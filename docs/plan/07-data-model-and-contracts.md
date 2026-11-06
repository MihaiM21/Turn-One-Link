# 07 — Data model and contracts

Builds on the existing Hub schema (`turn-one-backend/Infrastructure/TurnOneDbContext.cs`):
`TelemetrySessions`, `TelemetryLaps`, `LapTelemetries` (2 m grid, Brotli), `LapCorners`,
`TrackProfiles`, `SimUsers`, `OverlayShareTokens`. New entities are additive; each is specified
in full in its HCR — this file is the overview and the cross-cutting rules.

## 1. New entities

| Entity | Key fields | HCR |
|---|---|---|
| `Device` | Id, UserId, Name ("DESKTOP-ABC"), Platform, AppVersion, CreatedAt, LastSeenAt, RevokedAt | 006 |
| `DevicePairing` | DeviceCodeHash, UserCode, ExpiresAt (10 min), ApprovedByUserId?, DeviceId? | 006 |
| `DeviceRefreshToken` | DeviceId, TokenHash, FamilyId, ExpiresAt (180 d), RevokedAt, ReplacedById (rotation as HCR-003) | 006 |
| `UploadBatch` | Id, UserId, DeviceId, SessionId, Kind (lap / session_file), ContentHash, SizeBytes, Status, Error, ReceivedAt | 007 |
| `Track` / `TrackLayout` | Slug, Name, Country, LengthM, CornerNames (jsonb); layout → existing `TrackProfile` | 008 |
| `TrackAlias` | Source, SimTrackId, SimLayout?, TrackLayoutId | 008 |
| `Car` / `CarClass` / `CarAlias` | Slug, Name, ClassId; alias = (Source, SimCarId) → CarId | 008 |
| `LapValidation` | LapId, SimValid, ServerValid, Reasons[] (e.g. `sector_sum_mismatch`, `distance_short`, `speed_spike`, `position_jump`, `cut_flag`), CheckedAt, ValidatorVersion | 009 |
| `LeaderboardEntry` (materialised) | Source, TrackLayoutId, CarId, CarClassId, ConditionKey, GameVersion, Platform, UserId, LapId, LapTimeMs, Rank, UpdatedAt | 009 |
| `LapConditions` | LapId, Weather (dry/damp/wet), TrackTempC, AirTempC, Assists (jsonb, F1), Platform, GameVersion | 009 |
| `Setup` / `SetupFile` | Setup: Id, UserId, Source, TrackLayoutId, CarId, Name, Conditions, Visibility, ProofLapId?, Downloads; File: SetupId, Format (acc_json / iracing_sto / f1_values), ContentHash, Blob, ParsedValues (jsonb) | 010 |
| `LapSetupLink` | LapId, SetupId | 010 |
| `SetupInstallRequest` | Id, UserId, SetupId, DeviceId?, Status, CreatedAt | 010 |
| `Comment` / `Report` | Comment: Id, TargetType (setup/track/lap), TargetId, UserId, Body, EditedAt, DeletedAt; Report: TargetType, TargetId, ReporterId, Reason, Status | 011 |
| `Challenge` / `ChallengeEntry` | Challenge: Id, Source, TrackLayoutId, CarId, StartsAt, EndsAt, F1EventRef?, Prizes (jsonb); Entry: ChallengeId, UserId, LapId, LapTimeMs, Rank | 012 |
| `XpEvent` | UserId, Kind, RefId, Amount, CreatedAt — unique (UserId, Kind, RefId) | 012 |
| `DiscordLink` | UserId, DiscordUserId, LinkedAt | 013 |
| `AiConsent` | UserId, Version, GrantedAt, WithdrawnAt | 015 |
| `CoachTip` / `CoachFeedback` | Tip: LapId, CornerIndex, RuleId, Text, Params (jsonb), RulesetVersion; Feedback: TipId, UserId, Vote (+1/-1), Comment? | 015 |
| `Team` / `TeamMember` | Phase 3 | 016 |

Additions to existing tables (nullable, default null): `TelemetryLaps.ValidationId`,
`TelemetryLaps.SetupId`, `TelemetrySessions.DeviceId`, `TelemetrySessions.Platform`,
`TelemetrySessions.GameVersion`, `TelemetrySessions.TrackLayoutId`, `TelemetrySessions.CarRefId`.

## 2. Upload contract (v2.1) — summary of HCR-007

Link uploads the **same v2 frames** it already produces (`docs/telemetry/backend_contract_v2.md`),
packaged, so the existing `LapCutRule`/`LapProcessor` pipeline is reused unchanged.

**Lap upload** — sent as soon as a lap closes:
```
POST /api/simracing/uploads/laps
Authorization: Bearer <scope=link access token>
Content-Type: application/x-ndjson ; Content-Encoding: br
Idempotency-Key: <sessionId>:<lapNumber>:<sha256 of body>

{"v":2,"type":"session_start",...}        ← first line always repeats session_start
{"v":2,"type":"tick",...}  × N            ← ticks of this lap only (+ 2 s before/after)
{"v":2,"type":"lap_complete",...}
{"v":2,"type":"setup_ref","data":{"hash":"…"}}   ← optional (HCR-010)
```
→ `202 { lapId, status: "queued" }` · `200` with the same body when the key was already seen ·
`409` if the sessionId belongs to another user · `413` > 8 MB · `429` rate limit.

**Session finalise** — at `session_end`: `POST /api/simracing/uploads/sessions/{sessionId}/end`
with the `session_end` frame and the list of lap numbers + hashes Link holds; the server answers
with the laps it is missing, Link re-sends those.

**Rules**
- `sessionId` stays client-generated (32-hex) but is **bound to the first user who uses it**; any
  other user → 409 (also fixes H-03 for uploads).
- Uploads and live WSS for the same session are merged; the lap with the most ticks wins.
- Max 120 laps/hour/user; 8 MB per lap after compression (~5 min at 20 Hz).
- Clock: `timestamp` from Link is advisory; server receive time is authoritative for challenges.

## 3. Lap validity (HCR-009)

A lap is **leaderboard-eligible** when all hold:

| Check | Rule |
|---|---|
| Sim valid | Sim's own flag true for the whole lap (ACC `isValidLap`, iRacing no incident/cut flag + `LapCompleted`, F1 `currentLapInvalid=0`). |
| Sector sum | \|S1+S2+S3 − lap\| ≤ 50 ms (when sectors available). |
| Distance | Integrated distance within ±3 % of `TrackLayout.LengthM`. |
| Speed | No tick-to-tick speed change implying > 6 g longitudinal; top speed ≤ car-class ceiling. |
| Continuity | No position jump > 50 m between ticks at 20 Hz (teleport / reset). |
| Pause | No pause or replay frames inside the lap. |
| Lap type | Not an out-lap; for F1 a flying lap in time trial / quali / race. |
| Client | Recorded by a signed Link build ≥ minimum version (from HCR-014). |

Failing laps are still stored and analysable — they just don't rank. Reasons are shown on the lap page.

**Condition key** (separate leaderboards): `dry|wet` for ACC/iRacing (from track wetness / rain);
for F1, an assist bucket: `none` (no TC, no ABS, manual gears, no racing line) vs `assisted`.
Platform and game version are filters, not separate boards.

## 4. Canonical identity

See [02-architecture.md](02-architecture.md) §"Canonical identity". Seed data: ACC tracks/cars
(the 25-track table in `Services/Telemetry/AccTrackLengths.cs` is a starting point), F1 25/26
track ids, iRacing tracks/cars imported from session info as they appear (provisional) and curated
by an admin. Unknown ids never block an upload.
