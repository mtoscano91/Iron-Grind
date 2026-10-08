# Story 003: Critical Strike with Injected Server RNG

> **Epic**: Damage Calculation
> **Status**: Ready (Implementation Notes completed from ADR-013 on 2026-10-08; run `/story-readiness` before `/dev-story`)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-08
> **Estimate**: 4 hours (the story also creates the random provider types, the two shared test doubles and the adapter tests — ADR-013 Migration Plan step 1)

## Context

**GDD**: `design/gdd/damage-calculation.md` — Resolution Sequence Steps 1 and 8, F-DC-3, Edge Cases (`CritChance = 0.0`, roll exactly `0.75`, `CritMultiplier = 1.0`, `CritMultiplier` below 1.0, `NextFloat()` returning `0.0`), OQ-DC-2 (resolved by ADR-013).
**Requirement**: `TR-dmg-004` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-013-server-random-provider.md` — **Accepted 2026-10-08**. It defines `IRandomProvider` (`NextFloat` built from 24 bits of one integer draw, no rounding), constructor injection, one generator per zone process, the shared `ScriptedRandomProvider` / `RecordingRandomProvider` test doubles, and makes this story the one that creates those types (Migration Plan step 1).
**Also applies**: ADR-012 (the new types are `IronGrind.ServerLogic`; the namespace goes on the boundary test's `ServerOnlyNamespaces` list).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No engine API beyond what `DamageCalculator` already uses. `UnityEngine.Random` is forbidden in `IronGrind.ServerLogic`.

**Control Manifest Rules (2026-10-08)**:
- Required (Core): `DamageCalculator(stats, weapons, items, bonuses, config, random)` draws `NextFloat()` for the crit roll — exactly one draw per `Calculate` call (including when `CritChance` is 0.0) and none on a rejected call; `Calculate` keeps its signature
- Required (Core): the AC-DC-F-09b test scripts `0.74999994f`
- Required (Foundation): `IRandomProvider` and `SystemRandomProvider` in namespace `IronGrind.Randomness`, folder `src/ServerLogic/Randomness/`; the namespace is added to `ServerOnlyNamespaces`
- Required (Foundation): constructor injection into a `private readonly` field; a null argument throws `ArgumentNullException`
- Required (Foundation): tests implement `IRandomProvider`; a pseudo-random sequence is `new SystemRandomProvider(new System.Random(SEED))` with `SEED` a named constant; a path that must not draw is asserted with an empty `ScriptedRandomProvider`; seeded tests assert properties, never specific values
- Forbidden: `UnityEngine.Random`; a static accessor for the provider; `(float)NextDouble()`; passing the roll into `Calculate` as a value; a `System.Random` subclass as a test double; `RandomProviderFactory.CreateSeededFromEntropy` in a test
- Guardrail: no heap allocation per draw or per `Calculate` call

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story, plus the deliverables of ADR-013 Migration Plan step 1.*

- [ ] **AC-DC-F-07**: guaranteed crit (scripted roll = 0.0), `CritMultiplier = 1.5`, `BaseDamage = 768`, `Defense = 394`, `ElementalBonus = 50`, `MagicDefense = 8` → `IsCrit = true`, `FinalDamage = 633`.
- [ ] **AC-DC-F-09**: `CritChance = 0.75`, scripted roll exactly `0.75f` → `IsCrit = false`.
- [ ] **AC-DC-F-09b**: `CritChance = 0.75`, scripted roll `0.74999994f` (the float directly below 0.75) → `IsCrit = true`.
- [ ] **AC-DC-F-11**: `BaseDamage = 1`, `Defense = 1`, guaranteed crit, `CritMultiplier = 3.0` → `FinalDamage = 1`, `IsCrit = true`.
- [ ] **AC-DC-F-14 / F-14c**: identical stats and identical scripted roll with `PhysicalAuto`, `PhysicalSkill` and `MagicalSkill` → identical `FinalDamage`, `PhysicalDamage`, `ElementalDamage` and `IsCrit`.
- [ ] **AC-DC-E-03**: guaranteed crit, `CritMultiplier = 1.0`, `BaseDamage = 100`, `Defense = 0` → `IsCrit = true`, `FinalDamage = 100`.
- [ ] **AC-DC-E-05**: `CritChance = 0.75` over 10,000 calls with a seeded provider → no call with `r >= 0.75` reports `IsCrit = true`; the observed rate is within [0.737, 0.763].
- [ ] **AC-DC-I-04**: the AC-DC-I-03 configuration with a guaranteed crit and `CritMultiplier = 1.5` → `FinalDamage = 633`, `IsCrit = true`.
- [ ] **One roll per call**: exactly one draw per `Calculate`, including when `CritChance = 0.0`; none on the guard paths of Story 001.
- [ ] **Pre-crit components**: on a crit, `PhysicalDamage` and `ElementalDamage` keep their pre-crit values (374 and 48 in AC-DC-F-07).
- [ ] **CritMultiplier below 1.0** (user decision 2026-10-08): a crit whose attacker `CritMultiplier` reads below 1.0 (stat never set reads `0f`) uses 1.0 — `FinalDamage` equals the normal hit, `IsCrit = true`, and a dev error is logged in editor and development builds only.
- [ ] **Provider types**: `IRandomProvider` and `SystemRandomProvider` exist in `src/ServerLogic/Randomness/`, namespace `IronGrind.Randomness`, with the members ADR-013 Key Interfaces lists; `IronGrind.Randomness` is in `ServerOnlyNamespaces` and the boundary test passes.
- [ ] **Shared test doubles**: `ScriptedRandomProvider` and `RecordingRandomProvider` exist in `tests/EditMode/Randomness/` with the behaviour of ADR-013 Decision 6.
- [ ] **Adapter tests pass**: the `ToUnitFloat` boundaries, the seeded range run, the pass-through of `NextDouble` and `NextInt`, and the null and negative arguments (see QA Test Cases).
- [ ] **Null provider**: `new DamageCalculator(..., random: null)` throws `ArgumentNullException`.
- [ ] **Suite**: the full EditMode suite passes; the existing Damage Calculation tests keep their expected values.

---

## Implementation Notes

**Files to create**
- `src/ServerLogic/Randomness/IRandomProvider.cs` — the three members of ADR-013 Decision 1, with doc comments.
- `src/ServerLogic/Randomness/SystemRandomProvider.cs` — sealed; wraps the `System.Random` handed to its constructor (null → `ArgumentNullException`). `NextFloat()` returns `ToUnitFloat(_random.Next())`. `public static float ToUnitFloat(int sample)` returns `(sample >> 7) * (1f / 16777216f)` and throws `ArgumentOutOfRangeException` for a negative sample. `NextDouble()` and `NextInt(min, max)` return `_random.NextDouble()` and `_random.Next(min, max)` unchanged.
- `tests/EditMode/Randomness/ScriptedRandomProvider.cs`, `RecordingRandomProvider.cs` — ADR-013 Decision 6.
- `tests/EditMode/Randomness/SystemRandomProvider_tests.cs` — the adapter tests.
- `tests/EditMode/DamageCalculation/DamageCalculation_CriticalStrike_tests.cs`.
- Each new `.cs` gets its `.meta` (and each new folder its folder `.meta`) from the Editor.

**Files to change**
- `src/ServerLogic/DamageCalculation/DamageCalculator.cs` — constructor parameter `IRandomProvider random` (last), stored in `private readonly IRandomProvider _random`; Steps 1 and 8; `isCrit` in the result; class and constructor doc comments.
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` — add `IronGrind.Randomness` to `ServerOnlyNamespaces`.
- The nine construction sites: `DamageCalculation_PhysicalMitigation_tests.cs` (3), `DamageCalculation_Elemental_tests.cs` (4), `DamageCalculation_KillDetection_tests.cs` (1), `tests/EditMode/Integration/DamageCalculation/DamageCalculation_KillSequence_integration_tests.cs` (1).

**Not created here**: `RandomProviderFactory` (it replaces `LootRandomFactory` in the Loot Table migration story). No server composition root exists yet; nothing outside tests constructs the calculator.

**Resolution sequence**
- **Step 1**: after the guards and before Step 2, read `CritChance` and `CritMultiplier` with `_stats.GetEffectiveStatFloat(attackerId, ...)`. Character Stats clamps them to [0.0, 0.75] and [1.0, 3.0] when the stat is set; an entity whose stat was never set reads `0f` (the absent-stat rule of `GetEffectiveStatFloat`).
- **Step 8**: `bool isCrit = _random.NextFloat() < critChance;` — strict `<`, drawn unconditionally so that the count is one per accepted call, also at `CritChance = 0.0`. On a crit `damageAfterCrit = preCritDamage * critMultiplier`, applied to the sum, once.
- **CritMultiplier below 1.0** (user decision 2026-10-08): on a crit, a multiplier below 1.0 is replaced by 1.0 and a dev error is logged under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, like the dead-entity guard. The check runs only when the roll is a crit, so a non-crit hit by an attacker with no crit stats logs nothing.
- **Step 9** then floors `damageAfterCrit`. The crit flag is reported even when the multiplier is 1.0 or the absolute-1 floor fires.
- The guard paths return before Step 1, so a rejected call reads no stat and draws nothing.
- No heap allocation per call.

**Tests**
- "Guaranteed crit" in the criteria: Character Stats clamps `CritChance` to 0.75, so these setups set `CritChance = 0.75` and script the roll with `EnqueueFloat(0.0f)`.
- The existing nine construction sites pass `new SystemRandomProvider(new System.Random(SEED))` with `SEED` a named constant per test class. Their attackers have no crit stats, `CritChance` reads 0 and no roll is below it, so every existing expected value holds. The null-argument tests among them pass a valid provider and one new test passes a null one.
- Seeded tests assert properties only (AC-DC-E-05: no crit at or above the threshold, rate within the band).

---

## Out of Scope

- `RandomProviderFactory` and the `[Random] PRNG seed:` log line (Loot Table migration story, ADR-013 Migration Plan step 2).
- Migrating `EnhancementService`, `LootTableService` and `LootDropRoller` to `IRandomProvider`, and the reflection test for the `System.Random` ban (Migration Plan steps 2 and 3).
- A thread check in `SystemRandomProvider` (an option for the story that writes the server composition root).
- Crit VFX and audio (VAR-1, VAR-2, VAR-6).

---

## QA Test Cases

**File**: `tests/EditMode/DamageCalculation/DamageCalculation_CriticalStrike_tests.cs` (new). The roll is scripted with `ScriptedRandomProvider` unless stated.

- **AC-DC-F-07 / I-04** — Given attacker `CritChance = 0.75`, `CritMultiplier = 1.5`, target `Defense = 394`, `MagicDefense = 8`, Fire 50, roll `0.0f`; When base 768; Then `IsCrit`, `FinalDamage == 633`, `PhysicalDamage == 374`, `ElementalDamage == 48`.
- **AC-DC-F-09** — roll `0.75f`, chance 0.75; Then `IsCrit == false`.
- **AC-DC-F-09b** — roll `0.74999994f`; Then `IsCrit == true`.
- **AC-DC-F-11** — base 1, `Defense = 1`, roll `0.0f`, multiplier 3.0; Then `FinalDamage == 1`, `IsCrit == true`.
- **AC-DC-F-14 / F-14c** — three calls, same scripted roll, one per context; Then the four compared fields are equal and only `DamageContext` differs.
- **AC-DC-E-03** — roll `0.0f`, multiplier 1.0, base 100, `Defense = 0`; Then `IsCrit == true`, `FinalDamage == 100`.
- **AC-DC-E-05** — `RecordingRandomProvider` over `new SystemRandomProvider(new System.Random(SEED))`; 10,000 calls; Then for every recorded roll `>= 0.75`, `IsCrit == false`; the rate is within [0.737, 0.763]. `SEED` is a named constant.
- **Zero chance** — chance 0.0, roll `0.0f`; Then `IsCrit == false` and `FloatDrawCount == 1`.
- **One draw per call** — one accepted call; Then `DrawCount == 1`, `DoubleDrawCount == 0`, `IntDrawCount == 0`.
- **Guard paths** — base 0, base above `MaxBaseDamage`, and attacker equal to target, each with an empty `ScriptedRandomProvider`; Then the call returns the rejected result and `DrawCount == 0` (a draw would throw).
- **CritMultiplier never set** — attacker `CritChance = 0.75`, no `CritMultiplier`, roll `0.0f`, base 100, `Defense = 0`; Then `IsCrit == true`, `FinalDamage == 100`, and the dev error is logged (`LogAssert.Expect`).
- **Null provider** — constructor with `random: null`; Then `ArgumentNullException` with parameter name `random`.

**File**: `tests/EditMode/Randomness/SystemRandomProvider_tests.cs` (new).

- **ToUnitFloat boundaries** — `ToUnitFloat(0)` and `ToUnitFloat(127)` give `0.0f`; `ToUnitFloat(128)` gives 2⁻²⁴; `ToUnitFloat(int.MaxValue - 1)` gives `0.99999994f`; `ToUnitFloat(12582911 * 128)` gives `0.74999994f`; `ToUnitFloat(12582912 * 128)` gives `0.75f`; a negative sample throws `ArgumentOutOfRangeException`.
- **Range** — a seeded run of 1,000,000 `NextFloat()` draws; Then every result is `>= 0.0f` and `< 1.0f`.
- **Pass-through** — `NextDouble()` and `NextInt(min, max)` return what a second `System.Random` with the same seed returns.
- **Null argument** — `new SystemRandomProvider(null)` throws `ArgumentNullException`.
- **ScriptedRandomProvider** — returns queued values unchanged and in order, per queue; a draw from an empty queue throws `InvalidOperationException`; the four counters are correct.
- **RecordingRandomProvider** — forwards each call, keeps the last value per method, and counts.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DamageCalculation/DamageCalculation_CriticalStrike_tests.cs` and `tests/EditMode/Randomness/SystemRandomProvider_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (Complete); ADR-013 (Accepted 2026-10-08).
- Unlocks: the Loot Table and Enhancement migration stories (ADR-013 Migration Plan steps 2 and 3), which use the types created here. Auto-Attack Combat and the Skill System need crits before they ship.
