# Equipment System

> **Status**: Approved (Pass 5 lean, 2026-05-22)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-22 (Pass 4: MergeResult code gap resolved — RejectedMergeError added for clean-rollback path; CriticalRollbackFailed reserved for rollback-fails path; CR-EQS-14 precondition 1 and rollback block updated; CR-EQS-15 crash recovery language clarified; RejectedEquipped documented as defense-in-depth; AC-EQS-24 updated; AC-EQS-26–27 added)
> **Implements Pillar**: Legendary Gear (primary), Earned Power (secondary)

## Overview

The Equipment System manages the seven gear slots of a player character (Weapon, Helmet, Chest, Legs, Boots, Ring, Necklace), each holding at most one equipped item at a time. When a player equips an item, the system moves it from the Inventory into the corresponding slot and registers its stat modifiers with Character Stats via `AddEquipmentModifier()`; when an item is unequipped or swapped, the system removes those modifiers and returns the item to Inventory. This makes the Equipment System the exclusive bridge between the Item Database's gear definitions and the Character Stats modifier stack — without it, loot has no mechanical effect. From the player's perspective, equipping gear is the primary expression of progression: choosing which Bronze Sword to replace with Iron, deciding which Ring to keep as stats grow, and seeing effective stats update immediately after every swap is the core feedback loop that gives earned drops their meaning.

## Player Fantasy

Equipped gear is a player's biography made visible. The moment a Dark Steel blade — Fire-elemental, enhanced to +7 — enters the Weapon slot and the Attack stat climbs, the player is not merely getting stronger; they are displaying what they survived to earn. Other players in the zone hub see the tier shape, the elemental glow, the enhancement halo — no tooltip required. Enhancement deepens this pride without inflating power: a +9 Bronze Sword is a declaration of dedication and risk, not a bypass past Iron. The system should always feel like: *I fought for this, I chose this, and the world can see it.*

## Detailed Design

### Core Rules

**CR-EQS-1: Slot Registry**
The Equipment System maintains an internal registry of 7 gear slots, one per GearSlot enum value (Weapon=0 through Necklace=6). The registry is a fixed-capacity array of `EquipmentSlotEntry` (readonly struct, length 7) indexed directly by `(int)GearSlot`. No Dictionary is used — enum values index the array directly, satisfying IL2CPP enum-key requirements.

```
readonly struct EquipmentSlotEntry {
    ItemID ItemId          // ItemID.Invalid = slot is empty
    bool   IsTransitioning // true during mid-swap modifier sequence
}
```

**CR-EQS-2: Slot Type Enforcement**
Each item's GearSlot value (from Item Database) must match the target slot. Equipping a Helmet into the Weapon slot returns `EquipResult.SlotMismatch` and makes no state changes.

**CR-EQS-3: Equip Requirement Check (Stat Gate)**
Before any equip operation, the Equipment System reads `EquipRequirementStat` and `EquipRequirementMin` from the item's record in Item Database and evaluates:

```
GetBaseStat(entityID, item.EquipRequirementStat) >= item.EquipRequirementMin
```

If the check fails, abort with `EquipResult.StatRequirementNotMet`. No Inventory calls, no Character Stats calls, no state changes.

Stat gate by slot category:
- **Weapon** — `EquipRequirementStat` is set per item in Item Database: `StatID.STR` for Warrior-type weapons, `StatID.INT` for Healer-type weapons
- **Armor** (Helmet, Chest, Legs, Boots) — `EquipRequirementStat = StatID.MaxHP` (base MaxHP from leveling progression, not current HP)
- **Accessories** (Ring, Necklace) — `EquipRequirementStat = null`; no check performed

The check always uses `GetBaseStat()`. Effective stats, equipment bonuses, and buff/debuff modifiers are excluded. If a character's base stat drops below the threshold after equipping (hypothetical — base stats only fall at level reset, which is not designed), the item remains equipped; the gate is enforced only at equip time.

**CR-EQS-4: Same-Item Guard**
If the incoming ItemID is identical to the ItemID already occupying the target slot, short-circuit with `EquipResult.NoChange`. No Inventory calls, no Character Stats calls, no stat events.

**CR-EQS-5: Equip Into Empty Slot**
Steps, executed in order:
1. Validate equip requirement (CR-EQS-3). Abort if not met.
2. Call `Inventory.MoveItemOut(inventorySlotIndex)` → `MoveItemOutResult`. If `Code ≠ Success`, abort. No Character Stats calls.
3. For each entry in `item.StatModifiers[]`: call `CharacterStats.AddEquipmentModifier(entityID, modifier.StatID, modifier.FlatBonus, modifier.PctBonus, itemID)`. If the modifier layer cap is reached (should never occur in correctly authored data): log CriticalError and halt.
4. Write ItemID to `_slots[(int)slot]`.
5. Update `equipmentAppearanceFlags` (CR-EQS-11).

**CR-EQS-6: Auto-Swap (Equip Into Occupied Slot)**
Steps, executed in order:
1. Apply CR-EQS-4 same-item guard first.
2. Validate equip requirement (CR-EQS-3). Abort if not met.
3. Check `Inventory.HasFreeSlot()`. Abort with `EquipResult.InventoryFull` if false.
4. Set `_slots[(int)slot].IsTransitioning = true`.
5. For each entry in old item's `StatModifiers[]`: call `CharacterStats.RemoveEquipmentModifier(entityID, modifier.StatID, oldItemID)`. `OnStatChanged` fires here — slot state is Transitioning; HUD subscribers must check `IsSlotTransitioning(slot)` before reading equipment state.
6. Call `Inventory.MoveItemIn(oldItemID)` → `MoveItemInResult`. If `success = false`: execute CR-EQS-8 failure recovery. Do not proceed to step 7. On success, store `MoveItemInResult.slotIndex` as `oldItemReturnedSlotIndex` for use in the step 7 rollback path.
7. Call `Inventory.MoveItemOut(newItemInventorySlotIndex)` → `MoveItemOutResult`. If `Code ≠ Success`: call `MoveItemOut(oldItemReturnedSlotIndex)` to reclaim old item from inventory, re-apply old item's modifiers via `AddEquipmentModifier` for each, clear IsTransitioning, abort with `EquipResult.InventoryError`.
8. For each entry in new item's `StatModifiers[]`: call `CharacterStats.AddEquipmentModifier(entityID, modifier.StatID, modifier.FlatBonus, modifier.PctBonus, newItemID)`.
9. Write new ItemID to `_slots[(int)slot]`. Clear IsTransitioning.
10. Update `equipmentAppearanceFlags` (CR-EQS-11).

**CR-EQS-7: Unequip (Occupied → Empty)**
Steps, executed in order:
1. Check `Inventory.HasFreeSlot()`. Abort with `EquipResult.InventoryFull` if false.
2. Call `ItemDatabase.GetItem(itemID)` → `EquipmentData`. If `GetItem()` returns null (item definition removed from database while equipped): log `CriticalError("Unequip GetItem null: {itemID}")`, skip modifier removal, clear slot (ItemID → Invalid, IsTransitioning → false), update appearance flags, return `EquipResult.Success`. Modifiers for an undefined item cannot be safely removed; clearing the slot is the least-corrupt outcome.
3. Set `_slots[(int)slot].IsTransitioning = true`.
4. For each entry in item's `StatModifiers[]`: call `RemoveEquipmentModifier`. `OnStatChanged` fires.
5. Call `Inventory.MoveItemIn(itemID)` → `MoveItemInResult`. If `success = false`: restore modifiers via `AddEquipmentModifier` for each, clear IsTransitioning, return `EquipResult.InventoryError`.
6. Clear `_slots[(int)slot]` (set ItemID to Invalid). Clear IsTransitioning.
7. Update `equipmentAppearanceFlags`.

**CR-EQS-8: MoveItemIn Failure Recovery (Auto-Swap)**
Triggered when step 6 of CR-EQS-6 fails — modifiers have already been removed but the old item cannot enter inventory:
1. Attempt `Inventory.ForceInsert(oldItemID)` — Inventory emergency path, bypasses `HasFreeSlot()` cap.
2. If ForceInsert succeeds: re-apply old item's modifiers via `AddEquipmentModifier`, clear IsTransitioning, return `EquipResult.InventoryError`. Old item is back in inventory; slot unchanged.
3. If ForceInsert fails: log `CriticalError("Item loss risk: {oldItemID} cannot return to inventory. Entity {entityID}.")`, alert monitoring, leave slot **Empty** (do not force-re-equip — persistence cannot track an item outside inventory or equipped slots), return `EquipResult.CriticalFailure`.

**CR-EQS-9: Transitioning Guard**
`IsSlotTransitioning(GearSlot): bool` is a public read method on the Equipment System. During any Transitioning window, effective stats for the affected slot are in an intermediate state. Subscribers to `CharacterStats.OnStatChanged` that read equipment data must check this method and defer rendering if true.

**CR-EQS-10: No On-Equip Trigger Effects at MVP**
Items produce no gameplay effects at equip time beyond stat modifier registration, `OnStatChanged` events, and the appearance flags wire update. If future items add on-equip triggers, an equip-trigger cooldown (value owned by Enhancement System GDD) must gate each trigger to prevent rapid-swap proc farming.

**CR-EQS-11: equipmentAppearanceFlags Encoding**
The byte is written to `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` after every equip, unequip, or swap. Bit layout:

| Bits | Field | Values |
|------|-------|--------|
| [7:6] | WeaponTier | Bronze=0b00, Iron=0b01, Steel=0b10, DarkSteel=0b11; 0b00 if no weapon |
| [5:3] | ElementType | None=000, Fire=001, Cold=010, Lightning=011, Poison=100; 101–111 reserved |
| [2:1] | PrestigeBand | NONE=0b00 (levels 0–4), VISIBLE_NO_GLOW=0b01 (levels 5–6; badge color, no glow at range), GLOW_LOW=0b10 (level 7; low glow visible at range), HIGH=0b11 (levels 8–10; high glow with particle pulse) |
| [0] | ArmorTier | 0 = highest equipped armor is Bronze or Iron; 1 = Steel or DarkSteel |

`PRESTIGE_MID_THRESHOLD = 5`, `ENHANCEMENT_GLOW_THRESHOLD = 7`, `PRESTIGE_HIGH_THRESHOLD = 8` — constants owned by Enhancement System GDD (Approved 2026-05-23, CR-ENH-12). ArmorTier = 0 if no armor equipped.

Encode: `flags = (byte)(((int)weaponTier & 0x03) << 6 | ((int)elementType & 0x07) << 3 | (prestigeBand & 0x03) << 1 | (armorBit & 0x01))`

**CR-EQS-12: Elemental Weapon Data Flow**
The Equipment System does not write elemental data to Character Stats. It exposes `GetEquippedWeaponItemID(): ItemID`, returning `ItemID.Invalid` if no weapon is equipped. Damage Calculation calls this method and reads `ElementType` and `ElementalDamage` directly from Item Database. `ItemID.Invalid` is treated as a non-elemental weapon with 0 elemental damage.

**CR-EQS-13: Input Validation**
All public equip/unequip entry points validate:
- `itemID != ItemID.Invalid` — reject silently in release builds; dev builds throw `InvalidOperationException`
- `(int)slot < 7` — reject before indexing `_slots`; IL2CPP does not bounds-check enum casts

---

**CR-EQS-15: IsTransitioning Non-Persistence and Crash Recovery (Added 2026-05-22)**

`IsTransitioning` is a runtime in-memory flag only — it is not saved to disk as part of Character Persistence. If the server crashes or restarts mid-swap:

1. On load, all `EquipmentSlotEntry` structs are reconstructed from the 7 saved `ItemID` values. `IsTransitioning` defaults to `false` (C# default struct initialization).
2. Any orphaned mid-swap state resolves to: the slot holds whatever `ItemID` was saved at last checkpoint. The critical crash window is between step 7 of CR-EQS-6 (new item removed from inventory) and step 9 (slot write complete) — if the server crashes here, the new item is absent from both inventory and the equipment slot on reload and requires manual monitoring recovery. The old item moved to inventory in step 6 (before the crash) will conflict with the saved slot state; the saved `ItemID` is authoritative on load, so the Equipment System re-applies the old item's modifiers and the inventory copy is flagged as a duplicate requiring manual resolution.
3. The Equipment System does not perform any startup cleanup pass — it trusts the saved `ItemID` as authoritative on load.

**Stat transaction API decision:** The Equipment System does NOT use `BeginStatTransaction()` / `EndStatTransaction()` from Character Stats at MVP. `OnStatChanged` fires twice during auto-swap (once on modifier removal, once on addition). The `IsSlotTransitioning` flag (CR-EQS-9) provides HUD debounce — subscribers must check this flag before reading equipment state. Other subscribers (Damage Calculation) treat the intermediate stat value as correct for the duration of the window (documented in EC-EQS-6). The transaction API remains available for future use if mid-swap intermediate stat events cause problems in downstream systems.

---

**CR-EQS-14: Accessory Merge (Added 2026-05-22)**

Players may upgrade an accessory by merging three identical accessories of the same level.

**Entry point:** `RequestMerge(int slotIdx1, int slotIdx2, int slotIdx3): MergeResult` — the Inventory UI passes the three inventory slot indices of the selected source items. The Equipment System does not discover slot indices internally.

Preconditions (all must hold; reject with no state change if any fail):

1. Three items with the same `ItemID` exist at the three passed inventory slot indices, and none are locked by the Enhancement System. (Defense-in-depth equipped check: items in equipment slots cannot simultaneously be in inventory slots under normal gameplay. If the system detects — through an internal consistency check — that any passed slot's item is also present in an equipment slot, reject with `RejectedEquipped`. A player with 4 identical accessories, 1 equipped and 3 in inventory, may merge the 3 inventory copies; the equipped copy is unaffected and does not trigger this check.)
2. The item's `GearSlot` is `Ring` or `Necklace` (only accessories are mergeable).
3. The item's `EquipmentData.MergeResultItemID` is non-null (i.e., AccessoryLevel < 10).
4. At least one free inventory slot exists to receive the merged result (checked via `HasFreeSlot()` before consuming source items).

Merge execution (atomic — all or nothing):
1. Remove three source items from inventory (`MoveItemOut` on each occupied slot).
2. Insert one item of `MergeResultItemID` into inventory (`ForceInsert(MergeResultItemID)`).
3. If `ForceInsert` returns false (inventory filled between check and execution): restore the three source items via `ForceInsert × 3` — step 1 freed 3 slots and step 2 consumed none, so all three rollback calls will succeed under normal conditions.
   - If all rollback `ForceInsert` calls succeed: return `MergeResult.RejectedMergeError`. No CriticalError logged — all items are accounted for.
   - If any rollback `ForceInsert` unexpectedly fails (memory/hardware fault): log `CriticalError("Merge rollback incomplete — {count} source item(s) not restored for entity {entityID}")`, alert monitoring, return `MergeResult.CriticalRollbackFailed`.

The defense-in-depth equipped check (precondition 1) executes before any item removal. Under normal gameplay an item cannot be simultaneously in inventory and an equipment slot; this check guards against inconsistent server state only.

`MAX_ACCESSORY_LEVEL = 10`. Items at +10 have `MergeResultItemID = null`. Merge UI must disable the merge action for items at +10. Server validates `MergeResultItemID != null` server-side regardless of UI state.

---

### States and Transitions

| State | `ItemId` | `IsTransitioning` | Modifier status in Character Stats |
|-------|----------|-------------------|-------------------------------------|
| **Empty** | `ItemID.Invalid` | false | None registered for this slot |
| **Occupied** | valid ItemID | false | Fully applied |
| **Transitioning** | valid ItemID (old item — slot not yet updated) | true | Partially applied (mid-operation) |

| Transition | Trigger | Guards | Rule |
|------------|---------|--------|------|
| Empty → Occupied | Player equips | Stat req met; MoveItemOut success | CR-EQS-5 |
| Occupied → Occupied | Auto-swap | Stat req met; HasFreeSlot=true; MoveItemIn success | CR-EQS-6 |
| Occupied → Empty | Player unequips | HasFreeSlot=true; MoveItemIn success | CR-EQS-7 |
| Occupied → Empty | Swap critical failure | MoveItemIn + ForceInsert both fail | CR-EQS-8 step 3: Empty is less corrupt than phantom Occupied |
| Any → unchanged | Same item | ItemID matches slot | CR-EQS-4: NoChange |
| Any → unchanged | Stat req not met | BaseStat < EquipRequirementMin | CR-EQS-3: abort before any mutation |
| Any → unchanged | Inventory full | HasFreeSlot=false | Abort before state mutation |

---

### Interactions with Other Systems

| System | Direction | What flows | Interface |
|--------|-----------|-----------|-----------|
| Item Database | Read | `GearSlot`, `GearTier`, `StatModifiers[]`, `EquipRequirementStat`, `EquipRequirementMin`, `ElementType`, `ElementalDamage` | `GetItem(ItemID): EquipmentData` |
| Character Stats | Write | Modifier registration / removal | `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)` / `RemoveEquipmentModifier(EntityID, StatID, ItemID)` |
| Character Stats | Read | Base stat for equip gate | `GetBaseStat(EntityID, StatID): float` |
| Inventory System | Read / Write | Item transfer, capacity check | `HasFreeSlot(): bool` / `MoveItemOut(slotIndex): MoveItemOutResult` / `MoveItemIn(ItemID): MoveItemInResult` / `ForceInsert(ItemID): bool` *(emergency)* |
| Networking | Write | Zone appearance byte | `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` — written on every slot change |
| Damage Calculation | Provides | Equipped weapon ItemID | `GetEquippedWeaponItemID(): ItemID` |

**Cross-document impacts from this section:**
- *Item Database GDD*: Must add `EquipRequirementStat: StatID?` and `EquipRequirementMin: float` fields to `EquipmentData` schema (CR-EQS-3). Accessories: `EquipRequirementStat = null`.
- *Inventory System GDD*: Must add `ForceInsert(ItemID): bool` emergency path (CR-EQS-8). `MoveItemOut` return type must be extended to `MoveItemOutResult` to distinguish empty vs. locked slot (current `ItemID` return is ambiguous).

## Formulas

### F-EQS-1: Effective Stat Contribution (Inherited from Character Stats)

The Equipment System does not own a separate formula — it feeds the existing Character Stats formula by registering flat bonuses. For reference:

```
EffectiveStat = clamp(BaseStat + ΣFlatEquip + ΣFlatBuff, StatMin, StatMax)
```

Variables:
- `ΣFlatEquip` = sum of all flat bonuses across all equipped items' `StatModifiers[]`
- `ΣPctEquip` = 0 for all MVP equipment (pctBonus=0.0f always)
- Source: character-stats.md F-1

At full load (7 slots × 2 modifiers = 14 entries), `ΣFlatEquip` is the sum of up to 14 flat values across all registered equipment modifiers.

---

### F-EQS-2: Flat Bonus Ranges by Tier (OQ-1 Resolution)

Authoring bounds for flat bonus values on each equipment item. Validated against Enhancement System F-ENH-3 parity constraint (2026-05-23). Item Database authors must stay within these ranges.

| Tier | Attack-class modifiers | HP/Defense-class modifiers | Example slots |
|------|----------------------|--------------------------|---------------|
| Bronze | 8 – 12 | 25 – 40 | Starting gear |
| Iron | 22 – 28 | 55 – 75 | First upgrade |
| Steel | 32 – 42 | 110 – 145 | Midgame |
| Dark Steel | 58 – 68 | 195 – 225 | Endgame |

**Slot-to-stat-class affinity** (one primary stat per slot; secondary slot is authoring-flexible within range):

| Slot | Primary stat class | Notes |
|------|--------------------|-------|
| Weapon | Attack | Main source of ΣFlatEquip for Attack |
| Helmet | HP/Defense | |
| Chest | HP/Defense | |
| Legs | HP/Defense | |
| Boots | HP/Defense | |
| Ring | Attack or HP/Defense | AccessoryLevel system — see F-EQS-5. Not tier-based. No stat gate. |
| Necklace | Attack or HP/Defense | AccessoryLevel system — see F-EQS-5. Not tier-based. No stat gate. |

Each weapon/armor item has at most 2 modifier slots. The first modifier follows the slot's primary stat class; the second may be primary or secondary. Ring and Necklace bonuses are determined by AccessoryLevel (F-EQS-5), not this table.

**Full-loadout estimates (weapon/armor only — 5 slots × 2 modifiers = 10 entries; accessories use F-EQS-5):**
- Full Dark Steel Attack contribution from weapon/armor slots (≈4–5 Attack-class entries): ~250–340 flat Attack
- Full Dark Steel HP contribution from weapon/armor slots (≈5 HP/Defense-class entries): ~975–1,125 flat HP
- Accessory contribution (Ring + Necklace at +10 max, both Attack-class): +60 flat Attack additional (see F-EQS-5)
- Accessory contribution (Ring + Necklace at +10 max, both HP-class): +200 flat HP additional (see F-EQS-5)

---

### F-EQS-3: Stat Requirement Thresholds by Tier

Minimum base stat required to equip each tier. Uses `GetBaseStat()` (progression-only base, no equipment or buff modifiers). Values are **provisional** — must be calibrated against the Character Progression GDD (not yet authored) to confirm natural progression reaches these thresholds at the correct zone levels.

**Weapon equip requirements (STR for Warrior weapons, INT for Healer weapons):**

| Tier | EquipRequirementMin | Target character level |
|------|--------------------|-----------------------|
| Bronze | 0 (no gate) | L1+ |
| Iron | 30 | ~L15 |
| Steel | 55 | ~L30 |
| Dark Steel | 85 | ~L45 |

**Armor equip requirements (MaxHP base):**

| Tier | EquipRequirementMin | Target character level |
|------|--------------------|-----------------------|
| Bronze | 0 (no gate) | L1+ |
| Iron | 350 | ~L15 |
| Steel | 750 | ~L30 |
| Dark Steel | 1,200 | ~L45 |

**Accessories:** No equip requirement for any tier.

Example: Dark Steel Sword requires `GetBaseStat(StatID.STR) >= 85`. A Warrior at ~L45 must reach 85 base STR through natural progression. If the Character Progression GDD produces different L45 STR values, these thresholds must be revised together.

---

### F-EQS-4: Prestige Constraint (Enhancement System Anchor)

The enhancement prestige principle: **+5 Bronze ≈ +0 Iron** in effective stats. Constraint on the relationship between flat bonus tiers and the Enhancement System bonus curve:

```
(TierBonus_Iron × 2) ≈ (TierBonus_Bronze × 2) + (EnhancementBonus × MAX_ENHANCE_PER_TIER)
```

Rearranged:
```
EnhancementBonus ≈ (TierBonus_Iron − TierBonus_Bronze) × (2 / MAX_ENHANCE_PER_TIER)
```

Using Attack-class midpoints (Bronze=10, Iron=19), assuming `MAX_ENHANCE_PER_TIER` = 5 (unconfirmed — owned by Enhancement System GDD):
```
EnhancementBonus ≈ (19 − 10) × (2 / 5) = 3.6 flat Attack per enhancement level
```

The Enhancement System GDD must confirm `MAX_ENHANCE_PER_TIER` and the bonus curve. If confirmed values differ from these estimates, F-EQS-2 flat bonus ranges and/or the enhancement curve must be revised together — these two GDDs are co-constrained.

**Boundary constraint (must hold at all valid item values, not just midpoints):**

```
Bronze_max_per_item + EnhancementBonus × MAX_ENHANCE_PER_TIER < Iron_min_per_item
```

Where `X_per_item = X_per_modifier × modifiers_per_item`. Using current F-EQS-2 values (Bronze_max=12, Iron_min=16, 2 modifiers, estimated EnhancementBonus=3.6, MAX_ENHANCE_PER_TIER=5 assumed):

`12×2 + 3.6×5 = 24 + 18 = 42` vs `Iron_min_per_item = 16×2 = 32`

**This fails at the boundary.** Current F-EQS-2 ranges violate the prestige constraint at Bronze_max + full enhancement vs Iron_min combinations. Resolution options when Enhancement System GDD is authored:

- **Option A (narrow ranges):** Raise `Iron_min` to ≥ `Bronze_max + ceil(EnhancementBonus × MAX_ENHANCE_PER_TIER / 2)` per modifier. With estimated values: Iron_min ≥ 12 + ceil(18/2) = 21 per modifier. Iron range would become ≈ 22–28.
- **Option B (cap enhancement):** Constrain `MAX_ENHANCE_PER_TIER × EnhancementBonus < (Iron_min − Bronze_max) × 2`. With current ranges (gap = 4): max total enhancement = 8. At MAX_ENHANCE_PER_TIER=5: EnhancementBonus ≤ 1.6 per level.

**This constraint is BLOCKING for Enhancement System GDD authoring.** Tag this document as a dependency of the Enhancement System GDD.

---

### F-EQS-5: Accessory Flat Bonus Scale (Added 2026-05-22)

Accessories (Ring, Necklace) do not use the tier-based bonus table (F-EQS-2). Each accessory type exists at 10 discrete levels (+1 through +10), where each level is a distinct `ItemID` in the Item Database. Flat bonus scales linearly with level:

```
AccessoryFlatBonus = BaseBonus × AccessoryLevel
```

Variables:
- `BaseBonus` — per-item value defined in Item Database; authoring values below are **provisional**
- `AccessoryLevel` ∈ {1, 2, …, MAX_ACCESSORY_LEVEL}; `MAX_ACCESSORY_LEVEL = 10`

| AccessoryLevel | Attack-class flat bonus | HP/Defense-class flat bonus |
|----------------|------------------------|-----------------------------|
| +1  | 3  | 10  |
| +2  | 6  | 20  |
| +3  | 9  | 30  |
| +4  | 12 | 40  |
| +5  | 15 | 50  |
| +6  | 18 | 60  |
| +7  | 21 | 70  |
| +8  | 24 | 80  |
| +9  | 27 | 90  |
| +10 | 30 | 100 |

**L1 exploit closure:** L1 base AttackPower = 30. A +10 Attack Ring grants +30, for a total of 60 (+100%). A +1 Ring (realistic early drop) grants +3, for a total of 33 (+10%). Neither breaks the Earned Power pillar. Reaching +10 requires 3^9 = 19,683 base rings — effectively late-game only.

**Upgrade path:** see CR-EQS-14 (Accessory Merge mechanic).

**Item Database note:** Each Ring/Necklace at each level is a distinct `ItemID`. The Item Database `EquipmentData` schema carries `MergeResultItemID: ItemID?` — the ItemID produced when 3 of this accessory are merged. `null` at +10 (maximum; no further upgrades). See item-database.md.

## Edge Cases

**EC-EQS-1: Inventory Full on Unequip**
Situation: Player attempts to unequip an item but all inventory slots are occupied.
Rule: CR-EQS-7 step 1 checks `HasFreeSlot()` before any state mutation. If false, return `EquipResult.InventoryFull`. No modifiers removed, no slot state changed. The item remains equipped. UI surfaces: "Inventory full — make room before unequipping."

**EC-EQS-2: Stat Requirement Not Met at Equip Time**
Situation: Player loots a Dark Steel Sword (EquipRequirementMin=85 STR) but has 62 base STR.
Rule: CR-EQS-3 rejects the equip before any Inventory or Character Stats calls. The item stays in Inventory. UI surfaces the specific stat shortfall: "Requires 85 STR (you have 62)." The item is not locked — the player may attempt again when the stat increases.

**EC-EQS-3: Stat Below Threshold While Item Is Equipped**
Situation: A character has a Dark Steel Sword equipped (85 STR requirement), then their base STR drops (hypothetical — no base-stat reduction mechanics exist at MVP).
Rule: The item remains equipped. The stat gate is enforced only at equip time, not as a continuous upkeep check. This case cannot occur in MVP, but the rule is explicit to guard future systems.

**EC-EQS-4: MoveItemOut on a Locked Inventory Slot**
Situation: A player attempts to equip an item whose inventory slot is locked (under Enhancement operation). `MoveItemOut` returns a failed result.
Rule: CR-EQS-5 step 2 and CR-EQS-6 step 7 both abort on `MoveItemOutResult.Code ≠ Success`. No Character Stats calls are made. The item stays in the locked slot. UI surfaces: "Item is locked — complete or cancel its current operation first."
Note: `MoveItemOut` must return `MoveItemOutResult` (Code: Success / SlotEmpty / SlotLocked) to allow the Equipment System to distinguish locked from empty. The current Inventory System GDD's `MoveItemOut` return type is `ItemID` — extending it is a cross-document impact noted in Interactions.

**EC-EQS-5: Modifier Layer Cap Reached**
Situation: A data authoring error creates an item with more than 2 modifiers, pushing the total past the 16-entry cap in Character Stats.
Rule: Character Stats drops the modifier past the cap and logs CriticalError in dev builds. The Equipment System detects a partial equip and also logs CriticalError. The item remains equipped with incomplete stat contribution. This is a data authoring defect — the Item Database CI validator must enforce the 2-modifier cap so this situation never reaches a production build.

**EC-EQS-6: Swap Mid-Combat — Transient Stat Drop**
Situation: A player swaps a weapon mid-combat. Between `RemoveEquipmentModifier` and `AddEquipmentModifier`, Attack effective stat is transiently lower.
Rule: `OnStatChanged` fires twice. Damage Calculation resolving between the two events uses the lower intermediate Attack value — this is correct behavior. The Transitioning flag (CR-EQS-9) exists for HUD rendering deferral only, not damage calculation mitigation.

**EC-EQS-7: Swap Mid-Combat — MaxHP Drop Below Current HP**
Situation: A player swaps high-HP armor mid-combat. `RemoveEquipmentModifier` for MaxHP runs; transient effective MaxHP may fall below current HP.
Rule: Character Stats EC-18 handles this: when effective MaxHP drops below current HP, current HP is immediately clamped to new effective MaxHP, firing `OnHealthChanged`. The Equipment System takes no additional action.

**EC-EQS-8: Equipping Identical Item to Same Slot (Race Condition)**
Situation: UI sends an equip request for an item already in the target slot (e.g., double-tap race).
Rule: CR-EQS-4 short-circuits immediately. `EquipResult.NoChange` returned. No Inventory calls, no stat events, no appearance flag recompute.

**EC-EQS-9: No Weapon Equipped — Elemental Data Flow**
Situation: A character has no weapon equipped.
Rule: `GetEquippedWeaponItemID()` returns `ItemID.Invalid`. Damage Calculation treats this as a non-elemental weapon with 0 elemental damage. The `equipmentAppearanceFlags` WeaponTier and ElementType bits are both 0 (no-weapon encoding).

**EC-EQS-10: MoveItemIn Failure After Modifier Removal**
Situation: `HasFreeSlot()` returned true but `MoveItemIn` fails at execution time (concurrent inventory write race).
Rule: CR-EQS-8 executes. `ForceInsert` is attempted. If it succeeds, old modifiers are restored and the swap rolls back cleanly. If `ForceInsert` also fails, the slot is set to Empty, CriticalError is logged, and the player is notified. The orphaned item is a monitoring event requiring manual resolution — it must not manifest as a silent item loss.

**EC-EQS-11: All Slots Empty — Appearance Flags**
Situation: A fresh character with all slots empty.
Rule: `equipmentAppearanceFlags = 0x00`. Other players see the default unequipped appearance. 0x00 is an unambiguous "fully unequipped" state at all bit groups.

## Dependencies

**Upstream dependencies** (systems this GDD depends on):

| System | GDD status | What this GDD takes from it |
|--------|-----------|----------------------------|
| Item Database | Approved ✓ | `GearSlot` enum, `GearTier` enum, `EquipmentData` schema (`StatModifiers[]`, `ElementType`, `ElementalDamage`, `EquipRequirementStat: StatID?`, `EquipRequirementMin: float`, `MergeResultItemID: ItemID?`) — additions written 2026-05-22 |
| Inventory System | Approved ✓ | `HasFreeSlot()`, `MoveItemOut(slotIndex): MoveItemOutResult`, `MoveItemIn(ItemID)`, `ForceInsert(ItemID): bool` — `MoveItemOutResult` extension and `ForceInsert` written 2026-05-22 |
| Character Stats | Approved ✓ | `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)`, `RemoveEquipmentModifier(EntityID, StatID, ItemID)`, `GetBaseStat(EntityID, StatID)`; equipment modifier layer (16 entries); EC-18 MaxHP clamping |
| Networking Core | Approved ✓ | `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags: byte` wire field — Equipment System writes this field |
| Networking Wire Protocol | Approved ✓ | `EquipRequest` (client → server), `EquipResult` (server → client), `AppearanceChangedEvent` (server → zone) — wire schemas added 2026-05-22 |
| Enhancement System | **Approved (Pass 4 lean, 2026-05-23)** | `PRESTIGE_MID_THRESHOLD=5`, `PRESTIGE_HIGH_THRESHOLD=8`, `ENHANCEMENT_GLOW_THRESHOLD=7`, `MAX_ENHANCEMENT_LEVEL=10`; `IEnhancementBonusProvider` (GetFlatBonus, GetElementalBonus); F-ENH-3 Iron range amendment applied to F-EQS-2 |

**Downstream dependents** (systems that depend on this GDD):

| System | GDD status | What it takes from this GDD |
|--------|-----------|------------------------------|
| Inventory UI | Not Started (#30) | `IsSlotTransitioning(GearSlot): bool`, `GetEquipmentSlotState(GearSlot)`, equip/unequip result codes (`EquipResult`) — needed to render the equipment panel and react to swap events. Player fantasy (Section B) social-visibility anchor depends on appearance rendering landing on iOS. |
| Damage Calculation | Not yet mapped | `GetEquippedWeaponItemID(): ItemID` — reads weapon item from Item Database for elemental damage resolution |
| Character Persistence | Not yet mapped | Saves `_slots[(int)slot].ItemID` for all 7 slots. On load, Equipment System re-registers modifiers from Item Database. Equipment state must be fully reconstructible from ItemIDs alone. |

**Bidirectionality:**
- Item Database GDD references Equipment System as a consumer of `EquipmentData` ✓
- Character Stats GDD references Equipment System as the owner of the equipment modifier layer (line 188) ✓
- Inventory System GDD references Equipment System as the caller of `MoveItemOut`/`MoveItemIn`/`ForceInsert` — updated 2026-05-22 ✓

## Tuning Knobs

| Knob | Current value | Safe range | Gameplay effect |
|------|--------------|-----------|-----------------|
| **Bronze Attack flat bonus** | 8 – 12 per modifier | 5 – 20 | Lower end → Bronze feels weak; upper end → Iron tier feels redundant. Constrained by F-EQS-4 prestige equation. |
| **Iron Attack flat bonus** | 22 – 28 per modifier | 20 – 35 | Gap vs. Bronze drives tier-upgrade motivation. Range corrected 2026-05-23 (was 16–22) — F-ENH-3 parity constraint requires Iron midpoint (25) ≈ Bronze midpoint (10) + 5×BonusPerLevel[Bronze] (15) = 25. |
| **Steel Attack flat bonus** | 32 – 42 per modifier | 25 – 60 | Midgame power bump. Calibrate so Steel content is only clearable in Steel+ gear. |
| **Dark Steel Attack flat bonus** | 58 – 68 per modifier | 50 – 90 | Endgame ceiling. Upper bound constrained by StatMax clamp — excessive values hit the cap and become invisible to players. |
| **Bronze HP flat bonus** | 25 – 40 per modifier | 15 – 60 | Same tier-gap logic, HP axis. |
| **Iron HP flat bonus** | 55 – 75 per modifier | 40 – 110 | |
| **Steel HP flat bonus** | 110 – 145 per modifier | 90 – 200 | |
| **Dark Steel HP flat bonus** | 195 – 225 per modifier | 150 – 300 | Upper bound: `BaseStat(MaxHP) + ΣFlatEquip` must not exceed `StatMax(MaxHP)` at L60 full Dark Steel load. |
| **Iron Weapon EquipRequirementMin (STR)** | 30 | 20 – 45 | Lower → tier-gating loses meaning; upper → players can't equip Iron until well past the Iron-drop zone. |
| **Steel Weapon EquipRequirementMin (STR)** | 55 | 40 – 75 | |
| **Dark Steel Weapon EquipRequirementMin (STR)** | 85 | 65 – 110 | User-anchored at 85. Must be reachable through natural L45 progression. |
| **Iron Armor EquipRequirementMin (MaxHP)** | 350 | 250 – 500 | |
| **Steel Armor EquipRequirementMin (MaxHP)** | 750 | 550 – 1,000 | |
| **Dark Steel Armor EquipRequirementMin (MaxHP)** | 1,200 | 900 – 1,600 | |
| **PRESTIGE_MID_THRESHOLD** | 5 (owned by Enhancement System GDD) | [3, 6] | Enhancement level at which PrestigeBand transitions NONE → VISIBLE_NO_GLOW (badge color activates). |
| **ENHANCEMENT_GLOW_THRESHOLD** | 7 (owned by Enhancement System GDD) | [6, 9] | Enhancement level at which PrestigeBand transitions VISIBLE_NO_GLOW → GLOW_LOW (glow visible at zone range). |
| **PRESTIGE_HIGH_THRESHOLD** | 8 (owned by Enhancement System GDD) | [7, 10] | Enhancement level at which PrestigeBand transitions GLOW_LOW → HIGH (high-intensity glow with particle pulse). |

**Revision dependencies:** Flat bonus ranges re-evaluated against Enhancement System GDD (Approved 2026-05-23) — Iron range corrected to 22–28 per F-ENH-3. All stat requirement thresholds must be re-evaluated against the Character Progression GDD — they must be naturally reachable at the target level ranges.

## Visual/Audio Requirements

Visual requirements are primarily owned by the Inventory UI GDD. The Equipment System's contribution is limited to data provision:

**Visual (owned by Equipment System):** `equipmentAppearanceFlags: byte` — recomputed and written to `ZoneStateSnapshotEntityEntry` after every slot change. The rendering layer reads this byte to determine tier shape (WeaponTier bits), elemental glow (ElementType bits), enhancement halo (PrestigeBand bits), and armor silhouette (ArmorTier bit). Encoding defined in CR-EQS-11.

**Audio:** No audio events are owned by the Equipment System at MVP. Equip and unequip sound effects are triggered by the Inventory UI layer in response to `EquipResult` codes. No audio cues originate from within the Equipment System itself.

## UI Requirements

All UI surfaces are owned by the Inventory UI GDD. The Equipment System exposes the following data surface for UI consumption:

- `GetEquipmentSlotState(GearSlot): (ItemID, SlotState)` — slot contents and current state (Empty / Occupied / Transitioning)
- `IsSlotTransitioning(GearSlot): bool` — HUD subscribers defer rendering while true
- `EquipResult` codes and associated data:
  - `NoChange` — no-op; UI does nothing
  - `InventoryFull` — UI surfaces: "Inventory full — make room before unequipping"
  - `StatRequirementNotMet(StatID, required, actual)` — UI surfaces: "Requires {required} {statName} (you have {actual})". `actual` = `GetBaseStat(entityID, StatID)` — the base stat value only, not effective stat. Buffs that bring effective stat above `EquipRequirementMin` do not satisfy the gate (CR-EQS-3, AC-EQS-6). The UI must display base stat in the shortfall message, not the buffed value, to avoid misleading the player.
  - `SlotMismatch` — UI surfaces: item tooltip shows the correct slot type
  - `InventoryError` — UI surfaces a generic equip failure with retry option
  - `CriticalFailure` — UI surfaces: "Equip failed — please try again. If the issue persists, contact support."
- `RequestMerge(int slotIdx1, int slotIdx2, int slotIdx3): MergeResult` — merge trigger; Inventory UI passes the three inventory slot indices of the selected source items. `MergeResult` codes:
  - `Success` — merge complete; 3 source items removed, 1 merged result item added to inventory
  - `RejectedEquipped` — defense-in-depth: a passed slot's item was detected simultaneously in an equipment slot (inconsistent server state; unreachable through normal gameplay). No state change.
  - `RejectedMaxLevel` — item is at `MAX_ACCESSORY_LEVEL` (`MergeResultItemID = null`); no state change
  - `RejectedInventoryFull` — no free inventory slot for merge result; no state change
  - `RejectedMergeError` — `ForceInsert(MergeResultItemID)` failed; all 3 source items restored via rollback; no item loss; no CriticalError
  - `CriticalRollbackFailed` — `ForceInsert(MergeResultItemID)` failed AND one or more rollback `ForceInsert` calls also failed; CriticalError logged; monitoring alert sent; one or more source items unrecovered

The equipment panel layout, slot visual design, tap/drag interaction model, and stat comparison overlays are specified in the Inventory UI GDD — not here.

## Acceptance Criteria

**AC-EQS-1 [BLOCKING]: Equip into empty slot registers all modifiers**
Setup: Character with all slots empty. Item X has `StatModifiers[] = [{StatID.STR, +10}, {StatID.DEF, +8}]`. `GetEffectiveStat(STR)` = 50, `GetEffectiveStat(DEF)` = 30. Equip requirement met.
Action: Equip Item X into Weapon slot.
Pass: `GetEffectiveStat(STR)` = 60, `GetEffectiveStat(DEF)` = 38. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemX, Occupied)`. Inventory no longer contains Item X.

**AC-EQS-2 [BLOCKING]: Auto-swap moves old item to inventory and registers new item's modifiers**
Setup: Iron Sword (ItemA, +18 STR) equipped in Weapon slot. Steel Sword (ItemB, +35 STR, `EquipRequirementMin=55 STR`) in inventory. Character `GetBaseStat(STR)` = 60.
Action: Equip ItemB into Weapon slot.
Pass: Net `GetEffectiveStat(STR)` change = +17 (+35 − 18). ItemA in inventory. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemB, Occupied)`. ItemB no longer in inventory.

**AC-EQS-3 [BLOCKING]: Unequip returns item to inventory and removes modifiers**
Setup: Item Y (`StatModifiers: +12 MaxHP`) equipped in Helmet slot. `GetEffectiveStat(MaxHP)` = 512. Inventory has a free slot.
Action: Unequip Helmet slot.
Pass: `GetEffectiveStat(MaxHP)` = 500. ItemY in inventory. `GetEquipmentSlotState(GearSlot.Helmet)` = `(Invalid, Empty)`.

**AC-EQS-4 [BLOCKING]: Stat requirement gate — equip rejected below threshold**
Setup: Dark Steel Sword (`EquipRequirementStat=STR`, `EquipRequirementMin=85`). Character `GetBaseStat(STR)` = 62.
Action: Attempt to equip.
Pass: `EquipResult.StatRequirementNotMet`. Sword in inventory. Slot state unchanged. No `OnStatChanged` events fired.

**AC-EQS-5 [BLOCKING]: Stat requirement gate — equip accepted at threshold**
Setup: Character with `GetBaseStat(STR)` = 85 (at requirement). Dark Steel Sword (`EquipRequirementStat=STR`, `EquipRequirementMin=85`). Free inventory slot available.
Action: Equip Dark Steel Sword into Weapon slot.
Pass: No `EquipResult.StatRequirementNotMet`. Sword in Weapon slot (`GetEquipmentSlotState(GearSlot.Weapon).ItemId` = sword's ItemID). Sword removed from inventory. `GetEffectiveStat(STR)` increases by sword's flat STR modifier.

**AC-EQS-6 [BLOCKING]: Stat gate uses base stat only — buffs do not satisfy the requirement**
Setup: Dark Steel Sword (`EquipRequirementMin=85 STR`). Character `GetBaseStat(STR)` = 62. A buff adds +30 flat STR. `GetEffectiveStat(STR)` = 92.
Action: Attempt to equip.
Pass: `EquipResult.StatRequirementNotMet`. Buff does not satisfy the gate. Sword stays in inventory.

**AC-EQS-7 [BLOCKING]: Inventory full blocks unequip**
Setup: Item equipped. Inventory full (no free slot).
Action: Attempt to unequip.
Pass: `EquipResult.InventoryFull`. No modifiers removed. Slot state unchanged.

**AC-EQS-8 [BLOCKING]: Same-item guard prevents redundant operations**
Setup: Iron Sword (ItemA, known modifier value) equipped in Weapon slot. Record `GetEffectiveStat(STR)` before action.
Action: Equip ItemA into Weapon slot again.
Pass: `EquipResult.NoChange` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemA, Occupied)` — unchanged. `GetEffectiveStat(STR)` = same value as before action (no double-application). ItemA still in Weapon slot, not moved to inventory.

**AC-EQS-9 [BLOCKING]: Slot type enforcement — mismatched slot rejected**
Setup: A Helmet item (GearSlot=Helmet).
Action: Attempt to equip into Weapon slot.
Pass: `EquipResult.SlotMismatch`. No state changes.

**AC-EQS-10 [BLOCKING]: IsSlotTransitioning is false after swap completes**
Setup: Weapon slot occupied (ItemA). Inventory has a free slot. ItemB (stat requirement met) in inventory.
Action: Equip ItemB into Weapon slot (triggers auto-swap).
Pass: `EquipResult.Success`. `IsSlotTransitioning(GearSlot.Weapon)` = false after the call returns. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemB, Occupied)`.
Unit-test note: The intermediate true state (between `RemoveEquipmentModifier` and `AddEquipmentModifier`) requires test-stub instrumentation. Inject a spy on `AddEquipmentModifier` and assert `IsSlotTransitioning` = true inside the callback; assert false after the equip call returns.

**AC-EQS-11 [BLOCKING]: equipmentAppearanceFlags encodes correctly**
Setup: Equip a Dark Steel sword (WeaponTier=3, ElementType=Fire=1, PrestigeBand=None=0). Steel or DarkSteel armor equipped (ArmorTier=1).
Pass: `equipmentAppearanceFlags = 0b11_001_00_1 = 0xC9`. Verify decode: WeaponTier=DarkSteel, ElementType=Fire, PrestigeBand=None, ArmorTier=upper.

**AC-EQS-12 [BLOCKING]: GetEquippedWeaponItemID returns Invalid when no weapon equipped**
Setup: Weapon slot empty.
Pass: `GetEquippedWeaponItemID()` = `ItemID.Invalid`. No exception thrown.

**AC-EQS-13 [BLOCKING]: GetEquippedWeaponItemID returns correct ItemID when weapon is equipped**
Setup: Weapon slot starts empty (`GetEquippedWeaponItemID()` = `ItemID.Invalid` confirmed). Equip a Weapon item (ItemX, stat requirement met) into Weapon slot.
Pass: `GetEquippedWeaponItemID()` = ItemX's `ItemID`. No exception thrown. Value persists across multiple calls (not consumed on read).

**AC-EQS-14 [BLOCKING]: All 7 slots can be equipped independently and contribute to effective stats**
Setup: Character with all slots empty. Prepare 7 items (one per GearSlot), all stat requirements met. Each item has exactly 1 modifier of a known stat and value. Record baseline `GetEffectiveStat` for each affected stat.
Action: Equip all 7 items, one per slot, in any order.
Pass: All 7 equip calls return success (no error codes). `GetEquipmentSlotState(slot)` = Occupied for all 7 GearSlot values (0–6). For each item's modifier, `GetEffectiveStat(statID)` = baseline + that item's modifier value. Total effective stat delta = sum of all 7 modifier values for each stat axis.

**AC-EQS-15 [BLOCKING]: Armor HP gate — Iron Armor rejected below 350 base MaxHP**
Setup: Character `GetBaseStat(MaxHP)` = 280. Iron Helmet (`EquipRequirementStat=MaxHP`, `EquipRequirementMin=350`).
Action: Attempt to equip.
Pass: `EquipResult.StatRequirementNotMet`. Helmet stays in inventory.

**AC-EQS-16 [BLOCKING]: Auto-swap MoveItemIn failure with ForceInsert success — slot rolls back cleanly**
Note: Requires unit-test injection (stub `MoveItemIn` to return `success=false`, stub `ForceInsert` to return `true`).
Setup: Weapon slot occupied (ItemA). ItemB in inventory.
Action: Trigger auto-swap to ItemB (CR-EQS-6 path). `MoveItemIn` fails at step 6; `ForceInsert` succeeds.
Pass: `EquipResult.InventoryError` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemA, Occupied)` — slot unchanged. `GetEffectiveStat` reflects ItemA's modifiers (re-applied). `IsSlotTransitioning(GearSlot.Weapon)` = false. No item lost.

**AC-EQS-17 [BLOCKING]: Auto-swap MoveItemIn failure with ForceInsert failure — CriticalFailure and slot Empty**
Note: Requires unit-test injection (stub both `MoveItemIn` and `ForceInsert` to fail).
Setup: Weapon slot occupied (ItemA). ItemB in inventory.
Action: Trigger auto-swap to ItemB. Both `MoveItemIn` and `ForceInsert` fail.
Pass: `EquipResult.CriticalFailure` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemID.Invalid, Empty)` — slot set to Empty (CR-EQS-8 step 3). CriticalError log entry exists and contains oldItemID and entityID. `IsSlotTransitioning(GearSlot.Weapon)` = false.

**AC-EQS-18 [BLOCKING]: No on-equip trigger effects beyond stat registration at MVP**
Setup: Any item with stat requirements met. Monitor for any game events beyond `OnStatChanged` and `equipmentAppearanceFlags` write.
Action: Equip item into any empty slot.
Pass: Observable side-effects are exactly: `OnStatChanged` events for each registered modifier; `equipmentAppearanceFlags` updated on the `ZoneStateSnapshotEntityEntry`. No damage events, healing events, status effect applications, or sound cues originate from the Equipment System itself as a direct result of equipping.

**AC-EQS-19 [BLOCKING]: Input validation — ItemID.Invalid rejected without state mutation**
Setup: Any equipment slot (Weapon slot, any state).
Action: Call equip with `ItemID.Invalid` as the item argument.
Pass: Request rejected. No exception in release builds. `GetEquipmentSlotState(GearSlot.Weapon)` unchanged. No Inventory or Character Stats calls observable.

**AC-EQS-20 [BLOCKING]: Input validation — out-of-range GearSlot rejected without array access**
Setup: Any valid Weapon item in inventory.
Action: Call equip with `(GearSlot)7` (value 7; valid range is 0–6).
Pass: Request rejected before any array index operation. No `IndexOutOfRangeException` in release or dev builds. No state change. No Inventory or Character Stats calls.

**AC-EQS-21 [BLOCKING]: Accessory merge — 3 identical accessories produce one next-level item**
Setup: Player inventory contains exactly 3 items of the same `ItemID` (e.g., Iron Ring of Attack +1). All three are in inventory (not equipped, not locked). `MergeResultItemID` for this item is non-null (level < +10). At least one free inventory slot exists.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)` where each index is the inventory slot of the corresponding source item.
Pass: Returns `MergeResult.Success`. All 3 source items removed from inventory. Exactly 1 item of `MergeResultItemID` added to inventory. Total inventory item count changes by −2 (3 removed, 1 added).

**AC-EQS-22 [BLOCKING]: Accessory merge rejected at max level**
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at `MAX_ACCESSORY_LEVEL` = +10). `MergeResultItemID` for this item is `null`. No items equipped. Free inventory slot exists.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`.
Pass: Returns `MergeResult.RejectedMaxLevel`. No state change. Inventory still contains all 3 source items.

**AC-EQS-23 [BLOCKING]: Accessory merge rejected when inventory full**
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at +1, `MergeResultItemID` non-null). No items equipped. Inventory is full (no free slot).
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`.
Pass: Returns `MergeResult.RejectedInventoryFull`. No state change. Inventory still contains all 3 source items. Verify no `MoveItemOut` calls were made (precondition step 4 in CR-EQS-14 must run before any item removal).

**AC-EQS-24 [BLOCKING]: Accessory merge — ForceInsert of result fails, rollback succeeds**
Note: Requires unit-test injection (stub `ForceInsert(MergeResultItemID)` to return false; rollback `ForceInsert × 3` calls are NOT stubbed and succeed normally).
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at +1, `MergeResultItemID` non-null). At least one free slot exists. All three not equipped, not locked.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`. `MoveItemOut` × 3 succeeds; `ForceInsert(MergeResultItemID)` fails (injected); rollback `ForceInsert × 3` succeeds.
Pass: Returns `MergeResult.RejectedMergeError`. All 3 source items restored to inventory. No CriticalError logged (rollback succeeded — all items accounted for). `IsSlotTransitioning` unaffected (merge does not touch equipment slots).

**AC-EQS-25 [PLANNED — BLOCKED: needs Character Persistence GDD]: IsTransitioning defaults to false on persistence load**
Setup: Simulate persistence load with valid ItemIDs in all 7 equipment slots (using test stub for persistence layer). No prior in-memory Equipment System state.
Action: Initialize Equipment System from 7 saved ItemIDs.
Pass: `IsSlotTransitioning(slot)` = false for all 7 `GearSlot` values. Modifier stack reflects all 7 items' `StatModifiers[]` (re-registered from Item Database on load). No residual `IsTransitioning = true` from any prior mid-swap state.

**AC-EQS-26 [BLOCKING]: Accessory merge — ForceInsert of result fails AND rollback also fails**
Note: Requires unit-test injection (stub `ForceInsert(MergeResultItemID)` to return false; also stub one or more rollback `ForceInsert(sourceItemID)` calls to return false).
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at +1, `MergeResultItemID` non-null). At least one free slot exists. All three not equipped, not locked.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`. `MoveItemOut` × 3 succeeds; `ForceInsert(MergeResultItemID)` fails (injected); at least one rollback `ForceInsert` also fails (injected).
Pass: Returns `MergeResult.CriticalRollbackFailed`. CriticalError log entry exists and contains `entityID` and count of unrestored source items (≥ 1). Monitoring alert sent. `IsSlotTransitioning` unaffected (merge does not touch equipment slots).

**AC-EQS-27 [ADVISORY]: Accessory merge — defense-in-depth RejectedEquipped check**
Note: Requires unit-test injection (stub Equipment System's internal equipped-item consistency check to report one of the 3 source items is simultaneously in an equipment slot — simulates corrupted server state unreachable through normal gameplay).
Setup: Player inventory contains 3 items of the same `ItemID`. Inject: the consistency check reports one item as also present in an equipment slot.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`.
Pass: Returns `MergeResult.RejectedEquipped`. No state change. No `MoveItemOut` calls made (precondition check fires before any item removal).

## Open Questions

**OQ-EQS-1 (from Item Database GDD — resolved):** Flat bonus ranges by tier resolved in F-EQS-2. Provisional pending Enhancement System GDD validation.

**OQ-EQS-2 (RESOLVED 2026-05-22):** Item Database GDD now includes `EquipRequirementStat: StatID?`, `EquipRequirementMin: float`, and `MergeResultItemID: ItemID?` fields on `EquipmentData`. See item-database.md.

**OQ-EQS-3 (RESOLVED 2026-05-22):** Inventory System GDD now includes `ForceInsert(ItemID): bool` and `MoveItemOut` extended return type `MoveItemOutResult { ItemID, Code: Success | SlotEmpty | SlotLocked }`. See inventory-system.md.

**OQ-EQS-4 (RESOLVED 2026-05-23):** Enhancement System GDD (Approved) confirmed: `PRESTIGE_MID_THRESHOLD = 5`, `ENHANCEMENT_GLOW_THRESHOLD = 7`, `PRESTIGE_HIGH_THRESHOLD = 8`, `MAX_ENHANCEMENT_LEVEL = 10`. F-EQS-2 Iron flat bonus range corrected to 22–28 (F-ENH-3 parity constraint validated: Bronze→Iron Δ=0 ✓, Iron→Steel Δ=+8 ✓, Steel→DarkSteel Δ=+4 ✓). See enhancement-system.md F-ENH-3.

**OQ-EQS-5 (pending Character Progression GDD):** Stat requirement thresholds in F-EQS-3 must be calibrated against the Character Progression GDD. Target: L45 character reaches ≥85 base STR through natural play without gear contributions.

**OQ-EQS-6 (pending Networking Core GDD review):** Confirm that `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` is included in every zone snapshot (not only delta updates). If snapshot is delta-compressed, initial full-state snapshots must always include this field.

**OQ-EQS-7 (pending Character Persistence GDD):** Equipment serialization contract assumed: save 7 `ItemID` values per character; on load, re-register modifiers from Item Database. Must be confirmed when Character Persistence GDD is authored.
