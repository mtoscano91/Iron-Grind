# Story 007: `SaveSession` — the Step Sequence

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (5 open decisions, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-10 (the step sequence); CR-CP-6 (one transaction, the version predicate, WARNING on a `SaveSession` conflict); CR-CP-11 (failure handling per reason); EC-CP-3, EC-CP-4; AC-CP-15, AC-CP-18, AC-CP-20, AC-CP-30, AC-CP-31.
**Requirement**: `TR-cp-006`, `TR-cp-010`, `TR-cp-011`

**ADR Governing Implementation**: ADR-011 Decision 3 (snapshot in the synchronous prefix); ADR-006 Decision 4 (latency thresholds, the 5-second write timeout).
**ADR Decision Summary**: everything the save needs from live game state is copied on the tick thread before the first `await`; after it the method touches only that copy and the store.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: `ICharacterPersistence` implementations copy everything they need from live game state before their first `await`, on the calling thread — ADR-011
- Forbidden: reading or writing live game state after the first `await` — ADR-011
- Guardrail: persistence write — warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006

---

## Acceptance Criteria

- [ ] **Snapshot before the first await**: the reads of steps 2–6 are all made before `SaveSession` returns its task to the caller; a game-state change made right after the call, before the store completes, is not in the written record.
- [ ] **Step 2**: the base-stat fields are read with `GetBaseStat`; `GetEffectiveStat` is never called.
- [ ] **Step 4**: `IInventoryService.ExportSnapshot(charId)` is expanded to the 20-slot dense layout; a slot absent from the snapshot is written as `ItemID.Invalid`, count 0, level 0.
- [ ] **Step 5**: `GoldBalance` and `GoldVersion` are both read from the Currency System and written.
- [ ] **Step 6**: `LastZoneID` is the character's current zone.
- [ ] **AC-CP-18 / step 7**: one store update with the expected version; on success the stored `SaveVersion` is the previous value + 1 and `LastSavedAtUtc` is new; `CreatedAtUtc` is unchanged.
- [ ] **AC-CP-15 (in-memory store)**: save, then load into fresh systems: all persisted fields match — the base stats, `GoldBalance`, `GoldVersion`, `LastZoneID`, the 20 inventory slots (item, count, level), `CurrentHP`, `CurrentMP`; `SaveVersion` is N + 1. Gear slots join this criterion when Story 005 is done.
- [ ] **AC-CP-30**: `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)` for a character with `CurrentHP` 650 writes `CurrentHP` 0; `CurrentMP` is the value read from Character Stats.
- [ ] **AC-CP-31**: the overload without an override writes the `CurrentHP` read from Character Stats (350 → 350).
- [ ] **Conflict (CR-CP-6, step 8)**: zero rows affected returns `ConcurrencyConflict` and logs one WARNING, no critical log; no retry.
- [ ] **AC-CP-20 / EC-CP-4**: a second `SaveSession` for the same character issued before the first store round-trip returns ends with `ConcurrencyConflict` and a WARNING; the stored record is the first call's.
- [ ] **Failure per reason (CR-CP-11, steps 9–10)**: a store failure returns `DatabaseError` and logs one critical error for `CleanLogout`, `SessionTTLExpiry` and `ZoneClosed` alike; the method disconnects no one and tears nothing down.
- [ ] **Unknown character**: a character with no loaded state returns `CharacterNotFound` without a store call.
- [ ] **Write timeout**: a store update not complete after `PERSISTENCE_WRITE_TIMEOUT_SECONDS` completes the task with `DatabaseError` (injected short timeout in the test).
- [ ] **Latency log**: a write that takes ≥ 50 ms logs a warning, ≥ 100 ms a critical alert (injected clock).

---

## Implementation Notes

- The write goes through Story 006's queue.
- `Inventory.UnregisterCharacter` (step 4's second sentence) belongs to the release of the session after the save: Story 010.
- EC-CP-3: `IsTransitioning` is not read and not written.

**Open decisions for `/story-readiness`**

1. **Step 1 against ADR-011.** CR-CP-10 acquires the save slot first, "waits if a save is already in flight", then reads state. ADR-011 Decision 3 forbids reading state after an `await`. The snapshot must come first; this reorders the GDD's steps and needs a line in the GDD. See Story 006's open decision 1 for the version consequence.
2. **Where the expected `SaveVersion` lives in memory**: who holds it for a loaded character and who advances it after a successful write.
3. **Gear slots (step 3).** Same question as Story 005's open decision 1; until the Equipment System exists, the criterion of this story covers the other fields.
4. **`GetBalance` returns the balance only** (`ICurrencyService.GetBalance(CharacterID)` → `uint`); step 5 needs the version too. An addition to the Currency System's interface.
5. **`LastZoneID` (step 6)** comes from Zone Instancing, which has no code: a seam, and what it returns today.

---

## Out of Scope

- Story 006: the queue
- Story 008: `SaveIrreversibleOutcome`
- Story 010: who calls `SaveSession`, session release, `UnregisterCharacter`
- Zone Instancing's teardown loop (the caller of the `ZoneClosed` reason)
- Story 014: the SQL

---

## QA Test Cases

- **Snapshot timing** — Given a save started, When gold changes before the store completes, Then the stored gold is the earlier value.
- **Dense expansion** — Given a snapshot with slots 2 and 11, Then 18 slots are written empty and two with their values.
- **Round trip** — Given distinct values in every field, When saved and loaded, Then every field matches and `SaveVersion` rose by one.
- **HP override** — 650 with override 0 → 0, MP untouched; no override, 350 → 350. Edge: override above MaxHP (what is written is a readiness question if it matters).
- **Conflict** — Given the store row advanced behind the service, Then `ConcurrencyConflict`, one WARNING, zero critical.
- **Double call** — Two calls, first held → first Success, second `ConcurrencyConflict`.
- **Store failure** — For each of the three reasons: `DatabaseError`, one critical log, no disconnect call.
- **Timeout and latency** — Injected clock: 49 ms no log, 50 ms warning, 100 ms critical; never-completing store → `DatabaseError`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveSession_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 004, 006
- Unlocks: Stories 008, 010, 016
