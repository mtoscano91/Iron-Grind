# ADR-002: NavMesh Service Execution & Concurrency Contract

## Status

Accepted (2026-06-14)

## Date

2026-06-14

## Last Verified

2026-06-14

## Decision Makers

Technical Director, Lead Programmer, Network Programmer

## Summary

The Navigation/Pathfinding GDD (`navigation-pathfinding.md`) described the `ZoneNavigationService` tick integration in three mutually contradictory ways and assumed synchronous NavMesh path computation — which is incorrect: Unity's NavMesh pathfinding is asynchronous. This ADR establishes the single authoritative execution contract: canonical intra-tick ordering, async pathfinding semantics, teardown↔tick synchronization ownership, process-global NavMesh scope constraint, and required server build configuration for IL2CPP stripping and frame rate.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Navigation / Networking |
| **Knowledge Risk** | HIGH — Unity 6.x is past LLM cutoff; async pathfinding timing and `NavMeshAgent` behavior in `UNITY_SERVER` builds must be verified empirically |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/breaking-changes.md` |
| **Post-Cutoff APIs Used** | `NavMeshAgent` (stable across 6.x — no breaking changes recorded in project docs), `NavMesh.AddNavMeshData` (stable) |
| **Verification Required** | (1) Measure `NavMeshAgent` path resolution latency at 150 agents in representative zone geometry on target server hardware. (2) Confirm `pathPending` behavior when NavMesh loaded via `AddNavMeshData` in `UNITY_SERVER` headless build. (3) Confirm `Application.targetFrameRate = 20` suppresses `NavMeshAgent` internal simulation to 20Hz in `UNITY_SERVER` build (not just rendering). |

> **Note**: Knowledge Risk is HIGH. This ADR must be re-validated if the project upgrades Unity versions beyond 6.3.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | `navigation-pathfinding.md` implementation sprint; Agent Lifecycle State Machine spec |
| **Blocks** | Any navigation sprint story is blocked until this ADR is Accepted AND the Agent Lifecycle State Machine spec is authored |
| **Ordering Note** | `ZoneNavigationService` implementation must not begin until this ADR is Accepted and the Agent Lifecycle State Machine spec exists on disk |

---

## Context

### Problem Statement

`navigation-pathfinding.md` described the server tick integration in three ways that cannot all be true simultaneously:

1. CR-NAV-12: `SyncAgentPositions` runs "AFTER all `GetCurrentVelocity` reads and position integration, BEFORE the next tick begins."
2. CR-NAV-14 and EC-NAV-7: "Tick loop calls `SyncAgentPositions()` at the start of each tick (after position commit), then `Tick()`, then Enemy AI reads `GetCurrentVelocity()`."
3. CR-NAV-14: Path resolution "happens within one Unity main-thread frame (≤50ms at 20Hz)."

Statement 3 is factually wrong. Unity `NavMeshAgent.SetDestination()` queues an asynchronous path computation on Unity's internal pathfinding worker thread. `agent.pathPending` is true until the result arrives — which is not guaranteed within one frame at any queue depth. Additionally, `NavMeshAgent` internal steering simulation runs at Unity's application frame rate (not at 20Hz), and without an explicit `Application.targetFrameRate` pin the headless server runs at uncapped frame rate.

Without a single canonical execution contract, two implementers reading different sections produce divergent server tick loops. The resulting bugs are silent: the mob navigates incorrectly but all systems appear to operate.

### Current State

The GDD (`navigation-pathfinding.md`) is in **Major Revision Needed** status following first-pass review (2026-06-14). This ADR must be Accepted before the GDD revision begins, so the revision corrects the contradictory rules against a settled contract.

### Constraints

- `NavMeshAgent` APIs must execute on the Unity main thread (`UnityEngine.AI` is not thread-safe).
- One zone per server process at MVP (process-global `NavMesh.AddNavMeshData` constraint — see Decision 4).
- 20Hz server tick loop: all navigation state must be consistent within the 50ms tick window.
- `UNITY_SERVER` IL2CPP build strips managed types the linker cannot prove are used through native bridging — `UnityEngine.AI` requires explicit preservation.
- iOS target: no PVRTC, no render pipeline requirements relevant here, but IL2CPP is mandatory.

### Requirements

- The intra-tick ordering of `SyncAgentPositions`, `Navigation.Tick()`, and `GetCurrentVelocity` must be described in exactly one canonical location and implemented consistently.
- The semantics of `GetCurrentVelocity` during `pathPending` must be explicitly defined so Enemy AI can reason about it.
- Zone teardown must not race with an in-flight `Tick()` or `SyncAgentPositions()` call.
- The NavMeshAgent simulation must run at 20Hz to match the tick budget, not at Unity's uncapped frame rate.
- The `UnityEngine.AI` namespace must survive IL2CPP stripping in the Dedicated Server build.

---

## Decision

### Decision 1 — Canonical Intra-Tick Ordering

The authoritative tick phase sequence for **every tick** is:

```
[Tick N − 1 concludes]:
    position_integration(velocities) → committedPositions_N

[Tick N begins]:
    Phase 1: SyncAgentPositions(committedPositions_N)
    Phase 2: Navigation.Tick()                          ← drains path queue
    Phase 3: Enemy AI + other systems read nav state    ← GetCurrentVelocity, IsPathStale
    Phase 4: position_integration(GetCurrentVelocity)  → committedPositions_{N+1}
    [Tick N concludes]
```

**Rationale for this ordering:**
- `SyncAgentPositions` must come before `Navigation.Tick()` so that path requests are issued from the agent's most recently committed world position, not from one-tick-old state.
- `Navigation.Tick()` must come before `GetCurrentVelocity` reads so Enemy AI sees the updated `desiredVelocity` from paths drained this tick.
- `GetCurrentVelocity` reads must come before position integration so the correct velocities drive mob movement.

**Rule:** No code outside `ZoneNavigationService` may call `Navigation.Tick()` or `SyncAgentPositions()` except in this sequence. Both calls are owned by the server tick loop implementation. `INavigationProvider` methods (`SetDestination`, `Stop`, `IsPathStale`, `GetCurrentVelocity`) may be called in Phase 3 only.

**Supersedes:** CR-NAV-12 (the "AFTER GetCurrentVelocity reads" framing), CR-NAV-14 (the conflicting ordering note in Tick()). The GDD revision must adopt this table as the canonical reference and delete conflicting descriptions.

This ordering must also be registered in `networking-core.md` CR-NET-2's tick-phase list. No navigation calls appear there today — that is a propagation gap to fix.

---

### Decision 2 — Async Pathfinding Contract

`NavMeshAgent.SetDestination()` is **asynchronous**. After the call returns, `agent.pathPending == true`. The path is computed by Unity's NavMesh pathfinding worker thread and may resolve in 1 frame (simple geometry, light load) or in multiple frames (complex geometry, 150-agent burst). The claim in CR-NAV-14 that "path resolution happens within one Unity main-thread frame (≤50ms at 20Hz)" is incorrect and must be removed from the GDD.

**`GetCurrentVelocity` semantics during `pathPending`:**

| Agent state | `GetCurrentVelocity` returns |
|-------------|------------------------------|
| Prior path exists (re-route) | Last computed `desiredVelocity` — mob continues on stale heading |
| No prior path (first path, or after `Warp()`) | `Vector3.zero` — mob is stationary |
| Path resolved this tick or prior | Current `desiredVelocity` — correct heading |
| `isStopped == true` | `Vector3.zero` regardless of path state |

**`IsPathStale` during `pathPending`:** Returns `false`. The path is in-flight, not stale — Enemy AI must not re-issue `SetDestination` while `pathPending == true`.

**Worst-case `pathPending` window:** Per EC-NAV-9 in `navigation-pathfinding.md`, `SyncAgentPositions` during `pathPending` (setting `agent.nextPosition`) may internally restart Unity's path computation, extending the window to 2 ticks. The mob may move on its prior heading for up to 2 ticks (100ms at 20Hz) after `SetDestination`. This is bounded, documented, and acceptable at MVP mob speeds.

**No synchronous guarantee.** Implementers must not write code that assumes the path is available on the same tick as `SetDestination`. Read `agent.pathPending` before using `desiredVelocity` if the caller needs to distinguish "has new path" from "still on old heading."

---

### Decision 3 — Teardown ↔ Tick Synchronization

**Problem:** `ZoneNavigationService.Teardown()` calls `NavMesh.RemoveNavMeshData()`. If the server tick loop calls `Navigation.Tick()`, `SyncAgentPositions()`, or `GetCurrentVelocity()` after `RemoveNavMeshData` executes, the behavior of Unity's NavMesh subsystem is undefined and may crash in a headless build.

**Decision:** The server tick loop (or Zone Instancing zone-close handler) is responsible for **halting all navigation calls before invoking `Teardown()`**. The sequence on zone `Closed` state entry must be:

```
1. Set zoneState = Closing (blocks new tick cycles from starting)
2. Wait for any in-flight tick cycle to complete (tick loop is synchronous — check a flag)
3. Call ZoneNavigationService.Teardown()
4. Continue with remaining zone close procedures
```

`ZoneNavigationService` must also implement a defensive guard:

```csharp
private bool _tornDown = false;

public void Tick() {
    if (_tornDown) return;   // silent guard — teardown won already
    // ... drain logic ...
}

public void SyncAgentPositions(...) {
    if (_tornDown) return;
    // ...
}

public Vector3 GetCurrentVelocity(EntityID id) {
    if (_tornDown) return Vector3.zero;
    // ...
}
```

The `_tornDown` guard is a defensive fallback, not the primary synchronization mechanism. The tick loop must not call navigation methods after zone close. The guard protects against future maintenance changes where the ordering is violated.

**Ownership:** Zone Instancing owns the zone lifecycle. Zone Instancing's `Closed` state entry procedure must add "halt navigation tick calls" as step 1. This must be propagated to `zone-instancing.md` when this ADR is accepted.

---

### Decision 4 — Process-Global NavMesh Scope

`NavMesh.AddNavMeshData()` is a Unity process-global operation. There is no per-zone NavMesh namespace. Loading two different zone NavMesh assets in the same process would cause path queries to return results across both meshes — producing incorrect pathfinding for all mobs in both zones.

**Decision: One zone per server process is a binding architectural constraint through Vertical Slice.** This is not an "option to explore later" (as framed in OQ-NAV-1 of the GDD). It is a closed constraint of Unity's NavMesh architecture.

**Closing OQ-NAV-1:** The three options listed in OQ-NAV-1 are evaluated here:

| Option | Status |
|--------|--------|
| NavMesh area masks for zone separation | Rejected — area masks distinguish terrain types, not zone boundaries; two zones' geometry cannot be isolated by area alone |
| Process-per-zone (hard constraint) | Accepted — this is the current architecture; it is the only viable path with Unity's NavMesh |
| Custom isolation wrapper | Rejected for MVP and Vertical Slice — would require coordinate-space tricks or baked offset NavMesh assets; not supported natively; substantial engineering cost |

**Implication:** Infrastructure planning must assume one zone process per active zone. Multi-zone per process is deferred to Post-Launch and requires either migrating to Unity's NavMesh Query API (if one becomes available) or baking zone meshes with non-overlapping coordinate offsets.

The GDD revision must remove OQ-NAV-1 and replace it with a reference to this ADR.

---

### Decision 5 — Server Build Configuration

Two Unity server build configuration requirements are mandatory for `ZoneNavigationService` to function correctly:

**5a — Frame Rate Pin**

In the server build initialization (before any zone is loaded):

```csharp
Application.targetFrameRate = TICK_RATE_HZ;   // 20
```

`NavMeshAgent` internal steering simulation frequency is tied to Unity's application update rate. If `targetFrameRate` is not set, Unity runs uncapped in headless builds (or at platform default), causing the NavMesh AI thread to process agents at N× the intended rate and burning CPU proportionally. At 60Hz instead of 20Hz: 3× the `NavMeshAgent` simulation work per second with no benefit, since the tick loop only reads `desiredVelocity` 20 times per second.

**Note:** Verify that `Application.targetFrameRate` actually controls `NavMeshAgent` simulation cadence in Unity 6.3 `UNITY_SERVER` builds — this behavior may differ from standard Unity builds. If it does not (e.g., NavMeshAgent simulation is tied to `Time.fixedDeltaTime` or a separate subsystem update), use `Time.fixedDeltaTime = 1f / TICK_RATE_HZ` as the fallback, and/or toggle `agent.enabled` to `true` only during the navigation tick phase.

**5b — IL2CPP Stripping Preservation**

Create (or update) `Assets/link.xml` to preserve `UnityEngine.AI`:

```xml
<linker>
  <assembly fullname="UnityEngine.AIModule">
    <namespace fullname="UnityEngine.AI" preserve="all"/>
  </assembly>
</linker>
```

Without this, IL2CPP's managed code linker strips `UnityEngine.AI` types during Dedicated Server builds because the native-to-managed bridge calls are invisible to static analysis. The result is a build that compiles and links successfully but throws `MissingMethodException` at runtime when `NavMeshAgent` is first accessed.

**Player Settings:** Dedicated Server build target must have IL2CPP stripping level set to **Low** or configure `link.xml` as above. Document which approach the project uses in the server build pipeline configuration.

---

## Key Interfaces

```
Canonical tick-phase contract (authoritative):

    ZoneTickLoop.ExecuteTick() {
        // Phase 1: feed committed positions to NavMesh agents
        _navigationService.SyncAgentPositions(_lastCommittedPositions);

        // Phase 2: drain path request queue
        _navigationService.Tick();

        // Phase 3: game systems read nav state
        _enemyAIService.Tick();           // calls GetCurrentVelocity, IsPathStale
        // ... other game systems ...

        // Phase 4: integrate velocities into new positions
        IntegratePositions();
        _lastCommittedPositions = _committedPositions;
    }

Teardown contract:

    ZoneLifecycle.OnZoneClosed() {
        _tickLoopActive = false;          // prevents new tick cycles from starting
        WaitForCurrentTickToComplete();   // tick loop is synchronous — just check flag
        _navigationService.Teardown();   // safe: no tick in flight
        // ... continue zone close ...
    }

    // Defensive guard inside ZoneNavigationService (fallback only):
    public void Tick() { if (_tornDown) return; /* ... */ }
```

---

## Alternatives Considered

### Alternative 1: SyncAgentPositions After GetCurrentVelocity (CR-NAV-12 ordering)

- **Description**: Run GetCurrentVelocity and position integration, then SyncAgentPositions at the very end of the tick (or beginning of the next)
- **Pros**: Agents get positions from the same tick's integration result
- **Cons**: Path requests issued in Tick() use positions that are one tick old; desiredVelocity returned to Enemy AI is from paths computed against stale positions
- **Rejection Reason**: Produces one-tick-old path origin for all requests; for fast mobs this is a persistent directional error. CR-NAV-14 and EC-NAV-7's ordering (SyncAgentPositions before Tick) is correct and is adopted here.

### Alternative 2: Synchronous NavMesh Baking Per-Tick (rejected at architecture)

- **Description**: Use `NavMeshBuilder.UpdateNavMeshData()` to bake fresh path data each tick
- **Pros**: Paths would always be fully resolved within the tick
- **Cons**: Runtime NavMesh baking is prohibitively expensive and is prohibited by CR-NAV-2 (iOS constraint). Not viable.
- **Rejection Reason**: Platform constraint.

### Alternative 3: Multi-Zone Per Process Via Coordinate Offsets

- **Description**: Bake each zone's NavMesh at a unique coordinate offset (zone 1 at world origin, zone 2 at +10000,0,0, etc.) to prevent cross-zone path queries
- **Pros**: Enables multiple zones per process, reducing server instance count
- **Cons**: Requires all mob entity positions to be stored in zone-local space and transformed at every NavMesh API call; complex and error-prone; not natively supported; coordinate drift at large offsets causes floating-point precision issues
- **Rejection Reason**: Cost exceeds benefit at MVP and Vertical Slice scope. Deferred to Post-Launch evaluation.

---

## Consequences

### Positive

- Single canonical tick-phase contract eliminates ambiguity for implementers.
- Async pathfinding contract prevents "path resolves in one frame" assumption from causing latency-dependent bugs.
- Teardown guard prevents undefined behavior during zone close.
- Frame rate pin prevents 3–5× CPU waste from uncapped NavMesh simulation.
- link.xml prevents `MissingMethodException` in the shipping build.

### Negative

- Process-per-zone constraint limits infrastructure cost optimization at Vertical Slice. Zone server count must scale linearly with active zone count.
- Async pathfinding means mobs move on stale headings for up to 2 ticks after target repositioning. This is bounded and documented, but it is a visible behavior limitation.

### Neutral

- The `_tornDown` guard adds a boolean check to every public navigation method. Cost is negligible.
- `link.xml` must be maintained as the project adds other Unity subsystems that use native bridging.

---

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| `Application.targetFrameRate` does not control `NavMeshAgent` simulation in `UNITY_SERVER` builds | MEDIUM | HIGH — NavMesh burns 3–5× intended CPU | Test empirically in Unity 6.3 server build. Fallback: toggle `agent.enabled` to true only during navigation tick phase. |
| Path resolution latency at 150 agents exceeds the 50ms tick budget on target server hardware | MEDIUM | HIGH — server tick deficit | Profile on representative hardware before sprint commitment (see Verification Required). |
| `link.xml` preservation breaks if Unity changes `UnityEngine.AI` assembly structure in a future patch | LOW | HIGH — `MissingMethodException` at runtime | Pin the Unity version in the build pipeline. Update `link.xml` during any engine upgrade. |
| Multi-zone per process requirement emerges before Post-Launch | LOW | MEDIUM — forces emergency architecture work | Decision 4 closes OQ-NAV-1 cleanly. If the requirement emerges, write a superseding ADR before committing to any multi-zone approach. |

---

## Performance Implications

| Metric | Before (unconstrained) | Expected After | Budget |
|--------|------------------------|----------------|--------|
| `NavMeshAgent` simulation overhead | Unbounded (uncapped frame rate) | ~N × (1/20Hz) per agent = bounded to 20Hz | Within tick budget (verify empirically) |
| Path computation latency | "1 frame" assumption (wrong) | 1–N ticks async; worst-case 2 ticks for pathPending extension | 10 ticks total worst-case pursuit lag (F-NAV-3) |
| Teardown race risk | Present (undefined behavior) | Eliminated by `_tornDown` guard + tick loop quiesce | N/A |

---

## Validation Criteria

- [ ] Server build with `Application.targetFrameRate = 20` shows `NavMeshAgent` simulation CPU bounded to 20Hz equivalent in Unity Profiler
- [ ] `link.xml` with `UnityEngine.AI` preservation allows `NavMeshAgent` construction in a headless `UNITY_SERVER` IL2CPP build without `MissingMethodException`
- [ ] Tick loop with canonical Phase 1→2→3→4 order produces correct `desiredVelocity` reads: mobs navigate toward targets, not toward prior or null destinations
- [ ] Zone close sequence halts navigation calls before `Teardown()` executes: no Unity crash or `NullReferenceException` during zone close under load test
- [ ] 150 simultaneous `SetDestination` calls at zone init show `pathPending == true` after the calls and resolve to `pathPending == false` within a measurable number of frames (not one guaranteed frame)

---

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|---|---|---|---|
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-1: server-only, main thread, 20Hz | Establishes canonical tick ordering and frame rate pin that enforces 20Hz execution |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-12: SyncAgentPositions tick placement | Decision 1 provides the authoritative placement (Phase 1 of tick N, before Tick()) |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-14: Tick() placement and path completion claim | Decision 1 places Tick() in Phase 2; Decision 2 removes the incorrect "completes in one frame" claim |
| `design/gdd/navigation-pathfinding.md` | Navigation | OQ-NAV-1: multi-zone per process | Decision 4 closes OQ-NAV-1 as a binding architectural constraint |
| `design/gdd/networking-core.md` | Networking | CR-NET-2: tick-driven phase list | Decision 1 adds navigation Phase 1 (SyncAgentPositions) and Phase 2 (Tick()) to the tick-phase list — propagation required |
| `design/gdd/zone-instancing.md` | Zone Instancing | Zone Closed state entry sequence | Decision 3 adds "halt navigation calls" as step 1 of zone close — propagation required |

---

## Related

- ADR-001: Purchase Transaction Integrity — unrelated domain, reference format
- `design/gdd/navigation-pathfinding.md` — primary GDD being unblocked by this ADR
- `design/gdd/networking-core.md` — must add navigation phases to CR-NET-2 tick list
- `design/gdd/zone-instancing.md` — must add navigation halt to zone Closed state entry
- Agent Lifecycle State Machine spec — companion document resolving Root Cause B; must be authored before navigation implementation begins
