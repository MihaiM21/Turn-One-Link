# HCR-016 — Teams (outline)

| | |
|---|---|
| **Status** | outline — write the full contract at the start of Phase 3 |
| **Requested by** | turn-one-link — HF-30, LF-35 (decision D16) |
| **Needed by** | Phase 3 (after public launch) |
| **Breaking?** | **No — additive only** (expected). |

## Why the app needs this
Leagues and esports teams share laps and setups and have coaches who review drivers' data.
D16 puts this after launch, paid per team.

## What exists today
Nothing team-shaped. Visibility today is Private/Public per session.

## Scope to specify in Phase 3
- `Team { Id, Name, Slug, OwnerId, Plan }`, `TeamMember { TeamId, UserId, Role: owner|driver|coach, JoinedAt }`, invites by link.
- New visibility value `Team` for sessions and setups; coach role read-only on members' sessions (respecting each member's opt-in).
- Team leaderboards (filter of HCR-009 boards to members).
- Team setups appear in Link's install list (HCR-010 `pending-installs` scope).
- Billing per team (seat count) — through whatever payment flow the paywall work introduced.

## Open questions for the owner (Phase 3)
1. Can a driver belong to several teams?
2. Does a team plan grant PRO features to members, or only team features?
3. Coach access: per-session opt-in, or blanket while a member?

## Implementation notes
_(Hub-side session fills this in.)_
