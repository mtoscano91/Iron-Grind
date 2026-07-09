# Story 001: Core Types & AddGold (Cap-Safe Addition)

> **Epic**: Currency System
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/currency-system.md`
**Requirement**: `TR-currency-002` (partial — `AddGold` half; reason logging)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the in-memory balance model itself. `docs/architecture/ADR-006-persistence-layer.md` defines the eventual `character_records.gold_balance`/`gold_version` DB columns this system's state will map to once Character Persistence is implemented — but that mapping is out of scope for this story. This story is a pure in-memory C# model, exactly like `CharacterStats` and `ItemDatabase` before their respective persistence layers existed.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: Pure C# logic, no engine API surface beyond what `src/Foundation/` already uses (`UnityEngine.Debug` for dev-error logging, matching `ItemDatabase`'s pre-init guard pattern).

**Control Manifest Rules (Foundation layer)**:
- Required: Service interface naming `I[SystemName]Service` — source: ADR-010. Define `ICurrencyService` for the public API surface, matching the `IItemDatabase` precedent (Currency System will have multiple future consumers: NPC Shop, Loot Table System, Class/Leveling System).
- Guardrail: All mutations must be additive (`AddGold`) or subtractive (`TrySpendGold`) — GDD Rule 9 forbids a `SetGold` operation. Do not add one, even for test convenience.

---

## Acceptance Criteria

*From GDD `design/gdd/currency-system.md`, scoped to this story:*

- [x] **AC-CS-A-01** [BLOCKING]: `AddGold(charId, 150)` on a character with balance=500: `GetBalance` returns 650.
- [x] **AC-CS-A-04** [BLOCKING]: `AddGold(charId, 200)` on a character with balance=9,999,900: returns `Success`, `NewBalance=9,999,999`, `Error=None`. `GetBalance` returns 9,999,999. 101g discarded. Callers detect the cap was hit from `result.NewBalance == GOLD_CAP`.
- [x] **AC-CS-A-05** [BLOCKING]: `AddGold(charId, GOLD_CAP)` on a character with balance=1: `GetBalance` returns 9,999,999. `NewBalance` never exceeds `GOLD_CAP` regardless of input size.
- [x] **AC-CS-C-01** [BLOCKING]: `AddGold(charId, 2)` on a character with balance=`GOLD_CAP - 1` (9,999,998): `GetBalance` returns 9,999,999. Does NOT return 0 or any value less than `GOLD_CAP` (no `uint` wrap). *(Tests F-CS-1 overflow guard.)*
- [x] **AC-CS-C-02** [BLOCKING]: `AddGold(charId, GOLD_CAP)` on a character with balance=0: `GetBalance` returns exactly 9,999,999 (not overflow, not less).

---

## Implementation Notes

*Derived from GDD Rules 2–4, 12 and Formula F-CS-1:*

### Core Types (register in `design/registry/entities.yaml` — already pre-registered, use as authoritative source)

```csharp
namespace IronGrind.Currency
{
    // CharacterID(0) = Invalid — reserved, must not be assigned to any character.
    // Implement IEquatable<CharacterID> directly on the struct (proven IL2CPP-safe
    // pattern from EntityID/ItemID this session) — do not build a separate nested
    // comparer class; entities.yaml's mention of one predates this session's
    // established, working convention.
    public readonly struct CharacterID : IEquatable<CharacterID>
    {
        public static readonly CharacterID Invalid = new CharacterID(0u);
        // ... constructor, Equals, GetHashCode, ==, !=, ToString() => $"CharacterID({_value})" ...
    }

    // enum : byte — values are AUTHORITATIVE from entities.yaml, do not renumber.
    public enum GoldTransactionReason : byte
    {
        MonsterDrop = 0, ScrollPurchase = 1, RespecStat = 2, RespecSkill = 3,
        AdminAdjust = 4, Enhancement = 5, ItemPurchase = 6, ItemSell = 7,
        CompensatingRefund = 8, Other = 255,
    }

    public enum GoldMutationError : byte
    {
        None = 0, InsufficientFunds = 1, CharacterNotFound = 2,
        InvalidAmount = 3, ConcurrencyConflict = 4, NotImplemented = 5,
    }

    // readonly struct — zero GC allocation, per entities.yaml.
    public readonly struct GoldMutationResult
    {
        public readonly bool Success;
        public readonly uint NewBalance;
        public readonly uint PreviousBalance;
        public readonly GoldMutationError Error;
        // ... constructor ...
    }
}
```

### GOLD_CAP

`private const uint GOLD_CAP = 9_999_999u;` — per `entities.yaml`, fixed at this value; not a tuning knob (do not expose as a Tuning Knob or ScriptableObject field).

### ICurrencyService / CurrencySystem

```csharp
public interface ICurrencyService
{
    GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason);
    // TrySpendGold, GetBalance added in Story 002.
}

public sealed class CurrencySystem : ICurrencyService
{
    private readonly Dictionary<CharacterID, uint> _balances = new Dictionary<CharacterID, uint>();
    // Version tracking (per-character monotonic counter) is introduced in Story 004/005 —
    // do not add a Version field in this story if it is not yet consumed; avoid dead state.

    public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
    {
        // Guard rails (AC-CS-B-01, B-03) are implemented in Story 003 — for THIS story,
        // implement only the happy-path cap-safe addition formula (F-CS-1). Do not add
        // amount==0 or CharacterNotFound handling yet; that is Story 003's explicit scope
        // and will be layered on without needing to revisit this method's core formula.
        // ...
    }
}
```

### F-CS-1 — Cap-Safe Gold Addition (subtraction-first to avoid uint overflow)

```csharp
uint balance = GetOrCreateBalance(charId); // 0 if character has never received gold
uint newBalance = (amount > GOLD_CAP - balance) ? GOLD_CAP : balance + amount;
```

**Why subtraction-first**: `balance + amount` overflows `uint` when both are near `GOLD_CAP`. `GOLD_CAP - balance` is always safe since `balance` is invariantly `≤ GOLD_CAP`. Evaluate the guard before ever computing `balance + amount` directly.

### Character existence for this story

A `CharacterID` not previously seen is treated as starting at balance 0 (lazy dictionary entry creation) for THIS story only — AC-CS-B-03's `CharacterNotFound` guard (which requires distinguishing "known character" from "truly nonexistent character," implying an explicit registration/removal API) is Story 003's scope. Do not implement `CharacterNotFound` detection here; every `CharacterID` is currently treated as valid.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 002**: `TrySpendGold` (F-CS-2 spend guard)
- **Story 003**: Zero-amount / `CharacterNotFound` guards; Empty/Normal/AtCap state machine
- **Story 004**: Thread-safety (`lock`), concurrent-call atomicity
- **Story 005**: `GoldSyncEvent` emission, `Version` tracking
- **Story 006**: `TransferGold` stub, compensating refund flow
- Real PostgreSQL persistence — Character Persistence's future responsibility (ADR-006 defines the eventual schema only)
- `ServerLogic.asmdef` isolation (GDD Group G) — no server/client assembly split exists in this project yet; deferred to whenever that infrastructure is built
- Session resync / `GoldSyncEvent` transport (GDD Group I) — depends on Networking Core's session handshake, not yet built

---

## QA Test Cases

*Test file*: `tests/EditMode/Currency/Currency_AddGold_tests.cs`

- **AC-CS-A-01**: Given a character with balance=500, when `AddGold(charId, 150)` is called, then `GetBalance(charId) == 650`.
- **AC-CS-A-04**: Given a character with balance=9,999,900, when `AddGold(charId, 200)` is called, then the result has `Success=true`, `NewBalance=9,999,999`, `Error=None`; `GetBalance(charId) == 9,999,999`.
- **AC-CS-A-05**: Given a character with balance=1, when `AddGold(charId, GOLD_CAP)` is called, then `GetBalance(charId) == 9,999,999` (never exceeds cap regardless of input magnitude).
  - Edge case: verify with an even larger `amount` value (e.g. `uint.MaxValue`) that `NewBalance` still clamps to exactly `GOLD_CAP` — no overflow wrap to a small number.
- **AC-CS-C-01**: Given a character with balance=9,999,998, when `AddGold(charId, 2)` is called, then `GetBalance(charId) == 9,999,999` — explicitly assert the result is NOT `0` or any value `< GOLD_CAP` (guards against a naive `balance + amount` `uint` wraparound bug).
- **AC-CS-C-02**: Given a character with balance=0, when `AddGold(charId, GOLD_CAP)` is called, then `GetBalance(charId) == 9,999,999` exactly.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Currency/Currency_AddGold_tests.cs` — must exist and pass

**Status**: [x] Created — 7 test methods, all 5 blocking ACs covered

---

## Dependencies

- Depends on: None (first story in this epic)
- Unlocks: Story 002 (`TrySpendGold` reads the balance this story writes), Story 003, Story 004, Story 005, Story 006

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 5/5 passing (0 deferred)
**Deviations**: None
**Test Evidence**: Logic — `tests/EditMode/Currency/Currency_AddGold_tests.cs` (7 test methods)
**Code Review**: Complete — `/code-review` verdict APPROVED WITH SUGGESTIONS (both suggestions applied: added `GetBalance` default-value test, simplified `AddGold` to remove a redundant double write)
