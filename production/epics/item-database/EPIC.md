# Epic: Item Database

> **Layer**: Foundation
> **GDD**: design/gdd/item-database.md
> **Architecture Module**: Item Database
> **Status**: Complete (reopened and closed 2026-10-02 for GDD Amendment #4 — Enhancement Scrolls)
> **Stories**: 6 stories created (001–006), all Complete

## Overview

The Item Database is the centralized, read-only data store for every collectible and equippable item in Project Iron Grind. It owns the authoritative definition of all items — names, categories, gear slot assignments, stat modifiers, elemental damage values, upgrade parameters, shop prices, and display metadata. Every system that needs to know anything about an item queries the Item Database by `ItemID`. The database holds no runtime state (no ownership, no enhancement level — those belong to Inventory, Equipment, and Enhancement). MVP contains exactly 38 item records. The unit of cross-system reference is `ItemID`, a `readonly struct` wrapping `uint`, formally defined in this module.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-001: Purchase Integrity | `ItemID` is the cross-system item reference key used in `BuyRequest`/`SellRequest` and `PendingPurchaseRecord` | LOW |

> No dedicated ADR is required for this module — it is pure, stateless data loading with LOW engine risk and no Unity API surface beyond asset loading.

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-itemdb-001 | `GetItem(ItemID)` returns single authoritative `ItemDefinition` reference; `GetItem(ItemID.Invalid)` returns null, no exception | ❌ No ADR (design-only, LOW risk) |
| TR-itemdb-002 | Import validator rejects: duplicate ItemIDs, equipment with StackLimit ≠ 1, consumables with GearSlot/GearTier ≠ None, equipment with GearTier = None, zero-flat-bonus StatModifier entries, > 2 StatModifier entries per item | ❌ No ADR (design-only, LOW risk) |
| TR-itemdb-003 | `GetItemsByCategory()` returns filtered `IReadOnlyList<ItemDefinition>`; unknown category enum value returns empty list + dev-build warning, no exception | ❌ No ADR (design-only, LOW risk) |
| TR-itemdb-004 | Database is read-only at runtime — no system may write or mutate item definitions after initialization | ❌ No ADR (design-only, LOW risk) |
| TR-itemdb-005 | MVP loader initializes exactly 38 item records (28 Equipment, 10 Consumable — 6 potions, 4 Enhancement Scrolls); any validation failure aborts loading — game does not enter gameplay with a corrupt database *(revised 2026-10-02, Amendment #4: was 34)* | ❌ No ADR (design-only, LOW risk) |
| TR-itemdb-006 | `ScrollData { TargetGearTier }` sub-schema on `ItemDefinition`; a Consumable record carries exactly one of `ConsumableData` / `ScrollData`; import validator rejects: Consumable with neither or both, Equipment with `ScrollData`, `TargetGearTier` of `None` or undefined; a well-formed scroll (`SellPriceGold = 0`) validates with zero issues *(added 2026-10-02, Amendment #4)* | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria in `design/gdd/item-database.md` (AC-1 through AC-47) are verified
- All Logic stories (import validator, query API) have passing test files in `tests/EditMode/ItemDatabase/`
- The 38 MVP item records are authored as data assets and pass the import validator
- `GetItem(ItemID.Invalid)` and `GetItemsByCategory(unknown)` defensive-path tests pass

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | IItemDatabase Interface, Runtime Database, and Core Type Definitions | Logic | Complete | None (design-only) |
| 002 | Import Validator — Reject Rules (Error Path) | Logic | Complete | None (design-only) |
| 003 | Import Validator — Warning Rules (Accept Path) | Logic | Complete | None (design-only) |
| 004 | MVP Item Records — 34 Authored ScriptableObject Assets | Config/Data | Complete | None (data authoring) |
| 005 | ScrollData Sub-Schema and Validator Scroll Rules | Logic | Complete | None (design-only) |
| 006 | MVP Item Records — Scroll Records 35–38 and Potion Value Alignment | Config/Data | Complete | None (data authoring) |

Work through stories in order — each story's `Depends on:` field tells you what must be Done before you can start it.
