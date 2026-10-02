# Story 005: Ground Item Lifecycle and TTL Despawn

> **Epic**: Loot Table System
> **Status**: Complete
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

**Performance**: `Tick` is O(live ground items) per server tick and allocates nothing on a tick where no item expires. The GDD's worst case is about 600 live records. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

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
- [ ] **Zero items** (Edge Cases): a kill resolved through a real `LootTableService` whose rolls all miss raises no spawn event.
- [ ] **`GroundItemID`**: IDs are unique within the service's lifetime and never `0` (entities.yaml: `0 = Invalid`).
- [ ] **Spawn event timing** *(readiness decision, 2026-10-02)*: `OnGroundItemSpawned` is raised synchronously inside `Spawn`, exactly once, before any `Tick`.
- [ ] **Invalid arguments** *(readiness decision, 2026-10-02)*: `Spawn` with an `ItemID` of `0` or a `CharacterID` of `0` logs one server error, returns `GroundItemID.Invalid`, creates no record and raises no event.

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

**Hand-off from Story 004** *(readiness decision, 2026-10-02)*: this story delivers the ground item service only. It does not implement `ILootDropSink` — that interface carries no assignee, so Story 006 implements it (choosing the assignee, then calling `Spawn` once per item at the mob's position). In this story `Spawn` takes the assignee as a parameter and tests call it directly.

**Settled at readiness (2026-10-02):**
- Position is a `UnityEngine.Vector3`, matching `ILootDropSink` and `MobInfo`.
- The record also stores `SpawnTick`: `ExpiryTick` moves in Story 009, and the one-tick `Spawning` rule needs the spawn tick. An item leaves `Spawning` on the first `Tick` whose tick is newer than `SpawnTick` (wraparound-safe, `StaleDiscardComparer.IsNewerVersion`; a tick that is not newer leaves it in `Spawning` — code review, 2026-10-02).
- The spawn event is raised inside `Spawn`, after the record is stored.
- `Tick` removes an expired record before raising its despawn event, so a handler that calls back into the service cannot corrupt the iteration.
- Invalid `Spawn` arguments: one `Debug.LogError`, return `GroundItemID.Invalid`, nothing else (same rule as Story 003's unknown mob).

**Added at code review (2026-10-02, user: "fix all"):**
- An exception thrown by a spawn or despawn subscriber is logged (`Debug.LogException`) and not rethrown: `Spawn` always returns the ID of the stored record, and every item that expires on a tick is announced. This differs from `LootTableService.ResolveMobDrop` (Story 004), which does not catch — there the record is cleared first, so nothing can be paid twice; here a lost despawn event would leave the item on clients for good.
- After a `uint` wrap, the ID counter skips any ID that still belongs to a live item.
- The order of despawn events within one tick is unspecified.
- For Stories 007 and 010–011: `Tick` expires every non-terminal state. Those stories must branch on the state there (a claim in flight is not destroyed; an auction is resolved, not despawned).

**GDD wording mismatch (not fixed here):** the loot GDD state table says `GroundItemSpawned` is "broadcast to zone clients"; `networking-wire-protocol.md` says a common drop's message goes to the assigned character only. This story follows the wire protocol.

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
  - Then: `OnGroundItemSpawned` has fired exactly once when `Spawn` returns (before any `Tick`) and never again; the state is `Spawning` after spawn and after `Tick(100)`, and `Assigned` after `Tick(101)`
  - Edge cases: the event's `isAuction` is `false` and its assignee is the `Spawn` argument

- **Terminal states**
  - Given: a despawned item
  - When: further `Tick` calls
  - Then: no event, and `TryGetGroundItem` returns `false`
  - Edge cases: none

- **Zero items**
  - Given: a real `LootTableService` with a scripted PRNG whose every roll misses, and a test sink that forwards each drop to `Spawn`
  - When: `ResolveMobDrop` is called for a tagged mob
  - Then: no `OnGroundItemSpawned`
  - Edge cases: none

- **Invalid arguments**
  - Given: `Spawn` called with `ItemID` `0`, and separately with `CharacterID` `0`
  - Then: each call returns `GroundItemID.Invalid`, logs exactly one error, raises no event and leaves no record
  - Edge cases: the next valid `Spawn` still returns a non-zero ID

- **`GroundItemID`**
  - Given: three spawns
  - Then: three distinct IDs, none equal to `GroundItemID.Invalid`
  - Edge cases: none

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs` — must exist and pass.

**Status**: [x] Created — 23 test methods (23 NUnit cases), all 8 criteria covered

---

## Dependencies

- Depends on: Story 004 (drop hand-off seam), Story 002 (equipment cache for `gearTier`).
- Unlocks: Story 006.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 8/8 passing (0 deferred). The "no pickup is attempted" clause of AC-LT-16 holds trivially — no pickup code exists until Story 007; only `Despawned` is exercised as a terminal state (`Inventory` is entered by Story 007)
**Deviations**: Advisory only — `TR-loot-007` is not in the TR registry (same accepted gap as Stories 001–004); three rules decided by the user that are not in the GDD: an invalid `Spawn` argument logs an error and spawns nothing, the spawn event is raised inside `Spawn`, and subscriber exceptions are caught and logged (Story 004 propagates them); the code follows the wire protocol, not the loot GDD state table, on who receives `GroundItemSpawned`; 14 tests go beyond the written QA cases (4 from implementation — gear tier ×2, null guard, re-entrancy; 10 from the code review); `GroundItemService` has no interface yet
**Test Evidence**: Logic — `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs` (23 test methods, 23 NUnit cases; fake item database, scripted PRNG, stub party and mob providers, forwarding test sink). Full EditMode suite 1243/1243 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `GroundItemService.cs` and siblings returned CHANGES REQUIRED (zero-items test passed vacuously; a throwing subscriber lost the ID or left expired items unannounced); both required changes and the suggestions applied except removing the `ToArray()` allocation on expiry ticks; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 extended (the three rules above and the `GroundItemSpawned` recipient wording are not in the loot GDD)
**Note for Story 006**: implement `ILootDropSink` and call `Spawn` once per drop with the chosen assignee; add an interface for `GroundItemService` as its first consumer
**Note for Stories 007 and 010–011**: `Tick` expires every non-terminal state — branch on `Claiming` / `Auctioning` there
**Note for Story 009**: the spawn event's `ExpiryTick` goes stale once the TTL is extended; clients need a separate update
