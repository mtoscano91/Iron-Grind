# Story 015: Migrate Loot Table to `IRandomProvider`

> **Epic**: Loot Table System
> **Status**: Draft (written 2026-10-08 from ADR-013 Migration Plan step 2; run `/story-readiness` before `/dev-story`)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-08
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-1 (drop roll architecture and PRNG seeding: "a single process-level `IRandomProvider` (ADR-013), created once at server startup and seeded explicitly from system entropy so that the seed can be logged"), F-LT-1 (`baseGold` drawn uniformly from `[GoldMin, GoldMax]`). No rule a player can observe changes in this story.
**Requirement**: none registered — the requirement is ADR-013's (one random-number contract for every server system). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: `docs/architecture/ADR-013-server-random-provider.md` — **Accepted 2026-10-08**. Migration Plan step 2: `LootTableService` and `LootDropRoller` take `IRandomProvider`; `LootRandomFactory` is replaced by `RandomProviderFactory` in `IronGrind.Randomness` with the new log line; the factory test follows; the `System.Random` doubles in the four Loot Table test files are replaced.
**ADR Decision Summary**: Decision 1 (the interface), Decision 2 (a caller whose threshold is a `float` may widen it to `double` and draw `NextDouble()` — what `LootDropRoller` does), Decision 3 (constructor injection for a class, a parameter for a static method), Decision 4 (the factory: one generator per zone process), Decision 5 (draw counts: one per drop-table entry and one for gold), Decision 6 (test doubles implement `IRandomProvider`; none subclasses `System.Random`).
**Also applies**: ADR-012 — `RandomProviderFactory` is `IronGrind.ServerLogic`; `IronGrind.Randomness` is already in `ServerOnlyNamespaces` (Damage Calculation Story 003).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The only Unity API is `UnityEngine.Debug.Log` in the factory, as in `LootRandomFactory` today. `RandomNumberGenerator.Create()` has only run in the Editor (ADR-013 Verification Required 3); checking it in a stripped Linux IL2CPP server build is not part of this story.

**Control Manifest Rules (2026-10-08, Foundation → Server random provider)**:
- Required: a class that rolls takes `IRandomProvider` as a constructor parameter stored in a `private readonly` field (null → `ArgumentNullException`); a static method that rolls takes it as a parameter (`LootDropRoller.Roll(table, random)`)
- Required: `RandomProviderFactory` replaces `LootRandomFactory`: it draws a 32-bit seed from `RandomNumberGenerator.Create()`, logs `[Random] PRNG seed: {seed}` once with `UnityEngine.Debug.Log`, and returns `new SystemRandomProvider(new System.Random(seed))`
- Required: tests implement `IRandomProvider`; a pseudo-random sequence is `new SystemRandomProvider(new System.Random(SEED))` with `SEED` a named constant; a path that must not draw is asserted with an empty `ScriptedRandomProvider`
- Forbidden: `new System.Random(...)` in `IronGrind.ServerLogic` anywhere except `RandomProviderFactory`; `UnityEngine.Random`; a static accessor for the provider; `(float)NextDouble()`; a `System.Random` subclass as a test double; calling `RandomProviderFactory.CreateSeededFromEntropy` from a test other than the factory's own; asserting a specific value sequence in a seeded test
- Guardrail: no allocation per draw

---

## Acceptance Criteria

*From ADR-013 Migration Plan step 2. The Loot Table GDD criteria already covered by Stories 002, 004, 005 and 006 must keep passing with their expected values unchanged.*

- [ ] **Roller**: `LootDropRoller.Roll(LootTableDefinition table, IRandomProvider random)` draws exactly one `NextDouble()` per entry, in entry order, and an entry drops when the draw is below its `DropChance` widened to `double`. It draws no `NextFloat()` and no `NextInt()`. A null `table` or `random` throws `ArgumentNullException`.
- [ ] **Service**: `LootTableService` takes `IRandomProvider` in the constructor position the `System.Random` has today, stores it in a `private readonly` field, and throws `ArgumentNullException` for null. The gold draw is one `NextInt(table.GoldMin, table.GoldMax + 1)` per resolved kill, after the entry rolls.
- [ ] **Draw counts unchanged**: one `NextDouble()` per table entry and one `NextInt()` per resolved kill; no draw on the paths that draw nothing today (the `TotalDraws == 0` case of the kill-resolution tests); a tier shift consumes no extra draw.
- [ ] **Gold range cannot overflow**: `GoldMax + 1` stays below `int.MaxValue` for every table the service can see — the validator rejects `GoldMax > GOLD_CAP` (9,999,999) and the registry is built only from tables with no issues. The existing tests `Validate_GoldMaxAboveCap_OneErrorNamingGoldMax` and `Validate_GoldMaxEqualsCap_NoError` are the evidence; the story confirms that no path registers an unvalidated table.
- [ ] **Factory**: `RandomProviderFactory.CreateSeededFromEntropy(out int seed)` exists in `src/ServerLogic/Randomness/`, namespace `IronGrind.Randomness`; it returns a non-null `IRandomProvider` and logs exactly one line `[Random] PRNG seed: {seed}`.
- [ ] **Old factory removed**: `LootRandomFactory.cs` and its `.meta` are deleted; no file under `src/` or `tests/` names `LootRandomFactory`.
- [ ] **No `System.Random` in Loot Table**: no field, constructor parameter or method parameter in `src/ServerLogic/LootTableSystem/` has the type `System.Random`, written qualified or as `Random` under `using System;`. A source search of `src/ServerLogic/` for `new Random(` and `new System.Random(` finds only `Randomness/RandomProviderFactory.cs`; `UnityEngine.Random` is found nowhere.
- [ ] **Test doubles**: no type in the four Loot Table test files derives from `System.Random`. `LootTable_DropRoll_tests.cs`, `LootTable_GroundItemLifecycle_tests.cs` and `LootTable_RoundRobin_integration_tests.cs` use the shared `ScriptedRandomProvider` and `RecordingRandomProvider`. `LootTable_KillResolution_integration_tests.cs` keeps one local double that implements `IRandomProvider` (see Implementation Notes).
- [ ] **Bounds and order still asserted**: the kill-resolution tests still assert that the gold draw is asked for `[GoldMin, GoldMax + 1)` and that the entry rolls come before the gold draw.
- [ ] **Factory test**: one test asserts the log line and a non-null result, and nothing about the values drawn. It is the only test that calls `CreateSeededFromEntropy`.
- [ ] **Suite**: the full EditMode suite passes. No expected value in an existing Loot Table test changes, except where a test's value came from the real `System.Random` behaviour a double inherited (see Implementation Notes — each such case is listed in the completion notes).

---

## Implementation Notes

**Files to create**
- `src/ServerLogic/Randomness/RandomProviderFactory.cs` — `public static class RandomProviderFactory` with `public static IRandomProvider CreateSeededFromEntropy(out int seed)`. Body as `LootRandomFactory` today (4 bytes from `RandomNumberGenerator.Create()`, `BitConverter.ToInt32`), with the log text `[Random] PRNG seed: ` and the return value `new SystemRandomProvider(new System.Random(seed))`. Doc comments carry over what `LootRandomFactory` says (one instance per process, tick thread only, a logged seed reproduces a sequence on the same runtime and build only) and add ADR-013's note that the logged seed is sensitive: anyone who can read the log can predict the rolls.
- `tests/EditMode/Randomness/RandomProviderFactory_tests.cs` — the factory test.
- Each new `.cs` gets its `.meta` from the Editor.

**Files to delete**
- `src/ServerLogic/LootTableSystem/LootRandomFactory.cs` and `LootRandomFactory.cs.meta` (`git rm`). Nothing outside the Loot Table folder and `LootTable_DropRoll_tests.cs` names the type. It is a static class and is not serialized, so no `[MovedFrom]` is needed.

**Files to change**
- `src/ServerLogic/LootTableSystem/LootDropRoller.cs` — parameter `Random rng` becomes `IRandomProvider random`; the draw stays `random.NextDouble() < entry.DropChance` (the existing comment about comparing in `double` stays true); doc comments point to `IRandomProvider.NextDouble` and ADR-013 instead of `Random.NextDouble` and `LootRandomFactory`.
- `src/ServerLogic/LootTableSystem/LootTableService.cs` — field `private readonly Random _rng` becomes `private readonly IRandomProvider _random`; constructor parameter `Random rng` becomes `IRandomProvider random` in the same position; `_rng.Next(table.GoldMin, table.GoldMax + 1)` becomes `_random.NextInt(table.GoldMin, table.GoldMax + 1)`; doc comments updated.
- `tests/EditMode/LootTableSystem/LootTable_DropRoll_tests.cs` — see Tests.
- `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs` — see Tests.
- `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` — see Tests.
- `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` — see Tests.

**Parameter rename**: `rng` becomes `random`, the name ADR-013 uses (`LootDropRoller.Roll(table, random)`). No existing test asserts the parameter name of the null-argument exception for either type; if the implementation adds such an assertion it uses `random`.

**Tests**
- `LootTable_DropRoll_tests.cs`:
  - `CountingRandom(FIXED_SEED)` becomes `new RecordingRandomProvider(new SystemRandomProvider(new System.Random(FIXED_SEED)))`; `NextDoubleCalls` becomes `DoubleDrawCount`. Add `FloatDrawCount == 0` and `IntDrawCount == 0` to at least one of the count tests.
  - `ScriptedRandom(d1, d2, ...)` becomes a `ScriptedRandomProvider` with one `EnqueueDouble` per value (a small local helper is fine). `0.99999999` stays a `double`.
  - `Factory_CreateSeededFromEntropy_LogsSeedAndReturnsReproducibleRandom` is removed from this file; the factory test moves to `tests/EditMode/Randomness/RandomProviderFactory_tests.cs` and asserts `^\[Random\] PRNG seed: -?\d+$` with `LogAssert.Expect` and a non-null result. The old test's comparison against a second `System.Random(seed)` is dropped: ADR-013 Decision 6 says the factory test asserts nothing about the values.
- `LootTable_GroundItemLifecycle_tests.cs` and `LootTable_RoundRobin_integration_tests.cs`: the local `ScriptedRandom(double draw)` returns the same value for every `NextDouble()` and, because it subclasses `System.Random`, returns real seeded values from `Next(min, max)` for the gold draw. With `ScriptedRandomProvider` each draw is queued: one `EnqueueDouble(draw)` per table entry of each kill and one `EnqueueInt(gold)` per kill, with `gold` a named constant inside the table's `[GoldMin, GoldMax]`. If a test asserted a gold amount that came from the seeded `Next`, its expected value changes to the scripted constant — list each such test in the completion notes. `PRNG_SEED` is deleted from a file where nothing uses it any more.
- `LootTable_KillResolution_integration_tests.cs` (**user decision 2026-10-08**): `FixedGoldRandom` records the bounds passed to the gold draw and the order of draws across methods, which neither shared double does. It is rewritten in place as a local class that implements `IRandomProvider` — it does not subclass `System.Random`:
  - `NextDouble()` counts, appends `DRAW_ROLL` to `DrawOrder`, and returns the next value of an inner `SystemRandomProvider(new System.Random(PRNG_SEED))`.
  - `NextInt(min, max)` counts, appends `DRAW_GOLD`, records `LastNextMin` and `LastNextMax`, and returns the fixed gold value.
  - `NextFloat()` throws `InvalidOperationException` — Loot Table never draws a float.
  - The properties the tests read (`NextDoubleCalls`, `NextCalls`, `TotalDraws`, `LastNextMin`, `LastNextMax`, `DrawOrder`) keep their names, so the assertions at the bounds, order and zero-draw tests do not change.
  - This differs from the wording of ADR-013 Migration Plan step 2 ("replaced by the shared ones") for one of the four files. It satisfies Decision 6 (tests implement `IRandomProvider`, no `System.Random` subclass) and will pass the reflection test of Migration Plan step 3.
- The seven null-argument assertions of `LootTableService` (one per constructor parameter) keep each null in the same position.
- Seeded tests assert properties (counts, list lengths, membership), never a specific value sequence. Check each test that used `CountingRandom(FIXED_SEED)` for an assertion that depends on which entries dropped with that seed; such an assertion is rewritten with scripted draws.

**After this story**: `EnhancementService` is the only listed exception to the `System.Random` type rule (ADR-013 Decision 3). It migrates in the Enhancement migration story (Migration Plan step 3), which also adds the reflection test.

---

## Out of Scope

- Migrating `EnhancementService` and `EnhancementTestDoubles.cs`, and the reflection test for the `System.Random` ban (ADR-013 Migration Plan step 3).
- A server composition root that calls `RandomProviderFactory` (none exists; nothing outside tests constructs `LootTableService`).
- A thread check in `SystemRandomProvider`.
- Verifying `RandomNumberGenerator.Create()` in a stripped Linux IL2CPP server build (ADR-013 Verification Required 3).
- Any change to `ScriptedRandomProvider`, `RecordingRandomProvider`, `IRandomProvider` or `SystemRandomProvider`.
- Any change to drop chances, gold ranges, tier classification or distribution rules.

---

## QA Test Cases

**File**: `tests/EditMode/Randomness/RandomProviderFactory_tests.cs` (new).

- **Factory log line** — Given `LogAssert.Expect(LogType.Log, new Regex(@"^\[Random\] PRNG seed: -?\d+$"))`; When `RandomProviderFactory.CreateSeededFromEntropy(out int seed)`; Then the result is not null and the expected log was received. Nothing is asserted about the values drawn.

**File**: `tests/EditMode/LootTableSystem/LootTable_DropRoll_tests.cs` (changed).

- **One draw per entry** — the three existing count tests, with `RecordingRandomProvider`: `DoubleDrawCount` equals the entry count (3, 2, 0); `FloatDrawCount == 0` and `IntDrawCount == 0`.
- **Scripted outcomes** — the existing boundary tests (`0.0` drops at any positive chance; `0.99999999` drops at `DropChance = 1.0`; `0.5` against two entries) with `ScriptedRandomProvider`; expected drops unchanged.
- **Unexpected draw** — a table with one entry and an empty `ScriptedRandomProvider`; Then `InvalidOperationException` (the roller draws once per entry and the double fails the test when nothing is queued).
- **Null arguments** — null `table` and null `random` each throw `ArgumentNullException`.

**File**: `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` (changed).

- **Gold bounds and draw order** — existing test: `LastNextMin == GoldMin`, `LastNextMax == GoldMax + 1`, `DrawOrder == DRAW_ROLL + DRAW_GOLD`.
- **Tier shift** — existing test: the draw order with and without a tier shift is equal.
- **No draw** — existing test: `TotalDraws == 0`.
- **Null provider** — existing constructor test: null in the provider position throws `ArgumentNullException`.
- **No float draw** — covered by construction: the local double throws on `NextFloat()`, so every test in the file fails if the service draws one.

**Files**: `LootTable_GroundItemLifecycle_tests.cs`, `LootTable_RoundRobin_integration_tests.cs` (changed).

- Every existing test passes with queued draws; a test that queues too few draws fails with `InvalidOperationException`, which is how a miscounted draw is caught.

**Source search** (recorded in the completion notes, not automated here — the reflection test is Migration Plan step 3).

- `src/ServerLogic/` contains `new Random(` or `new System.Random(` only in `Randomness/RandomProviderFactory.cs`; no `UnityEngine.Random`; no `LootRandomFactory` in `src/` or `tests/`; no `: System.Random` or `: Random` in the four Loot Table test files.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Randomness/RandomProviderFactory_tests.cs` (new), and the four changed Loot Table test files — must exist and pass with the full EditMode suite.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Damage Calculation Story 003 (Complete, 2026-10-08, commit `f22c24b`) — it created `IRandomProvider`, `SystemRandomProvider`, `ScriptedRandomProvider` and `RecordingRandomProvider`; ADR-013 (Accepted 2026-10-08); Loot Table Story 014 (Complete).
- Unlocks: the Enhancement migration story (ADR-013 Migration Plan step 3), after which the `System.Random` exception list is empty and the reflection test is added.
