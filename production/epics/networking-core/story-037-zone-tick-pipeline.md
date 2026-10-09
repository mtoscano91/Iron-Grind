# Story 037: Zone Tick Pipeline — the Order of One Server Tick

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 3 hours

*Added 2026-10-09. ADR-014 Migration Plan step 2. Stories 035 and 036 (step 1) built the guard chain changes and the dispatcher; this story builds the one class that fixes the order in which the completion queue, the dispatcher, the simulation and the outbound writers run on a tick.*

## Context

**GDD**: `design/gdd/networking-core.md` — CR-NET-2 (fixed 20 Hz tick; game logic runs on the tick). No rule or value of the GDD changes.
**Requirement**: none in the epic's TR table — this story adds a component as decided by ADR-014
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09) — Decision 6. Also ADR-011 Decision 2 (`ITickCompletionQueue.Drain` runs before any game logic) and ADR-002 Decision 1 (simulation phases 1–4).
**ADR Decision Summary**: `ZoneTickPipeline` is one class in `IronGrind.ServerLogic`. Its `Tick` is the one tick-driven delegate of `ServerTickLoop`. It runs four steps in a fixed order — completion, requests, simulation, outbound — each in its own `try`/`catch`, so a failing step never skips the later steps or the rest of `ServerTickLoop.AdvanceTick`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The only Unity API is `Debug.LogError`. No post-cutoff API.
**Performance**: `Tick` allocates nothing when no step throws. The error message is built only inside a `catch`.

**Control Manifest Rules (Foundation layer — "Inbound request dispatch and tick order (ADR-014)")**:
- Required: `ZoneTickPipeline` is one class in `IronGrind.ServerLogic`; the composition root registers its `Tick` as the one tick-driven delegate of `ServerTickLoop`
- Required: four steps in this order: (1) Completion — `ITickCompletionQueue.Drain(currentTick)`; (2) Requests — `InboundRequestDispatcher.DispatchTick(currentTick)`; (3) Simulation — ADR-002 phases 1–4; (4) Outbound — the per-connection writers flush
- Required: each step runs in its own `try`/`catch` that logs a server error and continues
- Required (enforcement): the pipeline test shows Drain → Pass A → Pass B → simulation → outbound; a source search finds `RegisterTickDriven(` called only by the composition root, for `ZoneTickPipeline`
- Forbidden: calling `ServerTickLoop.RegisterTickDriven` for game logic
- Forbidden: `DispatchTick` called by anything other than `ZoneTickPipeline`

---

## Acceptance Criteria

*From ADR-014 Decision 6 and Validation Criteria, scoped to this story:*

**Construction**

- [x] **One class**: `public sealed class ZoneTickPipeline` in namespace `IronGrind.Networking`, assembly `IronGrind.ServerLogic`. Its constructor takes `ITickCompletionQueue`, `IInboundRequestDispatcher`, `IZoneSimulationStep`, `IZoneOutboundStep` and `IServerTickSource`. A null argument throws `ArgumentNullException` that names the parameter.
- [x] **Two step interfaces**: `IZoneSimulationStep` with `void Run(uint currentTick, float deltaTime)` and `IZoneOutboundStep` with `void Flush(uint currentTick)`. *(Not named in ADR-014, which says what runs in steps 3 and 4 but gives no type; decided by the author and approved by the user 2026-10-09. Nothing in `src/` composes ADR-002's phases or the outbound writers yet, so the pipeline takes them through these two interfaces.)*

**Order**

- [x] **`Tick(float deltaTime)`**: `public void Tick(float deltaTime)` fits `Action<float>`, so `tickLoop.RegisterTickDriven(pipeline.Tick)` compiles with no lambda. *(Signature approved by the user 2026-10-09.)*
- [x] **Four steps, fixed order**: one `Tick` call makes exactly these calls, in this order: `ITickCompletionQueue.Drain`, `IInboundRequestDispatcher.DispatchTick`, `IZoneSimulationStep.Run`, `IZoneOutboundStep.Flush`. Each is called once.
- [x] **One tick number**: `Tick` reads `IServerTickSource.ServerTickNumber` once, before the first step, and passes that value to all four steps. A step that changes the tick source's value does not change what the later steps receive.
- [x] **`deltaTime`** goes to `IZoneSimulationStep.Run` unchanged, and to no other step.

**Isolation**

- [x] **A throwing step does not skip the later ones**: when any one of the four steps throws, the steps after it are still called, with the same arguments as when nothing throws. The steps before it are not called again.
- [x] **One server error per failed step**: a step that throws logs exactly one `Debug.LogError` of the form `[ZoneTickPipeline] StepFailed: step=<name> tick=<tick> - <exception type name>: <exception message>`, where `<name>` is `Completion`, `Requests`, `Simulation` or `Outbound`. A step that does not throw logs nothing. *(Format decided by the author and approved by the user 2026-10-09; the same style as `InboundRequestDispatcher`.)*
- [x] **`Tick` never throws**: with all four steps throwing, `Tick` returns normally and four errors are logged, in step order.
- [x] **The rest of `AdvanceTick` still runs**: with `pipeline.Tick` registered on a real `ServerTickLoop` and one step throwing, `AdvanceTick` does not throw, a TTL timer due on that tick fires, and the test observer's `OnTickCompleted` is called with that tick.

**End to end with the real dispatcher**

- [x] **Drain → Pass A → Pass B → simulation → outbound**: with a real `TickCompletionQueue`, a real `InboundRequestDispatcher` and a gate, a held request whose gate is opened by a completion callback in `Drain` of tick N is handled on tick N before a new request that arrived before tick N; the simulation step runs after both handlers and the outbound step runs last. The recorded order is: completion callback, held request's handler (`WasHeld` true), new request's handler (`WasHeld` false), simulation, outbound.

**Enforcement and documentation**

- [x] **Source search**: `RegisterTickDriven(` appears in no executable line of `src/` other than the declaration in `ServerTickLoop.cs`. *(The composition root that makes the one allowed call does not exist yet; see Out of Scope.)*
- [x] **`DispatchTick` has one caller**: a source search finds `.DispatchTick(` called in `src/` only by `ZoneTickPipeline`.
- [x] **Doc comments corrected**: no doc comment in `src/` tells the reader to register game logic with `RegisterTickDriven`. The comments in `ServerTickLoop.cs`, `ConnectionStateMachine.cs` and `CrossCuttingRpcGuardChain.cs` that do so today say instead that the composition root registers `ZoneTickPipeline.Tick` once and that a system that ticks runs in the pipeline's simulation step. No executable line of those three files changes. *(Approved by the user 2026-10-09.)* The two doc-comment examples that show a bare `dispatcher.DispatchTick(tickLoop.ServerTickNumber)` — `IInboundRequestDispatcher.cs` near line 15 and `InboundRequestDispatcher.cs` near line 46 — say instead that `ZoneTickPipeline.Tick` makes the call (comments only). *(Added at `/story-readiness` 2026-10-09.)*

---

## Implementation Notes

- **New files**, in `src/ServerLogic/Networking/TickLoop/` (namespace `IronGrind.Networking`): `ZoneTickPipeline.cs`, `IZoneSimulationStep.cs`, `IZoneOutboundStep.cs`.
- **Dependencies are the interfaces**: `ITickCompletionQueue` (`TickCompletion/`), `IInboundRequestDispatcher` and `IServerTickSource` (`InboundDispatch/`). The ADR text says `InboundRequestDispatcher.DispatchTick`; `DispatchTick` is a member of `IInboundRequestDispatcher`, and taking the interface lets the order test use a recording fake. `ServerTickLoop` already implements `IServerTickSource`.
- **`Tick`**: read `_tickSource.ServerTickNumber` into a local, then four `try`/`catch (Exception ex)` blocks in a row, one per step. No loop over a list of delegates and no lambda: four direct calls, so nothing is allocated per tick. Write the four blocks out, or use one private method per step; do not pass a closure to a shared helper.
- **Why each step is isolated**: `ServerTickLoop.AdvanceTick` lets an exception from a tick-driven delegate escape, and then skips the TTL pass, the drift sample and the observer. The pipeline is the only tick-driven delegate, so it must not let one escape.
- **`Drain` and `DispatchTick` already catch their own failures.** `Drain` still throws `InvalidOperationException` when called off the tick thread or from a completion callback, and `DispatchTick` has a catch-all that logs `DispatchTickFailed`. The pipeline's `catch` is the outer bound for both; do not remove or change either inner one.
- **The error message is built inside the `catch`** only. One private method `LogStepFailed(string stepName, uint tick, Exception ex)` is fine: it is called only from a `catch`.
- **`currentTick` inside `AdvanceTick`**: `ServerTickNumber` is incremented before the tick-driven delegates run, so the value the pipeline reads is the number of the tick that is running.
- **Doc comments to correct** (comments only): `ServerTickLoop.cs` — the class remarks near lines 62 and 84–87, the class example near line 134, and the example of `RegisterTickDriven` near line 244; `ConnectionStateMachine.cs` — near lines 217 and 446; `CrossCuttingRpcGuardChain.cs` — near line 28. `RegisterTickDriven`'s own example becomes `tickLoop.RegisterTickDriven(pipeline.Tick);`. Search `src/` for `RegisterTickDriven` after the edit and read every hit. Also comments only: the `DispatchTick` line of the examples in `IInboundRequestDispatcher.cs` (near line 15) and `InboundRequestDispatcher.cs` (near line 46) becomes a comment line saying that `ZoneTickPipeline.Tick` calls `DispatchTick` once per tick.
- **Doc comments on the new types**: a summary and an example on `ZoneTickPipeline`, `Tick` and both interfaces. `IZoneSimulationStep`'s summary says its implementation runs ADR-002 phases 1–4 in ADR-002's order, with the game systems that tick in phase 3, in an order the composition root lists. `IZoneOutboundStep`'s summary lists what flushes: priority path, R-U batch, cycle broadcast, position packet.
- **`.meta` files**: Unity generates them on import; commit them with the story.

---

## Out of Scope

- The server composition root and the one `RegisterTickDriven(pipeline.Tick)` call in production code. No composition root exists in `src/` yet; the tests make the call on a `ServerTickLoop` they create.
- Implementations of `IZoneSimulationStep` (ADR-002 phases 1–4, the list of systems that tick) and of `IZoneOutboundStep` (the per-connection writers). They come with the composition root and the systems' own stories.
- Any change to `ServerTickLoop.AdvanceTick`, `TickCompletionQueue` or `InboundRequestDispatcher` code.
- The transport adapter (ADR-014 Migration Plan step 4, after ADR-004 OQ-ADR4-3).
- Enhancement Story 009 and the AC-ENH-38 wording at its line 33 (Migration Plan step 3).
- Removing `RegisterTickDriven` / `UnregisterTickDriven` or limiting them to one delegate.

---

## QA Test Cases

**File**: `tests/EditMode/Networking/TickLoop_ZoneTickPipeline_tests.cs` (new). Recording fakes for the four step dependencies and a settable tick source are private to this file or reuse `InboundSettableTickSource` from `InboundDispatchTestDoubles.cs`; every fake appends its name and arguments to one shared list, so the order is asserted on that list. Error logs are asserted with `LogAssert.Expect(LogType.Error, Regex)`. No sleeps, no real threads; a completed write is `Task.FromResult`.

**Construction**
- **Null arguments** — each of the five constructor arguments null in turn → `ArgumentNullException` whose `ParamName` is that parameter.
- **Fits `Action<float>`** — `tickLoop.RegisterTickDriven(pipeline.Tick)` on a real `ServerTickLoop`, then `AdvanceTick()` → the four fakes were each called once.

**Order**
- **Four steps in order** — tick source at 7, `Tick(0.05f)` → the shared list is exactly `Drain(7)`, `DispatchTick(7)`, `Run(7, 0.05)`, `Flush(7)`.
- **One tick number** — the completion fake sets the tick source to 99 when called; tick source starts at 7 → the three later steps still receive 7.
- **Two ticks** — tick source 7, `Tick`, tick source 8, `Tick` → eight entries; the first four carry 7 and the last four carry 8.
- **`deltaTime` passed through** — `Tick(0.123f)` → the simulation fake received `0.123f`.
- **No log when nothing throws** — one `Tick` with four fakes that do not throw → no error logged (`LogAssert.NoUnexpectedReceived`).

**Isolation**
- **Each step throwing in turn** (four cases, one per step) — that fake throws `InvalidOperationException("boom")` → the other three fakes were each called once, in order, with the tick; exactly one error is logged and it matches `\[ZoneTickPipeline\] StepFailed: step=<name> tick=7 - InvalidOperationException: boom`; `Tick` did not throw.
- **All four throw** → `Tick` returns; four errors, in the order `Completion`, `Requests`, `Simulation`, `Outbound`.
- **Next tick after a failure** — the simulation fake throws on tick 7 only → on tick 8 all four steps run and nothing is logged.
- **Rest of `AdvanceTick`** — real `ServerTickLoop`, `pipeline.Tick` registered, a TTL timer due on the next tick, the outbound fake throws, a recording `INetworkTestObserver` → `AdvanceTick` does not throw; the TTL callback ran once; the observer received `OnTickCompleted` with the loop's `ServerTickNumber`; one `StepFailed` error with `step=Outbound`.

**End to end with the real dispatcher**
- **Drain → Pass A → Pass B → simulation → outbound** — a real `TickCompletionQueue` (built as `TickLoop_CompletionQueue_tests.cs` builds it), a real `InboundRequestDispatcher` with one held R-OD type and one plain R-OD type registered and sealed (descriptors and message builder from `InboundDispatchTestDoubles.cs`), one ready connection, and a gate. Close the gate for the connection's character. Accept one held-type request and run one pipeline tick → its handler did not run, `HeldCount` is 1. Track `Task.FromResult(true)` with a callback that appends `"completion"` to the shared list and opens the gate. Accept one plain-type request. Run the next pipeline tick → the shared list for that tick is: `completion`, the held type's handler with `WasHeld` true, the plain type's handler with `WasHeld` false, `Run`, `Flush`; `HeldCount` is 0.

**Enforcement** (checked at `/story-done`, not automated)
- **Source search** — `RegisterTickDriven(` in `src/`: the declaration in `ServerTickLoop.cs` and doc comments only. `.DispatchTick(` in `src/`: one hit, the call in `ZoneTickPipeline.cs` (the two doc-comment examples no longer contain it).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_ZoneTickPipeline_tests.cs` — must exist and pass. The suite total before this story is 2182; record the total after it at `/story-done`. The tests of `TickLoop_Core_tests.cs`, `TickLoop_CompletionQueue_tests.cs` and the five `InboundDispatch_*` files must pass with no edit to those files.

**Status**: [x] Created and passing — 21/21; suite 2203/2203 (2026-10-09)

---

## Dependencies

- Depends on: Story 009 (Complete — `ServerTickLoop`), Story 030 (Complete — `ITickCompletionQueue`, `ICharacterMutationGate`), Story 036 (Complete — `IInboundRequestDispatcher`, `IServerTickSource`). ADR-014 Accepted (2026-10-09).
- Unlocks: the server composition root (registers `ZoneTickPipeline.Tick`); the transport adapter story (ADR-014 Migration Plan step 4) does not depend on this story.

---

## Completion Notes

**Completed**: 2026-10-09
**Criteria**: 14/14 passing. `/story-done` in lean mode (QL-TEST-COVERAGE and LP-CODE-REVIEW skipped).
**Test Evidence**: Logic — `tests/EditMode/Networking/TickLoop_ZoneTickPipeline_tests.cs`, 21/21. Suite 2203/2203 (baseline 2182), no compile error. `TickLoop_Core_tests.cs` (29), `TickLoop_CompletionQueue_tests.cs` (37) and the five `InboundDispatch_*` files pass with no edit.
**Enforcement searches (by hand)**: `RegisterTickDriven(` in `src/` — the declaration in `ServerTickLoop.cs` and four doc-comment examples, all `tickLoop.RegisterTickDriven(pipeline.Tick)`. `.DispatchTick(` in `src/` — one hit, the call in `ZoneTickPipeline.cs`. Every changed line of the five existing source files is a doc comment.
**Code Review**: Complete — `/code-review` 2026-10-09 (`unity-specialist` + `qa-tester`; ADR-014 Decision 6 check): APPROVED WITH SUGGESTIONS, nothing blocking. The user chose "fix all"; the four suggestions were applied.
**Deviations (advisory)**:
- `LogStepFailed` wraps its log call in a `try`/`catch` that swallows: an exception whose `Message` getter throws, or a throwing log handler, cannot escape `Tick`. A step whose log call fails leaves no log. Beyond the criteria; from the review, with one test (`Tick_ExceptionMessageGetterThrows_ReturnsWithoutLoggingAndLaterStepsStillRun`).
- Two `ServerTickLoop.cs` doc comments beyond the list in the Implementation Notes were corrected: the `RegisterTickDriven` summary and the `UnregisterTickDriven` example.
- 21 test cases against 12 QA cases: the null-argument case and the "each step throwing" case are `[TestCase]` ×5 and ×4, and two tests come from the review (the runtime type name in the log; the log-call guard). `StepFailedRegex` is anchored.
- The end-to-end test builds `new TickCompletionQueue()` with its defaults instead of a fake thread-id function; the real-loop tests use `NetworkTestObserver` and `ServerTickLoop.FIXED_DELTA_TIME`.
- "`deltaTime` goes to no other step" holds by the shape of the interfaces (no other step has a `float` parameter); the test asserts the value `Run` receives.
**Tech debt**: none logged.
