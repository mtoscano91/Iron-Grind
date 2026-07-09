# Smoke Check: Item Database — Story 004 (MVP Item Records)

**Date**: 2026-07-04
**Story**: `production/epics/item-database/story-004-mvp-item-records.md`
**Environment**: Unity 6000.3.10f1 LTS, run via `IronGrind > Item Database > Seed MVP Item Records (Story 004)` menu item
**Executed by**: User, in the actual Unity Editor (first real execution of this tool)

## Result: PASS

Console output from `ItemDatabaseSeeder.RunValidationPass`:

```
[ItemDatabaseSeeder] Validation PASSED — 0 fatal, 0 errors, 0 warnings.
```

34 `.asset` files created under `Assets/data/items/equipment/` (28 records) and `Assets/data/items/consumables/` (6 records).

## Smoke Check Assertions (per story `## QA Test Cases`)

All assertions below are satisfied either directly by the seeder's validation pass (0 fatal / 0 errors confirms every validator rule in Stories 002–003 passed for all 34 records) or by the corresponding EditMode test in `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs`, which shares the exact same data (`MvpItemRecordData.BuildAll()`) used to author these assets — so a passing test is a direct guarantee about the real asset data, not a separate approximation.

| Assertion | Evidence | Status |
|---|---|---|
| `ValidateAll` returns zero errors, zero fatal errors for all 34 records | Seeder console output | PASS |
| `GetItemsByCategory(Equipment).Count == 28` | `MvpRecords_GetItemsByCategoryEquipment_Returns28EquipmentOnly` (128/128 suite pass) | PASS |
| `GetItemsByCategory(Consumable).Count == 6` | `MvpRecords_CategoryCounts_Are28EquipmentAnd6Consumable` | PASS |
| Bronze Sword: `SellPriceGold == 10` | `MvpRecords_BronzeSword_SellPriceGoldIs10` | PASS |
| DarkSteel Sword: `SellPriceGold == 270` | `MvpRecords_DarkSteelSword_SellPriceGoldIs270` | PASS |
| HP Potion Small: 2, Medium: 6, Large: 18 | `MvpRecords_HpPotionSellPrices_MatchF2Table` | PASS |
| MP Potion Small: 2, Medium: 6, Large: 18 | `MvpRecords_MpPotionSellPrices_MatchF2Table` | PASS |
| All 28 equipment records: `IsUpgradeable == true` | `MvpRecords_IsUpgradeableFlag_MatchesCategoryWithNoExceptions` | PASS |
| All 6 consumable records: `IsUpgradeable == false` | `MvpRecords_IsUpgradeableFlag_MatchesCategoryWithNoExceptions` | PASS |
| All 34 ItemIDs are unique (no duplicate) | `MvpRecords_AllItemIds_AreUniqueAndNeverZero` | PASS |
| No record uses `ItemID(0)` | `MvpRecords_AllItemIds_AreUniqueAndNeverZero` | PASS |
| All equipment records have `GearTier != None` | Implied by 0 errors (AC-10 would have fired otherwise) | PASS |
| All consumable records have `EffectMagnitude > 0` | Implied by 0 errors (AC-21 would have fired otherwise) | PASS |

## Notes

- This is the first time the `ItemDatabaseSeeder` tool has actually run in a real Unity Editor. It, along with 128 other EditMode tests across Character Stats and Item Database, had only ever been verified by code review prior to this session (see `docs/tech-debt-register.md` TD-006).
- Warning-rule paths (Story 003: negative FlatBonus, sell-price deviation, zero sell price) all report 0 warnings, confirming the authored placeholder data (OQ-1, OQ-5 — see `ITEM_ID_REGISTRY.txt`) is internally consistent with the F-1/F-2 economy tables, even though the underlying balance values themselves remain provisional pending those open questions.
