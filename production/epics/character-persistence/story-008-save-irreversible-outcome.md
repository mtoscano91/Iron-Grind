# Story 008: `SaveIrreversibleOutcome`

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (2 open decisions, listed below — the first is a gap in the GDD)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (the four irreversible triggers); CR-CP-6 (one transaction, the version predicate, CRITICAL on a conflict); CR-CP-7; AC-CP-19. `design/gdd/enhancement-system.md` — CR-ENH-11 (the transaction commits before any result message).
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
- [ ] **Snapshot before the first await**: a change to game state made after the call returns its task is not in the written record.
- [ ] **The task completes only after the store has confirmed**: with the store held open the task is not complete; it completes when the store is released.
- [ ] **AC-CP-19 (in-memory store)**: with the row's `SaveVersion` advanced behind the service before the write, the result is `ConcurrencyConflict` and exactly one CRITICAL log is written. No retry.
- [ ] **Store failure**: a faulting store gives `DatabaseError`; the task itself does not fault.
- [ ] **Timeout**: a store update not complete after `PERSISTENCE_WRITE_TIMEOUT_SECONDS` gives `DatabaseError` (injected short timeout).
- [ ] **Cancellation**: when the caller's token is cancelled (the watchdog), the task completes as cancelled or with `DatabaseError` — one of the two, stated in the doc comment — and the service's own state for that character stays usable.
- [ ] **Unknown character**: `CharacterNotFound` without a store call.
- [ ] **Queue (CR-CP-7)**: a `SaveSession` issued while an irreversible write is in flight starts its store operation only after it.
- [ ] **Latency log**: ≥ 50 ms a warning, ≥ 100 ms a critical alert (injected clock).
- [ ] **The method disconnects no one and reverts nothing**: CR-CP-5's protocol belongs to the caller.

---

## Implementation Notes

- **Version rule, settled at Story 006 readiness (2026-10-15):** `SaveIrreversibleOutcome` reads the **latest** `SaveVersion` **at its turn** in Story 006's queue, so two irreversible writes for one character give N+1 then N+2 — AC-CP-21's version clause, moved here from Story 006 (add it as a criterion at this story's readiness). Where "the latest version" lives (an in-memory per-character version the service updates after each successful write, or a fetch at its turn) is for this story's readiness. *Story 007 readiness (2026-10-15): the service now keeps a session table — `CharacterID → { EntityID, identity fields, gear, SaveVersion }`, filled by `ApplyLoadedRecord`, advanced after each successful write — so "the latest version" can be read from that entry at this write's turn in the queue.*
- "Session aborted" in AC-CP-19 is the caller's act (the coordinator's failure branch), not this method's.
- After a failed or cancelled write the outcome in the database is unknown (ADR-011: "cancelling the token does not abort a command already running"). The in-memory expected version must not be advanced on anything but `Success`; what a later save then meets is in open decision 2.

**Open decisions for `/story-readiness`**

1. **What `SaveIrreversibleOutcome` writes.** CR-CP-10 gives the step sequence of `SaveSession` only; no rule of the GDD says which fields an irreversible save reads and writes, and the `trigger` parameter carries no payload. Either it writes the whole record exactly as `SaveSession` does (one code path, and mid-session gold is persisted too), or a subset per trigger. CR-CP-4's "one atomic write for the final state" and AC-CP-17 read like the whole record. This needs a line in the GDD.
2. **After an unknown outcome.** If a write timed out but in fact committed, the next save's expected version is one behind and it conflicts — at the TTL-expiry `SaveSession` that CR-CP-5 relies on to keep the rolled-back state. Decide whether the service re-reads the version after a timeout.

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

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveIrreversible_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 006, 007
- Unlocks: Story 009; **Enhancement Story 011** (the real `SaveIrreversibleOutcome`)
