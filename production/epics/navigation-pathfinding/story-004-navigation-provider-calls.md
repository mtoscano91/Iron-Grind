# Story 004: `INavigationProvider` Calls

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-11-06
> **Estimate**: 3 hours

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

- [ ] **SetDestination** (CR-NAV-7, AC-NAV-08): two calls before a `Tick` leave one `_latestRequests` entry holding the second destination and one queue entry; the agent's `SetDestination` is never called inside the method; unregistered entity → warning, nothing queued.
- [ ] **SetSpeed** (CR-NAV-8, AC-NAV-09): clamps to [0.1, 20.0] and logs a warning when clamping; 5.0 is set as is; unregistered → warning.
- [ ] **Stop** (CR-NAV-9, AC-NAV-10, 11, EC-NAV-1): `isStopped = true`, no `ResetPath`, entity removed from `_latestRequests`; a `SetDestination` queued before `Stop` is not issued by the next `Tick` and the agent stays stopped.
- [ ] **Resume** (CR-NAV-16, AC-NAV-24): `isStopped = false`; no `SetDestination`; `_latestRequests` and the queue unchanged; `GetCurrentVelocity` then returns the preserved path's velocity; unregistered → warning, no throw.
- [ ] **IsPathStale** (CR-NAV-10, AC-NAV-12): every row of the GDD table, parametrised; unregistered → true; O(1) (no agent method call besides property reads).
- [ ] **GetCurrentVelocity** (CR-NAV-11, AC-NAV-13): returns `(desiredVelocity.x, 0, desiredVelocity.z)`; never reads `velocity`; unregistered → zero; stopped → zero (whatever the fake's `desiredVelocity`, rely on the real agent for the stopped case and assert the method does not special-case it — see note).
- [ ] **AC-NAV-22 / EC-NAV-3**: `SetDestination` then `OnMobDied` before `Tick` → `_latestRequests` lacks the entity; the next `Tick` makes no agent call for it.

---

## Implementation Notes

- **"Never set" tracking**: `IsPathStale` must return true if `SetDestination` was never called for the agent. Keep a per-agent flag set when the drain issues the first real `SetDestination` (or when `SetDestination` is called — pick one and say so; the GDD row reads "SetDestination never called for this agent", so set it in `SetDestination`). Clear it on release (extend Story 003's release test).
- **Row precedence** in `IsPathStale`: evaluate the GDD's true conditions for not-registered and off-mesh first, then `pathPending` → false, then `PathInvalid` / `isPathStale` / never-set → true, then the partial count (counter from Story 006; until then treat partial as fresh). Write the order in a comment; the parametrised test fixes it.
- **Stopped velocity**: with the real agent `isStopped` makes `desiredVelocity` zero. Do not add a manual `isStopped ? zero` branch unless Story 009 shows the real agent does not zero it; record the outcome there.
- The interface doc comment states the phase-3 rule and the async contract (ADR-002 Decision 2).

---

## Out of Scope

- Story 005: the drain that issues the queued requests (tests here may call a minimal drain from Story 005 if it lands first; otherwise assert queue contents directly)
- Story 006: the partial-path counter rows of `IsPathStale` beyond "counter below timeout"
- Enemy AI epic: when to call these methods

---

## QA Test Cases

- **Dedup**: `SetDestination(e, A)`, `SetDestination(e, B)` → `_latestRequests[e] == B`, queue count 1, agent `SetDestination` count 0.
- **SetDestination unregistered** → one warning; nothing queued.
- **SetSpeed** 0.0 → 0.1 (warning); 25.0 → 20.0 (warning); 5.0 → 5.0 (no warning); 0.1 and 20.0 exactly → no warning.
- **Stop**: `isStopped` true; `ResetPath` count 0; `_latestRequests` lacks e.
- **Stop after queued SetDestination** → after `Tick`, agent `SetDestination` count 0, `isStopped` still true.
- **Resume**: after `Stop`, `Resume` → `isStopped` false; `SetDestination` count 0; queue count unchanged; velocity = fake's `desiredVelocity` (XZ).
- **Resume unregistered** → one warning; no throw.
- **IsPathStale table** (one case per row, fresh service each): not registered → true; off-mesh → true; `PathInvalid` → true; `isPathStale` → true; never set → true; `pathPending` → false; `PathComplete` → false; `PathPartial` with counter below timeout → false.
- **Velocity**: `desiredVelocity (3, 5, -2)` → `(3, 0, -2)`; fake throws if `velocity` is read; unregistered → zero.
- **MobDied with queued request** → `_latestRequests` lacks e; `Tick` makes no call on the released agent; agent back in the pool.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Navigation/Navigation_Provider_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Story 005
