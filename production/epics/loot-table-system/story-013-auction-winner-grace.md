# Story 013: Auction Winner Grace

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

> **Added 2026-10-03** from a user decision taken at `/story-readiness` for Story 012: a winner whose bag is full gets a short time to free a slot; if they do not, the next bid wins. This replaces the bag-full-winner behaviour shipped in Story 011 (item `Assigned` to the winner, bid already paid). The GDD amendment (CR-LT-9.1) was revised the same day after two lean re-reviews (balance read before a grace; no grace for a bidder disconnected when their bid is tried; fresh TTL for a fallback after a grace, carried by `GroundItemAssigned.expiryTick`) and was Approved at lean re-review #3 on 2026-10-03. `/story-readiness` 2026-10-03: READY (party query confirmed by the user).

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-9.1 (Winner Grace on Bag Full), CR-LT-9, CR-LT-10, CR-LT-12 (fresh TTL for a fallback at or after `expiryTick`, or after a grace), Tuning Knobs (`AUCTION_WINNER_GRACE_TICKS`)
**Requirement**: `TR-loot-012`
*(Placeholder ID — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: The loot service calls `ICurrencyService`, `IInventoryService` and `IPartyService` directly (Tier 1) and announces outcomes with Tier 2 events (`OnAuctionResolved`, `OnBagFullPickupBlocked`).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API.

**Control Manifest Rules (Core layer)**:
- Required: Tier 1 direct calls on injected interfaces; Tier 2 events with `readonly struct` args; subscribe in the constructor, unsubscribe in `Dispose()` — ADR-010
- Forbidden: lambda captures for persistent subscriptions; shared mutable state polling between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [x] **Winner with a free slot — unchanged**: AC-LT-12 and AC-LT-13 behave exactly as in Story 011 (one `TrySpendGold`, one pickup, pool split, one `OnAuctionResolved` on the close tick).
- [x] **AC-LT-25 (grace starts)** [BLOCKING]: DarkSteel auction of AC-LT-12, A's bag full at `windowCloseTick`, A connected with a balance ≥ 400g → no `TrySpendGold`, no `AddGold`; one `OnBagFullPickupBlocked` for A with `remainingTicks = AUCTION_WINNER_GRACE_TICKS`; the item stays `Auctioning`; no `OnAuctionResolved`.
- [x] **AC-LT-25 (slot freed)** [BLOCKING]: A's inventory changes and has a free slot before the grace expires → on the first tick after the change: one `TrySpendGold(A, 400, AuctionBid)`, one `Pickup(A, itemID, 1)`, four `AddGold(_, 100, MonsterDrop)`, one `OnAuctionResolved` naming A. A does not need to be near the item.
- [x] **AC-LT-25 (grace expires)** [BLOCKING]: A never frees a slot → at `windowCloseTick + AUCTION_WINNER_GRACE_TICKS` A is disqualified with no gold moved, and B (400g at tick 120) is tried on that tick under the same rule.
- [x] **AC-LT-25 (cannot afford)** [BLOCKING]: A's bag full and A's balance 399g at `windowCloseTick` → no blocked notice for A, no `TrySpendGold(A, …)`, B is tried on `windowCloseTick`.
- [x] **AC-LT-25 (disconnected)** [BLOCKING]: A's bag full and A not connected at `windowCloseTick` (`IsMemberConnected(A)` false — party member status `Ghost`; the GDD's AC-LT-25 writes `Disconnected`, which is not a status value) → no blocked notice, B is tried on `windowCloseTick`. A stops being connected during a grace → nothing changes; the grace runs to its deadline.
- [x] **AC-LT-25 (all disqualified)** [BLOCKING]: every bidder's grace expires → round-robin fallback (CR-LT-10), no gold moved, `OnAuctionResolved` with the fallback outcome, and the item's `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS`.
- [x] **AC-LT-25 (pool on the paying tick)** [BLOCKING]: a member leaves during A's grace, then A frees a slot → three `AddGold(_, 133, MonsterDrop)`.
- [x] **No despawn during a grace**: the item is still live and `Auctioning` past its `expiryTick` while a grace runs. A fallback reached at or after `expiryTick`, or after at least one grace, gets `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS` (CR-LT-12).
- [x] **Leaver during a grace**: the reserved bidder leaves the party → disqualified on the first tick after, with no gold moved; the next bid is tried.

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-9.1.*

**Module conventions:** see Story 001.

**Constant:** `LootTableConstants.AUCTION_WINNER_GRACE_TICKS = 600` (safe range [200, 1200]).

**Resolution becomes resumable.** `LootAuctionService` keeps, per auction that has closed but not resolved, the ordered list of remaining bids and the grace deadline of the bidder currently reserved. The auction stays in the set of open auctions until its final outcome.

**Per tick, for an auction in a grace:**
1. If the reserved bidder is no longer in the auction's party, or `currentTick` has reached the deadline (`StaleDiscardComparer.IsTickExpired`) → disqualify, continue the close sequence below with the next bid.
2. Else if the bidder's inventory changed since the last tick and `IInventoryService.HasFreeSlot(bidder)` → run the normal Story 011 sequence for that bidder (spend, pool split with the party size on this tick, `AwardAuctionItem`, `OnAuctionResolved`). A failed spend (gold spent during the grace) disqualifies the bidder exactly as in Story 011.

**Close sequence (replaces Story 011 step 2):** for each remaining bid in order —
1. `HasFreeSlot(bidder)` true → spend / pool / award as today. A non-`Success` spend moves to the next bid.
2. `HasFreeSlot(bidder)` false and (`ICurrencyService.GetBalance(bidder)` < bid, or `IPartyService.IsMemberConnected(bidder)` is false) → disqualify with no notice; move to the next bid on this tick.
3. `HasFreeSlot(bidder)` false otherwise → start the grace: store the deadline `currentTick + AUCTION_WINNER_GRACE_TICKS`, raise the blocked notice, mark the auction as "had a grace", stop for this tick.
4. No bids left → round-robin fallback (Story 011 step 4). If the auction had a grace, or the fallback is at or after `expiryTick`, the assigned item gets `expiryTick = currentTick + GROUND_ITEM_TTL_TICKS`.

**Connection state.** CR-LT-9.1 reads the bidder's party member status once, when a full-bag bid is tried; a disconnect during a grace changes nothing. The loot module's `IPartyService` has no status read today (`GetPartyID`, `GetPartyMembers`, `GetMemberAtIndex`, `GetRrNextIndex`, `AdvanceRrNextIndex`): add one query, `bool IsMemberConnected(CharacterID characterId)` — false when the member's party status is `Ghost` (disconnected for longer than the 5-second reconnect window; `Ghost` is the only disconnected value of `PartyMemberStatus`). No push from the network layer and no state kept in the auction service. *(Confirmed by the user at `/story-readiness`, 2026-10-03.)*

**Test fixtures.** The new `IPartyService` method must be added to every `StubPartyService` (six test files under `tests/EditMode/`; return `true` except in this story's tests). `RecordingInventoryService.FreeSlot` in `tests/EditMode/LootTableSystem/LootTestFakes.cs` is one answer for every character; this story needs A full while B has a free slot, so add a per-character override that falls back to `FreeSlot` — existing tests must keep passing unchanged. `LootAuctionService` gains an `IInventoryService` constructor argument; update its test rigs.

**Fresh `expiryTick` on the wire.** `GroundItemAssigned` now carries `expiryTick` (networking-wire-protocol.md, 2026-10-03). Add `ExpiryTick` to `GroundItemAssignedEventArgs` and set it from the item record at every raise, so the network layer can fill the field.

**Inventory changes.** `LootAuctionService` takes `IInventoryService` and subscribes to `OnInventoryChanged` with a named handler that only records the character (never calls the inventory from the handler — it throws on a synchronous mutation); the set is snapshotted at the start of `Tick`, as `GroundItemService` does. Unsubscribe in `Dispose()`.

**Blocked notice.** The existing `OnBagFullPickupBlocked` event lives on `IGroundItemService`. Add a method there to raise it for an auction item with a caller-supplied `remainingTicks` (the service owns the item ID, name lookup and the event), rather than a second event on the auction service — the network layer already maps this one to `BagFullPickupBlocked`.

**What changes from Story 011.** `AwardAuctionItem` is now only called for a bidder who had a free slot a moment earlier. If it still reports a full bag (the slot was taken between the check and the pickup), the bid is already spent: keep Story 011's behaviour for that case — the item stays `Assigned` to the winner — and log one warning. The two Story 011 bag-full-winner tests (`Tick_DarkSteelAuctionWinnerBagFull_…`, `Tick_AuctionClosesAtExpiryTickWithWinnerBagFull_…`) and `Tick_WinnerWithFullBagStandsOnTheItem_…` are rewritten for the grace.

**Confirmed by the user 2026-10-03:** the leaver rule, the party size taken on the paying tick, no TTL pause during a grace, the safe range [200, 1200], no new item state; plus the balance read before a grace, no grace for a bidder who is disconnected when their bid is tried (party member status; a disconnect during a grace does not end it), and the fresh TTL for a fallback after any grace.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 012**: zone teardown (no grace; full-bag bidders are skipped).
- The auction UI countdown and the discard modal.
- A refund path: none is needed, the bid is spent only when delivery is possible. The thrown-pickup loss stays as TD-051.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionWinnerGrace_integration_tests.cs` (real `CurrencySystem` behind a call-recording wrapper; `RecordingInventoryService` with `FreeSlot` and `RaiseInventoryChanged`; stub `IPartyService`)

- **Free slot — unchanged**
  - Given: the AC-LT-12 auction, every bag has a free slot
  - When: `Tick` reaches `windowCloseTick`
  - Then: as Story 011 — winner A, one spend, four `AddGold(_, 100, MonsterDrop)`, `OnAuctionResolved(item, A, 100, false)` on that tick
  - Edge cases: none

- **AC-LT-25: grace starts**
  - Given: the same auction, A's bag full
  - When: `Tick` reaches `windowCloseTick`
  - Then: no spend, no `AddGold`; one blocked notice for A with `remainingTicks = AUCTION_WINNER_GRACE_TICKS`; item `Auctioning`; no `OnAuctionResolved`
  - Edge cases: further ticks inside the grace with no inventory change → nothing new (one notice only)

- **AC-LT-25: slot freed**
  - Given: A in a grace
  - When: A's inventory raises a change with a free slot, then the next `Tick`
  - Then: one `TrySpendGold(A, 400, AuctionBid)`, one `Pickup(A, item, 1)`, four `AddGold`, `OnAuctionResolved(item, A, 100, false)`; balances as AC-LT-13
  - Edge cases: an inventory change that leaves the bag full → still in the grace; A cannot pay (300g) when the slot frees → A disqualified, B tried on that tick

- **AC-LT-25: grace expires**
  - Given: A in a grace, never frees a slot; B has a free slot
  - When: `Tick` reaches `windowCloseTick + AUCTION_WINNER_GRACE_TICKS`
  - Then: no spend for A; `TrySpendGold(B, 400, AuctionBid)`; B wins on that tick
  - Edge cases: B's bag is also full → B gets its own notice and a full grace from that tick

- **AC-LT-25: cannot afford**
  - Given: the AC-LT-12 auction, A's bag full, A's balance 399g; B has a free slot
  - When: `Tick` reaches `windowCloseTick`
  - Then: no blocked notice for A, no `TrySpendGold(A, …)`; B wins on that tick
  - Edge cases: A's balance exactly 400g → the grace starts

- **AC-LT-25: disconnected**
  - Given: the AC-LT-12 auction, A's bag full, the party stub reports A as not connected; B has a free slot
  - When: `Tick` reaches `windowCloseTick`
  - Then: no blocked notice; B wins on that tick
  - Edge cases: A not connected with a free slot → A wins as in Story 011; A becomes not connected during a grace → still reserved, and A wins if a slot frees before the deadline

- **AC-LT-25: all disqualified**
  - Given: bids A 400 and B 350, both bags full for both graces
  - When: `Tick` reaches the end of B's grace
  - Then: round-robin assignment; no spend, no `AddGold`; `OnAuctionResolved(item, invalid, 0, true)`; the item's `expiryTick` = that tick + `GROUND_ITEM_TTL_TICKS`, and `OnGroundItemAssigned` carries that `ExpiryTick`
  - Edge cases: none

- **AC-LT-25: pool on the paying tick**
  - Given: A in a grace, party of 4
  - When: a member other than A leaves the party, then A's inventory raises a change with a free slot, then the next `Tick`
  - Then: three `AddGold(_, 133, MonsterDrop)`; `OnAuctionResolved(item, A, 133, false)`
  - Edge cases: none

- **No despawn during a grace**
  - Given: an auction whose `expiryTick` falls inside A's grace
  - When: `Tick` passes `expiryTick`
  - Then: item still live and `Auctioning`; no `OnGroundItemDespawned`
  - Edge cases: the fallback is then reached after `expiryTick` → the item is `Assigned` with `expiryTick = that tick + GROUND_ITEM_TTL_TICKS`; a fallback reached before `expiryTick` after one grace → same fresh `expiryTick`

- **Leaver during a grace**
  - Given: A in a grace
  - When: A leaves the party, then the next `Tick`
  - Then: no spend for A; the next bid is tried on that tick
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionWinnerGrace_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 22 tests, all passing (EditMode 1427/1427, Unity 6000.3.10f1 batch mode, 2026-10-03)

---

## Dependencies

- Depends on: Story 011 (auction resolution — Complete 2026-10-03), Story 008 (blocked notice and inventory-change handling — Complete).
- Unlocks: Story 012.

**Performance**: no impact expected — one membership check, one `GetBalance` read at a grace start, and at most one `HasFreeSlot` call per auction in a grace per tick; auctions in a grace are bounded by open auctions.

---

## Completion Notes
**Completed**: 2026-10-03
**Criteria**: 10/10 passing
**Test Evidence**: Integration: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionWinnerGrace_integration_tests.cs` (22 tests). Full EditMode suite 1427/1427 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — lean self-review in the implementing session (no specialist agents); CHANGES REQUIRED → fixed → APPROVED WITH SUGGESTIONS. QL-TEST-COVERAGE and LP-CODE-REVIEW gates skipped (Lean mode).
**Deviations** (all advisory):
- The "bidder had a free slot but the pickup failed" case logs `Debug.LogError` (GDD States table), not the warning these notes describe. The message says "the pickup failed" because it also fires for a failure other than a full bag.
- A `Tick` re-entered from a subscriber returns without doing anything (the due list is read in place, so an auction in a grace does not allocate every tick).
- `TryGetBid` returns false once an auction has closed into a grace: the bids move into the pending resolution state.
- Estimate was 4 hours; the work touched 15 files and one code-review round.

**User decisions 2026-10-03**: the party query is `bool IsMemberConnected(CharacterID)`; a bidder who frees a slot on the deadline tick itself is disqualified (the deadline is checked first) — pinned by `Tick_ASlotFreesOnTheDeadlineTick_AIsStillDisqualifiedAndBWins`.

**Additions beyond the story text**: `IGroundItemService.RaiseAuctionGraceBlocked`; a `freshPickupWindow` parameter on `AssignAuctionItem` and `AwardAuctionItem`; `PendingResolution.SlotCheckDue` (an inventory change is kept until `HasFreeSlot` is read, so a tick that fails at the party read does not lose it); `SubmitBid` returns `WindowClosed` for an auction in a grace; `RecordingInventoryService.FreeSlotByCharacter`.

**Tech debt logged (2026-10-03)**: TD-054 (rig and fakes duplicated between the auction test files), TD-055 (a bidder not registered in the Inventory System gets a grace), TD-056 (GDD wording items of the 2026-10-03 design re-review and of this story).

**For Story 012**: zone teardown must end a running grace, and the pending resolution state is private to `LootAuctionService` — that story needs a method for it. `TR-loot-012` is still a placeholder (`tr-registry.yaml` is empty).
