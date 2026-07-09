# Story 006: TransferGold Stub & Compensating Refund

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 1–2 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-002` (compensating refund half)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-001-purchase-transaction-integrity.md`
**ADR Decision Summary**: Decision 3 (Compensating Refund) defines `CompensatingRefund` (`GoldTransactionReason` value 8) as the dedicated reason code for refunding a gold debit after a downstream failure (e.g. `PickupRequest` fails after `TrySpendGold` succeeded). Currency System's role is narrow: it just needs `AddGold` to accept `CompensatingRefund` as a valid reason like any other — the actual orchestration (detecting the downstream failure, calling the refund) belongs to NPC Shop (not yet built).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: Pure C# logic.

**Control Manifest Rules (Foundation layer)**:
- Guardrail: GDD Rule 8 — `TransferGold` has no implementation at MVP; a stub that always returns `NotImplemented` is correct, not a placeholder to "finish later" within this epic. Do not implement real P2P transfer logic.

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-E-02** [BLOCKING]: `TransferGold(fromId, toId, 100)`: returns `NotImplemented`. No write to either balance. `GetBalance` for both characters unchanged.
- [x] **AC-CS-E-03** [BLOCKING]: Compensating refund flow: call `TrySpendGold(charId, 400)` (Success, balance=800); simulate item-grant failure; call `AddGold(charId, 400, CompensatingRefund)` as compensating transaction. `GetBalance` returns 1,200 (original balance fully restored). No `uint` overflow.

---

## Implementation Notes

*Derived from GDD Rule 8 and EC-CS-5:*

### TransferGold Stub

```csharp
public GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount)
{
    // GDD Rule 8: not implemented at MVP. Deferred to the Trade System GDD (outside
    // the 35-system MVP scope). Do not touch either character's balance.
    return new GoldMutationResult(success: false, newBalance: 0, previousBalance: 0, GoldMutationError.NotImplemented);
}
```

Add to `ICurrencyService`. `NewBalance`/`PreviousBalance` are meaningless for a two-party operation that never executes — populate with `0` and note in the XML doc comment that callers must not read these fields when `Error == NotImplemented`.

### Compensating Refund — No New API

EC-CS-5 does **not** require a dedicated `RefundGold` method — a compensating refund is just a normal `AddGold` call with `reason: GoldTransactionReason.CompensatingRefund`. The overflow-safety argument from the GDD applies automatically because it's already covered by Story 001's F-CS-1 guard: "the refund `AddGold` cannot overflow: `balance` was just decremented by `cost`, so `balance + cost ≤ previous_balance ≤ GOLD_CAP`" — this is a proof that the existing cap-safe formula handles this case correctly, not a new code path to add. This story's job is purely to write the test that demonstrates the full sequence works end-to-end.

---

## Out of Scope

*Handled by neighbouring stories / future work — do not implement here:*

- Real P2P gold transfer — Trade System GDD, outside MVP scope entirely (GDD Rule 8)
- Automatic refund orchestration (detecting a downstream failure and calling `AddGold` automatically) — that is NPC Shop's responsibility per ADR-001 Decision 3; Currency System only needs to accept the call when NPC Shop makes it

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_EdgeCases_tests.cs`

- **AC-CS-E-02**: Given two registered characters with distinct balances, when `TransferGold(fromId, toId, 100)` is called, then the result has `Error=NotImplemented`; `GetBalance(fromId)` and `GetBalance(toId)` are both unchanged from their pre-call values.
- **AC-CS-E-03**: Given a registered character with balance=1200, when `TrySpendGold(charId, 400, ScrollPurchase)` succeeds (`GetBalance == 800`), and then `AddGold(charId, 400, CompensatingRefund)` is called (simulating a failed item grant), then `GetBalance(charId) == 1200` (fully restored) with no overflow or clamp artifact — assert `Error=None` and `Success=true` on the refund call itself, not just the final balance.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_EdgeCases_tests.cs` — must exist and pass

**Status**: [x] Created — 2 test methods, both ACs covered

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003 must be Done (Story 004/005 not strictly required for this story's own ACs, but implement after them anyway to keep the epic's story order linear and avoid merge conflicts in `CurrencySystem.cs`)
- Unlocks: None — this is the last story in the Currency System epic

## Completion Notes
**Completed**: 2026-07-08
**Criteria**: 2/2 passing
**Deviations**: None blocking. Advisory: TR-currency-002 not yet in `tr-registry.yaml` (pre-existing project-wide registry gap, not new); implementation correctly follows ADR-001/control-manifest's `CompensatingRefund` reason code rather than the stale `AdminAdjust` reference still in `design/gdd/currency-system.md` EC-CS-5 (GDD text fix recommended as a follow-up propagation edit, not done this session).
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_EdgeCases_tests.cs`, 2 test methods
**Code Review**: Complete — verdict APPROVED WITH SUGGESTIONS (non-blocking: optional input-invariance regression test for the stub, optional explicit assertion on the `NotImplemented` result's placeholder fields)
