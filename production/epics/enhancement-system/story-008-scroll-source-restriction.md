# Story 008: Scroll Source Restriction Scan

> **Epic**: Enhancement System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 1 hour

## Context

**GDD**: `design/gdd/enhancement-system.md` — AC-ENH-24 (Enhancement Scrolls have no monster loot table entry; they are sold only by the NPC Shop), F-ENH-5 economy validation (the scroll economy is the primary gold sink).
**Requirement**: none in the epic's TR table — this story covers one acceptance criterion directly (AC-ENH-24)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: None — design-only (a data invariant check; no runtime behaviour).
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (Feature layer)**:
- N/A — test-only story; no new runtime code is expected.

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [ ] **AC-ENH-24**: an automated scan of every monster loot table finds zero entries that can yield an Enhancement Scroll.
- [ ] **The scan identifies scrolls by data, not by name**: an item is an Enhancement Scroll iff its `ItemDefinition.ScrollData` is non-null (item-database.md Rule 13); the four MVP scroll records are found that way.
- [ ] **The scan fails when it should**: given a loot table that does include a scroll, the same check reports it.

---

## Implementation Notes

- This is a guard test. It adds no production code unless the Loot Table module has no way to enumerate what a table can drop — in that case the smallest read-only accessor is acceptable; say so in the completion notes.
- **Confirm the data source at `/story-readiness`.** The Loot Table code (`LootTableDefinition`, `LootTableEntry`, `LootTableRegistry`, `LootTableValidator` in `src/Foundation/LootTableSystem/`) defines the table shape, and equipment drops are resolved through a cached `IItemDatabase.GetItemsByCategory` lookup (loot-table-system.md CR-LT-2). Check two things before writing the test:
  - whether production loot tables are authored anywhere yet — if only test-local tables exist, the scan has nothing real to scan, and the story should assert the structural rule instead (next point) and be re-run when mob loot data lands;
  - how a Consumable can enter a table. If a table entry can name a category or pool rather than an `ItemID`, a scroll is a Consumable (`ItemCategory.Consumable` with `ScrollData`) and could be drawn from a consumable pool without any entry naming it. The scan must cover that path, not only literal `ItemID` matches.
- If a structural rule is needed (for example "the loot validator rejects a table that can yield an item with `ScrollData`"), that is a Loot Table rule change — it needs a line in `loot-table-system.md` first. Do not add it under this story without that.
- The NPC Shop half of the criterion ("appear only in NPC Shop purchase records") cannot be checked: no shop inventory data exists in code. Note it as deferred to the NPC Shop epic.

---

## Out of Scope

- NPC Shop epic: scroll prices (TK-ENH-9) and the shop's stock list
- Loot Table System: any change to how tables are authored or validated
- Story 003: recognising a scroll at attempt validation

---

## QA Test Cases

**File**: `tests/EditMode/EnhancementSystem/EnhancementSystem_ScrollSourceRestriction_tests.cs` (new)

- **AC-ENH-24** — enumerate every registered monster loot table and every item each can yield; assert none has `ScrollData != null`. The failure message names the table and the scroll.
- **Scrolls are identified by data** — the MVP item records contain exactly four items with `ScrollData`, one per gear tier (Bronze, Iron, Steel, Dark Steel).
- **Negative control** — a test-local table that includes a scroll makes the same scan report one violation.
- **Consumable path** — if tables can draw from a consumable pool, a test-local table using that pool is scanned and the scan result reflects whether scrolls are reachable through it.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnhancementSystem/EnhancementSystem_ScrollSourceRestriction_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None within this epic. Item Database Stories 005–006 (Complete — scroll records), Loot Table System epic (Complete).
- Unlocks: None
