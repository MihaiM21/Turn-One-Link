# 05 — Hub security and fixes

Hub-side work in `C:\Stuff and Games\Formula One\Turn One` (BE = `turn-one-backend\`,
FE = `turn-one-client\`). These **change existing behaviour**, so they are not HCRs: the owner
approves this list and a Hub-side session implements it. All **H-0x** items are Phase 0 and block
the closed beta — the beta invites strangers to an API that today lets them sign admin tokens.

Verified 2026-11-04 (spot-checked: overlay `Scope` claim is written in
`OverlayTokenService.cs:81` and read nowhere; the `default123` seed is in `API/Program.cs`;
seven `Forbid("…")` calls exist).

## Critical (Phase 0, in this order)

| ID | Problem | Where | Fix | Status |
|---|---|---|---|---|
| H-01 | **Production JWT signing key in pushed git history** (commits `0f353cb`, `3c377e2`, `f6c6b1e` on `dev` and other branches); same key in dev/local/prod. SMTP password in local stash `103ec0d`. Anyone with the key can mint ADMIN tokens. | `BE/API/appsettings.json` history, `.env.production` | 1) Generate new per-environment keys (≥ 256-bit), stored only in Coolify secrets. 2) Deploy — **every user is logged out once**; announce it. 3) Rotate the SMTP password. 4) Purge history with `git filter-repo` and force-push all branches (coordinate: other clones must re-clone). 5) Add gitleaks to CI. | merged to `dev` (PR #139), not on prod |
| H-02 | **Overlay token → full owner login.** `POST /api/simracing/overlay/exchange` (anonymous) returns a 30-min JWT with the owner's `NameIdentifier`; the `Scope=overlay` claim is never checked, so it works on every authenticated endpoint (delete sessions, edit profile, stream to WSS). | `BE/Infrastructure/Services/OverlayTokenService.cs:77-92` | Global authorization policy: tokens with a `Scope` claim are rejected everywhere except endpoints that opt in to that scope (`[Authorize(Policy="overlay")]` on the overlay read endpoints and hub methods). Same mechanism serves `scope=link` (HCR-006). Store overlay tokens hashed, not plaintext. | merged to `dev` (PR #139), not on prod |
| H-03 | **Telemetry sessions can be hijacked.** Ingestion trusts the client-chosen `sessionId`: `StartOrUpsertSessionAsync`/`V2Async` overwrite and reactivate any session; ticks and `lap_complete` inject into any live session; end/pause/heartbeat act on any session. Public session ids are listed. Injected ticks reach spectators. | `BE/Infrastructure/Services/TelemetrySessionService.cs:25-40, 98-109, 221-255`; `BE/API/Services/TelemetryIngestionService.cs:345-437` | Every ingest path loads the session and requires `session.UserId == connection.UserId`; an unknown id is created for the caller; a foreign id → close 1008. Cache ownership per connection. Tests for each message type. | merged to `dev` (PR #139), not on prod |
| H-04 | **Password hashes and reset tokens returned to users.** Whole `User` entity in responses. | `BE/API/Controllers/SubscriptionController.cs:66-72, 129-135, 149-155`; `AdminController.cs:45-54` | **HCR-005** (turn-one-mobile) covers the subscription routes; extend to `AdminController`; add a test that no response DTO serialises `Password`/`*Token`. | merged to `dev` (PR #139), not on prod |
| H-05 | **Hardcoded admin** `mihai@t1f1.com` / `default123` created on startup if missing. | `BE/API/Program.cs:369-390` | Delete the seed; create admins with a one-off CLI command reading the password from stdin. Check prod that this account's password isn't still `default123`. | merged to `dev` (PR #139), not on prod |
| H-06 | **Unlimited self-awarded XP.** | `BE/API/Controllers/LevelSystemController.cs:53` (`POST add-experience`) | Make it internal (service call only) or `ADMIN`; all XP from server events (HCR-012). | merged to `dev` (PR #140), not on prod |
| H-07 | **No rate limiting.** Login, register, forgot-password, contact, api-wishlist, WSS unlimited; raw-tick queue unbounded (`Program.cs:322`). | `BE/API/Program.cs` | ASP.NET `RateLimiter`: per-IP on auth (5/min), forgot-password (3/h), contact (5/h); per-user on WSS connects and upload (HCR-007); bounded channel for raw ticks (drop + metric). | merged to `dev` (PR #140), not on prod |
| H-08 | **T1API open relay.** Catch-all proxy forwards any path and method to the paid T1API with the server key; gate is an anon cookie anyone receives; cookie secret has a hardcoded fallback. | `FE/app/api/[...endpoint]/route.ts:140-147, 257-260`; `FE/lib/server/anon-cookie.ts:7` | Allowlist of GET paths actually used; reject non-GET; per-IP rate limit; fail closed when the cookie secret is missing. | merged to `dev` (PR #140), not on prod |
| H-09 | **Spectating unchecked.** `SpectateSession` joins any session group. | `BE/API/Hubs/SimTelemetryHub.cs:41` | Require session `Visibility=Public` (or owner) and the caller's plan per D5. | merged to `dev` (PR #140), not on prod |
| H-10 | **Admin pages gated only in the browser.** | `FE/proxy.ts:7-19` (server check commented out) | Restore the server-side role check (BE still enforces on APIs — this is defence in depth and stops leaking admin UI). | partly — admin pages can't be gated server-side while the token is in localStorage; `noindex` added, backend roles enforce (`fix/phase0-hub`) |

## Important (Phase 0–1)

Status as of 2026-11-07: everything marked merged is on `dev`, **not on prod** until `dev` is deployed.

| ID | Problem | Where | Fix | Status |
|---|---|---|---|---|
| H-11 | `Forbid("message")` treats the text as an auth scheme → **500** instead of 403. | `TelemetryController.cs:97,110,346,382`; `CoachingController.cs:31,56`; `OverlayController.cs:61` | `StatusCode(403, new { error = … })` / ProblemDetails with an `upgradeRequired` code the FE can turn into an upsell. | merged to `dev` (PR #140) |
| H-12 | Plan read from the 7-day JWT — upgrades/downgrades lag; a WSS connection keeps its plan. | auth + `PlanFeatures` | Read plan from DB (cached 60 s) in a policy handler; short access tokens via HCR-003. | merged to `dev` (PR #147) — plan read from the DB on each request (60 s cache); access tokens still 7 days until HCR-003 |
| H-13 | **Session auto-deletion** at 1/5/15 sessions (contradicts D5). | `TelemetrySessionService.cs:42-60, 112-129`; `Domain/PlanFeatures.cs` | Stop deleting; replace with D5 retention (full-res for best laps on free, downsample the rest). Ship before the beta so no beta data is lost. | merged to `dev` (PR #140) — deletion stopped; D5 downsampling still todo |
| H-14 | User ids exposed by anonymous leaderboard. | `TelemetryController.cs:79` | Return username + avatar only. | todo — `TelemetryController.cs:79` still returns `UserId` |
| H-15 | Console log on every authenticated request. | `Program.cs:284,291,296` | Remove / Debug level. | partly — two `Console.WriteLine` left in `Program.cs:291,298` |
| H-16 | Tests don't gate CI. | `.github/workflows/pipeline.yml:112-121` (`continue-on-error: true`); FE Vitest never run | Make BE tests blocking; add `npm test` + `typecheck`; add controller/auth/ingestion security tests for H-02, H-03, H-04, H-09. | merged to `dev` (PR #140) |
| H-17 | OAuth buttons are "coming soon"; Turnstile keys unused. | `FE/components/auth/oauth-buttons.tsx:20,30` | Turnstile on register/forgot-password now; Google/Discord sign-in in Phase 2 (Discord pairs with HCR-013). | merged to `dev` (PR #147) — Turnstile on register only (forgot-password would break turn-one-mobile); off until both keys are set |
| H-18 | Download page promises "Link stays signed in" — false today. | `FE/components/turn-one-link/setup-content.ts:21` | True after HCR-006; until then, reword. | todo — now true via pairing, but `setup-content.ts:21` still says "email and password" |
| H-19 | Release feed `DownloadUrl` empty → "coming soon". | `SimracingLinkController` + `TurnOneLink` config | HCR-014. | merged to `dev` (PR #142, HCR-014) — needs `TurnOneLink:DownloadUrl` set in Coolify |

## Housekeeping (when touched)

- Sitemap misses `/download`, `/how-it-works`, `/explore` (`FE/app/sitemap.ts:22-31`); add leaderboard/track pages when they ship.
- `live` and `live2` both exist — pick one.
- Doc drift: CLAUDE.md references `lib/api.ts`, `lib/signalr-client.ts`, `hooks/use-f1-signalr-live-data.ts` (gone); README/overview say Next 15; `sim-racing-telemetry.md:70` says `api/overlay` (it's `api/simracing/overlay`); CLAUDE.md says SQLite in dev (Postgres only). HCR rules mention SQLite migrations — confirm which DB dev really uses.
- Landing page issues in `docs/LANDING_PAGE_REDESIGN_RESEARCH.md` (hidden H1, no CTA, loading gate) — fix before the March launch push.
- Uncommitted work on `chore/post-paywall-hygiene` (22 files) — land or park it before Phase 0 work branches off.
