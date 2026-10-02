# Story 007: Proximity Pickup and Bag-Full Drop Fate

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-7 (Common Drop Pickup), CR-LT-13 base rule (Drop Fate on Bag Full), States (`Assigned` ↔ `Claiming` → `Inventory`), Edge Cases (member disconnects with an assigned drop; all members' bags full)
**Requirement**: `TR-loot-009`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection.
**ADR Decision Summary**: The loot service calls `IInventoryService.Pickup` directly (Tier 1) and acts on the returned `PickupResult`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; distance math on `UnityEngine.Vector3` (use `sqrMagnitude` against the squared radius). No post-cutoff API.

**Performance**: Per server tick, per `Assigned` item: one position lookup and one squared-distance compare (worst case about 600 live items). No allocation on a tick where no pickup triggers and nothing expires. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

**Control Manifest Rules (Core layer)**:
- Required: Tier 1 direct calls on injected interfaces — ADR-010
- Forbidden: shared mutable state polling between systems — ADR-010 (positions are read through an injected provider, not a shared field)

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-8** [BLOCKING]: a common drop is assigned to Character 42; when Character 42 enters `PICKUP_RADIUS_UNITS` of the item, the server calls pickup for `(42, itemID, 1)` with no client input. On success the item goes to `Inventory` state. On failure (bag full) it stays `Assigned` to Character 42 only — not reassigned to any other party member.
- [ ] **AC-LT-9** [BLOCKING] *(re-entry and expiry parts)*: after a bag-full failure, Character 42 frees a slot and re-enters the pickup radius before `expiryTick` → the server retries pickup `(42, itemID, 1)` and delivers on success. If `expiryTick` is reached first, the item goes to `Despawned`, the despawn event is raised, and the drop is permanently lost.
- [ ] **AC-LT-18** [BLOCKING]: a party of 4, all with full inventories; Item X is assigned to A and Item Y to B; both proximity triggers fire and both pickups fail → X remains assigned to A only, Y to B only, and both keep their original `expiryTick`.
- [ ] **CR-LT-7 exclusivity**: a character who is not the assignee entering the radius triggers nothing.
- [ ] **Edge case — disconnect**: an assigned character with no known position triggers nothing; the item stays assigned and its timer keeps running.
- [ ] **Pickup before expiry** *(readiness decision, 2026-10-02)*: if the assignee enters the radius on the tick the item reaches `expiryTick`, the pickup is attempted first. On success the item goes to `Inventory` and no despawn event is raised; on failure the item despawns on that same tick.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-7, CR-LT-13 and the `IInventoryService` contract.*

**Module conventions:** see Story 001.

**Inventory call:** the GDD's `PickupRequest(characterID, itemID, quantity)` is `IInventoryService.Pickup(CharacterID, ItemID, int)` in code, returning `PickupResult` (`Success`, `Reason`).

**Positions.** No movement system exists in code. Declare a consumer-side `ICharacterPositionProvider` — `bool TryGetPosition(CharacterID, out Vector3)` — and inject it. `false` means the character has no position (disconnected, not in the zone).

**Proximity is edge-triggered.** CR-LT-13 says a failed pickup is retried "on re-entry", so the trigger is *entering* the radius, not *being* in it. Per item, remember whether the assignee was inside the radius on the previous tick; attempt pickup only on an outside → inside transition (an assignee already inside when the item becomes `Assigned` counts as entering).

**Radius:** `PICKUP_RADIUS_UNITS = 2.0` in `LootTableConstants` (design/registry/entities.yaml). The GDD does not say whether the boundary itself is inside; treat `distance ≤ radius` as inside and state that choice in the constant's comment.

**On trigger:** `Assigned` → `Claiming` → call `Pickup(assignee, itemId, 1)`:
- `Success` → `Inventory` (terminal; destroy the record).
- failure → back to `Assigned`, same assignee, `expiryTick` unchanged. The GDD describes the failure as bag full (`PickupFailReason.InventoryFull`); any other reason is a caller bug — also return to `Assigned`, and additionally log a server error.

`Pickup` is synchronous, so `Claiming` never outlives the call.

**Settled at readiness (2026-10-02):**
- **Where it lives (user decision):** in `GroundItemService` — its `Tick` already owns every state transition and expiry. The constructor gains `IInventoryService` and `ICharacterPositionProvider`; the record gains an "assignee was inside the radius on the previous tick" flag. `IGroundItemService` does not change. The two existing test files that construct `GroundItemService` update their constructor calls.
- **Order inside `Tick`:** (1) `Spawning` → `Assigned`; (2) proximity for `Assigned` items and the pickup attempts; (3) expiry. An assignee standing on the mob therefore gets the item on the first tick after spawn. Pickup comes before expiry (user decision).
- **Unknown position** counts as outside the radius, so a reconnect inside the radius counts as entering.
- **Re-entrancy:** `Pickup` raises `OnInventoryChanged` synchronously. Collect the items to claim during the iteration and call `Pickup` only after it — never while enumerating the records. An exception from `Pickup` is logged (`Debug.LogException`); what happens to the item was changed at code review — see below.
- **Tests** use a recording `IInventoryService` fake with a settable result ("the bag holds the item" = a recorded successful `Pickup`) and a settable position provider. `IInventoryService` is large, so both fakes live in one shared helper file, `tests/EditMode/LootTableSystem/LootTestFakes.cs`, used by this story's tests and by the two existing files that construct `GroundItemService`.

**Added at code review (2026-10-02, user: "fix all"):**
- **`Pickup` throws → the item is removed (user decision; replaces "returns to `Assigned`" above).** `InventoryService.Pickup` commits the item and then raises `OnInventoryChanged`; a subscriber exception propagates to the caller, so after an exception the item may already be in the bag. The ground item is marked `Despawned`, removed, and announced through `OnGroundItemDespawned`, with the exception and one server error logged. A drop can be lost; it can never be delivered twice. TD-049 tracks making `InventoryService` isolate subscriber exceptions.
- **Distance is 3D (user decision):** height counts; an item on another level directly above or below is out of reach. Stated on the constant and on `ICharacterPositionProvider`.
- `ICharacterPositionProvider.TryGetPosition` is called while the records are being iterated: implementations must be plain reads and must not call back into the ground item service.
- Expiry skips an item in `Claiming` (only visible to a re-entrant `Tick` from inside `Pickup`).
- The shared inventory fake throws `NotSupportedException` from every member except `Pickup`, so a later story that starts calling one fails loudly.
- **For Story 008:** the retry on `OnInventoryChanged` must not call `Pickup` from inside the handler (`InventoryService` throws `InvalidOperationException` during a dispatch). The handler should only mark the item "retry pending"; the next `Tick`'s claim step performs the pickup. Subscribe in the constructor and unsubscribe in `Dispose` (ADR-010 Decision 4). The fake's events never fire today — Story 008 needs a way to raise them.
- **For Story 010:** when an item is reassigned (CR-LT-10 fallback), reset its "assignee was inside" flag, or the new assignee's first entry is missed.

**GDD gap (not fixed here):** on a successful pickup the record is destroyed, but no message tells clients to remove the item's beacon — `GroundItemDespawned` is defined for expiry and zone teardown only (networking-wire-protocol.md). This story adds no message and no event for it.

**HUD part of AC-LT-18.** "A bag-full HUD notification is active on A's client" is driven by `BagFullPickupBlocked` (Story 008) and by the Inventory System's own `OnInventoryFull`. This story asserts the server-side state only.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 008**: `BagFullPickupBlocked`, the in-radius retry after a discard (the third part of AC-LT-9), the expiry warning.
- **Story 009**: extending `expiryTick` while backgrounded.
- **Story 011**: delivering an auction winner's item.
- A server → client message for a successful delivery — the loot GDD names none (CR-LT-15 lists spawn, assign, bid, resolve and despawn only); do not add one.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_ProximityPickup_integration_tests.cs` (real `InventoryService` with a seeded Item Database, or a recording `IInventoryService` fake; a settable `ICharacterPositionProvider` fake)

- **AC-LT-8**: auto-pickup on entry
  - Given: an item assigned to Character 42 at the origin; 42 positioned 10 units away, with a free slot
  - When: 42's position is set 1 unit from the item and `Tick` runs
  - Then: `Pickup(42, itemID, 1)` was called once; the record is gone (`Inventory`); 42's bag holds the item
  - Edge cases: 42's bag full → `Pickup` called once, the item is `Assigned` to 42; a second character moving into the radius → no `Pickup` call

- **AC-LT-9**: retry on re-entry; expiry
  - Given: the bag-full state above; 42 moves out of the radius, a slot is freed, 42 moves back in before `expiryTick`
  - When: `Tick` runs after each move
  - Then: a second `Pickup(42, itemID, 1)` call succeeds; the record is gone
  - Edge cases: 42 stays inside the radius after the failure → no second `Pickup` call from this story's logic; 42 never returns → at `expiryTick` the item despawns with one `OnGroundItemDespawned`

- **AC-LT-18**: no reassignment
  - Given: A, B, C, D all with full bags; item X assigned to A, item Y assigned to B; all four standing on both items
  - When: `Tick` runs
  - Then: exactly two `Pickup` calls — `(A, X, 1)` and `(B, Y, 1)`; X is `Assigned` to A, Y is `Assigned` to B; both `ExpiryTick` values equal their values before the tick
  - Edge cases: none

- **Disconnect**
  - Given: an item assigned to 42; the position provider returns `false` for 42
  - When: `Tick` runs up to `expiryTick − 1`
  - Then: no `Pickup` call; the item is still `Assigned` to 42
  - Edge cases: none

- **Pickup before expiry**
  - Given: an item assigned to 42, with 42 outside the radius until `expiryTick`
  - When: 42 is moved inside the radius and `Tick(expiryTick)` runs
  - Then: with a free slot — one `Pickup` call, the record is gone, no `OnGroundItemDespawned`; with a full bag — one `Pickup` call, then one `OnGroundItemDespawned` on that same tick
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_ProximityPickup_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 21 test methods (25 NUnit cases), all 6 criteria covered

---

## Dependencies

- Depends on: Story 006 (assigned ground items). Inventory System Story 002 (`Pickup`) is Complete.
- Unlocks: Story 008.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 6/6 passing (0 deferred). "The bag holds the item" is asserted as a recorded successful `Pickup` on a fake (as the story allows) — no test runs against the real `InventoryService`; the HUD part of AC-LT-18 is out of scope (Story 008 and the Inventory System)
**Deviations**: Advisory only — `TR-loot-009` is not in the TR registry (same accepted gap as Stories 001–006); user decisions not in the GDD: pickup is attempted before expiry on the same tick, a `Pickup` that throws removes the item and announces a despawn (this replaced the story's original "returns to `Assigned`" at code review — see TD-049), the pickup distance is 3D with the boundary inside; GDD gap: no message tells clients to remove the beacon after a successful pickup; 14 test methods go beyond the written QA cases (6 from implementation, 8 from the code review)
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_ProximityPickup_integration_tests.cs` (21 test methods, 25 NUnit cases; real `GroundItemService`, shared `RecordingInventoryService` and `SettablePositionProvider` fakes). Full EditMode suite 1292/1292 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `GroundItemService.cs` and the new files returned CHANGES REQUIRED (a thrown `Pickup` could lead to a second delivery; two weak test assertions; doc drift); all applied; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-049 (new — `InventoryService` should isolate subscriber exceptions); TD-048 extended (pickup rules not in the GDD; no "item picked up" client message)
**Files outside the story's own list**: new shared test helper `tests/EditMode/LootTableSystem/LootTestFakes.cs`; `new GroundItemService(...)` calls in `LootTable_GroundItemLifecycle_tests.cs` and `LootTable_RoundRobin_integration_tests.cs`; doc comments in `IGroundItemService.cs` and `GroundItemState.cs`
**Note for Story 008**: see "For Story 008" under "Added at code review" — no `Pickup` from inside the `OnInventoryChanged` handler; mark "retry pending" and pick up in the next `Tick`; `GroundItemService` becomes `IDisposable`; the shared fake needs a way to raise its events
**Note for Story 010**: reset the "assignee was inside" flag when an item is reassigned
