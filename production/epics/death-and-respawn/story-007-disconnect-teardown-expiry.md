# Story 007: Disconnect, Reconnect, Teardown and Expiry While Dead

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — EC-DR-1, EC-DR-2, EC-DR-3, EC-DR-7, AC-DR-16; `zone-instancing.md` CR-ZI-8 step 6, CR-ZI-12 step 2; `character-persistence.md` CR-CP-3 step 8
**Requirement**: `TR-dr-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-011: Async Persistence and the Tick Loop
**ADR Decision Summary**: Saves are started on the tick and tracked, never awaited; the in-memory state is the source for what is written.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await`, `Task.Wait()`, `.Result` in tick code (ADR-011)

---

## Acceptance Criteria

- [ ] **AC-1 Dead survives disconnect (the slot-state overwrite)**: today a disconnect sets the slot to `Disconnected_SessionActive`, which overwrites `Dead`; the snapshot builder and teardown read "dead" from the slot state. Make `DeathState` the source of truth: `ZoneSnapshotBuilder` and `ZoneCloseSteps` read dead-ness from an injected `IPlayerDeathStateReader` (implemented by the service). Test: P dies, disconnects; `IsDead(P)` is still true and the readers report dead.
- [ ] **AC-2 Timer runs while disconnected (EC-DR-1)**: P dies at T, disconnects at T + 10; on T + delay the full T-3 runs (HP / MP full, position at town point, party Online); messages to P are skipped (Story 005 AC-5).
- [ ] **AC-3 Reconnect snapshot (EC-DR-1)**: P reconnects at T + 50 → P's `EntityState` has `isInDeadState = true` and `respawnTicksRemaining = RespawnTick − currentTick` (90 with delay 140). Reconnect after respawn → `isInDeadState = false`, remaining 0, HP full. `ZonePlayerStateView.RespawnTick` (`uint`) is filled from `DeathState.RespawnTick` (`long`) with a range guard (clamp and log if it does not fit).
- [ ] **AC-4 Teardown (EC-DR-3)**: zone closes with P dead (connected or not) → `SaveSession(..., ZoneClosed, 0f, ...)` (the HP 0 overload) is used for P; an alive player uses the normal save.
- [ ] **AC-5 Expiry and load (AC-DR-16, EC-DR-2, EC-DR-7)**: session TTL expiry while dead writes HP 0; the next `LoadCharacter` of that record yields `CurrentHP == MaxHP` (existing CR-CP-3 step 8 clamp). Integration test against the persistence fakes; the Database-backed variant runs under the `Database` test filter if one exists for this path.
- [ ] **AC-6 Release clears state**: when the slot is released, the player's `DeathState` resets to Alive, so a new occupant of the slot never inherits it.

---

## Implementation Notes

- Readiness: decide whether the slot keeps `Dead` / `Respawning` at all once `DeathState` is authoritative (simplest: keep writing it for anything else that reads it, but no reader decides dead-ness from it).
- Re-check Story 003 AC-6: once `DeathState` is authoritative, a Dead player who disconnected and gets a second `OnEntityDied` should no longer take the ghost path (decide and update that test).
- Readiness: confirm where the session-TTL-expiry save picks its HP (the dead path must pass 0, not the stale in-memory value if that differs).
- Reset `_retryAttempts[slot]` with `DeathState` when the slot is released (Story 004 review).

---

## Out of Scope

- Client re-rendering of the overlay on reconnect (presentation)
- Ghost deaths (Story 003)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1/3**: Die at 1000 (delay 140), disconnect 1010, reconnect 1050 → snapshot entry dead, remaining 90.
- **AC-2**: Die 1000, disconnect 1010, no reconnect → at 1140 HP full, position town point; zero messages addressed to P.
- **AC-4**: Close the zone with P dead and Q alive → P saved via HP 0 overload, Q via normal save.
- **AC-5**: Expiry with P dead → stored HP 0; load → HP == MaxHP.
- **AC-6**: P dies, slot released, new player in same slot → Alive, `IsDead == false`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DisconnectTeardown_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 004
- Unlocks: Story 008
