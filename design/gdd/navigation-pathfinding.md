# Navigation / Pathfinding

> **Status**: Approved (reviewed 2026-06-14; see design/gdd/reviews/navigation-pathfinding-review-log.md)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-14
> **Implements Pillar**: Pillar 2 — Rhythm Mastery (mob spatial behavior must not disrupt combat timing); Pillar 3 — Social Gravity (mob density and spacing affect group dynamics)
> **Architecture**: ADR-002 (Execution & Concurrency Contract), ADR-003 (Agent Lifecycle State Machine)

## Overview

The Navigation/Pathfinding system is a server-side infrastructure service that provides mob spatial guidance for the 20 Hz authoritative tick loop. It owns two responsibilities: (1) per-zone NavMesh lifecycle management — loading pre-baked `NavMeshData` assets at zone initialization and releasing them at teardown — and (2) per-agent path management, implementing the `INavigationProvider` interface that Enemy AI consumes to steer mob entities through walkable zone geometry. Each active mob entity has a corresponding `NavMeshAgent` hosted on a lightweight server-side `GameObject`; the agent's `desiredVelocity` is read by the tick loop to derive movement direction, while actual position integration remains the server's responsibility (Enemy AI + server tick own the position write). The system does not validate player positions (Movement System owns that via `NavMesh.SamplePosition`) and does not own zone geometry or bounds (Zone Instancing CR-ZI-10). All NavMesh baking occurs in the Unity Editor prior to build; no runtime baking is performed on the server (iOS performance constraint, Movement System CR-MOV-7).

## Player Fantasy

Mobs should feel *purposeful and committed* — not glitchy, rubber-banding, or frozen. When a mob pursues a player, it takes a reasonable path around obstacles; when a mob gets stuck, it should never visibly freeze for more than one second before self-correcting. When a mob returns to its spawn, it walks back with the same deliberate cadence a guard would use, not sprinting as if teleporting. The player should never be able to attribute a mob's navigation behavior to a technical artifact. This system's Player Fantasy is delivered entirely through Enemy AI's feel — its own quality bar is: **the player never sees a mob frozen, rubber-banding, or teleporting.** (See `enemy-ai.md` Player Fantasy for mob behavior expectations.)

## Detailed Rules

### Core Rules

**CR-NAV-1: Execution Domain**
Navigation/Pathfinding executes within `ServerLogic.asmdef` (`defineConstraints: ["UNITY_SERVER"]`). All `INavigationProvider` calls execute synchronously on the Unity main thread as part of the 20 Hz server tick loop (Networking Core CR-NET-2; `TICK_RATE_HZ = 20`). Unity's NavMesh APIs are main-thread only; no background threads, `Task.ConfigureAwait(false)`, or concurrent data structures are used. One `ZoneNavigationService` instance exists per server process at MVP — one zone per server process (see OQ-NAV-1).

---

**CR-NAV-2: NavMesh Asset Lifecycle — Load**
`ZoneNavigationService.Initialize(ZoneID zoneId, NavMeshData data)` is called by Zone Instancing during the zone `Loading` phase, before the zone transitions to `Active` and before any `MobEventBus.MobSpawned` events are raised.

1. Call `NavMesh.AddNavMeshData(data)` and store the returned `NavMeshDataInstance` as `_navMeshInstance`.
2. If `_navMeshInstance.valid == false`: log error with `ZoneID`; report load failure to Zone Instancing; zone initialization is aborted. No mobs spawn in a zone without a valid NavMesh.
3. After confirming `_navMeshInstance.valid`, run the spawn-point assertion (CR-NAV-13).

Runtime baking is prohibited. All `NavMeshData` assets are baked in the Unity Editor prior to build (iOS performance constraint; Movement System CR-MOV-7). Asset naming convention: `Assets/NavMesh/{ZoneTemplateID}.asset` — must be included in the server build.

---

**CR-NAV-3: NavMesh Asset Lifecycle — Unload**
Zone Instancing calls `ZoneNavigationService.Teardown()` as its first action on zone `Closed` state entry, before per-mob despawn processing.

Teardown sequence:
1. Disable all agents in `_activeAgents` and return them to the pool.
2. Clear `_activeAgents` dictionary.
3. Clear `_pendingRequests` queue and `_latestRequests` dictionary.
4. If `_navMeshInstance.valid`: call `NavMesh.RemoveNavMeshData(_navMeshInstance)`.
5. Set `_navMeshInstance = default`.

`Teardown` is not part of `INavigationProvider` — it is a lifecycle contract between `ZoneNavigationService` and Zone Instancing.

---

**CR-NAV-4: Agent Pool Initialization**
`ZoneNavigationService` maintains a fixed pool of `MAX_MOBS_PER_ZONE` (150) pre-allocated `NavMeshAgent`-bearing `GameObjects`. The pool is filled during `Initialize`, after NavMesh load is confirmed. Each pooled agent is configured with:
- `NavMeshAgent.updatePosition = false` — server tick loop owns position integration
- `NavMeshAgent.updateRotation = false` — Enemy AI owns `EntityState.FacingAngle`
- `NavMeshAgent.autoBraking = false` — prevents `desiredVelocity` from dropping near the destination, which would misrepresent mob speed to the tick loop
- `NavMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance` — mob clustering is an intentional danger signal for group play (Pillar 3 — Social Gravity); CPU cost at 150 agents makes avoidance unjustifiable
- `NavMeshAgent.enabled = false` — inactive until claimed

Pooled agents are parented to a root `GameObject` named `"NavigationPool_{ZoneID}"` in the server scene. No `Instantiate` or `Destroy` calls occur during zone gameplay.

---

**CR-NAV-5: Agent Lifecycle — Claim on MobSpawned**
`ZoneNavigationService` subscribes to `MobEventBus.MobSpawned(ZoneID, EntityID, MobTypeID, Vector3 spawnPosition)`.

On receipt:
1. If pool is empty: log error with `ZoneID` and `EntityID`; do not register the agent. This mob receives zero-velocity navigation responses; Enemy AI's `isOnNavMesh` guard (CR-AI-11) suppresses calls. Should not occur if `MAX_MOBS_PER_ZONE` is respected by Mob Spawning.
2. Claim one agent: `_agentPool.Dequeue()`.
3. Enable `NavMeshAgent` component.
4. Call `NavMeshAgent.Warp(spawnPosition)`. If `Warp` returns false (spawn point not on walkable NavMesh): disable agent, return to pool, log error with `ZoneID`, `EntityID`, `spawnPosition`. This indicates a bake defect that CR-NAV-13's startup assertion should have caught.
5. Set initial speed: `NavMeshAgent.speed = IMobDefinitionRegistry.GetDefinition(mobTypeId).MoveSpeed`.
6. Keep `NavMeshAgent.isStopped = true` — inactive until Enemy AI first calls `SetDestination`.
7. Register: `_activeAgents[entityID] = agent`.

---

**CR-NAV-6: Agent Lifecycle — Release on MobDied**
`ZoneNavigationService` subscribes to `MobEventBus.MobDied(ZoneID, EntityID)`.

On receipt:
1. Look up agent in `_activeAgents`. If not found: log warning, return.
2. Set `NavMeshAgent.isStopped = true`.
3. Call `NavMeshAgent.ResetPath()` — clears path state for safe pool reuse.
4. Disable `NavMeshAgent`.
5. Return agent to `_agentPool`.
6. Remove from `_activeAgents` and `_partialPathTickCount`.

Agent release occurs at `MobDied` time because Enemy AI issues no `INavigationProvider` calls during `Dead` state.

---

**CR-NAV-7: SetDestination(EntityID agent, Vector3 target)**
Called by Enemy AI in `Pursuing` when target moves more than `PURSUIT_REPOSITION_THRESHOLD` (1.0m) from the last issued destination, or when `IsPathStale` returns true. Also called on `Returning` entry with the spawn point as target.

`SetDestination` does not call `NavMeshAgent.SetDestination` directly — it enqueues the request for staggered processing by `Tick()` (CR-NAV-14):
1. If `agent` not in `_activeAgents`: log warning, return.
2. Overwrite latest: `_latestRequests[agent] = target`.
3. If `agent` not already queued: enqueue `(agent, target)` into `_pendingRequests`.

Actual path computation deferred to the next `Tick()` drain. **Path computation is asynchronous** — `NavMeshAgent.SetDestination` queues an async request on Unity's pathfinding worker thread; `pathPending == true` until the result arrives, which may span multiple frames (see ADR-002 Decision 2). During the pathPending window: `desiredVelocity` returns the prior tick's value (or `Vector3.zero` if no prior path exists); `IsPathStale` returns false (suppresses re-triggering while pending). There is no single-frame guarantee.

---

**CR-NAV-8: SetSpeed(EntityID agent, float speed)**
Called once at mob initialization with `MobDefinition.MoveSpeed`. Not called at runtime at MVP.

1. If `agent` not in `_activeAgents`: log warning, return.
2. Clamp `speed` to `[0.1f, 20.0f]`; log warning if clamping occurs (indicates a `MobDefinition` authoring defect).
3. Set `NavMeshAgent.speed = speed`.

---

**CR-NAV-9: Stop(EntityID agent)**
Called by Enemy AI exactly once on `WindingUp` entry (CR-AI-11).

1. If `agent` not in `_activeAgents`: log warning, return.
2. Set `NavMeshAgent.isStopped = true`.
3. Do NOT call `NavMeshAgent.ResetPath()` — path data is preserved so `Pursuing` re-entry can call `Resume(entityId)` to immediately restore movement without re-queuing a path request.
4. Remove `entityId` from `_latestRequests` (see EC-NAV-1 — prevents drain from reversing the stop after a concurrent SetDestination).

After `Stop`: `GetCurrentVelocity` returns `Vector3.zero`. `IsPathStale` returns the last path status (unchanged by stopping). Enemy AI re-entry pattern: call `Resume(entityId)` (CR-NAV-16) on Pursuing re-entry; if `IsPathStale == true` after Resume, follow up with `SetDestination` (ADR-003 Decision 1).

---

**CR-NAV-10: IsPathStale(EntityID agent) → bool**
Polled by Enemy AI every tick in `Pursuing` state. Must be O(1) — no path computation is triggered.

Returns **true** (stale — Enemy AI will re-issue `SetDestination`) if ANY of the following hold:
- `agent` not in `_activeAgents` (log warning)
- `!NavMeshAgent.isOnNavMesh`
- `NavMeshAgent.pathStatus == PathInvalid`
- `NavMeshAgent.isPathStale` (NavMesh geometry change flagged by Unity)
- `SetDestination` has never been called for this agent
- `_partialPathTickCount[agent] ≥ PARTIAL_PATH_TIMEOUT_TICKS` (CR-NAV-15)

Returns **false** (valid — no re-trigger) if:
- `NavMeshAgent.pathPending == true` (path computation in progress; suppress re-trigger during 1-tick lag)
- `NavMeshAgent.pathStatus == PathComplete`
- `NavMeshAgent.pathStatus == PathPartial` AND `_partialPathTickCount[agent] < PARTIAL_PATH_TIMEOUT_TICKS`

---

**CR-NAV-11: GetCurrentVelocity(EntityID agent) → Vector3**
Called by Enemy AI every tick to derive mob movement direction for position integration.

1. If `agent` not in `_activeAgents`: return `Vector3.zero`; log warning.
2. Return `NavMeshAgent.desiredVelocity`.

`desiredVelocity` (not `NavMeshAgent.velocity`) is returned because `velocity` is smoothed for 60Hz+ update loops; at 20 Hz it lags 1–2 ticks behind directional intent. `desiredVelocity` is the NavMesh's raw steering output — the correct input for the server tick loop's position integration formula.

**Y-axis handling:** The caller must use XZ components only. `desiredVelocity.y` reflects NavMesh surface gradient and must not be fed into horizontal position integration.

| Agent state | desiredVelocity |
|-------------|----------------|
| Stopped (`isStopped = true`) | `Vector3.zero` |
| Path pending (1-tick lag) | Last computed or `Vector3.zero` if no prior path |
| PathComplete, navigating | Toward next waypoint; magnitude ≈ `NavMeshAgent.speed` |
| PathPartial, navigating toward closest point | Toward partial path end; magnitude ≈ `NavMeshAgent.speed` |
| PathPartial, arrived at partial path end | `Vector3.zero` |
| PathInvalid | `Vector3.zero` |

---

**CR-NAV-12: Position Synchronization (SyncAgentPositions)**
With `NavMeshAgent.updatePosition = false`, the agent's internal position does not follow the server's authoritative mob position. Without correction, all path computations originate from the agent's last `Warp` position — `desiredVelocity` always points toward the first waypoint regardless of actual mob location.

The server tick loop calls `ZoneNavigationService.SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3> committedPositions)` at the **start** of each tick, before `Tick()` is called and before any `GetCurrentVelocity` reads (canonical order per ADR-002 Decision 1):

```csharp
foreach (var (id, pos) in committedPositions)
    if (_activeAgents.TryGetValue(id, out var agent) && agent.isOnNavMesh)
        agent.nextPosition = pos;
```

`SyncAgentPositions` is not part of `INavigationProvider`. The server tick loop holds a concrete reference to `ZoneNavigationService` for this call alongside the `INavigationProvider` reference Enemy AI uses. Both are injected by Zone Instancing at zone initialization. **Lead Programmer decision before implementation:** whether to extract as a separate `INavigationPositionSync` interface for testing with stubs.

---

**CR-NAV-13: Spawn Point Validation at Zone Load**
After NavMesh load confirmed (CR-NAV-2), assert every spawn point position in the zone's `ZoneSpawnTable` is on the walkable NavMesh:

```
validCount = 0
totalCount = ZoneSpawnTable.Count
for each SpawnPoint in ZoneSpawnTable:
    NavMesh.SamplePosition(spawnPoint.Position, out hit, radius: 0.1f, NavMesh.AllAreas)
    if no hit: log error(ZoneID, SpawnPointID, Position); exclude from active spawn table
    else: validCount++

validFraction = validCount / totalCount
if validFraction < MIN_VALID_SPAWN_FRACTION:
    log critical(ZoneID, validCount, totalCount)
    abort zone initialization — zone cannot serve
```

Excluded spawn points are never passed to Mob Spawning, so no `MobSpawned` event is raised for them. This is a zone authoring constraint, not a runtime recovery path. If fewer than `MIN_VALID_SPAWN_FRACTION` of the declared spawn points survive validation, the zone is considered unserviceable and zone initialization is aborted entirely — a partially-spawnable zone would produce a critically underloaded encounter space that the server cannot detect at runtime.

---

**CR-NAV-14: Path Request Staggering (Tick)**
`ZoneNavigationService.Tick()` is called by the server tick loop once per tick, **after** `SyncAgentPositions` and **before** Enemy AI reads `GetCurrentVelocity`. Drains up to `MAX_PATH_UPDATES_PER_TICK` requests from `_pendingRequests`:

```
for up to MAX_PATH_UPDATES_PER_TICK dequeued entries (entityId, dest):
    if entityId not in _activeAgents: skip
    if !_activeAgents[entityId].isOnNavMesh: skip
    if entityId not in _latestRequests: skip  // cancelled by Stop() or MobDied — drain the queue slot
    agent = _activeAgents[entityId]
    agent.isStopped = false
    agent.SetDestination(_latestRequests[entityId])  // always use latest, not queued destination
    remove entityId from _latestRequests
```

At `MAX_PATH_UPDATES_PER_TICK = 20`, a 150-agent burst drains in ≤ 8 ticks (400ms). Mobs on valid paths continue following them during the drain period. See Tuning Knobs for safe range.

---

**CR-NAV-15: Partial Path Timeout Tracking**
`_partialPathTickCount[EntityID]` is maintained per active agent:
- **Incremented by 1** each tick that `NavMeshAgent.pathStatus == PathPartial` AND `isStopped == false`.
- **Reset to 0** when: `pathStatus` changes to `PathComplete`; or a fresh `SetDestination` is processed for this agent in `Tick()`.
- **Stale trigger:** When count reaches `PARTIAL_PATH_TIMEOUT_TICKS` (default 20 ticks = 1.0s): `IsPathStale` returns true; Enemy AI re-issues `SetDestination` with the current target position; counter resets when `SetDestination` is processed.

Counter is removed from `_partialPathTickCount` on agent release (CR-NAV-6).

---

**CR-NAV-16: Resume(EntityID agent)**
Called by Enemy AI on Pursuing re-entry after a WindingUp or Recovering cycle (ADR-003 Decision 1). Clears `isStopped` so the preserved path immediately resumes driving `desiredVelocity`, without consuming a drain slot.

1. If `agent` not in `_activeAgents`: log warning, return.
2. Set `NavMeshAgent.isStopped = false`.
3. Do NOT call `NavMeshAgent.SetDestination`. Do NOT modify `_latestRequests` or `_pendingRequests`. The preserved path (from before `Stop()`) immediately resumes producing valid `desiredVelocity`.

**Enemy AI usage contract (ADR-003 Decision 1):**
- On Pursuing re-entry after WindingUp/Recovering: call `Resume(entityId)` first, then check `IsPathStale()`. If stale (target moved, partial-path timeout, etc.), follow up immediately with `SetDestination(entityId, currentTargetPos)`.
- Do NOT call `Resume()` on Dormant → Pursuing re-aggro. Dormant mobs require a new `SetDestination` — no prior pursuit path is valid.

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Zone Instancing** | ← calls | `Initialize(ZoneID, NavMeshData)` / `Teardown()` | Zone loading / zone `Closed` entry |
| **Enemy AI** | → implements | `INavigationProvider` (CR-AI-11): `SetDestination`, `SetSpeed`, `Stop`, `Resume`, `IsPathStale`, `GetCurrentVelocity` | Every tick in `Pursuing`; `WindingUp` entry (`Stop`); `Pursuing` re-entry after WindingUp (`Resume`); `Returning` entry (`SetSpeed` + `SetDestination`) |
| **Mob Spawning (MobEventBus)** | ← subscribes | `MobEventBus.MobSpawned` / `MobEventBus.MobDied` | Mob lifecycle events |
| **Server Tick Loop** | ← calls | `Tick()` (before Enemy AI reads velocity); `SyncAgentPositions()` (after position integration) | Every tick |
| **Movement System** | shares asset | Pre-baked `NavMeshData` loaded by Navigation at zone init; `NavMesh.SamplePosition` used independently by Movement for player validation | Zone load |

## Formulas

### Scope Declaration

This section covers operational and timing relationships internal to the Navigation/Pathfinding system. The following formula categories are explicitly **out of scope** for this document:

- **Position integration** (integrating velocity into world position each tick): owned by the server tick loop (consumes `GetCurrentVelocity` XZ output).
- **Movement speed** (base speed, modifiers): owned by Character Stats and Movement System GDDs.
- **Combat formulas** (damage, pursuit range thresholds, aggro radius): owned by the Enemy AI and Damage Calculation GDDs.

---

### F-NAV-1: Path Request Drain Time

The F-NAV-1 formula is defined as:

```
DrainTicks(Q, R)   = ceil(Q / R)
DrainSeconds(Q, R) = ceil(Q / R) / TICK_RATE_HZ
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Pending requests | Q | int | [1, 150] | Total pending path requests in the queue at the moment of evaluation |
| Max updates per tick | R | int | [1, 150] | `MAX_PATH_UPDATES_PER_TICK` — path updates processed per tick (tuning knob, default 20) |
| Tick rate | TICK_RATE_HZ | int | 20 (fixed) | Server ticks per second (Networking Core CR-NET-2) |
| Drain ticks | DrainTicks | int | [1, ceil(M/R)] | Ticks until all queued requests are serviced |
| Drain seconds | DrainSeconds | float | [0.05, DrainTicks/20] | Wall-clock seconds until queue is empty |

**Output Range:** At default R = 20 and Q = 150: DrainTicks = 8 ticks, DrainSeconds = 0.40s. Hard ceiling = `ceil(MAX_MOBS_PER_ZONE / R) / TICK_RATE_HZ`. R must be ≥ 1; setting R = 0 is invalid.

**Example:**
- Q = 150 (all mobs queued), R = 20, TICK_RATE_HZ = 20 → `ceil(150 / 20) = 8 ticks = 0.40s`
- Q = 50, R = 20 → `ceil(50 / 20) = 3 ticks = 0.15s`

---

### F-NAV-2: Partial Path Staleness Trigger

The F-NAV-2 formula is defined as:

```
RerouteTick(issuedTick, T) = issuedTick + T
TimeoutSeconds(T)          = T / TICK_RATE_HZ
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Partial path issue tick | issuedTick | int | [0, ∞) | Server tick when partial path was last issued |
| Timeout ticks | T | int | [1, ∞) | `PARTIAL_PATH_TIMEOUT_TICKS` — ticks until re-route triggered (tuning knob, default 60) |
| Tick rate | TICK_RATE_HZ | int | 20 (fixed) | Server ticks per second |
| Re-route tick | RerouteTick | int | [issuedTick + 1, ∞) | Server tick at which `IsPathStale` returns true and Enemy AI re-issues `SetDestination` |
| Timeout seconds | TimeoutSeconds | float | [0.05, ∞) | Player-visible maximum "stuck" duration |

**Output Range:** At default T = 20: TimeoutSeconds = **1.0s** — chosen to match the ≤1s freeze tolerance stated in Player Fantasy. At T = 60 (prior default, superseded): TimeoutSeconds = 3.0s. T = 0 is invalid (immediate re-route loops).

**Example:**
- T = 20 (default), TICK_RATE_HZ = 20 → `20 / 20 = 1.0s` max stuck window
- T = 40 → `40 / 20 = 2.0s`
- T = 60 (prior default, for reference) → `60 / 20 = 3.0s`
- issuedTick = 1000, T = 20 → re-route fires at tick 1020

---

### F-NAV-3: Maximum Worst-Case Mob Pursuit Lag

The F-NAV-3 formula is defined as:

```
MaxPursuitLagTicks(M, R)   = ceil(M / R)
MaxPursuitLagSeconds(M, R) = ceil(M / R) / TICK_RATE_HZ
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Mob count | M | int | 150 (fixed at MAX_MOBS_PER_ZONE) | Maximum concurrent mob entities (Networking Core F-NET-9) |
| Max updates per tick | R | int | [1, 150] | `MAX_PATH_UPDATES_PER_TICK` (tuning knob, default 20) |
| Tick rate | TICK_RATE_HZ | int | 20 (fixed) | Server ticks per second |
| Max lag ticks | MaxPursuitLagTicks | int | [1, M] | Ticks the last-serviced mob travels on a stale heading in a worst-case burst |
| Max lag seconds | MaxPursuitLagSeconds | float | [0.05, 7.5] | Wall-clock pursuit lag for the last-serviced mob |

**Output Range:** At default R = 20: **8 ticks = 0.40s** — below player-perceptible threshold on mobile at 60fps. Minimum when R ≥ M: 1 tick / 0.05s. Maximum when R = 1: 150 ticks / 7.5s (unacceptable; R = 5 → 1.5s is the performance floor alert threshold).

**Example:**
- M = 150, R = 20 (default) → `ceil(150 / 20) = 8 ticks = 0.40s`
- R = 10 → `ceil(150 / 10) = 15 ticks = 0.75s` (approaching perceptible)
- R = 5 → `ceil(150 / 5) = 30 ticks = 1.50s` (performance floor alert — visible stutter)

**Amendment (EC-NAV-9):** `MaxPursuitLagTicks` covers queue-drain lag only. Once `SetDestination` is called, `pathPending` adds 1 tick in nominal operation and up to 2 ticks if `SyncAgentPositions` triggers a Unity-internal path restart (EC-NAV-9). Total worst-case pursuit lag: `ceil(M / R) + 2` ticks. At default R = 20: **10 ticks = 0.50s**.

## Edge Cases

**EC-NAV-1: Stop() does not flush a pending SetDestination**
If `Stop(entityID)` is called while `entityID` exists in `_latestRequests`, the
next `Tick()` drain sets `isStopped = false` and calls `NavMeshAgent.SetDestination`
— silently reversing the stop. CR-NAV-9 must also remove `entityID` from
`_latestRequests`; the drain's `_activeAgents` skip guard does not protect against
this because the agent is still active.

**EC-NAV-2: MobSpawned fires twice for the same EntityID (pool leak)**
If `MobEventBus.MobSpawned` publishes the same `EntityID` twice, the second call
overwrites `_activeAgents[entityID]` with a newly claimed agent; the first agent
is neither in the pool nor in `_activeAgents` — a permanent pool slot leak.
CR-NAV-5 must guard at entry: if `entityID` is already in `_activeAgents`, log
error and return immediately without claiming from the pool.

**EC-NAV-3: _latestRequests entry leaks after MobDied**
If `MobDied` fires for an entity whose `SetDestination` is queued but not yet
drained, CR-NAV-6 removes from `_activeAgents` and `_partialPathTickCount` but
not from `_latestRequests`. The drain skips the stale entry but never removes it.
CR-NAV-6 must also call `_latestRequests.Remove(entityID)`.

**EC-NAV-4: MobSpawned received after Teardown**
If a queued `MobSpawned` fires after `NavMesh.RemoveNavMeshData` has executed,
`Warp` returns false on a NavMesh-less agent. CR-NAV-5 must guard at entry:
if `!_navMeshInstance.valid`, log error with tag `"post-teardown MobSpawned"`
and return immediately without claiming from the pool.

**EC-NAV-5: Empty ZoneSpawnTable produces no warning**
If `ZoneSpawnTable.Count == 0` when CR-NAV-13 runs, the assertion loop iterates
zero times — no errors, zone loads, but no valid spawn points exist. This output
is identical for an intentional safe zone and an authoring defect. Log warning
when `ZoneSpawnTable.Count == 0`: `"Zone {ZoneID} has an empty spawn table — no
mobs will spawn. Verify this is intentional."` Zone load continues normally.

**EC-NAV-6: Initialize() called while _navMeshInstance is already valid**
A second `Initialize()` call (zone reload bug or routing defect) leaves the prior
`NavMeshDataInstance` un-removed (leaked in the Unity NavMesh system for the
process lifetime) and the pool un-reallocated. CR-NAV-2 must guard at entry:
if `_navMeshInstance.valid`, log error and return immediately. This guard also
serves as the recommended protection for OQ-NAV-1 if multi-zone processes are
ever introduced.

**EC-NAV-7: Tick() / SyncAgentPositions ordering invariant**
If the server tick loop inverts order — calling `Tick()` before
`SyncAgentPositions()` — all path requests drained in that pass originate from
stale positions (maximum error: one tick of movement, ≤ 1.0 m at max speed
20 m/s). The server tick loop MUST execute
`SyncAgentPositions → Tick() → GetCurrentVelocity` in exactly that order every
tick. Violating this ordering causes all path requests drained in that tick to
originate from stale positions.

**EC-NAV-8: Returning mob targeting an off-NavMesh spawn point**
If a mob's respawn point later falls off the NavMesh (marginal bake point),
`SetDestination(spawnPoint)` from the Returning state yields
`PathInvalid → IsPathStale = true → SetDestination` — an infinite re-route loop.
If the same `EntityID` issues `SetDestination` to the same `Vector3` for
`NAV_STUCK_REROUTE_DETECTION_TICKS` (default: 10 = 0.5 s) consecutive ticks
while `pathStatus == PathInvalid`, log error `(ZoneID, EntityID, target)`.
Navigation cannot unilaterally fix the authoring defect; the log trace makes it
diagnosable. *(New constant — added to Tuning Knobs.)*

**EC-NAV-9: nextPosition update during pathPending may extend pending window**
`SyncAgentPositions` sets `agent.nextPosition = committedPos` unconditionally when
`agent.isOnNavMesh` is true. If `pathPending == true` at that moment and
`nextPosition` shifts the agent's effective origin past Unity's internal re-path
threshold, Unity may restart path computation — extending the pathPending window
to **2 ticks (100 ms)** instead of 1. `desiredVelocity` returns the prior path's
value or `Vector3.zero` during the extension; `IsPathStale` suppresses re-trigger
throughout. No fix required (behavior is bounded and self-correcting). See
F-NAV-3 amendment for worst-case lag update.

**EC-NAV-10: Active agent drifts off NavMesh surface**
If `isOnNavMesh` becomes false post-spawn due to floating-point drift in
`nextPosition` updates, both `SyncAgentPositions` (guards on `isOnNavMesh`) and
the `Tick()` drain (skips non-NavMesh agents) stop updating the agent.
`IsPathStale` returns true, Enemy AI re-issues `SetDestination`, drain skips it
— the mob freezes permanently with no self-recovery. If `isOnNavMesh` remains
false for more than `NAV_OFF_MESH_RECOVERY_TICKS` (default: 3 = 150 ms)
consecutive ticks, `ZoneNavigationService` calls
`NavMeshAgent.Warp(lastKnownCommittedPos)` to force re-constraint and logs a
warning `(ZoneID, EntityID)`. `lastKnownCommittedPos` is tracked per-agent in
`SyncAgentPositions` whenever `isOnNavMesh` is true. *(New constant — added to
Tuning Knobs.)*

## Dependencies

### Upstream Dependencies

| System | Interface | Status |
|--------|-----------|--------|
| **Zone Instancing** | Calls `ZoneNavigationService.Initialize(ZoneID, NavMeshData)` during zone `Loading` phase and `Teardown()` on zone `Closed` entry. Passes the pre-baked `NavMeshData` asset and manages the service's lifecycle. | Approved |
| **Mob Spawning (MobEventBus)** | Navigation subscribes to `MobEventBus.MobSpawned` (claim agent, CR-NAV-5) and `MobEventBus.MobDied` (release agent, CR-NAV-6). Bus lifetime managed by Zone Instancing; navigation never holds a direct reference to Mob Spawning. | Approved |
| **Enemy AI (`IMobDefinitionRegistry`)** | Navigation calls `IMobDefinitionRegistry.GetDefinition(MobTypeID)` at spawn to read `MoveSpeed` for `NavMeshAgent.speed` (CR-NAV-5). Interface owned and implemented by Enemy AI. Returns null for unknown IDs; callers must null-check. | Approved |
| **Movement System** | Pre-baked `NavMeshData` assets (one per zone template) are the shared spatial contract. Movement System uses `NavMesh.SamplePosition` for player position validation; Navigation loads the same asset for mob guidance. The asset is never baked at runtime (CR-MOV-7). Asset naming: `Assets/NavMesh/{ZoneTemplateID}.asset`. | Approved |

### Downstream Dependents

| System | Interface | Status |
|--------|-----------|--------|
| **Enemy AI (`INavigationProvider`)** | Navigation implements `INavigationProvider` (stub defined in CR-AI-11): `SetDestination`, `SetSpeed`, `Stop`, `Resume`, `IsPathStale`, `GetCurrentVelocity`. `Resume` added per ADR-003 Decision 1. Enemy AI is the sole consumer of this interface. | Approved |
| **Server Tick Loop (Networking Core)** | Tick loop calls `SyncAgentPositions()` at the start of each tick (after position commit), then `Tick()` (drains path request queue), then Enemy AI reads `GetCurrentVelocity()`. Ordering is a hard invariant (EC-NAV-7). Not part of `INavigationProvider` — server tick loop holds a concrete `ZoneNavigationService` reference. | Approved (Networking Core) |

### Bidirectional Consistency Check

| System | Check | Result |
|--------|-------|--------|
| Zone Instancing | Navigation listed in zone-instancing.md? | ✓ Listed; status shows "Not Started" — update to Approved in propagation pass |
| Enemy AI | Navigation listed in enemy-ai.md upstream deps? | ✓ Listed; INavigationProvider marked "provisional stub" — update to Approved in propagation pass |
| Movement System | Navigation listed in movement-system.md downstream deps? | ✓ Listed as downstream dependent sharing NavMesh assets |
| Mob Spawning | MobEventBus contracts match mob-spawning.md? | ✓ `MobSpawned` and `MobDied` events match CR-MS-5 and mob-spawning.md event schema |

*Propagation note:* After this GDD is approved, update `zone-instancing.md` (dependency status + bidirectional check) and `enemy-ai.md` (INavigationProvider stub → confirmed implementation, dependency status).

## Tuning Knobs

| Constant | Default | Range | Gameplay Effect |
|----------|---------|-------|-----------------|
| `MAX_AGENT_POOL_SIZE` | 150 | [1, 150] | Pre-allocated `NavMeshAgent` pool. Must equal `MAX_MOBS_PER_ZONE` (Networking Core F-NET-9). Increasing beyond 150 requires Networking Core sign-off; reducing below expected zone mob density causes pool exhaustion (EC-NAV-2 guard logs error). |
| `MAX_PATH_UPDATES_PER_TICK` | 20 | [1, 150] | Path requests drained per server tick. Lower = smoother per-tick CPU cost; higher = faster mob reaction to new destinations. At default: worst-case burst of 150 mobs drains in 8 ticks = 0.40s (F-NAV-1). Do not set below 5 (R = 5 → 1.50s lag — visible stutter threshold per F-NAV-3). |
| `PARTIAL_PATH_TIMEOUT_TICKS` | 20 | [10, 200] | Ticks before a `PathPartial` result triggers `IsPathStale = true` and Enemy AI re-issues `SetDestination` (F-NAV-2). Default 20 = 1.0s — chosen to match the player's ≤1s freeze tolerance stated in Player Fantasy. Prior default of 60 (3.0s) was set before the Player Fantasy quality bar was established. Values below 10 risk re-route loops on complex geometry. |
| `RETURN_SPEED_CAP` | 6.0 | [1.0, 10.0] m/s | Maximum speed during Returning state. Enemy AI calls `SetSpeed(entityId, RETURN_SPEED_CAP)` on Returning entry and restores `MobDefinition.MoveSpeed` on exit (ADR-003 Decision 2). Prevents fast mobs (MoveSpeed > 10 m/s) from overshooting spawn point by > 0.5m per tick when `autoBraking = false`. Upper bound: 10 m/s → maximum 0.5m overshoot per tick = RETURN_ARRIVAL_THRESHOLD. |
| `MIN_VALID_SPAWN_FRACTION` | 0.90 | [0.50, 1.00] | Fraction of zone spawn points that must pass NavMesh validation for zone initialization to succeed (CR-NAV-13). If fewer than this fraction survive `NavMesh.SamplePosition`, zone initialization is aborted. Default 0.90 = ≥90% valid. Lower values permit zones with significant authoring defects to serve, risking critically underloaded encounter spaces. |
| `NAV_STUCK_REROUTE_DETECTION_TICKS` | 10 | [3, 60] | Consecutive ticks the same `EntityID` must issue `SetDestination` to the same `Vector3` with `PathInvalid` before a warning is logged (EC-NAV-8). Detects permanent-return-loop defects caused by off-NavMesh spawn points. Logging only — navigation cannot unilaterally resolve the authoring defect. |
| `NAV_OFF_MESH_RECOVERY_TICKS` | 3 | [1, 10] | Consecutive ticks `isOnNavMesh == false` before `ZoneNavigationService` calls `NavMeshAgent.Warp(lastKnownCommittedPos)` to force re-constraint (EC-NAV-10). Default 3 = 150ms — fast enough to be imperceptible to players; low enough to avoid false-positive Warps during legitimate 1–2 tick `isOnNavMesh` gaps. |
| Agent speed | Per `MobDefinition.MoveSpeed` | [0.1, 20.0] m/s | Per-mob speed set from `IMobDefinitionRegistry` at spawn (CR-NAV-8). Clamped by Navigation; actual values authored in `MobDefinition` data assets. |
| Spawn point sample radius | 0.1f | [0.01, 1.0] m | Search radius passed to `NavMesh.SamplePosition` in CR-NAV-13 spawn point assertion. Tighter radius = stricter bake quality enforcement; looser radius = more permissive zone load at cost of marginal spawn point acceptance. |

## Acceptance Criteria

### Lifecycle

**AC-NAV-01: NavMesh loads before mob events fire.**
Test method: Unit. Before any `MobSpawned` event is dispatched, `NavMesh.AddNavMeshData` completes and `_navMeshInstance.valid == true`; if invalid `NavMeshData` is injected, zone initialization is aborted and the `MobSpawned` handler is never invoked.
Verification: Assert `_navMeshInstance.valid == true` before the first `MobSpawned` callback fires; assert zone abort and zero `MobSpawned` handler invocations when an invalid `NavMeshData` is passed.

**AC-NAV-02: Teardown executes in the required sequence.**
Test method: Unit. Calling `ZoneNavigationService.Teardown()` disables all active agents first, then clears `_activeAgents` and `_latestRequests`, then calls `NavMesh.RemoveNavMeshData` — in exactly that order.
Verification: Inject an ordered call recorder; assert recorded call order is [DisableAgents, ClearDictionaries, RemoveNavMeshData] with no interleaving; assert all three calls occur on the main thread.

**AC-NAV-03: All navigation execution occurs on the main thread under UNITY_SERVER.**
Test method: Unit. Every public method of `ZoneNavigationService` executes on Unity's main thread; no NavMeshAgent API calls are dispatched from background threads.
Verification: Inside a `UNITY_SERVER` build, assert `Thread.CurrentThread.ManagedThreadId` matches the main thread ID for `Tick()`, `SetDestination()`, `Stop()`, `SyncAgentPositions()`, and `GetCurrentVelocity()`.

---

### Agent Pool

**AC-NAV-04: Pool pre-allocates exactly 150 agents at zone load; no agents are instantiated during gameplay; a 151st claim logs an error without throwing.**
Test method: Unit. At zone initialization, the pool contains exactly 150 inactive `NavMeshAgent` instances; the instantiation counter does not increment from first `MobSpawned` until teardown begins; dispatching a 151st `MobSpawned` event does not throw an exception.
Verification: Assert `pool.AvailableCount + pool.ActiveCount == 150` after init; assert `pool.InstantiationCount == 0` after 150 `MobSpawned` events; on a 151st `MobSpawned`, assert **no exception is thrown**, assert exactly one `UnityEngine.Debug.LogError` call with message containing "pool exhausted", assert `_activeAgents.Count == 150` (pool did not grow).

**AC-NAV-05: MobSpawned claims one agent, sets speed from registry, and warps to spawn point.**
Test method: Unit. On `MobSpawned(entityId, spawnPoint)`: pool `AvailableCount` decrements by 1; claimed agent's `speed == IMobDefinitionRegistry.GetDefinition(mobTypeId).MoveSpeed` (exact); `Warp(spawnPoint)` is called before any `SetDestination` or `Tick` call.
Verification: Assert pool `AvailableCount` decreases by 1; assert `agent.speed == registry speed` (no arithmetic applied, exact float); assert `Warp` was called with the spawn point before the first `Tick()`.

**AC-NAV-06: MobDied releases agent — ResetPath called, agent returned to pool.**
Test method: Unit. On `MobDied(entityId)`: `ResetPath()` is called on the agent; pool `AvailableCount` increments by 1; `_activeAgents.ContainsKey(entityId) == false`; `_latestRequests.ContainsKey(entityId) == false`.
Verification: Assert `agent.ResetPath` call count == 1; assert pool `AvailableCount` increments by 1; assert neither `_activeAgents` nor `_latestRequests` contain `entityId`.

**AC-NAV-07: Double MobSpawned for the same EntityID claims exactly one agent (log error, no pool leak).**
Test method: Unit. When `MobSpawned` fires twice with the same `EntityID` without an intervening `MobDied`, the second event logs an error and returns immediately; pool `AvailableCount` decrements by exactly 1, not 2.
Verification: Assert `pool.AvailableCount` decrements by 1 (not 2); assert `_activeAgents[entityId]` is reference-equal after both events (same agent); assert an error is logged containing `entityId`; assert `pool.InstantiationCount == 0`. *(FLAG 1 resolved: log error, not throw — per EC-NAV-2.)*

---

### INavigationProvider

**AC-NAV-08: SetDestination enqueues and deduplicates via _latestRequests; does not call NavMeshAgent.SetDestination directly.**
Test method: Unit. Calling `SetDestination(entityId, destA)` then `SetDestination(entityId, destB)` without an intervening `Tick()` leaves `_latestRequests` with one entry for `entityId` holding `destB`; `NavMeshAgent.SetDestination` is never called inside `SetDestination`.
Verification: Assert `_latestRequests` count for `entityId` == 1 and destination == `destB`; assert `NavMeshAgent.SetDestination` call count == 0 during both `SetDestination` calls.

**AC-NAV-09: SetSpeed clamps incoming values to [0.1, 20.0].**
Test method: Unit. `SetSpeed(entityId, 0.0f)` sets `agent.speed = 0.1f`; `SetSpeed(entityId, 25.0f)` sets `agent.speed = 20.0f`; `SetSpeed(entityId, 5.0f)` sets `agent.speed = 5.0f`.
Verification: Assert `agent.speed == 0.1f` for inputs ≤ 0.1f; assert `agent.speed == 20.0f` for inputs ≥ 20.0f; assert `agent.speed == input` for inputs strictly in (0.1, 20.0).

**AC-NAV-10: Stop sets isStopped, does NOT call ResetPath, removes entity from _latestRequests.**
Test method: Unit. Calling `Stop(entityId)` sets `agent.isStopped = true`; `ResetPath` is not called; `_latestRequests.ContainsKey(entityId) == false`; a subsequent `Tick()` does not call `agent.SetDestination` for that entity.
Verification: Assert `agent.isStopped == true`; assert `agent.ResetPath` call count == 0; assert `_latestRequests.ContainsKey(entityId) == false`; assert zero `agent.SetDestination` calls after `Tick()`.

**AC-NAV-11: Stop() while SetDestination is queued does NOT reverse the stop.**
Test method: Unit. `SetDestination(entityId, dest)` enqueued, then `Stop(entityId)` called before `Tick()` — the enqueued request is discarded; after `Tick()`, `agent.isStopped` remains true and `agent.SetDestination` is never called.
Verification: Assert `_latestRequests.ContainsKey(entityId) == false` after `Stop()`; after `Tick()`, assert `agent.isStopped == true`; assert `agent.SetDestination` call count == 0. *(EC-NAV-1 coverage.)*

**AC-NAV-12: IsPathStale returns the correct value for every defined input condition.**
Test method: Unit (parametrized).

| Input condition | Expected return |
|---|---|
| Entity not in `_activeAgents` | `true` |
| `agent.isOnNavMesh == false` | `true` |
| `pathStatus == PathInvalid` | `true` |
| `agent.isPathStale == true` | `true` |
| `SetDestination` never called for entity | `true` |
| `PathPartial` AND `_partialPathTickCount >= PARTIAL_PATH_TIMEOUT_TICKS` | `true` |
| `agent.pathPending == true` | `false` |
| `pathStatus == PathComplete` | `false` |
| `PathPartial` AND `_partialPathTickCount < PARTIAL_PATH_TIMEOUT_TICKS` | `false` |

Verification: Assert each row independently; assert no state bleed between parametrized cases.

**AC-NAV-13: GetCurrentVelocity returns desiredVelocity with Y = 0; never returns agent.velocity.**
Test method: Unit. `GetCurrentVelocity(entityId)` returns `agent.desiredVelocity` with `Y = 0`; with `agent.desiredVelocity = (3f, 5f, -2f)`, returns `(3f, 0f, -2f)`; does not return `agent.velocity`.
Verification: Assert return == `Vector3(desiredVelocity.x, 0f, desiredVelocity.z)`; assert `agent.velocity` is never read inside `GetCurrentVelocity`.

---

### Tick Loop

**AC-NAV-14: Tick() drains at most MAX_PATH_UPDATES_PER_TICK requests per call; excess remain queued.**
Test method: Unit. With 150 requests queued and `MAX_PATH_UPDATES_PER_TICK = 20`: one `Tick()` call invokes `NavMeshAgent.SetDestination` exactly 20 times; 130 requests remain in the queue; after 8 total `Tick()` calls, the queue is empty (150 total `SetDestination` calls).
Verification: Assert `agent.SetDestination` count == 20 after first `Tick()`; assert queue count == 130; assert queue count == 0 after 8th `Tick()`; assert total `SetDestination` calls across all 8 ticks == 150.

**AC-NAV-15: SyncAgentPositions executes before SetDestination calls in each tick (ordering invariant).**
Test method: Unit. Within any tick cycle, `SyncAgentPositions` is called before `NavMeshAgent.SetDestination`; `GetCurrentVelocity` is available to callers after `Tick()` returns and is not called internally by `Tick()`.
Verification: Inject ordered call recorder; assert `SyncAgentPositions` appears before the first `SetDestination` in the recording; assert `GetCurrentVelocity` is not invoked inside `Tick()`.

**AC-NAV-16: SyncAgentPositions sets agent.nextPosition to the server-committed position.**
Test method: Unit. `SyncAgentPositions(entityId, committedPos)` sets `agent.nextPosition = committedPos`; `agent.Warp` is not called; `transform.position` is not directly assigned.
Verification: Assert `agent.nextPosition == committedPos`; assert `agent.Warp` call count == 0; assert no `transform.position` assignment.

**AC-NAV-17: _partialPathTickCount increments per tick on PathPartial; resets on PathComplete or new SetDestination.**
Test method: Unit. After 19 `Tick()` calls with `pathStatus == PathPartial`: `_partialPathTickCount == 19` and `IsPathStale == false`; after tick 20: `IsPathStale == true` (default `PARTIAL_PATH_TIMEOUT_TICKS = 20`); after `pathStatus → PathComplete`: counter resets to 0; after a new `SetDestination`: counter resets to 0.
Verification: Assert `IsPathStale == false` at count 19; assert `IsPathStale == true` at count 20; assert counter == 0 after `PathComplete`; assert counter == 0 after `SetDestination`.

---

### Formulas

**AC-NAV-18: F-NAV-1 — Q=150, R=20 drains in exactly 8 ticks (0.40 s).**
Test method: Unit. Initialize `_latestRequests` with 150 unique entity destinations; call `Tick()` 8 times; assert queue empty after exactly 8 ticks with exactly 150 total `SetDestination` calls.
Verification: Assert `_latestRequests.Count == 0` after 8 ticks; assert total `SetDestination` call count == 150; assert queue non-empty after 7 ticks.

**AC-NAV-19: F-NAV-2 — T=20 (default), partial timeout fires at exactly tick 20 (1.0 s).**
Test method: Unit. Set `_partialPathTickCount = 19` with `pathStatus == PathPartial`; one more `Tick()` triggers `IsPathStale == true`; changing `PARTIAL_PATH_TIMEOUT_TICKS` to 19 causes timeout one tick earlier.
Verification: Assert `IsPathStale == false` at count 19; assert `IsPathStale == true` at count 20; parametrized boundary: T=19 fires at count 19.

**AC-NAV-20: F-NAV-3 — At M=150, R=20, all mob destinations are serviced within 10 ticks (0.50 s worst case).**
Test method: Integration (preferred — requires live NavMesh bake fixture, see OQ-NAV-2). Spawn 150 mobs, issue `SetDestination` for all on tick 0; verify all path requests are processed within 10 ticks accounting for up to 2-tick `pathPending` extension (EC-NAV-9).
Verification: Assert no mob's destination update is delayed beyond 10 ticks from issue time; assert ≥ 140 of 150 mobs processed within 8 ticks; assert zero mobs exceed 10 ticks regardless of `pathPending` state.
Fallback if OQ-NAV-2 fixture not provisioned before sprint start: documented manual playtest — spawn 150 mobs in a test zone, issue mass SetDestination via debug console command, instrument Tick() with per-entity processed timestamps, capture log, QA Lead reviews and signs off. Fallback evidence stored in `production/qa/evidence/nav-ac20-playtest-[date].md`. Gate: ADVISORY (integration test is BLOCKING once fixture is provisioned).

---

### Diagnostics & Edge Cases

**AC-NAV-21: Spawn point assertion excludes non-walkable points at zone load.**
Test method: Unit. During `Initialize`, any spawn point that fails `NavMesh.SamplePosition(pos, 0.1f)` causes an error to be logged and that point to be excluded from the valid spawn set; zone load continues with the remaining valid points.
Verification: Inject a spawn list with one off-mesh point; assert the invalid point's coordinates appear in the error log; assert zone load completes; assert the invalid point is not in the validated spawn set.

**AC-NAV-22: MobDied while SetDestination queued removes the queued request.**
Test method: Unit. `SetDestination(entityId, dest)` enqueued; `MobDied(entityId)` fires before `Tick()`; assert `_latestRequests.ContainsKey(entityId) == false`; after `Tick()`, `agent.SetDestination` is never called for the released agent.
Verification: Assert `_latestRequests.ContainsKey(entityId) == false` after `MobDied`; assert `agent.SetDestination` call count == 0 after `Tick()`; assert agent returned to pool. *(EC-NAV-3 coverage.)*

**AC-NAV-23: Agent off-NavMesh for 3 consecutive ticks triggers Warp to last committed position.**
Test method: Unit. `agent.isOnNavMesh == false` for 3 consecutive `Tick()` calls for the same entity: `Warp` is not called after 2 ticks; `Warp(lastKnownCommittedPos)` is called on the 3rd tick; counter resets if `isOnNavMesh` returns to true between ticks. Counter is per-entity.
Verification: Assert `Warp` call count == 0 after 2 consecutive off-mesh ticks; assert `Warp` call count == 1 on 3rd tick with the exact position from last `SyncAgentPositions`; assert counter resets to 0 when `isOnNavMesh == true` for one tick. *(EC-NAV-10 coverage; Flag 2 resolved: counter is per-entity per SyncAgentPositions tracking.)*

**AC-NAV-24: Resume() clears isStopped without modifying path state or queue.**
Test method: Unit. Calling `Resume(entityId)` after `Stop(entityId)`: sets `agent.isStopped = false`; does not call `NavMeshAgent.SetDestination`; does not add `entityId` to `_latestRequests` or `_pendingRequests`; `GetCurrentVelocity` returns the preserved path's `desiredVelocity` (non-zero if a prior path existed). Calling `Resume(entityId)` for an entity not in `_activeAgents` logs a warning and returns without error.
Verification: Assert `agent.isStopped == false` after `Resume()`; assert `NavMeshAgent.SetDestination` call count == 0; assert `_latestRequests.ContainsKey(entityId) == false`; assert `_pendingRequests.Count` unchanged; assert `GetCurrentVelocity(entityId)` returns the pre-Stop `desiredVelocity` value. For unknown entity: assert no exception thrown, assert one warning logged. *(CR-NAV-16; ADR-003 Decision 1.)*

---

### Traceability

| AC | Covers |
|----|--------|
| AC-NAV-01 | CR-NAV-2 |
| AC-NAV-02 | CR-NAV-3 |
| AC-NAV-03 | CR-NAV-1 |
| AC-NAV-04 | CR-NAV-4 |
| AC-NAV-05 | CR-NAV-5 |
| AC-NAV-06 | CR-NAV-6 |
| AC-NAV-07 | CR-NAV-5 (guard), EC-NAV-2 |
| AC-NAV-08 | CR-NAV-7 |
| AC-NAV-09 | CR-NAV-8 |
| AC-NAV-10 | CR-NAV-9 |
| AC-NAV-11 | CR-NAV-9 (queue flush), EC-NAV-1 |
| AC-NAV-12 | CR-NAV-10 |
| AC-NAV-13 | CR-NAV-11 |
| AC-NAV-14 | CR-NAV-14 |
| AC-NAV-15 | CR-NAV-14 (order), EC-NAV-7 |
| AC-NAV-16 | CR-NAV-12 |
| AC-NAV-17 | CR-NAV-15 |
| AC-NAV-18 | F-NAV-1 |
| AC-NAV-19 | F-NAV-2 |
| AC-NAV-20 | F-NAV-3, EC-NAV-9 |
| AC-NAV-21 | CR-NAV-13 |
| AC-NAV-22 | CR-NAV-6 (queue), EC-NAV-3 |
| AC-NAV-23 | EC-NAV-10 |
| AC-NAV-24 | CR-NAV-16 |

## Open Questions

**OQ-NAV-1: Multi-zone per process at Vertical Slice. — CLOSED**
Resolved by ADR-002 Decision 4. `NavMesh.AddNavMeshData` is process-global with no per-zone namespace. Multi-zone alternatives were evaluated: (a) separate NavMesh area masks per zone — does not isolate path computation, agents from different zones would interfere; (b) process-per-zone hard constraint — selected as the binding architectural constraint at MVP and Vertical Slice; (c) full NavMesh isolation wrapper — impractical at MVP scale and not supported by Unity's NavMesh API without custom baking per frame. **Decision: one zone per process is the binding constraint for the lifetime of this project until the NavMesh API changes or DOTS NavMesh with per-world isolation becomes viable.** The EC-NAV-6 `_navMeshInstance.valid` guard in `Initialize()` enforces this at runtime. Any architecture review proposing multi-zone processes must supersede ADR-002 Decision 4 first.

**OQ-NAV-2: Integration test environment for AC-NAV-20.**
F-NAV-3 verification (AC-NAV-20) requires a live NavMesh bake fixture and a 150-agent instantiation pass inside Unity Test Framework. This is not possible in a pure unit test context. A baked NavMesh test asset and a headless server fixture must be provisioned before this story can be sprint-committed. Owner: Lead Programmer. Deadline: before Navigation/Pathfinding sprint start.
