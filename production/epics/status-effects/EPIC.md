# Epic: Status Effects / Buffs

> **Layer**: Core
> **GDD**: design/gdd/status-effects.md
> **Architecture Module**: Status Effects
> **Status**: Complete (2026-10-29) — TR-se-006 (client buff display) deferred per the Definition of Done
> **Stories**: 9 stories created 2026-10-28 (001–009), all Complete

## Overview

Status Effects / Buffs is the system responsible for applying, tracking, and expiring all temporary stat modifications and periodic resource restoration in Iron Grind. It owns the buff modifier layer of `CharacterStats` — the sole system permitted to call `AddBuffModifier()`/`RemoveBuffModifier()` — and drives all tick-counted duration expiry, plus all HP/MP regen events via `ApplyRegen()`/`ApplyManaRegen()`. A status effect is a named bundle of stat modifiers and/or a periodic regen rate, attached to a target entity for a fixed tick duration and identified by a `BuffID`. A second entry point, `ApplyInstantHeal`, delivers one-shot HP restoration without creating an `ActiveEffect` entry.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event/Messaging Architecture | Consumes `CharacterStats.OnStatChanged` (already ADR-010-governed `event Action<T>` with struct args) as its notification path when modifiers expire; this epic does not define a new event of its own | LOW (consumer only) |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-se-001 | `BuffDefinition`/`StatModifier`/`ActiveEffect` are fully value-type structs, no reference-type fields, no managed heap allocation on copy/storage — IL2CPP-safe (CR-SE-1) | ❌ No ADR (design-only, LOW risk) |
| TR-se-002 | Any `StatID` except `Level`, `Experience`, `CurrentHP`, `CurrentMP` may be targeted by a modifier; `MaxHP`/`MaxMP` permitted with expiry clamping; `CurrentHP`/`CurrentMP` prohibited — resource restoration is regen-only, never modifier-layer (CR-SE-2) | ❌ No ADR (design-only, LOW risk) |
| TR-se-003 | Exactly two public entry points exist; no external caller may invoke `CharacterStats.AddBuffModifier()`/`ApplyRegen()` directly — all buff/regen traffic routes through Status Effects (CR-SE-3) | ❌ No ADR (design-only, LOW risk) |
| TR-se-004 | On expiry, `RemoveBuffModifier()` is called and `OnStatChanged` fires so subscribers see the updated effective value without polling | ADR-010 ✅ (consumes existing event) |
| TR-se-005 | `ApplyInstantHeal(EntityID, float amount)` delivers one-shot HP restoration without creating an `ActiveEffect` entry — distinct code path from the duration-tracked buff system | ❌ No ADR (design-only, LOW risk) |
| TR-se-006 | The wire protocol does not yet define buff-state broadcast messages; client display of buff icons/durations is a provisional dependency on future networking additions, not resolved in this GDD | ❌ No ADR — **forward dependency on a future Networking Core wire-schema addition** |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.
> **Note**: Debuffs (movement slows, AP reductions) are in scope as future mob-ability/skill mechanics but require the Enemy AI and Skill System GDDs to define concrete parameters — not blocking for this epic's own stories, which cover the buff/regen engine itself.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/status-effects.md` are verified
- Logic stories have passing test files in `tests/EditMode/StatusEffects/`
- Client-facing buff-icon/duration display is explicitly deferred (TR-se-006) rather than silently dropped, pending the future wire-schema addition

## Stories

*Written 2026-10-28 (`/create-stories`, lean). Code findings at creation: OQ-SE-1 HP side holds in code (`ApplyDamage(0f)` clamps without an event); MP side needs `CharacterStats.ClampCurrentMPToMax` (Story 006). OQ-SE-2 is resolved by a direct cleanup call on Zone Instancing's slot-release paths (Story 008, hook point decided at its readiness). `CharacterStats.AddBuffModifier` returns `void`, so application goes through an `IBuffModifierLayer` seam (Story 003). Character Stats caps 32 buff entries per stat; the GDD's guard (32 per entity) is stricter and stays. No Auto-Attack Combat code exists yet — AC-SE-24 uses a stand-in (Story 009). Recommended order: 001 → 002 → 003 → 005 → 004 → 006 → 007 → 008 → 009.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Data Model, Constants and Duration Conversion](story-001-data-model-and-constants.md) | Logic | Complete (2026-10-28) | ADR-012, ADR-010 |
| 002 | [ApplyEffect Validation](story-002-apply-effect-validation.md) | Logic | Complete (2026-10-28) | ADR-012 |
| 003 | [Application, Atomic Rollback and Caster Attribution](story-003-application-and-rollback.md) | Logic | Complete | ADR-012, ADR-010 |
| 004 | [Refresh on Reapply](story-004-refresh-on-reapply.md) | Logic | Complete | ADR-012 |
| 005 | [Per-Tick Processing and Expiry](story-005-tick-processing-and-expiry.md) | Logic | Complete (2026-10-28) | ADR-010, ADR-014 |
| 006 | [Expiry Clamp and Batched Stat Events](story-006-expiry-clamp-and-batched-events.md) | Logic | Complete | ADR-010 |
| 007 | [Death Cleanup and Instant Heal](story-007-death-cleanup-and-instant-heal.md) | Logic | Complete | ADR-010 |
| 008 | [Zone Departure and Slot Cleanup](story-008-zone-departure-and-slot-cleanup.md) | Integration | Complete (2026-10-29) | ADR-010, ADR-012 |
| 009 | [Tick Pipeline Wiring, Mob Targets and Allocation Budget](story-009-tick-pipeline-wiring-and-allocation.md) | Integration | Complete (2026-10-29) | ADR-014, ADR-012 |

TR-se-006 (client buff display) has no story: it waits on a Networking Core wire-schema addition, as the Definition of Done says.

## Next Step

Stories 001–007 Complete 2026-10-28 (TD-085 resolved by 006; TD-086 logged by 007); Story 008 Complete 2026-10-29 (`IZoneSlotReleaseListener.BeforeRelease` on `ZoneSlotPool`, set-once `SetReleaseListener`; service `ClearEntity` clears every row and the dead flag before a slot is released). Story 009 Complete 2026-10-29: the composition builds one zone `CharacterStats` (internal `ZoneCharacterStats`) and the service (internal `StatusEffects`); phase 3 = `[ZoneInstancingWiring, StatusEffectsService]`; `ZoneSlotPool.TryGetMobSlot` + `ZoneSlotPoolEntityIndex` (mob → OWL index); `ZoneSimulationStep` isolates each entry (`EntryFailed`); allocation asserted with `Is.Not.AllocatingGCMemory()` (TD-087 logged for an older vacuous check). **Epic complete.** For later epics: Auto-Attack Combat appends its phase-3 entry after Status Effects and keeps the AC-SE-24 test; the Skill System takes the service from the composition; filling in player/mob base stats (admission, spawn) and the Death & Respawn `OnEntityRespawned` caller are still open; TR-se-006 waits on the Networking Core wire schema.
