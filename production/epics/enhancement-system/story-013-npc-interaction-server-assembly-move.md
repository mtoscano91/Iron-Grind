# Story 013: Move NPC Interaction to the Server Assembly

> **Epic**: Enhancement System
> **Status**: Complete (2026-10-08)
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 1 hour

## Context

**GDD**: `design/gdd/enhancement-system.md` — AC-ENH-29 (CloseNPCInteraction clears the flag) and AC-ENH-30 (a zone transition clears `NPCInteractionActive`); `design/gdd/npc-shop.md` CR-SHOP-3 (opening a shop session). The per-character session flag is server state: it gates `ConfirmEnhancement`, and the client must not be able to read or set it.
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification), Decision 6 check 1 (boundary test), Migration Plan step 2 (NPC Interaction is fourth in the move order: "`INpcInteractionSessions` is used by Enhancement").
**ADR Decision Summary**: a system moves from `IronGrind.Foundation` to `IronGrind.ServerLogic` only after every system that uses its server types has moved. The only production code that uses an NPC Interaction type from outside its folder is `EnhancementService`, which moved in Enhancement Story 012. A move is a folder and assembly change only.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the same pattern compiled and passed the suite three times (Damage Calculation Story 006, Loot Table Story 014, Enhancement Story 012).
**Engine Notes**:
- Move every `.cs` together with its `.cs.meta`, and the folder `.meta`, with `git mv`, so GUIDs are preserved.
- The folder uses no `UnityEngine` type.
- Verify by running the EditMode suite. With the Editor closed, use batch mode, which leaves a results file. With the Editor open, the Test Runner writes its totals to `%USERPROFILE%/AppData/LocalLow/DefaultCompany/IronGrind/TestResults.xml`; read the total from there and check the file is newer than the move.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Feature layer)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list with a named client consumer, and leaves the suite green.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name.
- Forbidden: `InternalsVisibleTo` between production assemblies.

---

## Classification (ADR-012 Decision 3)

All 4 types of `src/Foundation/NpcInteraction/` (namespace `IronGrind.NpcInteraction`) go to `IronGrind.ServerLogic`. None has a client consumer today: no file in `src/Client/` and no wire codec names an NPC Interaction type.

| Type | Rule |
|---|---|
| `NpcInteractionSessionTracker` | 2 — server session state that gates `ConfirmEnhancement` |
| `INpcInteractionSessions` | interface lives with its consumer (`EnhancementService`, in `ServerLogic`) |
| `ITownHubQuery` | interface lives with its consumer (`NpcInteractionSessionTracker`); a server-side zone query |
| `NpcInteractionOpenResult` | result enum follows its producer |

**Expected to come back.** None is expected. The client learns the outcome of an open request from the wire messages `NPCInteractionOpened` and `RejectedNotInTownHub` (`design/gdd/networking-wire-protocol.md`), which are separate messages with no result-code body, so `NpcInteractionOpenResult` is not a wire enum. If a later amendment puts it in a message body, it moves back to `Foundation` with its consumer named on the shared allow-list.

---

## Acceptance Criteria

- [x] **Graph re-checked before the move**: a grep for the 4 type names and for `IronGrind.NpcInteraction` in `src/` outside the NPC Interaction folder finds code references only under `src/ServerLogic/`. If one is found in `src/Foundation/` or `src/Client/`, stop and report it.
- [x] **Moved**: the 4 files of `src/Foundation/NpcInteraction/` are in `src/ServerLogic/NpcInteraction/`; namespace `IronGrind.NpcInteraction` unchanged; `.meta` GUIDs unchanged (git shows 9 renames with no content change: 4 `.cs`, 4 `.cs.meta`, the folder `.meta`). `src/Foundation/NpcInteraction/` no longer exists.
- [x] **Lists updated**: `AssemblyBoundaryLists.NotYetMovedList` loses exactly its 4 NPC Interaction entries (153 → 149) and the "NPC Interaction" header; `SharedAllowList` is unchanged (26); `ServerOnlyNamespaces` gains `IronGrind.NpcInteraction`.
- [x] **No production code change**: no `.cs` under `src/` is edited. If a moved file turns out to reach an `internal` member of a `Foundation` type, that member becomes `public` (ADR-012 Decision 5) and the change is listed under Deviations.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning, and the total is written down from a results file. Expected: 2003, the total recorded from the results file after Enhancement Story 012 (no test is added or removed).

---

## Implementation Notes

1. **Re-check the graph** (first acceptance criterion). Checked at story creation, 2026-10-08: the only production code reference from outside the folder is `src/ServerLogic/EnhancementSystem/EnhancementService.cs`. Mentions of "NPC Interaction" elsewhere in `src/Foundation/` are doc comments.
2. **Move**: `git mv src/Foundation/NpcInteraction src/ServerLogic/NpcInteraction` and `git mv src/Foundation/NpcInteraction.meta src/ServerLogic/NpcInteraction.meta`. Do not edit the moved files.
3. **What the folder depends on** stays in `Foundation` and compiles, because `ServerLogic` references `Foundation`: Currency (`CharacterID`, on the shared allow-list) and `System` only.
   - Checked at story creation: the folder declares no `internal` type or member and imports no namespace other than `System`, `System.Collections.Generic` and `IronGrind.Currency`.
4. **Serialization checklist** — checked at story creation: no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]` or `[SerializeReference]` type in the folder. No `[MovedFrom]` and no asset re-save are needed. Re-run the grep (add `Type.GetType`, `AssemblyQualifiedName`, `TypeNameHandling`) and say so in the completion notes.
5. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`): delete the "NPC Interaction" block of `NotYetMovedList`, with the blank line after it, so the list starts with "Inventory"; add `"IronGrind.NpcInteraction",   // Enhancement Story 013` to `ServerOnlyNamespaces`. No change to `AssemblyBoundary_tests.cs` is needed.
6. **Tests**: the two test files that use NPC Interaction types (`tests/EditMode/Integration/EnhancementSystem/Enhancement_NpcInteractionSession_integration_tests.cs`, `EnhancementTestDoubles.cs`) do not move and need no edit; the test assembly already references `IronGrind.ServerLogic`.
7. **Verification**: see Engine Notes. Record the total, not only "all passed".

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.

---

## Out of Scope

- Moving any other system. Next in ADR-012's order: Inventory.
- The NPC Shop: it will be written against `src/ServerLogic/NpcInteraction/` from its first story.
- Wire messages for opening and closing an NPC interaction (`NPCInteractionOpened`, `RejectedNotInTownHub`) and any client screen.
- Stories 009, 010 and 011 of this epic (Blocked).
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the NPC Interaction type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, not edited) with `AssemblyBoundaryLists.cs` (edited).

- **Server-only namespaces** (`test_server_only_namespaces_have_no_type_outside_server_logic`, new data) — Given `ServerOnlyNamespaces` now lists `IronGrind.NpcInteraction`; Then no type of that namespace is defined in `IronGrind.Foundation` or `IronGrind.Client`, and at least one is defined in `IronGrind.ServerLogic`.
- **Every `Foundation` type is listed** (`test_every_foundation_type_is_listed`) — passes with the shortened list.
- **No stale entry** (`test_no_list_entry_is_stale`) — passes. *This is the test that fails if the NPC Interaction block is left on the list after the move.*
- **Regression** — the NPC interaction session integration tests and the other Enhancement tests pass unchanged.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` with the NPC Interaction namespace listed, and the boundary tests passing.
- The full EditMode suite result after the move, with the total, from a results file (batch-mode XML or the Test Runner's `TestResults.xml`).

**Status**: [x] Complete — 2003 of 2003 passed (Test Runner results file, 2026-10-08)

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Enhancement Story 012 (Complete — `EnhancementService`, the only user of `INpcInteractionSessions`, is in `ServerLogic`); Enhancement Story 006 (Complete — the code to move); Loot Table Story 014 (Complete — `ServerOnlyNamespaces` exists).
- Unlocks: the Inventory move story (fifth in ADR-012's order; `IInventoryService` and the inventory types are used by Enhancement and Loot Table, both moved).

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 5/5 passing
- Graph re-checked before the move: the only code reference from outside the folder is `src/ServerLogic/EnhancementSystem/EnhancementService.cs`.
- Moved: 9 git renames (4 `.cs`, 4 `.cs.meta`, the folder `.meta`), 0 lines changed.
- Lists: `NotYetMovedList` 153 → 149, `SharedAllowList` 26, `ServerOnlyNamespaces` gains `IronGrind.NpcInteraction`.
- No `.cs` under `src/` was edited.
- Suite: 2003 total, 2003 passed, 0 failed, 0 skipped — the user ran the EditMode suite in the Editor's Test Runner after the move; totals read from `TestResults.xml` (run 13:04, move 13:03). The boundary tests that read the lists pass in that file. The Editor log shows no compile error and no compiler warning.
**Serialization re-check**: the grep was re-run at implementation time and found no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]`, `[SerializeReference]`, `Type.GetType`, `AssemblyQualifiedName`, `TypeNameHandling` or `UnityEngine` use in the folder. No `[MovedFrom]` and no asset re-save.
**Deviations**: None
**Test Evidence**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (edited) with `AssemblyBoundary_tests.cs` (unchanged); full EditMode suite 2003/2003.
**Code Review**: Inline review of the 7-line list diff by the orchestrator; no specialist panel was run (same shape as the Story 012 diff reviewed the same day, and the compile and the suite confirm it).
