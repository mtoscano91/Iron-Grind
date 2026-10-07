# Story 003: Attempt Validation and Result Codes

> **Epic**: Enhancement System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-2 (maximum level), CR-ENH-3 (scroll tier match), CR-ENH-4 (bag items only), CR-ENH-5 (accessory exclusion), CR-ENH-15 step 2 (validation and rejection codes), CR-ENH-17 (enforcement of the NPC session at validation), UI-ENH-2 (`EnhancementResultCode`), EC-ENH-3, EC-ENH-4, EC-ENH-7, AC-ENH-3, 4, 5, 14, 22, 27, 37.
**Requirement**: `TR-enh-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Tier 1 reads of the injected `IInventoryService` and `IItemDatabase`).
**ADR Decision Summary**: The Enhancement System calls the injected services directly and acts on their return values; validation mutates nothing.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable against the real `InventoryService` and a stub item database.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010
- Forbidden: no `EventBus` class; no class-typed event args — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story. "Rejected" always means: the code is returned, no slot is locked, no scroll is consumed, no level changes.*

- [x] **Result codes**: `EnhancementResultCode` is a `byte` enum with, in this order, `Success`, `Destruction`, `RejectedAtMaxLevel`, `RejectedTierMismatch`, `RejectedAccessoryType`, `RejectedItemNotFound`, `RejectedConcurrentAttempt`, `RejectedNoNPCSession`, `RejectedScrollNotFound`, `RejectedNotUpgradeable` (UI-ENH-2). The numeric values are provisional until the wire encoding is fixed by TD-046.
- [x] **AC-ENH-27**: no active NPC session for the player → `RejectedNoNPCSession`.
- [x] **AC-ENH-22 / AC-ENH-4**: the item slot is empty (item removed, or equipped since selection) → `RejectedItemNotFound`. An equipped item cannot be addressed: the request names a bag slot only.
- [x] **Locked slot**: the item slot is locked → `RejectedConcurrentAttempt`.
- [x] **AC-ENH-37**: the item's `IsUpgradeable` is false → `RejectedNotUpgradeable`.
- [x] **AC-ENH-5**: the item's `GearSlot` is `Ring` or `Necklace` → `RejectedAccessoryType`.
- [x] **AC-ENH-14**: the item is at `MAX_ENHANCEMENT_LEVEL` → `RejectedAtMaxLevel`.
- [x] **Scroll absent**: the scroll slot is empty, or holds an item whose `ScrollData` is `null` → `RejectedScrollNotFound`.
- [x] **AC-ENH-3**: the scroll's `TargetGearTier` differs from the item's `GearTier` → `RejectedTierMismatch`.
- [x] **Cases the GDD names no code for** *(decided 2026-10-07 at readiness)*:
  - an out-of-range `itemSlotIndex` → `RejectedItemNotFound`; an out-of-range `scrollSlotIndex` → `RejectedScrollNotFound`;
  - the same index for both slots needs no special rule — a sword there fails the scroll check (`RejectedScrollNotFound`), a scroll there fails the upgradeable check (`RejectedNotUpgradeable`);
  - a non-equipment item in the item slot (no `EquipmentData` — a potion or a scroll) → `RejectedNotUpgradeable`, even if its record says `IsUpgradeable = true`;
  - an item the Item Database cannot resolve → `RejectedItemNotFound`, with one server error logged;
  - an unregistered character → `RejectedItemNotFound` (the Inventory System's own "not registered" error is the only log).
- [x] **Valid request**: an upgradeable non-accessory item below the maximum level, unlocked, with a matching-tier scroll and an active NPC session, passes validation. `ValidateAttempt` reports it as valid and returns the item's `ItemID`, its current enhancement level and the scroll's `ItemID`.
- [x] **No state change on any rejection**: after each rejection the item slot, the scroll slot (quantity included) and both lock flags are exactly as before, and no `OnInventoryChanged` event fired.

---

## Implementation Notes

- New `EnhancementService` in `src/Foundation/EnhancementSystem/`, constructed with `IInventoryService`, `IItemDatabase`, `EnhancementConfig` and an NPC session query. This story adds the validation step only; Story 004 adds the rest of `ConfirmEnhancement(CharacterID, int itemSlotIndex, int scrollSlotIndex)`.
- **Validation entry point**: `internal EnhancementAttemptValidation ValidateAttempt(CharacterID charId, int itemSlotIndex, int scrollSlotIndex)` returning a `readonly struct` — `IsValid`; `RejectionCode` (meaningful only when not valid); and, when valid, `ItemId`, `CurrentLevel` and `ScrollItemId`. It is internal (the test assembly can see internals); the public `ConfirmEnhancement` arrives with Story 004 and calls it. Story 005's rollback needs those three values captured before the bag changes, so they are part of the result from the start. (The type name is a suggestion; the fields are not.)
- **Check order** *(decided 2026-10-07 at readiness)*: NPC session → **an attempt already in progress for this player** → item exists at `itemSlotIndex` → slot unlocked → `IsUpgradeable` (and has `EquipmentData`) → not an accessory → below the maximum level → scroll exists at `scrollSlotIndex` → tier match. First failure wins. The GDD lists the in-progress check last but gives it the same code as "slot locked" (`RejectedConcurrentAttempt`); it is checked second here because it is per player, not per slot, and while a commit is in flight every later answer could change when it lands. The in-progress flag itself is added by Story 004 — in this story leave the hook in place (it is always false) so the order is fixed.
- **Range-check both slot indices before any inventory read.** `IInventoryService.GetSlot` and `IsSlotLocked` log a server error for an out-of-range index; an out-of-range index from a client is an ordinary rejection here and must not produce that log. Valid range: `[0, InventoryConstants.INVENTORY_SLOT_COUNT)`.
- **NPC session query is consumer-side**: declare a small interface in this module (e.g. `INpcInteractionSessions.IsActive(CharacterID): bool`). Story 006 provides the implementation; tests here use a stub. This is the pattern the Loot Table module uses for `IPartyService`.
- **Reads only**: `IInventoryService.GetSlot`, `IsSlotLocked`, `IItemDatabase.TryGetItem`. An item is an Enhancement Scroll iff its `ItemDefinition.ScrollData` is non-null (item-database.md Rule 13). The item's tier and gear slot come from `ItemDefinition.EquipmentData`.
- **Not upgradeable** means `IsUpgradeable == false` **or** `EquipmentData == null`. The Item Database validator has no rule tying `IsUpgradeable` to equipment, so a consumable wrongly marked upgradeable must not reach the gear-slot or tier checks (they read `EquipmentData`).
- **Unresolvable item** (`TryGetItem` fails for the `ItemID` in the item slot, including when the database is not ready): `RejectedItemNotFound` and one `Debug.LogError` naming the character, the slot and the `ItemID` — the bag holds an item the database does not know. An unresolvable item in the *scroll* slot is `RejectedScrollNotFound` with the same kind of error.
- **Unregistered character**: `GetSlot` returns an empty slot and logs its own error, so the result is `RejectedItemNotFound`; do not add a second log.
- The rejection test for "another attempt in progress" lives in Story 005 (it needs a commit that is still in flight).
- Validation must not log an error for a normal player-caused rejection (tier mismatch, max level, out-of-range index and so on are expected outcomes, not caller bugs). The only error logs are the two cases above.

---

## Out of Scope

- Story 004: lock, scroll consumption, the draw, applying the outcome
- Story 005: commit and rollback; `RejectedConcurrentAttempt` for an attempt already in progress (AC-ENH-8)
- Story 006: opening, closing and expiring the NPC session
- Story 010: the wire request and the result message

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` (new). Real `InventoryService`, stub item database with: a Bronze sword (upgradeable), an Iron sword, a Bronze ring, a non-upgradeable Bronze item, a Bronze and an Iron Enhancement Scroll, an HP potion.

- **AC-ENH-27** — session stub inactive; valid item and scroll → `RejectedNoNPCSession`; nothing changed.
- **AC-ENH-22** — item slot 3 emptied before the call → `RejectedItemNotFound`; scroll quantity unchanged.
- **AC-ENH-4** — slot 0 empty (the sword it held is "equipped"), scroll in slot 1; `ConfirmEnhancement(0, 1)` → `RejectedItemNotFound`; no slot locked.
- **Locked slot** — item slot locked beforehand → `RejectedConcurrentAttempt`; the lock is still set afterwards.
- **AC-ENH-37** — non-upgradeable item → `RejectedNotUpgradeable`; slot never locked.
- **AC-ENH-5** — ring → `RejectedAccessoryType`; scroll remains.
- **AC-ENH-14** — sword seeded at level 10 → `RejectedAtMaxLevel`; scroll remains; level still 10.
- **Scroll slot empty** → `RejectedScrollNotFound`. **Scroll slot holds a potion** → `RejectedScrollNotFound`.
- **AC-ENH-3** — Bronze sword with an Iron scroll → `RejectedTierMismatch`; Iron scroll quantity unchanged; level unchanged.
- **Check order** — an inactive session and an empty item slot together → `RejectedNoNPCSession`; a ring at the maximum level → `RejectedAccessoryType`; a max-level sword with a mismatched scroll → `RejectedAtMaxLevel`.
- **Out-of-range indices** — item slot −1 and 20 → `RejectedItemNotFound`; scroll slot −1 and 20 (with a valid item) → `RejectedScrollNotFound`; no error logged in any of the four.
- **Same slot twice** — `ValidateAttempt(0, 0)` with a sword in slot 0 → `RejectedScrollNotFound`; with a Bronze scroll in slot 0 → `RejectedNotUpgradeable`.
- **Non-equipment item** — an HP potion in the item slot → `RejectedNotUpgradeable`; a consumable record with `IsUpgradeable = true` and no `EquipmentData` → `RejectedNotUpgradeable`, no exception.
- **Unresolvable item** — the item slot holds an `ItemID` the stub database does not know → `RejectedItemNotFound`, one error logged. Same for the scroll slot → `RejectedScrollNotFound`, one error logged.
- **Unregistered character** — session stub active for an unregistered character → `RejectedItemNotFound`; exactly one error logged (the Inventory System's).
- **Enum** — `EnhancementResultCode` has the ten members in the listed order with underlying type `byte`.
- **No event** — every rejection case: `OnInventoryChanged` count is 0.
- **No log on ordinary rejections** — every case above that is not marked "error logged" ends with `LogAssert.NoUnexpectedReceived()`.
- **Valid request** — `ValidateAttempt` returns `IsValid = true` with the sword's `ItemID`, its seeded level (e.g. 2) and the scroll's `ItemID`; nothing in the bag changed and no slot is locked.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 31 test methods + 6 parameterised cases (37 cases), passing (EditMode 1676/1676, Unity 6000.3.10f1 batch mode, 2026-10-07)

---

## Dependencies

- Depends on: Story 001 (config — maximum level); Inventory System Story 010 (Complete); Item Database Stories 005–006 (Complete — `ScrollData`, scroll records)
- Unlocks: Story 004

---

## Completion Notes
**Completed**: 2026-10-07
**Criteria**: 12/12 passing (none deferred)
**Deviations**: None blocking. Notes: the "attempt already in progress" check is in place second in the order but `IsAttemptInProgress` always returns false until Story 004 adds the flag (its rejection test is in Story 005); `EnhancementAttemptValidation` is `internal` — this story fixed its fields, not its visibility, and only the internal `ValidateAttempt` returns it.
**Test Evidence**: Integration — `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` (37 cases, real `InventoryService`); EditMode 1676/1676 in Unity 6000.3.10f1 batch mode.
**Code Review**: Complete — /code-review APPROVED WITH SUGGESTIONS; all three applied (result struct made `internal`, class summary wording, above-cap test). LP-CODE-REVIEW / QL-TEST-COVERAGE skipped (lean mode).
**Tech debt**: None logged.
**Files**: `src/Foundation/EnhancementSystem/EnhancementService.cs`, `EnhancementResultCode.cs`, `EnhancementAttemptValidation.cs`, `INpcInteractionSessions.cs`.
**For Story 004**: `ValidateAttempt` returns the item's `ItemID`, its level and the scroll's `ItemID`; helpers return `EnhancementResultCode.Success` to mean "check passed". A locked *scroll* slot is not a validation failure — `ConsumeItem` decides at step 4.
