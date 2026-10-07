# Story 011: Commit Orchestration

> **Epic**: Enhancement System
> **Status**: Blocked — **three things must exist first: (1) a decision, probably an ADR, on how server tick-loop code consumes an asynchronous persistence call; (2) the Character Persistence implementation of `SaveIrreversibleOutcome`; (3) for the client-facing criteria, the TD-046 wire-protocol amendment.**
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: not estimated — depends on the decision in blocker 1

> **Created 2026-10-07 at Story 005's readiness.** Split out of the original Story 005 ("Commit-Then-Deliver and Rollback") when the service became two-phase. Story 005 keeps the service-side rollback; this story is whoever stands between `BeginAttempt` and `CompleteAttempt` / `RollBackAttempt`.

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-11 (commit-then-deliver), CR-ENH-15 step 6b, Transaction boundary, Rollback step 4, EC-ENH-1, EC-ENH-2, EC-ENH-6, and the commit halves of AC-ENH-13, AC-ENH-23, AC-ENH-34 and AC-ENH-35. `design/gdd/character-persistence.md` — `SaveIrreversibleOutcome(CharacterID, IrreversibleOutcomeTrigger, CancellationToken): Task<CharacterSaveResult>`, CR-CP-5 (failure protocol: caller-owned rollback, client disconnect, session preserved), CR-CP-6 (one transaction), CR-CP-7 (at most one write in flight per character). `design/gdd/networking-core.md` — CR-NET-5 (commit-before-broadcast).
**Requirement**: `TR-enh-006` (commit side)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-006: Persistence Layer (Accepted — every persistence call is `async Task<>`; one transaction per irreversible outcome; write budget ≤ 50 ms P95). **Missing**: no ADR says how game logic on the server tick consumes that asynchronous result — where the continuation runs, and what the tick does while the write is in flight.
**ADR Decision Summary**: An irreversible outcome is written in a single transaction and confirmed before any client-visible result is sent; on a failed write the caller restores its own in-memory changes.

**Engine**: Unity 6.3 LTS | **Risk**: to be set by the decision (an `async`/`await` path under Unity's synchronization context is a known hazard in EditMode tests and on a headless server)
**Engine Notes**: To be filled in when blocker 1 is decided.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010; R-OD messages with a `requestId` use `(charId, messageType, requestId)` as the dedup key — ADR-001 Amendment A1 (applies if the amended request carries one)
- Forbidden: no `EventBus` class — ADR-010

---

## What is blocking

1. **How the tick loop consumes an asynchronous commit.** `EnhancementService` is deliberately synchronous: `BeginAttempt` → *commit* → `CompleteAttempt` or `RollBackAttempt`. Something has to run the commit in between. What exists and what conflicts:
   - ADR-006 and `character-persistence.md` define the commit as `Task<CharacterSaveResult>`.
   - Networking Core's `CommitBeforeBroadcastSequencer.Execute` already implements validate → acknowledge → compute → persist → broadcast with revert, disconnect and session preservation — but its `persistOutcome` is a synchronous `Func<TOutcome, bool>`, so it would block the tick for the write (budget ≤ 50 ms P95 against a 50 ms tick).
   - Candidates: (a) an orchestrator over the existing synchronous sequencer (blocks the tick during the write); (b) an asynchronous overload of the sequencer, with a stated rule for where the continuation runs; (c) a small completion queue drained on the tick thread, so the write runs off-thread and `CompleteAttempt` / `RollBackAttempt` are called on the tick.
   - The same question applies to the other callers of `SaveIrreversibleOutcome` (level-up, respec, item consumption) and to OQ-ENH-7 (which layer holds a character's other requests while the write is in flight). It should be decided once — `/architecture-decision`.
2. **Character Persistence is not implemented** and has no epic. This story can be tested against a fake of its interface, but it cannot be closed against the real one.
3. **TD-046** blocks the criteria that mention what a client receives. If this story is wanted before TD-046 closes, those criteria can stay with Story 010 and this story can end at "the result is handed to the delivery seam only after a successful commit".

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. To be re-checked and sharpened at `/story-readiness` once the blockers clear.*

- [ ] **One commit per attempt (AC-ENH-35)**: for a pending attempt the commit is called exactly once, with trigger `EnhancementResult`; no commit is called for a rejected request.
- [ ] **The committed record holds the outcome (AC-ENH-35)**: the bag the commit reads shows the outcome and the consumed scroll (Story 005 already verifies the bag state during the pending window; this criterion verifies the commit reads it then, not earlier or later).
- [ ] **Success path**: when the commit reports `Success`, `CompleteAttempt` is called and its result is handed on for delivery — and not before.
- [ ] **Failure path (AC-ENH-23, AC-ENH-34)**: when the commit reports any non-success code (`CharacterNotFound`, `ConcurrencyConflict`, `DatabaseError`), `RollBackAttempt` is called; no attempt result and no +9 broadcast are produced; the client is disconnected and the rolled-back session is preserved for `SESSION_TTL_SECONDS` (CR-CP-5).
- [ ] **No retry**: a failed commit is not retried (networking-core.md CR-NET-5.5 — irreversible writes are not idempotent).
- [ ] **A commit that throws** is treated as a failure (the existing sequencer already does this; state the rule for whichever mechanism is chosen).
- [ ] **AC-ENH-13 (disconnect after commit)**: the connection drops after the commit succeeds and before the result is delivered → the outcome stands (item at its new level, scroll gone, slot unlocked) and the result is not re-sent on reconnect.
- [ ] **EC-ENH-2 (disconnect while the write is in flight)**: the attempt is not aborted; the write runs to completion and the outcome stands if it succeeds.
- [ ] **Write in flight**: while the commit has not returned, `IsAttemptInProgress` is true and the item slot is locked (Story 005 verifies the lock; this criterion verifies it across a real in-flight write).

---

## Implementation Notes

- **Nothing to implement until blocker 1 is decided.** What already exists for this story to build on: `EnhancementService.BeginAttempt` / `CompleteAttempt` (Story 004), `RollBackAttempt` (Story 005), `IsAttemptInProgress`, and Networking Core's `CommitBeforeBroadcastSequencer` and `CommitBeforeBroadcastResult`.
- The service raises its outcome events from `CompleteAttempt` (Story 007). Because this story calls `CompleteAttempt` only after a successful commit, "nothing is announced before the commit" follows from the call order — verify it here end to end.
- A pending attempt whose character is unregistered or whose session ends is this story's to define (the pending record lives in `EnhancementService`; today nothing clears it except `CompleteAttempt` and `RollBackAttempt`).
- `SESSION_TTL_SECONDS` is currently a provisional constant on `CommitBeforeBroadcastSequencer`.
- When the decision is recorded, re-run `/story-readiness` on this file and replace this section with concrete guidance.

---

## Out of Scope

- Story 004 / Story 005: the service's own behaviour in each phase and its rollback
- Story 007: the events themselves
- Story 009: holding other inventory-mutating requests during the write (AC-ENH-38) — likely decided by the same ADR
- Story 010: wire handlers and message encoding
- Character Persistence epic: the real `SaveIrreversibleOutcome`, CR-CP-5's disconnect and session preservation, AC-ENH-2

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs` (to be created). The cases state what must be observable; the setup depends on the chosen mechanism.

- **One commit, right trigger** — pending attempt → the persistence fake records exactly one call with `EnhancementResult`.
- **No commit on rejection** — tier mismatch → zero calls.
- **Snapshot at commit time** — the fake captures `ExportSnapshot` when called: success run slot 0 at level 3 and one scroll fewer; destruction run no slot 0 entry.
- **Success** — fake completes with `Success` → result `{SUCCESS, 3, Success}` handed to the delivery seam once, after completion; slot unlocked.
- **Each failure code** — `CharacterNotFound`, `ConcurrencyConflict`, `DatabaseError` (`[TestCase]`) → bag restored, `CriticalEnhancementWriteFailed` logged once, zero results delivered, disconnect and session-preserve seams each called once.
- **No retry** — after a failure the fake has exactly one call.
- **Commit throws** — treated as a failure, same assertions.
- **AC-ENH-13** — delivery seam reports the client gone → bag at the committed state; no later delivery attempt.
- **Write in flight** — fake held open: `IsAttemptInProgress` true, item slot locked, a second `BeginAttempt` → `RejectedConcurrentAttempt`; complete the fake → normal success.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_CommitOrchestration_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **the tick-loop/async-persistence decision (blocking)**; **Character Persistence implementation (blocking for closure)**; Story 005 (`RollBackAttempt`); Story 007 for the event-order check; TD-046 for the client-facing criteria
- Unlocks: Story 009 (shares the decision), Story 010 (result delivery)
