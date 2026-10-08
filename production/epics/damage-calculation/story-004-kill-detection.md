# Story 004: Kill Detection and Dead-Entity Guard

> **Epic**: Damage Calculation
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/damage-calculation.md` — Resolution Sequence Step 10, Core Rule 4 (kill credit), Edge Cases (target `CurrentHP = 0.0`), Calibration Table. `design/gdd/character-stats.md` EC-08 (`ApplyDamage` fires `OnEntityDied`).
**Requirement**: `TR-dmg-006` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: none (design-only, LOW risk).
**ADR gate not met (deviation, user decision 2026-10-07)**: same as Story 001 — built in `IronGrind.Foundation` without the server/client assembly ADR; tracked in Story 006.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#.

**Control Manifest Rules (Core layer) and coding standards**:
- Forbidden: the resolver never calls `ApplyDamage`, `AddExperience` or any XP source and fires no event — it reports `IsKill` and returns (TR-dmg-006)
- Guardrail: no heap allocation per call

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story. Every case uses `CritChance = 0.0`.*

- [x] **AC-DC-K-01**: `CurrentHP = 100.0`, `FinalDamage = 100` → `IsKill = true`; the resolver does not call `AddExperience` and `OnEntityDied` is not fired.
- [x] **AC-DC-K-02**: `CurrentHP = 101.0`, `FinalDamage = 100` → `IsKill = false`.
- [x] **AC-DC-K-03**: in a kill scenario the target's `CurrentHP` and the attacker's `Experience` are unchanged after the call and `OnEntityDied` is not fired.
- [x] **AC-DC-K-04**: `CurrentHP = 50.0`, `FinalDamage = 50` → `IsKill = true` (`>=` at equality).
- [x] **AC-DC-K-05**: `CurrentHP = 100.5`, `FinalDamage = 100` → `IsKill = false`.
- [x] **AC-DC-K-06**: `CurrentHP = 0.0` → `IsKill = false` (dead-entity guard) and a dev error is logged.
- [x] **AC-DC-K-07**: `CurrentHP = 0.1`, `FinalDamage = 1` → `IsKill = true`.
- [x] **AC-DC-I-03**: `BaseDamage = 768`, `Defense = 394`, `MagicDefense = 8`, `ElementalBonus = 50`, default config, target alive above 422 HP → `{ PhysicalDamage: 374, ElementalDamage: 48, FinalDamage: 422, IsCrit: false, IsKill: false }`.
- [x] **AC-DC-I-05**: `BaseDamage = 768`, `Defense = 900`, no elemental weapon, `CurrentHP = 1000.0` → `FinalDamage = 38`, `IsKill = false`.
- [x] **AC-DC-I-06 (resolver half)**: `BaseDamage = 500`, `Defense = 0`, no elemental weapon, `CurrentHP = 100.0` → `IsKill = true`, `FinalDamage = 500`; the resolver does not call `AddExperience` and `OnEntityDied` is not fired.

---

## Implementation Notes

- **From Story 001 (2026-10-07)**: `DamageCalculationConfig` takes four arguments (the fourth is `maxBaseDamage`); rejected calls return `DamageResult.Rejected(context)`.
- **Unknown target (user decision 2026-10-08)**: a target with no stat record keeps `Defense = 0` and its damage is computed; it has no HP record, so `GetCurrentHP` returns 0 and the dead-entity guard applies (`IsKill = false`, dev error). No new `CharacterStats` API. Update `DamageCalculation_Calculate_UnknownTarget_TreatedAsDefenseZero` to expect the dev error and assert `IsKill == false`.
- **Existing fixtures**: the guard logs for any target without an HP record. In `SetUp` of `DamageCalculation_PhysicalMitigation_tests.cs` and `DamageCalculation_Elemental_tests.cs`, give the target `MaxHP` and `CurrentHP` above any damage those fixtures deal, so no existing test logs the error or flips `IsKill`. Tests that set their own HP keep doing so.
- Story 001 side-effect test (`AttackerEqualsTarget_ReturnsZeroAndNoSideEffects`): the call is `Calculate(100, Target, Target, ...)`, so `Target` is the acting entity and `Experience` is correctly set and read on it. Corrected 2026-10-08 (code review): an earlier version of this note asked for the attacker entity, which is not part of that call. The test stats use a leveling stub that treats entities as players, and the entity is below the level cap, so a stray `AddExperience` would change the value.
- **Files**: modified `src/Foundation/DamageCalculation/DamageCalculator.cs`; modified `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` and `DamageCalculation_Elemental_tests.cs` (setup only, plus the two test fixes above).
- **Step 10**, after Step 9 and before the return: read the target's current HP as a float and compare `(float)finalDamage >= currentHp`.
- **Which accessor**: the GDD writes `GetEffectiveStat(TargetID, CurrentHP)` "(float)". In `CharacterStats`, `GetEffectiveStat` returns a floored `int`, which would turn 100.5 into 100 and break AC-DC-K-05. Use `stats.GetCurrentHP(targetId)`, which returns the float pool value. Record this as a GDD wording gap in the completion notes.
- **Dead-entity guard**: `currentHp == 0f` → `IsKill = false` and a dev error (`Debug.LogError` inside `#if UNITY_EDITOR || DEVELOPMENT_BUILD`). `FinalDamage` and the other fields are still computed and returned. `GetCurrentHP` also returns 0 for an entity with no HP record; the same guard applies.
- **No side effects**: the calculator holds no reference to the Leveling System and never writes to `CharacterStats`.
- **Guard paths of Story 001** (`baseDamage <= 0`, attacker equal to target) return before Step 10 with `IsKill = false`.

---

## Out of Scope

- Story 005: the caller's kill sequence (XP → `AddExperience` → `ApplyDamage` → `OnEntityDied`).
- The double-kill race between two attackers in the same tick (OQ-DC-4 — a server tick ordering contract for the callers; needs an ADR before Auto-Attack Combat is implemented).
- Party XP (Rule 4a), `GetXPAward` (Leveling System / callers).

---

## QA Test Cases

**File**: `tests/EditMode/DamageCalculation/DamageCalculation_KillDetection_tests.cs` (new). Real `CharacterStats` with `CurrentHP` set through its API; an `OnEntityDied` subscriber (named method) counting calls. `FinalDamage` values are produced with `Defense = 0` and no weapon unless stated.

- **AC-DC-K-01** — Given target HP 100.0; When base 100; Then `FinalDamage == 100`, `IsKill == true`, died count 0.
- **AC-DC-K-02** — Given HP 101.0; When base 100; Then `IsKill == false`.
- **AC-DC-K-03** — Given HP 100.0 and the attacker's `Experience` noted; When base 500; Then `IsKill == true`, target HP still 100.0, attacker `Experience` unchanged, died count 0.
- **AC-DC-K-04** — Given HP 50.0; When base 50; Then `IsKill == true`.
- **AC-DC-K-05** — Given HP 100.5; When base 100; Then `IsKill == false`.
- **AC-DC-K-06** — Given HP 0.0 (declared `LogAssert.Expect` for the dev error); When base 100; Then `IsKill == false`, `FinalDamage == 100`. Edge: an entity that never had HP set behaves the same.
- **AC-DC-K-07** — Given HP 0.1, target `Defense = 1`; When base 1; Then `FinalDamage == 1`, `IsKill == true`.
- **AC-DC-I-03** — Given `Defense = 394`, `MagicDefense = 8`, Fire 50 at +0, HP 1000.0; When base 768; Then all five fields as listed.
- **AC-DC-I-05** — Given `Defense = 900`, HP 1000.0; When base 768; Then `FinalDamage == 38`, `IsKill == false`.
- **AC-DC-I-06 (resolver half)** — Given `Defense = 0`, HP 100.0; When base 500; Then `IsKill == true`, `FinalDamage == 500`, died count 0, attacker `Experience` unchanged.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DamageCalculation/DamageCalculation_KillDetection_tests.cs` — must exist and pass.

**Status**: [x] Created and passing (17 cases)

---

## Dependencies

- Depends on: Story 001, Story 002 (AC-DC-I-03 needs the elemental path).
- Unlocks: Story 005.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 10/10 passing (none deferred). Full EditMode suite in Unity batch mode: 1987 / 1987.
**Deviations** (all advisory):
- Assembly ADR gate not met: the code is in `IronGrind.Foundation` by user decision (2026-10-07). Tracked in Story 006.
- GDD wording gap: Step 10 and AC-DC-K-06 write `GetEffectiveStat(TargetID, CurrentHP)` "(float)"; in `CharacterStats` that accessor returns a floored int. The code reads `CharacterStats.GetCurrentHP` (the float pool value), as this story directs. Correct the GDD wording when `damage-calculation.md` is next edited.
- The dead-entity guard is `!(currentHp > 0f)`, wider than the story's `== 0f`: HP is clamped to [0, MaxHP] so negative values cannot occur, and a NaN HP (which `SetCurrentHP` does not reject) also takes the guard path — `IsKill = false`, dev error. Pinned by a test.
- Unknown target (user decision 2026-10-08): no HP record reads as 0, so the guard applies; `Defense = 0` and the damage is still computed. No new `CharacterStats` API.
- The guard's dev error is logged on every such call (editor and development builds only), not rate-limited.
- Step 10 is a private helper, `ResolveIsKill`; most of the `Calculate` summary moved to `<remarks>`.
- Existing fixtures: the Story 001 and Story 002 fixtures give the target 99999 MaxHP and HP in `SetUp`. Story 001 tests changed beyond setup, none weakened: `UnknownTarget_TreatedAsDefenseZero` (expects the guard error, asserts `IsKill == false`); `NormalHit_ElementalAndFlagsAreInactive` (target HP 1000 instead of 100, since 374 damage on 100 HP is now a kill); the two base-damage ceiling tests (assert `IsKill == true`); `AttackerEqualsTarget_ReturnsZeroAndNoSideEffects` (entity set to Level 1 so its Experience assertion can fail).
- Extra file beyond the story's list: `tests/EditMode/DamageCalculation/DamageCalculationTestFakes.cs` gained `AllPlayersLevelingService`. The Kill Detection and Story 001 fixtures build their stats with it, because the default fixture stub makes `AddExperience` a no-op (TD-062).
- `TR-dmg-006` is a placeholder (registry empty).
**For Story 003** (from code review): put Steps 1 and 8 in a private `ResolveCrit(..., out bool isCrit)` between the pre-crit sum and the floor, with no allocation; the crit roll then happens before the dead-entity guard, so a call on a dead target still consumes a roll.
**Test Evidence**: Logic — `tests/EditMode/DamageCalculation/DamageCalculation_KillDetection_tests.cs` (17 cases; 7 beyond the story's QA list: two fixture controls, NaN HP, elemental damage completing a kill, rejected call on a dead target, dead-target call leaves HP and events untouched, target with no HP record).
**Code Review**: Complete — CHANGES REQUIRED (unity-specialist, qa-tester; 3 required, 5 suggestions); all 8 applied. Director gates QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (Lean mode).
**Tech debt logged**: TD-062.
