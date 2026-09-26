# Epic: Inventory System

> **Layer**: Core
> **GDD**: design/gdd/inventory-system.md
> **Architecture Module**: Inventory
> **Status**: Ready
> **Stories**: 9 stories created 2026-09-25 (all Ready)

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
- Logic stories (all 9) have passing test files in `tests/EditMode/InventorySystem/`

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Slot Container, Core Types & Read API](story-001-slot-container-core-types.md) | Logic | Ready | ADR-010 (event) |
| 002 | [Atomic Pickup Resolution & Stack Limits](story-002-atomic-pickup.md) | Logic | Ready | ADR-010 (event) |
| 003 | [Bag-Full Notification & 30s Dedup Window](story-003-bag-full-notification.md) | Logic | Ready | ADR-010 |
| 004 | [Slot Locks & RemoveItem](story-004-slot-locks.md) | Logic | Ready | ADR-010 (event) |
| 005 | [Discard](story-005-discard.md) | Logic | Ready | ADR-010 (event) |
| 006 | [Slot Move — Merge, Swap & Relocate](story-006-move-merge-swap.md) | Logic | Ready | ADR-010 (event) |
| 007 | [Equipment System Interface](story-007-equipment-interface.md) | Logic | Ready | ADR-010 |
| 008 | [NPC Shop Sell & Consumable Use](story-008-sell-and-consume.md) | Logic | Ready | ADR-010 |
| 009 | [InventorySnapshot Save/Load & Load Validation](story-009-snapshot-save-load.md) | Logic | Ready | ADR-006, ADR-010 |

**GDD AC coverage**: 17 of 18 blocking ACs. **AC-INV-11** (tapping a consumable opens the detail view without consuming) is pure UI — **deferred to a future Inventory UI epic** (Inventory UI GDD and `design/ux/inventory-screen.md` not yet authored). AC-INV-10 is covered on the inventory side only (Story 007); its "equipped item stays equipped" half belongs to the Equipment System epic.

**Open questions to resolve at `/story-readiness`**: ~~(1) Story 001 — `InventoryChangedEvent` payload shape vs ADR-010~~ — RESOLVED 2026-09-25 (reused-buffer `readonly struct` + struct enumerator + re-entrancy guard, see Story 001); (2) Story 006 — moving a consumable onto a different item (proposed default: swap; would add a line to GDD Rule 7.20); plus smaller confirmations noted in Stories 004, 007, 008, 009.

**Out of scope for this epic**: wire-protocol codecs for inventory messages (Networking), Equipment/Loot Table/Persistence/NPC Shop orchestration (their own epics).

## Next Step

Run `/story-readiness production/epics/inventory-system/story-001-slot-container-core-types.md`, then `/dev-story`. Work through stories in order — each story's `Depends on:` field lists its prerequisites.
