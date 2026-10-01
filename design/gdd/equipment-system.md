# Equipment System

> **Status**: Approved (Pass 7 lean, 2026-10-01 — all 5 Pass 6 blockers verified closed; 1 pre-implementation gate open before `/create-stories`: OQ-EQS-9). Previous: NEEDS REVISION (Pass 6 lean, 2026-10-01 — TD-044/045 amendments); Approved (Pass 5 lean, 2026-05-22)
> **Revision 2026-10-01 (post-review)**: `Inventory.GetSlot` read added for CR-EQS-4/13; `GetSlotEnhancementLevel(GearSlot)` added; `EquipResult` → wire mapping stated (`InventoryError = 6`); OQ-EQS-8 resolved (PrestigeBand = Weapon slot only); Dependencies refreshed; F-EQS-4 collapsed to the F-ENH-3 result; AC-EQS-2/11/16/29 revised, AC-EQS-30–31 added; OQ-EQS-9 added. Same day, after approval (Enhancement System Revision Pass 2): the `GetElementalBonus` signature quoted in the elemental-weapon rule gains `baseElementalDamage` — reference update only, no rule change.
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-10-01 (TD-045/TD-044 design session: `EquipmentSlotEntry` gains `EnhancementLevel: byte` — Enhancement System upstream amendment #2, previously unapplied in this body; the level is carried through equip, auto-swap and unequip via the Inventory interface; modifiers registered through `IEnhancementBonusProvider.GetFlatBonus`; `GetEquippedWeaponEnhancementLevel()` added; CR-EQS-4 guard compares ItemID and level; CR-EQS-8 rewritten — failed swap aborts and keeps the old item equipped, no `ForceInsert` retry, no Empty-slot outcome; AC-EQS-8/16/17 revised, AC-EQS-28–29 added; OQ-EQS-8 added. **Lean re-review pending.**) Previous: 2026-05-22 (Pass 4: MergeResult code gap resolved — RejectedMergeError added for clean-rollback path; CriticalRollbackFailed reserved for rollback-fails path; CR-EQS-14 precondition 1 and rollback block updated; CR-EQS-15 crash recovery language clarified; RejectedEquipped documented as defense-in-depth; AC-EQS-24 updated; AC-EQS-26–27 added)
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
    ItemID ItemId           // ItemID.Invalid = slot is empty
    bool   IsTransitioning  // true during mid-swap modifier sequence
    byte   EnhancementLevel // [0, MAX_ENHANCEMENT_LEVEL]; 0 when the slot is empty
}
```

`EnhancementLevel` is the equipped item's enhancement level (Enhancement System CR-ENH-1). The Equipment System never changes it: it receives the level from the Inventory System when the item is equipped (`MoveItemOutResult.EnhancementLevel`), holds it while the item is equipped, and hands it back when the item returns to the bag (`MoveItemIn(ItemID, enhancementLevel)`). An item cannot be enhanced while equipped (Enhancement System operates on inventory slots only). *(Added 2026-10-01 — Enhancement System upstream amendment #2; TD-045.)*

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
The incoming item is read with `Inventory.GetSlot(inventorySlotIndex)` — a read-only peek that mutates nothing. If it has the same `ItemID` **and the same `EnhancementLevel`** as the item already occupying the target slot, short-circuit with `EquipResult.NoChange`. No Inventory mutations (`MoveItemOut` / `MoveItemIn` are not called), no Character Stats calls, no stat events. *(`GetSlot` read named 2026-10-01 at the lean re-review — the level comparison cannot be made without it.)* Two items with the same `ItemID` but different enhancement levels are different items: equipping a +5 Iron Sword over an equipped +0 Iron Sword is a normal auto-swap (CR-EQS-6). *(Level comparison added 2026-10-01, TD-045.)*

**Enhanced modifier registration (applies to every `AddEquipmentModifier` call in CR-EQS-5/6/7/8):** the flat bonus passed to Character Stats is `IEnhancementBonusProvider.GetFlatBonus(enhancementLevel, modifier.FlatBonus, item.GearTier)`, not the raw Item Database `FlatBonus` (Enhancement System F-ENH-1; at level 0 the two are equal). `enhancementLevel` is the level of the item being registered. *(Added 2026-10-01 — Enhancement System upstream amendment #2.)*

**CR-EQS-5: Equip Into Empty Slot**
Steps, executed in order:
1. Validate equip requirement (CR-EQS-3). Abort if not met.
2. Call `Inventory.MoveItemOut(inventorySlotIndex)` → `MoveItemOutResult { ItemID, EnhancementLevel, Code }`. If `Code ≠ Success`, abort. No Character Stats calls.
3. For each entry in `item.StatModifiers[]`: call `CharacterStats.AddEquipmentModifier(entityID, modifier.StatID, enhancedFlatBonus, modifier.PctBonus, itemID)` using the returned `EnhancementLevel`. If the modifier layer cap is reached (should never occur in correctly authored data): log CriticalError and halt.
4. Write ItemID and `EnhancementLevel` to `_slots[(int)slot]`.
5. Update `equipmentAppearanceFlags` (CR-EQS-11).

**CR-EQS-6: Auto-Swap (Equip Into Occupied Slot)**
Steps, executed in order:
1. Apply CR-EQS-4 same-item guard first.
2. Validate equip requirement (CR-EQS-3). Abort if not met.
3. Check `Inventory.HasFreeSlot()`. Abort with `EquipResult.InventoryFull` if false.
4. Set `_slots[(int)slot].IsTransitioning = true`.
5. For each entry in old item's `StatModifiers[]`: call `CharacterStats.RemoveEquipmentModifier(entityID, modifier.StatID, oldItemID)`. `OnStatChanged` fires here — slot state is Transitioning; HUD subscribers must check `IsSlotTransitioning(slot)` before reading equipment state.
6. Call `Inventory.MoveItemIn(oldItemID, oldEnhancementLevel)` → `MoveItemInResult` (`oldEnhancementLevel` = the slot entry's `EnhancementLevel`). If `success = false`: execute CR-EQS-8 failure recovery. Do not proceed to step 7. On success, store `MoveItemInResult.slotIndex` as `oldItemReturnedSlotIndex` for use in the step 7 rollback path.
7. Call `Inventory.MoveItemOut(newItemInventorySlotIndex)` → `MoveItemOutResult`. If `Code ≠ Success`: call `MoveItemOut(oldItemReturnedSlotIndex)` to reclaim old item from inventory (its returned `EnhancementLevel` equals `oldEnhancementLevel`), re-apply old item's modifiers via `AddEquipmentModifier` for each, clear IsTransitioning, abort with `EquipResult.InventoryError`. The slot entry is unchanged — old item and old level.
8. For each entry in new item's `StatModifiers[]`: call `CharacterStats.AddEquipmentModifier(entityID, modifier.StatID, enhancedFlatBonus, modifier.PctBonus, newItemID)` using the new item's `MoveItemOutResult.EnhancementLevel`.
9. Write new ItemID and its `EnhancementLevel` to `_slots[(int)slot]`. Clear IsTransitioning.
10. Update `equipmentAppearanceFlags` (CR-EQS-11).

**CR-EQS-7: Unequip (Occupied → Empty)**
Steps, executed in order:
1. Check `Inventory.HasFreeSlot()`. Abort with `EquipResult.InventoryFull` if false.
2. Call `ItemDatabase.GetItem(itemID)` → `EquipmentData`. If `GetItem()` returns null (item definition removed from database while equipped): log `CriticalError("Unequip GetItem null: {itemID}")`, skip modifier removal, clear slot (ItemID → Invalid, `EnhancementLevel` → 0, IsTransitioning → false), update appearance flags, return `EquipResult.Success`. Modifiers for an undefined item cannot be safely removed; clearing the slot is the least-corrupt outcome.
3. Set `_slots[(int)slot].IsTransitioning = true`.
4. For each entry in item's `StatModifiers[]`: call `RemoveEquipmentModifier`. `OnStatChanged` fires.
5. Call `Inventory.MoveItemIn(itemID, enhancementLevel)` → `MoveItemInResult` (`enhancementLevel` = the slot entry's `EnhancementLevel`). If `success = false`: restore modifiers via `AddEquipmentModifier` for each, clear IsTransitioning, return `EquipResult.InventoryError`.
6. Clear `_slots[(int)slot]` (set ItemID to Invalid, `EnhancementLevel` to 0). Clear IsTransitioning.
7. Update `equipmentAppearanceFlags`.

**CR-EQS-8: MoveItemIn Failure Recovery (Auto-Swap)** *(rewritten 2026-10-01, TD-044)*
Triggered when step 6 of CR-EQS-6 fails — modifiers have already been removed but the old item cannot enter inventory. At this point nothing has moved: the old item is still recorded in the gear slot (`ItemId` and `EnhancementLevel` unchanged) and the new item is still in its inventory slot. Recovery therefore aborts the swap in place:
1. Re-apply the old item's modifiers via `AddEquipmentModifier` for each entry (using the slot entry's `EnhancementLevel`).
2. Clear IsTransitioning. The slot entry is unchanged — the old item remains equipped.
3. Log a server error (`"Auto-swap aborted: old item {oldItemID} could not return to inventory. Entity {entityID}."`) and return `EquipResult.InventoryError`.

No item leaves the gear slot or the bag on this path, so there is no item-loss risk and no Empty-slot outcome. `ForceInsert` is **not** retried here: it uses the same lowest-empty-slot placement as `MoveItemIn`, so on a single-threaded server tick a retry can never succeed where `MoveItemIn` just failed (the previous step 1–2 was unreachable, and its step 3 destroyed the equipped item's slot state for no benefit). `ForceInsert` remains in use for accessory merge (CR-EQS-14) only.

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

PrestigeBand source: the band is computed from the **Weapon slot's** `EquipmentSlotEntry.EnhancementLevel` only (band NONE if no weapon is equipped), consistent with the WeaponTier and ElementType fields of the same byte. The enhancement level of any other gear slot never affects the byte — enhanced armor shows its level on inspect only. *(Confirmed at the 2026-10-01 lean re-review — OQ-EQS-8 resolved; matches the registry rule "non-weapon items never glow" and Enhancement System CR-ENH-12/13.)*

Encode: `flags = (byte)(((int)weaponTier & 0x03) << 6 | ((int)elementType & 0x07) << 3 | (prestigeBand & 0x03) << 1 | (armorBit & 0x01))`

**CR-EQS-12: Elemental Weapon Data Flow**
The Equipment System does not write elemental data to Character Stats. It exposes `GetEquippedWeaponItemID(): ItemID`, returning `ItemID.Invalid` if no weapon is equipped. Damage Calculation calls this method and reads `ElementType` and `ElementalDamage` directly from Item Database. `ItemID.Invalid` is treated as a non-elemental weapon with 0 elemental damage. It also exposes `GetEquippedWeaponEnhancementLevel(): byte` — the Weapon slot entry's `EnhancementLevel`, 0 if no weapon is equipped — which Damage Calculation passes, together with the weapon's base `ElementalDamage` from the Item Database, to `IEnhancementBonusProvider.GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` (Enhancement System F-ENH-2; signature updated 2026-10-01). *(Added 2026-10-01 — required by damage-calculation.md and the Enhancement System GDD; previously missing from this body.)*

**CR-EQS-13: Input Validation**
Entry points: `Equip(GearSlot slot, ItemID itemID, int inventorySlotIndex): EquipResult` and `Unequip(GearSlot slot): EquipResult` — the same three values the wire `EquipRequest` carries (`gearSlot`, `itemId`, `inventorySlot`; `itemId = 0` routes to `Unequip`). *(Signature stated 2026-10-01; previously implied.)*

All public equip/unequip entry points validate:
- `itemID != ItemID.Invalid` — reject silently in release builds; dev builds throw `InvalidOperationException`
- `(int)slot < 7` — reject before indexing `_slots`; IL2CPP does not bounds-check enum casts
- `inventorySlotIndex` in `[0, 19]` and `Inventory.GetSlot(inventorySlotIndex).ItemID == itemID` — the stale-render guard of the `EquipRequest` validation (networking-wire-protocol.md). A mismatch makes no state change and is answered on the wire with `EquipFailReason.ItemNotInInventory` (see OQ-EQS-9 for which layer owns that code).

---

**CR-EQS-15: IsTransitioning Non-Persistence and Crash Recovery (Added 2026-05-22)**

`IsTransitioning` is a runtime in-memory flag only — it is not saved to disk as part of Character Persistence. If the server crashes or restarts mid-swap:

1. On load, all `EquipmentSlotEntry` structs are reconstructed from the 7 saved `{ItemID, EnhancementLevel}` pairs (Character Persistence `GearSlots[7]`). `IsTransitioning` defaults to `false` (C# default struct initialization). Modifiers are re-registered using each slot's saved `EnhancementLevel`.
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
2. Insert one item of `MergeResultItemID` into inventory (`ForceInsert(MergeResultItemID, 0)`). The merge result is a newly created item and always starts at `EnhancementLevel = 0`; accessories cannot be enhanced (Enhancement System: `GearSlot ≠ Ring/Necklace`), so the three source items are always level 0 as well.
3. If `ForceInsert` returns false (inventory filled between check and execution): restore the three source items via `ForceInsert(sourceItemID, 0) × 3` — step 1 freed 3 slots and step 2 consumed none, so all three rollback calls will succeed under normal conditions.
   - If all rollback `ForceInsert` calls succeed: return `MergeResult.RejectedMergeError`. No CriticalError logged — all items are accounted for.
   - If any rollback `ForceInsert` unexpectedly fails (memory/hardware fault): log `CriticalError("Merge rollback incomplete — {count} source item(s) not restored for entity {entityID}")`, alert monitoring, return `MergeResult.CriticalRollbackFailed`.

The defense-in-depth equipped check (precondition 1) executes before any item removal. Under normal gameplay an item cannot be simultaneously in inventory and an equipment slot; this check guards against inconsistent server state only.

`MAX_ACCESSORY_LEVEL = 10`. Items at +10 have `MergeResultItemID = null`. Merge UI must disable the merge action for items at +10. Server validates `MergeResultItemID != null` server-side regardless of UI state.

---

### States and Transitions

| State | `ItemId` | `IsTransitioning` | Modifier status in Character Stats |
|-------|----------|-------------------|-------------------------------------|
| **Empty** | `ItemID.Invalid` (`EnhancementLevel` = 0) | false | None registered for this slot |
| **Occupied** | valid ItemID | false | Fully applied |
| **Transitioning** | valid ItemID (old item — slot not yet updated) | true | Partially applied (mid-operation) |

| Transition | Trigger | Guards | Rule |
|------------|---------|--------|------|
| Empty → Occupied | Player equips | Stat req met; MoveItemOut success | CR-EQS-5 |
| Occupied → Occupied | Auto-swap | Stat req met; HasFreeSlot=true; MoveItemIn success | CR-EQS-6 |
| Occupied → Empty | Player unequips | HasFreeSlot=true; MoveItemIn success | CR-EQS-7 |
| Occupied → Occupied (unchanged) | Swap aborted | MoveItemIn fails at CR-EQS-6 step 6 | CR-EQS-8: modifiers restored, old item stays equipped, `InventoryError` |
| Any → unchanged | Same item | ItemID and EnhancementLevel match slot | CR-EQS-4: NoChange |
| Any → unchanged | Stat req not met | BaseStat < EquipRequirementMin | CR-EQS-3: abort before any mutation |
| Any → unchanged | Inventory full | HasFreeSlot=false | Abort before state mutation |

---

### Interactions with Other Systems

| System | Direction | What flows | Interface |
|--------|-----------|-----------|-----------|
| Item Database | Read | `GearSlot`, `GearTier`, `StatModifiers[]`, `EquipRequirementStat`, `EquipRequirementMin`, `ElementType`, `ElementalDamage` | `GetItem(ItemID): EquipmentData` |
| Character Stats | Write | Modifier registration / removal | `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)` / `RemoveEquipmentModifier(EntityID, StatID, ItemID)` |
| Character Stats | Read | Base stat for equip gate | `GetBaseStat(EntityID, StatID): float` |
| Inventory System | Read / Write | Item transfer (ItemID + EnhancementLevel), capacity check, slot peek | `HasFreeSlot(): bool` / `GetSlot(slotIndex): { ItemID, Quantity, EnhancementLevel }` *(read-only — CR-EQS-4 guard and CR-EQS-13 validation)* / `MoveItemOut(slotIndex): MoveItemOutResult { ItemID, EnhancementLevel, Code }` / `MoveItemIn(ItemID, enhancementLevel): MoveItemInResult` / `ForceInsert(ItemID, enhancementLevel): bool` *(accessory merge only)* |
| Enhancement System | Read | Enhanced flat bonus for modifier registration | `IEnhancementBonusProvider.GetFlatBonus(level, baseFlatBonus, gearTier): int` |
| Networking | Write | Zone appearance byte | `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` — written on every slot change |
| Damage Calculation | Provides | Equipped weapon ItemID and enhancement level | `GetEquippedWeaponItemID(): ItemID` / `GetEquippedWeaponEnhancementLevel(): byte` |
| Character Persistence | Read / Write | 7 gear slots | `GearSlots[7]: {ItemID, EnhancementLevel: byte}` — saved and restored per slot; read per slot via `GetEquipmentSlotState(slot)` + `GetSlotEnhancementLevel(slot)` |
| Inventory UI | Provides | Per-slot contents, state and enhancement level | `GetEquipmentSlotState(GearSlot)` / `GetSlotEnhancementLevel(GearSlot): byte` / `IsSlotTransitioning(GearSlot)` |

**Cross-document impacts from this section:**
- *Item Database GDD*: Must add `EquipRequirementStat: StatID?` and `EquipRequirementMin: float` fields to `EquipmentData` schema (CR-EQS-3). Accessories: `EquipRequirementStat = null`.
- *Inventory System GDD*: Must add `ForceInsert(ItemID): bool` emergency path (CR-EQS-8). `MoveItemOut` return type must be extended to `MoveItemOutResult` to distinguish empty vs. locked slot (current `ItemID` return is ambiguous). *(Both applied 2026-05-22. 2026-10-01: `MoveItemOutResult` gains `EnhancementLevel`, and `MoveItemIn`/`ForceInsert` gain an `enhancementLevel` parameter — applied to inventory-system.md Rule 8.24a the same day; `ForceInsert` is no longer used by CR-EQS-8.)*
- *Networking Wire Protocol GDD*: `EquipRequest` must identify the item by **inventory slot index** — an `ItemID` alone cannot distinguish two items of the same type at different enhancement levels; `EquipResult` must carry the equipped slot's `EnhancementLevel`. *(Applied 2026-10-01.)* `EquipFailReason` must carry `InventoryError` — see the mapping below. *(Applied 2026-10-01, lean re-review: `InventoryError = 6`.)*
- *Inventory System GDD (2026-10-01 lean re-review)*: `GetSlot(slotIndex)` added to the Equipment System's interface rows (read-only; already defined for the Enhancement System). The historical `ForceInsert(ItemID)` note above predates the `enhancementLevel` parameter and CR-EQS-8's rewrite — `ForceInsert(ItemID, enhancementLevel)` is now used by CR-EQS-14 only.

**`EquipResult` → wire `EquipResult` message mapping** *(added 2026-10-01, lean re-review)*:

| `EquipResult` (this system) | wire `success` | wire `failReason` (`EquipFailReason`) | Slot fields in the message |
|-----------------------------|----------------|---------------------------------------|----------------------------|
| `Success` | true | `None` (0) | New slot contents |
| `NoChange` | true | `None` (0) | Unchanged slot contents (same ItemID and level as before) |
| `StatRequirementNotMet` | false | `StatRequirementNotMet` (1) | Unchanged |
| `InventoryFull` | false | `InventoryFull` (2) | Unchanged |
| `SlotMismatch` | false | `SlotMismatch` (3) | Unchanged |
| `InventoryError` | false | `InventoryError` (6) | Unchanged (old item still equipped — CR-EQS-6 step 7, CR-EQS-7 step 5, CR-EQS-8) |
| `CriticalFailure` | false | `CriticalFailure` (255) | Reserved — not produced by any rule in this GDD |

Wire reasons `ItemLocked` (4) and `ItemNotInInventory` (5) have no `EquipResult` member yet — see OQ-EQS-9.

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

The enhancement prestige principle: **a +5 item of tier T ≈ a +0 item of tier T+1** in effective stats (+5 Bronze ≈ +0 Iron). The constraint is owned and verified by Enhancement System F-ENH-3; it is restated here because it bounds the F-EQS-2 ranges. *(Rewritten 2026-10-01 — the original estimate-based derivation, written before the Enhancement System GDD existed, was superseded on 2026-05-23.)*

```
midpoint(T) + PRESTIGE_MID_THRESHOLD × BonusPerLevel[T] ≈ midpoint(T+1)
```

Variables:
- `midpoint(T)` — midpoint of tier T's Attack-class flat bonus range in F-EQS-2 (per modifier)
- `BonusPerLevel[T]` — flat bonus added per modifier per enhancement level (Enhancement System F-ENH-1): Bronze 3, Iron 4, Steel 6, Dark Steel 10
- `PRESTIGE_MID_THRESHOLD` = 5 (Enhancement System)

| Tier T | Range (F-EQS-2) | midpoint(T) | + 5 × BonusPerLevel[T] | midpoint(T+1) | Δ |
|--------|-----------------|-------------|------------------------|---------------|---|
| Bronze | 8 – 12 | 10 | 10 + 15 = 25 | Iron: 25 | 0 |
| Iron | 22 – 28 | 25 | 25 + 20 = 45 | Steel: 37 | +8 |
| Steel | 32 – 42 | 37 | 37 + 30 = 67 | Dark Steel: 63 | +4 |

Example: a Bronze Sword Attack modifier of 10 at +5 registers `10 + (5 × 3) = 25` — the Iron midpoint.

**Boundary behavior:** the constraint holds at midpoints, not at range extremes. A max-roll +5 item of tier T can exceed a min-roll +0 item of tier T+1 (e.g. Bronze 12 + 15 = 27 > Iron 22). This overlap is an explicit, accepted design decision recorded in Enhancement System F-ENH-3 ("Worst-case overlap") — higher tiers keep their advantage through a higher ceiling, not a guaranteed floor.

**Co-constraint:** any change to an F-EQS-2 Attack-class range or to `BonusPerLevel` requires re-verifying this table in both GDDs.

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
Rule: CR-EQS-4 short-circuits immediately. `EquipResult.NoChange` returned. No Inventory calls, no stat events, no appearance flag recompute. The guard matches on `ItemID` **and** `EnhancementLevel`: a bag item with the same `ItemID` but a different level is a different item and proceeds as an auto-swap (CR-EQS-6).

**EC-EQS-9: No Weapon Equipped — Elemental Data Flow**
Situation: A character has no weapon equipped.
Rule: `GetEquippedWeaponItemID()` returns `ItemID.Invalid`. Damage Calculation treats this as a non-elemental weapon with 0 elemental damage. The `equipmentAppearanceFlags` WeaponTier and ElementType bits are both 0 (no-weapon encoding).

**EC-EQS-10: MoveItemIn Failure After Modifier Removal**
Situation: `HasFreeSlot()` returned true but `MoveItemIn` fails at execution time (concurrent inventory write race).
Rule: CR-EQS-8 executes. The swap is aborted in place: the old item's modifiers are restored, the old item stays equipped at its enhancement level, the new item stays in its inventory slot, a server error is logged, and `EquipResult.InventoryError` is returned. No item is lost or orphaned. *(Rewritten 2026-10-01, TD-044 — the former `ForceInsert` retry could never succeed and its failure branch emptied the gear slot.)*

**EC-EQS-11: All Slots Empty — Appearance Flags**
Situation: A fresh character with all slots empty.
Rule: `equipmentAppearanceFlags = 0x00`. Other players see the default unequipped appearance. 0x00 is an unambiguous "fully unequipped" state at all bit groups.

## Dependencies

**Upstream dependencies** (systems this GDD depends on):

| System | GDD status | What this GDD takes from it |
|--------|-----------|----------------------------|
| Item Database | Approved ✓ | `GearSlot` enum, `GearTier` enum, `EquipmentData` schema (`StatModifiers[]`, `ElementType`, `ElementalDamage`, `EquipRequirementStat: StatID?`, `EquipRequirementMin: float`, `MergeResultItemID: ItemID?`) — additions written 2026-05-22 |
| Inventory System | Approved ✓ | `HasFreeSlot()`, `GetSlot(slotIndex)` (read-only peek — CR-EQS-4/13), `MoveItemOut(slotIndex): MoveItemOutResult { ItemID, EnhancementLevel, Code }`, `MoveItemIn(ItemID, enhancementLevel)`, `ForceInsert(ItemID, enhancementLevel): bool` — `MoveItemOutResult` extension and `ForceInsert` written 2026-05-22; enhancement-level fields and parameters written 2026-10-01 (TD-045) |
| Character Stats | Approved ✓ | `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)`, `RemoveEquipmentModifier(EntityID, StatID, ItemID)`, `GetBaseStat(EntityID, StatID)`; equipment modifier layer (16 entries); EC-18 MaxHP clamping |
| Networking Core | Approved ✓ | `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags: byte` wire field — Equipment System writes this field |
| Networking Wire Protocol | Approved ✓ | `EquipRequest` (client → server), `EquipResult` (server → client), `AppearanceChangedEvent` (server → zone) — wire schemas added 2026-05-22 |
| Enhancement System | **Approved (Pass 4 lean, 2026-05-23)** | `PRESTIGE_MID_THRESHOLD=5`, `PRESTIGE_HIGH_THRESHOLD=8`, `ENHANCEMENT_GLOW_THRESHOLD=7`, `MAX_ENHANCEMENT_LEVEL=10`; `IEnhancementBonusProvider` (GetFlatBonus, GetElementalBonus); F-ENH-3 Iron range amendment applied to F-EQS-2 |

**Downstream dependents** (systems that depend on this GDD):

| System | GDD status | What it takes from this GDD |
|--------|-----------|------------------------------|
| Inventory UI | Not Started (#30) | `IsSlotTransitioning(GearSlot): bool`, `GetEquipmentSlotState(GearSlot)`, `GetSlotEnhancementLevel(GearSlot): byte` (the "+N" shown on an equipped item), equip/unequip result codes (`EquipResult`) — needed to render the equipment panel and react to swap events. Player fantasy (Section B) social-visibility anchor depends on appearance rendering landing on iOS. |
| Damage Calculation | Approved ✓ | `GetEquippedWeaponItemID(): ItemID` — reads weapon item from Item Database for elemental damage resolution; `GetEquippedWeaponEnhancementLevel(): byte` — the level passed to `IEnhancementBonusProvider.GetElementalBonus` (CR-EQS-12). *Known naming drift:* damage-calculation.md calls these `GetEquippedWeaponID(AttackerID)` / `GetEquippedWeaponEnhancementLevel(EntityID)` — to be reconciled in that GDD. |
| Character Persistence | Approved ✓ | Saves `{ItemID, EnhancementLevel}` for all 7 slots (`GearSlots[7]`; `IsTransitioning` is never saved — CR-EQS-15). On load, modifiers are re-registered from Item Database using each slot's saved level (`IEnhancementBonusProvider.GetFlatBonus`). Equipment state must be fully reconstructible from the 7 `{ItemID, EnhancementLevel}` pairs alone. *(Corrected 2026-10-01 — previously "from ItemIDs alone", which the per-slot level made false.)* |
| Enhancement System | Approved ✓ | `EquipmentSlotEntry.EnhancementLevel` storage while an item is equipped; the PrestigeBand bits of `equipmentAppearanceFlags` (CR-EQS-11); the "equipped items cannot be enhanced" boundary (CR-EQS-1). Also an upstream dependency (table above) — the two GDDs are mutually dependent. |

**Bidirectionality:**
- Item Database GDD references Equipment System as a consumer of `EquipmentData` ✓
- Character Stats GDD references Equipment System as the owner of the equipment modifier layer (line 188) ✓
- Inventory System GDD references Equipment System as the caller of `HasFreeSlot`/`GetSlot`/`MoveItemOut`/`MoveItemIn`/`ForceInsert` — updated 2026-05-22; `GetSlot` added 2026-10-01 ✓
- Enhancement System GDD references Equipment System as the consumer of `IEnhancementBonusProvider` and owner of `equipmentAppearanceFlags` (Interactions and Dependencies tables) ✓
- Damage Calculation GDD references Equipment System as the source of the equipped weapon and its enhancement level ✓ (method-name drift noted above)
- Character Persistence GDD references Equipment System for `GearSlots[7]` save/load ✓

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
- `GetSlotEnhancementLevel(GearSlot): byte` — the slot entry's `EnhancementLevel`; 0 if the slot is empty. Valid for all 7 slots. `GetEquippedWeaponEnhancementLevel()` (CR-EQS-12) is equivalent to `GetSlotEnhancementLevel(GearSlot.Weapon)`. *(Added 2026-10-01, lean re-review — no per-slot level read existed for non-weapon slots.)*
- `IsSlotTransitioning(GearSlot): bool` — HUD subscribers defer rendering while true
- `EquipResult` codes and associated data:
  - `NoChange` — no-op; UI does nothing
  - `InventoryFull` — UI surfaces: "Inventory full — make room before unequipping"
  - `StatRequirementNotMet(StatID, required, actual)` — UI surfaces: "Requires {required} {statName} (you have {actual})". `actual` = `GetBaseStat(entityID, StatID)` — the base stat value only, not effective stat. Buffs that bring effective stat above `EquipRequirementMin` do not satisfy the gate (CR-EQS-3, AC-EQS-6). The UI must display base stat in the shortfall message, not the buffed value, to avoid misleading the player.
  - `SlotMismatch` — UI surfaces: item tooltip shows the correct slot type
  - `InventoryError` — UI surfaces a generic equip failure with retry option
  - `CriticalFailure` — UI surfaces: "Equip failed — please try again. If the issue persists, contact support." *(As of 2026-10-01 no equip, swap or unequip rule in this GDD returns `CriticalFailure` — CR-EQS-8 now returns `InventoryError`. The code is retained for wire-enum stability and future use.)*
- `RequestMerge(int slotIdx1, int slotIdx2, int slotIdx3): MergeResult` — merge trigger; Inventory UI passes the three inventory slot indices of the selected source items. `MergeResult` codes:
  - `Success` — merge complete; 3 source items removed, 1 merged result item added to inventory
  - `RejectedEquipped` — defense-in-depth: a passed slot's item was detected simultaneously in an equipment slot (inconsistent server state; unreachable through normal gameplay). No state change.
  - `RejectedMaxLevel` — item is at `MAX_ACCESSORY_LEVEL` (`MergeResultItemID = null`); no state change
  - `RejectedInventoryFull` — no free inventory slot for merge result; no state change
  - `RejectedMergeError` — `ForceInsert(MergeResultItemID, 0)` failed; all 3 source items restored via rollback; no item loss; no CriticalError
  - `CriticalRollbackFailed` — `ForceInsert(MergeResultItemID, 0)` failed AND one or more rollback `ForceInsert` calls also failed; CriticalError logged; monitoring alert sent; one or more source items unrecovered

The equipment panel layout, slot visual design, tap/drag interaction model, and stat comparison overlays are specified in the Inventory UI GDD — not here.

## Acceptance Criteria

**AC-EQS-1 [BLOCKING]: Equip into empty slot registers all modifiers**
Setup: Character with all slots empty. Item X has `StatModifiers[] = [{StatID.STR, +10}, {StatID.DEF, +8}]`. `GetEffectiveStat(STR)` = 50, `GetEffectiveStat(DEF)` = 30. Equip requirement met.
Action: Equip Item X into Weapon slot.
Pass: `GetEffectiveStat(STR)` = 60, `GetEffectiveStat(DEF)` = 38. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemX, Occupied)`. Inventory no longer contains Item X.

**AC-EQS-2 [BLOCKING]: Auto-swap moves old item to inventory and registers new item's modifiers**
Setup: Iron Sword (ItemA, +25 STR) equipped in Weapon slot. Steel Sword (ItemB, +35 STR, `EquipRequirementMin=55 STR`) in inventory. Both at `EnhancementLevel = 0`. Character `GetBaseStat(STR)` = 60.
Action: Equip ItemB into Weapon slot.
Pass: Net `GetEffectiveStat(STR)` change = +10 (+35 − 25). ItemA in inventory. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemB, Occupied)`. ItemB no longer in inventory.

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
Pass: `EquipResult.NoChange` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemA, Occupied)` — unchanged. `GetEffectiveStat(STR)` = same value as before action (no double-application). ItemA still in Weapon slot, not moved to inventory. "Again" means an inventory item with the same `ItemID` and the same `EnhancementLevel` as the equipped one; a same-`ItemID` item at a different level is covered by AC-EQS-28.

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
Setup: Equip a Dark Steel sword (WeaponTier=3, ElementType=Fire=1) at `EnhancementLevel = 0` (PrestigeBand=None=0). Steel or DarkSteel armor equipped (ArmorTier=1).
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

**AC-EQS-16 [BLOCKING]: Auto-swap MoveItemIn failure — swap aborted, old item stays equipped** *(rewritten 2026-10-01, TD-044)*
Note: Requires unit-test injection (stub `MoveItemIn` to return `success=false`; `ForceInsert` is a spy).
Setup: Weapon slot occupied by ItemA at `EnhancementLevel = 3`. ItemB in inventory slot S.
Action: Trigger auto-swap to ItemB (CR-EQS-6 path). `MoveItemIn` fails at step 6.
Pass: `EquipResult.InventoryError` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemA, Occupied)` and `GetSlotEnhancementLevel(GearSlot.Weapon)` = 3 — slot unchanged. `GetEffectiveStat` equals its pre-action value (ItemA's enhanced modifiers re-applied exactly once). `IsSlotTransitioning(GearSlot.Weapon)` = false. ItemB is still in inventory slot S. `ForceInsert` was called 0 times. A server error log entry exists containing oldItemID and entityID. No item lost.

**AC-EQS-17 [BLOCKING]: Enhancement level survives equip and unequip** *(replaced 2026-10-01 — the former ForceInsert-failure / Empty-slot criterion no longer exists, TD-044/TD-045)*
Setup: Weapon slot empty. Iron Sword at `EnhancementLevel = 5` in inventory slot S (stat requirement met). At least one other inventory slot free.
Action: Equip from slot S; then unequip.
Pass: After equip — `GetEquipmentSlotState(GearSlot.Weapon)` = Iron Sword with `EnhancementLevel = 5`; `GetEquippedWeaponEnhancementLevel()` = 5; inventory slot S empty. After unequip — the sword is in the lowest-index free inventory slot at `EnhancementLevel = 5` (verified by reading the slot); Weapon slot Empty with `EnhancementLevel = 0`; `GetEquippedWeaponEnhancementLevel()` = 0.

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
Note: Requires unit-test injection (stub `ForceInsert(MergeResultItemID, 0)` to return false; rollback `ForceInsert × 3` calls are NOT stubbed and succeed normally).
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at +1, `MergeResultItemID` non-null). At least one free slot exists. All three not equipped, not locked.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`. `MoveItemOut` × 3 succeeds; `ForceInsert(MergeResultItemID, 0)` fails (injected); rollback `ForceInsert × 3` succeeds.
Pass: Returns `MergeResult.RejectedMergeError`. All 3 source items restored to inventory. No CriticalError logged (rollback succeeded — all items accounted for). `IsSlotTransitioning` unaffected (merge does not touch equipment slots).

**AC-EQS-25 [BLOCKING]: IsTransitioning defaults to false on persistence load** *(unblocked 2026-10-01 — character-persistence.md is Approved; was PLANNED — BLOCKED)*
Setup: Simulate persistence load with valid ItemIDs in all 7 equipment slots (using test stub for persistence layer). No prior in-memory Equipment System state.
Action: Initialize Equipment System from 7 saved `{ItemID, EnhancementLevel}` pairs.
Pass: `IsSlotTransitioning(slot)` = false for all 7 `GearSlot` values. Modifier stack reflects all 7 items' `StatModifiers[]` (re-registered from Item Database on load). No residual `IsTransitioning = true` from any prior mid-swap state.

**AC-EQS-26 [BLOCKING]: Accessory merge — ForceInsert of result fails AND rollback also fails**
Note: Requires unit-test injection (stub `ForceInsert(MergeResultItemID, 0)` to return false; also stub one or more rollback `ForceInsert(sourceItemID)` calls to return false).
Setup: Player inventory contains 3 items of the same `ItemID` (Ring at +1, `MergeResultItemID` non-null). At least one free slot exists. All three not equipped, not locked.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`. `MoveItemOut` × 3 succeeds; `ForceInsert(MergeResultItemID, 0)` fails (injected); at least one rollback `ForceInsert` also fails (injected).
Pass: Returns `MergeResult.CriticalRollbackFailed`. CriticalError log entry exists and contains `entityID` and count of unrestored source items (≥ 1). Monitoring alert sent. `IsSlotTransitioning` unaffected (merge does not touch equipment slots).

**AC-EQS-27 [ADVISORY]: Accessory merge — defense-in-depth RejectedEquipped check**
Note: Requires unit-test injection (stub Equipment System's internal equipped-item consistency check to report one of the 3 source items is simultaneously in an equipment slot — simulates corrupted server state unreachable through normal gameplay).
Setup: Player inventory contains 3 items of the same `ItemID`. Inject: the consistency check reports one item as also present in an equipment slot.
Action: Call `RequestMerge(slotIdx1, slotIdx2, slotIdx3)`.
Pass: Returns `MergeResult.RejectedEquipped`. No state change. No `MoveItemOut` calls made (precondition check fires before any item removal).

**AC-EQS-28 [BLOCKING]: Same ItemID at a different enhancement level auto-swaps** *(added 2026-10-01, TD-045)*
Setup: Weapon slot occupied by Iron Sword at `EnhancementLevel = 0`. A second Iron Sword at `EnhancementLevel = 5` in inventory slot S. At least one free inventory slot.
Action: Equip from slot S.
Pass: `EquipResult.Success` (not `NoChange`). Weapon slot holds Iron Sword at `EnhancementLevel = 5`. The level-0 sword is in inventory at `EnhancementLevel = 0`. `GetEffectiveStat` reflects the level-5 sword's enhanced modifiers only (no residue from the level-0 sword).

**AC-EQS-29 [BLOCKING]: Modifiers are registered with the enhanced flat bonus** *(added 2026-10-01 — Enhancement System upstream amendment #2)*
Note: Requires a stub `IEnhancementBonusProvider` whose `GetFlatBonus(level, base, tier)` returns a known value.
Setup: Weapon slot empty. Bronze Sword with one modifier `{STR, FlatBonus = 10}` at `EnhancementLevel = 5` in inventory. Stub returns 28 for `GetFlatBonus(5, 10, GearTier.Bronze)`.
Action: Equip the sword.
Pass: `AddEquipmentModifier` was called with flat bonus 28 (not 10). `GetFlatBonus` was called with `(5, 10, GearTier.Bronze)`. The same sword at `EnhancementLevel = 0` (stub returns 10 for level 0) registers flat bonus 10.
Note: 28 is deliberately not the real F-ENH-1 value (25) — the test proves the Equipment System registers whatever the provider returns rather than recomputing the formula itself.

**AC-EQS-30 [BLOCKING]: PrestigeBand is driven by the Weapon slot only** *(added 2026-10-01, lean re-review — OQ-EQS-8)*
Setup: All slots empty. Items in inventory (stat requirements met): Iron Sword (`ElementType = None`) at `EnhancementLevel = 7`; a second Iron Sword (`ElementType = None`) at `EnhancementLevel = 0`; Bronze Chest at `EnhancementLevel = 9`. At least one free inventory slot.
Action / Pass (in order):
(a) Equip the Bronze Chest only → `equipmentAppearanceFlags = 0x00` (no weapon: band NONE despite the +9 chest). `GetSlotEnhancementLevel(GearSlot.Chest)` = 9.
(b) Equip the +0 Iron Sword → `equipmentAppearanceFlags = 0b01_000_00_0 = 0x40` (WeaponTier=Iron, band NONE — the +9 chest does not raise the band).
(c) Equip the +7 Iron Sword over it (auto-swap) → `equipmentAppearanceFlags = 0b01_000_10_0 = 0x44` (band GLOW_LOW).
(d) Unequip the weapon → `equipmentAppearanceFlags = 0x00`.

**AC-EQS-31 [BLOCKING]: Auto-swap MoveItemOut failure — old item reclaimed at its enhancement level** *(added 2026-10-01, lean re-review — CR-EQS-6 step 7 had no criterion)*
Note: Requires unit-test injection (stub `MoveItemOut(S)` to return `Code = SlotLocked`; `GetSlot`, `MoveItemIn` and the reclaiming `MoveItemOut` are not stubbed).
Setup: Weapon slot occupied by ItemA at `EnhancementLevel = 3`. ItemB (different `ItemID`, stat requirement met) in inventory slot S. Exactly one free inventory slot F.
Action: Trigger auto-swap to ItemB. Step 6 succeeds (ItemA placed in F at level 3); step 7 `MoveItemOut(S)` fails.
Pass: `EquipResult.InventoryError` returned. `GetEquipmentSlotState(GearSlot.Weapon)` = `(ItemA, Occupied)` and `GetSlotEnhancementLevel(GearSlot.Weapon)` = 3. `GetEffectiveStat` equals its pre-action value (ItemA's enhanced modifiers re-applied exactly once). `IsSlotTransitioning(GearSlot.Weapon)` = false. Inventory slot F is empty again (ItemA is not duplicated in the bag). ItemB is still in slot S.

## Open Questions

**OQ-EQS-9 (added 2026-10-01, lean re-review):** The wire `EquipFailReason` enum has `ItemLocked` (4) and `ItemNotInInventory` (5), but `EquipResult` has no matching members, and CR-EQS-5 step 2 aborts on a failed `MoveItemOut` without naming a result code (CR-EQS-6 step 7 returns `InventoryError`; EC-EQS-4 expects the UI to say "Item is locked"). Decide which layer produces the two wire reasons: (a) add `ItemLocked` and `ItemNotInInventory` to `EquipResult` and have CR-EQS-13 / CR-EQS-5 step 2 / CR-EQS-6 step 7 return them, or (b) keep `EquipResult` as-is and have the request handler derive them (`GetSlot` mismatch → `ItemNotInInventory`; `IsSlotLocked` → `ItemLocked`) before calling `Equip`. *Owner*: Game Designer + Network Programmer. *Target*: before `/create-stories` for the Equipment System epic.

**OQ-EQS-8 (RESOLVED 2026-10-01, lean re-review):** The PrestigeBand bits of `equipmentAppearanceFlags` are driven by the **Weapon slot's** enhancement level only (CR-EQS-11, AC-EQS-30). The "highest level across all slots" alternative was rejected: it contradicts the registry rule that non-weapon items never glow and would decouple the halo from the weapon it is drawn on.

**OQ-EQS-1 (from Item Database GDD — RESOLVED 2026-05-23):** Flat bonus ranges by tier resolved in F-EQS-2 and validated against Enhancement System F-ENH-3 (see OQ-EQS-4).

**OQ-EQS-2 (RESOLVED 2026-05-22):** Item Database GDD now includes `EquipRequirementStat: StatID?`, `EquipRequirementMin: float`, and `MergeResultItemID: ItemID?` fields on `EquipmentData`. See item-database.md.

**OQ-EQS-3 (RESOLVED 2026-05-22):** Inventory System GDD now includes `ForceInsert(ItemID): bool` and `MoveItemOut` extended return type `MoveItemOutResult { ItemID, Code: Success | SlotEmpty | SlotLocked }`. See inventory-system.md.

**OQ-EQS-4 (RESOLVED 2026-05-23):** Enhancement System GDD (Approved) confirmed: `PRESTIGE_MID_THRESHOLD = 5`, `ENHANCEMENT_GLOW_THRESHOLD = 7`, `PRESTIGE_HIGH_THRESHOLD = 8`, `MAX_ENHANCEMENT_LEVEL = 10`. F-EQS-2 Iron flat bonus range corrected to 22–28 (F-ENH-3 parity constraint validated: Bronze→Iron Δ=0 ✓, Iron→Steel Δ=+8 ✓, Steel→DarkSteel Δ=+4 ✓). See enhancement-system.md F-ENH-3.

**OQ-EQS-5 (open — calibration not yet performed):** Stat requirement thresholds in F-EQS-3 must be calibrated against the Character Progression GDD. Target: L45 character reaches ≥85 base STR through natural play without gear contributions. *(2026-10-01: no GDD named "Character Progression" exists; base-stat growth is specified in leveling-system.md (Approved), which is the likely source for this calibration.)*

**OQ-EQS-6 (pending Networking Core GDD review):** Confirm that `ZoneStateSnapshotEntityEntry.EquipmentAppearanceFlags` is included in every zone snapshot (not only delta updates). If snapshot is delta-compressed, initial full-state snapshots must always include this field.

**OQ-EQS-7 (RESOLVED 2026-10-01):** Equipment serialization contract confirmed by character-persistence.md (Approved): 7 `{ItemID, EnhancementLevel}` pairs per character (`GearSlots[7]`); `IsTransitioning` is not saved; on load, modifiers are re-registered from Item Database using each slot's saved level (load step 4). AC-EQS-25 is no longer blocked on that GDD being authored.
