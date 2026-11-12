# Story 006: Path Health — Partial Timeout, Off-Mesh Recovery, Stuck Re-Route

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-15, F-NAV-2, EC-NAV-8, EC-NAV-10, AC-NAV-17, 19, 23
**Requirement**: `TR-nav-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract; ADR-003: Nav Agent Lifecycle State Machine
**ADR Decision Summary**: Navigation reports stale paths so Enemy AI re-requests; it never makes steering decisions. Off-mesh agents are pulled back with `Warp` to the last committed position; repeated invalid re-routes are only logged.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`isOnNavMesh` drift with `updatePosition = false` is the reason EC-NAV-10 exists; Story 009 tries it on the real agent)
**Engine Notes**: none beyond the above.

**Control Manifest Rules (this layer)**:
- Required: all values from `NavigationConfig`
- Required: main thread, inside phases 1–2 only

---

## Acceptance Criteria

- [ ] **Partial counter** (CR-NAV-15, AC-NAV-17): +1 each `Tick` while `pathStatus == PathPartial` and not stopped; reset to 0 on `PathComplete` and when the drain issues a fresh `SetDestination`; removed on release.
- [ ] **F-NAV-2 / AC-NAV-19**: with T = 20, `IsPathStale` is false at count 19 and true at 20; with T = 19 it fires at 19.
- [ ] **Off-mesh recovery** (EC-NAV-10, AC-NAV-23): the last committed position is stored per agent in `SyncAgentPositions` while the agent is on the mesh; after `NAV_OFF_MESH_RECOVERY_TICKS` (3) consecutive off-mesh ticks the service calls `Warp(lastKnownCommittedPos)` once and logs a warning with `ZoneID` and `EntityID`; no `Warp` after 2 ticks; the count resets when the agent is on the mesh for one tick; per entity.
- [ ] **Stuck re-route** (EC-NAV-8): when the same entity's drained `SetDestination` targets the same `Vector3` while `pathStatus == PathInvalid` for `NAV_STUCK_REROUTE_DETECTION_TICKS` (10) consecutive ticks, log one error with `ZoneID`, `EntityID` and target; no other action.
- [ ] Every new per-entity counter is cleared in `HandleMobDied` (extend Story 003's release test).

---

## Implementation Notes

- **Where counts advance**: the partial and stuck counters advance in `Tick()` (phase 2); the off-mesh count in the phase-1 sync, since that is where `isOnNavMesh` is read with a fresh committed position. Document which phase owns each counter.
- **Agent with no committed position yet** (spawned this tick): use the spawn position as `lastKnownCommittedPos` (set at claim).
- **Stuck detection** compares exact `Vector3` equality on the latest destination (Returning to a fixed spawn point gives the same value). It does not need a separate queue scan: track `(lastTarget, consecutiveInvalidCount)` per agent, updated when a drained request is issued and `pathStatus == PathInvalid` on following ticks. Pick the rule, write it in a comment, and test the boundary at 9 and 10.
- **Log once per streak** for the stuck error (reset after the streak breaks), so a permanent defect does not flood the log.

---

## Out of Scope

- Enemy AI: reacting to `IsPathStale` (re-issuing `SetDestination`)
- Story 007: spawn-point checks at zone load (the authoring fix for EC-NAV-8)

---

## QA Test Cases

- **Partial count**: `PathPartial`, not stopped, 19 `Tick`s → count 19, `IsPathStale` false; 20th → true.
- **Stopped does not count**: `PathPartial` with `isStopped` true → count unchanged after 5 ticks.
- **Reset on complete**: count 10 → status `PathComplete` → next `Tick` count 0.
- **Reset on fresh SetDestination**: count 10 → `SetDestination` drained → count 0.
- **T = 19** (config) → stale at 19.
- **Off-mesh**: on-mesh tick at P → 2 off-mesh ticks → `Warp` count 0 → 3rd → `Warp(P)` once + one warning with zone and entity.
- **Off-mesh reset**: off, off, on, off, off → no `Warp`.
- **Per entity**: e1 off-mesh 3 ticks, e2 on-mesh → only e1 warped.
- **Never committed**: claimed at spawn S, off-mesh 3 ticks before any sync → `Warp(S)`.
- **Stuck**: same target, `PathInvalid`, 9 ticks → no error; 10th → one error with zone, entity, target; 20th → no second error; target changes → streak resets.
- **Release clears counters**: release with non-zero counters, re-claim the same entity id → all counters start at 0.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_PathHealth_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Story 010
