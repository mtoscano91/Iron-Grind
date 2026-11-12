# Story 004: `INavigationProvider` Calls

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 hours (was 3; readiness fixes 2026-11-12 added two sets, Teardown and non-finite cases)

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-7 to CR-NAV-11, CR-NAV-16, EC-NAV-1, EC-NAV-3, AC-NAV-08 to 13, 22, 24
**Requirement**: `TR-nav-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decisions 1, 2, 4, 5); ADR-002 Decision 2
**ADR Decision Summary**: `SetDestination` only queues (latest destination wins); the drain issues the real call. `Stop` keeps the path and drops the queued request; `Resume` clears `isStopped` only. `IsPathStale` is O(1) and false while a path is pending. Unregistered entities get safe defaults. `SetDestination` is async — no same-tick path.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (`pathPending` / `desiredVelocity` timing in Unity 6.3)
**Engine Notes**: return `desiredVelocity`, never `velocity` (smoothed for 60 Hz, lags at 20 Hz). Use XZ only.

**Control Manifest Rules (this layer)**:
- Required: `INavigationProvider` methods are called in phase 3 only (callers' rule; document it on the interface)
- Required: `Resume()` clears `isStopped` and uses the preserved path without queuing
- Required: pool-exhausted / unregistered mob → `IsPathStale = true`, `GetCurrentVelocity = Vector3.zero`

---

## Acceptance Criteria

- [x] **Provider wiring**: `ZoneNavigationService` implements `INavigationProvider`; the `GetCurrentVelocity` doc no longer says "wired in Story 005".
- [x] **SetDestination** (CR-NAV-7, AC-NAV-08): two calls before a `Tick` leave one `_latestRequests` entry holding the second destination and one queue entry (`_queuedEntities` decides "already queued", O(1)); the agent's `SetDestination` is never called inside the method; the entity is added to `_destinationRequested`; unregistered entity → one warning, nothing queued; non-finite target → one warning, nothing queued, `_latestRequests` unchanged.
- [x] **SetSpeed** (CR-NAV-8, AC-NAV-09): clamps to [`AGENT_SPEED_MIN`, `AGENT_SPEED_MAX`] = [0.1, 20.0] and logs one warning when clamping; 5.0 and both exact bounds are set as is with no warning; non-finite speed (NaN, ±∞) → one warning, agent speed unchanged; unregistered → one warning.
- [x] **Stop** (CR-NAV-9, AC-NAV-10, AC-NAV-11 state part, EC-NAV-1): `isStopped = true`, no `ResetPath`, entity removed from `_latestRequests`; after `SetDestination` then `Stop`, `_latestRequests` lacks the entity and the (stub) `Tick` makes no agent call. The post-drain half of AC-NAV-11 is asserted in Story 005 ("Skips", stopped e1).
- [x] **Resume** (CR-NAV-16, AC-NAV-24): `isStopped = false`; no `SetDestination`; `_latestRequests`, the queue and `_queuedEntities` unchanged; `GetCurrentVelocity` then returns the preserved path's velocity; unregistered → one warning, no throw.
- [x] **IsPathStale** (CR-NAV-10, AC-NAV-12): every row of the GDD table, parametrised; unregistered → true and one warning; O(1) (property reads only, no agent method call).
- [x] **GetCurrentVelocity** (CR-NAV-11, AC-NAV-13): returns `(desiredVelocity.x, 0, desiredVelocity.z)`; unregistered → zero and one warning; stopped → no `isStopped` branch in the method (the real agent zeroes `desiredVelocity`; see note). `velocity` cannot be read: `INavAgent` exposes only `DesiredVelocity`.
- [x] **AC-NAV-04 follow-up (Story 003 carry)**: the 151st entity (pool exhausted) gets `IsPathStale == true` and `GetCurrentVelocity == Vector3.zero`, one warning each, no throw.
- [x] **AC-NAV-22 / EC-NAV-3 (state part)**: `SetDestination` then `MobDied` before `Tick` → `_latestRequests` and `_destinationRequested` lack the entity, the agent is back in the pool, and the (stub) `Tick` makes no agent call. The post-drain half is asserted in Story 005 ("Skips", released e2).
- [x] **After Teardown**: all six methods return safe defaults (`IsPathStale` true, velocity zero, the other four do nothing) with no warning and no throw; `TrackedStateCount == 0`.

---

## Implementation Notes

- **"Never set" tracking**: `IsPathStale` returns true if `SetDestination` was never called for the agent (GDD row: "SetDestination never called for this agent"). Decision: a `HashSet<EntityID> _destinationRequested`, added to in `SetDestination` after the registered and finiteness checks (not in the drain). Release and `Teardown` clear the entity / the set. Extend Story 003's release test to assert it.
- **"Already queued" tracking**: `_pendingRequests` is a `Queue<EntityID>`, so `Contains` is O(n), and `_latestRequests` cannot stand in (Stop removes the entry while the queue slot stays). Add `HashSet<EntityID> _queuedEntities`: `SetDestination` enqueues and adds only when the entity is not in it. Story 005's drain removes on dequeue. Release does not touch it (the stale slot is skipped by the drain; a respawned id with the same value reuses the slot with its new latest destination). `Teardown` clears it.
- **Pre-size and count**: both sets are pre-sized to the pool in the constructor like the dictionaries, and `TrackedStateCount` includes both.
- **Row precedence** in `IsPathStale`: evaluate the GDD's true conditions for not-registered and off-mesh first, then `pathPending` → false, then `PathInvalid` / `isPathStale` / never-set → true, then the partial count (counter from Story 006; until then treat partial as fresh). Write the order in a comment; the parametrised test fixes it.
- **Stopped velocity**: with the real agent `isStopped` makes `desiredVelocity` zero. Do not add a manual `isStopped ? zero` branch unless Story 009 shows the real agent does not zero it; record the outcome there.
- **SetSpeed**: finiteness check first (`Mathf.Clamp` passes NaN through), then clamp with `NavigationConfigValues.AGENT_SPEED_MIN` / `AGENT_SPEED_MAX` (Story 003's constants; no new literals).
- **Unregistered warnings**: one warning per call, as CR-NAV-7 to 11 and 16 say. Accepted consequence: a pool-exhausted mob polled by Enemy AI logs about 3 warnings per tick; the exhaustion itself is already an error (Story 003). Revisit (warn once per entity) only if playtest logs show it as a problem.
- **After Teardown**: each method checks `_tornDown` first and returns its safe default silently (matches the existing `GetCurrentVelocity`).
- **Interface doc**: the phase-3 rule and the async contract (ADR-002 Decision 2) are already in `INavigationProvider.cs`; keep them. Update the service's `GetCurrentVelocity` doc.
- **Performance**: all six methods are O(1) and allocate nothing on the success path; warning strings are built only on failure paths. Called every tick by Enemy AI for up to 150 mobs.

---

## Out of Scope

- Story 005: the drain that issues the queued requests and removes entities from `_queuedEntities` on dequeue (tests here assert queue / dictionary contents directly; the post-drain halves of AC-NAV-11 and AC-NAV-22 are Story 005's "Skips" test)
- Story 006: the partial-path counter rows of `IsPathStale` beyond "counter below timeout"
- Enemy AI epic: when to call these methods

---

## QA Test Cases

- **Dedup**: `SetDestination(e, A)`, `SetDestination(e, B)` → `_latestRequests[e] == B`, queue count 1, agent `SetDestination` count 0.
- **Re-request after Stop**: `SetDestination(e, A)`, `Stop(e)`, `SetDestination(e, B)` → queue count still 1, `_latestRequests[e] == B`.
- **SetDestination unregistered** → one warning; nothing queued.
- **SetDestination non-finite** (NaN, +∞ in any component, `[TestCase]`) → one warning; nothing queued; `_destinationRequested` lacks e.
- **SetSpeed** 0.0 → 0.1 (warning); 25.0 → 20.0 (warning); 5.0 → 5.0 (no warning); 0.1 and 20.0 exactly → no warning; NaN, +∞, −∞ → one warning, speed unchanged.
- **SetSpeed unregistered** → one warning.
- **Stop**: `isStopped` true; `ResetPath` count 0; `_latestRequests` lacks e.
- **Stop after queued SetDestination** → `_latestRequests` lacks e; after the stub `Tick`, agent `SetDestination` count 0, `isStopped` still true.
- **Resume**: after `Stop`, `Resume` → `isStopped` false; `SetDestination` count 0; queue count unchanged; velocity = fake's `desiredVelocity` (XZ).
- **Resume unregistered** → one warning; no throw.
- **IsPathStale table** (one case per row, fresh service each): not registered → true (warning); off-mesh → true; `PathInvalid` → true; `isPathStale` → true; never set → true; `pathPending` → false; `PathComplete` → false; `PathPartial` with counter below timeout → false. Precedence cases: off-mesh + `pathPending` → true; `pathPending` + `PathInvalid` → false; `pathPending` + never set → false.
- **Velocity**: `desiredVelocity (3, 5, -2)` → `(3, 0, -2)`; unregistered → zero (warning).
- **Pool exhausted (151st)**: `IsPathStale` true, `GetCurrentVelocity` zero, one warning each, no throw.
- **MobDied with queued request** → `_latestRequests` and `_destinationRequested` lack e; agent back in the pool; stub `Tick` makes no call on the released agent.
- **After Teardown**: each of the six methods (`[TestCase]` or one test per method) → safe default, no warning (`LogAssert.NoUnexpectedReceived`), no throw; `TrackedStateCount == 0`.
- **Allocation**: none required as an automated test; reviewed in `/code-review`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_Provider_tests.cs` — must exist and pass

**Status**: [x] Created and passing (32 [Test] + 20 [TestCase] = 52 cases)

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 005

---

## Completion Notes
**Completed**: 2026-11-12
**Criteria**: 11/11 passing (post-drain halves of AC-NAV-11 / AC-NAV-22 asserted in Story 005 "Skips" and "Respawn with a stale slot", as the ACs allow)
**Deviations** (advisory): stopped velocity relies on the real agent zeroing `desiredVelocity` (no `isStopped` branch; verified in Story 009); one warning per unregistered call accepted (a pool-exhausted mob logs ~3 per tick); release keeps the `_queuedEntities` slot, the Story 005 drain removes it on dequeue (pinned by tests). Code-review fixes: single lookup via `TryGetAgent`, `Stop` docs ("a request not yet drained is discarded") in `INavigationProvider.cs` and the service, `IsPathStale` async-window comment, shared fakes in `tests/EditMode/Navigation/NavigationTestFakes.cs` (Story 003 tests use them; Lifecycle tests keep their own, different fakes).
**Test Evidence**: Logic: `tests/EditMode/Navigation/Navigation_Provider_tests.cs` (32 [Test] + 20 [TestCase] = 52 cases). EditMode 7222 / 7093 passed / 0 failed / 129 skipped.
**Code Review**: Complete (unity-specialist ISSUES, qa-tester GAPS, no blockers; "fix all" applied). Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
