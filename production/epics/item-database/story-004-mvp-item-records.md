# Story 004: MVP Item Records — 34 Authored ScriptableObject Assets

> **Epic**: Item Database
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Config/Data
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/item-database.md`
**Requirement**: `TR-itemdb-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (data authoring only; no code logic)
**ADR Decision Summary**: No ADR governs item data authoring. `ItemDefinition` ScriptableObject assets are authored in the Unity Editor (or via a data-entry tool). `ItemID` assignment is sequential starting at 1 (OQ-6 process for MVP). The import validator (Stories 002 + 003) must pass for every record before this story can be marked Done.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- Create assets via Unity Editor: right-click in Project window → Create → IronGrind → Item Definition (requires `[CreateAssetMenu]` attribute on `ItemDefinition`)
- Each item is one `.asset` file in `Assets/data/items/` organized by category subfolder
- `[SerializeReference]` on `EquipmentData` and `ConsumableData` requires Unity 2020.1+ — fully supported in Unity 6.3
- Run the import validator on all 34 records before marking this story Done

**Control Manifest Rules (Foundation layer)**:
- Required: `[SerializeField]` on private fields only — compile error on properties in Unity 6.3 — source: ADR-009
- Guardrail: Any validation failure must abort loading — do not enter gameplay with a corrupt database (TR-itemdb-005)

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story:*

- [x] **AC-18** [BLOCKING]: Database initialized with the 34 MVP records — `GetItemsByCategory(ItemCategory.Equipment).Count == 28`; every element has `ItemCategory == Equipment`; no element has `ItemCategory == Consumable`.
- [x] **AC-24** [BLOCKING]: Every Equipment record has `IsUpgradeable = true`; every Consumable record has `IsUpgradeable = false`; no exceptions.
- [x] **AC-30** [BLOCKING]: Bronze Sword record: `SellPriceGold == 10` (F-1 minimum at default tuning).
- [x] **AC-31** [BLOCKING]: DarkSteel Sword record: `SellPriceGold == 270` (F-1 maximum at default tuning).
- [x] **AC-33** [BLOCKING]: HP Potion Small `SellPriceGold == 2`, HP Potion Medium `SellPriceGold == 6`, HP Potion Large `SellPriceGold == 18`. Same values for MP Potion variants.
- [x] **AC-34** [BLOCKING]: Exactly 28 records have `ItemCategory == Equipment` and exactly 6 have `ItemCategory == Consumable`. No record holds both categories.

---

## Implementation Notes

*Derived from GDD Rule 12 — MVP Item Count and Formulas F-1, F-2:*

### Asset Location

`Assets/data/items/equipment/` — 28 equipment records
`Assets/data/items/consumables/` — 6 consumable records

### ItemID Assignment (OQ-6 resolution for MVP)

Sequential IDs starting at 1, assigned at authoring time:

| Range | Category |
|-------|----------|
| 1–4   | Swords (Bronze, Iron, Steel, DarkSteel) |
| 5–8   | Helmets (Bronze, Iron, Steel, DarkSteel) |
| 9–12  | Chest Armor (Bronze, Iron, Steel, DarkSteel) |
| 13–16 | Leg Armor (Bronze, Iron, Steel, DarkSteel) |
| 17–20 | Boots (Bronze, Iron, Steel, DarkSteel) |
| 21–24 | Rings (Bronze, Iron, Steel, DarkSteel) |
| 25–28 | Necklaces (Bronze, Iron, Steel, DarkSteel) |
| 29–31 | HP Potions (Small, Medium, Large) |
| 32–34 | MP Potions (Small, Medium, Large) |

Document this mapping in `Assets/data/items/ITEM_ID_REGISTRY.txt` to prevent collision as new items are added.

### Sell Prices (F-1 / F-2)

**Equipment (F-1 default values):**
| GearTier | SellPriceGold |
|----------|--------------|
| Bronze   | 10           |
| Iron     | 30           |
| Steel    | 90           |
| DarkSteel | 270         |

**Consumables (F-2, SmallBasePrice = 2g):**
| Size   | SellPriceGold |
|--------|--------------|
| Small  | 2            |
| Medium | 6            |
| Large  | 18           |

### Flat Bonus Placeholder Values (OQ-1 UNRESOLVED)

OQ-1 (flat bonus values by stat and tier) is unresolved. Author the following **placeholder values** for MVP development and playtest iteration. These must be replaced before Vertical Slice based on economy design and OQ-1 resolution.

**Sword FlatBonus (AttackPower):**
| GearTier | AP FlatBonus |
|----------|-------------|
| Bronze   | 5           |
| Iron     | 12          |
| Steel    | 25          |
| DarkSteel | 45         |

**Helmet FlatBonus (Defense):**
| GearTier | DEF FlatBonus |
|----------|--------------|
| Bronze   | 3            |
| Iron     | 8            |
| Steel    | 18           |
| DarkSteel | 32          |

**Chest Armor FlatBonus (Defense + Vitality):**
| GearTier | DEF FlatBonus | VIT FlatBonus |
|----------|--------------|--------------|
| Bronze   | 5            | 2            |
| Iron     | 12           | 5            |
| Steel    | 25           | 10           |
| DarkSteel | 45          | 18           |

**Leg Armor FlatBonus (Defense):**
| GearTier | DEF FlatBonus |
|----------|--------------|
| Bronze   | 4            |
| Iron     | 10           |
| Steel    | 22           |
| DarkSteel | 38          |

**Boots FlatBonus (Defense + MovementSpeed):**
| GearTier | DEF FlatBonus | MOV FlatBonus |
|----------|--------------|--------------|
| Bronze   | 2            | 0.05         |
| Iron     | 5            | 0.08         |
| Steel    | 11           | 0.12         |
| DarkSteel | 20          | 0.18         |

**Ring FlatBonus (Intelligence or Dexterity — 1 stat only):**
| GearTier | INT FlatBonus |
|----------|--------------|
| Bronze   | 3            |
| Iron     | 8            |
| Steel    | 18           |
| DarkSteel | 32          |

**Necklace FlatBonus (MaxHP):**
| GearTier | MaxHP FlatBonus |
|----------|----------------|
| Bronze   | 20             |
| Iron     | 50             |
| Steel    | 110            |
| DarkSteel | 200           |

### Equip Requirements

**Placeholder** — no equip requirements for Bronze gear. Apply placeholder STR requirements to higher tiers (Iron: STR ≥ 15, Steel: STR ≥ 30, DarkSteel: STR ≥ 50). Ring and Necklace: `EquipRequirementStat = null` (no stat gate at MVP per Equipment System GDD OQ-EQS-2 resolution).

### Consumable Records

| Item | EffectType | EffectMagnitude | CooldownSeconds | StackLimit |
|------|-----------|----------------|----------------|-----------|
| HP Potion Small | RestoreHP | 150.0 | 30.0 | 20 |
| HP Potion Medium | RestoreHP | 400.0 | 30.0 | 10 |
| HP Potion Large | RestoreHP | 1000.0 | 30.0 | 5 |
| MP Potion Small | RestoreMP | 100.0 | 30.0 | 20 |
| MP Potion Medium | RestoreMP | 280.0 | 30.0 | 10 |
| MP Potion Large | RestoreMP | 700.0 | 30.0 | 5 |

*(CooldownSeconds = 30.0 is a placeholder — OQ-5 is unresolved. Requires designer confirmation.)*

### Validation Pass

Before marking this story Done, run `ItemDefinitionValidator.ValidateAll(all34Records)`:
- Zero fatal errors
- Zero errors
- Any warnings are documented and acknowledged by the designer

---

## Out of Scope

*Handled by neighbouring stories / future work:*

- **OQ-1**: Exact flat bonus values per stat and tier — placeholders used here; resolution required before VS
- **OQ-5**: Consumable cooldown tuning — placeholders used; resolution required before playtesting
- **OQ-6**: Long-term ItemID assignment process beyond MVP — sequential IDs for now
- **OQ-8**: DarkSteel sell price vs Enhancement System cost curve — provisional 270g; verify after Enhancement System GDD
- `MergeResultItemID` on Ring/Necklace — Equipment System GDD (CR-EQS-14) owns this; `null` at MVP
- ElementalDamage authoring — all MVP Swords are `ElementType.None`, `ElementalDamage = 0` (physical only)
- Icon assets (`IconAddress` string field) — authored as placeholder strings; actual sprites belong to Art pipeline
- AC-23: HP/MP cooldown independence — untestable until Consumable Use System exists; carry to that epic

---

## QA Test Cases

*Story Type: Config/Data — smoke check, no unit test file required.*

**Smoke check**: Run `ItemDefinitionValidator.ValidateAll(all34Records)` in the Unity Editor after authoring all records. Evidence file: `production/qa/smoke-[date]-item-database.md`

Smoke check assertions (manually verified in editor):
- [ ] `ValidateAll` returns zero errors, zero fatal errors for all 34 records
- [ ] `GetItemsByCategory(Equipment).Count == 28`
- [ ] `GetItemsByCategory(Consumable).Count == 6`
- [ ] Bronze Sword: `SellPriceGold == 10`
- [ ] DarkSteel Sword: `SellPriceGold == 270`
- [ ] HP Potion Small: `SellPriceGold == 2`, Medium: 6, Large: 18
- [ ] All 28 equipment records: `IsUpgradeable == true`
- [ ] All 6 consumable records: `IsUpgradeable == false`
- [ ] All 34 ItemIDs are unique (no duplicate)
- [ ] No record uses `ItemID(0)`
- [ ] All equipment records have `GearTier != None`
- [ ] All consumable records have `EffectMagnitude > 0`

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: `production/qa/smoke-[date]-item-database.md` — smoke check pass report

**Status**: [x] Complete — `production/qa/smoke-2026-07-04-item-database.md` (0 fatal, 0 errors, 0 warnings)

---

## Dependencies

- Depends on: Story 001 (ItemDefinition type), Story 002 (error validator), Story 003 (warning validator) must be Done
- Unlocks: All downstream epics that require Item Database to be ready (Inventory System, Equipment System, Loot Table System, Enhancement System, NPC Shop)

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 6/6 passing (0 deferred)
**Deviations**: Advisory only — `ItemDefinition.SetForTesting` extended with `description`/`iconAddress` params (backward-compatible); this session also bootstrapped the project's first real Unity Editor project and fixed 4 pre-existing latent bugs in Character Stats/Item Database test code as enabling work — see `docs/tech-debt-register.md` (TD-002, TD-006, both closed) and `production/session-state/active.md` for full detail
**Test Evidence**: Config/Data — `production/qa/smoke-2026-07-04-item-database.md` (0 fatal, 0 errors, 0 warnings; seeder run confirmed in real Unity Editor, 6000.3.10f1)
**Code Review**: Not applicable (Config/Data story, no code review gate)
