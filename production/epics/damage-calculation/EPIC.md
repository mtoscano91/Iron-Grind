# Epic: Damage Calculation

> **Layer**: Core
> **GDD**: design/gdd/damage-calculation.md
> **Architecture Module**: Damage Calc
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories damage-calculation`

## Overview

Damage Calculation is the authoritative resolver of every damage event in Iron Grind. It accepts an attacker identity, a target identity, a base damage value, and a damage type, then applies the complete resolution sequence — Defense mitigation for physical hits, MagicDefense mitigation for elemental hits, optional elemental bonus damage from the attacker's weapon, critical strike evaluation — and returns a single integer `FinalDamage` for the caller to apply to target health. No caller performs its own damage math; every damage source in the game (auto-attacks, skills) routes through this single resolver. It is also the single place where kill detection occurs: when `FinalDamage` would reduce the target's `CurrentHP` to or below zero, Damage Calculation sets `IsKill = true` in the returned result.

## Governing ADRs

**⚠️ Untraced architecture decision — explicitly required by the GDD itself, not by this epic's inference**: the GDD's own Core Rules text states "Damage Calculation and all types it owns... reside in a `ServerLogic.asmdef` assembly excluded from the client build via Unity platform constraints. Using `[Server]` attribute alone is insufficient — it ships code to the client binary. **The ADR specifying the complete server/client assembly boundary must be authored before implementation begins.**" No such ADR exists in `docs/architecture/`. This contradicts architecture.md's blanket "🟢 LOW / by-design no ADR" classification for this system — the classification covers gameplay-formula risk, not the assembly-boundary/anti-cheat concern the GDD itself flags as blocking. Flag for `/architecture-decision` before Story 001 of this epic is implemented.

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| *(none accepted)* | Server/client assembly boundary (`ServerLogic.asmdef` exclusion) — required per the GDD's own text, not yet written | Unclassified — likely LOW/MEDIUM (build-configuration decision, not an engine API risk) |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-dmg-001 | `DamageCalculation(BaseDamage, AttackerID, TargetID, DamageContext) → DamageResult` is server-only; no client code path calls it; enforced via `ServerLogic.asmdef` client-build exclusion | ❌ No ADR — see Untraced Architecture Decision above |
| TR-dmg-002 | `DamageContext` (`PhysicalAuto`/`PhysicalSkill`/`MagicalSkill`) is echoed in `DamageResult` for VFX routing only; at MVP all three contexts produce identical formula output for the same `BaseDamage` — any divergence is the caller's responsibility, never a formula branch here | ❌ No ADR (design-only, LOW risk) |
| TR-dmg-003 | `BaseDamage` is caller-precomputed (`GetEffectiveStat(AttackerID, AttackPower)`); this system never re-queries AttackPower itself | ❌ No ADR (design-only, LOW risk) |
| TR-dmg-004 | Resolution executes as a fixed 11-step sequence (crit stats read → defense read → physical mitigation → ... ); no step may be reordered | ❌ No ADR (design-only, LOW risk) |
| TR-dmg-005 | `PhysicalMitigated = max(BaseDamage × MIN_DAMAGE_FRACTION, BaseDamage − Defense)` — damage floor guarantees non-zero chip damage regardless of defense stacking | ❌ No ADR (design-only, LOW risk) |
| TR-dmg-006 | Kill detection is owned here: `FinalDamage` reducing target `CurrentHP` to ≤0 sets `IsKill = true` in the returned result; callers act on it (XP award via Leveling, `CharacterStats.ApplyDamage`) — this system never fires events itself | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/damage-calculation.md` are verified, including the Group A–D test suites the GDD itself specifies (Formula, Kill Detection, Edge Case, Integration)
- Logic stories have passing test files in `tests/EditMode/DamageCalculation/`
- The server/client assembly boundary ADR (TR-dmg-001) is Accepted before implementation begins — this is a hard GDD-stated prerequisite, not a soft recommendation

## Next Step

Run `/architecture-decision` for the `ServerLogic.asmdef` server/client boundary first, then `/create-stories damage-calculation`. Story 001 should not begin without that ADR Accepted.
