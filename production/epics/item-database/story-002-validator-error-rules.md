# Story 002: Import Validator — Reject Rules (Error Path)

> **Epic**: Item Database
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 4–5 hours

## Context

**GDD**: `design/gdd/item-database.md`
**Requirement**: `TR-itemdb-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the import validator. The validator is pure C# logic with no engine API surface. One IL2CPP constraint applies: `Enum.IsDefined<StatID>()` boxes the value and may fail under aggressive iOS stripping — use a `static readonly HashSet<StatID>` with explicit `EqualityComparer<StatID>.Default` and add `StatID` to `link.xml` preservation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- **IL2CPP StatID validation**: Do NOT use `Enum.IsDefined` — it boxes the value and may fail under aggressive iOS stripping. Build a `static readonly HashSet<StatID>` from `Enum.GetValues<StatID>()` once at class initialization; use `_validStatIds.Contains(entry.StatId)` for per-entry validation. Add `StatID` to `link.xml`: `<type fullname="IronGrind.CharacterStats.StatID" preserve="All" />`
- Validator runs at import time (Unity Editor `OnValidate()` or a standalone tool), not at runtime hot-path — no performance constraints.

**Control Manifest Rules (Foundation layer)**:
- Required: `[SerializeField]` on private fields only — compile error on properties in Unity 6.3 — source: ADR-009
- Guardrail: Duplicate ItemID detection must name both conflicting records in the error message — silent overwrite is a Rule 1 violation

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story — error/rejection cases only:*

- [x] **AC-3** [BLOCKING]: Validator receives two records sharing the same `ItemID` → returns fatal error naming both records; database loader aborts without registering either record.
- [x] **AC-4** [BLOCKING]: Validator receives equipment record with `StackLimit ≠ 1` → error, record rejected.
- [x] **AC-5** [BLOCKING]: Validator receives consumable record with `GearSlot ≠ None` → error, rejected. Independently: consumable with `GearTier ≠ None` → error, rejected. Both conditions simultaneously → error, rejected.
- [x] **AC-7** [BLOCKING]: Validator receives equipment record with `GearSlot` outside `{Weapon=0, Helmet=1, Chest=2, Legs=3, Boots=4, Ring=5, Necklace=6}` (e.g., a cast integer value of 7 or 255) → error, rejected.
- [x] **AC-8** [BLOCKING]: Validator receives non-weapon equipment (`GearSlot ≠ Weapon`) with `ElementType ≠ None` → error, rejected. Independently: non-weapon with `ElementalDamage > 0` → error, rejected.
- [x] **AC-10** [BLOCKING]: Validator receives equipment record with `GearTier == GearTier.None` → error, rejected. (Equipment must carry Bronze/Iron/Steel/DarkSteel.)
- [x] **AC-11** [BLOCKING]: Validator receives equipment record with any `StatModifierEntry` where `FlatBonus == 0.0f` → error, rejected.
- [x] **AC-12** [BLOCKING]: Validator receives weapon record with `ElementType == ElementType.None` and `ElementalDamage > 0` → error, rejected.
- [x] **AC-14** [BLOCKING]: Validator receives weapon record with `ElementalDamage == 10_000` (one above ceiling of 9,999) → error, rejected.
- [x] **AC-15** [BLOCKING]: Validator receives equipment record with 3 or more `StatModifier` entries → error, rejected. (Max 2 per item to stay within 16-entry Character Stats capacity.)
- [x] **AC-21** [BLOCKING]: Validator receives consumable record with `EffectMagnitude ≤ 0` → error, rejected. (Test: == 0 separately from < 0.)
- [x] **AC-22** [BLOCKING]: Validator receives consumable record with `StackLimit == 0` → error, rejected.
- [x] **AC-25** [BLOCKING]: Validator receives any item record with `SellPriceGold < 0` → error, rejected.
- [x] **AC-26** [BLOCKING]: Validator receives record with `ItemID == 0` (`ItemID.Invalid`) → fatal error, rejected.
- [x] **AC-41** [BLOCKING]: Validator receives equipment record with `StatModifierEntry.StatId` not present in the `StatID` enum → error, rejected. (Character Stats silently drops unknown StatIDs in release builds — invisible stat loss.)

---

## Implementation Notes

*Derived from GDD Rules 1–12, Edge Cases section:*

### Validator Architecture

Create `ItemDefinitionValidator` in `src/Foundation/ItemDatabase/`:

```csharp
public static class ItemDefinitionValidator
{
    public static ValidationResult Validate(ItemDefinition record);
    public static ValidationResult ValidateAll(IReadOnlyList<ItemDefinition> records);
}

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public bool IsFatal { get; }   // true for duplicate-ID and ItemID=0
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
}
```

`ValidateAll` is needed for AC-3 (duplicate ID detection requires seeing all records). The database loader calls `ValidateAll` before registering any records — if `IsFatal == true` on any result, abort the entire load.

### Valid GearSlot Range

`{Weapon=0, Helmet=1, Chest=2, Legs=3, Boots=4, Ring=5, Necklace=6}` — 7 values (OQ-3 resolved; old `Accessory=5` is superseded by Ring=5, Necklace=6). Cast integers outside [0, 6] are invalid.

### StatID Validation (IL2CPP Safe)

```csharp
private static readonly HashSet<StatID> _validStatIds =
    new HashSet<StatID>(Enum.GetValues<StatID>().Cast<StatID>(),
                        EqualityComparer<StatID>.Default);
```

Add to `link.xml`:
```xml
<type fullname="IronGrind.CharacterStats.StatID" preserve="All" />
```

### Error Message Format

For AC-3 (duplicate ID): `"[ItemDatabase] Duplicate ItemID({id}) found in records '{record1.DisplayName}' and '{record2.DisplayName}'. Aborting load."` — must name both.

For AC-26 (ItemID = 0): `"[ItemDatabase] Record '{record.DisplayName}' uses ItemID(0) = ItemID.Invalid. ItemID 0 is permanently reserved."` — fatal.

### Rejection vs Warning

This story implements **only rejection (error) cases**. Warning rules are in Story 003. A rejected record is never registered in the database — `ItemDatabase.Initialize()` must abort loading if any record produces an error result (not just if `IsFatal`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 003**: Warning rules (FlatBonus<0, sell price deviation, CooldownSeconds=0, SellPrice=0, same StatID twice in one item)
- **Story 004**: The 34 actual `.asset` records that pass the validator
- `OnValidate()` ScriptableObject editor hook wiring — editor tooling, not Foundation layer
- F-1 tolerance check (sell price deviation) — belongs in Story 003

---

## QA Test Cases

*Test file*: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Error_tests.cs`

**Fixture note**: Build minimal `ItemDefinition` instances via `ScriptableObject.CreateInstance<ItemDefinition>()` and set fields via test-exposed setters or reflection as needed. Each test validates one rule in isolation.

- **AC-3**: Duplicate ItemID → fatal error
  - Given: Two records both with `ItemID(5)`, `DisplayName` "Alpha" and "Beta"
  - When: `ValidateAll(new[] { alpha, beta })`
  - Then: `IsFatal == true`; error message contains "Alpha" and "Beta"; database does not register either record

- **AC-4**: Equipment StackLimit ≠ 1 → error
  - Given: Equipment record with `StackLimit = 2`
  - When: `Validate(record)`
  - Then: `IsValid == false`; error message references StackLimit

- **AC-5**: Consumable with GearSlot ≠ None → error (three sub-cases)
  - Given: Consumable with `GearSlot = GearSlot.Weapon`, `GearTier = GearTier.None`
  - Then: rejected
  - Given: Consumable with `GearSlot = GearSlot.None` (N/A — consumables have no slot field; test that GearTier ≠ None is caught)
  - Given: Consumable with both `GearSlot ≠ None` and `GearTier ≠ None`
  - Then: rejected (at least one error)

- **AC-7**: GearSlot outside valid enum → error
  - Given: Equipment record with `GearSlot = (GearSlot)255`
  - When: `Validate(record)`
  - Then: `IsValid == false`

- **AC-8**: Non-weapon with ElementType ≠ None → error; with ElementalDamage > 0 → error
  - Given: Helmet record with `ElementType = ElementType.Fire` → rejected
  - Given: Helmet record with `ElementalDamage = 10` → rejected
  - Edge case: Weapon with ElementType.Fire + ElementalDamage = 10 → accepted (valid weapon)

- **AC-10**: Equipment GearTier = None → error
  - Given: Equipment record with `GearTier = GearTier.None`
  - Then: rejected

- **AC-11**: StatModifierEntry FlatBonus = 0.0 → error
  - Given: Equipment record with one `StatModifierEntry { StatId = AttackPower, FlatBonus = 0.0f }`
  - Then: rejected; error names the zero-contribution entry

- **AC-12**: Weapon ElementType.None + ElementalDamage > 0 → error
  - Given: Sword record with `ElementType.None` and `ElementalDamage = 25`
  - Then: rejected

- **AC-14**: ElementalDamage = 10,000 → error
  - Given: Weapon record with `ElementalDamage = 10_000`
  - Then: rejected
  - Edge case: `ElementalDamage = 9_999` → accepted

- **AC-15**: 3+ StatModifiers → error
  - Given: Equipment record with 3 `StatModifierEntry` elements
  - Then: rejected
  - Edge case: 2 entries → accepted

- **AC-21**: Consumable EffectMagnitude ≤ 0 → error
  - Given: Consumable with `EffectMagnitude = 0.0f` → rejected
  - Given: Consumable with `EffectMagnitude = -1.0f` → rejected
  - Edge case: `EffectMagnitude = 0.001f` → accepted

- **AC-22**: Consumable StackLimit = 0 → error
  - Given: Consumable with `StackLimit = 0`
  - Then: rejected
  - Edge case: `StackLimit = 1` → accepted

- **AC-25**: SellPriceGold < 0 → error
  - Given: Any item with `SellPriceGold = -1`
  - Then: rejected
  - Edge case: `SellPriceGold = 0` → accepted by error rules (warning only — Story 003)

- **AC-26**: ItemID = 0 → fatal error
  - Given: Record with `ItemID = ItemID.Invalid` (uint 0)
  - When: `Validate(record)`
  - Then: `IsFatal == true`; error logged

- **AC-41**: Unknown StatID in StatModifierEntry → error
  - Given: Equipment record with `StatModifierEntry { StatId = (StatID)255, FlatBonus = 10.0f }`
  - Then: rejected; error names the unrecognized StatID value

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Error_tests.cs` — must exist and pass

**Status**: [x] Created — 25 test methods, all 15 blocking ACs covered (reject + accept/boundary pairs)

---

## Dependencies

- Depends on: Story 001 must be Done (ItemDefinition type required)
- Unlocks: Story 003 (warning rules extend the same validator), Story 004 (assets must pass both error and warning rules)

## Completion Notes
**Completed**: 2026-07-04
**Criteria**: 15/15 passing (0 deferred)
**Deviations**: Advisory only — `StatModifierEntry.cs`/`EquipmentData.cs`/`ConsumableData.cs` extended with `CreateForTesting` seams (outside stated file scope, approved mid-session, consistent with Story 001's pattern); `ValidationResult` API redesigned from the story's sketched `IsFatal`/`Errors`/`Warnings` shape to `Issues`/`ValidationSeverity` (approved, no downstream dependents yet); a mid-session scope-creep incident (Story 003 warning logic added then removed) left no residual trace in the final code
**Test Evidence**: Logic — `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Error_tests.cs` (25 test methods)
**Code Review**: Complete — `/code-review` ran twice (CHANGES REQUIRED → APPROVED WITH SUGGESTIONS after the 6 missing boundary tests were added)
**Tech debt logged**: TD-004, TD-005 in `docs/tech-debt-register.md`
