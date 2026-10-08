# Architecture Review Report — ADR-013

> **Date:** 2026-10-08
> **Engine:** Unity 6.3 LTS (6000.3)
> **ADR reviewed:** ADR-013 Server Random Provider (Proposed, `aa7998c`)
> **Mode:** `/architecture-review` — run as a review of ADR-013 only, plus a check of what changed project-wide since the last full review
> **Prior reviews:** `architecture-review-2026-10-07.md` (full, CONCERNS, 11 ADRs); `architecture-review-2026-10-08.md` and `architecture-review-2026-10-08-rereview.md` (ADR-012)
> **Verdict:** **ADR-013 has no blocking issue and can be marked Accepted.** Seven small items (R1–R7); R1–R3 are worth folding in first. The project-wide verdict stays **CONCERNS** because of the items carried from 2026-10-07, which this ADR does not touch.

## Scope Note

- **Read in full:** ADR-013, the three prior reviews listed above, `architecture-traceability.md`, `tr-registry.yaml`, `src/ServerLogic/LootTableSystem/LootRandomFactory.cs`, the header and acceptance criteria of Damage Calculation Story 003.
- **Read in part:** ADR-002, ADR-007, ADR-009, ADR-010, ADR-011, ADR-012 and the control manifest at the points ADR-013 relies on; the GDDs at every line ADR-013 cites (`damage-calculation.md`, `loot-table-system.md`, `enhancement-system.md`, `enemy-ai.md`) and, by search for "random" / "RNG" / "seed" / "chance", twenty other GDDs.
- **Source checks:** every use of `System.Random`, `UnityEngine.Random` and `RandomNumberGenerator` under `src/`; `System.Random` subclasses and `DamageCalculator` construction sites under `tests/`; `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`; the `.asmdef` files; `LootTableValidator.cs`.
- **Not run:** the `unity-specialist` consultation (the ADR uses no engine API beyond `Debug.Log`, and records a specialist validation from the authoring session); the full traceability matrix (Phases 2–3) — the GDDs were not re-read in full. `tr-registry.yaml` left empty, as before. `docs/consistency-failures.md` does not exist.

---

## Claims Checked

### Arithmetic (Decision 2) — re-derived by hand, confirmed
- `System.Random.Next()` returns at most 2³¹ − 2, so `sample >> 7` is an integer in [0, 2²⁴ − 1]; every such integer times 2⁻²⁴ is exactly representable as a `float`.
- The largest result is (2²⁴ − 1) × 2⁻²⁴ = `0.99999994f`. The top value has 127 source integers, every other value 128.
- P(`NextFloat() < c`) = ceil(c × 2²⁴) / 2²⁴: exactly `c` when `c` is a multiple of 2⁻²⁴ (0.75 is).
- `Next(min, max)` returns `min` when the two are equal and throws `ArgumentOutOfRangeException` when `min > max`, as Decision 1 states for `NextInt`.

### Against the repository — confirmed
- Six test doubles subclass `System.Random`: `ScriptedRandom` ×4, `CountingRandom`, `FixedGoldRandom`.
- Nine `new DamageCalculator(` sites in four test files; none in `src/`.
- 2009 test attributes under `tests/`.
- `LootTable_DropRoll_tests.cs:364` asserts the log line `[LootTable] PRNG seed:`.
- Nothing in `src/` constructs `LootTableService` or `EnhancementService` (no composition root yet).
- `EnhancementService.cs:143` draws one `NextDouble()`; `LootDropRoller.cs:38` one per entry; `LootTableService.cs:108` one `Next(GoldMin, GoldMax + 1)`.
- `SessionTokenStore` already uses `RandomNumberGenerator.Create()` — consistent with Decision 3's out-of-scope rule.
- `ServerOnlyNamespaces` exists in `AssemblyBoundaryLists.cs` and means what Decision 1 assumes: no type of a listed namespace in `Foundation` or `Client`, at least one in `ServerLogic`. One test assembly covers `tests/EditMode/`, so `tests/EditMode/Randomness/` needs no new `.asmdef`.
- `GoldMax + 1` cannot overflow: `LootTableValidator.cs:93` rejects `GoldMax > GOLD_CAP` (9,999,999). The check Migration Plan step 2 leaves to the story is already satisfied.

### Against the GDDs — confirmed
- `damage-calculation.md` Step 8 (line 64), F-DC-3 (190), the `0.0` edge case (289), the prerequisite note (440), AC-DC-F-09b (462) and OQ-DC-2 (542) are quoted accurately.
- `loot-table-system.md` CR-LT-1 (35), `enemy-ai.md` CR-AI-12 (66–74) and AC-AI-13 (452), `enhancement-system.md` outcome roll (60, 106) and "RNG is never drawn" on a rejected attempt (691) are quoted accurately.
- No other GDD names a generator, a seeding rule or a gameplay roll. The remaining "random" mentions are audio sample pools, test data and prose.

---

## Items — none blocking

### R1 — The predictability risk is weakest where it matters
- Risks, fifth bullet, rests on a client's draws being "interleaved with every other player's and mob's draws in an order it cannot observe", and says the difficulty has not been analysed.
- `enhancement-system.md` lines 10 and 125: Enhancement can only be started at the NPC in the town hub. The town hub is a zone, so it is its own process with its own generator (Decision 4, ADR-007).
- If the hub has no mobs (not checked in this review), that process's stream is almost entirely Enhancement rolls — one `NextDouble()` per attempt.
- A player can then make cheap +0 attempts off-peak. Each outcome is a threshold test on a known draw position; about a hundred of them carry enough information to recover a 2³¹-state seed offline. After that the player enhances a valuable item only when the next draw is a success.
- The seed written to the server log gives the same ability to anyone who can read the log.
- The MVP acceptance and the `security-engineer` gate can stand. The ADR should name this case in Risks, and state that the logged seed is sensitive.

### R2 — The bans in Decision 3 have no automated check
- Validation Criteria, third bullet: "a search of `src/ServerLogic/` finds `System.Random` only under `Randomness/`".
- `LootTableService.cs:20` (`private readonly Random _rng;`) and `LootDropRoller.cs:21` (`Roll(LootTableDefinition table, Random rng)`) use the unqualified name under `using System;`. A text search for `System.Random` would pass while the type is still there.
- ADR-012 gave the assembly rule a test; this ADR leaves its rule to a manual search.
- Suggested, in Migration Plan step 3: a reflection test beside the boundary test — no field, constructor parameter or method parameter of type `System.Random` in `IronGrind.ServerLogic` outside `IronGrind.Randomness`, and no type in the test assembly deriving from `System.Random`. `new System.Random(...)` and `UnityEngine.Random` inside method bodies are not visible to reflection and stay a search.

### R3 — `0.7499999f` is not the float adjacent to 0.75
- Floats in [0.5, 1) are 2⁻²⁴ apart. The one below 0.75 is 0.74999994; the next is 0.74999988.
- The literal `0.7499999` is nearer the second (1.9 × 10⁻⁸ away against 4.0 × 10⁻⁸), so `0.7499999f` is 0.74999988.
- `damage-calculation.md` AC-DC-F-09b calls it the "nearest representable float below 0.75"; ADR-013 Context repeats "exactly `0.7499999f`".
- The test passes either way. Only `0.74999994f` pins the strict `<` at the boundary.
- Add to the step 4 wording pass and to Story 003's QA cases.

### R4 — Interface naming rule
- ADR-010 line 104 and control manifest line 103: service interfaces are named `I[SystemName]Service`, with two grandfathered exceptions. `IRandomProvider` is neither.
- Same finding as 2026-10-07 Engine finding 5: the rule no longer matches about fifteen role-named interfaces in `src/`. Scope it to a system's facade interface at the next `/create-control-manifest update`, which this ADR triggers anyway.

### R5 — No startup step creates the provider
- The Ordering Note says no composition root exists and Decision 4 says what it must do.
- ADR-009 Decision 2's startup sequence has no service wiring step at all. This is carried item P4.
- Add "create the one `IRandomProvider` and pass it to every system" to ADR-009 Amendment 1.

### R6 — Draw counts are not in every consumer's GDD
- Decision 5: "Each consumer's GDD states how many draws one operation makes, and its tests assert that count."
- `enemy-ai.md` CR-AI-12 and AC-AI-13 do not say whether a draw is made when `EnragedChance` is 0.0. Damage Calculation does say it for `CritChance = 0.0`.
- Add to the step 4 wording pass.

### R7 — Small
- `ToUnitFloat` is documented for a sample in [0, `int.MaxValue`) but throws only for a negative one; `int.MaxValue` returns `0.99999994f`. Harmless.
- Damage Calculation Story 003 says "Estimate: 2 hours" and Manifest Version 2026-10-07. Migration Plan step 1 now also gives it the interface, the provider, two test doubles and the adapter tests.

---

## Cross-ADR Conflicts

None. ADR-013 against each ADR it touches:

| ADR | Relation | Result |
|---|---|---|
| ADR-007 | One process per zone instance (line 43) | Consistent with Decision 4 |
| ADR-011 | Tick code; persistence continuations run on the thread pool | Decision 4 (draw on the tick thread, pass the value in) matches Decision 3's synchronous-prefix snapshot |
| ADR-012 | Home assembly; `ServerOnlyNamespaces`; composition root in `ServerLogic` | Consistent; ADR-012 lists ADR-013 under Enables |
| ADR-002 | Navigation APIs on the main thread | No draw is made off the tick thread |
| ADR-010 | Naming rule | R4 |
| ADR-009 | Startup sequence | R5 |
| ADR-006 / session token | Cryptographic values | Out of scope by Decision 3; the code already uses `RandomNumberGenerator` |

C1 and C2 of 2026-10-07 are unchanged.

## ADR Dependency Order

No cycle. ADR-013 depends on ADR-011 and ADR-012, both Accepted.

```
Level 3:  ADR-009  Scene/Zone-Load Management      → ADR-002, ADR-004, ADR-007
          ADR-012  Server/Client Assembly Boundary → ADR-007, ADR-004, ADR-010
Level 4:  ADR-011  Async Persistence in the Tick   → ADR-006, ADR-007, ADR-010
Level 5:  ADR-013  Server Random Provider          → ADR-011, ADR-012          [Proposed]
```

Damage Calculation Story 003 stays Blocked while ADR-013 is Proposed.

## Engine Compatibility

Engine: Unity 6.3 LTS (6000.3). ADRs with an Engine Compatibility section: 13 / 13. No deprecated API referenced. No stale version reference. ADR-013 uses no post-cutoff API.

- The three Verification Required entries (range of `Next()`, no allocation under IL2CPP, `RandomNumberGenerator.Create()` in the stripped Linux server build) are attached to stories and stay open by design. The third is not new to this ADR: `SessionTokenStore` and `LootRandomFactory` make the same call today.
- `docs/engine-reference/unity/` has no entry on `System.Random`, IL2CPP stripping or the scripting runtime; nothing there contradicts the ADR.

## GDD Revision Flags

No GDD assumption conflicts with verified engine behaviour. Wording to correct after acceptance (not a status change): the three GDDs in Migration Plan step 4, plus AC-DC-F-09b's literal (R3) and the Enemy AI draw count (R6).

## Architecture Document Coverage

Unchanged: `architecture.md` is still at 8 ADRs.

---

## Project-Wide State

Nothing carried from 2026-10-07 has changed: C1, C2, P1 (inbound dispatch — now ADR-014), P2–P5, G1, G2, the `architecture.md` refresh, the empty TR registry (94 TR-IDs in use in EPIC files).

Two bookkeeping points:
- `architecture-review-2026-10-07.md` lines 103 and 216 call the inbound-dispatch decision "ADR-012", and the two ADR-012 reviews call it "ADR-013". It is ADR-014. Dated reports keep their text.
- The traceability index summary said the ADR-012 code was "not yet moved" while its own row said all nine systems had moved. Corrected with this review.

## Verdict

**ADR-013: ready for acceptance.** No blocking issue.

Suggested before the status changes, in one short authoring session: R1 (name the town hub case and the log in Risks), R2 (a reflection test in Migration Plan step 3), R3 (the literal). R4–R7 can ride with the follow-ups.

Then: the registry entries, `/create-control-manifest update`, the GDD wording pass (Migration Plan step 4, plus R3 and R6), Story 003's Implementation Notes and `/story-readiness`, and the two migration stories (Loot Table, Enhancement).

**Project-wide: CONCERNS**, unchanged.
