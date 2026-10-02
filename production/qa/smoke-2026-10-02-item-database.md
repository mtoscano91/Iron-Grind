# Smoke Check: Item Database — Story 006 (Scroll Records 35–38 and Potion Value Alignment)

**Date**: 2026-10-02
**Story**: `production/epics/item-database/story-006-scroll-records-and-potion-value-alignment.md`
**Environment**: Unity 6000.3.10f1 LTS, batch mode (`-batchmode`), Editor not open
**Executed by**: Claude Code, via the command line — `-runTests -testPlatform EditMode` for the test suite, then `-executeMethod IronGrind.ItemDatabase.ItemDatabaseSeeder.SeedMvpItemRecords` for the seeder (the same method the `IronGrind > Item Database > Seed MVP Item Records` menu item calls). Not run through the Editor UI; the assets were inspected as YAML text, not in the Inspector.

## Result: PASS

Seeder log output:

```
[ItemDatabaseSeeder] Created 38 item records (Assets/data/items/equipment, Assets/data/items/consumables).
[ItemDatabaseSeeder] Validation PASSED — 0 fatal, 0 errors, 0 warning(s).
```

EditMode suite (run before seeding): 1125 / 1125 passed, 0 compile errors.

| ItemDatabase fixture | Cases | Result |
|---|---|---|
| `ItemDatabase_Core_Tests` | 12 | Passed |
| `ItemDatabase_MvpRecords_Tests` | 25 | Passed |
| `ItemDatabase_Validator_Error_Tests` | 26 | Passed |
| `ItemDatabase_Validator_Scroll_Tests` | 15 | Passed |
| `ItemDatabase_Validator_Warning_Tests` | 12 | Passed |

## Smoke Check Assertions (per story `## QA Test Cases`)

| Assertion | Evidence | Status |
|---|---|---|
| Seeder log: 38 records created; validation 0 fatal, 0 errors, 0 warnings | Seeder log output above | PASS |
| `Assets/data/items/equipment/` holds 28 assets | File count | PASS |
| `Assets/data/items/consumables/` holds 10 assets | File count — IDs 29–34 (potions), 35–38 (scrolls) | PASS |
| Each scroll asset: `_scrollData` non-null with the expected tier, `_consumableData` null, `_equipmentData` null | Asset YAML: each of the 4 scroll assets has one `ScrollData` reference and no `ConsumableData` / `EquipmentData` reference; `_targetGearTier` = 1, 2, 3, 4 (Bronze, Iron, Steel, DarkSteel) for IDs 35–38 | PASS |
| Each potion asset: `_consumableData` non-null with the aligned values, `_scrollData` null | Asset YAML: each of the 6 potion assets has one `ConsumableData` reference and no `ScrollData` reference; `_effectMagnitude` 80 / 220 / 500, `_cooldownSeconds` 20 / 30 / 45, `_stackLimit` 99, `_sellPriceGold` 2 / 6 / 18 for Small / Medium / Large, HP and MP alike | PASS |
| No equipment asset carries `ScrollData` or `ConsumableData` | Asset YAML: 0 of 28 | PASS |
| EditMode `ItemDatabase` test folder: all tests pass | 90 cases across the 5 fixtures above | PASS |
| `ITEM_ID_REGISTRY.txt` lists IDs 35–38 and `NEXT FREE ID: 39` | File content | PASS |

Acceptance criteria asserted by `ItemDatabase_MvpRecords_tests.cs` against the same data table the seeder uses (`MvpItemRecordData.BuildAll()`):

| Criterion | Test | Status |
|---|---|---|
| AC-47 | `MvpRecords_ScrollRecords_AreOnePerGearTierWithScrollShape` | PASS |
| AC-18 | `MvpRecords_GetItemsByCategoryEquipment_Returns28EquipmentOnly`, `MvpRecords_GetItemsByCategoryConsumable_Returns10ConsumablesOnly` | PASS |
| AC-24 | `MvpRecords_IsUpgradeableFlag_MatchesCategoryWithNoExceptions` | PASS |
| AC-34 | `MvpRecords_CategoryCounts_Are28EquipmentAnd10Consumable` | PASS |
| AC-33 | `MvpRecords_HpPotionSellPrices_MatchF2Table`, `MvpRecords_MpPotionSellPrices_MatchF2Table` | PASS |
| Rule 13 item 37 (IDs 35–38, names, tiers; IDs 1–34 unchanged) | `MvpRecords_ScrollRecord_HasExpectedIdNameAndTier` (4 cases), `MvpRecords_ExistingRecord_KeepsItsItemId` (4 cases: IDs 1, 28, 29, 34) | PASS |
| Potion `EffectMagnitude`, `CooldownSeconds`, `StackLimit` | `MvpRecords_PotionRecord_HasDesignValues` (6 cases) | PASS |
| `ValidateBatch` — zero fatal, zero errors, zero warnings | `MvpRecords_ValidateBatch_ZeroIssues` | PASS |

## Notes

- **Asset GUIDs did not change.** The story warned that re-seeding (delete and recreate) would give every asset a new GUID. A pre-check found no file referencing the 34 item asset GUIDs, and after the re-seed no tracked `.meta` file under `Assets/data/items/` is modified — the 34 existing assets kept their GUIDs. Only the 4 new scroll assets have new `.meta` files.
- **All 34 existing `.asset` files changed on disk**, not only the 6 potions: each now has a `_scrollData` entry (null on equipment and potions) and new `[SerializeReference]` reference IDs. The 28 equipment records' data values are unchanged.
- **`_itemId` is not written to any `.asset` file.** `ItemDefinition._itemId` is an `ItemID` (`readonly struct` with a `readonly` field, not `[Serializable]`), which Unity does not serialize. This predates this story — the Story 004 assets in git have no `_itemId` either. The EditMode tests and the seeder's validation pass run on the in-memory records, where the ID is set, so they do not detect it. Nothing in `src/` loads the `.asset` files yet; when a loader is written, every record read from disk will have `ItemID(0)` unless this is fixed first. Not fixed here — outside this story's scope.
- **Addendum, later on 2026-10-02 — the `_itemId` gap above is fixed (TD-047).** `_itemId` and `EquipmentData._mergeResultItemID` are now stored as raw `uint`. The seeder was re-run in batch mode after the fix: 38 records created, validation 0 fatal / 0 errors / 0 warnings; every one of the 38 `.asset` files has an `_itemId` equal to the ID in its file name; no tracked `.meta` file changed. EditMode suite 1129 / 1129, including the 4 new `ItemDatabase_Serialization_Tests`. The assertions in the tables above were not re-checked one by one after this re-seed; the record data table (`MvpItemRecordData`) did not change.
