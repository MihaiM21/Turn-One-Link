# 08 — AI roadmap

**Decision D8:** own model later, not now. Until then a deterministic coach, and from day one an
opt-in dataset that makes the later model possible.

## Stage A — rule-based coach (Phase 2, ships with launch)

Replaces the stub in `CoachingController` (`GET sessions/{id}/tips`, `POST sessions/{id}/chat`).

**Inputs** (already computed by `LapProcessor`): per-corner `LapCorners` rows — braking point,
entry/apex/exit speed, throttle-on point, gear, trail-braking — and the 2 m-grid channels.

**Reference laps**, in order: own PB on the same sim × layout × car → top-10 median on the
leaderboard (paid) → challenge target.

**Rules** (versioned `RulesetVersion`, each a pure function → `CoachTip`):

| Rule | Fires when | Tip template |
|---|---|---|
| `brake_early` | braking point ≥ 8 m earlier than reference and entry speed lower | "Brake {Δ} m later into {corner} — you brake at {you} m, reference {ref} m." |
| `min_speed_low` | apex speed ≥ 4 km/h below reference | "Carry {Δ} km/h more through {corner}'s apex." |
| `late_throttle` | throttle-on ≥ 10 m after reference | "Get back on throttle {Δ} m earlier out of {corner}." |
| `coast` | > 0.3 s with neither pedal > 5 % | "You coast for {t} s before {corner}." |
| `lift_flat` | throttle < 90 % where reference stays flat | "The top 10 take {corner} flat." |
| `gear` | different gear at apex | "Try {gear} instead of {yourGear} in {corner}." |
| `consistency` | std-dev of a corner's time over the session > 0.25 s | "{corner} varies by {sd} s lap to lap — your biggest consistency loss." |

Tips are **ranked by time gained** (corner-time delta from the 2 m grid) and the top 3 shown.
Every tip has 👍/👎 (`CoachFeedback`).

## Stage B — dataset (starts with the beta)

| What | Why | Consent |
|---|---|---|
| Processed laps (2 m grid + corners) | Training/eval input | AI consent (D12) |
| Tips generated + rule + params | Labels for "what advice fits this situation" | AI consent |
| 👍/👎 + optional comment | Human preference signal | AI consent |
| PB progression after a tip was shown | Outcome label: did the advice make them faster? | AI consent |
| Setup values ↔ lap time | Setup recommendation data | AI consent |

- `AiConsent` is versioned; withdrawing removes the user's rows from future training exports.
- Exports are pseudonymised (user id → random id per export).
- Privacy policy section written before the beta (FE `privacy` page).

## Stage C — own model (Phase 4, go/no-go gate)

**Go only when all hold:**
- ≥ 1 M consented laps and ≥ 50 k rated tips.
- Stage A tips have ≥ 70 % 👍 — otherwise improve rules first; a model trained on bad advice learns bad advice.
- A paying base that covers inference (estimate per question before building).

**Shape (to decide at the gate):**
1. *Race Engineer* — answers questions about the user's own data ("where do I lose time in sector 3
   at Spa?") by calling tools over laps/corners/leaderboards; returns text + chart specs.
2. *Pit Wall* — real-F1 Q&A over T1API (stats, strategy, history), tied to the YouTube channel.
3. *Setup advisor* — suggests setup changes from the lap + setup dataset.

**Build options at the gate:** fine-tune an open-weights model on the dataset and host it, or a
small model for tip ranking + a general LLM for language. Evaluate on a held-out set of laps
where we know which advice led to improvement.

**Evaluation set** (start curating in Phase 2): 500 laps with expert-written ideal advice
(owner + 2–3 fast drivers from the beta), used to score Stage A and any Stage C candidate.
