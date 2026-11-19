# Story 007: Buy Failure and Refund Paths

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-5 steps 9d–9e, Edge Cases (Inventory full / PickupRequest failure, Session flag / zone change during transaction)
**Requirement**: `TR-shop-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001 Decision 3, Amendment A2 + addendum (store-first refund, `OutcomeUnknown`, idempotent `BeginPurchase`)
**ADR Decision Summary**: `BeginPurchase` failure → in-memory refund only. Grant failure → `RefundPurchase` (store first, cap-safe balance + total, version + 1) then `AddGold(CompensatingRefund)` only on success. `OutcomeUnknown` → retry the same call, touch nothing. A failed refund keeps the record for reconnect and logs a critical alert.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: refund store first (ADR-001 A2); on `OutcomeUnknown` no compensation and no memory change, retry same arguments (A2 addendum 2)
- Forbidden: crediting memory before `RefundPurchase` succeeds

---

## Acceptance Criteria

- [ ] **AC-1 Inventory full**: 20 slots full with no stack room → after the debit and `BeginPurchase`, `PickupRequest` fails → `RefundPurchase(purchaseId, charId, balance + total (cap-safe), version + 1)` → on `Success` `AddGold(total, CompensatingRefund)` → `RejectedInventoryFull`; final balance = prior, bag unchanged, record gone (AC-NS-15).
- [ ] **AC-2 Begin failure**: `BeginPurchase` → `DatabaseError` → `AddGold(total, CompensatingRefund)` in memory, nothing durable, rejection sent (which one — `RejectedInventoryFull` or another — decide at readiness; the GDD has no dedicated code), balance = prior.
- [ ] **AC-3 OutcomeUnknown**: `BeginPurchase` or `RefundPurchase` → `OutcomeUnknown` → the same call is retried with the same arguments (same `requestId` / `purchaseId`, same gold values) on a later tick, with no memory change in between, until a definite result; a bounded retry count with a critical log when exhausted (record the bound at readiness).
- [ ] **AC-4 Refund failure**: `RefundPurchase` → `DatabaseError` → no `AddGold`, one critical log with charId, totalCost and the error, record kept (reconnect refunds it, Story 008), `RejectedInventoryFull` sent.
- [ ] **AC-5 Zone change mid-buy**: the session closes (zone change or TTL) after the debit and before the grant → the buy still completes or refunds by the rules above; final balance = prior when the grant fails (AC-NS-36). A session close before the request is handled → `RejectedNoNPCSession`, no debit.
- [ ] **AC-6 Partial pickup**: pickup is atomic; a quantity that only partly fits takes the AC-1 path (no partial grant).

---

## Implementation Notes

- Extend Story 006's per-call state object with the failure branches; no shared mutable state between characters.
- Zone change mid-buy: the session flag is read only at step a; the async continuation must not re-check it (refund obligation survives session closure).
- Test seam: a fake `ICharacterPersistence` whose purchase methods return scripted results per call (`DatabaseError`, `OutcomeUnknown` then `Success`), plus the real in-memory store for AC-1.

---

## Out of Scope

- Story 008: reconnect refunds of records left by AC-4

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: full bag → call order on the fakes: TrySpendGold, BeginPurchase, Pickup, RefundPurchase, AddGold; balance and bag unchanged; record gone.
- **AC-2**: Begin → DatabaseError → AddGold once, no Refund call, balance = prior.
- **AC-3**: Begin → OutcomeUnknown, OutcomeUnknown, Success → three calls with identical arguments, no currency call between them, then the happy path. Same for Refund. Exhausted → critical log.
- **AC-4**: Refund → DatabaseError → AddGold count 0, record present, one critical log, `RejectedInventoryFull`.
- **AC-5**: debit, then `NotifyZoneTransition`, then failing pickup → balance = prior; close before handling → `RejectedNoNPCSession`, no debit.
- **AC-6**: bag with room for 3 of 5 → refund path, bag unchanged.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_BuyFailure_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006
- Unlocks: Stories 008, 010
