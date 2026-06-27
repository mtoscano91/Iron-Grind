---
name: project-iron-grind
description: Core facts about Project Iron Grind that shape all design recommendations
metadata:
  type: project
---

Mobile MMORPG, Unity 6.3 LTS, iOS primary. Old-school MMORPG inspiration (Knight Online, Risk Your Life, Curse of Aros). Target audience: hardcore mobile gamers / MMORPG veterans, 20-35.

**Pillars**: Earned Power (primary), Legendary Gear (social gravity from rare +9 items, item destruction).

**Session length**: 20-60 min typical; grind loops can extend 2+ hours.

**MVP scope**: 34 item records (28 equipment: 7 slots x 4 tiers; 6 consumables). 35 systems total, all MVP.

**Core loop**: Kill mobs → loot drops → return to town → sell at NPC Shop → enhance gear → repeat.

**Why:** Enhancement System has item destruction — a +9 weapon is a server-wide social event. Enhancement prestige principle (from user memory): +5 Bronze ≈ +0 Iron in stats; enhancement is prestige, not a parallel power axis.

**Key approved upstream contracts (Inventory System specifically)**:
- Item Database: ItemID (uint readonly struct), StackLimit (Equipment=1, Consumable authored ≥1 up to 99), ItemCategory (Equipment|Consumable), SellPriceGold, GearSlot (7 values)
- Currency System owns gold — Inventory does not store gold
- Equipment System enforces one item per gear slot — Inventory does not
- Character Persistence saves ItemID refs only; Inventory loads from persistence and re-reads from Item Database

**How to apply:** All design recommendations must preserve the grind-town-sell-enhance loop pacing. Enhancement prestige principle must be maintained. No pay-to-win mechanics.
