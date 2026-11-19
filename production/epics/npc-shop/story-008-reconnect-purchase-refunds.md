# Story 008: Reconnect Purchase Refunds

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/networking-session.md` CR-NET-6.4 step 2; `character-persistence.md` CR-CP-12; `npc-shop.md` CR-SHOP-5 note
**Requirement**: `TR-shop-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001 Decision 4, Amendment A2; ADR-011
**ADR Decision Summary**: After the session is accepted and before any gameplay message runs, every `GoldDebited` record of the character is refunded once — store first (`RefundPurchase`), then `AddGold(CompensatingRefund)` — and logged.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A.

**Control Manifest Rules (this layer)**:
- Required: refund store first (A2); reconciliation before gameplay messages (Decision 4); tick never awaits (ADR-011)
- Forbidden: memory credit before the store write

---

## Acceptance Criteria

- [ ] **AC-1 Fresh login**: a character with outstanding records entering the zone (load path) gets each refunded before the zone accepts its gameplay requests: `RefundPurchase(id, charId, cap-safe balance + total, version + 1)` then `AddGold(total, CompensatingRefund)`; one log line per refund (charId, itemId, quantity, totalCost, requestId); balance in the session handshake / first `GoldSyncEvent` includes the refunds.
- [ ] **AC-2 Re-auth**: the same on the re-auth path (`CompleteReAuthSuccess`), replacing the `QueryNoPurchases` / `MarkNoPurchase` stand-ins in `ZoneInstancingWiring`.
- [ ] **AC-3 Order fixed**: the existing loop in `ConnectionStateMachine.CompleteReAuthSuccess` credits memory before marking the record; it is changed to store first — a `RefundPurchase` failure leaves the record and memory unchanged (one critical log), so a later reconnect refunds it exactly once.
- [ ] **AC-4 Once**: two reconnects in a row refund a record once (the second finds nothing); n > 1 outstanding records → all refunded and one warning (ADR-001 Performance note).
- [ ] **AC-5 Async without blocking**: the load and refund writes are async; gameplay requests for the character that arrive before reconciliation finishes are not processed (decide at readiness: held, or rejected — the existing admission gate may already cover it).
- [ ] **AC-6 OutcomeUnknown**: a refund returning `OutcomeUnknown` is retried with the same arguments; memory untouched until `Success`.

---

## Implementation Notes

- Read first: `ConnectionStateMachine.CompleteReAuthSuccess` (reconcile loop at ~line 1050, synchronous delegates `queryGoldDebitedPurchases` / `markPurchaseRefunded`), `ZoneInstancingWiring.cs:351` (stand-ins), `ZoneEntryAdmission` (`LoadCharacter`), `CharacterPersistenceService.LoadOutstandingPurchases` (async).
- The re-auth loop is synchronous today; the store calls are async. Likely shape: an async reconciliation step that runs after load / re-auth, then releases the character for gameplay — the same "hold until done" idea as the load. Settle the shape at readiness; it may need an ADR-011 note.
- Shared with Consumable Use later (ADR-001 affected systems) — keep the reconciler shop-agnostic (`PendingPurchaseReconciler`).

---

## Out of Scope

- Story 009: composing the reconciler in the zone server (this story may wire it if the re-auth stand-in replacement needs it — agree at readiness)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: in-memory store with one record (total 90); load → after pumping, store record gone, balance +90, one log; a Buy queued before completion is not processed early.
- **AC-2**: re-auth with one record → same result; stand-in removed (grep).
- **AC-3**: Refund fake → DatabaseError → balance unchanged, record kept, one critical log; next re-auth with Success → refunded once.
- **AC-4**: two re-auths → one refund; three records → three refunds + one warning.
- **AC-5**: per readiness decision.
- **AC-6**: OutcomeUnknown, Success → two identical calls, one credit.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Reconciliation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007 (refund helper)
- Unlocks: Stories 009, 010
