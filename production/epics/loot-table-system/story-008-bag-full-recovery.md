# Story 008: Bag-Full Recovery — Blocked Notice, In-Radius Retry, Expiry Warning

> **Epic**: Loot Table System
> **Status**: Ready
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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 007 (proximity pickup, blocked state). Inventory System Stories 001–005 (`OnInventoryChanged`, `HasFreeSlot`, `Discard`) are Complete.
- Unlocks: Story 009.
