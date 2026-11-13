# Story 010: Composition Wiring

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 6 hours (frame-rate build check added 2026-11-13; composition-built navigation and probe flag, readiness 2026-11-13)

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-2, CR-NAV-3, CR-NAV-12, CR-NAV-14, EC-NAV-7, Interactions table
**Requirement**: `TR-nav-001`, `TR-nav-005`, `TR-nav-008` (frame-rate build check only, moved from Story 009 on 2026-11-13)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002 (Decisions 1, 3); ADR-014: Inbound Request Dispatch and Tick Order; ADR-009 Amendment 1
**ADR Decision Summary**: The simulation step runs ADR-002 phases 1–4; phase-3 systems run in the order the composition lists. Navigation phases 1–2 come before every phase-3 system. Teardown halts the tick before navigation teardown (step 3).

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (wiring; agent behaviour verified in Story 009 PlayMode; the server-build frame-rate check lands here)
**Engine Notes**: ADR-002 5a: verify `Application.targetFrameRate = TICK_RATE_HZ` actually drives `NavMeshAgent` simulation in a `UNITY_SERVER` build; fallback `Time.fixedDeltaTime` (moved from Story 009, 2026-11-13).

**Control Manifest Rules (this layer)**:
- Required: the tick order is code in one class; one failing step does not stop the tick
- Required: the composition root creates server-only components in code
- Required: zone teardown nav sequence — tick halted, then `Teardown()`, then NavMesh removed
- Forbidden: lambda subscriptions on persistent events

---

## Acceptance Criteria

- [x] **The composition builds the real navigation** (user decision, readiness 2026-11-13): when `CreateWithRealBackend` receives no `IZoneNavigation` (`null`), the composition creates the `MobEventBus`, the Story 009 adapters (`UnityNavAgentFactory`, `UnityNavMeshQuery`, `UnityNavMeshInstanceOps`), `NavigationConfig` (in-code defaults via `ScriptableObject.CreateInstance`), `StandInNavMobSpeedSource`, the `ZoneNavigationService` and the `NavigationPhaseStep`, before S6. Test hooks: `internal Func<INavAgentFactory> NavAgentFactoryOverride` and `internal NavigationConfig NavigationConfigOverride`. `ZoneServerHost` passes `null` instead of `new StandInZoneNavigation()`. A caller that passes an `IZoneNavigation` (the existing tests with `StandInZoneNavigation`) keeps today's behaviour: no bus, no navigation step.
- [x] The composition's `MobEventBus` is one per zone, created before S6, and exposed (`internal MobEventBus MobEvents`) for the Enemy AI and Mob Spawning epics; the service subscribes in `Initialize`.
- [x] A navigation phase step (Story 005 entry point) joins `_simulationSteps` before Status Effects, Movement, Combat and Skill (phase 1–2 before phase 3); `IMobCommittedPositionSource` is an empty stand-in until Enemy AI.
- [x] S6: a valid instance initialises navigation (pool of 150 created); an invalid instance makes `Initialize` throw → S6 fails → teardown runs step 3 (`Teardown`) because `MarkNavigationInitialized` was set first.
- [x] Teardown step 3 calls the real `Teardown` after the tick is halted, then `UnityNavAgentFactory.DestroyPoolRoots()` (the service only disables the agents; the composition keeps the concrete factory, and with a test factory override there is nothing to destroy); `NavMeshDataInstance` is removed once (no stand-in "removal belongs to the Navigation epic" path left in `UnitySceneLoader` comments).
- [x] The composed service is reachable as `INavigationProvider` for the Enemy AI epic (a property or the composition's existing service registry).
- [x] Wiring test by type (the Auto-Attack Story 009 / Skill Story 011 precedent) and step-order test; the full EditMode suite still passes; the Story 008 smoke (zone start + login) still passes.
- [x] **Frame-rate pin in a server build** (TR-nav-008, moved from Story 009 2026-11-13): in a Windows Dedicated Server build with the real adapters wired, record whether agent simulation follows `Application.targetFrameRate = TICK_RATE_HZ`. Measure the cadence, not the distance (agent movement is delta-time scaled, so ≈ 3.5 m per second at any frame rate): frames per real second, and the agent's `nextPosition` step per frame (≈ speed / 20 = 0.175 m at 20 Hz). If the cadence is not ≈ 20 Hz, apply the ADR-002 5a fallback and record which. Result in Completion Notes; an ADR-002 note if it contradicts the ADR.
- [x] **Probe flag** (user decision, readiness 2026-11-13): `-navFrameRateProbe`, honoured only when `Debug.isDebugBuild` (ignored with one warning otherwise). Once the zone is Active it raises one `OnMobSpawned` on the composed bus at (0, 0, 0) (probe entity id outside the live id range, documented), calls `SetDestination` to (40, 0, 0), and for 10 s logs once per real second: frames in that second, mean `nextPosition` step per frame, total advance. It then raises `OnMobDied` for the probe entity and logs "[NavProbe] done". EditMode test: flag absent → nothing spawned; flag present in a non-debug build → warning, nothing spawned (debug check behind a seam).

---

## Implementation Notes

- **Host**: `src/ServerLogic/ZoneServer/ZoneServerHost.cs:66` builds `new StandInZoneNavigation()`; it passes `null` now and the composition builds the real navigation. Keep `StandInZoneNavigation` for tests that pass it (`ZoneServer_CompositionTeardown_tests.cs:870`, `ZoneServer_CompositionWiring_tests.cs`, `GatewayLink_ZoneWiring_tests.cs`). `NavigationPhaseStep` takes the concrete `ZoneNavigationService`, which is why the composition builds it.
- **Pool root**: the factory creates the root on the first `Create`, inside `Initialize` at S6, after the Single-mode scene load and after `AddNavMeshData` (`ZoneServerComposition.cs` ~1172: `AddNavMeshData` → `MarkNavigationInitialized` → `Initialize`), so the root needs no `DontDestroyOnLoad` and the Story 009 park snapping finds the mesh. The service's `Teardown` does not destroy it: teardown step 3 calls `DestroyPoolRoots()` (AC above).
- **Step position**: the link pump is added last (Gateway Story 011: `ZoneServerComposition.cs` ~1862, after the Skill cast step, so it sees every session-count change of the tick). The order today is Zone Instancing wiring, Status Effects, Movement, Combat, Skill cast, link pump (~1949–2128). Insert the navigation step right after the Zone Instancing wiring step and before Status Effects; add a comment citing ADR-002 Decision 1 and CR-AI-2 (Status Effects → Enemy AI → Auto-Attack follows in the Enemy AI epic).
- **Missing NavMesh**: with the real service, a zone whose definition has no baked NavMesh now fails S6 (`Initialize` throws on the invalid instance). Only StartingZone exists today and it has the Story 008 bake; note it in the S6 doc comment.
- **Probe**: put it in `src/ServerLogic/Navigation/Unity/NavFrameRateProbe.cs` (a plain class the host drives from `Update`, holding the composition's bus and provider); parse the flag with the existing server argument parsing (`ZoneServerArgs` or equivalent). Run: `IronGrindServer.exe -batchmode -nographics -zoneTemplate StartingZone -navFrameRateProbe` on a Development Windows Dedicated Server build.
- **Observe**: call `Observe(nameof(ZoneNavigationService))` like the other builds.
- **Existing tests** that count steps or assert `Observe` order will need updating (Skill Story 011 precedent: three wiring tests updated).
- **Frame-rate check**: no mob spawns yet (Mob Spawning epic), so the probe flag above claims one agent. A Linux build is blocked (Authentication Story 019): use the Windows Dedicated Server build and mind `project_server_build_side_effects` (revert the dedicated-server package / settings changes after the build).
- **Bus lifetime**: Zone Instancing destroys the bus when the zone closes (CR-MS-5); at minimum unsubscribe navigation in `Teardown` (Story 003) and drop the reference at composition dispose.

---

## Out of Scope

- Enemy AI epic: the committed-position source, speed source (`IMobDefinitionRegistry`), the phase-3 Enemy AI step
- Mob Spawning epic: raising `OnMobSpawned`, calling the spawn-point validator

---

## QA Test Cases

- **Host type**: with `null` navigation, the composed zone's navigation is a `ZoneNavigationService` with Unity adapters (or the `NavAgentFactoryOverride` factory); the speed source is `StandInNavMobSpeedSource`. With a passed `StandInZoneNavigation`, no bus and no navigation step are built.
- **Pool root destroyed**: teardown step 3 calls `Teardown()` then `DestroyPoolRoots()` (recorder; real factory in a PlayMode or Editor check, or a counting subclass seam).
- **Probe flag**: absent → nothing spawned; present in a non-debug build → one warning, nothing spawned.
- **Step order**: the composed `_simulationSteps` lists the navigation step before `StatusEffectsService`, Movement, Combat and the Skill cast step.
- **Bus**: one `MobEventBus` per composition; raising `OnMobSpawned` after S6 reaches the service (fake agent factory through the override hook → a claim).
- **Valid start**: S6 with a valid fake instance → pool of 150 (fake factory count); startup continues.
- **Invalid start**: S6 with an invalid instance → S6 returns false or the startup fails as other S6 failures do; teardown calls `Teardown` once; no throw escapes teardown.
- **Teardown order**: tick halted before `Teardown` (recorder across the composition's teardown steps).
- **INavigationProvider exposed**: the composition returns the same instance as `INavigationProvider`.
- **Regression**: full EditMode suite passes; smoke from Story 008 passes.
- **Manual check — frame-rate pin**: Setup: Development Windows Dedicated Server build, run with `-navFrameRateProbe`. Verify: the `[NavProbe]` per-second lines. Pass: ≈ 20 frames per second (18–22) and a mean `nextPosition` step ≈ 0.175 m per frame; or the fallback applied, re-measured and recorded.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Navigation/Navigation_Composition_tests.cs` — must exist and pass; the server-build frame-rate result recorded in Completion Notes

**Status**: [x] Created — `tests/EditMode/Navigation/Navigation_Composition_tests.cs` (21 tests, pass); server-build evidence `production/qa/smoke-2026-11-13-navigation-010.md`

---

## Dependencies

- Depends on: Stories 006, 008, 009
- Unlocks: Enemy AI epic (consumes `INavigationProvider` and the bus); Mob Spawning epic

---

## Completion Notes

**Completed**: 2026-11-13
**Criteria**: 9/9 passing (AC7 smoke and AC8 frame-rate pin verified manually on a Development Windows Dedicated Server build)
**Frame-rate pin result (TR-nav-008, ADR-002 5a)**: `Application.targetFrameRate = TICK_RATE_HZ` drives `NavMeshAgent` simulation in the server build. Two runs of `-navFrameRateProbe`: 20/20 one-second intervals with `frames=20 movingFrames=20`, mean `nextPosition` step 0.1750–0.1754 m (expected 3.5 / 20 = 0.175), ≈ 3.5 m advance per second; `[ZoneTickProbe]` 20 ticks/s, one tick per frame. **No fallback applied**; consistent with ADR-002, so no ADR note.
**Server build checks**: S6 initialised the real service with the Story 008 bake; teardown step 3 (real `Teardown` + `DestroyPoolRoots`) done in 2 ms on the idle close, quit(0); Story 008 smoke (zone start + `zetest`/`Juanperez` login to zone entry) PASS; no `[StandIn] no zone NavMesh data`, `[Movement] NavMesh unavailable` or `Exception` lines.
**Deviations** (advisory, accepted):
- Third test hook `NavMeshInstanceOpsOverride` (EditMode fake scenes return an invalid instance; needed for the valid-start test).
- Internal accessors beyond the story: composition `NavigationService`, `UnityNavFactory`, `ZoneId`, `IsTickRunning`; service `TryGetAgentNextPosition`, `TeardownCallCount`, `IsTornDown`, `SpeedSource` (test / probe only).
- `NavFrameRateProbe` takes the composition (the bus and service exist only after S5); probe output extended at code review to `movingFrames`, min/max step and a 0.5 s warm-up, because mean step alone (= speed / fps) cannot show the agent simulation cadence.
**Out-of-list files**: `IZoneNavigation.cs`, `ZoneServerStandIns.cs` (doc fixes), `tests/EditMode/Navigation/NavigationTestFakes.cs` (`ThrowOnRemove`, default off), `production/qa/smoke-2026-11-13-navigation-010.md`. Launch script `Builds/start-zone-probe.ps1` is gitignored.
**Test Evidence**: `tests/EditMode/Navigation/Navigation_Composition_tests.cs` (21 tests); EditMode 7354 / 7225 passed / 0 failed / 129 skipped; manual evidence `production/qa/smoke-2026-11-13-navigation-010.md`
**Code Review**: Complete — unity-specialist (1 required: probe cadence) + qa-tester (GAPS, 4 required); all fixed and re-run. Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
**Tech debt**: none.
