# Story 010: Rare Drop Auction — Open, Bid Validation, Broadcast

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-8 (Auction Window), CR-LT-15 (Server Authority), States (`Auctioning`), Edge Cases (late bid; bid below floor; member leaves before close), Tuning Knobs (Auction)
**Requirement**: `TR-loot-010`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: `LootBidRequest` arrives through an NGO handler that only enqueues; the tick loop calls the loot service's bid method. Accepted bids are announced with a Tier 2 event the network layer turns into `LootBidUpdate`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API.

**Control Manifest Rules (Core layer)**:
- Required: NGO message handlers enqueue to `Queue<T>`; game logic runs in the tick loop — ADR-010
- Required: `event Action<T>` with `readonly struct` T, `On` prefix — ADR-010
- Forbidden: calling game logic directly from an NGO handler — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-11** [BLOCKING]: a Steel item (`SellPriceGold = 90`) drops in a party of 2 → the item enters `Auctioning`. A bid of 89g is rejected silently (no bid-update broadcast). A bid of exactly 90g is accepted and broadcast to all party members.
- [ ] **AC-LT-17** [BLOCKING]: an auction's `windowCloseTick` has been reached; a bid arrives with `receivedTick > windowCloseTick` → it is rejected; no auction state changes; no bid update is broadcast; the rejection is logged.
- [ ] **CR-LT-8 window**: the auction window lasts `AUCTION_WINDOW_TICKS` (600) from the tick the auction opens.
- [ ] **CR-LT-8 floor**: the minimum bid is the item's `SellPriceGold` (90g Steel, 270g DarkSteel), read from the item definition — not a separate constant.
- [ ] **CR-LT-8 revision**: a member may raise their own bid any number of times before the window closes; a bid that is not higher than that member's current bid is rejected.
- [ ] **CR-LT-8 eligibility**: only a current member of the winning party may bid on that auction.
- [ ] **Spawn visibility**: a rare drop's spawn event has `isAuction = true` and is addressed to all current party members.

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-8.*

**Module conventions:** see Story 001.

**Opening.** Replace the party-size ≥ 2 branch of `BeginRareDrop` (Story 006): spawn the item, and after its 1-tick `Spawning` state move it to `Auctioning` with `windowCloseTick = auctionOpenTick + AUCTION_WINDOW_TICKS` (600; `LootTableConstants`). Record the owning `PartyID`. `OnGroundItemSpawned` carries `isAuction = true` and the party's members as recipients.

**Bid entry point:** `SubmitBid(CharacterID bidder, GroundItemID, uint bidAmount, uint receivedTick)`, returning a result code (accepted or a rejection reason) for the caller's log. Validation, first failure wins:
1. the ground item exists and is `Auctioning`;
2. `receivedTick ≤ windowCloseTick` — otherwise reject and log (AC-LT-17);
3. the bidder is currently a member of the auction's party (`IPartyService.GetPartyMembers`);
4. `bidAmount ≥ SellPriceGold` of the item (equipment cache, Story 002);
5. `bidAmount >` the bidder's current bid on this auction, if any.

An accepted bid stores `(bidder, amount, receivedTick)` — the tick is the tie-break in Story 011 — replacing the bidder's previous bid, and raises `OnLootBidUpdate` addressed to all party members. A rejected bid raises nothing and changes nothing; no other member is notified.

**The bidder's gold balance is not checked here.** The GDD checks affordability only at resolution (`TrySpendGold`, Story 011).

**`AUCTION_MIN_BID_STEEL` / `AUCTION_MIN_BID_DARKSTEEL`** in the GDD's Tuning Knobs are defined as equal to the tiers' `SellPriceGold` and "must be kept synchronized". Read `SellPriceGold` directly; do not create separate constants that could drift.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 011**: closing the window, choosing the winner, gold, zero-bid fallback, `AuctionResolved`. Until Story 011, an auction whose window closes simply stays `Auctioning` until `expiryTick` despawns it.
- The auction bid window UI and its audio cue — no acceptance criteria in this GDD and no UX spec.
- Wire codecs for `LootBidRequest` / `LootBidUpdate`.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionBids_integration_tests.cs` (stub `IPartyService`; Item Database seeded with a Steel item, `SellPriceGold = 90`)

- **AC-LT-11**: open and floor
  - Given: party `[A, B]`; a kill whose drop list is one Steel item
  - When: resolved and ticked once
  - Then: the item is `Auctioning`; `SubmitBid(A, item, 89, …)` is rejected and raises no `OnLootBidUpdate`; `SubmitBid(A, item, 90, …)` is accepted and raises one `OnLootBidUpdate` addressed to A and B
  - Edge cases: a DarkSteel item (`SellPriceGold = 270`): 269 rejected, 270 accepted

- **AC-LT-17**: late bid
  - Given: an auction with `windowCloseTick = W` and one accepted bid
  - When: `SubmitBid(B, item, 500, W + 1)`
  - Then: rejected; the stored bids are unchanged; no `OnLootBidUpdate`; one rejection log line
  - Edge cases: `receivedTick = W` is accepted

- **Window length**
  - Given: an auction opened at tick T
  - Then: `windowCloseTick = T + 600`
  - Edge cases: none

- **Revision**
  - Given: A's accepted bid of 100
  - When: A bids 150, then 150 again, then 120
  - Then: 150 is accepted (one update); the repeat 150 and the 120 are rejected (no update); A's stored bid is 150
  - Edge cases: none

- **Eligibility**
  - Given: an auction for party `[A, B]`; character C in another party
  - When: `SubmitBid(C, item, 500, …)`
  - Then: rejected, no update
  - Edge cases: a bid on an unknown `GroundItemID`, or on an item that is `Assigned`, is rejected

- **Spawn visibility**
  - Given: the Steel drop in party `[A, B]`
  - Then: `OnGroundItemSpawned` has `isAuction = true` and recipients A and B
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionBids_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006 (rare-drop routing, `IPartyService`), Story 005 (ground items).
- Unlocks: Story 011.
