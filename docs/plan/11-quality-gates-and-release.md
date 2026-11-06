# 11 — Quality gates and release

## 1. Link CI (GitHub Actions, `MihaiM21/Turn-One-Link`)

| Job | On | Steps |
|---|---|---|
| `build-test` | every push / PR | `dotnet restore` → `dotnet build -warnaserror` → `dotnet test tests/` (unit + replay) |
| `package` | tag `v*` | `dotnet publish -c Release -r win-x64 --self-contained` → Velopack pack → **sign** (Azure Trusted Signing) → upload to GitHub Release (+ Hub release feed, HCR-014) |
| `secrets` | every push | gitleaks |

A PR merges only with `build-test` green. Release tags come from the csproj `<Version>` (T5).

## 2. Tests

| Layer | What | Where |
|---|---|---|
| Unit | `SessionManager` lifecycle (one start/end per session, pause/resume), `DeltaEngine`, `FuelManager`, mappers (clutch convention, F1 driverStatus/trackId), upload queue state machine | `tests/Turn One Link.Tests/` |
| Parser | F1 packets at spec offsets (existing 10 tests), per-`packetFormat` size tables | existing `F1/` |
| **Replay** | Recorded sessions run through each adapter; assert lap count, lap times, sectors sum, validity, single `session_start`. Fixtures: ACC Paul Ricard logs from `dev_logs/telemetry_20260918/19`, plus one recording per sim made during Phase 0 (ACC shared memory dumped per frame; F1 raw UDP pcap-style; iRacing IBT). | `tests/fixtures/` (trimmed, < 20 MB, git LFS if larger) |
| Network | LAN server: concurrent sends, handshake limits, PIN lockout | in-process WS client |
| Contract | Frames Link emits validate against the v2 schema the Hub expects (`ChannelRegistry` mirror) | JSON schema in `docs/telemetry/` |
| Manual | Release checklist below | — |

## 3. Release checklist (every release)

- [ ] Clean Windows 11 VM: install, sign-in via pairing, first lap uploaded, uninstall leaves nothing but (optional) sessions.
- [ ] Update N-1 → N keeps sign-in and outbox.
- [ ] One session each in ACC, F1 (PC), F1 (console UDP), iRacing.
- [ ] SmartScreen shows the signed publisher.
- [ ] Changelog in the release feed; version matches everywhere.

## 4. Beta operations

- Discord `#link-beta` channel; "Report a problem" (LF-18) bundles logs.
- Weekly beta build on the `beta` channel; `stable` promoted after 7 days without P0 reports.
- Dashboards (Hub): sessions recorded vs fully received, upload error codes, lap validity reasons,
  crash-free rate (Sentry), time-to-first-lap.

## 5. Privacy / GDPR checklist (before beta)

- [ ] Privacy policy section for sim telemetry: what is collected, retention (D5), visibility defaults (D12), AI consent (D8).
- [ ] Data export (all my laps/sessions) and account deletion cover sim data (coordinate with HCR-002).
- [ ] Leaderboard opt-out / anonymous mode works and removes the name everywhere, including Discord posts.
- [ ] Sentry events carry no telemetry payloads, no tokens, no emails.
- [ ] LAN dashboard PIN never logged.

## 6. Distribution

- Website download page (HF-06) is the only official source; GitHub Releases as the update origin.
- No Microsoft Store in v1 (Velopack + signing covers trust; Store review slows hotfixes).
- Minimum OS: Windows 10 22H2 / Windows 11, x64.
