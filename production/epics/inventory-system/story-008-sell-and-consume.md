# Story 008: NPC Shop Sell & Consumable Use Interfaces

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Interactions table (NPC Shop and Consumable Use System rows), Rule 5.12 (locks), Cross-System Interface Edge Cases, AC-INV-16
**Requirement**: `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: NPC Shop and Consumable Use System call the injected `IInventoryService` directly (Tier 1) and act on the return value; slot changes are broadcast via `OnInventoryChanged`. Gold is never touched here — NPC Shop calls the Currency System itself.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args; interface dependency — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story. Signatures: `SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity)` and `ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity)`.*

**SellItem**

- [x] **AC-INV-16 (full stack)**: Slot 4 holds 5 HP Potions, unlocked. `SellItem(charId, 4, HPPotion, 5)` returns `Success = true, Reason = None, QuantitySold = 5`; slot 4 becomes `ItemID.Invalid, Quantity = 0`; one `OnInventoryChanged` with one entry `{ slotIndex: 4, itemId: 0, quantity: 0 }`.
- [x] **AC-INV-16 (partial stack)**: Same starting state. `SellItem(charId, 4, HPPotion, 2)` returns success with `QuantitySold = 2`; slot 4 holds HP Potion ×3; one `OnInventoryChanged` with one entry `{ slotIndex: 4, itemId: HPPotion, quantity: 3 }`.
- [x] **Sell — ItemID mismatch**: the slot's current ItemID differs from `itemId` (including an empty slot, and `itemId == ItemID.Invalid`) → `Success = false, Reason = ItemMismatch, QuantitySold = 0`; no mutation, no event, no log.
- [x] **Sell — locked slot**: a locked slot → `Reason = SlotLocked`, even for a matching ItemID and valid quantity (Rule 5.12); no mutation, no event, no log.
- [x] **Sell — invalid quantity**: `quantity ≤ 0` or `quantity >` the slot's current `Quantity` → `Reason = InvalidQuantity`; no mutation, no event, no log.
- [x] **Sell — invalid slot**: out-of-range `slotIndex` (< 0 or ≥ 20) → `Reason = InvalidSlot` + server error; unregistered `charId` → `Reason = InvalidSlot` + server error; neither throws, mutates state, or fires an event.
- [x] **Sell — equipment**: selling an equipment item (qty 1, `quantity = 1`) empties the slot — `SellItem` is category-agnostic.

**ConsumeItem**

- [x] **Consume — lowest index first**: decrements from the lowest-index unlocked slot holding `itemId` (0→19); when `quantity` exceeds that stack it continues into the next lowest-index unlocked stack. A stack reaching 0 becomes `ItemID.Invalid, Quantity = 0`. Returns `Success = true, Reason = None`; exactly one `OnInventoryChanged` per call, listing every changed slot in ascending slot order.
- [x] **Consume — insufficient or missing**: the total held in unlocked slots is less than `quantity` (including an ItemID no longer in the bag, and `itemId == ItemID.Invalid`) → `Success = false, Reason = InsufficientQuantity`; no slot mutated, no event, no log.
- [x] **Consume — locked slots skipped**: a locked stack of `itemId` is neither decremented nor counted toward the available total (decided 2026-10-01; GDD Rule 5.12). With stacks in a locked slot and an unlocked slot, consumption takes only from the unlocked one; with only a locked stack, the call fails `InsufficientQuantity` even though `HasItem` is true.
- [x] **Consume — invalid quantity / unregistered**: `quantity ≤ 0` → `Reason = InvalidQuantity`, no log; unregistered `charId` → `Reason = CharacterNotRegistered` + server error; neither mutates state or fires an event.

**Both**

- [x] **Mutation-seam re-entrancy**: `SellItem` or `ConsumeItem` called synchronously from an `OnInventoryChanged` subscriber throws `InvalidOperationException` and does not mutate any slot.
- [x] **Result contract**: every success returns `Reason = None`; every failure returns a non-`None` reason, and a failed `SellItem` returns `QuantitySold = 0`.
- [x] **No bag-full side effects**: neither method fires `OnInventoryFull` nor resets the bag-full dedup window (GDD Rule 4.10, literal — only a successful `Pickup` resets it).

---

## Implementation Notes

- **Signatures** on `IInventoryService` (with XML docs), matching every other mutation's `charId`-first shape:
  - `SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity)`
  - `ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity)`
- **`quantity` on SellItem (decided 2026-10-01)**: partial-stack sells are supported. The Approved NPC Shop GDD (CR-SHOP-7 step 16, CR-SHOP-8) and the wire `SellRequest.quantity` field both require it; the Inventory GDD's "entire stack only" text was the stale side and has been updated (Interactions table, Dependencies table, edge cases, AC-INV-16). "Sell all" is the caller passing the full stack count.
- **Result types** in `IronGrind.InventorySystem`, mirroring `DiscardResult` / `PickupResult`:
  - `public readonly struct SellItemResult { readonly bool Success; readonly SellItemFailReason Reason; readonly int QuantitySold; }` — `static SellItemResult Succeeded(int quantitySold)`, `static SellItemResult Fail(SellItemFailReason reason)` with `UnityEngine.Debug.Assert(reason != SellItemFailReason.None)`.
  - `public enum SellItemFailReason : byte { None = 0, InvalidSlot = 1, ItemMismatch = 2, SlotLocked = 3, InvalidQuantity = 4 }` — one member per NPC Shop CR-SHOP-7 step 15 inventory-side rejection (`RejectedInvalidSlot`, `RejectedItemMismatch`, `RejectedSlotLocked`, `RejectedInvalidQuantity`).
  - `public readonly struct ConsumeItemResult { readonly bool Success; readonly ConsumeItemFailReason Reason; }` — `static readonly ConsumeItemResult Succeeded`, `static ConsumeItemResult Fail(ConsumeItemFailReason reason)` with the same assert.
  - `public enum ConsumeItemFailReason : byte { None = 0, InvalidQuantity = 1, CharacterNotRegistered = 2, InsufficientQuantity = 3 }`.
- **Naming note**: the wire message is already named `SellResult` (`networking-wire-protocol.md`); the in-process type is `SellItemResult`, so there is no collision.
- **SellItem validation order** (matches CR-SHOP-7 step 15 b → c → d → f): range → registered → item match → lock → quantity bounds (`1 ≤ quantity ≤ current Quantity`). An empty slot holds `ItemID.Invalid`; reject `itemId == ItemID.Invalid` explicitly as `ItemMismatch` so an empty slot can never "match".
- **SellItem does not check `SellPriceGold`** — whether an item is sellable is NPC Shop's rule (CR-SHOP-7 step 15e). No Item Database lookup is needed in either method.
- **ConsumeItem guard order** (mirrors `Pickup`): `quantity ≤ 0` → `InvalidQuantity`; unregistered → `CharacterNotRegistered`; then plan-then-commit — scan slots 0→19, skipping locked slots, summing `Quantity` of slots holding `itemId`; if the sum is less than `quantity`, fail `InsufficientQuantity` with nothing written; otherwise decrement in ascending order. MVP callers pass `quantity = 1`.
- **No category check on ConsumeItem** — it decrements whatever `itemId` it is given, consistent with the Story 007 decision for `MoveItemIn` / `ForceInsert`. This also makes it usable for the proposed TD-043 fix (Enhancement consuming one scroll from a stack).
- **`HasItem` is unchanged** — it still reports true for a locked stack (Story 001 contract). A caller that gets true from `HasItem` must still handle a failed `ConsumeItem`.
- **Mutation-seam contract** (Story 001, class remarks): `ThrowIfDispatching()` first → all validation → `RecordSlotChange(...)` per changed slot → write slots → one `EmitInventoryChanged(charId)`. No `RecordSlotChange` before validation completes, so failure paths never need `DiscardPendingChanges()`.
- **Logging**: `ItemMismatch`, `SlotLocked`, `InvalidQuantity` and `InsufficientQuantity` log nothing — they are legitimate race outcomes the caller handles. Out-of-range `slotIndex` and unregistered `charId` → server error: both are Tier 1 caller bugs (NPC Shop range-validates before calling), mirroring `RemoveItem` / `MoveItemOut`.
- **Performance**: `SellItem` is O(1) — one slot read, one slot write. `ConsumeItem` is one scan of 20 slots plus at most 20 writes. No allocation beyond the existing pending-change buffer. Neither is on a per-frame path.

---

## Out of Scope

- Gold payout on sell and the `SellPriceGold > 0` check (NPC Shop → Currency System / Item Database)
- `SellRequest` / `SellResult` / `UseItemRequest` wire codecs and `NPCInteractionActive` validation (Networking, NPC Shop)
- Consumable effects, cooldowns, hotbar model (Consumable Use System; OQ-INV-2)
- Item detail view Use / Assign to Hotbar UI (AC-INV-11 — deferred to Inventory UI epic)
- Switching Enhancement scroll consumption from `RemoveItem` to `ConsumeItem` (TD-043 — Enhancement GDD)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs`

*(All calls below take `charId` first; omitted for brevity.)*

- **AC-INV-16 full** — Given HP Potion ×5 in slot 4; When `SellItem(4, HPPotion, 5)`; Then success, `QuantitySold == 5`, slot 4 Invalid/0, one event with one entry `{4, 0, 0}`. Edge: subsequent `HasItem(HPPotion)` false.
- **AC-INV-16 partial** — Given ×5 in slot 4; When `SellItem(4, HPPotion, 2)`; Then success, `QuantitySold == 2`, slot 4 ×3, one event `{4, HPPotion, 3}`.
- **Mismatch** — slot 4 HP Potion; `SellItem(4, BronzeSword, 1)` → `ItemMismatch`, `QuantitySold == 0`, slot unchanged, 0 events. Edge: empty slot 10, `SellItem(10, HPPotion, 1)` → `ItemMismatch`; `SellItem(10, ItemID.Invalid, 1)` → `ItemMismatch`.
- **Locked** — `LockSlot(4)` then `SellItem(4, HPPotion, 5)` → `SlotLocked`, slot still ×5, 0 events; after `UnlockSlot(4)` the same call succeeds.
- **Invalid quantity** — ×5 in slot 4; `SellItem(4, HPPotion, 6)`, `(…, 0)`, `(…, -1)` → `InvalidQuantity`, slot still ×5, 0 events.
- **Invalid slot / unregistered** — `SellItem(20, HPPotion, 1)`, `SellItem(-1, HPPotion, 1)` → `InvalidSlot`, no exception, error logged (`LogAssert.Expect`), all 20 slots unchanged; unregistered `charId` → `InvalidSlot`, error logged, no event.
- **Validation order** — locked slot holding HP Potion: `SellItem(4, BronzeSword, 1)` → `ItemMismatch` (match before lock); `SellItem(4, HPPotion, 99)` → `SlotLocked` (lock before quantity).
- **Equipment** — Bronze Sword in slot 0; `SellItem(0, BronzeSword, 1)` → success, `QuantitySold == 1`, slot empty, event `{0, 0, 0}`.
- **Consume lowest index** — HP Potion ×3 in slot 2 and ×10 in slot 8; `ConsumeItem(HPPotion, 1)` → success, slot 2 ×2, slot 8 ×10, one event `{2, HPPotion, 2}`.
- **Consume empties a stack** — slot 2 ×1, slot 8 ×10; `ConsumeItem(HPPotion, 1)` → slot 2 Invalid/0, event `{2, 0, 0}`; the next `ConsumeItem(HPPotion, 1)` takes from slot 8 (×9).
- **Consume across stacks** — slot 2 ×3, slot 8 ×10; `ConsumeItem(HPPotion, 5)` → slot 2 empty, slot 8 ×8, one event with two entries in order `{2, 0, 0}`, `{8, HPPotion, 8}`.
- **Insufficient total** — slot 2 ×3 only; `ConsumeItem(HPPotion, 5)` → `InsufficientQuantity`, slot 2 still ×3, 0 events.
- **Missing item** — no HP Potion in bag → `ConsumeItem(HPPotion, 1)` → `InsufficientQuantity`, 0 events; `HasItem` false. Edge: `ConsumeItem(ItemID.Invalid, 1)` → `InsufficientQuantity`.
- **Locked skipped** — slot 2 ×3 locked, slot 8 ×10; `ConsumeItem(HPPotion, 1)` → slot 2 still ×3, slot 8 ×9. Only slot 2 ×3 locked → `ConsumeItem(HPPotion, 1)` → `InsufficientQuantity` while `HasItem` is true. Slot 2 ×3 locked + slot 8 ×2; `ConsumeItem(HPPotion, 3)` → `InsufficientQuantity`, both unchanged.
- **Other items untouched** — MP Potion in slot 1 (lower index than the HP Potion stack); `ConsumeItem(HPPotion, 1)` leaves slot 1 unchanged and absent from the event.
- **Invalid quantity / unregistered** — `ConsumeItem(HPPotion, 0)` and `(…, -1)` → `InvalidQuantity`, no log, 0 events; unregistered `charId` → `CharacterNotRegistered`, error logged.
- **Two characters** — both hold HP Potions; `SellItem` / `ConsumeItem` on character A leaves character B's slots unchanged and the event carries A's `CharacterID`.
- **Re-entrancy** — subscriber on `OnInventoryChanged` calls `SellItem`, and separately `ConsumeItem` → `InvalidOperationException`; target slots unchanged.
- **Result contract / no bag-full** — every success asserts `Reason == None`; every failure asserts the specific reason; `OnInventoryFull` never fires in any case in this file.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs` — must exist and pass

**Status**: [x] Created — 41 tests, all passing in live Test Runner (2026-10-01)

---

## Dependencies

- Depends on: Story 001; Story 004 (lock state for the locked-sell and locked-consume cases)
- Unlocks: NPC Shop and Consumable Use System epics (not yet created)

---

## Completion Notes
**Completed**: 2026-10-01
**Criteria**: 14/14 passing (none deferred)
**Deviations**: None. TR registry has no `TR-inv-*` entries yet (pre-existing, noted in Context). GDD updated at readiness (2026-10-01): `SellItem` takes `quantity` (partial-stack sells), `ConsumeItem` skips locked slots.
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs` (41 tests, passing in live Test Runner)
**Code Review**: Complete — /code-review CHANGES REQUIRED (test-only: dedup-window clause unverified); all 10 items applied (+9 tests, re-entrancy message asserts, 2 doc edits). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
