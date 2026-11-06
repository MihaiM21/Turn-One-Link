# HCR-013 — Discord account link + record/challenge webhooks

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — HF-15 (decision D9) |
| **Needed by** | Phase 2 (public launch) |
| **Breaking?** | **No — additive only.** |

## Why the app needs this
Discord is the community (D9). New records and challenge results posted there pull people back;
linking accounts lets the bot mention people and sync plan roles.

## What exists today (verified 2026-11-04)
No Discord integration. OAuth buttons are stubs (`FE/components/auth/oauth-buttons.tsx:20,30`).

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `GET /api/integrations/discord/authorize` | user | — | 302 to Discord OAuth (`identify` scope only) | — |
| `GET /api/integrations/discord/callback` | user (state) | `code, state` | 302 back to settings | 400 bad state |
| `DELETE /api/integrations/discord` | user | — | `204` | — |
| `GET /api/bot/simracing/pb` | bot key | `?discordUserId&track` | PBs for the bot's `/pb` command | 404 |

### Data model / migration
`DiscordLink { UserId, DiscordUserId, LinkedAt }`.

### Behaviour
1. Subscribers to `LeaderboardRecord` (rank 1 only, debounced 10 min per board) and `ChallengeClosed` post to configured channel webhooks.
2. Anonymous/hidden users (HCR-009) are never named in posts.
3. Role sync job (hourly) for PRO/ELITE — requires a bot token with Manage Roles.

### Configuration
`Discord:ClientId`, `Discord:ClientSecret`, `Discord:BotToken`, `Discord:Webhooks:Records`, `Discord:Webhooks:Challenges`, `Discord:GuildId` — values from the owner.

## Compatibility — why the website is unaffected
New routes; settings page gains a section.

## Security
OAuth `state` bound to the session; bot endpoint authenticated with a separate key; webhook URLs are secrets.

## Acceptance criteria
- [ ] Link/unlink flow; state mismatch rejected.
- [ ] Record post fires once per new #1, not for anonymous users.

## Rollout / rollback
Webhooks off by config until the server is ready. Rollback = clear webhook config.

## Implementation notes
_(Hub-side session fills this in.)_
