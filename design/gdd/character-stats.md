# Character Stats

> **Status**: Approved (pass 5 — 2026-04-23)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-04-23
> **Implements Pillar**: Earned Power (primary), Rhythm Mastery (secondary)

## Overview

Character Stats is the authoritative data store for every numeric attribute that defines a character's combat capabilities and survivability. It holds the base values — health, mana, attack damage, defense, attack speed, attack range, movement speed, and any other attribute that a game system reads or modifies — and exposes them through a read interface queried live on demand. No gameplay system caches these values; any gameplay system that needs a stat value queries Character Stats at the point of use, so mid-combat changes from buffs, debuffs, or equipment swaps take effect immediately without requiring broadcasts or invalidation. Display systems (HUD, stat sheet) maintain HUD-local caches of the last notified value, updated via OnStatChanged — see Rule 8. Character Stats does not apply damage, calculate DPS, or enforce game rules — it holds numbers and answers queries. Every system that modifies character power (Leveling, Equipment, Status Effects) writes to Character Stats; every system that uses character power (Auto-Attack Combat, Damage Calculation, Skill System) reads from it. Character Stats is the foundation all other systems stand on.

## Player Fantasy

Character Stats is infrastructure — the player never reads a query or sees a delta. What they feel is the result.

**The Weight of Hours** *(Survivability — Earned Power)*: You feel yourself becoming harder to kill. The mobs that used to threaten you now bounce off your defense; the hits that once chunked your HP bar now barely move it. Every point of HP, every notch of defense, every tick of attack power is a receipt for hours you invested — and when you open your stats sheet, you're not reading numbers, you're reading the shape of what you endured to get them.

**The Sharpening Blade** *(Offensive identity — Rhythm Mastery)*: Every Warrior — whether their free points go to STR or VIT — shares this identity. Attack power climbs with STR; crit chance ticks upward with DEX; and at the tier breakpoints — L20, L40, L60 — both snap to a higher plateau instantly. The player should feel that crossing a tier boundary makes their character noticeably more dangerous: their auto-attack sequence lands harder, crits fire more often, and the rhythm of kills tightens. DEX investment rewards precise timing — the stat sheet shows a higher ASM value, but the felt payoff is that their window of opportunity within the 1.0s attack cadence widens. Note: "Warrior Tank" and "Warrior DPS" are player labeling conventions — both builds share identical CritChance and ASM progression, because both receive the same DEX auto-alloc. The Sharpening Blade is a class identity, not a build identity.

*Pillar alignment*: Earned Power (survivability stats are the record of genuine effort) and Rhythm Mastery (offensive stats gate into the auto-attack timing loop). Nothing in Character Stats is gifted; every value was changed by something the player did.

## Detailed Rules

### Core Rules

**Stat Schema**

1. Every character entity — player characters and enemy mobs alike — is represented by a single `CharacterStats` container holding the following stats. Mobs leave player-only fields at their default zero values; the query interface is identical for all entity types.

| Stat | Type | Default (Player) | Range | Notes |
|------|------|-----------------|-------|-------|
| `Level` | int | 1 | [1, 60] | MVP level cap is 60. Only Leveling System may write this. |
| `Experience` | int | 0 | [0, XP for level 60] | Added via `AddExperience()` only — never set directly. |
| `Strength` | int | 10 | [1, 999] | Primary attribute. Feeds AttackPower growth at level-up. |
| `Dexterity` | int | 10 | [1, 999] | Primary attribute. Feeds CritChance and AttackSpeedMultiplier growth at level-up. |
| `Vitality` | int | 10 | [1, 999] | Primary attribute. Feeds MaxHP growth at level-up. |
| `Intelligence` | int | 10 | [1, 999] | Primary attribute. Feeds MaxMP. Post-MVP: scales elemental attack power for mage. |
| `MaxHP` | int | 200 | [1, 99999] | Source of truth for HP pool. Never set below 1. |
| `CurrentHP` | float | = MaxHP at spawn | [0.0, MaxHP] | Only modified via `ApplyDamage()` and `ApplyRegen()`. Clamped on every write. |
| `MaxMP` | int | 100 | [0, 9999] | 0 for mobs (no skills in MVP). |
| `CurrentMP` | float | = MaxMP at spawn | [0.0, MaxMP] | Only modified via `ConsumeMana()` and `ApplyManaRegen()`. |
| `AttackPower` | int | 10 | [1, 9999] | Physical offensive stat. Source of BaseDamage computation. |
| `Defense` | int | 5 | [0, 9999] | Mitigates physical damage only. At 0, physical damage is unmitigated — not amplified. |
| `MagicDefense` | int | 0 | [0, 9999] | Mitigates all elemental damage (fire, cold, lightning, poison) with a single shared value. No per-element resistances in MVP. |
| `CritChance` | float | 0.05 | [0.0, 0.75] | Hard cap at 0.75 (75%). Prevents crit-stack build degeneracy. Applies to physical and elemental damage. |
| `CritMultiplier` | float | 1.5 | [1.0, 3.0] | 1.0 floor — a crit never deals less than a normal hit. Applies to both damage components. |
| `AttackRange` | float | 3.0 | [0.5, 10.0] | Ground-plane radius in Unity world units. Base 3.0 locked by Auto-Attack Combat GDD. |
| `AttackSpeedMultiplier` | float | 1.0 | [0.5, 2.0] | Clamp enforced at query time. Default 1.0 = 1.0s cycle duration. |
| `MovementSpeed` | float | 5.0 | [0.5, 20.0] | Unity units per second. Not read by combat systems. |

*Note on Default column*: The Default values above are the formula base constants (the flat intercept in F-3 through F-9), not the actual values at L1 spawn. At spawn the Leveling System applies F-3–F-9 with LevelTierMultiplier ×1.0 and all primary attributes at 10 (no level-up auto-alloc has fired yet). Actual L1 spawn values for all classes: MaxHP=400, MaxMP=220, AttackPower=30, Defense=20. Classes diverge from L2 onward as auto-alloc fires. See F-3 through F-9 for formula details.

**Container type**: `CharacterStats` is a plain C# class, not a `MonoBehaviour`. The Class System allocates a `CharacterStats` instance per entity at spawn and distributes it via dependency injection to all owner systems. This enables unit testing without a Unity scene and avoids `MonoBehaviour` lifecycle overhead.

**Modifier collection type**: Both the equipment modifier list and buff modifier list are fixed-capacity arrays, not `List<T>`. Default capacity: equipment layer 16 entries; buff layer 32 entries. If capacity is exceeded, `AddEquipmentModifier()` / `AddBuffModifier()` logs an error in dev builds and returns without adding. This is a programming or data-authoring error — the capacity should be raised in code to accommodate all expected concurrent modifiers.

**Modifier entry type**: Equipment and buff modifier entries must be implemented as `readonly struct`, not `class`. Class-allocated modifier entries create GC heap pressure at mobile scale (250+ entities × 48+ modifier slots × 18 stats on iOS IL2CPP). A struct entry with fields (e.g., `float FlatBonus`, `float PctBonus`, `ItemID Id` for equipment; `float FlatBonus`, `float PctBonus`, `int DurationTicks`, `BuffID Id` for buffs) allocated inside the fixed-capacity array costs zero additional heap allocation beyond the array itself.

**Player-only fields** (zero and ignored for mobs): `MaxMP`, `CurrentMP`, `Strength`, `Dexterity`, `Vitality`, `Intelligence`, `Experience`.

**Mob stat population**: Mob stats are authored directly in mob data tables and written to base stat slots at spawn via `SetBaseStat()`. Mobs have no equipment modifier layer at spawn — all mob base stat values are authored directly in mob data tables. The buff modifier layer is fully operational on mobs (see EC-21 and Status Effects CR-SE-12); `AddBuffModifier()` / `RemoveBuffModifier()` apply identically to mob entity slots as to player entity slots.

---

**Elemental Damage Model**

2. A weapon's elemental damage (fire, cold, lightning, poison) is a flat bonus stored as an attribute on the weapon item in the Item Database — **not a Character Stat**. When Damage Calculation resolves a hit from an elementally enchanted weapon, it reads the physical component from `GetEffectiveStat(AttackPower)` and the elemental component from the equipped weapon's item data, then applies them against `Defense` and `MagicDefense` respectively as two separate calculations in one hit. Character Stats has no per-element attack stats in MVP.

3. `MagicDefense` follows the same three-layer modifier stack as all other stats. Equipment bonuses (e.g., magic-resist gear) write to the equipment modifier layer; buff bonuses (e.g., a paladin aura) write to the buff modifier layer.

4. **Post-MVP note**: When the mage class is added, elemental attack power stats or a unified `ElementalAttackPower` driven by `Intelligence` will be added to this schema. The modifier stack model is already compatible with this extension — no schema restructuring required.

---

**Modifier Stack**

5. The effective value of any stat is computed from a three-layer stack. Character Stats owns this computation entirely; calling systems receive only the result.

```
EffectiveStat = clamp(
  (BaseStat + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff),
  StatMin,
  StatMax
)
```

| Symbol | Type | Description |
|--------|------|-------------|
| `BaseStat` | int or float | The level-up-derived base value; the only value persisted to disk |
| `ΣFlatEquip` | float | Sum of all flat bonuses from currently equipped items |
| `ΣFlatBuff` | float | Sum of all flat bonuses from active temporary buffs/debuffs |
| `ΣPctEquip` | float | Sum of all percentage bonuses from equipment (e.g., 0.10 = +10%) |
| `ΣPctBuff` | float | Sum of all percentage bonuses from active buffs/debuffs |
| `StatMin / StatMax` | stat-defined | Applied as the final clamp after all multiplication |

Percentage bonuses within a layer are **additive with each other** before becoming a multiplier. Two +10% equipment bonuses = +20% total at Step 4 (not +21%). The clamp is always the last operation.

**Worked example — AttackPower:**
Base = 100, +30 flat from weapon, +10 flat from warrior cry, +15% equipment, +20% party buff:
`(100 + 30 + 10) × 1.15 × 1.20 = 140 × 1.38 = 193.2`

6. `BaseDamage` is a **derived stat** computed at query time, not a stored value. `GetEffectiveStat(StatID.BaseDamage)` returns `EffectiveStat(AttackPower)` as an `int` (per the Rule 7 return type contract — AttackPower is int-schema). No separate BaseDamage field exists in the schema. The Auto-Attack Combat system widens this int to `float` at the call site for damage arithmetic. This resolves Auto-Attack Combat GDD Provisional Assumption A: BaseDamage is always a flat value, never a range.

---

**Query Interface**

7. Two query methods exist. No other read path is permitted.

| Method | Returns | Callers |
|--------|---------|---------|
| `GetEffectiveStat(EntityID, StatID)` | Fully computed EffectiveStat (all modifiers applied) | Auto-Attack Combat, Damage Calculation, Skill System, Combat UI/HUD |
| `GetBaseStat(EntityID, StatID)` | Raw base value only (no modifiers) | Leveling System, Equipment System, Character Persistence |

**EntityID type**: `EntityID` is a `readonly struct` wrapping `uint`. Do not use `string` as an entity identifier — string keys generate GC allocations on every stat query under Unity 6.3 IL2CPP on iOS. All internal collections key on `uint`.

**StatID type**: `StatID` is defined as `public enum StatID : byte`. *(Cross-doc correction applied from Networking Core GDD CR-NET-7.4: the original `uint` underlying type conflicts with the network serialization requirement that all game-message enums with <256 values use `enum : byte`. A 256-stat ceiling is more than sufficient for this game.)* Do not replace with a `string` or a generic struct parameter — string hashing generates GC allocations and generic struct constraints require explicit comparers under IL2CPP on iOS. If `StatID` is ever used as a dictionary key, provide an explicit `IEqualityComparer<StatID>` — IL2CPP's AOT compilation may fall back to the object-based `EqualityComparer`, boxing the enum value on every `TryGetValue` and `Add` call. Do not use LINQ (`.Where`, `.Select`, `.Any`, etc.) in any CharacterStats code path — `GetEffectiveStat` is called every Beat Event; LINQ enumerators box under IL2CPP and will cause GC pressure at mobile scale.

**BuffID type**: `BuffID` is defined as `public enum BuffID : uint` for the same IL2CPP reasons. All Acceptance Criteria use typed `BuffID` values — do not substitute `string` keys in implementation.

**Equipment modifier ID type**: Equipment modifier entries are keyed by `ItemID` — a `readonly struct` wrapping `uint`, analogous to `EntityID`. `ItemID` will be formally defined in the Item Database GDD. All Acceptance Criteria use typed `ItemID` values — do not substitute `string` keys in implementation. String keys generate GC allocations on every remove call under Unity 6.3 IL2CPP.

**Return type contract**: `GetEffectiveStat` returns an `int` for stats whose schema type is `int` (AttackPower, MaxHP, MaxMP, Defense, MagicDefense, Level, Experience, Strength, Dexterity, Vitality, Intelligence). The modifier stack computes a `float` intermediate, then applies `return Mathf.FloorToInt(effectiveFloat)` before returning. Do not use `(int)Math.Floor(effectiveFloat)` — `System.Math.Floor` returns `double`, causing a silent float→double widening that diverges from the Unity float domain; `Mathf.FloorToInt` takes `float` directly and is the correct Unity API. AC-25 uses `FloorToInt` as the reference for Leveling System formula implementation — be consistent. Stats whose schema type is `float` (CritChance, CritMultiplier, AttackRange, AttackSpeedMultiplier, MovementSpeed, CurrentHP, CurrentMP) are returned as `float` without floor rounding. `GetBaseStat` always returns the stored value in its declared type without modification.

8. Character Stats computes EffectiveStat on every call. Gameplay-critical systems (Auto-Attack Combat, Damage Calculation, Skill System) must query live via `GetEffectiveStat()` — no caching across game ticks. Display systems (HUD, stat sheet) must subscribe to `OnStatChanged(EntityID, StatID)` instead of polling every frame. `OnStatChanged` fires synchronously within the same call stack as any modifier change (add, remove, expire). HUD-local caches of the last notified value are permitted; direct per-frame polling is not. The event fires only when a modifier changes — not on every game tick.

**Re-entry rules**: Subscribers MAY call read-only queries (`GetEffectiveStat`, `GetBaseStat`) during the event handler. Subscribers MUST NOT call write operations (`SetBaseStat`, `AddBuffModifier`, `RemoveBuffModifier`, `AddEquipmentModifier`, `RemoveEquipmentModifier`, `ApplyDamage`, `ApplyRegen`, `ConsumeMana`) during event dispatch.

**Re-entrance guard**: The implementation enforces this with a `bool _isFiring` field. When `_isFiring` is true and a write operation is attempted, dev builds throw `InvalidOperationException`; IL2CPP release builds drop the write silently. This prevents modifier collection corruption during iteration under IL2CPP where bounds checks are stripped.

**Event delegate type**: `public delegate void StatChangedHandler(EntityID entityId, StatID statId)` — a named, non-generic delegate. Do not substitute `Action<EntityID, StatID>`; generic `Action` delegates with two value-type parameters can cause IL2CPP boxing on iOS.

**Thread safety**: `OnStatChanged` fires on the Unity main thread only. Modifier expiry (triggered by the tick system via `RemoveBuffModifier`) must be called from the main thread. Do not call any Character Stats write operation from a Job, Task, or background thread.

**Cross-system event ordering note**: The Equipment System's item-swap pattern (remove old modifier, add new modifier) fires two separate `OnStatChanged` events for the same stat — the first when the old modifier is removed (showing a lower intermediate value), the second when the new modifier is added. A HUD subscriber reading `GetEffectiveStat(AttackPower)` in the first handler will see a transient value that is lower than both old and new effective stats. The transaction API does not currently cover Equipment System swaps. Mitigation options — extending the transaction API to Equipment System or requiring HUD subscribers to debounce within-frame reads — are deferred to the Equipment System GDD as a required interface contract decision.

**Subscriber collection**: The subscriber list for `OnStatChanged` is a fixed-capacity array iterated by index (not `List<T>` with `foreach`). This avoids heap enumerator allocation per event invocation under IL2CPP — measurable GC pressure at combat scale on iOS. Capacity: 16 subscribers per stat. If all slots are filled and a new subscriber attempts to register, `Subscribe()` logs an error in dev builds and returns without registering. This is a programming error — increase capacity in code, not silently at runtime.

**OnEntityDied delegate type**: `public delegate void EntityDiedHandler(EntityID entityId)` — a named, non-generic delegate. Subscribers receive only the entity ID; CurrentHP is already 0.0 when the event fires. Obeys the same re-entry rules as `OnStatChanged`: read-only queries permitted inside the handler; write operations prohibited.

---

**Write Ownership**

9. Every write path has exactly one owner and one mechanism. No system may read a stat and write a modified version back to the base slot; all modification uses the modifier layer API.

| Stat / Layer | Owner | Mechanism |
|---|---|---|
| Primary attributes (STR, DEX, VIT, INT) | Leveling System | `SetBaseStat(EntityID, StatID, value)` |
| Derived stats (MaxHP, AttackPower, Defense, MagicDefense, etc.) base values | Leveling System (growth), Class System (spawn) | `SetBaseStat()` |
| `Level` | Leveling System only | `SetBaseStat()` — no other system may write Level |
| `Experience` | Damage Calculation (on-kill callback) | `AddExperience(EntityID, amount)` — notifies Leveling System if threshold crossed |
| `CurrentHP` | Damage Calculation (damage intake), Status Effects (regen) | `ApplyDamage(EntityID, amount)`, `ApplyRegen(EntityID, amount)` — never via modifier layer |
| `CurrentMP` | Skill System (cost on cast), Status Effects (regen) | `ConsumeMana(EntityID, amount)`, `ApplyManaRegen(EntityID, amount)` — never via modifier layer |
| Equipment modifier layer | Equipment System only | `AddEquipmentModifier(EntityID, StatID, flatBonus, pctBonus, ItemID)` / `RemoveEquipmentModifier(EntityID, StatID, ItemID)` |
| Buff modifier layer | Status Effects system only | `AddBuffModifier(EntityID, StatID, flatBonus, pctBonus, durationTicks, buffID)` / `RemoveBuffModifier(EntityID, StatID, buffID)` |

`CurrentHP` and `CurrentMP` are current resource values, not base stats. They are never modified through the modifier stack. Their floors (0.0) and ceilings (MaxHP / MaxMP) are enforced on every write.

**Critical invariant**: No system reads a stat value and then writes a modified version back to the base stat slot. All modification is done through the modifier layer API. Base stats only change via `SetBaseStat()` (level-up, spawn). Removing a buff always restores the correct effective value without requiring any base-stat undo.

---

### States and Transitions

Character Stats has no runtime states. It is a passive data store. The only lifecycle events are:

| Event | Action |
|-------|--------|
| Entity spawns | `SetBaseStat()` called for all base values; modifier layers initialized empty; CurrentHP = MaxHP, CurrentMP = MaxMP |
| Equipment equipped / unequipped | Equipment System calls `AddEquipmentModifier` / `RemoveEquipmentModifier`; `OnStatChanged` fires for affected stats |
| Buff applied / expired | Status Effects calls `AddBuffModifier` / `RemoveBuffModifier`; `OnStatChanged` fires for affected stats |
| Level-up | Leveling System calls `SetBaseStat()` for primary attributes and derived base values; `OnStatChanged` fires for each updated stat |
| Any modifier changes | `OnStatChanged(EntityID, StatID)` fires synchronously — HUD subscribers update their local cache |
| Entity serialized (save) | Character Persistence reads all base stats via `GetBaseStat()` and the modifier lists |
| Entity deserialized (load) | Character Persistence restores base stats via `SetBaseStat()` and re-populates modifier lists |

---

### Interactions with Other Systems

| System | Direction | Data | Notes |
|--------|-----------|------|-------|
| Auto-Attack Combat | ← Stats | `BaseDamage` (int from `GetEffectiveStat`, widened to float at call site), `AttackRange` (float), `AttackSpeedMultiplier` (float) via `GetEffectiveStat()` | Queried live per Beat Event. Not cached. |
| Damage Calculation | ← Stats | `AttackPower`, `Defense`, `MagicDefense`, `CritChance`, `CritMultiplier` via `GetEffectiveStat()` | Per damage resolution event. `MagicDefense` queried when elemental damage component is present. |
| Skill System | ← Stats | `CurrentMP`, `MaxMP`, skill-specific modifiers via `GetEffectiveStat()` | Reads MP before cast; writes via `ConsumeMana()`. |
| Leveling System | Stats → | Writes primary attributes and derived bases via `SetBaseStat()` on level-up | Reads `GetBaseStat(Level)` to determine current level. |
| Equipment System | Stats → | Writes equipment modifier layer via `AddEquipmentModifier()` / `RemoveEquipmentModifier()` | Reads `GetBaseStat()` to know what it is modifying on top of. Elemental weapon damage is an item attribute — not written to Character Stats. |
| Status Effects / Buffs | Stats → | Writes buff modifier layer via `AddBuffModifier()` / `RemoveBuffModifier()` | Owns buff expiry — calls Remove when duration elapses. |
| Class System | Stats → | Writes base stats at spawn via `SetBaseStat()` for class-specific values | Sets AttackRange, AttackSpeedMultiplier, base primary attribute allocations per class. |
| Character Persistence | ← Stats | Reads all base stats via `GetBaseStat()` and modifier lists for serialization | Restores via `SetBaseStat()` and modifier re-registration on load. |
| Combat UI / HUD | ← Stats | `CurrentHP`, `MaxHP`, `CurrentMP`, `MaxMP`, `Level` via `GetEffectiveStat()` | Display only. No writes. |
| Item Database | ← (indirect) | Elemental weapon damage values (flat fire/cold/lightning/poison bonus per weapon) | Damage Calculation reads both Character Stats and item data when resolving elemental hits. Character Stats has no elemental attack stats in MVP. |

## Formulas

All formulas compute base stats from primary attributes. The modifier stack (F-1) then applies on top of these base values. All base-stat formulas are evaluated at level-up by the Leveling System via `SetBaseStat()`.

---

**F-1 — Effective Stat (Modifier Stack)**

```
EffectiveStat = clamp(
  (BaseStat + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff),
  StatMin,
  StatMax
)
```

| Variable | Type | Description |
|----------|------|-------------|
| `BaseStat` | int or float | Level-up-derived value; the only value persisted to disk |
| `ΣFlatEquip` | float | Sum of all flat bonuses from currently equipped items |
| `ΣFlatBuff` | float | Sum of all flat bonuses from active buffs/debuffs |
| `ΣPctEquip` | float | Sum of all percentage bonuses from equipment (e.g., 0.10 = +10%) |
| `ΣPctBuff` | float | Sum of all percentage bonuses from active buffs/debuffs |
| `StatMin / StatMax` | stat-defined | Applied as the final clamp after all multiplication |

Percentage bonuses within a layer are additive with each other before becoming a multiplier. Two +10% equipment bonuses = +20% total at that step (not +21%). The clamp is always the last operation.

*Example A — AttackPower with buffs:* Base=100, +30 flat equip, +10 flat buff, +15% equip, +20% buff → `(100 + 30 + 10) × 1.15 × 1.20 = 193.2` → clamped to [1, 9999] → **193**

*Example B — CritChance hard-capped:* Base=0.05, +0.40 flat equip, +0.35 flat buff → `(0.05 + 0.40 + 0.35) × 1.0 × 1.0 = 0.80` → clamped to [0.0, 0.75] → **0.75**

---

**F-2 — BaseDamage (Derived)**

```
BaseDamage = EffectiveStat(AttackPower)
```

BaseDamage is not a stored stat. `GetEffectiveStat(StatID.BaseDamage)` returns `EffectiveStat(AttackPower)` as an `int` — the fully modifier-inclusive AttackPower value, floor-truncated per the Rule 7 return type contract. The Auto-Attack Combat system widens this `int` to `float` at the call site for damage arithmetic. Resolves Auto-Attack Combat GDD Provisional Assumption A: BaseDamage is always a flat value, never a range.

---

**F-2a — LevelTierMultiplier**

A step function applied by the Leveling System to all derived stat base calculations (F-3 through F-9) at every level-up. At milestone levels (20, 40, 60), the tier advances and derived stats receive a full recalculation with the new multiplier — creating discrete power spikes that mark progression milestones.

| Level Range | LevelTierMultiplier |
|-------------|---------------------|
| 1 – 19 | 1.0 |
| 20 – 39 | 1.2 |
| 40 – 59 | 1.5 |
| 60 | 2.0 |

The multiplier is applied inside the Leveling System when computing the `SetBaseStat()` value. Character Stats stores the final computed value — it does not store or evaluate LevelTierMultiplier directly. The HUD receives the updated base value via `OnStatChanged` when the Leveling System writes it.

CritChance (F-8) and AttackSpeedMultiplier (F-9) **do** receive the LevelTierMultiplier, creating perceptible Rhythm Mastery milestones at L20, L40, and L60. Both F-8 and F-9 apply the multiplier to the DEX-derived component only — not to the base constant (0.05 for CritChance, 1.0 for ASM). This preserves a stable base floor for all characters while amplifying the DEX investment payoff at each tier. See F-8 and F-9 formula notes for details.

**Tier transition recompute**: At a milestone level-up (L20, L40, L60), the Leveling System reads the current total primary attribute values via `GetBaseStat()` — not an incremental delta — and recomputes F-3 through F-9 from scratch with the new multiplier. This ensures the full accumulated stat total is reflected in the power spike, regardless of how many points were earned across which levels.

**Spawn path ownership (L1)**: At entity spawn, the Class System delegates to the Leveling System's stat-initialization routine, which computes F-3 through F-9 using LevelTierMultiplier ×1.0 and writes the results via `SetBaseStat()`. L1 spawn is treated as a tier ×1.0 level-up event for initialization purposes.

**Persistence load path**: Character Persistence stores the final computed base stat integers to disk (e.g., MaxHP=2,940 for a L40 Warrior Tank). On load, Character Persistence restores these integers directly via `SetBaseStat()` — it does not re-evaluate F-3 through F-9. The stored values already reflect the correct LevelTierMultiplier from the last level-up event. The tier multiplier is not re-applied on load.

---

**F-3 — MaxHP**

```
MaxHP = floor((200 + VIT × 20) × LevelTierMultiplier)
```

| Variable | Value | Notes |
|----------|-------|-------|
| HP base | 200 | Spawn floor before any VIT contribution |
| HPPerVIT | 20 | Tuning constant — see Tuning Knobs |
| VIT | primary attribute | Only player stat; mob MaxHP set directly via SetBaseStat() |
| LevelTierMultiplier | step function | See F-2a; ×1.0/×1.2/×1.5/×2.0 at L1-19/L20-39/L40-59/L60 |

*Level 60 targets (base stats only, no equipment, tier ×2.0):*
- Warrior Tank (VIT=128, L60): MaxHP = floor((200 + 128 × 20) × 2.0) = floor(2,760 × 2.0) = **5,520**
- Healer Support (VIT=69, L60): MaxHP = floor((200 + 69 × 20) × 2.0) = floor(1,580 × 2.0) = **3,160**

*Design note:* With the revised Healer class template (+1 VIT auto/level vs. previous +2), the role-appropriate INT-focused Healer reaches MaxHP=3,160 at L60 — 43% less than the Warrior Tank at 5,520. The Warrior Tank holds a clear survivability advantage in their intended role. A Healer who maximizes VIT (Healer Survivability build: 3 VIT/level total) reaches MaxHP=7,880 at L60, but this comes at the cost of MaxMP dropping from 6,104 (INT build) to 3,272 — severely limiting healing capacity. The Healer Survivability build is a high-HP, low-sustainability hybrid that trades the Healer's primary role (healing) for personal durability. A party that brings this Healer instead of a Warrior Tank still needs a healer — the build creates an interesting role-dilution decision, not a dominant strategy.

---

**F-4 — MaxMP**

```
MaxMP = floor((100 + INT × 12) × LevelTierMultiplier)
```

| Variable | Value | Notes |
|----------|-------|-------|
| MP base | 100 | Spawn floor before any INT contribution |
| MPPerINT | 12 | Tuning constant — see Tuning Knobs |
| INT | primary attribute | Player-only; mobs have MaxMP = 0 (no skills in MVP) |
| LevelTierMultiplier | step function | See F-2a |

*Level 60 targets (base stats only, tier ×2.0):*
- Warrior (INT=10): MaxMP = floor((100 + 10 × 12) × 2.0) = floor(220 × 2.0) = **440**
- Healer Support (INT=246, support build): MaxMP = floor((100 + 246 × 12) × 2.0) = floor(3,052 × 2.0) = **6,104**

*Schema ceiling note*: The MaxMP schema max is 9,999. The formula exceeds this at INT ≥ 409 (`floor((100 + 409×12) × 2.0) = 10,016` → clamped to 9,999). Normal Healer support builds peak around INT=246 (MaxMP=6,104), well below the ceiling. Free-point INT stacking above INT=409 yields no additional MaxMP. This clamp is intentional — the schema range [0, 9,999] prevents economy-breaking mana pools on extreme INT-stack builds.

*Clamp ownership*: The **Leveling System** is responsible for clamping the F-4 result to `StatMax(MaxMP) = 9,999` before calling `SetBaseStat(MaxMP, ...)`. Character Stats' `SetBaseStat()` does not validate against StatMax — the Leveling System evaluates F-4, clamps the result to `min(rawResult, 9999)`, then writes the clamped integer. This ensures `GetBaseStat(MaxMP)` always returns an in-range value, and Character Persistence serializes only valid integers. See AC-34 for the test that verifies this boundary.

---

**F-5 — AttackPower**

```
AttackPower = floor((10 + STR × 2) × LevelTierMultiplier)
```

| Variable | Value |
|----------|-------|
| AP base | 10 |
| APPerSTR | 2 |
| LevelTierMultiplier | step function — see F-2a |

*Level 60 targets (base stats only, tier ×2.0):*
- Warrior DPS (STR=187, L60): AttackPower = floor((10 + 187 × 2) × 2.0) = floor(384 × 2.0) = **768**
- Healer Support (STR=10, L60): AttackPower = floor((10 + 10 × 2) × 2.0) = floor(30 × 2.0) = **60**

---

**F-6 — Defense**

```
Defense = floor((5 + VIT × 1.5) × LevelTierMultiplier)
```

| Variable | Value |
|----------|-------|
| Def base | 5 |
| DefPerVIT | 1.5 |
| LevelTierMultiplier | step function — see F-2a |

*Level 60 targets (base stats only, tier ×2.0):*
- Warrior Tank (VIT=128, L60): Defense = floor((5 + 128 × 1.5) × 2.0) = floor(197 × 2.0) = **394**
- Healer Support (VIT=69, L60): Defense = floor((5 + 69 × 1.5) × 2.0) = floor(108.5 × 2.0) = **217**

---

**F-7 — MagicDefense**

```
MagicDefense = floor(INT × 0.4 × LevelTierMultiplier)
```

| Variable | Value |
|----------|-------|
| MD base | 0 |
| MDPerINT | 0.4 |
| LevelTierMultiplier | step function — see F-2a |

Player INT schema range is [1, 999]; INT=0 is not a valid player state. Mobs set MagicDefense directly via `SetBaseStat()` — F-7 is never evaluated for mob entities. A mob with no MagicDefense authored in its data table will have MagicDefense=0 (all elemental damage unmitigated).

*Design note — Healer INT party utility*: In Character Stats, INT drives MaxMP (F-4) and MagicDefense (F-7) — both are self-facing stats. Healer INT has no direct party-facing expression at this layer. Party utility from INT investment (HealPower — a modifier to healing skill output) will be defined in the Skill System GDD. See OQ-7.

*Level 60 targets (base stats only, tier ×2.0):*
- Healer Support (INT=246, L60): MagicDefense = floor(246 × 0.4 × 2.0) = floor(196.8) = **196**
- Warrior (INT=10, L60): MagicDefense = floor(10 × 0.4 × 2.0) = floor(8.0) = **8**

---

**F-8 — CritChance**

```
CritChance = 0.05 + (DEX × 0.0015 × LevelTierMultiplier)
```

| Variable | Value |
|----------|-------|
| Crit base | 0.05 (5%) — constant, not multiplied by tier |
| CritPerDEX | 0.0015 |
| LevelTierMultiplier | step function — see F-2a; ×1.0/×1.2/×1.5/×2.0 at L1-19/L20-39/L40-59/L60 |
| Hard cap | 0.75 (75%) — applied by F-1 clamp |

*Note on formula structure*: LevelTierMultiplier is applied to the DEX-derived component only, not to the 0.05 base. This mirrors F-9's structure exactly: the base floor is stable across tiers; tier milestones amplify DEX investment, not the baseline. Multiplying the full expression `(0.05 + DEX×0.0015) × LevelTierMultiplier` would inflate the 5% base at every tier (reaching 10% at L60 for zero-DEX characters), making tier the primary crit driver rather than DEX investment.

*Level snapshots (Warrior auto-alloc only, DEX goes from 10 at L1 to 69 at L60):*
- L1 (DEX=10, ×1.0): CritChance = 0.05 + (10×0.0015×1.0) = 0.05 + 0.015 = **0.065 (6.5%)**
- L20 (DEX=29, ×1.2): CritChance = 0.05 + (29×0.0015×1.2) = 0.05 + 0.0522 = **0.102 (10.2%)**
- L40 (DEX=49, ×1.5): CritChance = 0.05 + (49×0.0015×1.5) = 0.05 + 0.11025 = **0.160 (16.0%)**
- L60 (DEX=69, ×2.0): CritChance = 0.05 + (69×0.0015×2.0) = 0.05 + 0.207 = **0.257 (25.7%)**

The tier milestones (6.5% → 10.2% → 16.0% → 25.7%) create felt Rhythm Mastery power spikes at L20, L40, and L60 alongside the MaxHP/AP tier transitions. A Warrior who directs all 1 free point to DEX reaches DEX=128: CritChance = 0.05 + (128×0.0015×2.0) = 0.05 + 0.384 = **0.434 (43.4%)**. Both values are below the 0.75 hard cap — the cap remains equipment-only reachable in normal play.

---

**F-9 — AttackSpeedMultiplier**

```
AttackSpeedMultiplier = 1.0 + DEX × 0.003 × LevelTierMultiplier
```

| Variable | Value |
|----------|-------|
| ASM base | 1.0 (constant — not multiplied by tier) |
| ASMPerDEX | 0.003 |
| LevelTierMultiplier | step function — see F-2a |
| Clamp | [0.5, 2.0] — applied by F-1 |

*Note on formula structure:* LevelTierMultiplier is applied to the DEX-derived component only, not to the base 1.0. Multiplying the full expression (1.0 + DEX×0.003) × LevelTierMultiplier would cause all characters to reach or exceed the 2.0 cap at L60 regardless of DEX investment (base 1.0 × 2.0 = 2.0 already saturates the cap). Applying the multiplier to the DEX term only preserves the base cadence and amplifies the DEX investment payoff at each tier.

*Level 60 targets (base stats only, Warrior DEX auto-alloc only: DEX=69):*
- L1 (×1.0): ASM = 1.0 + 10×0.003×1.0 = **1.030** — cycle 0.971s
- L20 (×1.2): ASM = 1.0 + 29×0.003×1.2 = 1.0 + 0.1044 = **1.104** — cycle 0.906s
- L40 (×1.5): ASM = 1.0 + 49×0.003×1.5 = 1.0 + 0.2205 = **1.221** — cycle 0.819s
- L60 (×2.0): ASM = 1.0 + 69×0.003×2.0 = 1.0 + 0.414 = **1.414** — cycle 0.707s

The progression (1.030 → 1.104 → 1.221 → 1.414) creates perceptible Rhythm Mastery milestones. The L60 auto-alloc Warrior attacks ~29% faster than a freshly spawned character. Maximum base ASM (Warrior, all free points to DEX, DEX=128): 1.0 + 128×0.003×2.0 = 1.768 — below the 2.0 cap. The hard cap requires equipment stacking to reach even for DEX-focused builds.

---

**F-10 — Level-Up Attribute Allocation**

Each level-up grants 5 total attribute points:

```
Warrior: 4 auto-allocated (class template) + 1 free (player choice)
Healer:  3 auto-allocated (class template) + 2 free (player choice)
```

**Auto-allocation templates:**

| Class | Auto Points | Free Points | Total/Level |
|-------|-------------|-------------|-------------|
| Warrior | +2 STR, +1 VIT, +1 DEX | +1 any | 5 |
| Healer | +1 VIT, +2 INT | +2 any | 5 |

Warrior DEX auto-alloc ensures Rhythm Mastery (CritChance, AttackSpeedMultiplier) delivers perceptible milestones at L20, L40, and L60 alongside the MaxHP/AP power spikes. Warrior reaches DEX=69 at L60 from auto-alloc alone; CritChance milestones: 6.5% → 10.2% → 16.0% → 25.7%.

**Warrior role designation**: "Warrior Tank" and "Warrior DPS" are player labeling conventions, not mechanical distinctions in this GDD. Warrior is a single class with one auto-alloc template. A player who directs the free point to STR becomes a de facto DPS build; directing it to VIT becomes a de facto Tank build. The stat-build consequences are shown in the Level 60 snapshots below. Skill-level role differentiation (active skills, passive bonuses) is defined in the Class System GDD.

Healer INT auto-alloc ensures the support role scales MaxMP and MagicDefense significantly across 59 level-ups. The Healer template was revised from +2 VIT/+1 INT auto to +1 VIT/+2 INT auto — this ensures the role-appropriate Healer Support (INT build) reaches MaxHP=3,160 at L60 vs. Warrior Tank at 5,520, giving the Warrior a clear survivability advantage. Post-MVP Archer and Assassin classes will auto-alloc 2–3 DEX/level — DEX remains their primary stat identity.

**Healer free-point policy**: Healer free-point investment is intentionally unrestricted — directing both free points to VIT is a valid, if off-meta, choice. A Healer who maximizes VIT reaches MaxHP=7,880 at L60 (higher than Warrior Tank) at the cost of MaxMP dropping from 6,104 to 3,272. This trade-off is intentional: the VIT-heavy Healer sacrifices healing capacity for personal durability, creating an interesting off-meta role-dilution decision rather than a dominant strategy. No mechanical restriction on free-point target is imposed at this layer. See F-3 Healer Survivability design note for full build snapshot and balance rationale.

Starting stats (all classes, level 1): STR=10, DEX=10, VIT=10, INT=10. Level cap: 60. Total level-ups: 59. Total points allocated: 59 × 5 = 295.

**Respec policy:** Players may respec all free stat points by consuming a rare in-game item (item definition TBD by Economy Designer). Auto-allocated points (+2 STR, +1 VIT, +1 DEX for Warrior; +1 VIT, +2 INT for Healer) are permanent and cannot be respecced. The Leveling System implements respec via the following sequence:
1. Call `Character Stats.BeginStatTransaction()` — `OnStatChanged` events are deferred until the transaction closes.
2. Zero all free-point contributions to primary attributes (STR, DEX, VIT, INT) via `SetBaseStat()` — leaving only auto-alloc totals.
3. Re-add the player's chosen free-point allocation via `SetBaseStat()`.
4. Re-evaluate F-3 through F-9 from the new total primary attribute values using the current `LevelTierMultiplier` (looked up from `GetBaseStat(StatID.Level)`), and write each derived stat via `SetBaseStat()`.
5. Call `Character Stats.EndStatTransaction()` — all deferred `OnStatChanged` events fire once per modified stat.

The `BeginStatTransaction()` / `EndStatTransaction()` API is defined on Character Stats. Permitted callers: the Class System (character initialization at L1 — see Class System SA-2), the Leveling System (respec), and the Status Effects system (tick-path buff flush — see CR-SE-17 in status-effects.md). No other system may open a transaction. Steps 2–4 are atomic with respect to `OnStatChanged`; subscribers see one notification per stat after the full respec completes, not intermediate states.

**Transaction constraints**: `BeginStatTransaction()` throws `InvalidOperationException` if called while a transaction is already open — transactions are not nestable. During a transaction, deferred events are deduplicated by `StatID`: if the same stat is written multiple times within one transaction, `OnStatChanged` fires once per affected stat at `EndStatTransaction()`, not once per write. The dedup set is implemented as a fixed-size `StatID[]` array with linear scan (not `HashSet<StatID>`) — linear scan is faster for n≤18 stats and avoids IL2CPP boxing risks from `HashSet` without an explicit comparer. `EndStatTransaction()` throws `InvalidOperationException` if called without a prior `BeginStatTransaction()`. If an exception occurs mid-transaction (between Begin and End), the caller must call `RollbackStatTransaction()` to discard the deferred queue and reset transaction state — leaving a transaction open permanently will block all future `OnStatChanged` events for that entity. `RollbackStatTransaction()` is safe to call when no transaction is open — it is a no-op in that case, allowing correct `try/catch` patterns where the `catch` block unconditionally calls Rollback without first checking transaction state.

**Level 60 stat snapshots (representative builds — free points directed to one stat):**

All derived stats include LevelTierMultiplier (×1.0 at L1, ×1.2 at L20, ×1.5 at L40, ×2.0 at L60).

*Warrior Tank (free → VIT): +2 STR, +2 VIT, +1 DEX per level*

| Level | STR | DEX | VIT | INT | MaxHP | Defense | AP | CritChance | ASM |
|-------|-----|-----|-----|-----|-------|---------|-----|-----------|-----|
| 1 | 10 | 10 | 10 | 10 | 400 | 20 | 30 | 6.5% | 1.030 |
| 20 | 48 | 29 | 48 | 10 | 1,392 | 92 | 127 | 10.2% | 1.104 |
| 40 | 88 | 49 | 88 | 10 | 2,940 | 205 | 279 | 16.0% | 1.221 |
| 60 | 128 | 69 | 128 | 10 | 5,520 | 394 | 532 | 25.7% | 1.414 |

*Warrior DPS (free → STR): +3 STR, +1 VIT, +1 DEX per level*

| Level | STR | DEX | VIT | INT | MaxHP | AP | CritChance | ASM |
|-------|-----|-----|-----|-----|-------|-----|-----------|-----|
| 1 | 10 | 10 | 10 | 10 | 400 | 30 | 6.5% | 1.030 |
| 20 | 67 | 29 | 29 | 10 | 936 | 172 | 10.2% | 1.104 |
| 40 | 127 | 49 | 49 | 10 | 1,770 | 396 | 16.0% | 1.221 |
| 60 | 187 | 69 | 69 | 10 | 3,160 | 768 | 25.7% | 1.414 |

*Healer Support (free → INT): +1 VIT, +4 INT per level* *(revised template: +1 VIT, +2 INT auto + 2 free → INT)*

| Level | STR | DEX | VIT | INT | MaxHP | MaxMP | MagDef |
|-------|-----|-----|-----|-----|-------|-------|--------|
| 1 | 10 | 10 | 10 | 10 | 400 | 220 | 4 |
| 20 | 10 | 10 | 29 | 86 | 936 | 1,358 | 41 |
| 40 | 10 | 10 | 49 | 166 | 1,770 | 3,138 | 99 |
| 60 | 10 | 10 | 69 | 246 | 3,160 | 6,104 | 196 |

*Healer Survivability (free → VIT): +3 VIT, +2 INT per level* *(revised template: +1 VIT, +2 INT auto + 2 free → VIT)*

| Level | STR | DEX | VIT | INT | MaxHP | MaxMP | Defense |
|-------|-----|-----|-----|-----|-------|-------|---------|
| 1 | 10 | 10 | 10 | 10 | 400 | 220 | 20 |
| 20 | 10 | 10 | 67 | 48 | 1,848 | 811 | 126 |
| 40 | 10 | 10 | 127 | 88 | 4,110 | 1,734 | 293 |
| 60 | 10 | 10 | 187 | 128 | 7,880 | 3,272 | 571 |

*Design note (Healer Survivability):* This build trades healing capacity (MaxMP 3,272 vs. 6,104 for Healer Support) for personal durability (MaxHP 7,880). At L60 a Healer Survivability build out-tanks the Warrior Tank on raw HP, but is a poor healer due to limited mana. This is an intentional off-meta design space — parties that use a VIT-Healer as a tank still lack a functional healer, and the Warrior Tank brings AP=532 vs. Healer VIT-build AP=10, contributing far more offensive output. Balance tuning of the HP ratio between Healer Survivability and Warrior Tank should happen during playtest.

---

**Tuning constants summary:**

| Constant | Value | Formula | Design target |
|----------|-------|---------|---------------|
| HPPerVIT | 20 | F-3 | Warrior Tank L60: 5,520 HP (tier ×2.0, VIT=128) |
| MPPerINT | 12 | F-4 | Healer Support L60: 6,104 MP (tier ×2.0, INT=246) |
| APPerSTR | 2 | F-5 | Warrior DPS L60: 768 AP (tier ×2.0, STR=187) |
| DefPerVIT | 1.5 | F-6 | Warrior Tank L60: 394 Defense (tier ×2.0, VIT=128) |
| MDPerINT | 0.4 | F-7 | Healer Support L60: 196 MagicDefense (tier ×2.0, INT=246) |
| CritPerDEX | 0.0015 | F-8 | Warrior auto-alloc only: 25.7% crit at L60 (DEX=69, tier ×2.0 on DEX component) |
| ASMPerDEX | 0.003 | F-9 | Warrior auto-alloc only: 1.414 ASM at L60 (DEX=69, tier ×2.0 on DEX component) |
| LevelTierMultiplier | 1.0/1.2/1.5/2.0 | F-2a | Power spikes at L20 (+20%), L40 (+25%), L60 (+33%) — now applied to F-8 and F-9 |

## Edge Cases

**EC-01 (Modifier Stack): Negative Flat Debuff Drives Pre-Clamp Value Below StatMin**
Situation: A debuff applies −200 flat to AttackPower on a player whose BaseStat is 50 and equipment adds +30 flat. Pre-clamp result: `(50 + 30 − 200) × 1.0 × 1.0 = −120`.
Rule: F-1 clamp always runs last. The result is clamped to StatMin=1. `GetEffectiveStat(AttackPower)` returns **1**, not 0 or a negative value. The debuff is fully absorbed by the clamp; no underflow is possible.

**EC-02 (Modifier Stack): Negative Percentage Debuff on Buff Layer**
Situation: A slow debuff applies −50% to MovementSpeed (ΣPctBuff = −0.50) while equipment applies +10% (ΣPctEquip = +0.10). Base = 5.0.
Rule: `(5.0) × 1.10 × 0.50 = 2.75`. Clamped to [0.5, 20.0] → **2.75**. Percentage layers are independent multipliers; the debuff layer does not override the equipment layer.

**EC-03 (Modifier Stack): Compounded Debuffs Drive Percentage Multiplier Negative**
Situation: Multiple stacked debuffs sum to ΣPctBuff = −1.20. Pre-clamp value is negative.
Rule: F-1 clamp returns **StatMin** (e.g., 0.5 for MovementSpeed). No negative effective stat is ever returned. StatMin is the hard floor regardless of debuff magnitude.

**EC-04 (Modifier Stack): Percentage Bonuses Are Additive Within a Layer, Not Compounding**
Situation: Two equipment items each grant +15% AttackPower (ΣPctEquip = 0.30, not 0.3225).
Rule: Bonuses within the same layer are summed into a single additive multiplier before applying. `(BaseStat + ΣFlat) × 1.30`. Intra-layer compounding does not occur; inter-layer multiplication does.

**EC-05 (CurrentHP/MP): MaxHP Decreases Mid-Combat While CurrentHP Exceeds New Max**
Situation: Player has CurrentHP = 2,500. Equipment unequip reduces MaxHP from 3,000 to 2,000.
Rule: When `RemoveEquipmentModifier()` affects MaxHP, Character Stats immediately clamps `CurrentHP = min(CurrentHP, newMaxHP)` before returning — not deferred to the next damage or regen write. **CurrentHP becomes 2,000.0 — the player is not killed.** `GetEffectiveStat(CurrentHP)` never returns a value above `GetEffectiveStat(MaxHP)` within the same frame.

**EC-06 (CurrentHP/MP): MaxHP Increases Mid-Combat (Level-Up)**
Situation: A player levels up mid-combat. `SetBaseStat(MaxHP)` is called with a higher value.
Rule: MaxHP increases; CurrentHP remains unchanged. The player receives no free healing from leveling up. Only `ApplyRegen()` may increase CurrentHP.

**EC-07 (CurrentHP/MP): Simultaneous Damage and Regen in the Same Tick**
Situation: A regen tick and a damage event both arrive in the same frame targeting the same entity's CurrentHP.
Rule: Character Stats applies each write atomically in call-arrival order. There is no simultaneous-write merge. The clamp [0.0, MaxHP] is enforced independently on each write. Frame ordering is the responsibility of the game loop, not Character Stats.

**EC-08 (CurrentHP/MP): Death Boundary — CurrentHP Reaches Exactly 0.0**
Situation: `ApplyDamage(amount)` is called with amount ≥ CurrentHP.
Rule: CurrentHP is clamped to 0.0. Character Stats emits `OnEntityDied`. It does NOT remove the entity, handle respawn, or change any other stat. All subsequent reads of CurrentHP return 0.0 until a resurrection mechanism writes CurrentHP above 0.0.

**EC-09 (CurrentHP/MP): Fractional HP Accumulation from Float Regen**
Situation: A regen effect applies 0.3 HP per tick.
Rule: `ApplyRegen()` adds the float amount directly to the `CurrentHP` float field without per-tick rounding. Character Stats stores CurrentHP as a float and preserves sub-integer values. The HUD displays `Mathf.FloorToInt(CurrentHP)` for presentation; the internal float is authoritative.

**EC-10 (CurrentHP/MP): Mana Spend Attempt When CurrentMP Is Insufficient**
Situation: The Skill System calls `ConsumeMana(EntityID, cost)` but `CurrentMP < cost`.
Rule: `ConsumeMana()` returns **false** and makes no write. CurrentMP is unchanged. The Skill System is responsible for checking the return value and blocking the cast. CurrentMP never goes below 0.0.

**EC-11 (Buff/Debuff): Buff Removal Restores Correct Effective Value Without Base-Stat Undo**
Situation: A +50 flat AttackPower buff is active, raising EffectiveStat from 150 to 200. The buff is removed.
Rule: `RemoveBuffModifier()` removes the entry from the list. On the next `GetEffectiveStat()`, F-1 recomputes from scratch using remaining modifiers. No "undo delta" is stored — the stack recomputes fresh every query. EffectiveStat returns **150**.

**EC-12 (Buff/Debuff): Expired Buff vs. Manually Removed Buff**
Situation: A buff expires by duration OR is dispelled by a player ability before expiry.
Rule: Both paths call the same `RemoveBuffModifier(EntityID, StatID, buffID)`. Character Stats does not distinguish between the two removal triggers — it only removes by ID. Status Effects owns the timer and the dispel path; Character Stats only processes the remove call.

**EC-13 (Buff/Debuff): Same Buff Applied Twice to the Same Entity**
Situation: `AddBuffModifier(..., buffID="warrior_cry")` is called when `warrior_cry` is already active on the entity.
Rule: `AddBuffModifier()` finds the existing entry by ID and **refreshes the duration**, overwriting flat/pct values with the new call's values. No second independent entry is created. The same named buff source never double-stacks.

**EC-14 (Buff/Debuff): Multiple Different Buffs on the Same Stat**
Situation: `warrior_cry` (+30 flat AP), `weapon_polish` (+15 flat AP), and `party_aura` (+20% AP) are all active simultaneously.
Rule: ΣFlatBuff = 45, ΣPctBuff = 0.20. With Base=100 and no equipment: `(100 + 45) × 1.0 × 1.20 = 174.0`. Removing any one buff removes only its contribution; the other two remain.

**EC-15 (Buff/Debuff): Buff Modifier Registered for a Non-Stack-Eligible Stat**
Situation: A buff attempts to target `StatID.Experience`, `StatID.CurrentHP`, `StatID.CurrentMP`, or `StatID.Level` via `AddBuffModifier()`.
Rule: These stats are write-locked to their specific write paths and are **not modifier-stack-eligible**. `AddBuffModifier()` returns an error; no write is made.

**EC-16 (Equipment): Equip and Unequip During Active Combat**
Situation: A player swaps weapons mid-combat. Equipment System calls `RemoveEquipmentModifier()` then `AddEquipmentModifier()` in the same frame.
Rule: Character Stats processes both calls atomically in sequence. No "swap transaction" API exists in MVP. EffectiveStat recomputes fresh on the next query. Call ordering is the Equipment System's responsibility.

**EC-17 (Equipment): Item With Both Flat and Percentage Bonus**
Situation: An armor piece grants +80 flat Defense and +12% Defense in a single `AddEquipmentModifier()` call.
Rule: Both bonuses are registered as a single modifier entry. When removed via `RemoveEquipmentModifier()`, both flat and pct contributions are removed atomically — no risk of one persisting without the other.

**EC-18 (Equipment): MaxHP Reduction from Equipment Removal**
Situation: Equipment removal reduces effective MaxHP while CurrentHP exceeds the new value (equipment-specific sub-case of EC-05).
Rule: Reconciliation is immediate — not deferred. `CurrentHP` is clamped within the same `RemoveEquipmentModifier()` call before returning. `GetEffectiveStat(CurrentHP)` cannot return a value above `GetEffectiveStat(MaxHP)` in the same frame. The player is not killed; CurrentHP floors at the new MaxHP.

**EC-19 (Mob): Mob with MagicDefense = 0 Taking Elemental Damage**
Situation: `GetEffectiveStat(EntityID_Mob, StatID.MagicDefense)` returns 0.
Rule: Character Stats returns 0. Elemental damage is **unmitigated** — not amplified. 0 is a valid in-range return, not an error state. The Damage Calculation GDD owns the mitigation formula; Character Stats only guarantees the clamped value.

**EC-20 (Mob): Player-Only Stats Queried on a Mob Entity**
Situation: A system calls `GetEffectiveStat(EntityID_Mob, StatID.Intelligence)`.
Rule: Returns **0** (the schema default for mob entities on player-only fields). No error is thrown. The query interface is identical for all entity types.

**EC-21 (Mob): Buff Modifier Applied to a Mob**
Situation: A player's status effect (e.g., poison debuff) calls `AddBuffModifier()` on a mob entity.
Rule: Mob entities support the buff modifier layer. F-1 applies identically for mobs and players. The MVP restriction is that mobs have no **equipment** modifier layer at spawn — the buff layer is fully operational on mobs.

**EC-22 (Boundary/Clamping): Stat Pushed to Hard Ceiling by Buffs**
Situation: CritChance pre-clamp = 0.80 (base 0.30 + buff 0.50). Hard cap is 0.75.
Rule: F-1 clamps to **0.75**. The excess 0.05 is silently discarded at query time — not stored, not carried over. If a buff contributing 0.10 is later removed, new pre-clamp = 0.70 → result = **0.70**. The cap has no hysteresis.

**EC-23 (Boundary/Clamping): Defense = 0 Behavior**
Situation: A debuff drives effective Defense to 0 (or below, before the clamp).
Rule: Character Stats returns **0**. Physical damage is unmitigated at Defense = 0, not amplified — 0 is the valid floor. Damage Calculation owns the mitigation formula and must handle Defense = 0 correctly; Character Stats makes no guarantee about that formula.

**EC-24 (Boundary/Clamping): VIT Debuff Does Not Reduce MaxHP**
Situation: A debuff reduces effective VIT. Does MaxHP recompute live?
Rule: F-3 through F-9 are evaluated **at level-up only** by the Leveling System and written via `SetBaseStat()`. At runtime, MaxHP is a stored base stat — not recomputed live from VIT. A VIT debuff does NOT lower MaxHP. A direct buff/debuff on `StatID.MaxHP` would reduce effective MaxHP and trigger EC-05/EC-18 reconciliation. There is no cascading formula chain at query time.

**EC-25 (Write Ownership): Duplicate Modifier Registration — Same ModifierID**
Situation: Equipment System calls `AddEquipmentModifier(..., modifierID="sword_01")` twice with the same ID.
Rule: `AddEquipmentModifier()` finds the existing entry by ID and **overwrites** it with the new values. No duplicate entry is created. The add call is idempotent with respect to ID; double-registration from a retry bug cannot cause double-counting.

**EC-26 (Write Ownership): Modifier Removal of a Non-Existent ID**
Situation: `RemoveEquipmentModifier(..., modifierID="ring_of_speed")` is called but no modifier with that ID exists.
Rule: The call is a **no-op** and returns without modifying the modifier list or throwing an exception. Remove is idempotent — safe to call from death cleanup, zone transition, or any cleanup path without guarding for double-removes.

**EC-27 (Write Ownership): Unauthorized Write to Level**
Situation: A system other than the Leveling System calls `SetBaseStat(EntityID, StatID.Level, newValue)`.
Rule: `SetBaseStat()` validates caller identity for write-locked stats. Any caller that is not the Leveling System receives an **error return**; no write is made. The Level field is unchanged.

**EC-28 (Write Ownership): AddExperience Called on a Mob Entity**
Situation: Damage Calculation's on-kill callback accidentally targets a mob entity rather than the killing player.
Rule: `AddExperience()` validates that the target is a player character. If the target is a mob, the call is a **no-op**. Mob Experience is inert — it cannot be written via `AddExperience()` and is never read by any system.

## Dependencies

Character Stats sits at the foundation of the system stack — it has no upstream system dependencies of its own, but is depended on by every combat and progression system in the game. The dependency relationships below are bidirectional: systems that write to Character Stats appear as writers; systems that read from it appear as readers.

**Leveling System** *(Writer)*
Calls `SetBaseStat()` on level-up for all primary attributes (STR, DEX, VIT, INT) and all derived base values (MaxHP, AttackPower, Defense, MagicDefense, CritChance, AttackSpeedMultiplier). Calls `SetBaseStat(Level)` exclusively — no other system may write Level. Reads `GetBaseStat(Level)` to determine current level before computing growth.

**Class System** *(Writer)*
Calls `SetBaseStat()` at entity spawn to initialize class-specific base values: AttackRange, AttackSpeedMultiplier, and starting primary attribute allocations per class. Writes are one-time at spawn; all subsequent writes come from the Leveling System.

**Equipment System** *(Writer)*
Calls `AddEquipmentModifier()` and `RemoveEquipmentModifier()` when items are equipped or unequipped. Owns the equipment modifier layer exclusively. Reads `GetBaseStat()` to know what it is modifying on top of. Does not write elemental weapon damage to Character Stats — elemental weapon bonuses are attributes on the weapon item in the Item Database.

**Status Effects** *(Writer and Reader)*
Writes the buff modifier layer via `AddBuffModifier()` and `RemoveBuffModifier()`. Owns buff expiry — calls Remove when duration elapses. Calls `ApplyRegen(EntityID, amount)` and `ApplyManaRegen(EntityID, amount)` for periodic resource recovery effects. Does not read stat values to compute damage — Damage Calculation does that.

**Damage Calculation** *(Writer and Reader)*
Reads `GetEffectiveStat()` for AttackPower, Defense, MagicDefense, CritChance, and CritMultiplier on every damage resolution event. Writes CurrentHP indirectly via `ApplyDamage(EntityID, amount)` after computing damage. Writes Experience via `AddExperience(EntityID, amount)` on-kill, notifying the Leveling System if a threshold is crossed.

**Skill System** *(Reader and Writer)*
Reads `GetEffectiveStat(CurrentMP)` and `GetEffectiveStat(MaxMP)` before a cast to verify mana is available. Reads skill-specific stat modifiers via `GetEffectiveStat()` as needed. Writes CurrentMP via `ConsumeMana(EntityID, amount)` when a skill is cast. `ConsumeMana()` returns false if mana is insufficient; the Skill System must handle the rejection.

**Auto-Attack Combat** *(Reader)*
Reads `GetEffectiveStat(BaseDamage)` (= EffectiveStat(AttackPower)), `GetEffectiveStat(AttackRange)`, and `GetEffectiveStat(AttackSpeedMultiplier)` live on every Beat Event. Values are never cached across ticks. Reads `GetEffectiveStat(AttackRange)` for the melee range check before damage resolves.

**Character Persistence** *(Reader and Writer)*
Reads all base stats via `GetBaseStat()` and the modifier lists for serialization to disk. On load, restores base stats via `SetBaseStat()` and re-populates modifier lists (equipment and buff modifier re-registration). Owns the save/load boundary for Character Stats — no other system reads from or writes to disk on behalf of this system.

**Combat UI / HUD** *(Reader)*
Reads `GetEffectiveStat(CurrentHP)`, `GetEffectiveStat(MaxHP)`, `GetEffectiveStat(CurrentMP)`, `GetEffectiveStat(MaxMP)`, and `GetEffectiveStat(Level)` for display. Display-only — no writes.

**Item Database** *(Indirect)*
Not a direct dependency. Elemental weapon damage (flat fire/cold/lightning/poison bonus per weapon) is an attribute on the weapon item in the Item Database. Damage Calculation reads both Character Stats and Item Database when resolving elemental hits. Character Stats has no elemental attack stats in MVP; the Item Database is not queried by Character Stats directly.

## Tuning Knobs

All constants are data-driven and must not be hardcoded in the Leveling System or Class System. Changes to any constant require re-validating the affected formula's level-60 targets.

| Knob | Current Value | Formula | Safe Range | Gameplay Effect |
|------|--------------|---------|-----------|-----------------|
| `HPPerVIT` | 20 | F-3: MaxHP | [10, 40] | Controls how tanky VIT-stacking is. Below 10: VIT feels meaningless. Above 40: defense/HP split breaks — tanks become unkillable at level 60. **Playtest tripwire**: At HPPerVIT=20, the Healer Survivability build (VIT=187, L60) reaches MaxHP=7,880 — 43% higher than Warrior Tank (5,520). In a game built on visible stat sheets and class identity signals, a Healer class appearing as the tankiest entity in a zone is a social perception risk. If first-playtest feedback indicates Healer is publicly identified as the tankiest class, revisit HPPerVIT downward or apply a class-specific coefficient (e.g., Warrior gets ×1.0, Healer gets ×0.75 on F-3) before class identities are established in the player community. |
| `MPPerINT` | 12 | F-4: MaxMP | [8, 20] | Controls how long healers can cast before going OOM. Below 8: healers go OOM every fight. Above 20: no mana management pressure. |
| `APPerSTR` | 2 | F-5: AttackPower | [1, 4] | Controls DPS scaling. Above 4: late-game DPS exceeds survivability tuning; fights end before defense matters. Below 1: damage feels flat and stats meaningless. |
| `DefPerVIT` | 1.5 | F-6: Defense | [0.5, 3.0] | Controls physical damage mitigation at high VIT. Above 3.0: Warrior Tank becomes immune to physical damage with moderate equipment. Below 0.5: Defense feels irrelevant. |
| `MDPerINT` | 0.4 | F-7: MagicDefense | [0.2, 1.0] | Controls elemental damage mitigation. Above 1.0: high-INT builds become immune to elemental damage. Below 0.2: MagicDefense feels cosmetic. |
| `CritPerDEX` | 0.0015 | F-8: CritChance | [0.0005, 0.0025] | Controls DEX investment needed for meaningful crit rates. Formula: `0.05 + (DEX × CritPerDEX × LevelTierMultiplier)`. At current value 0.0015, Warrior DEX=128 (full free-point DEX, tier ×2.0) reaches 0.05 + (128×0.0015×2.0) = 0.434 — below the 0.75 cap. Safe ceiling ≈ 0.00273: above this value, Warrior DEX=128 at tier ×2.0 saturates the cap from base stats alone (`0.05 + 128×0.003×2.0 = 0.818 > 0.75`). Above 0.0025: approaching cap saturation risk. Below 0.0005: DEX crit return is imperceptible even with the tier multiplier. Future classes with higher DEX auto-alloc require re-validating this ceiling. |
| `ASMPerDEX` | 0.003 | F-9: AttackSpeedMultiplier | [0.001, 0.004] | Controls attack speed payoff from DEX. LevelTierMultiplier is applied to the DEX component only. At 0.003, Warrior auto-alloc (DEX=69) reaches ASM=1.414 at L60 (0.707s cycle). Above 0.004: Warrior DEX=128 approaches the 2.0 cap from base stats at L60. Below 0.001: DEX attack speed gain is sub-perceptible even with tier multiplier. |
| Level cap | 60 | F-10 | [40, 100] | Controls total progression points (59 level-ups × 5 pts = 295). Increasing the cap requires re-validating all formula level-60 snapshot targets. Decreasing below 40 compresses progression unacceptably. |
| `CritMultiplier` default | 1.5 | F-1 clamp input | [1.2, 2.5] | Starting crit damage bonus (50% extra). Below 1.2: crits feel like noise. Above 2.5: crit-stacking one-shot builds become viable and fun-destructive for other players. Hard cap at 3.0 enforced by schema. |
| `CritChance` hard cap | 0.75 | F-1 clamp | [0.60, 0.85] | Prevents crit-only builds from reliably one-shotting. Do not raise above 0.85 — near-guaranteed crits make crit-stat investment too dominant. |
| `AttackRange` base | 3.0 | — | [2.0, 5.0] | Set by Class System at spawn per class. 3.0 is the base melee range locked by the Auto-Attack Combat GDD. Ranged classes (post-MVP) will use higher values up to the 10.0 schema max. |
| `LevelTierMultiplier` breakpoints | 1.0/1.2/1.5/2.0 | F-2a | [1.0/1.1/1.3/1.8, 1.0/1.3/1.7/2.5] | Controls milestone power spike magnitude. The four values are the L1-19/L20-39/L40-59/L60 multipliers. Narrowing the range produces flatter progression; widening it amplifies milestone moments. Do not set L60 multiplier above 2.5 — late-game stat inflation will invalidate all mob data tables. |

## Visual/Audio Requirements

Character Stats is a passive data store with no visual or audio output of its own. It does not trigger animations, VFX, or sound events.

**What other systems owe to Character Stats:**
- The HUD (Combat UI) must subscribe to `OnStatChanged` for `CurrentHP`, `MaxHP`, `CurrentMP`, `MaxMP`, and update its local cache on notification — not poll per frame. See Rule 8 and UI Requirements.
- Character Stats emits `OnEntityDied` when CurrentHP reaches 0.0. The death animation, death sound, and entity removal are the responsibility of the entity's owner system (Character Controller, Enemy AI), not Character Stats.

**No art or audio assets are owned by this system.**

## UI Requirements

Character Stats owns no UI. The following requirements are constraints Character Stats imposes on the systems that display its data:

- **HP/MP bars**: The HUD must subscribe to `OnStatChanged` for `CurrentHP`, `MaxHP`, `CurrentMP`, and `MaxMP`. On notification, call `GetEffectiveStat()` once to read the new value and update the local display cache. Do not poll these stats every frame — subscribe to events. HP values must be displayed as `Mathf.FloorToInt(CurrentHP)` — the internal float is authoritative; display is always floor-integer.
- **Experience bar**: The HUD must display a small XP bar driven by `GetBaseStat(Experience)` and the XP threshold for the next level (supplied by the Leveling System). Fill ratio = `CurrentXP / XPToNextLevel`. At level 60 (cap), the bar displays full and the XP label shows "MAX". Character Stats provides the raw Experience value; the Leveling System owns the XP thresholds.
- **Stat sheet / character screen**: Any stat inspection screen must read `GetEffectiveStat()` for display of effective values and `GetBaseStat()` for display of base values. The distinction must be visible to the player (e.g., base value + modifier delta shown separately).
- **Real-time update**: When a buff or equipment change modifies a stat, Character Stats fires `OnStatChanged`. The HUD or stat sheet must subscribe to this event and update its local display cache synchronously. No polling is required or permitted for display purposes. The display system must unsubscribe cleanly on HUD destruction to prevent dangling references.
- **Level display**: `GetEffectiveStat(Level)` returns the current level as an int. No fractional levels are possible. The HUD may display Level as an integer label without floor/rounding.

## Acceptance Criteria

### Modifier Stack (F-1)

**AC-01 [BLOCKING]: F-1 computes flat and percentage layers in correct order**
Setup: BaseStat(AttackPower) = 100. Equipment: +30 flat, +15% pct. Buff: +10 flat, +20% pct.
Action: `GetEffectiveStat(AttackPower)`.
Pass: Returns **193** (pre-clamp: `(100 + 30 + 10) × 1.15 × 1.20 = 193.2`).

**AC-02 [BLOCKING]: Percentage bonuses within the same layer are additive, not compounding**
Setup: BaseStat(AttackPower) = 100. Two equipment modifiers: `eq_ring` +10%, `eq_amulet` +10%. No buffs.
Action: `GetEffectiveStat(AttackPower)`.
Pass: Returns **120** (`100 × 1.20`). A result of 121 (compounded) is a failure.

**AC-03 [BLOCKING]: StatMin/StatMax clamp is always applied last — value above StatMax**
Setup: BaseStat(CritChance) = 0.05. Buff: +0.40 flat. Equipment: +0.35 flat.
Action: `GetEffectiveStat(CritChance)`.
Pass: Returns **0.75** (pre-clamp 0.80, clamped to StatMax). Any value above 0.75 is a failure.

**AC-04 [BLOCKING]: Negative flat debuff clamped at StatMin — no underflow**
Setup: BaseStat(AttackPower) = 50. Equipment: +30 flat. Buff (debuff): −200 flat.
Action: `GetEffectiveStat(AttackPower)`.
Pass: Returns **1** (StatMin). Pre-clamp: `(50 + 30 − 200) = −120`. A return of 0 or any negative value is a failure.

**AC-05 [BLOCKING]: Compounded negative-pct debuffs cannot produce a negative effective value**
Setup: BaseStat(MovementSpeed) = 5.0. Buff debuffs summing to ΣPctBuff = −1.20. No equipment modifiers.
Action: `GetEffectiveStat(MovementSpeed)`.
Pass: Returns **0.5** (StatMin). Pre-clamp: `5.0 × (1 − 1.20) = −1.0`. Any value ≤ 0.0 is a failure.

### CurrentHP and CurrentMP Protection

**AC-06 [BLOCKING]: CurrentHP is never modified through the modifier stack**
Setup: Entity with CurrentHP = 500, MaxHP = 1000. Attempt `AddBuffModifier()` targeting StatID.CurrentHP.
Action: Observe return value. Then call `GetEffectiveStat(CurrentHP)`.
Pass: `AddBuffModifier()` returns an error. `GetEffectiveStat(CurrentHP)` returns **500** — unchanged.

**AC-07 [BLOCKING]: MaxHP decrease immediately clamps CurrentHP — no deferred reconciliation**
Setup: Entity with CurrentHP = 2500.0, effective MaxHP = 3000. Remove equipment that reduces effective MaxHP to 2000.
Action: Call `RemoveEquipmentModifier()`. Then immediately call `GetEffectiveStat(CurrentHP)` in the same frame.
Pass: Returns **2000.0**. Entity is not killed. A return of 2500.0 (unreconciled) or 0.0 (dead) is a failure.

**AC-08 [BLOCKING]: MaxHP increase from level-up grants no free HP restoration**
Setup: Entity with CurrentHP = 800.0, MaxHP = 1000. `SetBaseStat(MaxHP, 1200)` called.
Action: `GetEffectiveStat(CurrentHP)` immediately after.
Pass: Returns **800.0** — unchanged. MaxHP is 1200; no HP increase occurred.

**AC-09 [BLOCKING]: Death event fires exactly at CurrentHP = 0.0**
Setup: Entity with CurrentHP = 50.0. No regen active. Register a counter subscriber on `OnEntityDied`: `int diedCount = 0; OnEntityDied += _ => diedCount++;`.
Action: `ApplyDamage(EntityID, 50.0)`.
Pass: `GetEffectiveStat(CurrentHP)` = **0.0**. `diedCount == 1` (fires exactly once — not zero, not twice). No other stat is modified by Character Stats.

**AC-10 [BLOCKING]: Overkill damage clamps CurrentHP at 0.0 — no negative HP**
Setup: Entity with CurrentHP = 30.0. Register counter: `int diedCount = 0; OnEntityDied += _ => diedCount++;`.
Action: `ApplyDamage(EntityID, 500.0)`.
Pass: `GetEffectiveStat(CurrentHP)` = **0.0**. `diedCount == 1` (fires exactly once). No negative value stored or returned.

**AC-10b [BLOCKING]: ApplyDamage on an already-dead entity is a no-op — OnEntityDied does not re-fire**
Setup: Entity with CurrentHP = 0.0. Register counter: `int diedCount = 0; OnEntityDied += _ => diedCount++;`.
Action: `ApplyDamage(EntityID, 100.0)`.
Pass: `GetEffectiveStat(CurrentHP)` = **0.0** — unchanged. `diedCount == 0` — event does not fire on a dead entity.

**AC-11 [BLOCKING]: ConsumeMana returns false and makes no write when mana is insufficient**
Setup: Entity with CurrentMP = 40.0, MaxMP = 200.
Action: `ConsumeMana(EntityID, 100.0)`.
Pass: Returns **false**. `GetEffectiveStat(CurrentMP)` = **40.0** — unchanged. A true return or any MP change is a failure.

**AC-12 [BLOCKING]: ConsumeMana succeeds and writes exactly the cost when mana is sufficient**
Setup: Entity with CurrentMP = 150.0, MaxMP = 200.
Action: `ConsumeMana(EntityID, 100.0)`.
Pass: Returns **true**. `GetEffectiveStat(CurrentMP)` = **50.0**.

**AC-12b [BLOCKING]: ApplyRegen adds to CurrentHP — float precision preserved, clamped at MaxHP**
Setup: Entity with CurrentHP = 50.25f, MaxHP = 100.
Action: `ApplyRegen(EntityID, 0.75f)`.
Pass: `GetEffectiveStat(CurrentHP)` = **51.0f** exactly (50.25f and 0.75f are both exactly representable in IEEE 754 single-precision; their sum is exactly 51.0f — no rounding error on any IEEE 754-compliant platform including iOS IL2CPP ARM). Not rounded, not truncated. Note: do not use non-representable values like 50.3f + 0.7f in this test — their IEEE 754 sum is approximately 50.9999...f, which may floor to 50.0f on ARM devices and fail the assertion.

**AC-12c [BLOCKING]: ApplyRegen at MaxHP boundary — clamped, no overflow**
Setup: Entity with CurrentHP = 99.5, MaxHP = 100.
Action: `ApplyRegen(EntityID, 5.0)`.
Pass: `GetEffectiveStat(CurrentHP)` = **100.0** (clamped to MaxHP; not 104.5). No error thrown.

### Write Ownership Enforcement

**AC-13 [ADVISORY — pending OQ-1]: Only the Leveling System may write Level — unauthorized caller rejected**
*Note: This AC tests caller identity enforcement, which requires OQ-1 (caller identity mechanism) to be resolved before a concrete test can be authored. The observable behavior — error returned, stat unchanged — is correct; the mechanism to construct an "unauthorized caller" in a unit test depends on the chosen enforcement approach (enum tag, interface cast, or compile-time restriction). Downgraded from BLOCKING until OQ-1 is answered.*
Setup: A caller that is not the Leveling System (per the OQ-1 mechanism).
Action: `SetBaseStat(EntityID, StatID.Level, 5)`.
Pass: Returns error. `GetBaseStat(Level)` = **previous value unchanged**.

**AC-14 [BLOCKING]: AddExperience on a mob entity is a no-op**
Setup: Mob entity, Experience = 0.
Action: `AddExperience(MobEntityID, 500)`.
Pass: Completes without error. `GetBaseStat(MobEntityID, Experience)` = **0**. No Leveling System notification fires.

**AC-15 [ADVISORY — pending OQ-1]: AddBuffModifier targeting a write-locked stat is rejected**
*Note: The mechanism by which a stat is designated "write-locked for buff modifiers" (attribute flag vs. caller identity check) depends on OQ-1. The observable behavior — error returned, no modifier created — is specified; the concrete test implementation depends on the chosen enforcement approach.*
Setup: Any entity. Targets: StatID.CurrentHP, StatID.CurrentMP, StatID.Level, StatID.Experience.
Action: `AddBuffModifier(EntityID, [each locked stat], 100, 0, 10, BuffID.TestBuff)`.
Pass: Each call returns an error. No modifier entry created. `GetEffectiveStat()` for each stat returns its pre-call value.

### Buff and Equipment Modifier Lifecycle

**AC-16 [BLOCKING]: Buff removal recomputes effective value from scratch — no base-stat undo**
Setup: BaseStat(AttackPower) = 150. Buff `BuffID.WarriorCry`: +50 flat. EffectiveStat = 200.
Action: `RemoveBuffModifier(EntityID, StatID.AttackPower, BuffID.WarriorCry)`. Then `GetEffectiveStat(AttackPower)`.
Pass: Returns **150**. BaseStat unchanged. F-1 recomputes from empty buff list.

**AC-17 [BLOCKING]: Duplicate buff ID refreshes duration and values — does not double-stack**
Setup: BaseStat(AttackPower) = 100. `AddBuffModifier(..., +30 flat, 0 pct, 20 ticks, BuffID.WarriorCry)`. EffectiveStat = 130.
Action: `AddBuffModifier(..., +30 flat, 0 pct, 40 ticks, BuffID.WarriorCry)` again.
Pass: `GetEffectiveStat(AttackPower)` = **130** (not 160). Duration refreshed to 40 ticks. A return of 160 is a failure.

**AC-18 [BLOCKING]: Duplicate equipment modifier ID overwrites — does not double-count**
Setup: BaseStat(AttackPower) = 100. `AddEquipmentModifier(..., +40 flat, ItemID.Sword01)`. EffectiveStat = 140.
Action: `AddEquipmentModifier(..., +40 flat, ItemID.Sword01)` again.
Pass: `GetEffectiveStat(AttackPower)` = **140** (not 180). One entry for `sword_01` exists.

**AC-19 [BLOCKING]: Removing a non-existent modifier ID is a no-op — no error thrown**
Setup: Entity with BaseStat(MovementSpeed) = 5.0. No modifier `ItemID.RingOfSpeed` registered.
Action: `RemoveEquipmentModifier(EntityID, StatID.MovementSpeed, ItemID.RingOfSpeed)`.
Pass: No exception. `GetEffectiveStat(MovementSpeed)` = **5.0** — unchanged.

**AC-20 [BLOCKING]: Equipment item with flat and pct bonus removes both atomically**
Setup: BaseStat(Defense) = 50. `AddEquipmentModifier(..., +80 flat, +0.12 pct, ItemID.HeavyArmor)`. EffectiveStat = floor((50 + 80) × 1.12) = **145**.
Action: `RemoveEquipmentModifier(EntityID, StatID.Defense, ItemID.HeavyArmor)`.
Pass: `GetEffectiveStat(Defense)` = **50**. A return of 56 (pct persisting) or 130 (flat persisting) is a failure.

### Mob Entity Behavior

**AC-21 [BLOCKING]: Player-only fields return 0 on mob entities — no error**
Setup: Mob entity spawned with mob stat table (no player-only fields written).
Action: `GetEffectiveStat(MobEntityID, StatID.Intelligence)`, `StatID.MaxMP`, `StatID.Experience`.
Pass: Each returns **0**. No exception thrown.

**AC-22 [ADVISORY]: Buff modifier applied to a mob entity applies F-1 correctly**
Setup: Mob with BaseStat(AttackPower) = 80. Buff debuff: −20 flat, `BuffID.Poison`.
Action: `GetEffectiveStat(MobEntityID, StatID.AttackPower)`.
Pass: Returns **60** (`(80 − 20) × 1.0 × 1.0`, clamped to [1, 9999]).

### Formula Correctness

**AC-23 [BLOCKING]: F-3 MaxHP round-trip — Leveling System writes correct value, Character Stats stores and returns it unchanged**
*Note: This AC tests the store-and-retrieve round-trip (the Leveling System computes and writes the correct integer; Character Stats stores and returns it exactly). It does not test formula correctness in Character Stats itself — formula correctness is the Leveling System's concern.*
Setup (a): Leveling System calls `SetBaseStat(MaxHP, floor((200 + 128 × 20) × 2.0))` at L60 (tier ×2.0, VIT=128).
Action: `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass: Returns **5520**. Any other value is a failure.
Setup (b): Leveling System calls `SetBaseStat(MaxHP, floor((200 + 10 × 20) × 1.0))` at L1 (tier ×1.0, VIT=10).
Action: `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass: Returns **400**.

**AC-24 [BLOCKING]: F-8 CritChance hard cap enforced with no hysteresis**
Setup: BaseStat(CritChance) = 0.30. Equipment: +0.50 flat. Pre-clamp = 0.80.
Action: `GetEffectiveStat(CritChance)` → must return **0.75**. Remove equipment. `GetEffectiveStat(CritChance)`.
Pass: After removal returns **0.30** — no memory of the prior cap.

**AC-25 [BLOCKING]: F-6 Defense round-trip — Leveling System applies floor() and writes correct value, Character Stats stores and returns it unchanged**
*Note: This AC tests the round-trip correctness (Leveling System must use FloorToInt, not Mathf.RoundToInt). A return value of 394 confirms both correct formula computation by the Leveling System and correct storage by Character Stats.*
Setup: Leveling System calls `SetBaseStat(Defense, FloorToInt((5 + 128 × 1.5) × 2.0))` at L60 (tier ×2.0, VIT=128).
Action: `GetBaseStat(EntityID, StatID.Defense)`.
Pass: Returns **394** (`floor(197 × 2.0) = floor(394.0)`). A return of 395 (rounding instead of floor) or any other value is a failure.

### XP Bar

**AC-26 [ADVISORY]: GetBaseStat(Experience) returns the raw XP value — not inflated by buff modifiers**
*Note: This AC tests a Character Stats guarantee: Experience has no modifier stack, so GetBaseStat and GetEffectiveStat must return the same value, and no buff can inflate the XP figure. How the HUD wires up its display is a HUD-GDD concern.*
Setup: BaseStat(Experience) = 3500. No buff or equipment modifiers on Experience.
Action: `GetBaseStat(EntityID, StatID.Experience)` and `GetEffectiveStat(EntityID, StatID.Experience)`.
Pass: Both return **3500**. `GetEffectiveStat(Experience)` must equal `GetBaseStat(Experience)` — Experience is not modifier-stackable.

### Tier Multiplier and Event Model

**AC-27 [BLOCKING]: LevelTierMultiplier step fires correctly at milestone level transitions**

*Build note (corrected 2026-09-25):* "Warrior Tank" gains 2 VIT per level = **1 VIT auto-alloc** (Warrior template, applied inside the level-up sequence) **+ 1 free point spent on VIT** (a separate `AllocateFreePoint(VIT)` call after the level-up, which recomputes F-3–F-9 at the current tier). Each sub-case therefore has two observation points: immediately after the level-up (VIT +1) and after the free-point spend (VIT +2). An earlier revision of this AC attributed the full +2 to auto-alloc and listed an incorrect ordering-violation value (2,790; the correct figure for VIT=86 at ×1.5 is 2,880).

Sub-case (a) — L19→L20 transition: Warrior Tank at L19 with VIT=46. Leveling System processes level-up to L20 (auto-alloc: VIT 46→47; tier ×1.0→×1.2; F-3 recomputed).
Action 1: `GetBaseStat(EntityID, StatID.MaxHP)` immediately after level-up.
Pass 1: Returns **1,368** (`floor((200 + 47×20) × 1.2) = floor(1140 × 1.2) = 1368`). A return of 1,140 (tier ×1.0 still applied) or 1,344 (VIT=46 — recompute fired before auto-alloc) is a failure.
Action 2: `AllocateFreePoint(EntityID, StatID.Vitality)` (VIT 47→48), then `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass 2: Returns **1,392** (`floor((200 + 48×20) × 1.2) = floor(1160 × 1.2) = 1392`). A return of 1,160 (tier ×1.0 applied by the free-point recompute) is a failure.

Sub-case (b) — L39→L40 transition: Warrior Tank at L39 with VIT=86 (10 base + 38 level-ups × 2 VIT/level). Leveling System processes level-up to L40 (auto-alloc: VIT 86→87; tier ×1.2→×1.5).
Ordering contract: The Leveling System MUST apply all auto-allocations via `SetBaseStat()` — including the VIT auto-alloc for this level-up — **before** triggering the F-3 MaxHP recomputation for the tier transition. This test verifies Ordering A (auto-alloc → recompute). If the F-3 recompute fires with VIT=86 (before auto-alloc), MaxHP will be `floor((200+86×20)×1.5)=2,880` instead of 2,910. This AC is the regression detector for ordering violations.
Action 1: `GetBaseStat(EntityID, StatID.MaxHP)` immediately after level-up.
Pass 1: Returns **2,910** (`floor((200 + 87×20) × 1.5) = floor(1940 × 1.5) = 2910`). A return of 2,880 (VIT=86 — auto-alloc fired after recompute), 1,940 (tier ×1.0), or 2,328 (tier ×1.2 still applied) is a failure.
Action 2: `AllocateFreePoint(EntityID, StatID.Vitality)` (VIT 87→88), then `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass 2: Returns **2,940** (`floor((200 + 88×20) × 1.5) = floor(1960 × 1.5) = 2940`). A return of 1,960 (tier ×1.0) or 2,352 (tier ×1.2) is a failure.

Sub-case (c) — L59→L60 transition: Warrior Tank at L59 with VIT=126. Leveling System processes level-up to L60 (auto-alloc: VIT 126→127; tier→×2.0).
Action 1: `GetBaseStat(EntityID, StatID.MaxHP)` immediately after level-up.
Pass 1: Returns **5,480** (`floor((200 + 127×20) × 2.0) = floor(2740 × 2.0) = 5480`). A return of 5,440 (VIT=126 — recompute before auto-alloc) or 4,110 (tier ×1.5 still applied) is a failure.
Action 2: `AllocateFreePoint(EntityID, StatID.Vitality)` (VIT 127→128), then `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass 2: Returns **5,520** (`floor((200 + 128×20) × 2.0) = floor(2760 × 2.0) = 5520`). A return of 2,760 (tier ×1.0 applied) or 4,140 (tier ×1.5 still applied) is a failure.

**AC-28 [BLOCKING]: F-9 AttackSpeedMultiplier clamp enforced at [0.5, 2.0]**
Setup (a): Entity at L60 (tier ×2.0), Warrior auto-alloc (DEX=69). Leveling System has written BaseStat(AttackSpeedMultiplier) = 1.414 via F-9: (1.0 + 69×0.003×2.0). Equipment modifier: +1.0 flat ASM bonus added via `AddEquipmentModifier()`. Pre-clamp intermediate: 1.414 + 1.0 = 2.414.
Action: `GetEffectiveStat(EntityID, StatID.AttackSpeedMultiplier)`.
Pass: Returns **2.0** (clamped to StatMax). Any value above 2.0 is a failure.
Setup (b): Entity at L1 (tier ×1.0). BaseStat(AttackSpeedMultiplier) = 1.03 (DEX=10, F-9: 1.0 + 10×0.003×1.0). Buff debuff applied: `AddBuffModifier(EntityID, StatID.AttackSpeedMultiplier, −0.7 flat, 0 pct, 99 ticks, BuffID.SlowDebuff)`. Pre-clamp: 1.03 − 0.7 = 0.33.
Action: `GetEffectiveStat(EntityID, StatID.AttackSpeedMultiplier)`.
Pass: Returns **0.5** (clamped at StatMin). Any value below 0.5 is a failure.

**AC-29 [BLOCKING]: OnStatChanged fires synchronously on modifier add and remove**
Setup: HUD subscriber registered for `OnStatChanged(EntityID, StatID.AttackPower)`. Subscriber sets `bool handlerFired = true` and records `notifiedValue = GetEffectiveStat(entityId, StatID.AttackPower)` — this read-only call is permitted by Rule 8. BaseStat(AttackPower) = 100. No modifiers. Set `handlerFired = false`.
Action: `AddBuffModifier(EntityID, StatID.AttackPower, +30 flat, 0 pct, 10 ticks, BuffID.WarriorCry)`. Immediately after `AddBuffModifier` returns — before any yield or await — assert `handlerFired == true`.
Pass: `handlerFired == true` (synchronous fire confirmed). `notifiedValue == 130` (`GetEffectiveStat` returns 130 at handler call time). Any deferred or coroutine-delayed fire is a failure.
Action (b): Set `handlerFired = false`. `RemoveBuffModifier(EntityID, StatID.AttackPower, BuffID.WarriorCry)`.
Pass: `handlerFired == true`. `notifiedValue == 100`.

**AC-29b [BLOCKING]: Re-entrance guard throws on write-during-handler in dev builds**
Setup: `bool exceptionThrown = false`. Register subscriber for `OnStatChanged(EntityID, StatID.AttackPower)`. Subscriber body: attempt `AddBuffModifier(EntityID, StatID.AttackPower, +10 flat, 0, 5, BuffID.ReentryTest)` inside handler; catch `InvalidOperationException` and set `exceptionThrown = true`. BaseStat(AttackPower) = 100.
Action: `AddBuffModifier(EntityID, StatID.AttackPower, +30 flat, 0 pct, 10 ticks, BuffID.WarriorCry)`.
Pass (dev build): `exceptionThrown == true`. `GetEffectiveStat(AttackPower)` = **130** — only the outer add applied; the inner attempted write was rejected by the re-entrance guard. No modifier corruption.

**AC-30 [BLOCKING]: Mob combat stats set via SetBaseStat() return correct values**
Setup: Mob entity spawned. `SetBaseStat(MobEntityID, StatID.AttackPower, 75)` called.
Action: `GetEffectiveStat(MobEntityID, StatID.AttackPower)`.
Pass: Returns **75** (no formula applied — mob stats are raw base values). `GetBaseStat(MobEntityID, StatID.AttackPower)` also returns **75**.

### Spawn Path and Persistence Load

**AC-31 [BLOCKING]: L1 spawn path initializes derived stats with LevelTierMultiplier ×1.0**
Setup: New Warrior character created. Class System calls Leveling System init. No level-up event has fired.
Action: `GetBaseStat(EntityID, StatID.MaxHP)` immediately after spawn.
Pass: Returns **400** (`floor((200 + 10×20) × 1.0) = floor(400 × 1.0) = 400`). A return of 200 (formula base only, tier not applied) or any other value is a failure. This path is triggered by Class System → Leveling System initialization, not by a level-up event.

**AC-32 [BLOCKING]: Persistence load restores derived stats without re-evaluating formulas**
Setup: Warrior Tank entity at L40 has been saved (persistence stores final computed integer values). Load the entity.
Action: `GetBaseStat(EntityID, StatID.MaxHP)` immediately after load, before any level-up fires.
Pass: Returns **2,940** (the persisted value at L40 with VIT=88). The load path must NOT re-run F-3 through F-9 — it restores the stored integers directly. A return of 200 (formula base) or any value other than 2,940 is a failure.

**AC-33 [BLOCKING]: Respec at a tier boundary recomputes derived stats using current LevelTierMultiplier**
Setup: Entity at L45 (tier ×1.5). Pre-respec: all 44 free points directed to VIT → VIT=98 (10 base + 44 auto-alloc at +1 VIT/level + 44 free-point VIT). Respec is triggered.
Action: `BeginStatTransaction()`. Zero all free-point VIT contributions. Post-respec auto-alloc VIT = 10 base + 1 VIT/level × 44 level-ups = 54 (Warrior auto-alloc is +1 VIT/level). Re-evaluate F-3 with VIT=54 and LevelTierMultiplier ×1.5. `EndStatTransaction()`.
Action: `GetBaseStat(EntityID, StatID.MaxHP)`.
Pass: Returns **1,920** (`floor((200 + 54×20) × 1.5) = floor(1280 × 1.5) = 1920`). A return computed with ×1.0 or ×1.2 (wrong tier) is a failure. A return computed with pre-respec VIT=98 is a failure.

**AC-34 [BLOCKING]: F-4 MaxMP ceiling — Leveling System clamps at 9,999 before SetBaseStat**
Setup (a): Character with INT = 409 at L60 (tier ×2.0). F-4 raw result: `floor((100 + 409×12) × 2.0) = 10,016` — exceeds schema max of 9,999. Leveling System evaluates F-4, clamps to `min(10016, 9999) = 9999`, calls `SetBaseStat(MaxMP, 9999)`.
Action: `GetBaseStat(EntityID, StatID.MaxMP)`.
Pass: Returns **9999** — the clamped value. A return of 10016 (unclamped raw formula output stored to base stat) is a failure — it would mean `GetBaseStat` returns an out-of-range value that Character Persistence would serialize to disk incorrectly.
Setup (b): Same character, increase INT to 500. Leveling System evaluates F-4: `floor((100 + 500×12) × 2.0) = 12,200`. Clamp: `min(12200, 9999) = 9999`.
Action: `GetBaseStat(EntityID, StatID.MaxMP)`.
Pass: Returns **9999** — clamp holds regardless of how far INT exceeds the threshold.
Note: System ownership is explicit — the Leveling System clamps F-4 output; Character Stats `SetBaseStat()` does not enforce StatMax.

## Open Questions

**OQ-1: Caller identity enforcement mechanism**
AC-13 and AC-15 require that `SetBaseStat(Level)` and `AddBuffModifier()` on write-locked stats reject unauthorized callers. The GDD specifies the *what* (reject and return error) but not the *how*. How caller identity is verified at runtime (enum tag, interface cast, compile-time type restriction, or trust-based with runtime asserts) is an ADR decision. Flag for `/architecture-decision` before implementation.

**OQ-2: OnEntityDied event contract**
AC-09 specifies that `OnEntityDied` fires when CurrentHP reaches 0.0. The event receiver and the sequence of handling (who removes the entity, who triggers respawn, who stops auto-attack) is not defined in this GDD. The Character Controller and Enemy AI GDDs must specify their subscription and response to this event before either system is implemented.

**OQ-3: Modifier list serialization format**
Character Persistence reads modifier lists for serialization. The storage format (JSON, binary, ScriptableObject) for the equipment and buff modifier lists is not specified here. This is an ADR decision — the persistence format must be chosen before the Character Persistence GDD is authored.

**OQ-4: VIT debuff does not reduce MaxHP — communicate to Leveling System GDD**
The rule that runtime VIT debuffs do not cascade into MaxHP changes (F-3 is level-up only, per EC-24) must be explicitly called out in the Leveling System GDD to prevent a future implementer from wiring a live VIT→MaxHP feed. Flag this as a cross-GDD constraint when authoring the Leveling System.

**OQ-5: Stat-change events for HUD polling — RESOLVED (fully specified)**
Decision: Character Stats fires `OnStatChanged(EntityID, StatID)` synchronously on every modifier change. HUD subscribes and maintains a local display cache — no per-frame polling. Delegate type, subscriber registration pattern, re-entrance guard, and IL2CPP safety requirements are all now specified in Rule 8. No ADR required before HUD implementation for the event contract itself; subscriber registration lifetime and HUD architecture remain HUD-GDD concerns.

**OQ-7: Healer INT party expression — BLOCKING cross-GDD dependency**
In this GDD, Healer INT drives only MaxMP and MagicDefense — both are self-facing stats. The Social Gravity pillar ("party play is always better") requires that Healer INT investment be party-visible, not just personally beneficial. HealPower — a derived value scaling with INT that modifies healing skill output — must be defined in the Skill System GDD.

**Dependency constraint**: Character Stats GDD APPROVAL does not depend on this. However, **Character Stats implementation sign-off** is contingent on the Skill System GDD confirming that INT scales HealPower. Without this confirmation, the Social Gravity pillar is architecturally unvalidated at the stat foundation: a Healer's primary auto-alloc stat (INT) has zero party-visible mechanical output at this layer, which is a pillar delivery gap.

If the Skill System GDD defines heals as flat values unrelated to INT, the Healer Survivability build's balance rationale (see F-3 design note) also collapses — a VIT-Healer with high HP and flat heals would not be limited by MaxMP in the way the GDD assumes. The Skill System GDD author must treat this as a hard constraint, not a suggestion. Flag this dependency explicitly when authoring the Skill System GDD.

**OQ-6: Respec policy — RESOLVED**
Decision: Costly respec. Players may reallocate all free stat points by consuming a rare in-game item. Auto-allocated points are permanent. Implementation: Leveling System zeros free-point contributions and reapplies them via `SetBaseStat()` in a single transaction, suppressing intermediate `OnStatChanged` events. Item definition and acquisition path TBD by Economy Designer (Leveling System GDD). See F-10 for the full respec policy statement.
