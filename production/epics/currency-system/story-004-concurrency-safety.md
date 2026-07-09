# Story 004: Concurrency Safety (Thread-Safe Balance Mutation)

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-003` (in-memory model — see scope note below)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-006-persistence-layer.md` (reference only — see scope note)
**ADR Decision Summary**: ADR-006 defines optimistic concurrency at the **database** level (`WHERE save_version = @expected`, `rows-affected = 0` → `ConcurrencyConflict`). This story implements the analogous pattern **in-memory**, in pure C#, since Character Persistence (the system that will eventually own the real DB-backed version) does not exist yet. The two mechanisms are structurally parallel (optimistic version check, retry-once, surface `ConcurrencyConflict` after 2 consecutive failures) but this story's implementation is not literally ADR-006's SQL — it is the in-memory stand-in GDD Rule 11 describes.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: Pure C# logic. Uses `System.Threading` primitives (`lock` or `Interlocked`), no Unity-specific concurrency API.

**Control Manifest Rules (Foundation layer)**:
- Guardrail: Per `.claude/rules/test-standards.md`, tests must be deterministic — no reliance on real thread-scheduling timing to produce a specific error code. See the required test-seam design below.

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-D-01** [BLOCKING]: Two concurrent `TrySpendGold(charId, 600)` calls on a character with balance=800 (combined cost 1,200 > 800): exactly one call returns `Success` with `NewBalance=200`; the other returns `InsufficientFunds` or `ConcurrencyConflict`. Final `GetBalance` returns exactly 200. Balance never goes below 0 and never shows a double-debit.
- [x] **AC-CS-D-02** [BLOCKING]: After a `ConcurrencyConflict` result, a retry `TrySpendGold` on the same character with sufficient balance: returns `Success`. Balance decremented correctly.
- [x] **AC-CS-J-01** [BLOCKING]: 4 concurrent `AddGold(charId, 100, GoldTransactionReason.MonsterDrop)` calls on a character with balance=1000: after all 4 calls complete, `GetBalance` returns exactly 1400. No lost updates (balance ≠ 1100, 1200, or 1300).

---

## Implementation Notes

*Derived from GDD Rule 11 (optimistic locking, atomic AddGold):*

### Per-Character Version Counter

Add a `Version` field alongside each character's balance (`Dictionary<CharacterID, (uint Balance, uint Version)>`, or two parallel dictionaries — implementer's choice). `Version` increments by exactly 1 on every successful mutation (`AddGold` or `TrySpendGold`), starting at 0.

### AddGold — Single Atomic Expression (Rule 11, second paragraph)

`AddGold` MUST be implemented as a single atomic operation with **no prior version read** — this is the GDD's explicit distinction from `TrySpendGold`'s CAS-retry pattern, since `AddGold` (server-generated, e.g. monster drops) never needs to reject on conflict, only to never lose an update:

```csharp
public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
{
    lock (GetLockFor(charId)) // one lock object per registered CharacterID
    {
        // ... guard checks (Story 003) ...
        // ... F-CS-1 cap-safe addition (Story 001) ...
        // balance and version updated together inside the same lock — this IS
        // the "single atomic storage-layer expression" Rule 11 requires, translated
        // to an in-memory equivalent of the DB's single UPDATE statement.
    }
}
```

A per-character `lock` around the whole read-modify-write is sufficient and correct for `AddGold` because there is no reject-on-conflict requirement — every call must simply apply cleanly without losing any other call's contribution (AC-CS-J-01).

### TrySpendGold — Optimistic CAS with Retry-Once (Rule 11, first paragraph)

`TrySpendGold` uses the literal optimistic-concurrency pattern Rule 11 describes — a version-checked compare-and-swap, retried once, surfacing `ConcurrencyConflict` on a second consecutive failure:

```csharp
public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
{
    for (int attempt = 0; attempt < 2; attempt++)
    {
        uint expectedVersion = ReadVersion(charId); // no lock held here — this is the race window
        var result = TryCompareAndSwapSpend(charId, cost, reason, expectedVersion);
        if (result.Error != GoldMutationError.ConcurrencyConflict)
            return result; // Success or InsufficientFunds — either is a final answer
    }
    return new GoldMutationResult(success: false, /* ... */ GoldMutationError.ConcurrencyConflict);
}

// internal — the atomic primitive. A short lock covers only the compare+write, not the
// full read-modify-write from the caller's perspective, faithfully modelling the DB's
// "WHERE version = @expected" pattern rather than serializing all access with one big lock.
internal GoldMutationResult TryCompareAndSwapSpend(CharacterID charId, uint cost, GoldTransactionReason reason, uint expectedVersion)
{
    lock (GetLockFor(charId))
    {
        if (CurrentVersion(charId) != expectedVersion)
            return new GoldMutationResult(success: false, /* ... */ GoldMutationError.ConcurrencyConflict);

        uint balance = CurrentBalance(charId);
        if (balance < cost)
            return new GoldMutationResult(success: false, balance, balance, GoldMutationError.InsufficientFunds);

        // ... write newBalance, increment version ...
        return new GoldMutationResult(success: true, balance - cost, balance, GoldMutationError.None);
    }
}
```

**Why expose `TryCompareAndSwapSpend` as `internal` rather than private**: per `.claude/rules/test-standards.md`, tests must be deterministic — real thread races cannot reliably produce a `ConcurrencyConflict` outcome on demand. AC-CS-D-02 requires testing the retry-after-conflict path specifically; the `internal` CAS primitive lets a test call it directly with a deliberately stale `expectedVersion` to deterministically force `ConcurrencyConflict`, without depending on actual thread-scheduling timing. `InternalsVisibleTo` is already wired for the EditMode test assembly (see `src/Foundation/AssemblyInfo.cs` — extend its `InternalsVisibleTo` target list if Currency System lands in a different assembly than `IronGrind.Foundation`).

AC-CS-D-01 and AC-CS-J-01, in contrast, test **final-state correctness under real concurrency** — spin up N `Task.Run` calls via `Task.WhenAll` and assert only the deterministic final balance, not the intermediate interleaving. This satisfies the determinism rule because the assertion (final balance) is invariant regardless of thread scheduling, even though the internal execution path is not.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 005**: `GoldSyncEvent` emission per successful mutation, including under concurrent calls (Version field introduced here feeds directly into `GoldSyncEvent.Version`)
- **Story 006**: Compensating refund flow
- Real database-level optimistic concurrency (Character Persistence's future responsibility)

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_Concurrency_tests.cs`

- **AC-CS-D-01**: Given a registered character with balance=800, when two `TrySpendGold(charId, 600)` calls are issued concurrently via `Task.WhenAll`, then exactly one result has `Success=true, NewBalance=200`, and the other has `Success=false` with `Error` in `{InsufficientFunds, ConcurrencyConflict}`; final `GetBalance(charId) == 200`.
- **AC-CS-D-02**: Given a registered character with balance=1000 at version V, when `TryCompareAndSwapSpend(charId, 100, reason, expectedVersion: V+1)` is called directly (deliberately stale version — deterministic conflict, no real race needed), then the result has `Error=ConcurrencyConflict`, balance unchanged. When `TrySpendGold(charId, 100, reason)` is then called normally (reads the CURRENT correct version), then the result has `Success=true`; `GetBalance(charId) == 900`.
- **AC-CS-J-01**: Given a registered character with balance=1000, when 4 `AddGold(charId, 100, MonsterDrop)` calls are issued concurrently via `Task.WhenAll`, then final `GetBalance(charId) == 1400` exactly (not 1100, 1200, or 1300 — each of those values indicates a lost update). All 4 calls return `Success=true`.
  - Edge case: assert the 4 returned `NewBalance` values, sorted, are exactly `{1100, 1200, 1300, 1400}` — confirms no two concurrent calls observed the same pre-mutation balance (a stronger check than just the final value).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_Concurrency_tests.cs` — must exist and pass

**Status**: [x] Created — 5 test methods (2 real-concurrency via Task.WhenAll, 3 deterministic via the internal CAS seam), all 3 blocking ACs covered

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003 must be Done
- Unlocks: Story 005 (Version field this story introduces feeds `GoldSyncEvent.Version`), Story 006

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 3/3 passing (0 deferred)
**Deviations**: None (one advisory doc-comment gap on `RegisterCharacter`'s concurrent-call safety was identified and fixed during code review)
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_Concurrency_tests.cs` (5 test methods)
**Code Review**: Complete — `/code-review` verdict APPROVED WITH SUGGESTIONS (doc-comment caveat applied; two lower-priority test-annotation/extra-coverage suggestions deferred, not applied)
