# Epic: Equipment System

> **Layer**: Core
> **GDD**: design/gdd/equipment-system.md
> **Architecture Module**: Equipment (core gameplay/data — by-design no ADR, `architecture.md` Engine Knowledge Gap Summary: 🟢 LOW)
> **Status**: Ready — **one design gate open before `/create-stories`: OQ-EQS-9**
> **Stories**: Not yet created — run `/create-stories equipment-system`

## Overview

The Equipment System manages the seven gear slots of a player character (Weapon, Helmet, Chest, Legs, Boots, Ring, Necklace), each holding at most one equipped item. Equipping moves an item from the Inventory into its slot and registers its stat modifiers with Character Stats; unequipping or swapping removes those modifiers and returns the item to the bag. It is the only bridge between the Item Database's gear definitions and the Character Stats modifier stack. Each slot stores `{ItemID, EnhancementLevel}`: the level arrives from the Inventory when the item is equipped, is held unchanged while equipped, and is handed back when the item returns to the bag. The system also writes the one-byte `equipmentAppearanceFlags` other players see, exposes the equipped weapon and its level to Damage Calculation, and owns the Accessory Merge mechanic.

**Depends on**: Item Database (Complete), Inventory System (Complete 2026-10-07 — Story 010 delivered the per-slot level on `MoveItemOut` / `MoveItemIn` / `ForceInsert`), Character Stats (Complete), Networking Core (Complete). **Mutual dependency with the Enhancement System** (sibling epic, created the same day): this epic consumes `IEnhancementBonusProvider` and the prestige threshold constants, which that system owns. How the interface reaches this epic (the Enhancement epic's provider story first, or a consumer-side stub here) is a `/create-stories` decision.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event/Messaging Architecture | Direct Tier 1 calls into the injected Inventory and Character Stats services, acting on return values; `readonly struct` data; events for cross-system notification | LOW |
| ADR-006: Persistence Layer | `gear_slots` JSONB holds 7 `{item_id, enhancement_level}` entries; equipment state is rebuilt from them on load | LOW |
| ADR-004: Networking Library (NGO) | Transport for `EquipRequest` / `EquipResult` / `AppearanceChangedEvent` and the snapshot's `EquipmentAppearanceFlags` byte | HIGH (applies to the wire-facing stories only) |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-equip-001 | Slot registry: fixed array of 7 `EquipmentSlotEntry { ItemId, IsTransitioning, EnhancementLevel }` indexed by `(int)GearSlot`, no Dictionary; the system never changes a level, it only carries it (CR-EQS-1) | ADR-010 (`readonly struct`) |
| TR-equip-002 | Validation before any mutation: slot type match (`SlotMismatch`), stat gate on `GetBaseStat` (`StatRequirementNotMet`; accessories ungated), same-item guard on `ItemID` and level via `Inventory.GetSlot` (`NoChange`), and entry-point input validation (CR-EQS-2, CR-EQS-3, CR-EQS-4, CR-EQS-13, F-EQS-3) | ❌ No ADR (design-only, LOW risk) |
| TR-equip-003 | Enhanced modifier registration: every `AddEquipmentModifier` call passes `IEnhancementBonusProvider.GetFlatBonus(enhancementLevel, modifier.FlatBonus, item.GearTier)`, never the raw Item Database value (modifier registration rule, F-EQS-1, F-EQS-2, F-EQS-5) | ADR-010 (interface dependency) |
| TR-equip-004 | Equip into an empty slot and unequip, through `MoveItemOut` / `MoveItemIn` carrying the level; unequip restores modifiers and returns `InventoryError` when the bag refuses the item; a missing item definition on unequip clears the slot with a critical error (CR-EQS-5, CR-EQS-7) | ADR-010 (Tier 1 calls) |
| TR-equip-005 | Auto-swap into an occupied slot with rollback: free-slot check, old modifiers removed, old item to the bag, new item out, new modifiers added; a failed `MoveItemIn` aborts in place with the old item still equipped and no `ForceInsert` retry (CR-EQS-6, CR-EQS-8) | ADR-010 (Tier 1 calls) |
| TR-equip-006 | Transitioning guard: `IsSlotTransitioning(GearSlot)` is true for the whole swap window; the flag is never saved, and no stat transaction API is used (`OnStatChanged` fires twice during a swap) (CR-EQS-9, CR-EQS-15) | ADR-010 (events) |
| TR-equip-007 | `equipmentAppearanceFlags` byte re-encoded after every equip, unequip and swap: WeaponTier [7:6], ElementType [5:3], PrestigeBand [2:1] from the **Weapon slot's** level only, ArmorTier [0] (CR-EQS-11) | ADR-004 (snapshot field) |
| TR-equip-008 | Read surface for other systems: `GetEquippedWeaponItemID()`, `GetEquippedWeaponEnhancementLevel()`, `GetSlotEnhancementLevel(GearSlot)`, `GetEquipmentSlotState(GearSlot)`; no elemental data is written to Character Stats (CR-EQS-12) | ❌ No ADR (design-only, LOW risk) |
| TR-equip-009 | Accessory Merge: `RequestMerge(slotIdx1, slotIdx2, slotIdx3)` consumes three identical Ring / Necklace items and inserts `MergeResultItemID` at level 0, atomically, with rollback and a critical log if the rollback itself fails (CR-EQS-14) | ADR-010 (Tier 1 calls) |
| TR-equip-010 | Save and load: state is fully reconstructible from 7 `{ItemID, EnhancementLevel}` pairs; on load modifiers are re-registered at each slot's saved level and `IsTransitioning` is false; no startup cleanup pass (CR-EQS-15, AC-EQS-25) | ADR-006 |
| TR-equip-011 | Wire request handling: `EquipRequest` (`gearSlot`, `itemId`, `inventorySlot`; `itemId = 0` routes to `Unequip`) answered by `EquipResult` with the stated `EquipResult` → `EquipFailReason` mapping (CR-EQS-13) — **the source of `ItemLocked` and `ItemNotInInventory` is undecided (OQ-EQS-9)** | ADR-004 — ⚠️ blocked on OQ-EQS-9 |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Open Gates

- **OQ-EQS-9 (before `/create-stories`)** — which layer produces the wire reasons `ItemLocked` and `ItemNotInInventory`: (a) add both to `EquipResult`, or (b) the request handler derives them before calling `Equip`. Owner: Game Designer + Network Programmer. Stories under TR-equip-002 and TR-equip-011 cannot be written unambiguously until this is decided.
- **OQ-EQS-5 (open, not blocking)** — F-EQS-3 stat requirement thresholds are not yet calibrated against base-stat growth in `leveling-system.md`. The thresholds are data, so stories can proceed with the current values.
- **OQ-EQS-6 (open, not blocking the logic stories)** — confirm `EquipmentAppearanceFlags` is present in every full zone snapshot, not only deltas.
- **Known naming drift** — `damage-calculation.md` calls the weapon reads `GetEquippedWeaponID(AttackerID)` / `GetEquippedWeaponEnhancementLevel(EntityID)`; this GDD names them without parameters. Reconcile when the read-surface story is written.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/equipment-system.md` (AC-EQS-1 to AC-EQS-31) are verified
- All Logic and Integration stories have passing test files in `tests/`
- Wire-facing stories that need a live client are scoped as forward-dependency placeholders where no codec or request handler exists yet, matching the project's established pattern

## Next Step

Decide OQ-EQS-9, then run `/create-stories equipment-system` to break this epic into implementable stories.
