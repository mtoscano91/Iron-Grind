# Epic: Inventory System

> **Layer**: Core
> **GDD**: design/gdd/inventory-system.md
> **Architecture Module**: Inventory
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories inventory-system`

## Overview

The Inventory System is the character's personal item bag — a fixed-capacity slot grid that holds all items the character owns but has not equipped. Every item the character picks up from a monster drop, purchases from the NPC Shop, or removes from an equipment slot lands in inventory. The system owns the canonical list of what a character currently carries: slot assignment, item identity (`ItemID`), and stack quantity. It does not own equipped gear (Equipment System), gold (Currency System), or item property definitions (Item Database). At MVP, inventory capacity is a fixed integer per character (20 slots) with no expansion mechanic.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| *(none)* | Pure server-side data structure, no engine API surface — by-design no ADR (architecture.md Engine Knowledge Gap Summary: 🟢 LOW) | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-inv-001 | Fixed 20-slot array (indices 0–19); each slot holds one `ItemID` + `Quantity`; empty = `ItemID.Invalid` with `Quantity = 0`; slot indices are stable across sessions — no repacking on login (Rule 1) | ❌ No ADR (design-only, LOW risk) |
| TR-inv-002 | Equipment items always `StackLimit = 1` (never stack); Consumables stack up to their authored `StackLimit` (max 99); multiple stacks of the same `ItemID` may coexist in different slots (Rule 2) | ❌ No ADR (design-only, LOW risk) |
| TR-inv-003 | Pickup resolution is a single atomic transaction: fill partial stacks first (ascending slot order), then open new slots (ascending slot order); if no empty slot exists and remainder > 0, the entire pickup fails and all partial fills roll back — no partial pickups exist (Rule 3) | ❌ No ADR (design-only, LOW risk) |
| TR-inv-004 | Inventory provides the data interface Equipment System, Enhancement System, and NPC Shop read when a player interacts with items — Inventory does not own equipped gear, gold, or item property definitions | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/inventory-system.md` are verified
- Logic stories (slot structure, stack limits, atomic pickup) have passing test files in `tests/EditMode/InventorySystem/`

## Next Step

Run `/create-stories inventory-system` to break this epic into implementable stories.
