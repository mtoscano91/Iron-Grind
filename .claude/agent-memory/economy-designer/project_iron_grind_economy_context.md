---
name: Project Iron Grind — Economy Context
description: Core economy design state for Project Iron Grind (Knight Online-style mobile MMORPG) — item tiers, sell prices, open invariants, and cross-GDD dependencies
type: project
---

Project Iron Grind is a Knight Online-style mobile MMORPG for iOS (Unity 6.3 LTS). Core pillar: "Legendary Gear" — high-enhancement gear is rare because item destruction on failure creates genuine scarcity. A +9 weapon is a server event.

**Why:** All economy decisions must preserve this pillar. Selling beats building = pillar collapse.

**How to apply:** When reviewing or authoring any economy system, the first question is always: does this design make +9 weapons rarer or more common? Rarer = good. More common or indifferent = investigate.

## Item Sell Price Table (F-1, F-2) — as of 2026-04-25

| Item | Sell Price |
|------|-----------|
| Bronze gear (all slots) | 10g |
| Iron gear | 30g |
| Steel gear | 90g |
| DarkSteel gear | 270g (provisional — OQ-8) |
| HP/MP Potion Small | 2g |
| HP/MP Potion Medium | 6g |
| HP/MP Potion Large | 18g |

Note: Large HP Potion (18g) > Bronze gear (10g) — inverts gear hierarchy. Flagged in Pass 3 review as Finding 3.

## Key Open Invariants

**OQ-8 (critical):** DarkSteel 270g must remain < expected value of attempting enhancement (+0→+9), NOT merely < total enhancement cost. The Enhancement System GDD (design order #18) must be authored before DarkSteel sell price is locked. Current OQ-8 formulation in the GDD is incorrect — it compares to total cost, not expected value. This was flagged as BLOCKING in Pass 3.

**NPC buy > sell:** No authored invariant ensures NPC item buy price > NPC sell-back price. Arbitrage risk. NPC Shop GDD (not yet authored) must own this constraint.

## Character Stats Reference (for sell price calibration)

- L1 AttackPower: 30 (base, no gear)
- L60 Warrior DPS AttackPower: 768 (base, no gear)
- L60 Warrior Tank MaxHP: 5,520
- L60 Healer Support MaxHP: 3,160
- LevelTierMultiplier: ×1.0/×1.2/×1.5/×2.0 at L1-19/L20-39/L40-59/L60

## GDD Status (2026-04-25)

- item-database.md: Revised, Pending Re-Review (Pass 3 not yet resolved)
- character-stats.md: Approved (Pass 5)
- Enhancement System GDD: NOT YET AUTHORED (design order #18)
- NPC Shop GDD: NOT YET AUTHORED

## Known Stale Session State

active.md shows "StatModifiers max per item | 6" — WRONG. Corrected to 2 in Pass 2. Do not rely on session state for this value; read the GDD directly.
