# Story 003: Ghost Death Branch

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 2–3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-Ghost, T-4, Event 5; `networking-ghost-session.md` CR-GH-6
**Requirement**: `TR-dr-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: The ghost hand-off is a direct call into the existing ghost-session code from the `OnEntityDied` handler; no new event.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await` in the handler; a persistence write is started and tracked per ADR-011 by the existing ghost cleanup, not awaited here

---

## Acceptance Criteria

- [ ] **AC-1 First check**: the `OnEntityDied` handler checks ghost status before anything else (before duplicate suppression), through a narrow `IGhostStatusSource.IsGhost(EntityID)` backed in production by `GhostEntityTracker.IsGhost` (EntityID → CharacterID mapping from the zone slot).
- [ ] **AC-2 Ghost path (AC-DR-10)**: for a ghost, the existing ghost-death cleanup runs (`GhostCleanupSequencer.CompleteGhostDeathCleanup` / the connection state machine path that records `GhostDeath = 3` for reconnect delivery) and the zone slot is released; `DeathState.CurrentState` stays Alive; no respawn tick is set.
- [ ] **AC-3 Nothing else (CR-DR-Ghost)**: for a ghost, no `SetCombatProhibited`, no movement prohibition, no party sink call, no `DeathStateEntered`, no `EntityDied` (AC-DR-11; Story 005 adds the wire assertion against the outbox).
- [ ] **AC-4 Non-ghost unchanged**: a non-ghost death still follows Story 002 exactly.
- [ ] **AC-5 Cleanup failure**: if the ghost cleanup call throws, the error is logged with the entity id and the handler returns without entering Dead.

---

## Implementation Notes

- Readiness must confirm which existing entry point is the right one for a ghost dying in a live zone (`CompleteGhostDeathCleanup` takes persistence and broadcast callbacks; `HandleGhostDeathWhileReconnecting` is for the Reconnecting race). Wire whichever the Networking Core stories built for this case; do not duplicate their persistence logic.
- `ZoneSessionEnded(GhostDeath = 3)` is delivered on reconnect by the ghost session (CR-GH-6), not sent here.

---

## Out of Scope

- Story 005: the outbox-level "no `EntityDied`" assertion
- Ghost session reconnect delivery (Networking Core, already built)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-2**: Given P is a ghost, When `OnEntityDied(P)`, Then cleanup fake called once with P's character, slot release called, `IsDead(P) == false`.
- **AC-3**: Same setup; combat, movement and party fakes record zero calls.
- **AC-1**: Given P is a ghost and (artificially) already Dead, When `OnEntityDied(P)`, Then the ghost path still runs (ghost check precedes dedup).
- **AC-5**: Cleanup fake throws; error logged naming P; P not Dead; no exception escapes the handler.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostDeath_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 005
