# Story 015: Move Leveling to the Server Assembly

> **Epic**: Leveling System
> **Status**: Complete (2026-10-08)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 1.5 hours

## Context

**GDD**: `design/gdd/leveling-system.md` — the server owns leveling: the level-up loop (CR-2.9, CR-2.10), free-point allocation and the respec commit sequence (CR-4.3, CR-4.4) are validated and applied by `LevelingService`. A client that could run or replace this code could grant itself levels or stats.
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification, type by type), Decision 7 (what stays in `Foundation` for the client: display formulas, views, class definitions, `LevelUpEventArgs`, `ILevelingEventBroadcaster`), Decision 6 check 1 (boundary test), Migration Plan step 2 item 6.
**ADR Decision Summary**: this is the second half of ADR-012's Leveling step. Story 014 removed every reference to `LevelingService` from `IronGrind.Client`; this story moves the server types. A move is a folder and assembly change only.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the same pattern compiled and passed the suite five times; the split-folder variant (some files stay) was done in Inventory Story 011.
**Engine Notes**:
- Move each `.cs` together with its `.cs.meta` with `git mv`, so GUIDs are preserved. `src/Foundation/LevelingSystem/` and its `.meta` stay, because nine files remain in it.
- `src/ServerLogic/LevelingSystem.meta` does not exist until the Editor imports the new folder; it is generated on the first refresh and must be committed with the story.
- `LevelingService` uses `UnityEngine.Debug`; `IronGrind.ServerLogic` has engine references. Its `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` test seams compile the same way in either assembly.
- Verify by running the EditMode suite; read the total from the Test Runner's `TestResults.xml` and check the file is newer than the move.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Core layer)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list with a named client consumer, and leaves the suite green.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name.
- Forbidden: `InternalsVisibleTo` between production assemblies.

---

## Classification (ADR-012 Decision 3 and Decision 7)

`src/Foundation/LevelingSystem/` (namespace `IronGrind.LevelingSystem`) holds 14 types. 5 go to `IronGrind.ServerLogic`; 9 stay in `IronGrind.Foundation`.

| Type | Destination | Rule | List today |
|---|---|---|---|
| `LevelingService` | `ServerLogic` | 2 — validates and mutates authoritative state | not-yet-moved |
| `RespecTwoPhaseCommitCoordinator` | `ServerLogic` | 2 — persistence coordination | not-yet-moved |
| `IItemReservation` | `ServerLogic` | interface lives with its consumer (`RespecTwoPhaseCommitCoordinator`) | not-yet-moved |
| `LevelingStateSnapshot` | `ServerLogic` | 4 — the save/load shape; no client consumer | not-yet-moved |
| `AllocateFreePointResult` | `ServerLogic` | result type follows its producer | not-yet-moved |
| `ILevelingEventBroadcaster` | `Foundation` | 3 — ADR-012 Decision 7 names it; consumer `LevelUpOverlayPresenter` | not-yet-moved → shared |
| `LevelUpEventArgs` | `Foundation` | 3 — ADR-012 Decision 7 names it; consumer `LevelUpOverlayPresenter` | not-yet-moved → shared |
| `XpThresholdTable` | `Foundation` | Decision 7 display data — "the experience threshold for a level"; consumer `PlayerResourceClusterPresenter` (the XP bar), which receives the table as a list | not-yet-moved → shared |
| `ClassDefinition`, `IClassRegistry`, `ClassRegistry` | `Foundation` | Decision 7 (already decided) | shared |
| `LevelingDisplayFormulas`, `ILocalPlayerLevelingView`, `IRespecRequestSender` | `Foundation` | Decision 7 (Story 014) | shared |

**`XpThresholdTable` is the one judgment call.** No production code names it today: the HUD is handed a threshold list, and only its own test file reads the table. It stays shared because Decision 7 lists the experience threshold among the display formulas, the values are shown to the player as the XP bar, and the client composition root will hand `XpThresholdTable.Values` to the HUD. Its allow-list comment says the wiring is planned. The alternative, moving it and bringing it back when the client composition root is written, is rule 4 read strictly.

`IronGrind.CharacterStats.ILevelingService` (the interface `LevelingService` implements for `CharacterStats`) is in the Character Stats folder and is not part of this story.

Because the namespace keeps shared types, `IronGrind.LevelingSystem` is **not** added to `ServerOnlyNamespaces` (its doc comment already names Leveling among the namespaces that keep shared types).

---

## Acceptance Criteria

- [x] **Graph re-checked before the move**: a grep for the 5 moving type names in `src/` and `Assets/` outside `src/Foundation/LevelingSystem/` finds code references only under `src/ServerLogic/` and `src/DevHarness/`. If one is found in `src/Foundation/`, `src/Client/` or `Assets/`, stop and report it. Doc comments and `//` comments do not count.
- [x] **Moved**: `LevelingService.cs`, `RespecTwoPhaseCommitCoordinator.cs`, `IItemReservation.cs`, `LevelingStateSnapshot.cs` and `AllocateFreePointResult.cs` are in `src/ServerLogic/LevelingSystem/`; namespace unchanged; `.cs.meta` GUIDs unchanged (git shows 10 renames with no content change). `src/Foundation/LevelingSystem/` holds exactly the 9 files of the types that stay, and their `.meta` files.
- [x] **New folder `.meta` committed**: `src/ServerLogic/LevelingSystem.meta` exists (generated by Unity) and is part of the changeset.
- [x] **Lists updated**: `NotYetMovedList` loses all 8 Leveling entries and the "Leveling" header (127 → 119); `SharedAllowList` gains `ILevelingEventBroadcaster`, `LevelUpEventArgs` and `XpThresholdTable`, each with its consumer in a comment (31 → 34); `ServerOnlyNamespaces` is unchanged.
- [x] **No behaviour change**: no moved file is edited. The only `.cs` edits allowed under `src/` are doc comments whose `<see cref>` names a moved type from a file that can no longer see it (Implementation Notes 4). If a moved file turns out to reach an `internal` member of a `Foundation` type, that member becomes `public` (ADR-012 Decision 5) and the change is listed under Deviations.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning; the total is read from a results file. Expected: 2008 (no test is added or removed).

---

## Implementation Notes

1. **Re-check the graph** (first acceptance criterion). Checked at story creation, 2026-10-08: outside the folder, the only code that names a moving type is in `src/DevHarness/UI/LevelingSystem/` (`LevelingHudManualTestHarness.cs` and `HarnessLevelingAdapter.cs` use `LevelingService`), and `IronGrind.DevHarness` references `IronGrind.ServerLogic`. The one hit under `src/Client/` (`RespecScreenPresenter.cs`) is a trailing `//` comment.
2. **Move**: create `src/ServerLogic/LevelingSystem/`, then `git mv` each of the 5 `.cs` files and its `.cs.meta` into it. Do not move the other 9 files or `src/Foundation/LevelingSystem.meta`.
3. **What the moved files depend on** stays in `Foundation` and compiles: Character Stats (`CharacterStats`, `EntityID`, `StatID`, `ILevelingService`), and the shared Leveling types (`IClassRegistry`, `ClassDefinition`, `LevelUpEventArgs`, `ILevelingEventBroadcaster`, `LevelingDisplayFormulas`).
   - Checked at story creation: the Leveling folder uses no `internal` member of Character Stats (its only internal member, `StatSlotCount`, is not used here) and imports no namespace other than `System`, `System.Collections.Generic` and `UnityEngine`. Re-check at implementation: Leveling shared an assembly with everything it uses, so an `internal` use would have compiled until now.
   - The service's own `internal` test seams (`TestOnly_ThrowDuringRespecStep2`, `TestOnly_ThrowAfterRecomputeDerivedStats`) are used by tests only; `IronGrind.ServerLogic` grants `InternalsVisibleTo` to the test assembly.
4. **Doc comments that name a moved type from an assembly that can no longer see it.** After the move, `<see cref="LevelingService…"/>` (and any cref to the other four moved types) in a `Foundation` or `Client` file no longer resolves. Replace each with `<c>…</c>` text and change nothing else. Known at story creation: `ClassDefinition.cs`, `ClassRegistry.cs`, `LevelUpEventArgs.cs`, `XpThresholdTable.cs`, `LevelingDisplayFormulas.cs` (all `src/Foundation/LevelingSystem/`), `src/Foundation/CharacterStats/CharacterStats.cs`, and `src/Client/UI/LevelingSystem/PlayerResourceClusterPresenter.cs`. Grep `src/Foundation/` and `src/Client/` for `cref="[^"]*(LevelingService|AllocateFreePointResult|IItemReservation|LevelingStateSnapshot|RespecTwoPhaseCommitCoordinator)` and fix every hit. Crefs to `IronGrind.CharacterStats.ILevelingService` are a different type and stay.
5. **Serialization checklist** — checked at story creation: no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]`, `[SerializeReference]`, `[Serializable]`, `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use in the folder. No `[MovedFrom]` and no asset re-save are needed. Re-run the grep and say so in the completion notes.
6. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`):
   - Delete the "Leveling" block of `NotYetMovedList` (8 entries), with the blank line after it, so the list starts with "Networking".
   - Add to the "Leveling: client read model (ADR-012 Decision 7)" group of `SharedAllowList`:
     ```
     "IronGrind.LevelingSystem.ILevelingEventBroadcaster", // consumer: LevelUpOverlayPresenter
     "IronGrind.LevelingSystem.LevelUpEventArgs",          // consumer: LevelUpOverlayPresenter
     "IronGrind.LevelingSystem.XpThresholdTable",          // consumer: PlayerResourceClusterPresenter (XP bar; receives Values as a list — wiring by the client composition root is planned)
     ```
   - Do not edit `ServerOnlyNamespaces` or `AssemblyBoundary_tests.cs`.
7. **Tests**: the files of `tests/EditMode/LevelingSystem/` and the other tests that use `LevelingService` (Character Stats, Damage Calculation fakes) do not move and need no edit.
8. **Verification**: after the Editor refresh, confirm `src/ServerLogic/LevelingSystem.meta` exists and stage it. Record the suite total, not only "all passed". The manual harness is in `IronGrind.DevHarness`, which already references `ServerLogic`; a Play-mode check that its buttons still work is worth doing once, since the harness constructs `LevelingService`.

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.

---

## Out of Scope

- Moving any other system. Next in ADR-012's order: Networking, server part (classified file by file first).
- `IronGrind.CharacterStats.ILevelingService` and everything else in the Character Stats folder (ninth in the order), including the stats view (`ILocalPlayerStatsView`).
- The client composition root that hands `XpThresholdTable.Values` to the HUD, and the networked implementations of `ILocalPlayerLevelingView` and `IRespecRequestSender`.
- Updating ADR-012 and the control manifest for Verification Required 8 (confirmed in Story 014).
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the 5 moved type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, not edited) with `AssemblyBoundaryLists.cs` (edited).

- **Every `Foundation` type is listed** (`test_every_foundation_type_is_listed`) — Given the 9 Leveling types left in `IronGrind.Foundation`; Then all are on `SharedAllowList`. *Fails if one of the three newly shared types is deleted from the not-yet-moved list without being added to the allow-list.*
- **No stale entry** (`test_no_list_entry_is_stale`) — passes. *Fails if a moved type is left on either list.*
- **No type on both lists** (`test_no_type_is_on_both_lists_or_duplicated`) — passes.
- **No `ServerLogic` type in a `Foundation` or `Client` signature** (`test_no_server_logic_type_in_foundation_or_client_signature`) — passes. *This is the test that would fail if a presenter or a shared interface still carried `LevelingService` or `AllocateFreePointResult`.*
- **Regression** — the Leveling, Character Stats and Damage Calculation tests pass unchanged.

Not covered by an automated test: that each of the 5 types is in `IronGrind.ServerLogic` rather than deleted. The compiler covers it (the tests and the harness use them), and the git rename status is the record.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` with the three shared entries, and the boundary tests passing.
- The full EditMode suite result after the move, with the total, from a results file.

**Status**: [x] Complete — 2008 of 2008 passed (Test Runner results file, 2026-10-08)

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Leveling Story 014 (Complete — no `Client` file names `LevelingService`; the harness is in `IronGrind.DevHarness`); Leveling Stories 001–013 (Complete — the code to move); Damage Calculation Story 006 (Complete — the assemblies and the boundary test).
- Unlocks: the Networking server-part move (seventh in ADR-012's order), which starts with a file-by-file classification of 108 types.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 6/6 passing
- Graph re-checked before the move: outside the folder, only `src/DevHarness/UI/LevelingSystem/` (`LevelingHudManualTestHarness`, `HarnessLevelingAdapter`) uses a moving type in code.
- Moved: 10 git renames (5 `.cs`, 5 `.cs.meta`), 0 lines changed. `src/Foundation/LevelingSystem/` holds the 9 files of the types that stay.
- New folder `.meta`: `src/ServerLogic/LevelingSystem.meta` generated by Unity (16:07:19) and staged.
- Lists: `NotYetMovedList` 127 → 119; `SharedAllowList` 31 → 34 (`ILevelingEventBroadcaster`, `LevelUpEventArgs`, `XpThresholdTable`); `ServerOnlyNamespaces` unchanged.
- No moved file was edited. 10 doc-comment `cref`s to moved types became `<c>` text in 7 files (`CharacterStats.cs` 1, `ClassDefinition.cs` 1, `ClassRegistry.cs` 1, `LevelingDisplayFormulas.cs` 1, `LevelUpEventArgs.cs` 1, `XpThresholdTable.cs` 3, `PlayerResourceClusterPresenter.cs` 2); no non-comment line under `src/` changed.
- Suite: 2008 total, 2008 passed, 0 failed — run started 16:07:26, after the Editor imported the move; totals read from `TestResults.xml`. All 9 boundary tests pass. The Editor log shows no compile error and no compiler warning. An earlier run at 16:05 was reported green but predated the import and is not the evidence.
**Internals and serialization re-check**: the 5 moved files use no `internal` member declared elsewhere in `Foundation`; no serialization attribute or by-name type lookup in the folder. No `[MovedFrom]` and no asset re-save.
**Harness**: `IronGrind.DevHarness` compiles against `LevelingService` in `ServerLogic`. The user reported the harness works; the Editor log shows no Play-mode entry after the move, so that report is not corroborated from the log. Not an acceptance criterion of this story.
**Deviations**: None
**Test Evidence**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (edited) with `AssemblyBoundary_tests.cs` (unchanged); full EditMode suite 2008/2008.
**Code Review**: Inline review of the diff by the orchestrator; no specialist panel was run.
