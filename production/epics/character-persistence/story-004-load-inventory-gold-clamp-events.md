# Story 004: `LoadCharacter` — Inventory, Gold, At-Cap, HP/MP Clamp, Client Events

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (4 open decisions, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 steps 5 to 10; F-CP-4 (the clamp); EC-CP-2; AC-CP-8, AC-CP-9, AC-CP-10, AC-CP-25. `design/gdd/inventory-system.md` — Persistence and Load Edge Cases (a refused import is never an empty inventory). `design/gdd/death-and-respawn.md` — EC-DR-2 (dead-on-load).
**Requirement**: `TR-cp-003`

**ADR Governing Implementation**: ADR-011 Decision 3; ADR-010 (direct calls on injected interfaces).
**ADR Decision Summary**: the load restores state by calling each owning system's restore API on the tick thread; it computes no game value.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: cross-system communication is a direct call on an injected interface — ADR-010
- Forbidden: a central EventBus / MessageBus — ADR-010
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011

---

## Acceptance Criteria

- [ ] **Inventory (step 5)**: the 20 dense slots are translated to an `InventorySnapshot` of `{SlotIndex, ItemId, Quantity, EnhancementLevel}` entries, skipping slots whose item is `ItemID.Invalid`, and passed to `IInventoryService.ImportSnapshot(charId, snapshot)`. A record with items in slots 0, 7 and 19 gives a three-entry snapshot with those indices.
- [ ] **Refused import**: if `ImportSnapshot` returns false the load fails — it does not continue with an empty bag. The code returned is open decision 3.
- [ ] **Gold (step 6)**: `GoldBalance` and `GoldVersion` are written into the Currency System as a state restore, not through `AddGold`: no gold transaction event is raised, and afterwards the balance and the version both equal the record's.
- [ ] **AC-CP-10 (step 7)**: for a record with `Level == 60` the Leveling System reports at-cap after the load, and no level-up event is raised; for Level 59 it does not report at-cap.
- [ ] **Dead-on-load (step 8, EC-DR-2)**: a record with `CurrentHP <= 0` loads with `CurrentHP = MaxHP`.
- [ ] **AC-CP-25 / F-CP-4 (step 8)**: `CurrentHP` is clamped to [0, MaxHP] and `CurrentMP` to [0, MaxMP], using the maxima as they stand after the equipment step; saved 300 of 400 loads as 300; saved 650 with a MaxHP of 500 loads as 500, with no error and no alert. The clamp runs on every load.
- [ ] **AC-CP-8 (step 9)**: one `GoldSyncEvent` with reason `GoldTransactionReason.Other` goes to the client, carrying the restored balance and version.
- [ ] **AC-CP-9 (step 10)**: one stat snapshot goes to the client after the gold event.
- [ ] **Order**: steps 5, 6, 7, 8, 9, 10 run in that order, after Story 003's step 3 (and after Story 005's step 4 when it exists); a recording test double shows the sequence.
- [ ] **A failed step sends nothing**: when the load fails at step 5, no gold is restored and no client event is sent.

---

## Implementation Notes

- `IInventoryService.ImportSnapshot`, `ExportSnapshot` and `UnregisterCharacter` exist (`src/ServerLogic/InventorySystem/IInventoryService.cs`). `LevelingService.RestoreLevelingState` exists. `GoldSyncEvent` exists as a wire type.
- **Not in code (checked 2026-10-12)**: a Currency restore call that sets balance and version without a transaction (`ICurrencyService.GetBalance` returns the balance only); a `StatSnapshotEvent` type; the path by which a server system sends these two messages to one client — Networking Core Story 039's `IClientMessageOutbox` is that path now.
- Steps 9 and 10 are sends to the client that is still connecting: check `networking-session.md` for whether they are queued before `SessionReady` or sent after it.

**Open decisions for `/story-readiness`**

1. **The Currency restore API.** A new method on `ICurrencyService` (an edit to the Currency System from this story) — its name, and that it raises no event.
2. **`StatSnapshotEvent`.** Its schema and message type id, and which system owns it (Character Stats, by the look of `character-stats.md`); the wire-protocol document has OQ-NC-SER-2 open on the `SessionReady` fields (GDD OQ-CP-3).
3. **The code for a refused inventory import.** CR-CP-3 says "retry or reject the login" and names no `CharacterLoadCode`; `DatabaseError` and `CorruptRecord` both mislead.
4. **At-cap notification.** Whether `RestoreLevelingState` already covers step 7 or a separate call is needed.

---

## Out of Scope

- Story 003: steps 1–3 and 11
- Story 005: step 4 (equipment); AC-CP-24 (the clamp after an item miss) is there
- The `SessionReady` message and the client side

---

## QA Test Cases

- **Dense to sparse** — Given items in slots 0, 7, 19 with counts and levels, When loaded, Then `ImportSnapshot` receives exactly three entries with those values. Edge: an empty bag gives an empty snapshot, not null.
- **Refused import** — Given `ImportSnapshot` returns false, Then the load is not Success, Currency is untouched, no client event.
- **Gold restore** — Given balance 1,250 and version 9, Then Currency reports both and its transaction event count is 0.
- **At-cap** — Level 60 → at-cap, zero level-up events; Level 59 → not at-cap.
- **Dead-on-load** — Given `CurrentHP` 0 and MaxHP 400, Then 400. Edge: negative saved HP.
- **Clamp** — 300 / 400 → 300; 650 / 500 → 500; MP likewise.
- **Events and order** — Then the recorded call order is import, gold restore, at-cap, HP/MP, `GoldSyncEvent`, stat snapshot.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadRestore_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (and its open decision 1); Networking Core Story 039 (`IClientMessageOutbox`) — Complete
- Unlocks: Story 016
