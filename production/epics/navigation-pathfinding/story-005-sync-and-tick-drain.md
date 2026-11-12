# Story 005: `SyncAgentPositions` and the `Tick` Drain

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-12, CR-NAV-14, EC-NAV-7, EC-NAV-9, F-NAV-1, AC-NAV-14 to 16, 18
**Requirement**: `TR-nav-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (Decisions 1, 2); ADR-003 Decision 4
**ADR Decision Summary**: Every tick: phase 1 `SyncAgentPositions(committed)`, phase 2 `Tick()` drain, phase 3 systems read nav state, phase 4 position integration. The drain takes at most `MAX_PATH_UPDATES_PER_TICK` entries, always issues the latest destination, and skips entries that were stopped or released (`TryGetValue` guard).

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (setting `nextPosition` during `pathPending` may extend the pending window to 2 ticks, EC-NAV-9)
**Engine Notes**: with `updatePosition = false`, `nextPosition` is how the agent learns the server position; never `Warp` or assign `transform.position` here.

**Control Manifest Rules (this layer)**:
- Required: phase order sync → drain → read → integrate, every tick
- Required: drain guard `_latestRequests.TryGetValue` then `continue`
- Forbidden: calling `Tick()` / `SyncAgentPositions()` outside `ZoneNavigationService` and the canonical phase

---

## Acceptance Criteria

- [ ] **SyncAgentPositions** (CR-NAV-12, AC-NAV-16): for each committed position whose entity is registered and on the mesh, sets `nextPosition`; never calls `Warp`; skips off-mesh agents and unknown entities silently.
- [ ] **Drain** (CR-NAV-14, AC-NAV-14): one `Tick` issues at most `MAX_PATH_UPDATES_PER_TICK` agent `SetDestination` calls; the rest stay queued; each issued call uses `_latestRequests[e]`, sets `isStopped = false` first, then removes the entry.
- [ ] **Drain skips** (ADR-003 Decision 4): unregistered entities, off-mesh agents, and entities missing from `_latestRequests` (stopped or released) are dequeued without a call and without throwing; skipped entries do not count toward the per-tick limit. Every dequeue, issued or skipped, removes the entity from `_queuedEntities`. An off-mesh skip keeps the entity's `_latestRequests` entry (the ADR-003 code); `IsPathStale` is true off the mesh, so Enemy AI re-requests and the entity is queued again (recovery is Story 006).
- [ ] **Drain failure paths**: the entry is dequeued and removed from `_queuedEntities` before the agent call. Agent `SetDestination` returns false → one warning, the `_latestRequests` entry is removed, and the call counts toward the limit. Agent `SetDestination` throws → the exception propagates (`ZoneTickPipeline` logs it); queue and sets are already consistent, and the rest of the queue drains next tick.
- [ ] **F-NAV-1 / AC-NAV-18**: 150 unique queued destinations at R = 20 → queue non-empty after 7 ticks, empty after exactly 8, 150 calls in total.
- [ ] **Order** (AC-NAV-15, EC-NAV-7): within a tick, `SyncAgentPositions` runs before the first drained `SetDestination`; `Tick` never calls `GetCurrentVelocity`. The one entry point is `NavigationPhaseStep : IZoneSimulationStep` (`src/ServerLogic/Navigation/NavigationPhaseStep.cs`): `Run(currentTick, deltaTime)` clears its buffer, fills it from the source, calls `SyncAgentPositions`, then `Tick()`. It holds the concrete `ZoneNavigationService`.
- [ ] **Committed positions input**: the step reads committed mob positions through `IMobCommittedPositionSource` (new, `src/ServerLogic/Navigation/`) with `void CopyCommittedPositions(Dictionary<EntityID, Vector3> into)` (the caller owns and clears the buffer), because Enemy AI owns mob position integration (phase 4). This story also adds `EmptyMobCommittedPositionSource` (copies nothing), the stand-in Story 010 wires until Enemy AI exists.
- [ ] **Performance**: sync is O(committed positions), the drain O(entries dequeued); neither allocates on the success path (one buffer owned by the step, no boxed enumerators); strings only on log paths.

---

## Implementation Notes

- **Signature**: the GDD has `SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3>)`. Keep it public, and add an `internal SyncAgentPositions(Dictionary<EntityID, Vector3>)` overload that holds the loop: `foreach` over the interface boxes the enumerator every tick, the concrete `Dictionary` does not. The public method forwards (its own enumeration may allocate; only tests and future callers use it). The step calls the overload with its reused buffer.
- **`INavigationPositionSync` decision (lead programmer, `/story-readiness` 2026-11-12)**: not extracted. `NavigationPhaseStep` owns phases 1–2 and holds the concrete service; the order test uses a call recorder over the fake agents (`NextPosition` set, `IsStopped` set, `SetDestination`), so no extra interface is needed. The service keeps `SyncAgentPositions` and `Tick`; Story 010 adds the step to `_simulationSteps`.
- **Skip counting**: CR-NAV-14 pseudocode reads "for up to MAX dequeued entries"; ADR-003 Decision 4 code increments `drained` only on an issued call. The ADR governs (skips are cheap; the queue holds at most one slot per entity through `_queuedEntities`). Fix the CR-NAV-14 wording at the next GDD pass.
- **Re-queue on the same tick**: a `SetDestination` called again for an entity already in the queue only overwrites `_latestRequests` (Story 004). If a queued entry was drained and the entity calls again later, it is re-enqueued. Test that an entity drained on tick N and re-requested on N is issued on N+1.
- **Partial counter reset** on a processed `SetDestination` (CR-NAV-15) belongs to Story 006; leave a clear hook.
- **Story 003 carry (2026-11-12)**: release (`HandleMobDied`) removes the entity from `_activeAgents`, `_latestRequests` and `_failureCounts` but does not search `_pendingRequests` (a queue). The drain must dequeue and skip any entry whose entity is not in `_activeAgents` or has no `_latestRequests` entry (the "released e2" skip case), and a released-then-respawned `EntityID` must not be issued a stale destination. Until the drain empties the queue, `TrackedStateCount` can be non-zero after a release; the release test asserts it only when nothing was queued.
- **Story 004 carry (2026-11-12)**: Story 004 adds `HashSet<EntityID> _queuedEntities` ("already queued", O(1)); `SetDestination` enqueues only when the entity is not in it. The drain removes the entity from `_queuedEntities` on every dequeue, issued or skipped, so a later request re-enqueues. Release does not touch the set; the stale slot is skipped here. The post-drain halves of AC-NAV-11 (Stop after queued `SetDestination` → no call, `isStopped` stays true) and AC-NAV-22 (MobDied after queued `SetDestination` → no call) are asserted by the "Skips" test (stopped e1, released e2); cite them there.
- No same-tick path: tests must not assert a path or non-zero velocity on the drain tick (ADR-002 Decision 2).

---

## Out of Scope

- Story 006: partial-path counting, off-mesh recovery (both tick inside `Tick` / sync; add them there)
- Story 010: adding the phase step (with `EmptyMobCommittedPositionSource`) to the composition before the phase-3 systems
- Enemy AI epic: the real committed-position source and phase 4 integration

---

## QA Test Cases

- **Sync**: two registered agents on mesh, one off mesh, one unknown id → `nextPosition` set on the two; untouched on the off-mesh agent; no throw for the unknown id; `Warp` count 0.
- **Drain 20**: 150 queued → after one `Tick`, 20 calls, 130 queued; each call's destination equals the entity's latest.
- **Latest wins across the queue**: `SetDestination(e, A)`, then `(e, B)` before its turn → the drain issues B once.
- **Skips** (also AC-NAV-11, AC-NAV-22 post-drain): queue [stopped e1, released e2, off-mesh e3, valid e4..e23] with R = 20 → no throw; calls go to e4..e23 (20 calls); e1–e3 dequeued; e1 `isStopped` still true; `_queuedEntities` lacks all 23.
- **F-NAV-1**: 150 queued → non-empty after 7 ticks; empty after 8; total 150.
- **Order**: recorder over the phase entry point → `nextPosition` writes precede the first `SetDestination`; `GetCurrentVelocity` not called.
- **isStopped before SetDestination**: recorder shows `isStopped = false` then `SetDestination`.
- **Re-request after drain**: drained on tick N, requested again on N → issued on N+1.
- **Respawn with a stale slot** (Story 004 code-review carry): `SetDestination(e, A)` → `MobDied(e)` → `MobSpawned(e)` → `SetDestination(e, B)` → one queue slot; `Tick` issues B exactly once on the new agent (decided from live `_activeAgents` / `_latestRequests`, not slot age); `_queuedEntities` lacks e afterwards; a later `SetDestination` re-enqueues.
- **Empty source**: `EmptyMobCommittedPositionSource`, nothing queued → `Run` makes no agent calls.
- **SetDestination returns false**: agent scripted to return false → one warning; `_latestRequests` lacks e; `_queuedEntities` lacks e; the call counts toward R (R = 1, two queued → second stays queued).
- **SetDestination throws**: agent scripted to throw → exception propagates out of `Tick`; e no longer queued and not in `_queuedEntities`; the next `Tick` issues the remaining entries.
- **Off-mesh skip keeps the request**: off-mesh e3 dequeued → `HasLatestRequest(e3)` still true, `IsQueued(e3)` false; a later `SetDestination(e3, …)` re-enqueues.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_TickDrain_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004
- Unlocks: Story 006
