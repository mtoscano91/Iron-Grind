# Story 007: Transaction API — BeginStatTransaction / EndStatTransaction / RollbackStatTransaction

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 4–6 hours

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-003`, `TR-stats-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the transaction API. The deduplication set uses a fixed-size `StatID[]` array with linear scan (not `HashSet<StatID>`) — linear scan is faster for n≤18 stats and avoids IL2CPP boxing risks from `HashSet` without an explicit comparer.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `HashSet<StatID>` without an explicit `IEqualityComparer<StatID>` may fall back to the object-based `EqualityComparer` under IL2CPP AOT compilation, boxing the enum value on every `TryGetValue` and `Add`. Use a fixed-size `StatID[]` array with linear scan instead.

**Control Manifest Rules (Foundation layer)**:
- Required: Deduplication set as fixed-size `StatID[]` array with linear scan — never `HashSet<StatID>` without explicit comparer — source: GDD Rule F-10
- Forbidden: Transactions are not nestable — `BeginStatTransaction` while open → `InvalidOperationException`
- Guardrail: During a transaction, base stat writes are immediate; only `OnStatChanged` events are deferred

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-33** [BLOCKING]: Entity; subscribe `OnStatChanged` for VIT and MaxHP; `StatEventRecorder` starts all counts at 0.
  `BeginStatTransaction()` → assert `firedCount[VIT]==0` AND `firedCount[MaxHP]==0`.
  `SetBaseStat(VIT, 54)` → assert `firedCount[VIT]==0` immediately (deferred — not fired yet).
  `SetBaseStat(MaxHP, 1920)` → assert `firedCount[MaxHP]==0` immediately (deferred).
  `EndStatTransaction()` → `GetBaseStat(MaxHP)` = **1920**; `firedCount[VIT]==1`; `firedCount[MaxHP]==1`.
  *The intermediate assertions after each SetBaseStat are what prove deferral — not just the final count.*
- [ ] **[Gate] Non-nestable Begin** [BLOCKING]: `BeginStatTransaction()` when already open → `InvalidOperationException`.
- [ ] **[Gate] End with no open transaction** [BLOCKING]: `EndStatTransaction()` with no prior `Begin` → `InvalidOperationException`.
- [ ] **[Gate] Rollback no-op** [BLOCKING]: `RollbackStatTransaction()` with no open transaction → no exception; no state change.
- [ ] **[Gate] Deduplication** [BLOCKING]: `BeginStatTransaction()`, `SetBaseStat(VIT, 40)`, `SetBaseStat(VIT, 54)`, `EndStatTransaction()` → `firedCount[VIT]==1` (not 2); `GetBaseStat(VIT)` = 54 (last write wins).
- [ ] **[NEW] Mid-transaction Rollback semantic** [BLOCKING]: `BeginStatTransaction()`, `SetBaseStat(VIT, 54)` (write immediate), `RollbackStatTransaction()` → `GetBaseStat(VIT)` = **54** (write preserved); `firedCount[VIT]==0` (deferred event discarded). Transaction closed: `EndStatTransaction()` → `InvalidOperationException`.
- [ ] **[NEW] GetBaseStat during open transaction** [ADVISORY]: `BeginStatTransaction()`, `SetBaseStat(VIT, 54)`, `GetBaseStat(VIT)` mid-transaction → **54** (write is immediate; only events are deferred, not reads).

---

## Implementation Notes

*Derived from GDD F-10 (respec policy) and GDD Rule 8 (transaction API):*

**Transaction semantics:**
- `BeginStatTransaction()`: Set `_transactionOpen = true`. Initialize deferred dedup array (fixed-size `StatID[]`, 18 elements). `OnStatChanged` events are queued, not fired.
- `SetBaseStat()` during a transaction: Apply the write immediately (base stat updated in-place). Add `StatID` to the deferred dedup array using linear scan (skip if already present). Do NOT fire `OnStatChanged`.
- `EndStatTransaction()`: Fire `OnStatChanged` once per StatID in the dedup array. Clear dedup array. Set `_transactionOpen = false`.
- `RollbackStatTransaction()`: Clear the deferred dedup array. Set `_transactionOpen = false`. Base stat writes are NOT reversed — Rollback discards pending events only. Safe to call with no open transaction (no-op in that case).

**Why writes are not reversed on Rollback:** Rollback is for exception recovery — the Leveling System or Status Effects system uses a `try/catch` where the `catch` block calls Rollback and either restarts or aborts the operation. The stat store reflects the partially-applied state, which is the caller's responsibility to handle.

**Deduplication array:** Fixed-size `StatID[]` with 18 elements (one per stat in the schema). Linear scan on add: if `StatID` already present, skip. On `EndStatTransaction`, iterate and fire `OnStatChanged` for each non-empty slot. Zero heap allocation beyond the array itself (allocated once at construction or transaction open, reused per transaction).

**Non-nestable:** `BeginStatTransaction()` when `_transactionOpen == true` throws `InvalidOperationException`. The dedup array is not re-initialized on a second Begin — the exception prevents state corruption.

**Permitted callers:** Class System (L1 init), Leveling System (respec), Status Effects (tick-path buff flush). No other system may open a transaction.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Respec procedure (F-3 through F-9 re-evaluation)**: Belongs in the Leveling System epic — this story implements only the transaction API
- **Status Effects tick-path buff flush**: Belongs in the Status Effects epic
- **Story 008**: Integration test verifying the Leveling System calls this API in the correct sequence for tier transitions

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs`
*Uses `StatEventRecorder` from `tests/EditMode/CharacterStats/TestHelpers/`*

- **AC-33**: Transaction defers OnStatChanged; fires once per stat at EndStatTransaction
  - Given: Entity; `StatEventRecorder` tracking `firedCount` per `StatID` (all start at 0). Subscribe `OnStatChanged` for VIT and MaxHP.
  - When: `BeginStatTransaction()` → assert `firedCount[VIT]==0` AND `firedCount[MaxHP]==0`
  - When: `SetBaseStat(VIT, 54)` → assert `firedCount[VIT]==0` immediately (deferred)
  - When: `SetBaseStat(MaxHP, 1920)` → assert `firedCount[MaxHP]==0` immediately (deferred)
  - When: `EndStatTransaction()`
  - Then: `GetBaseStat(MaxHP)` = 1920; `firedCount[VIT]==1`; `firedCount[MaxHP]==1`.
  - Edge cases: Write VIT twice inside one transaction → `firedCount[VIT]==1` after End (see dedup test below).

- **[Gate] Same stat written twice fires OnStatChanged exactly once (dedup)**
  - Given: Entity; `StatEventRecorder`; subscribe `OnStatChanged` for VIT.
  - When: `BeginStatTransaction()`, `SetBaseStat(VIT, 40)`, `SetBaseStat(VIT, 54)`, `EndStatTransaction()`
  - Then: `firedCount[VIT]==1` (not 2). `GetBaseStat(VIT)` = 54 (last write wins).

- **[Gate] BeginStatTransaction nested throws InvalidOperationException**
  - Given: `BeginStatTransaction()` already open.
  - When: `BeginStatTransaction()` again.
  - Then: `InvalidOperationException`. Transaction state not corrupted — `EndStatTransaction()` still works.

- **[Gate] EndStatTransaction with no open transaction throws**
  - Given: No transaction open.
  - When: `EndStatTransaction()`.
  - Then: `InvalidOperationException`.

- **[Gate] RollbackStatTransaction with no open transaction is a no-op**
  - Given: No transaction open.
  - When: `RollbackStatTransaction()`.
  - Then: No exception. No state change.
  - Edge cases: Call Rollback multiple times consecutively → all no-ops.

- **[NEW] Mid-transaction Rollback: writes preserved; deferred events discarded**
  - Given: Entity with VIT=10; `StatEventRecorder`; subscribe `OnStatChanged` for VIT.
  - When: `BeginStatTransaction()`, `SetBaseStat(VIT, 54)`, `RollbackStatTransaction()`
  - Then: `GetBaseStat(VIT)` = 54 (write preserved). `firedCount[VIT]==0` (deferred event discarded). Transaction closed.
  - When: `EndStatTransaction()` after Rollback.
  - Then: `InvalidOperationException` (transaction is already closed).
  - Edge cases: Rollback with multiple writes; all writes preserved; zero events fired.

- **[NEW] GetBaseStat during open transaction returns updated value**
  - Given: Entity with VIT=10.
  - When: `BeginStatTransaction()`, `SetBaseStat(VIT, 54)`, `int midValue = GetBaseStat(VIT)`
  - Then: `midValue == 54` (write is immediate — only events are deferred, not reads or base stat access).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003, Story 004, Story 005, Story 006 must be Done
- Unlocks: Story 008 (integration — requires this transaction API to exist for the Leveling System integration path)

---

## Completion Notes
**Completed**: 2026-07-02
**Criteria**: 7/7 passing (6 blocking + 1 advisory — all covered)
**Deviations**:
- ADVISORY: Partial event delivery on handler throw — consistent with existing non-transaction behavior; no spec change needed
- ADVISORY: Multi-entity transaction untested — dedup array correctness across entities unverified; suggest follow-up test story
- ADVISORY: Empty transaction (Begin → End with no writes) untested — harmless by inspection but unverified
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (7 tests)
**Code Review**: Approved with suggestions (lean mode; required change applied)
