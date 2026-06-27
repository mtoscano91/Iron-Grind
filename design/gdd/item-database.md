# Item Database

> **Status**: Approved (Review Passes 1–4 complete — 2026-04-25)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-06-07 (F-2 invariant superseded; OQ-9 resolved — SmallBasePrice = 2g per NPC Shop authoring session)
> **Implements Pillar**: Legendary Gear (primary), Earned Power (secondary)

## Overview

The Item Database is the centralized, read-only data store for every collectible and equippable item in Project Iron Grind. It owns the authoritative definition of all items — their names, categories, gear slot assignments, stat modifiers, elemental damage values, upgrade parameters, shop prices, and display metadata. Every system that needs to know anything about an item — what it does, what it costs, how far it can be upgraded — queries the Item Database. The database itself holds no runtime state: it does not track what any player owns (that is the Inventory System), which items are equipped (Equipment System), or what enhancement level an item has reached (Enhancement System). It is a pure, stateless data layer. The unit of reference across all systems is `ItemID`, a `readonly struct` wrapping `uint`, formally defined here. In MVP, the Item Database contains exactly 30 base items: one weapon type (Sword) in four gear tiers, five armor and accessory slots in four gear tiers, and six consumable potions. All items of the same name have identical stats for all players — there is no per-player or quality-tier variation within an item name.

## Player Fantasy

The Item Database does not have a player-facing moment. Players never see it — they see what it enables: the exact same Iron Sword in your inventory as the one your guildmate is wearing; the Steel Longsword you spent two weeks farming for, identifiable on sight by anyone on the server; the +9 Dark Steel that stops strangers in the middle of town because every player on the server knows the same stat lines, the same tier names, the same rules.

That shared vocabulary is the Item Database's player fantasy — delivered entirely through indirection.

A +9 weapon is a server event because everyone on the server agrees on what a +9 weapon *is*. The social weight of Legendary Gear collapses without a single authoritative definition of every item that every system — Inventory, Equipment, Loot, Enhancement, Shop — is reading from in lockstep. The database exists so the game has a common language, and players earn the right to speak it in high enhancement.

## Detailed Design

### Core Rules

**Rule 1 — Item Identity and Authority**

1. Every item in the game is represented by exactly one record in the Item Database, identified by a unique `ItemID` (a `readonly struct` wrapping `uint`). No two records share the same `ItemID`. `ItemID(0)` is reserved as `ItemID.Invalid` — no item record may use ID 0.
2. The Item Database is the single authoritative source for all item properties. Downstream systems (Inventory, Equipment, Loot, Enhancement, Shop) read from it but never modify it. On-disk character persistence stores `ItemID` values only — item properties are always re-read from the database on load.
3. A given `ItemID` always resolves to the same `ItemDefinition`. A Bronze Sword with `ItemID(1)` has identical name, stats, and parameters for every player on every server at all times. There is no per-player or per-session item variation.

**Rule 2 — Item Categories**

4. Every item belongs to exactly one `ItemCategory`: `Equipment` or `Consumable`.
5. **Equipment** items occupy a gear slot and can be equipped to a character. Equipment always has `StackLimit = 1`.
6. **Consumables** are single-use items with an immediate effect. They do not occupy gear slots.
7. No item belongs to both categories.

**Rule 3 — Gear Slots (Equipment only)**

8. Each equipment item is assigned to exactly one of seven `GearSlot` values: `Weapon`, `Helmet`, `Chest`, `Legs`, `Boots`, `Ring`, or `Necklace`. The Equipment System enforces a maximum of one equipped item per slot per character. *(OQ-3 resolved 2026-05-15: the former single `Accessory` slot was split into `Ring=5` and `Necklace=6`.)*
9. Weapons may carry an elemental type; all other gear slots always have `ElementType.None`.

**Rule 4 — Gear Tiers (Equipment only)**

10. All equipment items belong to exactly one of four `GearTier` values: `Bronze`, `Iron`, `Steel`, or `DarkSteel`. Consumables have `GearTier.None`.
11. Gear tiers represent categorical power levels, not a continuous scale. An Iron Sword is categorically stronger than a Bronze Sword in base stats. Tier advancement is one of the primary equipment progression axes.
12. MVP contains exactly four tiers. Additional tiers are a Vertical Slice or later addition — they require new item records, not schema changes.

**Rule 5 — Item Stack Rules**

13. Every `ItemDefinition` has a top-level `StackLimit: int` field. For Equipment, `StackLimit` is always `1` — the validator rejects any equipment record where `StackLimit ≠ 1`. For Consumables, `StackLimit` is an authored value (minimum 1).
14. `StackLimit` is exposed at the top level of `ItemDefinition` so the Inventory System can read it for any item without branching on category or accessing a nullable sub-schema.

**Rule 6 — Elemental Damage (Weapons only)**

16. Each weapon item carries two elemental fields: `ElementType` (enum: `None`, `Fire`, `Cold`, `Lightning`, `Poison`) and `ElementalDamage: int` (base elemental bonus at enhancement +0, range [0, 9,999]).
17. A weapon has exactly one element type. Most weapons are `ElementType.None` (physical-only). Non-`None` weapons deal additional damage of their element type on top of physical damage.
18. Armor, helmets, boots, leg armor, and accessories always have `ElementType.None` and `ElementalDamage = 0`.
19. Elemental weapon damage is NOT a Character Stat and is NOT written to the Character Stats equipment modifier layer. Damage Calculation reads `ElementType` and `ElementalDamage` directly from Item Database when resolving a hit involving an elemental weapon. The `MagicDefense` stat in Character Stats mitigates the elemental component.
20. The VFX System reads `ElementType` from Item Database when a weapon's enhancement level reaches ≥+7, and activates the corresponding elemental glow. No glow activates for `ElementType.None` at any enhancement level. The ≥+7 threshold is a constant owned by the Enhancement System GDD.

**Rule 7 — Equipment Stat Modifiers**

21. Each equipment item has a `StatModifiers` array of `StatModifierEntry` structs. `StatModifierEntry` is a named `[Serializable]` struct with two fields: `StatID StatId` and `float FlatBonus`. An item may define up to 2 stat modifier entries (authoring budget — enforced by data validation tools, not at runtime). The cap of 2 keeps the total equipment modifier count (2 × 6 slots = 12 entries) within the 16-entry Character Stats equipment modifier layer capacity.
22. At MVP, all stat modifiers are **flat bonuses only**. `FlatBonus` contributes to `ΣFlatEquip` in the Character Stats three-layer modifier stack. The Equipment System passes `pctBonus = 0.0f` in every call to `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)`. Percentage equipment bonuses (`ΣPctEquip`) are reserved for post-MVP content.
23. A `StatModifierEntry` with `FlatBonus = 0.0` must not be authored. Data validation at import time flags zero-contribution entries as errors.
24. `FlatBonus` may be negative (e.g., heavy armor reducing MovementSpeed). The validator emits a warning (not a rejection) when a negative `FlatBonus` is detected, naming the item and the stat for designer confirmation.

**Rule 8 — Consumables**

25. Each consumable record defines: `EffectType` (`RestoreHP` or `RestoreMP` in MVP), `EffectMagnitude: float` (amount restored), `CooldownSeconds: float` (per-type cooldown duration), and `StackLimit: int` (max quantity per inventory slot).
26. HP Potions and MP Potions have independent per-type cooldown timers. Using an HP Potion starts the HP Potion cooldown only — the MP Potion cooldown is unaffected. Cooldown enforcement is runtime state owned by the Consumable Use System (not yet GDD'd); `CooldownSeconds` is the authored duration that system reads.
27. MVP consumable types: HP Potion (Small / Medium / Large) and MP Potion (Small / Medium / Large) — 6 consumable records total.

**Rule 9 — Upgrade Eligibility**

28. `IsUpgradeable: bool` is stored on each item record. All equipment items are `IsUpgradeable = true` in MVP — every piece of equipment can be put through the Enhancement System. All consumables are `IsUpgradeable = false`. The Enhancement System GDD owns `MAX_ENHANCEMENT_LEVEL` (a universal constant), the destruction threshold, enhancement cost curves, and success probability tables.

**Rule 10 — Economy**

29. `SellPriceGold: int` is the gold received when selling to an NPC vendor. `SellPriceGold = 0` means the item cannot be sold to NPCs. All items carry this field. Sell prices scale by gear tier (see Tuning Knobs).
30. NPC buy prices are NOT stored in the Item Database. The NPC Shop GDD owns shop stock and pricing independently. **Cross-document constraint for NPC Shop GDD**: NPC buy prices for any item must be ≥ `SellPriceGold × 1.5` (provisional markup floor) to prevent buy-and-resell arbitrage. This invariant derives from the sell prices defined here and must be documented as an explicit dependency constraint in the NPC Shop GDD.

**Rule 11 — ItemID Assignment**

31. `ItemID` values are assigned by the designer at authoring time. Valid range: `[1, 2^32−1]`. `ItemID(0)` is permanently reserved as `ItemID.Invalid`.
32. `ItemID` values are never reused after assignment. If an item is retired, its record is disabled in the data source — its `ItemID` is not recycled and not reassigned.

**Rule 12 — MVP Item Count**

33. MVP Item Database contains exactly 34 authored item records:

| Category | Count | Breakdown |
|----------|-------|-----------|
| Weapons | 4 | 1 type (Sword) × 4 tiers |
| Helmets | 4 | 1 type × 4 tiers |
| Chest Armor | 4 | 1 type × 4 tiers |
| Leg Armor | 4 | 1 type × 4 tiers |
| Boots | 4 | 1 type × 4 tiers |
| Rings | 4 | 1 type × 4 tiers *(OQ-3 resolved 2026-05-15)* |
| Necklaces | 4 | 1 type × 4 tiers *(OQ-3 resolved 2026-05-15)* |
| HP Potions | 3 | Small / Medium / Large |
| MP Potions | 3 | Small / Medium / Large |
| **Total** | **34** | |

Additional weapon sub-types (Axe, Mace, etc.) are Vertical Slice additions, designed after the Class System GDD resolves weapon-class differentiation (OQ-2 — now closed for MVP).

---

### States and Transitions

The Item Database is stateless at runtime. It has one lifecycle transition and no runtime state machine.

| Lifecycle State | Trigger | Observable Effect |
|----------------|---------|------------------|
| **Uninitialized** | Application start, before initialization completes | `IsReady == false`. Query calls are invalid; dev builds log an error and return `null`. |
| **Ready** | `Initialize()` completes (synchronous, during initialization scene before main menu) | `IsReady == true`. `OnDatabaseReady` fires once. All downstream systems may now query. |

No further state transitions occur during a gameplay session. The database does not unload, reload, or enter error states at runtime. Application shutdown destroys the owning Unity object through normal teardown — no explicit shutdown required.

---

### Interactions with Other Systems

| System | Direction | Interface | When |
|--------|-----------|-----------|------|
| **Inventory System** | ← reads | `GetItem(ItemID)` → `DisplayName`, `IconAddress`, `StackLimit`, `SellPriceGold`, `ItemCategory` | On pickup, inventory open, sell action |
| **Equipment System** | ← reads | `GetItem(ItemID)` → `ItemCategory`, `EquipmentData.GearSlot`, `EquipmentData.StatModifiers[]`, `EquipmentData.ElementType`, `EquipmentData.ElementalDamage` | On equip/unequip; confirms `ItemCategory == Equipment`, then passes `StatModifiers[]` to Character Stats |
| **Loot Table System** | ← reads | `GetItemsByCategory(ItemCategory.Equipment)` at startup | Pre-indexes drop pools at init; not called per-drop |
| **Enhancement System** | ← reads | `GetItem(ItemID)` → `IsUpgradeable`, `EquipmentData.StatModifiers[]` (base stat reference for scaling) | On enhancement attempt |
| **NPC Shop** | ← reads | `GetItem(ItemID)` → `DisplayName`, `SellPriceGold` | On shop open, on sell |
| **Damage Calculation** | ← reads | `GetItem(ItemID)` → `EquipmentData.ElementType`, `EquipmentData.ElementalDamage` (equipped weapon only, ID provided by Equipment System) | Per damage event when elemental component is present |
| **VFX System** | ← reads | `GetItem(ItemID)` → `EquipmentData.ElementType` | On weapon enhancement reaching ≥+7; determines glow color |
| **Inventory UI / Equipment UI** | ← reads | `GetItem(ItemID)` → `DisplayName`, `Description`, `IconAddress` | On tooltip, item inspect screen |
| **Character Stats** | Indirect | `ItemID` is the modifier key in `AddEquipmentModifier()`. Character Stats does not query Item Database directly — Equipment System is the bridge. `ΣPctEquip` contribution from items is 0.0 at MVP (flat bonuses only). | — |

---

### Schema Reference

Canonical field list for all types. This table is authoritative — it supersedes any field mentions elsewhere in this document if they conflict.

**`ItemDefinition`** (C# `class`, extends `UnityEngine.ScriptableObject`)

> **Implementation note**: Each `ItemDefinition` is a `.asset` file in the Unity project (one asset per item record). `ScriptableObject` inheritance is required for per-asset `OnValidate()` import hooks, independent Addressables addressing, and correct `[SerializeReference]` semantics on sub-data fields. Implement as `public class ItemDefinition : ScriptableObject`.

| Field | Type | Notes |
|-------|------|-------|
| `ItemId` | `ItemID` | `readonly struct` wrapping `uint`; never 0 |
| `DisplayName` | `string` | Player-visible name |
| `Description` | `string` | Tooltip body text |
| `IconAddress` | `string` | Addressables key for icon sprite |
| `ItemCategory` | `ItemCategory` | `Equipment` or `Consumable` |
| `SellPriceGold` | `int` | ≥ 0; 0 means unsellable to NPCs |
| `IsUpgradeable` | `bool` | `true` for all MVP equipment; `false` for all consumables |
| `StackLimit` | `int` | Equipment: always 1 (validator enforces); Consumables: authored ≥ 1 |
| `EquipmentData` | `EquipmentData?` | `null` when `ItemCategory == Consumable`. **Implementation note**: must use `[SerializeReference]` in Unity to preserve null semantics — Unity's serializer creates a default instance for class fields without it, causing silent null-check passes on Consumable items. |
| `ConsumableData` | `ConsumableData?` | `null` when `ItemCategory == Equipment`. Same `[SerializeReference]` requirement as `EquipmentData`. |

**`EquipmentData`** (C# `class`, nullable sub-schema)

| Field | Type | Notes |
|-------|------|-------|
| `GearSlot` | `GearSlot` | `Weapon`, `Helmet`, `Chest`, `Legs`, `Boots`, `Ring`, or `Necklace` *(OQ-3 resolved 2026-05-15)* |
| `GearTier` | `GearTier` | `Bronze`, `Iron`, `Steel`, or `DarkSteel` |
| `StatModifiers` | `StatModifierEntry[]` | Max 2 entries; flat bonuses only at MVP (2 × 6 slots = 12 entries ≤ 16-entry Character Stats equipment cap) |
| `ElementType` | `ElementType` | Weapons only; all other slots must be `None` |
| `ElementalDamage` | `int` | [0, 9,999]; weapons only; 0 for non-elemental weapons |
| `EquipRequirementStat` | `StatID?` | Nullable. The stat checked against `EquipRequirementMin` before equip is allowed. `null` = no stat gate (Ring, Necklace at MVP). Added 2026-05-22 per Equipment System upstream contract (OQ-EQS-2). |
| `EquipRequirementMin` | `float` | Minimum base stat value required to equip (checked via `GetBaseStat(entityID, EquipRequirementStat)`). Ignored when `EquipRequirementStat` is `null`. Must be ≥ 0. |
| `MergeResultItemID` | `ItemID?` | Accessories only (Ring, Necklace). The ItemID produced when 3 of this accessory are merged (CR-EQS-14). `null` for non-accessories and for accessories at AccessoryLevel +10 (maximum — no further upgrades). Added 2026-05-22 per Equipment System upstream contract (F-EQS-5). |

**`ConsumableData`** (C# `class`, nullable sub-schema)

| Field | Type | Notes |
|-------|------|-------|
| `EffectType` | `EffectType` | `RestoreHP` or `RestoreMP` at MVP |
| `EffectMagnitude` | `float` | > 0. **Flat HP or MP restored** (e.g., `150.0` restores 150 HP for `RestoreHP`, or 150 MP for `RestoreMP`). Not a percentage of `MaxHP` / `MaxMP`. |
| `CooldownSeconds` | `float` | ≥ 0.0; zero is valid but emits a validation warning |

**`StatModifierEntry`** (C# `[Serializable]` struct — NOT a ValueTuple)

| Field | Type | Notes |
|-------|------|-------|
| `StatId` | `StatID` | Must match a valid entry in the `StatID` enum. **IL2CPP validation note**: do not use `Enum.IsDefined` — it boxes the value and may fail under aggressive iOS stripping. Build a `static readonly HashSet<StatID>` from `Enum.GetValues<StatID>()` once at startup; use `Contains()` with `StatIDComparer.Instance` for per-entry validation. Add `StatID` to `link.xml` preservation to prevent stripping. |
| `FlatBonus` | `float` | Contributes to `ΣFlatEquip`; may be negative (penalty item); must not be 0.0 |

**`IItemDatabase`** (interface — injected to all consumers)

| Method / Property | Signature | Notes |
|-------------------|-----------|-------|
| `GetItem` | `ItemDefinition? GetItem(ItemID id)` | Sync; returns `null` for Invalid, unregistered, or retired IDs |
| `TryGetItem` | `bool TryGetItem(ItemID id, out ItemDefinition item)` | Preferred for callers that need to branch on existence |
| `GetItemsByCategory` | `IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)` | Pre-indexed at `Initialize()`; invalid enum → empty list + dev error |
| `IsReady` | `bool IsReady { get; }` | `false` until `Initialize()` completes |
| `OnDatabaseReady` | `event Action OnDatabaseReady` | Fires exactly once on `Initialize()` completion. **Late-subscriber contract**: if `IsReady` is already `true` when a handler is added, the handler is invoked immediately on the calling thread. |

## Formulas

The Item Database is a data schema, not a computational system. Its formulas define the mathematical structure of its fields — bounds, scaling lookup tables, and inter-system constraints — rather than runtime calculations.

---

**F-1: Equipment Sell Price**

The Equipment Sell Price formula is defined as:

`SellPriceGold = TierBasePrice(GearTier)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Gear tier | `GearTier` | enum | {Bronze, Iron, Steel, DarkSteel} | The item's tier |
| Tier base price | `TierBasePrice` | int | {10, 30, 90, 270} | Sell price for all items of this tier |

**Lookup table (tuning knobs — see Section G):**

| GearTier | TierBasePrice |
|----------|--------------|
| Bronze | 10g |
| Iron | 30g |
| Steel | 90g |
| DarkSteel | 270g |

**Output Range:** 10g (Bronze) to 270g (DarkSteel) under normal authored items.

**Example:** Iron Sword: `30g`. DarkSteel Sword: `270g`.

**Not stored as a derived field.** `SellPriceGold` is authored directly on each item record, not computed at runtime from F-1. F-1 is the authoring guide for consistency. Data validation tools flag deviations of more than ±5% as warnings. **Tolerance specification**: 5% of `TierBasePrice` (not of the authored value); fractional-gold results rounded via `Mathf.RoundToInt`; threshold is exclusive (strictly > 5% is flagged). Tolerance windows at default tuning: Bronze [9g, 11g], Iron [28g, 32g], Steel [85g, 95g], DarkSteel [256g, 284g].

---

**F-2: Consumable Sell Price**

Consumable sell prices are fixed authored values, not derived from F-1. They scale linearly within each potion size type.

`ConsumableSellPrice(size) = SmallBasePrice × SizeMultiplier(size)`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Small base price | `SmallBasePrice` | int | [1, 10]; default 2 | Sell value of the Small variant. Resolved: `SmallBasePrice = 2g` (OQ-9). |
| Size multiplier | `SizeMultiplier` | float | {1.0, 3.0, 9.0} | Multiplier per size tier |

| Potion Size | SizeMultiplier | HP/MP Potion Sell Price |
|-------------|---------------|------------------------|
| Small | 1.0 | 2g |
| Medium | 3.0 | 6g |
| Large | 9.0 | 18g |

**Output Range:** 2g (Small) to 18g (Large) at default tuning (`SmallBasePrice = 2`, `SizeMultiplier(Large) = 9.0`). *(OQ-9 resolved 2026-06-06: prior invariant `SmallBasePrice × SizeMultiplier(Large) ≤ TierBasePrice(Bronze)` superseded — see OQ-9 resolution below.)*

**Example:** HP Potion (Large): `2 × 9.0 = 18g`. MP Potion (Medium): `2 × 3.0 = 6g`.

**Rounding:** Output rounded via `Mathf.RoundToInt`. Tolerance specification: 5% of the formula output, rounded the same way; threshold is exclusive (strictly > 5% is flagged).

---

**F-3: ElementalDamage Valid Range**

`ElementalDamage ∈ [0, 9,999]`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Elemental damage | `ElementalDamage` | int | [0, 9,999] | Base elemental bonus at +0 enhancement |

**Constraint source:** The ceiling of 9,999 matches the ceiling of `MagicDefense` in the Character Stats GDD (F-7 range [0, 9,999]). No weapon should exceed this value at +0 — doing so would guarantee unmitigated elemental overflow even against a maximally defended target.

`ElementalDamage = 0` is valid and represents a physical-only weapon regardless of `ElementType`. However, `ElementType.None` with `ElementalDamage > 0` is a data authoring error — data validation must flag it.

**Example:** A Fire Sword with `ElementalDamage = 25` at +0 enhancement deals 25 fire damage per hit before `MagicDefense` mitigation. Enhancement scaling of elemental damage (if any) is defined in the Enhancement System GDD.

**Floor case — flagged to Damage Calculation GDD:** `MagicDefense` defaults to 0 in Character Stats. At `MagicDefense = 0` (plausible for Bronze-tier mobs), any non-zero `ElementalDamage` is fully unmitigated. A weapon authored at the 9,999 ceiling deals full unmitigated elemental burst at +0 enhancement against an undefended mob. The practical authored ceiling for `ElementalDamage` must be calibrated against expected per-tier mob `MagicDefense` values when the Damage Calculation GDD is authored. This calibration is an explicit input requirement for that GDD.

---

*F-4 (PctBonus caps by rarity) was removed. Rarity and percentage equipment modifiers are not part of the MVP design. All MVP equipment uses flat bonuses only. The Character Stats modifier stack supports `ΣPctEquip` for Vertical Slice content when needed — the formula is already compatible, items simply pass `pctBonus = 0.0f` at MVP.*

## Edge Cases

**Query-time conditions**

- **If `GetItem(ItemID.Invalid)` is called** (`ItemID(0)`): return `null` immediately without querying the collection. No error logged — `ItemID.Invalid` is a valid sentinel for empty gear slots and callers are expected to null-check.

- **If any system calls `GetItem` while `IsReady == false`**: return `null` and log an error in dev builds ("Item Database queried before initialization"). Do not throw — a thrown exception crashes the initialization sequence.

- **If `GetItem` is called with a valid but unregistered ID** (e.g., a stale save file referencing a retired item): return `null`. The database does not know if the ID came from a save file or a programming error. The caller (Character Persistence, Inventory System) is responsible for logging a warning and cleaning up stale references.

- **If `GetItemsByCategory` is called with a value outside the `ItemCategory` enum** (e.g., via incorrect casting): return an empty `IReadOnlyList<ItemDefinition>` and log a dev-build error. Do not throw.

**Elemental damage**

- **If a weapon is authored with `ElementType.None` and `ElementalDamage > 0`**: data validation error — reject the record at import. Damage Calculation branches on `ElementType`; a physical weapon with nonzero elemental damage would bypass `MagicDefense` silently.

- **If a weapon is authored with `ElementType != None` and `ElementalDamage = 0`**: valid. Represents a weapon with elemental affinity but no base bonus at +0 — useful if the Enhancement System scales elemental damage from zero. The VFX System still activates the glow at ≥+7 for this weapon.

- **If a non-weapon equipment record is authored with `ElementalDamage > 0` or `ElementType != None`**: data validation error — reject the record. No downstream system reads elemental fields from non-weapon slots.

**Stat modifier rules**

- **If a `StatModifierEntry` has `FlatBonus = 0.0`**: data validation error — reject the record. Zero-contribution entries waste one of the 2 authoring budget slots and cause Character Stats to fire `OnStatChanged` for a stat that did not change, consuming a modifier layer slot without effect.

- **If an equipment record has 3 or more `StatModifier` entries**: data validation error — reject the record. The cap is 2 entries per item (2 × 6 slots = 12 entries), which stays safely within the 16-entry Character Stats equipment modifier capacity. Raising the per-item cap beyond 2 requires raising the Character Stats cap and an ADR.

- **If a `StatModifierEntry` has `FlatBonus < 0.0`**: validation warning, not a hard error. A design penalty (e.g., heavy chest reducing MovementSpeed) is intentionally negative. The validation tool must emit the item name, stat ID, and value for designer confirmation.

- **If an equipment record has an empty `StatModifiers` array (zero entries)**: valid. Contributes nothing to `ΣFlatEquip` or `ΣPctEquip`. The Equipment System calls `AddEquipmentModifier` zero times for this item — no Character Stats impact.

- **If a `StatModifier` entry references a `StatID` not present in the `StatID` enum**: data validation error — reject the record. Character Stats silently drops unrecognized `StatID` values in release builds, producing invisible stat loss with no runtime signal.

- **If the same `StatID` appears more than once in a single item's `StatModifiers` array**: validation warning, not a hard error. The net effect is additive (Character Stats adds both entries independently), but the likely intent was one consolidated entry. The validation tool must name the item, the duplicated `StatID`, and both values for designer acknowledgment.

- **If a `StatModifier` `FlatBonus` for a float-schema stat (e.g., `CritChance`, max 0.75) alone exceeds that stat's ceiling**: validation warning. Character Stats will clamp silently; the warning surfaces what is likely a decimal authoring error (e.g., `5.0` intended as `0.05`).

**Category constraint violations**

- **If a consumable record has any `GearSlot` or `GearTier` value other than `None`**: data validation error — reject the record. Consumables have no slot and no tier; a mismatch would produce incorrect F-1 sell price expectations.

- **If an equipment record has any `ConsumableData` fields set** (`EffectType`, `EffectMagnitude`, `CooldownSeconds`) **or `StackLimit > 1`**: data validation error — reject the record. Equipment is not consumable; `StackLimit = 1` is mandatory for all equipment.

**Economy fields**

- **If `SellPriceGold` does not match the F-1 / F-2 authoring guide by more than ±5%**: validation warning. F-1 is an authoring guide, not a runtime formula. Small deviations from `Mathf.RoundToInt` are acceptable; deviations beyond ±5% require designer acknowledgment before import clears.

- **If `SellPriceGold` substantially exceeds 270g** (the F-1 maximum for DarkSteel — e.g., values above 300g not covered by the ±5% tolerance): validation warning. A higher value implies a magnitude authoring error (e.g., typing `2700` instead of `270`) or an unapproved future tier.

**Consumable rules**

- **If a consumable has `StackLimit = 0`**: data validation error — reject the record. A stack limit of zero makes the item impossible to carry.

- **If a consumable has `EffectMagnitude ≤ 0`**: data validation error — reject the record. A zero-magnitude restoration is a no-op; a negative value would drain the resource on use, a behavior the Consumable Use System does not support in MVP.

- **If a consumable has `CooldownSeconds = 0.0`**: validation warning, not a hard error. A zero cooldown allows per-frame use, effectively removing resource management from consumables. The designer must acknowledge the warning. Minimum recommended value: 0.5 seconds.

**ItemID rules**

- **If two item records share the same `ItemID`**: fatal validation error — abort the entire database load and log both conflicting record names. Do not silently overwrite one with the other. A duplicate `ItemID` violates Rule 1 and would make one definition silently unreachable.

- **If `GetItem` is called with an ID belonging to a retired (disabled) record**: return `null`. Disabled records are excluded from the runtime dictionary at load time. Their `ItemID` remains permanently reserved and must never be reassigned (Rule 32).

- **If `ItemID = 4,294,967,295` (uint maximum) is assigned to an item**: valid. The only reserved `ItemID` value is `0` (`ItemID.Invalid`). All other uint values including the maximum are available for assignment.

**Cross-system stacking concern**

*No MVP edge case.* At MVP all equipment uses flat bonuses only (`ΣPctEquip = 0`). Percentage equipment modifier stacking edge cases are deferred to the Vertical Slice when `pctBonus` authoring is introduced.

## Dependencies

**Upstream dependencies:** None. Item Database is a Foundation layer system with no dependencies on any other system. It is a pure data store loaded before any other game system initializes.

**Downstream dependents** — systems that depend on Item Database:

| System | Dependency Type | Interface | Hard or Soft |
|--------|----------------|-----------|-------------|
| **Inventory System** | Reads | `GetItem(ItemID)` → `DisplayName`, `IconAddress`, `StackLimit`, `SellPriceGold`, `ItemCategory` | **Hard** — Inventory cannot represent items without item definitions |
| **Equipment System** | Reads | `GetItem(ItemID)` → `ItemCategory`, `EquipmentData.GearSlot`, `EquipmentData.StatModifiers[]`, `EquipmentData.ElementType`, `EquipmentData.ElementalDamage` | **Hard** — Equipment System cannot call `AddEquipmentModifier()` without stat modifier data |
| **Loot Table System** | Reads | `GetItemsByCategory(ItemCategory.Equipment)` at startup; `ItemID` references in loot entries | **Hard** — Drop pools cannot be built without item records |
| **Enhancement System** | Reads | `GetItem(ItemID)` → `IsUpgradeable`, `EquipmentData.StatModifiers[]` | **Hard** — Enhancement System must verify `IsUpgradeable` before any enhancement attempt |
| **NPC Shop** | Reads | `GetItem(ItemID)` → `DisplayName`, `SellPriceGold` | **Hard** — Shop cannot display or value items without item definitions |
| **Mob Spawning** | Reads | `ItemID` references in mob data tables (drop configurations) | **Hard** — Mobs reference items by `ItemID`; undefined IDs produce null on pickup |
| **Damage Calculation** | Reads | `GetItem(ItemID)` → `ElementType`, `ElementalDamage` (equipped weapon only) | **Hard** for elemental weapons; physical damage is unaffected by Item Database |
| **VFX System** | Reads | `GetItem(ItemID)` → `ElementType` | **Soft** — Enhancement visual glow is absent if Item Database is unavailable; gameplay continues |
| **Consumable Use System** | Reads | `GetItem(ItemID)` → `ConsumableData.CooldownSeconds`, `ConsumableData.EffectType`, `ConsumableData.EffectMagnitude` | **Hard** — Consumable Use System cannot apply effects or enforce cooldowns without authored parameters |
| **Inventory UI** | Reads | `GetItem(ItemID)` → `DisplayName`, `Description`, `IconAddress` | **Hard** for MVP — UI cannot render item entries without display data |
| **Equipment UI** | Reads | `GetItem(ItemID)` → `DisplayName`, `Description`, `IconAddress` | **Hard** for MVP — Equipment screen cannot render without item definitions |

**Indirect connection:**
- **Character Stats** references `ItemID` as the equipment modifier key in `AddEquipmentModifier()` but does not query Item Database directly. The Equipment System is the bridge between Item Database and Character Stats.

**Bidirectionality note:** Item Database has no upstream GDD to update. When each downstream system above is authored, its GDD must list `Item Database` in its own Dependencies section. This is a flagged requirement — the author of each downstream GDD is responsible for the bidirectional entry.

**No dependency on:**
- Currency System — sell prices are integers stored directly on items; no currency logic is needed at the Item Database layer
- Networking Core — Item Database is read-only static data; it has no network state
- Authentication / Character Persistence — persistence stores `ItemID` values only; item properties are re-read from this database on load, but persistence does not depend on it at initialization time (the database loads first)

## Tuning Knobs

All values in this section are designer-adjustable without code changes. They are stored in item records as authored data (F-1, F-2) or as named constants in the data validation / import pipeline (F-4 caps, authoring limits).

---

**Sell Price Scaling (F-1) — Equipment**

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| `TierBasePrice(Bronze)` | 10g | [5, 50] | Too high: new players accumulate gold too quickly, undermining the currency grind. Too low: selling dropped items feels pointless. |
| Tier-to-tier multiplier | ×3 | [×2, ×5] | Too low: Iron and Steel sell prices feel indistinguishable from Bronze. Too high: a single tier-advance makes the previous zone's economy irrelevant. |
| `TierBasePrice(DarkSteel)` | 270g | [100, 500] | **Invariant constraint**: DarkSteel sell price must remain less than the expected total Enhancement System cost from +0→+9 at the design success rate — otherwise selling beats building, and +9 weapons stop existing as server events. This invariant must be verified against the Enhancement System GDD (design order #18) before locking DarkSteel sell prices. (See OQ-8.) |

*All four tier prices are individually tunable. The ×3 multiplier is the current default; individual tier prices can deviate from it as long as the DarkSteel invariant (above) is maintained.*

---

**Sell Price Scaling (F-2) — Consumables**

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| `SmallBasePrice` (HP/MP Potion, Small) | 2g *(OQ-9)* | [1, 10] | Too high: Large Potion sell value approaches or exceeds Bronze gear sell value, making consumable farming more rewarding than gear farming in Bronze zones. Too low: consumable economy feels unmoored from the rest of the sell price table. |
| `SizeMultiplier(Medium)` | 3.0 | [2.0, 5.0] | Too low: Medium and Small potions are indistinguishable in value. Too high: Medium potions become the dominant sell target, making Small potions irrelevant. |
| `SizeMultiplier(Large)` | 9.0 | [5.0, 20.0] | Too low: Large potions don't represent a meaningful gold sink over multiples of Small. Too high: Large potions become so valuable that players hoard and sell them instead of using them — defeats the HP/MP sustain intent. |

---

**Flat Bonus Gear Ceiling (L60 reference)**

At MVP, all equipment stat modifiers are flat bonuses only (`ΣPctEquip = 0` for all equipment). The effective equipment contribution is:

`EffectiveAP = floor((BaseStat + ΣFlatEquip) × (1 + ΣPctEquip) × (1 + ΣPctBuff))`

With `ΣPctEquip = 0` from equipment: `EffectiveAP = floor(BaseStat + ΣFlatEquip) × (1 + ΣPctBuff)`.

**Flat bonus authoring ceiling** — exact flat bonus values per stat and tier are defined in OQ-1. The ceiling guideline is: gear should contribute meaningfully to the L60 character without overshadowing base stat investment.

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| Flat AP bonus per weapon (guideline) | TBD (OQ-1) | [10, 80] | Too high: gear dominates over base stats; STR investment feels irrelevant. Too low: gear progression feels cosmetic. Exact values require OQ-1 resolution. |

---

**Authoring Budget Limits**

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| Max `StatModifier` entries per equipment item | 2 | [1, 2] | Cap is set at 2 so that total equipment modifiers (2 × 6 slots = 12) stay within the 16-entry Character Stats hard cap. Raising to 3 requires raising Character Stats cap to ≥18; raising to 6 requires ≥36. Any increase beyond 2 requires an ADR and a Character Stats GDD amendment before re-approval. |
| `ElementalDamage` ceiling | 9,999 | [500, 9,999] | Ceiling is constrained by the `MagicDefense` ceiling in Character Stats (9,999). Raising this above 9,999 requires a corresponding change to Character Stats — cross-system coordination required. |

---

**MVP Item Count**

| Knob | Default | Safe Range | What Breaks |
|------|---------|-----------|-------------|
| Weapon sub-types per tier | 1 (Sword) at MVP | — | Additional weapon types (Axe, Mace) are VS additions after the Class System GDD resolves weapon-class differentiation. The single-type MVP eliminates retroactive player investment risk from undefined differentiation. |
| Armor types per slot | 1 | [1, 2] | Increasing to 2 doubles the armor authoring burden and requires the Class System GDD to distinguish which armor types are class-locked vs. universal. |
| Consumable size tiers | 3 (Small/Medium/Large) | [2, 3] | Reducing to 2 (Small/Large only) simplifies economy but removes the mid-game currency sink. |

## Visual/Audio Requirements

N/A — Item Database is a stateless data schema. It has no direct audio or visual output. All audio cues, visual effects, and icon rendering are owned by downstream systems (VFX System, Inventory UI, Equipment UI) that read from this database.

## UI Requirements

N/A — Item Database is a stateless data schema with no user interface. All item display, tooltip rendering, and inventory screens are owned by Inventory UI and Equipment UI, which read display data (`DisplayName`, `Description`, `IconAddress`) from this database.

## Acceptance Criteria

**AC-1** [BLOCKING]
GIVEN the Item Database has completed initialization, WHEN `GetItem(ItemID.Invalid)` is called (`ItemID(0)`), THEN the return value is `null`, no exception is thrown, and no error is logged.

**AC-2** [BLOCKING]
GIVEN the database contains a record with `ItemID(1)`, WHEN `GetItem(new ItemID(1))` is called from two independent callers in the same frame, THEN both return the exact same `ItemDefinition` reference (reference-equal, not just value-equal), confirming single authoritative data with no per-caller variation.

**AC-3** [BLOCKING]
GIVEN a validator function receives two records sharing the same `ItemID`, WHEN the validator is invoked, THEN it returns a fatal error naming both records, and the database loader aborts without registering either record.

**AC-4** [BLOCKING]
GIVEN the import validator receives an equipment record with `StackLimit ≠ 1`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-5** [BLOCKING]
GIVEN the import validator receives a consumable record with `GearSlot ≠ None` or `GearTier ≠ None`, WHEN the validator runs, THEN it returns an error and rejects the record. *(Test independently: one test for `GearSlot ≠ None` with `GearTier == None`; one test for `GearTier ≠ None` with `GearSlot == None`; one test for both conditions simultaneously.)*

**AC-6** [REMOVED — Pass 1: duplicate of AC-34 with conflicting severity label. BLOCKING version retained as AC-34.]

**AC-7** [BLOCKING]
GIVEN the import validator receives an equipment record with `GearSlot` outside `{Weapon, Helmet, Chest, Legs, Boots, Accessory}`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-8** [BLOCKING]
GIVEN the import validator receives a non-weapon equipment record with `ElementType ≠ None` or `ElementalDamage > 0`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-9** [REMOVED — Pass 3: strict subset of AC-5 (`GearTier ≠ None` branch). See AC-5 split test note for `GearTier` coverage.]

**AC-10** [BLOCKING]
GIVEN the import validator receives an equipment record with `GearTier = None`, WHEN the validator runs, THEN it returns an error and rejects the record. *(Equipment must carry exactly one of Bronze/Iron/Steel/DarkSteel.)*

**AC-11** [BLOCKING]
GIVEN the import validator receives any equipment `StatModifierEntry` with `FlatBonus = 0.0`, WHEN the validator runs, THEN it returns an error and rejects the record. *(Zero flat bonus is a zero-contribution entry — see Rule 7.)*

**AC-12** [BLOCKING]
GIVEN the import validator receives a weapon record with `ElementType.None` and `ElementalDamage > 0`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-13** [BLOCKING]
GIVEN the import validator receives a weapon record with `ElementType = Fire` and `ElementalDamage = 0`, WHEN the validator runs, THEN it returns a pass result and accepts the record. *(Non-None element type with zero elemental damage is valid.)*

**AC-14** [BLOCKING]
GIVEN the import validator receives a weapon record with `ElementalDamage = 10,000` (one above ceiling), WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-15** [BLOCKING]
GIVEN the import validator receives an equipment record with 3 or more `StatModifier` entries, WHEN the validator runs, THEN it returns an error and rejects the record. *(Max 2 entries per item enforces the 2 × 6 = 12 ≤ 16 Character Stats capacity constraint.)*

**AC-16** [REMOVED — Pass 2: verbatim duplicate of AC-11.]

**AC-17** [ADVISORY]
GIVEN the import validator receives an equipment `StatModifierEntry` with `FlatBonus < 0.0`, WHEN the validator runs, THEN it emits a warning naming the item and the `StatId`, and accepts the record (does not reject it). *(Negative flat bonuses are valid penalty modifiers.)*

**AC-18** [BLOCKING]
GIVEN the database has completed initialization with exactly 34 MVP item records, WHEN `GetItemsByCategory(ItemCategory.Equipment)` is called, THEN the return value is an `IReadOnlyList<ItemDefinition>` with `Count == 28`, every element has `ItemCategory == Equipment`, and no element has `ItemCategory == Consumable`. *(Count updated 2026-05-15: OQ-3 resolution adds 4 Ring + 4 Necklace records.)*

**AC-19** [BLOCKING]
GIVEN the database has completed initialization, WHEN `GetItemsByCategory` is called with an integer cast to `ItemCategory` that falls outside `{Equipment, Consumable}`, THEN the return value is an empty `IReadOnlyList<ItemDefinition>`, a dev-build error is logged, and no exception is thrown.

**AC-20** [REMOVED — Pass 1.]

**AC-21** [BLOCKING]
GIVEN the import validator receives a consumable record with `EffectMagnitude ≤ 0`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-22** [BLOCKING]
GIVEN the import validator receives a consumable record with `StackLimit = 0`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-23** [ADVISORY — BLOCKED: requires Consumable Use System]
GIVEN the Consumable Use System holds independent per-type cooldown state for one entity, WHEN an HP Potion is used (starting the HP Potion cooldown), THEN checking whether an MP Potion is on cooldown for that entity returns `false`. *(Integration test — untestable until Consumable Use System GDD is authored and implemented. Carry to that implementation sprint.)*

**AC-24** [BLOCKING]
GIVEN the database has completed initialization, WHEN all 34 MVP item records are enumerated, THEN every Equipment record has `IsUpgradeable = true` and every Consumable record has `IsUpgradeable = false` with no exceptions.

**AC-25** [BLOCKING]
GIVEN the import validator receives any item record with `SellPriceGold < 0`, WHEN the validator runs, THEN it returns an error and rejects the record.

**AC-26** [BLOCKING]
GIVEN the import validator receives a record with `ItemID = 0`, WHEN the validator runs, THEN it returns a fatal error and rejects the record.

**AC-27** [ADVISORY]
GIVEN the database contains a retired (disabled) item record with `ItemID(N)`, WHEN `GetItem(new ItemID(N))` is called at runtime, THEN the return value is `null` and no exception is thrown.

**AC-28** [BLOCKING]
GIVEN the Item Database has been constructed but `Initialize()` has not been called, WHEN `GetItem` is called with any valid `ItemID`, THEN the return value is `null`, `IsReady` is `false`, a dev-build error is logged, and no exception is thrown.

**AC-29a** [BLOCKING]
GIVEN the Item Database has been constructed and `Initialize()` is called, WHEN `Initialize()` completes, THEN `IsReady` is `true`.

**AC-29b** [BLOCKING]
GIVEN a handler is subscribed to `OnDatabaseReady` before `Initialize()` is called, WHEN `Initialize()` completes, THEN the handler is invoked exactly once. A second call to `Initialize()` does not fire `OnDatabaseReady` again.

**AC-29c** [BLOCKING]
GIVEN `Initialize()` has already completed (`IsReady == true`), WHEN a new handler is added to `OnDatabaseReady`, THEN the handler is invoked immediately on the calling thread (late-subscriber guarantee).

**AC-30** [BLOCKING]
GIVEN an authored Bronze Sword (`GearTier.Bronze`), WHEN `SellPriceGold` is read from the database, THEN `SellPriceGold = 10g` (F-1 minimum at default tuning).

**AC-31** [BLOCKING]
GIVEN an authored DarkSteel Sword (`GearTier.DarkSteel`), WHEN `SellPriceGold` is read from the database, THEN `SellPriceGold = 270g` (F-1 maximum at default tuning).

**AC-32** [ADVISORY]
GIVEN the import validator receives an equipment record where `SellPriceGold` deviates from the F-1 `TierBasePrice` value by more than ±5%, WHEN the validator runs, THEN it emits a warning (not a blocking error) naming the item, the authored value, and the F-1 expected value.

**AC-33** [BLOCKING]
GIVEN HP Potion (Small), (Medium), and (Large) records are in the initialized database with `SmallBasePrice = 2`, WHEN `SellPriceGold` is read from each, THEN Small = 2g, Medium = 6g, Large = 18g.

**AC-34** [BLOCKING]
GIVEN the database has completed initialization, WHEN all 34 MVP item records are enumerated, THEN exactly 28 are `ItemCategory.Equipment` and exactly 6 are `ItemCategory.Consumable`, with no record holding both categories. *(Updated 2026-05-15: OQ-3 resolution adds Ring and Necklace slots, each 1 type × 4 tiers.)*

**AC-35** [ADVISORY]
GIVEN the import validator receives an equipment record where `SellPriceGold = 0`, WHEN the validator runs, THEN it emits a warning (not a blocking error) naming the item, since F-1 defines a minimum of 10g for the lowest equipment tier and zero is likely a data authoring error.

**AC-36** [BLOCKING]
GIVEN the database has completed initialization and contains a record with `ItemID(1)`, WHEN `TryGetItem(new ItemID(1), out var def)` is called, THEN it returns `true` and `def` is the same reference returned by `GetItem(new ItemID(1))` (`object.ReferenceEquals(def, GetItem(new ItemID(1)))` is `true`).

**AC-37** [BLOCKING]
GIVEN the database has completed initialization, WHEN `TryGetItem(ItemID.Invalid, out var def)` is called, THEN it returns `false`, `def` is `null`, and no exception is thrown.

**AC-38** [BLOCKING]
GIVEN the database has been constructed but `Initialize()` has not been called, WHEN `TryGetItem` is called with any valid `ItemID`, THEN it returns `false`, the out parameter is `null`, `IsReady` is `false`, a dev-build error is logged, and no exception is thrown.

**AC-39** [REMOVED — Pass 3 numbering error: AC-40 was added as "40" instead of "39." AC count unaffected — total remains 38.]

**AC-40** [BLOCKING]
GIVEN the database has been constructed but `Initialize()` has not been called, WHEN `GetItemsByCategory(ItemCategory.Equipment)` is called, THEN the return value is an empty `IReadOnlyList<ItemDefinition>`, `IsReady` is `false`, a dev-build error is logged, and no exception is thrown.

**AC-41** [BLOCKING]
GIVEN the import validator receives an equipment record with a `StatModifierEntry` whose `StatId` does not match any defined value in the `StatID` enum, WHEN the validator runs, THEN it returns an error and rejects the record. *(Character Stats silently drops unrecognized `StatID` values in release builds, producing invisible stat loss with no runtime signal.)*

**BLOCKING: 33 | ADVISORY: 5 | Total: 38**

*QA lead flag: AC-29a–29c require `OnDatabaseReady` be a standard C# `event Action` (not `UnityEvent`) so the test harness can subscribe counter delegates without editor dependency. AC-29c (late-subscriber guarantee) must be tested by subscribing after `Initialize()` returns and verifying immediate invocation — specifically: set a `bool` flag in the handler and assert it `true` on the next line after the `+=` expression ("synchronously before the assignment returns").*

## Open Questions

**OQ-1 — Flat bonus authoring ranges by stat and tier**
MVP uses flat bonuses only. The specific flat bonus values per stat and per gear tier have not been authored (e.g., how much flat AttackPower should a Bronze Sword have vs. an Iron Sword). This must be resolved before authoring any actual item records. The L60 character stat snapshot in the Character Stats GDD provides the ceiling — gear flat bonuses should be calibrated so that a fully-geared character is meaningfully stronger than a character with base stats, but base stat investment still matters.
*Owner*: Game Designer / Economy Designer. *Target*: Before first item authoring pass, during Equipment System GDD.

**OQ-2 — Weapon sub-type differentiation** *(CLOSED for MVP)*
MVP ships one weapon type (Sword). Axe and Mace are deferred to Vertical Slice, to be designed after the Class System GDD resolves weapon-class mechanical differentiation. No open decision remains for MVP.
*Resolution date*: 2026-04-24. *VS Owner*: Game Designer. *VS Target*: Class System GDD (design order #17).

**OQ-3 — Accessory sub-type definition** *(CLOSED 2026-05-15)*
*Resolution*: Split into two distinct slots — `Ring=5` and `Necklace=6` — replacing the former single `Accessory=5`. GearSlot enum now has 7 values. MVP ships 1 Ring type × 4 tiers + 1 Necklace type × 4 tiers = 8 accessory records (34 total items, 28 equipment). Modifier count: 2 × 7 = 14 entries, within the 16-entry Character Stats cap. Equipment System GDD must enforce one item per Ring slot and one item per Necklace slot independently.

**OQ-4 — Enhancement System scaling of elemental damage**
EC-5 permits weapons with `ElementType != None` and `ElementalDamage = 0` (for weapons whose elemental damage scales from the Enhancement System). Whether and how the Enhancement System scales elemental damage is unresolved — does each enhancement level add a flat elemental bonus? Is the scaling independent of physical stat scaling? This must be answered in the Enhancement System GDD.
*Owner*: Systems Designer. *Target*: Enhancement System GDD (design order #18).

**OQ-5 — Consumable cooldown values (CooldownSeconds)**
The schema stores `CooldownSeconds` as a per-item authored field, but the actual values have not been tuned (e.g., HP Potion Small cooldown = 30s?). This needs to be locked before playtesting.
*Owner*: Game Designer. *Target*: First item authoring pass / Consumable Use System GDD.

**OQ-6 — ItemID authoring process**
Who assigns `ItemID` values and how is uniqueness enforced? Options include: sequential counter in a spreadsheet, auto-assigned by a ScriptableObject editor tool, or UUID-style random values. The duplicate-ID validation (AC-3) catches violations at import but does not prevent them. An explicit authoring process is needed before the first item record is created.
*Owner*: Tools Programmer / Lead Programmer. *Target*: Before first item authoring.

**OQ-7 — Character Persistence save/load of ItemID references**
EC-3 and EC-20 establish that `GetItem` returns `null` for stale or retired `ItemID` values, and callers are responsible for cleanup. The Character Persistence GDD must define the save format, how retired-item null returns are handled on load, and whether a migration pass is needed when items are retired post-launch.
*Owner*: Network Programmer / Character Persistence GDD. *Target*: Character Persistence GDD (design order #12).

**OQ-8 — DarkSteel sell price vs. Enhancement System cost curve** *(provisional pending Enhancement System GDD)*
The Legendary Gear pillar requires that selling a top-tier item is never the rational dominant strategy over enhancing it. The invariant: `SellPriceGold(DarkSteel) < expected total Enhancement System cost from +0→+9 at the designed success rate`. The current DarkSteel sell price (270g default) must be verified against the Enhancement System cost curve when that GDD is authored (design order #18). If the Enhancement System makes the cost curve cheaper than 270g, the DarkSteel sell price must be reduced or the cost curve adjusted.
*Owner*: Economy Designer + Systems Designer. *Target*: Enhancement System GDD (design order #18). *Current status*: 270g is provisional.

> **Economy design note (2026-04-25)**: Sell prices are acknowledged as very low at current tuning. The OQ-8 invariant must also be reframed: the correct comparison is *expected cost* of reaching +9 (accounting for failure/retry probability at each level), not advertised total cost. At low success rates, expected cost is substantially higher than total cost, and 270g could be the dominant rational choice even against a high total cost ceiling. Revisit the full sell price and enhancement cost table together after economy design and enhancement fail rates are established.
>
> **Enhancement prestige principle**: A +5 Bronze Sword is calibrated to have approximately the same effective stat contribution as a +0 Iron Sword. Enhancement levels are a social prestige and risk mechanism — enhancement should not outpace the base stats of the next gear tier. This calibration requires joint resolution of OQ-1 (flat bonus values by tier) and OQ-4 (enhancement scaling formula).

**OQ-9 — Consumable sell price defaults** *(RESOLVED 2026-06-06 — NPC Shop GDD authoring session)*
*Resolution (Path A)*: `SmallBasePrice = 2g`, `SizeMultiplier(Large) = 9.0` unchanged. Consumable sell prices: Small = 2g, Medium = 6g, Large = 18g. The prior invariant `SmallBasePrice × SizeMultiplier(Large) ≤ TierBasePrice(Bronze)` is superseded — Large Potion sell price (18g) intentionally exceeds Bronze gear sell price (10g). Anti-arbitrage is enforced via NPC Shop GDD F-NS-3 (BuyPrice ≥ SellPriceGold × 1.5). AC-33 already reflects the resolved values.
