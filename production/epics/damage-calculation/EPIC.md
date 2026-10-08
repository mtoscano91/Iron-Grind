# Epic: Damage Calculation

> **Layer**: Core
> **GDD**: design/gdd/damage-calculation.md
> **Architecture Module**: Damage Calc
> **Status**: In Progress (5/7 — 5 Complete, 0 Ready, 2 Blocked: 003 on ADR-013, written 2026-10-08 and still Proposed, 007 on the client build pipeline)
> **Stories**: 6 stories created 2026-10-07 (001–006); 006 rewritten and 007 split from it 2026-10-08

## Overview

Damage Calculation is the authoritative resolver of every damage event in Iron Grind. It accepts an attacker identity, a target identity, a base damage value, and a damage type, then applies the complete resolution sequence — Defense mitigation for physical hits, MagicDefense mitigation for elemental hits, optional elemental bonus damage from the attacker's weapon, critical strike evaluation — and returns a single integer `FinalDamage` for the caller to apply to target health. No caller performs its own damage math; every damage source in the game (auto-attacks, skills) routes through this single resolver. It is also the single place where kill detection occurs: when `FinalDamage` would reduce the target's `CurrentHP` to or below zero, Damage Calculation sets `IsKill = true` in the returned result.

## Governing ADRs

**⚠️ Untraced architecture decision — explicitly required by the GDD itself, not by this epic's inference**: the GDD's own Core Rules text states "Damage Calculation and all types it owns... reside in a `ServerLogic.asmdef` assembly excluded from the client build via Unity platform constraints. Using `[Server]` attribute alone is insufficient — it ships code to the client binary. **The ADR specifying the complete server/client assembly boundary must be authored before implementation begins.**" No such ADR exists in `docs/architecture/`. This contradicts architecture.md's blanket "🟢 LOW / by-design no ADR" classification for this system — the classification covers gameplay-formula risk, not the assembly-boundary/anti-cheat concern the GDD itself flags as blocking. Flag for `/architecture-decision` before Story 001 of this epic is implemented.

**Resolved 2026-10-08**: that ADR is ADR-012, Accepted. The paragraph above is kept as the record of why Stories 001–005 were built in `IronGrind.Foundation`; the GDD text it quotes now names `IronGrind.ServerLogic` and the define constraint.

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-012 Server/Client Assembly Boundary (Accepted 2026-10-08) | Three assemblies; `IronGrind.ServerLogic` excluded from client builds by the define constraint `UNITY_SERVER \|\| UNITY_EDITOR`; boundary test on every test run, client-binary scan on a real client build | HIGH (knowledge risk; eight engine points still to verify on real builds) |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-dmg-001 | `DamageCalculation(BaseDamage, AttackerID, TargetID, DamageContext) → DamageResult` is server-only; no client code path calls it; enforced by placing the resolver in `IronGrind.ServerLogic`, which is excluded from client builds | ✅ ADR-012 (Stories 006, 007) |
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

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Result Types, Tuning Config, Physical Mitigation and Final Floor | Logic | Complete (2026-10-07) | none (design-only) |
| 002 | Elemental Bonus and Mitigation | Logic | Complete (2026-10-08) | none (design-only) |
| 003 | Critical Strike with Injected Server RNG | Logic | Blocked — OQ-DC-2 (ADR-013 Server Random Provider is Proposed, not yet Accepted) | ADR-013 (Proposed) |
| 004 | Kill Detection and Dead-Entity Guard | Logic | Complete (2026-10-08) | none (design-only) |
| 005 | Kill Sequence Against Real Character Stats | Integration | Complete (2026-10-08) | none (design-only) |
| 006 | Server Assembly Isolation — First Move and Boundary Test | Integration | Complete (2026-10-08) | ADR-012 |
| 007 | Client-Binary Scan (AC-DC-I-01) | Integration | Blocked — no client build pipeline | ADR-012 |

Work through stories in order — each story's `Depends on:` field tells you what must be Done before you can start it.

**Decision recorded 2026-10-07 (user)**: the GDD and the Definition of Done above require the server/client assembly ADR before implementation begins. Stories 001, 002, 004 and 005 are nevertheless Ready and are built in `IronGrind.Foundation`, as the Currency epic did for its Group G; each records this as a deviation. AC-DC-I-01 stays open in Story 006, so the epic cannot close until that ADR is Accepted and Story 006 is done.

**Update 2026-10-08**: ADR-012 is Accepted. Story 006 now covers the first move and the boundary test; AC-DC-I-01 itself (a scan of a real client build) is Story 007, which waits for a client build pipeline. The epic cannot close until Story 007 is done.

**ADRs still to write** (`/architecture-decision`):
- Server RNG injection contract (OQ-DC-2) — `docs/architecture/ADR-013-server-random-provider.md`, **Proposed 2026-10-08**; blocks Story 003 until Accepted. It rules on the existing `System.Random` injection in Enhancement and Loot Table (both migrate to `IRandomProvider` in two later stories) and on how a float in [0.0, 1.0) is produced (24 bits of one integer draw, no rounding).
- Server tick ordering for sequential damage resolution per entity (OQ-DC-4, double-kill race) — no story here; it constrains the callers and must exist before Auto-Attack Combat is implemented.

**Seam introduced by Story 002**: `IEquippedWeaponQuery` (`GetEquippedWeaponID`, `GetEquippedWeaponEnhancementLevel`) is declared on the Damage Calculation side because the Equipment System is not built; the Equipment epic implements it.

**Open design mismatch found in Story 001 (2026-10-07)**: the GDD gives `BaseDamage` and AttackPower the range [1, 9999] (`design/gdd/damage-calculation.md` Formulas variable table and the `BaseDamage = 0` edge case); `src/Foundation/CharacterStats/StatSchema.cs` clamps AttackPower to 99999. Story 001 added a configurable ceiling, `DamageCalculationConfig.MaxBaseDamage`, defaulting to 99999 to match the code. Correct the GDD or the schema, and add the ceiling to the GDD's Tuning Knobs if it stays.

## Next Step

No Ready story remains in this epic. Story 003 needs ADR-013 (Server Random Provider, Proposed 2026-10-08) to be reviewed in a fresh session and Accepted; when it is implemented, its code is written in `src/ServerLogic/DamageCalculation/`. Story 007 needs a client build pipeline. Damage Calculation now lives in `IronGrind.ServerLogic` (Story 006); the next assembly move under ADR-012 is Loot Table, which has no move story yet.
