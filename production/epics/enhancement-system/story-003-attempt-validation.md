# Story 003: Attempt Validation and Result Codes

> **Epic**: Enhancement System
> **Status**: Ready
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

- [ ] **Result codes**: `EnhancementResultCode` has `Success`, `Destruction`, `RejectedAtMaxLevel`, `RejectedTierMismatch`, `RejectedAccessoryType`, `RejectedItemNotFound`, `RejectedConcurrentAttempt`, `RejectedNoNPCSession`, `RejectedScrollNotFound`, `RejectedNotUpgradeable` (UI-ENH-2).
- [ ] **AC-ENH-27**: no active NPC session for the player → `RejectedNoNPCSession`.
- [ ] **AC-ENH-22 / AC-ENH-4**: the item slot is empty (item removed, or equipped since selection) → `RejectedItemNotFound`. An equipped item cannot be addressed: the request names a bag slot only.
- [ ] **Locked slot**: the item slot is locked → `RejectedConcurrentAttempt`.
- [ ] **AC-ENH-37**: the item's `IsUpgradeable` is false → `RejectedNotUpgradeable`.
- [ ] **AC-ENH-5**: the item's `GearSlot` is `Ring` or `Necklace` → `RejectedAccessoryType`.
- [ ] **AC-ENH-14**: the item is at `MAX_ENHANCEMENT_LEVEL` → `RejectedAtMaxLevel`.
- [ ] **Scroll absent**: the scroll slot is empty, or holds an item whose `ScrollData` is `null` → `RejectedScrollNotFound`.
- [ ] **AC-ENH-3**: the scroll's `TargetGearTier` differs from the item's `GearTier` → `RejectedTierMismatch`.
- [ ] **Valid request**: an upgradeable non-accessory item below the maximum level, unlocked, with a matching-tier scroll and an active NPC session, passes validation.
- [ ] **No state change on any rejection**: after each rejection the item slot, the scroll slot (quantity included) and both lock flags are exactly as before, and no `OnInventoryChanged` event fired.

---

## Implementation Notes

- New `EnhancementService` in `src/Foundation/EnhancementSystem/`, constructed with `IInventoryService`, `IItemDatabase`, `EnhancementConfig` and an NPC session query. This story adds the validation step only; Story 004 adds the rest of `ConfirmEnhancement(CharacterID, int itemSlotIndex, int scrollSlotIndex)`.
- **Check order** — the order CR-ENH-15 step 2 lists them: NPC session → item exists at `itemSlotIndex` → slot unlocked / no attempt in progress → `IsUpgradeable` → not an accessory → below the maximum level → scroll exists at `scrollSlotIndex` → tier match. First failure wins.
- **NPC session query is consumer-side**: declare a small interface in this module (e.g. `INpcInteractionSessions.IsActive(CharacterID): bool`). Story 006 provides the implementation; tests here use a stub. This is the pattern the Loot Table module uses for `IPartyService`.
- **Reads only**: `IInventoryService.GetSlot`, `IsSlotLocked`, `IItemDatabase.TryGetItem`. An item is an Enhancement Scroll iff its `ItemDefinition.ScrollData` is non-null (item-database.md Rule 13). The item's tier and gear slot come from `ItemDefinition.EquipmentData`.
- **Cases the GDD does not name a code for** — raise at `/story-readiness` rather than choosing silently:
  - an out-of-range `itemSlotIndex` or `scrollSlotIndex`, or the same index for both;
  - a non-equipment item in the item slot (e.g. a potion — it has no `EquipmentData`; `IsUpgradeable` is presumably false, which would give `RejectedNotUpgradeable`);
  - an item whose definition the Item Database cannot resolve;
  - an unregistered character.
- "Another attempt in progress" is part of the same check as "slot locked" in the GDD; the per-character in-progress flag is added by Story 004 and its rejection test lives in Story 005 (it needs a commit that is still in flight).
- Validation must not log an error for a normal player-caused rejection (tier mismatch, max level and so on are expected outcomes, not caller bugs).

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
- **No event** — every rejection case: `OnInventoryChanged` count is 0.
- **Valid request** — passes validation (asserted through the seam Story 004 leaves; in this story a validation-only entry point returning `Success`-eligible is enough).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (config — maximum level); Inventory System Story 010 (Complete); Item Database Stories 005–006 (Complete — `ScrollData`, scroll records)
- Unlocks: Story 004
