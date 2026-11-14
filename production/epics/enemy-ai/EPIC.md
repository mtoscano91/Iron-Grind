# Epic: Enemy AI

> **Layer**: Feature
> **GDD**: design/gdd/enemy-ai.md
> **Architecture Module**: Enemy AI (`architecture.md` layer map, Feature; phase 3 game system of the ADR-002 tick), `src/ServerLogic/` (ADR-012)
> **Status**: In Progress
> **Stories**: 14 created 2026-11-13 — see Stories
> **Manifest Version**: 2026-11-06

## Overview

Enemy AI is the server-authoritative system that drives every monster on the 20 Hz tick: proximity aggro, pursuit through `INavigationProvider`, the two-phase melee attack and the Enraged spawn variant. Each mob runs a `MobController` state machine (Dormant → Pursuing → WindingUp → AttackExecution → Recovering → Returning, with Dead as a global override). The defining mechanic is the locked swing: on `WindingUp` entry the mob's `FacingAngle` is converted to `capturedForward` and held for the whole wind-up (≥ `MIN_WIND_UP_TICKS`, 600 ms), then passed unchanged to Hit Detection's `CheckHit`, so a player who reads the tell can step out of range or past the facing tolerance. Enemy AI owns `MobDefinition` and `IMobDefinitionRegistry` (read by Mob Spawning, Navigation and Combat), validates every definition at startup, rolls Enraged at spawn on the injected `IRandomProvider`, and on death calls `ResolveMobDrop(tierShift)` before raising `MobEventBus.MobDied`. It runs only in `IronGrind.ServerLogic`; clients receive mob state as `EntityState` data.

## Scope (user decisions 2026-11-12 and 2026-11-13, `/create-epics`)

**Server plus wire.** The decisions recorded in the Navigation epic (2026-11-12) apply:

1. **Mob death: Enemy AI bridges.** Combat's `ICombatService.OnMobDied` stays the only kill event (ADR-010, manifest). Enemy AI subscribes, enters `Dead`, calls `ResolveMobDrop(tierShift)`, then raises `MobEventBus.MobDied`. A mob that reaches HP ≤ 0 with no killer (CR-AI-13) runs the same Dead-entry routine, which ignores a second call. ADR-010 gets a dated note with this epic.
2. **Server plus wire.** Client visuals, UI and audio go to Combat UI / HUD / VFX / Audio; party hooks are a seam owned by the Party System epic.
3. **Content split by system.** Enemy AI authors the MVP mob roster (`MobDefinition` data); Mob Spawning authors the starting-zone spawn table.

Moved out:

| Item | Owner |
|------|-------|
| V-AI-1..3, A-AI-1..2, state-transition cues, Enraged indicator | Combat UI / VFX / Audio epics (presentation) |
| Raising `MobEventBus.MobSpawned`, spawn table, respawn timers | Mob Spawning epic (not created yet — Enemy AI tests drive the bus directly) |
| XP award on kill | Killer's controller (Auto-Attack / Skill System, ADR-010 note 2026-10-30) — not Enemy AI |
| Line-of-sight aggro (OQ-AI-2), Enraged UI label (OQ-AI-3) | Deferred (Vertical Slice / Polish) |

Seams this epic replaces: `StandInNavMobSpeedSource` (`INavMobSpeedSource` → the mob definition registry); `StandInAutoAttackMobTypeSource` if the roster-backed source is still not wired by then.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-003: Nav Agent Lifecycle State Machine | Enemy AI's usage contract: `Resume` on Pursuing re-entry after WindingUp/Recovering (not on Dormant re-aggro); `SetSpeed` on Returning ↔ Dormant/Pursuing; `INavigationProvider` calls only in phase 3 | HIGH |
| ADR-002: NavMesh Service Execution Contract | Enemy AI runs in phase 3, reads `GetCurrentVelocity` / `IsPathStale`; no same-tick path after `SetDestination` | HIGH |
| ADR-010: Event and Messaging Architecture | Per-zone `MobEventBus` (`MobSpawned` in, `MobDied` out); `OnMobDied` is Combat's single kill event; XP never from an `OnMobDied` subscriber | LOW |
| ADR-013: Server Random Provider | Enraged roll = one `NextDouble()` per spawn on the injected process-level `IRandomProvider`; never a per-mob `System.Random` (overrides that sentence of CR-AI-12) | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | Phase 3 systems run in an order the composition root lists explicitly — Status Effects → Enemy AI → Auto-Attack (CR-AI-2) | LOW |
| ADR-012: Server/Client Assembly Boundary | Written in `IronGrind.ServerLogic` from the first story; no client assembly references (AC-AI-21) | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-13). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-ai-001 | `MobDefinition` (sealed class) and `IMobDefinitionRegistry`; startup checks `AttackWindUpTicks ≥ MIN_WIND_UP_TICKS`, `MoveSpeed > 0`, `AggroRange > 0`, `LeashRange ≥ AggroRange + 2`, `AttackRange ≥ ATTACK_RANGE_MIN`, no NaN; a failing type is logged and refused (CR-AI-7, EC-AI-4, EC-AI-12, AC-AI-7, AC-AI-8) | ADR-012 ✅ |
| TR-ai-002 | `MobController` state machine: Dormant, Pursuing, WindingUp, single-tick AttackExecution, Recovering (EC-AI-8 Recovery = 0), Returning (arrival within `RETURN_ARRIVAL_THRESHOLD`), Dead; `pendingReturn` lifecycle (CR-AI-9, CR-AI-14, EC-AI-5, AC-AI-6, AC-AI-9, AC-AI-11) | ADR-002, ADR-014 ✅ |
| TR-ai-003 | Dormant aggro scan every `AGGRO_SCAN_INTERVAL_TICKS`, XZ squared distance, first player in entity list order wins, aggro never transfers (CR-AI-3, F-AI-2, EC-AI-3, AC-AI-1, AC-AI-2, AC-AI-22) | ADR-014 ✅ |
| TR-ai-004 | `FacingAngle` written every Pursuing tick as `short` ×10 (0–3599, validated); `capturedForward` locked on WindingUp entry (F-AI-3) and passed unmodified to `CheckHit` on AttackExecution (CR-AI-4, CR-AI-5, AC-AI-4, AC-AI-5, AC-AI-16) | ADR-012 ✅ |
| TR-ai-005 | Attack cadence: `hasAttackedOnce` + `lastAttackExecutionTick` cooldown gate, effective interval F-AI-1 (CR-AI-6, AC-AI-3, AC-AI-20a, AC-AI-20b, AC-AI-25) | ADR-014 ✅ |
| TR-ai-006 | Leash (F-AI-4) and target invalidation: immediate Returning outside the attack cycle, `pendingReturn` inside it, re-scan on target death, `CheckHit` skipped for an invalid target (CR-AI-9, CR-AI-10, EC-AI-2, AC-AI-9, AC-AI-10, AC-AI-12b, AC-AI-23, AC-AI-26) | ADR-014 ✅ |
| TR-ai-007 | Navigation delegation: `SetDestination` only past `PURSUIT_REPOSITION_THRESHOLD` or when `IsPathStale`; `Stop` on WindingUp entry; `Resume` on Pursuing re-entry; `SetDestination(spawn)` once on Returning; `SetSpeed` per ADR-003 (CR-AI-11, AC-AI-17, AC-AI-18, AC-AI-24) | ADR-002, ADR-003 ✅ |
| TR-ai-008 | Enraged spawn roll `NextDouble() < EnragedChance` on the injected `IRandomProvider`; success applies F-AI-E-1 through `SetBaseStat` (not a buff) and sets HP to the new MaxHP (CR-AI-12, F-AI-E-1, AC-AI-13, AC-AI-14, AC-AI-15) | ADR-013 ✅ |
| TR-ai-009 | Dead: HP ≤ 0 check at tick start (CR-AI-13); bridge Combat's `OnMobDied` → `ResolveMobDrop(mob, IsEnraged ? EnragedLootQualityBonusTier : 0)` → `MobEventBus.MobDied`, idempotent; `DEATH_LINGER_TICKS` then despawn; no XP (EC-AI-1, EC-AI-11, AC-AI-12a, AC-AI-19) | ADR-010 ✅ (dated note due with this epic) |
| TR-ai-010 | Zone wiring: `MobController` creation on `MobEventBus.MobSpawned`; phase 3 order Status Effects → Enemy AI → Auto-Attack in the composition root; registry replaces `StandInNavMobSpeedSource`; MVP mob roster data; assembly isolation (CR-AI-1, CR-AI-2, AC-AI-21) | ADR-010, ADR-012, ADR-014 ✅ |

## Open Items for Story Readiness

1. **Mob → player damage path.** The GDD says only "writes damage to target's `EntityState.HP`" on `HitResult.Hit`. Whether the hit goes through Damage Calculation (target Defense, Status modifiers) and which applier writes HP must be settled before the AttackExecution story is Ready.
2. **OQ-AI-4 tick budget.** Profile Enemy AI at 150 mobs against the server budget before the first zone load test. Baseline from Navigation Story 011: `NavigationPhaseStep.Run()` max 0.198 ms after the first tick (Editor batch mode, main thread only).
3. **GDD wording fixes (not blocking):** the Dependencies row "Character Persistence `AwardXP`" and OQ-AI-6 are stale (XP is the killer's direct call, ADR-010 note 2026-10-30); OQ-AI-1 is closed (`ILootTableService.ResolveMobDrop(EntityID, int)` exists, Loot Table epic Complete); CR-AI-12's per-mob generator wording is overridden by ADR-013.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/enemy-ai.md` are verified
- All Logic and Integration stories have passing test files in `tests/`
- All Visual/Feel and UI stories have evidence docs with sign-off in `production/qa/evidence/`
- ADR-010 carries the dated note on the `OnMobDied` → `MobEventBus.MobDied` bridge

## Decisions (2026-11-13, `/create-stories`)

4. **Mob → player damage goes through Damage Calculation**: `baseDamage` = mob `AttackPower`, `DamageCalculator.Calculate`, then Character Stats `ApplyDamage` on the player (Story 010; resolves Open Item 1).
5. **Combat's mob stand-ins are in scope**: target validity, mob HP applier, mob type source, XP award, plus TD-102 mob stat seeding and mob runtime position/facing (Stories 008, 012), so a player can kill a moving mob end to end.
6. **Loot Table is not wired into the zone** (nothing builds `LootTableService`): the death bridge calls `ILootTableService` through a seam; the zone uses a logging stand-in (TD-107).

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Mob definition, registry and startup validation](story-001-mob-definition-registry.md) | Logic | Complete | ADR-012 |
| 002 | [Enemy AI config and MVP mob roster asset](story-002-config-and-mob-roster.md) | Logic | Complete | ADR-012, ADR-009 |
| 003 | [MobController core — states, spawn init, dead check, facing conversion](story-003-mob-controller-core.md) | Logic | Complete | ADR-014 |
| 004 | [Aggro scan and pursuit](story-004-aggro-and-pursuit.md) | Logic | Complete | ADR-003, ADR-002 |
| 005 | [Attack cycle — wind-up, execution, recovery, cooldown](story-005-attack-cycle.md) | Logic | Complete | ADR-003 |
| 006 | [Leash, target invalidation and Returning](story-006-leash-invalidation-returning.md) | Logic | Complete | ADR-003 |
| 007 | [Enraged spawn roll and stat scaling](story-007-enraged-spawn-roll.md) | Logic | Ready | ADR-013 |
| 008 | [Mob runtime state and stat seeding (TD-102)](story-008-mob-runtime-state.md) | Integration | Ready | ADR-002, ADR-010 |
| 009 | [Enemy AI phase step and position integration](story-009-enemy-ai-phase-step.md) | Integration | Ready | ADR-002, ADR-014 |
| 010 | [Mob → player damage](story-010-mob-to-player-damage.md) | Integration | Ready | ADR-012, ADR-010 |
| 011 | [Death bridge — OnMobDied → loot → MobEventBus.MobDied](story-011-death-bridge.md) | Integration | Ready | ADR-010 |
| 012 | [Replace Combat's mob stand-ins](story-012-combat-mob-seams.md) | Integration | Ready | ADR-010 |
| 013 | [Composition wiring](story-013-composition-wiring.md) | Integration | Ready | ADR-014, ADR-002, ADR-012 |
| 014 | [150-mob tick budget fixture (OQ-AI-4)](story-014-tick-budget-fixture.md) | Integration | Ready | ADR-002, ADR-004 |

Order: 001 → 002, 003 → 004 → 005 → 006; 003 → 007 → 008; 006 + 008 → 009 → 010, 011 → 012 → 013 → 014. The end-to-end smoke run (a player kills a spawned mob) needs Mob Spawning to raise `MobSpawned` and belongs to that epic.

## Next Step

Run `/story-readiness production/epics/enemy-ai/story-001-mob-definition-registry.md`.
