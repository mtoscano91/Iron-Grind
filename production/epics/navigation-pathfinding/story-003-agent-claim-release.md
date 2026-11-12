# Story 003: Agent Claim and Release on the Bus

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
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

- [ ] The service subscribes named handlers to `MobEventBus.OnMobSpawned` and `OnMobDied` once, and unsubscribes in `Teardown`.
- [ ] **Claim** (CR-NAV-5, AC-NAV-05): `AvailableCount` drops by 1; the agent is enabled, then `Warp(spawnPosition)` is called before any `SetDestination` or `Tick`; `speed` equals `INavMobSpeedSource` speed exactly; `isStopped == true`; the agent is in `_activeAgents`.
- [ ] **Warp fails**: agent disabled and returned to the pool; error logged with `ZoneID`, `EntityID`, position; entity not registered.
- [ ] **Unknown mob type** (speed source returns false): error logged; agent returned; entity not registered (decision recorded here: the GDD says only "callers must null-check").
- [ ] **EC-NAV-2 / AC-NAV-07**: a second `OnMobSpawned` for a registered `EntityID` logs an error with the id and claims nothing (`AvailableCount` drops by 1 in total; same agent instance).
- [ ] **AC-NAV-04 / ADR-003 Decision 5**: the 151st claim throws nothing, logs one error containing "pool exhausted" and the entity id, `_activeAgents.Count` stays 150, and that entity later gets `IsPathStale == true` and `GetCurrentVelocity == Vector3.zero` (assert once Story 004 lands; until then assert it is not registered).
- [ ] **EC-NAV-4**: `OnMobSpawned` after `Teardown` logs `"post-teardown MobSpawned"` and claims nothing.
- [ ] **Release** (CR-NAV-6, AC-NAV-06): `isStopped = true`, `ResetPath()` once, disabled, back in the pool (`AvailableCount` +1); entity removed from `_activeAgents`, `_latestRequests` (EC-NAV-3), the partial-path and off-mesh counters.
- [ ] `OnMobDied` for an unknown entity logs a warning and returns. This includes an entity already released: a handler removed from `MobEventBus` during a raise still receives that raise (Story 001 bus semantics), so a late `OnMobDied` after `Teardown` or a second release must be a no-op (Story 001 carry, 2026-11-12).

---

## Implementation Notes

- **Handlers**: private named methods `HandleMobSpawned(MobSpawnedArgs)` / `HandleMobDied(MobDiedArgs)`. Ignore events for another `ZoneID` with a warning (one zone per process, so it signals a wiring bug).
- **Order inside claim** (ADR-003 Decision 6 transition "Pool → Registered"): Dequeue → `enabled = true` → `Warp` → `speed` → `isStopped = true` → register. Speed is set directly on the agent here (CR-NAV-5 step 5), not through the clamped `SetSpeed` path — but clamp it the same way if the source returns an out-of-range value, and log (consistent with CR-NAV-8).
- **Story 002 carries (2026-11-12)**: the post-teardown guard reads the service's `internal bool IsAcceptingSpawns` (false before a successful `Initialize` and after `Teardown`). Add the teardown order test with a claimed agent: claim one, `Teardown` → recorder shows [claimed agent `Enabled = false`, collections cleared, `Remove()`], `AvailableCount` back to 150.
- **Release** removes the entity from every per-entity collection the service owns. Stories 005–006 add counters; each of them must extend `HandleMobDied` and the release test.
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
- **Unknown type**: speed source false → error; `AvailableCount` 150; entity not active.
- **Out-of-range speed** from the source (25) → agent speed 20, warning logged.
- **Double spawn**: same entity twice → `AvailableCount` 149; one error containing the id; registered agent reference unchanged.
- **151 spawns**: 150 registered; 151st → no exception, exactly one error containing "pool exhausted" and the id, `ActiveCount == 150`, factory call count still 150.
- **Post-teardown spawn**: after `Teardown`, raise `OnMobSpawned` → error contains "post-teardown MobSpawned"; nothing claimed.
- **Release**: claim then `OnMobDied` → recorded [isStopped=true, ResetPath, enabled=false]; `ResetPath` count 1; `AvailableCount` back to 150; entity absent from every collection (assert through internal accessors or reflection, as earlier epics did).
- **Release unknown**: `OnMobDied(unknown)` → one warning, no throw, counts unchanged.
- **Other zone id**: event for another zone → warning, ignored.
- **Unsubscribe**: after `Teardown`, raising either event reaches no service handler (no log).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_AgentClaim_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 004
