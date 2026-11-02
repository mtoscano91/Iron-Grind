# Story 001: Combat State and Activation

> **Epic**: Auto-Attack Combat
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/auto-attack-combat.md` — Rules 1–4, 23, States and Transitions, AC-12 to AC-15, EC-1 to EC-4, EC-9, EC-27 to EC-30
**Requirement**: `TR-aa-001` (placeholder; `tr-registry.yaml` has no entries for this GDD)

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary; ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: the system lives in `IronGrind.ServerLogic` from its first story; game logic runs on the tick, and request-driven state changes apply before the simulation step.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none. Plain C#.

**Control Manifest Rules (this layer)**:
- Required: new systems are written in `src/ServerLogic/` from their first story
- Required: dependency injection over singletons
- Forbidden: per-tick allocation on the server hot path

---

## Acceptance Criteria

*From GDD `design/gdd/auto-attack-combat.md`, scoped to this story:*

- [ ] Toggle ON with no target, or a target with the toggle OFF, stays `IDLE`; each condition alone prevents `COMBAT_ACTIVE` (AC-12, EC-1).
- [ ] While `IDLE`, the timer does not advance (AC-13).
- [ ] Toggle OFF freezes the timer; toggle ON resets it to 0 (AC-14, Rule 2, EC-27 to EC-30).
- [ ] Any new target selection resets the timer to 0, including re-selecting the same target (AC-15, Rule 3).
- [ ] Target loss, deselect or toggle OFF enters `IDLE` and freezes the timer (Rules 4, 23, EC-2, EC-9).
- [ ] Deselect plus re-select, or toggle OFF plus ON, in one tick is one fresh activation (EC-3, EC-4).
- [ ] A target is valid only when it is a living mob in the same zone instance; a player is never valid (Rule 1).
- [ ] While not `IDLE`, the timer advances by exactly 1 per `Run`; `Run` re-checks target validity first, so a target lost before that tick does not advance the timer.
- [ ] Only an OFF→ON change is an activation. Toggle ON while already ON, or OFF while already OFF, changes nothing.
- [ ] Target loss and deselect clear Combat's target and leave the toggle unchanged, so the next valid selection activates. Toggle OFF keeps the target.
- [ ] A selection with the toggle OFF sets the timer to 0 and stays `IDLE` (Rule 3).
- [ ] Selecting a target Combat rejects clears Combat's target and enters `IDLE` with the timer frozen; `TargetSlotTracker` is not touched.
- [ ] `ReleasePlayer` clears the slot's state, so a new occupant starts `IDLE`, timer 0, toggle OFF. Any call for an entity with no player slot does nothing and does not throw.

---

## Implementation Notes

- New folder `src/ServerLogic/AutoAttackCombat/`, namespace `IronGrind.AutoAttackCombat`.
- State lives in arrays of `pool.MaxPlayers` entries, indexed by the slot from `ZoneSlotPool.TryGetPlayerSlot`. The constructor takes `ZoneSlotPool` and `IAutoAttackTargetValidity`.
- `_actionTimer` is an `int` tick count. The system implements `IZoneSimulationStep`; `Run` ignores `deltaTime`. Each tick, for each occupied slot not in `IDLE`: re-check validity (no valid target → clear it, `IDLE`, no advance), then advance by 1. Story 002 adds `CycleTicks` and the ready check.
- `IAutoAttackTargetValidity.IsValidTarget(EntityID)` is the injected check, so tests use a fake and Enemy AI supplies the real mob answer. Dead-entity rejection lands here (`death-and-respawn.md` CR-DR-16). Polling replaces a "target invalidated" call: it also covers a mob killed by another player and EC-10.
- Public entry points for this story: `SetTarget(attacker, target)` (`EntityID.Invalid` = deselect), `SetAutoAttack(attacker, bool)`, `ReleasePlayer(attacker)`, read state, read timer. Story 006 connects them to the wire and reaches `ReleasePlayer` through `IZoneSlotReleaseListener`, which must not throw.
- `OUT_OF_RANGE` and `PROHIBITED` are added by Story 003.

---

## Out of Scope

- Story 002: `CycleTicks` and the ready check
- Story 003: resolution, `OUT_OF_RANGE`, `PROHIBITED`
- Story 006: `SetTarget` and `SetAutoAttack` wiring, the composition step, the release listener

---

## QA Test Cases

- **AC-12**: toggle ON, no target → `IDLE`. Target, toggle OFF → `IDLE`. Both → `COMBAT_ACTIVE`.
- **AC-13**: `IDLE` with a non-zero frozen timer; 100 ticks → same value.
- **AC-14**: timer 12 ticks, toggle OFF, 60 ticks → 12; toggle ON → 0.
- **AC-15**: timer 12, new target → 0; timer 12, same target → 0.
- **Deactivation**: each of target loss, deselect, toggle OFF → `IDLE`, timer frozen at its value.
- **Same-tick pairs**: EC-3 and EC-4 → `COMBAT_ACTIVE`, timer 0.
- **Validity**: a fake rejecting a player id, a dead mob and a mob in another zone → selection leaves `IDLE`.
- **Advance**: `COMBAT_ACTIVE`, 12 `Run` calls → timer 12.
- **Target lost by polling**: fake turns invalid at timer 12 → next `Run` gives `IDLE`, timer 12, target cleared.
- **Repeat toggle**: ON while ON at timer 12 → still 12; OFF while OFF → no change.
- **After target loss**: toggle stays ON; a new valid selection → `COMBAT_ACTIVE`, timer 0.
- **Toggle OFF keeps target**: toggle OFF, then ON → `COMBAT_ACTIVE`, timer 0.
- **Selection while OFF**: frozen timer 12, select → timer 0, `IDLE`.
- **Rejected selection in combat**: `COMBAT_ACTIVE` at timer 12, select a rejected id → `IDLE`, timer 12, target cleared.
- **Release**: `ReleasePlayer`, new occupant of the slot → `IDLE`, timer 0, toggle OFF.
- **No slot**: every entry point with an unknown id → no throw, no state change.
- **Isolation**: two players' states do not affect each other.
- **Allocation**: toggling, targeting and `Run` for `MaxPlayers` inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_Activation_tests.cs` — must exist and pass

**Status**: [x] Created and passing

---

## Dependencies

- Depends on: None
- Unlocks: Story 002

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 13/13 passing
**Deviations**: Advisory only. TR-aa-001 is not in `tr-registry.yaml`, so the GDD text was checked directly. `Run` does not check slot occupancy; a released slot stays clean only if `ReleasePlayer` is reached from `BeforeRelease`, which Story 006 must wire and test. Toggle ON with no target leaves a frozen timer unchanged; the next selection resets it (test locks this). The Rule 1 cases prove the injected validity check is honoured; the real mob answer is Enemy AI's. Extra file touched: Story 006 (implementation note and Zone exit QA case, from code review).
**Test Evidence**: Logic: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_Activation_tests.cs` (34 cases); EditMode 5584 / 5455 passed / 0 failed / 129 skipped
**Code Review**: Complete (CHANGES REQUIRED, all applied)
