# Story 011: Auction Resolution and Gold Pool

> **Epic**: Loot Table System
> **Status**: Blocked
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

> **BLOCKED (decided 2026-10-02):** `ICurrencyService.TrySpendGold(charId, cost, reason)` requires a `GoldTransactionReason`, CR-LT-9 names none, and the enum has no auction value (`MonsterDrop=0 … CompensatingRefund=8, Other=255`). A new value must be added first so auction debits are distinguishable in audit logs — amend `design/gdd/currency-system.md` (Rule owning `GoldTransactionReason`), the wire enum in `design/gdd/networking-wire-protocol.md`, and `design/registry/entities.yaml`, then add the value in `src/Foundation/Currency/GoldTransactionReason.cs`. Use `/quick-design` or a short authoring session. Once the value exists, set this story to Ready.

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
2. For each bid in order: `TrySpendGold(bidder, amount, <new auction reason>)`.
   - `Success` → this bidder wins; go to step 3.
   - `InsufficientFunds` → disqualified; try the next bid.
3. Winner found: `N` = current member count of the party; `goldPerMember = winnerBid / N` (integer division = `floor`); remainder discarded. For every current member, including the winner, `AddGold(member, goldPerMember, GoldTransactionReason.MonsterDrop)` — skip the call if `goldPerMember` is 0. Then call `Pickup(winner, itemId, 1)`: success → `Inventory`; bag full → the item becomes `Assigned` to the winner and Stories 007–009 apply. The gold split happens either way.
4. No winner (zero bids, or every bidder failed): assign by round-robin (Story 006 path), state `Assigned`, raise `OnGroundItemAssigned`; no gold moves.
5. Raise `OnAuctionResolved` — `groundItemId`, `winnerCharacterId`, `goldPerMember`, `isRoundRobinFallback` (payload of the `AuctionResolved` wire schema; winner is `CharacterID` invalid and `goldPerMember` 0 on fallback).

**Disconnected members** keep their bids and can win; `Pickup` and `AddGold` run against their server-side records regardless of connection state (Edge Cases).

**Points the GDD leaves open — settle at `/story-readiness`, do not guess:**
- A bid placed by a member who has **left the party** before close: the Edge Cases say such a member "is not eligible to bid and receives no pool share" but not whether a bid already placed is dropped. The natural reading is to exclude it at resolution.
- `TrySpendGold` errors other than `InsufficientFunds` (`CharacterNotFound`, `ConcurrencyConflict`): the GDD names only `InsufficientFunds`. Treating any non-success as disqualification, with a server error logged, is the conservative reading.
- When the auction closes at `expiryTick` and falls back to round-robin, the item's TTL has already run out. The GDD says CR-LT-10 "applies" but not what lifetime the reassigned item has.

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
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionResolution_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 010 (auction state and bids), Story 007 (drop fate for a winner with a full bag), Story 006 (round-robin fallback). **Blocked on** a new `GoldTransactionReason` value (see the note at the top).
- Unlocks: Story 012.
