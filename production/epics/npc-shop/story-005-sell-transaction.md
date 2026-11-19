# Story 005: Sell Transaction

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-7, CR-SHOP-8 (server half), CR-SHOP-9, F-NS-2, Edge Cases (Gold balance, Sell-back, Stale UI)
**Requirement**: `TR-shop-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001: Purchase Transaction Integrity (note 2026-11-19: no `PendingSell`); ADR-014
**ADR Decision Summary**: A sell changes only in-memory bag and gold, saved together by `SaveSession`; no store write of its own. Handled on the tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: server-authoritative validation in GDD order; dedup + rate limit (Story 004)
- Forbidden: `AddGold(0)`

---

## Acceptance Criteria

- [ ] **AC-1 Order**: checks run in this order and stop at the first failure, each answered with its rejection carrying `requestId`, gold and bag unchanged: no session → `RejectedNoNPCSession` (AC-NS-20); slot outside 0–19 → `RejectedInvalidSlot` (AC-NS-21); slot does not hold `itemId` → `RejectedItemMismatch` (AC-NS-22); slot locked → `RejectedSlotLocked` (AC-NS-23); `GetItem(itemId)` null or `SellPriceGold = 0` → `RejectedUnsellable` (null also logs charId + id) (AC-NS-24); `quantity < 1` or `> stack` → `RejectedInvalidQuantity`.
- [ ] **AC-2 Success**: `SellItem(charId, slot, itemId, quantity)` then `AddGold(charId, SellPriceGold × quantitySold, ItemSell)`; `SellResult(requestId, itemId, quantitySold, goldEarned, newGoldBalance)`; the slot decrements by `quantity` (AC-NS-17). Vectors: 1× HP Small → 2 g; 10× HP Large → 180 g; 1× MP Medium → 6 g (AC-NS-18).
- [ ] **AC-3 Stack**: a 10-stack of HP Large sold with quantity 10 → slot empty (AC-NS-19); with quantity 3 → 7 left, 54 g (AC-NS-19b).
- [ ] **AC-4 Enhanced gear**: a +7 Iron Sword sells for its base `SellPriceGold` (30 g) (AC-NS-25).
- [ ] **AC-5 Gold cap**: balance at `GOLD_CAP` → `goldEarned = 0`, `SellResult` still sent with 0; balance `GOLD_CAP − 5` selling 18 g → `goldEarned = 5` — the amount actually credited, not the F-NS-2 product.
- [ ] **AC-6 Corruption guard**: `SellItem` returning `quantitySold = 0` → one `LogError`, no `AddGold`, no `SellResult`.
- [ ] **AC-7 GoldSyncEvent**: a successful sell queues a `GoldSyncEvent` with `NewBalance = prior + goldEarned` (AC-NS-33).
- [ ] **AC-8 Dedup**: the handler runs through Story 004's cache: a repeated `SellRequest` with the same `requestId` returns the cached `SellResult` and sells nothing more.

---

## Implementation Notes

- `NpcShopService.HandleSell` in `IronGrind.NpcShop` over seams: `INpcInteractionSessions`, `IInventoryService` (`GetSlot`, `IsSlotLocked`, `SellItem`), item lookup, `ICurrencyService.AddGold`, outbox. Charge nothing to the store (D2).
- Find how `AddGold` reports the credited amount when clamped (`GoldMutationResult`); if it does not, compute it from the balance before and after.
- `GoldSyncEvent` is emitted by the Currency System on `AddGold`; the AC asserts it is raised/queued with the right balance.

---

## Out of Scope

- Story 006: buy; Story 009: dispatcher registration

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: one test per rejection, each built so every earlier check passes; assert the message type, echoed `requestId`, unchanged slot and balance. Null item test also expects one log.
- **AC-2**: the three vectors → `goldEarned`, balance, slot counts.
- **AC-3**: quantity 10 → slot empty; quantity 3 → 7 left, 54 g.
- **AC-4**: item instance with enhancement level 7 → 30 g.
- **AC-5**: at cap → 0 and result sent; cap − 5 → 5.
- **AC-6**: fake inventory returning 0 → error log, balance unchanged, no result.
- **AC-7**: subscribe to the gold event → one event with the new balance.
- **AC-8**: same request twice → one sale, two identical results.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Sell_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 004
- Unlocks: Stories 009, 010
