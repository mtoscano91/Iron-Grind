# Story 007: Proximity Pickup and Bag-Full Drop Fate

> **Epic**: Loot Table System
> **Status**: Ready
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

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_ProximityPickup_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006 (assigned ground items). Inventory System Story 002 (`Pickup`) is Complete.
- Unlocks: Story 008.
