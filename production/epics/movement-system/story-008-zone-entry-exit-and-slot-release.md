# Story 008: Zone Entry, Exit and Slot Release

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (5 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/movement-system.md` — EC-MOV-13, 14, 15, 18; CR-MOV-5 (auto-run never persisted — client); AC-MOV-27. `design/gdd/zone-instancing.md` — entry sequence, snapshot (CR-ZI-8 step 6)
**Requirement**: `TR-mov-007`

**ADR Governing Implementation**: ADR-009 (spawn position: `GetTownRespawnPoint(ZoneID)` before client rendering; snapshot carries it); ADR-010 (direct calls / listener interfaces)
**ADR Decision Summary**: a player enters at the zone's town respawn point; Zone Instancing assembles `ZoneStateSnapshot` through `IZoneEntityStateSource`.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: none new.

**Control Manifest Rules (this layer)**:
- Required: zone state mutated on the main thread only (ADR-011)
- Required: slot cleanup runs before the slot is released (the Status Effects 008 `BeforeRelease` contract)

---

## Acceptance Criteria

- [ ] **Entry hook** (readiness decision 2026-10-29): new `IZonePlayerEntryListener` in `src/ServerLogic/ZoneInstancing/` with `void OnPlayerEntering(byte slot, EntityID id)`, and a set-once `ZoneEntryCompletion.SetEntryListener(IZonePlayerEntryListener)` (null → `ArgumentNullException`; second call → `InvalidOperationException`, same pattern as `ZoneSlotPool.SetReleaseListener`). `OnPlayerAdmitted` calls it after the `TryGetPlayer` check and **before** step 6 `SendSnapshot`. Without a listener the completion behaves as today. An exception from the listener is caught by the existing try (logged, remaining steps skipped — the current contract).
- [ ] **Entry registration** (`MovementService` implements `IZonePlayerEntryListener`, using the `slot` argument directly): position = `ITownRespawnPointProvider.GetTownRespawnPoint(ownZone)` — always; ADR-006 stores no position, so there is no saved position — facing = 0 (`Vector3.forward`) — **EC-MOV-18**, Prohibited cleared — **EC-MOV-13**, buffered intent discarded, override empty, stall counter 0, reported/overflow/speed-failing flags cleared, no last valid speed. A slot index outside `[0, SLOT_COUNT)` → one error, nothing registered. A slot already occupied → one warning, overwritten. A respawn point that fails `MovementPositionGuard.IsEncodable` → one error, registered anyway.
- [ ] **AC-MOV-27 / EC-MOV-13**: on zone exit (the slot's release), the buffered intent for that entity is discarded (`TryTakeIntent(slot, out _)`; no new buffer API); no position delta is applied from a message that arrived before the exit, in this zone or after re-entry.
- [ ] **EC-MOV-14**: a pending auto-face override is cleared on exit; nothing carries into a new registration of the same slot.
- [ ] **Release** (`MovementService` implements `IZoneSlotReleaseListener`): `BeforeRelease(id)` maps the id through `IEntitySlotIndex`; an unknown id, an index ≥ `SLOT_COUNT` (mob ids map to the OWL index ≥ 50) or a slot not occupied → no change, no log. Otherwise: discard intent, clear override, Prohibited, stall counter, reported/overflow/speed-failing flags and last valid speed, then `occupied = false`. Never throws.
- [ ] **Composite release listener** (readiness decision: option (a)): new `CompositeZoneSlotReleaseListener(params IZoneSlotReleaseListener[] listeners)` in `src/ServerLogic/ZoneInstancing/`; null array or null element → `ArgumentNullException`. `BeforeRelease` calls each listener in array order; an exception from one is caught and logged as one error and the next still runs. Composition (replacing `Pool.SetReleaseListener(statusEffects)` with the composite of Status Effects then Movement) is Story 009.
- [ ] **Snapshot position source** (readiness decision): new `MovementZoneEntityStateSource(IZoneEntityStateSource inner)` decorator with set-once `AttachMovement(MovementService)`. `TryGetPlayerState` returns `inner`'s result; when `inner` returns true and Movement holds the player, it overwrites `PosX/Y/Z` (×100, rounded), `FacingAngle` (stored ×10) and `RotX..W` (yaw quaternion of the facing, ×32,767); all other fields are `inner`'s. Movement not attached or not holding the player → `inner`'s view unchanged. `TryGetMobState` passes through. Allocation-free. While `inner` is `LoggingZoneEntityStateSource` (always false), production snapshots are unchanged until the other sources exist.
- [ ] **EC-MOV-15** (server side): a ghost reconnect within the TTL resumes the existing slot (CR-NET-6) — no release, no re-registration — so the committed position survives and the snapshot (decorator above) carries it; the server keeps no client-side cached position (the client snap is Story 012/013's concern). A re-entry after release registers fresh at the respawn point. The ghost-resume path is a stand-in today, so this AC is verified by the re-entry and snapshot tests only.

---

## Implementation Notes

- Reuse the Status Effects 008 test approach (`StatusEffects_ZoneDeparture_tests.cs`): real `ZoneSlotPool`, real release paths.
- Mob slots: Movement does not register mobs (Navigation moves them); the release hook ignores them through the slot range check.
- `MovementService` needs the own `ZoneID` and an `ITownRespawnPointProvider` for registration: add a new 8-arg ctor (the 6-arg one + `ZoneID ownZone`, `ITownRespawnPointProvider respawn`; invalid zone → `ArgumentException`, null → `ArgumentNullException`). The 4- and 6-arg ctors stay (existing tests compile) and leave the provider null; `OnPlayerEntering` on a service without a provider logs one error and registers nothing.
- Yaw quaternion from facing: `Quaternion.Euler(0, facing / 10f, 0)`, each component ×32,767 rounded to `short`; reuse an existing `EntityState` encoding helper if one exists.

---

## Out of Scope

- Story 009: composition wiring (entry listener, composite release listener, state-source decorator) and broadcast
- Saved-position persistence (not in ADR-006 today) — a later change if wanted
- The ghost-resume path itself (Networking Ghost Session)

---

## QA Test Cases

- **Entry**: Given a zone whose town respawn point is (12, 0, −4) — When a player enters — Then `TryGetPosition` = (12, 0, −4), facing 0, not Prohibited, no intent.
- **Entry before snapshot**: `ZoneEntryCompletion` with the listener set → the listener runs before `SendSnapshot`; the snapshot built through the decorator (fake `inner` returning true) carries (1200, 0, −400).
- **Entry listener set-once**: second `SetEntryListener` throws; null throws; no listener → entry steps unchanged.
- **AC-MOV-27**: intent buffered for tick N, the player exits before tick N runs → tick N applies nothing; the player re-enters the same slot → tick N+1 with no new message applies nothing.
- **EC-MOV-14**: override injected, exit before the tick → after re-entry, the first tick uses the client intent.
- **Prohibited cleared on entry**: Prohibited set, exit, re-enter → not Prohibited.
- **Stall state cleared**: stall counter at threshold − 1 and reported flag set, exit, re-enter → a miss counts from 0 again.
- **Composite listener**: release calls Status Effects then Movement; Status Effects' listener throws → Movement still cleans up, one error logged.
- **Snapshot**: `ZoneSnapshotBuilder` for a moved player returns the committed position in wire units (×100) and the facing ×10; `inner` false → false; Movement not attached → `inner`'s view.
- **Mob slot release**: Movement state untouched.
- **Double registration**: occupied slot registered again → one warning, new position.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_ZoneEntryExit_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 005, 006 (Complete); Story 007 (Complete — stall flags reset on release/entry)
- Unlocks: Story 009
