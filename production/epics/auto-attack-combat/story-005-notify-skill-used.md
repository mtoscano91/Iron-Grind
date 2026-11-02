# Story 005: NotifySkillUsed

> **Epic**: Auto-Attack Combat
> **Status**: Ready
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

- [ ] `NotifySkillUsed(casterEntityID)` sets the caster's `_actionTimer` to 0 and has no other effect (Rule 12).
- [ ] With the timer at 8 ticks of a 20-tick cycle, the call resets it and the next auto fires 20 ticks later, at t = 1.40s ±1 tick (AC-04).
- [ ] Auto at t = 1.0s and a skill at t = 1.05s stay two independent full-value events; the timer is 0 at t = 1.05s (AC-05).
- [ ] A call after an auto resolved reverses nothing and only restarts the countdown (EC-21).
- [ ] Two calls in a row each reset the timer (EC-24).
- [ ] A reset ends a miss streak (Story 003).
- [ ] `LastBeatServerTickTracker.RecordBeat` is not called by Combat (user decision 2026-10-30).

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Skill System epic
