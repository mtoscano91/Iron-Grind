# Story 007: `SaveSession` — the Step Sequence

> **Epic**: Character Persistence
> **Status**: Ready — `/story-readiness` 2026-10-15 (NEEDS WORK → decisions taken, see "Readiness decisions")
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 12 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-10 (the step sequence, reordered — decision 1); CR-CP-6 (one transaction, the version predicate, WARNING on a `SaveSession` conflict); CR-CP-11 (failure handling per reason); EC-CP-3, EC-CP-4; AC-CP-15, AC-CP-18, AC-CP-20, AC-CP-30, AC-CP-31.
**Requirement**: `TR-cp-006`, `TR-cp-010`, `TR-cp-011`
*(Epic placeholders: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness`, as for Stories 001–006.)*

**ADR Governing Implementation**: ADR-011 Decision 3 (snapshot in the synchronous prefix); ADR-006 Decision 4 (latency thresholds, the 5-second write timeout).
**ADR Decision Summary**: everything the save needs from live game state is copied on the tick thread before the first `await`; after it the method touches only that copy and the store.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — plain C#; `Debug.LogWarning` / `Debug.LogError` for logs, `LogAssert` in tests.

**Control Manifest Rules (this layer)**:
- Required: `ICharacterPersistence` implementations copy everything they need from live game state before their first `await`, on the calling thread — ADR-011
- Forbidden: reading or writing live game state after the first `await` — ADR-011
- Guardrail: persistence write — warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006

---

## Readiness decisions (2026-10-15)

1. **Snapshot first, then the queue** (CR-CP-10 reordered; epic design conflict 1, settled at Story 006 readiness). On the tick thread, inside `SaveSession`'s synchronous prefix: look up the session entry, read steps 2–6 into a `CharacterRecord`, take the expected `SaveVersion` from the entry; then hand the write to `CharacterWriteQueue.Enqueue` (Story 006). The write function closes over that copy and touches no live state. A second `SaveSession` queued behind the first therefore carries the same expected version and conflicts (EC-CP-4, AC-CP-20).
2. **A session table in the service.** `ApplyLoadedRecord`, when it returns `Success` (and `ItemDatabaseMiss`, from Story 005), records `CharacterID → { EntityID, AccountID, CharacterName, ClassType, CreatedAtUtc, GearSlots (copy), SaveVersion }`, taken from the loaded record. The table is lock-guarded persistence-layer state, not game state: `SaveSession` reads it on the tick thread; the write function, after a successful update, advances the entry's `SaveVersion` to expected + 1 (under the lock, and only if the entry is still there with the expected version). A `CharacterID` with no entry → `CharacterNotFound`, no store call. Story 010 removes entries on session release; Story 008 reads "the latest version" from this table.
3. **Gear: the loaded gear is written back unchanged.** No Equipment System exists; the GDD's Interactions table says save "reads `GearSlots` directly from record". The entry's `GearSlots` copy is written as is. When an Equipment System exists, the source moves there (Story 005 or later). Epic design conflict 3, resolved for now.
4. **Gold: a read on the narrow persistence interface.** `ICurrencyStateRestore` gains `bool TryGetState(CharacterID charId, out uint balance, out uint version)`, implemented by `CurrencySystem` under its per-character lock (false for an unregistered character). Its doc says it is the persistence seam for both directions. `ICurrencyService` is unchanged.
5. **Zone: a narrow seam.** New `public interface ICharacterZoneLocator { ZoneID GetCurrentZone(CharacterID charId); }` in `src/ServerLogic/CharacterPersistence/`, injected into the service, faked in tests; Zone Instancing implements it later. `ZoneID.Invalid` from the locator keeps the loaded `LastZoneID` (kept in the session entry).
6. **Logs, clock, timeout.** WARNING = `Debug.LogWarning`, critical = `Debug.LogError` (the codebase's critical-alert log; no alert service exists — epic design conflict 6); tests use `LogAssert`. The service takes an injected `Func<DateTime>` clock for the latency measurement and an injected write timeout (`TimeSpan`, default `PERSISTENCE_WRITE_TIMEOUT_SECONDS`).

---

## Acceptance Criteria

- [ ] **Session entry**: after `ApplyLoadedRecord` returns `Success`, the service holds an entry for the character (EntityID, identity fields, gear, `SaveVersion`); after `InventoryNotRestored`, it holds none.
- [ ] **Unknown character**: `SaveSession` for a `CharacterID` with no entry returns `CharacterNotFound` with no store call.
- [ ] **Snapshot before the first await**: all reads of steps 2–6 are made before `SaveSession` returns its task; a game-state change made right after the call, before the store completes, is not in the written record.
- [ ] **Step 2 (four getters)**: the 11 int stats are read with `GetBaseStat`, the 5 floats with `GetBaseStatFloat`, `CurrentHP` / `CurrentMP` with `GetCurrentHP` / `GetCurrentMP`, held free points with `LevelingService.GetLevelingState`; `GetEffectiveStat` is never called.
- [ ] **Step 3**: the gear slots written are the session entry's (the loaded ones), unchanged.
- [ ] **Step 4**: `IInventoryService.ExportSnapshot(charId)` is expanded to the 20-slot dense layout; a slot absent from the snapshot is written as `ItemID.Invalid`, count 0, level 0.
- [ ] **Step 5**: `GoldBalance` and `GoldVersion` both come from `ICurrencyStateRestore.TryGetState` and are written.
- [ ] **Step 6**: `LastZoneID` is `ICharacterZoneLocator.GetCurrentZone(charId)`; when that is `ZoneID.Invalid`, the loaded `LastZoneID` is written.
- [ ] **AC-CP-18 / step 7**: one store update with the expected version; on success the stored `SaveVersion` is the previous value + 1, `LastSavedAtUtc` is new, `CreatedAtUtc` is unchanged, and the session entry's `SaveVersion` is advanced to match.
- [ ] **AC-CP-15 (in-memory store)**: save, then load into fresh systems: every persisted field matches — the 16 base stats, held free points, `CurrentHP`, `CurrentMP`, `GoldBalance`, `GoldVersion`, `LastZoneID`, the 20 inventory slots (item, count, level), the gear slots (unchanged) and the identity fields; `SaveVersion` is N + 1.
- [ ] **AC-CP-30**: `SaveSession(id, ZoneClosed, hpOverride: 0f, ct)` for a character with `CurrentHP` 650 writes `CurrentHP` 0; `CurrentMP` is the value read from Character Stats. An override above MaxHP is written as given (the next load clamps it, F-CP-4).
- [ ] **AC-CP-31**: the overload without an override writes the `CurrentHP` read from Character Stats (350 → 350).
- [ ] **Conflict (CR-CP-6, step 8)**: zero rows affected returns `ConcurrencyConflict`, logs one warning and no error; no retry; the session entry's version is not advanced.
- [ ] **AC-CP-20 / EC-CP-4**: a second `SaveSession` for the same character issued before the first store round-trip returns ends with `ConcurrencyConflict` and a warning; the stored record is the first call's.
- [ ] **Failure per reason (CR-CP-11, steps 9–10)**: a store failure returns `DatabaseError` and logs one error for `CleanLogout`, `SessionTTLExpiry` and `ZoneClosed` alike; the method disconnects no one and tears nothing down; the returned task never faults.
- [ ] **Write timeout**: a store update not complete after the injected timeout completes the task with `DatabaseError` (short injected timeout in the test, no wall-clock wait beyond it).
- [ ] **Latency log**: with the injected clock, a write measured at 49 ms logs nothing, ≥ 50 ms a warning, ≥ 100 ms an error.
- [ ] **Doc comments** on `SaveSession`, `ICharacterZoneLocator`, `TryGetState` and the new constructor.

---

## Implementation Notes

- The write goes through Story 006's queue (`CharacterWriteQueue.Enqueue`). Per its doc: the snapshot is taken before `Enqueue`; the write function must not read live game state.
- **Version rule (Story 006 readiness):** `SaveSession` takes its expected `SaveVersion` at call time, from the session entry.
- `Inventory.UnregisterCharacter` (step 4's second sentence) belongs to the release of the session after the save: Story 010.
- EC-CP-3: `IsTransitioning` is not read and not written.
- The constructor grows again (zone locator, clock, write timeout, the queue); keep the earlier constructors compiling, or update the existing test files' `SetUp` only (no assertion changes).
- `SaveSession` is called from tick code and tracked by `ITickCompletionQueue` (ADR-011): its returned task must never fault.

**Authorized files**: `src/ServerLogic/CharacterPersistence/CharacterPersistenceService.cs`, `src/ServerLogic/CharacterPersistence/ICharacterZoneLocator.cs` (new), `src/ServerLogic/Currency/ICurrencyStateRestore.cs`, `src/ServerLogic/Currency/CurrencySystem.cs`, `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveSession_tests.cs` (new), `tests/EditMode/CharacterPersistence/CharacterPersistenceTestFakes.cs`, and the `SetUp` of the existing CharacterPersistence / Currency test files if a constructor change requires it.

---

## Out of Scope

- Story 006: the queue
- Story 008: `SaveIrreversibleOutcome`
- Story 010: who calls `SaveSession`, session release (removing the session entry), `UnregisterCharacter`
- Zone Instancing's teardown loop (the caller of the `ZoneClosed` reason) and its `ICharacterZoneLocator` implementation
- An Equipment System as the gear source
- Story 014: the SQL

---

## QA Test Cases

- **Snapshot timing** — Given a save started with the store held, When gold changes before the store completes, Then the stored gold is the earlier value.
- **Dense expansion** — Given a snapshot with slots 2 and 11, Then 18 slots are written empty and two with their values.
- **Round trip** — Given distinct values in every field, When saved and loaded, Then every field matches and `SaveVersion` rose by one.
- **HP override** — 650 with override 0 → 0, MP untouched; no override, 350 → 350; override 9,000 above MaxHP → 9,000 written.
- **Zone** — locator returns zone 42 → 42 written; locator returns `ZoneID.Invalid` → the loaded zone written.
- **Conflict** — Given the store row advanced behind the service, Then `ConcurrencyConflict`, one warning, zero errors, entry version unchanged.
- **Double call** — Two calls, first held → first Success, second `ConcurrencyConflict`.
- **Store failure** — For each of the three reasons: `DatabaseError`, one error log, no disconnect call.
- **Timeout and latency** — Injected clock: 49 ms no log, 50 ms warning, 100 ms error; never-completing store with a short timeout → `DatabaseError`.
- **Unknown character** — no entry → `CharacterNotFound`, store call count 0.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_SaveSession_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 004, 006 (all Complete)
- Unlocks: Stories 008, 010, 016
