# ADR-011: Asynchronous Persistence in the Server Tick Loop

## Status
Accepted (2026-10-07)

## Date
2026-10-07

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Core — managed C# `Task` consumption on the headless server main thread |
| **Knowledge Risk** | MEDIUM — `Task`, `TaskCompletionSource` and `CancellationToken` are engine-agnostic .NET. The Unity-specific part is where an `await` continuation resumes (`UnitySynchronizationContext`, pumped by the player loop and not by `ServerTickLoop`) and how that behaves in EditMode tests and batch mode. This decision is written so that game logic never depends on that behaviour. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `current-best-practices.md` (none has an entry on async / `Awaitable` / synchronization context); ADR-006 Engine Compatibility (Npgsql under IL2CPP) |
| **Post-Cutoff APIs Used** | None. `UnityEngine.Awaitable` is deliberately not used. |
| **Verification Required** | (1) On a headless IL2CPP Linux build, a `Task` started on the main thread whose internals use `ConfigureAwait(false)` completes on the thread pool without the player loop pumping anything, and `Task.IsCompleted` read from the main thread observes it. Record `ThreadPool.GetMinThreads` and the host's CPU limit, and run one real Npgsql write (not a fake) in that build to prove nothing hangs on a captured context. (2) The shutdown drain (a bounded blocking wait on in-flight tasks after the tick loop has stopped) does not deadlock. (3) EditMode tests that complete a `TaskCompletionSource` and then advance the tick pass with no `async` test method and no `yield`. (4) SIGTERM sent to the headless build reaches zone teardown and the drain. Scripting backend: IL2CPP, per ADR-007; ADR-006's risk table still suggests Mono for the Linux server as a fallback — the pattern here does not depend on the backend, but the checks must run on the backend that ships. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-006 (Persistence Layer — Accepted): every `ICharacterPersistence` method is `async Task<>`; write budget ≤ 50 ms P95; at most one write in flight per character. ADR-007 (Hosting — Accepted): one headless process per zone, all zone logic on the main thread. ADR-010 (Messaging — Accepted): interface injection, named-method subscriptions, NGO handlers do not call game logic directly. |
| **Enables** | Enhancement Stories 009 (held requests) and 011 (commit orchestration); every later caller of `SaveIrreversibleOutcome` (level-up, respec, item consumption); the Character Persistence epic's session load and save flows. |
| **Blocks** | Enhancement Stories 009 and 011 cannot start until this ADR is Accepted. Any story that calls `ICharacterPersistence` from tick-driven code. |
| **Ordering Note** | The inbound request dispatcher named in Decision 4 sits next to ADR-004 (NGO) code that does not exist yet. The gate and the completion queue are plain C# and can be built and tested first. Resolves enhancement-system.md OQ-ENH-7. |

## Context

### Problem Statement
Server game logic runs single-threaded on `ServerTickLoop` at 20 Hz — one tick is 50 ms. Every persistence call is an asynchronous `Task` with a write budget of ≤ 50 ms P95 (ADR-006), the same size as a tick. No decision says where such a call runs, on which thread and at which point of the tick its result is handled, or what a character's other requests do while a write is in flight. Three pieces of existing work disagree: `ICharacterPersistence` is asynchronous; `CommitBeforeBroadcastSequencer.Execute` takes a synchronous `Func<TOutcome, bool> persistOutcome`; and `EnhancementService` is two-phase (`BeginAttempt` → `CompleteAttempt` / `RollBackAttempt`) with nothing in between. Enhancement Stories 009 and 011 are blocked on it, and level-up, respec and item consumption will hit the same question.

### Constraints
- All zone logic runs on the Unity main thread (ADR-007, Zone Instancing runtime model). Game state is not thread-safe and must not become so.
- No outcome message may be sent before the write confirms; a failed irreversible write is not retried; the caller rolls back, the client is disconnected and the session is preserved (networking-core.md CR-NET-5, character-persistence.md CR-CP-5).
- While an irreversible write is in flight, the character's bag must not change, so that the rollback cannot fail for lack of space (enhancement-system.md CR-ENH-18).
- At most one write in flight per character (CR-CP-7).
- Tests must be deterministic EditMode tests (coding standards): no timing, no real threads.

### Requirements
- The tick must never wait on I/O.
- A write's result must be handled on the tick thread, at a known point of the tick.
- One rule for every persistence call made from tick code, and one enforcement point for held client requests.
- Server-originated bag mutations that cannot be queued (loot pickup returns a synchronous result) must have a defined behaviour.

## Decision

**Tick-driven code starts a persistence `Task` and never awaits it. The task is handed to a completion queue that the tick drains; the result is handled on the tick thread, on the first tick after the task completes. While an irreversible write is in flight a per-character gate is closed: the request dispatcher holds that character's bag-mutating requests, and server-originated bag mutations are deferred.**

### Decision 1 — No `await` in tick-driven code
In any code that runs on the server tick (game systems, request handlers, orchestration):
- Forbidden: `await`, `async` methods, `Task.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `ContinueWith`, `async void`.
- Allowed: calling a `Task`-returning method and passing the returned task, in the same statement, to `ITickCompletionQueue.Track`.

Where a continuation would resume is therefore never a question: there are no continuations in game logic.

### Decision 2 — Tick completion queue
`ITickCompletionQueue` holds `(Task, callback)` pairs. `Drain()` runs **once per tick, before any game logic of that tick**, and for every tracked task whose `IsCompleted` is true, in the order the tasks were tracked, removes it and invokes its callback on the tick thread with a `TickTaskResult<T>`:
- `RanToCompletion` → the task's value.
- `Faulted` or `Canceled` → a failure result carrying the exception. `Drain` never throws into the tick; an exception thrown by a callback is caught, logged as a server error, and does not stop the remaining callbacks (same policy as `ServerTickLoop` tick-driven delegates).

`Drain` reads `Task.IsCompleted` only. It installs no continuation and does not depend on any synchronization context.

**Watchdog.** A task still incomplete `PERSISTENCE_WATCHDOG_TICKS` (default 200 = 10 s, equal to `CHARACTER_LOAD_TIMEOUT_SECONDS`) after it was tracked has its `CancellationToken` cancelled, a critical alert is raised, and its callback is invoked with a `TimedOut` failure. A cancelled write has an unknown outcome: cancelling the token does not abort a command already running in the database. A late completion of a timed-out task is logged, its `Exception` is read (so it is observed), and it is otherwise ignored.

**Threading.** `Track` and `Drain` are called from the tick thread only; both assert it.

**Shutdown.** After the tick loop has stopped, zone teardown makes one bounded blocking wait on all tracked tasks (timeout = the watchdog duration; an `AggregateException` from faulted or cancelled tasks is caught, not rethrown), then drains one last time so every task, including failed ones, gets its callback. Tasks still incomplete after the timeout are logged and alerted, and the process goes on to exit. The drain runs on the explicit shutdown path before `Application.Quit()`, not from `OnApplicationQuit`. This is the only blocking wait on a task in the server.

### Decision 3 — Contract for `ICharacterPersistence` implementations
- **Snapshot in the synchronous prefix.** Everything a method needs from live game state is copied before its first `await`, on the calling (tick) thread. After the first `await` the method touches only that copy and the database. Game state is never read or written off the tick thread.
- **`ConfigureAwait(false)` on every `await`** in the persistence layer and in everything it calls that this project owns — including `await using` and `await foreach` — so its continuations run on the thread pool and never post to Unity's synchronization context. The first `await` of a method is reached on the tick thread, where that context is current; if any awaited call cannot be trusted not to capture it, the method runs its body through `Task.Run` after the snapshot is taken.
- **`TaskCreationOptions.RunContinuationsAsynchronously`** on every `TaskCompletionSource` created by the persistence layer and by test fakes, so no continuation runs inline on the completing thread.
- The interface in character-persistence.md and ADR-006 is unchanged.

### Decision 4 — Per-character mutation gate and held requests (resolves OQ-ENH-7)
`ICharacterMutationGate` records, per character, that an irreversible write is in flight.
- **Closed** by the irreversible-outcome coordinator (Decision 5) before the outcome is applied in memory; **opened** by it after the result has been handled (success or rollback).
- **Client requests.** The session's inbound request dispatcher — the single point between the RPC guard chain and game logic (ADR-010 Decision 5) — checks the gate once per request. If the gate is closed and the request type is marked `HeldDuringIrreversibleWrite` on its descriptor (move, equip, unequip, discard, sell, buy, consumable use, accessory merge — every bag-mutating request), the request is appended to that character's hold queue instead of being dispatched. Requests not so marked (movement, chat, a second enhancement request — which CR-ENH-8 rejects) are dispatched normally.
- **Release.** When the gate opens, the held requests are dispatched in arrival order on the same tick, after `Drain` and before that tick's new requests. If the gate opened because of a failed write, the client is being disconnected and the held requests are discarded.
- **Bound.** `MAX_HELD_REQUESTS_PER_CHARACTER` (default 16). A request arriving at a full queue is dropped and logged; a write lasts one to two ticks, so a full queue indicates abuse.
- **Server-originated bag mutations** (loot auto-pickup, auction delivery — callers that get a synchronous result and cannot be queued) read `ICharacterMutationGate.IsHeld(charId)` before mutating. If held, the mutation is **not attempted**: no bag-full result, no client notice. The caller retries on the first tick after `OnGateOpened(charId)`. Game systems other than these callers never read the gate.

### Decision 5 — Irreversible-outcome coordinator
The commit-before-broadcast sequence is split across ticks. `IrreversibleOutcomeCoordinator.Begin` runs on tick N: validate → close gate → acknowledge → compute and apply the outcome in memory → start the write → `Track`. Its completion callback runs on tick N+k:
- `Success` → deliver / broadcast the outcome → open gate.
- Any non-success code, a fault, a cancellation or a watchdog timeout → revert (caller-owned rollback) → disconnect the client → critical alert → preserve the session for `SESSION_TTL_SECONDS` → open gate. No retry.

The failure protocol steps are the ones `CommitBeforeBroadcastSequencer` already implements; they are extracted so both share them. The synchronous `CommitBeforeBroadcastSequencer.Execute` remains only for a persist step that is genuinely synchronous; no production caller of `SaveIrreversibleOutcome` may use it.

For the Enhancement System: `Begin` calls `EnhancementService.BeginAttempt`; the success branch calls `CompleteAttempt`; the failure branch calls `RollBackAttempt`.

### Architecture Diagram

```
tick N                                   thread pool                tick N+k
──────────────────────────────           ───────────────            ─────────────────────────────────
Drain()  (nothing for this char)                                    Drain()
dispatcher → request                                                  └ task.IsCompleted → callback
  Coordinator.Begin                                                       Success:  CompleteAttempt
    validate                                                                        deliver/broadcast
    gate.Close(char)                                                      Failure:  RollBackAttempt
    acknowledge                                                                     disconnect, alert,
    apply outcome in memory                                                         preserve session
    task = SaveIrreversibleOutcome(…) ──► snapshot (sync prefix)          gate.Open(char)
    queue.Track(task, callback)           await … ConfigureAwait(false)  dispatcher releases held requests
other requests for char → HELD            DB transaction                 this tick's new requests
loot pickup for char   → deferred         task completes ────────────►   deferred pickups retried next tick
simulation                                                          simulation
```

### Key Interfaces

```csharp
public interface ITickCompletionQueue
{
    /// Tracks a started task; the callback runs on the tick thread, in Drain, once the task has completed.
    void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick);
    /// Called once per tick before any game logic. Never throws.
    void Drain(uint currentTick);
    int InFlightCount { get; }
}

public readonly struct TickTaskResult<T>
{
    public TickTaskStatus Status { get; }   // Completed | Faulted | Canceled | TimedOut
    public T Value { get; }                 // valid only when Completed
    public Exception Error { get; }         // null when Completed
}

public interface ICharacterMutationGate
{
    bool IsHeld(CharacterID charId);
    void Close(CharacterID charId);         // throws if already closed (CR-CP-7: one write in flight)
    void Open(CharacterID charId);
    event Action<CharacterID> OnGateOpened;
}
```

Constants: `PERSISTENCE_WATCHDOG_TICKS = 200`, `MAX_HELD_REQUESTS_PER_CHARACTER = 16`.

## Alternatives Considered

### Alternative 1: `async`/`await` in game logic, with an asynchronous sequencer overload
- **Description**: `await persistence.SaveIrreversibleOutcome(...)` inside the orchestration; the continuation is resumed on the main thread by `UnitySynchronizationContext`.
- **Pros**: Reads top to bottom; least new code.
- **Cons**: The continuation runs when Unity pumps its context, not at a defined point of `ServerTickLoop` — it can land between two systems' tick work. EditMode tests have no player loop, so awaits need async test plumbing or hang. `async void` entry points swallow exceptions. Every future handler must remember the rules.
- **Rejection Reason**: Makes the order of state changes within a tick depend on engine scheduling that the project does not control and cannot test deterministically.

### Alternative 2: Block the tick on the write
- **Description**: Keep the synchronous sequencer; wrap the task with `GetAwaiter().GetResult()`.
- **Pros**: No new concepts; the commit-before-broadcast order stays structural; no held requests needed.
- **Cons**: One write stalls every player in the zone for up to a full tick at P95 and longer in the tail; several writes in one tick stack. Blocking the main thread on a task is a deadlock if any continuation needs that thread.
- **Rejection Reason**: Spends the whole frame budget of every player on one player's database write.

### Alternative 3: Each mutating system checks the in-flight flag
- **Description**: Inventory, Equipment, NPC Shop and Consumable Use each check and each hold or reject.
- **Pros**: No Networking Core rule.
- **Cons**: Four enforcement points to implement and test; every new mutating system must remember it.
- **Rejection Reason**: The dispatcher already sees every client request once.

## Consequences

### Positive
- The tick never waits on I/O; a slow database slows one player's result, not the zone.
- All game state changes happen on the tick thread at a fixed point of the tick; no locks in game code.
- Tests are deterministic: a fake persistence returns a `TaskCompletionSource` task, the test completes it and advances the tick.
- One pattern for every persistence caller; OQ-ENH-7 is closed with one enforcement point.

### Negative
- An irreversible outcome takes at least one extra tick: the result is handled 50–100 ms after the request in the normal case (inside `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` = 200 ms).
- Orchestration is split into a begin half and a completion callback — more code than a linear `await`.
- New components to build: the queue, the gate, the coordinator, the dispatcher hold queue.
- The Loot Table System gains a "pickup deferred" path, which its GDD does not describe yet.

### Risks
- **Watchdog fires, or the write is cancelled, but it actually committed.** Memory is rolled back while the database holds the outcome. Mitigation: the database is the truth — the session-TTL save of the rolled-back state fails on `save_version` (optimistic concurrency) and is dropped, so on re-login the character loads with the outcome applied. Raise a critical alert; revisit the value of `PERSISTENCE_WATCHDOG_TICKS` with real latency data.
- **A persistence implementation reads live state after an `await`.** Mitigation: Decision 3 is a control-manifest rule; code review checks the synchronous prefix; the snapshot type is immutable.
- **Callback captures.** Completion callbacks are one-shot, so the ADR-010 ban on lambdas for persistent subscriptions does not apply, but each allocates. Acceptable off the hot path (one per persistence call).
- **Gate left closed** by a bug in a callback. Mitigation: the coordinator opens the gate in a `finally`; the watchdog bounds the worst case.
- **A missing `ConfigureAwait(false)` is invisible in EditMode tests** (no player loop) and shows only on the server build, as a late or stalled write, or as a shutdown drain that waits out its timeout. Mitigation: the headless-build check with a real database call (Verification Required 1); code review of the persistence layer.
- **Thread pool starvation on a small host.** The pool starts at one thread per core and grows slowly; a 1–2 core VM with several writes in flight could queue them. Mitigation: record the pool's minimum threads and the CPU limit in the headless check; raise the minimum if writes queue.
- **Shutdown signal.** Thread-pool threads are background threads: if the process exits before the drain ends, in-flight writes are killed. Mitigation: the drain is on the explicit shutdown path; Verification Required 4.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| enhancement-system.md | CR-ENH-11 / CR-ENH-15 step 6b — commit, then deliver | The result is delivered only from the completion callback on `Success` |
| enhancement-system.md | CR-ENH-18, OQ-ENH-7 — other bag-mutating requests are held during an attempt | Mutation gate + dispatcher hold queue; server-originated mutations deferred |
| enhancement-system.md | EC-ENH-2 — a disconnect during the write does not abort it | The task is tracked by the zone, not by the session; the callback runs regardless |
| character-persistence.md | CR-CP-1 — asynchronous interface | Unchanged; consumed through `ITickCompletionQueue` |
| character-persistence.md | CR-CP-5 — failure protocol | Failure branch of the coordinator |
| character-persistence.md | CR-CP-7 — one write in flight per character | `Close` on an already closed gate is an error |
| networking-core.md | CR-NET-5.1–5.5 — commit before broadcast, no retry | Coordinator sequence across ticks; no retry on any failure |
| loot-table-system.md | CR-LT-7 / CR-LT-9 — server calls `PickupRequest` | Callers read the gate and defer; **needs a GDD note** |

## Performance Implications
- **CPU**: `Drain` is O(tasks in flight) per tick — at most one per character in the zone; negligible. No per-tick allocation when nothing is in flight.
- **Memory**: one small record per in-flight task; a hold queue of up to 16 requests per character with a closed gate.
- **Load Time**: none.
- **Network**: none; held requests are delayed by one to two ticks.

## Migration Plan
1. Build `TickCompletionQueue`, `CharacterMutationGate` and their tests (plain C#, no dependencies on unbuilt systems).
2. Extract the failure protocol from `CommitBeforeBroadcastSequencer`; build `IrreversibleOutcomeCoordinator` on it. Existing sequencer tests keep passing.
3. Enhancement Story 011 uses the coordinator against a fake `ICharacterPersistence`; Story 009 adds the dispatcher hold queue (needs the dispatcher — ADR-004 code).
4. `GroundItemService` and `LootAuctionService` read the gate before a pickup (new Loot Table story, after a GDD note).
5. Add the Decision 1 and Decision 3 rules to the control manifest (`/create-control-manifest update`).

## Validation Criteria
- A grep of tick-driven source for `await`, `.Result`, `.Wait(`, `GetResult(` and `ContinueWith` finds nothing outside the persistence layer and the shutdown drain.
- With a fake persistence that completes after k ticks: no outcome event before tick N+k; requests sent in between are processed after it, in arrival order; a pickup in between is deferred and delivered after.
- A task already complete when it is tracked is handled by the next `Drain`.
- With a fake that fails, faults, cancels or never completes: rollback, disconnect and session preservation happen exactly once and the gate ends open.
- The four engine checks under Verification Required pass on a headless build.

## Related Decisions
- ADR-006 (Persistence Layer), ADR-007 (Hosting), ADR-010 (Messaging), ADR-004 (NGO), ADR-001 (Purchase integrity — `BeginPurchase` / `CompletePurchase` are persistence calls and follow Decision 1 and 2).
- `design/gdd/enhancement-system.md` OQ-ENH-7; `design/gdd/character-persistence.md` CR-CP-5, CR-CP-7; `design/gdd/networking-core.md` CR-NET-5.
