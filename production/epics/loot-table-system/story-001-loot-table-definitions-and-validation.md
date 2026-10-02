# Story 001: Loot Table Definitions and Startup Validation

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-2 (static data), F-LT-1 Notes, F-LT-4 variable range, Edge Cases ("If `floor(baseGold / N)` produces 0"), Tuning Knobs → Gold Drops constraint and cross-reference
**Requirement**: `TR-loot-002`
*(Placeholder ID — `docs/architecture/tr-registry.yaml` is empty; the requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection and events for the module; this story raises no events.
**ADR Decision Summary**: Systems are reached through injected interfaces, never singletons. This story only defines immutable data types and a validator.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#, no Unity API beyond `Debug` logging. For `Dictionary` keys, follow the existing code: ID structs (`ItemID`, `CharacterID`, `EntityID`) implement `IEquatable<T>` and dictionaries use the default comparer (e.g. `CharacterStats.cs`). entities.yaml's `MobTypeID` il2cpp_note asks for an explicit `IEqualityComparer<MobTypeID>`, but no ID struct in the codebase has one — do not introduce a new pattern here.

**Control Manifest Rules (Core layer)**:
- Required: dependency injection over singletons; naming conventions (PascalCase types, `_camelCase` private fields, `UPPER_SNAKE_CASE` constants)
- Forbidden: shared mutable state between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [x] **One table per mob type** (Overview; CR-LT-2): a `LootTableRegistry` built from a set of `(MobTypeID, LootTableDefinition)` pairs returns the definition for a registered `MobTypeID` and reports "not found" for an unregistered one. A duplicate `MobTypeID` or `MobTypeID.Invalid` (0) in the input is a validation error.
- [x] **Immutable at runtime** (CR-LT-2): after construction no public API can add, remove or change a table, an entry, a `DropChance` or a gold range.
- [x] **`GoldMin ≥ 4`** (F-LT-1 Notes; Edge Cases; Tuning Knobs constraint): a table with `GoldMin < MAX_PARTY_SIZE` (4) is a validation error. `GoldMin = 4` is valid; `GoldMin = 3` is an error.
- [x] **`GoldMax ≤ GOLD_CAP`** (Tuning Knobs cross-reference): a table with `GoldMax > 9,999,999` is a validation error; `GoldMax = 9,999,999` is valid.
- [x] **Non-empty gold range** (derived from F-LT-1: `baseGold` is drawn from `[GoldMin, GoldMax]`): `GoldMax < GoldMin` is a validation error; `GoldMax = GoldMin` is valid.
- [x] **`DropChance ∈ [0.0, 1.0]`** (F-LT-4 variable range): an entry with `DropChance < 0` or `> 1` is a validation error; `0.0` and `1.0` are valid.
- [x] **Validation is enforced at startup**: building a registry from a set containing any validation error fails — no registry is produced — and every error names the offending `MobTypeID` and field.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from the GDD rules above and existing code conventions.*

**Module conventions (all Loot Table stories):** code in `src/Foundation/LootTableSystem/`, namespace `IronGrind.LootTableSystem` (every system's code, including the Core-layer Inventory System, lives under `src/Foundation/`). Tuning constants go in a `LootTableConstants` static class. Logic tests in `tests/EditMode/LootTableSystem/`.

**Types:**
- `MobTypeID` — `readonly struct` wrapping `uint`, `Invalid = 0`, `IEquatable<MobTypeID>` with `==` / `!=` and `GetHashCode`, mirroring `ItemID.cs`. It is owned by enemy-ai.md, but no Enemy AI code exists; declare it here as its first consumer (same precedent as `ItemID`, which lives in `IronGrind.CharacterStats`). Note this in the doc comment.
- `LootTableEntry` — `readonly struct { ItemID ItemId; float DropChance; }`.
- `LootTableDefinition` — immutable class: `IReadOnlyList<LootTableEntry> Entries`, `int GoldMin`, `int GoldMax`. Copy the entry list on construction so the caller cannot mutate it afterwards.
- `LootTableValidator` — returns a list of issues for a set of tables; pure function, no logging of its own.
- `LootTableRegistry` — created through a factory that runs the validator and fails on any issue; exposes `bool TryGetTable(MobTypeID, out LootTableDefinition)`.

**Constants — do not duplicate literals:**
- `GOLD_CAP` is a `private const` in `src/Foundation/Currency/CurrencySystem.cs`. Expose it (make it accessible to this module) rather than re-typing `9_999_999`; that one-line Currency change is in scope for this story.
- `MAX_PARTY_SIZE = 4` already exists as `RelevanceFilter.MAX_PARTY_SIZE` (Networking). Reference it; it is the Party System's constant and should move when a Party System epic exists — note that in a comment, do not add a second literal.

**Tables are plain C# objects in this story.** The GDD says loot tables are "static data assets loaded at startup" but defines no asset format, and no mob roster exists yet. Authoring per-mob table assets is not part of this epic's current stories.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 002**: drawing rolls against `DropChance`, the equipment cache, tier classification.
- **Story 004**: the gold draw and distribution.
- Per-mob loot table assets and their loader — no mob roster exists (Enemy AI / Mob Spawning have no epics yet).
- Validating that an entry's `ItemID` exists in the Item Database, or that Enhancement Scrolls are absent from loot tables (entities.yaml notes scrolls are shop-only) — the loot GDD defines neither check.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD rules cited above. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/LootTableSystem/LootTable_DefinitionValidation_tests.cs`

- **One table per mob type**
  - Given: tables for `MobTypeID(1)` and `MobTypeID(2)`, both valid
  - When: the registry is built; `TryGetTable(MobTypeID(1))`, `TryGetTable(MobTypeID(3))`
  - Then: the first returns `true` with the authored definition; the second returns `false`
  - Edge cases: two tables with `MobTypeID(1)` → build fails, error names `MobTypeID(1)`; a table keyed `MobTypeID.Invalid` → build fails

- **Immutable at runtime**
  - Given: a definition built from a `List<LootTableEntry>`
  - When: the caller mutates its original list after construction
  - Then: `Entries` is unchanged (same count and values)
  - Edge cases: none

- **`GoldMin ≥ 4`**
  - Given: a table with `GoldMin = 3`, `GoldMax = 8`
  - When: validated
  - Then: one error naming the table and `GoldMin`
  - Edge cases: `GoldMin = 4` → no error; `GoldMin = 0` → error

- **`GoldMax ≤ GOLD_CAP`**
  - Given: `GoldMin = 4`, `GoldMax = 10,000,000`
  - Then: one error naming `GoldMax`
  - Edge cases: `GoldMax = 9,999,999` → no error

- **Non-empty gold range**
  - Given: `GoldMin = 10`, `GoldMax = 9`
  - Then: one error
  - Edge cases: `GoldMin = GoldMax = 10` → no error

- **`DropChance ∈ [0.0, 1.0]`**
  - Given: one entry with `DropChance = 1.01f`
  - Then: one error naming the entry
  - Edge cases: `-0.01f` → error; `0.0f` → no error; `1.0f` → no error

- **Enforced at startup**
  - Given: two tables, one valid, one with `GoldMin = 3`
  - When: the registry factory is called
  - Then: it fails and returns no registry; the reported issues include the `GoldMin` error
  - Edge cases: a table with two distinct errors reports both

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LootTableSystem/LootTable_DefinitionValidation_tests.cs` — must exist and pass (run in the Unity Test Runner or batch mode).

**Status**: [x] Created — 19 test methods (24 NUnit cases), all 7 criteria covered

---

## Dependencies

- Depends on: None (Item Database and Currency System epics are Complete).
- Unlocks: Story 002, Story 004.

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 7/7 passing (0 deferred)
**Deviations**: Advisory only — `TR-loot-002` is not in `tr-registry.yaml` (registry is empty; checked against the GDD directly); `LootTableDefinition`'s constructor takes `IEnumerable<LootTableEntry>` (wider than the list the story names, same behaviour); one extra type, `LootTableValidationIssue.cs` (the validator's issue type); no `LootTableConstants` class was created — this story owns no constant; the validator also rejects a null definition and treats a null table set as empty (not in the story's QA cases; tests added at code review)
**Test Evidence**: Logic — `tests/EditMode/LootTableSystem/LootTable_DefinitionValidation_tests.cs` (19 test methods, 24 NUnit cases). Full EditMode suite 1153/1153 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `LootTableValidator.cs` returned CHANGES REQUIRED (validator itself clean; `LootTableDefinition.Entries` exposed its backing array; three tests asserted less than the criteria); all 3 required changes and 6 suggestions applied; suite re-run green; fixes not re-reviewed
**Tech debt logged**: None
