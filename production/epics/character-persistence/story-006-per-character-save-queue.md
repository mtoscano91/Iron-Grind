# Story 006: Per-Character Save Queue

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (2 open decisions, listed below)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-7 (at most one save in flight per `CharacterID`; a second call is queued, never run in parallel; enforced by the persistence layer); AC-CP-21.
**Requirement**: `TR-cp-007`

**ADR Governing Implementation**: ADR-006 (the service-layer per-`CharacterID` async queue; the database gives no independent enforcement); ADR-011 Decision 3.
**ADR Decision Summary**: the service serialises writes per character; continuations never run inline on the completing thread.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — `System.Threading.Tasks` only.

**Control Manifest Rules (this layer)**:
- Required: `ConfigureAwait(false)` on every `await` in the persistence layer — ADR-011
- Required: `TaskCreationOptions.RunContinuationsAsynchronously` on every `TaskCompletionSource` — ADR-011
- Forbidden: blocking the tick on a persistence write — ADR-011

---

## Acceptance Criteria

*"The queue" is the component of the service that orders writes; a "write" is one store operation handed to it.*

- [ ] **AC-CP-21**: two writes for the same `CharacterID`, the first held open, never overlap: the second's store operation starts only after the first has completed. A recording spy of `(CharacterID, started, completed)` shows no overlap; both complete.
- [ ] **Order**: three writes queued for one character run in the order they were queued.
- [ ] **Different characters do not wait for each other**: with character A's write held open, character B's write starts and completes.
- [ ] **A failed write does not stop the queue**: when the first write faults or is cancelled, the second still runs, and each caller gets its own result.
- [ ] **Enqueue never blocks**: handing a write to the queue returns a task at once, on the calling thread, whatever is in flight.
- [ ] **No inline continuation**: completing the first write on a pool thread does not run the second write's start on the thread that completed it synchronously inside the completion call (asserted with a `TaskCompletionSource` created as ADR-011 requires).
- [ ] **No leak**: after a character's queue is empty its bookkeeping is removed; 1,000 characters each writing once leave no entry behind.
- [ ] **Cancellation**: a write whose token is cancelled while it waits in the queue completes as cancelled without calling the store, and the write behind it runs.

---

## Implementation Notes

- AC-CP-21 asks for an "`ISaveCoordinator` spy interface defined in the persistence layer": the spy seam is the store (Story 002 records start and completion), unless readiness decides a separate interface is worth having.
- ADR-011's `ICharacterMutationGate.Close` throws when the gate is already closed, which already prevents two *irreversible* writes for one character. This queue is the wider rule: a `SaveSession` arriving while an irreversible write is in flight (a TTL expiry during an enhancement commit) waits its turn.
- The queue orders store operations; it holds no game state.

**Open decisions for `/story-readiness`**

1. **What is queued — and when the snapshot is taken.** CR-CP-10 lists "acquire the save slot" as step 1, before the reads of steps 2–6. ADR-011 Decision 3 requires the snapshot before the first `await`, so a save that waits in the queue has already copied its state — it would write state as of the call, not as of its turn. With two queued saves the second then carries an expected `SaveVersion` that the first has already advanced. Decide: the queue passes the version forward (each queued write uses the version the previous one produced), or the second write is allowed to fail with `ConcurrencyConflict` as EC-CP-4 and AC-CP-20 describe for a doubled `SaveSession`. AC-CP-21 expects versions N+1 then N+2; AC-CP-20 expects a conflict. Both cannot hold without a rule that tells the two cases apart.
2. **Bound.** Whether the queue has a maximum depth per character, and what a full queue returns.

---

## Out of Scope

- Stories 007, 008: what a save reads and writes
- Story 014: the same invariant against PostgreSQL (50 concurrent calls, ADR-006's concurrency gate)

---

## QA Test Cases

- **No overlap** — Given write 1 held open, When write 2 is queued, Then write 2's start is recorded only after write 1's completion.
- **Order** — Three writes → started 1, 2, 3.
- **Independence** — A held open → B completes.
- **Failure isolation** — Write 1 faults → write 2 runs and succeeds; write 1's caller sees the fault.
- **Cancellation in the queue** — Write 2 cancelled while waiting → store not called for it; write 3 runs.
- **No leak** — 1,000 characters → internal entry count 0 at the end.
- **Edge** — a write queued from inside the completion of another.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveQueue_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 007, 008
