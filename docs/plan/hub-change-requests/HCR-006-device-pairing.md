# HCR-006 — Device pairing + scoped device tokens for Turn One Link

| | |
|---|---|
| **Status** | draft |
| **Requested by** | turn-one-link — LF-10, HF-01 |
| **Needed by** | Phase 1 (closed beta, Jan 2027) |
| **Breaking?** | **No — additive only.** Builds on HCR-003; website login unchanged. |

## Why the app needs this
Link today signs in with the website email + password via `POST /api/auth/login` and gets a
7-day JWT; when it expires the user is signed out, and the download page promises the opposite.
Storing passwords in a desktop app is wrong, and a full-power user token on a gaming PC is a
bigger blast radius than needed. Link needs a browser-approved pairing and a long-lived,
revocable token that can only do Link things.

## What exists today (verified 2026-11-04)
- `Infrastructure/Services/AuthService.cs:295-341` — HS256 JWT, 7 days, no refresh.
- Link: `Services/AuthService.cs` reads the token from several response shapes; `SecureTokenStore` (DPAPI).
- Overlay tokens already carry a `Scope` claim (`OverlayTokenService.cs:81`) — H-02 adds scope enforcement; this HCR reuses it.

## The change
### Contract
| Method & path | Auth | Request | Response | Errors |
|---|---|---|---|---|
| `POST /api/devices/pair/start` | none | `{ deviceName, platform: "windows", appVersion }` | `{ deviceCode, userCode: "K7QF-2M9D", verifyUrl, expiresIn: 600, interval: 5 }` | 429 |
| `GET /api/devices/pair/{userCode}` | user | — | `{ deviceName, platform, appVersion, createdAt }` | 404 expired/unknown |
| `POST /api/devices/pair/{userCode}/approve` | user | `{}` | `204` | 404, 409 already approved |
| `POST /api/devices/pair/poll` | none | `{ deviceCode }` | pending: `428 { status: "pending" }` · approved: `{ accessToken, accessTokenExpiresAt, deviceToken, deviceId }` | 400 expired, 429 slow_down |
| `POST /api/devices/token/refresh` | none | `{ deviceToken }` | `{ accessToken, accessTokenExpiresAt, deviceToken }` (rotated) | 401 invalid/reused/revoked |
| `GET /api/devices` | user | — | `[{ id, name, platform, appVersion, createdAt, lastSeenAt }]` | — |
| `DELETE /api/devices/{id}` | user | — | `204` (revokes all its tokens) | 404 |

### Data model / migration
`Device`, `DevicePairing`, `DeviceRefreshToken` as in [07](../07-data-model-and-contracts.md) §1.
Hashes are SHA-256; codes are 32 random bytes (device) / 8 chars from an unambiguous alphabet (user).

### Behaviour
1. Access tokens: 15 min, claims `NameIdentifier`, `scope=link`, `deviceId`.
2. `scope=link` is accepted only on: `/api/simracing/uploads/*` (HCR-007), `/api/ws/telemetry`,
   `/api/setups/*` read + upload + pending-installs (HCR-010), `GET /api/telemetry/sessions/me`,
   `GET /api/auth/me`, `/api/devices/token/refresh`. Everything else → 403.
3. Refresh rotates; reuse of a rotated token revokes the device (same family logic as HCR-003).
4. Device token lifetime 180 days sliding; `LastSeenAt` updated on refresh.
5. Password reset revokes nothing (devices are separately managed); account deletion (HCR-002) revokes all.

### Configuration
`Devices:PairingTtlSeconds`, `Devices:AccessTokenMinutes`, `Devices:RefreshDays` — values from the owner.

## Compatibility — why the website is unaffected
- Existing endpoints touched: none. `/api/ws/telemetry` additionally accepts `scope=link` tokens (it already accepts user JWTs).
- New page `/link` and Settings → Devices are new routes.

## Security
Rate limit `pair/start` 10/h per IP, `poll` honours `interval`, `approve` requires a fresh page view
(CSRF-safe: token in `Authorization`, not cookies). Never log codes or tokens.

## Acceptance criteria
- [ ] Full pairing flow test: start → approve → poll returns tokens; poll before approve → 428.
- [ ] `scope=link` token rejected on a non-allowlisted endpoint (e.g. `PUT /api/users/me`).
- [ ] Refresh rotation; reuse → device revoked; revoked device → 401 on refresh and WSS.
- [ ] Expired pairing → 400; `GET /api/devices` lists only own devices.

## Rollout / rollback
Deploy; Link ships pairing behind a setting, keeps email/password as fallback for one beta build,
then removes it. Rollback = Link falls back to password login.

## Implementation notes
_(Hub-side session fills this in.)_
