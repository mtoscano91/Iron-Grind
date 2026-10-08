# Story 002: Elemental Bonus and Mitigation

> **Epic**: Damage Calculation
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/damage-calculation.md` — Resolution Sequence Steps 4–7 and 11, F-DC-2, `DamageResult` field contract (`HasElementalContribution`), Interactions rows for Equipment System, Item Database and Enhancement System, Edge Cases (`ItemID.Invalid`, `MagicDefense = 9999`, Fire weapon with `ElementalBonus = 0`). `design/gdd/enhancement-system.md` F-ENH-2.
**Requirement**: `TR-dmg-004` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: none (design-only, LOW risk).
**ADR gate not met (deviation, user decision 2026-10-07)**: same as Story 001 — built in `IronGrind.Foundation` without the server/client assembly ADR; tracked in Story 006.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; `Mathf.FloorToInt` only. Float arithmetic throughout; truncate only when writing `ElementalDamage` and `FinalDamage`.

**Control Manifest Rules (Core layer) and coding standards**:
- Required: dependencies injected by constructor, behind interfaces where one exists (`IItemDatabase`, `IEnhancementBonusProvider`)
- Required: `K_MAGIC` and `MIN_ELEMENTAL_FRACTION` come from `DamageCalculationConfig`
- Guardrail: no heap allocation per call

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story. Every case uses `CritChance = 0.0`.*

- [x] **AC-DC-F-04**: `ElementalBonus = 50`, `MagicDefense = 8`, `K_MAGIC = 200` → `ElementalDamage = 48`.
- [x] **AC-DC-F-05**: `ElementalBonus = 100`, `MagicDefense = 9999` → `ElementalDamage = 10` (`MIN_ELEMENTAL_FRACTION` floor — never 0).
- [x] **AC-DC-F-06**: the weapon query returns `ItemID.Invalid` → `ElementalDamage = 0`, `HasElementalContribution = false`, and `IItemDatabase.GetItem` / `TryGetItem` is not invoked.
- [x] **AC-DC-F-08**: `BaseDamage = 768`, `Defense = 394`, `ElementalBonus = 50`, `MagicDefense = 8` → `IsCrit = false`, `FinalDamage = 422`.
- [x] **AC-DC-F-12**: same inputs → `PhysicalDamage = 374`, `ElementalDamage = 48`, `FinalDamage = 422`.
- [x] **AC-DC-F-12b**: with `PhysicalMitigated = 374.6` and `ElementalMitigated = 48.6` → `FinalDamage = 423` while `PhysicalDamage + ElementalDamage = 422`.
- [x] **AC-DC-E-04**: a weapon with `ElementType = Fire` and `ElementalBonus = 0` → `ElementalDamage = 0`, `HasElementalContribution = false`, no error.
- [x] **AC-DC-E-06**: `ElementType = Fire`, `ElementalBonus = 9`, `MagicDefense = 9999` → `ElementalDamage = 0` and `HasElementalContribution = true`.
- [x] **Non-elemental weapon**: a weapon with `ElementType.None` → `ElementalDamage = 0`, `HasElementalContribution = false`; neither `MagicDefense` nor the bonus provider is consulted.
- [x] **Bonus provider contract**: for an elemental weapon the provider is called once with `(level from the weapon query, base ElementalDamage and GearTier from the Item Database, isWeapon: true)` and its return value is the `ElementalBonus` of F-DC-2.
- [x] **Physical unaffected**: with no weapon equipped, the Story 001 results are unchanged.

---

## Implementation Notes

- **From Story 001 (2026-10-07)**: `DamageCalculationConfig` takes four arguments — `(minDamageFraction, kMagic, minElementalFraction, maxBaseDamage)`; `Default` uses 99999 for the last. Rejected calls return `DamageResult.Rejected(context)`; build the normal result with named arguments. The test fixture has `AssertRejected` and named config constants to reuse.
- **Files**: new `src/Foundation/DamageCalculation/IEquippedWeaponQuery.cs`; modified `DamageCalculator.cs`.
- **`IEquippedWeaponQuery` (seam; the Equipment System is not built)**: the two methods the GDD names for the Equipment System — `ItemID GetEquippedWeaponID(EntityID entityId)` (returns `ItemID.Invalid` when no weapon is equipped) and `byte GetEquippedWeaponEnhancementLevel(EntityID entityId)`. It is declared here so the resolver does not wait for the Equipment epic; the Equipment System implements it later. The interface name is decided at story creation — the GDD writes `EquipmentSystem.GetEquippedWeaponID(...)`.
- **Constructor** becomes `DamageCalculator(CharacterStats stats, IEquippedWeaponQuery weapons, IItemDatabase items, IEnhancementBonusProvider bonuses, DamageCalculationConfig config)`; null checks for the three new parameters. Update the Story 001 fixture's factory method.
- **Step 4**: `weapons.GetEquippedWeaponID(attackerId)`. If `ItemID.Invalid` → `ElementalBonus = 0`, skip Steps 5–6, do not touch the Item Database. Otherwise read the item's `EquipmentData` (`ElementType`, `ElementalDamage`, `GearTier`). If `ElementType.None` → skip. Otherwise `ElementalBonus = bonuses.GetElementalBonus(weapons.GetEquippedWeaponEnhancementLevel(attackerId), baseElementalDamage, gearTier, isWeapon: true)`.
- **Item not found, or not equipment (GDD is silent; decided at story creation)**: treat as no elemental contribution and log a dev error (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`). Physical damage is still returned.
- **Step 5–6**: `MagicDefense = stats.GetEffectiveStat(targetId, StatID.MagicDefense)`; `ElementalMitigated = ElementalBonus × max(MinElementalFraction, 1 − MagicDefense / (MagicDefense + KMagic))` in float. When `ElementalBonus = 0` do not evaluate (F-DC-2: returns 0.0).
- **Step 7 and 9**: `PreCritDamage = PhysicalMitigated + ElementalMitigated`; `FinalDamage = max(1, FloorToInt(PreCritDamage))` until Story 003 inserts the crit step.
- **Step 11**: `ElementalDamage = FloorToInt(ElementalMitigated)`, `HasElementalContribution = ElementalMitigated > 0f`.

---

## Out of Scope

- Story 003: crit (Steps 1 and 8). Story 004: kill detection (Step 10).
- Any Equipment System implementation of `IEquippedWeaponQuery`; elements other than `Fire`; VFX and audio triggers keyed on `HasElementalContribution` (VAR-3, VAR-6).
- Item Database authoring warning for `ElementType != None && ElementalDamage == 0` (GDD Edge Cases — tooling, not this resolver).

---

## QA Test Cases

**File**: `tests/EditMode/DamageCalculation/DamageCalculation_Elemental_tests.cs` (new). Real `CharacterStats`; a fake `IEquippedWeaponQuery`; a fake `IItemDatabase` that counts calls and returns prepared items (reuse the item-building helpers already used under `tests/EditMode/`); the real `EnhancementBonusProvider(EnhancementConfig.Default)` at enhancement level 0, where the bonus equals the base `ElementalDamage`, except in the contract test, which uses a recording fake provider.

- **AC-DC-F-04** — Given a Fire weapon with base 50 at +0, target `MagicDefense = 8`, `Defense = 0`; When base 100; Then `ElementalDamage == 48`.
- **AC-DC-F-05** — Given base elemental 100, `MagicDefense = 9999`; Then `ElementalDamage == 10`.
- **AC-DC-F-06** — Given the weapon query returns `ItemID.Invalid`; Then `ElementalDamage == 0`, `HasElementalContribution == false`, the fake database's call count is 0.
- **AC-DC-F-08 / F-12** — Given `Defense = 394`, `MagicDefense = 8`, Fire 50; When base 768; Then `PhysicalDamage == 374`, `ElementalDamage == 48`, `FinalDamage == 422`, `IsCrit == false`.
- **AC-DC-F-12b** — Given `Defense = 9999`, `MagicDefense = 9999`, Fire base 486; When base 7492 (`7492 × 0.05 = 374.6`, `486 × 0.10 = 48.6`); Then `FinalDamage == 423`, `PhysicalDamage == 374`, `ElementalDamage == 48`. Confirm the arithmetic in the test's comments before relying on these inputs.
- **AC-DC-E-04** — Given a Fire weapon with base 0; Then `ElementalDamage == 0`, `HasElementalContribution == false`, no log.
- **AC-DC-E-06** — Given Fire base 9, `MagicDefense = 9999`; Then `ElementalDamage == 0` and `HasElementalContribution == true`.
- **Non-elemental weapon** — Given a weapon with `ElementType.None`; Then elemental fields are zero / false and the recording provider was not called.
- **Bonus provider contract** — Given a weapon at level 7, base 50, a known `GearTier`; Then the recording provider received `(7, 50, thatTier, true)` once and its returned value (for example 80) drives `ElementalDamage`.
- **Item missing** — Given a weapon id the database does not know; Then physical damage is returned, elemental is zero, the dev error is logged.
- **Null arguments** — each new constructor parameter null → `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DamageCalculation/DamageCalculation_Elemental_tests.cs` — must exist and pass; the Story 001 test file still passes.

**Status**: [x] Created and passing (22 cases)

---

## Dependencies

- Depends on: Story 001. Enhancement Story 002 (`IEnhancementBonusProvider` — Complete), Item Database epic (Complete).
- Unlocks: Story 003, Story 004.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 11/11 passing (none deferred). Full EditMode suite in Unity batch mode: 1970 / 1970.
**Deviations** (all advisory):
- Assembly ADR gate not met: the code is in `IronGrind.Foundation` by user decision (2026-10-07). Tracked in Story 006.
- "Neither `MagicDefense` nor the bonus provider is consulted" for a non-elemental weapon: the provider and the enhancement-level query are asserted not called; the skipped `MagicDefense` read is true in the code (`ResolveElementalMitigated` returns before the read) but is not observable through the real `CharacterStats`.
- Decided where the GDD is silent, each pinned by a test: a negative value from the bonus provider counts as 0 (no elemental contribution, physical damage not reduced); an exception from the bonus provider (`GearTier.None`, out-of-range level) propagates uncaught; a database that reports an id as found but returns no definition is treated as a missing item; a target with no stat record has `MagicDefense = 0`.
- A weapon id that is missing from the Item Database, or is not equipment, logs a dev error on every hit (editor and development builds only). Not rate-limited.
- The item lookup uses `IItemDatabase.TryGetItem`; "not equipment" is detected by `EquipmentData == null`.
- Extra file beyond the story's list: `tests/EditMode/DamageCalculation/DamageCalculationTestFakes.cs` (`FakeEquippedWeaponQuery`, `CountingItemDatabase`, `RecordingBonusProvider`, shared with the Story 001 fixture).
- `Calculate` was split in code review: guards in private `IsInvalidRequest`, Steps 5–6 in private `ResolveElementalMitigated`, Step 4 in private `ResolveElementalBonus`. Stories 003 and 004 add their steps to `Calculate`.
- `TR-dmg-004` is a placeholder (registry empty).
**Test Evidence**: Logic — `tests/EditMode/DamageCalculation/DamageCalculation_Elemental_tests.cs` (22 cases; 7 beyond the story's QA list, 6 of them added in code review). `DamageCalculation_PhysicalMitigation_tests.cs` still passes (constructor calls updated only).
**Code Review**: Complete — CHANGES REQUIRED (unity-specialist, qa-tester; 3 required, 9 suggestions); items 1–11 applied, item 12 (rate-limiting the dev error) left as is. Director gates QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (Lean mode).
