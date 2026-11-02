# Story 003: Resolution Order

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rule 10, Rule 11, F-2, F-3, AC-09 to AC-11, AC-20, EC-5 to EC-8, EC-10, EC-17, EC-19; `design/gdd/hit-detection.md` — CR-HD-10, AC-HD-11a, AC-HD-12, AC-HD-13
**Requirement**: `TR-aa-003`, and the miss-streak part of `TR-aa-009` (placeholders)

**ADR Governing Implementation**: ADR-012; ADR-014; ADR-013: Server Random Provider
**ADR Decision Summary**: state changes apply in the request step, before the fire check (EC-8); the crit roll stays inside `DamageCalculator.Calculate`, one draw per call.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: use `HitDetector.CheckHit`, then `IHitResultEmitter.Emit`; `CheckHit` does not emit
- Forbidden: passing the roll into `DamageCalculator.Calculate` as a value
- Forbidden: per-tick allocation on the server hot path

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md` and `hit-detection.md`, scoped to this story:*

- [ ] Rule 10 runs in order: prohibited check → `CheckHit` → damage → reset. `autoShouldFire` follows F-3.
- [ ] Out of range: no damage, timer keeps running, the auto retries every tick and fires on the first in-range tick (AC-09, AC-20, EC-6).
- [ ] A 5.0-unit Y difference within 3.0 units on XZ hits (AC-10).
- [ ] `PROHIBITED`: no damage, timer keeps running, the auto fires on the first tick after the flag clears, and the timer is not reset on exit (AC-11, EC-7).
- [ ] A prohibition cleared in the request step of a tick lets that tick's check fire (EC-8).
- [ ] A miss calls neither `DamageCalculator.Calculate` nor health (AC-HD-12, AC-HD-13).
- [ ] `IHitResultEmitter.Emit` runs once per hit and once per miss streak; a repeat of the same miss is not re-sent; a hit, timer reset, target change or deactivation ends the streak (Rule 10 Step 2, AC-HD-11a).
- [ ] A target invalidated on the tick the timer is ready does not fire and does not reset the timer (EC-10).
- [ ] A timer past `CycleTicks` fires one auto and resets to 0 (EC-17); a second evaluation in that tick does nothing (EC-19).

---

## Implementation Notes

- `capturedForward = normalize(targetPos − attackerPos)` on XZ. Attacker and target at the same XZ must not produce NaN; check `HitDetector`'s handling and pin it with a test.
- Positions come through `IZoneEntityStateSource` (`TryGetPlayerState`, `TryGetMobState`) or a narrower interface. Each fire attempt decodes position only for ready players.
- `AttackRange` and `BaseDamage` are read per attempt, not cached. `DamageCalculator.Calculate` takes `int baseDamage`; confirm at `/story-readiness` that the source is `StatID.AttackPower`.
- Add `OUT_OF_RANGE` and `PROHIBITED`. `SetCombatProhibited(entityId, bool)` is public here; Status Effects and Death and Respawn call it later. It is not a deactivation.
- Depend on an interface for damage application, so Story 004 can add the kill path and Story 007 the output, without changing this flow. A rejected calculation (`FinalDamage` 0) applies nothing.
- A `DamageResult.IsKill` result stops at a seam in this story; Story 004 fills it.
- Keep the streak's last `HitResult` in the per-player state.

---

## Out of Scope

- Story 004: kill sequence
- Story 005: `NotifySkillUsed`
- Story 006: AC-16 in the running composition
- Story 007: `DamageEvent` and `SelfDamageEvent`

---

## QA Test Cases

Use recording fakes for the emitter and damage; real `HitDetector`.

- **AC-09 / AC-20**: distance 3.5, 60 ticks past ready → 0 damage; move to 2.0 → damage on that tick; timer 0.
- **AC-10**: Y delta 5.0, XZ 2.5 → damage.
- **F-2 boundary**: distance exactly 3.0 hits.
- **AC-11**: prohibit for 60 ticks, clear → damage on the next tick, not 20 ticks later.
- **EC-8**: clear in the same tick before `Run` → fires on that tick.
- **AC-HD-12 / AC-HD-13**: miss → `Calculate` and health never called.
- **Miss streak**: 30 out-of-range ticks → 1 `Emit`. A hit, a reset, a new target or a toggle-OFF then the same miss → 1 more `Emit`.
- **Hits**: every hit emits, including two hits in a row.
- **EC-10**: target invalid on the ready tick → no `Emit`, no damage, timer unchanged.
- **EC-19**: `Run` twice in one tick → one damage call.
- **Rejected damage**: `DamageResult.Rejected` → health untouched, timer still resets (Rule 10 Step 4).
- **Allocation**: firing and missing inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_Resolution_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002; Hit Detection Stories 001, 004 (Complete)
- Unlocks: Stories 004, 005, 006
