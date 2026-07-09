# Story 009: Fixed 20Hz Server Tick Loop

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-core.md`
**Requirement**: `TR-net-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 5 — `NetworkManager.ServerTime.Tick` (NGO `NetworkTickSystem`) is the authoritative tick counter for all tick-numbered operations, including `ServerTickNumber`. `NetworkConfig.TickRate` must be configured to 20 to match CR-NET-2's fixed 50ms tick.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: HIGH
**Engine Notes**: `NetworkConfig.TickRate` accepting 20Hz and the exact `NetworkTickSystem` API surface are both listed as ADR-004 Verification Required items — confirm against `docs/engine-reference/unity` before wiring the concrete NGO tick callback. This story's tick-driven/event-driven/connection-driven categorization logic is independent of that binding and can be implemented and tested against a mock tick source first.

**Control Manifest Rules (Foundation layer)**:
- Required: server tick at exactly 20Hz via `NetworkManager.ServerTime.Tick` — source: CR-NET-2, ADR-004 Decision 5
- Required: `deltaTime` for all tick-driven systems is the fixed constant `1.0/TICK_RATE_HZ`, never actual wall-clock elapsed time, even under tick drift — source: CR-NET-2

---

## Acceptance Criteria

*From `design/gdd/networking-core.md`, scoped to this story:*

- [ ] **AC-NC-04** [BLOCKING]: Given a running server with at least one connected player, when the server runs for 10 seconds, then it completes exactly 200 tick iterations (±2 for jitter), verified via `OnTickCompleted` callback count.
- [ ] **AC-NC-05** [BLOCKING]: Given a Warrior entity in `COMBAT_ACTIVE` state, when the server tick advances `_cycleTimer` by `deltaTime` each tick, then a Beat event fires every 20 ticks (1.0s at 20Hz), verified by counting `OnTickCompleted` callbacks between consecutive Beat-resolved events.
- [ ] **AC-TICK-1** [BLOCKING] (EC-NET-10, tick drift): If a tick takes longer than 50ms, `ServerTickNumber` still advances by exactly 1 — the loop never runs multiple ticks to compensate. Tick drift exceeding 25ms average over a 60-second window logs a performance alert (not handled gracefully at runtime — this is a monitoring signal, not a correction mechanism).
- [ ] **AC-TICK-2** [BLOCKING] (generic TTL-timer boundary, AC-NC-06's non-Inventory-dependent portion): Given any tick-registered TTL timer, when the timer's expiry tick is reached, then the timer's release callback fires within one tick boundary (≤50ms) of expiry — proven generically via a mock timer, independent of any specific system's TTL (e.g. the respec-scroll-specific assertion in the original AC-NC-06 is deferred to the Inventory System epic once that GDD exists).

---

## Implementation Notes

*Derived from CR-NET-2:*

- Three execution categories, each a distinct dispatch path from the tick loop:
  - **Tick-driven** (every 50ms): advance all active `_cycleTimer` values; evaluate Beat events; flush one per-tick batch packet per client (Story 007); evaluate active TTL timers; advance tick-registered cooldown/duration counters via registered system delegates.
  - **Event-driven** (fires on condition, results queued into next tick batch): `GoldSyncEvent`, `OnLevelUp`, enhancement outcome (after persistence commit), `AllocateFreePoint`/respec responses, damage/kill notifications.
  - **Connection-driven** (exempt from batch buffering, sent immediately): new connection handshake, reconnection recovery, clean disconnection TTL start, TTL expiry final write+release.
- `deltaTime` is always the fixed constant `1.0/TICK_RATE_HZ` — never `Time.deltaTime` or measured wall-clock delta between ticks. This is the single most load-bearing invariant in the whole tick loop (resolves Auto-Attack GDD OQ-1) — a regression here silently breaks every downstream timing-sensitive system.
- Entity scope: "every entity in `COMBAT_ACTIVE`" includes both player and mob entities (Zone Instancing confirms `MAX_MOBS_PER_ZONE=150`); this story's tick advance loop must not special-case player-only entities.
- Register a generic "tick-registered delegate" mechanism (cooldowns, TTL timers) so downstream systems (Enhancement, Respec, Currency, Skill cooldowns) can hook into the tick loop without this story needing to know about their specific domain logic.

---

## Out of Scope

*Handled by neighbouring stories:*

- The specific respec-scroll TTL business logic — deferred to a future Inventory System epic (this story only proves the generic timer-boundary mechanism)
- OWL/`LastBeatServerTick` update — Story 022 (hooks into this tick loop's Beat-evaluation phase, but is implemented separately)
- Priority-path/batch flush content — Stories 006–007 (this story only calls their `Flush()` once per tick)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/TickLoop_Core_tests.cs`

- **AC-NC-04**: Given a 10-second run, then exactly 200±2 `OnTickCompleted` callbacks fire.
- **AC-NC-05**: Given a `COMBAT_ACTIVE` entity, then Beat fires every 20 ticks.
- **AC-TICK-1**: Given an artificially delayed tick (>50ms), then `ServerTickNumber` still advances by exactly 1, never 2+; given sustained >25ms average drift over a simulated 60s window, a performance alert is logged.
- **AC-TICK-2**: Given a mock TTL timer registered for expiry at tick T, then its release callback fires within tick T's boundary (≤50ms after T).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_Core_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006, Story 007 (tick loop calls their Flush methods), Story 001/002 (test harness)
- Unlocks: Stories 010–029 (nearly everything in this epic runs on this tick loop)
