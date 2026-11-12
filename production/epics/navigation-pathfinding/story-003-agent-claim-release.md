# Story 003: Agent Claim and Release on the Bus

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-5, CR-NAV-6, EC-NAV-2, EC-NAV-3, EC-NAV-4, AC-NAV-04 (151st claim), AC-NAV-05 to 07
**Requirement**: `TR-nav-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decisions 3, 5, 6)
**ADR Decision Summary**: On `MobSpawned`: claim, enable, Warp, set speed, stay stopped (state Registered). On `MobDied`: stop, ResetPath, disable, return to pool, remove from every collection. Pool exhaustion logs an error and the mob gets safe defaults; it never throws.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-003: agent state across `enabled` toggles is unverified on 6.3; Story 009 checks the real agent)
**Engine Notes**: `Warp` returns false when the point is off the mesh; treat as a failed claim.

**Control Manifest Rules (this layer)**:
- Required: `enabled = true` on claim before `Warp`; `false` on release
- Required: pool-exhausted mob → error log, safe defaults (`IsPathStale` true, `GetCurrentVelocity` zero), no exception
- Forbidden: lambda subscriptions on persistent events (subscribe a named method once)

---

## Acceptance Criteria

- [x] The service subscribes named handlers to `MobEventBus.OnMobSpawned` and `OnMobDied` once, at the end of a successful `Initialize` (after the pool fill; a throwing `Initialize` subscribes nothing), and `Teardown` always unsubscribes both (removing a handler that was never added is a no-op on the bus).
- [x] **Claim** (CR-NAV-5, AC-NAV-05): `AvailableCount` drops by 1; the agent is enabled, then `Warp(spawnPosition)` is called before any `SetDestination` or `Tick`; `speed` equals `INavMobSpeedSource` speed exactly; `isStopped == true`; the agent is in `_activeAgents`.
- [x] **Warp fails**: agent disabled and returned to the pool; error logged with `ZoneID`, `EntityID`, position; entity not registered.
- [x] **Unknown mob type** (speed source returns false) **or non-finite speed** (NaN / ±Infinity): error logged with the id and type; no agent is dequeued or touched (the speed lookup runs before the claim); entity not registered (decision recorded here: the GDD says only "callers must null-check").
- [x] **Speed clamp** (CR-NAV-8 range): a finite source speed outside `[0.1f, 20.0f]` is clamped into it and a warning is logged; an in-range speed is set exactly, with no log.
- [x] **EC-NAV-2 / AC-NAV-07**: a second `OnMobSpawned` for a registered `EntityID` logs an error with the id and claims nothing (`AvailableCount` drops by 1 in total; same agent instance).
- [x] **AC-NAV-04 / ADR-003 Decision 5**: the 151st claim throws nothing, logs one error containing "pool exhausted" and the entity id, `_activeAgents.Count` stays 150, and that entity later gets `IsPathStale == true` and `GetCurrentVelocity == Vector3.zero` (assert once Story 004 lands; until then assert it is not registered).
- [x] **EC-NAV-4**: a `MobSpawned` that reaches the handler after `Teardown` logs `"post-teardown MobSpawned"` and claims nothing. Since `Teardown` unsubscribes, this is reachable only through the bus snapshot: `Teardown` runs inside an earlier subscriber's handler during the same raise.
- [x] **Release** (CR-NAV-6, AC-NAV-06): `isStopped = true`, `ResetPath()` once, disabled, back in the pool (`AvailableCount` +1); entity removed from `_activeAgents`, `_latestRequests` (EC-NAV-3) and `_failureCounts`. `_pendingRequests` is a queue and is not searched on release; Story 005's drain skips entries for entities no longer active.
- [x] `OnMobDied` for an unknown entity logs a warning and returns. This includes an entity already released: a handler removed from `MobEventBus` during a raise still receives that raise (Story 001 bus semantics), so a late `OnMobDied` after `Teardown` or a second release must be a no-op (Story 001 carry, 2026-11-12).

---

## Implementation Notes

- **Handlers**: private named methods `HandleMobSpawned(MobSpawnedArgs)` / `HandleMobDied(MobDiedArgs)`. Ignore events for another `ZoneID` with a warning (one zone per process, so it signals a wiring bug).
- **Order of checks in `HandleMobSpawned`** (fixed, 2026-11-12 readiness): zone mismatch (warning) → `!IsAcceptingSpawns` (error, "post-teardown MobSpawned") → entity already active (EC-NAV-2 error) → speed lookup (unknown type or non-finite → error) → clamp (warning if clamped) → pool empty ("pool exhausted" error) → claim. Looking up the speed before the claim means an unknown type never touches an agent; the agent-side order below is unchanged.
- **Order inside claim** (ADR-003 Decision 6 transition "Pool → Registered"): Dequeue → `enabled = true` → `Warp` → `speed` → `isStopped = true` → register. Speed is set directly on the agent here (CR-NAV-5 step 5), not through the `SetSpeed` path (Story 004), but with the same CR-NAV-8 clamp `[0.1f, 20.0f]` (`Mathf.Clamp` passes NaN through, so the finiteness check comes first). Put the two bounds in one place both stories use (constants on `NavigationConfigValues`, not tunable knobs).
- **Story 002 carries (2026-11-12)**: the post-teardown guard reads the service's `internal bool IsAcceptingSpawns` (false before a successful `Initialize` and after `Teardown`). Add the teardown order test with a claimed agent: claim one, `Teardown` → recorder shows [claimed agent `Enabled = false`, collections cleared, `Remove()`]; afterwards `AvailableCount == 0` (Teardown clears the pool, as the Story 002 tests assert), `ActiveCount == 0`, `TrackedStateCount == 0`.
- **Release** removes the entity from `_activeAgents`, `_latestRequests` and `_failureCounts`. Stories 005–006 that add per-entity collections must extend `HandleMobDied` and the release test.
- **Pool**: a `Stack` or `Queue` of `INavAgent` pre-sized at 150; no allocation per claim.

---

## Out of Scope

- Story 004: the `INavigationProvider` methods (the safe defaults for unregistered entities are asserted there)
- Enemy AI epic: raising `OnMobDied` (bridged from Combat's `OnMobDied`)
- Mob Spawning epic: raising `OnMobSpawned`

---

## QA Test Cases

- **Claim**: raise `OnMobSpawned(zone, e1, typeA, p)` → `AvailableCount` 149; fake agent records [enabled=true, Warp(p)], `speed == source speed` (exact float), `isStopped == true`.
- **Warp false**: fake `Warp` returns false → agent `enabled == false`, `AvailableCount` 150, error log contains zone, entity and position.
- **Unknown type**: speed source false → error; `AvailableCount` 150; entity not active; no agent call recorded.
- **Non-finite speed** `[TestCase]` NaN, +Inf, −Inf → error; `AvailableCount` 150; entity not active; no agent call recorded.
- **Out-of-range speed** `[TestCase]` 25 → 20, 0 → 0.1, −3 → 0.1: agent speed clamped, one warning each. Boundaries 0.1 and 20 → set exactly, no warning.
- **Double spawn**: same entity twice → `AvailableCount` 149; one error containing the id; registered agent reference unchanged.
- **151 spawns**: 150 registered; 151st → no exception, exactly one error containing "pool exhausted" and the id, `ActiveCount == 150`, factory call count still 150.
- **Post-teardown spawn (snapshot)**: subscribe a test handler that calls `service.Teardown()` to `OnMobSpawned` *before* `Initialize`; raise `OnMobSpawned` once → the service handler still runs for that raise, logs an error containing "post-teardown MobSpawned", claims nothing (no agent call recorded).
- **Initialize failure subscribes nothing**: invalid instance → `Initialize` throws; raise `OnMobSpawned` → no service log, factory count 0.
- **Teardown with a claimed agent**: claim one, `Teardown` → recorder order [claimed agent `Enabled = false`, collections cleared, `Remove()`]; `AvailableCount == 0`, `ActiveCount == 0`, `TrackedStateCount == 0`.
- **Release**: claim then `OnMobDied` → recorded [isStopped=true, ResetPath, enabled=false]; `ResetPath` count 1; `AvailableCount` back to 150; `TrackedStateCount == 0` and the entity is absent from `_activeAgents`, `_latestRequests`, `_failureCounts` (seed the last two through reflection, as earlier epics did, so the removal is observable).
- **Release unknown**: `OnMobDied(unknown)` → one warning, no throw, counts unchanged.
- **Other zone id**: event for another zone → warning, ignored.
- **Unsubscribe**: after `Teardown` (called outside a raise), raising either event reaches no service handler (no log).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_AgentClaim_tests.cs` — must exist and pass

**Status**: [x] Created and passing (31 cases)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 004

---

## Completion Notes
**Completed**: 2026-11-12
**Criteria**: 10/10 passing (AC-NAV-04's `IsPathStale` / `GetCurrentVelocity` follow-up asserts in Story 004, as the AC allows)
**Deviations** (advisory): exception safety beyond the story — a throwing agent mid-claim is disabled and re-pooled and the exception propagates to the bus; release removes the entity from every collection first and always re-pools the disabled agent. Log wording for open cases chosen in implementation ("Duplicate MobSpawned", "No valid move speed", "clamped to", "Warp failed", "MobDied for unknown or already released"). Speed clamp bounds are `NavigationConfigValues.AGENT_SPEED_MIN` / `AGENT_SPEED_MAX` (plain constants, shared with Story 004).
**Test Evidence**: Logic — `tests/EditMode/Navigation/Navigation_AgentClaim_tests.cs` (22 [Test] + 9 [TestCase], all pass; EditMode 7170 / 7041 / 0 failed / 129 skipped)
**Code Review**: Complete — unity-specialist ISSUES + qa-tester GAPS (no blockers), "fix all 1–11" applied (item 11 → Story 009 carry)
