# Story 011: `PendingPurchase` Lifecycle on the Store Seam

> **Epic**: Character Persistence
> **Status**: Ready — written 2026-10-12; `/story-readiness` 2026-10-15: NEEDS WORK (3 open decisions) → Deferred; `/story-readiness` 2026-10-26: NEEDS WORK (10 gaps) → fixed → Ready. User decisions 2026-10-26: write it now (unblocks Story 015); in-memory Currency is the gold authority (ADR-001 Amendment A2).
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 7 hours (readiness 2026-10-26; was 4 h provisional — includes the signature change and the new store seam)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-12 (storage and lifecycle, rewritten 2026-10-26 for ADR-001 A2); CR-CP-1 (the four purchase methods, `PendingPurchaseResult`, `PendingPurchaseRecord`). The GDD has no acceptance criterion for CR-CP-12: the criteria below are written from the rule's text. `design/gdd/networking-session.md` — CR-NET-6.4 step 2 (reconnect reconciliation: store first, then `AddGold`).
**Requirement**: `TR-cp-012`

**ADR Governing Implementation**: ADR-001 (Purchase Transaction Integrity) Decisions 2–4 and **Amendment A2 (2026-10-26)**; ADR-006 Decision 3 (a separate `pending_purchases` table; the INSERT and the gold UPDATE in one transaction) and its 2026-10-26 note.
**ADR Decision Summary**: the Currency System's in-memory balance is the authority during a session. The caller debits in memory with `TrySpendGold` first, then `BeginPurchase` records the purchase and writes the resulting balance and gold version as absolute values in one write, without advancing `save_version`. A refund runs `RefundPurchase` (delete + absolute write) first and credits memory only on its success. Nothing is ever stored as `Refunded` or `Completed`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved (plain C#, `IronGrind.ServerLogic`).

**Control Manifest Rules (this layer)**:
- Required: PostgreSQL is the sole persistence store — ADR-006 (the in-memory store of this story is a test and no-database stand-in, not a second store)
- Required: `ConfigureAwait(false)` on every `await` in the persistence layer — ADR-011
- Required: `PendingPurchase` record and the durable gold write in one store write; refunds store first — ADR-001 Amendment A2 (manifest lines 47 and 68, reworded 2026-10-26)

---

## Acceptance Criteria

- [ ] **Signatures (ADR-001 A2)**: `ICharacterPersistence.BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct)` returning `Task<PendingPurchaseBeginResult>` (new readonly struct: `Result`, `PurchaseId` — 0 unless `Success`) and `RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct)` replace today's signatures (`refundAmount` is removed); `CompletePurchase` and `LoadOutstandingPurchases` keep theirs. `CharacterPersistenceService` implements all four (no `NotSupportedException` left). Doc comments state the caller's order (debit in memory first; refund store first).
- [ ] **Store seam**: a new sibling interface `IPendingPurchaseStore` (`src/ServerLogic/CharacterPersistence/IPendingPurchaseStore.cs`) holds the four store operations; `ICharacterRecordStore` is unchanged. The in-memory implementation shares its data with `InMemoryCharacterRecordStore` (one class implements both, or the purchase store takes the record store), so a purchase write and a `FetchAsync` see the same gold. `CharacterPersistenceService` takes the purchase store through its constructor. Story 015 implements the interface in SQL.
- [ ] **`BeginPurchase` — success**: for a stored character, one record is created in state `GoldDebited` with a store-generated id (> 0, unique), the stored `GoldBalance` and `GoldVersion` become exactly `goldBalance` and `goldVersion`, the stored `SaveVersion` is unchanged, and `Success` is returned.
- [ ] **`BeginPurchase` — no balance check**: the store does not compare `goldBalance` with `TotalCost` or with the stored balance (a `goldBalance` above the stored balance is written as given) and never returns `InsufficientGold` or `ConcurrencyConflict`.
- [ ] **`BeginPurchase` — unknown character**: no stored record for `charId` → `DatabaseError`, no purchase record, nothing written.
- [ ] **`BeginPurchase` — atomicity**: with the new fault point `FailNextAfterInsert` armed (the in-memory store fails after staging the record and before the gold write), neither the record nor the gold write is applied and `DatabaseError` is returned. `FailNext` (whole operation) also gives `DatabaseError` with nothing applied.
- [ ] **`CompletePurchase`**: removes the record and returns `Success`; the stored gold, gold version and save version are unchanged.
- [ ] **`RefundPurchase`**: removes the record and sets the stored `GoldBalance` / `GoldVersion` to exactly `goldBalance` / `goldVersion` in one write; `SaveVersion` unchanged; `Success`. With `FailNextAfterInsert` (or `FailNext`) armed, the record stays `GoldDebited`, the gold is unchanged and `DatabaseError` is returned.
- [ ] **Idempotent on a missing id**: `CompletePurchase` and `RefundPurchase` for an id that does not exist (or was already removed) return `Success` and change nothing — `RefundPurchase` writes no gold in that case.
- [ ] **`RefundPurchase` — wrong character**: a `purchaseId` that exists but belongs to another character returns `CharacterNotFound` and changes nothing.
- [ ] **`LoadOutstandingPurchases`**: returns every record of the character in state `GoldDebited`, and none of another character; an empty list, not null, when there is none.
- [ ] **Outside the character record**: `LoadCharacter`, `SaveSession` and `SaveIrreversibleOutcome` neither read nor write pending purchases; a pending record survives a `SaveSession`. A `SaveSession` after a `BeginPurchase` (with the `SaveVersion` loaded before the purchase) succeeds — the purchase did not advance it.
- [ ] **Cancellation and threading**: a cancelled token returns `DatabaseError` with nothing applied; every `await` uses `ConfigureAwait(false)` (ADR-011); the in-memory store is safe to call from several threads (same locking as `InMemoryCharacterRecordStore`).

---

## Implementation Notes

- Caller order (for the NPC Shop and reconnect reconciliation, not implemented here — ADR-001 A2): buy = `TrySpendGold` → `BeginPurchase(balanceAfter, versionAfter)` → on failure `AddGold(CompensatingRefund)`; refund = compute cap-safe `balance + totalCost` and `version + 1` → `RefundPurchase` → on `Success` `AddGold(CompensatingRefund)`. Put this order in the `ICharacterPersistence` doc comments with an `<example>`.
- `PendingPurchaseResult.InsufficientGold` and `ConcurrencyConflict` stay in the enum (wire and log stability); update their doc comments to say the store no longer returns them (ADR-001 A2).
- `PendingPurchaseRecord.Id` is 0 before insert; the store assigns ids from a counter starting at 1 (the SQL store uses `BIGSERIAL`). `BeginPurchase` did not return the id before 2026-10-26; the caller needs it for `CompletePurchase` / `RefundPurchase`, so it now returns `PendingPurchaseBeginResult` (Signatures criterion; result-struct style of Story 001).
- The in-memory store's fault points: `FailNext` (existing) fails the whole call before any change; `FailNextAfterInsert` (new) fails between the two halves of `BeginPurchase` and of `RefundPurchase` (after the record change is staged, before the gold write) and rolls the staged change back. Both are test and diagnostic seams like the existing ones.
- Gold values are written as given: `goldBalance` above `GOLD_CAP` is the caller's error; the store does not clamp (Currency already did).

---

## Out of Scope

- The NPC Shop's `PurchaseRequest` / `PickupRequest` handlers and the in-memory `TrySpendGold` / `AddGold` calls around the store (ADR-001; NPC Shop epic)
- Whether a purchase holds the character mutation gate, and tracking the returned tasks from tick code (ADR-011) — the NPC Shop's handler design (former open decision 2)
- Reconnect reconciliation wiring (session epic, CR-NET-6.4 step 2)
- Story 015: the SQL and the shared `NpgsqlTransaction`
- A `PendingSell` record (ADR-001 OQ-ADR1-2)

---

## Performance

No tick-path cost: the methods return tasks and run off the tick (ADR-011); the in-memory store is O(records of the character) for `LoadOutstandingPurchases` and O(1) for the others. No per-tick allocation is added.

---

## QA Test Cases

- **Begin** — Stored balance 500, version 4, save version 7; `BeginPurchase(record cost 200, goldBalance 300, goldVersion 5)` → `Success`, id > 0, stored 300 / 5, save version still 7, one `GoldDebited` record. Edge: `goldBalance 0` → stored 0. Edge: `goldBalance 600` (above stored) → stored 600 (no check).
- **Unknown character** — no record → `DatabaseError`, no purchase record.
- **Atomicity** — `FailNextAfterInsert` → no record, stored 500 / 4, `DatabaseError`; `FailNext` → same.
- **Complete** — Then the record is gone, gold unchanged; again with the same id → `Success`.
- **Refund** — After Begin (300 / 5): `RefundPurchase(id, goldBalance 500, goldVersion 6)` → record gone, stored 500 / 6, save version 7; again → `Success`, stored still 500 / 6. Fault armed → record still outstanding, stored 300 / 5, `DatabaseError`.
- **Wrong character** — A's purchase id refunded as B → `CharacterNotFound`, A's record and gold unchanged.
- **Outstanding** — Two records for A, one for B → A's list has two; C → empty list.
- **Independence** — A pending record, then `SaveSession` with the pre-purchase save version → `Success`, the record is still outstanding.
- **Cancellation** — Cancelled token on each method → `DatabaseError`, nothing applied.
- **Ids** — Three Begins → three distinct increasing ids.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_PendingPurchase_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002 (Complete)
- Unlocks: Story 015; the NPC Shop epic
