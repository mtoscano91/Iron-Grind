# Epic: Character Stats

> **Layer**: Foundation
> **GDD**: design/gdd/character-stats.md
> **Architecture Module**: Character Stats
> **Status**: Ready (7/8 stories Complete; Story 008 Blocked on Leveling System epic, not yet created)
> **Stories**: 8 stories created (001–007 Complete, 008 Blocked)

## Overview

Character Stats is the authoritative data store for every numeric attribute that defines a character's combat capabilities and survivability. It holds the base values (STR, DEX, VIT, INT, and derived HP/MP/AP/DEF) and exposes them through a read interface queried live on demand — no gameplay system caches these values. All systems that modify character power (Leveling, Equipment, Status Effects) write to Character Stats; all systems that use character power (Auto-Attack Combat, Damage Calculation, Skill System) read from it. This module also owns the `OnStatChanged` broadcast event and the `OnEntityDied` event, both implemented as C# `event Action<T>` with struct args per ADR-010.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event/Messaging Architecture | `OnStatChanged` and `OnEntityDied` must be C# `event Action<T>` with struct args; no per-emit heap allocation; no central EventBus | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-stats-001 | Modifier stack applies flat bonuses first, then percentage bonuses multiplicatively; result clamped to StatMin/StatMax last | ❌ No ADR (design-only, LOW risk) |
| TR-stats-002 | Percentage bonuses within the same modifier layer are additive (not compounding): two +10% bonuses = +20%, not +21% | ❌ No ADR (design-only, LOW risk) |
| TR-stats-003 | CurrentHP and CurrentMP are protected from the modifier stack; MaxHP decrease immediately clamps CurrentHP; MaxHP increase grants no HP restoration | ❌ No ADR (design-only, LOW risk) |
| TR-stats-004 | `ApplyDamage` at CurrentHP=0 is a no-op; `OnEntityDied` fires exactly once at CurrentHP=0.0; overkill clamps at 0.0, never negative | ❌ No ADR (design-only, LOW risk) |
| TR-stats-005 | `OnStatChanged` broadcast uses C# `event Action<StatChangedArgs>` with struct args — no per-emit heap allocation on the server tick path | ADR-010 ✅ |
| TR-stats-006 | All gameplay systems query stats live on demand at point of use; no system other than HUD holds a cached stat value | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [CharacterStats container — stat schema, IL2CPP-safe types, GetBaseStat/SetBaseStat](story-001-container-schema.md) | Logic | Complete | ADR-010 |
| 002 | [F-1 modifier stack — GetEffectiveStat, intra-layer additive pct, StatMin/StatMax clamp](story-002-modifier-stack.md) | Logic | Complete | — |
| 003 | [Modifier lifecycle — AddBuffModifier, AddEquipmentModifier, Remove, idempotency](story-003-modifier-lifecycle.md) | Logic | Complete | — |
| 004 | [CurrentHP/MP lifecycle — ApplyDamage, ApplyRegen, ConsumeMana, death boundary](story-004-resource-pools.md) | Logic | Complete | — |
| 005 | [OnStatChanged/OnEntityDied events — named delegates, fixed subscriber array, re-entrance guard](story-005-events.md) | Logic | Complete | ADR-010 |
| 006 | [Write ownership — ILevelingService injection, mob guard, write-locked stat rejection](story-006-write-ownership.md) | Logic | Complete | ADR-010 |
| 007 | [Transaction API — BeginStatTransaction/EndStatTransaction/RollbackStatTransaction](story-007-transaction-api.md) | Logic | Complete | — |
| 008 | [Integration — Leveling System ↔ Character Stats (spawn path, tier transitions, MaxMP ceiling)](story-008-integration-leveling.md) | Integration | Blocked | — |

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria in `design/gdd/character-stats.md` (AC-01 through AC-34) are verified
- All Logic stories (001–007) have passing test files in `tests/EditMode/CharacterStats/`
- Integration story (008) has a passing test file in `tests/Integration/CharacterStats/` (unblocked after Leveling System epic is Done)
- Story 008 is unblocked when Leveling System epic stories are Done

## Next Step

All 7 logic stories are READY (story-readiness run 2026-06-28).
Run `/dev-story production/epics/character-stats/story-001-container-schema.md` to begin implementation.
Work through stories in order — each story's `Depends on:` field tells you what must be Done before you can start it.
Note: populate `docs/architecture/tr-registry.yaml` with TR-stats-001 through TR-stats-006 before TR-ID checks can be enforced.
