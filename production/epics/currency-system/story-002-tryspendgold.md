# Story 002: TrySpendGold (Spend Guard)

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 1–2 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs this method. Same in-memory scope note as Story 001.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: Pure C# logic, no engine API surface.

**Control Manifest Rules (Foundation layer)**:
- Required: `TrySpendGold` must be atomic from the caller's perspective — either the full amount is debited or the balance is left completely unchanged. No partial spends (GDD Rule 5).
- Guardrail: `TrySpendGold` has no rollback mechanism (GDD Rule 6) — a caller that grants a benefit after a successful spend and then needs to undo it is responsible for calling `AddGold` itself. Do not add an "undo" API to `CurrencySystem`.

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-A-02** [BLOCKING]: `TrySpendGold(charId, 400)` on a character with balance=1,200: returns `Success`, `NewBalance=800`. `GetBalance` returns 800.
- [x] **AC-CS-A-03** [BLOCKING]: `TrySpendGold(charId, 400)` on a character with balance=300: returns `InsufficientFunds`. `GetBalance` still returns 300 (unchanged).
- [x] **AC-CS-E-01** [BLOCKING]: `TrySpendGold(charId, 1)` on a character with balance=0: returns `InsufficientFunds`. No write.

---

## Implementation Notes

*Derived from GDD Formula F-CS-2:*

### F-CS-2 — Spend Guard

```csharp
public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
{
    uint balance = GetBalance(charId); // Story 001's lazy-creation convention (0 if never touched)

    if (balance < cost)
        return new GoldMutationResult(success: false, newBalance: balance, previousBalance: balance, GoldMutationError.InsufficientFunds);

    uint newBalance = balance - cost; // safe: guard above ensures no underflow
    _balances[charId] = newBalance;
    return new GoldMutationResult(success: true, newBalance, previousBalance: balance, GoldMutationError.None);
}
```

**No overflow risk on subtraction**: the `balance >= cost` guard is enforced *before* the subtraction. If the guard passes, `balance - cost` cannot underflow — this is the mirror-image safety argument to Story 001's `AddGold` overflow guard.

**Atomicity note**: on the failure path, `_balances[charId]` must not be touched at all — return immediately after the guard check, before any dictionary write.

Add `TrySpendGold` to the `ICurrencyService` interface defined in Story 001.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 003**: Zero-cost / `CharacterNotFound` guards for `TrySpendGold`
- **Story 004**: Thread-safety for concurrent `TrySpendGold` calls
- **Story 005**: `GoldSyncEvent` emission on successful spend
- **Story 006**: Compensating refund flow (calls `AddGold`, not `TrySpendGold`)

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs`

- **AC-CS-A-02**: Given a character with balance=1,200, when `TrySpendGold(charId, 400)` is called, then the result has `Success=true`, `NewBalance=800`; `GetBalance(charId) == 800`.
- **AC-CS-A-03**: Given a character with balance=300, when `TrySpendGold(charId, 400)` is called, then the result has `Success=false`, `Error=InsufficientFunds`; `GetBalance(charId) == 300` (unchanged — assert explicitly, not just that the call failed).
- **AC-CS-E-01**: Given a character with balance=0, when `TrySpendGold(charId, 1)` is called, then the result has `Success=false`, `Error=InsufficientFunds`; `GetBalance(charId) == 0`.
  - Edge case: `TrySpendGold(charId, cost)` where `cost == balance` exactly (boundary, not tested by any numbered AC but implied by F-CS-2's `>=` guard) — must succeed, `NewBalance == 0`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` — must exist and pass

**Status**: [x] Created — 4 test methods, all 3 blocking ACs + boundary edge case covered

---

## Dependencies

- Depends on: Story 001 (`CurrencySystem`, `CharacterID`, `GoldMutationResult`, balance storage) must be Done
- Unlocks: Story 003, Story 004, Story 005, Story 006

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 3/3 passing (0 deferred)
**Deviations**: None
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` (4 test methods)
**Code Review**: Complete — `/code-review` verdict APPROVED (no required changes; two non-blocking suggestions noted, both pre-existing from Story 001 or cosmetic)
