# Epic: Navigation / Pathfinding

> **Layer**: Core
> **GDD**: design/gdd/navigation-pathfinding.md
> **Architecture Module**: Navigation (`architecture.md` module table, ADR-002/003): `ZoneNavigationService`, `src/ServerLogic/`
> **Status**: Complete
> **Stories**: 11 created 2026-11-12 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

Navigation / Pathfinding is the server-side service that steers mobs through a zone's walkable geometry on the 20 Hz tick. It owns two jobs: the zone NavMesh lifecycle (the scene loader adds the zone's pre-baked `NavMeshData`, ADR-009 A1; the service takes the `NavMeshDataInstance` at `Initialize` and removes it at `Teardown`) and per-mob path management through `INavigationProvider`, the only interface Enemy AI uses (`SetDestination`, `SetSpeed`, `Stop`, `Resume`, `IsPathStale`, `GetCurrentVelocity`). Each live mob holds one `NavMeshAgent` from a pool of 150 pre-allocated agents, claimed on `MobSpawned` and released on `MobDied`. Agents never move themselves (`updatePosition = false`): the tick loop syncs the committed mob positions in (phase 1), drains at most `MAX_PATH_UPDATES_PER_TICK` path requests (phase 2), Enemy AI reads `desiredVelocity` (phase 3), and the server integrates positions (phase 4). No runtime baking; one zone per server process.

## Scope (user decisions 2026-11-12, `/create-epics`)

**Server side.** `ZoneNavigationService` replaces `StandInZoneNavigation` behind the existing `IZoneNavigation` seam (`src/ServerLogic/ZoneServer/IZoneNavigation.cs`) and implements `INavigationProvider`. The epic also bakes the starting zone's NavMesh and wires it to `ZoneDefinition.BakedNavMesh` (no zone has one today), and adds the server build configuration from ADR-002 Decision 5.

Moved out:

| Item | Owner |
|------|-------|
| Mob steering decisions (when to call `SetDestination`, `Stop`, `Resume`, the Returning speed cap) | Enemy AI epic |
| Raising `MobSpawned` and `ZoneSpawnTable` authoring | Mob Spawning epic |
| Raising `MobDied` (Enemy AI bridges Combat's `OnMobDied` to `MobEventBus.MobDied`, decision 1) | Enemy AI epic |
| Player position validation (`NavMesh.SamplePosition`) | Movement System (Complete) |

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-002: NavMesh Service Execution Contract | Phase order sync → drain → read → integrate; async `SetDestination` (no same-tick path); teardown after the tick halts plus a `_tornDown` guard; one zone per process; frame-rate pin and `link.xml` preserve for `UnityEngine.AI` | HIGH |
| ADR-003: Nav Agent Lifecycle State Machine | `Resume(EntityID)`; Returning speed cap through `SetSpeed`; the service owns `NavMeshAgent.enabled`; `_latestRequests` drain guard; pool exhaustion logs, never throws; full lifecycle table | HIGH |
| ADR-009: Scene / Zone-Load Management (Amendment 1) | The scene loader adds the definition's baked NavMesh (`IZoneScene.AddNavMeshData`); navigation receives the instance at S6 and removes it at teardown | HIGH (consumer of existing code) |
| ADR-014: Inbound Request Dispatch and Tick Order | Navigation phases 1–2 sit inside the simulation step (ADR-002 phases 1–4), ahead of the phase 3 systems | LOW |
| ADR-012: Server/Client Assembly Boundary | Lives in `IronGrind.ServerLogic` from its first story | LOW |
| ADR-010: Event and Messaging Architecture | Subscribes to the per-zone `MobEventBus` (`MobSpawned`, `MobDied`); `INavigationProvider` is a grandfathered interface name | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-12). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-nav-001 | `IZoneNavigation` lifecycle: `Initialize` takes the scene's `NavMeshDataInstance`, an invalid instance aborts zone initialisation; `Teardown` order (agents, dictionaries, `RemoveNavMeshData`); `_tornDown` guard; main thread only; one zone per process (CR-NAV-1 to 3, EC-NAV-4, EC-NAV-6, AC-NAV-01 to 03) | ADR-002, ADR-009 ✅ |
| TR-nav-002 | Pool of `MAX_AGENT_POOL_SIZE` (150) agents filled at `Initialize` with `updatePosition`, `updateRotation`, `autoBraking` false, no obstacle avoidance, disabled; no `Instantiate` / `Destroy` during gameplay (CR-NAV-4, AC-NAV-04) | ADR-003 ✅ |
| TR-nav-003 | Claim on `MobSpawned` (Warp, speed from `IMobDefinitionRegistry`, stopped) and release on `MobDied` (ResetPath, disable, return); double claim and pool exhaustion log an error without throwing (CR-NAV-5, CR-NAV-6, EC-NAV-2, EC-NAV-3, AC-NAV-05 to 07, 22) | ADR-003, ADR-010 ✅ |
| TR-nav-004 | `INavigationProvider`: `SetDestination` queues (latest wins), `SetSpeed` clamps to [0.1, 20], `Stop` keeps the path and drops the queued request, `Resume`, O(1) `IsPathStale`, `GetCurrentVelocity` returns `desiredVelocity` on XZ (CR-NAV-7 to 11, CR-NAV-16, EC-NAV-1, AC-NAV-08 to 13, 24) | ADR-003 ✅ |
| TR-nav-005 | `SyncAgentPositions` (phase 1, `nextPosition`) and `Tick` drain of at most `MAX_PATH_UPDATES_PER_TICK` (phase 2) in ADR-002 order; no same-tick path assumed (CR-NAV-12, CR-NAV-14, EC-NAV-7, EC-NAV-9, F-NAV-1, AC-NAV-14 to 16, 18) | ADR-002, ADR-014 ✅ |
| TR-nav-006 | Partial-path timeout `PARTIAL_PATH_TIMEOUT_TICKS`, off-mesh recovery `Warp` after `NAV_OFF_MESH_RECOVERY_TICKS`, stuck re-route warning after `NAV_STUCK_REROUTE_DETECTION_TICKS` (CR-NAV-15, F-NAV-2, EC-NAV-8, EC-NAV-10, AC-NAV-17, 19, 23) | ADR-002, ADR-003 ✅ |
| TR-nav-007 | Spawn-point check at zone load: off-mesh points excluded and logged; zone initialisation aborts below `MIN_VALID_SPAWN_FRACTION` (CR-NAV-13, EC-NAV-5, AC-NAV-21) | ADR-002 ✅ |
| TR-nav-008 | Server build configuration: `Assets/link.xml` preserves `UnityEngine.AI` (missing today); confirm the S8 frame-rate pin drives `NavMeshAgent` in a `UNITY_SERVER` build, else apply the ADR-002 5a fallback | ADR-002, ADR-012 ✅ |
| TR-nav-009 | Starting-zone NavMesh baked in the Editor and set as `ZoneDefinition.BakedNavMesh`; 150-agent fixture for AC-NAV-20 (F-NAV-3, OQ-NAV-2) | ADR-002, ADR-009 ✅ |

## Decisions (2026-11-12, `/create-epics`)

Taken for all four combat-loop epics (Navigation, Enemy AI, Mob Spawning, Death & Respawn); only this epic is written so far.

1. **Mob death: Enemy AI bridges.** Combat's `ICombatService.OnMobDied` stays the only kill event (ADR-010, manifest). Enemy AI subscribes, enters `Dead`, calls `ResolveMobDrop(tierShift)`, then raises `MobEventBus.MobDied`. Navigation and Mob Spawning listen on the bus, as their GDDs say. A mob that reaches HP ≤ 0 with no killer (CR-AI-13) runs the same Dead-entry routine, which ignores a second call. ADR-010 gets a dated note with the Enemy AI epic.
2. **Server plus wire.** Client visuals, UI and audio go to Combat UI / HUD / VFX / Audio; party hooks are a seam owned by the Party System epic.
3. **Content split by system.** Navigation bakes the starting-zone NavMesh (TR-nav-009); Enemy AI authors the MVP mob roster; Mob Spawning authors the starting-zone spawn table (no boss points, OQ-MS-1).

## Carried into stories

- **`MobEventBus` type**: Navigation is its first consumer. CR-MS-5 has Zone Instancing create one per zone at T-1 and inject it. Decide at `/create-stories` where the type lives and which story adds it.
- **TR-nav-007 needs the `ZoneSpawnTable`** (Mob Spawning epic). Read it through a seam until that epic lands; the zone has no spawn table today.
- **`INavigationPositionSync`**: CR-NAV-12 leaves extracting the sync call as an interface to the lead programmer.
- **Tuning knobs** (`MAX_PATH_UPDATES_PER_TICK`, `PARTIAL_PATH_TIMEOUT_TICKS`, `RETURN_SPEED_CAP`, `MIN_VALID_SPAWN_FRACTION`, the two diagnostic tick counts, sample radius) go in a config asset, not constants (coding standards).
- **Tests and `NavMeshAgent`**: most ACs assert agent calls (`SetDestination`, `ResetPath`, `Warp`), so stories need an agent seam for EditMode tests; AC-NAV-20 needs a real baked mesh (TR-nav-009).
- **GDD drift**: CR-NAV-2 says the service calls `NavMesh.AddNavMeshData`; the scene loader does (ADR-009 A1), and `IZoneNavigation.Initialize` takes `(ZoneID, NavMeshDataInstance)`. Fix the GDD text at `/create-stories`.
- **ADR-002 Decision 3**: the zone close path already halts the tick before teardown step 3; confirm at readiness that `Teardown` runs after the last tick.

## Decisions (2026-11-12, `/create-stories`)

4. **`MobEventBus` is built here** (Story 001), in `src/ServerLogic/ZoneInstancing/`, with `OnMobSpawned` / `OnMobDied` and `readonly struct` args in `IronGrind.Events` (manifest event rules); the composition creates one per zone (Story 010).
5. **Seams for missing systems**: `INavMobSpeedSource` (stand-in until Enemy AI's `IMobDefinitionRegistry`), `IMobCommittedPositionSource` (empty until Enemy AI integrates mob positions), `INavAgent` / `INavAgentFactory` / `INavMeshQuery` so Logic stories run in EditMode; Unity adapters and engine checks in Story 009 (PlayMode).
6. **Invalid NavMesh fails S6** (`Initialize` throws). The bake (Story 008) lands before the composition swap (Story 010), so the live zone keeps starting.
7. **Spawn-point validator is standalone** (Story 007); the Mob Spawning epic calls it at zone T-1. **Hand-off (Story 007 readiness, 2026-11-12)**: Mob Spawning builds `SpawnPointInput` entries from `ZoneSpawnTable` (`SpawnPointID: uint`, authored position), calls `SpawnPointValidator.Validate` after Navigation's `Initialize`, spawns only from `validOut`, and aborts zone initialisation (with Zone Instancing) when `ShouldAbort` is true — the validator only computes the flag. Copy this into the Mob Spawning epic when it is created.
8. **Placeholder floor**: the starting zone scene is empty; Story 008 adds a flat floor over the ±50 m walkable bounds and bakes it with `NavMeshSurface` (AI Navigation 2.0.10).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Navigation seams, config and `MobEventBus`](story-001-seams-config-mob-event-bus.md) | Logic | Complete | ADR-003, ADR-010 |
| 002 | [Service lifecycle and agent pool](story-002-service-lifecycle-agent-pool.md) | Logic | Complete | ADR-002, ADR-003, ADR-009 |
| 003 | [Agent claim and release on the bus](story-003-agent-claim-release.md) | Logic | Complete | ADR-003 |
| 004 | [`INavigationProvider` calls](story-004-navigation-provider-calls.md) | Logic | Complete | ADR-003, ADR-002 |
| 005 | [`SyncAgentPositions` and the `Tick` drain](story-005-sync-and-tick-drain.md) | Logic | Complete | ADR-002, ADR-003 |
| 006 | [Path health](story-006-path-health.md) | Logic | Complete | ADR-002, ADR-003 |
| 007 | [Spawn-point validator](story-007-spawn-point-validator.md) | Logic | Complete | ADR-002 |
| 008 | [Starting-zone floor and NavMesh bake](story-008-starting-zone-navmesh-bake.md) | Config/Data | Complete | ADR-009, ADR-002 |
| 009 | [Unity adapters and server build config](story-009-unity-adapters-build-config.md) | Integration | Complete | ADR-002, ADR-012 |
| 010 | [Composition wiring](story-010-composition-wiring.md) | Integration | Complete | ADR-002, ADR-014 |
| 011 | [150-agent drain fixture (AC-NAV-20)](story-011-drain-fixture-150-agents.md) | Integration | Complete | ADR-002 |

Order: 001 → 002 → 003 → 004 → 005 → 006; 007 after 001; 008 any time (early); 009 after 002, 004, 008; 010 after 006, 008, 009; 011 after 008, 009.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/navigation-pathfinding.md` are verified
- All Logic and Integration stories have passing test files in `tests/`
- All Visual/Feel and UI stories have evidence docs with sign-off in `production/qa/evidence/`

## Next Step

Run `/story-readiness production/epics/navigation-pathfinding/story-001-seams-config-mob-event-bus.md`, then `/dev-story`. Story 008 (bake) can go first.
