# Story 014: Composition Wiring, Quit-Request Drain and Draining Rejection

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-26) — written 2026-10-22; `/story-readiness` 2026-10-23: NEEDS WORK (13 gaps) → fixed → Ready; `/story-done` 2026-10-26: COMPLETE WITH NOTES
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 18 hours (readiness 2026-10-23; was 4 h provisional — includes TD-078, ~2 h)

## Context

**GDD**: `design/gdd/zone-instancing.md` — T-3 to T-7, CR-ZI-12 drain paragraph, AC-ZI-21 to AC-ZI-25
**Requirement**: `TR-zi-002`, `TR-zi-012` *(epic placeholders)*

**ADR Governing Implementation**: ADR-009 Amendment 1 (composition root is the only place that constructs server systems; S9 Active; `Application.wantsToQuit` → teardown) + dated note 2026-10-22 (a quit request starts T-4; a second one is T-6)
**ADR Decision Summary**: `ZoneServerComposition` constructs and connects every server system; the quit hook now starts the drain instead of tearing down at once; teardown runs when the lifecycle reaches Closed.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`Application.wantsToQuit` behaviour on a Dedicated Server build — Story 045 findings)
**Engine Notes**: `OnWantsToQuit` returning false defers the quit; it keeps returning false until teardown step 8 (Story 045 AC-14). Windows has no SIGTERM: the local run triggers the quit with the development-only `-teardownAfterSeconds <n>` argument (Story 045). The lifecycle's `observer` constructor parameter exists only under `UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` — check a non-development server build compiles.

**Control Manifest Rules (this layer)**:
- Required: the composition root is the only place that constructs server systems — ADR-009 A1
- Forbidden: registering a core system's own delegate with `ServerTickLoop.RegisterTickDriven` — the tick order is `ZoneTickPipeline` — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [x] **Construction site (user decision, readiness 2026-10-23)**: a new `ZoneInstancingWiring` (`src/ServerLogic/ZoneServer/ZoneInstancingWiring.cs`) is built at S5 (`ConstructObjects`) after the dispatcher is created and before it is sealed, from the S0 `ZoneID` and the S5 outputs (tick loop as `IServerTickSource`, `TickCompletionQueue`, dispatcher, outbox, `ICharacterPersistence`, transport disconnector). It constructs and owns Stories 001–013's systems: `ZoneLifecycle`, `ZoneSlotPool`, `ZoneMobRoster`, `ZoneConnectionCharacterDirectory`, `ZoneTickRoster`, `ZoneEntryAdmission` (with `ZoneEntryReclamation`, `ZoneEntryCompletion` as continuation), `ZoneSnapshotRequestService`, `ZoneCloseSteps`, `ZonePlayerExit`, `SessionEndCoordinator`. It implements the adapters `IZoneStateOwner` (S9 `MarkActive` = T-2, S6 invalid bounds = `MarkInitFailed`, T-3), `IZoneCloseSteps`, `IZoneLifecycleListener`, `IZoneSlotPoolListener`, `IZoneDrainNoticeLog`. The directory replaces `StandInConnectionCharacterDirectory` in the dispatcher. `ZoneServerHost` no longer passes `StandInZoneStateOwner`, `StandInZoneCloseSteps` or the bounds stand-in; the real bounds validator and `AddNavMeshData` path is Story 004's. The composition's backend-independent parts (quit hook, close → teardown link) take the wiring through an internal seam so they are testable with the fake backend.
- [x] **Remaining stand-ins**, each logging once: Party (`NoPartyZonePresence`), mob spawn timers (`LoggingZoneMobSpawnTimers`), navigation, gateway registration, token validation (`RejectingZoneEntryTokenValidator`), `SessionReady` (`LoggingZoneEntryResponder`), the connection activity sink, the reclaim session sink (TD-077), the teardown session sink (TD-079), a new logging `IZoneEntityStateSource` stand-in, and the session-layer inputs (transport disconnect → coordinator, logout → `BeginCleanLogout`, ghost TTL enumeration → `EvaluateSessionTTL`: session epic — the coordinator is constructed but nothing in production calls it yet).
- [x] **Slot pool → lifecycle**: the `IZoneSlotPoolListener` adapter forwards `OnOccupiedPlayerSlotsChanged` to `ZoneLifecycle.OnOccupiedPlayerSlotsChanged` and `OnEntityIDCounterExhausted` to `RequestFatal`. **AC-ZI-21** through the wiring: idle 60 s (test config), last slot released, `60 × TICK_RATE_HZ` ticks → T-7 at that tick; a slot allocated one tick earlier cancels it.
- [x] **Tick order**: the lifecycle is ticked from `ZoneTickPipeline` (not `RegisterTickDriven`); one wiring simulation step runs, in this order: a pending quit (below) → `ZoneTickRoster.ActivatePending(currentTick)` → `ZoneEntryReclamation.OnTick` → session TTL evaluation (stand-in source; empty in production) → `ZoneLifecycle.OnTick`. The roster is built with the same `IServerTickSource` as the pipeline. No allocation per tick in this step.
- [x] **Loading tick (T-3 timeout)**: the server tick does not advance before S9, so during the S6 wait the composition calls `lifecycle.OnTick(floor((clock − S6 start) × TICK_RATE_HZ))` each polled frame, with the lifecycle built with `loadingStartTick = 0`. T-3 by timeout ends the S6 wait as S-fail (`StartupFailed`); a test advances the fake clock past `ZONE_LOAD_TIMEOUT_SECONDS`.
- [x] **Quit (T-4)**: `OnWantsToQuit` before S5 keeps today's `RequestTeardown(Clean)`; while Loading it calls `RequestQuit` at once (T-6); in Active or Draining it records a pending quit that the next tick applies with that tick's number (T-4 in Active, T-6 in Draining). Two quits before the next tick count as one quit request per tick applied, in order. It still returns false.
- [x] **T-4 broadcast**: `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=ZONE_DRAIN_GRACE_SECONDS)` to every `Connected` client, none to ghosts; every client sent one is recorded and answers `IZoneDrainNoticeLog.WasNotifiedAtDrain` for Story 012 step 4.
- [x] **Draining rejection**: a new `SessionHandshake` while Draining gets no slot (`ZoneFullResponse(ZoneDoesNotExist)` via Story 009's step 4).
- [x] **Close → teardown, exit codes**: `OnClosed` calls `RequestTeardown` with: Drained (T-5) → `Clean`; Idle (T-7) → `Clean`; Emergency from a quit (second quit, quit while Loading) → `Clean`; Emergency from `RequestFatal` or exhaustion → `Fatal`; LoadFailed (T-3) → no call from the listener (S-fail already requests `StartupFailed`). The wiring records the cause before calling the lifecycle. A direct `RequestTeardown(Fatal)` (tick failure) also calls `lifecycle.RequestFatal` so admission stops before the next `Update`; `RequestTeardown` keeps the first code. The listener catches and logs (never throws).
- [x] **Drain after close steps**: teardown step 6 (`DrainOnShutdown`) runs whenever the close steps ran (step 4), including the T-3 path where the tick never started (Story 012 `/code-review` note e).
- [x] **AC-ZI-22**: 2 Connected + 1 ghost; quit → Draining, `ZoneSessionEnded(grace)` to both Connected only, a new handshake gets no slot; all 3 released before grace → T-5 on the last release's tick. The test drives the coordinator directly: the 2 Connected by `BeginCleanLogout`, the ghost by `EvaluateSessionTTL` at its expiry tick. *(The ghost's release by a reconnect while Draining is deferred with the gateway re-routing work — Story 013 readiness.)*
- [x] **AC-ZI-23 (timing)**: Draining with 1 Connected + 1 ghost (250 s TTL left); after `ZONE_DRAIN_GRACE_SECONDS × TICK_RATE_HZ` ticks → T-5 at that tick; the close steps run: the teardown session sink (TD-079 stand-in) gets `SaveGhostSnapshotAndExpire` exactly once for the ghost's character, no `SaveSession` for the ghost, the Connected client is closed (`ExpireConnectedSession` once) and receives no second `ZoneSessionEnded`.
- [x] **AC-ZI-24**: second quit while Draining → Closed on that tick (T-6), exit `Clean`; every occupied slot saved; no second `ZoneSessionEnded`.
- [x] **AC-ZI-25**: Active with no slot; quit → Closed on that tick, exit `Clean`; no `ZoneSessionEnded`.
- [x] **Exhaustion**: Story 002's `OnEntityIDCounterExhausted` → T-6 → `RequestTeardown(Fatal)`.
- [x] **TD-078 (user decision, readiness 2026-10-23)**: `ZoneEntryAdmission` keys pending loads by `CharacterID` as well; a second `SessionHandshake` for a character whose load is in flight drops the first pending entry and closes that connection with no message (as reclamation case (b)); the character ends with at most one slot. TD-078 closed in the register.
- [x] **Knobs**: `ZONE_IDLE_CLOSE_SECONDS`, `ZONE_DRAIN_GRACE_SECONDS`, `ZONE_LOAD_TIMEOUT_SECONDS` stay `public const` defaults passed through constructor parameters (Story 040 pattern); no config file entry.
- [x] **Verification**: the host's `-teardownAfterSeconds` trigger calls the composition's quit path (not `RequestTeardown(Clean)`); Story 045's local run reaches Active, is triggered within `ZONE_IDLE_CLOSE_SECONDS` (120 s) of Active, and logs T-4 and T-5 on the same tick (no players), teardown, exit code `Clean`.
- [x] **Deployment constraint** recorded in the story's completion notes: `ZONE_DRAIN_GRACE_SECONDS` + teardown persistence drain < host stop timeout (systemd `TimeoutStopSec`, default 90 s). Also: an idle zone process now exits `Clean` after 120 s (T-7).

---

## Implementation Notes

- **From Story 001's code review (2026-10-22):** (a) T-3 by timeout needs `OnTick` during the S6 wait — see the Loading tick criterion; (b) the `observer` parameter call site must sit inside the same `#if`; (c) `IZoneLifecycleListener` implementations must not throw.
- **From Story 011 (2026-10-23):** (a) pass `ZoneEntryReclamation` to `ZoneEntryAdmission`, call `ZoneEntryReclamation.Register(dispatcher, …)` before the dispatcher is sealed, tick it in the order above; (b) its close seam is `IInboundTransportDisconnector.DisconnectClient(clientId)` — the zone client id must equal the transport client id, or pass an adapter; (c) session sink `LoggingZoneEntryReclaimSessionSink` until TD-077.
- **From Story 012 readiness (2026-10-23):** (a) `ZoneCloseSteps` constructor order: pool, directory, tick roster, `ZoneEntryReclamation`, persistence, tick completion queue, party, `IZoneMobSpawnTimers`, `IZoneTeardownSessionSink`, `IZoneDrainNoticeLog`, outbound sink, `ZoneID`; (b) `NotifyZoneExit` for saved characters runs from save callbacks inside `DrainOnShutdown` (step 6), so the drain stays after the close steps; connections close only at step 7; (c) session sink stand-in is TD-079.
- **From Story 013 (2026-10-23):** construct `SessionEndCoordinator` with `removeEntityFromZone: _ => { }` and `broadcastPlayerLeftZone:` `ZonePlayerExit.ReleasePlayer`. `zone.RemoveEntity` in the coordinator's `<example>` does not exist; future per-entity removal hooks go in the leave path. Do not pass `ReleasePlayer` to a direct `ProcessExplicitDisconnect` call.
- Nothing in `src/` constructs `ConnectionStateMachine`, and the composition has no transport-disconnect callback; wiring them is session-epic work (Out of Scope).
- `ZoneServerComposition.cs` is ~1,300 lines: put construction and adapters in `ZoneInstancingWiring`; the composition change is S5 construction, the S6 loading tick, the quit hook and the direct-Fatal link.
- The wiring's step is added to `SimulationSteps` (phase 3), not registered with the tick loop.

---

## Out of Scope

- Gateway routing disable / operator drain command (gateway epic)
- Real Party / Mob Spawning / navigation / token validation / `IZoneEntityStateSource`
- Session layer: `ConnectionStateMachine` construction, transport-disconnect handling, logout message routing, ghost TTL enumeration (session epic); ghost release by reconnect while Draining (gateway re-routing)
- TD-077 / TD-079 sinks

---

## QA Test Cases

- **AC-ZI-21 / 22 / 23 / 24 / 25, exhaustion, TD-078** — on `ZoneInstancingWiring` with fakes (persistence, outbox, disconnector, tick source), ticks advanced programmatically; no database.
- **Quit hook / exit codes / loading tick / direct Fatal** — on the composition with the fake backend.
- **Stand-in log** — a default wiring logs each remaining stand-in once.
- **Release build** — a non-development server build compiles (observer `#if`).
- **Verification** — Story 045's local run: start → Active → `-teardownAfterSeconds` (< 120 s) → T-4 + T-5 → teardown → exit code `Clean`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Wiring_tests.cs` — AC-ZI-21..25, exhaustion, stand-in log, TD-078
- `tests/EditMode/Networking/ZoneServer_CompositionDrain_tests.cs` — quit hook, exit-code table, loading tick, direct Fatal, drain after close steps
- the Story 045 local verification rerun noted in the story

**Status**: [x] Created — Wiring 36, DuplicateLoad 12, CompositionDrain 22 (all pass 2026-10-26); local run 2026-10-26 (Completion Notes)

---

## Dependencies

- Depends on: Stories 001–013 (Complete); TD-078 (fixed here)
- Unlocks: None (epic complete apart from the gateway-blocked requirements)

---

## Completion Notes
**Completed**: 2026-10-26
**Criteria**: 19/19 passing (all test-covered; Verification and Release build by local server builds)
**Deviations** (advisory, accepted; no tech debt logged — the session epic and Inventory work own them):
(1) The wiring constructs a `ConnectionStateMachine` that nothing feeds yet (`SessionEndCoordinator` requires one; session epic).
(2) Snapshot request serving (`ServePending`) is the last action of the wiring's tick step (Story 008 needs a tick); not in the criterion's order list.
(3) `StandInZoneInventoryService` added (no `InventoryService` in `src/`), logs once.
(4) Quit-stall fallback added at `/code-review`: `QUIT_TICK_STALL_SECONDS` = 5 — a pending quit or Draining with no tick for 5 s requests a Clean teardown (host `Update` → `PollQuitStall`).
(5) `RequestQuit` does nothing once a teardown was requested; two quits before one tick apply in order (T-4 then T-6).
(6) Dev-only transition log added 2026-10-26 for the verification run: `ZoneInstancingWiringOptions.LogTransitions` (from the composition's `LogTeardownSteps`, compiled only under `DEVELOPMENT_BUILD || UNITY_EDITOR`) logs `[ZoneInstancingWiring] ZoneID(<id>) Active|Draining|Closed(<transition>) at tick <n>`; 2 tests.
(7) Not done: the duplicated test fakes are not merged into one file; in EditMode tests `ZoneCloseTransition` is inferred from path, occupancy and tick (no production seam).
**Scope**: also touched `ZoneEntryReclamation.cs` (public `CloseConnection`, TD-078), `ZoneServerStandIns.cs`, `ZoneServerHost.cs`, `LoggingZoneEntityStateSource.cs` — all needed by the criteria.
**Deployment constraint**: `ZONE_DRAIN_GRACE_SECONDS` (60 s) + the teardown persistence drain must stay below the host stop timeout (systemd `TimeoutStopSec`, default 90 s): about 30 s left for the drain. An idle zone process now exits `Clean` 120 s (`ZONE_IDLE_CLOSE_SECONDS`, T-7) after its last player leaves.
**Verification run (2026-10-26)**: Windows Dedicated Server, Mono, Development (Build Profile `ZoneServer`, batch build), portable PostgreSQL 16.15; `-zoneTemplate StartingZone -zoneId 1 -port 7777 -teardownAfterSeconds 60`: `ZoneID(1) Active at tick 0` → `-teardownAfterSeconds 60 reached: requesting a quit (drain)` → `Draining at tick 1185` + `Closed(Drained) at tick 1185` (T-4 + T-5 same tick, no players, ~59 s after Active < 120 s) → teardown steps 1–7 (2a listener stop 335 ms) → `teardown step 8: quit(0)`; exit code 0 (`Clean`), 68.8 s wall; no quit-stall warning; 0 connection-string lines. Non-development server build: Build Finished Success, no `error CS`. Build side effects reverted.
**Test Evidence**: Integration — `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Wiring_tests.cs` (36), `ZoneInstancing_EntryAdmissionDuplicateLoad_tests.cs` (12), `tests/EditMode/Networking/ZoneServer_CompositionDrain_tests.cs` (22); EditMode batch 4122 total, 4052 passed, 0 failed, 70 skipped (DB).
**Code Review**: Complete — `/code-review` 2026-10-23 CHANGES REQUIRED → fix all; the 2026-10-26 transition-log addition was not re-reviewed (lean).
