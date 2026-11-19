# Story 006: Buy Transaction — Happy Path and Rejections

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-2, CR-SHOP-5, F-NS-1, Edge Cases (Quantity boundaries, Gold balance)
**Requirement**: `TR-shop-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001: Purchase Transaction Integrity (Amendment A2 + addendum); ADR-006; ADR-011
**ADR Decision Summary**: `TrySpendGold` in memory is the funds gate; `BeginPurchase(charId, record, goldBalance, goldVersion)` makes the debit durable together with the record; the grant follows; `CompletePurchase` deletes the record. Store calls are async through the per-character write queue; the tick never awaits.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: buy order (a) `TrySpendGold` → (b) `BeginPurchase` with the in-memory balance and version after (a) → grant → `CompletePurchase` (ADR-001 A2); tick thread never blocks on persistence (ADR-011)
- Forbidden: calling the store before `TrySpendGold`; awaiting on the tick

---

## Acceptance Criteria

- [ ] **AC-1 Rejections before gold**: in order, no gold call for any of them: no session → `RejectedNoNPCSession` (AC-NS-09); quantity 0 or > 99 → `RejectedInvalidQuantity` (AC-NS-10, AC-NS-11; AC-NS-37 negative — see note); item not in catalog (e.g. Iron Sword) → `RejectedItemNotInCatalog` (AC-NS-12, AC-NS-31). Gold, bag and store unchanged.
- [ ] **AC-2 Funds**: balance 0 buying 1 HP Small → `RejectedInsufficientFunds`, balance 0 (AC-NS-13); balance `price × qty − 1` → `RejectedInsufficientFunds`, unchanged (AC-NS-14, separate test). The store is not called.
- [ ] **AC-3 Happy path**: open session, enough gold, bag room → `TrySpendGold(F-NS-1 total, ScrollPurchase | ItemPurchase from the catalog entry)` → `BeginPurchase` with record `(charId, requestId, itemId, quantity, totalCost, GoldDebited)` and the in-memory balance / version after the debit → on `Success`, `PickupRequest(charId, itemId, quantity)` → `CompletePurchase(purchaseId, charId)` → `BuyResult(requestId, itemId, quantity, newGoldBalance)`; bag holds `quantity` more; record gone (AC-NS-07).
- [ ] **AC-4 F-NS-1 vectors**: 1× Bronze Scroll → 90; 99× HP Small → 297; 3× Dark Steel → 1,050; 1× HP Large → 30 (AC-NS-08).
- [ ] **AC-5 Async**: `BeginPurchase` completes on a later tick through the tick completion queue; until then no `BuyResult` is sent and a duplicate request is held by Story 004's in-flight state. A `CompletePurchase` failure after a successful grant logs an error and keeps `BuyResult` (the item was delivered; the outstanding record is refunded wrongly at next reconnect — record the decision at readiness).
- [ ] **AC-6 Infinite stock and replace**: after 5 Bronze Scrolls, a 6th buy succeeds (AC-NS-34); after the Story 002 replace test (Enhancement npc → shop npc), a buy succeeds (AC-NS-03 (2)).
- [ ] **AC-7 GoldSyncEvent**: a successful buy queues a `GoldSyncEvent` with `NewBalance = prior − totalCost` (AC-NS-32).

---

## Implementation Notes

- `NpcShopService.HandleBuy` over: catalog (Story 001), `INpcInteractionSessions`, `ICurrencyService` (`TrySpendGold`, `TryGetState` for balance / version), `ICharacterPersistence` (`BeginPurchase`, `CompletePurchase`), `IInventoryService.Pickup…`, `ITickCompletionQueue`, outbox, Story 004 cache. Pattern: `EnhancementCommitOrchestrator` (async write, per-call state object, completion on the tick).
- `BuyRequest.quantity` is a byte on the wire (Story 003): a negative value cannot be sent; AC-NS-37 is met by the type plus the `< 1` check — record it at readiness.
- Gold version for `BeginPurchase`: the Currency in-memory version after `TrySpendGold` (`TryGetState`).

---

## Out of Scope

- Story 007: `BeginPurchase` failure, `OutcomeUnknown`, inventory-full refund, zone change mid-buy
- Story 008: reconnect refunds

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: each rejection with a recording currency fake → zero `TrySpendGold` calls, zero store calls.
- **AC-2**: two separate tests; store call count 0.
- **AC-3**: in-memory store + completion queue pumped → record written with the right fields and post-debit balance / version; after pump → bag +qty, record deleted, `BuyResult` fields.
- **AC-4**: four vectors → debit amounts.
- **AC-5**: before pumping → no result; duplicate → no reply; after pumping → one result. `CompletePurchase` fake returning `DatabaseError` → result sent + one error log.
- **AC-6**: 6 buys → all succeed.
- **AC-7**: one gold event with the new balance.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Buy_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 003, 004
- Unlocks: Stories 007, 009, 010
