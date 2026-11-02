# Story 005: NotifySkillUsed

> **Epic**: Auto-Attack Combat
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rules 11–15, AC-04, AC-05, EC-21, EC-24
**Requirement**: `TR-aa-005`

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Skill System calls Combat directly on an injected interface.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces
- Forbidden: per-tick allocation on the server hot path

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md`, scoped to this story:*

- [x] `NotifySkillUsed(casterEntityID)` sets the caster's `_actionTimer` to 0 and has no other effect (Rule 12).
- [x] With the timer at 8 ticks of a 20-tick cycle, the call resets it and the next auto fires 20 ticks later, at t = 1.40s ±1 tick (AC-04).
- [x] Auto at t = 1.0s and a skill at t = 1.05s stay two independent full-value events; the timer is 0 at t = 1.05s (AC-05).
- [x] A call after an auto resolved reverses nothing and only restarts the countdown (EC-21).
- [x] Two calls in a row each reset the timer (EC-24).
- [x] A reset ends a miss streak (Story 003).
- [x] `LastBeatServerTickTracker.RecordBeat` is not called by Combat (user decision 2026-10-30).

---

## Implementation Notes

- Only validated, accepted casts call this (Rules 13, 14). That check belongs to the Skill System; the method does not validate.
- A call for an entity with no combat state does nothing and does not throw. A call while `IDLE` also just writes 0 to the frozen timer.
- Expose the method on a small interface the Skill System can inject.
- Doc edits in this story (user decision 2026-10-30): mark `networking-core.md` CR-NET-8 and the grace-window text in `networking-owl-compensation.md` as superseded by the reactive cooldown model. Keep the formulas readable, and add a dated note. `LastBeatServerTickTracker` and the OWL code stay in `src/` unchanged, for a later cleanup.
- Run the Propagation Check on `LastBeatServerTick`, `grace window` and `NotifySkillUsed` before closing.

---

## Out of Scope

- The Skill System itself; AC-05's skill damage is a fake event here
- The `SkillUsedNotification` wire message: Skill System epic
- Removing the OWL code

---

## QA Test Cases

- **AC-04**: run 8 ticks, call, assert timer 0; next auto exactly 20 ticks after the call.
- **AC-05**: auto on tick 20, call on tick 21 → one auto damage at full value, not reversed; timer 0 after the call; a recorded fake skill event is untouched.
- **EC-21**: as AC-05, then the next auto arrives 20 ticks after the call.
- **EC-24**: two calls 2 ticks apart → next auto 20 ticks after the second.
- **Prohibited**: a call while prohibited and past ready → timer 0, no fire on exit.
- **Unknown entity**: no throw, no state change.
- **Only effect**: state, target and toggle are unchanged by the call.
- **Tracker**: a recording `LastBeatServerTickTracker` is not injected; a source search finds no `RecordBeat` call under `src/ServerLogic/AutoAttackCombat/`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_NotifySkillUsed_tests.cs` — must exist and pass

**Status**: [x] Created — 17 tests, all passing

---

## Dependencies

- Depends on: Story 003
- Unlocks: Skill System epic

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 7/7 passing (all auto-verified by `AutoAttackCombat_NotifySkillUsed_tests.cs`; none deferred)
**Deviations** (advisory, none logged as tech debt):
- The Tracker QA case uses a reflection test (no constructor parameter or field of type `LastBeatServerTickTracker`, static included) instead of a source search, since unit tests do no file I/O; a grep of `src/ServerLogic/AutoAttackCombat/` found no `RecordBeat` or `LastBeatServerTick`.
- AC-05 is verified for Combat's half only: the resolved auto keeps its full value and is not reversed. The skill's damage is the Skill System's (out of scope).
- The method is exposed on a new `IAutoAttackSkillUseSink` (not `ICombatService`), so the Skill System injects only this one call. Idle: only the frozen timer is zeroed and the attack speed is not read; active: `ResetTimer` (timer 0, miss streak ended, cycle re-sampled).
- Doc edits done: superseded notes on `networking-core.md` CR-NET-8, F-NET-OWL and the `LastBeatServerTick` line, and on `networking-owl-compensation.md` (header, CR-OWL-2). Stale grace-window wording left for the OWL cleanup: `networking-test-harness.md:281-283` (`OnSkillGraceWindowEvaluated`), `networking-wire-protocol.md:1773` (`RTT_PROBE_INTERVAL_SECONDS` rationale).
**Test Evidence**: Logic: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_NotifySkillUsed_tests.cs` (17 tests). EditMode 5722 / 5593 passed / 0 failed / 129 skipped, no `error CS`.
**Code Review**: Complete (`/code-review` 2026-11-02: APPROVED WITH SUGGESTIONS → 1-6 applied, 7 (fold duplicated fakes) deferred); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean)
