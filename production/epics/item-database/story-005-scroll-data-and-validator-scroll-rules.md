# Story 005: ScrollData Sub-Schema and Validator Scroll Rules

> **Epic**: Item Database
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/item-database.md` (Amendment #4, 2026-10-02 — Rule 13, `ScrollData` schema, Edge Cases "Category constraint violations")
**Requirement**: `TR-itemdb-006`
*(Placeholder ID — `docs/architecture/tr-registry.yaml` is empty; the requirement text lives in the EPIC's GDD Requirements table until the registry is populated.)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the Item Database schema or its import validator. The module is pure, stateless data plus pure C# validation logic. ADR-001 (Purchase Integrity, Accepted) only establishes `ItemID` as the cross-system reference key and is not affected by this story.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- `ItemDefinition._scrollData` must use `[SerializeReference]`, not `[SerializeField]` — same reason as `_equipmentData` / `_consumableData`: without it Unity's serializer builds a default instance and `ScrollData != null` would be true on every record (GDD Schema Reference, `ScrollData` row). `ScrollData != null` is the Enhancement System's scroll test (Rule 13 item 36), so a false positive here makes every item pass as a scroll.
- `ScrollData` must be a `[Serializable]` `class` (not a struct) so `[SerializeReference]` can hold a true `null`.
- The 34 existing `.asset` files have no `_scrollData` entry; Unity deserializes a missing `[SerializeReference]` field as `null`, which is the correct value for all 34. No asset migration is needed in this story.
- No post-cutoff Unity API is used.

**Control Manifest Rules (Foundation layer)**:
- Required: `[SerializeField]` on private fields only — compile error on properties in Unity 6.3 — source: ADR-009
- Cross-cutting: C# null-coalescing operators (`?.`, `??`) do not work correctly with Unity `Object` subclasses — use explicit null checks on `ItemDefinition`. (`ScrollData`, `ConsumableData` and `EquipmentData` are plain C# classes; ordinary null checks apply.)
- Naming: private fields `_camelCase`, public members PascalCase.

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story:*

- [x] **AC-42** [BLOCKING]: Validator receives a consumable record with `ConsumableData == null` and `ScrollData == null` → error, record rejected.
- [x] **AC-43** [BLOCKING]: Validator receives a consumable record with both `ConsumableData` and `ScrollData` set → error, record rejected.
- [x] **AC-44** [BLOCKING]: Validator receives an equipment record that is otherwise valid but has `ScrollData` set → error, record rejected.
- [x] **AC-45** [BLOCKING]: Validator receives a scroll record with `ScrollData.TargetGearTier = GearTier.None` → error, record rejected. Also tested with an integer cast to `GearTier` outside the defined values.
- [x] **AC-46** [BLOCKING]: Validator receives a scroll record with `ItemCategory = Consumable`, `ScrollData.TargetGearTier = Bronze`, `ConsumableData == null`, `EquipmentData == null`, `StackLimit = 99`, `SellPriceGold = 0`, `IsUpgradeable = false` → result is valid and contains zero issues: no error and no warning. *(Confirms the zero-sell-price warning of AC-35 is not raised for scrolls.)*
- [x] **Regression**: all existing tests in `ItemDatabase_Core_tests.cs`, `ItemDatabase_Validator_Error_tests.cs`, `ItemDatabase_Validator_Warning_tests.cs` and `ItemDatabase_MvpRecords_tests.cs` still pass unchanged.

---

## Implementation Notes

*Derived from GDD Rule 13 (items 34–39), the Schema Reference, and the Edge Cases section. No ADR Implementation Guidelines apply.*

### Files

| File | Change |
|------|--------|
| `src/Foundation/ItemDatabase/ScrollData.cs` | **New.** `[Serializable] public class ScrollData` with `[SerializeField] private GearTier _targetGearTier`, a `TargetGearTier` property, and an `#if UNITY_EDITOR` `internal static ScrollData CreateForTesting(GearTier targetGearTier)` seam — same layout as `ConsumableData.cs`. |
| `src/Foundation/ItemDatabase/ItemDefinition.cs` | Add `[SerializeReference] private ScrollData _scrollData;`, a `ScrollData` property with a doc comment, and an optional `ScrollData scrollData = null` parameter on `SetForTesting`. Update the class remarks and the `ConsumableData` property comment (it is also `null` on scroll records). |
| `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs` | Rules below. |
| `tests/EditMode/ItemDatabase/TestHelpers/ItemDefinitionBuilder.cs` | Add an optional `ScrollData scrollData = null` parameter and pass it through. |
| `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Scroll_tests.cs` | **New.** Tests for AC-42–46. |

Append the new `scrollData` parameter as the **last** parameter of `SetForTesting` and of `ItemDefinitionBuilder.Build`. Existing callers pass `equipmentData` and `consumableData` positionally (`ItemDefinitionBuilder.Build`) and `MvpItemRecordData` uses named arguments; appending keeps every existing call site compiling with the same meaning.

### Validator rules

1. **Zero-sell-price warning — equipment only.** `ValidateSellPrice` currently raises the `SellPriceGold == 0` warning for every category. The GDD scopes it to equipment records (AC-35; Edge Cases "Economy fields": "The zero-price warning applies to equipment records only"). Restrict it to `ItemCategory.Equipment`. The `SellPriceGold < 0` error (AC-25) stays category-independent. Without this change AC-46 cannot pass.
2. **Equipment with `ScrollData` (AC-44).** In `ValidateEquipment`, add an error when `item.ScrollData != null`, next to the existing `ConsumableData != null` check.
3. **Exactly one of `ConsumableData` / `ScrollData` (AC-42, AC-43; Rule 13 item 35).** `ValidateConsumable` currently returns an error as soon as `ConsumableData == null`. Replace that with:
   - both `null` → error (AC-42);
   - both set → error (AC-43).
4. **Checks that apply to potions and scrolls alike.** These must run for a scroll record too — today the early return on `ConsumableData == null` skips them:
   - `EquipmentData != null` → error (AC-5; Rule 13 item 34 requires `EquipmentData == null` on a scroll);
   - `StackLimit == 0` → error (AC-22; Edge Cases: "This applies to potions and scrolls alike").
5. **Potion-only check.** `EffectMagnitude <= 0` (AC-21) runs only when `ConsumableData != null`.
6. **Scroll tier (AC-45).** When `ScrollData != null`: `TargetGearTier == GearTier.None`, or a value that is not a defined `GearTier` member, → error. `GearTier` is `byte`-backed with members 0–4, so `(GearTier)255` is the out-of-range test value. Use the same enum-membership idiom the validator already uses for `GearSlot` (AC-7).
7. The F-1 / F-2 sell-price deviation warning does not apply to scrolls (Rule 13 item 39). The existing deviation check lives inside `ValidateEquipment`, so no change is needed — do not add one for consumables.

Error messages follow the existing format: name the item (`DisplayName`), its `ItemId`, the field and the value.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 006**: the four scroll records (IDs 35–38) in `MvpItemRecordData`, the potion record value alignment, record-count test updates (34 → 38), the seeder and `ITEM_ID_REGISTRY.txt`.
- The Enhancement System's use of `ScrollData.TargetGearTier` (tier match, CR-ENH-3 / CR-ENH-15) — Enhancement System epic. The Item Database stores the tier; it does not apply the match.
- The Consumable Use System treating an item with `ConsumableData == null` as not usable — that system's epic.
- A `CooldownSeconds = 0` warning — listed in the GDD Edge Cases but has no acceptance criterion and is not implemented in the validator today. Not part of Amendment #4.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases below are derived one-to-one from the GDD acceptance criteria. The developer implements against these — do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Scroll_tests.cs`

**Fixture note**: build records with `ItemDefinitionBuilder.Build(...)` and destroy them in `[TearDown]` via `Object.DestroyImmediate`, as the existing validator suites do. Each test validates one rule in isolation with `ItemDefinitionValidator.ValidateRecord`.

- **AC-42**: Consumable with neither sub-schema → error
  - Given: `ItemCategory.Consumable`, `ConsumableData == null`, `ScrollData == null`, `StackLimit = 99`, valid `ItemID`
  - When: `ValidateRecord(record)`
  - Then: `IsValid == false`; at least one issue with `ValidationSeverity.Error`
  - Edge cases: none beyond the rule itself

- **AC-43**: Consumable with both sub-schemas → error
  - Given: `ItemCategory.Consumable`, `ConsumableData = (RestoreHP, 80, 20)`, `ScrollData = (Bronze)`, `StackLimit = 99`
  - When: `ValidateRecord(record)`
  - Then: `IsValid == false`; at least one issue with `ValidationSeverity.Error`
  - Edge cases: a potion record with only `ConsumableData` set (same values, `ScrollData == null`) → `IsValid == true`

- **AC-44**: Equipment with `ScrollData` → error
  - Given: an otherwise valid Bronze Weapon record (`StackLimit = 1`, `SellPriceGold = 10`, valid `EquipmentData`) with `ScrollData = (Bronze)`
  - When: `ValidateRecord(record)`
  - Then: `IsValid == false`; exactly one `Error` issue and its message names `ScrollData`
  - Edge cases: the same record with `ScrollData == null` → `IsValid == true`, zero issues

- **AC-45**: Scroll with invalid `TargetGearTier` → error
  - Given: a scroll record as in AC-46 but with `TargetGearTier = GearTier.None`
  - When: `ValidateRecord(record)`
  - Then: `IsValid == false`; `Error` issue present
  - Edge cases: `TargetGearTier = (GearTier)255` → `IsValid == false`; each of `Bronze`, `Iron`, `Steel`, `DarkSteel` → `IsValid == true`

- **AC-46**: Well-formed scroll → valid, zero issues
  - Given: `ItemCategory.Consumable`, `ScrollData.TargetGearTier = Bronze`, `ConsumableData == null`, `EquipmentData == null`, `StackLimit = 99`, `SellPriceGold = 0`, `IsUpgradeable = false`
  - When: `ValidateRecord(record)`
  - Then: `IsValid == true` and `Issues.Count == 0`
  - Edge cases: same scroll with `StackLimit = 0` → `IsValid == false` (AC-22 applies to scrolls); same scroll with non-null `EquipmentData` → `IsValid == false` (AC-5 / Rule 13 item 34); a Bronze equipment record with `SellPriceGold = 0` still produces the AC-35 warning (existing `ItemDefinitionValidator_SellPriceGoldZero_ReturnsWarning` must keep passing)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Scroll_tests.cs` — must exist and pass. Run the full `ItemDatabase` EditMode folder in the Unity Test Runner (a live Editor is available) and record the pass count.

**Status**: [x] Created — 12 test methods (15 NUnit cases), AC-42–46 covered (reject + accept/boundary pairs)

---

## Dependencies

- Depends on: Stories 001–003 (Complete) — `ItemDefinition`, the validator and its test seams.
- Unlocks: Story 006 (the scroll records need `ScrollData` and must pass these validator rules).

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 6/6 passing (0 deferred)
**Deviations**: Advisory only — `TR-itemdb-006` is not in `tr-registry.yaml` (registry is empty; requirement checked against GDD AC-42–46 directly, same condition as stories 001–004); two test files outside the story's file table were touched by the code-review fixes (`ItemDatabase_Validator_Warning_tests.cs`: stale AC-35 comment corrected + `PotionSellPriceGoldZero_NoWarning`; `ItemDatabase_Validator_Error_tests.cs`: `UndefinedItemCategory_ReturnsError`) — no existing test method was modified
**Test Evidence**: Logic — `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Scroll_tests.cs` (12 test methods, 15 NUnit cases). Full EditMode suite 1109/1109 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `ItemDefinitionValidator.cs` returned CHANGES REQUIRED (`ValidateConsumable` complexity, `ValidateEquipment` length); both required changes and all six suggestions applied (helpers `ValidateEquipmentRecordShape`, `ValidateConsumableSubSchemaChoice`, `ValidateScrollData`); suite re-run green; fixes not re-reviewed
**Tech debt logged**: None
