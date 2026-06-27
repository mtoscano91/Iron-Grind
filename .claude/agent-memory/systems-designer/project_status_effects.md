---
name: project-status-effects-gdd
description: Status Effects GDD authoring state — formula audit pass completed 2026-05-19; formulas section pending write
metadata:
  type: project
---

File: design/gdd/status-effects.md
Status: In Design (2026-05-19)

Detailed Rules (CR-SE-1 through CR-SE-17) and States/Transitions are written.
Formulas, Edge Cases, Dependencies, Tuning Knobs, Acceptance Criteria sections are [To be designed].

**Why:** Formula audit pass completed before writing the Formulas section to catch boundary issues early.

**How to apply:** When the user resumes this GDD, start from the Formulas section. All formulas have been audited; see the formula audit analysis in conversation history or re-derive from the locked constants.

Key formula decisions from audit (2026-05-19):
- DurationTicks = floor(DurationSeconds × TICK_RATE_HZ) — floor() is correct; reject DurationSeconds ≤ 0
- MAX_ACTIVE_BUFFS_PER_ENTITY recommendation: 4 (conservative) or 8 (comfortable) — user decision pending
- HpRegenPerTick: flat value (not % of MaxHP) — matches CharacterStats ApplyRegen() interface; range [0.5, 50.0] float
- Capacity pre-check (OccupiedSlots + def.modifiers.Length ≤ 32) is a rule worth stating in the GDD
- TotalHpRegen formula is informational/tuning-only, not a runtime formula
- Memory footprint formula: TotalActiveEffectSlots = MAX_ENTITY_SLOTS × MAX_ACTIVE_BUFFS_PER_ENTITY

Missing formula identified: stack modifier effective value is fully owned by CharacterStats (F-1), not Status Effects.
Missing formula identified: buff wire size formula (if networking broadcasts buff state) — deferred to networking additions.
