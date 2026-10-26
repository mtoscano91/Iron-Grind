# Story 006: Accessory Merge

> **Epic**: Equipment System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-14 (Accessory Merge), F-EQS-5 (accessory scale, `MAX_ACCESSORY_LEVEL = 10`), UI Requirements (`MergeResult` codes)
**Requirement**: `TR-equip-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Tier 1 calls into the injected Inventory service (`HasFreeSlot`, `GetSlot`, `IsSlotLocked`, `MoveItemOut`, `ForceInsert`), acting on each return value.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (this layer)**:
- Required: Tier 1 calls; server-side validation in `IronGrind.ServerLogic` (ADR-012)
- Forbidden: `async` in tick-driven core code (ADR-011)
- Guardrail: all-or-nothing — every precondition runs before the first `MoveItemOut`

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [x] **AC-EQS-21** — three identical +1 rings at three slots, a free slot → `Success`; the three removed; exactly one `MergeResultItemID` added at level 0; bag item count −2
- [x] **AC-EQS-22** — three +10 rings (`MergeResultItemID = null`) → `RejectedMaxLevel`; no state change
- [x] **AC-EQS-23** — full bag → `RejectedInventoryFull`; no `MoveItemOut` call (`HasFreeSlot` runs before any removal)
- [x] **AC-EQS-24** — result `ForceInsert` fails, rollback `ForceInsert × 3` succeeds → `RejectedMergeError`; all three restored; no CriticalError log; no equipment slot touched
- [x] **AC-EQS-26** — result `ForceInsert` fails and ≥ 1 rollback `ForceInsert` fails → `CriticalRollbackFailed`; a CriticalError log containing the entity id and the unrestored count (≥ 1); no equipment slot touched
- [x] **AC-EQS-27** [ADVISORY] — the internal equipped-item consistency check (injectable) reports a source item as equipped → `RejectedEquipped`; no `MoveItemOut`
- [x] **AC-EQS-34** — preconditions 0–2: indices in `[0, 19]` and pairwise distinct, the three slots hold the same `ItemID`, none is locked, the item's `GearSlot` is Ring or Necklace; otherwise `RejectedInvalidSources` with no state change and no `MoveItemOut`
- [x] Merge never reads or writes an equipment slot; `IsSlotTransitioning` is unaffected

---

## Implementation Notes

*Derived from CR-EQS-14:*

- `RequestMerge(CharacterID, int slotIdx1, int slotIdx2, int slotIdx3)`. Precondition order: indices in `[0, 19]` and pairwise distinct → `GetSlot` ×3 same non-Invalid `ItemID`, none `IsSlotLocked` → item is equipment with `GearSlot` Ring / Necklace → defense-in-depth equipped check → `MergeResultItemID != null` → `HasFreeSlot`.
- Execution: `MoveItemOut` ×3 → `ForceInsert(MergeResultItemID, 0)` → on false, `ForceInsert(sourceItemId, 0)` ×3 (attempt all three even after one fails, count the failures) → `RejectedMergeError` or `CriticalRollbackFailed` with `Debug.LogError("Merge rollback incomplete — {count} source item(s) not restored for entity {entityID}")`. "Alert monitoring" = that log line (no alert service exists; same convention as Character Persistence).
- A `MoveItemOut` failing during execution cannot happen after the lock check on a single-threaded tick; if it does, restore the ones already removed and return `RejectedMergeError` — document it as defence.
- **Defense-in-depth equipped check (precondition 1):** make it an injectable seam (e.g. an internal `Func`-free interface or a protected virtual) so AC-EQS-27 can force it; the production check compares each source `ItemID` against the character's equipped entries **only when** it would mean the same physical item — under the current model an `ItemID` in the bag and the same `ItemID` equipped are two items (GDD: "a player with 4 identical accessories, 1 equipped and 3 in inventory, may merge the 3"), so the production implementation returns "not equipped". Record that in the doc comment.
- **`RejectedInvalidSources` (resolved at `/story-readiness` 2026-10-26, user decision):** append `RejectedInvalidSources = 6` to `src/ServerLogic/EquipmentSystem/MergeResult.cs` (values 0–5 unchanged; doc comment like the others). Every precondition 0–2 failure (out-of-range or repeated index, empty slot, differing `ItemID`, locked slot, non-accessory) returns it; `RejectedEquipped` stays the defense-in-depth code only. GDD CR-EQS-14 (new precondition 0), UI Requirements and AC-EQS-34 amended the same day.
- No wire message for merge exists in `networking-wire-protocol.md`; the entry point is server-side only in this epic.

---

## Out of Scope

- A client request / wire message for merge (not in the wire protocol yet)
- Inventory UI merge-button disabling at +10 (Inventory UI GDD)

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AccessoryMerge_tests.cs` (new). Inventory decorator to fail chosen `ForceInsert` calls and count `MoveItemOut`.

- **AC-EQS-21** — three +1 rings in slots 2, 5, 9, slot 0 free → `Success`; slots 2/5/9 empty; one +2 ring at level 0 in the lowest free slot; `FilledSlots` −2.
- **AC-EQS-22** — three +10 rings → `RejectedMaxLevel`; all three still in place; 0 `MoveItemOut`.
- **AC-EQS-23** — three +1 rings, bag otherwise full → `RejectedInventoryFull`; 0 `MoveItemOut`.
- **AC-EQS-24** — result `ForceInsert` fails → `RejectedMergeError`; three source rings back in the bag at level 0; `LogAssert.NoUnexpectedReceived`; Ring and Necklace equipment slots unchanged.
- **AC-EQS-26** — result insert fails and the second rollback insert fails → `CriticalRollbackFailed`; one `LogError` containing the entity id and "1"; the other two rollback inserts were still attempted.
- **AC-EQS-27** — consistency seam reports equipped → `RejectedEquipped`; 0 `MoveItemOut`.
- **AC-EQS-34 preconditions** — one ring different `ItemID`; one slot locked; three identical swords (not accessories); an empty slot; a repeated index (2, 2, 5); an index 20; a negative index → each `RejectedInvalidSources`, 0 `MoveItemOut`, bag unchanged.
- **Four identical, one equipped** — Ring slot holds the same `ItemID`; three in the bag → `Success`; the equipped ring untouched.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_AccessoryMerge_tests.cs` — must exist and pass

**Status**: [x] Created — 19 tests + 9 TestCase rows (28 cases), all passing 2026-10-26

---

## Dependencies

- Depends on: Story 002 (service, registration and Inventory wiring); independent of Stories 003–005
- Unlocks: None

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 8/8 passing, all covered by `EquipmentSystem_AccessoryMerge_tests.cs` (no deferred items)
**Deviations** (advisory, accepted):
1. An unregistered character logs a server error and returns `RejectedInvalidSources` (the GDD names no code for this caller bug).
2. Not a GDD rule: a `MoveItemOut` failing during execution (impossible after the lock check on a single-threaded tick) restores the sources already removed → `RejectedMergeError`, or `CriticalRollbackFailed` with the CR-EQS-14 critical log if a restore fails.
**Test Evidence**: Logic — `tests/EditMode/EquipmentSystem/EquipmentSystem_AccessoryMerge_tests.cs`; Equipment System filter 204/204 passed. `EquipmentSystem_SlotRegistry_tests.cs` `MergeResult` member pin updated for `RejectedInvalidSources` (outside the story file list, valid knock-on).
**Code Review**: Complete — 2026-10-26, APPROVED WITH SUGGESTIONS (unity-specialist CLEAN, qa-tester GAPS, all advisory; ADR-010/011/012 compliant) → "fix all" applied (`RequestMerge` split into precondition helpers, rollback position comment, discriminating AC-EQS-21 slot check, new restore-fails defence test). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
