# Story 002: Drop Roll, Equipment Cache and Tier Classification

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-1 (Drop Roll Architecture, PRNG seeding), CR-LT-2 (Drop Pool Initialization), CR-LT-5 (Drop Tier Classification)
**Requirement**: `TR-loot-001`, `TR-loot-002`, `TR-loot-005`
*(Placeholder IDs — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection.
**ADR Decision Summary**: `IItemDatabase` and the PRNG are constructor-injected (Tier 1 direct calls on injected interfaces); no singleton access.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C# (`System.Random`). No post-cutoff API. `Dictionary<ItemID, …>` uses the default comparer, as existing code does (`ItemID` implements `IEquatable<ItemID>`); the codebase has no explicit ID comparer classes.

**Control Manifest Rules (Core layer)**:
- Required: dependency injection over singletons — the PRNG and `IItemDatabase` are injected
- Forbidden: shared mutable state polling between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-1** [BLOCKING]: given a loot table with three entries at `DropChance = 1.0`, `0.0`, `1.0`, the drop roll returns exactly the two `1.0` items; all three entries were evaluated (confirmed by a roll-count instrument); no entry is skipped because of an earlier entry's result.
- [ ] **AC-LT-2** [BLOCKING]: at initialization `GetItemsByCategory(ItemCategory.Equipment)` is called exactly once; a subsequent drop roll and classification do not call it again (call count stays 1).
- [ ] **AC-LT-6** [BLOCKING]: given a pending drop list with Bronze, Iron, None (Consumable), Steel and DarkSteel items, Bronze / Iron / None classify as Common and Steel / DarkSteel as Rare; classification uses the pre-indexed cache — no `GetItem` call is made.
- [ ] **CR-LT-1 seeding** (TR-loot-001): the process-level PRNG is seeded from system entropy and its seed is written to the server log; tests inject their own seeded `System.Random`.
- [ ] **Zero-drop result** (CR-LT-1; Edge Cases "If the drop roll produces zero items"): a table whose entries all miss returns an empty list — not `null`, no error.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-1, CR-LT-2, CR-LT-5.*

**Module conventions:** see Story 001 (`src/Foundation/LootTableSystem/`, namespace `IronGrind.LootTableSystem`, constants in `LootTableConstants`).

**Roll (CR-LT-1):** one draw per table entry, in entry order, every entry always evaluated; an entry drops when `roll < DropChance`. All entries are evaluated before anything else happens. Result: `List<ItemID>` of length 0..N.

**Compare in `double`.** `(float)rng.NextDouble()` can round up to exactly `1.0f`, which would make a `DropChance = 1.0` entry miss. Use `rng.NextDouble() < entry.DropChance` (the `float` chance widens to `double`), which keeps the roll in `[0, 1)`.

**Roll-count instrument (AC-LT-1):** tests pass a `System.Random` subclass that counts `NextDouble()` calls (it is `virtual`). Draw only through `NextDouble()` in the roll path so the count is exact.

**Seeding (CR-LT-1).** The GDD says "`new System.Random()` with default seeding" and also that "the seed value is written to the server startup log" — a default-seeded `System.Random` has no readable seed. Implement the intent: a factory draws an `int` seed from system entropy, logs it, and returns `new System.Random(seed)`. One instance per process; no per-zone or per-mob re-seeding. The roll code itself only ever receives an injected `System.Random`.

**Equipment cache (CR-LT-2):** on initialization call `IItemDatabase.GetItemsByCategory(ItemCategory.Equipment)` once and index it as `Dictionary<ItemID, ItemDefinition>`. Never call it again.

**Classification (CR-LT-5):** look the `ItemID` up in the cache.
- In the cache with `EquipmentData.GearTier` `Steel` or `DarkSteel` → **Rare**.
- In the cache with `Bronze` or `Iron` → **Common**.
- Not in the cache → **Common**. The cache holds Equipment only, so a Consumable is a cache miss; the GDD describes these as `GearTier.None`. Do not call `GetItem` to confirm (AC-LT-6 forbids it).

Expose classification as a small enum (`DropTier.Common` / `DropTier.Rare`).

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 003**: who owns the kill (party tag).
- **Story 004**: `ResolveMobDrop`, gold, and wiring the roll to a kill event; the `tierShift` parameter.
- **Stories 005–006, 010**: what happens to Common and Rare drops after classification.
- F-LT-4 (expected drops) — informational only, not evaluated at runtime.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/LootTableSystem/LootTable_DropRoll_tests.cs`

- **AC-LT-1**: independent per-entry rolls
  - Given: a table with entries `(A, 1.0)`, `(B, 0.0)`, `(C, 1.0)`; a counting `System.Random`
  - When: the roll runs once
  - Then: the result is exactly `[A, C]`; the PRNG's `NextDouble()` count is 3
  - Edge cases: entries `(A, 0.0)`, `(B, 1.0)` → result `[B]`, count 2 (an early miss does not stop evaluation); an empty table → empty result, count 0

- **AC-LT-2**: cache built once
  - Given: a fake `IItemDatabase` that counts `GetItemsByCategory` calls
  - When: the system initializes, then runs a roll and classifies an item
  - Then: the call count is 1 after initialization and still 1 afterwards
  - Edge cases: none

- **AC-LT-6**: tier classification without `GetItem`
  - Given: a cache containing a Bronze, an Iron, a Steel and a DarkSteel equipment item; a potion `ItemID` that is not in the cache; a fake `IItemDatabase` that counts `GetItem` / `TryGetItem` calls
  - When: each of the five `ItemID`s is classified
  - Then: Bronze, Iron and the potion → `Common`; Steel and DarkSteel → `Rare`; the `GetItem` / `TryGetItem` count is 0
  - Edge cases: none

- **CR-LT-1 seeding**
  - Given: the seed factory
  - When: called
  - Then: it returns a seed and a `System.Random`; a second `new System.Random(seed)` produces the same first 5 `NextDouble()` values; the seed appears in a logged message (`LogAssert.Expect` on the log line)
  - Edge cases: none (no assertion on the seed's value — it is entropy)

- **Zero-drop result**
  - Given: a table with entries `(A, 0.0)`, `(B, 0.0)`
  - When: the roll runs
  - Then: the result is a non-null empty list
  - Edge cases: none

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LootTableSystem/LootTable_DropRoll_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (table definitions).
- Unlocks: Story 004.
