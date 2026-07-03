# Epic: Item Database

> **Layer**: Foundation
> **GDD**: design/gdd/item-database.md
> **Architecture Module**: Item Database
> **Status**: Ready
> **Stories**: 4 stories created (001–004)

## Overview

The Item Database is the centralized, read-only data store for every collectible and equippable item in Project Iron Grind. It owns the authoritative definition of all items — names, categories, gear slot assignments, stat modifiers, elemental damage values, upgrade parameters, shop prices, and display metadata. Every system that needs to know anything about an item queries the Item Database by `ItemID`. The database holds no runtime state (no ownership, no enhancement level — those belong to Inventory, Equipment, and Enhancement). MVP contains exactly 34 item records. The unit of cross-system reference is `ItemID`, a `readonly struct` wrapping `uint`, formally defined in this module.

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
| TR-itemdb-005 | MVP loader initializes exactly 34 item records; any validation failure aborts loading — game does not enter gameplay with a corrupt database | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria in `design/gdd/item-database.md` (AC-1 through AC-34) are verified
- All Logic stories (import validator, query API) have passing test files in `tests/EditMode/ItemDatabase/`
- The 34 MVP item records are authored as data assets and pass the import validator
- `GetItem(ItemID.Invalid)` and `GetItemsByCategory(unknown)` defensive-path tests pass

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | IItemDatabase Interface, Runtime Database, and Core Type Definitions | Logic | Ready | None (design-only) |
| 002 | Import Validator — Reject Rules (Error Path) | Logic | Ready | None (design-only) |
| 003 | Import Validator — Warning Rules (Accept Path) | Logic | Ready | None (design-only) |
| 004 | MVP Item Records — 34 Authored ScriptableObject Assets | Config/Data | Ready | None (data authoring) |

Work through stories in order — each story's `Depends on:` field tells you what must be Done before you can start it.
