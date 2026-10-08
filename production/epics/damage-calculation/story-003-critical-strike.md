# Story 003: Critical Strike with Injected Server RNG

> **Epic**: Damage Calculation
> **Status**: Blocked — **OQ-DC-2: ADR-013 (Server Random Provider) is written but still Proposed (2026-10-08). Review it in a fresh session (`/architecture-review`); on acceptance, complete this story's Implementation Notes from the ADR and set it Ready.**
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 2 hours (after the ADR)

## Context

**GDD**: `design/gdd/damage-calculation.md` — Resolution Sequence Steps 1 and 8, F-DC-3, Edge Cases (`CritChance = 0.0`, roll exactly `0.75`, `CritMultiplier = 1.0`, `NextFloat()` returning `0.0`), OQ-DC-2.
**Requirement**: `TR-dmg-004` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-013-server-random-provider.md` — **Proposed, not yet Accepted**. It defines `IRandomProvider` (`NextFloat` built from 24 bits, no rounding), constructor injection, the shared `ScriptedRandomProvider` / `RecordingRandomProvider` test doubles, and makes this story the one that creates those types (Migration Plan step 1). The rest of this header predates the ADR: OQ-DC-2 asks the ADR to define the interface, the injection point, the test-double contract, and to confirm the RNG is stateless per call so it survives zone migration.
**Existing precedent the ADR should rule on**: `EnhancementService` and `LootDropRoller` inject `System.Random` and tests subclass it (`ScriptedRandom`). `System.Random` yields a `double`; the GDD specifies `NextFloat()` uniform in [0.0, 1.0) and a strict `<` against a float `CritChance`. A `(float)NextDouble()` conversion can round up to `1.0f`, and can move a value across the `0.75` boundary — the ADR must say how the float is produced.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. `UnityEngine.Random` is not an option on the server tick (global state, not injectable).

**Control Manifest Rules (Core layer) and coding standards**:
- Required: the RNG is injected; tests are deterministic — no unseeded randomness, no time-dependent assertions
- Guardrail: one roll per call; no heap allocation per call

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story. The GDD marks F-07, F-09, F-09b, F-11 and E-03 as blocked on OQ-DC-2.*

- [ ] **AC-DC-F-07**: guaranteed crit (mocked RNG = 0.0), `CritMultiplier = 1.5`, `BaseDamage = 768`, `Defense = 394`, `ElementalBonus = 50`, `MagicDefense = 8` → `IsCrit = true`, `FinalDamage = 633`.
- [ ] **AC-DC-F-09**: `CritChance = 0.75`, mocked RNG returns exactly `0.75` → `IsCrit = false`.
- [ ] **AC-DC-F-09b**: `CritChance = 0.75`, mocked RNG returns `0.7499999` → `IsCrit = true`.
- [ ] **AC-DC-F-11**: `BaseDamage = 1`, `Defense = 1`, guaranteed crit, `CritMultiplier = 3.0` → `FinalDamage = 1`, `IsCrit = true`.
- [ ] **AC-DC-F-14 / F-14c**: identical stats and identical mocked RNG with `PhysicalAuto`, `PhysicalSkill` and `MagicalSkill` → identical `FinalDamage`, `PhysicalDamage`, `ElementalDamage` and `IsCrit`.
- [ ] **AC-DC-E-03**: guaranteed crit, `CritMultiplier = 1.0`, `BaseDamage = 100`, `Defense = 0` → `IsCrit = true`, `FinalDamage = 100`.
- [ ] **AC-DC-E-05**: `CritChance = 0.75` over 10,000 calls with a seeded RNG → no call with `r >= 0.75` reports `IsCrit = true`; the observed rate is within [0.737, 0.763].
- [ ] **AC-DC-I-04**: the AC-DC-I-03 configuration with a guaranteed crit and `CritMultiplier = 1.5` → `FinalDamage = 633`, `IsCrit = true`.
- [ ] **One roll per call**: exactly one RNG draw per `Calculate`, including when `CritChance = 0.0`; none on the guard paths of Story 001.
- [ ] **Pre-crit components**: on a crit, `PhysicalDamage` and `ElementalDamage` keep their pre-crit values (374 and 48 in AC-DC-F-07).

---

## Implementation Notes

*To be completed from the ADR once it is Accepted. What the GDD already fixes:*

- **Step 1**: read `CritChance` and `CritMultiplier` with `stats.GetEffectiveStatFloat(attackerId, ...)` at the start of the sequence, before Step 2. Character Stats clamps them to [0.0, 0.75] and [1.0, 3.0].
- **"CritChance = 1.0" in the criteria**: Character Stats clamps `CritChance` to 0.75, so the GDD's "guaranteed crit" setups rely on the mocked RNG returning `0.0`, not on a chance of 1.0.
- **Step 8**: `r < CritChance` with a strict `<`; on a crit `DamageAfterCrit = PreCritDamage × CritMultiplier`, applied to the sum, once.
- **Step 9** then floors `DamageAfterCrit`.
- The crit flag is reported even when the multiplier is 1.0 or the absolute-1 floor fires.

---

## Out of Scope

- The ADR itself (`/architecture-decision`).
- Changing how `EnhancementService` or the Loot Table System obtain randomness — if the ADR changes that contract, it is separate work.
- Crit VFX and audio (VAR-1, VAR-2, VAR-6).

---

## QA Test Cases

**File**: `tests/EditMode/DamageCalculation/DamageCalculation_CriticalStrike_tests.cs` (new). The test double for the RNG is the one the ADR defines.

- **AC-DC-F-07 / I-04** — Given attacker `CritChance = 0.75`, `CritMultiplier = 1.5`, target `Defense = 394`, `MagicDefense = 8`, Fire 50, RNG scripted to `0.0`; When base 768; Then `IsCrit`, `FinalDamage == 633`, `PhysicalDamage == 374`, `ElementalDamage == 48`.
- **AC-DC-F-09** — RNG scripted to `0.75`, chance 0.75; Then `IsCrit == false`.
- **AC-DC-F-09b** — RNG scripted to `0.7499999f`; Then `IsCrit == true`.
- **AC-DC-F-11** — base 1, `Defense = 1`, RNG `0.0`, multiplier 3.0; Then `FinalDamage == 1`, `IsCrit == true`.
- **AC-DC-F-14 / F-14c** — three calls, same scripted roll, one per context; Then the four compared fields are equal and only `DamageContext` differs.
- **AC-DC-E-03** — RNG `0.0`, multiplier 1.0, base 100, `Defense = 0`; Then `IsCrit == true`, `FinalDamage == 100`.
- **AC-DC-E-05** — a seeded RNG wrapped so the test sees each roll; 10,000 calls; Then for every roll `>= 0.75`, `IsCrit == false`; the rate is within the band. The seed is a named constant.
- **Zero chance** — chance 0.0, RNG `0.0`; Then `IsCrit == false` and the RNG was drawn once.
- **Guard paths** — base 0, and attacker equal to target; Then the RNG was not drawn.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DamageCalculation/DamageCalculation_CriticalStrike_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002; **ADR-013 Accepted** (the server RNG injection contract, OQ-DC-2 — Proposed as of 2026-10-08).
- Unlocks: None inside this epic. Auto-Attack Combat and the Skill System need crits before they ship.
