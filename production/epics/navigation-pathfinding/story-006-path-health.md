# Story 006: Path Health — Partial Timeout, Off-Mesh Recovery, Stuck Re-Route

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4–5 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-15, F-NAV-2, EC-NAV-8, EC-NAV-10, AC-NAV-17, 19, 23
**Requirement**: `TR-nav-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract; ADR-003: Nav Agent Lifecycle State Machine
**ADR Decision Summary**: Navigation reports stale paths so Enemy AI re-requests; it never makes steering decisions. Off-mesh agents are pulled back with `Warp` to the last committed position; repeated invalid re-routes are only logged.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`isOnNavMesh` drift with `updatePosition = false` is the reason EC-NAV-10 exists; Story 009 tries it on the real agent)
**Engine Notes**: a real `NavMeshAgent.SetDestination` to a point off the mesh usually returns **false** rather than producing `PathInvalid`, so the stuck rule counts both (see AC). How `Warp` treats the current path on a real agent is verified in Story 009 PlayMode.

**Control Manifest Rules (this layer)**:
- Required: all values from `NavigationConfig`
- Required: main thread, inside phases 1–2 only

---

## Acceptance Criteria

- [x] **Health pass in `Tick()`** (readiness fix 1–2): `Tick()` runs a health pass over `_activeAgents` **before** the drain. The pass advances the partial, off-mesh and stuck counters and calls `Warp`. It writes only to the counter dictionaries, never to `_activeAgents` while iterating it (Mono `Dictionary` throws on modification during enumeration). No counter advances while `PathPending` is true. `SyncAgentPositions` never warps (CR-NAV-12 unchanged); it only stores `lastKnownCommittedPos` for registered, on-mesh agents.
- [x] **Partial counter** (CR-NAV-15, AC-NAV-17): +1 per health pass while `pathStatus == PathPartial`, not stopped and not pending. Reset to 0 when the status is `PathComplete`, and when the drain issues `SetDestination` for the entity (any issued call, accepted or rejected). Because the pass runs before the drain, an agent issued this tick ends the tick at 0. Removed on release.
- [x] **F-NAV-2 / AC-NAV-19**: `IsPathStale` gains the row "partial and count ≥ `PARTIAL_PATH_TIMEOUT_TICKS` → true" after the existing PathInvalid / own-stale / never-set row (CR-NAV-10 precedence comment updated). With T = 20, false at count 19 and true at 20; with T = 19 it fires at 19.
- [x] **Off-mesh recovery** (EC-NAV-10, AC-NAV-23): `lastKnownCommittedPos` is set to the spawn position at claim and updated in sync while on the mesh. The health pass counts consecutive off-mesh ticks per entity. At `NAV_OFF_MESH_RECOVERY_TICKS` (3) it calls `Warp(lastKnownCommittedPos)` and resets the count to 0, so a still-off-mesh agent retries every N ticks. There is no `Warp` after 2 ticks, and one on-mesh tick resets the count. The first `Warp` of a streak logs a warning with `ZoneID` and `EntityID`; retries in the same streak don't log. `Warp` returning false logs an error (zone, entity, position). If `Warp` throws, the exception propagates out of `Tick` (same as the drain).
- [x] **Stuck re-route** (EC-NAV-8): track `(lastIssuedTarget, streak)` per entity. A health-pass tick extends the streak when the last issued target is unchanged and the agent is not pending and either its last issued `SetDestination` was rejected or `pathStatus == PathInvalid`. A drained request to a different `Vector3` (exact equality), or a valid non-pending path, resets the streak. At `NAV_STUCK_REROUTE_DETECTION_TICKS` (10) it logs **one error** per streak with `ZoneID`, `EntityID` and the target; no other action. (EC-NAV-8 says error; the tuning-knob row and TR text say "warning" — GDD fix later.)
- [x] **Rejected-`SetDestination` rate limit** (Story 005 carry): `_failureCounts` holds consecutive rejected drain calls per entity. The rejection warning is logged only on the 0 → 1 transition; an accepted call resets the count to 0 (entry removed or zero, documented).
- [x] **State bookkeeping**: the new per-entity collections (partial count, off-mesh count, last committed position, stuck target + streak, and any warned-this-streak flags) are pre-sized to the pool, counted in `TrackedStateCount`, cleared in `Teardown`, and removed in `HandleMobDied`. The AgentClaim release test's expected `TrackedStateCount` values are updated to the new totals. Tests read counters through internal accessors.
- [x] **Performance**: the health pass is O(active agents) per tick and allocates nothing on the success path (struct enumerator over the concrete `Dictionary`); strings are built only on log paths.

---

## Implementation Notes

- **Phase ownership**: phase 1 (`SyncAgentPositions`) only records `lastKnownCommittedPos`. Phase 2 (`Tick`) = health pass, then drain. Write this in the `Tick` doc comment.
- **Never-committed agent**: `lastKnownCommittedPos` is set at claim to `args.SpawnPosition`, so an agent that drifts off before any sync warps to its spawn point.
- **Health pass iteration**: iterate `_activeAgents` (concrete `Dictionary`, struct enumerator); read the agent once per property per entity; write to the counter dictionaries only.
- **Drain hook** (Story 005 `TryIssue` comment): on an issued call, reset the partial count, record the issued target for the stuck rule (resetting the streak if it changed), record accepted / rejected, and update `_failureCounts` + rate-limited warning. Replace the Story 006 hook comment.
- **Stuck rule comment**: write the rule (target unchanged + not pending + rejected-or-PathInvalid) in a comment and test the boundary at 9 and 10.
- **Log once per streak** for the stuck error and the off-mesh warning, so a permanent defect does not flood the log.

---

## Out of Scope

- Enemy AI: reacting to `IsPathStale` (re-issuing `SetDestination`)
- Story 007: spawn-point checks at zone load (the authoring fix for EC-NAV-8)
- Story 009: real-agent behaviour of `Warp` and `isOnNavMesh` drift (PlayMode)

---

## QA Test Cases

- **Partial count**: `PathPartial`, not stopped, 19 `Tick`s → count 19, `IsPathStale` false; 20th → true.
- **Stopped does not count**: `PathPartial` with `isStopped` true → count unchanged after 5 ticks.
- **Pending does not count**: `PathPartial` + `PathPending` true → count unchanged.
- **Reset on complete**: count 10 → status `PathComplete` → next `Tick` count 0.
- **Reset on fresh SetDestination**: count 10 → `SetDestination` drained in the same `Tick` → count 0 at end of tick (pass-before-drain order).
- **Reset on rejected SetDestination**: count 10 → drained call returns false → count 0.
- **T = 19** (config) → stale at 19.
- **Off-mesh**: on-mesh sync at P → 2 off-mesh ticks → `Warp` count 0 → 3rd → `Warp(P)` once + one warning with zone and entity.
- **Off-mesh reset**: off, off, on, off, off → no `Warp`.
- **Off-mesh retry**: 6 off-mesh ticks → `Warp` twice, one warning.
- **Warp false**: 3rd off-mesh tick, `Warp` returns false → one error with zone, entity, position.
- **Warp throws**: exception propagates out of `Tick`.
- **Per entity**: e1 off-mesh 3 ticks, e2 on-mesh → only e1 warped.
- **Never committed**: claimed at spawn S, off-mesh 3 ticks before any sync → `Warp(S)`.
- **Sync never warps**: sync with an off-mesh agent → `Warp` count 0, `lastKnownCommittedPos` unchanged.
- **Stuck (invalid)**: same target, `PathInvalid`, 9 ticks → no error; 10th → one error with zone, entity, target; 20th → no second error; target changes → streak resets.
- **Stuck (rejected)**: same target, every drained call returns false, 10 ticks → one error.
- **Rejected warning rate limit**: 5 consecutive rejections → one warning; accepted call, then rejection → second warning.
- **Release clears counters**: release with non-zero counters, re-claim the same entity id → all counters start at 0; `TrackedStateCount` 0 after release of the only agent.
- **Teardown clears counters**: `TrackedStateCount` 0 after `Teardown`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_PathHealth_tests.cs` — must exist and pass

**Status**: [x] Created — 45 [Test], all pass (EditMode 7292 / 7163 / 0 failed / 129 skipped, 2026-11-12 17:58:45Z)

---

## Dependencies

- Depends on: Story 005 (Complete)
- Unlocks: Story 010

---

## Readiness Notes (2026-11-12)

`/story-readiness` NEEDS WORK → fixes 1–9 applied: (1) off-mesh counting + `Warp` moved from sync to a `Tick` health pass, sync only records `lastKnownCommittedPos`; (2) health pass before drain, nothing counts while pending; (3) stuck rule settled (rejected or PathInvalid, same target, one error per streak); (4) Story 005 carry: `_failureCounts` rate-limits the rejection warning; (5) off-mesh retry every N ticks, one warning per streak, false `Warp` → error, throw propagates; (6) partial reset on any issued call; (7) bookkeeping (`TrackedStateCount`, Teardown, release, internal accessors); (8) performance AC; (9) estimate 2–3 → 4–5 h. GDD fixes for the next pass: F-NAV-2 table "default 60", EC-NAV-8 error vs knob-row "warning", CR-NAV-14 skip-count wording (from Story 005).

---

## Completion Notes
**Completed**: 2026-11-12
**Criteria**: 8/8 passing (performance AC verified by code review; no automated allocation test)
**Deviations** (advisory):
- Code review added two rules not in the original AC text: (1) a stopped agent does not count toward the stuck streak, and `Stop` clears the stored target, streak and logged flag; (2) after a successful recovery `Warp`, the entity leaves `_destinationRequested` and its partial / stuck counters reset, so `IsPathStale` is true and Enemy AI re-requests. Also: failed-`Warp` error once per off-mesh streak; exact `Vector3.Equals` target match.
- Rule (2) assumes a real `NavMeshAgent.Warp` clears the path — carried to Story 009 (five real-agent checks in its Implementation Notes).
- ADR-003 gained an implementation note: phase 2 owns the recovery `Warp`.
- GDD wording for the next pass: F-NAV-2 table "default 60"; EC-NAV-8 error vs tuning-knob row "warning"; CR-NAV-14 skip-count wording (from Story 005).
**Test Evidence**: Logic — `tests/EditMode/Navigation/Navigation_PathHealth_tests.cs` (45 [Test]); AgentClaim release test TrackedStateCount 10/5.
**Code Review**: Complete — unity-specialist ISSUES + qa-tester GAPS, no blockers → "fix all 1–9" applied. Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
**Tech debt**: none.
