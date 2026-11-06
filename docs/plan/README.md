# Turn One Link — product plan

The plan for taking Turn One Link from an unpublished telemetry bridge to a public sim-racing
product that works together with **turnonehub.com** (the Hub). It was agreed with the owner on
**2026-11-04** through a design interview; every decision and its reasoning is in
[01-decisions.md](01-decisions.md). Read that before proposing to change anything.

## Reading order

| File | What it answers |
|---|---|
| [00-vision-and-scope.md](00-vision-and-scope.md) | Who it is for, what v1 is and is not, where we win |
| [01-decisions.md](01-decisions.md) | D1–D17, with reasoning and rejected alternatives |
| [02-architecture.md](02-architecture.md) | How Link, the Hub, the website and the phone fit together |
| [03-link-bugs.md](03-link-bugs.md) | Every known Link bug, prioritised P0/P1/P2, with cause and fix |
| [04-link-features.md](04-link-features.md) | What the desktop app gains, phase by phase |
| [05-hub-security-and-fixes.md](05-hub-security-and-fixes.md) | Hub-side fixes that must land before any external user |
| [06-hub-features.md](06-hub-features.md) | Website features: leaderboards, setups, tracks, challenges, coach |
| [07-data-model-and-contracts.md](07-data-model-and-contracts.md) | New entities, the upload contract, lap validity rules |
| [08-ai-roadmap.md](08-ai-roadmap.md) | Rule-based coach now, own model later |
| [09-mobile-and-ecosystem.md](09-mobile-and-ecosystem.md) | LAN phone dash, turn-one-mobile, turn-one-media, Discord |
| [10-roadmap.md](10-roadmap.md) | Phases 0–4 with dates, exit criteria and dependencies |
| [11-quality-gates-and-release.md](11-quality-gates-and-release.md) | CI, tests, installer, signing, privacy checklist |
| [12-ideas-backlog.md](12-ideas-backlog.md) | Good ideas with no phase yet |
| [hub-change-requests/](hub-change-requests/README.md) | HCR-006…016: additive Hub changes Link needs |

## Status legend

Used in every table that tracks work: `todo` · `in progress` · `done` · `blocked (on X)` · `dropped`.

## Repos this plan touches

| Repo | Path | Role |
|---|---|---|
| Turn One Link | `D:\Formula 1\Turn One\turn-one-link\Turn One Link\` | This app (.NET 9 WPF) |
| Turn One Hub | `C:\Stuff and Games\Formula One\Turn One\` | `turn-one-backend` (ASP.NET 9) + `turn-one-client` (Next.js 16) |
| turn-one-mobile | `D:\Formula 1\Turn One\turn-one-mobile\` | Expo app; owns HCR-001…005 |
| turn-one-media | `D:\Formula 1\Turn One\turn-one-media\` | Tokens (colours/fonts) and Remotion/Blender video pipeline |

## How work reaches the Hub

Two routes, deliberately different:

1. **Hub-side fixes** ([05](05-hub-security-and-fixes.md)) change existing behaviour (security
   holes, the session auto-delete). The owner approves them directly and a session opened in the
   Hub repo implements them.
2. **Additive features** go through **Hub Change Requests**, exactly as turn-one-mobile does it:
   a self-contained spec in [hub-change-requests/](hub-change-requests/README.md), approved by the
   owner, implemented by a Hub-side session. Numbering continues the mobile series
   (HCR-001…005 are mobile's; Link's start at **HCR-006**).

To hand an HCR over, open a Claude Code session in `C:\Stuff and Games\Formula One\Turn One` and say:

> Implement `D:\Formula 1\Turn One\turn-one-link\Turn One Link\docs\plan\hub-change-requests\HCR-NNN-<slug>.md`.
> Follow the Hub's CLAUDE.md and conventions. Show every acceptance criterion passing.
> Do not change anything the HCR doesn't ask for. Report back the PR link and the deployed-to-dev confirmation.
