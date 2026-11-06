# HCR-014 — Link release feed with channels + minimum version

| | |
|---|---|
| **Status** | approved (owner, 2026-11-06) |
| **Requested by** | turn-one-link — LF-01, HF-06, lap validity "client" rule |
| **Needed by** | Phase 0–1 |
| **Breaking?** | **No — additive only.** `GET api/simracing/link/release` keeps its response; new optional fields and a new route. |

## Why the app needs this
The download page shows "coming soon" because `TurnOneLink:DownloadUrl` is empty. Link needs to
know about beta/stable releases for auto-update, and the Hub needs a minimum supported version
to refuse uploads from builds with known lap-timing bugs.

## What exists today (verified 2026-11-04)
- `API/Controllers/SimracingLinkController.cs` — `GET api/simracing/link/release` (anonymous,
  cached 300 s), DTO `{ Available, Version, ReleasedAt, DownloadUrl, Sha256, SizeBytes, MinWindowsBuild, Changelog[] }` from config section `TurnOneLink`.
- `API/appsettings.json` `TurnOneLink`: Version `1.1.0`, `DownloadUrl` empty.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/simracing/link/release` *(existing)* | none | `?channel=stable|beta` (optional, default stable) | existing fields **plus** `channel`, `minSupportedVersion`, `velopackFeedUrl` | — |
| `GET /api/simracing/link/compat` | none | `?version=1.2.3` | `{ supported, uploadsAccepted, message? }` | 400 |

### Data model / migration
None — config only: `TurnOneLink:Channels:{stable|beta}:{Version,DownloadUrl,Sha256,SizeBytes,ReleasedAt,VelopackFeedUrl}`,
`TurnOneLink:MinSupportedVersion`, `TurnOneLink:MinUploadVersion`. Existing flat keys remain the stable channel's fallback.

### Behaviour
1. Without `channel`: byte-for-byte today's response plus the new fields.
2. Uploads (HCR-007) and WSS from versions below `MinUploadVersion` → 426 with a message; Link shows "Update required".
3. CI writes the new release into config (or a small `LinkReleases` table later).

## Compatibility — why the website is unaffected
Download page reads the same fields; new fields are ignored until the page uses them.

## Security
`Sha256` published so the download page and Link can verify.

## Acceptance criteria
- [ ] No-channel response matches today's snapshot plus new fields.
- [ ] `compat` returns `uploadsAccepted=false` below `MinUploadVersion`.

## Rollout / rollback
Config-driven; rollback = revert config.

## Implementation notes
_(Hub-side session fills this in.)_
