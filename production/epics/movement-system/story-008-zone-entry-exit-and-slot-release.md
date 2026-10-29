# Story 008: Zone Entry, Exit and Slot Release

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/movement-system.md` — EC-MOV-13, 14, 15, 18; CR-MOV-5 (auto-run never persisted — client); AC-MOV-27. `design/gdd/zone-instancing.md` — entry sequence, snapshot (CR-ZI-8 step 6)
**Requirement**: `TR-mov-007`

**ADR Governing Implementation**: ADR-009 (spawn position: `GetTownRespawnPoint(ZoneID)` before client rendering; snapshot carries it); ADR-010 (direct calls / listener interfaces)
**ADR Decision Summary**: a player enters at the zone's town respawn point unless a saved position applies; Zone Instancing assembles `ZoneStateSnapshot` through `IZoneEntityStateSource`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: none new.

**Control Manifest Rules (this layer)**:
- Required: zone state mutated on the main thread only (ADR-011)
- Required: slot cleanup runs before the slot is released (the Status Effects 008 `BeforeRelease` contract)

---

## Acceptance Criteria

- [ ] On player entry (a Zone Instancing hook — chosen at readiness from the existing entry completion path), Movement registers the slot: position = the entry position (town respawn point, `TownRespawnPointCache`; a saved position only if Character Persistence stores one — **readiness checks**; ADR-006's schema has no position column today), facing = a non-zero default (`Vector3.forward` ⇒ 0°) — **EC-MOV-18**, Prohibited cleared — **EC-MOV-13**, intent buffer empty, override empty, stall counter 0.
- [ ] **AC-MOV-27 / EC-MOV-13**: on zone exit (the slot's release), every buffered intent for that entity is discarded; no position delta is applied from a message that arrived before the exit, in this zone or after re-entry.
- [ ] **EC-MOV-14**: a pending auto-face override is cleared on exit; nothing carries into a new registration of the same slot.
- [ ] **Slot release hook — decided at readiness**: `ZoneSlotPool.SetReleaseListener` is set-once and Status Effects holds it (Status Effects Story 008/009). Options: (a) a composite `IZoneSlotReleaseListener` that fans out in a fixed order (Status Effects, then Movement), owned by the composition; (b) Movement registers through a second hook. Recommendation: (a), with a test that both listeners run and one throwing does not skip the other.
- [ ] **Snapshot position source**: `IZoneEntityStateSource.TryGetPlayerState` returns the Movement committed position and facing for the player (the real source replaces the logging stand-in for these fields only; the other fields stay with their owners). **Readiness checks** how `ZonePlayerStateView` is filled and whether a composite source is needed.
- [ ] **EC-MOV-15**: a reconnect within the TTL registers the slot fresh from the snapshot path; the server keeps no client-side cached position (the client snap is Story 012/013's concern).

---

## Implementation Notes

- Reuse the Status Effects 008 test approach (`StatusEffects_ZoneDeparture_tests.cs`): real `ZoneSlotPool`, real release paths.
- Mob slots: Movement does not register mobs (Navigation moves them); the release hook ignores mob slots.

---

## Out of Scope

- Story 009: composition wiring and broadcast
- Saved-position persistence (not in ADR-006 today) — a later change if wanted

---

## QA Test Cases

- **Entry**: Given a zone whose town respawn point is (12, 0, −4) — When a player enters — Then `TryGetPosition` = (12, 0, −4), facing 0, not Prohibited, no intent.
- **AC-MOV-27**: intent buffered for tick N, the player exits before tick N runs → tick N applies nothing; the player re-enters the same slot → tick N+1 with no new message applies nothing.
- **EC-MOV-14**: override injected, exit before the tick → after re-entry, the first tick uses the client intent.
- **Prohibited cleared on entry**: Prohibited set, exit, re-enter → not Prohibited.
- **Composite listener (if chosen)**: release calls Status Effects then Movement; Status Effects' listener throws → Movement still cleans up, one error logged.
- **Snapshot**: `ZoneSnapshotBuilder` for a moved player returns the committed position in wire units (×100).
- **Mob slot release**: Movement state untouched.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_ZoneEntryExit_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 005, 006
- Unlocks: Story 009
