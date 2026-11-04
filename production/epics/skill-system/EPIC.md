# Epic: Skill System

> **Layer**: Core
> **GDD**: design/gdd/skill-system.md
> **Architecture Module**: Skill System (`architecture.md` layer map), `src/ServerLogic/`
> **Status**: Ready
> **Stories**: 12 created 2026-11-02 — see Stories
> **Manifest Version**: 2026-10-26

## Overview

The Skill System is the server-authoritative active-ability layer. A client sends a `SkillCastRequest`; the server keeps at most one per entity per tick and resolves it in the simulation phase: validation V-0 to V-9 (mana is consumed last, at V-9), `NotifySkillUsed` to reset the auto-attack timer, BaseDamage from AP, INT or STR and a call to Damage Calculation for damaging skills, the kill sequence on a kill, buff, debuff and instant-heal application through Status Effects, then the cooldown as an absolute expiry tick. Each character holds a `SkillInstance[10]` aligned with its class skill list; unlock state is cached at spawn and updated on `OnLevelUp`. Clients receive `SkillCastResult`, `SkillCooldownUpdate`, `SkillCooldownSnapshot` on zone join and `SkillUnlockNotification`. At MVP, 10 skills per class ship as data.

## Scope (user decision 2026-11-02, `/create-epics`)

**Server side plus wire messages.** In this epic: `SkillID`, `SkillDefinition`, `SkillDatabase`, `SkillSystemConfig`; the class skill list (Decision 2); the per-entity skill store, spawn, despawn and unlocks; cast intake, validation and execution; cooldowns and mana cost; the five wire messages; the 20-skill MVP roster as data, with its BuffIDs (Decision 4).

Moved out:

| Item | Owner |
|------|-------|
| Skill bar, cooldown sweep, locked state, unlock toast, tooltip, locked-skill label, error shakes; party frame and friendly targeting | Combat UI / HUD |
| Cast and hit VFX | VFX System |
| Cast, hit, rejection, ready and unlock audio | Audio System |
| Real mob targets and `KillEvent` | Enemy AI (seams and fakes here, as in Auto-Attack Combat) |

Carried in from Auto-Attack Combat: OQ-HD-4 (`AutoFaceEvent` for facing changes after the first auto, such as on a skill cast) and AC-HD-19 (b).

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-012: Server/Client Assembly Boundary | New system lives in `IronGrind.ServerLogic` from its first story; shared types in Foundation | LOW |
| ADR-014: Inbound Request Dispatch and Tick Order | `SkillCastRequest` already has its routing row (U-U, per-type stale check); its handler stores the request, and a Skill System step resolves it in phase 3 (Decision 3) | LOW |
| ADR-010: Event and Messaging Architecture | Direct calls into Damage Calculation, Status Effects, Auto-Attack Combat and Leveling (XP); subscribes to `ILevelingEventBroadcaster.OnLevelUp` | LOW |
| ADR-004: Networking — NGO Substrate | Project envelope and channels for the five messages | LOW (consumer only) |
| ADR-013: Server Random Provider | Crit rolls go through Damage Calculation | LOW |

## GDD Requirements

TR-IDs below are registered in `docs/architecture/tr-registry.yaml` (2026-11-02). No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-sk-001 | `SkillID`, `SkillDefinition`, `SkillDatabase`, `SkillSystemConfig` (SkillDefinition Schema, AC-SK-33 to AC-SK-35) | ADR-012 ✅ |
| TR-sk-002 | Per-entity `SkillInstance[10]`, spawn and despawn, unlock state cached at spawn (CR-SK-20, AC-SK-10, 13, 47, 48) | ADR-012 ✅ |
| TR-sk-003 | Unlock on `OnLevelUp`, `SkillUnlockNotification` (CR-SK-21, AC-SK-11, 14, 45, 46, 49) | ADR-010, ADR-004 ✅ |
| TR-sk-004 | Cast intake, one per entity per tick, V-0 to V-9, `Self` targets resolve to the caster (CR-SK-1, CR-SK-2, CR-SK-19, AC-SK-01 to 05, 12, 34, 39 to 41) | ADR-014 ✅ |
| TR-sk-005 | `NotifySkillUsed`, BaseDamage F-SK-1 / F-SK-2, Damage Calculation call, kill sequence (CR-SK-3 to CR-SK-6, AC-SK-20 to 25, 30 to 32) | ADR-010, ADR-013 ✅ |
| TR-sk-006 | Effect application: buff, debuff, instant heal; none on a target killed this tick (CR-SK-7, F-SK-3, AC-SK-26 to 29) | ADR-010 ✅ |
| TR-sk-007 | Cooldowns as absolute expiry ticks, no global cooldown (CR-SK-8, CR-SK-10, CR-SK-11, AC-SK-06 to 08) | ADR-014 ✅ |
| TR-sk-008 | Mana cost `Ceil(ManaCost × MANA_COST_MULTIPLIER)`, consumed at V-9, no refund (CR-SK-13, CR-SK-16, F-SK-4, AC-SK-15 to 19) | ADR-012 ✅ |
| TR-sk-009 | `SkillCastResult`: accepted casts by relevance, rejected casts to the caster only, none for V-1 / V-2 drops (CR-SK-9, AC-SK-42, 43) | ADR-004 ✅ |
| TR-sk-010 | `SkillCooldownUpdate` on accept and on `OnCooldown` rejection; `SkillCooldownSnapshot` on zone join (CR-SK-12, CR-SK-23, AC-SK-09, 44) | ADR-004 ✅ |
| TR-sk-011 | MVP roster of 20 skills as data, with BuffIDs (Skill Roster, CR-SK-14, 15, 22, AC-SK-36 to 38, OQ-SK-6) | ADR-012 ✅ |

## Decisions (2026-11-02, `/create-epics`)

Applied to `skill-system.md` and `entities.yaml`.

1. **Server side plus wire.** Client UI, VFX and audio move to their epics (table above).
2. **The class skill list is built here.** A story adds `SkillList` to `ClassDefinition` as `uint[]` (Class System CD-4: Unity drops a serialized `SkillID[]`), `GetClassSkills` on `IClassRegistry` returning `IReadOnlyList<SkillID>`, and calls `OnEntitySpawned` / `OnEntityDespawned` from the code that spawns characters today. Class System has no epic yet. AC-SK-35 now reads `uint[]`.
3. **Casts resolve in phase 3.** The `SkillCastRequest` handler only stores the request (first one per entity per tick wins, EC-SK-11); a Skill System step listed after Auto-Attack Combat in ADR-014 phase 3 validates and executes. This keeps EC-SK-1 (the auto first) and EC-SK-6 (a level-up from an auto kill on tick T lands before casts on T).
4. **BuffIDs are assigned in the roster story.** It authors the BuffIDs and BuffDefinitions for the 10 buff and debuff skills and records them in `skill-system.md` and `entities.yaml` (OQ-SK-6).
5. **Text drift fixed.** The auto-attack timer counts whole ticks (Auto-Attack Combat decision 4), not float seconds: CR-SK-3, EC-SK-1, AC-SK-30. The snapshot carries `cooldownExpiryTick`, not `ticksRemaining`: AC-SK-09, AC-SK-44. Header status is Approved. OQ-SK-5 is resolved by `leveling-system.md` CR-2.10.

## Decisions (2026-11-02, `/create-stories`)

6. **Silence is a seam (Decision A).** V-5b reads `ISkillSilenceSource`; the production stand-in never silences, because `status-effects.md` defines no silence effect. Story 005.
7. **Skill damage reaches clients through `SkillCastResult` only (Decision B).** Skills apply damage through the inner damage applier, never the `DamageOutputApplier` wrapper, so no `DamageEvent` / `SelfDamageEvent` is sent for a skill. Story 007.
8. **Targeted casts face the target (Decision C, OQ-HD-4).** An accepted `SingleHostile` or `SingleFriendly` cast on another entity turns the caster and sends `AutoFaceEvent` through `AutoFaceEmitter`; `Self` casts don't. Closes AC-HD-19 (b). Story 007; rule written into `hit-detection.md` OQ-HD-4.
9. **Skill message ids reserved:** R-U / U-U path: `0x0308` `SkillCastRequest`, `0x0309` `SkillCastResult`, `0x030A` `SkillCooldownUpdate`. R-OD priority block `0xE150`–`0xE15F` (`ClientMessageOutbox` sends R-OD only in `0xE000`–`0xEFFF`): `0xE150` `SkillCooldownSnapshot`, `0xE151` `SkillUnlockNotification` (reassigned 2026-11-02 from `0x030B` / `0x030C`, Story 003 code review). Each story records its id in `networking-wire-protocol.md`.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Skill data types and class skill lists](story-001-skill-data-types.md) | Logic | Complete | ADR-012 |
| 002 | [Per-entity skill store, spawn and despawn](story-002-skill-store-spawn-despawn.md) | Logic | Complete | ADR-012 |
| 003 | [Unlock on level-up and `SkillUnlockNotification`](story-003-unlock-on-level-up.md) | Logic | Complete | ADR-010, ADR-004 |
| 004 | [Cast intake (`SkillCastRequest`, V-1, V-2)](story-004-cast-intake.md) | Logic | Complete | ADR-014 |
| 005 | [Validation sequence (V-0, V-3 to V-8)](story-005-validation-sequence.md) | Logic | Complete | ADR-014 |
| 006 | [Mana cost (V-9) and cooldown registration](story-006-mana-and-cooldowns.md) | Logic | Ready | ADR-014, ADR-012 |
| 007 | [Cast execution (`NotifySkillUsed`, damage, kill, facing)](story-007-cast-execution.md) | Logic | Ready | ADR-010, ADR-013 |
| 008 | [Effect application (buffs, debuffs, instant heal)](story-008-effect-application.md) | Logic | Ready | ADR-010 |
| 009 | [Cast output (`SkillCastResult`, `SkillCooldownUpdate`)](story-009-cast-output.md) | Logic | Ready | ADR-004 |
| 010 | [`SkillCooldownSnapshot` on zone join](story-010-cooldown-snapshot.md) | Integration | Ready | ADR-004 |
| 011 | [Composition and tick-order wiring](story-011-composition.md) | Integration | Ready | ADR-014 |
| 012 | [MVP skill roster and BuffIDs](story-012-mvp-roster-data.md) | Config/Data | Ready | ADR-012 |

Order: 001 → 002 → 003; 001 → 004 → 005 → 006 → 007 → 008; 006 → 009 → 010; 011 last of the code stories; 012 after 001, buff data final after 008.

## Carried into stories

- `ConsumeMana` takes a `float` in code (`CharacterStats.cs`); the GDD says `int`. The cast passes the integer effective cost.
- AC-SK-15 needs a seam between V-9 and CR-SK-4 (flagged in the GDD).
- AC-SK-28 (no direct `AddBuffModifier` call) is a text search, not a runtime test.
- Character spawn path for `OnEntitySpawned`: find where Leveling / Character Stats register a character today.
- OQ-HD-4 and AC-HD-19 (b): decided at `/create-stories` (Decision 8), implemented in Story 007.
- Open for `/story-readiness`: who raises `OnMobDied` on a skill kill (Story 007, recommended a combat-owned kill reporter); `SkillCastResult` relevance (Story 009); resolved at Story 005 readiness 2026-11-04: V-7 any player = friendly (Party System epic narrows it), V-8 on XZ; the missing buff numbers and skill ranges (Story 012); no production code registers the Warrior / Healer `ClassDefinition`s, so `GetClassSkills` is empty in a real zone (Story 012, found at Story 001 readiness).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/skill-system.md` in this epic's scope are verified
- All Logic and Integration stories have passing test files in `tests/`
- All Visual/Feel and UI stories have evidence docs with sign-off in `production/qa/evidence/`

## Next Step

Run `/story-readiness production/epics/skill-system/story-001-skill-data-types.md`, then `/dev-story`.
