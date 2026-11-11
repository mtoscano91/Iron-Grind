# Quick Design Spec: Production zone entity state source (TD-100)

**Type**: Addition
**System**: Zone Instancing (zone process)
**GDD Reference**: `design/gdd/zone-instancing.md` — CR-ZI-8 steps 6 (snapshot) and 9 (`PlayerJoinedZone`); `design/gdd/networking-wire-protocol.md` — `EntityState`
**Date**: 2026-11-11
**Estimated Implementation**: ~5 h

## Change Summary

Adds the production `IZoneEntityStateSource`: a new `ZoneEntityStateSource` that reads each player and mob field from the system that owns it. The zone composition uses it instead of `LoggingZoneEntityStateSource`, still wrapped in `MovementZoneEntityStateSource` for position.

## Motivation

The seam was left as a stand-in by Zone Instancing Story 014. The stand-in returns false for every entity, so in production:
- `ZoneSnapshotBuilder` skips every player and every mob ("has no state data"), so a joining client sees an empty zone;
- `ZoneEntryCompletion` never sends `PlayerJoinedZone`, so other clients never see the newcomer;
- the `EntityState.characterName` half of AC-AUTH-21 (e) cannot pass (TD-100, found at Gateway Story 012).

## Design Delta

Current code (`ZoneServerComposition`, ~line 1703):

> `new MovementZoneEntityStateSource(_owner.EntityStateSourceOverride ?? new LoggingZoneEntityStateSource())`

This spec changes the default inner source to `ZoneEntityStateSource` (rules below). `EntityStateSourceOverride` stays for tests.

`EntityState.isInCombat` has a wire field but no definition anywhere; rule 6 defines it (user decision 2026-11-11).

## New Rules / Values

**Players** (`TryGetPlayerState(EntityID)`):
1. The `EntityID` must be an occupied player slot in `ZoneSlotPool`, which gives its `CharacterID`. Otherwise return false (the builder skips the entry with its existing warning).
2. `CurrentHp` / `CurrentMp` = Character Stats `GetCurrentHP` / `GetCurrentMP` (float) rounded down to `int` (`Mathf.FloorToInt`; no float in authoritative wire state, CR-NET-7.2). `MaxHp`, `MaxMp`, `Level` = `GetEffectiveStat(entity, StatID.MaxHP / MaxMP / Level)`.
3. `CharacterName` = `ICharacterNameLookup.TryGetCharacterName(CharacterID)`. When it returns false (record not applied yet, released), return false: a player is never sent without a name. The returned string is the cached session string (no allocation — Zone Instancing Story 007 rule).
4. `EquipmentAppearanceFlags` = `EquipmentService.GetAppearanceFlags(CharacterID)`.
5. `CycleTimer` = `CycleTimerBroadcastStep.NormalizeCycleTimer(GetActionTimer(entity), GetCycleTicks(entity))` from `AutoAttackCombatSystem` — the same value `CycleTimerBroadcast` sends for that tick.
6. `IsInCombat` = the entity's `AutoAttackCombatSystem.GetState` is `CombatActive` or `OutOfRange` (engaged with a target); false for `Idle` and `Prohibited`. *(User decision 2026-11-11.)*
7. `RespawnTick` = 0. Death & Respawn does not exist yet; the builder only derives `respawnTicksRemaining` for `Dead` / `Respawning` slots, which nothing sets today. Death & Respawn takes this field over when it is built.
8. Position, rotation and facing are left zero here; `MovementZoneEntityStateSource` overwrites them (unchanged behaviour).

**Mobs** (`TryGetMobState(EntityID)`):
9. The `EntityID` must be a mob in `ZoneMobRoster` (`TryGetMob`); otherwise return false. `CurrentHp` / `MaxHp` per rule 2, `IsInCombat` per rule 6, `CycleTimer` per rule 5; position per rule 8.

**Both**:
10. Main thread only. No allocation per call. Each dependency is injected through the constructor (no singletons); a missing system at construction is an `ArgumentNullException`, not a silent default.

No new tuning knobs.

## Affected Systems

| System | Impact | Action Required |
|--------|--------|-----------------|
| Zone Instancing (composition) | Stand-in replaced | New `ZoneEntityStateSource`; composition default changed |
| `zone-instancing.md` / `networking-wire-protocol.md` | `isInCombat` undefined | Add rule 6 to the `EntityState` notes (separate approval) |
| `docs/tech-debt-register.md` TD-100 | Row understated the gap | Widened 2026-11-11 (mobs, `PlayerJoinedZone`); close when the story ships |
| Gateway Story 012 composition test | Name half of AC-AUTH-21 (e) open | Add the `EntityState.characterName == "Brakka"` assertion |
| Death & Respawn (future) | Owns `RespawnTick` | Replace rule 7 when built |

## Acceptance Criteria

- [ ] An entered player "Brakka" with level 7, 80 / 100 HP and 30 / 40 MP appears in the joining client's snapshot with exactly those values, and other clients receive `PlayerJoinedZone` with name "Brakka", level 7 and the player's position.
- [ ] A player slot whose name lookup returns false is skipped (source returns false; one builder warning); no entry with an empty name is sent.
- [ ] `IsInCombat` is true for `CombatActive` and `OutOfRange`, false for `Idle` and `Prohibited` (one case each, players and mobs).
- [ ] Current HP 79.6 is sent as 79; `CycleTimer` equals `NormalizeCycleTimer(GetActionTimer, GetCycleTicks)` for the same tick.
- [ ] A mob in the roster appears in the snapshot with its current and max HP; an `EntityID` that is neither an occupied player slot nor a roster mob returns false.
- [ ] The composition no longer constructs `LoggingZoneEntityStateSource` (the stand-in log line no longer appears in a composition test); the Gateway Story 012 composition handshake test also asserts `EntityState.characterName == "Brakka"`, closing AC-AUTH-21 (e).
- [ ] No regression: existing `ZoneSnapshotBuilder`, `ZoneEntryCompletion` and Movement state-source tests pass; `TryGetPlayerState` / `TryGetMobState` allocate nothing per call (assert with the suite's existing zero-allocation constraint).

## GDD Update Required?

Yes — `design/gdd/zone-instancing.md` (the CR-ZI-8 step 6 `EntityState` field notes) and the `isInCombat` comment in `design/gdd/networking-wire-protocol.md` should state rule 6. Asked separately.
