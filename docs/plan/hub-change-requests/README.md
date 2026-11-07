# Hub Change Requests from Turn One Link

Same process, rules and template as turn-one-mobile
(`D:\Formula 1\Turn One\turn-one-mobile\docs\hub-change-requests\README.md` and `TEMPLATE.md`):
**additive only**, website unaffected, complete contract, acceptance tests, rollout/rollback, no
secrets. The owner approves; a session opened in `C:\Stuff and Games\Formula One\Turn One`
implements; that session updates only **Status** and **Implementation notes**.

```
draft → approved → in progress → on dev → on prod → closed      (or rejected / sent back)
```

Numbering continues the mobile series so numbers never collide: **HCR-001…005 belong to
turn-one-mobile; Link's start at HCR-006.** Add a row to mobile's index too when a new HCR is written.

Changes that alter existing behaviour (security holes, session auto-deletion) are **not** HCRs —
they are in [05-hub-security-and-fixes.md](../05-hub-security-and-fixes.md).

## Index

| HCR | Title | Needed for | Status |
|---|---|---|---|
| HCR-003 *(mobile's)* | Refresh tokens, additive | Base for HCR-006 | draft |
| HCR-005 *(mobile's)* | No `User` entity in token-consumption responses | Phase 0 (H-04) | draft |
| [HCR-006](HCR-006-device-pairing.md) | Device pairing + scoped device tokens for Link | Phase 1 (beta) | on dev (2026-11-07) |
| [HCR-007](HCR-007-lap-upload-api.md) | Lap / session upload API (local-first, idempotent) | Phase 1 (beta) | on dev (2026-11-07) |
| [HCR-008](HCR-008-track-car-catalog.md) | Canonical track & car catalog with per-sim aliases | Phase 1 (beta) | on dev (2026-11-07) |
| [HCR-009](HCR-009-leaderboards.md) | Lap validation + per-track/car leaderboards | Phase 1 (beta) | on dev (2026-11-07) |
| [HCR-010](HCR-010-setups.md) | Setups linked to laps + install requests | Phase 2 (launch) | draft |
| [HCR-011](HCR-011-comments-reports.md) | Contextual comments + reports | Phase 2 (launch) | draft |
| [HCR-012](HCR-012-weekly-hotlap-xp.md) | Weekly Hotlap Challenge + server-side sim XP | Phase 2 (launch) | draft |
| [HCR-013](HCR-013-discord.md) | Discord account link + record/challenge webhooks | Phase 2 (launch) | draft |
| [HCR-014](HCR-014-link-release-feed.md) | Link release feed with channels + minimum version | Phase 0–1 | on dev (2026-11-07) |
| [HCR-015](HCR-015-ai-consent-coach-feedback.md) | AI dataset consent + coach tips & feedback | Phase 1 (consent) / 2 (feedback) | consent on dev (2026-11-07); tips + feedback draft |
| [HCR-016](HCR-016-teams.md) | Teams (outline only) | Phase 3 | outline |
