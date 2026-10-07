# Story 030: Tick Completion Queue and Character Mutation Gate

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-07
> **Estimate**: 4 hours

*Added 2026-10-07, after ADR-011 was accepted. ADR-011 Migration step 1: the two components that are plain C# and depend on no unbuilt system. The epic's first 29 stories are Complete; this story reopens it.*

## Context

**GDD**: `design/gdd/networking-core.md` — CR-NET-5 (commit-before-broadcast, no retry). `design/gdd/character-persistence.md` — CR-CP-1 (asynchronous interface), CR-CP-7 (one write in flight per character). `design/gdd/enhancement-system.md` — CR-ENH-18 (bag-mutating requests held during an attempt).
**Requirement**: none in the epic's TR table — this story builds components decided by ADR-011
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-011: Asynchronous Persistence in the Server Tick Loop — Decision 2 (tick completion queue) and Decision 4 (per-character mutation gate; the gate only, not the dispatcher).
**ADR Decision Summary**: Tick-driven code starts a persistence `Task` and never awaits it. The task is handed to a completion queue that the tick drains; the result is handled on the tick thread, on the first tick after the task completes. A per-character gate records that an irreversible write is in flight.

**Engine**: Unity 6.3 LTS | **Risk**: LOW for this story
**Engine Notes**: Plain C# (`Task`, `CancellationTokenSource`, `TaskCompletionSource`); no Unity API except `Debug.LogError`. `Drain` reads `Task.IsCompleted` only — it installs no continuation and depends on no synchronization context. ADR-011's four headless-build checks (Verification Required) are not part of this story; they gate shipping.
**Performance**: `Drain` is O(tasks in flight) per tick — at most one per character in the zone. No allocation when nothing is in flight.

**Control Manifest Rules (Foundation layer — "Asynchronous persistence in the tick loop (ADR-011)")**:
- Required: `Drain()` runs once per tick; for every tracked task whose `IsCompleted` is true, in the order the tasks were tracked, it invokes the callback on the tick thread with a `TickTaskResult<T>`; `Drain` never throws into the tick
- Required: `Track` and `Drain` are called from the tick thread only; both assert it
- Required: watchdog — `PERSISTENCE_WATCHDOG_TICKS` (200) after tracking: cancel the token, raise a critical alert, invoke the callback with `TimedOut`
- Required: shutdown drain — one bounded blocking wait on all tracked tasks, `AggregateException` caught, then one last `Drain`
- Required: `ICharacterMutationGate.Close` on an already closed gate is an error
- Required: every `TaskCompletionSource` in test fakes uses `TaskCreationOptions.RunContinuationsAsynchronously`
- Forbidden: `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith`, `async void` in tick-driven code — the shutdown drain is the only blocking wait in the server
- Forbidden: lambda captures for persistent event subscriptions (ADR-010); one-shot completion callbacks are exempt (ADR-011 Risks)

---

## Acceptance Criteria

*From ADR-011 Decision 2, Decision 4 and Validation Criteria, scoped to this story:*

**Tick completion queue**

- [ ] **Completed task handled on the next drain**: a tracked task that has run to completion gets its callback on the next `Drain`, exactly once, with status `Completed` and the task's value. A later `Drain` does not invoke it again.
- [ ] **Already complete when tracked**: a task that is complete at the time of `Track` is handled by the next `Drain`, not inside `Track`.
- [ ] **Track order**: when several tracked tasks are complete at one `Drain`, their callbacks run in the order the tasks were tracked, whatever order they completed in.
- [ ] **Incomplete task waits**: an incomplete task gets no callback; `InFlightCount` counts it and drops when it is handled.
- [ ] **Fault and cancellation**: a faulted task yields `Faulted` with the task's exception; a cancelled task yields `Canceled`. `Drain` does not throw.
- [ ] **Throwing callback**: a callback that throws is logged as a server error; the remaining callbacks of that `Drain` still run, and `Drain` does not throw.
- [ ] **Watchdog**: a task still incomplete the watchdog number of ticks after it was tracked has its `CancellationTokenSource` cancelled, a critical alert logged, and its callback invoked once with `TimedOut`. It no longer counts in `InFlightCount`.
- [ ] **Late completion after a timeout**: when a timed-out task later completes or faults, it is logged and no second callback runs. *(The entry's `task.Exception` is also read so the fault is observed — checked in code review, not by a test.)*
- [ ] **Tick thread only**: `Track` and `Drain` throw `InvalidOperationException` when called from a thread other than the one the queue was created on.
- [ ] **No allocation when idle**: `Drain` with nothing tracked allocates nothing.
- [ ] **Shutdown drain**: `DrainOnShutdown(timeout)` waits once, up to the timeout, on all tracked tasks, catches the `AggregateException` of faulted or cancelled tasks, then drains so every finished task gets its callback (including failed ones). Tasks still incomplete after the timeout are logged and alerted and get no callback; the method returns.
- [ ] **Runs before game logic when registered first**: with `Drain` registered as the first tick-driven delegate of a `ServerTickLoop`, a completion callback runs before a second tick-driven delegate on the tick after the task completes.
- [ ] **Constants**: `PERSISTENCE_WATCHDOG_TICKS` is 200 and `MAX_HELD_REQUESTS_PER_CHARACTER` is 16.

**Character mutation gate**

- [ ] **Hold state**: `IsHeld` is false for a character never closed, true after `Close`, false after `Open`. Closing one character does not hold another.
- [ ] **Double close is an error**: `Close` on an already closed gate throws `InvalidOperationException` (CR-CP-7: one write in flight) and leaves the gate closed.
- [ ] **Open raises the event**: `Open` on a closed gate raises `OnGateOpened` once, with that character's id, after `IsHeld` has become false.
- [ ] **Open on an open gate**: `Open` for a character whose gate is not closed does nothing and raises no event. *(ADR-011 is silent; decided 2026-10-07 — the coordinator opens the gate in a `finally`.)*

---

## Implementation Notes

- **Files** (namespace `IronGrind.Networking`, assembly `IronGrind.Foundation`):
  - `src/Foundation/Networking/TickCompletion/ITickCompletionQueue.cs`, `TickCompletionQueue.cs`, `TickTaskResult.cs` (the `readonly struct TickTaskResult<T>` and `enum TickTaskStatus { Completed, Faulted, Canceled, TimedOut }`), `TickCompletionConstants.cs`
  - `src/Foundation/Networking/MutationGate/ICharacterMutationGate.cs`, `CharacterMutationGate.cs`
- **Interfaces**: exactly as in ADR-011 "Key Interfaces". `ITickCompletionQueue` gains one member the ADR describes in prose but does not list: `void DrainOnShutdown(TimeSpan timeout)`. `CharacterID` is `IronGrind.Currency.CharacterID` (a `readonly struct`, so `event Action<CharacterID>` satisfies ADR-010 Decision 3).
- **Generic storage without per-tick allocation**: `Track<T>` may allocate one small entry object per tracked task (one per persistence call, off the hot path). `Drain` itself must not allocate when the list is empty; iterate by index, no LINQ, no enumerator boxing.
- **`Error` by status**: `Completed` → `null`. `Faulted` → `task.Exception.GetBaseException()` (the original exception when there is exactly one, otherwise the `AggregateException`). `Canceled` → a new `TaskCanceledException(task)`, because a cancelled task's `Task.Exception` is null. `TimedOut` → a new `TimeoutException` naming the watchdog length in ticks. `Value` is `default` for every status except `Completed`.
- **`cancellation`**: may be null (a task with nothing to cancel, e.g. `Task.FromResult`); the watchdog then skips the cancel step and still reports `TimedOut`. The caller owns the source: the queue only calls `Cancel()` and never disposes it. `Cancel()` is wrapped in `try`/`catch` and a failure is logged, since it can throw (`ObjectDisposedException`, or an `AggregateException` from registered callbacks) and `Drain` must not.
- **Order**: keep tracked entries in a list in track order. `Drain` walks it front to back, handles each entry whose task `IsCompleted` or whose watchdog tick has passed, and removes handled entries without reordering the rest. A callback may call `Track` (the coordinator will); an entry added during a `Drain` is handled on a later `Drain`, not the current one.
- **Watchdog tick**: store `trackedAtTick + watchdogTicks` using the `currentTick` passed to `Drain`; `Track` needs the current tick too — take it from the last `Drain` call (initially the constructor's `initialTick`, default 0). Compare with `StaleDiscardComparer.IsTickExpired` so `uint` wraparound is safe. The watchdog length is a constructor parameter that defaults to `PERSISTENCE_WATCHDOG_TICKS`, so tests use a small number.
- **Timed-out entries**: move them to a second list. Each `Drain` checks that list; when the task has completed, log it, read `task.Exception` (so it is observed) and drop the entry. `DrainOnShutdown` does the same for whatever is left.
- **Tick thread assert**: the constructor takes an optional `Func<int> currentThreadId` (default `() => Environment.CurrentManagedThreadId`), calls it once and stores the result; `Track`, `Drain` and `DrainOnShutdown` compare. Tests pass a fake provider — no real second thread. The off-thread `InvalidOperationException` is a programming-error assert and the one exception to "`Drain` never throws"; the doc comment on `Drain` says "never throws for a task or callback failure; throws `InvalidOperationException` if called off the tick thread".
- **Logging**: follow `CommitBeforeBroadcastSequencer` — `Debug.LogError` with a `[TickCompletionQueue]` prefix for a throwing callback, a watchdog timeout ("critical infrastructure alert"), a late completion and an incomplete task at shutdown. Tests assert them with `LogAssert.Expect`.
- **`DrainOnShutdown`**: the only place in the server that blocks on a task. `Task.WaitAll(tasks, timeout)` inside `try`/`catch (AggregateException)`; then one `Drain`-equivalent pass that ignores the watchdog. Do not call it from `Drain` or from any tick code.
- **Gate**: a `HashSet<CharacterID>`; no thread assert in the ADR, none added. `Open` removes the id first, then raises the event. An exception thrown by a subscriber propagates (the gate is already open at that point).
- **Not wired in**: nothing in production constructs the queue or the gate yet (no zone bootstrap exists). Say so in the completion notes.

---

## Out of Scope

- `IrreversibleOutcomeCoordinator` and extracting the failure protocol from `CommitBeforeBroadcastSequencer` (ADR-011 Migration step 2)
- The inbound request dispatcher and its hold queue (Enhancement Story 009; architecture review 2026-10-07 item P1). `MAX_HELD_REQUESTS_PER_CHARACTER` is only declared here.
- Loot pickup and auction delivery reading the gate (ADR-011 Migration step 4; TD-059)
- Guaranteeing that `Drain` is the first thing a real zone tick does, and the place of `DrainOnShutdown` in the zone teardown sequence (review items P1 and C2)
- A second irreversible outcome while the gate is closed, and expected `SaveVersion` ownership (review items P2 and P5)
- Any `ICharacterPersistence` implementation (Character Persistence epic)

---

## QA Test Cases

**Files**: `tests/EditMode/Networking/TickLoop_CompletionQueue_tests.cs` and `tests/EditMode/Networking/TickLoop_CharacterMutationGate_tests.cs` (both new). Tasks come from `TaskCompletionSource<T>` created with `TaskCreationOptions.RunContinuationsAsynchronously`, or `Task.FromResult`. No `async` test methods, no `yield`, no sleeps, no real threads.

**Queue**
- **Completed** — track, `SetResult(42)`, `Drain` → callback once, `Completed`, value 42, `Error` null; second `Drain` → no further call; `InFlightCount` 1 then 0.
- **Already complete** — track `Task.FromResult(7)` → no callback during `Track`; next `Drain` → `Completed`, 7.
- **Track order** — track A, B, C; complete C, then A, then B; one `Drain` → callbacks A, B, C.
- **Incomplete** — track, `Drain` three times → no callback, `InFlightCount` 1.
- **Faulted** — `SetException(e)` → `Faulted`, `Error` is the same instance as `e`. **Cancelled** — `SetCanceled()` → `Canceled`, `Error` is a `TaskCanceledException`.
- **Throwing callback** — two complete tasks, the first callback throws → error logged, second callback still runs, `Drain` returns.
- **Callback tracks another task** — the callback of A tracks an already complete B → B is not handled in the same `Drain`; it is handled in the next.
- **Watchdog** — watchdog 3 ticks; track at tick 10; `Drain(11)`, `Drain(12)` → nothing; `Drain(13)` → token cancelled, alert logged, callback `TimedOut` once, `Error` is a `TimeoutException`, `InFlightCount` 0.
- **Null cancellation** — track with a `null` source, let the watchdog fire → `TimedOut`, no exception.
- **Cancel throws** — a source already disposed by the caller → error logged, callback still `TimedOut`, `Drain` returns.
- **Watchdog boundary** — a task that completes on the tick the watchdog would fire is reported `Completed`, not `TimedOut`.
- **Watchdog across wraparound** — track at `uint.MaxValue - 1` with watchdog 3 → fires at tick 1.
- **Late completion** — after the timeout above, `SetException` then `Drain` → logged, no second callback.
- **Off-thread** — fake thread-id provider returns 1 at construction and 2 afterwards → `Track` and `Drain` throw `InvalidOperationException`.
- **No allocation** — `Drain` on an empty queue inside `Assert.That(() => …, Is.Not.AllocatingGCMemory())`.
- **Shutdown, all complete** — two complete tasks (one faulted) → both callbacks run, no exception escapes.
- **Shutdown, one incomplete** — `DrainOnShutdown(TimeSpan.Zero)` with one incomplete task → returns, logs the incomplete task, no callback for it; the complete ones are handled.
- **Null arguments** — `Track` with a null task or null callback throws `ArgumentNullException`.
- **Constants** — 200 and 16.

**Gate**
- **Hold state** — default false; `Close(a)` → `IsHeld(a)` true, `IsHeld(b)` false; `Open(a)` → false.
- **Double close** — second `Close(a)` throws; `IsHeld(a)` still true.
- **Event** — subscribe with a named method; `Open(a)` on a closed gate → one call with `a`; `IsHeld(a)` is false inside the handler.
- **Open when open** — `Open(a)` never closed → no event, no exception; `Close`, `Open`, `Open` → one event.
- **Reuse** — `Close`, `Open`, `Close` on the same character works.

**Integration with the tick loop** (in `TickLoop_CompletionQueue_tests.cs`)
- Registering `dt => queue.Drain(tickLoop.ServerTickNumber)` first on a `ServerTickLoop` makes the completion callback run before a second tick-driven delegate on the tick after the task completes.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_CompletionQueue_tests.cs` and `tests/EditMode/Networking/TickLoop_CharacterMutationGate_tests.cs` — must exist and pass.

**Status**: [x] Created and passing — 37 queue tests and 7 gate tests; batch-mode EditMode run 2026-10-07, 1861/1861 passed (Unity 6000.3.10f1)

---

## Dependencies

- Depends on: Story 009 (Complete — `ServerTickLoop`), Story 005 (Complete — `StaleDiscardComparer`). ADR-011 Accepted (2026-10-07).
- Unlocks: the `IrreversibleOutcomeCoordinator` story (not yet written); Enhancement Stories 009 and 011 (each still blocked on other work as well).

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 17/17 passing (the "`Exception` is read" clause of the late-completion criterion is checked by review, as stated in the criterion)
**Deviations** (advisory, none blocking):
- `TickTaskResult<T>` has a public constructor that ADR-011 Key Interfaces does not list (needed by fakes).
- Added in code review, beyond the story text: `Drain` / `DrainOnShutdown` called from a completion callback throw `InvalidOperationException` (caught and logged by the outer call); `DrainOnShutdown` rejects a negative or infinite timeout (`ArgumentOutOfRangeException`) and caps it at `int.MaxValue` ms; it also waits on timed-out tasks and alerts those still incomplete; a task tracked by a callback during `DrainOnShutdown` is alerted and dropped with no callback. A watchdog length below 1 throws `ArgumentOutOfRangeException`.
- `CharacterMutationGate.Open` stops at the first throwing `OnGateOpened` subscriber, as specified here — logged as TD-060.
**Not wired in**: nothing in production constructs `TickCompletionQueue` or `CharacterMutationGate` yet (no zone bootstrap exists). A zone that constructs the queue after tick 0 must pass the current tick as `initialTick`.
**Test Evidence**: Logic — `tests/EditMode/Networking/TickLoop_CompletionQueue_tests.cs` (37 tests), `tests/EditMode/Networking/TickLoop_CharacterMutationGate_tests.cs` (7 tests); full EditMode suite 1861/1861 passed in Unity batch mode.
**Code Review**: Complete — `/code-review` 2026-10-07 (`unity-specialist`, `qa-tester`); CHANGES REQUIRED, all required changes and suggestions 1–5 and 7–9 applied and re-verified. Director gates skipped (lean mode).
