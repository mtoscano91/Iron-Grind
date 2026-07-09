# Story 003: Input Guards & State Machine

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-001`, `TR-currency-002` (guard clauses)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: Pure C# logic.

**Control Manifest Rules (Foundation layer)**:
- Guardrail: Guard-clause rejections (`InvalidAmount`, `CharacterNotFound`) are caller bugs per GDD EC-CS-1/EC-CS-9, not player-facing errors — log server-side (`Debug.LogError`, matching the `ItemDatabase` dev-error convention), never throw.

---

## Important: Revises Stories 001/002's Balance-Lookup Assumption

Stories 001 and 002 treated any `CharacterID` as implicitly valid, lazily creating a balance entry at 0 on first access. This story **supersedes that simplification**: a `CharacterID` must now be explicitly registered before `AddGold`/`TrySpendGold` will operate on it — matching GDD EC-CS-9's implication that `CharacterNotFound` is a real, detectable state (a session-management bug), which requires the system to be able to distinguish "known but zero balance" from "never registered."

Add to `ICurrencyService`:
```csharp
void RegisterCharacter(CharacterID charId, uint initialBalance);
```

This is the test/bootstrap seam a real system would call from Character Persistence on character load — for now, tests call it directly to set up fixtures (same role `CharacterStatsFixture.Create()` plays for Character Stats). Update `AddGold` and `TrySpendGold` (Stories 001–002) to check registration first and return `CharacterNotFound` if the `CharacterID` was never registered, before evaluating any other guard.

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-B-01** [BLOCKING]: `AddGold(charId, 0)`: returns `InvalidAmount`. No write. `GetBalance` unchanged.
- [x] **AC-CS-B-02** [BLOCKING]: `TrySpendGold(charId, 0)`: returns `InvalidAmount`. No write. `GetBalance` unchanged.
- [x] **AC-CS-B-03** [BLOCKING]: `AddGold` or `TrySpendGold` with a `CharacterID` that was never registered: returns `CharacterNotFound`. No write.
- [x] **AC-CS-H-01** [BLOCKING]: Balance=0 (Empty), `AddGold(1)`: balance transitions to Normal (0 < balance < GOLD_CAP).
- [x] **AC-CS-H-02** [BLOCKING]: Balance=`GOLD_CAP - 1` (Normal), `AddGold(1)`: balance transitions to AtCap (balance = GOLD_CAP).
- [x] **AC-CS-H-03** [BLOCKING]: Balance=`GOLD_CAP` (AtCap), `TrySpendGold(1)`: balance transitions to Normal.
- [x] **AC-CS-H-04** [BLOCKING]: Balance=1 (Normal), `TrySpendGold(1)`: balance transitions to Empty (balance = 0).
- [x] **AC-CS-H-05** [BLOCKING]: Balance=`GOLD_CAP` (AtCap), `AddGold(any)`: balance remains `GOLD_CAP`. State does not change.
- [x] **AC-CS-H-06** [BLOCKING]: Balance=0 (Empty), `AddGold(GOLD_CAP)`: balance transitions directly to AtCap. Returns `Success`, `NewBalance=GOLD_CAP`.
- [x] **AC-CS-H-07** [BLOCKING]: Balance=`GOLD_CAP` (AtCap), `TrySpendGold(GOLD_CAP)`: returns `Success`, `NewBalance=0`. Balance transitions directly to Empty.

---

## Implementation Notes

*Derived from GDD Rules 2, 9 and the States/Transitions table:*

### Guard Order

Both `AddGold` and `TrySpendGold` check guards in this order (fail fast, first match wins):
1. `CharacterNotFound` — `charId` was never registered
2. `InvalidAmount` — `amount == 0` (for `AddGold`) or `cost == 0` (for `TrySpendGold`)
3. The formula-specific guard (F-CS-1 cap clamp always succeeds; F-CS-2 `balance >= cost`)

### State Machine

The `Empty` / `Normal` / `AtCap` states are **derived**, not stored — do not add an explicit `enum CharacterGoldState` field. A state is a classification of the current `balance` value at any point in time:

```csharp
// Derived, not stored:
// Empty:  balance == 0
// Normal: 0 < balance < GOLD_CAP
// AtCap:  balance == GOLD_CAP
```

This keeps the state machine ACs (H-01 through H-07) as pure assertions on `GetBalance()` before/after a call — no separate state-tracking code path to keep in sync with the balance itself.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 004**: Thread-safety for concurrent guard evaluation
- **Story 005**: `GoldSyncEvent` non-emission on guard-rejected calls (AC-CS-F-02, F-03 — natural extension of this story's guards, but scoped to Story 005 since it depends on the event infrastructure not yet built)
- **Story 006**: `TransferGold` stub (has its own guard: always `NotImplemented`, unrelated to registration)

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs`

- **AC-CS-B-01**: Given a registered character with balance=500, when `AddGold(charId, 0)` is called, then the result has `Error=InvalidAmount`; `GetBalance(charId) == 500` (unchanged).
- **AC-CS-B-02**: Given a registered character with balance=500, when `TrySpendGold(charId, 0)` is called, then the result has `Error=InvalidAmount`; `GetBalance(charId) == 500` (unchanged).
- **AC-CS-B-03**: Given a `CharacterID` that was never passed to `RegisterCharacter`, when `AddGold` is called, then the result has `Error=CharacterNotFound`. Repeat for `TrySpendGold`.
- **AC-CS-H-01**: Given balance=0, when `AddGold(charId, 1)` is called, then `GetBalance(charId) == 1` (Normal range: `0 < 1 < GOLD_CAP`).
- **AC-CS-H-02**: Given balance=`GOLD_CAP - 1`, when `AddGold(charId, 1)` is called, then `GetBalance(charId) == GOLD_CAP` (AtCap).
- **AC-CS-H-03**: Given balance=`GOLD_CAP`, when `TrySpendGold(charId, 1)` is called, then `GetBalance(charId) == GOLD_CAP - 1` (Normal).
- **AC-CS-H-04**: Given balance=1, when `TrySpendGold(charId, 1)` is called, then `GetBalance(charId) == 0` (Empty).
- **AC-CS-H-05**: Given balance=`GOLD_CAP`, when `AddGold(charId, 500)` is called, then `GetBalance(charId)` remains exactly `GOLD_CAP` (no change).
- **AC-CS-H-06**: Given balance=0, when `AddGold(charId, GOLD_CAP)` is called, then the result has `Success=true`, `NewBalance=GOLD_CAP`; `GetBalance(charId) == GOLD_CAP`.
- **AC-CS-H-07**: Given balance=`GOLD_CAP`, when `TrySpendGold(charId, GOLD_CAP)` is called, then the result has `Success=true`, `NewBalance=0`; `GetBalance(charId) == 0`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs` — must exist and pass

**Status**: [x] Created — 13 test methods (11 original + 2 guard-order tests added after code review), all 10 blocking ACs covered

---

## Dependencies

- Depends on: Story 001, Story 002 must be Done (this story revises their guard behavior)
- Unlocks: Story 004, Story 005, Story 006

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 10/10 passing (0 deferred)
**Deviations**: None
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs` (13 test methods); also required retrofitting 10 of 11 pre-existing tests in `Currency_AddGold_tests.cs`/`Currency_TrySpendGold_tests.cs` with `RegisterCharacter` calls (verified no semantic drift)
**Code Review**: Complete — `/code-review` verdict APPROVED WITH SUGGESTIONS (guard-order-proving test added; two lower-priority suggestions — double-register overwrite test, `TryGetValue` refactor — deferred, not applied)
