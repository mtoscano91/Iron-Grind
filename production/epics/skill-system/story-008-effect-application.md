# Story 008: Effect Application (Buffs, Debuffs, Instant Heal)

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

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

- [ ] **CR-SK-7**: a skill with `ApplyBuff` or `ApplyDebuff` calls `StatusEffects.ApplyEffect(target, caster, def.BuffDefinition)` once per target.
- [ ] **AC-SK-26**: for a damage + buff/debuff skill, `ApplyEffect` is called only after `DamageCalculation` returns.
- [ ] **AC-SK-27 / EC-SK-4**: `ApplyEffect` is not called on a target with `IsKill == true` from this cast.
- [ ] **AC-SK-29 / F-SK-3**: `ApplyInstantHeal(target, floor(INT × HealMultiplier))`; INT 246 × 2.0 → 492.
- [ ] **AC-SK-28**: no file in `src/ServerLogic/SkillSystem/` calls `AddBuffModifier` (text-search test).
- [ ] **AC-SK-41 (effect part)**: a `Self` buff resolves on the caster: `ApplyEffect(caster, caster, ...)`.
- [ ] **EC-SK-9**: when `ApplyEffect` returns a capacity-exceeded result, mana and cooldown stay paid, no retry, and the cast is still accepted (its result carries any damage dealt, no buff).
- [ ] **EC-SK-4**: a target that another entity killed earlier this tick gets `ApplyEffect`'s own rejection (`InvalidTarget`); the cast stays accepted.
- [ ] A skill with both a damage flag and `InstantHeal` (none at MVP, but the flags allow it) runs damage first, then the heal.
- [ ] `HealAmount` is passed to `SkillCastResult` (`healAmount`) via the cast-output seam.

---

## Implementation Notes

- **APIs** (`src/ServerLogic/StatusEffects/StatusEffectsService.cs`): `ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition)` (line 548) and `ApplyResult ApplyInstantHeal(EntityID entity, float amount)` (line 922). `ApplyInstantHeal` returns a result, not fire-and-forget as the GDD table says; read it but don't reject the cast on it. Seam: `ISkillEffectSink` with both methods, an adapter over `StatusEffectsService` and a recording fake.
- **Target list**: single target at MVP (no AoE, CR-SK-17), so a list of one. Keep the shape as a loop over a pre-allocated span or array of one, so AoE later is a list change and not a rewrite. Allocate nothing.
- **Heal**: `floor(INT × HealMultiplier)` as an `int` (same `double` floor as Story 007); pass it as a float.
- **Prayer (HoT)**: an `ApplyBuff` skill; all heal parameters live in its `BuffDefinition` (GDD Prayer note). Nothing special here.
- **`ApplyResult` values**: check `ApplyResult.cs` for the capacity-exceeded and invalid-target members, and use their exact names in the tests.
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
- **Non-buff skill**: a pure damage skill → zero `ApplyEffect`, zero `ApplyInstantHeal`.
- **No allocation**: a buff cast inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Effects_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007
- Unlocks: Story 011; Story 012 (buff data)
