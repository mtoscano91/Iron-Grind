# Story 002: Atomic Pickup Resolution & Stack Limits

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 4–5 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 2 (Stack Limits), Rule 3 (Pickup Resolution — Atomic), Rule 10 (Within-Tick Ordering), F-INV-2 (Stack Overflow on Pickup), Pickup Edge Cases
**Requirement**: `TR-inv-002`, `TR-inv-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (`OnInventoryChanged` emission). Pickup algorithm: none — design-only, LOW risk.
**ADR Decision Summary**: Tier 1 — Loot Table System calls `IInventoryService.Pickup(...)` directly and gets a result back; Tier 2 — `OnInventoryChanged` (`readonly struct` arg) fires once per successful mutation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable. Item definitions in tests via `ItemDefinition.SetForTesting` (existing `#if UNITY_EDITOR` seam) + a real or stub `IItemDatabase`.

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args, `On`-prefixed event names — ADR-010
- Forbidden: per-emit heap allocation / boxing on the server tick path — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [x] **AC-INV-2**: Slot 2 holds 80 HP Potions (StackLimit=99), slot 5 is empty, all other slots occupied. A pickup of 30 HP Potions succeeds; one `InventoryChangedEvent` contains exactly two entries: slot 2 → quantity 99, slot 5 → quantity 11 (F-INV-2: Overflow = max(0, 80+30−99) = 11; FIFO by slot index).
- [x] **AC-INV-5**: All 20 slots occupied, exactly two slots each hold 98 HP Potions (StackLimit=99). A pickup of 3 HP Potions fails atomically: `PickupResult(fail)`, both potion slots still hold 98 (the Step 1 partial fills are rolled back), no `InventoryChangedEvent` fires.
- [x] **AC-INV-6**: One slot holds a Bronze Sword (Equipment, StackLimit=1) and at least one empty slot exists. A pickup of a second Bronze Sword places it in a new empty slot; the original slot still holds exactly 1 Bronze Sword.
- [x] **AC-INV-12**: Two pickups of different ItemIDs processed in a defined order (same tick) — the first occupies the lowest-index available slot, the second the next available slot. Verified via each `InventoryChangedEvent`'s slot index.
- [x] **AC-INV-13**: Empty slots at 3, 7, 15 only; no partial stacks of the incoming ItemID. A pickup of HP Potions lands in slot 3.
- [x] **Rule 3.7 Step 2 (multi-slot placement)**: when the remainder exceeds `StackLimit`, it fills successive empty slots in ascending order (e.g. StackLimit=10, pickup of 25 into an empty bag → slots 0/1/2 = 10/10/5), all reported in one event.
- [x] **Edge — IncomingQty = 0**: rejected immediately, no slot mutation, no event, no phantom `(ItemID, 0)` slot.
- [x] **Edge — consumable with StackLimit = 1**: each unit occupies its own slot, identical to equipment.
- [x] **PickupResult fail reasons**: each failure path returns its specific `PickupFailReason` — full bag → `InventoryFull`; qty ≤ 0 → `InvalidQuantity`; unknown/Invalid item or database not ready → `UnknownItem`; unregistered character → `CharacterNotRegistered`. Success returns `Reason == None`. No failure mutates any slot or fires `OnInventoryChanged`.
- [x] **Story 001 regression**: all Story 001 tests still pass after the constructor change.

---

## Implementation Notes

*Derived from GDD Rule 3 (no ADR governs the algorithm):*

- **Signature**: `PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity)` on `IInventoryService` (GDD Interactions table). `quantity = 1` for all MVP loot drops; the algorithm handles any positive quantity.
- **`PickupResult`** — new `readonly struct` (mirrors `GoldMutationResult`):
  ```csharp
  public enum PickupFailReason : byte
  {
      None = 0,                // success
      InventoryFull = 1,       // Step 3 — remainder > 0 after Step 2 (the ONLY reason Story 003 notifies on)
      InvalidQuantity = 2,     // quantity <= 0
      UnknownItem = 3,         // ItemID.Invalid, not in Item Database, or database not ready
      CharacterNotRegistered = 4,
  }
  public readonly struct PickupResult
  {
      public readonly bool Success;
      public readonly PickupFailReason Reason; // None iff Success
  }
  ```
  Story 003 fires `InventoryFullNotification` only for `Reason == InventoryFull` — invalid requests must never trigger bag-full UI.
- Algorithm (single atomic transaction):
  1. Read `StackLimit` via `IItemDatabase.TryGetItem(itemId, out var def)`.
  2. **Step 1** — scan slots 0→19; for each slot with the same `ItemID` and `Quantity < StackLimit`, plan `min(remainder, StackLimit − Quantity)` units (F-INV-2). Items with StackLimit=1 skip Step 1.
  3. **Step 2** — scan 0→19 for empty slots; plan `min(remainder, StackLimit)` per empty slot until remainder = 0.
  4. **Step 3** — if remainder > 0 after Step 2, fail with **no writes at all**. Implement as plan-then-commit (compute all target slots first, write only on success) rather than write-then-rollback, so a failed pickup can never leave a partial state or fire an event.
- **Item Database injection**: `InventoryService` gains a constructor `InventoryService(IItemDatabase itemDatabase)` (Tier 1, ADR-010; null → `ArgumentNullException`). The parameterless constructor is removed. **In scope:** updating Story 001's `InventorySystem_SlotContainer_tests.cs` `SetUp` to pass a stub. Look up `StackLimit` via `IItemDatabase.TryGetItem`.
- **Test stub**: add `tests/EditMode/InventorySystem/StubItemDatabase.cs` — a minimal `IItemDatabase` backed by a `Dictionary<ItemID, ItemDefinition>`, `IsReady` settable, `OnDatabaseReady` unused. Test items (HP Potion StackLimit=99, Bronze Sword StackLimit=1, a StackLimit=10 consumable, a StackLimit=1 consumable) are built with `ScriptableObject.CreateInstance<ItemDefinition>()` + the existing `ItemDefinition.SetForTesting` seam (`#if UNITY_EDITOR` — fine for EditMode) and destroyed in `TearDown` (`Object.DestroyImmediate`).
- **Story 001 mutation-seam contract (binding)**: `Pickup` calls `ThrowIfDispatching()` first; performs all validation and the full plan (Steps 1–3) with no writes; only on success writes the planned slots, calls `RecordSlotChange(characterId, slot, itemId, newQty)` once per planned slot in ascending slot order, then `EmitInventoryChanged(characterId)` once. Because planning never writes, a failed pickup never records — `DiscardPendingChanges()` is not needed on any `Pickup` path.
- **Guard order and fail reasons** (first match wins; no mutation, no event on any failure):
  1. `quantity <= 0` → `InvalidQuantity` (no log — caller-validatable, but also not a server fault).
  2. `characterId` not registered → `Debug.LogError` + `CharacterNotRegistered`.
  3. `itemId == ItemID.Invalid`, `!itemDatabase.IsReady`, or `TryGetItem` fails → `Debug.LogError` + `UnknownItem`.
  3b. *(Added during implementation, 2026-09-26 code review)* the definition's `StackLimit < 1` (data error) → `Debug.LogError` + `UnknownItem` — never `InventoryFull`, so bad data cannot trigger bag-full UI.
  4. Plan leaves remainder > 0 → `InventoryFull` (no log — normal gameplay outcome).
  Quantities above 99 are **accepted**: the plan is bounded by `StackLimit` per slot and the 20-slot capacity, so an oversized pickup either spans slots or fails with `InventoryFull` atomically.
- **Locked slots (Rule 5.12)**: add the guard in this story — Step 1 skips any slot where the lock flag is set (a locked partial stack receives no units). Step 2 is unaffected (an empty slot cannot be locked). Not testable until Story 004 adds lock mutation; Story 004 owns the test.
- Rule 10 FIFO: the service processes calls in the order received; no reordering or priority logic — AC-INV-12 is proven by sequential calls.
- Notification side effects (`InventoryFullNotification`, 30s dedup) are Story 003 — this story only returns `PickupResult(fail)`.
- **Performance:** zero heap allocation on the pickup path — the plan is held in a fixed-size service-owned structure (e.g. an `int[InventoryConstants.INVENTORY_SLOT_COUNT]` of planned quantities), not a `List<T>` per call. At most 2 × 20 slot scans + one `TryGetItem` dictionary lookup per call; no frame-budget impact expected (server-side, ≤ 40 array reads).

---

## Out of Scope

- Story 003: `InventoryFullNotification` + dedup window on failed pickups
- Story 004: lock state (see guard note above)
- Loot Table System's drop-fate handling on fail (Loot Table GDD CR-LT-13)
- Changes to Story 001 code beyond the constructor signature (Story 001 test `SetUp` update IS in scope)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_AtomicPickup_tests.cs`

- **AC-INV-2** — Given the seeded state above; When `Pickup(HPPotion, 30)`; Then result success, one event with entries `[{2, HPPotion, 99}, {5, HPPotion, 11}]` in that order; `GetSlot(2)`=99, `GetSlot(5)`=11. Edge: pickup of exactly 19 → only slot 2 changes (99), slot 5 stays empty, event has 1 entry.
- **AC-INV-5** — Given all 20 occupied with two 98-potion slots; When `Pickup(HPPotion, 3)`; Then fail, both slots 98, zero events fired (subscriber call count 0), all 20 slots byte-identical before/after. Edge: `Pickup(HPPotion, 2)` in the same state succeeds (98→99, 98→99).
- **AC-INV-6** — Given sword in slot 0, rest empty; When `Pickup(BronzeSword, 1)`; Then slot 1 = sword qty 1, slot 0 unchanged (qty 1). Edge: sword never merges even though slot 0 has the same ItemID.
- **AC-INV-12** — Given empty bag; When `Pickup(HPPotion,1)` then `Pickup(BronzeSword,1)`; Then first event slotIndex 0, second event slotIndex 1. Edge: reverse call order reverses slot assignment.
- **AC-INV-13** — Given empties at 3/7/15; When `Pickup(HPPotion, 1)`; Then event `slotIndex: 3`, slots 7 and 15 still empty.
- **Multi-slot placement** — StackLimit=10 test consumable, empty bag, `Pickup(25)` → slots 0/1/2 = 10/10/5 in one event. Edge: same pickup with only 2 empty slots → fail, no mutation.
- **Edge — qty 0** — `Pickup(HPPotion, 0)` → fail, no event, all slots unchanged.
- **Edge — consumable StackLimit=1** — two pickups of 1 → two separate slots, each qty 1.
- **Fail reasons** — `Pickup(qty -1)` → `InvalidQuantity`; `Pickup(ItemID.Invalid, 1)` → `UnknownItem` + error log; unregistered ItemID → `UnknownItem` + error log; stub `IsReady=false` → `UnknownItem` + error log; unregistered character → `CharacterNotRegistered` + error log. Each: zero events, all slots unchanged.
- **Oversized quantity** — StackLimit=99 potion, empty bag, `Pickup(150)` → slots 0/1 = 99/51, one event with 2 entries.
- **Pickup inside subscriber** — an `OnInventoryChanged` subscriber that calls `Pickup` → `InvalidOperationException` to the outer caller (re-entrancy guard applies to the real mutator).
- **Constructor** — `new InventoryService(null)` → `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_AtomicPickup_tests.cs` — must exist and pass

**Status**: [x] Created 2026-09-26 — 28 cases (23 + 5 added after code review), all passing in live Test Runner (2026-09-26)

---

## Dependencies

- Depends on: Story 001 (slot container, event type, seeding seam)
- Unlocks: Story 003

---

## Completion Notes
**Completed**: 2026-09-26
**Criteria**: 10/10 passing (none deferred)
**Deviations** (advisory, no tech debt logged):
- Added guard 3b: `StackLimit < 1` → `UnknownItem` + error log (never `InventoryFull`), tested.
- Commit loop records each slot change before writing it; no-mid-loop-throw invariants documented on `CommitPickupPlan`.
- `PickupResult.Fail` asserts `reason != None` (code review suggestion).
- Rule 5.12 locked-slot guard in Step 1 is implemented but untested until Story 004 (planned).
- Implemented directly by the orchestrator instead of a delegated programmer agent.
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_AtomicPickup_tests.cs` (28 cases) + Story 001 regression suite, all passing in live Test Runner.
**Code Review**: Complete — unity-specialist CLEAN, qa-tester TESTABLE; all 6 suggestions applied (Pickup split into helpers, `#nullable enable`, invariant comment, story note, Fail assert, +5 tests).
