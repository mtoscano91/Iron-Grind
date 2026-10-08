# Story 012: Move Enhancement to the Server Assembly

> **Epic**: Enhancement System
> **Status**: Complete (2026-10-08)
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 1.5 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — the attempt sequence (the server rolls the outcome, commits, then delivers the result; CR-ENH-14 server broadcast at +9). Success odds and destruction rules are values the player must not be able to read or replace.
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification; `EnhancementService` and `EnhancementConfig` are named there as `ServerLogic`), Decision 6 check 1 (boundary test), Migration Plan step 2 (Enhancement is third in the move order).
**ADR Decision Summary**: a system moves from `IronGrind.Foundation` to `IronGrind.ServerLogic` only after every system that uses its server types has moved. The only code that uses an Enhancement type from outside its folder is `DamageCalculator`, which moved in Damage Calculation Story 006. A move is a folder and assembly change only.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the same pattern compiled and passed the suite twice (Damage Calculation Story 006, Loot Table Story 014).
**Engine Notes**:
- Move every `.cs` together with its `.cs.meta`, and the folder `.meta`, with `git mv`, so GUIDs are preserved.
- `EnhancementService` uses `UnityEngine.Debug`; `IronGrind.ServerLogic` has engine references (`noEngineReferences: false`), so this compiles unchanged.
- Verify by running the EditMode suite. Prefer batch mode with the Editor closed, which leaves a results file; check for a running `Unity.exe` and `Temp/UnityLockfile` first.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Feature layer)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list with a named client consumer, and leaves the suite green.
- Required: feature services and their configs (`EnhancementService`, `EnhancementConfig`) are `IronGrind.ServerLogic`.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name.
- Forbidden: `InternalsVisibleTo` between production assemblies; placing enhancement odds or any other outcome formula in `IronGrind.Foundation`.

---

## Classification (ADR-012 Decision 3)

All 14 types of `src/Foundation/EnhancementSystem/` (namespace `IronGrind.EnhancementSystem`) go to `IronGrind.ServerLogic`. None has a client consumer today: no file in `src/Client/` and no wire codec names an Enhancement type.

| Group | Types | Rule |
|---|---|---|
| Service, config, constants | `EnhancementService`, `EnhancementConfig`, `EnhancementConstants`, `EnhancementBonusProvider` | 2 — odds, costs and the bonus formula |
| Server-side interface | `IEnhancementBonusProvider` | interface lives with its consumer (`DamageCalculator`, in `ServerLogic`) |
| Attempt records | `EnhancementAttemptStart`, `EnhancementAttemptResult`, `EnhancementAttemptValidation` (internal) | result types follow their producer |
| Enums | `EnhancementOutcome`, `EnhancementResultCode`, `PrestigeBand` | 4 |
| Event argument structs | `EnhancementSuccessEventArgs`, `EnhancementDestructionEventArgs`, `EnhancementBroadcastEventArgs` | 4 |

**Expected to come back.** Story 010 (client requests and result delivery, Blocked on TD-046) and the enhancement screen will need some of these on the client: most likely `EnhancementOutcome` and `EnhancementResultCode` (the result message), and `PrestigeBand` (the item glow is drawn from it). The stat bonus an item tooltip shows may also need a display formula in `Foundation` under ADR-012 Decision 7. Each moves back with its consumer named on the shared allow-list when that consumer is written. None is left in `Foundation` now, because the allow-list admits no entry without a named consumer.

---

## Acceptance Criteria

- [x] **Graph re-checked before the move**: a grep for the 14 type names and for `IronGrind.EnhancementSystem` in `src/` outside the Enhancement folder finds code references only under `src/ServerLogic/`. If one is found in `src/Foundation/` or `src/Client/`, stop and report it.
- [x] **Moved**: the 14 files of `src/Foundation/EnhancementSystem/` are in `src/ServerLogic/EnhancementSystem/`; namespace `IronGrind.EnhancementSystem` unchanged; `.meta` GUIDs unchanged (git shows renames with no content change). `src/Foundation/EnhancementSystem/` no longer exists.
- [x] **Lists updated**: `AssemblyBoundaryLists.NotYetMovedList` loses exactly its 14 Enhancement entries (167 → 153) and the "Enhancement" header; `SharedAllowList` is unchanged (26); `ServerOnlyNamespaces` gains `IronGrind.EnhancementSystem`.
- [x] **No production code change**: no `.cs` under `src/` is edited. If a moved file turns out to reach an `internal` member of a `Foundation` type, that member becomes `public` (ADR-012 Decision 5) and the change is listed under Deviations.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning, and the total is written down. Expected: the same total as before this story (no test is added or removed; 2003 after Loot Table Story 014, which itself was never confirmed from a results file).

---

## Implementation Notes

1. **Re-check the graph** (first acceptance criterion). Checked at story creation, 2026-10-08: the only code references from outside the folder are in `src/ServerLogic/DamageCalculation/DamageCalculator.cs` (`using IronGrind.EnhancementSystem;`, `IEnhancementBonusProvider`). The mentions in `src/Foundation/Networking/WireProtocol/PriorityPathQueue.cs` and `QueuedMessage.cs` are the string `"EnhancementOutcome"` inside doc-comment examples, not the type.
2. **Move**: `git mv src/Foundation/EnhancementSystem src/ServerLogic/EnhancementSystem` and `git mv src/Foundation/EnhancementSystem.meta src/ServerLogic/EnhancementSystem.meta`. Do not edit the moved files.
3. **What the folder depends on** stays in `Foundation` for now and compiles, because `ServerLogic` references `Foundation`: Character Stats ids, Currency (`CharacterID`), Inventory (`IInventoryService`, `InventorySlot`, `InventoryConstants`), Item Database, NPC Interaction (`INpcInteractionSessions`).
   - Checked at story creation: the folder reaches no `internal` member and no `internal` type of another system. The names `CharacterId` and `Outcome` that a grep turns up are Enhancement's own members; the folder does not import `IronGrind.Networking`.
   - The folder's own internals (`EnhancementAttemptValidation`, `EnhancementService.ValidateAttempt`) are used by tests only; `IronGrind.ServerLogic` already grants `InternalsVisibleTo` to the test assembly.
4. **Serialization checklist** — checked at story creation: no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]` or `[SerializeReference]` type and no `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use in the folder. No `[MovedFrom]` and no asset re-save are needed. Re-run the grep and say so in the completion notes.
5. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`): delete the "Enhancement" block of `NotYetMovedList`; add `"IronGrind.EnhancementSystem", // Enhancement Story 012` to `ServerOnlyNamespaces`. No change to `AssemblyBoundary_tests.cs` is needed.
6. **Tests**: the 9 Enhancement test files do not move and need no edit.
7. **Verification**: with the Editor closed, run the suite in batch mode and keep the totals from the results XML. If the Editor is open, ask the user to run the Test Runner and to report the total, not only "all passed".

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.

---

## Out of Scope

- Moving any other system. Next in ADR-012's order: NPC Interaction.
- Stories 009, 010 and 011 of this epic (Blocked). Their story files name paths under `src/Foundation/EnhancementSystem/`; those paths change to `src/ServerLogic/EnhancementSystem/` and are corrected when each story goes through `/story-readiness`.
- Wire messages for enhancement results and any enhancement UI; returning a type to `Foundation` for them.
- The server RNG injection decision (OQ-DC-2): `EnhancementService` keeps its injected `System.Random` as is.
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the Enhancement type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, not edited) with `AssemblyBoundaryLists.cs` (edited).

- **Server-only namespaces** (existing test, new data) — Given `ServerOnlyNamespaces` now lists `IronGrind.EnhancementSystem`; Then no type of that namespace is defined in `IronGrind.Foundation` or `IronGrind.Client`, and at least one is defined in `IronGrind.ServerLogic`.
- **Every `Foundation` type is listed** (existing) — passes with the shortened list.
- **No stale entry** (existing) — passes. *This is the test that fails if the Enhancement block is left on the list after the move.*
- **Regression** — the 9 Enhancement test files and the Damage Calculation tests (which use `IEnhancementBonusProvider`) pass unchanged.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` with the Enhancement namespace listed, and the boundary tests passing.
- The full EditMode suite result after the move, with the total: batch-mode results XML, or the user's Test Runner result including the count.

**Status**: [x] Complete — 2003 of 2003 passed (Test Runner results file, 2026-10-08)

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Damage Calculation Story 006 (Complete — the assemblies, the boundary test, and `DamageCalculator` already in `ServerLogic`); Loot Table Story 014 (Complete — `ServerOnlyNamespaces` exists); Enhancement Stories 001–008 (Complete — the code to move).
- Unlocks: the NPC Interaction move story (fourth in ADR-012's order; `INpcInteractionSessions` is used by Enhancement).

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 5/5 passing
- Graph re-checked before the move: the only code reference from outside the folder is `src/ServerLogic/DamageCalculation/DamageCalculator.cs`.
- Moved: 29 git renames (14 `.cs`, 14 `.cs.meta`, the folder `.meta`), 0 lines changed.
- Lists: `NotYetMovedList` 167 → 153, `SharedAllowList` 26, `ServerOnlyNamespaces` gains `IronGrind.EnhancementSystem`.
- No `.cs` under `src/` was edited. No `internal` member of a `Foundation` type is used by the moved files.
- Suite: 2003 total, 2003 passed, 0 failed, 0 skipped — the user ran the EditMode suite in the Editor's Test Runner after the move; totals read from the Test Runner results file (`TestResults.xml`, written 2026-10-08 12:54). The Editor log shows no compile error and no compiler warning after the move. The total matches the 2003 expected after Loot Table Story 014, which confirms that figure from a results file for the first time.
**Serialization re-check**: the grep was re-run at implementation time and found no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]`, `[SerializeReference]`, `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` in the folder; no asset references a moved script GUID. No `[MovedFrom]` and no asset re-save.
**Deviations**: None
**Test Evidence**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (edited) with `AssemblyBoundary_tests.cs` (unchanged); full EditMode suite 2003/2003.
**Code Review**: Complete — APPROVED (unity-specialist: clean; qa-tester: testable). Noted, no change: the server-only namespace test needs one type in `ServerLogic`, so "all 14 moved" rests on the git rename status and the compiler.
