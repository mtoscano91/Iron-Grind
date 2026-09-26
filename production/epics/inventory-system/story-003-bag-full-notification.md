# Story 003: Bag-Full Notification & 30-Second Dedup Window

> **Epic**: Inventory System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours (revised at readiness 2026-09-26: +ctor change and two existing test SetUp updates)

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 4 (Full Inventory and Blocked Drops)
**Requirement**: `TR-inv-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: The bag-full signal is a Tier 2 broadcast (`OnInventoryFull`, `readonly struct` arg carrying `CharacterID`) consumed by the network layer (which sends the 0-byte `InventoryFullNotification` wire message) and later the Inventory UI. Inventory does not send wire messages itself.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The 30s window is measured in **server ticks** (decided at readiness 2026-09-26, option A — matches the codebase's tick convention, `ServerTickLoop.cs` / `HeartbeatActivityTracker.cs`): the service receives an injected `Func<uint>` current-tick provider (production: `() => serverTickLoop.ServerTickNumber`; tests: a fake counter). Never `Time.time`, `DateTime.Now`/`UtcNow` or `Stopwatch` — tests stay deterministic (coding-standards: no time-dependent assertions). Expiry uses the existing wraparound-safe `StaleDiscardComparer.IsTickExpired(currentTick, expiryTick)` (boundary-inclusive).

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args, `On`-prefixed names — ADR-010
- Forbidden: lambda captures for persistent subscriptions — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [x] **AC-INV-1**: With a full inventory (20/20, no partial stack for the incoming item), a drop returns `PickupResult(fail)`, no slot is mutated, and `OnInventoryFull` fires for that character — unless within the 30-second dedup window.
- [x] **AC-INV-15**: With all 20 slots occupied, a Bronze Sword (Equipment) pickup returns `PickupResult(fail)`, no slot is mutated, and `OnInventoryFull` fires (subject to the dedup window).
- [x] **Rule 4.10 dedup**: at most one `OnInventoryFull` per character per 30-second window; further blocked pickups inside the window return fail but do not fire the event. A successful pickup resets the window (the next blocked pickup fires immediately).
- [x] **Rule 4.11 per-character isolation**: one character's full bag and dedup window never affect another character's pickups or notifications.
- [x] **Tuning**: the window is the named constant `InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS = 30 * ServerTickLoop.TICK_RATE_HZ` (= 600 ticks = 30 s), not an inline literal.
- [x] **Not a blocked drop**: pickups failing with `InvalidQuantity`, `UnknownItem` or `CharacterNotRegistered` never fire `OnInventoryFull` and never touch dedup state.
- [x] **Re-registration reset**: `RegisterCharacter` clears that character's dedup state (the next blocked pickup fires immediately).

---

## Implementation Notes

- **Event contract** (added to `IInventoryService`):
  ```csharp
  public readonly struct InventoryFullEventArgs
  {
      public readonly CharacterID CharacterID;
  }
  event Action<InventoryFullEventArgs> OnInventoryFull;
  ```
- **Constructor**: `InventoryService(IItemDatabase itemDatabase, Func<uint> currentTick)` — either null → `ArgumentNullException`. The single-argument constructor is removed. **In scope:** updating `SetUp` in `InventorySystem_SlotContainer_tests.cs` (Story 001) and `InventorySystem_AtomicPickup_tests.cs` (Story 002) to pass a fake tick provider.
- **Dedup helper (shared)**: implement as one internal method, e.g. `NotifyInventoryFull(CharacterID)`, that applies the dedup and fires the event. Story 002's `Pickup` calls it only on `PickupFailReason.InventoryFull` (Rule 3 Step 3), after the fail decision (nothing was written). **Story 007's `ForceInsert` MUST reuse this same helper** — one bag-full policy for the whole system (resolves Story 007's open dedup question).
- Per-character state: `Dictionary<CharacterID, uint>` of the *expiry tick* of the current window (absent = no window). Fire when absent or `StaleDiscardComparer.IsTickExpired(now, expiry)`; then store `expiry = now + BAG_FULL_DEDUP_WINDOW_TICKS` (uint arithmetic wraps; the comparer is wraparound-safe). A successful `Pickup` removes the entry. `RegisterCharacter` removes the entry.
- Boundary: exactly 600 ticks after the last notification → fires (window is `[t, t+600)`).
- **Re-entrancy**: dispatch of `OnInventoryFull` sets the same `_isDispatching` guard as `OnInventoryChanged` (reset in `finally`); a subscriber that mutates the inventory synchronously gets `InvalidOperationException` propagated to the `Pickup` caller. Dedup state is updated *before* the dispatch, so a throwing subscriber still consumes the window.
- The persistent HUD bag-full indicator is UI-owned; it derives from `IsFull` (Story 001) + `OnInventoryChanged`, not from this event.
- **Performance:** one dictionary lookup (+ at most one write/remove) per failed-full or successful pickup; the event arg is a `readonly struct` — zero heap allocation on fire (ADR-010). Server-side only; no frame-budget impact.

---

## Out of Scope

- `InventoryFullNotification` wire codec and sending (Networking)
- HUD bag-full indicator and toast (future Inventory UI epic)
- Drop fate of the blocked item (Loot Table System)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs`
*(Fake tick provider injected; all times in server ticks, 20 ticks = 1 s, window = 600)*

- **AC-INV-1** — Given 20 occupied non-potion slots, tick 0; When `Pickup(HPPotion,1)`; Then `InventoryFull`, 20 slots unchanged, `OnInventoryFull` fired once with the right `CharacterID`, no `OnInventoryChanged`.
- **AC-INV-15** — Given 20 occupied slots; When `Pickup(BronzeSword,1)`; Then `InventoryFull`, no mutation, `OnInventoryFull` fired once.
- **Dedup window** — tick 0 blocked → fires; tick 200 blocked → no fire; tick 599 blocked → no fire; tick 600 blocked → fires (count 2). Each blocked call still returns `InventoryFull`.
- **Window reset** — tick 0 blocked → fires; free a slot (seed), tick 100 successful pickup; refill (seed), tick 120 blocked → fires immediately (count 2).
- **Per-character isolation** — character A blocked at tick 0 (fires) → character B blocked at tick 20 → fires for B (A's window irrelevant); A blocked at tick 40 → no fire.
- **Wraparound** — first blocked pickup at tick `uint.MaxValue − 100` → fires; tick 200 (wrapped, 301 ticks later) → no fire; tick 499 (600 later) → fires.
- **Not a blocked drop** — on a full bag: `Pickup(HPPotion, 0)` → `InvalidQuantity`; `Pickup(unregistered ItemID)` → `UnknownItem` (expect error log); `Pickup(unregistered character)` → `CharacterNotRegistered` (expect error log). None fires `OnInventoryFull`; a following real blocked pickup still fires (dedup state untouched).
- **Full bag but mergeable** — 20 occupied incl. HP Potion at 50/99 → `Pickup(HPPotion,1)` succeeds, no `OnInventoryFull` (F-INV-3 note).
- **Re-registration reset** — blocked at tick 0 (fires) → `RegisterCharacter` → re-fill (seed) → blocked at tick 10 → fires (count 2).
- **Subscriber mutation** — an `OnInventoryFull` subscriber calling `Pickup` → `InvalidOperationException` to the outer caller; no slot mutated; a later blocked pickup inside the window does not fire (window consumed).
- **Constructor** — `new InventoryService(stubDb, null)` → `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs` — must exist and pass

**Status**: [x] Created 2026-09-26 — 17 tests (13 + 4 added after code review), all passing in live Test Runner (2026-09-26)

---

## Dependencies

- Depends on: Story 002 (pickup fail path)
- Unlocks: None

---

## Completion Notes
**Completed**: 2026-09-26
**Criteria**: 7/7 passing (none deferred)
**Deviations** (advisory):
- Tech debt logged at code review: TD-041 (Inventory → Networking dependency on `StaleDiscardComparer` / `TICK_RATE_HZ`), TD-042 (per-character dictionaries never purged — no `UnregisterCharacter`).
- Implemented directly by the orchestrator instead of a delegated programmer agent.
**Test Evidence**: Logic — `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs` (17 tests) + Story 001/002 suites, all passing in live Test Runner.
**Code Review**: Complete — unity-specialist CLEAN, qa-tester TESTABLE; all suggestions applied (+4 tests, 2 tech-debt entries).
