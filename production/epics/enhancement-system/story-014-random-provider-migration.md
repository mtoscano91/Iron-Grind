# Story 014: Migrate Enhancement to `IRandomProvider`

> **Epic**: Enhancement System
> **Status**: Complete (2026-10-09; written from ADR-013 Migration Plan step 3; `/story-readiness` 2026-10-09: READY)
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-10-08
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-9 and CR-ENH-10 (one uniform draw `r ∈ [0, 1)`; success when `r < P_s[k]`, otherwise destruction), F-ENH-4. No rule a player can observe changes in this story. The GDD does not name `System.Random`, so it needs no wording change.
**Requirement**: `TR-enh-003` (placeholder — `docs/architecture/tr-registry.yaml` is empty): "RNG injectable for tests". The contract that the injected type follows is ADR-013's.

**ADR Governing Implementation**: `docs/architecture/ADR-013-server-random-provider.md` — **Accepted 2026-10-08**. Migration Plan step 3: `EnhancementService` takes `IRandomProvider`; `ScriptedRandom` in `EnhancementTestDoubles.cs` is deleted in favour of `ScriptedRandomProvider`; the story adds a reflection test in `tests/EditMode/Architecture/`, beside the ADR-012 boundary test.
**ADR Decision Summary**: Decision 1 (the interface; Enhancement draws `NextDouble()`), Decision 3 (constructor injection into a `private readonly` field; the `System.Random` type rule and its reflection test — the exception list ends with this story), Decision 5 (one draw per Enhancement attempt, none on a rejected attempt; no carried state), Decision 6 (test doubles implement `IRandomProvider`; none subclasses `System.Random`).
**Also applies**: ADR-012 — `EnhancementService` is already in `IronGrind.ServerLogic` (Story 012), and `IronGrind.Randomness` is already in `ServerOnlyNamespaces` (Damage Calculation Story 003).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C# and `System.Reflection`. No Unity API is added.

**Control Manifest Rules (2026-10-08, Foundation → Server random provider)**:
- Required: a class that rolls takes `IRandomProvider` as a constructor parameter stored in a `private readonly` field (null → `ArgumentNullException`)
- Required: tests implement `IRandomProvider`; a test that does not care about the roll still supplies a double; a path that must not draw is asserted with an empty `ScriptedRandomProvider`
- Required: the `System.Random` type rule is asserted by a reflection test in `tests/EditMode/Architecture/`, beside the ADR-012 boundary test, from Migration Plan step 3
- Forbidden: `System.Random` as the type of a field, constructor parameter or method parameter in `IronGrind.ServerLogic`, except inside `SystemRandomProvider` and `RandomProviderFactory`; `new System.Random(...)` in `IronGrind.ServerLogic` anywhere except `RandomProviderFactory`; `UnityEngine.Random`; a static accessor for the provider; a `System.Random` subclass as a test double; asserting a specific value sequence in a seeded test
- Guardrail: no allocation per draw

---

## Acceptance Criteria

*From ADR-013 Migration Plan step 3 and its Validation Criteria. The Enhancement GDD criteria already covered by Stories 003–007 must keep passing with their expected values unchanged.*

- [ ] **Service**: `EnhancementService` takes `IRandomProvider` in the constructor position the `System.Random` has today (fifth and last), stores it in a `private readonly` field, and throws `ArgumentNullException` for null. The parameter keeps the name `random`.
- [ ] **Draw counts unchanged**: exactly one `NextDouble()` per attempt that passes validation; no draw on a rejected attempt; no `NextFloat()` and no `NextInt()` on any path.
- [ ] **No `System.Random` in Enhancement**: no field, constructor parameter or method parameter under `src/ServerLogic/EnhancementSystem/` has the type `System.Random`, written qualified or as `Random` under `using System;`.
- [ ] **Test double removed**: the class `ScriptedRandom` is deleted from `EnhancementTestDoubles.cs`; no file under `tests/` names `ScriptedRandom` except as part of `ScriptedRandomProvider`. The other doubles in that file are untouched.
- [ ] **Scripted tests**: `Enhancement_AttemptSequence_integration_tests.cs`, `Enhancement_NpcInteractionSession_integration_tests.cs`, `Enhancement_Rollback_integration_tests.cs` and `EnhancementSystem_OutcomeEvents_tests.cs` use the shared `ScriptedRandomProvider`. Every existing `DrawCount` assertion stays and keeps its expected value.
- [ ] **Validation tests**: `Enhancement_AttemptValidation_integration_tests.cs` passes an empty `ScriptedRandomProvider` where it passes `new System.Random(0)` today (five places). That file calls `ValidateAttempt` only, so an unexpected draw now fails the test.
- [ ] **Reflection test — type rule**: a test in `tests/EditMode/Architecture/` fails if any type in the `IronGrind.ServerLogic` assembly whose namespace is not `IronGrind.Randomness` (or nested under it) has a field, a constructor parameter or a method parameter of type `System.Random`. It covers public and non-public, instance and static members, nested types and compiler-generated types. The failure message lists every offending `Type.Member`.
- [ ] **Reflection test — no subclass**: a test fails if any type in the EditMode test assembly derives from `System.Random`, directly or indirectly; the failure message lists the types.
- [ ] **Reflection test — failure path probed**: a third test runs the same field/parameter checker on a private fixture type in the test assembly that has a `System.Random` field, a `System.Random` constructor parameter and a `System.Random` method parameter, and asserts that all three are reported. The fixture does not derive from `System.Random`.
- [ ] **Source search**: `src/ServerLogic/` contains `new Random(` or `new System.Random(` only in `Randomness/RandomProviderFactory.cs`, and `UnityEngine.Random` nowhere. Recorded in the completion notes.
- [ ] **Suite**: the full EditMode suite passes; expected total 2058 (2055 + 3). No expected value in an existing Enhancement test changes. If one does, it is listed in the completion notes.

---

## Implementation Notes

**Files to create**
- `tests/EditMode/Architecture/RandomTypeRule_tests.cs` — the three reflection tests. Test names follow the neighbouring file's style (`test_[scenario]_[expected]`). The `.meta` comes from the Editor.

**Files to change**
- `src/ServerLogic/EnhancementSystem/EnhancementService.cs` — add `using IronGrind.Randomness;`; field `private readonly System.Random _random` becomes `private readonly IRandomProvider _random`; constructor parameter `System.Random random` becomes `IRandomProvider random`; the call `_random.NextDouble()` (line 143 today) does not change; the `<param name="random">` doc comment points to `IRandomProvider.NextDouble` and ADR-013.
- `tests/EditMode/Integration/EnhancementSystem/EnhancementTestDoubles.cs` — delete the class `ScriptedRandom` and its doc comment. Remove `using System;` or `using System.Collections.Generic;` only if nothing else in the file needs them.
- `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptSequence_integration_tests.cs`, `Enhancement_NpcInteractionSession_integration_tests.cs`, `Enhancement_Rollback_integration_tests.cs`, `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` — add `using IronGrind.Tests.EditMode.Randomness;`; field type `ScriptedRandom` becomes `ScriptedRandomProvider`; `new ScriptedRandom()` becomes `new ScriptedRandomProvider()`; `_random.Enqueue(x)` becomes `_random.EnqueueDouble(x)` (13 calls across the four files). `_random.DrawCount` (15 assertions) stays: the shared double has the same property, and with only doubles queued it counts the same draws.
- `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` — add `using IronGrind.Tests.EditMode.Randomness;`; the five `new System.Random(0)` (the `SetUp` construction and the four null-argument tests) become `new ScriptedRandomProvider()`.

**Null-argument tests**: `Constructor_NullRandom_Throws` in the attempt-sequence file and the four null-collaborator tests in the validation file keep each null in the same position. None asserts a parameter name.

**Reflection test**
- Server assembly: `typeof(EnhancementService).Assembly` (or any other `IronGrind.ServerLogic` type). Test assembly: the assembly of the test class itself (`IronGrind.Foundation.EditModeTests`).
- Namespace exclusion: the type's namespace equals `IronGrind.Randomness` or starts with `IronGrind.Randomness.` — the same rule as `IsInNamespace` in `AssemblyBoundary_tests.cs`. That helper is private; either make it `internal static` and reuse it, or repeat the four lines. Do not change its behaviour.
- A nested type has the namespace of its outermost type, so a nested type of `SystemRandomProvider` is excluded and a nested type of `EnhancementService` is checked.
- Members: `GetFields`, `GetConstructors` and `GetMethods` with `Public | NonPublic | Instance | Static | DeclaredOnly`. Compiler-generated types are not skipped: a lambda that captures a `System.Random` shows up as a field of a closure class, and that is a real hold.
- The match is `FieldType == typeof(System.Random)` and `ParameterType == typeof(System.Random)`. Return types, properties (their backing fields are caught as fields), arrays and generic arguments (`List<Random>`) are outside ADR-013's wording and outside this test.
- The checker is one private static method that takes a `Type` and returns the offending member names, so the probe test can call it on the fixture.
- `ReflectionTypeLoadException`: handle as `GetTypesOrFail` does in `AssemblyBoundary_tests.cs`.

**After this story**: no `System.Random` type appears in `IronGrind.ServerLogic` outside `IronGrind.Randomness`, and the exception list of ADR-013 Decision 3 is empty. Two lines of `docs/architecture/control-manifest.md` (Foundation → Server random provider "Migration status", and the Forbidden entry on the `System.Random` type rule) still say "until step 3 is done … listed exceptions"; they are corrected at `/story-done`, not by the programmer.

---

## Out of Scope

- A server composition root that calls `RandomProviderFactory` (none exists; nothing outside tests constructs `EnhancementService`).
- The `security-engineer` verdict on generator predictability in the town hub (ADR-013 Risks; a gate before any public release, not before this story).
- Any change to `IRandomProvider`, `SystemRandomProvider`, `RandomProviderFactory`, `ScriptedRandomProvider` or `RecordingRandomProvider`.
- Any change to the probability table, outcome resolution or the attempt sequence.
- Stories 009–011 (Blocked) and the text of Stories 004, 005, 007 and 012, which mention `System.Random` or `ScriptedRandom` as history.
- Extending the type rule to return types, properties, arrays or generic arguments.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/RandomTypeRule_tests.cs` (new).

- **Type rule** — Given every type of the `IronGrind.ServerLogic` assembly outside `IronGrind.Randomness`; When its declared fields, constructor parameters and method parameters are read; Then none has the type `System.Random`. On failure the message lists each `Type.Member`.
- **No subclass** — Given every type of the EditMode test assembly; Then `typeof(System.Random).IsAssignableFrom(type)` is false for all of them.
- **Probe** — Given a private fixture class with a `System.Random` field, a constructor taking a `System.Random` and a method taking a `System.Random`; When the checker runs on it; Then it reports exactly those three members.

**Files**: the four scripted Enhancement test files (changed).

- Every existing test passes with `EnqueueDouble`; every existing `DrawCount` assertion keeps its value (1 after a resolved attempt, 0 after a rejection).
- A test that queues nothing and reaches the draw fails with `InvalidOperationException` — unchanged behaviour, now from the shared double.

**File**: `Enhancement_AttemptValidation_integration_tests.cs` (changed).

- Every existing test passes with an empty `ScriptedRandomProvider`; nothing in the file draws.

**Source search** (recorded in the completion notes).

- `new Random(` / `new System.Random(` in `src/ServerLogic/`: only `Randomness/RandomProviderFactory.cs`. `UnityEngine.Random` in `src/ServerLogic/`: none. `ScriptedRandom` not followed by `Provider` in `tests/`: none. `: System.Random` / `: Random` in `tests/`: none.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Architecture/RandomTypeRule_tests.cs` (new, 3 tests) and the five changed Enhancement test files — must exist and pass with the full EditMode suite (expected 2058).

**Status**: [x] Created and passing (2026-10-09)

**Test attribute counts at story creation (2026-10-08)**, for the diff review: attempt sequence 32, attempt validation 37, NPC interaction session 44, rollback 24, outcome events 25. None of these changes.

---

## Dependencies

- Depends on: Loot Table Story 015 (Complete, 2026-10-08, commit `d333f2a`) — after it `EnhancementService` is the only remaining exception; Damage Calculation Story 003 (Complete, commit `f22c24b`) — `IRandomProvider` and the shared doubles; Enhancement Story 012 (Complete) — the service is in `IronGrind.ServerLogic`; ADR-013 (Accepted 2026-10-08).
- Unlocks: ADR-013 Migration Plan complete (steps 1–5); the ADR's "after Migration Plan step 3" validation criterion can be checked.

---

## Completion Notes
**Completed**: 2026-10-09
**Criteria**: 11/11 passing. `/story-done` verdict: COMPLETE WITH NOTES.
**Suite**: EditMode 2059/2059, run by the user in the Editor's Test Runner after the review fixes; totals read from the Test Runner results file (`TestResults.xml`, 2026-10-09 09:08 local). The run after the implementation and before the review fixes was 2058/2058 (09:03 local). Baseline 2055.
**Expected values**: none changed in any existing Enhancement test.
**Deviations (advisory, from review fixes the user approved 2026-10-09)**:
- Suite total 2059, not 2058: the review added `BeginAttempt_ProviderThrows_UnlocksItemNothingPendingAndRethrows` (the provider throws at the draw, after the scroll is consumed — a path `System.Random` did not have).
- `Enhancement_AttemptSequence_integration_tests.cs`: 33 tests (story: 32) and 13 `DrawCount` assertions (story: 12), from that test. The two "exactly one draw" call-sequence tests also assert `FloatDrawCount == 0` and `IntDrawCount == 0`. The other four files are unchanged in count (37 / 44 / 24 / 25).
- `RandomTypeRule_tests.cs` is stricter than the story words it: the checker also matches `ref` / `in` / `out` parameters (`System.Random&`); the probe fixture has six members (static and instance field, constructor parameter, public and private method parameter, `out` parameter), not three; the probe is named `test_checker_reports_every_system_random_field_and_parameter_of_fixture`; the type-rule test also asserts that `EnhancementService` is scanned, that `SystemRandomProvider` is excluded, and that the checker reports `SystemRandomProvider` without the exclusion; the no-subclass test asserts the assembly name and that it has types.
- Manifest Version matches (2026-10-08).
**Source searches (2026-10-09)**: `new Random(` / `new System.Random(` in `src/ServerLogic/`: only `Randomness/RandomProviderFactory.cs:44`. `UnityEngine.Random` in `src/ServerLogic/`: none. `ScriptedRandom` not followed by `Provider` in `tests/`: none. `: System.Random` / `: Random` in `tests/`: none. `NextFloat` / `NextInt` under `src/ServerLogic/EnhancementSystem/`: none.
**Notes**:
- Implemented by `gameplay-programmer`; the diff was reviewed in the main session. One defect fixed before the first run: the probe expected `.#ctor(...)`, but `ConstructorInfo.Name` is `.ctor`.
- `docs/architecture/control-manifest.md`: the "Migration status" line and the Forbidden entry on the `System.Random` type rule no longer list exceptions.
- Review suggestions not applied, by choice: a `System.Random`-subclass check inside `IronGrind.ServerLogic` (beyond ADR-013's wording); renaming `test_test_assembly_has_no_system_random_subclass`.
**Code Review**: Complete — `/code-review` 2026-10-09 (`unity-specialist`, `qa-tester`; ADR-013 check in the main session): APPROVED WITH SUGGESTIONS, six fixes applied. `QL-TEST-COVERAGE` and `LP-CODE-REVIEW` skipped (lean mode).
**Tech debt**: TD-063 (reflection helpers duplicated between the two Architecture test files).
