# Story 014: Move Loot Table to the Server Assembly

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — Overview and the server-authority rule (line 91): "All drop roll results, tag resolution, round-robin advancement, auction bids, bid validation, and gold distribution are computed server-side. The client receives outcome events only."
**Requirement**: none registered — the requirement is ADR-012's (server-only code must not be compiled into a client player). `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 3 (classification), Decision 6 check 1 (boundary test), Migration Plan step 2 (Loot Table is second in the move order).
**ADR Decision Summary**: a system moves from `IronGrind.Foundation` to `IronGrind.ServerLogic` only after every system that uses its server types has moved. Nothing references a Loot Table type in code from outside its folder, so it moves next after Damage Calculation. A move is a folder and assembly change only.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — the pattern was proven on a real compile and a full suite run in Damage Calculation Story 006.
**Engine Notes**:
- Move every `.cs` together with its `.cs.meta`, and the folder `.meta`, with `git mv`, so GUIDs are preserved.
- Verify by running the EditMode suite (Test Runner, or batch mode when the Editor is closed). Check for `Temp/UnityLockfile` and a running `Unity.exe` before a batch run.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary)**:
- Required: namespaces do not change on a move; each move story re-checks the reference graph first, deletes its entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list with a named client consumer, and leaves the suite green.
- Required: `[MovedFrom]` and an asset re-save for any moved type serialized through `[SerializeReference]` or stored by name; `src/` searched for `Type.GetType`, `AssemblyQualifiedName`, `TypeNameHandling`.
- Forbidden: `InternalsVisibleTo` between production assemblies; placing a drop roll or any other outcome formula in `IronGrind.Foundation`.

---

## Classification (ADR-012 Decision 3)

All 37 types of `src/Foundation/LootTableSystem/` (namespace `IronGrind.LootTableSystem`) go to `IronGrind.ServerLogic`. None has a client consumer today: no file in `src/Client/` and no wire codec names a Loot Table type, so rule 3 does not apply and rule 4 (server-only is the default) decides.

| Group | Types | Rule |
|---|---|---|
| Services, rollers, validators, constants | `LootTableService`, `LootDropRoller`, `LootDropDistributor`, `LootRandomFactory`, `LootEquipmentCache`, `LootTableRegistry`, `LootTableValidator`, `LootTableConstants`, `GroundItemService`, `LootAuctionService`, `LootTeardownCoordinator`, `PartyTagTracker` | 2 |
| Static loot data | `LootTableDefinition`, `LootTableEntry`, `LootTableValidationIssue` | 2 — drop probabilities the player must not read |
| Server-side interfaces | `ILootTableService`, `IGroundItemService`, `ILootAuctionService`, `ILootDropSink`, `IPartyService`, `IMobInfoProvider`, `ICharacterPositionProvider` | interface lives with its consumer |
| Ids, enums, records | `GroundItemID`, `PartyID`, `MobTypeID`, `MobInfo`, `GroundItem`, `DropTier`, `GroundItemState`, `LootBidResult` | 4 |
| Event argument structs | `GroundItemSpawnedEventArgs`, `GroundItemAssignedEventArgs`, `GroundItemDespawnedEventArgs`, `GroundItemExpiryWarningEventArgs`, `BagFullPickupBlockedEventArgs`, `LootBidUpdateEventArgs`, `AuctionResolvedEventArgs` | 4 |

**Expected to come back.** The GDD says the client receives outcome events (`GroundItemSpawned`, …), and the wire codecs for the ten loot messages are not written yet. When they are, or when the loot UI is, some of the last two groups (most likely `GroundItemID`, `GroundItemState`, `DropTier`, `LootBidResult`) may be needed on the client. They then move to `IronGrind.Foundation` with the consumer named on the shared allow-list (ADR-012 Risks: "wrong classification of a result or config type"). They are not left in `Foundation` now, because the allow-list admits no entry without a named consumer.

---

## Acceptance Criteria

- [ ] **Graph re-checked before the move**: a grep for the 37 type names in `src/` outside the Loot Table folder finds no code reference (doc comments do not count). If one is found, stop and report it.
- [ ] **Moved**: the 37 files of `src/Foundation/LootTableSystem/` are in `src/ServerLogic/LootTableSystem/`; namespace `IronGrind.LootTableSystem` unchanged; `.meta` GUIDs unchanged (git shows renames with no content change). `src/Foundation/LootTableSystem/` no longer exists.
- [ ] **Lists updated**: `AssemblyBoundaryLists.NotYetMovedList` loses exactly its 37 Loot Table entries (204 → 167) and the "Loot Table" header; `SharedAllowList` is unchanged (26).
- [ ] **Server-only namespaces are data**: `AssemblyBoundaryLists` gains a `ServerOnlyNamespaces` list (`IronGrind.DamageCalculation`, `IronGrind.LootTableSystem`), and the boundary test asserts that no type of a listed namespace is defined in `IronGrind.Foundation` or `IronGrind.Client`, and that each listed namespace has at least one type in `IronGrind.ServerLogic`.
- [ ] **No production code change**: no `.cs` under `src/` is edited. If a moved file turns out to reach an `internal` member of a `Foundation` type, that member becomes `public` (ADR-012 Decision 5) and the change is listed under Deviations.
- [ ] **Suite green**: the full EditMode suite passes (2002 before this story, plus any test added), with no compile error and no new compiler warning.

---

## Implementation Notes

1. **Re-check the graph** (first acceptance criterion). Checked at story creation, 2026-10-08: no code reference from `src/Foundation/`, `src/Client/` or `src/ServerLogic/` outside the folder; the only outside mention is a doc comment in NPC Interaction (`IPartyService`).
2. **Move**: `git mv src/Foundation/LootTableSystem src/ServerLogic/LootTableSystem` and `git mv src/Foundation/LootTableSystem.meta src/ServerLogic/LootTableSystem.meta`. Do not edit the moved files.
3. **What the folder depends on** stays in `Foundation` for now and compiles, because `ServerLogic` references `Foundation`: Character Stats ids and the `CharacterStats` class, Currency (`ICurrencyService`, `CurrencySystem`, `GoldMutationResult`, `CharacterID`), Inventory (`IInventoryService`, pickup types), Item Database, and Networking (`StaleDiscardComparer`, `RelevanceFilter`).
   - Checked at story creation: the folder reaches no `internal` member and no `internal` type of another system. The names `State` and `ExpiryTick` that a grep turns up are Loot Table's own members.
4. **Serialization checklist** — checked at story creation: the folder has no `MonoBehaviour`, `ScriptableObject`, `[SerializeField]` or `[SerializeReference]` type and no `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use. No `[MovedFrom]` and no asset re-save are needed. Re-run the grep and say so in the completion notes.
5. **Lists** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`): delete the "Loot Table" block of `NotYetMovedList`; add `ServerOnlyNamespaces`.
6. **Boundary test** (`tests/EditMode/Architecture/AssemblyBoundary_tests.cs`): replace the namespace half of `test_damage_calculation_is_server_only` with a data-driven test over `ServerOnlyNamespaces` (keep the five `typeof` assertions for Damage Calculation, or fold them into the new test — one or the other, not both). Get the `ServerLogic` assembly as today, through `typeof(IronGrind.DamageCalculation.DamageCalculator).Assembly`.
7. **Tests**: the 15 Loot Table test files under `tests/EditMode/LootTableSystem/` and `tests/EditMode/Integration/LootTableSystem/` do not move and need no edit; the test assembly already references `IronGrind.ServerLogic` and has `InternalsVisibleTo` from it.

Other rules:
- No performance impact expected — files change assembly only; no runtime behaviour changes.
- Later move stories add their namespace to `ServerOnlyNamespaces` only if the whole namespace is server-only. Networking, Currency, Character Stats and Leveling keep shared types in their namespaces and are not listed.

---

## Out of Scope

- Moving any other system. Next in ADR-012's order: Enhancement.
- Wire codecs for the loot messages and any loot UI; returning a type to `Foundation` for them.
- Server-only loot *data* as assets (ADR-012 Risks, "server-only data still ships"): no loot table asset exists yet.
- Damage Calculation Story 007 (client-binary scan). When it is written, its forbidden-name list gains the Loot Table type names.

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (existing, edited).

- **Server-only namespaces** — Given `AssemblyBoundaryLists.ServerOnlyNamespaces`; Then for each namespace no type with that `Namespace` is defined in `IronGrind.Foundation` or `IronGrind.Client`, and at least one is defined in `IronGrind.ServerLogic`; the failure message names the namespace and the offending types.
- **Every `Foundation` type is listed** (existing) — passes with the shortened list; no Loot Table type is reported as unlisted.
- **No stale entry** (existing) — passes: no deleted-but-still-present and no present-but-moved entry. *This is the test that fails if the Loot Table block is left on the list after the move.*
- **Regression** — the 15 Loot Table test files pass unchanged.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` — the data-driven namespace test exists and passes.
- The full EditMode suite result after the move: pass count and zero failures (batch-mode results XML, or the user's Test Runner result).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Damage Calculation Story 006 (Complete — the assemblies and the boundary test exist); Loot Table Stories 001–013 (Complete — the code to move).
- Unlocks: the Enhancement move story (third in ADR-012's order).
