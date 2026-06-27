# Review Log: Character Stats

`design/gdd/character-stats.md`

---

## Review — 2026-04-23 — Verdict: NEEDS REVISION → APPROVED (pass 5, user-accepted)

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, unity-specialist, creative-director (synthesis)
Status: APPROVED — user elected to accept pass 5 revisions without a pass 6 re-review.

---

## Review — 2026-04-23 — Verdict: NEEDS REVISION → Revised (pass 5)

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, unity-specialist, creative-director (synthesis)
Blocking items: 7 | Recommended: 4 (applied in-session)
Summary: One formula structure error caught (F-8 multiplied full expression by LevelTierMultiplier instead of DEX component only — inconsistent with F-9; L60 Healer crit floor was 13% instead of 8%; formula corrected to `0.05 + (DEX × 0.0015 × LevelTierMultiplier)`; all snapshot tables updated). Internal contradiction resolved: Rule 6 claimed BaseDamage returns float while Rule 7 specifies int for AttackPower; resolved as Option A — GetEffectiveStat returns int, Auto-Attack Combat widens to float at call site. Math.Floor corrected to Mathf.FloorToInt (wrong Unity API, also contradicted AC-25). MaxMP ceiling clamp ownership assigned to Leveling System; AC-34 added for INT≥409 boundary. AC-12b rewritten with exactly-representable IEEE 754 values (50.25f + 0.75f) to prevent iOS IL2CPP test flakiness. IL2CPP safety cluster added: modifier entry type specified as readonly struct; StatID dict key warning corrected (requires explicit IEqualityComparer); LINQ forbidden in hot paths; transaction dedup changed from HashSet to fixed-size array. AC-27(b) ordering contract made explicit (auto-alloc before F-3 recompute). Also applied: OQ-7 elevated to blocking cross-GDD dependency constraint; Healer Survivability playtest tripwire added to HPPerVIT; Equipment System intermediate-state flicker documented; Player Fantasy Warrior DPS naming corrected.
Prior verdict resolved: Yes — all 7 blockers from pass 5 review addressed. Awaiting pass 6 re-review in clean context.

**Open items carrying forward:**
- OQ-1: Caller identity enforcement mechanism (blocks AC-13, AC-15)
- OQ-2: OnEntityDied event contract
- OQ-3: Modifier list serialization format
- OQ-7: Healer INT party expression — elevated to blocking implementation dependency on Skill System GDD

---

## Review — 2026-04-23 — Verdict: MAJOR REVISION NEEDED → Revised (pass 4)

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, unity-specialist, creative-director (synthesis)
Blocking items: 25 | Recommended: 0
Summary: One arithmetic error introduced during pass 3 revision caught (AC-33 used STR rate +2/level on VIT auto-alloc instead of +1/level; VIT=54 not 98 at L45, MaxHP=1,920 not 3,240). Two stale INT references corrected (F-7 and Tuning Knobs: INT=187 → INT=246, matching the revised Healer template). Seven implementation contracts added: GetEffectiveStat return type (floor-to-int for int stats), CharacterStats container type (plain C# class), ItemID as equipment modifier key, subscriber capacity (16 per stat, log error on overflow), EntityDiedHandler delegate, transaction constraints (no nesting, StatID dedup, RollbackStatTransaction), modifier collection type (fixed-capacity arrays). Three design-intent gaps closed: Healer free-point policy explicitly documented as intentional off-meta design; Warrior Tank/DPS named as player labeling convention not a mechanical distinction; Healer INT party expression deferred to Skill System GDD with OQ-7 added. Five new ACs added (AC-10b, AC-12b, AC-12c, AC-29b, transaction constraint spec). All string literals in ACs converted to typed enums (BuffID.WarriorCry, BuffID.Poison, ItemID.*).
Prior verdict resolved: Yes — all 25 items from pass 4 review addressed. Awaiting pass 5 re-review in clean context.

**Open items carrying forward:**
- OQ-1: Caller identity enforcement mechanism (blocks AC-13, AC-15)
- OQ-2: OnEntityDied event contract
- OQ-3: Modifier list serialization format
- OQ-7: Healer INT party expression — HealPower deferred to Skill System GDD

---

## Review — 2026-04-22 — Verdict: MAJOR REVISION NEEDED → Revised

Scope signal: L
Specialists: game-designer, systems-designer, economy-designer, qa-lead, performance-analyst, unity-specialist, creative-director (synthesis)
Blocking items: 8 | Recommended: 4
Summary: The three-layer modifier stack architecture and formula computation model were validated as sound. Two critical pillar-level design failures were identified and resolved: DEX orphan (Warrior auto-alloc now includes +1 DEX/level) and linear stat progression (LevelTierMultiplier ×1.2/×1.5/×2.0 at L20/L40/L60 added to F-3 through F-7). Cache/HUD contract resolved to OnStatChanged event model. EntityID specified as readonly struct wrapping uint. All four build snapshot tables corrected for off-by-one arithmetic errors. Four new ACs added for tier multiplier, ASM clamp, OnStatChanged, and mob SetBaseStat paths.
Prior verdict resolved: N/A — first review. All 8 blocking items addressed in same session.

**Open items requiring ADR before implementation:**
- OQ-1: Caller identity enforcement mechanism (unchanged)
- OQ-2: OnEntityDied event contract (unchanged)
- OQ-3: Modifier list serialization format (unchanged)
- OQ-5: OnStatChanged — ADR required for event signature and IL2CPP-safe delegate type

---

## Review — 2026-04-23 — Verdict: MAJOR REVISION NEEDED → Revised (pass 3)

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, unity-specialist, creative-director (synthesis)
Blocking items: 16 | Recommended: 7
Summary: Two design failures from pass 2 resolved via class template change — Healer VIT auto-alloc capped to +1/level (was +2), eliminating MaxHP parity with Warrior Tank at L60 (Healer Support: 3,160 vs Warrior Tank: 5,520). DEX milestone delivery fixed by applying LevelTierMultiplier to F-8 (CritChance) and F-9 (ASM), with ASMPerDEX increased from 0.001 to 0.003; Warrior auto-alloc now produces perceptible tier milestones on both axes. Technical blockers resolved: StatID specified as `enum StatID : uint`, Rule 8 disambiguated (read-only re-entry permitted, write re-entry prohibited), named delegate `StatChangedHandler` and `_isFiring` re-entrance guard fully specified, BeginStatTransaction/EndStatTransaction API added for respec path. Spec gaps closed: F-2a spawn/persistence/tier-transition paths fully specified with ownership, three new ACs added (AC-31 spawn, AC-32 persistence load, AC-33 respec at tier boundary). AC-27 expanded with three explicit sub-cases. AC-13 and AC-15 downgraded to ADVISORY pending OQ-1. Player Fantasy expanded to cover offensive identity (Rhythm Mastery pillar). F-4 MaxMP ceiling documented. Schema Default column clarified.
Prior verdict resolved: Yes — all 16 items from pass 2 addressed. Awaiting pass 4 re-review in clean context.

**Open items carrying forward:**
- OQ-1: Caller identity enforcement mechanism (blocks AC-13, AC-15)
- OQ-2: OnEntityDied event contract
- OQ-3: Modifier list serialization format

---

## Review — 2026-04-22 — Verdict: MAJOR REVISION NEEDED (pass 2)

Scope signal: L
Specialists: game-designer, systems-designer, qa-lead, unity-specialist, creative-director (synthesis)
Blocking items: 9 | Recommended: 7
Summary: Two pillar-level design failures identified: (1) Warrior Tank and Healer Support reach identical MaxHP=5,520 and Defense=394 at L60 — violates Earned Power and Social Gravity; (2) DEX auto-alloc delivers no perceptible Rhythm Mastery milestone across 59 levels (+0.15% crit/level, no tier amplification). Technical blockers: StatID type undefined (IL2CPP GC risk), AC-29 contradicts Rule 8 (read vs write re-entry distinction required), named delegate required for IL2CPP boxing safety, re-entrance guard (_isFiring) must be specified. Spec gaps: F-2a spawn path unowned, tier transition full-recompute not stated, respec derived-stat recompute not stated. AC issues: AC-28 missing DEX=69, AC-29 synchronous assertion mechanism unstated, AC-30 reclassified to BLOCKING, two new ACs required for L1 tier and persistence-load paths.
Prior verdict resolved: Yes — all 8 items from pass 1 addressed. Pass 2 revealed deeper issues from new formula complexity.
