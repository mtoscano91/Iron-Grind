# ADR-003: Navigation Agent Lifecycle State Machine

## Status

Accepted (2026-06-14)

## Date

2026-06-14

## Last Verified

2026-06-14

## Decision Makers

Technical Director, Lead Programmer, AI Programmer

## Summary

The Navigation/Pathfinding GDD described the `ZoneNavigationService` agent lifecycle with three critical gaps: no `Resume()` API (mobs permanently freeze after attack cycles), no Returning-state speed control (fast mobs overshoot spawn point and freeze in `Returning` forever), and contradictory `NavMeshAgent.enabled` ownership across three rules. This ADR establishes the authoritative lifecycle state machine with the complete transition table, resolves all three gaps, and defines the pool-exhaustion error policy.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Navigation / Scripting |
| **Knowledge Risk** | HIGH — NavMeshAgent state persistence across `enabled` toggle unverified for Unity 6.3 |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/breaking-changes.md` |
| **Post-Cutoff APIs Used** | `NavMeshAgent.isStopped`, `NavMeshAgent.enabled` (stable; no breaking changes recorded) |
| **Verification Required** | Confirm that setting `isStopped = true` on an enabled `NavMeshAgent`, then calling `agent.enabled = false` and later `agent.enabled = true`, does NOT auto-reset `isStopped` to false on re-enable. If it does, the pool release + reclaim sequence in CR-NAV-6 and CR-NAV-5 must set `isStopped` explicitly on each reclaim (already specified in CR-NAV-5 step 6 — verify this is sufficient). |

> **Note**: Knowledge Risk is HIGH. Re-validate `NavMeshAgent` state persistence on engine upgrade.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-002 (NavMesh Service Execution & Concurrency Contract) must be Accepted first |
| **Enables** | `navigation-pathfinding.md` GDD revision; `INavigationProvider` interface finalization; `enemy-ai.md` Dormant state documentation correction |
| **Blocks** | `ZoneNavigationService` implementation; any enemy AI story that calls `Stop()` + expects subsequent movement |
| **Ordering Note** | `INavigationProvider` interface is finalized by this ADR. Enemy AI implementation must not begin before this ADR is Accepted. |

---

## Context

### Problem Statement

Three gaps in the Navigation/Pathfinding GDD produce permanent mob freeze conditions baked into the specification:

**Gap 1 — No `Resume()` API (permanent freeze after attack cycles):**
Enemy AI calls `Stop(entityId)` on WindingUp entry (CR-AI-11). CR-NAV-9 sets `isStopped = true` and does not call `ResetPath()` (path preserved for re-entry). When the attack cycle completes and the mob re-enters `Pursuing`, the mob's target may not have moved more than `PURSUIT_REPOSITION_THRESHOLD (1.0m)` — the player was melee-tanking. No `SetDestination` is issued. `isStopped` is only cleared in `Tick()` when draining a queued `SetDestination`. Without a queued `SetDestination`, `isStopped` is never cleared. `desiredVelocity = 0`. The mob is permanently frozen after every attack cycle in the most common melee combat scenario.

**Gap 2 — Returning-state overshoot (fast mobs stuck in Returning forever):**
`autoBraking = false` prevents `desiredVelocity` from dropping near the path endpoint (correct for `Pursuing` — full-speed approach). In `Returning` state, the mob navigates to the spawn point at full `MoveSpeed`. On the final tick before reaching the spawn, `desiredVelocity` reports full speed. Position integration commits the mob to `spawn_point + MoveSpeed/TICK_RATE_HZ` meters past the target. `PathComplete` fires. `desiredVelocity = 0`. Enemy AI's arrival check fails (`sqrDistToSpawn > RETURN_ARRIVAL_THRESHOLD²`). No re-route (Enemy AI called `SetDestination(spawnPoint)` once on Returning entry). Mob is permanently stuck in `Returning`. Activates for any `MobDefinition.MoveSpeed > 10 m/s` (produces 0.5m+ overshoot per tick, exceeding the 0.5m arrival threshold).

**Gap 3 — `NavMeshAgent.enabled` ownership contradiction:**
CR-NAV-5 enables the agent on MobSpawned. CR-NAV-14 has a dead enable check implying agents start disabled and are enabled on first SetDestination. Enemy AI state table says Dormant mobs have NavMeshAgent disabled. These cannot all be true simultaneously.

### Constraints

- `INavigationProvider` is the only interface Enemy AI uses for navigation. Adding methods changes the interface.
- `autoBraking` cannot be per-state without adding a nav API call at state transitions.
- Pool size is fixed (150). Pool slot is stable from MobSpawned to MobDied.
- All agent state mutations are main-thread only (ADR-002 Decision 1).

---

## Decision

### Decision 1 — Add `Resume(EntityID)` to `INavigationProvider`

**Interface change:**

```csharp
public interface INavigationProvider {
    void SetDestination(EntityID entityId, Vector3 target);
    void SetSpeed(EntityID entityId, float speed);
    void Stop(EntityID entityId);
    void Resume(EntityID entityId);           // NEW
    bool IsPathStale(EntityID entityId);
    Vector3 GetCurrentVelocity(EntityID entityId);
}
```

**`Resume(EntityID entityId)` semantics:**

1. If entity not in `_activeAgents`: log warning, return.
2. `agent.isStopped = false`.
3. Does NOT call SetDestination or modify `_latestRequests`/`_pendingRequests`.
4. The preserved path (from before `Stop()`) immediately resumes producing valid `desiredVelocity`.

**Why `Resume()` rather than forcing a new `SetDestination`:**
Enemy AI calls `Stop()` specifically to preserve the path for re-entry (CR-AI-11 rationale). If re-entry always required a new `SetDestination`, the preserve-path behavior has no value — the mob would always wait for a drain slot. `Resume()` makes the preserved-path semantic possible and explicit. Enemy AI chooses at the call site whether to `Resume()` (use preserved path) or `SetDestination()` (request a new path).

**Enemy AI usage contract:**
- On Pursuing re-entry after WindingUp/Recovering: call `Resume(entityId)` first. Then check `IsPathStale()`. If stale (target moved, partial-path timeout, etc.): call `SetDestination(entityId, currentTargetPos)` immediately after.
- On Returning entry: call `SetDestination(entityId, spawnPoint)`. Do NOT call `Resume()` — Returning always requires a new destination.

**`Stop() → Resume()` interaction with `_latestRequests`:**
`Stop()` removes `entityId` from `_latestRequests` (CR-NAV-9, EC-NAV-1). `Resume()` does not re-add it. This is correct: Resume uses the already-computed path, not a queued destination. `_latestRequests` is only relevant to the drain queue in `Tick()`.

---

### Decision 2 — Returning State Speed Cap via `SetSpeed`

**Problem:** Full combat `MoveSpeed` on Returning causes overshoot past the spawn point for fast mobs.

**Decision:** Enemy AI must call `SetSpeed(entityId, RETURN_SPEED_CAP)` when entering `Returning` state, and restore `MobDefinition.MoveSpeed` when exiting `Returning` (on `Dormant` entry or if combat re-aggroes the mob before it arrives).

```
// Enemy AI state transition: any → Returning
navProvider.SetSpeed(entityId, RETURN_SPEED_CAP);
navProvider.SetDestination(entityId, spawnPoint);

// Enemy AI state transition: Returning → Dormant
navProvider.Stop(entityId);
navProvider.SetSpeed(entityId, mobDef.MoveSpeed);   // restore

// Enemy AI state transition: Returning → Pursuing (re-aggro during return)
navProvider.SetSpeed(entityId, mobDef.MoveSpeed);   // restore to combat speed
navProvider.SetDestination(entityId, newTargetPos);
```

**`RETURN_SPEED_CAP` definition:**

| Property | Value | Rationale |
|---|---|---|
| Name | `RETURN_SPEED_CAP` | Server constant, server-side navigation tuning knob |
| Value | `6.0 m/s` | Matches baseline player walk speed; mob does not sprint to spawn faster than the player can observe it |
| Source | `navigation-pathfinding.md` Tuning Knobs (add this entry) |
| Safe range | `[1.0, 10.0]` m/s | Upper bound: 10 m/s → max overshoot 0.5m/tick, within arrival threshold; Lower bound: 1.0 m/s → return trip ≤ ~70s across largest zone |

**Why this approach (vs. enabling autoBraking on Returning):**
`autoBraking` stops the mob short of the destination by the stopping distance. In practice, mob arrival at spawn is when `PathComplete` fires AND `sqrDist < threshold`. With autoBraking enabled, the mob may stop 0.5–1m short of the spawn point — which is also an arrival failure for a tight threshold. Speed-capping on return is more predictable: the mob arrives at the spawn point at a slower constant speed, `autoBraking=false` remains globally off (no per-state enable/disable needed), and overshoot is bounded to `RETURN_SPEED_CAP / TICK_RATE_HZ = 0.3m` at default cap — well within `RETURN_ARRIVAL_THRESHOLD (0.5m)`.

**`RETURN_SPEED_CAP` must be added to entities.yaml and to Tuning Knobs in `navigation-pathfinding.md`.**

---

### Decision 3 — `NavMeshAgent.enabled` Canonical Ownership

The authoritative rule: **`NavMeshAgent.enabled` tracks pool occupancy, not Enemy AI state.**

| Condition | `NavMeshAgent.enabled` | Responsible code |
|---|---|---|
| Agent in pool (unoccupied) | `false` | `ZoneNavigationService` constructor; also set in pool return (CR-NAV-6) |
| Agent claimed (MobSpawned) | `true` | CR-NAV-5 step 3 — enabled immediately on claim, before Warp |
| Agent released (MobDied) | `false` | CR-NAV-6 step 4 |
| Enemy AI Dormant state | `true` (but `isStopped = true`) | No change — Dormant does not disable the agent |
| Enemy AI any active state | `true` | No change at state transitions |

**Deletions / corrections required:**

- CR-NAV-14: Remove the dead enable check (`if !agent.enabled: agent.enabled = true`). This guard was dead code given CR-NAV-5 step 3. Delete it entirely.
- Enemy AI GDD (`enemy-ai.md`) Dormant state table: Change "NavMeshAgent disabled" → "NavMeshAgent enabled, `isStopped = true`." The agent being enabled in Dormant costs essentially nothing (`isStopped = true` suppresses all steering). The original documentation was incorrect.

**Why leave the agent enabled during Dormant:**
Disabling in Dormant would require a new `INavigationProvider` API (`Disable(EntityID)` / `Enable(EntityID)`) to let Enemy AI control the Unity component. That API complicates the interface and the pool contract without meaningful performance benefit: `NavMeshAgent` with `isStopped = true` and no path assigned does negligible per-frame work.

---

### Decision 4 — Tick Drain Crash Fix (`_latestRequests` Guard)

The drain loop in `Tick()` (CR-NAV-14) must add a `TryGetValue` guard after the `_activeAgents` check:

```csharp
while (drained < MAX_PATH_UPDATES_PER_TICK && _pendingRequests.TryDequeue(out var entityId)) {
    if (!_activeAgents.TryGetValue(entityId, out var agent)) continue;  // dead mob
    if (!agent.isOnNavMesh) continue;                                    // off-mesh skip
    if (!_latestRequests.TryGetValue(entityId, out var latestDest)) continue; // REQUIRED: stopped/dead
    agent.isStopped = false;
    agent.SetDestination(latestDest);
    _latestRequests.Remove(entityId);
    drained++;
}
```

Without the `TryGetValue` guard, the sequence `SetDestination(entityId, dest)` → `Stop(entityId)` → `Tick()` crashes with `KeyNotFoundException`. `Stop()` removes from `_latestRequests` but `_pendingRequests` Queue has no key-based removal; the stale `entityId` remains in the drain queue.

This fix must be reflected in CR-NAV-14's pseudocode in `navigation-pathfinding.md`.

---

### Decision 5 — Pool-Exhaustion Error Policy

When `MobSpawned` fires for a 151st mob and the pool is empty (CR-NAV-5 guard: "If pool empty, log error, do not register"):

1. The entity is **not** registered in `_activeAgents`.
2. All subsequent `INavigationProvider` calls for this entity receive **safe default responses**:

| Method | Safe default when entity not in `_activeAgents` |
|---|---|
| `SetDestination` | Log warning, return — do not enqueue |
| `SetSpeed` | Log warning, return |
| `Stop` | Log warning, return |
| `Resume` | Log warning, return |
| `IsPathStale` | Return `true` — mob is effectively stranded |
| `GetCurrentVelocity` | Return `Vector3.zero` — mob does not move |
| `MobDied` (CR-NAV-6) | Log warning, return — nothing to release |

This contract means a pool-exhausted mob is visibly stationary and never aggroes, but does not crash the server. Enemy AI observes `IsPathStale = true` and `GetCurrentVelocity = zero` — consistent signals indicating the mob cannot navigate. Whether Enemy AI spawns this mob as permanently dormant or handles the pool-exhaustion case explicitly is documented in `enemy-ai.md`.

**AC-NAV-04 correction (required in GDD revision):** The current AC-NAV-04 asserts an exception on the 151st claim. This contradicts the log-and-return policy and must be replaced with:
- Assert: no exception thrown
- Assert: one error log containing "pool exhausted" and the entityId
- Assert: `_activeAgents.Count == 150`
- Assert: 151st entityId not in `_activeAgents`
- Assert (follow-up): subsequent `GetCurrentVelocity(151st entity)` returns `Vector3.zero`
- Assert (follow-up): subsequent `IsPathStale(151st entity)` returns `true`

---

### Decision 6 — Complete Lifecycle State Table

The authoritative agent lifecycle. Each state is a combination of `_activeAgents` membership, `agent.enabled`, `agent.isStopped`, and path status.

```
State               _activeAgents  enabled  isStopped  path status   notes
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Pool (unoccupied)   No             false    (any)      (any)          Waiting for MobSpawned
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Registered          Yes            true     true       None           Post-MobSpawned, pre-first cmd
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Pursuing            Yes            true     false      Pending/       SetDestination issued;
                                                       Partial/       desiredVelocity drives movement
                                                       Complete
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Stopped (WindingUp/ Yes            true     true       Preserved      Stop() called;
 Attacking/         (unchanged)              desiredVelocity=0
 Recovering)
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Returning           Yes            true     false      Pending/       SetDestination(spawnPoint);
                                                       Partial/       SetSpeed(RETURN_SPEED_CAP)
                                                       Complete
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Dormant             Yes            true     true       Complete       Arrived at spawn; isStopped=true;
                                                       (or none)      SetSpeed(mobDef.MoveSpeed) restored
─────────────────── ─────────────  ───────  ─────────  ───────────   ─────
Releasing           Yes → No       true →   true       Reset          MobDied processing:
(MobDied)                          false               (none)         ResetPath → disable → pool return
```

**Transitions:**

| From | Event | Nav calls | To |
|---|---|---|---|
| Pool | MobSpawned | Claim, enable, Warp, SetSpeed(mobDef.MoveSpeed), isStopped=true | Registered |
| Registered | Enemy AI aggros | SetDestination(target) | Pursuing |
| Pursuing | WindingUp entry | Stop() | Stopped |
| Stopped | Pursuing re-entry (target in range) | Resume() → if IsPathStale: SetDestination(target) | Pursuing |
| Pursuing | Returning entry | SetSpeed(RETURN_SPEED_CAP) + SetDestination(spawnPoint) | Returning |
| Stopped | Returning entry (e.g. target died) | Stop() already set; SetSpeed(RETURN_SPEED_CAP) + SetDestination(spawnPoint) | Returning |
| Returning | Arrival (sqrDist < threshold) | Stop(); SetSpeed(mobDef.MoveSpeed) | Dormant |
| Returning | Re-aggro during return | SetSpeed(mobDef.MoveSpeed) + SetDestination(newTarget) | Pursuing |
| Dormant | Enemy AI aggros | SetDestination(target) (note: Resume() NOT correct here — new destination needed) | Pursuing |
| Any non-Dead | MobDied | (internal: ResetPath, disable, pool return, remove from all dicts) | Pool |

---

## Key Interfaces

```csharp
public interface INavigationProvider {
    void SetDestination(EntityID entityId, Vector3 target);
    void SetSpeed(EntityID entityId, float speed);
    void Stop(EntityID entityId);
    void Resume(EntityID entityId);     // clears isStopped, uses preserved path
    bool IsPathStale(EntityID entityId);
    Vector3 GetCurrentVelocity(EntityID entityId);
}

// Implemented by ZoneNavigationService (not on INavigationProvider — tick loop holds concrete ref):
// void SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3> committedPositions);
// void Tick();
// void Initialize(NavMeshData navMeshData, ZoneSpawnTable spawnTable);
// void Teardown();
```

---

## Alternatives Considered

### Alternative 1: Force SetDestination on Every Pursuing Re-Entry (no Resume needed)

- **Description**: Enemy AI always issues `SetDestination` on Pursuing re-entry, regardless of whether the target has moved.
- **Pros**: No new API needed. Simpler INavigationProvider.
- **Cons**: Every attack cycle consumes a drain slot, even when the mob could immediately resume on its preserved path. At 150 mobs fighting simultaneously, this adds up to 150 guaranteed drain-slot uses per attack cycle burst. Defeats the purpose of CR-AI-11's path-preservation rationale.
- **Rejection Reason**: Unnecessary throughput cost. `Resume()` is the correct API for the documented use case.

### Alternative 2: Enable autoBraking Per State (Pursuing=off, Returning=on)

- **Description**: Add `SetBrakingEnabled(EntityID, bool)` to `INavigationProvider` and call it at every state transition.
- **Pros**: Let Unity's autoBraking handle the stopping naturally near the spawn point.
- **Cons**: autoBraking's stopping distance is proportional to current speed and not configurable without also setting `stoppingDistance`. Stopping 0.5–1m short of the spawn point is also an arrival failure. More API surface, more state to manage.
- **Rejection Reason**: Speed-capping on return (Decision 2) is simpler, more predictable, and has a useful secondary effect (controlled return speed for visual plausibility).

### Alternative 3: Dynamic RETURN_ARRIVAL_THRESHOLD per mob speed

- **Description**: Enemy AI computes `RETURN_ARRIVAL_THRESHOLD = max(DEFAULT, MoveSpeed / TICK_RATE_HZ)` per mob on Returning entry.
- **Pros**: No `SetSpeed` call needed. Self-adjusting.
- **Cons**: The threshold lives in Enemy AI GDD, but the correct value depends on NavMesh integration timing — a nav concern. Threshold expansion can make arrival detection sloppy (mob "arrives" 2m from spawn at high speed). Speed capping on return is more principled.
- **Rejection Reason**: Cross-system coupling and poor threshold quality at high speeds. Speed cap is cleaner.

---

## Consequences

### Positive

- `Resume()` eliminates the permanent freeze after every attack cycle — the most critical mob behavior bug.
- `RETURN_SPEED_CAP` eliminates the Returning-forever bug for all mob definitions.
- Canonical `enabled` ownership removes the three-way contradiction from the GDD.
- Drain crash fix (`TryGetValue` guard) eliminates `KeyNotFoundException` on every stop-during-pursuit event.
- Pool-exhaustion safe defaults prevent the 151st mob from crashing the server.

### Negative

- `INavigationProvider` gains one method (`Resume`). Any future alternate implementation must implement it.
- `SetSpeed` must now be called at Returning entry AND on Dormant/Pursuing exit from Returning. Enemy AI has more call sites.
- `RETURN_SPEED_CAP` is a new constant that requires tuning against zone size and mob respawn cadence.

### Neutral

- `NavMeshAgent.enabled = true` in Dormant adds a minor per-tick cost vs. disabled. Measured cost is negligible for an agent with `isStopped = true` and no path.

---

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| `isStopped` state does not persist across NavMeshAgent disable/enable cycle | MEDIUM | MEDIUM — pool reclaim must always set isStopped explicitly | Decision 3 requires `isStopped = true` in CR-NAV-5 step 6 after enable + Warp; verify in engine |
| Enemy AI misses `SetSpeed(mobDef.MoveSpeed)` restore on Dormant entry | MEDIUM | LOW — mob returns fast and arrives slowly until next aggro, then restores on re-aggro | AC for Returning→Dormant must include speed assertion |
| `Resume()` called on entity with no preserved path (e.g. newly registered mob) | LOW | LOW — isStopped=false, agent navigates with zero path, desiredVelocity=0 | IsPathStale returns "never-called" condition = true; Enemy AI will follow up with SetDestination |

---

## Validation Criteria

- [ ] Mob in Pursuing state calls Stop() then immediately calls Resume() without SetDestination — assert `GetCurrentVelocity` returns non-zero (preserved path provides heading)
- [ ] Mob with MoveSpeed = 15.0 m/s enters Returning state with SetSpeed(RETURN_SPEED_CAP=6.0) — assert `NavMeshAgent.speed == 6.0` during return
- [ ] Mob with MoveSpeed = 15.0 m/s arrives at spawn point — assert `agent.PathComplete`, assert `sqrDistToSpawn < RETURN_ARRIVAL_THRESHOLD²` (not stuck in Returning)
- [ ] Mob returns to spawn and enters Dormant — assert `NavMeshAgent.speed == 15.0` (restored), assert `isStopped == true`
- [ ] SetDestination queued for entity → Stop() called → Tick() drain executes — assert no exception thrown, no SetDestination forwarded to NavMeshAgent
- [ ] 151st MobSpawned when pool full — assert no exception, assert `GetCurrentVelocity(151st) == Vector3.zero`, assert `IsPathStale(151st) == true`
- [ ] MobDied for entity not in pool (pool-exhausted mob) — assert no exception, log warning
- [ ] Enemy AI state table Dormant row: assert `NavMeshAgent.enabled == true && isStopped == true` after MobSpawned with no subsequent commands

---

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|---|---|---|---|
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-9: Stop preserves path for re-entry | Decision 1 adds Resume() to make the preserved path semantic implementable |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-4: autoBraking=false | Decision 2 adds RETURN_SPEED_CAP so autoBraking=false remains global while Returning overshoot is bounded |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-5 step 3 vs CR-NAV-14 vs Enemy AI Dormant | Decision 3 establishes canonical enabled=true-on-claim rule and deletes CR-NAV-14's dead enable check |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-14: Tick() drain | Decision 4 adds the mandatory TryGetValue guard to prevent KeyNotFoundException |
| `design/gdd/navigation-pathfinding.md` | Navigation | CR-NAV-5 pool exhaustion: log-and-return | Decision 5 defines complete safe-default response for all INavigationProvider methods on unregistered entities |
| `design/gdd/enemy-ai.md` | Enemy AI | CR-AI-11: Stop on WindingUp entry + Pursuing re-entry | Decision 1 requires Enemy AI to call Resume() (not SetDestination) on Pursuing re-entry when path is still valid |
| `design/gdd/enemy-ai.md` | Enemy AI | Returning state — SetDestination once on entry | Decision 2 requires Enemy AI to also call SetSpeed(RETURN_SPEED_CAP) on Returning entry |

---

## Downstream Propagation Required

| Document | Change |
|---|---|
| `design/gdd/navigation-pathfinding.md` | Add `Resume(EntityID)` to INavigationProvider; add TryGetValue guard to CR-NAV-14; add RETURN_SPEED_CAP tuning knob; fix AC-NAV-04; correct `enabled` ownership; delete CR-NAV-14 dead enable check |
| `design/gdd/enemy-ai.md` | Add `Resume()` call on Pursuing re-entry; add SetSpeed(RETURN_SPEED_CAP) on Returning entry and restore on exit; correct Dormant state table `enabled` entry |
| `design/registry/entities.yaml` | Register `RETURN_SPEED_CAP: 6.0 m/s, source: navigation-pathfinding.md` |

---

## Related

- ADR-002: NavMesh Service Execution & Concurrency Contract (prerequisite — must be Accepted first)
- `design/gdd/navigation-pathfinding.md` — primary GDD being unblocked
- `design/gdd/enemy-ai.md` — caller of `INavigationProvider`; must implement `Resume()` and `SetSpeed` on Returning
- `design/registry/entities.yaml` — must receive `RETURN_SPEED_CAP` entry
