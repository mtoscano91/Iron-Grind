# Story 011: `PendingPurchase` Lifecycle on the Store Seam

> **Epic**: Character Persistence
> **Status**: Deferred — `/story-readiness` 2026-10-15: NEEDS WORK (3 open decisions, provisional estimate, no performance note). Deferred until the NPC Shop epic, its only caller. Recommended answer to decision 1: in-memory Currency is the authority during a session; the store's `BeginPurchase` / `RefundPurchase` also write the stored balance in the same transaction but do not advance `save_version`, and the caller mirrors them with `TrySpendGold` / `AddGold(CompensatingRefund)` in memory, so the next `SaveSession` writes the same value — needs an ADR-001 amendment (it orders "record, then a separate debit").
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-12 (storage and lifecycle); CR-CP-1 (the four purchase methods, `PendingPurchaseResult`, `PendingPurchaseRecord`). The GDD has no acceptance criterion for CR-CP-12: the criteria below are written from the rule's text. `design/gdd/networking-session.md` — CR-NET-6.4 step 2 (reconnect reconciliation).
**Requirement**: `TR-cp-012`

**ADR Governing Implementation**: ADR-001 (Purchase Transaction Integrity) Decisions 2–4; ADR-006 Decision 3 (a separate `pending_purchases` table; the INSERT and the gold-debit UPDATE in one transaction).
**ADR Decision Summary**: a purchase debits gold and records a pending purchase atomically; the record is completed on a successful item grant or refunded otherwise, and reconciled on reconnect.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: PostgreSQL is the sole persistence store — ADR-006 (the in-memory store of this story is a test and no-database stand-in, not a second store)
- Required: `ConfigureAwait(false)` on every `await` in the persistence layer — ADR-011

---

## Acceptance Criteria

- [ ] **`BeginPurchase` — success**: with a stored gold balance ≥ `TotalCost`, one record is created in state `GoldDebited` with a store-generated id, the stored gold balance is lowered by `TotalCost`, and `Success` is returned. Both happen or neither.
- [ ] **`BeginPurchase` — insufficient gold**: with a balance below `TotalCost`, `InsufficientGold` is returned, no record exists and the balance is unchanged. A balance exactly equal to the cost succeeds.
- [ ] **`BeginPurchase` — atomicity**: a store failure between the two operations leaves neither applied (shown with the in-memory store's fault injection) and returns `DatabaseError`.
- [ ] **`CompletePurchase`**: removes the record and returns `Success`; no gold changes.
- [ ] **`RefundPurchase`**: credits `refundAmount` to the character's stored gold, removes the record and returns `Success`.
- [ ] **Idempotent on a missing id**: `CompletePurchase` and `RefundPurchase` for an id that does not exist return `Success` and change nothing.
- [ ] **`LoadOutstandingPurchases`**: returns every record of the character in state `GoldDebited`, and none of another character; an empty list, not null, when there is none.
- [ ] **Outside the character record**: `LoadCharacter`, `SaveSession` and `SaveIrreversibleOutcome` neither read nor write pending purchases; a pending record survives a `SaveSession`.
- [ ] **Store seam**: the purchase operations are on the store seam of Story 002 (or a sibling interface), so Story 015 can implement them in SQL.

---

## Implementation Notes

- CR-CP-12 says `RefundPurchase` "calls `AddGold(charId, totalCost, CompensatingRefund)` and deletes the record", and also that it "credits the gold, marks the record `Refunded`, and deletes it". Gold lives in two places at that moment — the Currency System's memory and the stored record: see open decision 1.
- `BeginPurchase` debits `character_records.gold_balance` in the database directly. The in-memory balance of the Currency System and its `GoldVersion` must agree with it afterwards: open decision 1 again.

**Open decisions for `/story-readiness`**

1. **In-memory gold against stored gold.** Who debits and credits the Currency System's in-memory balance around `BeginPurchase` and `RefundPurchase`, in what order with the store write, and what happens to `gold_version` and to the record's `save_version` when the purchase transaction updates the row (a later `SaveSession` carries an expected `SaveVersion` — does a purchase advance it).
2. **Tick code and these methods.** They return tasks, so ADR-011 applies: the NPC Shop's handler tracks them. Whether a purchase closes the mutation gate is ADR-001's and the shop's question.
3. **Write this story now or with the NPC Shop.** With no caller, every criterion here is tested against the in-memory store alone.

---

## Out of Scope

- The NPC Shop's `PurchaseRequest` / `PickupRequest` handlers (ADR-001)
- Story 015: the SQL and the shared `NpgsqlTransaction`
- A `PendingSell` record (ADR-001 OQ-ADR1-2)

---

## QA Test Cases

- **Begin** — Balance 500, cost 200 → `Success`, balance 300, one `GoldDebited` record. Edge: cost 500 → balance 0; cost 501 → `InsufficientGold`, balance 500, no record.
- **Atomicity** — Fault injected after the insert → no record, balance unchanged, `DatabaseError`.
- **Complete** — Then the record is gone, balance unchanged; again with the same id → `Success`.
- **Refund** — Refund 200 → balance back to 500, record gone; again → `Success`, balance still 500.
- **Outstanding** — Two records for A, one for B → A's list has two.
- **Independence** — A pending record, then `SaveSession` → the record is still outstanding.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_PendingPurchase_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002
- Unlocks: Story 015; the NPC Shop epic
