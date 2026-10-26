# Story 015: The Npgsql Pending-Purchase Store

> **Epic**: Character Persistence
> **Status**: Blocked — written 2026-10-12. Stories 012–014 Complete (2026-10-19); Story 011 decision 1 resolved 2026-10-26 (ADR-001 Amendment A2). Now blocked only on Story 011's implementation: it implements `IPendingPurchaseStore` (new), with the A2 signatures — `BeginPurchase(charId, record, goldBalance, goldVersion)` returning `PendingPurchaseBeginResult`, `RefundPurchase(purchaseId, charId, goldBalance, goldVersion)` — and the gold UPDATE writes absolute `gold_balance` / `gold_version`, no `save_version` bump. Re-run `/story-readiness` after Story 011.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-12.
**Requirement**: `TR-cp-012`

**ADR Governing Implementation**: ADR-006 Decision 3 (the `pending_purchases` INSERT and the gold-debit UPDATE inside the same `NpgsqlTransaction`; the DDL); ADR-001 Decisions 2–4.
**ADR Decision Summary**: `BeginPurchase` is the only write that shares a transaction with another system's data; if either statement fails, both roll back. Since ADR-001 Amendment A2 (2026-10-26) the gold UPDATE writes the caller's in-memory balance and version as absolute values and has no balance check.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: as Story 014 — read the pinned Npgsql and Dapper API, and Story 012's findings.

**Control Manifest Rules (this layer)**:
- Required: `ConfigureAwait(false)` on every `await`, including `await using` — ADR-011
- Forbidden: Entity Framework Core — ADR-006

---

## Acceptance Criteria

*Against a real PostgreSQL instance, as Story 014.*

- [ ] **Shared suite**: Story 011's purchase tests pass against this store unchanged.
- [ ] **One transaction**: `BeginPurchase` issues the INSERT and the gold UPDATE in one `NpgsqlTransaction`; a failure of the second statement leaves no row in `pending_purchases` (shown by forcing the UPDATE to fail).
- [ ] **Insufficient gold is decided in the database**: the debit's predicate refuses a balance below the cost, so two concurrent `BeginPurchase` calls for one character whose balance covers only one of them end with one `Success` and one `InsufficientGold`, and the balance is never negative.
- [ ] **Generated id**: the `BIGSERIAL` id is returned to the caller.
- [ ] **`CompletePurchase` / `RefundPurchase`**: remove the row; idempotent on a missing id; the refund's credit and the delete are one transaction.
- [ ] **`LoadOutstandingPurchases`**: uses `ix_pending_purchases_char_id`; returns only `state = GoldDebited` rows of that character.
- [ ] **Foreign key**: a purchase for a `char_id` with no character record fails and is mapped to a result, not an unhandled exception.

---

## Implementation Notes

- CR-CP-12 says the completed or refunded record is marked and deleted; with the row deleted the mark is never observable. Keep to the delete unless readiness wants an audit trail.
- The debit touches `character_records.gold_balance`: see Story 011's open decision 1 for `gold_version` and `save_version`.

**Open decisions for `/story-readiness`**

1. **Does the purchase UPDATE advance `save_version`.** If it does, the in-memory expected version must follow; if it does not, a `SaveSession` snapshot taken before the purchase overwrites the debited balance.

---

## Out of Scope

- The NPC Shop's handlers
- Reconnect reconciliation's caller (Networking Core session contract)

---

## QA Test Cases

- **Shared suite** — Story 011's tests against PostgreSQL.
- **Rollback** — Given a forced failure of the gold UPDATE, Then `pending_purchases` has no new row.
- **Race** — Given balance 300 and two concurrent purchases of 200, Then one `Success`, one `InsufficientGold`, balance 100.
- **Foreign key** — Given an unknown `char_id`, Then a non-success result.
- **Idempotency** — Complete twice, refund twice → `Success` each time, one effect.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: the database test file named at readiness — must exist and pass against a PostgreSQL instance.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Stories 012, 013, 014 (blocking)**; Story 011
- Unlocks: Story 016; the NPC Shop epic
