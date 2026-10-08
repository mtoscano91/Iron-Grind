# Story 005: Kill Sequence Against Real Character Stats

> **Epic**: Damage Calculation
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-07
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/damage-calculation.md` — Core Rule 4 (caller sequence on a kill), Step 10 "Caller responsibility on kill", Edge Cases (`OnEntityDied` subscriber reading `CurrentHP`), Dependencies → Auto-Attack Combat row. `design/gdd/character-stats.md` EC-08.
**Requirement**: `TR-dmg-006` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: none (design-only, LOW risk).
**ADR gate not met (deviation, user decision 2026-10-07)**: same as Story 001; tracked in Story 006.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. `CharacterStats` forbids write calls from inside an event handler (it throws in the Editor); the subscriber in these tests only reads.

**Control Manifest Rules (Core layer) and coding standards**:
- Required: event subscriptions use named methods, never lambda captures (ADR-010)
- Required: integration tests set up and tear down their own state

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story. No caller exists yet (Auto-Attack Combat and the Skill System are not built), so the test plays the caller and executes the sequence the GDD prescribes.*

- [ ] **AC-DC-I-02**: after `Calculate` returns `IsKill = true` and the caller applies `CharacterStats.ApplyDamage(TargetID, FinalDamage)`, an `OnEntityDied` subscriber that reads the target's current HP inside the handler observes `0.0`.
- [ ] **AC-DC-I-02b**: with a sequencing recorder, the order is [XP award obtained] → [`AddExperience` applied] → [`ApplyDamage` called] → [`OnEntityDied` fires]. `OnEntityDied` does not fire before the caller calls `ApplyDamage`.
- [ ] **AC-DC-I-06 (caller half)**: `BaseDamage = 500`, `Defense = 0`, no elemental weapon, `CurrentHP = 100.0` → the resolver returns `IsKill = true`, `FinalDamage = 500` with no side effect; when the caller then runs XP award → `AddExperience(AttackerID, xp)` → `ApplyDamage`, `OnEntityDied(TargetID)` fires exactly once.
- [ ] **Second hit on the dead target**: a further `Calculate` on the same target after the sequence returns `IsKill = false` (dead-entity guard) and a repeated `ApplyDamage` does not fire `OnEntityDied` again.

---

## Implementation Notes

- **From Story 001 (2026-10-07)**: `DamageCalculationConfig` takes four arguments (the fourth is `maxBaseDamage`); use `DamageCalculationConfig.Default` unless a test needs other values.
- **No production code is expected.** This story is a contract test over `DamageCalculator` (Stories 001, 002, 004) and the existing `CharacterStats`. If a criterion cannot pass without changing production code, stop and report it instead of changing `CharacterStats`.
- **The caller in the test**: a small private helper in the fixture that does what GDD Rule 4 prescribes — if `result.IsKill`: obtain the XP amount from a fake XP source, call `stats.AddExperience(attackerId, xp)`, then `stats.ApplyDamage(targetId, result.FinalDamage)`. It is test code; it is not the Auto-Attack Combat implementation.
- **XP source**: `LevelingSystem.GetXPAward` is caller-owned (OQ-DC-3). Use a fake that returns a fixed amount and records its call in the sequence list. Do not add a Leveling dependency to `DamageCalculator`.
- **Recording order**: the fake XP source, the stat-changed subscription for `StatID.Experience` (or a direct read after `AddExperience`), the helper's `ApplyDamage` step and the `OnEntityDied` handler each append to one list.
- **Leveling service**: `CharacterStats` takes an `ILevelingService`; use the same test double the existing `tests/EditMode/CharacterStats/` fixtures use.

---

## Out of Scope

- Auto-Attack Combat and Skill System implementations of the kill sequence; party XP (Rule 4a).
- `GetXPAward` returning 0, a negative value, or throwing (GDD Edge Cases — caller behaviour, tested with the callers).
- The double-kill race (OQ-DC-4).

---

## QA Test Cases

**File**: `tests/EditMode/Integration/DamageCalculation/DamageCalculation_KillSequence_integration_tests.cs` (new).

- **AC-DC-I-02** — Given target HP 100.0, `Defense = 0`, a named `OnEntityDied` handler that stores `stats.GetCurrentHP(target)`; When base 500 and the caller helper runs; Then the stored value is `0f`.
- **AC-DC-I-02b** — same setup with the sequence list; Then the list is exactly `xpAward, addExperience, applyDamage, entityDied`, and it holds no `entityDied` entry at the moment `Calculate` returns.
- **AC-DC-I-06 (caller half)** — Given the same setup and a fixed XP amount from the fake; Then `result.IsKill`, `result.FinalDamage == 500`, died count 0 before the helper and exactly 1 after, the attacker's `Experience` rose by the fake's amount.
- **Second hit** — after the sequence (declare `LogAssert.Expect` for the dead-entity dev error); When `Calculate` again and `ApplyDamage` again; Then `IsKill == false`, died count still 1.
- **Non-kill** — Given HP 1000.0; When base 500 and the helper runs; Then no XP source call, HP is 500.0, died count 0.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/DamageCalculation/DamageCalculation_KillSequence_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004.
- Unlocks: None inside this epic. Gives Auto-Attack Combat a tested reference for its kill sequence.
