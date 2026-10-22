# Story 001: Zone Lifecycle State Machine (CR-ZI-2, T-1 to T-7)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-2, States and Transitions (T-1 to T-7), Tuning Knobs (`ZONE_IDLE_CLOSE_SECONDS`, `ZONE_DRAIN_GRACE_SECONDS`, `ZONE_LOAD_TIMEOUT_SECONDS`); `networking-session.md` ST-NET-2 (occupancy rules that drive it)
**Requirement**: `TR-zi-002` *(epic placeholder — not yet in `tr-registry.yaml`)*

**ADR Governing Implementation**: ADR-009: Scene / Zone-Load Management, Amendment 1 (S9 `zone.State = Active`; teardown) + dated note 2026-10-22
**ADR Decision Summary**: one zone process hosts one zone instance; the zone reaches Active at S9 and closes through the teardown sequence; a quit request starts T-4 (drain), a second one is T-6.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#, no engine API)
**Engine Notes**: none — plain C# in `IronGrind.ServerLogic`; timers count ticks, never wall-clock time.

**Control Manifest Rules (this layer)**:
- Required: server state machines live in `IronGrind.ServerLogic` — ADR-012
- Required: broadcast events are `event Action<T>` with `readonly struct` T, named `On...` — ADR-010
- Forbidden: lambda captures for persistent subscriptions — ADR-010
- Guardrail: tick work is O(1) per tick (two counters); no allocation per tick

---

## Acceptance Criteria

*From GDD `design/gdd/zone-instancing.md`, scoped to this story:*

- [ ] **States**: Loading → Active → Draining → Closed (terminal). `ZoneState.Empty` in `src/Foundation/Networking/ZoneState.cs` is renamed `Loading` (same value 0; not on the wire or persisted). Draining never returns to Active; Closed never leaves.
- [ ] **T-2 / T-3**: `MarkActive` moves Loading → Active; an init failure (bounds, missing data) or `ZONE_LOAD_TIMEOUT_SECONDS × TICK_RATE_HZ` ticks in Loading moves Loading → Closed.
- [ ] **T-7 (AC-ZI-21)**: with `ZONE_IDLE_CLOSE_SECONDS = 60` (test config), when the last occupied player slot is released and the tick counter advances `60 × TICK_RATE_HZ` ticks with no slot allocated, Active → Closed fires exactly once at that tick; a slot allocated one tick before expiry cancels it and the zone stays Active; a zone that reaches Active with no slot allocated closes after the same tick count.
- [ ] **T-4**: a quit request in Active moves Active → Draining, cancels the idle timer and starts a `ZONE_DRAIN_GRACE_SECONDS × TICK_RATE_HZ` grace countdown; the transition is observable (event or callback) so Story 014 can broadcast `ZoneSessionEnded`.
- [ ] **T-5**: Draining → Closed on the tick the occupied player slot count reaches 0, or on the tick the grace expires, whichever comes first.
- [ ] **AC-ZI-25**: a quit request on an Active zone with no occupied player slot fires T-4 and T-5 on the same tick.
- [ ] **T-6 (AC-ZI-24, AC-ZI-26 state side)**: a fatal request, `EntityIDCounterExhausted`, a second quit request while Draining, or a quit request while Loading moves the zone to Closed on that tick.
- [ ] **One close**: every path into Closed raises exactly one close notification carrying the transition (T-3, T-5, T-6, T-7) — the teardown (Story 012) runs once.
- [ ] **Join gate**: the state machine answers "accepts players" true only in Active.
- [ ] The old population-driven behaviour is gone: no population Draining, no `Draining → Active`, no `ReinitializeFromClosed`, no immediate `Active → Closed` on explicit disconnect. The existing tests that asserted it (`Session_ZoneStateMachine_Capacity_tests.cs`, `GhostSession_Cleanup_Crash_Dismissal_tests.cs`, `ZoneTestConfigurator`) are rewritten to the CR-ZI-2 behaviour, not deleted.

---

## Implementation Notes

*Derived from ADR-009 A1 and the epic's Known Gap 3:*

- Replaces `src/ServerLogic/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (Networking Core Story 014). It is not wired into the composition yet (only tests and the test harness call it), so no running behaviour changes until Story 014.
- One zone per process (ADR-007, ADR-009): the new machine tracks one zone, not a dictionary of zone instances.
- The machine is driven by three inputs: `OnTick(serverTick)`, occupancy changes (occupied player slot count, from Story 002's pool), and requests (`MarkActive`, `MarkInitFailed`, `RequestQuit`, `RequestFatal`).
- Knobs as `public const` defaults passed through constructor parameters (Networking Core Story 040 pattern), so tests set 60 s and production reads the GDD default.
- The per-template `loadingInProgress` flag and the routing disable at T-4 are gateway-owned (epic gap 1) — not here.

**Open decisions for `/story-readiness`**

1. Rewrite `ZoneSessionStateMachine` in place or add `src/ServerLogic/ZoneInstancing/ZoneLifecycle.cs` and delete the old class (and move its test-harness callers).
2. The old class's capacity check (`EvaluateJoinAttempt`) moves to Story 002 / 009 — confirm nothing else calls it.

---

## Out of Scope

- Story 002: slot pools (the occupancy count this story consumes)
- Story 012: what the teardown does
- Story 014: wiring `OnWantsToQuit`, the tick and `IZoneStateOwner` to this machine; the `ZoneSessionEnded` broadcast at T-4

---

## QA Test Cases

- **AC-ZI-21 idle close** — Given Active, 1 slot, idle 60 s; When the slot is released and 1200 ticks advance; Then Closed (T-7) exactly once at tick 1200. Edge: allocate at tick 1199 → stays Active; Active with no slot from T-2 → Closed at 1200.
- **T-3 load timeout** — Given Loading, timeout 30 s; When 600 ticks advance; Then Closed (T-3). Edge: `MarkActive` at tick 599 → Active.
- **T-4 → T-5 by release** — Given Active with 2 slots; When quit, then both released at tick +40; Then Draining at the quit tick, Closed (T-5) at +40, not at grace expiry.
- **T-5 by grace** — Given Draining with 1 slot, grace 60 s; When 1200 ticks pass; Then Closed (T-5) at tick 1200.
- **AC-ZI-25** — Given Active, 0 slots; When quit; Then T-4 and T-5 on the same tick.
- **AC-ZI-24 / 26** — second quit while Draining → Closed (T-6) that tick; quit while Loading → Closed (T-6), never Active.
- **One close** — every path into Closed raises the close notification once; further requests after Closed are no-ops.
- **No return** — occupancy rising during Draining never re-enters Active.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_Lifecycle_tests.cs` — must exist and pass; the rewritten Networking Core tests pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 012, 013, 014
