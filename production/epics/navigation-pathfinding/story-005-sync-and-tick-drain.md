# Story 005: `SyncAgentPositions` and the `Tick` Drain

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

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
- [ ] **Drain skips** (ADR-003 Decision 4): unregistered entities, off-mesh agents, and entities missing from `_latestRequests` (stopped or released) are dequeued without a call and without throwing; skipped entries do not count toward the per-tick limit.
- [ ] **F-NAV-1 / AC-NAV-18**: 150 unique queued destinations at R = 20 → queue non-empty after 7 ticks, empty after exactly 8, 150 calls in total.
- [ ] **Order** (AC-NAV-15, EC-NAV-7): within a tick, `SyncAgentPositions` runs before the first drained `SetDestination`; `Tick` never calls `GetCurrentVelocity`. Expose one entry point the composition calls for phases 1–2 (for example `RunNavigationPhases(committed)`, or a step class), so the order is in one place.
- [ ] **Committed positions input**: the phase-1 entry point reads committed mob positions through a seam (`IMobCommittedPositionSource`), because Enemy AI owns mob position integration (phase 4). Until Enemy AI exists the source is empty.

---

## Implementation Notes

- **Signature**: the GDD has `SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3>)`. Keep it, but have the step pull from `IMobCommittedPositionSource` so no dictionary is allocated per tick (reuse one buffer). CR-NAV-12 leaves "extract `INavigationPositionSync`" to the lead programmer: decide at `/story-readiness` and record it (recommended: a `NavigationPhaseStep` class owning phases 1–2 that the composition adds to `_simulationSteps`; the service keeps the two methods).
- **Re-queue on the same tick**: a `SetDestination` called again for an entity already in the queue only overwrites `_latestRequests` (Story 004). If a queued entry was drained and the entity calls again later, it is re-enqueued. Test that an entity drained on tick N and re-requested on N is issued on N+1.
- **Partial counter reset** on a processed `SetDestination` (CR-NAV-15) belongs to Story 006; leave a clear hook.
- No same-tick path: tests must not assert a path or non-zero velocity on the drain tick (ADR-002 Decision 2).

---

## Out of Scope

- Story 006: partial-path counting, off-mesh recovery (both tick inside `Tick` / sync; add them there)
- Story 010: adding the phase step to the composition before the phase-3 systems
- Enemy AI epic: the real committed-position source and phase 4 integration

---

## QA Test Cases

- **Sync**: two registered agents on mesh, one off mesh, one unknown id → `nextPosition` set on the two; untouched on the off-mesh agent; no throw for the unknown id; `Warp` count 0.
- **Drain 20**: 150 queued → after one `Tick`, 20 calls, 130 queued; each call's destination equals the entity's latest.
- **Latest wins across the queue**: `SetDestination(e, A)`, then `(e, B)` before its turn → the drain issues B once.
- **Skips**: queue [stopped e1, released e2, off-mesh e3, valid e4..e23] with R = 20 → no throw; calls go to e4..e23 (20 calls); e1–e3 dequeued.
- **F-NAV-1**: 150 queued → non-empty after 7 ticks; empty after 8; total 150.
- **Order**: recorder over the phase entry point → `nextPosition` writes precede the first `SetDestination`; `GetCurrentVelocity` not called.
- **isStopped before SetDestination**: recorder shows `isStopped = false` then `SetDestination`.
- **Re-request after drain**: drained on tick N, requested again on N → issued on N+1.
- **Empty source**: no committed positions and nothing queued → no agent calls.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_TickDrain_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004
- Unlocks: Story 006
