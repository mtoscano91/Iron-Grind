# Story 001: Result Types, Tuning Config, Physical Mitigation and Final Floor

> **Epic**: Damage Calculation
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/damage-calculation.md` — Core Rules 1–3, Resolution Sequence Steps 2, 3, 9, 11, `DamageResult` struct table, F-DC-1, F-DC-4, Edge Cases (`BaseDamage = 0`, `AttackerID == TargetID`, `BaseDamage = 1`), Tuning Knobs.
**Requirement**: `TR-dmg-002`, `TR-dmg-003`, `TR-dmg-005` (placeholders — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: none. `architecture.md` classifies Damage Calc as design-only, LOW risk.
**ADR gate not met (deviation, user decision 2026-10-07)**: GDD Rule 1 and the epic's Definition of Done require an Accepted ADR for the server/client assembly boundary (`ServerLogic.asmdef`) before implementation begins. That ADR does not exist. The user chose to build this story in `IronGrind.Foundation`, as the Currency epic did for its Group G, and to track the boundary in Story 006 (Blocked). Record this under Deviations at `/story-done`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The only Unity APIs are `Mathf.FloorToInt` and `Debug.LogError`. Do not use `(int)Math.Floor()` (GDD Formulas preamble; Character Stats GDD Rule 7 — double precision diverges from Unity's float domain under IL2CPP).

**Control Manifest Rules (Core layer) and coding standards**:
- Required: dependencies are injected by constructor; no static singletons for game state
- Required: gameplay values are data-driven — the three tuning knobs come from an injected config, never from literals in the formula
- Required: doc comments on every public API
- Forbidden: this system fires no events and awards no XP (GDD Overview; TR-dmg-006)
- Guardrail: the resolver is called per hit on the tick thread — no heap allocation per call

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story. Every case uses `CritChance = 0.0` and no elemental weapon.*

- [x] **AC-DC-F-01**: `BaseDamage = 768`, `Defense = 394` → `PhysicalDamage = 374`.
- [x] **AC-DC-F-02**: `BaseDamage = 100`, `Defense = 9999` → `PhysicalDamage = 5` (`MIN_DAMAGE_FRACTION` floor).
- [x] **AC-DC-F-03**: `BaseDamage = 768`, `Defense = 0` → `PhysicalDamage = 768` exactly.
- [x] **AC-DC-F-10**: `BaseDamage = 1`, `Defense = 1` → `FinalDamage = 1` (absolute-1 floor).
- [x] **AC-DC-F-10b**: `BaseDamage = 19`, `Defense = 9999` → `FinalDamage = 1` through the absolute-1 floor (`PhysicalDamage = 0`).
- [x] **AC-DC-F-10c**: `BaseDamage = 20`, `Defense = 9999` → `FinalDamage = 1` through the `MIN_DAMAGE_FRACTION` path (`PhysicalDamage = 1`).
- [x] **AC-DC-F-13 / F-13b / F-14b**: the `DamageContext` passed in (`PhysicalSkill`, `PhysicalAuto`, `MagicalSkill`) is echoed unchanged in `DamageResult.DamageContext`.
- [x] **AC-DC-E-01**: `BaseDamage = 0` → `FinalDamage = 0`, `IsKill = false`, `IsCrit = false`; the target's `CurrentHP` is unchanged and `OnEntityDied` is not fired.
- [x] **AC-DC-E-02**: `AttackerID == TargetID` → `FinalDamage = 0`, `IsKill = false`; `CurrentHP` and `Experience` are unchanged and `OnEntityDied` is not fired.
- [x] **Config**: `DamageCalculationConfig.Default` holds `MIN_DAMAGE_FRACTION = 0.05`, `K_MAGIC = 200`, `MIN_ELEMENTAL_FRACTION = 0.10`; a result computed with a non-default `MIN_DAMAGE_FRACTION` uses the injected value.
- [x] **Null arguments**: a null `CharacterStats` or config in the constructor throws `ArgumentNullException`.

---

## Implementation Notes

- **Files** (new, namespace `IronGrind.DamageCalculation`, assembly `IronGrind.Foundation`, folder `src/Foundation/DamageCalculation/`): `DamageContext.cs`, `DamageResult.cs`, `DamageCalculationConfig.cs`, `DamageCalculator.cs`.
- **Naming (GDD is silent; decided at story creation)**: the GDD calls the function `DamageCalculation(...)`. A type with that name inside the namespace `IronGrind.DamageCalculation` forces full qualification everywhere (the `CharacterStats.CharacterStats` problem). The class is `DamageCalculator` and the method is `Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)` returning `DamageResult`.
- **`DamageContext`**: enum `PhysicalAuto`, `PhysicalSkill`, `MagicalSkill`. It is not the wire type `DamageType` (`src/Foundation/Networking/DamageType.cs`); do not map between them here.
- **`DamageResult`**: `readonly struct` with the seven fields of the GDD table (`PhysicalDamage`, `ElementalDamage`, `FinalDamage`, `IsCrit`, `IsKill`, `DamageContext`, `HasElementalContribution`). In this story `ElementalDamage = 0`, `HasElementalContribution = false`, `IsCrit = false` and `IsKill = false` always; Stories 002–004 fill them.
- **`DamageCalculationConfig`**: immutable, validated in the constructor, with a static `Default` — the pattern of `EnhancementConfig` (Enhancement Story 001). Validate `0 < minDamageFraction <= 1`, `kMagic > 0`, `0 < minElementalFraction <= 1`. The GDD's safe ranges are guidance and are not enforced.
- **Constructor**: `DamageCalculator(CharacterStats stats, DamageCalculationConfig config)`. `CharacterStats` is a sealed concrete class with no interface; inject it directly, as `LevelingService` does. Stories 002 and 003 add parameters.
- **Sequence in this story**: guards → Step 2 `stats.GetEffectiveStat(targetId, StatID.Defense)` → Step 3 `PhysicalMitigated = max(baseDamage × MinDamageFraction, baseDamage − defense)` in float → Step 9 `FinalDamage = max(1, Mathf.FloorToInt(...))` → Step 11. Keep the step order of the GDD and leave clearly marked places for Steps 1, 4–8 and 10.
- **Guards**: `baseDamage <= 0` and `attackerId == targetId` return `default`-like results with `FinalDamage = 0` and the context echoed, before any stat is read. The GDD asks for a "dev-build assert", but AC-DC-E-01 and E-02 expect a returned result, so use `Debug.LogError` inside `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, not an exception *(decided at story creation)*.
- **Server-only**: nothing in these files may reference client, UI or NGO types, so that moving them to a server-only assembly later is an asmdef change only.

---

## Out of Scope

- Story 002: weapon lookup, Item Database, Enhancement bonus, F-DC-2 (Steps 4–7).
- Story 003: crit stats and the RNG roll (Steps 1 and 8) — Blocked on OQ-DC-2.
- Story 004: kill detection (Step 10).
- Story 006: the server/client assembly boundary (AC-DC-I-01).
- XP award, `ApplyDamage`, wire serialization, VFX and audio (callers and other systems).

---

## QA Test Cases

**File**: `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` (new). Uses a real `CharacterStats` (see `tests/EditMode/CharacterStats/` for how fixtures build one and set stats). Build the calculator through one private factory method in the fixture, so later stories change one line. Each expected `Debug.LogError` is declared with `LogAssert.Expect` before the act.

- **AC-DC-F-01** — Given target `Defense = 394`; When `Calculate(768, a, t, PhysicalAuto)`; Then `PhysicalDamage == 374` and `FinalDamage == 374`.
- **AC-DC-F-02** — Given `Defense = 9999`; When base 100; Then `PhysicalDamage == 5`.
- **AC-DC-F-03** — Given `Defense = 0`; When base 768; Then `PhysicalDamage == 768` and `FinalDamage == 768`.
- **AC-DC-F-10** — Given `Defense = 1`; When base 1; Then `FinalDamage == 1`, `PhysicalDamage == 0`.
- **AC-DC-F-10b** — Given `Defense = 9999`; When base 19; Then `FinalDamage == 1` and `PhysicalDamage == 0`.
- **AC-DC-F-10c** — Given `Defense = 9999`; When base 20; Then `FinalDamage == 1` and `PhysicalDamage == 1`. Edge: the pair 10b / 10c is told apart by `PhysicalDamage`.
- **Context echo** — one case per enum value; Then `result.DamageContext` equals the input.
- **AC-DC-E-01** — Given target `CurrentHP = 100`, an `OnEntityDied` subscriber (named method); When base 0; Then `FinalDamage == 0`, `IsKill == false`, `IsCrit == false`, HP still 100, subscriber not called, the dev error logged.
- **AC-DC-E-02** — Given one entity with `CurrentHP = 100` and a known `Experience`; When `Calculate(100, e, e, PhysicalAuto)`; Then `FinalDamage == 0`, `IsKill == false`, HP and Experience unchanged, subscriber not called, the dev error logged.
- **Config default** — `Default` exposes 0.05, 200, 0.10. **Config injected** — Given a config with `MIN_DAMAGE_FRACTION = 0.20`, `Defense = 9999`; When base 100; Then `PhysicalDamage == 20`. **Config validation** — zero, negative and above-1 fractions and `kMagic <= 0` throw `ArgumentOutOfRangeException`.
- **Null arguments** — null stats, null config → `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` — must exist and pass.

**Status**: [x] Created and passing (48 cases)

---

## Dependencies

- Depends on: None (Character Stats epic Complete).
- Unlocks: Story 002, Story 004.

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 11/11 passing (none deferred). Full EditMode suite in Unity batch mode: 1948 / 1948.
**Deviations** (all advisory):
- Assembly ADR gate not met: the GDD requires the server/client assembly ADR before implementation; the code is in `IronGrind.Foundation` by user decision (2026-10-07). Tracked in Story 006.
- `DamageCalculationConfig` has a fourth value beyond the story's three knobs: `MaxBaseDamage` (default 99999, valid [1, 2^24]). `Calculate` rejects a base above it with a dev error. Added in code review to keep the float conversion exact and to avoid an int overflow in the floor at very large bases. Not in the GDD's Tuning Knobs.
- `DamageResult.Rejected(context)` factory added; `FinalDamage == 0` is documented as the rejected-call signal.
- A target with no stat record is treated as `Defense = 0` (GDD silent; pinned by a test). Open question recorded in Story 004.
- GDD and `StatSchema` disagree on the AttackPower ceiling (9999 vs 99999); recorded in `EPIC.md`.
- TR-IDs are placeholders (registry empty).
**Test Evidence**: Logic — `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` (48 cases; 21 added in code review beyond the story's QA list).
**Code Review**: Complete — APPROVED WITH SUGGESTIONS (unity-specialist, qa-tester); all 7 suggestions applied. Director gates QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (Lean mode).
