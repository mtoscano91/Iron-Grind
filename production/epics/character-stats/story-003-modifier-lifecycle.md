# Story 003: Modifier Lifecycle — AddBuffModifier, AddEquipmentModifier, Remove, Idempotency

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-001`, `TR-stats-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the modifier add/remove APIs. IL2CPP-safe type constraints (BuffID as `enum : uint`, ItemID as `readonly struct`) are specified in GDD Rule 7 and implemented in Story 001.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Modifier entries must be `readonly struct` (not `class`). Fixed-capacity arrays — no `List<T>`, no LINQ. On capacity overflow: log error in dev builds, return without adding. This is a programming error — raise capacity in code.

**Control Manifest Rules (Foundation layer)**:
- Required: Modifier entries as `readonly struct` — zero heap allocation beyond the array itself
- Forbidden: Never use `List<T>` for modifier collections — fixed-capacity arrays only
- Guardrail: Equipment modifier cap 16 entries; buff modifier cap 32 entries per stat — raise in code, not at runtime

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-16** [BLOCKING]: BaseStat(AP)=150, buff WarriorCry +50 flat → EffectiveStat=200. `RemoveBuffModifier(WarriorCry)` → `GetEffectiveStat(AP)` = **150**. F-1 recomputes from scratch — no undo-delta stored.
- [ ] **AC-17** [BLOCKING]: BaseStat(AP)=100, `AddBuffModifier(WarriorCry, +30 flat, 20 ticks)` → 130. `AddBuffModifier(WarriorCry, +30 flat, 40 ticks)` again → `GetEffectiveStat(AP)` = **130** (not 160 — no double-stack; exactly one modifier entry for WarriorCry). *Note: duration-refresh assertion ("40 ticks") requires a tick driver not available in EditMode — deferred to Status Effects integration test.*
- [ ] **AC-18** [BLOCKING]: BaseStat(AP)=100, `AddEquipmentModifier(Sword01, +40 flat)` → 140. `AddEquipmentModifier(Sword01, +40 flat)` again → `GetEffectiveStat(AP)` = **140** (not 180 — single entry for Sword01).
- [ ] **AC-19** [BLOCKING]: No modifier `RingOfSpeed` registered. `RemoveEquipmentModifier(MovementSpeed, RingOfSpeed)` → no exception; `GetEffectiveStat(MovementSpeed)` unchanged.
- [ ] **AC-20** [BLOCKING]: `AddEquipmentModifier(Defense, +80 flat, +0.12 pct, HeavyArmor)` → `floor((50+80)×1.12)` = **145**. `RemoveEquipmentModifier(HeavyArmor)` → `GetEffectiveStat(Defense)` = **50** (both flat and pct removed atomically). A return of 56 (pct persisting) or 130 (flat persisting) is failure.
- [ ] **AC-22** [ADVISORY]: Mob entity, BaseStat(AP)=80, `AddBuffModifier(poison, −20 flat)` → `GetEffectiveStat(MobEntityID, AP)` = **60** (F-1 applies identically to mobs).
- [ ] **[NEW] Equipment capacity overflow** [BLOCKING]: With 16 equipment modifiers registered (capacity full), attempt to `AddEquipmentModifier` for a 17th. Dev build: logs error and returns without adding. `GetEffectiveStat` reflects only the 16 existing modifiers; no exception thrown.

---

## Implementation Notes

*Derived from GDD Rules 5, 7, 9 and Edge Cases EC-11–EC-26:*

**`AddBuffModifier`**: If a modifier with the same `BuffID` already exists, **overwrite** values and refresh `DurationTicks` (EC-13). Do not create a second entry. Array capacity: 32 entries per stat.

**`AddEquipmentModifier`**: If a modifier with the same `ItemID` already exists, **overwrite** with new flat/pct values (EC-25). Single entry per ID — no double-counting.

**`RemoveBuffModifier` / `RemoveEquipmentModifier`**: If the ID is not found, return without modifying the list and without throwing (EC-26). Remove is idempotent — safe to call from death cleanup or zone transition without guarding for double-removes.

**Atomic removal**: Each modifier entry stores both flat and pct contributions as a single struct. Removing the entry removes both contributions in one operation (EC-17, EC-12).

**Write-locked stats**: `AddBuffModifier` must reject `StatID.CurrentHP`, `StatID.CurrentMP`, `StatID.Level`, `StatID.Experience` (EC-15). Return an error; no modifier created.

**No cached intermediates**: `GetEffectiveStat` recomputes F-1 from scratch on every call (EC-11). There is no intermediate to invalidate.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001**: Container, `GetBaseStat`, `SetBaseStat`
- **Story 002**: `GetEffectiveStat` — must be Done before this story starts
- **Story 004**: `ApplyDamage`, `ApplyRegen`, `ConsumeMana`
- **Story 005**: `OnStatChanged` event firing on modifier add/remove — stub the call here if needed; wire in Story 005
- **AC-17 duration-refresh assertion**: "duration refreshes to 40 ticks" — deferred to Status Effects integration test (requires a tick driver not present in EditMode)

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs`

- **AC-16**: Buff removal recomputes effective stat from scratch (no undo-delta)
  - Given: BaseStat(AP)=150; `AddBuffModifier(AP, +50 flat, 0 pct, 10 ticks, WarriorCry)` → EffectiveStat=200
  - When: `RemoveBuffModifier(EntityID, AP, BuffID.WarriorCry)`, then `GetEffectiveStat(AP)`
  - Then: Returns 150; BaseStat unchanged.
  - Edge cases: Add then remove multiple buffs, leaving one — verify only that buff's contribution remains.

- **AC-17**: Duplicate BuffID does not double-stack (no-double-stack assertion only)
  - Given: BaseStat(AP)=100; `AddBuffModifier(AP, +30 flat, 0 pct, 20 ticks, WarriorCry)` → 130
  - When: `AddBuffModifier(AP, +30 flat, 0 pct, 40 ticks, WarriorCry)` again
  - Then: `GetEffectiveStat(AP)` = 130 (not 160). Exactly one modifier entry for WarriorCry.
  - Edge cases: Add WarriorCry three times in a row; EffectiveStat must remain 130 every time.

- **AC-18**: Duplicate equipment ItemID overwrites, does not double-count
  - Given: BaseStat(AP)=100; `AddEquipmentModifier(AP, +40 flat, 0 pct, Sword01)` → 140
  - When: `AddEquipmentModifier(AP, +40 flat, 0 pct, Sword01)` again
  - Then: `GetEffectiveStat(AP)` = 140 (not 180).
  - Edge cases: Overwrite with different value: `AddEquip Sword01 +60 flat` → EffectiveStat = 160 (new value replaces old).

- **AC-19**: Remove non-existent modifier ID is a no-op
  - Given: BaseStat(MovementSpeed)=5.0f; no modifier for `RingOfSpeed` registered
  - When: `RemoveEquipmentModifier(EntityID, StatID.MovementSpeed, ItemID.RingOfSpeed)`
  - Then: No exception; `GetEffectiveStat(MovementSpeed)` = 5.0f.
  - Edge cases: Call Remove twice for same non-existent ID — both calls are no-ops.

- **AC-20**: Equipment with flat AND pct removes both contributions atomically
  - Given: BaseStat(Defense)=50; `AddEquipmentModifier(Defense, +80 flat, +0.12 pct, HeavyArmor)` → `floor((50+80)×1.12)` = `floor(145.6)` = 145
  - When: `RemoveEquipmentModifier(EntityID, StatID.Defense, HeavyArmor)`
  - Then: `GetEffectiveStat(Defense)` = 50. A return of 56 (pct persisting) or 130 (flat persisting) is failure.
  - Edge cases: Remove then re-add — must return to 145 on re-add. Note: `floor(145.6)=145` also validates `FloorToInt` vs `RoundToInt`.

- **AC-22**: Buff modifier on mob entity applies F-1 identically to player
  - Given: Mob EntityID; `SetBaseStat(MobEntityID, AP, 80)`; `AddBuffModifier(AP, −20 flat debuff, 0 pct, 10 ticks, BuffID.Poison)`
  - When: `GetEffectiveStat(MobEntityID, StatID.AttackPower)`
  - Then: Returns 60. (80−20)×1.0×1.0 = 60, clamped to [1, 9999].
  - Edge cases: Debuff reducing mob AP below 1 → clamped to 1 (StatMin applies to mobs too).

- **[NEW] Equipment capacity overflow**: 17th modifier rejected
  - Given: Entity with 16 equipment modifiers registered across any stats (capacity full)
  - When: `AddEquipmentModifier` for a 17th entry
  - Then: Dev build logs error and returns without adding. `GetEffectiveStat` reflects only the 16 existing modifiers. No exception thrown.
  - Edge cases: Verify the 16th slot WAS successfully added (capacity is 16, not 15).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` — must exist and pass

**Status**: [x] `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` — 8 test functions

---

## Dependencies

- Depends on: Story 001 (container) and Story 002 (GetEffectiveStat) must be Done
- Unlocks: Story 004, Story 005

---

## Completion Notes
**Completed**: 2026-06-29
**Criteria**: 7/7 passing (AC-16, AC-17, AC-18, AC-19, AC-20, AC-22, capacity overflow) + AC-16 edge case added during code review
**Deviations**:
- ADVISORY: TR-stats-001 / TR-stats-002 not yet in tr-registry.yaml (pre-existing infrastructure gap)
- ADVISORY: EquipmentModifierCapacity=16 and BuffModifierCapacity=32 are code constants by design (story and manifest specify "raise in code, not at runtime")
**Scope**: CharacterStatsFixture.cs updated out-of-scope (valid — required by B-02 IL2CPP boxing fix migrating inner Dictionary<StatID,T> to fixed-size T[][] arrays)
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` (8 tests)
**Code Review**: Complete — `/code-review` run this session; B-02 (IL2CPP boxing) and missing AC-16 edge case both fixed before close
