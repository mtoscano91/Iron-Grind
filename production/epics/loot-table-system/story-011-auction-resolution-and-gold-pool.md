# Story 011: Auction Resolution and Gold Pool

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

> **BLOCKED (decided 2026-10-02):** `ICurrencyService.TrySpendGold(charId, cost, reason)` requires a `GoldTransactionReason`, CR-LT-9 names none, and the enum has no auction value (`MonsterDrop=0 … CompensatingRefund=8, Other=255`). A new value must be added first so auction debits are distinguishable in audit logs — amend `design/gdd/currency-system.md` (Rule owning `GoldTransactionReason`), the wire enum in `design/gdd/networking-wire-protocol.md`, and `design/registry/entities.yaml`, then add the value in `src/Foundation/Currency/GoldTransactionReason.cs`. Use `/quick-design` or a short authoring session. Once the value exists, set this story to Ready.
>
> **Design done 2026-10-03:** the value is `GoldTransactionReason.AuctionBid = 9` (currency-system.md, loot-table-system.md CR-LT-9 / AC-LT-13, the wire enum, entities.yaml). The pool share stays `MonsterDrop`. **Still Blocked on code:** add `AuctionBid = 9` to `src/Foundation/Currency/GoldTransactionReason.cs`, move `WireEnumCodec.GoldTransactionReasonMaxNamedValue` to it (valid set `{0..9, 255}`), and add a round-trip test case for byte 9. Then set this story to Ready. In step 2 of the resolution below, `<new auction reason>` is `GoldTransactionReason.AuctionBid`.
>
> **Unblocked 2026-10-03:** `AuctionBid = 9` is in `GoldTransactionReason.cs`; `WireEnumCodec.GoldTransactionReasonMaxNamedValue` points at it (valid set `{0..9, 255}`); a byte-9 `TestCase` is added to `WireEnumCodec_DecodeGoldTransactionReason_ValidByte_PassesThroughUnchanged`. Not yet executed in the Unity Test Runner. The two notes above are history. Currency lean re-review of the amendment: APPROVED (2026-10-03).

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-9 (Auction Resolution), CR-LT-10 (Zero Bids), CR-LT-12 (auction closes at `expiryTick`), F-LT-2 (Auction Net Cost), Edge Cases (member disconnects during auction; member leaves before close)
**Requirement**: `TR-loot-010`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: The loot service calls `ICurrencyService`, `IInventoryService` and `IPartyService` directly (Tier 1) and announces the outcome with a Tier 2 event that the network layer sends as `AuctionResolved`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API.

**Control Manifest Rules (Core layer)**:
- Required: Tier 1 direct calls on injected interfaces; Tier 2 events with `readonly struct` args — ADR-010
- Forbidden: shared mutable state polling between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-12** [BLOCKING]: DarkSteel auction, party of 4: A bids 400g at tick 100, B bids 400g at tick 120, C bids 350g at tick 80, D bids nothing; at `windowCloseTick` A wins (earlier 400g bid); pickup `(A, itemID, 1)` is called once; `goldPerMember = floor(400 / 4) = 100`; all 4 members receive `AddGold(characterID, 100, GoldTransactionReason.MonsterDrop)` — 4 calls. If A's bag is full, CR-LT-13 applies to the item and the gold pool is still split among all 4.
- [ ] **AC-LT-13** [BLOCKING]: in that auction, with A holding enough gold, `TrySpendGold(A, 400)` is called exactly once and succeeds; `AddGold` is called exactly 4 times (100g each); A's balance delta is −300g; each other member's is +100g; the four deltas sum to 0. With A holding only 300g, `TrySpendGold(A, 400)` returns `InsufficientFunds`; resolution moves to B (next-highest valid bid, 400g at tick 120); `TrySpendGold(B, 400)` succeeds and B wins.
- [ ] **AC-LT-14** [BLOCKING]: a Steel item in `Auctioning` with zero valid bids at `windowCloseTick` → it is reclassified to the common path and assigned by round-robin at that tick; no `AddGold` is called; the auction-resolved event is raised with a zero-bid outcome.
- [ ] **AC-LT-16** [BLOCKING] *(auction half)*: a Steel item in `Auctioning` reaches `expiryTick` before `windowCloseTick` → the auction closes immediately at `expiryTick`; with ≥ 1 valid bid CR-LT-9 resolves normally; with zero valid bids CR-LT-10 applies. The item does not despawn silently while valid bids exist.
- [ ] **CR-LT-9 exhausted bidders**: if every bidder fails `TrySpendGold`, the auction falls to CR-LT-10 (round-robin, no gold changes hands).
- [ ] **Edge case — party size at close**: the pool split uses the party's size at close, not at kill time; a member who left before close gets no share.

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-9 / CR-LT-10.*

**Module conventions:** see Story 001.

**When resolution runs:** in `Tick`, for each `Auctioning` item, at `windowCloseTick` or at `expiryTick`, whichever comes first.

**Resolution:**
1. Order the stored bids by amount descending, then by `receivedTick` ascending (earlier wins a tie).
2. For each bid in order: `TrySpendGold(bidder, amount, GoldTransactionReason.AuctionBid)`.
   - `Success` → this bidder wins; go to step 3.
   - Any other result → disqualified; try the next bid.
3. Winner found: `N` = current member count of the party; `goldPerMember = winnerBid / N` (integer division = `floor`); remainder discarded. For every current member, including the winner, `AddGold(member, goldPerMember, GoldTransactionReason.MonsterDrop)` — skip the call if `goldPerMember` is 0. Then call `Pickup(winner, itemId, 1)`: success → `Inventory`; bag full → the item becomes `Assigned` to the winner and Stories 007–009 apply. The gold split happens either way.
4. No winner (zero bids, or every bidder failed): assign by round-robin (Story 006 path), state `Assigned`, raise `OnGroundItemAssigned`; no gold moves.
5. Raise `OnAuctionResolved` — `groundItemId`, `winnerCharacterId`, `goldPerMember`, `isRoundRobinFallback` (payload of the `AuctionResolved` wire schema; winner is `CharacterID` invalid and `goldPerMember` 0 on fallback).

**Disconnected members** keep their bids and can win; `Pickup` and `AddGold` run against their server-side records regardless of connection state (Edge Cases).

**Settled at `/story-readiness` 2026-10-03 (user decisions, now in the GDD — CR-LT-9, CR-LT-12, Edge Cases):**
- A bid placed by a character who has **left the party** before close is skipped at resolution — filter the stored bids against current membership before step 1.
- Any `TrySpendGold` result other than `Success` (`InsufficientFunds`, `CharacterNotFound`, `ConcurrencyConflict`) disqualifies that bidder; for the two unexpected errors also log a server error. Resolution moves to the next bid.
- When the auction closes at `expiryTick` and the item becomes `Assigned` (round-robin fallback, or a winner whose bag is full), set `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS` so the assignee has a full pickup window. *(The bag-full-winner half was the assistant's extension of the fallback decision; confirmed by the user at `/story-done` 2026-10-03.)*

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 010**: opening the auction and accepting bids.
- **Story 012**: resolving auctions at zone teardown.
- The `AuctionResolved` wire codec and the auction UI.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionResolution_integration_tests.cs` (real `CurrencySystem` with registered balances and a call-recording wrapper; real or fake `IInventoryService`; stub `IPartyService`)

- **AC-LT-12**: winner, tie-break, pool
  - Given: party `[A, B, C, D]`, each with 1,000g; DarkSteel auction; bids A 400 @100, B 400 @120, C 350 @80
  - When: `Tick` reaches `windowCloseTick`
  - Then: winner A; `Pickup(A, item, 1)` called once; 4 `AddGold(_, 100, MonsterDrop)` calls; `OnAuctionResolved(item, A, 100, false)`
  - Edge cases: A's bag full → item `Assigned` to A, the 4 `AddGold` calls still happen

- **AC-LT-13**: gold neutrality and disqualification
  - Given: the same auction
  - Then: one `TrySpendGold(A, 400, …)`; balances A 700, B 1,100, C 1,100, D 1,100 (deltas sum to 0)
  - Edge cases: A starts with 300g → `TrySpendGold(A, 400)` fails, then `TrySpendGold(B, 400)` succeeds; winner B; A's balance is 300 + 100

- **AC-LT-14**: zero bids
  - Given: a Steel auction in party `[A, B]`, cursor 0, no bids
  - When: `Tick` reaches `windowCloseTick`
  - Then: item `Assigned` to A; cursor advanced; no `AddGold`, no `TrySpendGold`; `OnAuctionResolved(item, invalid, 0, true)`; `OnGroundItemAssigned(item, A)`
  - Edge cases: none

- **AC-LT-16 (auction half)**: expiry before window close
  - Given: an auction whose `expiryTick` is earlier than its `windowCloseTick`, with one valid bid
  - When: `Tick` reaches `expiryTick`
  - Then: resolution runs at that tick as in AC-LT-12; no `OnGroundItemDespawned` before `OnAuctionResolved`
  - Edge cases: the same with zero bids → `OnAuctionResolved(…, true)`

- **Exhausted bidders**
  - Given: bids A 400 and B 350; A has 100g, B has 100g
  - Then: two failed `TrySpendGold` calls; round-robin fallback; no `AddGold`
  - Edge cases: none

- **Party size at close**
  - Given: party of 4 at kill time; D leaves before close; A wins with 400
  - Then: N = 3; 3 `AddGold(_, 133, MonsterDrop)` calls; D receives nothing
  - Edge cases: D had the highest bid before leaving → D's bid is skipped, no `TrySpendGold(D, …)`; the next-highest member wins

- **Unexpected spend error** *(added at readiness 2026-10-03)*
  - Given: bids A 400 and B 350; `TrySpendGold(A, …)` returns `CharacterNotFound` (A not registered in the currency service)
  - Then: A is disqualified, a server error is logged, B wins with 350
  - Edge cases: none

- **Fresh TTL on an `expiryTick` close** *(added at readiness 2026-10-03)*
  - Given: an auction whose `expiryTick` is earlier than its `windowCloseTick`, zero bids
  - When: `Tick` reaches `expiryTick`
  - Then: the item is `Assigned` with `expiryTick = that tick + GROUND_ITEM_TTL_TICKS`; no `OnGroundItemDespawned` at that tick
  - Edge cases: one valid bid, winner's bag full → item `Assigned` to the winner with the same fresh `expiryTick`

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionResolution_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 23 tests, all passing (Unity 6000.3.10f1 batch-mode EditMode run, 2026-10-03: 1405/1405)

---

## Dependencies

- Depends on: Story 010 (auction state and bids), Story 007 (drop fate for a winner with a full bag), Story 006 (round-robin fallback). All Complete. The `GoldTransactionReason.AuctionBid` value it was blocked on is in code (2026-10-03).
- Unlocks: Story 012.

---

## Completion Notes
**Completed**: 2026-10-03
**Criteria**: 6/6 passing (AC-LT-12, AC-LT-13, AC-LT-14, AC-LT-16 auction half, exhausted bidders, party size at close) — each covered by a passing test in `LootTable_AuctionResolution_integration_tests.cs`.
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionResolution_integration_tests.cs`, 23 tests. Real Unity Test Runner run (6000.3.10f1, batch mode, EditMode): 1405/1405 passed, including the edited Story 010 file and the byte-9 `AuctionBid` `TestCase`.
**Code Review**: Complete — `/code-review` twice on 2026-10-03 (unity-specialist + qa-tester each time): CHANGES REQUIRED → fixed → APPROVED WITH SUGGESTIONS → suggestions applied. QL-TEST-COVERAGE and LP-CODE-REVIEW gates skipped (lean mode).
**Implementation**: resolution runs in `LootAuctionService.Tick` (new `ICurrencyService` dependency, `OnAuctionResolved`); `GroundItemService` no longer despawns an `Auctioning` item and gains `AssignAuctionItem`, `AwardAuctionItem`, `DespawnAuctionItem` and `OnGroundItemAssigned`; new `AuctionResolvedEventArgs`, `GroundItemAssignedEventArgs`.
**Deviations** (advisory, none blocking):
- Additions beyond the story text: `PartyId` on `AuctionResolvedEventArgs` (routing); `IGroundItemService.DespawnAuctionItem`; a fallback with no member at the round-robin cursor, or a failure before assignment, removes the item and still raises `OnAuctionResolved` as a fallback; a failure after the winner paid delivers the item to the winner.
- An `Auctioning` item is cleaned up only by `LootAuctionService.Tick`, and the service must exist before any auction spawns. Story 012 must flush `Auctioning` items at teardown (`DespawnAuctionItem` is available).
- No refund when a paying winner gets no item — TD-051. Lost pool share of a party member not registered in the currency service — TD-052. Untested paths — TD-053.
- The bag-full winner's fresh TTL on an `expiryTick` close: confirmed by the user 2026-10-03.
- `TR-loot-010` is still a placeholder (`tr-registry.yaml` is empty, TD-014 pattern). `LootTable_AuctionBids_integration_tests.cs` (Story 010) was edited: 4-argument constructor, two expiry tests rewritten, one test added, one renamed.
