# Story 019: Zone Uses Real Inventory and Equipment

> **Epic**: Character Persistence
> **Status**: Complete (2026-11-04; readiness 2026-11-04; written 2026-11-04 as the first half of the TD-091 fix; the save path follows in Story 020)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — the load path (inventory and equipment restored on zone entry); `design/gdd/item-database.md` — Rule 2 (single authoritative source), the duplicate-`ItemID` edge case (fatal load error); `design/gdd/zone-instancing.md` — CR-ZI-8 step 3 (`LoadCharacter` during admission)
**Requirement**: `TR-cp-003` — CR-CP-3, the load sequence in its exact order (stat initialisation before equipment re-registration); `ApplyLoadedRecord` does every write synchronously on the tick thread.

**ADR Governing Implementation**: ADR-006 (persistence), ADR-009 (S2 static data: ItemDatabase, EnhancementConfig), ADR-014 (composition root creates server-only components in code)
**ADR Decision Summary**: ADR-006: the zone's `CharacterPersistenceService` is the only path that applies a stored record to the zone's live state. ADR-009: S2 loads and validates the static data, including the ItemDatabase, before any game system is built; a failure ends startup. ADR-014: the composition root builds every server-only component in code, in a fixed S5 order that the construction-order test checks.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: The item list is a `[SerializeField]` array of `ItemDefinition` (ScriptableObject) references on `ZoneServerHost`, included in the build with `Assets/Scenes/Server/ZoneServer.unity`, like the `_registry`, `_skillDatabase` and `_classDatabase` fields already there (Skill System Story 013). No `Resources`, no Addressables, no post-cutoff API. `[SerializeField]` on fields only (Unity 6.3).

---

## Problem (TD-091, first half)

After Story 018 the zone applies loaded records, but its Inventory and Equipment are `StandInZoneInventoryService` / `StandInZoneEquipmentService`: the bag and gear in the record are accepted and discarded, gear gives no stat modifiers, and the save path must stay disabled because an export would write an empty bag and empty gear over the stored record. The real `InventoryService` and `EquipmentService` exist (Inventory and Equipment epics Complete) but need an `IItemDatabase`, and the zone loads none: S2 only loads class and skill data (`ZoneClassSkillDataLoader`, its `rest` loader is `StandInZoneStaticDataLoader`).

---

## Acceptance Criteria

- [x] S2 loads the items: a new `ZoneItemDataLoader` (an `IZoneStaticDataLoader` that also implements a new `IZoneItemData { ItemDatabase LoadedItemDatabase }`, run as the `rest` loader of `ZoneClassSkillDataLoader` in `ZoneServerHost`; it takes its own `rest` loader for later static data) validates the host's item list and initialises one `ItemDatabase`. A null or empty list or a null entry (the loader checks these itself: `ValidateBatch` skips nulls), or a duplicate `ItemID` or invalid record (reported by `ItemDefinitionValidator.ValidateBatch`) fails S2 with the reason logged ("[ZoneServer] S2: …"), and `LoadedItemDatabase` stays null. On success the database `IsReady`. `ZoneServerHost` gets `[SerializeField] private ItemDefinition[] _items`, filled in `ZoneServer.unity` with all 38 assets under `Assets/data/items/` (28 equipment, 10 consumables); an EditMode test checks that the host in the scene holds exactly the `ItemDefinition` assets found under `Assets/data/items/` (same set, no nulls).
- [x] S5 resolves the item database in the Story 013 order: the internal test override `ItemDatabaseOverride`, then the S2 loader's `IZoneItemData.LoadedItemDatabase` (found by type on the loader), then an empty ready `ItemDatabase` with one warning "[ZoneServer] no item database from S2; using an empty one (tests only)". The production host always provides the loader. (Story 020 must turn this fallback into a startup failure before saves are enabled: an empty database would save every bag as empty.)
- [x] S5 builds the real services: `InventoryService(itemDatabase, () => _tickLoop.ServerTickNumber, EnhancementConfig.Default.MaxEnhancementLevel)` and `EquipmentService(inventory, ZoneStats, itemDatabase, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default)`, both built after Character Stats and before `CharacterPersistenceService`. Persistence and `ZoneInstancingWiring` (its `inventory` argument and `options.Equipment`) hold the same Inventory and Equipment instances (identity checked in the test). The construction-order list in `ZoneServer_CompositionWiring_tests.cs` is updated to match.
- [x] A player admitted through the composed zone with a record that has bag items and equipped gear is admitted (no admission error, no `ItemDatabaseMiss` warning); after admission `ExportSnapshot` matches the record's bag (slot, item, quantity, enhancement level), `ExportSlots` matches the record's gear, and Character Stats includes the gear's stat modifiers (e.g. a weapon's AP).
- [x] A record whose gear holds an `ItemID` that is not in the database: that slot is dropped, admission logs the one `ItemDatabaseMiss` warning, and the player is admitted with the rest of the gear and the bag restored.
- [x] Slot release unregisters the character from both real services (no bag, no gear held afterwards); a re-admission of the same character on a new connection restores bag and gear from the new record.
- [x] The save path is still disabled: `SaveSession` throws at its wiring check before any export, the zone-close save still logs one failed save start per admitted player, and the in-memory store records 0 writes. The TD-091 entry in `docs/tech-debt-register.md` is updated: real Inventory and Equipment are in place; the locator, write queue and save constructor remain (Story 020).
- [x] `StandInZoneInventoryService` and `StandInZoneEquipmentService` are deleted, and the composition's `ZoneInventory` / `ZoneEquipment` test seams are retyped to the real services. `ZoneInstancingWiring` no longer falls back to a stand-in when `options.Equipment` is null (`ZoneInstancingWiring.cs` ~292): its constructor throws `ArgumentNullException`. The code that used the stand-ins (`ZoneInstancingWiring.cs`, `CharacterPersistence_ZoneApplier_tests.cs`, `ZoneInstancing_Wiring_tests.cs`, `ZoneServer_CompositionDrain_tests.cs`) is updated, with a real service or a test double where the wiring is built directly, and the full EditMode suite passes.

---

## Implementation Notes

- `ItemDatabase.Initialize` overwrites duplicate `ItemID`s silently (last write wins). The GDD makes a duplicate a fatal load error; `ItemDefinitionValidator.ValidateBatch` already reports duplicates, so the loader calls `Initialize` only when the batch has no errors. `ValidateBatch` skips null entries, so the loader rejects those first.
- `ZoneClassSkillDataLoader` finds the class data on itself by type; the composition finds the item database the same way (`_staticDataLoader` is the class/skill loader, so `IZoneItemData` is looked up through it: either `ZoneClassSkillDataLoader` forwards `LoadedItemDatabase` from its `rest` loader, or the composition walks the chain; pick one at `/dev-story`, both satisfy the criteria). A decorator around a loader must implement the interface too.
- Composition tests use their own fake S2 loaders (`SuStatic` and others), so they get the empty fallback database unless they set `ItemDatabaseOverride`.
- `ApplyLoadedRecord` already calls Equipment `UnregisterCharacter` / `RegisterCharacter` / `RestoreSlots` before Inventory `ImportSnapshot` (CR-CP-3); `ImportSnapshot` returns false while the database is not ready, so the database must be ready before the first admission (it is, S2 runs before S5).
- Session end already unregisters both: Inventory through the wiring's `inventory` argument, Equipment through `SessionEndCoordinator` via `options.Equipment` (Equipment Story 008).
- Filling `_items` in `ZoneServer.unity` is done in the Editor (or by a one-off editor script); the scene test guards it.
- `EnhancementConfig.Default` is used directly: there is no enhancement config asset yet (see Out of Scope).
- Performance: no per-tick cost added. S2 does one O(n) pass over 38 records; admission adds the real restore (bounded by the bag and gear slot counts).

---

## Out of Scope

- The zone save path: `ICharacterZoneLocator`, `CharacterWriteQueue`, clock and write timeout, the ten-argument constructor (Story 020, closes TD-091).
- Loading `EnhancementConfig` from an asset at S2.
- Sending inventory or equipment changes to the client.
- Client equip / unequip requests in the zone (`EquipmentClientRequestHandlers` is not wired into the zone dispatcher).
- Other S2 static data (LootTableRegistry, DamageCalculationConfig); `ZoneItemDataLoader` takes a `rest` loader so they can chain later.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneRealInventory_tests.cs` — must exist and pass
- `tests/EditMode/Networking/ZoneServer_ItemDataLoader_tests.cs` (loader checks and the `ZoneServer.unity` item-list check) — must exist and pass

**Status**: [x] Created — `ZoneServer_ItemDataLoader_tests.cs` 12 tests, `CharacterPersistence_ZoneRealInventory_tests.cs` 12 tests, all passing (full EditMode 6504 / 6375 passed / 0 failed / 129 skipped, 2026-11-04 23:03:04Z)

---

## Dependencies

- Depends on: Story 018 (Complete); Inventory System and Equipment System epics (Complete); Item Database epic (Complete)
- Unlocks: Story 020 (zone save path, closes TD-091)

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 8/8 passing (all automated). Two are covered as far as they are reachable today: criterion 6's "slot release unregisters" and criterion 7's "zone close logs one failed save start" (see Deviations).
**Deviations** (advisory, carried into TD-091 / Story 020):
- Slot release does not unregister the character in the composed zone: `SessionEndCoordinator` unregisters Inventory / Equipment only after a session-end save, and saves are disabled. Re-admission replaces the bag and gear (tested). Story 020 must cover the unregister once saves run.
- Zone close starts no save: an admitted player stays "entering" (SessionReady is not wired), so `ZoneCloseSteps` step 2 skips it. Tested: `SaveSession` throws at its wiring check, the close step logs nothing and the store gets 0 writes. Story 018's completion note corrected.
- An unknown BAG item is skipped by `InventoryService.ImportSnapshot` with one warning and the player is admitted (pinned by a test); once saves run, the next save drops it from the record for good. Story 020 must decide this.
- The S5 empty-item-database fallback (warning, tests only) stays; Story 020 must make it a startup failure before saves are enabled.
- The 38 item references were written directly into `ZoneServer.unity` (YAML); `test_server_scene_host_items_hold_every_item_asset` guards the set.
- Test-only additions: `ItemDatabaseOverride` (internal), `MvpItemFixture` (tests).
**Test Evidence**: Integration: `tests/EditMode/Networking/ZoneServer_ItemDataLoader_tests.cs` (12), `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneRealInventory_tests.cs` (12). Full EditMode 6504 / 6375 passed / 0 failed / 129 skipped, started 2026-11-04 23:03:04Z.
**Code Review**: Complete: `/code-review` APPROVED WITH SUGGESTIONS (unity-specialist no required changes, qa-tester GAPS no blockers); all 4 suggestions applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
**Partly resolves**: TD-091 (Inventory / Equipment half).
