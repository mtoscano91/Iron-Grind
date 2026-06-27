# Cross-GDD Review Report

**Date:** 2026-05-23
**GDDs Reviewed:** 27 system GDDs (25 approved + game-concept + systems-index framing)
**Method:** Inline review building on the full `/consistency-check` completed the same session (registry baseline clean; 3 cross-doc conflicts resolved prior to this review).
**Pillars:** Earned Power, Rhythm Mastery, Social Gravity, Legendary Gear
**Anti-pillars:** NOT story-driven, NOT PvP-first (MVP), NOT hand-holding, NOT visually ambitious

---

## Consistency Issues

### Blocking
None. The 3 cross-doc conflicts found earlier this session were resolved before this review:
- PrestigeBand encoding (equipment-system.md CR-EQS-11 aligned to 4-band scheme)
- Iron flat bonus range (equipment-system.md F-EQS-2 corrected 16–22 → 22–28)
- GetElementalBonus interface shorthand (enhancement-system.md interactions table)

### Warnings

**W-1: OQ-DC-1 stale + Damage Calculation ↔ Enhancement dependency asymmetry**
- `damage-calculation.md` (OQ-DC-1, lines 116/306) still says enhancement-elemental scaling is unresolved and "must be resolved before the Enhancement System GDD is authored."
- The Enhancement System GDD is now **approved** and resolves it: F-ENH-2 defines `EnhancedElementalDamage(level)`; enhancement-system.md declares Damage Calculation consumes `IEnhancementBonusProvider.GetElementalBonus(...)` via `Equipment.GetEquippedWeaponEnhancementLevel()`.
- `damage-calculation.md` does not reference `IEnhancementBonusProvider`, does not list Enhancement System as a dependency, and reads the +0 base `ElementalBonus`.
- Registry lists `damage-calculation.md` in `referenced_by` for `F-ENH-2` and `IEnhancementBonusProvider` — a link the doc does not contain.
- **Functional impact:** as written, an enhanced weapon's elemental bonus never reaches the damage formula.
- **Resolution:** amend damage-calculation.md (resolve OQ-DC-1, consume enhanced elemental value, add Enhancement dependency). *Applied 2026-05-23 — see below.*

**W-2: +9 broadcast networking channel unspecified**
- enhancement-system.md CR-ENH-14 / UI-ENH-3 define `ServerBroadcast_Enhancement9` to all online players, but no approved networking GDD defines a cross-zone broadcast channel or MessageTypeID. Existing relevance is zone-scoped.
- Tracked open question. Not architecture-blocking.
- **Resolution:** add a server-wide broadcast channel to networking-wire-protocol.md + networking-channel-contract.md before implementation. *Deferred — tracked pre-implementation amendment.*

**W-3: Pillar count mismatch (trivial)**
- systems-index.md Overview says "three mechanical pillars"; game-concept.md defines four (Earned Power, Rhythm Mastery, Social Gravity, Legendary Gear).
- **Resolution:** align systems-index Overview to four-pillar framing. *Applied 2026-05-23.*

---

## Game Design Issues

### Blocking
None.

### Warnings / Notes

**D-1 (INFO): Difficulty curve not yet validatable.** Enemy AI, Mob Spawning, and Hit Detection are Not Started. Player power scaling is well-defined (enhancement flat bonuses, tier parity F-ENH-3, damage cap 59,994), but enemy HP/damage scaling has no GDD. The combat difficulty curve cannot be holistically validated until those exist. Sequencing note, not a flaw.

**D-2 (PASS): Progression loops — no competition.** Combat is the sole XP source (F-LS-1). Enhancement is a prestige/gold-sink endgame, not a competing XP track. Matches the concept's "kill → loot → enhance → return."

**D-3 (PASS): Economy — healthy sinks.** Gold sources (monster drops, sell-back) balanced by the enhancement scroll sink (~955K gold to +9 per F-ENH-5), explicitly the primary gold sink. Item destruction is a deliberate item sink serving Legendary Gear. Scrolls are NPC-only (AC-ENH-24), gold-gated. No unbounded surplus.

**D-4 (PASS): Dominant-strategy guards present.** Ghost-combat farming neutralized (GHOST_COMBAT_TTL 5→1 min). Party XP deduction (F-PS-1, ×0.70 at N=4) offset by throughput — a deliberate, playtest-flagged balance.

**D-5 (PASS): Pillar alignment clean.** Every system maps to a pillar; no anti-pillar violations (no story, no PvP, no pay-to-win shortcut). Player fantasies cohere around "warrior earning visible power through risk."

---

## Cross-System Scenario Issues

Scenarios walked: 3
1. Party kill → gold drop + XP + possible level-up
2. Enhance weapon to +9 in town → server broadcast + appearance update
3. Equip an enhanced weapon → stat + appearance + damage flow

### Blockers
None.

### Warnings
- **Scenario 3 — Equip enhanced weapon (Equipment + Damage Calculation + Enhancement):** physical flat-bonus path works (Equipment `AddEquipmentModifier` → `GetFlatBonus`); the elemental path is broken — Damage Calculation reads the +0 base `ElementalBonus` and never calls `GetElementalBonus`. Root cause = W-1.
- **Scenario 2 — Enhance to +9 (Enhancement + Networking):** broadcast has no transport channel (W-2). Outcome commits correctly; payload has nowhere to ride until wire protocol is amended.

### Info
- **Scenario 1 — Party kill:** gold (absolute-value GoldSyncEvent), XP (F-PS-1 guarded at N=0), and loot ownership (TAG_THRESHOLD_FRACTION) all have defined ordering and no race. Coherent.

---

## GDDs Flagged for Revision

| GDD | Reason | Type | Priority | Status |
|-----|--------|------|----------|--------|
| damage-calculation.md | OQ-DC-1 stale; missing Enhancement dependency + elemental enhancement consumption (W-1) | Consistency | Warning | Fixed 2026-05-23 |
| networking-wire-protocol.md | No channel for cross-zone +9 broadcast (W-2) | Consistency | Warning | Tracked pre-implementation amendment |
| systems-index.md | "three pillars" vs concept's four (W-3) | Consistency | Warning | Fixed 2026-05-23 |

---

## Verdict: CONCERNS

No blocking issues — architecture is not gated. Three warnings identified; W-1 (elemental enhancement path broken at Damage Calculation) and W-3 (pillar count) fixed inline the same session. W-2 (cross-zone broadcast channel) remains a tracked pre-implementation networking amendment.
