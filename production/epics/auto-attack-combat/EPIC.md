# Epic: Auto-Attack Combat

> **Layer**: Core
> **GDD**: design/gdd/auto-attack-combat.md
> **Architecture Module**: Auto-Attack Combat (`architecture.md` Core layer), `src/ServerLogic/`
> **Status**: In Progress
> **Stories**: 9 created 2026-10-30 — see Stories
> **Manifest Version**: 2026-10-26

## Overview

Auto-Attack Combat is the server-authoritative combat loop. With a valid target and the toggle ON, a player's `_actionTimer` counts ticks up to `CycleTicks`, then the server resolves one auto: `CheckHit`, Damage Calculation, the kill sequence on a kill, and a timer reset. Accepted skill casts reset the timer through `NotifySkillUsed`. The timer keeps running while prohibited or out of range, so a ready auto fires on the first tick it can. Clients receive `CycleTimerBroadcast` every tick for the charge bar, `AutoFaceEvent` on the first auto after activation, `HitCheckResult` and `DamageEvent`.

## Scope (user decision 2026-10-30, `/create-epics`)

**Server side plus wire messages.** In this epic: the state machine, tick timer, resolution order, kill sequence, `SetAutoAttack`, target validity, `NotifySkillUsed`, `SetCombatProhibited`, `CycleTimerBroadcast`, `AutoFaceEvent`, and typed `DamageEvent` intake in the R-U batch step.

Moved out:

| Item | Owner |
|------|-------|
| Charge bar, toggle button, damage numbers, out-of-range arc; AC-08, AC-17 | Combat UI / HUD |
| Real mob targets, `IMobDefinitionRegistry`, `GetXPAward` | Enemy AI, Leveling (seams and fakes here) |
| `KillEvent`, mob despawn | Enemy AI (user decision 2026-10-30, `/create-stories`); Combat raises `OnMobDied` |

Inherited from Hit Detection: AC-HD-12, AC-HD-13 (no damage on a miss), AC-HD-18, AC-HD-19 (facing updates and `AutoFaceEvent`).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-012: Server/Client Assembly Boundary | New system lives in `IronGrind.ServerLogic` from its first story | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | `SetTarget` and `SetAutoAttack` apply in the inbound phase, before the fire check (EC-8) | LOW |
| ADR-010: Event and Messaging Architecture | Direct calls for damage and XP (note 2026-10-30); `OnMobDied` for loot, party credit, respawn | LOW |
| ADR-004: Networking — NGO Substrate | Project envelope and channels for all messages | LOW (consumer only) |
| ADR-013: Server Random Provider | Crit rolls go through Damage Calculation | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-02). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-aa-001 | Activation, deactivation and the four states (Rules 1–4, AC-12 to AC-15) | ADR-012, ADR-014 ✅ |
| TR-aa-002 | Tick timer, `CycleTicks` and the clamp (Rules 5–7, F-1, AC-01 to AC-03) | ADR-014 ✅ |
| TR-aa-003 | Resolution order and one auto per tick (Rule 10, F-3, AC-09 to AC-11, AC-16, AC-20, AC-HD-12, AC-HD-13) | ADR-012, ADR-014 ✅ |
| TR-aa-004 | Kill sequence by direct calls (Rule 10 Step 3) | ADR-010 ✅ |
| TR-aa-005 | `NotifySkillUsed` resets the timer (Rules 12–15, AC-04, AC-05) | ADR-010 ✅ |
| TR-aa-006 | `SetAutoAttack` and target validity (Rule 1) | ADR-004, ADR-014 ✅ |
| TR-aa-007 | `CycleTimerBroadcast` every tick (Rule 9) | ADR-004 ✅ |
| TR-aa-008 | Auto-face on the first auto (Rules 20–22, AC-18, AC-19, AC-HD-18, AC-HD-19) | ADR-004 ✅ |
| TR-aa-009 | One `HitCheckResult` per miss streak; typed `DamageEvent` intake (Rule 10 Step 2) | ADR-004 ✅ |

## Decisions (2026-10-30, `/create-epics`)

Applied to `auto-attack-combat.md`, `hit-detection.md`, `networking-core.md`, ADR-010 and `entities.yaml`.

1. **Toggle ON resets the timer.** AC-14 rewritten to match Rule 2, EC-27 and EC-30.
2. **Prohibition is not a deactivation.** The timer keeps running (Rule 7, AC-11).
3. **A ready auto retries every tick.** AC-09 now matches Rule 10 and AC-20.
4. **The timer counts whole ticks.** `CycleTicks = ceil(CycleDuration × 20)`, tolerances are ±1 tick, and the `deltaTime` clamp is gone.
5. **`CycleTimerBroadcast` is per tick on NGO.** The Mirror text and the interval knob are gone.
6. **XP is a direct call, not an `OnMobDied` subscriber.** ADR-010 amended; this prevents double XP.
7. **No Targeting System.** Combat keeps the toggle and evaluates validity: a living mob in the same zone. Players are never valid targets at MVP.
8. **One `HitCheckResult` per miss streak** for auto-attack retries (CR-HD-10, AC-HD-11a).

## Decisions (2026-10-30, `/create-stories`)

9. **Auto-face fires on the first auto, not on toggle ON.** `hit-detection.md` CR-HD-7 and AC-HD-19(a) amended to match Rule 20.
10. **`KillEvent` goes to the Enemy AI epic.**
11. **Combat does not call `LastBeatServerTickTracker`.** Story 005 marks the grace-window text superseded; the OWL code stays for a later cleanup.
12. **The attacker's damage feedback is `SelfDamageEvent`,** per the channel contract. Story 007 adds it alongside `DamageEvent`.

## Carried into stories

- `SetAutoAttack` is `0xE050` and is in `networking-wire-protocol.md`, `networking-channel-contract.md`, `networking-message-criticality.md` and ADR-014 Decision 1 (Story 006, done).
- `networking-wire-protocol.md:406` states the miss-streak exception (Story 006, done).
- `networking-core.md` CR-NET-8 and `networking-owl-compensation.md` grace-window text marked superseded (Story 005, done). OWL cleanup (later, with the OWL code in `src/`): `networking-test-harness.md:281-283` (`OnSkillGraceWindowEvaluated`) and `networking-wire-protocol.md:1773` (`RTT_PROBE_INTERVAL_SECONDS` rationale) still describe the grace window.
- Skill System epic: inject `IAutoAttackSkillUseSink` (implemented by `AutoAttackCombatSystem`) and call `NotifySkillUsed` in the request step, before combat's `Run` of the same tick (Story 005).
- `death-and-respawn.md` still names a Targeting System; dead-entity rejection lands in Combat's validity check (Story 001).
- Composition (Story 006, done): stand-ins in `ZoneServerStandIns.cs` for target validity, damage calculator (rejects every hit), applier, XP award (0) and mob type source; Enemy AI replaces them (the real `ZoneMobRosterMobTypeSource` needs `ZoneInstancingWiring` to expose its roster). Zone entry must register player stats before combat activates (otherwise 40-tick cycle). TD-088: `TargetSlotTracker` not cleared on disconnect. Mobs have no `CharacterStats` HP, so production `IsKill` stays false for mobs until Enemy AI (Story 004, user decision 2026-11-02).
- Enemy AI: mob health must ignore a second `Apply` on a dead mob (same-tick double kill); decide whether respawn fires when `OnMobDied` is skipped for an unknown mob type (Story 004).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Combat state and activation](story-001-combat-state-and-activation.md) | Logic | Complete | ADR-012, ADR-014 |
| 002 | [Tick timer and `CycleTicks`](story-002-tick-timer-and-cycle-ticks.md) | Logic | Complete | ADR-014 |
| 003 | [Resolution order](story-003-resolution-order.md) | Logic | Complete | ADR-012, ADR-013, ADR-014 |
| 004 | [Kill sequence](story-004-kill-sequence.md) | Logic | Complete | ADR-010 |
| 005 | [`NotifySkillUsed`](story-005-notify-skill-used.md) | Logic | Complete | ADR-010 |
| 006 | [`SetAutoAttack`, input and composition](story-006-set-auto-attack-input-and-composition.md) | Integration | Complete | ADR-004, ADR-014 |
| 007 | [Damage output](story-007-damage-output.md) | Integration | Complete | ADR-004 |
| 008 | [`CycleTimerBroadcast` step](story-008-cycle-timer-broadcast-step.md) | Integration | Complete | ADR-004, ADR-014 |
| 009 | [Auto-face](story-009-auto-face.md) | Integration | Ready | ADR-004 |

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- AC-01 to AC-05, AC-09 to AC-16, AC-18 to AC-20 and the Hit Detection criteria above are verified, using fakes for mobs and XP
- All Logic and Integration stories have passing test files in `tests/`
- Real mob kills and AC-08, AC-17 are recorded in the Enemy AI, Combat UI and HUD epics when those are created

## Next Step

Run `/story-readiness production/epics/auto-attack-combat/story-009-auto-face.md`, then `/dev-story` (Stories 001-008 Complete; 009 is the last story of the epic).
