# Inventory System

> **Status**: Approved (lean re-review 2026-10-01 — per-slot `EnhancementLevel` amendments (TD-045/TD-043) reviewed; 2 blockers fixed in-session: level invariant keyed on `StackLimit = 1`, `MAX_ENHANCEMENT_LEVEL` dependency declared; identical-item move decided as a no-op; AC-INV-21/22 added). Previous: lean re-review 2026-05-17 — B-INV-1 PickupRequest signature fixed; R-2 MoveItemIn return type added; OQ-INV-5 resolved; OQ-INV-6 added for wire schemas)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-10-01 (TD-045/TD-043 design session: slots gain a per-item `EnhancementLevel: byte` — Rule 1.1/1.4, `SetEnhancementLevel` (Rule 5.14a), level travels on move/swap (Rule 7.20) and across the Equipment interface (Rule 8.24a), `InventoryChangedEvent` and `InventorySnapshot` carry it, load rules for an invalid level, AC-INV-17–22 added; Enhancement scroll consumption uses `ConsumeItem`. Lean re-review completed the same day — see Status.) Same day (Story 009: Persistence and Load Edge Cases extended — out-of-range `SlotIndex`, `ItemId = 0`, over-limit quantity loads as-is, load refused before Item Database ready, load fires no events; Story 008 readiness: `SellItem` gains a `quantity` parameter — partial-stack sells supported, aligning with NPC Shop CR-SHOP-7/8 and the wire `SellRequest.quantity`; `ConsumeItem` skips locked slots — Rule 5.12; AC-INV-16 updated). Previous: 2026-09-27 (Story 006 readiness: Rule 7.20 clarified — different-item moves swap, full-destination merge is a no-op success; AC-INV-9 reasons aligned to wire `MoveFailReason`); 2026-05-22 (Equipment System upstream contract: MoveItemOut return type extended to MoveItemOutResult; ForceInsert added — OQ-EQS-3)
> **Implements Pillar**: Earned Power (primary), Legendary Gear (secondary)

## Overview

The Inventory System is the character's personal item bag — a fixed-capacity slot grid that holds all items the character owns but has not equipped. Every item the character picks up from a monster drop, purchases from the NPC Shop, or removes from an equipment slot lands in inventory. The system owns the canonical list of what a character currently carries: slot assignment, item identity (`ItemID`), stack quantity, and — for an unequipped piece of gear — its enhancement level. It does not own equipped gear (Equipment System), gold (Currency System), or item property definitions (Item Database). The Inventory System enforces carry capacity — if the bag is full, new items cannot be picked up until space is freed. It provides the data interface that Equipment System, Enhancement System, and NPC Shop read when a player interacts with their items. At MVP, inventory capacity is a fixed integer per character with no expansion mechanic.

## Player Fantasy

The inventory is not the fantasy — it's where the fantasy is accounted for. After a kill streak, you open the bag and meet the evidence of the last hour: a column of drops, some worthless, one or two worth keeping. The satisfaction is quiet and honest — sorting through what you actually earned, deciding what feeds the furnace (sold), what builds the character (kept), and what risks the next upgrade attempt (enhancement candidates waiting in a slot). Capacity pressure gives every kept item weight: the bag is finite, so choosing to carry something is a small act of conviction. The inventory is the breath between grinding and progression — the ritual that makes the loop feel complete before you go back out.

## Detailed Design

### Core Rules

**Rule 1 — Slot Structure**

1. The inventory is a fixed-length array of 20 slots, indexed 0–19. Each slot holds exactly one `ItemID`, a `Quantity` integer, and an `EnhancementLevel: byte`. An empty slot is represented by `ItemID.Invalid` (`ItemID(0)`) with `Quantity = 0` and `EnhancementLevel = 0`.
2. Slot indices are stable across sessions — the server does not repack or reorder slots on login. A player's item in slot 7 before logout is in slot 7 after login.
3. Inventory capacity is fixed at 20 slots for MVP. No expansion mechanic exists.
4. `EnhancementLevel` is a property of the single item occupying a slot (Enhancement System CR-ENH-1), in the range `[0, MAX_ENHANCEMENT_LEVEL]` (`MAX_ENHANCEMENT_LEVEL` is owned by the Enhancement System; the Inventory System reads it as an upper bound only). It can be non-zero only for an item whose `StackLimit = 1` (in practice, equipment); a slot holding a stackable item (`StackLimit > 1`) always has `EnhancementLevel = 0`, whatever its quantity — so a later pickup or stack merge can never produce a stack with a level. Every item that enters the bag through a pickup (Rule 3) starts at `EnhancementLevel = 0`. A slot's `EnhancementLevel` returns to 0 whenever the slot becomes empty (discard, sell, equip-from-bag, `RemoveItem`, consume). The Inventory System stores and transports the level; it never computes with it — the Enhancement System is the only writer (Rule 5.14a) and the Equipment System the only other carrier (Rule 8.24a). *(Added 2026-10-01, TD-045.)*

**Rule 2 — Stack Limits**

4. Equipment items (`ItemCategory == Equipment`) always have `StackLimit = 1`: one item per slot, no stacking. The Equipment System enforces that equipment items are never placed into an already-occupied slot.
5. Consumable items (`ItemCategory == Consumable`) stack up to `StackLimit` units per slot, where `StackLimit` is the authored value on `ItemDefinition` (maximum 99 per slot in MVP).
6. Two stacks of the same consumable `ItemID` may coexist in different slots. They are merged only if a pickup or move operation triggers a merge (see Rule 3 and Rule 7).

**Rule 3 — Pickup Resolution (Atomic)**

7. When a loot event delivers an item to a character, the server resolves the pickup as a single atomic transaction:
   - **Step 1 — Fill partial stacks**: Scan slots 0–19 in ascending index order. For each slot holding the same `ItemID` with `Quantity < StackLimit`, add units until either the pickup remainder reaches 0 or the stack reaches `StackLimit`. Continue to the next partial stack if remainder > 0.
   - **Step 2 — Open new slots**: If remainder > 0 after filling all existing partial stacks, scan slots 0–19 in ascending index order for the first empty slot (`ItemID.Invalid`). Place the remainder (up to `StackLimit`) in that slot. Repeat if still more units remain.
   - **Step 3 — Atomic fail**: If at any point during Step 2 no empty slot exists and remainder > 0, the entire pickup fails. No units were added — Step 1 partial fills are rolled back. The item is not created on the server.
8. Partial pickups do not exist: the transaction either succeeds in full or fails in full.

**Rule 4 — Full Inventory and Blocked Drops**

9. When a mob dies and the server determines a drop would fail (full-pickup-fails result from Rule 3), the Inventory System returns `PickupResult(fail)` to the Loot Table System. The fate of the blocked drop — whether permanently discarded, placed in a short-lived ground pool, or held by another mechanism — is owned by the **Loot Table System GDD**. The Inventory System makes no decision about drop fate; it only reports the failure.
10. The owning player receives an `InventoryFullNotification` and the Inventory UI activates a **persistent HUD bag-full indicator** that remains visible while the bag is full. To prevent notification spam during active grinding, the server fires at most one `InventoryFullNotification` per character per **30-second deduplication window**; additional blocked drops within that window suppress the notification event while the HUD indicator stays active. The window resets when the character makes a successful pickup.
11. Party loot: each party member receives their own independent loot roll. One member's full inventory blocks only their own drops; other party members' pickups are unaffected. The Loot Table System GDD owns the party loot roll distribution contract.

**Rule 5 — Item Locks (Enhancement Reservation)**

12. Any inventory slot can be locked. A locked slot's item cannot be moved, equipped, sold, discarded, or consumed until the lock is released. `ConsumeItem` skips locked slots: a locked stack is neither decremented nor counted toward the quantity available to consume.
13. The Enhancement System is the only system that may lock slots. It calls `LockSlot(slotIndex)` when an enhancement attempt begins and `UnlockSlot(slotIndex)` when the attempt resolves (success or failure, including server timeout).
14. The Inventory System exposes `IsSlotLocked(slotIndex): bool` so the Inventory UI can render the locked visual state.
14a. The Enhancement System is the only system that may change a bag item's enhancement level. It calls `SetEnhancementLevel(slotIndex, level): bool` while it holds the lock on that slot (between `LockSlot` and `UnlockSlot`). The call succeeds only if the slot is occupied, **locked**, holds exactly one unit of an item whose `StackLimit = 1`, and `level ≤ MAX_ENHANCEMENT_LEVEL`; on success the slot's `EnhancementLevel` becomes `level` and one `InventoryChangedEvent` fires for that slot. Setting the level the slot already has is a no-op success with no event. Any other call (unlocked slot, empty slot, a stackable item or a quantity other than 1, level above the maximum, out-of-range index) is a caller bug: it returns `false`, logs a server error, and changes nothing. *(Added 2026-10-01, TD-045.)*

**Rule 6 — Discard**

15. A player may discard any inventory item that is not locked (Rule 5).
16. Discard is initiated by a long-press-hold gesture on the item in the Inventory UI. After the hold completes (recommended duration: 0.8s — see OQ-INV-1), a quantity selector appears. For equipment items (StackLimit = 1), the selector is omitted — the single unit is always discarded. For consumable stacks, the player adjusts the quantity (minimum 1, maximum current stack size) before confirming. A single tap does not discard — the hold-to-confirm and quantity-selection step prevents accidental destruction.
17. Discarding an equipment item removes it permanently. Discarding a consumable quantity removes exactly the selected quantity from the stack. If the selected quantity equals the current stack size, the slot becomes `ItemID.Invalid, Quantity = 0`. If less than the current stack, the slot retains the remainder.
18. Discard is server-authoritative: the client sends a `DiscardRequest(slotIndex, quantity)`. The server validates the lock state and validates `0 < quantity ≤ current slot Quantity`, then responds with `DiscardResult(success/fail)`. On success, the slot quantity is decremented by the requested quantity (or cleared entirely if quantity equals current stack size).

**Rule 7 — Slot Move (Rearrange)**

19. A player may move an item between any two unlocked slots in their own inventory.
20. Moving a consumable stack over another slot holding the same `ItemID`: the two stacks merge, up to `StackLimit`; overflow remains in the source slot. Moving an equipment item over an occupied slot swaps the two items. Moving any item over an occupied slot holding a different `ItemID` swaps the two items. Moving a stackable item (`StackLimit > 1`) onto a same-`ItemID` stack already at `StackLimit` is a no-op success (nothing transferred, no event). *(Clarified 2026-09-27, Story 006 readiness.)* Merging applies only to stackable items (`StackLimit > 1`), whose `EnhancementLevel` is always 0. Two items with `StackLimit = 1` never merge — they **swap**, including two items with the same `ItemID` but different `EnhancementLevel`, which are distinct items. The one exception is two **identical** `StackLimit = 1` items (same `ItemID` **and** same `EnhancementLevel`): exchanging them changes neither slot, so the move is a no-op success with no event. On every relocate and swap, each item's `EnhancementLevel` travels with it to its new slot. *(Clarified 2026-10-01, TD-045; identical-item no-op decided at the 2026-10-01 lean re-review.)*
21. Moves are server-authoritative: the client sends `MoveRequest(fromSlot, toSlot)`. The server validates both slots are unlocked and responds with the updated slot states.

**Rule 8 — Unequip to Bag**

22. When the Equipment System equips a new item into a slot that is already occupied, the old item returns to the character's inventory.
23. The Equipment System calls `HasFreeSlot(): bool` on Inventory before executing the equip. For equipment items (StackLimit = 1), a free slot means at least one slot with `ItemID.Invalid, Quantity = 0`.
24. If `HasFreeSlot()` returns `false`, the equip is blocked. The Equipment System surfaces: "Inventory full — free a slot before equipping." The currently-equipped item remains equipped.
24a. An item's `EnhancementLevel` crosses the Equipment boundary in both directions. `MoveItemOut(slotIndex)` returns the removed item's `EnhancementLevel` alongside its `ItemID`; the Equipment System stores it in the gear slot. `MoveItemIn(ItemID, enhancementLevel)` and `ForceInsert(ItemID, enhancementLevel)` place the item at quantity 1 with the given level (0 for a newly created item such as an accessory merge result). A level above `MAX_ENHANCEMENT_LEVEL`, or a non-zero level for an item whose `StackLimit > 1`, is a caller bug: the call fails with a server error and no mutation. *(Added 2026-10-01, TD-045.)*

**Rule 9 — Consumable Assignment (No Direct Use from Bag)**

25. Tapping a consumable stack in the Inventory UI opens an item detail view with two actions: **Use** (primary) and **Assign to Hotbar** (secondary). Use initiates an immediate `ConsumeItem(ItemID, 1)` call — no hotbar assignment is required for direct use. Assign to Hotbar links the item to a hotbar slot for repeated access during combat. The Consumable Use System owns the use-from-hotbar flow and per-type cooldown enforcement. Both paths call the same server `ConsumeItem(ItemID, quantity)` interface.

**Rule 10 — Within-Tick Request Ordering**

26. The server processes all inventory requests received in a single tick in FIFO order (server message-receipt order — the order the server received the messages, which may differ from the order the client sent them due to network reordering). If a pickup and a discard arrive in the same tick, the pickup is resolved first if received first. No priority override within normal inventory operations.

---

### States and Transitions

| Slot State | Condition | Transitions |
|------------|-----------|------------|
| **Empty** | `ItemID.Invalid, Quantity = 0` | → Occupied-Available (Pickup, Unequip-to-bag) |
| **Occupied — Available** | Valid `ItemID`, `Quantity > 0`, not locked | → Empty (Discard full quantity, Sell-all, Equip from slot); → Occupied-Locked (Enhancement System calls `LockSlot`); → Occupied-Available with updated qty (Pickup partial fill, Move merge, Discard partial quantity) |
| **Occupied — Locked** | Valid `ItemID`, `Quantity > 0`, lock flag set | → Occupied-Available (`UnlockSlot` called by Enhancement System after attempt resolution); → Empty (`RemoveItem` called by Enhancement System on item destruction); → Occupied-Locked with updated `EnhancementLevel` (`SetEnhancementLevel` called by Enhancement System on success) |

No state is reachable from Occupied-Locked except through Enhancement System calls. Any attempt to mutate a locked slot (equip, sell, move, discard) returns an error without modifying the slot.

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Item Database** | ← reads | `GetItem(ItemID)` → `StackLimit`, `ItemCategory`, `DisplayName`, `IconAddress`, `SellPriceGold` | On pickup (StackLimit check), on any UI display event |
| **Loot Table System** | ← receives events | `PickupRequest(CharacterID, ItemID, quantity)` → `PickupResult(success/fail)` | On mob death; Loot Table System owns the drop roll; Inventory owns the bag mutation. `quantity=1` for all single-item loot drops at MVP. |
| **Equipment System** | ↔ bidirectional | `HasFreeSlot(): bool` (queried before equip); `MoveItemOut(slotIndex): MoveItemOutResult { ItemID, EnhancementLevel: byte, Code: Success \| SlotEmpty \| SlotLocked }` (equip-from-bag empties the slot — returns item, its enhancement level, and status; SlotLocked when Enhancement System holds the slot); `MoveItemIn(ItemID, enhancementLevel: byte): MoveItemInResult { success: bool, slotIndex: int }` (unequip-to-bag fills a free slot at the given level; slotIndex = -1 on failure); `ForceInsert(ItemID, enhancementLevel: byte): bool` (places an accessory merge result, or restores merge source items on rollback, into any free slot; returns false only if all 20 slots occupied — triggers `InventoryFullNotification` wire message; added 2026-05-22 per OQ-EQS-3; level parameters added 2026-10-01, TD-045) | On equip, unequip and accessory-merge actions |
| **Enhancement System** | ← lock requests, level writes | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `SetEnhancementLevel(slotIndex, level): bool` (on success outcome — locked slot only, Rule 5.14a), `RemoveItem(slotIndex)` (on item destruction — clears the whole slot), `ConsumeItem(scrollItemID, 1)` (consumes exactly one Enhancement Scroll from the lowest-index unlocked stack — scrolls are stackable Consumables, so `RemoveItem` must not be used for them; 2026-10-01, TD-043). Reads the slot via `GetSlot(slotIndex): { ItemID, Quantity, EnhancementLevel }` for validation. | On enhancement attempt begin, resolve, and destroy outcomes |
| **NPC Shop** | ← sell requests | `SellItem(slotIndex, ItemID, quantity)` — Inventory validates the slot (index in range, slot holds `ItemID`, not locked, `1 ≤ quantity ≤` the slot's stack count), removes `quantity` units, returns quantity sold; a slot sold down to 0 becomes empty. NPC Shop adds gold via Currency System. Partial-stack sells are supported (NPC Shop CR-SHOP-8; "sell all" passes the full stack count). | On player sell action |
| **Character Persistence** | ↔ save/load | `InventorySnapshot { Slots: [{ SlotIndex: byte, ItemId: uint, Quantity: int, EnhancementLevel: byte }] }` — non-empty slots only | On session end (save) and session start (load) |
| **Inventory UI** | ← reads | Slot array (ItemID + Quantity + EnhancementLevel per slot), `IsSlotLocked(slotIndex)`, `InventoryChangedEvent { changes: [{ slotIndex: int, itemId: uint, quantity: int, enhancementLevel: byte }] }` (fired after any slot mutation — quantity = 0 means slot became empty) | On bag open, on any slot mutation |
| **Consumable Use System** | ← reads | `HasItem(ItemID): bool`, `ConsumeItem(ItemID, quantity)` — when multiple stacks of the same ItemID exist, decrements from the lowest slot index first (0→19 FIFO, consistent with the pickup scan pattern); locked slots are skipped (Rule 5.12); if the unlocked total is less than `quantity` the call fails with no mutation | On hotbar use or direct-from-bag Use action; Inventory decrements or removes the stack |

## Formulas

The Inventory System has no combat math. Its formulas define capacity boundaries and the arithmetic of stack management.

---

**F-INV-1: Theoretical Maximum Item Capacity**

`MaxItems = (S_equip × 1) + (S_cons × StackLimit_max)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Equipment slots in use | `S_equip` | int | [0, 20] | Slots holding equipment items (StackLimit = 1) |
| Consumable slots in use | `S_cons` | int | [0, 20] | Slots holding consumable stacks |
| Max consumable stack size | `StackLimit_max` | int | [1, 99] | Highest authored StackLimit on any ItemDefinition |

**Constraint:** S_equip and S_cons are not independent variables — their sum is bounded by `INVENTORY_SLOT_COUNT`: `S_equip + S_cons ≤ 20`. F-INV-1 represents the theoretical maximum capacity *given* that constraint; the variable ranges [0,20] each are individual bounds, not simultaneous maxima.

**Output Range:** 0 (empty bag) to 1,980 (20 slots × 99 consumables, S_cons = 20). Constrained at the slot level by `INVENTORY_SLOT_COUNT = 20`.

**Example:** 7 equipment slots filled + 13 consumable slots at StackLimit=99 → 7 + 1,287 = 1,294 total items carried.

---

**F-INV-2: Stack Overflow on Pickup**

`Overflow = max(0, CurrentQty + IncomingQty − StackLimit)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Existing stack quantity | `CurrentQty` | int | [0, 99] | Units already in the target slot |
| Pickup quantity | `IncomingQty` | int | [1, 99] | Units being added |
| Stack limit | `StackLimit` | int | [1, 99] | Authored on ItemDefinition for this item |
| Overflow | `Overflow` | int | [0, 98] | Units that cannot fit and must continue to the next FIFO slot |

**Precondition:** F-INV-2 is applied only within the Rule 3 Step 1 partial-stack scan, where the target slot holds the same `ItemID` with `Quantity < StackLimit`. Under this precondition, `CurrentQty ∈ [1, StackLimit-1]` and `StackLimit ∈ [2, 99]` (items with StackLimit=1 have no partial stacks and skip Step 1 entirely). Applying boundary values outside this precondition produces results that cannot occur in practice.

**Output Range:** 0 (pickup fits in current slot) to 98 (under the partial-stack precondition, maximum is max(0, (StackLimit-1) + 99 − StackLimit) = 98). Overflow units continue the atomic pickup sequence (Rule 3) — they do not disappear.

**Example:** CurrentQty=80, IncomingQty=30, StackLimit=99 → Overflow=11. Those 11 units attempt fill in the next available slot; if no slot exists, the entire pickup fails (Rule 3 Step 3).

---

**F-INV-3: Full-Inventory Gate**

`IsFull = (FilledSlots ≥ INVENTORY_SLOT_COUNT)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Slots with items | `FilledSlots` | int | [0, 20] | Count of slots where `Quantity > 0` |
| Slot count constant | `INVENTORY_SLOT_COUNT` | int | 20 | Fixed inventory size |
| Full state | `IsFull` | bool | {true, false} | Quick-rejection gate |

**Note:** `IsFull = true` does not automatically block pickups. A pickup of the same consumable `ItemID` can succeed if any slot of that type has `Quantity < StackLimit`, even when all 20 slots are occupied. The atomic pickup check (Rule 3) is the authoritative gate — F-INV-3 is the quick-rejection check used when no partial-stack merge is possible for this `ItemID`.

## Edge Cases

**Pickup Edge Cases**

- **If `IncomingQty = 0` arrives in a `PickupRequest`**: Reject immediately with no state mutation. Do not write `ItemID` into any slot with `Quantity = 0` — this would create a phantom occupied entry that corrupts `ItemID.Invalid` semantics.

- **If a consumable's `StackLimit = 1`**: The pickup algorithm treats it identically to equipment — each unit occupies its own slot. Valid behavior; note in Item Database authoring tools that `StackLimit = 1` on a consumable is unusual.

- **If two `PickupRequest`s for the same `ItemID` arrive in the same tick**: The second pickup's partial-stack scan runs against the state left by the first (post-first-operation state within the tick). Correct FIFO behavior — no special handling needed.

---

**Full Inventory / Capacity Edge Cases**

- **If `HasFreeSlot()` returned `true` at Equipment System query time but a slot fills before `MoveItemIn` executes**: `MoveItemIn` performs its own free-slot check at execution time. If no slot is available, return a failure result. The Equipment System handles the failure and surfaces it to the player — the unequipped item must not be silently lost.

- **If all 20 slots are locked by the Enhancement System simultaneously**: `HasFreeSlot()` returns `false`. The player cannot discard, move, or sell any item. The only unblock path is waiting for all enhancement attempts to resolve. This state is achievable but is not an error — no special handling beyond the standard lock rules.

---

**Lock State Edge Cases**

- **If `LockSlot(slotIndex)` is called against an empty slot** (e.g., player discarded the item in the same tick before `LockSlot` arrived): No-op — log a server warning, do not set a lock flag on an empty slot. The Enhancement System must call `UnlockSlot` on resolution regardless.

- **If `UnlockSlot(slotIndex)` is called on a slot that is not currently locked**: No-op, no exception thrown. Enhancement System timeout and failure paths call `UnlockSlot` defensively — throwing on double-unlock would make timeout recovery a crash vector.

- **If the server session expires (`SESSION_TTL_SECONDS = 300`) while slots are locked**: Slot contents are persisted in `InventorySnapshot`; lock flags are **not** persisted — locks are session-scoped. On next login, all slots that were `Occupied-Locked` load as `Occupied-Available` with their item and quantity intact. The Enhancement System is responsible for detecting that an in-progress attempt was interrupted — it must check its own persistence on login and handle the interrupted state without relying on the Inventory System reporting locked state. This GDD's contract: *locks do not survive a session boundary; the Inventory System presents all slots as unlocked at session start*.

---

**Discard and Move Edge Cases**

- **If a `DiscardRequest` and a `LockSlot` call target the same slot in the same tick**: FIFO ordering determines which lands first. If `LockSlot` arrives first: `DiscardRequest` is rejected (locked item cannot be discarded). If `DiscardRequest` arrives first: item is destroyed; subsequent `LockSlot` is a no-op against the empty slot.

- **If a `MoveRequest` targets a slot that became locked between the client gesture and server validation**: The server validates lock state at execution time. The move is rejected; the client rolls back any optimistic UI rendering without visible flicker.

- **If the source and destination slot of a `MoveRequest` are the same index**: No-op, return success. No state mutation.

---

**Cross-System Interface Edge Cases**

- **If `ConsumeItem(ItemID, quantity)` is called but the slot has been discarded since the hotbar assignment**: `ConsumeItem` returns a failure result. The Consumable Use System handles the failure and owns the hotbar desync UI feedback.

- **If `SellItem(slotIndex, ItemID, quantity)` is called by NPC Shop and the `ItemID` in the slot no longer matches the parameter**: Reject the sell and return an error. Both `slotIndex` and `ItemID` must match at execution time — guards against race conditions between UI render and server commit.

- **If `SellItem(slotIndex, ItemID, quantity)` is called with `quantity < 1` or `quantity` greater than the slot's current stack count**: Reject the sell with an invalid-quantity result; no units are removed. A sell never removes fewer units than requested.

- **If `ConsumeItem(ItemID, quantity)` is called and the only stacks of that ItemID are in locked slots**: `ConsumeItem` returns a failure result with no mutation, even though `HasItem(ItemID)` is true. In practice the Enhancement System locks equipment, not consumables, so this is a defensive rule.

- **If `RemoveItem(slotIndex)` is called by the Enhancement System with an out-of-range slot index** (`< 0` or `≥ 20`): Log a server error, no-op. No state mutation. Closes a crash path from stale slot indices after session reloads.

- **If `SetEnhancementLevel(slotIndex, level)` is called on a slot that is not locked, is empty, holds a stackable item (`StackLimit > 1`) or more than one unit, or with `level > MAX_ENHANCEMENT_LEVEL` or an out-of-range index**: Return `false`, log a server error, no state mutation, no event. The lock requirement guarantees the item cannot have been moved, sold, equipped or discarded between the Enhancement System's validation and its write.

- **If two items with the same `ItemID` but different `EnhancementLevel` are moved onto each other**: They swap (Rule 7.20) — both slots change and one `InventoryChangedEvent` carries both entries. If they also have the same level they are identical items: the move is a no-op success with no event (Rule 7.20).

- **If `MoveItemOut(slotIndex)` succeeds on an enhanced item**: The returned `EnhancementLevel` is the only copy of that item's level — the emptied slot resets to 0. The Equipment System must carry it into the gear slot, and must pass it back through `MoveItemIn` on any rollback, or the level is lost.

- **If the Enhancement System's `ConsumeItem(scrollItemID, 1)` fails** (no unlocked scroll of that type remains — e.g. discarded in the same tick): The call returns a failure result with no mutation. The Enhancement System aborts the attempt and unlocks the item slot; no outcome is rolled.

---

**Persistence and Load Edge Cases**

- **If `InventorySnapshot` contains an `ItemId` no longer in the Item Database** (deprecated item): Clear the slot to `ItemID.Invalid, Quantity = 0` on load. Log a server warning with character ID and the unknown `ItemId`.

- **If `InventorySnapshot` contains two entries with the same `SlotIndex`**: Corrupted data. Load the first-encountered entry; reject the second. Log the conflict server-side.

- **If `InventorySnapshot` contains an entry with `ItemId != ItemID.Invalid` and `Quantity = 0`**: Structurally contradictory. Clear the slot on load; log a server warning. Without this correction, F-INV-3's `FilledSlots` count would be inconsistent depending on which field is used as the predicate.

- **If `InventorySnapshot` contains an entry with `Quantity < 0`**: Structurally invalid. Clear the slot to `ItemID.Invalid, Quantity = 0` on load; log a server warning. Closes a load-corruption path where a negative quantity would make F-INV-3's `FilledSlots` count unreliable.

- **If `InventorySnapshot` contains an entry with `SlotIndex ≥ 20`**: Out of range. Reject the entry; log a server warning. No exception; the remaining entries still load.

- **If `InventorySnapshot` contains an entry with `ItemId = 0` (`ItemID.Invalid`)**: A snapshot holds non-empty slots only. Leave the slot empty; log a server warning. The entry still claims its `SlotIndex` for the duplicate rule above — the first-encountered entry for a slot claims it even when that entry is itself cleared by validation.

- **If `InventorySnapshot` contains an entry whose `Quantity` exceeds the item's `StackLimit`** (e.g. a `StackLimit` was lowered after the save): Load the entry **as-is** with its full quantity; log a server warning. No units are destroyed. Pickup (Rule 3 Step 1) already skips stacks at or above `StackLimit`, so an over-limit stack is safe in memory.

- **If `InventorySnapshot` contains an entry with `EnhancementLevel > MAX_ENHANCEMENT_LEVEL`** (e.g. the maximum was lowered after the save): Load the item with `EnhancementLevel = MAX_ENHANCEMENT_LEVEL`; log a server warning. The item is never destroyed.

- **If `InventorySnapshot` contains an entry with `EnhancementLevel > 0` for a stackable item (`StackLimit > 1`, at any quantity) or with `Quantity > 1`**: Structurally contradictory (Rule 1.4). Load the entry with `EnhancementLevel = 0`; log a server warning. The item and its quantity are kept.

- **If a snapshot load is attempted before the Item Database is ready**: Refuse the load — log a server error and change nothing (existing contents, locks, and registration untouched). Without this guard every entry would fail the Item Database lookup and the whole bag would be cleared. Character Persistence must retry or fail the login; it must not treat a refused load as an empty inventory.

- **A snapshot load is initial state, not a mutation**: it replaces the whole inventory (all slots empty and all locks cleared, then entries apply), resets the bag-full deduplication window, and fires no `InventoryChangedEvent` and no `InventoryFullNotification`. The Inventory UI reads full slot state on bag open.

## Dependencies

**Upstream dependencies** — systems this GDD depends on:

| System | Dependency Type | Interface | Hard or Soft |
|--------|----------------|-----------|-------------|
| **Item Database** | Reads | `GetItem(ItemID)` → `StackLimit`, `ItemCategory`, `DisplayName`, `IconAddress`, `SellPriceGold` | **Hard** — Inventory cannot enforce stack limits or display items without item definitions |
| **Enhancement System** | Reads (constant only) | `MAX_ENHANCEMENT_LEVEL` — the upper bound applied by `SetEnhancementLevel`, `MoveItemIn` / `ForceInsert` and snapshot load validation (Rule 1.4). Supplied to the Inventory System as a configured value; the Inventory System never calls into the Enhancement System. | **Soft** — without an Enhancement System nothing writes a level, every slot stays at 0 and the bound is unused |

**Downstream dependents** — systems that depend on this GDD:

| System | Dependency Type | What They Need | Hard or Soft |
|--------|----------------|----------------|-------------|
| **Equipment System** | Reads / calls | `HasFreeSlot(): bool`, `MoveItemOut(slotIndex): MoveItemOutResult { ItemID, EnhancementLevel: byte, Code: Success \| SlotEmpty \| SlotLocked }`, `MoveItemIn(ItemID, enhancementLevel: byte): MoveItemInResult { success: bool, slotIndex: int }`, `ForceInsert(ItemID, enhancementLevel: byte): bool` | **Hard** — Equipment System cannot execute equip/unequip without the slot interface |
| **Enhancement System** | Reads / calls | `LockSlot(slotIndex)`, `UnlockSlot(slotIndex)`, `SetEnhancementLevel(slotIndex, level)`, `RemoveItem(slotIndex)`, `ConsumeItem(scrollItemID, 1)`, `IsSlotLocked(slotIndex)`, `GetSlot(slotIndex): { ItemID, Quantity, EnhancementLevel }` | **Hard** — Enhancement System cannot store its outcome or safely manage item destruction without the inventory lock/level/remove interface |
| **NPC Shop** | Calls | `SellItem(slotIndex, ItemID, quantity)` → quantity removed; Currency System adds gold | **Hard** — Sell flow cannot execute without Inventory ownership of item removal |
| **Loot Table System** | Calls | `PickupRequest(CharacterID, ItemID, quantity)` → `PickupResult(success/fail)` | **Hard** — Loot Table cannot confirm whether a drop was received without Inventory's pickup result |
| **Inventory UI** | Reads | Slot array (ItemID + Quantity), `IsSlotLocked(slotIndex)`, inventory change events | **Hard** for MVP — UI cannot render without slot data |
| **Consumable Use System** | Calls | `HasItem(ItemID)`, `ConsumeItem(ItemID, quantity)` | **Hard** — Consumable use from hotbar cannot decrement inventory without this interface |
| **Character Persistence** | Reads/writes | `InventorySnapshot` serialization (save) and deserialization (load) | **Hard** — Inventory state is not preserved across sessions without persistence |

**Bidirectionality note:** Every downstream system listed above must reference Inventory System in its own Dependencies section. This is a flagged requirement for each of those GDDs. The Enhancement System appears on both sides: it is a downstream caller of the lock/level/remove interface, and the owner of the one constant (`MAX_ENHANCEMENT_LEVEL`) this GDD reads upstream. This is not a design-order cycle — the constant is a registry value, and the Inventory System is fully specified and implementable without the Enhancement System's rules.

**No dependency on:**
- Currency System — Inventory does not own gold; gold mutation is NPC Shop's responsibility (it calls Currency System directly after a sell)
- Networking Core — Inventory is a server-side data structure; networking message routing is handled by the session layer, not authored in this GDD

## Tuning Knobs

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| `INVENTORY_SLOT_COUNT` | 20 | [15, 40] | Too low (< 15): inventory fills mid-session before a reasonable town trip in 20 minutes — frustration exceeds pressure. Too high (> 30): capacity pressure is eliminated; the "carry what matters" fantasy weakens and sell-trip frequency drops, reducing the grind loop's natural pacing. |
| `StackLimit` (per consumable, authored in Item Database) | Up to 99 | [10, 99] | Below 10: consumable hoarding becomes slot-prohibitive and players stop picking up potions. Capped at 99 (F-INV-1 ceiling). Per-item `StackLimit` is the primary tuning lever for consumable pressure vs. gear slot availability. |
| `DISCARD_HOLD_DURATION` | TBD — UX-driven | [0.5s, 2.0s] | Too short (< 0.5s): accidental discards on mobile touch. Too long (> 2.0s): discard feels punishing; players avoid using it and remain stuck with full bags. |

**Cross-system knobs that affect inventory behavior:**
- `MAX_ENHANCEMENT_LEVEL` (Enhancement System; default 10, safe range [9, 12]) — the Inventory System uses it only as the upper bound for a slot's `EnhancementLevel`. Lowering it clamps any saved bag item above the new maximum on its next load (Persistence and Load Edge Cases); raising it has no effect on existing items.
- `DROP_RATE` (Loot Table System) — higher drop rates fill inventory faster, making `INVENTORY_SLOT_COUNT` feel smaller. Tune these two together during balance passes.
- `StackLimit` (Item Database) — raising it on consumables makes the same slot count feel more spacious; this is a gentler capacity adjustment than raising `INVENTORY_SLOT_COUNT`.

## Visual/Audio Requirements

N/A — The Inventory System has no direct audio or visual output. All inventory-related sound effects (pickup chimes, full-bag warning, discard confirmation) and visual feedback (slot highlight, lock visual) are owned by the Inventory UI and Audio System, which consume inventory change events. Audio cue specs are authored in the Audio System and Inventory UI GDDs.

## UI Requirements

The Inventory System requires a player-facing screen with the following capabilities:

1. A 20-slot grid displaying item icons and Quantity badges per slot. A slot whose item has `EnhancementLevel > 0` shows the level (e.g. "+5") — badge style specified in `design/ux/inventory-screen.md`.
2. Locked-slot visual indicator (greyed-out or chained icon) driven by `IsSlotLocked(slotIndex)`.
3. Tap-to-select → context action model (no drag-and-drop — touch platform). The selection state machine (how selection is entered, exited, and what a second tap means) is specified in `design/ux/inventory-screen.md`. Long-press-hold is independent of tap-selection state: it always targets the pressed slot directly.
4. Long-press-hold gesture to initiate discard. After hold completes, a quantity selector appears (omitted for equipment items — single unit always discarded). Player selects quantity (minimum 1, maximum current stack size), then confirms destruction.
5. Tapping a consumable opens an item detail view with **Use** (primary action) and **Assign to Hotbar** (secondary action). No bare Use button in the slot grid. Tapping equipment opens an item detail view with equipment-appropriate actions (equip, sell, discard) — layout specified in `design/ux/inventory-screen.md`.
6. Persistent HUD bag-full indicator (active while bag is full); "Inventory full" toast notification (at most one per 30-second window per Rule 4).
7. Sort affordance — **client-side display reorder only**. Sort reorders the visual grid; server slot indices are unchanged and stable (Rule 1). Sort order options are TBD by UX Designer, to be specified in `design/ux/inventory-screen.md`.

> **📌 UX Flag — Inventory System**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design inventory-screen` to create a UX spec for the inventory screen before writing stories that reference this UI. Stories referencing the inventory bag should cite `design/ux/inventory-screen.md`, not this GDD directly.

## Acceptance Criteria

**AC-INV-1** [BLOCKING]
GIVEN a character with a full inventory (20/20 slots), WHEN a mob dies with a drop for that character, THEN the Inventory System returns `PickupResult(fail)`, no inventory slot is mutated, and the character receives an `InventoryFullNotification` (if not within the 30-second dedup window). The fate of the blocked drop is owned by the Loot Table System GDD.

**AC-INV-2** [BLOCKING]
GIVEN a slot at index 2 holding 80 HP Potions (StackLimit=99) and slot index 5 empty (all other slots occupied), WHEN a pickup of 30 HP Potions arrives, THEN the `InventoryChangedEvent` contains two entries: slot 2 updated to quantity=99 and slot 5 updated to quantity=11 (F-INV-2 verified, FIFO scan confirmed via slot index).

**AC-INV-3** [BLOCKING]
GIVEN a character with items placed in inventory slots 0, 7, and 19 (ItemID and Quantity logged for each before logout), WHEN the character logs out and logs back in, THEN each item is in its original slot (0, 7, and 19 respectively) with the same ItemID and unchanged Quantity. Slots not in the snapshot remain empty. No reordering or reassignment is acceptable.

**AC-INV-4** [BLOCKING]
GIVEN a slot is locked by the Enhancement System, WHEN the player sends a `DiscardRequest(slotIndex, quantity=1)` for that slot, THEN the server responds with `DiscardResult(success=false, error=SlotLocked)`, no `InventoryChangedEvent` fires, and the item remains in the slot with unchanged ItemID and Quantity.

**AC-INV-5** [BLOCKING]
GIVEN a seeded inventory state (via test harness injection — see networking-test-harness.md for fixture protocol) with exactly two slots each holding 98 HP Potions (StackLimit=99) and all 20 slots occupied, WHEN a pickup of 3 HP Potions arrives, THEN the pickup fails atomically — both HP Potion slots retain exactly 98 HP Potions, no `InventoryChangedEvent` fires, and `PickupResult(fail)` is returned.

**AC-INV-6** [BLOCKING]
GIVEN a slot holding one Bronze Sword (Equipment, StackLimit=1) and at least one empty slot exists, WHEN a pickup of a second Bronze Sword arrives, THEN the `InventoryChangedEvent` shows the second sword placed in a new empty slot; the original slot retains exactly 1 Bronze Sword with unchanged ItemID.

**AC-INV-7a** [BLOCKING]
GIVEN a slot holding 45 HP Potions with no lock applied, WHEN the player selects quantity 20 via the discard quantity selector and confirms, THEN the server responds with `DiscardResult(success=true)`, the slot holds exactly 25 HP Potions, and `InventoryChangedEvent` containing one changes entry `{ slotIndex, itemId, quantity: 25 }` fires.

**AC-INV-7b** [BLOCKING]
GIVEN a slot holding 45 HP Potions with no lock applied, WHEN the player selects the full quantity (45) and confirms discard, THEN the slot becomes `ItemID.Invalid, Quantity = 0` and `InventoryChangedEvent` containing one changes entry `{ slotIndex, itemId: 0 (ItemID.Invalid), quantity: 0 }` fires.

**AC-INV-7c** [BLOCKING]
GIVEN a slot holding 45 HP Potions, WHEN the client sends `DiscardRequest(slotIndex, quantity=100)` (quantity exceeds current stack), THEN the server responds with `DiscardResult(success=false, error=InvalidQuantity)`, no `InventoryChangedEvent` fires, and the slot retains exactly 45 HP Potions.

**AC-INV-8** [BLOCKING]
GIVEN slot A holds 50 HP Potions and slot B holds 30 HP Potions (same ItemID, StackLimit=99), WHEN the player moves slot A onto slot B, THEN slot B holds exactly 80 HP Potions and slot A is empty (`ItemID.Invalid, Quantity = 0`). Both changes appear in a single `InventoryChangedEvent`.

**AC-INV-9** [BLOCKING]
GIVEN slot A is locked by the Enhancement System and slot B is unlocked, WHEN the player attempts to swap them via `MoveRequest(A, B)`, THEN the swap is rejected with `MoveResult(fail, reason=SourceLocked)`, and both slots are unchanged. If instead slot B is locked and slot A is unlocked, the move is rejected with `reason=DestLocked`. *(Reason names aligned with `networking-wire-protocol.md` `MoveFailReason`, 2026-09-27.)*

**AC-INV-10** [BLOCKING]
GIVEN all 20 inventory slots are occupied and a character attempts to equip a new item into a slot that already holds an equipped item (forcing an unequip-to-bag), WHEN the Equipment System calls `HasFreeSlot()` and receives `false`, THEN: (a) the currently-equipped item remains in the equipment slot with unchanged ItemID, (b) no inventory slot is mutated (verified by reading all 20 slots before and after the attempt), (c) no `InventoryChangedEvent` fires.

**AC-INV-11** [BLOCKING]
GIVEN a consumable is in an inventory bag slot, WHEN the player taps the consumable stack in the inventory UI, THEN no `ConsumeItem` call is made, no quantity is decremented, and the item detail view opens with **Use** (primary) and **Assign to Hotbar** (secondary) actions visible. A use action only fires if the player subsequently taps Use in the detail view.

**AC-INV-12** [BLOCKING]
GIVEN two `PickupRequest`s for different ItemIDs are injected by the test harness in a defined delivery order into the same server tick (per networking-test-harness.md injection protocol), WHEN both are processed, THEN the first-delivered request occupies the lower-index available slot and the second-delivered request occupies the next available slot. Verified via `InventoryChangedEvent` slot index assignments. (Note: server-receipt order is authoritative — client-send order is not guaranteed over network.)

**AC-INV-13** [BLOCKING]
GIVEN an inventory with empty slots at indices 3, 7, and 15 (all other slots occupied, no existing partial stacks for the incoming ItemID), WHEN a pickup of HP Potions (new ItemID) arrives, THEN the HP Potions are placed in slot index 3 (lowest-index empty slot). Verified via `InventoryChangedEvent` showing `slotIndex: 3`.

**AC-INV-14** [BLOCKING]
GIVEN slot A holds 70 HP Potions and slot B holds 60 HP Potions (same ItemID, StackLimit=99), WHEN the player moves slot A onto slot B (`MoveRequest(A, B)`), THEN slot B holds exactly 99 HP Potions, slot A holds exactly 31 HP Potions (overflow remainder stays in source), and `InventoryChangedEvent` shows both updated slot states.

**AC-INV-15** [BLOCKING]
GIVEN all 20 inventory slots are occupied and a mob drops a Bronze Sword (Equipment, StackLimit=1), WHEN the pickup is attempted, THEN `PickupResult(fail)` is returned, no inventory slot is mutated, and the character receives an `InventoryFullNotification` (if not within the 30-second dedup window). The Bronze Sword fate is owned by the Loot Table System GDD.

**AC-INV-16** [BLOCKING]
GIVEN slot 4 holds 5 HP Potions (Consumable) with no lock applied, WHEN the NPC Shop calls `SellItem(4, HPPotionItemID, 5)`, THEN the server returns `quantity=5`, slot 4 becomes `ItemID.Invalid, Quantity=0`, and `InventoryChangedEvent` containing one changes entry `{ slotIndex: 4, itemId: 0, quantity: 0 }` fires. From the same starting state, WHEN the NPC Shop instead calls `SellItem(4, HPPotionItemID, 2)`, THEN the server returns `quantity=2`, slot 4 holds 3 HP Potions, and `InventoryChangedEvent` containing one changes entry `{ slotIndex: 4, itemId: HPPotionItemID, quantity: 3 }` fires.

**AC-INV-17** [BLOCKING]
GIVEN slot 3 holds one Iron Sword at `EnhancementLevel = 5` and the slot is locked by the Enhancement System, WHEN the Enhancement System calls `SetEnhancementLevel(3, 6)`, THEN the call returns `true`, slot 3 holds one Iron Sword at `EnhancementLevel = 6`, and `InventoryChangedEvent` containing one changes entry `{ slotIndex: 3, itemId: IronSwordItemID, quantity: 1, enhancementLevel: 6 }` fires. From the same starting state but with slot 3 **unlocked**, WHEN `SetEnhancementLevel(3, 6)` is called, THEN it returns `false`, the level stays 5, and no `InventoryChangedEvent` fires.

**AC-INV-18** [BLOCKING]
GIVEN slot 3 holds one Iron Sword at `EnhancementLevel = 5`, unlocked, and slot 0 is the lowest-index empty slot, WHEN the Equipment System calls `MoveItemOut(3)`, THEN the result is `{ ItemID: IronSwordItemID, EnhancementLevel: 5, Code: Success }` and slot 3 is empty with `EnhancementLevel = 0`. WHEN the Equipment System then calls `MoveItemIn(IronSwordItemID, 5)`, THEN slot 0 holds one Iron Sword at `EnhancementLevel = 5` and the `InventoryChangedEvent` entry is `{ slotIndex: 0, itemId: IronSwordItemID, quantity: 1, enhancementLevel: 5 }`.

**AC-INV-19** [BLOCKING]
GIVEN slot A holds one Iron Sword at `EnhancementLevel = 5` and slot B holds one Iron Sword at `EnhancementLevel = 0` (same `ItemID`, both unlocked), WHEN the player moves slot A onto slot B (`MoveRequest(A, B)`), THEN slot A holds the level-0 sword and slot B holds the level-5 sword, and a single `InventoryChangedEvent` carries both entries with their enhancement levels. The two items are swapped, not merged. GIVEN instead that both swords are at `EnhancementLevel = 0` (identical items), WHEN the player moves slot A onto slot B, THEN the move succeeds, neither slot changes, and no `InventoryChangedEvent` fires.

**AC-INV-20** [BLOCKING]
GIVEN slot 7 holds one Iron Sword at `EnhancementLevel = 5`, WHEN the character logs out and logs back in, THEN slot 7 holds one Iron Sword at `EnhancementLevel = 5`. GIVEN a snapshot entry `{ SlotIndex: 2, ItemId: IronSwordItemID, Quantity: 1, EnhancementLevel: 200 }`, WHEN it is loaded, THEN slot 2 holds the sword at `EnhancementLevel = MAX_ENHANCEMENT_LEVEL` and a server warning is logged. GIVEN a snapshot entry `{ SlotIndex: 4, ItemId: HPPotionItemID, Quantity: 42, EnhancementLevel: 3 }`, WHEN it is loaded, THEN slot 4 holds 42 HP Potions at `EnhancementLevel = 0` and a server warning is logged.

**AC-INV-21** [BLOCKING]
GIVEN slot 3 holds one Iron Sword at `EnhancementLevel = 5` with no lock applied, WHEN the player discards it (`DiscardRequest(3, quantity=1)`), THEN slot 3 becomes `ItemID.Invalid, Quantity = 0, EnhancementLevel = 0` and the `InventoryChangedEvent` entry is `{ slotIndex: 3, itemId: 0, quantity: 0, enhancementLevel: 0 }`. WHEN an Iron Sword is then picked up into slot 3 (the lowest-index empty slot), THEN slot 3 holds one Iron Sword at `EnhancementLevel = 0` — a slot never retains a previous item's level, and every picked-up item starts at 0.

**AC-INV-22** [BLOCKING]
GIVEN slot 5 holds exactly one HP Potion (`StackLimit = 99`) and is locked, WHEN `SetEnhancementLevel(5, 1)` is called, THEN it returns `false`, the slot stays at `EnhancementLevel = 0`, no `InventoryChangedEvent` fires, and a server error is logged — a stackable item can never hold a level, even at quantity 1. GIVEN slot 3 holds one Iron Sword, locked, WHEN `SetEnhancementLevel(3, MAX_ENHANCEMENT_LEVEL + 1)` is called, THEN it returns `false` with no change. GIVEN at least one empty slot, WHEN the Equipment System calls `MoveItemIn(IronSwordItemID, MAX_ENHANCEMENT_LEVEL + 1)`, THEN the call fails, no slot is mutated, no `InventoryChangedEvent` fires, and a server error is logged. GIVEN a snapshot entry `{ SlotIndex: 6, ItemId: HPPotionItemID, Quantity: 1, EnhancementLevel: 3 }`, WHEN it is loaded, THEN slot 6 holds one HP Potion at `EnhancementLevel = 0` and a server warning is logged.

**BLOCKING: 24 | Total: 24**

## Open Questions

**OQ-INV-1 — Discard hold duration (`DISCARD_HOLD_DURATION`)**
Exact duration not yet set. Starting recommendation: 0.8s. Too short risks accidental discards on mobile touch; too long makes discard feel punishing.
*Owner*: UX Designer. *Target*: Before first playtest session.

**OQ-INV-2 — Consumable hotbar reference model**
If a player assigns a consumable stack to the hotbar, does the hotbar reference the slot by index, or by ItemID? If the player later moves the stack to a different slot, does the hotbar reference break? The Consumable Use System GDD must define this model — `ConsumeItem(ItemID, quantity)` implies ItemID-based lookup, but slot stability may be needed for the hotbar UI.
*Owner*: Game Designer / Consumable Use System GDD. *Target*: Consumable Use System GDD.

**OQ-INV-3 — Inventory sort order**
Default sort behavior (by ItemCategory, by ItemID, by acquisition order, or user-defined) has no mechanical impact on Inventory System rules but affects the UX spec and must be decided before `/ux-design inventory-screen` is authored.
*Owner*: UX Designer. *Target*: `/ux-design inventory-screen`.

**OQ-INV-4 — Enhancement System lock timeout (in-session)**
This GDD states that locks do not survive a session boundary. But an in-session lock without resolution is possible if the Enhancement System crashes mid-attempt. Should the server auto-unlock slots after a TTL (e.g., 60 seconds) even without an explicit `UnlockSlot` call? This GDD does not define a lock TTL — the Enhancement System GDD must specify the in-session lock lifetime and the server-side auto-cleanup mechanism.
*Owner*: Enhancement System GDD (design order #15). *Target*: Enhancement System GDD.

**~~OQ-INV-5 — Drop fate on full inventory~~** — RESOLVED (2026-05-17)
~~When the Inventory System returns `PickupResult(fail)` due to a full bag, this question was blocking for Loot Table System GDD authoring.~~
*Resolution*: Loot Table System GDD CR-LT-13.1–13.3 defines bag-full fate: TTL pause while the app is backgrounded, explicit discard modal on first pickup attempt, and proactive expiry warning, all via the ground pool mechanism. The "item not created on server" language in AC-INV-1 and AC-INV-15 remains accurate — the item is held as a ground entity by the Loot Table System, not created in the character's inventory.

**~~OQ-INV-6 — Wire schemas for inventory client messages~~** — **RESOLVED (2026-05-17)**
`DiscardRequest` (5-byte body, C→S), `DiscardResult` (3-byte body, S→C), `MoveRequest` (2-byte body, C→S), `MoveResult` (22-byte body since 2026-10-01 — per-slot enhancement-level bytes added; originally 20, S→C), and `InventoryFullNotification` (0-byte body, S→C) added to `networking-wire-protocol.md` Inventory System Messages section. `DiscardFailReason` and `MoveFailReason` enums added.
