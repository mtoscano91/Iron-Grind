# Story 010: Rare Drop Auction — Open, Bid Validation, Broadcast

> **Epic**: Loot Table System
> **Status**: Complete
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

**Performance**: A bid costs one item lookup, one party lookup and a scan of that auction's bids (at most one per party member, so four). No per-tick cost beyond the existing state step. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

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
- [ ] **Spawn visibility**: a rare drop's spawn event has `isAuction = true` and is addressed to all current party members — the event carries the auction's `PartyId` (and no single assignee); the network layer resolves the members *(representation settled at readiness, 2026-10-02)*.

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

**Settled at readiness (2026-10-02) — where these differ from the notes above, these win:**
- **Structure (user decision): a new `LootAuctionService`.** `GroundItemService` keeps what it owns — the item's state (now including `Auctioning`), its party and its `WindowCloseTick` — and gains `SpawnAuction(ItemID, Vector3, PartyID, uint spawnTick)`. `LootAuctionService` owns the bids and `SubmitBid`, raises `OnLootBidUpdate`, and drops an auction's bids when its item despawns (it subscribes to `OnGroundItemDespawned`, so it is `IDisposable`). It depends on `IGroundItemService`, `IPartyService` and `LootEquipmentCache`. Story 011's winner selection and gold go into this class.
- **Recipients on the events:** `GroundItemSpawnedEventArgs` gains `PartyId`. For an auction `IsAuction` is true, `AssignedTo` is `CharacterID.Invalid`, and the network layer sends to that party's current members; a common drop keeps `AssignedTo` and carries `PartyID.Uninitialized`. `OnLootBidUpdate` carries `groundItemId`, `bidderId`, `bidAmount` and `PartyId` the same way. (A member list on the event would allocate one for every common drop as well.) The `GroundItem` snapshot gains `PartyId` and `WindowCloseTick`.
- **Opening:** an auction item leaves `Spawning` for `Auctioning` (not `Assigned`) on the first newer tick, and `WindowCloseTick` = that tick + `AUCTION_WINDOW_TICKS`. A bid during the spawn tick is rejected as "not auctioning".
- **Eligibility:** `IPartyService.GetPartyID(bidder) == the auction's party` (equivalent to scanning `GetPartyMembers`, without a list per bid).
- **Result code:** `LootBidResult` — `Accepted`, `NotAuctioning` (unknown item or wrong state), `WindowClosed`, `NotPartyMember`, `BelowFloor`, `NotHigherThanOwnBid`. Rejections are silent apart from the return value; only `WindowClosed` writes one info-level log line (AC-LT-17).
- **Tick comparison** is wraparound-safe (`StaleDiscardComparer`): a bid is in time when `receivedTick` is not newer than `WindowCloseTick`.
- **Until Story 011:** an auction whose window has closed stays `Auctioning` and despawns at its expiry tick. An auction is never picked up, warned about, or paused (Stories 007–009 act on `Assigned` items only).
- **The round-robin cursor is not advanced for an auctioned drop.**
- **Existing tests change on purpose:** the Story 006 tests that assert "a Rare drop in a party of 2+ takes the round-robin path" are rewritten for the auction.

**Added at code review (2026-10-02, user pre-approved "fix all"; no item changed a gameplay rule):**
- **A bid of 0 is never accepted:** the floor is `max(1, SellPriceGold)`. An item authored with a price of 0 would otherwise let a bid of 0 through, and Story 011 would count it as a valid bid.
- **A raise moves the bidder to the end of the auction's bid list,** so the list is in arrival order of each member's current bid. That is the last-resort order when two bids share an amount and a received tick.
- **`Dispose` drops every stored bid,** and a disposed service rejects every bid as `NotAuctioning`.
- A missing price (the auctioned item is not in the equipment cache) is a data fault: `NotAuctioning` plus one error log.
- The `SpawnAuction` result is not checked by the distributor: with a valid item and party it cannot fail. A drop is not retried through round-robin if it ever does (it logs an error).
- A `receivedTick` earlier than the auction's opening tick is accepted — only "not after the close tick" is checked; the tick is assigned by the server.

**For Story 011 (from the code review — none of it is built):**
- **Nothing detects the window closing.** `LootAuctionService` has no `Tick`, and an auction with no bids has no entry in its bid book. Add a tick (or an "auction window closed" event from `GroundItemService`), raised once when `IsTickExpired(currentTick, WindowCloseTick)`.
- **Expiry must resolve, not despawn:** `GroundItemService` step 4 still despawns an `Auctioning` item at its expiry tick.
- **State changes belong in `GroundItemService`:** it needs methods to move an auction item to `Claiming` for the winner and to `Assigned` for the fallback member, doing the per-assignment reset there (assignee, "was inside" flag, blocked flag, bag-full flag, pause budget).
- **Clear the bids on resolution:** a delivered item raises no despawn event, so its bids would stay in the book for the life of the zone.
- **Re-check each stored bidder's party membership at resolution:** a member who left keeps their stored bid (they can no longer raise it).
- **Compare bid ticks with `StaleDiscardComparer`, never `<`:** ticks wrap, and a stored tick can precede the opening tick.
- **Do not cast a bid to `int` for `TrySpendGold`;** a bid can be up to `uint.MaxValue` here.
- **AC-LT-17 logging after resolution begins:** once the state has left `Auctioning`, a late bid returns `NotAuctioning` with no log. Decide whether that rejection should still be logged.
- **Log volume:** every late bid writes an info line; consider a counter or rate limit.
- **Party System question:** `PartyID` values must never be reused within a zone session, or a different group could match a running auction's party.

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

**Status**: [x] Created — 32 test methods (32 NUnit cases), all 7 criteria covered

---

## Dependencies

- Depends on: Story 006 (rare-drop routing, `IPartyService`), Story 005 (ground items).
- Unlocks: Story 011.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 7/7 passing (0 deferred). "Broadcast to all party members" is verified as an event carrying the party ID — no wire message exists yet, and resolving the party to its members is the network layer's job; AC-LT-17's clause "the in-progress resolution continues uninterrupted" cannot be tested until Story 011 adds resolution
**Deviations**: Advisory only — `TR-loot-010` is not in the TR registry (same accepted gap as Stories 001–009); the bids live in a new `LootAuctionService` (user decision; the story's notes assumed the loot service); the events carry `PartyId` instead of a recipient list; rules not in the GDD: a bid of 0 is never accepted, a bid tick before the opening tick is accepted, a missing item price rejects the bid with an error; **interim behaviour until Story 011: an auction whose window has closed stays `Auctioning` and despawns at its expiry tick — in a running game that loses the item**; three Story 006 tests were rewritten because this story replaces the behaviour they asserted
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionBids_integration_tests.cs` (32 test methods, 32 NUnit cases — 18 from implementation, 14 from the code review; real `GroundItemService`, `LootEquipmentCache`, `LootDropDistributor` and `LootAuctionService`, stub party service). Full EditMode suite 1380/1380 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` returned CHANGES REQUIRED (a bid of 0 accepted when the item's price is 0; bid list order not arrival order after a raise; `Dispose` kept the bids; stale docs; the floor and the validation order not pinned by the tests); all applied; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 extended (auction rules not in the GDD; `PartyID` reuse question for the Party GDD; the interim "closed auction despawns" behaviour)
**Files outside the story's own list**: `GroundItem.cs`, `GroundItemSpawnedEventArgs.cs`, `GroundItemState.cs` (doc), `LootTable_RoundRobin_integration_tests.cs`
**Note for Story 011**: see "For Story 011" under the implementation notes — nothing closes the window yet, expiry must resolve an auction instead of despawning it, state changes and the per-assignment reset belong in `GroundItemService`, bids must be cleared on resolution. Story 011 is Blocked until currency-system.md has a `GoldTransactionReason` for the auction debit
