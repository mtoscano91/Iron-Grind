# Story 042: Zone Server Composition — Lifecycle Shell, Teardown 1–8, Exit Codes, Quit Interception

> **Epic**: Networking Core
> **Status**: Ready — `/story-readiness` 2026-10-22: NEEDS WORK (4 gaps), all fixed the same day at the user's request (AC-8 relies on the queue's own shutdown alerts; AC-9 names `TryBeginTeardown()`; AC-14 defines `Dispose()`; `RegisterDisposable` named, drain timeout 10 s stated).
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 10 hours (provisional — confirm at `/story-readiness`)

*Added 2026-10-22 (`/create-stories networking-core`, ADR-009 Amendment 1 Migration Plan step 2, first of four stories 042–045). Nothing outside tests builds the server today (2026-10-19 `/create-stories` BLOCKED finding); these four stories add the server composition root. This story builds the class, its seams and the teardown; Story 043 adds startup, Story 044 the real object graph and database, Story 045 the Unity host and the local runs.*

## Context

**GDD**: `design/gdd/networking-core.md` (tick loop start and stop around zone lifetime); `design/gdd/zone-instancing.md` CR-ZI-12 (zone close steps 1–8 — called through a stand-in here); `design/gdd/navigation-pathfinding.md` CR-NAV-3 (nav teardown first on `Closed` — stand-in). No rule or value of a GDD changes.
**Requirement**: `TR-net-008` (tick loop lifetime around nav initialisation and gateway registration; this story covers the stop side)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-009 Amendment 1** — Decision 3 (replaced, teardown 1–8), Decision 5 (composition root), Key Interfaces (Amendment 1).
Also: **ADR-011** Decision 2 (shutdown drain, dated note 2026-10-19 — one of the two blocking waits); **ADR-015** Decision 3 (listener `Stop()` at step 2a and from the host's `OnDestroy`); **ADR-002** Decision 3 (nav calls halted once the tick stops); **ADR-012** (all types in `IronGrind.ServerLogic`).

**ADR Decision Summary**: one plain C# `ZoneServerComposition` owns everything the zone process builds and tears it down in one fixed order; triggers only call `RequestTeardown(code)`, the host's `Update` starts the teardown coroutine once, and the process ends only through `Application.Quit((int)code)` at step 8. Unity-facing work goes through small seams so EditMode tests run the whole order without NGO, a scene or a database.

**Engine**: Unity 6.3 LTS | **Risk**: LOW for this story — plain C# against seam interfaces; the engine behaviour behind the seams (A1-1 to A1-7) is Story 045's.
**Engine Notes**: `IEnumerator` coroutines are driven by the test directly (`MoveNext()` loop), not by Unity. The real `Application.wantsToQuit`, `Application.Quit(int)`, `NetworkManager.Shutdown()` / `ShutdownInProgress` and `NetworkTickSystem.Tick` are reached only through the seams (Story 045 implements them).

**Control Manifest Rules (Foundation, v2026-10-19)**:
- Required: `ZoneServerComposition` reaches NGO, the scene load, NavMesh and `Application` through small seams; EditMode tests run teardown order and idempotence without NGO, a scene or a database — ADR-009 A1 Decision 5
- Required: a trigger only calls `RequestTeardown(code)`, which records the first code; the host's `Update` starts the teardown coroutine once; strict order 1 → 2a → 3 → 4 → 4b → 5 → 6 → 7 → 8 — ADR-009 A1
- Required: `OnWantsToQuit` returns `true` in the Editor and once step 8 has set `quitAllowed`; otherwise it calls `RequestTeardown(ZoneExitCode.Clean)` and returns `false` — ADR-009 A1
- Required: the host's `OnDestroy` runs steps 1–7 synchronously (no yield in step 6, no step 8), unless the teardown already finished — ADR-009 A1, ADR-015
- Required: `ZoneExitCode` `Clean = 0`, `StartupFailed = 1`, `Fatal = 2`; tasks still incomplete after the drain are logged and alerted and do not change the code — ADR-009 A1
- Required: stand-ins log `"[ZoneServer] <step> not implemented"` once each and do nothing — ADR-009 A1
- Forbidden: ending the process with `Process.Exit` or `Environment.Exit`; running the teardown inside a tick, a completion callback or the `wantsToQuit` handler; using `OnApplicationQuit` as the teardown path; returning `true` from `OnWantsToQuit` in a player before step 8 — ADR-009 A1, ADR-011
- Guardrail: NGO shutdown wait at most `NGO_SHUTDOWN_WAIT_FRAMES` = 100 frames, then a warning and on (server config, not a constant) — ADR-009 A1
- Guardrail: blocking waits in the server are exactly two; this story owns the drain (teardown step 5) — ADR-009 A1, ADR-011

---

## Acceptance Criteria

*From ADR-009 Amendment 1 Decision 3 / Decision 5 and the Key Interfaces, scoped to this story:*

- [ ] **AC-1** `ZoneExitCode` exists with exactly `Clean = 0`, `StartupFailed = 1`, `Fatal = 2`.
- [ ] **AC-2** `RequestTeardown(code)` records the first code it receives and ignores later codes; it never tears anything down itself, and calling it any number of times from any state is safe.
- [ ] **AC-3** `RunTeardown()` runs the steps in exactly this order against the seams and the owned objects: 1 tick unsubscribe → 2a listener `Stop()` → 3 `IZoneNavigation.Teardown()` → 4 CR-ZI-12 (through `IZoneCloseSteps`) → 4b one `outbound.Flush(currentTick)` → 5 `queue.DrainOnShutdown(watchdog duration)` → 6 transport shell `Stop()`, NGO `Shutdown()`, wait → 7 Dispose pass → 8 `quitAllowed = true`, `Quit((int)code)` with the recorded code.
- [ ] **AC-4** Step 6 yields one frame at a time while the NGO seam reports a live manager with `ShutdownInProgress`, for at most `NGO_SHUTDOWN_WAIT_FRAMES` frames; at the cap it logs one warning and continues; a null or destroyed manager ends the wait at once.
- [ ] **AC-5** The Dispose pass disposes every `IDisposable` registered through `RegisterDisposable`, in reverse registration order; the data source is registered first so it is disposed last; an exception from one `Dispose` is logged (type name only) and the pass continues.
- [ ] **AC-6** Every teardown step null-guards what it touches: a step whose object was never built (or whose NGO manager is already destroyed) is skipped without throwing, and the following steps still run.
- [ ] **AC-7** An exception thrown inside a step is logged with the step name and the teardown continues with the next step; the exit code is not changed by it.
- [ ] **AC-8** Step 5 calls `DrainOnShutdown(timeout)` once and leaves the alerts for tasks still incomplete to the queue (`TickCompletionQueue` already raises them inside `DrainOnShutdown`; the composition does not count or re-log them). The recorded exit code is the same after step 5 as before it, also when `DrainOnShutdown` throws — the exception is logged as step 5 and the teardown goes on to step 6.
- [ ] **AC-9** The host contract: `bool TryBeginTeardown()` returns `true` exactly once, on the first call after `RequestTeardown`, and `false` before any request and on every later call; the host's `Update` starts `RunTeardown()` only when it returns `true`. `RunTeardown()` called a second time yields nothing and does nothing.
- [ ] **AC-10** `OnWantsToQuit()` returns `true` when the Application seam reports the Editor; returns `true` after step 8 set `quitAllowed`; otherwise calls `RequestTeardown(Clean)` and returns `false`.
- [ ] **AC-11** `TeardownNow()` runs steps 1–7 synchronously (step 6 issues `Shutdown()` without yielding or waiting) and never calls `Quit`; it does nothing if the teardown already finished, and if a coroutine teardown is part-way, it runs only the remaining steps up to 7.
- [ ] **AC-12** Stand-ins for `IZoneNavigation`, `IZoneBoundsValidator`, `IGatewayRegistration`, `IZoneCloseSteps` and `IZoneStaticDataLoader` each log `"[ZoneServer] <step> not implemented"` exactly once per instance, however often they are called, and do nothing else.
- [ ] **AC-13** A source search finds no `Process.Exit`, `Environment.Exit` or `OnApplicationQuit` in `src/ServerLogic/ZoneServer/`.
- [ ] **AC-14** `ZoneServerComposition : IDisposable` (ADR-009 A1 Key Interfaces): `Dispose()` does the same as `TeardownNow()` — steps 1–7, no `Quit`, idempotent; calling `Dispose()` twice, or after `TeardownNow()` or a finished `RunTeardown()`, adds no teardown call.

---

## Implementation Notes

*Derived from ADR-009 Amendment 1 Decisions 3 and 5, Key Interfaces:*

- Location: `src/ServerLogic/ZoneServer/` (namespace `IronGrind.ServerLogic.ZoneServer`), assembly `IronGrind.ServerLogic`. Files: `ZoneExitCode.cs`, `ZoneServerComposition.cs`, the seam interfaces (one file each), `ZoneServerStandIns.cs`.
- Seams this story declares (Story 043 adds the startup methods to them, Story 045 implements them over Unity):
  - `IZoneApplication` — `bool IsEditor`, `void Quit(int code)`, `void SubscribeWantsToQuit(Func<bool>)` / `Unsubscribe…` (named method group, no lambda — manifest forbids lambda captures for persistent subscriptions).
  - `IZoneNetwork` — tick subscription (`SubscribeTick(Action)` / `UnsubscribeTick(Action)`), `void Shutdown()`, `bool IsAlive` (non-null, not destroyed), `bool ShutdownInProgress`.
  - `IZoneNavigation` (`Initialize(ZoneID, NavMeshDataInstance)`, `Teardown()`), `IZoneBoundsValidator`, `IGatewayRegistration` (`RegisterZone(ZoneID, ushort)`) — exact shapes from the ADR's Key Interfaces; `IZoneCloseSteps` (`RunCloseSteps()` — CR-ZI-12 steps 1–8) and `IZoneStaticDataLoader` are this story's additions under the same rule.
- Teardown owns no game logic: it calls what Story 044 will build. In this story the composition holds those objects through narrow fields the tests can set (an `internal` test entry point or constructor overload, as Story 014's `NpgsqlRecordStoreTestAccess`), typed by the existing interfaces: `ITickCompletionQueue`, `IZoneOutboundStep`, a stop-able listener (`ICrossZoneRelayListener` with `Stop()` — add the interface over `NpgsqlRelayListener` if none exists; `Stop()` is already idempotent), the transport shell (an `IZoneInboundTransport` with `Stop()`).
- Registration for the Dispose pass: `internal void RegisterDisposable(IDisposable disposable)` appends to one list; only the composition itself (Story 044 calls it from S4 — the data source first — and S5) and the test entry point call it. `IronGrind.ServerLogic` already exposes internals to the EditMode test assembly (check `AssemblyInfo.cs`).
- Drain timeout = `TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS / ServerTickLoop.TICK_RATE_HZ` seconds as a `TimeSpan` — 200 / 20 = 10 s (the watchdog duration — ADR-011 Decision 2; matches the manifest's teardown budget "drain (10 s)"). Compute it, do not hard-code 10.
- `DrainOnShutdown` calls `AssertTickThread()`: the queue must be built on the thread that runs the teardown. In a player both are the main thread; tests build the queue (or a fake) on the test thread and drive the teardown there. The queue raises its own alerts for tasks left incomplete — the composition adds none.
- `RunTeardown()` is an `IEnumerator`; only step 6 yields. Wrap each step in its own `try`/`catch` inside a non-yielding method; the yield loop of step 6 sits outside any `try` with a `catch` (C# rule; manifest forbidden pattern).
- Track progress with one `int _teardownStep` (last completed step) so `TeardownNow()` and `Dispose()` resume and all three entry points stay idempotent.
- `RequestTeardown` must not touch anything but the recorded code and a flag; the host calls `TryBeginTeardown()` from `Update`. No teardown work in `OnWantsToQuit`.
- Logs use the prefix `[ZoneServer]`. Exception logging: type name and step, not messages that might carry configuration.
- Doc comments on every public type and member (coding standards).

---

## Out of Scope

- Story 043: `Start(ZoneStartupArgs)` S0–S10, S-fail, quit during startup, `ZoneServerConfig` and command-line parsing.
- Story 044: building the real objects (database, relay, dispatcher, outbox, pipeline) and registering them for the Dispose pass; the S4 database test.
- Story 045: `ZoneServerHost` `MonoBehaviour`, the Unity implementations of the seams, the scenes, the build profile and the A1 / ADR-015 V4, V6 runs.
- Zone Instancing / Navigation epics: real `IZoneCloseSteps`, `IZoneNavigation`, gateway client (replace the stand-ins).

---

## QA Test Cases

*Written at story creation (lean mode — QL-STORY-READY skipped). The developer implements against these.*

- **AC-1**: exit code values
  - Given: the enum. When: cast to `int`. Then: 0, 1, 2; exactly three members.
- **AC-2**: first code wins
  - Given: a composition. When: `RequestTeardown(Fatal)` then `RequestTeardown(Clean)`, then the teardown runs. Then: the Application seam receives `Quit(2)`.
  - Edge cases: no request → the host never starts the teardown; request after the teardown finished → no effect, no second `Quit`.
- **AC-3**: teardown order
  - Given: fakes for every seam and owned object that append their name to one shared call log. When: `RunTeardown()` is driven to completion. Then: the log equals `TickUnsubscribe, ListenerStop, NavTeardown, CloseSteps, OutboundFlush, Drain, ShellStop, NgoShutdown, Dispose…, Quit`.
  - Edge cases: the `Flush` call receives the last tick number; `Drain` receives the watchdog `TimeSpan`.
- **AC-4**: NGO wait cap
  - Given: NGO fake with `ShutdownInProgress` true for 3 frames. When: driven. Then: exactly 3 yields in step 6, no warning.
  - Edge cases: in progress forever with cap 100 → 100 yields, one warning, step 7 runs; `IsAlive` false → 0 yields; cap configured 1 → 1 yield.
- **AC-5**: Dispose order
  - Given: disposables A (data source), B, C registered in that order; B throws. When: step 7. Then: disposed C, B, A; one log naming B's exception type; A still disposed.
- **AC-6**: null-guards
  - Given: a composition where nothing was built. When: `RunTeardown()`. Then: no exception; only `Quit` is called (and the stand-ins' steps are skipped or log).
- **AC-7**: step exception isolation
  - Given: the listener fake throws from `Stop()`. When: teardown. Then: one log naming step 2a; steps 3–8 still run; exit code unchanged.
- **AC-8**: drain leaves the code alone
  - Given: a queue fake recording `DrainOnShutdown` calls; code `Fatal` recorded. When: teardown. Then: one `DrainOnShutdown(10 s)` call; `Quit(2)`; no composition log about incomplete tasks.
  - Given: the queue fake throws from `DrainOnShutdown`, code `Clean`. Then: one log naming step 5 and the exception type; step 6 onward run; `Quit(0)`.
  - Edge cases: a real `TickCompletionQueue` with one tracked task that never completes and a test-sized watchdog → the queue's own alert appears once (`LogAssert.Expect`), the composition adds none.
- **AC-9**: single start
  - Given: no request. When: `TryBeginTeardown()`. Then: `false`.
  - Given: `RequestTeardown(Clean)`. When: `TryBeginTeardown()` called 3 times. Then: `true`, `false`, `false`; a second `RunTeardown()` enumerator ends immediately with an empty call log.
- **AC-10**: `OnWantsToQuit`
  - Given: Application fake `IsEditor = true`. When: called. Then: `true`, no teardown requested.
  - Given: player, before teardown. Then: `false`, code `Clean` recorded.
  - Given: player, after step 8. Then: `true`.
  - Edge cases: called repeatedly before step 8 → always `false`, code stays the first one (e.g. `Fatal` recorded earlier stays `Fatal`).
- **AC-11**: `TeardownNow`
  - Given: nothing torn down. When: `TeardownNow()`. Then: steps 1–7 in order, `Shutdown()` called, zero waiting, no `Quit`.
  - Given: coroutine teardown stopped after step 4b (driven partially). When: `TeardownNow()`. Then: only 5, 6, 7 run.
  - Given: teardown finished. When: `TeardownNow()`. Then: empty call log.
- **AC-12**: stand-ins log once
  - Given: each stand-in. When: called 3 times. Then: exactly one log line `"[ZoneServer] <step> not implemented"` (`LogAssert.Expect` once, `LogAssert.NoUnexpectedReceived`).
- **AC-13**: source search
  - Given: the files under `src/ServerLogic/ZoneServer/` with comments stripped. Then: no `Process.Exit`, `Environment.Exit`, `OnApplicationQuit`.
- **AC-14**: `Dispose()`
  - Given: nothing torn down. When: `Dispose()` twice. Then: steps 1–7 once, in order; no `Quit`.
  - Edge cases: `Dispose()` after `TeardownNow()` → empty call log; `Dispose()` after a finished `RunTeardown()` → empty call log; `Dispose()` with a coroutine teardown stopped after step 4b → only 5, 6, 7.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Networking/ZoneServer_CompositionTeardown_tests.cs` — must exist and pass (project convention: EditMode tests under `tests/EditMode/<System>/`)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 030, 037, 039, 041 (Complete) — queue, pipeline, outbox, listener types it calls
- Unlocks: Story 043
