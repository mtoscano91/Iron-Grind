# Story 006: MVP Item Records — Scroll Records 35–38 and Potion Value Alignment

> **Epic**: Item Database
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Config/Data
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/item-database.md` (Amendment #4, 2026-10-02 — Rule 12, Rule 13 item 37, OQ-5 resolution)
**Requirement**: `TR-itemdb-005` (revised 2026-10-02: 38 records)
*(Placeholder ID — `docs/architecture/tr-registry.yaml` is empty; the requirement text lives in the EPIC's GDD Requirements table until the registry is populated.)*

**ADR Governing Implementation**: None (data authoring only)
**ADR Decision Summary**: No ADR governs item data authoring. Records are defined once in `MvpItemRecordData` and written to `.asset` files by `ItemDatabaseSeeder`. `ItemID` assignment is sequential; IDs are never reused (GDD Rule 11). This story supersedes the record count and the potion placeholder values of Story 004 — that story file is implementation history and is not edited.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- The seeder deletes and recreates every `.asset` at its target path, so re-running it gives all 38 assets **new GUIDs**. Nothing in `src/`, `tests/` or `Assets/` references the item assets by path or GUID as of 2026-10-02. Re-check before re-seeding (search `Assets/` and `ProjectSettings/` for the GUIDs in `Assets/data/items/**/*.asset.meta`); if a reference exists, stop and raise it instead of re-seeding.
- `[SerializeReference]` on `_scrollData` is added by Story 005. After seeding, open one scroll asset and one potion asset in the Inspector / as text and confirm the scroll has a non-null `_scrollData` and a null `_consumableData`, and the potion the reverse.
- No post-cutoff Unity API is used.

**Control Manifest Rules (Foundation layer)**:
- Required: `[SerializeField]` on private fields only — compile error on properties in Unity 6.3 — source: ADR-009
- Guardrail: any validation failure must abort loading — do not enter gameplay with a corrupt database (TR-itemdb-005)

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story:*

- [x] **AC-47** [BLOCKING]: Of the 38 MVP records, exactly 4 have `ScrollData != null`; their `TargetGearTier` values are `Bronze`, `Iron`, `Steel`, `DarkSteel`, each exactly once; each of the 4 has `ItemCategory == Consumable`, `ConsumableData == null`, `EquipmentData == null`, `StackLimit == 99`, `SellPriceGold == 0`, `IsUpgradeable == false`.
- [x] **AC-18** [BLOCKING] *(updated)*: Database initialized with the 38 MVP records — `GetItemsByCategory(ItemCategory.Equipment).Count == 28`; every element is `Equipment`; none is `Consumable`.
- [x] **AC-24** [BLOCKING] *(updated)*: Across all 38 records, every Equipment record has `IsUpgradeable = true` and every Consumable record (potions and Enhancement Scrolls) has `IsUpgradeable = false`.
- [x] **AC-34** [BLOCKING] *(updated)*: Exactly 28 records are `Equipment` and exactly 10 are `Consumable` (6 with `ConsumableData`, 4 with `ScrollData`); no record holds both categories.
- [x] **AC-33** [BLOCKING] *(unchanged — must still hold)*: HP Potion Small / Medium / Large `SellPriceGold` = 2 / 6 / 18; same for MP.
- [x] **Rule 13 item 37**: the four scroll records use `ItemID` 35–38 and the `DisplayName` values `Bronze Enhancement Scroll`, `Iron Enhancement Scroll`, `Steel Enhancement Scroll`, `Dark Steel Enhancement Scroll`. IDs 1–34 are unchanged.

*Potion value alignment — sourced outside this GDD's acceptance criteria:*

- [x] **`CooldownSeconds`** — Small 20, Medium 30, Large 45, for both HP and MP potions. *Source: item-database.md OQ-5 resolution ("The item records must carry these values"); consumable-use-system.md "CooldownSeconds — Authored Constants".*
- [x] **`EffectMagnitude`** — Small 80, Medium 220, Large 500, for both HP and MP potions. *Source: consumable-use-system.md F-CUS-3 table and Tuning Knobs; entities.yaml potion entries.*
- [x] **`StackLimit`** — 99 on all six potion records. *Source: entities.yaml `stack_limit: 99` on each potion entry; inventory-system.md Rule 5 (maximum 99 per slot in MVP).*

*Validation:*

- [x] `ItemDefinitionValidator.ValidateBatch(all38Records)` returns zero fatal, zero errors, **zero warnings**.

---

## Implementation Notes

*Derived from GDD Rules 11–13 and the sources cited above.*

### Files

| File | Change |
|------|--------|
| `src/Foundation/ItemDatabase/MvpItemRecordData.cs` | Add the four scroll records after MP Potion (Large); change the six `BuildConsumable` rows to the aligned values; list capacity and doc comments 34 → 38; remove the "placeholder consumable cooldowns (OQ-5 unresolved)" remark (OQ-5 is resolved). |
| `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` | Doc comment and menu label no longer say "34" / "(Story 004)"; the failure log line names this story. No logic change is required — scroll records are `Consumable` and land in `Assets/data/items/consumables/`. |
| `Assets/data/items/ITEM_ID_REGISTRY.txt` | Add IDs 35–38; totals 28 Equipment, 10 Consumable (38 records); remove the OQ-5 "UNRESOLVED" entry; `NEXT FREE ID: 39`; "Add new items … (id 39+)". |
| `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` | Counts 34 → 38 and 6 → 10; new AC-47 test; new potion value tests; `ValidateBatch` test also asserts zero warnings. |
| `Assets/data/items/consumables/*.asset` | Re-seeded in the Unity Editor (4 new scroll assets; 6 potion assets with new values). |
| `production/qa/smoke-[date]-item-database.md` | New smoke check report. |

### Scroll records (GDD Rule 13 item 37)

| ItemID | `DisplayName` | `ScrollData.TargetGearTier` | `StackLimit` | `SellPriceGold` | `IsUpgradeable` |
|--------|---------------|-----------------------------|--------------|-----------------|-----------------|
| 35 | Bronze Enhancement Scroll | `Bronze` | 99 | 0 | `false` |
| 36 | Iron Enhancement Scroll | `Iron` | 99 | 0 | `false` |
| 37 | Steel Enhancement Scroll | `Steel` | 99 | 0 | `false` |
| 38 | Dark Steel Enhancement Scroll | `DarkSteel` | 99 | 0 | `false` |

`ItemCategory = Consumable`, `ConsumableData = null`, `EquipmentData = null` on all four. Note the display name is "Dark Steel" (two words) while the enum member and the existing equipment names use `DarkSteel` — follow the GDD table for the scroll names and do not rename existing records. Scroll buy prices are not stored here (GDD Rule 10 / Rule 13 item 39 — NPC Shop owns them).

Add a `BuildScroll` helper beside `BuildConsumable`, using `ScrollData.CreateForTesting` and the `scrollData:` named argument added by Story 005. `Description` and `IconAddress` follow the existing placeholder pattern.

### Potion records — aligned values

| ItemID | Item | `EffectType` | `EffectMagnitude` | `CooldownSeconds` | `StackLimit` | `SellPriceGold` |
|--------|------|--------------|-------------------|-------------------|--------------|-----------------|
| 29 | HP Potion (Small) | RestoreHP | 80 | 20 | 99 | 2 |
| 30 | HP Potion (Medium) | RestoreHP | 220 | 30 | 99 | 6 |
| 31 | HP Potion (Large) | RestoreHP | 500 | 45 | 99 | 18 |
| 32 | MP Potion (Small) | RestoreMP | 80 | 20 | 99 | 2 |
| 33 | MP Potion (Medium) | RestoreMP | 220 | 30 | 99 | 6 |
| 34 | MP Potion (Large) | RestoreMP | 500 | 45 | 99 | 18 |

Current coded values (Story 004 placeholders): magnitudes 150 / 400 / 1000 (HP) and 100 / 280 / 700 (MP), all cooldowns 30, `StackLimit` 20 / 10 / 5. Sell prices are already correct and do not change.

### Seeding and validation pass

1. Run the EditMode `ItemDatabase` test folder in the Unity Test Runner — all green before seeding.
2. Run the seeder menu item. It must log 38 created records and "Validation PASSED — 0 fatal, 0 errors, 0 warning(s)". A scroll's `SellPriceGold = 0` must not produce a warning (Story 005).
3. Confirm 4 new assets `ItemID(35)_…` – `ItemID(38)_…` exist in `Assets/data/items/consumables/`.
4. Write the smoke check report.

---

## Out of Scope

*Handled by neighbouring stories / other epics — do not implement here:*

- **Story 005**: `ScrollData.cs`, `ItemDefinition._scrollData`, all validator rule changes.
- Scroll buy prices and the NPC Shop Buy catalog (npc-shop.md; enhancement-system.md TK-ENH-9).
- Any Enhancement System or Consumable Use System code.
- Editing `story-004-mvp-item-records.md` — kept as implementation history.
- **OQ-1** (flat-bonus values) — equipment placeholders are unchanged.
- **OQ-8** (DarkSteel sell price vs enhancement cost) — 270g unchanged.
- The four "recommended, not applied" wording fixes from the Pass 5 review of item-database.md — design authoring, not this story.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases below are derived from the GDD acceptance criteria and the cited source values.*

*Story Type: Config/Data — smoke check is the required evidence. Because the record set is defined in code (`MvpItemRecordData`), the criteria are also asserted in `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs`.*

**EditMode assertions (against `MvpItemRecordData.BuildAll()`):**

- **AC-47**: exactly 4 records have `ScrollData != null`; the set of their `TargetGearTier` values equals `{Bronze, Iron, Steel, DarkSteel}` with no duplicate; for each: `ItemCategory == Consumable`, `ConsumableData == null`, `EquipmentData == null`, `StackLimit == 99`, `SellPriceGold == 0`, `IsUpgradeable == false`.
- **AC-18**: after `Initialize(records)`, `GetItemsByCategory(Equipment).Count == 28`, all `Equipment`; `GetItemsByCategory(Consumable).Count == 10`.
- **AC-24**: all 28 Equipment `IsUpgradeable == true`; all 10 Consumable `IsUpgradeable == false`.
- **AC-34**: `records.Count == 38`; 28 Equipment; 10 Consumable, of which 6 have `ConsumableData != null` and 4 have `ScrollData != null`; no Consumable has both or neither.
- **AC-33**: existing HP / MP sell price tests pass unchanged (2 / 6 / 18).
- **Rule 13 item 37**: records with `ItemId` 35, 36, 37, 38 have the four scroll display names in tier order; the records with `ItemId` 1–34 keep their existing display names.
- **Potion values**: for each of the six potion records, `EffectMagnitude`, `CooldownSeconds` and `StackLimit` equal the "aligned values" table above (exact equality — these are authored constants).
- **Validation**: `ValidateBatch(records).IsValid == true` and `Issues.Count == 0`.
- **IDs**: all 38 `ItemId` values are unique and none is `ItemID.Invalid`.

**Smoke check** (manual, in the Unity Editor, after running the seeder) — evidence file `production/qa/smoke-[date]-item-database.md`:

- [x] Seeder log: 38 records created; validation 0 fatal, 0 errors, 0 warnings
- [x] `Assets/data/items/equipment/` holds 28 assets; `Assets/data/items/consumables/` holds 10
- [x] Each scroll asset: `_scrollData` non-null with the expected tier, `_consumableData` null, `_equipmentData` null
- [x] Each potion asset: `_consumableData` non-null with the aligned values, `_scrollData` null
- [x] EditMode `ItemDatabase` test folder: all tests pass (90 cases across 5 fixtures)
- [x] `ITEM_ID_REGISTRY.txt` lists IDs 35–38 and `NEXT FREE ID: 39`

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: `production/qa/smoke-[date]-item-database.md` — smoke check pass report (0 fatal, 0 errors, 0 warnings), plus `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` passing in the Unity Test Runner.

**Status**: [x] Complete — `production/qa/smoke-2026-10-02-item-database.md` (0 fatal, 0 errors, 0 warnings)

---

## Dependencies

- Depends on: Story 005 must be DONE (`ScrollData`, validator scroll rules, zero-price warning scoped to equipment).
- Unlocks: Enhancement System implementation (scroll records must exist — item-database.md Dependencies) and NPC Shop startup catalog validation (npc-shop.md F-NS-3: `GetItem(scrollId)` returns `null` until these records exist).

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 10/10 passing (0 deferred)
**Deviations**: Advisory only — `TR-itemdb-005` is not in `tr-registry.yaml` (registry is empty; checked against the GDD directly); the Engine Notes' warning that re-seeding changes every asset GUID did not hold — no tracked `.meta` file changed; the smoke check ran from the command line (Unity batch mode, `-executeMethod` on the seeder, assets inspected as YAML), not through the Editor menu / Inspector; the 28 equipment assets were rewritten too (new `_scrollData` entry and reference IDs — data values unchanged); potions are built by a `BuildPotionFamily` helper rather than six edited rows (same values)
**Test Evidence**: Config/Data — `production/qa/smoke-2026-10-02-item-database.md` (PASS: 38 records, 0 fatal, 0 errors, 0 warnings). `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` 25/25; full EditMode suite 1125/1125 in Unity 6000.3.10f1 batch mode
**Code Review**: Skipped (Config/Data story, no code review gate; `MvpItemRecordData.cs` gained `BuildPotionFamily` and `BuildScroll`)
**Tech debt logged**: TD-047 in `docs/tech-debt-register.md` — `_itemId` is not serialized into any item `.asset` (pre-existing since Story 004; must be fixed before any asset-loading code)
