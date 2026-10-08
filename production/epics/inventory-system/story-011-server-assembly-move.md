# Story 011: Move Inventory to the Server Assembly

> **Epic**: Inventory System
> **Status**: Complete (2026-10-08)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — the server owns the slot container: every mutation (pickup, move, discard, sell, consume, lock) is validated and applied by `InventoryService` (Rule 5 slot locks; Rule 4.10 bag-full notification). A client that could run or replace this code could forge slot contents.
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification, type by type), Decision 6 check 1 (boundary test and shared allow-list), Migration Plan step 2 (Inventory is fifth in the move order: "`IInventoryService` and inventory types are used by Enhancement and Loot Table").
**ADR Decision Summary**: a system moves from `IronGrind.Foundation` to `IronGrind.ServerLogic` only after every system that uses its server types has moved. The only production code that uses an Inventory type from outside its folder is in `src/ServerLogic/` (`EnhancementService`, `GroundItemService`, `LootAuctionService`). Enums that appear in wire messages stay in `Foundation` (Decision 3 rule 3) on the shared allow-list, with a client consumer named.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the same pattern compiled and passed the suite four times (Damage Calculation Story 006, Loot Table Story 014, Enhancement Stories 012 and 013). New here: the folder is split, so `src/ServerLogic/InventorySystem/` is a new folder and Unity generates its `.meta`.
**Engine Notes**:
- Move each `.cs` together with its `.cs.meta` with `git mv`, so GUIDs are preserved. The folder `src/Foundation/InventorySystem/` and its `.meta` stay, because two files remain in it.
- `src/ServerLogic/InventorySystem.meta` does not exist until the Editor imports the new folder. It is generated on the first refresh (or batch-mode run) and must be committed with the story.
- `InventoryService` uses `UnityEngine.Debug`; `IronGrind.ServerLogic` has engine references, so this compiles unchanged.
- Verify by running the EditMode suite. With the Editor closed, use batch mode. With the Editor open, the Test Runner writes its totals to `%USERPROFILE%/AppData/LocalLow/DefaultCompany/IronGrind/TestResults.xml`; read the total from there and check the file is newer than the move.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Core layer)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list with a named client consumer, and leaves the suite green.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name.
- Forbidden: `InternalsVisibleTo` between production assemblies.

---

## Classification (ADR-012 Decision 3)

`src/Foundation/InventorySystem/` (namespace `IronGrind.InventorySystem`) holds 22 types. 20 go to `IronGrind.ServerLogic`; 2 stay in `IronGrind.Foundation`.

| Group | Types | Destination | Rule |
|---|---|---|---|
| Service and its interface | `InventoryService`, `IInventoryService` | `ServerLogic` | 2 — validates and mutates authoritative state; the interface lives with its consumers (`EnhancementService`, `GroundItemService`, `LootAuctionService`) |
| Constants | `InventoryConstants` | `ServerLogic` | 4 — no client consumer today; it also holds the bag-full dedup window, a server tuning value |
| Slot state and persistence | `InventorySlot`, `InventorySnapshot`, `InventorySnapshotEntry` | `ServerLogic` | 4 — server state and the save/load shape (ADR-006) |
| Result types | `PickupResult`, `MoveResult`, `MoveItemInResult`, `MoveItemOutResult`, `DiscardResult`, `SellItemResult`, `ConsumeItemResult` | `ServerLogic` | result types follow their producer |
| Server-side codes | `PickupFailReason`, `SellItemFailReason`, `ConsumeItemFailReason`, `MoveItemOutCode` | `ServerLogic` | 4 — not named by the wire protocol |
| Event argument structs | `InventoryChangedEventArgs`, `InventoryFullEventArgs`, `SlotChange` | `ServerLogic` | 4 — server-side events; the client gets wire messages built from them |
| **Wire enums** | **`DiscardFailReason`, `MoveFailReason`** | **`Foundation`** | **3 — `networking-wire-protocol.md` defines both as wire enums (in the `DiscardResult` and `MoveResult` messages), and the code enums mirror them member for member and value for value** |

The two wire enums go on the shared allow-list with `consumer (planned): inventory screen (wire enum)`, the same form as `GoldTransactionReason`. They stay in `src/Foundation/InventorySystem/`, in the same namespace. Because the namespace keeps shared types, `IronGrind.InventorySystem` is **not** added to `ServerOnlyNamespaces`.

**Expected to come back.** The inventory screen (not written) will need the slot count and a view of slot contents. Under ADR-012 Decision 7 that is a read-only view and a request interface declared in `Foundation`, not `InventoryService`. `INVENTORY_SLOT_COUNT` (named by the wire protocol as the slot index range) will be needed by the client then; `InventoryConstants` is split at that point, with the dedup window staying server-side. Splitting it now would be a production code change with no consumer to name.

---

## Acceptance Criteria

- [x] **Graph re-checked before the move**: a grep for the 22 type names and for `IronGrind.InventorySystem` in `src/` outside the Inventory folder finds code references only under `src/ServerLogic/`. If one is found in `src/Foundation/` or `src/Client/`, stop and report it.
- [x] **Moved**: 20 files are in `src/ServerLogic/InventorySystem/` (every type in the table above except the two wire enums); namespace unchanged; `.cs.meta` GUIDs unchanged (git shows 40 renames with no content change). `src/Foundation/InventorySystem/` holds exactly `DiscardFailReason.cs`, `MoveFailReason.cs` and their `.meta` files.
- [x] **New folder `.meta` committed**: `src/ServerLogic/InventorySystem.meta` exists (generated by Unity) and is part of the changeset.
- [x] **Lists updated**: `AssemblyBoundaryLists.NotYetMovedList` loses all 22 Inventory entries and the "Inventory" header (149 → 127); `SharedAllowList` gains `IronGrind.InventorySystem.DiscardFailReason` and `IronGrind.InventorySystem.MoveFailReason`, each with its planned consumer in a comment (26 → 28); `ServerOnlyNamespaces` is unchanged.
- [x] **No behaviour change**: no moved file is edited. The only `.cs` edit allowed under `src/` is to doc comments of the two files that stay (see Implementation Notes 5). If a moved file turns out to reach an `internal` member of a `Foundation` type, that member becomes `public` (ADR-012 Decision 5) and the change is listed under Deviations.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning, and the total is written down from a results file. Expected: 2003 (no test is added or removed).

---

## Implementation Notes

1. **Re-check the graph** (first acceptance criterion). Checked at story creation, 2026-10-08: the only production files outside the folder that import `IronGrind.InventorySystem` are `src/ServerLogic/EnhancementSystem/EnhancementService.cs`, `src/ServerLogic/LootTableSystem/GroundItemService.cs` and `src/ServerLogic/LootTableSystem/LootAuctionService.cs`. No type name of the folder appears in code under `src/Foundation/` (outside the folder) or `src/Client/`.
2. **Move**: create `src/ServerLogic/InventorySystem/`, then `git mv` each of the 20 `.cs` files and its `.cs.meta` into it. Do not move `DiscardFailReason.cs`, `MoveFailReason.cs` or `src/Foundation/InventorySystem.meta`.
3. **What the moved files depend on** stays in `Foundation` and compiles, because `ServerLogic` references `Foundation`:
   - Character Stats ids, Currency (`CharacterID` only — the folder does not call the currency service), Item Database.
   - Networking: `ServerTickLoop.TICK_RATE_HZ` (public const, in `InventoryConstants`) and `StaleDiscardComparer.IsTickExpired` (public static, in `InventoryService`).
   - The two wire enums, used by `DiscardResult`, `MoveResult` and `InventoryService`.
   - Checked at story creation: the folder uses no `internal` member and no `internal` type of another system. This matters more here than in the earlier moves: Inventory was in the same assembly as Networking, so an `internal` use would have compiled until now.
4. **The folder's own internals** (`InventoryService.SeedSlotForTesting`, `RecordSlotChange`, `EmitInventoryChanged`, `DiscardPendingChanges`, `NotifyInventoryFull`; the `InventoryChangedEventArgs` constructor and its `Enumerator` constructor) all move together and are used only inside the folder and by tests. `IronGrind.ServerLogic` already grants `InternalsVisibleTo` to the test assembly.
5. **Doc comments of the two files that stay.** `DiscardFailReason.cs` and `MoveFailReason.cs` hold `<see cref="…"/>` references to types that will be in an assembly `Foundation` cannot see (`IInventoryService.Discard`, `IInventoryService.Move`, `PickupFailReason`). XML documentation output is not enabled, so they raise no compiler warning, but they no longer resolve. Replace those `cref`s with `<c>…</c>` text. Change nothing else in the two files; crefs to `None`, `byte` and to each other stay.
6. **Serialization checklist** — checked at story creation: no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]`, `[SerializeReference]`, `[Serializable]`, `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use in the folder. No `[MovedFrom]` and no asset re-save are needed. Re-run the grep and say so in the completion notes.
7. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`):
   - Delete the "Inventory" block of `NotYetMovedList` (22 entries, including the two wire enums), with the blank line after it, so the list starts with "Leveling".
   - Add to `SharedAllowList`, as a new group after the Item Database group:
     ```
     // Inventory: enums that networking-wire-protocol.md defines as wire enums.
     "IronGrind.InventorySystem.DiscardFailReason", // consumer (planned): inventory screen (wire enum, DiscardResult message)
     "IronGrind.InventorySystem.MoveFailReason",    // consumer (planned): inventory screen (wire enum, MoveResult message)
     ```
   - Do not edit `ServerOnlyNamespaces`. Its doc comment lists the namespaces that keep shared types ("Networking, Currency, Character Stats, Leveling"); add Inventory to that sentence.
   - No change to `AssemblyBoundary_tests.cs` is needed.
8. **Tests**: the 11 files of `tests/EditMode/InventorySystem/` and the Enhancement and Loot Table tests that use Inventory types do not move and need no edit.
9. **Verification**: see Engine Notes. Record the total, not only "all passed". After the Editor refresh, confirm `src/ServerLogic/InventorySystem.meta` exists and stage it.

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.

---

## Out of Scope

- Moving any other system. Next in ADR-012's order: Leveling (with the client read model of Decision 7).
- The inventory read-only view, the request interface and the split of `InventoryConstants` (ADR-012 Decision 7): they are written with the inventory screen.
- Wire codecs for the inventory messages.
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the 20 moved type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, not edited) with `AssemblyBoundaryLists.cs` (edited).

- **Every `Foundation` type is listed** (`test_every_foundation_type_is_listed`) — Given the two wire enums are the only Inventory types left in `IronGrind.Foundation`; Then both are found on `SharedAllowList` and the test passes. *This is the test that fails if the two enums are deleted from the not-yet-moved list without being added to the allow-list.*
- **No stale entry** (`test_no_list_entry_is_stale`) — passes. *This is the test that fails if a moved type is left on either list.*
- **No type on both lists** (`test_no_type_is_on_both_lists_or_duplicated`) — passes. *This is the test that fails if a wire enum is added to the allow-list and left on the not-yet-moved list.*
- **No `ServerLogic` type in a `Foundation` signature** (`test_no_server_logic_type_in_foundation_or_client_signature`) — passes; the two enums carry no moved type.
- **Regression** — the 11 Inventory test files and the Enhancement and Loot Table tests pass unchanged.

Not covered by an automated test: that each of the 20 types is in `IronGrind.ServerLogic` rather than deleted. The compiler covers it (the tests and three services use them), and the git rename status is the record.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` with the two shared entries, and the boundary tests passing.
- The full EditMode suite result after the move, with the total, from a results file (batch-mode XML or the Test Runner's `TestResults.xml`).

**Status**: [x] Complete — 2003 of 2003 passed (Test Runner results file, 2026-10-08)

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Enhancement Story 012 and Loot Table Story 014 (Complete — every user of the Inventory types is in `ServerLogic`); Inventory Stories 001–010 (Complete — the code to move); Damage Calculation Story 006 (Complete — the assemblies and the boundary test).
- Unlocks: the Leveling move story (sixth in ADR-012's order).

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 6/6 passing
- Graph re-checked before the move: the only code references from outside the folder are in `EnhancementService.cs`, `GroundItemService.cs` and `LootAuctionService.cs`, all in `src/ServerLogic/`.
- Moved: 40 git renames (20 `.cs`, 20 `.cs.meta`), 0 lines changed. `src/Foundation/InventorySystem/` holds `DiscardFailReason.cs`, `MoveFailReason.cs` and their `.meta` files.
- New folder `.meta`: `src/ServerLogic/InventorySystem.meta` generated by Unity on the Editor refresh and staged.
- Lists: `NotYetMovedList` 149 → 127; `SharedAllowList` 26 → 28 (the two wire enums, planned consumer: inventory screen); `ServerOnlyNamespaces` unchanged, its doc comment now names Inventory.
- No moved file was edited. The two files that stay had three doc-comment `cref`s to moved types replaced with `<c>` text (`IInventoryService.Discard`, `IInventoryService.Move`, `PickupFailReason`); no member or value changed.
- Suite: 2003 total, 2003 passed, 0 failed, 0 skipped — the user ran the EditMode suite in the Editor's Test Runner after the move; totals read from `TestResults.xml` (run 15:36). All 8 boundary tests pass in that file. The Editor log shows no compile error and no compiler warning.
**Serialization re-check**: the grep was re-run at implementation time and found no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]`, `[SerializeReference]`, `[Serializable]`, `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use in the folder. No `[MovedFrom]` and no asset re-save.
**Deviations**: None
**Test Evidence**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (edited) with `AssemblyBoundary_tests.cs` (unchanged); full EditMode suite 2003/2003.
**Code Review**: Inline review of the diff by the orchestrator (list file and the three doc-comment lines); no specialist panel was run.
