# Epic: Leveling System

> **Layer**: Core
> **GDD**: design/gdd/leveling-system.md
> **Architecture Module**: Leveling
> **Status**: Ready
> **Stories**: 13 stories created (001–013; Story 007 partially Blocked on AC-LS-18b/OQ-LS-3; Story 010 Blocked on OQ-LS-7)

## Overview

The Leveling System tracks character experience points and drives all stat growth that occurs when a player levels up. It owns the XP threshold table (L1–60), receives threshold-crossed notifications from Character Stats after each XP grant from Damage Calculation, and responds by writing updated primary attributes and derived base stats back to Character Stats via `SetBaseStat()`. At every level-up the player receives 5 attribute points (class-specific auto-allocation + free points). At milestone levels 20, 40, and 60 the `LevelTierMultiplier` advances, fully re-deriving dependent base stats. The MVP level cap is 60. The system also owns the respec mechanic — a transacted batch rewrite of previously spent free stat points.

**This epic directly unblocks Character Stats Story 008** (`production/epics/character-stats/story-008-integration-leveling.md`, currently Blocked), which proves the Leveling ↔ Character Stats integration path (spawn, tier transitions, MaxMP ceiling) from the Character Stats side.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event/Messaging Architecture | `ILevelingSystemListener.OnExperienceThresholdCrossed` is a Tier-1 direct interface call (CR-1.2, "only one system may register"), not a broadcast event — governed by ADR-010's three-tier communication model | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-lvl-001 | `AddExperience(EntityID, amount)` is owned by Character Stats; it fires `OnStatChanged(Experience)` then synchronously calls `ILevelingSystemListener.OnExperienceThresholdCrossed` if the new total crosses the next level's threshold; only one listener may ever register (CR-1.1/1.2) | ADR-010 ✅ (Tier-1 direct call) |
| TR-lvl-002 | XP is a monotonic running total in `StatID.Experience`; no "XP within level" value is stored — HUD fill is derived at display time via float-cast division (CR-1.3) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-003 | `AddExperience` with `amount ≤ 0` is rejected with an error log, no write; targeting a mob entity is a no-op (CR-1.4) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-004 | Party XP bonus computation is owned by the Party System (not yet an epic) — Leveling receives only final per-player XP grants and is not party-aware (CR-1.5) | ❌ No ADR (design-only, LOW risk) — **forward dependency on not-yet-created Party System epic** |
| TR-lvl-005 | Level-up sequence executes as a fixed, non-reorderable step sequence (CR-2), including an at-cap guard: `Level == 60` aborts level-up, Experience clamps to `XpThreshold[60]` | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-006 | At milestone levels 20/40/60, `LevelTierMultiplier` advances and all dependent base stats are fully re-derived from accumulated totals (F-LS-3) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-007 | Free point allocation: `AllocateFreePoint` guards (heldFreePoints==0, invalid StatID), decrement-then-write ordering, no HP/MP restore (CR-3) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-008 | Respec two-phase commit: Inventory System owns `ItemReservation`/combat gate (Phase 1), `TryApplyRespec` executes the transacted stat rewrite (Phase 2); exception safety guarantees the item is never lost (CR-4) | ❌ No ADR (design-only, LOW risk) — **OQ-LS-1 flags an ADR requirement for the reservation protocol interface; not yet written** |
| TR-lvl-009 | Level cap behavior: Level never exceeds 60; XP clamps at cap; sentinel array entry prevents out-of-bounds access (CR-5) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-010 | Spawn/load initialization: `InitializeAtL1` and `RestoreLevelingState` never fire level-up events; corrupted-value clamping on load (CR-6, EC-LS-38) | ❌ No ADR (design-only, LOW risk) |
| TR-lvl-011 | F-LS-1 XP threshold formula and cumulative table; F-LS-4 auto-alloc L60 snapshots | ❌ No ADR (design-only, LOW risk) — **BLOCKED on OQ-LS-7 (`GetXPAward` unspecified) per the GDD's own text** |
| TR-lvl-012 | F-PS-1 party XP detriment integration (`party-system.md`, supersedes this GDD's own F-LS-2) | ❌ No ADR (design-only, LOW risk) — **forward dependency on not-yet-created Party System epic** |
| TR-lvl-013 | HUD/UI display requirements: XP bar fill, level badge, stat screen, respec screen visual/audio cues (Visual/Audio Requirements, UI Requirements sections) | ❌ No ADR (design-only, LOW risk) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.
> **Note**: `leveling-system.md` also documents `F-LS-2` (Party XP Bonus) as **SUPERSEDED** — the canonical formula is `F-PS-1` in `party-system.md`. Stories should cite F-PS-1, not F-LS-2, once Party System's own epic exists.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/leveling-system.md` are verified, including all 7 AC groups the GDD specifies (XP/level-up sequence, free points, respec, cap, spawn/load, formula verification, edge cases)
- Logic stories have passing test files in `tests/EditMode/LevelingSystem/`
- Character Stats Story 008 (Integration — Leveling ↔ Character Stats) is unblocked and closed as part of, or immediately after, this epic
- OQ-LS-7 (`GetXPAward` spec) is resolved before Story 010 is implemented — this is a GDD-stated hard blocker, not a soft recommendation
- OQ-LS-3 (Status Effects "combat-tagged" definition) is resolved before Story 007's AC-LS-18b sub-case is implemented

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [XP Accumulation & Threshold-Crossed Notification](story-001-xp-accumulation.md) | Logic | Complete | ADR-010 |
| 002 | [Level-Up Sequence Core — Guard, Increment, Auto-Alloc, Derived Stats, HP/MP Restore](story-002-level-up-sequence-core.md) | Logic | Complete | — |
| 003 | [Consecutive Level-Up & Re-Entrancy Guards](story-003-consecutive-level-up-reentrancy.md) | Logic | Complete | ADR-010 |
| 004 | [Tier Transition From-Scratch Recompute & Raw-Write Ceilings](story-004-tier-transition-recompute.md) | Logic | Complete | — |
| 005 | [Free Point Allocation](story-005-free-point-allocation.md) | Logic | Complete | — |
| 006 | [Respec Core Commit Sequence](story-006-respec-commit-sequence.md) | Logic | Complete | — |
| 007 | [Respec Two-Phase Commit & Exception Safety](story-007-respec-two-phase-commit.md) | Integration | Complete (AC-LS-18b sub-case remains Blocked on OQ-LS-3) | — |
| 008 | [Level Cap Behavior](story-008-level-cap-behavior.md) | Logic | Ready | — |
| 009 | [Spawn Initialization & Persistence Load](story-009-spawn-persistence-load.md) | Integration | Ready | — |
| 010 | [XP Threshold Formula & Table](story-010-xp-threshold-formula-table.md) | Logic | Blocked (OQ-LS-7) | — |
| 011 | [Tier Multiplier & Auto-Alloc Formula Verification](story-011-tier-autoalloc-formula-verification.md) | Logic | Ready | — |
| 012 | [Party XP Detriment Integration](story-012-party-xp-detriment-integration.md) | Integration | Ready | — |
| 013 | [HUD/UI Display & Manual Verification](story-013-hud-ui-display.md) | Visual/Feel | Ready | — |

## Next Step

Run `/story-readiness [story-path]` per story, starting with Story 001. Story 010 is Blocked until OQ-LS-7 resolves — run `/architecture-decision` or route the `GetXPAward` spec question to whoever owns the Mob Definition / Economy System GDD first.
