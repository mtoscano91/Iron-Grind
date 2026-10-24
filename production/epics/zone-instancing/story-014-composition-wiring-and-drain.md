# Story 014: Composition Wiring, Quit-Request Drain and Draining Rejection

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — T-4 to T-7, CR-ZI-12 drain paragraph, AC-ZI-22 to AC-ZI-25
**Requirement**: `TR-zi-002`, `TR-zi-012` *(epic placeholders)*

**ADR Governing Implementation**: ADR-009 Amendment 1 (composition root is the only place that constructs server systems; S9 Active; `Application.wantsToQuit` → teardown) + dated note 2026-10-22 (a quit request starts T-4; a second one is T-6)
**ADR Decision Summary**: `ZoneServerComposition` constructs and connects every server system; the quit hook now starts the drain instead of tearing down at once; teardown runs when the lifecycle reaches Closed.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`Application.wantsToQuit` behaviour on a Dedicated Server build — Story 045 findings)
**Engine Notes**: `OnWantsToQuit` returning false defers the quit; check Story 045's verification notes before changing it.

**Control Manifest Rules (this layer)**:
- Required: the composition root is the only place that constructs server systems — ADR-009 A1
- Forbidden: registering a core system's own delegate with `ServerTickLoop.RegisterTickDriven` — the tick order is `ZoneTickPipeline` — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] The composition constructs Stories 001–013's systems and replaces the stand-ins: `IZoneStateOwner` → the lifecycle (S9 `MarkActive` = T-2, S6 failure = T-3), bounds validator and `AddNavMeshData` (Story 004), close steps (Story 012), connection directory (Story 010 `ZoneConnectionCharacterDirectory`, passed to the dispatcher and to `ZoneEntryAdmission`; `ZoneEntryCompletion` as the admission continuation). Stand-ins remain only for Party (`NoPartyZonePresence`), Mob Spawning, navigation, gateway registration, token validation, `SessionReady` (`LoggingZoneEntryResponder`), the connection activity sink (session epic; readiness of Story 010, 2026-10-23) and `IZoneEntityStateSource` (unless a production implementation exists by then), each logging once.
- [ ] The lifecycle is ticked from `ZoneTickPipeline` (not `RegisterTickDriven`); `ZoneTickRoster.ActivatePending(currentTick)` (Story 010, step 10) runs at the start of step 3. The roster is built with the same `IServerTickSource` as the pipeline (it records the admission tick and activates only in a later tick, code review 2026-10-23).
- [ ] **Quit (T-4)**: `OnWantsToQuit` in Active starts T-4 instead of `RequestTeardown(Clean)`; a second quit request while Draining → T-6; a quit while Loading → T-6.
- [ ] **T-4 broadcast**: `ZoneSessionEnded(ZoneClosed=0, gracePeriodSeconds=ZONE_DRAIN_GRACE_SECONDS)` to every `Connected` client, none to ghosts; the set of told clients is recorded for Story 012 step 4.
- [ ] **Draining rejection**: a new `SessionHandshake` while Draining gets no slot (`ZoneFullResponse(ZoneDoesNotExist)` via Story 009's step 4).
- [ ] **Close → teardown**: the lifecycle's close notification (T-3 excluded, which is S-fail) calls `RequestTeardown` with the matching exit code, so the existing teardown runs the close steps once and the process exits.
- [ ] **AC-ZI-22**: 2 Connected + 1 ghost; quit → Draining, `ZoneSessionEnded(grace)` to both Connected only, a new handshake gets no slot; all 3 released before grace → T-5 on the last release's tick. *(Story 013 readiness 2026-10-23: the ghost's release by a reconnect while Draining is deferred with the gateway re-routing work; this test releases the ghost by TTL expiry — `EvaluateSessionTTL` at its expiry tick — and the 2 Connected by clean logout.)*
- [ ] **AC-ZI-23 (timing)**: Draining with 1 Connected + 1 ghost (250 s TTL left); after `ZONE_DRAIN_GRACE_SECONDS × TICK_RATE_HZ` ticks → T-5 at that tick, Story 012's steps run.
- [ ] **AC-ZI-24**: second quit while Draining → Closed on that tick (T-6); every occupied slot saved; no second `ZoneSessionEnded`.
- [ ] **AC-ZI-25**: Active with no slot; quit → Closed on that tick; no `ZoneSessionEnded`.
- [ ] **Exhaustion**: Story 002's T-6 request → `RequestTeardown(Fatal)`.
- [ ] **Deployment constraint** recorded in the story's completion notes: `ZONE_DRAIN_GRACE_SECONDS` + teardown persistence drain < host stop timeout (systemd `TimeoutStopSec`, default 90 s).

---

## Implementation Notes

- **From Story 001's code review (2026-10-22):** (a) `ZoneLifecycle` evaluates the load timeout (T-3) only in `OnTick`, and the tick loop is not running during startup — the composition must call `OnTick` from the startup frame polling while Loading, or T-3 by timeout never fires. (b) The `observer` constructor parameter exists only under `UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`; a call site that passes it must sit inside the same `#if`, or the release server build fails — check a non-development server build. (c) `IZoneLifecycleListener` implementations must not throw: catch and log inside the composition's listener.
- **From Story 011 (2026-10-23):** (a) construct `ZoneEntryReclamation` and pass it to `ZoneEntryAdmission` (optional `reclamation` parameter), call `ZoneEntryReclamation.Register(dispatcher, …)` before the dispatcher is sealed, and call `ZoneEntryReclamation.OnTick` once per tick in the simulation step before the close steps (EC-ZI-15); (b) its close seam is `IInboundTransportDisconnector.DisconnectClient(clientId)` — the zone client id must equal the transport client id, or pass an adapter that maps them; (c) the session sink is `LoggingZoneEntryReclaimSessionSink` until TD-077 is paid; TD-078 (two in-flight loads for one character) is due before this wiring ships.
- **From Story 012 readiness (2026-10-23):** (a) construct `ZoneCloseSteps` in place of `StandInZoneCloseSteps` (constructor order as built: pool (`ZoneSlotPool` holds players and mobs), directory, tick roster, `ZoneEntryReclamation`, persistence, the tick completion queue, party, `IZoneMobSpawnTimers`, `IZoneTeardownSessionSink`, `IZoneDrainNoticeLog`, the outbound sink, the `ZoneID`); (b) implement `IZoneDrainNoticeLog.WasNotifiedAtDrain(clientId)` from the T-4 `ZoneSessionEnded` broadcast — record every client sent one at T-4 so the close steps skip it; (c) `NotifyZoneExit` for saved characters runs from save callbacks inside `DrainOnShutdown` (teardown step 6), so the drain must stay after the close steps; connections are closed only by teardown step 7; (d) the session sink stand-in is TD-079. (e) From Story 012's `/code-review`: teardown step 4 runs when `_zoneStateEntered` is set but step 6 (drain) only when `_tickStarted` is set — on the T-3 path (invalid bounds at S6) a save tracked by the close steps would never be drained and its `NotifyZoneExit` lost; make the drain run whenever the close steps ran (low risk today: no players exist while Loading).
- **From Story 013 readiness (2026-10-23):** construct `SessionEndCoordinator` (Character Persistence Story 010) with `removeEntityFromZone: _ => { }` and `broadcastPlayerLeftZone:` Story 013's `ReleasePlayer(uint characterId, DisconnectType)` — `LeaveZone` already removes the entity and sends the one `PlayerLeftZone`; call `EvaluateSessionTTL(accountId, tick, expiryTick)` each tick for every `Disconnected_SessionActive` account; route the clean-logout request to `BeginCleanLogout`. Nothing in `src/` builds or calls the coordinator today. From Story 013's `/code-review`: the coordinator's `<example>` passes `zone.RemoveEntity`, which does not exist in `src/`; if a per-entity registry (stats, combat, relevance) needs a removal hook by then, call it from the leave path, not from `removeEntityFromZone`, so the one-`PlayerLeftZone` rule holds. `ReleasePlayer`'s save-first ordering holds only on the coordinator path — do not pass it to a direct `ProcessExplicitDisconnect` call elsewhere.
- `ZoneServerComposition.cs` is ~1,300 lines; keep the change to construction, the quit hook and the close-notification → `RequestTeardown` link.
- The Story 045 local verification run must still reach Active and exit cleanly on quit (now after the drain; with no players, T-4 and T-5 fire together).

---

## Out of Scope

- Gateway routing disable / operator drain command (gateway epic)
- Real Party / Mob Spawning / navigation / token validation

---

## QA Test Cases

- **AC-ZI-22 / 23 / 24 / 25** — through the composition with fakes and the observer, ticks advanced programmatically.
- **Stand-in log** — a default composition logs each remaining stand-in once.
- **Verification** — Story 045's local run: start → Active → quit → Closed → exit code `Clean`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_Composition_tests.cs` — must exist and pass; the Story 045 local verification rerun noted in the story.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001–013
- Unlocks: None (epic complete apart from the gateway-blocked requirements)
