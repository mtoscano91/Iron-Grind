# Story 008: Scroll Exclusion Validator Rule (MVP)

> **Epic**: Enhancement System
> **Status**: Complete (2026-10-07)
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

*Rewritten 2026-10-07 at `/story-readiness`. The original story ("Scroll Source Restriction Scan", 1h) was a test-only scan of every monster loot table. Readiness found nothing real to scan — no production loot tables are authored, and a `LootTableRegistry` cannot list its tables — so the user chose to enforce the restriction as a loot table validation rule instead, and to mark it an MVP-only constraint (post-MVP, scrolls are intended to be a rare drop on some monsters: enhancement-system.md OQ-ENH-9).*

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-16 (Enhancement Scroll Exclusion at MVP), tuning knob `ALLOW_ENHANCEMENT_SCROLL_DROPS` (default `false`), AC-LT-26. `design/gdd/enhancement-system.md` — AC-ENH-24 (Scroll Source Restriction, MVP), F-ENH-5 economy validation (the scroll economy is the primary gold sink at MVP), OQ-ENH-9 (post-MVP scroll drops). `design/gdd/item-database.md` — Rule 36 (`ScrollData != null` is the test for "this item is an Enhancement Scroll").
**Requirement**: none in the epic's TR table — this story covers AC-ENH-24 / AC-LT-26 directly
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (interface dependency — the validator reads items through `IItemDatabase`, never the concrete class).
**ADR Decision Summary**: Systems are injected through interfaces and call each other directly; no central event bus.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — plain C#; no engine API involved.
**Performance**: No performance impact expected — one item lookup per loot table entry, once at server startup; nothing per tick.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From `loot-table-system.md` AC-LT-26 and `enhancement-system.md` AC-ENH-24, scoped to this story:*

- [x] **Scroll entry rejected (AC-LT-26, AC-ENH-24)**: with scroll drops not allowed, validating a table set in which one mob's table has an entry naming an Enhancement Scroll reports exactly one issue for that entry; the issue's mob type is that mob and its message names the entry index and the `ItemID`. `LootTableRegistry.TryCreate` on the same set returns false with a null registry.
- [x] **One issue per offending entry**: two scroll entries (in one table or in two tables) produce two issues.
- [x] **Scrolls are identified by data, not by name or id**: an entry is a scroll iff the item database returns a definition whose `ScrollData` is non-null (item-database.md Rule 36). A Consumable without `ScrollData` (a potion) and an Equipment item are not reported.
- [x] **Unknown item is not a scroll**: an entry whose `ItemID` is not in the item database is not reported by this rule.
- [x] **Switch on lifts the rule**: with scroll drops allowed, the same scroll-bearing set produces no issue from this rule and `TryCreate` builds the registry.
- [x] **MVP default is off**: `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS` is `false`.
- [x] **Existing rules unchanged**: every existing loot validation test passes with the new parameters supplied; a set that breaks an existing rule and also names a scroll reports both issues.

---

## Implementation Notes

- **Signature change** (`src/Foundation/LootTableSystem/`): `LootTableValidator.Validate(tables, IItemDatabase itemDatabase, bool allowEnhancementScrollDrops)` and `LootTableRegistry.TryCreate(tables, IItemDatabase itemDatabase, bool allowEnhancementScrollDrops, out registry, out issues)`. A null `itemDatabase` throws `ArgumentNullException` — do not skip the rule silently.
- **The knob**: `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS = false`, beside the module's other tuning constants. The validator takes the value as a parameter so both settings are testable; production wiring passes the constant. There is no production caller of `TryCreate` yet (no loot data loader exists) — say so in the completion notes.
- **The check**: for each entry, `itemDatabase.TryGetItem(entry.ItemId, out var item)`; when it returns true and `item.ScrollData != null`, add a `LootTableValidationIssue` in the existing message style, e.g. `Entries[{e}] ({ItemId}) is an Enhancement Scroll; scrolls cannot be dropped while ALLOW_ENHANCEMENT_SCROLL_DROPS is false (CR-LT-16).` Run it inside the existing per-entry loop, after the `DropChance` check, so issue order stays table by table, entry by entry.
- **Existing call sites**: 13 test call sites pass the new arguments (`LootTable_DefinitionValidation_tests.cs` ×10, `LootTable_KillResolution_integration_tests.cs`, `LootTable_RoundRobin_integration_tests.cs`, `LootTable_GroundItemLifecycle_tests.cs`). Use the item database each fixture already has, or an empty `StubItemDatabase` where it has none; pass `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS`.
- **Not part of this rule**: an entry naming an unknown `ItemID` stays legal for the validator, as today. Whether that should be an error is a separate Loot Table question — do not add it here.
- **Post-MVP**: turning the switch on is gated on enhancement-system.md OQ-ENH-9 (economy re-validation, a rare-drop classification for scrolls). This story adds no classification logic.

---

## Out of Scope

- Any loot data loader or production loot tables (none exist yet)
- Validating that an entry's `ItemID` exists in the item database
- A drop classification or drop rate for scrolls (post-MVP, OQ-ENH-9)
- NPC Shop epic: scroll prices (TK-ENH-9) and the shop's stock list — the "appear only in NPC Shop purchase records" half of AC-ENH-24 cannot be checked until shop data exists
- Story 003: recognising a scroll at attempt validation

---

## QA Test Cases

**File**: `tests/EditMode/LootTableSystem/LootTable_ScrollExclusion_tests.cs` (new). `StubItemDatabase` with a Bronze sword, a potion and a Bronze Enhancement Scroll built with `ItemDefinitionBuilder`; table sets built in the test.

- **Scroll entry rejected** — one table, entries [sword, scroll], switch off → exactly one issue; mob type matches; message contains the entry index (1) and the scroll's `ItemID`; `TryCreate` → false, registry null, same single issue.
- **One issue per entry** — two scroll entries in one table → two issues with indices 0 and 1; one scroll entry in each of two tables → two issues, one per mob type.
- **Identified by data** — potion entry (Consumable, no `ScrollData`) and sword entry → no issue; a scroll record with an unrelated display name and id → reported.
- **Unknown item** — entry naming an `ItemID` absent from the database → no issue from this rule.
- **Switch on** — the scroll-bearing set with the switch on → zero issues; `TryCreate` → true and `TryGetTable` returns the table.
- **Default** — `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS` is false.
- **Combined with an existing rule** — a table with `GoldMin` below the party size and a scroll entry → both issues reported.
- **Null item database** — `Validate` and `TryCreate` throw `ArgumentNullException`.
- **Regression** — the existing Loot Table validation, kill resolution, round-robin and ground item suites pass unchanged in behaviour.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LootTableSystem/LootTable_ScrollExclusion_tests.cs` — must exist and pass.

**Status**: [x] Created — 16 tests, EditMode 1817/1817 (2026-10-07)

---

## Dependencies

- Depends on: None within this epic. Item Database Stories 005–006 (Complete — scroll records, `ScrollData`), Loot Table Story 001 (Complete — `LootTableValidator`, `LootTableRegistry`).
- **Gate**: lean `/design-review design/gdd/loot-table-system.md` of the CR-LT-16 amendment — done 2026-10-07 (1 blocking item, the duplicate AC-LT-25 ID; fixed, revision accepted, GDD Approved). Gate closed.
- Unlocks: None

---

## Completion Notes

**Completed**: 2026-10-07
**Criteria**: 7/7 passing (none deferred)
**Deviations**:
- ADVISORY — `LootTableValidator.Validate` (and so `LootTableRegistry.TryCreate`) throws `InvalidOperationException` when scroll drops are not allowed and the item database is not ready. Added at code review: a not-ready database answers "not found" for every item, which would pass every scroll entry. `loot-table-system.md` CR-LT-16 gained one sentence for it the same day.
- ADVISORY — there is no production caller of `TryCreate` yet (no loot data loader exists). The rule is enforced in tests only until a loader is written; that loader must pass `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS` and run after the item database is ready.
**Test Evidence**: Logic — `tests/EditMode/LootTableSystem/LootTable_ScrollExclusion_tests.cs` (16 tests). Full EditMode suite 1817/1817, Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — lean self-review 2026-10-07, APPROVED WITH SUGGESTIONS; S1–S5 applied (not-ready database check, real potion fixture, alias removed, `EmptyItemDatabase` reused, `ItemID` assert).
**Files**: `src/Foundation/LootTableSystem/` — `LootTableConstants.cs`, `LootTableValidator.cs`, `LootTableRegistry.cs`; tests — the new file above, and call sites in `LootTable_DefinitionValidation_tests.cs`, `LootTable_GroundItemLifecycle_tests.cs`, `LootTable_KillResolution_integration_tests.cs`, `LootTable_RoundRobin_integration_tests.cs`.
