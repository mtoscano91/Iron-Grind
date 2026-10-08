# Story 032: Move Networking Server State to the Server Assembly (1 of 3)

> **Epic**: Networking Core
> **Status**: Complete (2026-10-08)
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/networking-session.md` and `design/gdd/networking-wire-protocol.md` — the server is authoritative: it runs the 20 Hz tick loop, the connection and zone session state machines, ghost sessions, the commit-before-broadcast and irreversible-outcome sequences, and the inbound RPC guards. None of that runs on a client.
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification), Decision 4 (message schemas and codecs stay in `Foundation`; server handlers and state are `ServerLogic`), Decision 6 check 1 (boundary test), Migration Plan step 2 item 7 ("Networking, server part … classified file by file first; wire schemas and ids stay").
**ADR Decision Summary**: the 108 `IronGrind.Networking` types were classified on 2026-10-08 and the user accepted the result: 62 go to `IronGrind.ServerLogic`, 46 stay in `IronGrind.Foundation`, none goes to `IronGrind.Client`. The move is done in three stories. This is the first: the twelve sub-folders that are server-only as a whole.

| Story | Moves | Types |
|---|---|---|
| **032 (this one)** | twelve whole sub-folders: state machines, tick loop, ghost sessions, gates, RPC guards | 40 |
| 033 (not yet written) | the server half of `WireProtocol/` (packet writers, queues, buffers, relevance filter, trackers), with two `BatchSubMessageCodec` methods made `public` | 13 |
| 034 (not yet written) | `TestHarness/` except the observer interface; the 46 shared types go from the not-yet-moved list to the shared allow-list | 9 |

The three groups can move in any order: the only reference between them is from this story's group to `ZoneBufferPool` (Story 033's group), and `ServerLogic` may reference a type that is still in `Foundation`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the same pattern compiled and passed the suite six times.
**Engine Notes**:
- Each sub-folder moves whole: `git mv` the folder and its folder `.meta`, so every `.cs.meta` and folder GUID is preserved.
- `src/ServerLogic/Networking/` is a new folder; Unity generates `src/ServerLogic/Networking.meta` on the first refresh, and it must be committed with the story.
- Several files use `UnityEngine.Debug` and `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`; both behave the same in `IronGrind.ServerLogic`.
- Verify by running the EditMode suite; read the total from the Test Runner's `TestResults.xml` and check the run is newer than the Editor's import of the move (the new folder `.meta` timestamp).

**Control Manifest Rules (Foundation layer → Server/client assembly boundary)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, and leaves the suite green.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name.
- Forbidden: `InternalsVisibleTo` between production assemblies.

---

## Classification (ADR-012 Decision 3) — this story's 40 types

All are rule 2 (server state machines, the tick loop, persistence coordination, RPC guards, server-side validation) or result and data types that follow their producer. Source: the 2026-10-08 classification, made from each type's doc comment and a whole-word reference scan, not from a reading of every method body.

| Sub-folder (files) | Types |
|---|---|
| `CommitBeforeBroadcast/` (2) | `CommitBeforeBroadcastSequencer`, `CommitBeforeBroadcastResult` |
| `ConnectionStateMachine/` (4) | `ConnectionStateMachine`, `PendingPurchaseRecord`, `RespecReservationStatus`, `SessionHandshakeData` |
| `EnhancementRequestDedup/` (1) | `EnhancementRequestDeduplicator`, `EnhancementRequestDedupResult` |
| `GhostSession/` (9) | `GhostCleanupSequencer`, `GhostDismissalCoordinator`, `GhostEntityTracker`, `GhostXpPoolTracker`, `MobDeTargetingCoordinator`, `PartyDisbandCoordinator`, `PreDisconnectSnapshot`, `PreDisconnectSnapshotWal`, `GhostZoneCrashSession`, `ZoneCrashCleanupHandler` |
| `IrreversibleOutcome/` (4) | `IIrreversibleOutcomeCoordinator`, `IrreversibleOutcomeBeginResult`, `IrreversibleOutcomeCoordinator`, `IrreversibleWriteFailureProtocol` |
| `MutationGate/` (2) | `ICharacterMutationGate`, `CharacterMutationGate` |
| `OwlCompensation/` (3) | `LastBeatServerTickTracker`, `OwlThresholdHysteresisTracker`, `OwlWrapCorrectionFormula`, `SkillGraceWindowResult` |
| `RpcGuards/` (4) | `CrossCuttingRpcGuardChain`, `InboundRpcDescriptor`, `RpcGuardResult`, `RpcTypeTag` |
| `SessionToken/` (1) | `SessionTokenStore` |
| `TickCompletion/` (4) | `ITickCompletionQueue`, `TickCompletionQueue`, `TickCompletionConstants`, `TickTaskStatus`, `TickTaskResult<T>` |
| `TickLoop/` (1) | `ServerTickLoop` |
| `ZoneSessionStateMachine/` (1) | `ZoneSessionStateMachine` |

36 files, 40 top-level types. The sub-folders that stay in `src/Foundation/Networking/` after this story are `WireProtocol/`, `TestHarness/`, `ZoneSnapshotReassembly/` and the five root files.

---

## Acceptance Criteria

- [x] **Graph re-checked before the move**: no non-comment line in `src/Foundation/` outside the twelve sub-folders, in `src/Client/` or in `src/DevHarness/` names one of the 40 types. Expected code users outside the folders: only `src/ServerLogic/` (`InventoryConstants` uses `ServerTickLoop.TICK_RATE_HZ`). If a code reference is found in `Foundation`, `Client` or `DevHarness`, stop and report it.
- [x] **No internal access across the new boundary**: none of the 36 files uses an `internal` member or type declared in a file that stays in `src/Foundation/` (the rest of Networking, Currency, Character Stats, the shared Leveling and Inventory files, Item Database). If one is found, stop and report it: the member becomes `public` under ADR-012 Decision 5, and that is listed under Deviations.
- [x] **Moved**: the twelve sub-folders are under `src/ServerLogic/Networking/`, each with its folder `.meta`; namespace `IronGrind.Networking` unchanged; git shows 84 renames with no content change (36 `.cs`, 36 `.cs.meta`, 12 folder `.meta`). None of the twelve remains under `src/Foundation/Networking/`.
- [x] **New folder `.meta` committed**: `src/ServerLogic/Networking.meta` exists (generated by Unity) and is part of the changeset.
- [x] **Lists updated**: `NotYetMovedList` loses exactly the 40 entries of the table above (119 → 79; `TickTaskResult` is listed as ``TickTaskResult`1``); the "Networking" header stays, since 68 Networking entries remain. `SharedAllowList` (34) and `ServerOnlyNamespaces` are unchanged.
- [x] **No behaviour change**: no moved file is edited. The only `.cs` edits allowed under `src/` are the doc comments of Implementation Notes 4.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning; the total is read from a results file whose run started after the Editor imported the move. Expected: 2008 (no test is added or removed).

---

## Implementation Notes

1. **Re-check the graph** (first criterion). Checked at story creation, 2026-10-08, by a whole-word scan of non-comment lines:
   - No file classified as shared names any of the 62 server types in code (one string literal in `MessageRoutingRegistry.cs:124` mentions `RelevanceFilter`).
   - The `WireProtocol` server half and the `TestHarness` types name none of this story's 40 types.
   - No file under `src/Foundation/Currency/`, `src/Foundation/CharacterStats/`, `src/Client/` or `src/DevHarness/` uses a Networking type in code. The three hits (`EntityID.cs`, `ItemID.cs`, `CharacterID.cs`) are doc comments.
2. **What the moved files depend on** stays in `Foundation` and compiles: the shared wire types and enums; `ZoneBufferPool.MAX_PLAYERS_PER_ZONE` (public const; `LastBeatServerTickTracker`); `INetworkTestObserver` and `PersistenceWriteReason`; Currency (`ICurrencyService`, `GoldMutationResult`, `CharacterID`, used by `ConnectionStateMachine`); Character Stats ids.
   - Checked at story creation: no `internal` member declared in the `WireProtocol` server half or in `TestHarness` is accessed from the 36 files, and the shared files' `internal` members used from server files are the two `BatchSubMessageCodec` methods called by `RelevanceFilter` — Story 033's concern, not this one's. The Currency and Character Stats internals (`TryCompareAndSwapSpend`, `StatSchema`) are not used by any Networking file. Re-check at implementation (second criterion): these files have shared an assembly with everything they use until now.
   - `MobDeTargetingCoordinator.IssueDeTargetCommands` is `internal` and is called by `GhostDismissalCoordinator`; both move together.
3. **Move**: create `src/ServerLogic/Networking/`, then for each of the twelve sub-folders `git mv src/Foundation/Networking/<Sub> src/ServerLogic/Networking/<Sub>` and the same for `<Sub>.meta`. Do not move `WireProtocol`, `TestHarness`, `ZoneSnapshotReassembly`, the root files or `src/Foundation/Networking.meta`. Do not edit the moved files.
4. **Doc comments in shared files that name a moved type.** Two files that stay in `Foundation` for good hold `<see cref>` references to this story's types: `TestHarness/INetworkTestObserver.cs` (5) and `ZoneSnapshotReassembly/ZoneSnapshotReassemblyTracker.cs` (6). Replace each with `<c>…</c>` text and change nothing else. Leave the crefs in the `WireProtocol` server-half files (`GoldSyncForcedDeliveryTracker`, `RelevanceFilter`, `SetTargetOutcome`, `TargetSlotTracker`): those files join the moved types in `ServerLogic` in Story 033, where the references resolve again. XML documentation output is not enabled, so an unresolved cref raises no warning in the meantime.
5. **Serialization checklist** — checked at story creation by grep: no `MonoBehaviour`, `NetworkBehaviour`, `ScriptableObject`, `[Serializable]`, `[SerializeField]` or `[SerializeReference]` declaration, and no `Type.GetType(string)`, `AssemblyQualifiedName` or `TypeNameHandling` use in the Networking folder (`ex.GetType().Name` in `CommitBeforeBroadcastSequencer` is not a by-name lookup). No `[MovedFrom]` and no asset re-save are needed. Re-run the grep on the 36 files and say so in the completion notes.
6. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`): delete the 40 entries from the "Networking" block of `NotYetMovedList`, leaving the other 68 in place and in order. Do not edit `SharedAllowList`, `ServerOnlyNamespaces` or `AssemblyBoundary_tests.cs`.
7. **Tests**: no test file moves or changes. The Networking tests use `internal` members of the moved types; `IronGrind.ServerLogic` already grants `InternalsVisibleTo` to the test assembly, as `IronGrind.Foundation` does.
8. **Verification**: after the Editor refresh, confirm `src/ServerLogic/Networking.meta` exists and stage it. Record the suite total, not only "all passed".

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.

---

## Out of Scope

- The `WireProtocol` server half and the two `BatchSubMessageCodec` methods (Story 033).
- `TestHarness/` and the reclassification of the 46 shared types onto the shared allow-list (Story 034).
- Correcting ADR-012's stated reason for the order ("Currency uses Networking types" — in code the references run from Networking to Currency). Wording only; do it in the next ADR authoring pass.
- Currency and Character Stats (eighth and ninth in ADR-012's order).
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the moved type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, not edited) with `AssemblyBoundaryLists.cs` (edited).

- **No stale entry** (`test_no_list_entry_is_stale`) — passes. *Fails if any of the 40 entries is left on the not-yet-moved list.*
- **Every `Foundation` type is listed** (`test_every_foundation_type_is_listed`) — passes. *Fails if an entry is deleted for a type that did not move.*
- **No `ServerLogic` type in a `Foundation` or `Client` signature** (`test_no_server_logic_type_in_foundation_or_client_signature`) — passes. *This is the test that would fail if a shared wire type or the observer interface carried one of the 40 types in a field, parameter or return type; the compiler fails first.*
- **Regression** — every Networking test file, and the Inventory, Loot Table and Enhancement tests, pass unchanged.

Not covered by an automated test: that each of the 40 types is in `IronGrind.ServerLogic` rather than deleted. The compiler covers it (the Networking tests use them), and the git rename status is the record.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` with the 40 entries removed, and the boundary tests passing.
- The full EditMode suite result after the move, with the total, from a results file.

**Status**: [x] Complete — 2008 of 2008 passed (Test Runner results file, 2026-10-08)

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); the 2026-10-08 Networking classification (accepted by the user); Inventory Story 011, Loot Table Story 014, Enhancement Story 012 and Leveling Story 015 (Complete — every system that uses these Networking types is already in `ServerLogic`); Networking Core Stories 001–031 (Complete — the code to move).
- Unlocks: Stories 033 and 034 (independent of this one, but written after it), then the Currency move.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 7/7 passing
- Graph re-checked before the move: one whole-word scan found no code use of the 40 types in `src/Foundation/` outside the twelve sub-folders, in `src/Client/` or in `src/DevHarness/`.
- No internal access across the new boundary: the 36 files use no `internal` member or type declared in a file that stays in `Foundation`.
- Moved: 84 git renames (36 `.cs`, 36 `.cs.meta`, 12 folder `.meta`), 0 lines changed. `src/Foundation/Networking/` holds `WireProtocol/`, `TestHarness/`, `ZoneSnapshotReassembly/` and the five root files.
- New folder `.meta`: `src/ServerLogic/Networking.meta` generated by Unity (16:31:26) and staged.
- Lists: the 40 entries removed from `NotYetMovedList` were compared with this story's table and match exactly (119 → 79; 68 Networking entries remain). `SharedAllowList` 34 and `ServerOnlyNamespaces` unchanged.
- No moved file was edited. 11 doc-comment `cref`s became `<c>` text (`INetworkTestObserver.cs` 5, `ZoneSnapshotReassemblyTracker.cs` 6); no non-comment line under `src/` changed.
- Suite: 2008 total, 2008 passed, 0 failed — run started 16:31:33, after the Editor imported the move; totals read from `TestResults.xml`. All 9 boundary tests pass. No compile error and no compiler warning in the Editor log.
**Serialization re-check**: none of the nine patterns appears in the twelve sub-folders. No `[MovedFrom]` and no asset re-save.
**Deviations**: None
**Test Evidence**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (edited) with `AssemblyBoundary_tests.cs` (unchanged); full EditMode suite 2008/2008.
**Code Review**: Inline review of the diff by the orchestrator; no specialist panel was run.
