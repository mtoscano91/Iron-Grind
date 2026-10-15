# Story 008: `SaveIrreversibleOutcome`

> **Epic**: Character Persistence
> **Status**: Ready — written 2026-10-12; both open decisions settled at `/story-readiness` 2026-10-15 (GDD lines written); TR-IDs registered 2026-10-15; readiness re-run 2026-10-15 (NEEDS WORK: two stale lines, fixed)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (6 as written, plus the unknown-version mark in the write path `SaveSession` shares)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (the four irreversible triggers; the whole record is written); CR-CP-6 (one transaction, the version predicate, CRITICAL on a conflict, the re-read after an unknown outcome); CR-CP-7; CR-CP-10 steps 2–7; EC-CP-5; AC-CP-19, AC-CP-21, AC-CP-32 to AC-CP-34. `design/gdd/enhancement-system.md` — CR-ENH-11 (the transaction commits before any result message).
**Requirement**: `TR-cp-004`, `TR-cp-006`

**ADR Governing Implementation**: ADR-006 (Decision 4: ≤ 50 ms P95, the thresholds, the 5-second timeout); ADR-011 Decisions 3 and 5.
**ADR Decision Summary**: the method awaits the write and its rows-affected check before it completes; the caller — the irreversible-outcome coordinator — delivers nothing until the result is `Success`, and never retries.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: snapshot before the first `await`; `ConfigureAwait(false)` on every `await` — ADR-011
- Forbidden: blocking the tick on a persistence write — ADR-011
- Guardrail: warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s (`PERSISTENCE_WRITE_TIMEOUT_SECONDS`), below the 10-second watchdog — ADR-006, ADR-011

---

## Acceptance Criteria

- [ ] **Success**: with the store available, `SaveIrreversibleOutcome(id, trigger, ct)` returns `Success` for each of the four triggers; the stored `SaveVersion` is the previous value + 1, and the stored record holds the state the caller had applied in memory when it made the call.
- [ ] **AC-CP-32 (the whole record)**: after gold, inventory and `CurrentHP` changed since the load with no save, `SaveIrreversibleOutcome(id, LevelUp, ct)` makes one store update whose record holds all 23 fields as they were in memory at the call (the new level, gold balance and version, inventory, HP); the record is the same for every trigger.
- [ ] **AC-CP-21 (version at its turn)**: two `SaveIrreversibleOutcome` calls for one character, the first held open, both return `Success` with the stored `SaveVersion` N + 1 then N + 2; the expected version is read from the session entry at the write's turn in the queue.
- [ ] **Snapshot before the first await**: a change to game state made after the call returns its task is not in the written record.
- [ ] **The task completes only after the store has confirmed**: with the store held open the task is not complete; it completes when the store is released.
- [ ] **AC-CP-19 (in-memory store)**: with the row's `SaveVersion` advanced behind the service before the write, the result is `ConcurrencyConflict` and exactly one CRITICAL log is written. No retry.
- [ ] **Store failure**: a faulting store gives `DatabaseError`; the task itself does not fault.
- [ ] **Timeout**: a store update not complete after `PERSISTENCE_WRITE_TIMEOUT_SECONDS` gives `DatabaseError` (injected short timeout).
- [ ] **Cancellation**: when the caller's token is cancelled (the watchdog), the task completes with `DatabaseError` and does not fault or complete as cancelled (the same as `SaveSession`); the service's own state for that character stays usable.
- [ ] **AC-CP-33 (unknown outcome that committed)**: an irreversible write times out (injected short timeout) but the store commits it (N → N + 1); the in-memory state is rolled back; then `SaveSession(id, SessionTTLExpiry, ct)` returns `Success`, the stored record holds the rolled-back state, `SaveVersion` = N + 2, one WARNING for the re-read, no CRITICAL from the second call.
- [ ] **AC-CP-34 (unknown outcome that did not commit)**: the same, but the timed-out write never commits: `SaveSession` returns `Success`, `SaveVersion` = N + 1, one WARNING for the re-read.
- [ ] **The mark (CR-CP-6)**: a `DatabaseError` after the store update started (timeout, cancel, store fault) marks the version unknown, for both save methods; the next write fetches the stored `SaveVersion` at its turn; a fetch failure gives `DatabaseError` with nothing written and the mark kept; `Success` clears the mark; a `ConcurrencyConflict`, and a save cancelled while still waiting in the queue, do not set it.
- [ ] **Unknown character**: `CharacterNotFound` without a store call.
- [ ] **Queue (CR-CP-7)**: a `SaveSession` issued while an irreversible write is in flight starts its store operation only after it.
- [ ] **Latency log**: ≥ 50 ms a warning, ≥ 100 ms a critical alert (injected clock).
- [ ] **The method disconnects no one and reverts nothing**: CR-CP-5's protocol belongs to the caller.

---

## Implementation Notes

- **Version rule, settled at Story 006 readiness (2026-10-15):** `SaveIrreversibleOutcome` reads the **latest** `SaveVersion` **at its turn** in Story 006's queue, so two irreversible writes for one character give N+1 then N+2 — AC-CP-21's version clause, moved here from Story 006 and now in the criteria. The expected version is the session-table entry's `SaveVersion` (Story 007: `CharacterID → { EntityID, identity fields, gear, SaveVersion }`, filled by `ApplyLoadedRecord`), read at this write's turn in the queue and advanced only on `Success`; after an unknown outcome the next write re-reads the stored value instead (decision 2).
- "Session aborted" in AC-CP-19 is the caller's act (the coordinator's failure branch), not this method's.
- After a failed or cancelled write the outcome in the database is unknown (ADR-011: "cancelling the token does not abort a command already running"). The in-memory expected version must not be advanced on anything but `Success`; what a later save then meets is decision 2 below.

**Decisions at `/story-readiness` (2026-10-15; GDD lines written the same day)**

1. **What `SaveIrreversibleOutcome` writes: the whole record** (CR-CP-4). The same snapshot as `SaveSession` — CR-CP-10 steps 2–6, taken on the caller's thread before the queue — and the same single `UPDATE` of all 23 fields (step 7). No HP override. `trigger` changes only the log lines. Reuse Story 007's snapshot and write code; do not write a second copy. Epic conflict 2.
2. **After an unknown outcome: re-read once** (CR-CP-6, EC-CP-5). A `DatabaseError` whose store update had started marks the session entry's version unknown. The next write, of either method, at its turn in the queue, fetches the stored `SaveVersion` with `ICharacterRecordStore.FetchAsync` (under the write timeout and the token), uses it as the expected version and logs one WARNING. This changes the write path `SaveSession` already uses (Story 007's code and its "stale version" remark on `SaveSession`). The mark lives in the session table; Story 010 removes it with the entry. Epic conflict 7.

---

## Out of Scope

- Story 009: the coordinator's persist step, CR-CP-5, AC-CP-13 / 14 / 17
- Enhancement Story 011: AC-CP-27
- Story 014 and Story 016: the same against PostgreSQL, the P95 gate

---

## QA Test Cases

- **Each trigger** — Given a loaded character, When saved with each of the four triggers, Then `Success` and version + 1.
- **Held store** — Given the store held, Then `IsCompleted` is false; When released, Then `Success`.
- **Conflict** — Given the row advanced behind the service, Then `ConcurrencyConflict`, critical log count 1, warning count 0.
- **Failure / timeout** — Faulting store → `DatabaseError`; never-completing store with a short timeout → `DatabaseError`.
- **Version after failure** — Given a failed write, Then the in-memory expected version is unchanged.
- **Queue** — Irreversible write held, `SaveSession` queued → no overlap in the store's record.
- **Edge** — two irreversible saves in a row (N + 1, N + 2).
- **Whole record** — Given gold, inventory and HP changed after the load, When `SaveIrreversibleOutcome(LevelUp)`, Then the stored record matches memory in all 23 fields.
- **Timeout that committed** — Given an in-memory store that commits after the injected timeout, When the irreversible save times out and `SaveSession(SessionTTLExpiry)` follows, Then `Success`, version N + 2, one re-read WARNING.
- **Timeout that did not commit** — Same, store never commits: `Success`, version N + 1, one re-read WARNING.
- **Fetch failure** — Given the mark set and a faulting fetch, Then `DatabaseError`, no update, and the following write fetches again.
- **No mark** — A `ConcurrencyConflict`, or a save cancelled while waiting in the queue, followed by a save: no fetch.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveIrreversible_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 006, 007
- Unlocks: Story 009; **Enhancement Story 011** (the real `SaveIrreversibleOutcome`)
