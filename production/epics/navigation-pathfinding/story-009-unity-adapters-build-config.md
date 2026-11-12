# Story 009: Unity Adapters and Server Build Configuration

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
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
**Engine Notes**: verify on Unity 6.3: (1) `NavMeshAgent` keeps path state across `isStopped` toggles (ADR-003); (2) `isStopped` zeroes `desiredVelocity` (Story 004 note); (3) `nextPosition` during `pathPending` (EC-NAV-9); (4) frame-rate pin vs agent update in a server build.

**Control Manifest Rules (this layer)**:
- Required: `link.xml` preserves `UnityEngine.AI` (exact XML in the manifest)
- Required: `Application.targetFrameRate = TICK_RATE_HZ` at process start
- Required: server-only code in `IronGrind.ServerLogic`

---

## Acceptance Criteria

- [ ] `UnityNavAgent : INavAgent` wraps a `NavMeshAgent` 1:1; `UnityNavAgentFactory : INavAgentFactory` creates a `GameObject` with a `NavMeshAgent` under the pool root (no `Instantiate` of a prefab needed); `UnityNavMeshQuery : INavMeshQuery` wraps `NavMesh.SamplePosition`. Files in `src/ServerLogic/Navigation/Unity/`.
- [ ] `UnityNavMeshInstanceOps : INavMeshInstanceOps` (Story 002 seam): `IsValid` returns `instance.valid`, `Remove` calls `instance.Remove()`. The PlayMode test checks a real instance from the Story 008 bake reads valid before `Teardown` and invalid after it (Story 002 carry, 2026-11-12).
- [ ] **PlayMode test with the Story 008 mesh**: create the service with the real adapters, claim an agent, `SetDestination` across the floor, drain, step frames → within a few ticks `pathPending` clears, `GetCurrentVelocity` points toward the destination on XZ with magnitude ≈ speed; `Stop` → velocity zero; `Resume` → velocity restored without a new `SetDestination` (ADR-003 Decision 1 on the real agent).
- [ ] The same test checks: `Warp` off the mesh returns false; `SamplePosition` hits on the floor and misses outside it; every service call runs on the main thread (AC-NAV-03: record `ManagedThreadId`).
- [ ] `Assets/link.xml` gains `<assembly fullname="UnityEngine.AIModule"><namespace fullname="UnityEngine.AI" preserve="all"/></assembly>`.
- [ ] **Frame-rate pin**: confirm startup S8 (`IZoneNetwork` / `NgoZoneNetwork`) sets `Application.targetFrameRate = TICK_RATE_HZ`; record in the story whether agent simulation follows it in a Windows Dedicated Server build (agent position advance per real second at a known speed, or Profiler). If it does not, apply the ADR-002 5a fallback and record which.
- [ ] Results of the four engine verifications are recorded in Completion Notes and, where they contradict the ADR, raised as an ADR note.

---

## Implementation Notes

- **Story 001 carries (2026-11-12)**: `UnityNavMeshQuery.SamplePosition` sets `hitPosition = default` when the call returns false; `UnityNavAgent.DisableObstacleAvoidance()` sets `obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance` (write-only seam member — EditMode fakes count the call).
- **Story 003 carry (2026-11-12)**: the claim enables a pooled agent before `Warp` (ADR-003 order). A real `NavMeshAgent` enabled while its transform is off the mesh logs "Failed to create agent because it is not close enough to the NavMesh" before `Warp` places it. Either have `UnityNavAgentFactory` park pooled agents at a known on-mesh point, or document the log as expected; the PlayMode claim test asserts which. `isStopped` / `ResetPath` on a disabled or off-mesh agent log an error, so the release order (stop, reset, then disable) must stay, and the failed-Warp path never calls them.
- **Story 006 carry (code review 2026-11-12)**: verify on a real agent with `updatePosition = false`: (1) `Warp` clears the path (`hasPath` false, `pathStatus` after Warp) and what it does to `isStopped` and `nextPosition` — Story 006 assumes the path is cleared and resets "destination requested" after a recovery Warp; (2) `pathPending` clears on the next tick after `SetDestination`; (3) `SetDestination` to an off-mesh point returns false and leaves `pathStatus` unchanged (the EC-NAV-8 stuck rule counts rejections); (4) `isOnNavMesh` drift while `nextPosition` is applied, and `Warp` back to the last committed position succeeds; (5) `pathStatus` after `ResetPath` and after clearing `isStopped` before `SetDestination`.
- **Story 007 carry (code review 2026-11-12)**: the real `INavMeshQuery` adapter forwards `areaMask` unchanged to `NavMesh.SamplePosition`. Verify against the Story 008 bake: (1) the default `SPAWN_SAMPLE_RADIUS` 0.1 accepts authored spawn points at ground height (spawn y within 0.1 of the mesh); (2) points at the mesh edge and just above / below the mesh; (3) `NavMesh.AllAreas` gives the same result as the explicit walkable mask; (4) with no NavMesh loaded the adapter returns false, not an exception.
- PlayMode tests live under `tests/PlayMode/Navigation/` (existing `PlayModeTests.asmdef`; check it references `IronGrind.ServerLogic` — Editor runs define `UNITY_EDITOR`, so ServerLogic compiles).
- Load the baked mesh in the test with `NavMesh.AddNavMeshData(asset)` (load the asset by path through `AssetDatabase` in Editor-only test code, or a `Resources` copy) and remove it in teardown.
- A Linux server build is blocked (Authentication Story 019); use the Windows Dedicated Server build for the frame-rate check, and mind `project_server_build_side_effects` (revert the dedicated-server package / settings changes after the build).

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
- **Main thread**: every recorded thread id equals the test's main thread id.
- **link.xml**: file contains the `UnityEngine.AIModule` / `UnityEngine.AI` preserve entry (EditMode text test).
- **Manual check — frame-rate pin**: Setup: Windows Dedicated Server build with one claimed agent moving at a known speed. Verify: Profiler or log of agent updates per second. Pass: ≈ 20 updates per second, or the fallback applied and recorded.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Navigation/Navigation_UnityAdapter_tests.cs` must exist and pass; the frame-rate check recorded in Completion Notes

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 004, 008
- Unlocks: Stories 010, 011
