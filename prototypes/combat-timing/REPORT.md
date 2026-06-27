# Prototype Report: Combat Timing

**Date:** 2026-04-19
**Status:** Concluded — PROCEED

---

## Hypothesis

Cancelling auto-attack damage when a skill fires before the beat feels like a fair,
learnable punishment that creates meaningful timing decisions on touch input.

---

## Approach

Built a single-scene Unity 6.3 prototype with no networking, no classes, no items.
One dummy enemy, a charge bar showing the upcoming auto-attack beat, and three skill
buttons with different cooldowns. Keyboard shortcuts (1/2/3) used for editor testing.

**Shortcuts taken:** no audio, no animations, no sprites (colored rectangles only),
no real enemy AI, no save state.

**Bugs fixed during prototype:**
- `StandaloneInputModule` incompatible with New Input System — replaced with
  `InputSystemUIInputModule`
- `_skillUsedThisCycle` flag persisted across cycles, incorrectly cancelling the
  auto even when a skill was cast after the beat — fixed with `_autoHasFiredThisCycle`
  guard flag
- Beat indicator used `Image.Type.Filled` + `FillMethod.Radial360` without a sprite,
  making it invisible — replaced with a horizontal fill bar

---

## Result

The core mechanic works. With a 1.0s cadence, cancellation read as a fair and
understandable consequence — not arbitrary or frustrating. The natural play pattern
that emerged: wait for the auto to land, then immediately cast available skills.
This is exactly the intended rhythm.

Testing at 2.0s cadence confirmed 1.0s is the correct target — 2.0s is too slow and
reduces the tension of the timing decision.

The beat indicator was invisible for most of the session (sprite bug) yet feel was
still positive. This suggests the mechanic is strong enough to survive imperfect
feedback, and that the fixed horizontal bar will improve clarity significantly.

---

## Metrics

| Metric | Value |
|--------|-------|
| Cadence tested | 1.0s (preferred), 2.0s (too slow) |
| Cancellation feel | Fair — not frustrating |
| Beat indicator visibility | Not visible (bug, now fixed) |
| Core mechanic verdict | Feels correct |

---

## Recommendation: PROCEED

The hypothesis is confirmed. The timing system creates a readable, learnable rhythm
without a tutorial. The punishment (lost auto damage) is proportionate and motivates
waiting rather than mashing. This mechanic is ready to be designed as a full GDD
and implemented in production.

---

## For Production Implementation

- **Rewrite from scratch** — do not migrate prototype code. Prototype is preserved
  for reference only.
- **State model:** The `_autoHasFiredThisCycle` / `_skillUsedThisCycle` two-flag
  pattern is the correct design. Document this in the Auto-Attack Combat GDD.
- **Audio is required:** The visual bar alone may not be sufficient feedback on a
  noisy mobile device. A distinct audio cue on each beat is necessary for rhythm
  mastery to feel natural.
- **Server authority:** The cadence timer must live on the server. Client shows a
  predicted ring; server resolves whether the skill arrived before or after the beat.
  Latency tolerance window needs design (recommend ±80ms grace window).
- **Cadence target:** 1.0s for melee. Consider 1.4s for slower heavy weapons if
  multiple weapon types are added post-MVP.
- **Cancellation feedback:** "CANCELLED" text is sufficient for prototype. In
  production, a brief red flash on the charge bar and a distinct audio stab will
  communicate the miss more clearly without screen text.

---

## Lessons Learned

- Unity 6.3 URP projects default to New Input System — prototype scaffolding must
  use `InputSystemUIInputModule`, not `StandaloneInputModule`
- `Image.Type.Filled` with `Radial360` requires a sprite asset — use horizontal
  fill bars for code-only prototypes
- The two-flag cancellation state (`autoHasFired` + `skillUsedThisCycle`) is
  cleaner than a single flag and correctly handles the post-beat safe window
