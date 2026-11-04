# Story 008: Effect Application (Buffs, Debuffs, Instant Heal)

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours (3 + 1 for the execution-outcome seam, readiness 2026-11-04)

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-7, F-SK-3, EC-SK-4, EC-SK-9; `design/gdd/status-effects.md` — CR-SE-14
**Requirement**: `TR-sk-006`

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: the Skill System calls Status Effects directly; it is the sole external caller of `ApplyEffect` and `ApplyInstantHeal` (CR-SE-14) and assembles the target list itself.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces
- Forbidden: the Skill System calling `AddBuffModifier` directly (AC-SK-28)

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [x] **CR-SK-7**: a skill with `ApplyBuff` or `ApplyDebuff` calls `StatusEffects.ApplyEffect(target, caster, def.BuffDefinition)` once per target.
- [x] **AC-SK-26**: for a damage + buff/debuff skill, `ApplyEffect` is called only after `DamageCalculation` returns.
- [x] **AC-SK-27 / EC-SK-4**: `ApplyEffect` is not called on a target with `IsKill == true` from this cast.
- [x] **AC-SK-29 / F-SK-3**: `ApplyInstantHeal(target, floor(INT × HealMultiplier))`; INT 246 × 2.0 → 492.
- [x] **AC-SK-28**: no file in `src/ServerLogic/SkillSystem/` calls `AddBuffModifier` (text-search test).
- [x] **AC-SK-41 (effect part)**: a `Self` buff resolves on the caster: `ApplyEffect(caster, caster, ...)`.
- [x] **EC-SK-9**: when `ApplyEffect` returns a capacity-exceeded result, mana and cooldown stay paid, no retry, and the cast is still accepted (its result carries any damage dealt, no buff).
- [x] **EC-SK-4**: a target that another entity killed earlier this tick gets `ApplyEffect`'s own rejection (`InvalidTarget`); the cast stays accepted.
- [x] A skill with both a damage flag and `InstantHeal` (none at MVP, but the flags allow it) runs damage first, then the heal.
- [x] **Miss (CR-SK-7)**: a damage + buff/debuff skill whose damage misses (`DamageDealt == 0`, `IsKill == false`) still calls `ApplyEffect` once. Only `IsKill` skips a target.
- [x] **Execution-outcome seam (readiness 2026-11-04)**: `SkillExecutionOutcome` is a mutable struct `{ int DamageDealt, bool IsCrit, bool IsKill, int HealAmount }`, zeroed per cast. `ISkillExecutionStep.Execute(in ValidatedSkillCast cast, ref SkillExecutionOutcome outcome)` replaces the current signature. The Story 007 damage step fills `DamageDealt` / `IsCrit` / `IsKill`, and the effects step reads `IsKill` and writes `HealAmount`. A composite step runs damage, then effects.
- [x] `ISkillCastOutput.SendAccepted(caster, in request, newCooldownTicks, in SkillExecutionOutcome outcome)`: `SkillCastExecutor` passes the filled outcome, so `HealAmount` (and the damage fields) reach Story 009's `SkillCastResult` (`healAmount`, `damageDealt`, crit, kill). A cast with no execution step sends a zeroed outcome.

---

## Implementation Notes

- **APIs** (`src/ServerLogic/StatusEffects/StatusEffectsService.cs`): `ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition)` (line 548) and `ApplyResult ApplyInstantHeal(EntityID entity, float amount)` (line 922). `ApplyInstantHeal` returns a result, not fire-and-forget as the GDD table says; read it but don't reject the cast on it. Seam: `ISkillEffectSink` with both methods, an adapter over `StatusEffectsService` and a recording fake.
- **Target list**: single target at MVP (no AoE, CR-SK-17), so a list of one. Keep the shape as a loop over a pre-allocated span or array of one, so AoE later is a list change and not a rewrite. Allocate nothing.
- **Heal**: `floor(INT × HealMultiplier)` as an `int` (same `double` floor as Story 007); pass it as a float.
- **Prayer (HoT)**: an `ApplyBuff` skill; all heal parameters live in its `BuffDefinition` (GDD Prayer note). Nothing special here.
- **`ApplyResult` values**: check `ApplyResult.cs` for the capacity-exceeded and invalid-target members, and use their exact names in the tests.
- **Outcome seam (readiness 2026-11-04)**: neither `ISkillExecutionStep.Execute` (returns `void`) nor `ISkillCastOutput.SendAccepted` (cooldown only) carries execution results today, and a separate effects step could not see `IsKill`. Add `SkillExecutionOutcome` (`src/ServerLogic/SkillSystem/`), change both signatures as in the criteria, and add a composite `ISkillExecutionStep` (damage step, then effects step) so Story 011 wires one step. Update every existing implementer and fake: `SkillDamageExecutionStep`, `RecordingExecution` and the `ISkillCastOutput` fakes in the Story 005/006/007 test files. The zeroed outcome is a stack local in `SkillCastExecutor.Accept`, so nothing is allocated. On the Decision E throw path nothing is sent, as before.
- **AC-SK-28 test**: read every `*.cs` under `src/ServerLogic/SkillSystem/` and assert no `AddBuffModifier(`. File I/O in a test is acceptable here (the GDD says static check); keep it in its own test class.

---

## Out of Scope

- Story 012: the BuffIDs and BuffDefinitions of the roster
- Buff icons, buff tray: Combat UI / HUD

---

## QA Test Cases

- **CR-SK-7**: a debuff skill on a mob → one `ApplyEffect(mob, caster, def)` with the authored definition (compare by `BuffID`).
- **AC-SK-26**: a damage + debuff skill → `Calculate` recorded before `ApplyEffect`.
- **AC-SK-27**: a fake calculator returns `IsKill` → no `ApplyEffect`; damage still applied.
- **AC-SK-29**: INT 246, multiplier 2.0 → 492; INT 1 × 0.5 → 0 (floor), still called.
- **AC-SK-28**: text search → zero matches.
- **AC-SK-41**: `Self` buff with submitted target 0 → `ApplyEffect(caster, caster, ...)`.
- **EC-SK-9**: fake returns capacity exceeded → cast accepted, mana and cooldown paid, `ApplyEffect` called once.
- **EC-SK-4**: fake returns invalid target → cast accepted.
- **Miss**: a damage + debuff skill, the fake calculator returns `DamageDealt == 0` and no kill → `ApplyEffect` called once.
- **Outcome**: a heal skill (INT 246 × 2.0) → `SendAccepted` receives `HealAmount == 492`. A damage skill → `SendAccepted` receives the calculator's `DamageDealt` / `IsCrit` / `IsKill`. No execution step → zeroed outcome.
- **Non-buff skill**: a pure damage skill → zero `ApplyEffect`, zero `ApplyInstantHeal`.
- **No allocation**: a buff cast inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Effects_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-04 (`SkillSystem_Effects_tests.cs` 35 cases + `SkillSystem_NoAddBuffModifier_tests.cs` 2; after code-review fixes EditMode 6282 / 6153 / 0 failed / 129 skipped)

---

## Dependencies

- Depends on: Story 007
- Unlocks: Story 011; Story 012 (buff data)

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 12/12 passing, none deferred
**Deviations** (advisory, no tech debt):
- Heal semantics decided at `/code-review` (the GDD is silent): `SkillExecutionOutcome.HealAmount` is the HP delivered, written only when `ApplyInstantHeal` returns `Success` (0 for a rejected heal: dead or unknown target, amount 0), and a target this cast killed gets no heal as well as no buff. Recorded in `SkillEffectExecutionStep` remarks, the `SkillExecutionOutcome` doc and the Story 009 carry note; add one line to `design/gdd/skill-system.md` CR-SK-7 / CR-SK-9 when Story 009 or 012 next touches it.
- `ApplyInstantHeal` returns an `ApplyResult` (the GDD interface table says fire-and-forget); the step reads it only to decide the reported heal, never to reject the cast.
- The step is stateless with a single target (no target array); AoE (post-MVP) adds the target list and per-target kill info.
- Not wired in production yet: Story 011 connects `StatusEffectsSkillEffectSink` and `CompositeSkillExecutionStep` to `SkillCastExecutor`.
**Test Evidence**: Logic: `tests/EditMode/SkillSystem/SkillSystem_Effects_tests.cs` (35 cases) + `SkillSystem_NoAddBuffModifier_tests.cs` (2, file I/O by design), plus outcome tests in `SkillSystem_Execution_tests.cs`; full EditMode 6282 / 6153 / 0 failed / 129 skipped, started 14:09:19Z after the last code edit.
**Code Review**: Complete (`/code-review` 2026-11-04: CHANGES REQUIRED → "fix all" applied; QL-TEST-COVERAGE and LP-CODE-REVIEW skipped, lean)
