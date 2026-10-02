# Story 005: Ground Item Lifecycle and TTL Despawn

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — States and Transitions (GroundItem), CR-LT-12 (Ground Item Timer), CR-LT-15 (Server Authority — outcome events), Edge Cases ("If the drop roll produces zero items")
**Requirement**: `TR-loot-007`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: The loot service does not send wire messages. It raises Tier 2 C# events (`event Action<T>`, `readonly struct` args) that the network layer subscribes to and turns into `GroundItemSpawned` / `GroundItemDespawned`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. Ticks come from the caller of `Tick(uint currentTick)` (the zone tick loop); expiry comparison uses the existing wraparound-safe `StaleDiscardComparer.IsTickExpired(currentTick, expiryTick)`. Never wall-clock time.

**Control Manifest Rules (Core layer)**:
- Required: `event Action<T>` with `readonly struct` T, names prefixed `On` — ADR-010
- Forbidden: class-typed event args; `UnityEvent` for server logic; a central EventBus — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-16** [BLOCKING] *(common-drop half)*: a common drop in `Assigned` state with no pickup occurring reaches `expiryTick` → it transitions to `Despawned`; the despawn event is raised; no pickup is attempted; the drop is permanently lost.
- [ ] **CR-LT-12**: every spawned ground item carries `expiryTick = spawnTick + GROUND_ITEM_TTL_TICKS` (default 2,400).
- [ ] **States — `Spawning`**: a spawned item is in `Spawning` for exactly 1 server tick, during which the spawn event is raised once; it then moves to `Assigned`.
- [ ] **States — terminal**: `Inventory` and `Despawned` are terminal; a terminal item's record is destroyed and it is never ticked, expired or announced again.
- [ ] **Zero items** (Edge Cases): a kill whose drop list is empty raises no spawn event.
- [ ] **`GroundItemID`**: IDs are unique within the service's lifetime and never `0` (entities.yaml: `0 = Invalid`).

---

## Implementation Notes

*Derived from ADR-010 (Tier 2 events) and the GDD state table.*

**Module conventions:** see Story 001.

**Types:**
- `GroundItemID` — `readonly struct` wrapping `uint`, `Invalid = 0`, allocated from a per-service sequential counter starting at 1 (scoped to the zone session — entities.yaml).
- `GroundItemState` — `Spawning`, `Assigned`, `Auctioning`, `Claiming`, `Inventory`, `Despawned` (GDD state table). `Auctioning` and `Claiming` are entered by Stories 010 and 007.
- A ground item record: `GroundItemID`, `ItemID`, position, `AssignedTo` (`CharacterID`), `ExpiryTick`, `State`, and `PauseBudgetRemaining` initialised to `GROUND_ITEM_TTL_PAUSE_CAP_TICKS` (used by Story 009).
- Constants in `LootTableConstants`: `GROUND_ITEM_TTL_TICKS = 2400`, `GROUND_ITEM_TTL_PAUSE_CAP_TICKS = 1200` (design/registry/entities.yaml).

**API (internal to the loot module):** `Spawn(ItemID, position, CharacterID assignedTo, uint spawnTick) → GroundItemID`; `Tick(uint currentTick)`; a read accessor (`TryGetGroundItem`) so tests and later stories can inspect state.

**`Tick`:** advance `Spawning` → `Assigned` one tick after spawn; for every non-terminal item whose `expiryTick` is reached, transition to `Despawned`, raise the despawn event, destroy the record.

**Events (payload mirrors the wire schemas in `design/gdd/networking-wire-protocol.md`):**
- `OnGroundItemSpawned` — `groundItemId`, `itemId`, `gearTier`, `isAuction` (always `false` in this story), position, `expiryTick`, and the assigned character (the network layer sends a common drop's message to that character only).
- `OnGroundItemDespawned` — `groundItemId`.

`gearTier` comes from the equipment cache (Story 002); a cache miss is `GearTier.None`.

**Hand-off from Story 004:** implement the drop seam so each pending item is spawned at the mob's position. Who the item is assigned to is decided by Story 006; in this story `Spawn` takes the assignee as a parameter and tests call it directly.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 006**: choosing the assignee (round-robin) and wiring kills to `Spawn`.
- **Story 007**: proximity, `Claiming`, pickup, the `Inventory` terminal state.
- **Story 008**: expiry warning. **Story 009**: `expiryTick` extension.
- **Stories 010–011**: `Auctioning` and auction resolution on expiry (the other half of AC-LT-16).
- **Story 012**: zone teardown.
- Wire encoding of the loot messages — the network layer subscribes to these events; no codec work here.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs`

- **AC-LT-16 (common)**: expiry despawns
  - Given: an item spawned at tick 100 (`expiryTick = 2500`), ticked into `Assigned`
  - When: `Tick` is called up to tick 2500
  - Then: at tick 2499 the item is still `Assigned`; at tick 2500 `OnGroundItemDespawned` fires once with its ID and the record no longer exists
  - Edge cases: ticking past 2500 raises no second event

- **CR-LT-12**: expiry tick
  - Given: `Spawn` at tick 100
  - Then: the record's `ExpiryTick` is 2500
  - Edge cases: none

- **`Spawning` lasts one tick**
  - Given: `Spawn` at tick 100
  - When: `Tick(100)`, then `Tick(101)`
  - Then: `OnGroundItemSpawned` fired exactly once; the state is `Spawning` after spawn and `Assigned` after `Tick(101)`
  - Edge cases: the event's `isAuction` is `false` and its assignee is the `Spawn` argument

- **Terminal states**
  - Given: a despawned item
  - When: further `Tick` calls
  - Then: no event, and `TryGetGroundItem` returns `false`
  - Edge cases: none

- **Zero items**
  - Given: the Story 004 seam invoked with an empty drop list
  - Then: no `OnGroundItemSpawned`
  - Edge cases: none

- **`GroundItemID`**
  - Given: three spawns
  - Then: three distinct IDs, none equal to `GroundItemID.Invalid`
  - Edge cases: none

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (drop hand-off seam), Story 002 (equipment cache for `gearTier`).
- Unlocks: Story 006.
