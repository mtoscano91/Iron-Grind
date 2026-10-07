# Story 002: Enhancement Bonus Provider

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — `IEnhancementBonusProvider` interface (Interactions with Other Systems), F-ENH-1 (flat bonus), F-ENH-2 (elemental bonus, weapons only), F-ENH-3 (tier parity constraint), AC-ENH-19, AC-ENH-20, AC-ENH-21.
**Requirement**: `TR-enh-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (interface dependency — consumers receive `IEnhancementBonusProvider` by injection).
**ADR Decision Summary**: Systems expose interfaces and are injected into their consumers; a consumer acts on return values of direct Tier 1 calls.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. No engine API.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency, no concrete-class dependency across systems — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [x] **Interface**: `IEnhancementBonusProvider` declares `GetFlatBonus(level: int, baseFlatBonus: int, gearTier: GearTier): int` and `GetElementalBonus(level: int, baseElementalDamage: int, gearTier: GearTier, isWeapon: bool): int`, owned by the Enhancement System.
- [x] **AC-ENH-19 (F-ENH-1)**: `GetFlatBonus(5, 10, GearTier.Bronze)` returns 25 (`10 + 5 × 3`).
- [x] **Flat bonus at level 0**: `GetFlatBonus(0, base, tier)` returns `base` for every tier.
- [x] **Flat bonus maximum**: `GetFlatBonus(10, 68, GearTier.DarkSteel)` returns 168 (the GDD's stated output maximum).
- [x] **AC-ENH-20 (F-ENH-2)**: `GetElementalBonus(7, 15, GearTier.DarkSteel, true)` returns 50; `GetElementalBonus(0, 15, GearTier.DarkSteel, true)` returns 15; `GetElementalBonus(10, 9990, GearTier.DarkSteel, true)` returns 9,999 (clamped to `ElementalDamage_ceiling`).
- [x] **AC-ENH-21**: `GetElementalBonus(5, 0, GearTier.Bronze, false)` returns 0; any non-weapon call returns 0 whatever the level, base or tier.
- [x] **Invalid input** *(decided 2026-10-07 at readiness — the GDD gives no rule)*: a `level` below 0 or above `MAX_ENHANCEMENT_LEVEL` throws `ArgumentOutOfRangeException`; `GearTier.None` or an undefined tier throws `ArgumentOutOfRangeException`. The base value is not validated — the formula is applied as written. A non-weapon `GetElementalBonus` call returns 0 before any other check, so it never throws.
- [x] **Per-tier values come from the config**: with a config whose `BonusPerLevel[Iron]` is changed, `GetFlatBonus` for Iron changes accordingly and the other tiers do not.
- [x] **F-ENH-3 tier parity**: with the default config, `midpoint(T) + 5 × BonusPerLevel[T] − midpoint(T+1)` is 0 for Bronze → Iron, +8 for Iron → Steel and +4 for Steel → Dark Steel, using the GDD's midpoints 10, 25, 37, 63.

---

## Implementation Notes

- `IEnhancementBonusProvider` and its implementation live in `src/Foundation/EnhancementSystem/`. The implementation takes `EnhancementConfig` (Story 001) in its constructor and is stateless otherwise.
- Formulas, exactly as written in the GDD:
  - `EnhancedFlatBonus = baseFlatBonus + level × BonusPerLevel[gearTier]`
  - `EnhancedElementalDamage = min(baseElementalDamage + level × ElementalBonusPerLevel[gearTier], ElementalDamage_ceiling)`; 0 when `isWeapon` is false.
- Parameters are `int` as the GDD declares them, although callers hold the level as a `byte`.
- **Invalid input** (decided at readiness, see the criterion): check the level against `EnhancementConfig.MaxEnhancementLevel` in the provider; the tier check comes for free from `EnhancementConfig.GetBonusPerLevel` / `GetElementalBonusPerLevel`, which already throw for `GearTier.None` and undefined values. In `GetElementalBonus`, test `isWeapon` first and return 0. Both callers pass a level that was validated when the item was stored, so a throw here means a caller bug.
- **`baseFlatBonus` is `int` although the Item Database stores a `float`**: `StatModifierEntry.FlatBonus` is `float` in code, while the GDD declares this parameter as `int`. Follow the GDD. Converting the item's value is the Equipment System's job at its call site (its stories must say how — the MVP ranges in F-EQS-2 are whole numbers).
- **Two copies of the elemental ceiling exist**: `ItemDefinitionValidator` has a private `MaxElementalDamage = 9_999` (the Item Database owns the value); `EnhancementConstants.ELEMENTAL_DAMAGE_CEILING` is the copy the config carries. They agree. Use the config's value here; do not reach into the validator. (Story 001's note that no constant existed in `src/` was wrong — the search missed the `9_999` spelling.)
- The parity test documents the constraint that ties `BonusPerLevel` to the Equipment System's flat bonus ranges (F-EQS-2). It asserts the GDD's verified table; it does not read item records.
- Zero allocation: both methods are pure arithmetic.

---

## Out of Scope

- Equipment System epic: calling `GetFlatBonus` inside `AddEquipmentModifier` registration
- Damage Calculation epic: calling `GetElementalBonus` for F-DC-2
- Character Persistence: re-registration of modifiers on load
- Story 001: the config values themselves

---

## QA Test Cases

**File**: `tests/EditMode/EnhancementSystem/EnhancementSystem_BonusProvider_tests.cs` (new)

- **AC-ENH-19** — `GetFlatBonus(5, 10, Bronze)` → 25.
- **Flat, each tier at +1** — base 10: Bronze 13, Iron 14, Steel 16, Dark Steel 20.
- **Flat at +0** — returns the base for each tier.
- **Flat maximum** — `GetFlatBonus(10, 68, DarkSteel)` → 168.
- **AC-ENH-20** — the three elemental calls → 50, 15, 9,999.
- **Elemental, each tier at +1** — base 15: Bronze 16, Iron 17, Steel 18, Dark Steel 20.
- **Elemental clamp boundary** — a call that lands exactly on 9,999 returns 9,999; one unit above returns 9,999.
- **AC-ENH-21** — non-weapon → 0 for level 5 base 0, and for level 10 base 500.
- **Level out of range** — `GetFlatBonus(-1, 10, Bronze)` and `GetFlatBonus(11, 10, Bronze)` throw `ArgumentOutOfRangeException`; levels 0 and 10 do not. Same four cases for `GetElementalBonus(…, true)`.
- **Tier** — `GearTier.None` and an undefined tier value throw `ArgumentOutOfRangeException` from both methods (weapon call for the elemental one).
- **Non-weapon never throws** — `GetElementalBonus(11, 0, GearTier.None, false)` → 0.
- **Base not validated** — `GetFlatBonus(2, -5, Bronze)` → 1 (`-5 + 2 × 3`).
- **Config-driven** — custom config with `BonusPerLevel[Iron] = 5`: Iron +2 on base 10 → 20; Bronze unchanged at 16.
- **F-ENH-3** — the three parity deltas are 0, +8, +4.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_BonusProvider_tests.cs` — must exist and pass.

**Status**: [x] Created — 15 test methods + 27 parameterised cases (42 cases), passing (EditMode 1639/1639, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 001 (config)
- Unlocks: Equipment System epic (enhanced modifier registration), Damage Calculation epic (elemental bonus input)

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 9/9 passing (none deferred)
**Deviations**: None.
**Test Evidence**: Logic — `tests/EditMode/EnhancementSystem/EnhancementSystem_BonusProvider_tests.cs` (42 cases); EditMode 1639/1639 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all three applied (unused import removed, custom-ceiling test added, default cases use `EnhancementConfig.Default`). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/IEnhancementBonusProvider.cs`, `EnhancementBonusProvider.cs`.
**For the Equipment epic**: `GetFlatBonus` takes an `int` base; `StatModifierEntry.FlatBonus` is a `float`, so the Equipment System converts at its call site. A level outside [0, cap] or `GearTier.None` throws.
