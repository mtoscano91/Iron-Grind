# Story 006: Per-Character Save Queue

> **Epic**: Character Persistence
> **Status**: Ready — `/story-readiness` 2026-10-15 (NEEDS WORK → decisions taken, see "Readiness decisions")
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-7 (at most one save in flight per `CharacterID`; a second call is queued, never run in parallel; enforced by the persistence layer); AC-CP-21 (the no-overlap half — its version half moved to Story 008, readiness decision 1).
**Requirement**: `TR-cp-007`
*(Epic placeholder: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness`, as for Stories 001–004.)*

**ADR Governing Implementation**: ADR-006 (the service-layer per-`CharacterID` async queue; the database gives no independent enforcement); ADR-011 Decision 3.
**ADR Decision Summary**: the service serialises writes per character; continuations never run inline on the completing thread.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — `System.Threading.Tasks` only.

**Control Manifest Rules (this layer)**:
- Required: `ConfigureAwait(false)` on every `await` in the persistence layer — ADR-011
- Required: `TaskCreationOptions.RunContinuationsAsynchronously` on every `TaskCompletionSource` — ADR-011
- Forbidden: blocking the tick on a persistence write — ADR-011

---

## Readiness decisions (2026-10-15)

1. **The queue is generic; the version rule is per method.** The queue runs a write function when its turn comes and knows nothing about `SaveVersion`. Which version a write expects is decided by the method that builds it (the GDD separates the two cases by trigger):
   - `SaveSession` (Story 007) takes its expected version **at call time**, so a second `SaveSession` queued behind the first conflicts — EC-CP-4, AC-CP-20, CR-CP-6 (WARNING).
   - `SaveIrreversibleOutcome` (Story 008) reads the **latest** version **at its turn**, so two irreversible writes give N+1 then N+2 — AC-CP-21's version clause, which moves to Story 008.
   This resolves epic design conflict 1.
2. **No bound.** No maximum depth per character: writes per character are rare (session end, irreversible outcomes, purchases), `ICharacterMutationGate` allows one irreversible write at a time, and every write has a 5 s timeout (`PERSISTENCE_WRITE_TIMEOUT_SECONDS`, ADR-006), so a queue cannot grow without limit behind a hung write. Stated in the class remarks.
3. **A public queue with a function seam; no `ISaveCoordinator`.** New `public sealed class CharacterWriteQueue` in `src/ServerLogic/CharacterPersistence/`:
   - `Task<T> Enqueue<T>(CharacterID id, Func<CancellationToken, Task<T>> write, CancellationToken ct)` — returns at once; `write` is started only after every write queued before it for the same `id` has completed (in any state);
   - `int ActiveCharacterCount { get; }` — characters with a write queued or in flight (for the no-leak criterion).
   Public because the test assembly sees only public types of `IronGrind.ServerLogic` (no `InternalsVisibleTo`). Tests record start and completion inside `write` — the spy AC-CP-21 calls `ISaveCoordinator` is not needed (epic design conflict 5, in part). The service does not use the queue yet: Stories 007 and 008 wire it in.

---

## Acceptance Criteria

*"A write" is one `write` function handed to `Enqueue`.*

- [ ] **AC-CP-21 (no overlap)**: two writes for the same `CharacterID`, the first held open, never overlap: the second's function starts only after the first's task has completed. A recording of `(CharacterID, started, completed)` shows no overlap; both complete.
- [ ] **Order**: three writes queued for one character start in the order they were queued.
- [ ] **Different characters do not wait for each other**: with character A's write held open, character B's write starts and completes.
- [ ] **A failed write does not stop the queue**: when the first write faults or is cancelled, the second still runs, and each caller's task carries its own outcome (the first faulted / cancelled, the second its result).
- [ ] **A write function that throws synchronously** is treated like a faulted write: its caller's task faults, and the next write runs.
- [ ] **Enqueue never blocks**: `Enqueue` returns a task at once, on the calling thread, whatever is in flight, and does not run `write` inline when something is already queued for that character.
- [ ] **No inline continuation**: completing the first write's `TaskCompletionSource` (created with `RunContinuationsAsynchronously`) does not start the second write synchronously inside the `SetResult` call.
- [ ] **No leak**: once a character's last write has completed, `ActiveCharacterCount` drops; 1,000 characters each writing once end at 0.
- [ ] **Cancellation**: a write whose token is cancelled while it waits in the queue completes as cancelled without its function being called, and the write behind it runs.
- [ ] **Doc comments** on the class, `Enqueue` (including the no-bound and the version-is-the-caller's notes) and `ActiveCharacterCount`.

---

## Implementation Notes

- One possible shape: a dictionary `CharacterID → Task` (the tail of that character's chain) under a lock; `Enqueue` chains the new write after the tail (awaiting the previous task without observing its outcome) and replaces the tail; when the write completes and it is still the tail, the entry is removed. Any shape that meets the criteria is fine.
- `ConfigureAwait(false)` on every `await`; `RunContinuationsAsynchronously` on any `TaskCompletionSource`.
- The queue orders store operations; it holds no game state and reads none.
- `ICharacterMutationGate.Close` (ADR-011) already prevents two *irreversible* writes in flight for one character; this queue is the wider rule (a `SaveSession` arriving during an irreversible write waits its turn).

**Authorized files**: `src/ServerLogic/CharacterPersistence/CharacterWriteQueue.cs` (new), `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveQueue_tests.cs` (new).

---

## Out of Scope

- Stories 007, 008: what a save reads and writes, which `SaveVersion` it expects, and wiring the queue into the service
- AC-CP-21's version clause (N+1, N+2) — Story 008
- Story 014: the same invariant against PostgreSQL (50 concurrent calls, ADR-006's concurrency gate)

---

## QA Test Cases

- **No overlap** — Given write 1 held open, When write 2 is queued, Then write 2's start is recorded only after write 1's completion.
- **Order** — Three writes → started 1, 2, 3.
- **Independence** — A held open → B completes.
- **Failure isolation** — Write 1 faults → write 2 runs and succeeds; write 1's caller sees the fault. Same for write 1 cancelled, and for a write function that throws synchronously.
- **Cancellation in the queue** — Write 2 cancelled while waiting → its function is not called; write 3 runs.
- **No leak** — 1,000 characters → `ActiveCharacterCount` 0 at the end.
- **Edge** — a write queued from inside the completion of another.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveQueue_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (Complete)
- Unlocks: Stories 007, 008
