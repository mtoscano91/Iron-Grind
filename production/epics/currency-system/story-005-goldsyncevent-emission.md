# Story 005: GoldSyncEvent Emission

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-010-event-messaging-architecture.md`
**ADR Decision Summary**: Tier 2 Broadcast Events (Decision 3) — a single producer notifying multiple independent consumers uses a C# `event Action<T>` with a `readonly struct` argument type, never a class-typed arg (boxing) and never `UnityEvent`. `GoldSyncEvent` is exactly this pattern: Currency System is the producer, HUD/Character Persistence/Networking Core are (future) consumers.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: `readonly struct` event args — zero GC allocation on emit, per ADR-010's server-tick-rate rationale.

**Control Manifest Rules (Foundation layer)**:
- Required: `event Action<T>` with `readonly struct` args for broadcast events — source: ADR-010 Decision 3
- Forbidden: `UnityEvent` for server-side logic; class-typed event args; central EventBus — source: ADR-010
- Forbidden: lambda captures for persistent event subscriptions — source: ADR-010 Decision 4 (relevant to any future consumer, not this story's own code, which has no subscriptions of its own)

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-F-01** [BLOCKING]: `GoldSyncEvent` is emitted after every successful balance mutation (`AddGold` or `TrySpendGold`). Fields: `CharacterID` matches caller, `NewBalance` matches post-operation `GetBalance`, `Version` = pre-operation `Version + 1`, `Reason` matches the reason enum passed by the caller.
- [x] **AC-CS-F-02** [BLOCKING]: `GoldSyncEvent` is NOT emitted when `AddGold` returns `InvalidAmount` or `CharacterNotFound`.
- [x] **AC-CS-F-03** [BLOCKING]: `GoldSyncEvent` is NOT emitted when `TrySpendGold` returns `InsufficientFunds`, `InvalidAmount`, or `CharacterNotFound`.
- [x] **AC-CS-F-04** [BLOCKING]: `GoldSyncEvent.Version` increments monotonically: after N sequential mutations to the same character's balance, the final `GoldSyncEvent.Version = initial_version + N`. No Version skips or repeats.

---

## Implementation Notes

*Derived from ADR-010 Decision 3 and GDD's Interactions table:*

### Event Args (readonly struct, per ADR-010)

```csharp
public readonly struct GoldSyncEventArgs
{
    public readonly CharacterID CharacterID;
    public readonly uint NewBalance;
    public readonly uint Version;
    public readonly GoldTransactionReason Reason;

    public GoldSyncEventArgs(CharacterID characterId, uint newBalance, uint version, GoldTransactionReason reason)
    {
        CharacterID = characterId; NewBalance = newBalance; Version = version; Reason = reason;
    }
}
```

### Event Declaration and Firing

Add to `ICurrencyService`:
```csharp
event Action<GoldSyncEventArgs> OnGoldSync;
```

Fire from `CurrencySystem` at the exact point a mutation succeeds — inside the same `lock` block from Story 004 (Version and NewBalance must be read from the same atomic write that produced them, not re-queried after releasing the lock, to avoid a race where another call mutates the balance between the write and the event firing):

```csharp
// Inside AddGold's lock block, immediately after the successful write:
OnGoldSync?.Invoke(new GoldSyncEventArgs(charId, newBalance, newVersion, reason));

// Inside TryCompareAndSwapSpend's lock block, immediately after the successful write:
OnGoldSync?.Invoke(new GoldSyncEventArgs(charId, newBalance, newVersion, reason));
```

**Guard-rejected calls never reach this line** — `InvalidAmount`, `CharacterNotFound`, `InsufficientFunds`, and `ConcurrencyConflict` all `return` before the write, so AC-CS-F-02/F-03 are satisfied by construction (no separate suppression logic needed) as long as the emit call is placed only on the success path, never in a `finally` block or similar.

### Subscriber Capacity

Follow the `CharacterStats.Subscribe`/`Unsubscribe` fixed-capacity-array pattern only if this project's convention requires it for consistency — however, `event Action<T>` (a real C# event, not `CharacterStats`'s custom fixed-array delegate list) already provides `+=`/`-=` semantics natively with no manual capacity management needed. Prefer the simpler native `event Action<T> OnGoldSync;` declaration over reinventing `CharacterStats`'s fixed-array subscriber list — that pattern was chosen there for its own historical reasons (documented in `CharacterStats.cs`'s Story 005 remarks) and is not a project-wide mandate; ADR-010 itself just specifies `event Action<T>`, not a specific subscription-storage mechanism.

---

## Out of Scope

*Handled by neighbouring stories / future work — do not implement here:*

- **Story 006**: Compensating refund flow (uses `AddGold`, inherits event firing automatically once this story lands)
- Actual network transport of `GoldSyncEvent` to a client connection — Networking Core's future responsibility (GDD OQ-CS-3, explicitly non-blocking for Currency System)
- Client-side stale-discard logic (`Version <= cachedVersion` → ignore) — that is client/HUD code, not Currency System's

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`

- **AC-CS-F-01**: Given a registered character at balance=500, version=V, when `AddGold(charId, 150, MonsterDrop)` is called with a subscriber attached to `OnGoldSync`, then the event fires exactly once with `CharacterID` matching, `NewBalance=650`, `Version=V+1`, `Reason=MonsterDrop`.
  - Repeat for `TrySpendGold` with a `ScrollPurchase` reason.
- **AC-CS-F-02**: Given a subscriber attached to `OnGoldSync`, when `AddGold(charId, 0, ...)` is called (triggers `InvalidAmount`), then the event does NOT fire. Repeat for an unregistered `charId` (`CharacterNotFound`).
- **AC-CS-F-03**: Given a subscriber attached, when `TrySpendGold` is called with insufficient balance, a zero cost, or an unregistered `charId`, then the event does NOT fire in any of the three cases.
- **AC-CS-F-04**: Given a registered character at version=V, when 3 sequential successful mutations occur (e.g. `AddGold`, `TrySpendGold`, `AddGold`), then the 3 captured `GoldSyncEventArgs.Version` values are exactly `V+1, V+2, V+3` in that order — no skip, no repeat.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs` — must exist and pass

**Status**: [x] Created — 9 test methods, all 4 ACs covered

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003, Story 004 must be Done (Version field from Story 004 is required)
- Unlocks: Story 006

## Completion Notes
**Completed**: 2026-07-08
**Criteria**: 4/4 passing
**Deviations**: None (advisory-only: TR-currency-004 not yet present in `tr-registry.yaml` — pre-existing project-wide registry gap, not new to this story)
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`, 9 test methods (a 9th test, proving `OnGoldSync` fires exactly once on a successful retry after a forced `ConcurrencyConflict`, was added during code review per independent findings from both the Unity specialist and qa-tester)
**Code Review**: Complete — verdict CHANGES REQUIRED (one test) → fixed → APPROVED WITH SUGGESTIONS. Non-blocking suggestions deferred: extract repeated version-increment/event-fire tail into a shared helper; minor dictionary-op reduction in the version increment; document (doc-comment only) that `OnGoldSync` subscribers must not throw or re-enter `ICurrencyService`.
