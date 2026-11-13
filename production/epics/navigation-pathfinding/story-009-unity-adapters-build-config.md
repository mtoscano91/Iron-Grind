# Story 009: Unity Adapters and Server Build Configuration

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-1, CR-NAV-4, CR-NAV-11, CR-NAV-13; AC-NAV-03 (main thread)
**Requirement**: `TR-nav-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decision 5); ADR-003; ADR-012
**ADR Decision Summary**: The server build pins `Application.targetFrameRate` to the tick rate so `NavMeshAgent` simulation runs at 20 Hz, and `link.xml` preserves `UnityEngine.AI` so IL2CPP does not strip it. ADR-002 5a says to verify the pin actually drives agent simulation in a `UNITY_SERVER` build, with `Time.fixedDeltaTime` as the fallback.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (the three ADR-002 / ADR-003 open verifications land here)
**Engine Notes**: verify on Unity 6.3: (1) `NavMeshAgent` keeps path state across `isStopped` toggles (ADR-003); (2) `isStopped` zeroes `desiredVelocity` (Story 004 note); (3) `nextPosition` during `pathPending` (EC-NAV-9); (4) frame-rate pin vs agent update in a server build — moved to Story 010 (readiness 2026-11-13).

**Control Manifest Rules (this layer)**:
- Required: `link.xml` preserves `UnityEngine.AI` (exact XML in the manifest)
- Required: `Application.targetFrameRate = TICK_RATE_HZ` at process start
- Required: server-only code in `IronGrind.ServerLogic`

---

## Acceptance Criteria

- [ ] `UnityNavAgent : INavAgent` wraps a `NavMeshAgent` 1:1; `UnityNavAgentFactory : INavAgentFactory` creates a `GameObject` with a `NavMeshAgent` under the pool root (no `Instantiate` of a prefab needed); `UnityNavMeshQuery : INavMeshQuery` wraps `NavMesh.SamplePosition`. Files in `src/ServerLogic/Navigation/Unity/`.
- [ ] `UnityNavAgentFactory.Create` never enables an agent off the mesh: it creates the `GameObject` inactive, adds the `NavMeshAgent`, sets `enabled = false`, then activates the `GameObject` (`AddComponent` on an active object runs `OnEnable` at once, before `FillPool` sets `Enabled = false`). The PlayMode test fills the pool with the factory parked off the mesh and checks no "Failed to create agent" log (`LogAssert.NoUnexpectedReceived`) (readiness 2026-11-13).
- [ ] `UnityNavMeshInstanceOps : INavMeshInstanceOps` (Story 002 seam): `IsValid` returns `instance.valid`, `Remove` calls `instance.Remove()`. The PlayMode test checks a real instance from the Story 008 bake reads valid before `Teardown` and invalid after it (Story 002 carry, 2026-11-12).
- [ ] **PlayMode test with the Story 008 mesh**: create the service with the real adapters, claim an agent, `SetDestination` across the floor, drain, step frames → within 5 ticks `pathPending` clears, `GetCurrentVelocity` points toward the destination on XZ with magnitude ≈ speed; `Stop` → velocity zero; `Resume` → velocity restored without a new `SetDestination` (ADR-003 Decision 1 on the real agent).
- [ ] The same test checks: `Warp` off the mesh returns false; `SamplePosition` hits on the floor and misses outside it; every service call runs on the main thread (AC-NAV-03): the test wraps the real adapters in test-only recording decorators (`INavAgent`, `INavAgentFactory`, `INavMeshQuery`, `INavMeshInstanceOps`) that store `Thread.CurrentThread.ManagedThreadId` on every call, and asserts every stored id equals the test's thread id.
- [ ] `Assets/link.xml` gains `<assembly fullname="UnityEngine.AIModule"><namespace fullname="UnityEngine.AI" preserve="all"/></assembly>` (ADR-002 5b) and `<assembly fullname="UnityEngine.CoreModule"><namespace fullname="UnityEngine.SceneManagement" preserve="all"/></assembly>` (ADR-009 step 6, manifest; missing until now, readiness 2026-11-13). EditMode text test checks both entries.
- [ ] **Frame-rate pin (code half only)**: confirm startup S8 sets `Application.targetFrameRate = TICK_RATE_HZ` (`ZoneServerComposition` passes `ServerTickLoop.TICK_RATE_HZ` to `IZoneNetwork.Configure`; `NgoZoneNetwork.Configure` assigns it) and record the call sites in Completion Notes. The build half (does agent simulation follow the pin in a Windows Dedicated Server build, else ADR-002 5a fallback) moves to Story 010, where the real adapters run in the server (readiness 2026-11-13, user decision: no throwaway verification host).
- [ ] Results of the four engine verifications are recorded in Completion Notes and, where they contradict the ADR, raised as an ADR note.

---

## Implementation Notes

- **Story 001 carries (2026-11-12)**: `UnityNavMeshQuery.SamplePosition` sets `hitPosition = default` when the call returns false; `UnityNavAgent.DisableObstacleAvoidance()` sets `obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance` (write-only seam member — EditMode fakes count the call).
- **Story 003 carry (2026-11-12)**: the claim enables a pooled agent before `Warp` (ADR-003 order). A real `NavMeshAgent` enabled while its transform is off the mesh logs "Failed to create agent because it is not close enough to the NavMesh" before `Warp` places it. Either have `UnityNavAgentFactory` park pooled agents at a known on-mesh point, or document the log as expected; the PlayMode claim test asserts which. `isStopped` / `ResetPath` on a disabled or off-mesh agent log an error, so the release order (stop, reset, then disable) must stay, and the failed-Warp path never calls them.
- **Story 006 carry (code review 2026-11-12)**: verify on a real agent with `updatePosition = false`: (1) `Warp` clears the path (`hasPath` false, `pathStatus` after Warp) and what it does to `isStopped` and `nextPosition` — Story 006 assumes the path is cleared and resets "destination requested" after a recovery Warp; (2) `pathPending` clears on the next tick after `SetDestination`; (3) `SetDestination` to an off-mesh point returns false and leaves `pathStatus` unchanged (the EC-NAV-8 stuck rule counts rejections); (4) `isOnNavMesh` drift while `nextPosition` is applied, and `Warp` back to the last committed position succeeds; (5) `pathStatus` after `ResetPath` and after clearing `isStopped` before `SetDestination`.
- **Story 007 carry (code review 2026-11-12)**: the real `INavMeshQuery` adapter forwards `areaMask` unchanged to `NavMesh.SamplePosition`. Verify against the Story 008 bake: (1) the default `SPAWN_SAMPLE_RADIUS` 0.1 accepts authored spawn points at ground height (spawn y within 0.1 of the mesh); (2) points at the mesh edge and just above / below the mesh; (3) `NavMesh.AllAreas` gives the same result as the explicit walkable mask; (4) with no NavMesh loaded the adapter returns false, not an exception. Bake facts (Story 008): the mesh surface is at y = 0.083 over the y = 0 floor, so a spawn point at floor height is only 0.017 inside the 0.1 radius; edges at ±49.333.
- **Story 008 carry (smoke 2026-11-12, user decision)**: a player moving on the real mesh. With `UnityNavMeshSampler` and the Story 008 asset, a committed position on the floor moves and a move toward x or z = ±50 stops near ±49.333. If this story's PlayMode tests cannot drive `MovementService`, carry it on to the first story that sends client movement intents (the client has only `LoggingMovementIntentSink`).
- PlayMode tests live under `tests/PlayMode/Navigation/` (existing `PlayModeTests.asmdef`; check it references `IronGrind.ServerLogic` — Editor runs define `UNITY_EDITOR`, so ServerLogic compiles).
- Load the baked mesh in the test with `NavMesh.AddNavMeshData(asset)` (load the asset by path through `AssetDatabase` in Editor-only test code, or a `Resources` copy) and remove it in teardown.
- No server build in this story: the frame-rate measurement moved to Story 010 (readiness 2026-11-13).
- The PlayMode test's mesh-loading code uses `AssetDatabase`, so guard it with `#if UNITY_EDITOR` (`PlayModeTests.asmdef` also compiles under `UNITY_SERVER`).

---

## Out of Scope

- Story 010: wiring the adapters into the composition
- Story 011: the 150-agent timing fixture

---

## QA Test Cases

- **Adapter round trip** (PlayMode): claim at (0, 0, 0), destination (10, 0, 0), drain → within 5 ticks `pathPending` false, velocity x > 0, |velocity| within 10% of speed, y = 0.
- **Stop / Resume** (PlayMode): after the path resolves, `Stop` → velocity zero; `Resume` → velocity non-zero again with agent `SetDestination` call count unchanged.
- **Warp off mesh** → false; on mesh → true.
- **SamplePosition**: (0, 0, 0) hit; (100, 0, 100) miss at radius 0.1.
- **Pool fill**: factory parked off the mesh, fill the pool → no "Failed to create agent" log; every created agent has `enabled == false`.
- **Main thread**: every id recorded by the decorators equals the test's main thread id.
- **link.xml**: file contains the `UnityEngine.AIModule` / `UnityEngine.AI` and `UnityEngine.CoreModule` / `UnityEngine.SceneManagement` preserve entries (EditMode text test).
- **Frame-rate pin (code)**: Completion Notes name the S8 call sites; the build measurement is Story 010's.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Navigation/Navigation_UnityAdapter_tests.cs` must exist and pass; the link.xml EditMode text test passes; the S8 frame-rate call sites recorded in Completion Notes

**Status**: [x] Created — PlayMode 19/19, link.xml EditMode 4/4 (batch mode 2026-11-13)

---

## Dependencies

- Depends on: Stories 002, 004, 008
- Unlocks: Stories 010, 011

---

## Completion Notes

**Completed**: 2026-11-13
**Criteria**: 8/8 passing (frame-rate build measurement moved to Story 010 at readiness; this story covers the code half)
**Test Evidence**: `tests/PlayMode/Navigation/Navigation_UnityAdapter_tests.cs` (19 PlayMode tests) and `tests/EditMode/Navigation/Navigation_LinkXml_tests.cs` (4); batch mode PlayMode 19/19, full EditMode 7333 / 7204 passed / 0 failed / 129 skipped.
**Code Review**: Complete (`/code-review` 2026-11-13: CHANGES REQUIRED → all fixed; unity-specialist CLEAN, qa-tester gaps closed). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.

**Frame-rate pin (code half)**: startup S8 `ZoneServerComposition.StartupStartNetwork` (`ZoneServerComposition.cs:1189`) calls `_network.Configure(NgoSharedSettings.TICK_RATE, ServerTickLoop.TICK_RATE_HZ, 0, port)`; `NgoZoneNetwork.Configure` sets `Application.targetFrameRate = targetFrameRate` (20) and `vSyncCount = 0` (`NgoZoneNetwork.cs:111`). Whether agent simulation follows it in a server build: Story 010.

**Engine verifications (Unity 6000.3.10f1, Editor PlayMode, Story 008 mesh)**:
1. ADR-003 Decision 1 holds: the path survives an `isStopped` toggle (`hasPath` true while stopped and after; `desiredVelocity` back to 3.5 m/s; no new `SetDestination`).
2. `isStopped = true` does NOT zero `desiredVelocity` in the same frame (still 3.5), only from the next frame. Contradicted the service's assumption → `GetCurrentVelocity` now returns zero while `IsStopped`; ADR-003 implementation note added.
3. `pathPending` is true in the frame of `SetDestination` and false the next frame; `nextPosition` does not move while pending (EC-NAV-9).
4. Frame-rate pin vs agent update in a server build: moved to Story 010.
- Story 006 carries: `Warp` with `updatePosition = false` returns true, clears the path (`hasPath` false, `pathStatus` PathComplete), leaves `isStopped` unchanged and sets `nextPosition` to the warp point; off-mesh `SetDestination` returns false with `pathStatus` unchanged; `ResetPath` clears the path (`pathStatus` PathComplete); `nextPosition` set past the edge clamps to x 49.33, `isOnNavMesh` stays true, `Warp` back to the origin succeeds.
- Story 007 carries: `SamplePosition` at floor height (y 0) hits at y 0.083 within the 0.1 radius; edge hit at x 49.3, miss at 49.5; `NavMesh.AllAreas` and the explicit Walkable mask agree; with the mesh removed it returns false with `hitPosition` default, no exception.
- Story 003 carry: a real agent enabled off the mesh logs "Failed to create agent because it is not close enough to the NavMesh" (warning), and the following `Warp` still places it. `UnityNavAgentFactory` therefore moves the park position to the nearest NavMesh point (`PARK_SAMPLE_RADIUS` 1000 m) when it creates a pool root (pool fill runs at S6 after `AddNavMeshData`): a (500, 0, 500) preference parks at (49.33, 0.08, 49.33) and claims log nothing. With no mesh in range the given position is kept and each claim logs the warning (tested). Claim → release → re-claim on real agents logs nothing; the re-claimed agent starts stopped.
- `NavMeshDataInstance.Remove()` on an already removed instance: no exception, nothing logged.

**Deviations (advisory, approved in session)**:
- Outside the file list: `ZoneNavigationService.GetCurrentVelocity` stopped branch + `Navigation_Provider_tests` (+2, one updated); `ZoneInstancing_ZoneAssets_tests` asserted no NavMesh and had failed since Story 008 — now asserts the Story 008 asset; ADR-003 implementation note; Story 010 took the frame-rate build check (readiness).
- Factory park snapping added (Story 003 carry). `PARK_SAMPLE_RADIUS` is an adapter constant, not a gameplay value.
- `Assets/link.xml` also gained `UnityEngine.CoreModule` / `UnityEngine.SceneManagement` (ADR-009 step 6, previously missing).

**Carried forward**: Story 008 carry (a player moving on the real mesh and stopping near ±49.333 through `MovementService`) needs client movement intents; carried to the first story that sends them (not yet on the roadmap). Story 010 must destroy the pool root at `Teardown` (`UnityNavAgentFactory.DestroyPoolRoots()` exists).
