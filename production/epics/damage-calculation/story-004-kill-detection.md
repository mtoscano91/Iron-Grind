# Story 004: Kill Detection and Dead-Entity Guard

> **Epic**: Damage Calculation
> **Status**: Ready
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

- [ ] **AC-DC-K-01**: `CurrentHP = 100.0`, `FinalDamage = 100` → `IsKill = true`; the resolver does not call `AddExperience` and `OnEntityDied` is not fired.
- [ ] **AC-DC-K-02**: `CurrentHP = 101.0`, `FinalDamage = 100` → `IsKill = false`.
- [ ] **AC-DC-K-03**: in a kill scenario the target's `CurrentHP` and the attacker's `Experience` are unchanged after the call and `OnEntityDied` is not fired.
- [ ] **AC-DC-K-04**: `CurrentHP = 50.0`, `FinalDamage = 50` → `IsKill = true` (`>=` at equality).
- [ ] **AC-DC-K-05**: `CurrentHP = 100.5`, `FinalDamage = 100` → `IsKill = false`.
- [ ] **AC-DC-K-06**: `CurrentHP = 0.0` → `IsKill = false` (dead-entity guard) and a dev error is logged.
- [ ] **AC-DC-K-07**: `CurrentHP = 0.1`, `FinalDamage = 1` → `IsKill = true`.
- [ ] **AC-DC-I-03**: `BaseDamage = 768`, `Defense = 394`, `MagicDefense = 8`, `ElementalBonus = 50`, default config, target alive above 422 HP → `{ PhysicalDamage: 374, ElementalDamage: 48, FinalDamage: 422, IsCrit: false, IsKill: false }`.
- [ ] **AC-DC-I-05**: `BaseDamage = 768`, `Defense = 900`, no elemental weapon, `CurrentHP = 1000.0` → `FinalDamage = 38`, `IsKill = false`.
- [ ] **AC-DC-I-06 (resolver half)**: `BaseDamage = 500`, `Defense = 0`, no elemental weapon, `CurrentHP = 100.0` → `IsKill = true`, `FinalDamage = 500`; the resolver does not call `AddExperience` and `OnEntityDied` is not fired.

---

## Implementation Notes

- **From Story 001 (2026-10-07)**: `DamageCalculationConfig` takes four arguments (the fourth is `maxBaseDamage`); rejected calls return `DamageResult.Rejected(context)`.
- **Open question carried from Story 001 — decide before implementing**: a target with no stat record is currently treated as `Defense = 0` and takes full damage, with no log (pinned by `DamageCalculation_Calculate_UnknownTarget_TreatedAsDefenseZero`). The GDD is silent. This story adds the dead-entity guard and reads `CurrentHP`; decide here whether an unknown target stays as it is or becomes a rejected call, and update that test accordingly.
- **File**: modified `src/Foundation/DamageCalculation/DamageCalculator.cs`.
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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002 (AC-DC-I-03 needs the elemental path).
- Unlocks: Story 005.
