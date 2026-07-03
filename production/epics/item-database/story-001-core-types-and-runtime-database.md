# Story 001: IItemDatabase Interface, Runtime Database, and Core Type Definitions

> **Epic**: Item Database
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 4–6 hours

## Context

**GDD**: `design/gdd/item-database.md`
**Requirement**: `TR-itemdb-001`, `TR-itemdb-003`, `TR-itemdb-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the Item Database runtime implementation. The system is pure, stateless data — no Unity gameplay API surface beyond `ScriptableObject` initialization. `ItemID` and `StatID` are already defined in `src/Foundation/CharacterStats/` — import from there, do not redefine. ADR-001 is referenced in the epic only because `ItemID` is the cross-system reference key used in purchase messages; ADR-001 does not constrain Item Database implementation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- `ItemDefinition : ScriptableObject` — each record is a `.asset` file. Use `ScriptableObject.CreateInstance<ItemDefinition>()` in EditMode tests; no scene or `MonoBehaviour` required.
- `[SerializeReference]` is required for `EquipmentData?` and `ConsumableData?` fields on `ItemDefinition`. Without it, Unity's serializer creates a default instance for class fields, causing null-checks to silently pass on Consumable items (EquipmentData would be non-null even though not authored).
- `[SerializeField]` on **private fields only** — compile error on properties in Unity 6.3 (source: ADR-009).
- `StatModifierEntry` is a `[Serializable]` struct (NOT ValueTuple). Struct equality works correctly under IL2CPP.
- `OnDatabaseReady` must be a standard C# `event Action` (not `UnityEvent`) so the test harness can subscribe counter delegates without editor dependency.

**Control Manifest Rules (Foundation layer)**:
- Required: Service interface naming `I[SystemName]Service` — `IItemDatabase` follows this convention — source: ADR-010
- Required: `[SerializeField]` on private fields only — no properties — source: ADR-009
- Forbidden: Never use `UnityEvent` for server-side game logic — source: ADR-010
- Forbidden: Never use `Action<object>` or class-typed event args — source: ADR-010
- Guardrail: Pre-init query paths return null/empty + dev error; must not throw — crashing the initialization sequence is worse than returning null

---

## Acceptance Criteria

*From GDD `design/gdd/item-database.md`, scoped to this story:*

- [ ] **AC-1** [BLOCKING]: `GetItem(ItemID.Invalid)` (ItemID(0)) returns `null`, no exception thrown, no error logged. `ItemID.Invalid` is a valid sentinel — not an error condition.
- [ ] **AC-2** [BLOCKING]: Two callers invoking `GetItem(new ItemID(1))` in the same frame receive the exact same `ItemDefinition` reference (`object.ReferenceEquals(ref1, ref2) == true`). Single authoritative data, no per-caller copies.
- [ ] **AC-19** [BLOCKING]: `GetItemsByCategory` called with a value outside `{Equipment, Consumable}` (e.g., `(ItemCategory)999`) returns an empty `IReadOnlyList<ItemDefinition>`, logs a dev-build error, and throws no exception.
- [ ] **AC-28** [BLOCKING]: Database constructed but `Initialize()` not called — `GetItem(new ItemID(1))` returns `null`, `IsReady == false`, a dev-build error is logged, no exception thrown.
- [ ] **AC-29a** [BLOCKING]: After `Initialize()` completes, `IsReady == true`.
- [ ] **AC-29b** [BLOCKING]: Handler subscribed to `OnDatabaseReady` before `Initialize()` is invoked exactly once when `Initialize()` completes. A second call to `Initialize()` does not fire `OnDatabaseReady` again.
- [ ] **AC-29c** [BLOCKING]: When `IsReady` is already `true` and a new handler is added via `+=`, the handler is invoked synchronously on the calling thread before the assignment expression returns.
- [ ] **AC-36** [BLOCKING]: `TryGetItem(new ItemID(1), out var def)` returns `true` and `def` is `object.ReferenceEquals`-equal to `GetItem(new ItemID(1))`.
- [ ] **AC-37** [BLOCKING]: `TryGetItem(ItemID.Invalid, out var def)` returns `false`, `def == null`, no exception.
- [ ] **AC-38** [BLOCKING]: Database not initialized — `TryGetItem(new ItemID(1), out var def)` returns `false`, `def == null`, `IsReady == false`, dev error logged, no exception.
- [ ] **AC-40** [BLOCKING]: Database not initialized — `GetItemsByCategory(ItemCategory.Equipment)` returns empty `IReadOnlyList<ItemDefinition>`, `IsReady == false`, dev error logged, no exception.

---

## Implementation Notes

*Derived from GDD Rules 1–12 and Schema Reference:*

### Types to Create

**`ItemCategory`** — `public enum ItemCategory : byte { Equipment = 0, Consumable = 1 }`

**`GearSlot`** — `public enum GearSlot : byte { Weapon = 0, Helmet = 1, Chest = 2, Legs = 3, Boots = 4, Ring = 5, Necklace = 6 }` *(OQ-3 resolved — Ring/Necklace replace old single Accessory slot)*

**`GearTier`** — `public enum GearTier : byte { None = 0, Bronze = 1, Iron = 2, Steel = 3, DarkSteel = 4 }`

**`ElementType`** — `public enum ElementType : byte { None = 0, Fire = 1, Cold = 2, Lightning = 3, Poison = 4 }`

**`EffectType`** — `public enum EffectType : byte { RestoreHP = 0, RestoreMP = 1 }`

**`StatModifierEntry`** — `[Serializable] public struct StatModifierEntry { [SerializeField] private StatID _statId; [SerializeField] private float _flatBonus; public StatID StatId => _statId; public float FlatBonus => _flatBonus; }` — NOT a ValueTuple; must be a named [Serializable] struct for ScriptableObject asset serialization.

**`EquipmentData`** — `public class EquipmentData` with fields: `GearSlot GearSlot`, `GearTier GearTier`, `StatModifierEntry[] StatModifiers`, `ElementType ElementType`, `int ElementalDamage`, `StatID? EquipRequirementStat`, `float EquipRequirementMin`, `ItemID? MergeResultItemID` (accessories only).

**`ConsumableData`** — `public class ConsumableData` with fields: `EffectType EffectType`, `float EffectMagnitude`, `float CooldownSeconds`.

**`ItemDefinition : ScriptableObject`** — all [SerializeField] on private backing fields only. `[SerializeReference]` on `EquipmentData _equipmentData` and `ConsumableData _consumableData` (nullable sub-schema contract).

### IItemDatabase Interface

```csharp
public interface IItemDatabase
{
    ItemDefinition? GetItem(ItemID id);
    bool TryGetItem(ItemID id, out ItemDefinition item);
    IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category);
    bool IsReady { get; }
    event Action OnDatabaseReady;
}
```

### ItemDatabase Class

- Internal `Dictionary<ItemID, ItemDefinition>` populated in `Initialize()`.
- Internal `Dictionary<ItemCategory, List<ItemDefinition>>` pre-indexed at `Initialize()` for `GetItemsByCategory`.
- `OnDatabaseReady` late-subscriber: in the `add` accessor, if `_isReady == true`, invoke the handler immediately before storing.
- `GetItem(ItemID.Invalid)`: fast-path return `null` without logging (sentinel — not an error).
- Pre-init paths (not yet initialized): return `null`/empty + log `Debug.LogError` in dev builds (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`); do not throw.
- `GetItemsByCategory` with unknown enum value: return empty list + `Debug.LogError`.

### Existing Types (import, do not redefine)

- `ItemID` — already at `src/Foundation/CharacterStats/ItemID.cs`, namespace `IronGrind.CharacterStats`
- `StatID` — already at `src/Foundation/CharacterStats/StatID.cs`, namespace `IronGrind.CharacterStats`

Place all new Item Database types in `src/Foundation/ItemDatabase/` with namespace `IronGrind.ItemDatabase`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 002/003**: Import validator (`ItemDefinitionValidator`) — validation logic is separate from the runtime database class
- **Story 004**: The 34 MVP `.asset` records — this story tests with `ScriptableObject.CreateInstance<ItemDefinition>()` instances created in test setup
- Addressables loading — `IconAddress` is a string field only; Addressables integration belongs to a UI story
- `[SerializeReference]` inspector tooling — editor UX is out of scope for Foundation layer

---

## QA Test Cases

*Test file*: `tests/EditMode/ItemDatabase/ItemDatabase_Core_tests.cs`

**Fixture note**: All tests use `ScriptableObject.CreateInstance<ItemDefinition>()` to build test records; `Initialize(IEnumerable<ItemDefinition> records)` is the test-friendly initialization overload.

- **AC-1**: GetItem(ItemID.Invalid) returns null silently
  - Given: Database initialized with one record (ItemID(1))
  - When: `GetItem(ItemID.Invalid)`
  - Then: Return value is `null`; no exception; no error logged

- **AC-2**: Reference equality across two callers
  - Given: Database initialized with ItemID(1)
  - When: `var a = db.GetItem(new ItemID(1)); var b = db.GetItem(new ItemID(1));`
  - Then: `object.ReferenceEquals(a, b) == true`
  - Edge case: Same check with a second distinct ItemID — no cross-ID aliasing

- **AC-19**: Unknown ItemCategory → empty list + error
  - Given: Database initialized
  - When: `GetItemsByCategory((ItemCategory)255)`
  - Then: Returns empty list (`Count == 0`); dev error logged; no exception

- **AC-28**: GetItem pre-init → null + error
  - Given: ItemDatabase constructed, Initialize() not called
  - When: `GetItem(new ItemID(1))`
  - Then: Returns null; `IsReady == false`; dev error logged; no exception

- **AC-29a**: IsReady after Initialize()
  - Given: Database constructed (IsReady == false)
  - When: `Initialize(records)` with at least one record
  - Then: `IsReady == true`

- **AC-29b**: OnDatabaseReady fires exactly once
  - Given: `int callCount = 0; db.OnDatabaseReady += () => callCount++;`
  - When: `Initialize()` called; then `Initialize()` called again
  - Then: `callCount == 1`

- **AC-29c**: Late-subscriber invoked synchronously
  - Given: `Initialize()` already completed (`IsReady == true`)
  - When: `bool fired = false; db.OnDatabaseReady += () => fired = true;`
  - Then: `fired == true` on the next line after `+=` (synchronous, before next statement)

- **AC-36**: TryGetItem happy path
  - Given: Database initialized, ItemID(1) registered
  - When: `bool found = db.TryGetItem(new ItemID(1), out var def);`
  - Then: `found == true`; `object.ReferenceEquals(def, db.GetItem(new ItemID(1))) == true`

- **AC-37**: TryGetItem invalid → false + null
  - Given: Database initialized
  - When: `bool found = db.TryGetItem(ItemID.Invalid, out var def);`
  - Then: `found == false`; `def == null`; no exception

- **AC-38**: TryGetItem pre-init → false + null + error
  - Given: Not initialized
  - When: `bool found = db.TryGetItem(new ItemID(1), out var def);`
  - Then: `found == false`; `def == null`; `IsReady == false`; dev error logged; no exception

- **AC-40**: GetItemsByCategory pre-init → empty + error
  - Given: Not initialized
  - When: `GetItemsByCategory(ItemCategory.Equipment)`
  - Then: Returns empty list; `IsReady == false`; dev error logged; no exception

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ItemDatabase/ItemDatabase_Core_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 through Story 007 in Character Stats epic must be Done (ItemID and StatID are defined there)
- Unlocks: Story 002 (Validator uses ItemDefinition from this story), Story 003, Story 004
