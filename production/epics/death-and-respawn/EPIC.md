# Epic: Death & Respawn

> **Layer**: Feature
> **GDD**: design/gdd/death-and-respawn.md
> **Architecture Module**: Death & Respawn (`architecture.md` layer map, Feature), `src/ServerLogic/` (ADR-012)
> **Status**: Ready
> **Stories**: 9 created 2026-11-17 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

Death & Respawn is the server-authoritative player death lifecycle. When Character Stats raises `OnEntityDied(EntityID)` for a player, the handler first checks for a ghost (a disconnected character still in auto-combat): a ghost death records `GhostDeath = 3` for the reconnect flow, releases the zone slot and returns, with no Dead state and no `EntityDied` broadcast. Otherwise, in the same tick, the player enters `Dead`: duplicate deaths are ignored, combat is prohibited (`SetCombatProhibited(true)`), `DeathTick` / `RespawnTick = DeathTick + RESPAWN_DELAY_TICKS` (F-DR-1, `long`) are recorded, `DeathStateEntered` goes to the player and `EntityDied` to the zone (relevance-filtered). Status Effects strips buffs through its own `OnEntityDied` subscription. On the exact respawn tick the player passes through the single-tick `Respawning` state back to `Alive`: town respawn point via `GetTownRespawnPoint` (retry, then fallback), HP and MP set to full, position set, combat re-enabled, `StatusEffects.OnEntityRespawned`, then `EntityRespawned` to the zone and `RespawnConfirmed` to the player. Character data is never reloaded. No items are lost; the cost is pacing (default 7 s, no early respawn). While dead, a player cannot use skills, auto-attack, be targeted or change zone voluntarily.

## Scope (user decision 2026-11-17, `/create-epics`)

**Server only.** The Enemy AI and Mob Spawning epic decisions carry over: client visuals, audio and the death overlay go to presentation epics.

**Party seam now, verify later.** Party System is not built. This epic adds an `IPartyMemberStatusSink` called with Dead on death and Online on respawn (no-op in production composition), and unit-tests the calls. The party behaviour criteria move to the Party System epic.

What already exists (built by earlier epics):

| Piece | Where |
|-------|-------|
| `OnEntityDied(EntityID)` on Character Stats (no player-side subscriber yet) | `src/ServerLogic/CharacterStats/CharacterStats.cs` |
| `SetCombatProhibited(EntityID, bool)` | `src/ServerLogic/AutoAttackCombat/AutoAttackCombatSystem.cs` |
| `ITownRespawnPointProvider.GetTownRespawnPoint` (authored → centroid → zero; CR-ZI-11) | `src/ServerLogic/ZoneInstancing/ITownRespawnPointProvider.cs`, `TownRespawnPointCache.cs`, `ZoneDefinitionSlotRespawnPointProvider.cs` |
| `ZoneSessionState.Dead` / `Respawning` on the player slot; nothing in production sets them yet | `src/ServerLogic/ZoneInstancing/ZoneSlotPool.cs` |
| Snapshot writes `isInDeadState` / `respawnTicksRemaining` from the slot state and `ZonePlayerStateView.RespawnTick` (CR-ZI-8 step 6) | `src/ServerLogic/ZoneInstancing/ZoneSnapshotBuilder.cs` |
| Teardown saves Dead / Respawning players with HP 0 (EC-DR-3) | `src/ServerLogic/ZoneInstancing/ZoneCloseSteps.cs` |
| Load-time HP clamp for saved HP ≤ 0 (CR-CP-3 step 8, EC-DR-2) | Character Persistence epic |
| `StatusEffects.OnEntityRespawned` and the deferred death cleanup (CR-SE-10) | `src/ServerLogic/StatusEffects/StatusEffectsService.cs` |
| Ghost tracking and ghost-death cleanup | `src/ServerLogic/Networking/GhostSession/GhostEntityTracker.cs`, `GhostCleanupSequencer.cs`, `ConnectionStateMachine.HandleGhostDeathWhileReconnecting` |
| Wire schemas for `EntityDied`, `DeathStateEntered`, `EntityRespawned`, `RespawnConfirmed` (design only, not in code) | `design/gdd/networking-wire-protocol.md` |

Gap this epic closes: nothing moves a player into `Dead`, so a player killed today stays at 0 HP with no respawn.

Moved out:

| Item | Owner |
|------|-------|
| Death overlay, greyscale / vignette, respawn pulse, camera fade, death and respawn audio (GDD Visual/Audio and UI Requirements, AC-DR-19, AC-DR-20); needs `/ux-design` for `design/ux/death-overlay.md` first | Presentation epics (Combat UI / HUD / VFX / Audio) |
| Party behaviour: AC-DR-13b (dead member gets 0 XP), AC-DR-18, AC-DR-NEW-4, AC-DR-NEW-7, AC-DR-NEW-8, AC-DR-NEW-9 (CR-DR-18 to CR-DR-21) | Party System epic (wires a real `IPartyMemberStatusSink`) |
| Party leader transfer on death (OQ-DR-5) | Deferred post-MVP |

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event and Messaging Architecture | Subscribes to Character Stats `OnEntityDied` (named method, unsubscribed on dispose); direct calls for `GetTownRespawnPoint`, `SetCombatProhibited`, `OnEntityRespawned` and the party sink | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | The respawn countdown step gets an explicit place in the phase list; dead-state rejection of skill, zone-change and `RespawnNow` requests at dispatch | LOW |
| ADR-012: Server/Client Assembly Boundary | Written in `IronGrind.ServerLogic`; no client assembly references | LOW |
| ADR-011: Async Persistence and the Tick Loop | HP 0 saves on teardown and session expiry while dead; no `LoadCharacter` on respawn | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-17). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-dr-001 | Respawn config: `RESPAWN_DELAY_SECONDS` (default 7.0, range [5.0, 10.0]) rejected below 5.0 at server startup with an error log and refusal to start (no clamp); F-DR-1 `RESPAWN_DELAY_TICKS = RoundToInt(seconds × 20)` with a ≥ 100 assertion; `RESPAWN_ANCHOR_RETRY_TICKS` (default 40, [20, 200]); data-driven (EC-DR-5, AC-DR-12, AC-DR-14) | ADR-012 ✅ |
| TR-dr-002 | Death entry for players: per-player `DeathState` (`long` `DeathTick` / `RespawnTick`), `OnEntityDied` subscription that ignores non-players, duplicate deaths in Dead or Respawning dropped, `SetCombatProhibited(true)`, slot set to `Dead`, `RespawnTick = DeathTick + RESPAWN_DELAY_TICKS`, all in the death tick (CR-DR-1 to CR-DR-3, CR-DR-6, AC-DR-1, AC-DR-2, AC-DR-3, AC-DR-15) | ADR-010 ✅ |
| TR-dr-003 | Ghost branch checked first: record `GhostDeath = 3` for the reconnect flow, release the zone slot, return; no Dead state, no timer, no combat ban, no `EntityDied` (CR-DR-Ghost, T-4, AC-DR-10, AC-DR-11) | ADR-010 ✅ |
| TR-dr-004 | Respawn on tick `RespawnTick` exactly (guard `State == Dead && currentTick >= RespawnTick`): Respawning for one tick, `GetTownRespawnPoint` with per-tick retry up to `RESPAWN_ANCHOR_RETRY_TICKS` then fallback and a critical log, HP / MP set to effective max via `SetBaseStat`, position set, `SetCombatProhibited(false)`, `StatusEffects.OnEntityRespawned`, slot back to Alive; no `LoadCharacter` (CR-DR-8 to CR-DR-11a, CR-DR-14, EC-DR-4, EC-DR-9, AC-DR-5 to AC-DR-9) | ADR-014 ✅ |
| TR-dr-005 | Wire messages (R-OD priority path, MessageTypeIDs in 0xE000–0xEFFF): `EntityDied` to zone clients (relevance-filtered, RFR-7; never for a ghost), `DeathStateEntered(respawnTimerSeconds)` to the dying player, `EntityRespawned(position)` to zone clients and `RespawnConfirmed` to the player, enqueued in the T-3 tick; `SelfPositionUpdate` suppressed while Dead / Respawning (CR-DR-6, CR-DR-7, CR-DR-13, AC-DR-NEW-1 to AC-DR-NEW-3, AC-NC-39 dead-state part) | ADR-010, ADR-014 ✅ |
| TR-dr-006 | Dead permissions: skill use rejected, voluntary zone change rejected, `RespawnNow` dropped silently, dead players excluded from player targeting and mob aggro / target re-checks; chat and read-only panels allowed (CR-DR-15 to CR-DR-17, AC-DR-4, AC-DR-17, AC-DR-NEW-5, AC-DR-NEW-6) | ADR-014 ✅ |
| TR-dr-007 | Disconnect, teardown and expiry: the timer keeps running while disconnected and respawn runs unattended; `ZonePlayerStateView.RespawnTick` feeds the reconnect snapshot; teardown and session expiry save HP 0 and the load clamp brings the character back alive (EC-DR-1, EC-DR-2, EC-DR-3, EC-DR-7, AC-DR-16) | ADR-011 ✅ |
| TR-dr-008 | Zone wiring: the service built per zone with Character Stats, Combat, Status Effects, the respawn point provider, slot pool, ghost tracker and outbound sender; countdown step placed in the phase list; end-to-end check that a mob kills a player who respawns at the town point with full HP / MP | ADR-010, ADR-012, ADR-014 ✅ |
| TR-dr-009 | Party seam: `IPartyMemberStatusSink.SetMemberStatus(entityId, Dead)` on death (non-ghost only) and `Online` at T-3, no-op in production until Party System exists; unit test verifies the calls (CR-DR-5, CR-DR-12, AC-DR-13 unit part) | ADR-010 ✅ |

## Open Items for Story Readiness

1. **Tick placement and state ownership (ADR-014).** Where the countdown step runs, its order against Status Effects' next-tick cleanup and Combat targeting; whether the slot's `ZoneSessionState` is the single source of Dead / Respawning or `DeathState` is kept beside it; how `RespawnTick` reaches `ZonePlayerStateView`.
2. **Message IDs.** Assign the four MessageTypeIDs in 0xE000–0xEFFF and register their codecs; check the relevance filter path for `EntityDied` (RFR-7).
3. **Mob targeting.** Confirm whether Enemy AI aggro and target re-checks already skip dead players by HP, or need the dead state (AC-DR-4).
4. **Town point.** Zone0 has no authored town respawn point (Mob Spawning Story 007 laid out its gradient from the origin). Decide whether to author one now; if so, re-check the spawn-table gradient and that no mob aggro reaches it (EC-DR-4).
5. **Ghost path.** How the handler reads `isGhost` (`GhostEntityTracker.IsGhost`) and hands off to `GhostCleanupSequencer.CompleteGhostDeathCleanup` / `HandleGhostDeathWhileReconnecting`.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/death-and-respawn.md` are verified, except those moved to the Party System and presentation epics (listed under Moved out)
- All Logic and Integration stories have passing test files in `tests/`
- A player killed in a production-composed zone respawns at the town point with full HP / MP

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Respawn config and F-DR-1](story-001-respawn-config.md) | Logic | Complete | ADR-012 |
| 002 | [Death entry — DeathState and T-1](story-002-death-entry.md) | Logic | Complete | ADR-010 |
| 003 | [Ghost death branch](story-003-ghost-death.md) | Logic | Ready | ADR-010 |
| 004 | [Respawn sequence — T-2 and T-3](story-004-respawn-sequence.md) | Logic | Ready | ADR-014 |
| 005 | [Death and respawn wire messages](story-005-wire-messages.md) | Logic | Ready | ADR-010, ADR-014 |
| 006 | [Dead player permissions](story-006-dead-permissions.md) | Logic | Ready | ADR-014 |
| 007 | [Disconnect, reconnect, teardown and expiry while dead](story-007-disconnect-teardown-expiry.md) | Integration | Ready | ADR-011 |
| 008 | [Zone composition wiring](story-008-zone-composition-wiring.md) | Integration | Ready | ADR-014 |
| 009 | [End-to-end — a mob kills a player, the player respawns](story-009-end-to-end-mob-kills-player.md) | Integration | Ready | ADR-014 |

Order: 001 → 002 → (003, 004, 006) → 005 after 003 + 004 → 007 after 004 → 008 after 001–007 → 009 after 006 + 008.

## Findings at `/create-stories` (2026-11-17)

1. **Disconnect overwrites Dead**: the slot's `ZoneSessionState` goes to `Disconnected_SessionActive` on disconnect, and the snapshot builder and teardown read dead-ness from it. Story 007 makes `DeathState` the source of truth through `IPlayerDeathStateReader`.
2. **No movement teleport**: Story 004 adds `TrySetAuthoritativePosition` to Movement; `SetProhibited` already covers "cannot move".
3. **HP > 0 already gates** mob target validity and skill casting (Story 006 mostly tests).
4. **Not in code**: no `RespawnNow` message (AC-DR-17 tested as "no early path"); no voluntary zone-change request (AC-DR-NEW-6 moves to the epic that builds zone travel); no production `SelfPositionUpdate` emitter (suppression deferred).
5. **Message IDs**: 0xE160–0xE163 (next free block after 0xE151).

## Next Step

Stories 001 (2026-11-17) and 002 (2026-11-18) Complete. Run `/story-readiness production/epics/death-and-respawn/story-003-ghost-death.md`, then `/dev-story`.
