# HCR-011 — Contextual comments + reports

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — HF-11, HF-12 (decision D9) |
| **Needed by** | Phase 2 (public launch) |
| **Breaking?** | **No — additive only.** |

## Why the app needs this
D9: no generic forum; discussion lives on the thing it's about — setups, track guides, public laps
— and leaderboard rows can be reported.

## What exists today (verified 2026-11-04)
No comment or report entities. Notifications (`Notifications`, `UserNotifications`) exist and can notify on replies.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/comments` | none | `?targetType=setup|track|lap&targetId&cursor` | `{ items: [{ id, author, body, createdAt, editedAt, parentId }], next }` | 400 |
| `POST /api/comments` | user (email confirmed) | `{ targetType, targetId, body (≤ 2000), parentId? }` | `201 { id }` | 400, 404 target, 429 |
| `PATCH /api/comments/{id}` | author, ≤ 15 min | `{ body }` | `204` | 403, 409 window passed |
| `DELETE /api/comments/{id}` | author or admin | — | `204` (soft delete) | 403 |
| `POST /api/reports` | user | `{ targetType: comment|setup|lap|leaderboard_entry, targetId, reason }` | `201` | 409 duplicate |
| `GET /api/admin/reports` / `POST /api/admin/reports/{id}/resolve` | admin | `{ action: dismiss|remove|exclude_lap|ban }` | — | — |

### Data model / migration
`Comment`, `Report` ([07](../07-data-model-and-contracts.md) §1).

### Behaviour
Markdown-lite (bold, italic, links rendered `rel="nofollow ugc"`), one level of replies, reply notification to the parent author.

## Compatibility — why the website is unaffected
New routes/components only.

## Security
Server-side sanitisation; per-user rate limit (10 comments / 10 min); Turnstile on first comment; banned users can't post.

## Acceptance criteria
- [ ] XSS payloads stored and rendered inert.
- [ ] Edit after 15 min → 409; soft-deleted comment shows "[deleted]" with replies kept.
- [ ] `exclude_lap` resolution removes the lap from boards (via HCR-009 admin exclude).

## Rollout / rollback
Flag per target type. Rollback = hide.

## Implementation notes
_(Hub-side session fills this in.)_
