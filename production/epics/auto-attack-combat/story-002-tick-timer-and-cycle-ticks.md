# Story 002: Tick Timer and CycleTicks

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rules 5–7, F-1, AC-01 to AC-03, EC-11 to EC-18
**Requirement**: `TR-aa-002`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: game systems that tick run in step 3 of `ZoneTickPipeline`, phase 3, in an order the composition root lists.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: a core system that ticks runs in step 3 of `ZoneTickPipeline`
- Forbidden: per-tick allocation on the server hot path
- Guardrail: gameplay values stay data-driven; the 0.5 floor is a named constant or config value, not a magic number

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md`, scoped to this story:*

- [ ] `CycleTicks = ceil((1.0 / max(AttackSpeedMultiplier, 0.5)) × TICK_RATE_HZ)` (F-1).
- [ ] Multiplier 1.0 → ready every 20 ticks over 10 cycles (AC-01).
- [ ] Multiplier 2.0 → ready every 10 ticks over 5 cycles (AC-02).
- [ ] Multiplier 0.0, negative or below 0.5 → 40 ticks, no exception and no error log (AC-03, EC-12 to EC-14).
- [ ] Multiplier 1.3 → 16 ticks; a fractional cycle never fires early (F-1).
- [ ] The timer advances by exactly 1 per tick in every non-`IDLE` state and never reads a clock (Rules 5, 7, EC-16, EC-18, EC-23).
- [ ] A multiplier change mid-cycle does not adjust the timer and takes effect from the next cycle (EC-11, EC-15).

---

## Implementation Notes

- Story 001 already implements `IZoneSimulationStep` and advances the timer by 1 per `Run`. This story adds `CycleTicks` and the ready check to `Run`. Composition wiring is Story 006.
- `AttackSpeedMultiplier` is read through a small injected stat interface that `CharacterStats.GetEffectiveStatFloat` can back. Not cached between cycles.
- EC-15 requires the value to be sampled when a cycle starts and held for that cycle. Sample on activation and on every timer reset.
- Performance: runs inside the 50 ms server tick (ADR-014). `Run` is O(MaxPlayers): one array pass, one integer compare per active slot, and at most one stat read per player per cycle start. It allocates nothing (allocation test below). No separate frame budget.
- The ready check only reports here. This story exposes a seam that a test can count; Story 003 replaces it with resolution and owns the reset after a fire.

---

## Out of Scope

- Story 003: Rule 10 resolution, the reset after a fire
- Story 006: registering the step in the composition
- Story 008: broadcasting the timer

---

## QA Test Cases

- **AC-01**: multiplier 1.0, run 200 ticks → 10 ready events, each delta exactly 20.
- **AC-02**: multiplier 2.0, run 50 ticks → 5 ready events, each delta exactly 10.
- **AC-03**: `[TestCase]` 0.0, -1.0, 0.25, 0.5 → 40 ticks; `LogAssert.NoUnexpectedReceived`.
- **F-1 rounding**: `[TestCase]` 1.3 → 16; 1.0 → 20; 2.0 → 10; 0.5 → 40. Boundary values are the point, so inline numbers are allowed.
- **Rule 7**: timer advances in every non-`IDLE` state available at this story.
- **EC-15**: change the multiplier at tick 5 of a 20-tick cycle → this cycle stays 20, the next uses the new value.
- **Allocation**: `Run` for 50 players inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_Timer_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 003
