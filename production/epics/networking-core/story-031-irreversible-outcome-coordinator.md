# Story 031: Irreversible-Outcome Coordinator and Shared Failure Protocol

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 4 hours

*Added 2026-10-07, after Story 030. ADR-011 Migration step 2: the coordinator that splits the commit-before-broadcast sequence across ticks, built on the tick completion queue and the mutation gate of Story 030.*

## Context

**GDD**: `design/gdd/networking-core.md` — CR-NET-5.1–5.5 (commit before broadcast; acknowledgment before outcome; no retry of an irreversible write). `design/gdd/character-persistence.md` — CR-CP-5 (failure protocol: no broadcast → caller rollback → disconnect → critical alert → session preserved for `SESSION_TTL_SECONDS`), CR-CP-7 (one write in flight per character). `design/gdd/enhancement-system.md` — CR-ENH-11 / CR-ENH-15 step 6b (commit, then deliver), EC-ENH-2 (a disconnect during the write does not abort it).
**Requirement**: none in the epic's TR table — this story builds a component decided by ADR-011
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-011: Asynchronous Persistence in the Server Tick Loop — Decision 5 (irreversible-outcome coordinator), using Decision 2 (queue) and Decision 4 (gate).
**ADR Decision Summary**: `IrreversibleOutcomeCoordinator.Begin` runs on tick N: validate → close gate → acknowledge → compute and apply the outcome in memory → start the write → `Track`. Its completion callback runs on tick N+k: success → deliver, open gate; any non-success code, fault, cancellation or watchdog timeout → revert → disconnect → critical alert → preserve the session → open gate. No retry. The failure steps are the ones `CommitBeforeBroadcastSequencer` already implements; they are extracted so both share them.

**Engine**: Unity 6.3 LTS | **Risk**: LOW for this story
**Engine Notes**: Plain C# (`Task`, `CancellationTokenSource`); no Unity API except `Debug.LogError`. No `await` anywhere: the coordinator never observes the task itself, only the `TickTaskResult<T>` handed to its callback. ADR-011's four headless-build checks (Verification Required) are not part of this story; they gate shipping.
**Performance**: `Begin` is off the hot path (one per irreversible request). It allocates one `CancellationTokenSource`, one small state object and the queue's entry per call; nothing per tick. No performance impact on the tick beyond Story 030's `Drain`.

**Control Manifest Rules (Foundation layer — "Asynchronous persistence in the tick loop (ADR-011)")**:
- Required: tick-driven code calls a `Task`-returning persistence method and passes the returned task, in the same statement, to `ITickCompletionQueue.Track`
- Required: `ICharacterMutationGate` is closed by the coordinator before the outcome is applied in memory and opened after the result has been handled (success or rollback; opened in a `finally`)
- Forbidden: `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith`, `async void` in tick-driven code
- Forbidden: retrying a failed irreversible write — any non-success code, fault, cancellation or watchdog timeout takes the failure branch once
- Forbidden: the synchronous `CommitBeforeBroadcastSequencer.Execute` for a production caller of `SaveIrreversibleOutcome` (it stays only for a persist step that is genuinely synchronous)
- Forbidden: lambda captures for persistent event subscriptions (ADR-010); one-shot completion callbacks are exempt (ADR-011 Risks)

---

## Acceptance Criteria

*From ADR-011 Decision 5 and Validation Criteria, CR-NET-5 and CR-CP-5, scoped to this story:*

**Begin (tick N)**

- [x] **Sequence**: for a valid request on an open gate, `Begin` runs, in this order and once each: validate → `gate.Close(charId)` → acknowledge → compute-and-apply → start the write. It returns `Started`, the gate is held, and the write's task is tracked (`InFlightCount` rises by 1).
- [x] **Invalid request**: when validation returns false, `Begin` returns `RejectedInvalidRequest`; no acknowledgment, no computation, no write, and the gate is not closed.
- [x] **Write already in flight**: when the character's gate is already closed, `Begin` returns `RejectedWriteInFlight` and calls none of its delegates, validation included; the gate stays closed and no event is raised. *(ADR-011 is silent — review item P2; decided 2026-10-07. Queueing a second outcome is out of scope.)*
- [x] **Same outcome instance**: the value returned by compute-and-apply is the one passed to the write delegate and later to deliver.

**Completion (tick N+k)**

- [x] **Nothing before completion**: while the write's task is incomplete, any number of `Drain` calls invoke neither deliver nor any failure step, and the gate stays held.
- [x] **Success**: when the task completes and `isSuccess(result)` is true, the next `Drain` calls deliver exactly once with the outcome; no failure step runs; the gate is open afterwards and `OnGateOpened` was raised once.
- [x] **Failure protocol**: when the task completes with a result for which `isSuccess` is false, the next `Drain` runs, in this order and once each: revert → `disconnectClient(clientId, DisconnectReason.Other)` → critical alert (a `Debug.LogError`, and the test observer's `OnCriticalInfrastructureAlertFired`) → `preserveSessionForTtl(clientId, SESSION_TTL_SECONDS)`. Deliver is never called. The gate is open afterwards.
- [x] **Fault, cancellation and timeout take the same branch**: a faulted task, a cancelled task and a watchdog `TimedOut` result each run the failure protocol exactly once, with the gate open afterwards. A task that completes after its timeout causes no further call.
- [x] **No retry**: on every failure path the write delegate has been called exactly once.
- [x] **Gate held until the result is handled**: `gate.IsHeld(charId)` is true inside deliver and inside every failure step; `OnGateOpened` is raised after the last of them.
- [x] **Gate opens even when a delegate throws**: when deliver, or a failure step, throws, the gate is open after that `Drain`, and `Drain` does not throw (the queue logs the exception).
- [x] **Watchdog cancels the write's token**: the `CancellationToken` passed to the write delegate is cancelled when the watchdog fires.

**Failures inside Begin**

- [x] **The write does not start**: when the write delegate throws, or returns null, `Begin` runs the failure protocol at once (same order), opens the gate and returns `PersistenceFailed`; nothing is tracked. *(ADR-011 is silent; decided 2026-10-07 — same treatment as a throwing persist step in `CommitBeforeBroadcastSequencer`.)*
- [x] **Acknowledge or compute throws**: the gate is open afterwards, the exception propagates to the caller of `Begin`, no write is started and no failure step runs. *(ADR-011 is silent; decided 2026-10-07.)*

**Shared protocol and guards**

- [x] **Independent characters**: a write in flight for character A does not affect `Begin` for character B; each completes on its own.
- [x] **Sequencer shares the protocol**: `CommitBeforeBroadcastSequencer.Execute` calls the extracted protocol for its failure path; its public signature, its log text and all 14 tests in `TickLoop_CommitBeforeBroadcast_tests.cs` are unchanged and pass.
- [x] **Null arguments**: a null queue or gate in the constructor, and a null delegate in `Begin`, throw `ArgumentNullException` before anything runs.

---

## Implementation Notes

- **Files** (namespace `IronGrind.Networking`, assembly `IronGrind.Foundation`):
  - new, in `src/Foundation/Networking/IrreversibleOutcome/`: `IIrreversibleOutcomeCoordinator.cs`, `IrreversibleOutcomeCoordinator.cs`, `IrreversibleOutcomeBeginResult.cs` (`enum : byte { Started = 0, RejectedInvalidRequest = 1, RejectedWriteInFlight = 2, PersistenceFailed = 3 }`), `IrreversibleWriteFailureProtocol.cs`
  - modified: `src/Foundation/Networking/CommitBeforeBroadcast/CommitBeforeBroadcastSequencer.cs` (failure path only)
- **Why two type parameters**: `ICharacterPersistence` and `CharacterSaveResult` do not exist in `src/` yet (Character Persistence epic), and a caller cannot turn a `Task<CharacterSaveResult>` into a `Task<bool>` without `await` or `ContinueWith`. The coordinator therefore takes the write as a delegate returning `Task<TResult>` and an `isSuccess` predicate, and references no persistence type.
- **`Begin` signature** (loose delegate parameters, following `CommitBeforeBroadcastSequencer.Execute` — each is a step with its own timing):
  ```csharp
  IrreversibleOutcomeBeginResult Begin<TOutcome, TResult>(
      uint clientId,
      CharacterID charId,
      Func<bool> validateRequest,
      Action emitAcknowledgment,
      Func<TOutcome> computeAndApplyOutcome,
      Func<TOutcome, CancellationToken, Task<TResult>> startWrite,
      Func<TResult, bool> isSuccess,
      Action<TOutcome> deliverOutcome,
      Action revertOnFailure,
      Action<uint, DisconnectReason> disconnectClient,
      Action<uint, int> preserveSessionForTtl);
  ```
  `CharacterID` is `IronGrind.Currency.CharacterID`. For the Enhancement System the delegates will be `BeginAttempt` (compute-and-apply), `CompleteAttempt` (deliver) and `RollBackAttempt` (revert); none of that is built here.
- **Constructor**: `IrreversibleOutcomeCoordinator(ITickCompletionQueue queue, ICharacterMutationGate gate)`, plus an optional `INetworkTestObserver observer = null` inside `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`, exactly as the sequencer guards it (Story 002 release-stripping contract).
- **Order inside `Begin`**: null checks → `gate.IsHeld(charId)` (→ `RejectedWriteInFlight`) → `validateRequest()` (→ `RejectedInvalidRequest`) → `gate.Close(charId)` → `emitAcknowledgment()` → `computeAndApplyOutcome()` → create the `CancellationTokenSource` → `queue.Track(startWrite(outcome, cts.Token), cts, state.OnCompleted)` in one statement. Wrap the part after `Close` so that an exception from acknowledge or compute opens the gate and is rethrown, and an exception or null from `startWrite` runs the failure protocol, opens the gate, disposes the source and returns `PersistenceFailed`. Check for a null task before calling `Track` (it would throw `ArgumentNullException`).
- **Per-call state**: one small private generic class holding the delegates, the outcome, the ids and the source, with an `OnCompleted(TickTaskResult<TResult>)` method — a one-shot callback, exempt from the ADR-010 lambda rule. No dictionary of in-flight writes in the coordinator: the gate already records them.
- **Callback**: success is `result.Status == TickTaskStatus.Completed && isSuccess(result.Value)`. Everything else is the failure branch. Open the gate in a `finally` around the branch. An exception from `isSuccess`, deliver or a failure step is not caught here: the gate opens and `TickCompletionQueue` logs it.
- **`CancellationTokenSource`**: created per `Begin`, owned by the coordinator. Dispose it in the callback unless the status is `TimedOut` (the write may still be running with that token; leave it to the garbage collector).
- **`IrreversibleWriteFailureProtocol`**: a `public static` class with one method that runs revert → disconnect (`DisconnectReason.Other`) → `Debug.LogError` → observer hook → preserve (`CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS`), in CR-CP-5's numbered order. It takes the log line (or the pieces the sequencer needs to keep its two existing messages byte-for-byte: prefix `[CommitBeforeBroadcastSequencer] PersistenceWriteFailed`, with and without an exception) so the 14 sequencer tests and their `LogAssert.Expect` patterns keep passing untouched. The coordinator logs with a `[IrreversibleOutcomeCoordinator] PersistenceWriteFailed: clientId=… charId=… — ` prefix and names the cause (non-success result, fault, cancellation, watchdog timeout, write did not start). `SESSION_TTL_SECONDS` stays where it is.
- **A throwing failure step**: the protocol does not catch between its steps (the sequencer does not today); the remaining steps are skipped, the gate still opens. Do not add catch-and-continue here — it would change the sequencer's behaviour.
- **Disconnected client (EC-ENH-2)**: the callback runs whether or not the client is still connected; deliver and disconnect are the caller's delegates and must tolerate a client that has gone.
- **Not wired in**: nothing in production constructs the coordinator yet. Say so in the completion notes.
- **Code review 2026-10-07 — "same statement"**: `startWrite` and `queue.Track` are two statements because of the null-task check above. ADR-011 Decision 1's "in the same statement" is read as "nothing yields or reads the task between the start and `Track`", which holds. If `Track` itself throws (it does only off the tick thread) the write is already running: the coordinator runs the failure protocol, leaves the token source undisposed, opens the gate and rethrows.

---

## Out of Scope

- The inbound request dispatcher, its hold queue, and discarding held requests when the gate opened because of a failed write (Enhancement Story 009; review item P1). `OnGateOpened` carries no success/failure flag today; if the dispatcher needs one, that belongs to the dispatcher's ADR.
- Queueing or deferring a second irreversible outcome for a character whose gate is closed (review item P2 — needs an ADR-011 clarification). This story only rejects.
- Expected `SaveVersion` ownership under queued writes (review item P5).
- Any `ICharacterPersistence` implementation or type (Character Persistence epic); `EnhancementService` and Enhancement Story 011.
- Promoting `DisconnectReason.Other` to a dedicated reason (OQ-CP-1).
- Loot pickup and auction delivery reading the gate (TD-059); the `OnGateOpened` subscriber-exception policy (TD-060).
- Removing or changing the synchronous `CommitBeforeBroadcastSequencer.Execute` beyond routing its failure path through the shared protocol.

---

## QA Test Cases

**File**: `tests/EditMode/Networking/TickLoop_IrreversibleOutcomeCoordinator_tests.cs` (new). Uses the real `TickCompletionQueue` (small watchdog, fake thread-id provider not needed) and the real `CharacterMutationGate`. Writes are `TaskCompletionSource<T>` created with `TaskCreationOptions.RunContinuationsAsynchronously`. A recording list of step names proves order. No `async` test methods, no `yield`, no sleeps, no real threads. Every expected `Debug.LogError` is declared with `LogAssert.Expect` before the act.

**Begin**
- **Sequence** — valid request → recorded order is validate, ack, compute, write; result `Started`; `gate.IsHeld(a)` true; `InFlightCount` 1; the gate was already held when ack ran.
- **Invalid** — validate returns false → `RejectedInvalidRequest`; only "validate" recorded; gate not held; `InFlightCount` 0.
- **Write in flight** — gate closed beforehand → `RejectedWriteInFlight`; nothing recorded; gate still held; no `OnGateOpened`.
- **Second Begin during a write** — `Begin` twice for the same character before the first completes → second returns `RejectedWriteInFlight`; the first still completes normally.
- **Same outcome** — the object returned by compute is the same instance seen by the write delegate and by deliver.

**Completion**
- **Nothing before completion** — three `Drain` calls with the task incomplete → no deliver, no failure step, gate held.
- **Success** — `SetResult(success)` then `Drain` → deliver once; no failure step; gate open; `OnGateOpened` once with `a`; a further `Drain` changes nothing.
- **Non-success result** — `SetResult(failureValue)` → order revert, disconnect, alert, preserve; disconnect received `(clientId, DisconnectReason.Other)`; preserve received `(clientId, 300)`; observer alert count 1; no deliver; gate open.
- **Faulted** — `SetException` → same protocol, once. **Cancelled** — `SetCanceled` → same. **Timed out** — never completed, drained past the watchdog → same, once; the token given to the write delegate is cancelled; then `SetResult(success)` and `Drain` → no deliver, no second protocol (the queue's late-completion log is expected).
- **No retry** — in each failure case the write delegate's call count is 1.
- **Gate held while handling** — deliver and each failure step record `gate.IsHeld(a)`; all true; the `OnGateOpened` handler (a named method) records after them.
- **Deliver throws** — deliver throws → `Drain` returns, the queue's "Completion callback threw" error is logged, gate open.
- **Revert throws** — revert throws → `Drain` returns, error logged, gate open; disconnect was not called (documents the no-catch rule).

**Failures inside Begin**
- **Write delegate throws** — → `PersistenceFailed`; protocol order as above, once; gate open; `InFlightCount` 0; no deliver.
- **Write delegate returns null** — same expectations.
- **Compute throws** — the exception reaches the caller; gate open; write delegate not called; no failure step.
- **Acknowledge throws** — same expectations; compute not called.

**Guards and sharing**
- **Two characters** — `Begin` for `a` and `b`; complete `b` only → `b` delivered and open, `a` still held; then complete `a`.
- **Null arguments** — null queue, null gate, and each null delegate → `ArgumentNullException`; nothing recorded.
- **Sequencer regression** — `TickLoop_CommitBeforeBroadcast_tests.cs` is not edited and its 14 tests pass; code review confirms `Execute` calls `IrreversibleWriteFailureProtocol`.
- **Forbidden patterns** — a grep of the four new files and the sequencer finds no `await`, `async`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith` (checked at `/story-done`).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_IrreversibleOutcomeCoordinator_tests.cs` — must exist and pass; `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs` — unchanged, must still pass.

**Status**: [x] Created and passing (2026-10-07) — 30 `[Test]` + 9 `[TestCase]` = 39 cases

---

## Dependencies

- Depends on: Story 030 (Complete — `TickCompletionQueue`, `CharacterMutationGate`), Story 011 (Complete — `CommitBeforeBroadcastSequencer`). ADR-011 Accepted (2026-10-07).
- Unlocks: Enhancement Story 011 (commit orchestration; still blocked on other work as well).

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 17/17 passing, each covered by at least one automated test. No deferred items.
**Test run**: Unity 6000.3 batch mode, EditMode — 1900 / 1900 passed, including the 39 cases of `TickLoop_IrreversibleOutcomeCoordinator_tests.cs` and the 14 unedited tests of `TickLoop_CommitBeforeBroadcast_tests.cs`. Forbidden-pattern grep (`await`, `async`, `.Result`, `.Wait(`, `GetResult(`, `ContinueWith`) over the four new files and the sequencer: no match.
**Deviations** (advisory, none blocking):
- `startWrite` and `queue.Track` are two statements (null-task check in between); see the "same statement" note under Implementation Notes.
- Beyond the story: if `queue.Track` throws after the write has started (off the tick thread), the coordinator runs the failure protocol, leaves the token source undisposed, opens the gate and rethrows. Added at code review; covered by `Begin_TrackThrowsAfterWriteStarted_RunsFailureProtocolOpensGateAndRethrows`.
- Sequencer, outside the failure block: two words of a doc comment changed ("awaited", "async") so the forbidden-pattern grep is clean, and the now unused `using UnityEngine;` removed. The failure log message is built before revert runs; its text is unchanged.
- A throwing `isSuccess` runs neither deliver nor the failure protocol (as specified: not caught, the gate opens, the queue logs it). Logged as TD-061.
- An `OnGateOpened` subscriber that throws replaces the exception or result of `Begin` (existing TD-060).
**Not wired in**: nothing in production constructs `IrreversibleOutcomeCoordinator` yet; Enhancement Story 011 is the first caller.
**Test Evidence**: Logic — `tests/EditMode/Networking/TickLoop_IrreversibleOutcomeCoordinator_tests.cs`
**Code Review**: Complete — `/code-review` 2026-10-07 (unity-specialist + qa-tester), APPROVED WITH SUGGESTIONS; all 9 suggestions applied and the suite re-run. LP-CODE-REVIEW and QL-TEST-COVERAGE gates skipped (lean mode).
