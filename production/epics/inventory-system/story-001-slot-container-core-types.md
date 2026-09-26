# Story 001: Slot Container, Core Types & Read API

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 1 (Slot Structure), F-INV-3 (Full-Inventory Gate), States and Transitions, Interactions table (Inventory UI / Consumable Use read surface)
**Requirement**: `TR-inv-001`, `TR-inv-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (for `InventoryChangedEvent` only). Slot storage itself: none — pure server-side data structure, LOW engine risk (architecture.md Engine Knowledge Gap Summary).
**ADR Decision Summary**: ADR-010 — cross-system point-to-point calls use Tier 1 direct interface injection; one-producer/many-subscriber notifications use Tier 2 `event Action<T>` with a `readonly struct` argument and zero per-emit allocation on the 20Hz server tick path.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C# — no `UnityEngine` API needed. EditMode-testable. IL2CPP: avoid generic `Enum.GetValues<T>()` (project convention — use the non-generic overload if enum iteration is needed).

**Control Manifest Rules (Core layer)**:
- Required: Tier 2 broadcast events are `event Action<T>` with `readonly struct` T; name prefixed `On` (e.g. `OnInventoryChanged`) — ADR-010
- Required: Subscribe in constructor/`Initialize()`, unsubscribe in `Dispose()` (server) — ADR-010
- Forbidden: lambda captures for persistent event subscriptions — ADR-010
- Forbidden: boxing struct event args (`Action<object>`) on the server tick path — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **Rule 1.1**: A newly created character inventory has exactly 20 slots (indices 0–19); every slot reads `ItemID.Invalid` with `Quantity = 0`. `INVENTORY_SLOT_COUNT = 20` is a named constant (tuning knob), not a scattered literal.
- [ ] **Rule 1 / TR-inv-001**: `GetSlot(characterId, slotIndex)` returns `(ItemID, Quantity)` for indices 0–19; an out-of-range index (`< 0` or `≥ 20`) is rejected without throwing on the server path (returns a failure/empty result and logs an error).
- [ ] **F-INV-3**: `IsFull(characterId)` is `true` iff all 20 slots have `Quantity > 0`; `FilledSlots` counts slots with `Quantity > 0`.
- [ ] **Rule 8.23**: `HasFreeSlot(characterId)` is `true` iff at least one slot is `ItemID.Invalid, Quantity = 0`.
- [ ] **Rule 5.14**: `IsSlotLocked(characterId, slotIndex)` exists and returns `false` for every slot of a new inventory (lock mutation is Story 004).
- [ ] **Consumable Use read surface**: `HasItem(characterId, itemId)` is `true` iff any slot holds that `ItemID` with `Quantity > 0`.
- [ ] **InventoryChangedEvent contract**: an `OnInventoryChanged` Tier 2 event exists whose argument is a `readonly struct` (`InventoryChangedEventArgs`) carrying `CharacterID` and `Count` `SlotChange { SlotIndex, ItemId, Quantity }` entries (quantity 0 = slot became empty), readable via indexer and allocation-free `foreach`. Mutating the inventory from inside a subscriber throws `InvalidOperationException`, and dispatch state is always reset afterwards. This story defines the types and the emit helper; mutation stories (002, 004–008) fire it.

---

## Implementation Notes

- Location: `src/Foundation/InventorySystem/` (same assembly as the Leveling System, also Core layer — see `production/epics/index.md` note on layer classification). Namespace `IronGrind.InventorySystem`.
- Service shape: follow `CurrencySystem`'s pattern — one service (`InventorySystem : IInventoryService`) keyed by `CharacterID` (`src/Foundation/Currency/CharacterID.cs`), holding per-character slot state. Other systems depend on `IInventoryService` (Tier 1 injection), never the concrete class.
- Empty slot = `ItemID.Invalid` (`src/Foundation/CharacterStats/ItemID.cs` — `None` and `Invalid` are aliases for `uint(0)`) with `Quantity = 0`. Never write a valid `ItemID` with `Quantity = 0` (phantom slot — GDD Edge Cases).
- Test seam: an `internal` (InternalsVisibleTo `IronGrind.Foundation.EditModeTests`) seeding method to set arbitrary slot contents — required by AC-INV-5/10/13 in later stories ("seeded inventory state via test harness injection").
- **Event payload shape — DECIDED 2026-09-25 (`/story-readiness`, `unity-specialist` consult):** a `readonly struct` wrapping a **reused, service-owned 20-entry buffer** + count, with a struct enumerator and an explicit re-entrancy guard. Satisfies ADR-010 Decision 3 (zero per-emit allocation, no boxing). Sketch:

  ```csharp
  public readonly struct SlotChange
  {
      public readonly int SlotIndex;
      public readonly ItemID ItemId;
      public readonly int Quantity; // 0 = slot became empty
      public SlotChange(int slotIndex, ItemID itemId, int quantity)
          { SlotIndex = slotIndex; ItemId = itemId; Quantity = quantity; }
  }

  public readonly struct InventoryChangedEventArgs
  {
      public readonly CharacterID CharacterID;
      private readonly SlotChange[] _buffer; // owned by InventorySystem, capacity INVENTORY_SLOT_COUNT
      public readonly int Count;

      internal InventoryChangedEventArgs(CharacterID characterId, SlotChange[] buffer, int count)
          { CharacterID = characterId; _buffer = buffer; Count = count; }

      public SlotChange this[int i] => _buffer[i];   // bounds-check i < Count
      public Enumerator GetEnumerator() => new Enumerator(this);

      public struct Enumerator
      {
          private readonly InventoryChangedEventArgs _args;
          private int _index;
          internal Enumerator(InventoryChangedEventArgs args) { _args = args; _index = -1; }
          public SlotChange Current => _args._buffer[_index];
          public bool MoveNext() => ++_index < _args.Count;
      }
  }

  // InventorySystem side
  private readonly SlotChange[] _changeBuffer = new SlotChange[INVENTORY_SLOT_COUNT];
  private int _changeCount;
  private bool _isDispatching;

  private void EmitInventoryChanged(CharacterID characterId)
  {
      if (_isDispatching)
          throw new InvalidOperationException(
              "Inventory mutated synchronously from an OnInventoryChanged subscriber.");
      _isDispatching = true;
      try { OnInventoryChanged?.Invoke(new InventoryChangedEventArgs(characterId, _changeBuffer, _changeCount)); }
      finally { _isDispatching = false; _changeCount = 0; }
  }
  ```

  - **Contract (document on the type):** the args are valid **only during the synchronous dispatch**. Subscribers that keep data (network sync, persistence dirty-tracking) must copy the `SlotChange` values out — never retain the args or the buffer reference.
  - Subscribers iterate with `foreach (var change in args)` — duck-typed enumerator, no `IEnumerable<T>` boxing, no allocation.
  - **Re-entrancy:** mutating the inventory from inside an `OnInventoryChanged` subscriber is a programming error and throws `InvalidOperationException` (fails loud rather than corrupting the shared buffer). The `finally` always clears the flag and resets the count, even if a subscriber throws. Note: a subscriber exception propagates to the mutating caller (e.g. `Pickup`) — subscribers must not throw on normal paths.
  - Rejected: fixed 20-inline-entry struct (copies ~240 bytes per subscriber for a typical 1–2 change event); one event per slot (tears atomic multi-slot mutations such as merge/swap into separate events); `ReadOnlySpan<T>` member (a ref struct cannot be an `Action<T>` argument — the same CS8175-family constraint already hit in this repo).
- **Performance:** zero per-emit heap allocation (ADR-010). The 20-entry change buffer is allocated once per service; all read APIs (`GetSlot`, `IsFull`, `HasFreeSlot`, `HasItem`, `IsSlotLocked`) are O(20) scans with no allocation. No frame-budget impact expected — server-side, ≤ 20 array reads per call.

---

## Out of Scope

- Story 002: pickup; Story 003: bag-full notification; Story 004: lock mutation; Story 005: discard; Story 006: move; Story 007: Equipment interface; Story 008: sell/consume; Story 009: persistence snapshot
- Wire-protocol codecs for any inventory message (Networking)
- Inventory UI rendering (future Inventory UI epic)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_SlotContainer_tests.cs`

- **Rule 1.1 — new inventory is 20 empty slots**
  - Given: a new character inventory
  - When: `GetSlot` is read for indices 0–19
  - Then: every slot is `ItemID.Invalid`, `Quantity = 0`; slot count constant == 20
  - Edge cases: index 19 valid; index 20 and -1 rejected without exception, no state change
- **F-INV-3 — IsFull / FilledSlots**
  - Given: seeded inventories with 0, 19, and 20 occupied slots
  - Then: `IsFull` = false, false, true; `FilledSlots` = 0, 19, 20
  - Edge cases: 20 occupied where some are partial consumable stacks → `IsFull` still true (F-INV-3 note: IsFull is only the quick-rejection gate)
- **HasFreeSlot**
  - Given: 19 occupied + 1 empty (at index 0, then at index 19 in a second case) → `true`; 20 occupied → `false`
- **IsSlotLocked default** — all 20 slots `false` on a new inventory
- **HasItem**
  - Given: HP Potion in slot 7 → `HasItem(HPPotion)` true; `HasItem(BronzeSword)` false; empty inventory → false
- **InventoryChangedEvent shape**
  - Given: a subscriber attached via a named method (no lambda capture)
  - When: the internal emit helper fires with 2 changes
  - Then: the subscriber receives `CharacterID`, `Count == 2`, both `{slotIndex, itemId, quantity}` entries in order
  - Edge cases: an empty-slot change reports `itemId == 0`, `quantity == 0`; iterating with `foreach` yields the same entries as the indexer
- **InventoryChangedEvent re-entrancy guard**
  - Given: a subscriber that calls the internal emit helper (or any mutation) from inside its `OnInventoryChanged` handler
  - When: an emit fires
  - Then: `InvalidOperationException` is thrown to the caller
  - Edge cases: after the exception, a fresh emit with 1 change dispatches normally (`Count == 1`, not stale) — proves the `finally` reset the dispatch flag and change count

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_SlotContainer_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Item Database epic (Complete) — `ItemID`, `IItemDatabase`; Currency epic (Complete) — `CharacterID`
- Unlocks: Stories 002–009
