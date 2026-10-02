# Story 008: Bag-Full Recovery — Blocked Notice, In-Radius Retry, Expiry Warning

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-13.2 (Discard Modal on Proximity Fail), CR-LT-13.3 (Expiry Warning)
**Requirement**: `TR-loot-009`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: The loot service subscribes to the Inventory System's Tier 2 `OnInventoryChanged` event and raises its own Tier 2 events for the network layer, which sends `BagFullPickupBlocked` and `GroundItemExpiryWarning`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API. Tick arithmetic on `uint` server ticks.

**Performance**: Per server tick, per `Assigned` item: one extra subtraction and compare for the expiry warning. The retry check runs only for blocked items, and only on a tick after some inventory changed. The inventory event handler does one set insert and nothing else. No allocation on an idle tick. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

**Control Manifest Rules (Core layer)**:
- Required: subscribe in the constructor / `Initialize()`, unsubscribe in `Dispose()`; server-side event subscribers implement `IDisposable` — ADR-010
- Required: `event Action<T>` with `readonly struct` T, `On` prefix — ADR-010
- Forbidden: lambda captures for persistent subscriptions (subscribe a named method) — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-22** [ADVISORY]: a common drop is assigned to Character 42, whose bag is full (20/20); when 42 enters `PICKUP_RADIUS_UNITS`, the pickup fails and the server raises the bag-full-blocked notice for 42 within 1 server tick, carrying the correct `groundItemID`, `itemID`, `displayName` and `remainingTicks`.
- [ ] **AC-LT-23** [ADVISORY]: Character 42 has received the blocked notice and remains inside the radius; 42 discards one item, producing an `InventoryChangedEvent` with at least one free slot → the server retries pickup `(42, itemID, 1)` within 1 server tick of that event, with no movement required, and delivers the item on success.
- [ ] **AC-LT-9** [BLOCKING] *(in-radius retry part)*: same behaviour as AC-LT-23 — a character already inside the radius who frees a slot gets the retry without leaving and re-entering.
- [ ] **AC-LT-24** [ADVISORY] *(first half)*: a ground item assigned to Character 42 has `expiryTick` 601 ticks ahead; when the server advances 1 tick (600 remaining = `EXPIRY_WARNING_TICKS`), the expiry warning is raised for 42 exactly once.
- [ ] **CR-LT-13.2 exit**: if the character leaves the radius before freeing a slot, the in-radius retry no longer applies; retry-on-re-entry (Story 007) applies instead.
- [ ] **CR-LT-13.3 suppression**: no warning is raised for an item that is in `Claiming` or a terminal state when the threshold tick is crossed.

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-13.2 / CR-LT-13.3.*

**Module conventions:** see Story 001.

**Events (payloads from the GDD):**
- `OnBagFullPickupBlocked` — `groundItemId`, `itemId`, `displayName`, `remainingTicks`, plus the assigned `CharacterID` as recipient. Raised when a proximity pickup fails while the assignee is inside the radius. `displayName` comes from the equipment cache, or `IItemDatabase.GetItem` for a consumable. `remainingTicks = expiryTick − currentTick`.
- `OnGroundItemExpiryWarning` — same fields.

**The retry cannot run inside the inventory event.** `IInventoryService` throws `InvalidOperationException` if a subscriber mutates the inventory synchronously from `OnInventoryChanged`. So the handler only records "character X's inventory changed"; the retry runs on the loot service's next `Tick`. "Within 1 server tick" (AC-LT-23) is still met.

**Retry rule (per `Tick`):** for each item that is `Assigned`, blocked (its last pickup failed) and whose assignee is still inside the radius: if the assignee's inventory changed since the last tick **and** `IInventoryService.HasFreeSlot(assignee)` is `true`, call `Pickup(assignee, itemId, 1)` again. Success → `Inventory`; failure → stays blocked.

**Leaving the radius** clears the item's "blocked, in radius" flag. Nothing is sent to the client — the GDD says the modal is dismissed on exit, which the client does from its own position.

**Expiry warning:** in `Tick`, for each `Assigned` item, when `expiryTick − currentTick == EXPIRY_WARNING_TICKS` (600; `LootTableConstants`), raise `OnGroundItemExpiryWarning` once. Equality on the tick makes it fire once per deadline by construction; an item in `Claiming` or a terminal state at that tick gets no warning.

**Subscription hygiene:** the loot service subscribes a named method to `OnInventoryChanged` and unsubscribes in `Dispose()`.

**Settled at readiness (2026-10-02):**
- **Where it lives:** in `GroundItemService` (Story 007 decision). It becomes `IDisposable`; Story 012 (zone teardown) is where `Dispose` gets called. `IGroundItemService` gains the two events.
- **Retry gate (user decision): free slot only, as CR-LT-13.2 says** — the assignee's inventory changed since the last tick **and** `HasFreeSlot(assignee)` is `true`. Known gap, recorded for TD-048: `Pickup` fills partial stacks before empty slots, so a full bag can gain room for a stackable drop without a slot becoming free (the player consumes one unit of that item); that case gets no in-radius retry — walking out and back in still works.
- **Order inside `Tick`:** (1) `Spawning` → `Assigned`; (2) proximity entries and in-radius retries, then the pickup attempts; (3) the expiry warning; (4) expiry. The set of "inventory changed" characters is consumed and cleared by each `Tick`.
- **Blocked notice:** raised when a proximity-entry pickup fails with `PickupFailReason.InventoryFull` — no other reason raises it. A failed in-radius retry raises no second notice (the modal is already showing). No notice is raised when the item expires on that same tick.
- **Blocked flag:** set by an `InventoryFull` failure while inside the radius; cleared when the assignee leaves the radius (or has no position).
- **Expiry warning:** for `Assigned` items only, after the pickup step, on the tick where `unchecked(expiryTick − currentTick) == EXPIRY_WARNING_TICKS`. Equality is the GDD's wording; if the zone loop ever skipped a tick, that warning would be missed.
- **`displayName`:** `GroundItemService` gains an injected `IItemDatabase` and reads `DisplayName` through `TryGetItem` when it raises either notice (equipment and consumables alike). An unknown item gives an empty string. Truncating to the wire's 24 UTF-8 bytes is the codec's job.
- **The inventory handler** only adds the event's `CharacterID` to a set. It must never throw: an exception there would propagate into the Inventory System's mutating caller (TD-049).
- **Tests** use the real `InventoryService` with a seeded Item Database, as the QA section says. The existing fixtures keep the shared `RecordingInventoryService` fake (its events accept subscriptions and never fire).

**Added at code review (2026-10-02, user pre-approved "fix all"; no item changed a gameplay rule):**
- **Re-entrancy defect fixed.** The retries ran before the entry pickups while the entry list was still in a shared scratch list; a `Tick` re-entered from inside a retry's `Pickup` cleared it, and those entries were lost until the assignee left and came back. `Tick` now takes both lists into arrays before any `HasFreeSlot` / `Pickup` call.
- **Name lookup hardened.** `IItemDatabase.TryGetItem` is no longer called while the records are being iterated, and a lookup that throws gives an empty name (exception logged) without aborting the tick.
- `IGroundItemService` now extends `IDisposable`, so Story 012 can dispose it through the interface. After `Dispose` the service must not be ticked: it would still expire items but no longer sees inventory changes.
- **The retry gate is proven on the recording fake**, which gained a settable `HasFreeSlot` answer and a way to raise `OnInventoryChanged`: with the real inventory a retry that fails on a full bag looks exactly like no retry. The fake also covers the failed-retry path (a free slot is reported, the pickup fails again → blocked stays, no second notice, a later change retries again), which the real inventory cannot produce.
- `Claiming` suppression of the warning is tested through a `Tick` re-entered on the threshold tick; `Claiming` is unreachable any other way.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 007**: the proximity trigger and the base drop-fate rule.
- **Story 009**: extending `expiryTick` on background, and the second warning at the new deadline (second half of AC-LT-24).
- The discard modal, HUD indicator and their visuals/audio — client UI; no acceptance criteria in this GDD and no UX spec yet.
- Wire encoding of `BagFullPickupBlocked` / `GroundItemExpiryWarning` — the network layer subscribes to the events.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_BagFullRecovery_integration_tests.cs` (real `InventoryService` with a seeded Item Database; settable position provider)

- **AC-LT-22**: blocked notice
  - Given: an item assigned to 42; 42's bag is 20/20
  - When: 42 enters the radius and `Tick` runs
  - Then: exactly one `OnBagFullPickupBlocked` for 42 with the item's `groundItemId`, `itemId`, its Item Database `DisplayName`, and `remainingTicks = expiryTick − currentTick`
  - Edge cases: a successful pickup raises no blocked notice

- **AC-LT-23 / AC-LT-9**: in-radius retry
  - Given: the blocked state above; 42 stays inside the radius
  - When: 42 discards one slot (`IInventoryService.Discard`), then `Tick` runs once
  - Then: a second `Pickup(42, itemID, 1)` succeeds on that tick; the record is gone; no exception was thrown from the inventory event
  - Edge cases: an inventory change that frees no slot (e.g. a move between two occupied slots) → no retry; a different character's inventory change → no retry

- **CR-LT-13.2 exit**
  - Given: the blocked state; 42 leaves the radius, then discards a slot
  - When: `Tick` runs
  - Then: no `Pickup` call until 42 re-enters the radius
  - Edge cases: none

- **AC-LT-24 (first half)**: expiry warning
  - Given: an `Assigned` item whose `expiryTick` is 601 ticks ahead of the current tick
  - When: `Tick` advances 1 tick
  - Then: exactly one `OnGroundItemExpiryWarning` for the assignee with `remainingTicks = 600`
  - Edge cases: further ticks raise no second warning; an item delivered before the threshold raises none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_BagFullRecovery_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 28 test methods (28 NUnit cases), all 6 criteria covered

---

## Dependencies

- Depends on: Story 007 (proximity pickup, blocked state). Inventory System Stories 001–005 (`OnInventoryChanged`, `HasFreeSlot`, `Discard`) are Complete.
- Unlocks: Story 009.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 6/6 passing (0 deferred). AC-LT-22 to AC-LT-24 say "verified via server outbound message log": the tests verify the C# events the network layer will subscribe to — no wire message exists yet (codecs are outside this epic). "Within 1 server tick" is met by construction: the retry runs on the tick after the inventory change
**Deviations**: Advisory only — `TR-loot-009` is not in the TR registry (same accepted gap as Stories 001–007); the retry gate follows CR-LT-13.2 literally (free slot only, user decision) — a full bag that gains room for a stackable drop without a free slot gets no in-radius retry; rules settled at readiness that the GDD does not state: no blocked notice for a failed retry or on the expiry tick, the warning is raised for `Assigned` items only and on exact tick equality (a skipped tick would miss it); 12 of the 28 tests were added at the code review, beyond the written QA cases
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_BagFullRecovery_integration_tests.cs` (28 test methods, 28 NUnit cases; 22 against the real `InventoryService` with a seeded item database, 6 on the shared recording fake where calls must be counted). Full EditMode suite 1320/1320 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `GroundItemService.cs` and the new files returned CHANGES REQUIRED (a re-entrant `Tick` during a retry lost the outer tick's entry pickups; unguarded name lookup inside the record iteration; the retry gate not proven by the tests; three weaker assertions); all applied; suite re-run green; fixes not re-reviewed. The regression test for the re-entrancy defect was not run against the unfixed code
**Tech debt logged**: TD-048 extended (stackable-room gap of the retry gate; notice and warning rules not in the GDD)
**Files outside the story's own list**: `LootTestFakes.cs` (`EmptyItemDatabase`; `RecordingInventoryService` gained `FreeSlot`, `HasFreeSlotCalls`, `RaiseInventoryChanged`); `new GroundItemService(...)` calls in the lifecycle, round-robin and proximity-pickup test files; `IGroundItemService` now extends `IDisposable`
**Note for Story 009**: `PauseBudgetRemaining` is on the record and untouched so far; extending `ExpiryTick` creates a new warning threshold (second half of AC-LT-24) — equality on the new deadline gives the second warning by construction, check it; the spawn event's `ExpiryTick` goes stale for clients after an extension
**Note for Story 012**: dispose `GroundItemService` through `IGroundItemService` at zone teardown and do not tick it afterwards
