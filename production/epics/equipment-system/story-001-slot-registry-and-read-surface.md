# Story 001: Slot Registry, Core Types and Read Surface

> **Epic**: Equipment System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/equipment-system.md` — CR-EQS-1 (slot registry), CR-EQS-9 (`IsSlotTransitioning`), CR-EQS-12 (weapon reads), UI Requirements (read surface, `EquipResult` and `MergeResult` codes), States and Transitions
**Requirement**: `TR-equip-001`, `TR-equip-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Systems talk through direct Tier 1 calls into injected services and act on return values; data crossing a boundary is a `readonly struct`; C# events are for one-producer, many-subscriber notification.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C# in `IronGrind.ServerLogic`; no post-cutoff Unity API. The registry is a fixed array indexed by `(int)GearSlot`, never a `Dictionary` keyed by an enum (IL2CPP).

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic lives in `IronGrind.ServerLogic` (ADR-012); data types crossing a boundary are `readonly struct` (ADR-010)
- Forbidden: lambda captures for persistent event subscriptions (ADR-010); a formula deciding a hidden outcome placed in `IronGrind.Foundation` (ADR-012)
- Guardrail: no per-call allocation on the read methods (they are called by Damage Calculation on every hit)

---

## Acceptance Criteria

*From GDD `design/gdd/equipment-system.md`, scoped to this story:*

- [ ] `EquipmentSlotEntry` is a `readonly struct { ItemID ItemId; bool IsTransitioning; byte EnhancementLevel }`; an empty slot is `ItemID.Invalid` with level 0 (CR-EQS-1)
- [ ] Each registered character holds a fixed array of 7 entries indexed by `(int)GearSlot` (Weapon = 0 … Necklace = 6); no `Dictionary` keyed by `GearSlot` (CR-EQS-1)
- [ ] `EquipResult` enum exists with `Success`, `NoChange`, `StatRequirementNotMet`, `InventoryFull`, `SlotMismatch`, `ItemLocked`, `ItemNotInInventory`, `InventoryError`, `CriticalFailure` (Interactions mapping table); `MergeResult` enum exists with `Success`, `RejectedEquipped`, `RejectedMaxLevel`, `RejectedInventoryFull`, `RejectedMergeError`, `CriticalRollbackFailed` (UI Requirements); `SlotState` enum `Empty`, `Occupied`, `Transitioning`
- [ ] `RegisterCharacter(CharacterID, EntityID)` creates 7 empty slots; `UnregisterCharacter(CharacterID)` drops them; a second register of the same character, or any call for an unregistered character, follows the `IInventoryService` convention (documented on the interface)
- [ ] Read surface (TR-equip-008): `GetEquipmentSlotState(charId, slot)` → `(ItemID, SlotState)`; `GetSlotEnhancementLevel(charId, slot)` → `byte` (0 when empty); `IsSlotTransitioning(charId, slot)` → `bool`; `GetEquippedWeaponItemID(charId)` → `ItemID.Invalid` when no weapon (AC-EQS-12); `GetEquippedWeaponEnhancementLevel(charId)` = `GetSlotEnhancementLevel(charId, GearSlot.Weapon)`
- [ ] `EquipmentService` implements the existing `IEquippedWeaponQuery` (`GetEquippedWeaponID(EntityID)`, `GetEquippedWeaponEnhancementLevel(EntityID)`) through the EntityID → CharacterID map kept by `RegisterCharacter`; an unknown entity returns `ItemID.Invalid` / 0 (EC-EQS-9)
- [ ] Every read method rejects a `GearSlot` value outside `[0, 6]` before indexing the array — no `IndexOutOfRangeException` (CR-EQS-13 bullet 2 applies to reads too)

---

## Implementation Notes

*Derived from ADR-010 and the GDD:*

- New folder `src/ServerLogic/EquipmentSystem/` (namespace `IronGrind.EquipmentSystem`, matching `IronGrind.EnhancementSystem`). Files: `EquipmentSlotEntry.cs`, `EquipResult.cs`, `MergeResult.cs`, `SlotState.cs`, `EquipmentSlotState.cs` (the `readonly struct (ItemID ItemId, SlotState State)` the read returns), `IEquipmentService.cs`, `EquipmentService.cs`.
- **Multi-character signatures (epic decision, 2026-10-26):** the GDD writes the entry points for one character (`Equip(GearSlot, ItemID, int)`). The server holds many characters, so every method takes `CharacterID` first, as `IInventoryService` does. Character Stats is keyed by `EntityID`, so `RegisterCharacter(CharacterID, EntityID)` records both; the service keeps `CharacterID → slots + EntityID` and `EntityID → CharacterID`.
- **Naming drift resolved here:** `damage-calculation.md` names the weapon reads `GetEquippedWeaponID(AttackerID)` / `GetEquippedWeaponEnhancementLevel(EntityID)`, and that interface (`IEquippedWeaponQuery`, returns `byte`) is already in code. The service implements it as-is; the GDD-named `GetEquippedWeaponItemID(charId)` methods sit on `IEquipmentService`. No GDD rename needed.
- Constructor takes the dependencies the later stories use (`IInventoryService`, `CharacterStats`, `IItemDatabase`, `IEnhancementBonusProvider`, `EnhancementConfig`), null-checked, so stories 002–006 add behaviour without changing the constructor. Unused fields are fine in this story.
- `IsTransitioning` is a runtime flag only; nothing in this story sets it (story 004 does).
- Doc comments on every public member (coding standards).

---

## Out of Scope

- Story 002: `Equip` / `Unequip` behaviour
- Story 003: input validation on the mutating entry points
- Story 005: `equipmentAppearanceFlags`
- Story 007: restoring slots from a character record

---

## QA Test Cases

**File**: `tests/EditMode/EquipmentSystem/EquipmentSystem_SlotRegistry_tests.cs` (new)

- **AC-EQS-12** — registered character, nothing equipped: `GetEquippedWeaponItemID` = `ItemID.Invalid`, no exception; `GetEquippedWeaponEnhancementLevel` = 0.
- **Empty registry** — for each of the 7 `GearSlot` values: state `(Invalid, Empty)`, level 0, `IsSlotTransitioning` false.
- **`IEquippedWeaponQuery`** — via the interface with the registered `EntityID`: `GetEquippedWeaponID` = Invalid, level 0. Unknown `EntityID`: Invalid / 0, no exception.
- **Out-of-range slot** — `(GearSlot)7` and `(GearSlot)255` on each read method: rejected per the documented convention, no `IndexOutOfRangeException`.
- **Unregistered character** — each read method follows the documented convention (assert the exact behaviour chosen and documented).
- **Register / unregister** — after `UnregisterCharacter`, reads behave as unregistered; the EntityID map entry is gone (`IEquippedWeaponQuery` returns Invalid).
- **Enum values** — `EquipResult`, `MergeResult`, `SlotState` member names present (reflection or exhaustive switch test).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EquipmentSystem/EquipmentSystem_SlotRegistry_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (Item Database, Inventory, Character Stats and Enhancement provider are all Complete)
- Unlocks: Stories 002, 005, 007
