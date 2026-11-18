# Story 003: Ghost Death Branch

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 2–3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-Ghost, T-4, Event 5; `networking-ghost-session.md` CR-GH-6
**Requirement**: `TR-dr-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: The ghost hand-off is a direct call from the `OnEntityDied` handler into a narrow `IGhostDeathSink`; no new event. The production sink (save, release, session end) is Story 010.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await` in the handler; the session end and its persistence write are started and tracked per ADR-011 by the sink (Story 010), not awaited here

---

## Acceptance Criteria

- [x] **AC-1 First check**: the `OnEntityDied` handler checks ghost status before anything else (before duplicate suppression), through a narrow `IGhostStatusSource.IsGhost(EntityID)`. The production adapter `ZoneSlotGhostStatusSource` is built in this story: true when `ZoneSlotPool.TryGetPlayer(id)` finds the slot and its state is `ZoneSessionState.Disconnected_SessionActive`; false for an unknown id or a mob.
- [x] **AC-2 Ghost path (AC-DR-10)**: for a ghost, the handler calls `IGhostDeathSink.OnGhostDied(entity)` exactly once and returns; `DeathState.CurrentState` stays Alive; no respawn tick is set. The handler does not release the slot, persist, or change session state — the sink owns all of that (Story 010).
- [x] **AC-3 Nothing else (CR-DR-Ghost)**: for a ghost, no `SetCombatProhibited`, no movement prohibition, no party sink call, no `DeathStateEntered`, no `EntityDied` (AC-DR-11; Story 005 adds the wire assertion against the outbox).
- [x] **AC-4 Non-ghost unchanged**: a non-ghost death still follows Story 002 exactly. The adapter is tested against a real `ZoneSlotPool`: Connected → false, Disconnected_SessionActive → true, unknown id → false, mob → false.
- [x] **AC-5 Sink failure**: if `OnGhostDied` throws, the error is logged with the entity id and the handler returns without entering Dead; no exception escapes the handler.
- [x] **AC-6 Dead, then disconnected**: P dies (Dead), then disconnects (slot `Disconnected_SessionActive`), then a second `OnEntityDied(P)` arrives: the ghost path is taken (sink called once, `DeathState` unchanged). Accepted behaviour until Story 007 makes `DeathState` the source of truth; Story 007 re-checks this case.

---

## Implementation Notes

- Decided at readiness (2026-11-18): `GhostEntityTracker` is not wired in production (no `PromoteToGhost` callers); the production ghost signal is the slot's `ZoneSessionState.Disconnected_SessionActive`. The Networking Core ghost methods (`GhostCleanupSequencer.CompleteGhostDeathCleanup`, `ConnectionStateMachine.CompleteGhostDeathFromDisconnected`, `HandleGhostDeathWhileReconnecting`) are not called here: production session ends run through `SessionEndCoordinator` (ADR-011 mutation gate + release chain), which gets its ghost-death path in Story 010.
- `IGhostStatusSource`, `IGhostDeathSink` and `ZoneSlotGhostStatusSource` live in `src/ServerLogic/DeathAndRespawn/`. The service constructor takes both seams (null → `ArgumentNullException`, like the other seams).
- `ZoneSessionEnded(GhostDeath = 3)` is delivered on reconnect by the ghost session (CR-GH-6), not sent here.

---

## Out of Scope

- Story 010: the production `IGhostDeathSink` — save (snapshot HP, respawn position, `wasKilledWhileDisconnected`), release chain, `Disconnected_SessionExpired` "GhostDeath", `GhostExpiredEvent`, mob de-targeting
- Story 008: wiring `ZoneSlotGhostStatusSource` and the Story 010 sink into `ZoneServerComposition`
- Story 005: the outbox-level "no `EntityDied`" assertion
- Ghost session reconnect delivery (Networking Core, already built)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-18.*

- **AC-1**: Given P is a ghost and (via `SetStateForTests`) already Dead, When `OnEntityDied(P)`, Then the sink is called (ghost check precedes dedup).
- **AC-2**: Given P is a ghost, When `OnEntityDied(P)`, Then sink fake called once with P, `IsDead(P) == false`, `TryGetDeathState(P)` Alive with no respawn tick.
- **AC-3**: Same setup; combat, movement and party fakes record zero calls.
- **AC-4**: Given P is not a ghost, When `OnEntityDied(P)`, Then the Story 002 path runs and the sink records zero calls. Adapter cases against a real `ZoneSlotPool` (Connected, Disconnected_SessionActive, unknown, mob).
- **AC-5**: Sink fake throws; `LogAssert.Expect` an error naming P; P not Dead; no exception escapes.
- **AC-6**: P dies, slot set to `Disconnected_SessionActive`, second `OnEntityDied(P)` → sink called once; `DeathTick` / `RespawnTick` unchanged.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostDeath_tests.cs` — must exist and pass

**Status**: [x] Created — 16 tests, pass (2026-11-18)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Stories 005, 010

---

## Completion Notes
**Completed**: 2026-11-18
**Criteria**: 6/6 passing (all automated)
**Deviations**: None blocking. Advisory: ghost deaths are not deduplicated, so a repeat death calls the sink again — `IGhostDeathSink` documents that sinks must be idempotent (Story 010). AC-6 (dead, then disconnected, takes the ghost path) is the accepted gap until Story 007, which has a re-check note. Three over-long lines from Story 002 were wrapped in `DeathAndRespawnService.cs`; Story 002's tests were updated only for the two new constructor arguments.
**Test Evidence**: Logic — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostDeath_tests.cs` 16/16; filter `DeathAndRespawn` 73/73; full EditMode `-testCategory "!Database"` 7907/7907
**Code Review**: Complete (`/code-review` with unity-specialist + qa-tester; all required changes and suggestions applied)
