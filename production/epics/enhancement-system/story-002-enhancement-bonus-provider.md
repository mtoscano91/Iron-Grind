# Story 002: Enhancement Bonus Provider

> **Epic**: Enhancement System
> **Status**: Ready
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

- [ ] **Interface**: `IEnhancementBonusProvider` declares `GetFlatBonus(level: int, baseFlatBonus: int, gearTier: GearTier): int` and `GetElementalBonus(level: int, baseElementalDamage: int, gearTier: GearTier, isWeapon: bool): int`, owned by the Enhancement System.
- [ ] **AC-ENH-19 (F-ENH-1)**: `GetFlatBonus(5, 10, GearTier.Bronze)` returns 25 (`10 + 5 × 3`).
- [ ] **Flat bonus at level 0**: `GetFlatBonus(0, base, tier)` returns `base` for every tier.
- [ ] **Flat bonus maximum**: `GetFlatBonus(10, 68, GearTier.DarkSteel)` returns 168 (the GDD's stated output maximum).
- [ ] **AC-ENH-20 (F-ENH-2)**: `GetElementalBonus(7, 15, GearTier.DarkSteel, true)` returns 50; `GetElementalBonus(0, 15, GearTier.DarkSteel, true)` returns 15; `GetElementalBonus(10, 9990, GearTier.DarkSteel, true)` returns 9,999 (clamped to `ElementalDamage_ceiling`).
- [ ] **AC-ENH-21**: `GetElementalBonus(5, 0, GearTier.Bronze, false)` returns 0; any non-weapon call returns 0 whatever the level, base or tier.
- [ ] **Per-tier values come from the config**: with a config whose `BonusPerLevel[Iron]` is changed, `GetFlatBonus` for Iron changes accordingly and the other tiers do not.
- [ ] **F-ENH-3 tier parity**: with the default config, `midpoint(T) + 5 × BonusPerLevel[T] − midpoint(T+1)` is 0 for Bronze → Iron, +8 for Iron → Steel and +4 for Steel → Dark Steel, using the GDD's midpoints 10, 25, 37, 63.

---

## Implementation Notes

- `IEnhancementBonusProvider` and its implementation live in `src/Foundation/EnhancementSystem/`. The implementation takes `EnhancementConfig` (Story 001) in its constructor and is stateless otherwise.
- Formulas, exactly as written in the GDD:
  - `EnhancedFlatBonus = baseFlatBonus + level × BonusPerLevel[gearTier]`
  - `EnhancedElementalDamage = min(baseElementalDamage + level × ElementalBonusPerLevel[gearTier], ElementalDamage_ceiling)`; 0 when `isWeapon` is false.
- Parameters are `int` as the GDD declares them, although callers hold the level as a `byte`.
- **Input the GDD does not specify**: a `level` outside `[0, MAX_ENHANCEMENT_LEVEL]`, a negative base, or `GearTier.None`. The GDD gives no rule. Raise this at `/story-readiness`; do not pick a behaviour silently. (A defensible default is `ArgumentOutOfRangeException`, because both callers pass values that were validated when the item was stored.)
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
- **Config-driven** — custom config with `BonusPerLevel[Iron] = 5`: Iron +2 on base 10 → 20; Bronze unchanged at 16.
- **F-ENH-3** — the three parity deltas are 0, +8, +4.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_BonusProvider_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (config)
- Unlocks: Equipment System epic (enhanced modifier registration), Damage Calculation epic (elemental bonus input)
