# Story 003: Import Validator — Warning Rules (Accept Path)

> **Epic**: Item Database
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/item-database.md`
**Requirement**: `TR-itemdb-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the import validator. Warning rules emit a non-blocking diagnostic and accept the record. The validator returns a `ValidationResult` with `IsValid = true` and populated `Warnings` list — the record is imported normally.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Same as Story 002 — pure C# logic, no engine API surface. F-1 tolerance check uses `Mathf.RoundToInt` for rounding consistency with the GDD formula.

**Control Manifest Rules (Foundation layer)**:
- Required: `[SerializeField]` on private fields only — source: ADR-009
- Guardrail: Warning rules must NOT reject records — warnings surface designer confirmation needs; the validator returns IsValid = true for all warning-only results

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story — warning cases and confirmed-accept paths:*

- [ ] **AC-13** [BLOCKING]: Validator receives weapon record with `ElementType = Fire` and `ElementalDamage = 0` → result is accepted (`IsValid = true`), no error, no warning. *(Non-None element + zero damage is valid — Enhancement System may scale elemental damage from zero.)*
- [ ] **AC-17** [ADVISORY]: Validator receives equipment record with `StatModifierEntry.FlatBonus < 0.0f` → result is accepted (`IsValid = true`); a warning is emitted naming the item DisplayName and the `StatId`; no error returned.
- [ ] **AC-32** [ADVISORY]: Validator receives equipment record where `SellPriceGold` deviates from the F-1 `TierBasePrice` for its `GearTier` by more than ±5% → result is accepted (`IsValid = true`); a warning is emitted naming the item, the authored value, and the F-1 expected value. *(Tolerance: exclusive threshold — deviation strictly > 5% triggers the warning.)*
- [ ] **AC-35** [ADVISORY]: Validator receives any item with `SellPriceGold = 0` → result is accepted (`IsValid = true`); a warning is emitted naming the item. *(Zero sell price is technically valid but likely a data authoring error for any non-gift item.)*

---

## Implementation Notes

*Derived from GDD Edge Cases and Formulas sections:*

### Extension to ItemDefinitionValidator (Story 002)

Add warning checks to the existing `ItemDefinitionValidator.Validate()` method. Warnings populate `ValidationResult.Warnings` but do not affect `IsValid`.

### F-1 Tolerance Check (AC-32)

**TierBasePrice lookup:**
| GearTier | TierBasePrice |
|----------|--------------|
| Bronze   | 10           |
| Iron     | 30           |
| Steel    | 90           |
| DarkSteel | 270         |

**Tolerance formula:**
```csharp
float expected = TierBasePrice(record.EquipmentData.GearTier);
float deviation = Mathf.Abs(record.SellPriceGold - expected) / expected;
if (deviation > 0.05f)
    warnings.Add($"[ItemDatabase] '{record.DisplayName}': SellPriceGold={record.SellPriceGold} deviates from F-1 expected {expected}g by {deviation:P1}.");
```

Only applies to Equipment records (F-2 governs Consumable sell prices separately — no tolerance check for consumables in MVP).

### AC-13 Positive Acceptance Test

`ElementType != None` with `ElementalDamage = 0` is explicitly valid (GDD Edge Case: "valid — represents a weapon with elemental affinity but no base bonus at +0"). AC-13 is a negative test confirming the validator does NOT emit an error or warning for this case.

### Warning Message Format

- AC-17: `"[ItemDatabase] '{DisplayName}': StatModifierEntry({StatId}) has FlatBonus={FlatBonus} (negative penalty). Confirm this is intentional."`
- AC-32: `"[ItemDatabase] '{DisplayName}': SellPriceGold={authored} deviates from F-1 expected {expected}g ({deviation:P1}). Confirm or correct."`
- AC-35: `"[ItemDatabase] '{DisplayName}': SellPriceGold=0 — item cannot be sold to NPCs. Confirm this is intentional."`

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 002**: All error/rejection rules
- **Story 004**: The 34 actual `.asset` records that run through the complete validator
- F-2 consumable sell price tolerance check — not in the GDD ACs; deferred if needed
- Duplicate StatID in one item's array (GDD Edge Case: advisory — log warning naming the item and both values) — not covered by a numbered AC; implement in this story as a bonus warning if time permits, but not required

---

## QA Test Cases

*Test file*: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Warning_tests.cs`

- **AC-13**: ElementType non-None + ElementalDamage = 0 → accepted without warning
  - Given: Sword record with `ElementType = ElementType.Fire`, `ElementalDamage = 0`
  - When: `Validate(record)`
  - Then: `IsValid == true`; `Errors.Count == 0`; `Warnings.Count == 0`
  - Edge case: `ElementType = ElementType.None`, `ElementalDamage = 0` (physical weapon) → also accepted, also no warning

- **AC-17**: FlatBonus < 0 → warning, accepted
  - Given: Equipment record with `StatModifierEntry { StatId = StatID.MovementSpeed, FlatBonus = -5.0f }`
  - When: `Validate(record)`
  - Then: `IsValid == true`; `Errors.Count == 0`; `Warnings.Count >= 1`; warning text contains "MovementSpeed"
  - Edge case: Two entries, both negative → two warnings, both named

- **AC-32**: SellPrice deviation > 5% → warning, accepted
  - Given: Bronze equipment (TierBasePrice=10) with `SellPriceGold = 8` (deviation = 20%, above 5% threshold)
  - When: `Validate(record)`
  - Then: `IsValid == true`; warning emitted naming item, authored value (8), expected value (10)
  - Edge case: `SellPriceGold = 10` (exactly F-1) → no warning
  - Edge case: `SellPriceGold = 9` (10% below — above threshold) → warning
  - Edge case: `SellPriceGold = 11` (within ±5% exclusive) → verify boundary (5% of 10 = 0.5g; 10.5g is > 5%? → yes, 10 + 0.5 = 10.5 is the exclusive boundary; SellPrice = 10 is no warning, = 11 is warning)
    - Boundary: Bronze tolerance window [9g–11g] exclusive — `SellPriceGold = 9` triggers warning; `SellPriceGold = 10` does not

- **AC-35**: SellPriceGold = 0 → warning, accepted
  - Given: Equipment record with `SellPriceGold = 0`
  - When: `Validate(record)`
  - Then: `IsValid == true`; `Errors.Count == 0`; warning emitted naming the item
  - Edge case: `SellPriceGold = 1` → no warning from AC-35 (though AC-32 might fire if it deviates from F-1)

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Warning_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (ItemDefinition type), Story 002 (validator error infrastructure) must be Done
- Unlocks: Story 004 (all 34 records must pass the complete validator — error + warning rules)
