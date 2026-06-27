# NPC Shop

> **Status**: Approved (2026-06-07 — 2 pre-implementation gates open before sprint: OQ-NS-4/6)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-07
> **Implements Pillar**: Earned Power (primary), Legendary Gear (secondary)

## Overview

The NPC Shop is the town hub's exchange point where accumulated gold converts into enhancement resources and combat sustain items. It maintains a permanent, infinite-stock catalog of two item categories: tier-matched Enhancement Scrolls (one per gear tier) and consumable potions (HP Potion and MP Potion in Small, Medium, and Large sizes). The shop also provides the sell-back interface where players convert unwanted loot into gold at fixed `SellPriceGold` rates defined by the Item Database.

All transactions are server-authoritative: purchases debit gold via the Currency System and inject items via the Inventory System; sell-backs remove items via the Inventory System and credit gold via the Currency System. The NPC Shop has no price volatility, no restocking cooldown, and no per-session purchase limits — its catalog is static and its stock is infinite. Every buy price is a fixed authored value that must satisfy the anti-arbitrage constraint: `NPC buy price ≥ SellPriceGold × 1.5`.

The shop window is the brief, weighted pause at the end of a farming session — the ritual stop between grinding and gambling. Deciding which tier of scroll to buy (and how many) before walking to the Enhancement NPC is a small commitment act: it turns raw gold into a specific bet. Spending here is always a choice to convert saved time into risk.

*Pillar alignment: Earned Power — the shop is the mechanism by which grind translates into enhancement attempts. Nothing is sold that shortcuts the grind; only resources that enable the next step.*

## Player Fantasy

The shop visit has two beats, and neither of them is exciting — which is exactly the point.

First: the sell-off. You open the bag, tap through the vendor trash and the off-tier drops that aren't going anywhere useful. Each sale lands a small number in the gold counter. The bag gets lighter. The total gets larger. You can see where you are now relative to where you need to be. This beat is not a decision — it's an accounting. It closes the session just completed.

Second: the buy. Now you know your balance. You open the scroll catalog and run the math. Three Dark Steel scrolls is aggressive. Two is conservative. One is optimistic. You know the probability table from memory. The button tap is quiet. No animation celebrates it. The scrolls appear in your bag. This is not the last moment of control — that comes at the Enhancement NPC, where you will see the probability display and still have a cancel path. What this moment is is the commitment: accumulated time converted into a specific tier of risk. Once the gold leaves, the tier is decided. The Enhancement NPC only decides whether the tier pays off.

The shop earns trust by refusing to manipulate. No limited-time stock. No rotating sale banner. No subtle upsell toward a tier you can't afford yet. The prices are the prices. The stock is always there. Veterans of games that used shops as traps read this immediately, and they relax. The calm is real — and that's what makes the Enhancement NPC three doors down feel dangerous by contrast.

## Detailed Design

### Core Rules

**CR-SHOP-1 — NPC and Location**
The NPC Shop is a single NPC in the town hub. There are no zone-specific shops or vendors outside the town hub at MVP.

**CR-SHOP-2 — Catalog**
The shop maintains a permanent, infinite-stock catalog. No item can be depleted; no restocking cooldown exists; no time-limited items appear. The full MVP catalog:

| Item | Buy Price | Sell Back? | Transaction Reason |
|------|-----------|------------|--------------------|
| Bronze Enhancement Scroll | 90g | No (`SellPriceGold=0`) | `ScrollPurchase=1` |
| Iron Enhancement Scroll | 150g | No | `ScrollPurchase=1` |
| Steel Enhancement Scroll | 230g | No | `ScrollPurchase=1` |
| Dark Steel Enhancement Scroll | 350g | No | `ScrollPurchase=1` |
| HP Potion (Small) | 3g | Yes (sell 2g) | `ItemPurchase=6` |
| HP Potion (Medium) | 10g | Yes (sell 6g) | `ItemPurchase=6` |
| HP Potion (Large) | 30g | Yes (sell 18g) | `ItemPurchase=6` |
| MP Potion (Small) | 3g | Yes (sell 2g) | `ItemPurchase=6` |
| MP Potion (Medium) | 10g | Yes (sell 6g) | `ItemPurchase=6` |
| MP Potion (Large) | 30g | Yes (sell 18g) | `ItemPurchase=6` |

The shop does not sell equipment gear. Gear is earned from monster drops only — selling gear to NPCs is supported (sell-back), but buying gear from an NPC is not.

**CR-SHOP-3 — Opening a Shop Session**

1. Player taps the Shop NPC. Client sends `OpenNPCInteraction(shopNpcId)`.
2. Server validates: character is in the town hub zone. If not → `RejectedNotInTownHub`; no state change.
3. If `NPCInteractionActive = true` already exists for this player (from any NPC), the server clears it silently before continuing. No notification is sent to the client for the cleared session. ⚠️ **OQ-NS-6**: If an Enhancement session is preempted, the Enhancement System must be notified before the flag is cleared so in-flight `ConfirmEnhancement` operations can resolve safely — an enhancement completing after the session flag is cleared must not silently destroy the item with no client notification.
4. Server sets `NPCInteractionActive = true` and returns `NPCInteractionOpened`. Client opens the shop window to the Sell tab by default. (Dominant player flow for this genre is sell-accumulated-loot → buy-scrolls; sell-first reduces tap count for the majority case. Final tab order to be validated via `/ux-design npc-shop`.)

**CR-SHOP-4 — Shop Session Lifetime**

5. The session has a hard wall-clock lifetime of `SESSION_TTL_SECONDS` (300s) measured from the moment `OpenNPCInteraction` succeeds, regardless of client activity. There is no inactivity timer — sending messages does NOT extend the timer. Mobile players who switch apps are protected by this wall-clock cap, not penalized for sending messages.
6. Close triggers (any of these clears `NPCInteractionActive = false`): player sends `CloseNPCInteraction()`; player's zone changes; the 300s wall-clock TTL from session open elapses; another `OpenNPCInteraction` arrives for any NPC.
7. The town hub is a safe zone — player death cannot occur there. `NPCInteractionActive` is never cleared by a death event. If future design introduces an unsafe town hub, this rule must be revisited.

**CR-SHOP-5 — Buy Transaction Sequence**

8. Player selects an item from the Buy tab, adjusts quantity with the selector, taps Confirm Buy. Client sends `BuyRequest(itemId: ItemID, quantity: int)`.
9. Server validates in order — aborts on first failure:
   - a. `NPCInteractionActive = true` → else `RejectedNoNPCSession`
   - b. `quantity ≥ 1` → else `RejectedInvalidQuantity`
   - c. `itemId` is in the catalog → else `RejectedItemNotInCatalog`
   - d. `TrySpendGold(charId, shopPrice × quantity, reason)` → if `InsufficientFunds`, `RejectedInsufficientFunds`; no gold debited
   - e. `PickupRequest(charId, itemId, quantity)` → if fail, call compensating `AddGold(charId, totalCost, CompensatingRefund)` immediately; return `RejectedInventoryFull`. Currency System event delivery is architecture-defined — do not specify wire batching behavior here (see OQ-NS-5).
10. On success: send `BuyResult(success=true, itemId, quantity, newGoldBalance)`.

> ✅ **ADR-001 Accepted (OQ-NS-5 resolved 2026-06-07)**: The Purchase Transaction Integrity ADR is accepted. Implementing CR-SHOP-5 requires: `requestId: uint` on `BuyRequest`, `PendingPurchase` durable record created before `TrySpendGold`, and reconnect reconciliation step in `SessionHandshake`. See `docs/architecture/ADR-001-purchase-transaction-integrity.md`.

**CR-SHOP-6 — Buy Quantity Selector**

11. Minimum: 1. UI maximum: `floor(player.GoldBalance / shopPrice)`, capped at 99. The selector does not pre-validate inventory capacity — inventory failures resolve at Confirm Buy via the refund path (CR-SHOP-5 step 9e).
12. **Touch model**: +/- stepper buttons flanking a numeric display. Minimum touch target: 44dp × 44dp per button. Single tap adjusts by 1. Holding a button for 500ms activates repeat at 3 increments/sec; holding for 1,500ms accelerates to 10 increments/sec. No keyboard/text-field input — touch-only.
13. **Post-rejection recovery**: After `RejectedInsufficientFunds`, the selector auto-corrects to `selectorMax` recalculated from the updated balance in the accompanying `GoldSyncEvent`. The player sees the highest affordable quantity without manual adjustment.

**CR-SHOP-7 — Sell-Back Transaction Sequence**

14. The Sell tab displays all non-empty inventory slots where `SellPriceGold > 0`. Locked slots are shown as greyed-out with a lock icon (non-actionable but visible). Enhancement Scrolls (`SellPriceGold = 0`) never appear. Player selects a slot, optionally adjusts the quantity selector (CR-SHOP-8), and taps Confirm Sell. Client sends `SellRequest(slotIndex: byte, itemId: ItemID, quantity: int)`.
15. Server validates in order:
    - a. `NPCInteractionActive = true` → else `RejectedNoNPCSession`
    - b. `slotIndex ∈ [0, 19]` → else `RejectedInvalidSlot`
    - c. Slot holds `itemId` → else `RejectedItemMismatch` (stale render guard)
    - d. `IsSlotLocked(slotIndex) = false` → else `RejectedSlotLocked`
    - e. `GetItem(itemId).SellPriceGold > 0` → else `RejectedUnsellable` (server-side guard even if UI filtered correctly)
    - f. `quantity ≥ 1` AND `quantity ≤ slotStackCount` → else `RejectedInvalidQuantity`
16. Validation passed: `SellItem(slotIndex, itemId, quantity)` removes `quantity` units from the slot, returns `quantitySold`. Then `AddGold(charId, SellPriceGold × quantitySold, ItemSell)`.
17. Send `SellResult(success=true, itemId, quantitySold, goldEarned, newGoldBalance)`. `goldEarned` is the actual amount credited by `AddGold` — capped by remaining space to GOLD_CAP, not the raw F-NS-2 product.

**CR-SHOP-8 — Sell Quantity Selector**

18. The Sell tab includes a quantity selector for each selected slot. Default: the full stack count ("sell all" is the default; partial sell is opt-in). Minimum: 1. Maximum: the full stack count in the selected slot. Touch model: identical to the Buy tab quantity selector (CR-SHOP-6 rules 12–13).
19. ⚠️ **Inventory System interface dependency**: Partial-stack sell requires `SellItem(slotIndex, itemId, quantity) → quantitySold` that removes only `quantity` units while leaving the remainder. The current Inventory System GDD defines `SellItem(slotIndex, itemId)` which removes the entire stack. A `quantity` parameter must be confirmed with the Inventory System GDD author before sprint commitment.

**CR-SHOP-9 — Equipment Sell-Back**

20. Both equipment and consumables with `SellPriceGold > 0` are eligible for sell-back. The shop is the intended loot drain for off-slot, duplicate-tier, and inferior-tier gear.
21. Enhanced equipment sells at its base `SellPriceGold` only — enhancement level does not increase sell-back value. A +7 Iron Sword sells for 30g (same as +0). Enhancement investment is a sunk cost that makes destruction meaningful; a sell premium would turn enhanced gear into a liquid hedge against destruction, undermining the Legendary Gear pillar.

**CR-SHOP-10 — Anti-Arbitrage Invariant**

22. For all items in the catalog: `BuyPrice ≥ SellPriceGold × 1.5`. The server validates this at startup from authored data. A buy-then-sell loop always loses ≥ 33% of the purchase price.
23. Enhancement Scrolls are exempt (`SellPriceGold = 0`). The invariant applies only to items appearing in both the Buy tab and the Sell tab.

**CR-SHOP-11 — Non-Refundable Purchase Confirmation**

24. Items with `SellPriceGold = 0` (currently: all Enhancement Scrolls) require a one-tap confirmation overlay before `BuyRequest` is sent. When the player taps Confirm Buy for a non-refundable item, a modal overlay appears before any server request:
    - Header: "Confirm Purchase"
    - Body: "[ItemName] × [quantity] — [totalCost]g. **This purchase cannot be refunded.**"
    - Buttons: **Confirm** (sends `BuyRequest`) and **Cancel** (dismisses overlay, returns to Buy tab, quantity selector unchanged, no request sent).
25. Consumables with `SellPriceGold > 0` do not require confirmation — they can be resold at any time.
26. The confirmation overlay is client-side only. No server request is issued until the player taps Confirm.

---

### States and Transitions

| State | Condition | Entry | Exit |
|-------|-----------|-------|------|
| **CLOSED** | `NPCInteractionActive = false` (shop-relevant) | Initial state; all close triggers | — |
| **OPEN** | `NPCInteractionActive = true` | Valid `OpenNPCInteraction(shopNpcId)` | `CloseNPCInteraction()`, zone change, TTL expiry, or another `OpenNPCInteraction` |

No intermediate session states. Transactions require a durable in-flight record to handle crash recovery between gold debit and item grant — ADR-001 defines the `PendingPurchase` mechanism (OQ-NS-5 resolved 2026-06-07; see `docs/architecture/ADR-001-purchase-transaction-integrity.md`).

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Currency System** | ← calls | `TrySpendGold(charId, totalCost, ScrollPurchase or ItemPurchase) → GoldMutationResult` | Purchase (CR-SHOP-5 step 9d) |
| **Currency System** | ← calls | `AddGold(charId, totalCost, CompensatingRefund)` | Compensating refund on inventory fail (CR-SHOP-5 step 9e) |
| **Currency System** | ← calls | `AddGold(charId, goldEarned, ItemSell)` | Sell-back (CR-SHOP-7 step 14) |
| **Inventory System** | ← calls | `PickupRequest(charId, itemId, quantity) → PickupResult` | Purchase, after gold debit |
| **Inventory System** | ← calls | `SellItem(slotIndex, itemId, quantity) → quantitySold` — partial-stack sell (CR-SHOP-8). ⚠️ Interface change required: current Inventory System GDD defines `SellItem(slotIndex, itemId)` removing the entire stack; `quantity` parameter must be added. | Sell-back |
| **Inventory System** | ← reads | `IsSlotLocked(slotIndex)` | Sell tab validation (CR-SHOP-7 step 13d) |
| **Item Database** | ← reads | `GetItem(itemId).SellPriceGold`, `DisplayName` | Sell tab display, sell-back calculation, anti-arbitrage startup check |
| **Enhancement System** | Shared flag + event | `NPCInteractionActive` — same per-player flag; opening shop clears any active Enhancement NPC session. ⚠️ OQ-NS-6: Enhancement System must expose a callback/event (e.g., `OnNPCSessionPreempted(charId)`) invoked before the flag is cleared, so in-flight `ConfirmEnhancement` can complete safely. | On `OpenNPCInteraction` |
| **HUD** | Consumer | `GoldSyncEvent` (via Currency System) — gold display updates after each transaction | After each buy or sell |
| **Networking / Session Layer** | Wire | `OpenNPCInteraction`, `CloseNPCInteraction`, `BuyRequest`, `BuyResult`, `SellRequest`, `SellResult` | Per transaction |

*Cross-document correction (OQ-NS-3 resolved 2026-06-07 — Path A): Item Database F-2 contained an invariant conflicting with Large Potion sell = 18g. Resolved: F-2 invariant removed; concrete sell prices filled in (Small=2g, Medium=6g, Large=18g). Amendment applied to Item Database GDD on 2026-06-07.*

## Formulas

### F-NS-1: Total Purchase Cost

`totalCost = shopPrice × quantity`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Shop price | `shopPrice` | uint | 3–350 | Authored buy price per unit (from CR-SHOP-2 catalog) |
| Quantity | `quantity` | int | 1–99 | Bounded by F-NS-4 and the UI selector |
| Total cost | `totalCost` | uint | 3–34,650 | Gold debited via `TrySpendGold` |

**Output Range:** [3, 34,650] — well below GOLD_CAP (9,999,999). No overflow risk at current catalog prices. If catalog prices are extended, guard: `shopPrice × 99 ≤ uint.MaxValue`. Compute as `(uint)shopPrice * (uint)quantity`.

**Example:** 3 Dark Steel Enhancement Scrolls × 350g = **1,050g** debited.

---

### F-NS-2: Sell-Back Gold Credit

`goldEarned = SellPriceGold × quantitySold`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Sell price | `SellPriceGold` | uint | 0–270 | Per-unit sell price from `ItemDefinition.SellPriceGold`; 0 = ineligible for sell-back (scrolls are excluded by Sell tab filter and server guard before F-NS-2 is applied) |
| Quantity sold | `quantitySold` | int | 1–(full stack count) | Returned by `SellItem()` — 1 to full stack count per CR-SHOP-8 (player-selected quantity) |
| Gold earned | `goldEarned` | uint | 2–26,730 | F-NS-2 product before GOLD_CAP clamping. `SellResult.goldEarned` reports the actual credited amount (post-clamping) — see GOLD_CAP edge case. |

**Output Range:** [2, 26,730] for the F-NS-2 product. `AddGold` clamps to GOLD_CAP internally; `SellResult.goldEarned` reflects the actual amount credited, not this formula's output if clamping occurred. Equipment items are single-stack (StackLimit=1) so their maximum sell value is 270g (DarkSteel). Consumable stacks: maximum 18g × 99 = 1,782g.

**Example:** 10× HP Potion (Large) at 18g sell price = **180g** credited.

---

### F-NS-3: Anti-Arbitrage Floor Check

*Startup validation only — not a per-transaction runtime formula.*

Integer form: `(BuyPrice × 10) ≥ (SellPriceGold × 15)` — equivalent to `BuyPrice / SellPriceGold ≥ ANTI_ARBITRAGE_RATIO (1.5)`.

**Variables:**
| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Buy price | `BuyPrice` | uint | 1–9,999,999 | Authored catalog buy price for items with `SellPriceGold > 0` |
| Sell price | `SellPriceGold` | uint | 1–9,999,999 | Per-unit sell value; checked only when `SellPriceGold > 0` |
| Ratio constant | `ANTI_ARBITRAGE_RATIO` | float | 1.5 (fixed) | Minimum required BuyPrice/SellPriceGold ratio |
| Result | `isValid` | bool | {true, false} | false = data authoring error; server halts with invariant violation log at startup |

**Exempt items:** Enhancement Scrolls (`SellPriceGold = 0`) and equipment items (appear in Sell tab only — no BuyPrice in catalog) are both skipped.

**Integer form rationale:** Avoids float comparison. Safe for all catalog prices: `270 × 15 = 4,050` — well within uint range.

**Example (pass):** HP Potion Large — `30 × 10 = 300 ≥ 18 × 15 = 270` ✓ (ratio 1.67×).

**Example (authoring fail):** If SmallPotion BuyPrice = 2g, SellPriceGold = 2g: `2 × 10 = 20 < 2 × 15 = 30` → server halts.

---

### F-NS-4: Quantity Selector Maximum

*Client-side display only — not a server validation gate.*

`selectorMax = min(floor(playerGoldBalance / shopPrice), QUANTITY_SELECTOR_CAP)`

**Variables:**
| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Gold balance | `playerGoldBalance` | uint | 0–9,999,999 | Client's cached balance from last `GoldSyncEvent` — may be stale |
| Shop price | `shopPrice` | uint | 3–350 | Must be > 0 (server startup rejects any catalog entry with shopPrice = 0) |
| `QUANTITY_SELECTOR_CAP` | — | int | 99 (fixed) | Hard ceiling regardless of balance |
| Selector max | `selectorMax` | int | 0–99 | 0 = player cannot afford even 1 unit; UI disables Confirm Buy |

**Server authority:** `TrySpendGold` (CR-SHOP-5 step 9d) is the authoritative gate. F-NS-4 is a UI display helper only — the selector can be stale if gold was spent concurrently.

**Integer arithmetic:** `selectorMax = Math.Min((int)(playerGoldBalance / shopPrice), QUANTITY_SELECTOR_CAP)`. C# integer division truncates toward zero, which is the correct `floor()` behaviour for positive values.

**Example 1:** 1,000g ÷ 350g = floor(2.857) = 2; min(2, 99) = **2**.

**Example 2:** 50,000g ÷ 3g = 16,666; min(16,666, 99) = **99**.

**Example 3:** 2g ÷ 3g = floor(0.67) = 0; selectorMax = **0**; Confirm Buy disabled.

## Edge Cases

**Quantity boundaries**

- **If `quantity = 0` arrives in `BuyRequest`**: Reject `RejectedInvalidQuantity`. A zero-cost `TrySpendGold` call would return `GoldMutationError.InvalidAmount` — a different error path than InsufficientFunds. Server-side quantity guard prevents this ambiguous path.
- **If `quantity > 99` arrives in `BuyRequest`**: Reject `RejectedInvalidQuantity`. The server enforces the QUANTITY_SELECTOR_CAP ceiling independently of the client — do not trust client-bounded inputs.
- **If `quantity = 99` is sent but balance is insufficient for 99 units**: `TrySpendGold` returns `InsufficientFunds`; return `RejectedInsufficientFunds`. Stale selector is the normal cause — routine rejection, not an integrity error.

**Gold balance**

- **If balance is exactly `shopPrice × quantity − 1` (off-by-one stale selector)**: `TrySpendGold` returns `InsufficientFunds`; return `RejectedInsufficientFunds`. This is the canonical selector-race case — the client showed the purchase as affordable using a balance that was one gold stale.
- **If the compensating `AddGold` (refund on PickupRequest failure) would push balance to exactly `GOLD_CAP`**: Safe — `balance + totalCost ≤ previous_balance ≤ GOLD_CAP`. The refund always fits; no overflow check needed. This invariant holds because `balance` was just decremented by `totalCost` before the refund.
- **If player balance is at `GOLD_CAP` when a sell-back `AddGold` fires**: `AddGold` clamps to GOLD_CAP and discards excess gold (Currency System Rule 4). `SellResult.goldEarned` reports the actual amount credited (0 if already at cap; a partial amount if partially capped) — NOT the F-NS-2 computed product. The "prices are the prices" trust design requires honest feedback: reporting a computed sell value that was never credited would read as a bug. The HUD "Gold pouch is full" toast fires independently via `GoldSyncEvent`.

**Inventory full / PickupRequest failure**

- **If `PickupRequest` fails after `TrySpendGold` succeeds**: Call compensating `AddGold(charId, totalCost, CompensatingRefund)` immediately and return `RejectedInventoryFull`. The net gold effect is zero (debit then equal credit). Currency System event delivery behavior is implementation-defined — wire batching is not specified here (see OQ-NS-5). The net-zero balance assertion in AC-NS-15 is the observable correctness gate.
- **If `PickupRequest` fails for a partial quantity** (first N units absorbed by partial stacks, remaining units have no slot): Inventory System Rule 3 is atomic — either all units are placed or none. No partial grant occurs. Apply the same compensating refund path as a full failure.
- **If the compensating `AddGold` itself fails** (e.g., `ConcurrencyConflict` — two retries exhausted, or pathological `CharacterNotFound`): Log a critical server alert with `charId`, `totalCost`, and the `GoldMutationError` code. Flag for manual remediation. Return `RejectedInventoryFull` to the client (the item was not delivered). Do not silently discard the outstanding debit.

**Session flag / zone change during transaction**

- **If zone change fires between `TrySpendGold` succeeding and `PickupRequest` executing**: If the zone change clears `NPCInteractionActive` before CR-SHOP-5 step 9a runs, no debit has occurred — no refund needed. If the debit already occurred before the zone change is processed, execute the compensating `AddGold` regardless of current session state. The refund obligation survives session closure.
- **If `SESSION_TTL_SECONDS` (300s) expires between the debit and item grant**: Same rule as zone change — execute the compensating refund regardless of session state.

**Stale UI / race conditions**

- **If a concurrent operation drains balance after F-NS-4 renders the selector** (e.g., a party-share gold fee, or enhancement fee from a concurrent session — not an MVP concern but a structural race): `TrySpendGold` uses the authoritative server balance. Returns `RejectedInsufficientFunds` if the balance is now insufficient. A subsequent `GoldSyncEvent` refreshes the client display. Stale selectors showing unaffordable purchases as affordable is expected and handled by the server gate.
- **If a slot's contents change between Sell tab render and `SellRequest` server execution**: CR-SHOP-7 step 13c validates `slotIndex` holds `itemId` at execution time. Returns `RejectedItemMismatch` if the slot has changed. Both `slotIndex` and `itemId` must match — `slotIndex` alone is insufficient.
- **If the Enhancement System locks a slot between Sell tab render and `SellItem` execution**: CR-SHOP-7 step 13d checks `IsSlotLocked` at execution time. Returns `RejectedSlotLocked`. The lock check runs at execution, not at render — no additional guard needed.

**Sell-back edge cases**

- **If `SellRequest` targets a slot with `SellPriceGold = 0`** (e.g., an Enhancement Scroll that bypassed Sell tab filtering): Return `RejectedUnsellable` at step 13e. Server-side guard is independent of client-side tab filtering.
- **If `quantitySold` returned by `SellItem` is 0**: Log a server error; do not call `AddGold(0)` (Currency System returns `InvalidAmount` on zero-amount calls). Do not send `SellResult(success=true)`. This indicates slot-state corruption — the slot passed validation but was empty at `SellItem` execution time.
- **If `GetItem(itemId)` returns `null`** (deprecated item in sell tab — live-deprecation race): Reject `RejectedUnsellable`; log with `charId` and the unknown `ItemID`. Inventory System clears deprecated items on session load; this case is a live-deprecation race condition where an item is deprecated while the player's session is already active.

**Startup validation**

- **If F-NS-3 fails for any catalog item at startup**: Server halts; log the specific `ItemID`, its `BuyPrice`, and `SellPriceGold`. No partial startup with a degraded catalog is permitted — the invariant must hold for all items before any player session begins.
- **If any catalog item has `shopPrice = 0`**: Server halts at startup. F-NS-4 performs integer division by `shopPrice`; division by zero must be caught at startup validation before any player session begins.
- **If a sell-tab-only item** (e.g., equipment — no catalog `BuyPrice`): Startup validator skips F-NS-3 for items without a `BuyPrice` in the catalog. Items that appear only in the Sell tab are not subject to the anti-arbitrage floor.

## Dependencies

### Upstream Dependencies (systems this GDD depends on)

| System | GDD Status | Dependency Type | Interface | Hard/Soft |
|--------|-----------|----------------|-----------|-----------|
| **Currency System** | Approved | Calls | `TrySpendGold(CharacterID, uint cost, GoldTransactionReason)` for purchases; `AddGold(CharacterID, uint amount, GoldTransactionReason)` for compensating refunds and sell-back credits | **Hard** — all gold transactions require Currency System |
| **Inventory System** | Approved | Calls / reads | `PickupRequest(CharacterID, ItemID, quantity) → PickupResult` (purchase item delivery); `SellItem(slotIndex, itemId, quantity) → quantitySold` ⚠️ Interface change required — see Interactions table (sell-back removal); `IsSlotLocked(slotIndex): bool` (sell tab validation) | **Hard** — item delivery and sell-back require Inventory System |
| **Item Database** | Approved | Reads | `GetItem(ItemID)` → `SellPriceGold`, `DisplayName`, `ItemCategory`; used for sell tab display, sell-back value calculation, and startup anti-arbitrage validation | **Hard** — sell prices and display data are authoritative from Item Database |

### Downstream Dependents (systems that depend on this GDD)

| System | GDD Status | What they need | Bidirectionality required |
|--------|-----------|----------------|--------------------------|
| **Consumable Use System** | Not Started | Consumables purchased at NPC Shop land in inventory and are available for use; no direct API dependency on NPC Shop | Must list NPC Shop in its Dependencies when authored |
| **Enhancement System** | Approved | Shares `NPCInteractionActive` flag (opening shop clears any active Enhancement NPC session); expects Enhancement Scrolls sold exclusively here; no direct API dependency | Enhancement System GDD already documents the scroll source restriction (CR-ENH-3) and `NPCInteractionActive` shared flag (CR-ENH-17) |
| **HUD** | Not Started | Gold balance display updates via `GoldSyncEvent` emitted by Currency System after shop transactions; no direct API dependency on NPC Shop | HUD GDD must list Currency System; NPC Shop is indirect via Currency System events — no direct bidirectionality required |

**Bidirectionality flag:** When the Consumable Use System GDD is authored, it must include NPC Shop in its Dependencies section. Enhancement System already satisfies bidirectionality.

### Cross-Document Correction — Resolved

**Item Database GDD — F-2 invariant (OQ-NS-3 resolved 2026-06-07):** The F-2 constraint `SmallBasePrice × SizeMultiplier(Large) ≤ TierBasePrice(Bronze) = 10g` conflicted with the approved Large Potion sell price of 18g. Resolved as Path A: F-2 invariant removed; concrete sell prices filled in (Small=2g, Medium=6g, Large=18g). Amendment applied to Item Database GDD on 2026-06-07.

## Tuning Knobs

| Knob | Current Value | Safe Range | Gameplay Effect |
|------|--------------|-----------|----------------|
| Bronze Enhancement Scroll price | 90g | [75, 125] | Verified: satisfies the 36–60 min farming window at Bronze tier (~90–150g/hr estimated; lower bound = 36 min at 150g/hr, upper bound = 60 min at 90g/hr). Below 75g: scrolls feel trivially cheap; players stockpile without restraint. Above 125g: Bronze-tier players face a loss spiral at tier entry. |
| Iron Enhancement Scroll price | 150g | [125, 210] | ⚠️ Provisional — Iron-tier farming rates unanchored until Loot Table GDD is authored. At Bronze g/hr, Iron would take ~62 min (25% over the target window upper bound). Validate against actual Iron-tier kill-rate × gold-per-kill before locking this price. |
| Steel Enhancement Scroll price | 230g | [190, 320] | ⚠️ Provisional — Steel-tier farming rates unanchored. At Bronze g/hr, Steel would take ~77 min (54% over target). Validate against Loot Table GDD data before locking. |
| Dark Steel Enhancement Scroll price | 350g | [270, 450] | ⚠️ Provisional — DarkSteel-tier farming rates unanchored. At Bronze g/hr, DarkSteel would take ~87 min (75% over target). Floor = DarkSteel gear sell price (270g); below this, gear liquidation is neutral vs. gold farming. Validate against Loot Table GDD data before locking. ⚠️ DarkSteel sell-to-scroll ratio (350/270 = 1.3) must be modeled against gear drop rates in the Loot Table spec — if drop rate exceeds ~39 min/item, gear liquidation beats gold farming as a scroll acquisition path (Earned Power pillar risk). |
| HP/MP Potion (Small) buy price | 3g | [3, 6] | Floor is anti-arbitrage constraint: 2g sell × 1.5 = 3g minimum. Above 6g: Small potions become disproportionately expensive vs. Medium per-use value. |
| HP/MP Potion (Medium) buy price | 10g | [9, 20] | Floor: 6g × 1.5 = 9g. Above 20g: Medium potions are no longer a meaningful upgrade over stacking Smalls. |
| HP/MP Potion (Large) buy price | 30g | [27, 60] | Floor: 18g × 1.5 = 27g. Above 60g: Large potions exceed one Bronze scroll's cost — they stop being casual mid-session restocks and become significant purchases. |
| `QUANTITY_SELECTOR_CAP` | 99 | [10, 99] | Hard ceiling on the buy quantity selector. Reducing below 50 inconveniences players stocking for long sessions. Raising above 99 is a UX choice, not a technical limit (F-NS-1 overflow: `350 × 99 = 34,650` — far within uint range). |
| `ANTI_ARBITRAGE_RATIO` | 1.5 | [1.2, 2.5] | Minimum BuyPrice/SellPriceGold ratio. Below 1.2: buy-then-sell loops approach breakeven (8–17% loss) and resemble a gold sink exploit. Above 2.5: consumables feel punishingly overpriced relative to their sell value. Changing this ratio requires re-validating all catalog items against the new floor via F-NS-3. |

**Cross-system tuning invariant:** Scroll prices must track the `LevelTierMultiplier` in the Loot Table System. If gold/hour per zone tier changes during a balance pass, scroll prices must be adjusted proportionally to preserve the 36–60 min farming window (Bronze tier verified at current prices; Iron/Steel/DarkSteel provisional pending Loot Table GDD kill-rate anchoring). This is a joint tuning concern between Loot Table System GDD (yield) and NPC Shop GDD (prices).

## Visual/Audio Requirements

The NPC Shop has no VFX or animation requirements. Audio is the primary sensory layer. The shop's tone should communicate the same thing as its design intent: calm restraint, honest prices, no manipulation.

| Event | Audio | Visual |
|-------|-------|--------|
| Successful purchase | Brief positive transaction chime (clean, short — not celebratory) | Gold counter ticks down to new balance |
| Failed purchase (`RejectedInsufficientFunds`) | Low rejection tone | Confirm Buy button shakes briefly; cost label turns red momentarily |
| Successful sell | Soft coin deposit sound (lighter than purchase chime) | Gold counter ticks up to new balance |
| Gold counter update (`GoldSyncEvent`) | No dedicated sound — counter updates silently unless a transaction just completed | Numeric fade-through to new value (no bounce, no flash) |
| Shop window open | Subtle interaction sound (cloth or wood — shopkeeper aesthetic) | Window slides in from bottom edge (standard mobile modal) |
| Shop window close | Mirror of open sound, reversed | Window slides out |

No screen shake, no particle effects, no fanfare. The quiet exchange is the design.

📌 **Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:npc-shop` to produce per-asset visual descriptions and audio event specs from this section.

## UI Requirements

The NPC Shop requires a two-tab modal screen accessible from the town hub.

**Both tabs:**
- Gold balance displayed prominently at the top (read from `GoldSyncEvent` cache; same display as HUD; updates on any `GoldSyncEvent`)
- Tab switcher between Buy and Sell

**Buy tab:**
- Scrollable item list: item icon, name, buy price, quantity selector (1 to `selectorMax` per F-NS-4), total cost preview (updates live as quantity changes)
- Confirm Buy button: disabled when `selectorMax = 0` (cannot afford even 1 unit); disabled and shows spinner after tap until `BuyResult` arrives. No optimistic balance update.
- Items grouped with visible section headers: "Enhancement Scrolls" first, then "Potions"
- Items not in the catalog are never shown (no "out of stock" states)
- **Scroll purchase confirmation**: Tapping Confirm Buy for any Enhancement Scroll opens the CR-SHOP-11 confirmation overlay before sending `BuyRequest`

**Sell tab:**
- Grid of eligible inventory slots: all slots where `SellPriceGold > 0`. Both equipment and consumables shown. Locked slots are shown greyed-out with a lock icon — visible but non-actionable. (Hiding locked slots reads as item loss for Korean MMO veterans with an item mid-enhancement; greyed-out communicates deliberate protection.)
- Each slot shows: item icon, name, stack count, per-unit sell price. For enhanced equipment, display "(Base price only)" beneath the sell price (CR-SHOP-9).
- When a slot is selected: a quantity selector appears (identical touch model to Buy tab, CR-SHOP-6 rules 12–13). Default quantity = full stack count. Total gold yield (`SellPriceGold × selectedQuantity`) updates live as quantity changes.
- Confirm Sell button: enabled when a slot is selected and quantity ≥ 1; disabled and shows spinner after tap until `SellResult` arrives. No optimistic balance update.
- Sort order: mirrors inventory slot index (0–19) by default. Final sort order deferred to `/ux-design npc-shop`.

> **📌 UX Flag — NPC Shop**: This system has UI requirements. Before writing epics, run `/ux-design npc-shop` to create a UX spec for the Buy and Sell tabs. Stories referencing shop UI should cite `design/ux/npc-shop.md`, not this GDD directly.

## Acceptance Criteria

### Session Management

**AC-NS-01** [BLOCKING]
GIVEN a player character is in the town hub zone, WHEN the client sends `OpenNPCInteraction(shopNpcId)`, THEN the server sets `NPCInteractionActive = true` for that character and returns `NPCInteractionOpened`.

**AC-NS-02** [BLOCKING]
GIVEN a player character is NOT in the town hub zone, WHEN the client sends `OpenNPCInteraction(shopNpcId)`, THEN the server returns `RejectedNotInTownHub` and `NPCInteractionActive` remains false.

**AC-NS-03** [BLOCKING]
GIVEN a player has an active Enhancement NPC session (`NPCInteractionActive = true` from a prior `OpenNPCInteraction(enhancementNpcId)`), WHEN the client sends `OpenNPCInteraction(shopNpcId)`, THEN: (1) the server returns `NPCInteractionOpened`; (2) a subsequent `BuyRequest` succeeds (confirming the shop session is active); (3) a subsequent `OpenNPCInteraction(enhancementNpcId)` succeeds without `RejectedActiveSession` (confirming the prior Enhancement session was cleared). If wire-capture instrumentation is available: add (4) no `NPCInteractionClearedNotification` message appears in the capture. *(Pre-condition for assertion (4): first verify a positive case — confirm `RejectedActiveSession` CAN be observed — before relying on the negative assertion.)*

**AC-NS-04** [BLOCKING]
GIVEN a player has an open shop session (`NPCInteractionActive = true`), WHEN the player's zone changes, THEN `NPCInteractionActive` is set to `false` and no further `BuyResult` or `SellResult` is processed for that session.

**AC-NS-05** [BLOCKING]
GIVEN a player has an open shop session opened at time T, WHEN `SESSION_TTL_SECONDS` (300s) elapses from T regardless of client activity during that period, THEN `NPCInteractionActive` is set to `false` and subsequent `BuyRequest` returns `RejectedNoNPCSession`. *(Wall-clock cap test — the session expires at T+300s even if the player has been continuously sending messages. Requires time injection or configurable TTL override in test environment — confirm with lead programmer before sprint commitment.)*

**AC-NS-06** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `CloseNPCInteraction()`, THEN `NPCInteractionActive` is set to `false`.

---

### Buy Transaction — Happy Path

**AC-NS-07** [BLOCKING]
GIVEN a player has an open shop session, sufficient gold (≥ `shopPrice × quantity`), and at least one free inventory slot, WHEN the client sends `BuyRequest(itemId, quantity)` for a valid catalog item, THEN: (1) server returns `BuyResult(success=true, itemId, quantity, newGoldBalance)` where `newGoldBalance = priorBalance − (shopPrice × quantity)`; (2) the player's inventory contains `quantity` additional units of `itemId`; (3) `GoldBalance = priorBalance − (shopPrice × quantity)`.

**AC-NS-08** [BLOCKING] — F-NS-1
GIVEN a player buys `quantity` units of an item with `shopPrice`, WHEN `BuyResult(success=true)` is returned, THEN `priorBalance − newGoldBalance = shopPrice × quantity` exactly. Test vectors: (1) 1× Bronze Scroll at 90g → debit 90g; (2) 99× HP Potion Small at 3g → debit 297g; (3) 3× Dark Steel Scroll at 350g → debit 1,050g; (4) 1× HP Potion Large at 30g → debit 30g.

---

### Buy Transaction — Rejection Paths

**AC-NS-09** [BLOCKING]
GIVEN `NPCInteractionActive = false` for a player, WHEN the client sends `BuyRequest(itemId, quantity)`, THEN the server returns `RejectedNoNPCSession` and gold and inventory are unchanged.

**AC-NS-10** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `BuyRequest(itemId, quantity=0)`, THEN the server returns `RejectedInvalidQuantity` before calling `TrySpendGold`. Gold and inventory are unchanged.

**AC-NS-11** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `BuyRequest(itemId, quantity=100)` (above `QUANTITY_SELECTOR_CAP`), THEN the server returns `RejectedInvalidQuantity`. Gold and inventory are unchanged.

**AC-NS-12** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `BuyRequest` with an `itemId` not in the catalog (e.g., an equipment item ID), THEN the server returns `RejectedItemNotInCatalog`. Gold and inventory are unchanged.

**AC-NS-13** [BLOCKING]
GIVEN a player has an open shop session and `GoldBalance = 0`, WHEN the client sends `BuyRequest(HP Potion Small, quantity=1)`, THEN the server returns `RejectedInsufficientFunds` and `GoldBalance` remains 0.

**AC-NS-14** [BLOCKING]
GIVEN a player has an open shop session and `GoldBalance = (shopPrice × quantity) − 1` (one gold short), WHEN the client sends `BuyRequest(itemId, quantity)`, THEN `TrySpendGold` returns `InsufficientFunds`, the server returns `RejectedInsufficientFunds`, and `GoldBalance` is unchanged. *(Canonical stale-selector race case — must be a distinct test, not collapsed with AC-NS-13.)*

---

### Buy Transaction — Inventory Full Refund Path

**AC-NS-15** [BLOCKING]
GIVEN a player has an open shop session, sufficient gold, and a full inventory (all 20 slots occupied with no stack room for the purchased item), WHEN the client sends `BuyRequest(itemId, quantity)`, THEN: (1) server returns `RejectedInventoryFull`; (2) `GoldBalance = priorBalance` (net-zero change — debit and compensating refund both completed); (3) player's inventory is unchanged. *(Integration test — crosses Currency System and Inventory System.)*

**AC-NS-16** [ADVISORY]
GIVEN AC-NS-15 passes and wire-layer message-capture instrumentation is available, WHEN the delivery sequence is inspected, THEN both `GoldSyncEvent` messages (debit and refund) arrive at the client before the gold display updates to the final balance. *(Wire-layer batching verification — advisory. Net-zero correctness is covered by AC-NS-15. File a separate story if batching is required by the architecture.)*

---

### Sell Transaction — Happy Path

**AC-NS-17** [BLOCKING]
GIVEN a player has an open shop session and a non-empty, non-locked inventory slot with `SellPriceGold > 0`, WHEN the client sends `SellRequest(slotIndex, itemId, quantity)`, THEN: (1) server returns `SellResult(success=true, itemId, quantitySold, goldEarned, newGoldBalance)`; (2) the slot's stack count decrements by `quantity` (slot is empty if `quantity` = full stack); (3) `GoldBalance = priorBalance + goldEarned` (where `goldEarned` is the actual credited amount).

**AC-NS-18** [BLOCKING] — F-NS-2
GIVEN a sell completes successfully, WHEN `SellResult` is returned, THEN `goldEarned = SellPriceGold × quantitySold` exactly. Test vectors: (1) 1× HP Potion Small at 2g sell → earn 2g; (2) 10× HP Potion Large at 18g sell → earn 180g; (3) 1× MP Potion Medium at 6g sell → earn 6g.

**AC-NS-19** [BLOCKING]
GIVEN a player has a stack of 10 HP Potions (Large) in one slot and has not adjusted the sell quantity selector, WHEN the player confirms sell via `SellRequest(slotIndex, itemId, quantity=10)` (default full-stack), THEN all 10 are removed and the slot is empty. *(Verifies CR-SHOP-8: default = full stack. N=10 is the explicit test value.)*

**AC-NS-19b** [BLOCKING]
GIVEN a player has a stack of 10 HP Potions (Large) in one slot and adjusts the sell quantity selector to 3, WHEN the player confirms sell via `SellRequest(slotIndex, itemId, quantity=3)`, THEN: (1) `SellResult.quantitySold = 3`; (2) the slot contains 7 HP Potions (Large) after the transaction; (3) `goldEarned = 3 × 18g = 54g`. *(Verifies CR-SHOP-8 partial-stack sell.)*

---

### Sell Transaction — Rejection Paths

**AC-NS-20** [BLOCKING]
GIVEN `NPCInteractionActive = false`, WHEN the client sends `SellRequest(slotIndex, itemId, quantity=1)`, THEN the server returns `RejectedNoNPCSession`. Inventory and gold are unchanged.

**AC-NS-21** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `SellRequest(slotIndex=20, itemId, quantity=1)` (index out of range [0, 19]), THEN the server returns `RejectedInvalidSlot`. Inventory and gold are unchanged.

**AC-NS-22** [BLOCKING]
GIVEN a player has an open shop session and slot 3 holds item A, WHEN the client sends `SellRequest(slotIndex=3, itemId=B, quantity=1)` where B ≠ A, THEN the server returns `RejectedItemMismatch`. Inventory and gold are unchanged.

**AC-NS-23** [BLOCKING]
GIVEN a player has an open shop session and slot 5 is locked (`IsSlotLocked(5) = true`), WHEN the client sends `SellRequest(slotIndex=5, itemId, quantity=1)`, THEN the server returns `RejectedSlotLocked`. Inventory and gold are unchanged.

**AC-NS-24** [BLOCKING]
GIVEN a player has an open shop session and a Bronze Enhancement Scroll in slot 2 (`SellPriceGold = 0`), WHEN the client sends `SellRequest(slotIndex=2, itemId=BronzeEnhancementScroll, quantity=1)` (bypassing any client-side filter), THEN the server returns `RejectedUnsellable`. The scroll remains in slot 2 and gold is unchanged. *(Tests server-side guard independently of Sell tab UI filtering.)*

---

### Equipment Sell-Back

**AC-NS-25** [BLOCKING]
GIVEN a player has a +7 Iron Sword (enhancement level 7) in slot 0 with `SellPriceGold = 30g` (base tier value), WHEN the player sells it via `SellRequest(slotIndex=0, itemId=IronSword, quantity=1)`, THEN `SellResult.goldEarned = 30g` — the enhancement level contributes no premium. `newGoldBalance = priorBalance + 30`. *(Verifies CR-SHOP-9: enhanced equipment sells at base SellPriceGold only.)*

**AC-NS-26** [ADVISORY]
GIVEN a player has equipment with `SellPriceGold > 0` in an unlocked slot, WHEN the player opens the Sell tab, THEN the equipment slot is visible in the Sell tab grid alongside potion slots. *(Manual walkthrough acceptable as evidence.)*

---

### Anti-Arbitrage Invariant

**AC-NS-27** [BLOCKING] — F-NS-3
GIVEN the NPC Shop catalog is loaded at server startup, WHEN the server validates all catalog items with `SellPriceGold > 0`, THEN `(BuyPrice × 10) ≥ (SellPriceGold × 15)` for all 6 potion types. Test vectors: HP Potion Small (30 ≥ 30 — boundary, must pass); HP Potion Medium (100 ≥ 90 ✓); HP Potion Large (300 ≥ 270 ✓); MP Potion Small/Medium/Large (same as HP equivalents ✓).

**Scroll exemption positive test**: GIVEN a test catalog containing a Bronze Enhancement Scroll (`SellPriceGold = 0`) AND a potion with `BuyPrice = 2g, SellPriceGold = 2g` (invariant violation), WHEN startup validation runs, THEN the server halts for the potion violation but completes startup validation of the scroll without error — confirming scrolls are correctly excluded from the anti-arbitrage check.

**AC-NS-28** [BLOCKING]
GIVEN a catalog item is authored with `BuyPrice = 2g` and `SellPriceGold = 2g` (violates F-NS-3: 20 < 30), WHEN the server attempts startup, THEN the server halts and logs the specific `ItemID`, `BuyPrice`, and `SellPriceGold` — no partial startup occurs. *(Requires catalog injection in test environment — confirm test harness supports this before sprint commitment.)*

---

### Quantity Selector (F-NS-4)

**AC-NS-29** [ADVISORY] — F-NS-4
GIVEN a player opens the Buy tab with a known `playerGoldBalance`, WHEN the quantity selector renders for each catalog item, THEN `selectorMax = min(floor(playerGoldBalance / shopPrice), 99)`. Test vectors: (1) 1,000g, Dark Steel Scroll 350g → selectorMax=2; (2) 50,000g, HP Potion Small 3g → selectorMax=99 (capped); (3) 2g, HP Potion Small 3g → selectorMax=0 (Confirm Buy disabled); (4) 90g, Bronze Scroll 90g → selectorMax=1. *(F-NS-4 is client-side display only; UI interaction test or manual walkthrough acceptable.)*

---

### Catalog Completeness and Equipment Buy Restriction

**AC-NS-30** [BLOCKING]
GIVEN the server is running with the MVP catalog, WHEN the Buy tab items are queried, THEN exactly 10 items are present: 4 Enhancement Scrolls (Bronze/Iron/Steel/Dark Steel) and 6 Potions (HP/MP × Small/Medium/Large). No equipment items appear in the Buy catalog.

**AC-NS-31** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `BuyRequest(itemId=IronSword, quantity=1)` (an equipment item ID), THEN the server returns `RejectedItemNotInCatalog`. Gold and inventory are unchanged.

---

### GoldSyncEvent Delivery

**AC-NS-32** [BLOCKING]
GIVEN a successful purchase, WHEN `BuyResult(success=true)` is returned, THEN the server has emitted a `GoldSyncEvent` with `NewBalance = priorBalance − totalCost`. *(Server-side assertion: verify the event is queued for delivery.)*

**AC-NS-32b** [ADVISORY]
GIVEN AC-NS-32 passes and the `GoldSyncEvent` is received by the client, THEN the client gold display updates to `priorBalance − totalCost`. *(Client rendering — manual walkthrough or UI interaction test acceptable.)*

**AC-NS-33** [BLOCKING]
GIVEN a successful sell, WHEN `SellResult(success=true)` is returned, THEN the server has emitted a `GoldSyncEvent` with `NewBalance = priorBalance + goldEarned`. *(Server-side assertion: verify the event is queued for delivery.)*

**AC-NS-33b** [ADVISORY]
GIVEN AC-NS-33 passes and the `GoldSyncEvent` is received by the client, THEN the client gold display updates to `priorBalance + goldEarned`. *(Client rendering — manual walkthrough or UI interaction test acceptable.)*

---

---

### Infinite Stock and Session Re-Open

**AC-NS-34** [BLOCKING]
GIVEN a player has purchased 5 Bronze Enhancement Scrolls in a single session, WHEN the player sends `BuyRequest(BronzeEnhancementScroll, quantity=1)` again, THEN the server processes it successfully and returns `BuyResult(success=true)`. *(Verifies CR-SHOP-2: stock is never depleted.)*

**AC-NS-35** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `OpenNPCInteraction(shopNpcId)` again (same NPC, session already active), THEN the server returns `NPCInteractionOpened`, the session restarts (wall-clock TTL resets to T=0), and a subsequent `BuyRequest` succeeds.

---

### Transaction Integrity Under Session Disruption

**AC-NS-36** [BLOCKING]
GIVEN a player's `TrySpendGold` has succeeded but `PickupRequest` has not yet executed, WHEN the player's zone changes (clearing `NPCInteractionActive`), THEN the compensating `AddGold(charId, totalCost, CompensatingRefund)` still executes and `GoldBalance = priorBalance` after both operations complete. *(Server-side integration test — requires test harness to inject zone change between the two operations. Pre-condition: see OQ-NS-5 Purchase Transaction Integrity ADR.)*

---

### Quantity Validation (Server-Side Guards)

**AC-NS-37** [BLOCKING]
GIVEN a player has an open shop session, WHEN the client sends `BuyRequest(itemId, quantity=-1)` (negative quantity), THEN the server returns `RejectedInvalidQuantity` before calling `TrySpendGold`. Gold and inventory are unchanged. *(Guards against negative-int-to-uint wrap in F-NS-1.)*

---

### Non-Refundable Purchase Confirmation (CR-SHOP-11)

**AC-NS-39** [BLOCKING]
GIVEN a player has an open shop session and taps Confirm Buy for a Bronze Enhancement Scroll, WHEN the CR-SHOP-11 confirmation overlay appears, THEN no `BuyRequest` has been sent to the server. Only after tapping the overlay's Confirm button is `BuyRequest` sent. *(Client-side: verify no server round-trip at the initial Confirm Buy tap for `SellPriceGold = 0` items.)*

**AC-NS-40** [BLOCKING]
GIVEN the CR-SHOP-11 scroll purchase confirmation overlay is displayed, WHEN the player taps Cancel, THEN no `BuyRequest` is sent, the overlay is dismissed, and the Buy tab shows the scroll selected with the prior quantity unchanged.

---

### Enhanced Equipment Sell Price Disclosure

**AC-NS-41** [ADVISORY]
GIVEN a player has enhanced equipment (e.g., a +7 Iron Sword) in inventory, WHEN the player opens the Sell tab, THEN the slot displays "(Base price only)" beneath the per-unit sell price. *(Manual walkthrough acceptable. Verifies CR-SHOP-9 base-price disclosure in UI.)*

---

**BLOCKING: 37 | ADVISORY: 7 | Total: 44**

*Infrastructure pre-conditions for sprint commitment: (1) AC-NS-05 requires time-injection or configurable TTL in test env. (2) AC-NS-36 requires test harness capable of injecting zone change mid-transaction. (3) AC-NS-28 requires catalog injection support in test env. (4) AC-NS-39/40 require client-side wire-capture to confirm no premature server request. Resolve all four with lead programmer before locking this GDD to a sprint. (5) AC-NS-19b/CR-SHOP-8 partial-stack sell requires Inventory System `SellItem` interface update — confirm before sprint (OQ-NS-7).*

## Open Questions

**OQ-NS-1 — NPCInteractionActive flag typing**
`NPCInteractionActive` is a boolean shared by all NPC types (Enhancement NPC, NPC Shop). When more NPC types are added (Respec NPC, Repair NPC in future tiers), the boolean loses specificity — the server knows a session is active but not which NPC. Should this be refactored to a typed `ActiveNpcId: uint?` or `NpcSessionType: enum?` before additional NPC types are introduced? Both Enhancement System and NPC Shop reference it as a boolean; any change requires amending both GDDs.
*Owner: Lead Programmer / Enhancement System GDD. Target: Before Vertical Slice NPC authoring.*

**OQ-NS-2 — Sell tab sort order**
Two candidate orders: (A) inventory slot index 0–19 (mirrors the bag, familiar); (B) sell value descending (surfaces most valuable items for quicker sell decisions). No mechanical impact — decision deferred to UX designer.
*Owner: UX Designer. Target: /ux-design npc-shop.*

**OQ-NS-3 — Item Database F-2 constraint amendment** ✅ **RESOLVED 2026-06-07**
Item Database GDD amended (F-2 Amendment, 2026-06-07): F-2 invariant removed, concrete sell prices filled in (Small=2g, Medium=6g, Large=18g). Large Potion sell = 18g is the authoritative value. Cross-document consistency confirmed.

**OQ-NS-4 — Enhancement Scroll item records in Item Database**
The 4 Enhancement Scroll types must be formally authored in the Item Database with `SellPriceGold = 0`, `ItemCategory = Consumable`, and `IsUpgradeable = false`. No Item Database records exist yet; `GetItem(scrollId)` returns null in startup validation (F-NS-3), causing a null-dereference before any session begins. Scroll records must be added before this GDD can be implemented.
*Owner: Game Designer / Item Database authoring. Target: First item authoring pass — blocking for NPC Shop implementation.*

**OQ-NS-5 — Purchase Transaction Integrity ADR** ✅ **RESOLVED 2026-06-07**
See `docs/architecture/ADR-001-purchase-transaction-integrity.md` (Accepted 2026-06-07). Key decisions accepted: `requestId: uint` idempotency key on BuyRequest/SellRequest; `PendingPurchase` durable record created before TrySpendGold; reconnect reconciliation refunds all `state=GoldDebited` records on SessionHandshake. Systems requiring updates before implementation: networking-wire-protocol.md, networking-channel-contract.md, networking-message-criticality.md, character-persistence.md, networking-session.md (see ADR Consequences table).

**OQ-NS-6 — Enhancement System session preemption callback**
When `OpenNPCInteraction(shopNpcId)` clears an active Enhancement session, the Enhancement System must be notified before the flag is cleared (e.g., `OnNPCSessionPreempted(charId)` callback), so in-flight `ConfirmEnhancement` can complete safely. Without this, an enhancement that completes after the session flag is cleared has no delivery path for `EnhancementAttemptResult` — the item may be destroyed with no client notification. Requires coordination between NPC Shop and Enhancement System GDD authors.
*Owner: Enhancement System GDD author + NPC Shop author. Target: Before NPC Shop implementation sprint. Blocking.*

**OQ-NS-7 — Wire message registration in networking stack** ✅ RESOLVED 2026-06-07
All 18 NPC Shop wire messages registered across all three networking documents (actual count is 18 — `NPCInteractionOpened` was omitted from the original shorthand list of 17). Registrations applied:
- `networking-message-criticality.md` — MCR-2 table: all 18 messages classified (R-OD, Pillar 1 or Infrastructure, Guaranteed delivery)
- `networking-channel-contract.md` — CCR-3 table: all 18 messages with routing invariants including ADR-001 deduplication notes
- `networking-wire-protocol.md` — "NPC Shop System Messages" section added: full wire schemas for all 18 messages including `requestId: uint` on `BuyRequest`/`SellRequest`, rate-limit notation (10 req/sec per ADR-001), and all 10 rejection messages documented with shared schema

*Owner: Network Programmer. Resolved: 2026-06-07.*
