# Epic: Status Effects / Buffs

> **Layer**: Core
> **GDD**: design/gdd/status-effects.md
> **Architecture Module**: Status Effects
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories status-effects`

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

## Next Step

Run `/create-stories status-effects` to break this epic into implementable stories.
