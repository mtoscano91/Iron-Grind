# Story 002: Record Store Seam, In-Memory Store, `CreateStub`

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-12 (COMPLETE WITH NOTES; see Completion Notes)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours (4 as written, plus the service class, the clock and the fault controls)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-9 (`CreateStub` writes a complete Level 1 record; no first-login path in `LoadCharacter`), F-CP-3 (the Level 1 values), F-CP-2 (`SaveVersion` + 1 on every write, the `WHERE SaveVersion = @expected` predicate), AC-CP-22, AC-CP-23.
**Requirement**: `TR-cp-009`, `TR-cp-013`
*(Epic placeholders: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness` 2026-10-12, as for Story 001.)*

**ADR Governing Implementation**: ADR-006 (Decision 2: explicit SQL through Dapper; the service owns the per-character queue, the database owns the version predicate); ADR-011 Decision 3.
**ADR Decision Summary**: the service is `CharacterPersistenceService : ICharacterPersistence`; what talks to PostgreSQL sits behind it. This story names that boundary so the service can be built and tested with no database.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C#.
**Performance**: no impact expected — `CreateStub` runs once per registration, off the tick path; the in-memory store copies one record per call.

**Control Manifest Rules (this layer)**:
- Required: `ICharacterPersistence` implementations use `ConfigureAwait(false)` on every `await` — ADR-011
- Required: every `TaskCompletionSource` in the persistence layer and in test fakes uses `TaskCreationOptions.RunContinuationsAsynchronously` — ADR-011
- Required: `ICharacterPersistence` implementations copy everything they need from live game state before their first `await` — ADR-011
- Forbidden: SQLite for the persistence store — ADR-006

---

## Acceptance Criteria

*"The store" is `ICharacterRecordStore`: the seam between the service and the database. "The in-memory store" is `InMemoryCharacterRecordStore`, its implementation over a dictionary, used by EditMode tests and by a server run with no database. All types are in `IronGrind.ServerLogic`, namespace `IronGrind.CharacterPersistence`.*

- [ ] **Store seam**: `ICharacterRecordStore` declares exactly these three methods:
  - `Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)` — the stored record, or `null` when the id has none (a miss is a result, not an exception);
  - `Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)` — `true` when written; `false`, with nothing written, when the record's `CharacterID` already has a row;
  - `Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)` — the number of rows affected, 0 or 1.
- [ ] **Insert**: stores the record with the `SaveVersion` it carries (0 for a stub — an insert does not add 1) and stamps `CreatedAtUtc` and `LastSavedAtUtc` with the store's clock.
- [ ] **Version predicate (F-CP-2)**: an update with the expected `SaveVersion` writes the record with `SaveVersion` = expected + 1 and `LastSavedAtUtc` = the store's clock, leaves `CreatedAtUtc` as stored, and returns 1; an update with any other expected version writes nothing and returns 0; an update for an id that does not exist returns 0.
- [ ] **Clock**: the in-memory store takes its UTC time from an injected `Func<DateTime>`; tests pass a fixed or stepped clock. Nothing in this story calls `DateTime.UtcNow` except the default the composition root would pass.
- [ ] **Isolation**: the in-memory store keeps and returns copies (`CharacterRecord.Clone()`) — changing a record after it was written, or after it was fetched, does not change what the store holds.
- [ ] **Cancellation**: a call made with an already-cancelled token returns a cancelled task and writes nothing.
- [ ] **Faults for tests**: the in-memory store has these controls, each applying to the next store call only:
  - `FailNext(Exception)` — the next call's task faults with that exception and writes nothing; the call after it behaves normally;
  - `HoldNext()` — the next call's task stays incomplete until the returned release action is invoked, then completes with its normal result;
  - `NeverCompleteNext()` — the next call's task never completes unless its token is cancelled, in which case it is cancelled;
  - `OverwriteSaveVersion(CharacterID, uint)` — changes a stored row's `SaveVersion` at once (the "separate connection" of AC-CP-19); returns `false` for an unknown id.
- [ ] **Service**: `CharacterPersistenceService : ICharacterPersistence` is constructed over an `ICharacterRecordStore`. `CreateStub` is implemented; each of the other eight methods returns a task faulted with `NotSupportedException` whose message names the story that implements it (003 load, 007 `SaveSession`, 008 `SaveIrreversibleOutcome`, 011 purchases). The call itself does not throw: tick code passes it straight to `ITickCompletionQueue.Track` (ADR-011), where a throw would be tracked by nothing. *(Reworded 2026-10-12 at `/code-review`, user decision; was "throws".)*
- [ ] **`CreateStub` (CR-CP-9, F-CP-3)**: inserts one record for the given account, character and class with Level 1, Experience 0, HeldFreePoints 0, STR / DEX / VIT / INT 10, MaxHP 400, MaxMP 220, AttackPower 30, Defense 20, MagicDefense 4, CritChance 0.065, CritMultiplier 1.5, AttackRange 3.0, AttackSpeedMultiplier 1.030, MovementSpeed 5.0, CurrentHP 400, CurrentMP 220, GoldBalance 0, GoldVersion 0, LastZoneID `ZoneID.Invalid`, all gear and inventory slots empty, SaveVersion 0, `CharacterName` the empty string (decision 5). The values are the same for every class byte.
- [ ] **AC-CP-22 (store half)**: after `CreateStub` completes, `FetchAsync` returns a record with those values, the given `AccountID`, `CharacterID` and class byte.
- [ ] **Stub for an existing id**: `CreateStub` for a `CharacterID` that already has a record returns a task that faults with `InvalidOperationException`; the existing record is unchanged.
- [ ] **Store failure**: when the store's insert faults, `CreateStub`'s task faults with the same exception; when the token is cancelled, it is cancelled.
- [ ] **Level 1 values are not literals in the service**: they are constants of one static class, `CharacterStubDefaults`; a test pins each against the CR-CP-9 table.
- [ ] **Doc comments** on every public type and member.

---

## Implementation Notes

- The seam is this epic's own choice, taken from the epic's "Known Gaps" item 3: ADR-006 shows `CharacterPersistenceService` holding an `NpgsqlDataSource` directly. Putting the SQL behind `ICharacterRecordStore` changes no ADR decision; Story 014 writes the Npgsql implementation.
- CR-CP-6 makes every save one transaction: the store's update is the unit; the service never composes two store calls for one save.
- The timestamps are the store's: ADR-006's SQL writes `last_saved_at_utc = NOW()`, the database's clock. The in-memory store's injected clock stands in for it.
- `CreateStub`'s `Task` has no result type in CR-CP-1, so failure is carried by the task: faulted for an existing id or a store failure, cancelled for a cancelled token. Say so in the doc comment.
- `CreateStub` reads no live game state, so its snapshot is the record it builds before the first `await`. `ConfigureAwait(false)` on the `await`.
- `CharacterRecord`, `AccountID`, `ZoneID`, `GearSlotRecord` and the constants exist (Story 001). A new record's slots are already empty.
- The purchase methods of the store are Story 011's; do not add them to `ICharacterRecordStore` here.

**Decisions — `/story-readiness` 2026-10-12 (user)**

1. **Source of the Level 1 values.** A constants class in this system (`CharacterStubDefaults`), pinned by a test against the GDD table. Character Stats has no Level 1 constants in code: there the values are formula results.
2. **`CreateStub` for an existing id.** The returned task faults with `InvalidOperationException`; nothing is written.
3. **OQ-CP-4.** No `CHARACTER_STUB_TIMEOUT_SECONDS`. The caller's token is the bound here; the write is bounded by `PERSISTENCE_WRITE_TIMEOUT_SECONDS` when the Npgsql store arrives (Story 014).
4. **Service shape.** `CharacterPersistenceService` is created in this story; the eight methods of later stories return a task faulted with `NotSupportedException` (a faulted task, not a throw — decided at `/code-review` 2026-10-12).
5. **Fields the CR-CP-9 table omits.** `CharacterName` is the empty string — `CreateStub` takes no name and the GDD gives none (epic design conflict 9). `CreatedAtUtc` and `LastSavedAtUtc` are stamped by the store.
6. **Seam signatures, clock and fault controls** as in the criteria above.

---

## Out of Scope

- Story 003: `LoadCharacter` (the second half of AC-CP-22 and AC-CP-23)
- Story 006: the per-character queue
- Story 011: the purchase methods of the store
- Story 014: the Npgsql store
- Where a character's name comes from (a design fix)

---

## QA Test Cases

- **Insert** — Given an empty store with a clock at T1, When a record is inserted, Then the result is `true` and a fetch returns it with `SaveVersion` as given and both timestamps T1. Edge: a second insert for the same id returns `false` and the first record is unchanged.
- **Fetch miss** — Given an empty store, When an id is fetched, Then the result is `null`.
- **Version predicate** — Given a record at version 4 inserted at T1 and the clock moved to T2, When updated with expected 4, Then 1, version 5, `LastSavedAtUtc` T2, `CreatedAtUtc` T1; When updated again with expected 4, Then 0 and the record is unchanged. Edge: unknown id → 0.
- **Isolation** — Given a record written, When the caller changes its gear array and a scalar, Then a fetch returns the written values; Given a fetched record changed by the caller, Then a second fetch returns the stored values.
- **Cancellation** — Given a cancelled token, When insert, fetch or update is called, Then the task is cancelled and the store is unchanged.
- **Faults** — Given `FailNext`, Then the next call's task faults with that exception, nothing is written, and the call after it succeeds; Given `HoldNext`, Then the task is incomplete until the release action runs and then has its normal result; Given `NeverCompleteNext`, Then the task is incomplete, and cancelled once its token is; Given `OverwriteSaveVersion(id, 9)` on a row at 4, Then an update with expected 4 returns 0.
- **CreateStub values** — Given a new account and character, When `CreateStub` completes, Then every field of the fetched record equals the criterion's list. Edge: class bytes 0, 1 and 255 give the same stats and keep their own class byte.
- **Stub defaults** — Then each constant of `CharacterStubDefaults` equals its CR-CP-9 value.
- **Existing id** — Given a record with GoldBalance 500, When `CreateStub` is called for its id, Then the task faults with `InvalidOperationException` and GoldBalance is still 500.
- **Store failure** — Given `FailNext(e)`, When `CreateStub` is called, Then its task faults with `e`.
- **Not yet implemented** — Then each of the other eight methods returns, without throwing, a task faulted with `NotSupportedException` naming its story.

*Tests are synchronous where the task is already complete (`task.IsCompleted`, `task.Result`); no test waits on wall-clock time.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_RecordStore_tests.cs` — must exist and pass.

**Status**: [x] Created — 67 test cases; EditMode 2590 / 2590 (2026-10-12 19:58:57Z, `TestResults.xml`)

---

## Dependencies

- Depends on: Story 001 (Complete 2026-10-12)
- Unlocks: Stories 003, 006, 007, 008, 011, 014

---

## Completion Notes

**Completed**: 2026-10-12
**Criteria**: 14/14 passing (thirteen by automated test; "Doc comments" by reading)
**Test Evidence**: Logic — `tests/EditMode/CharacterPersistence/CharacterPersistence_RecordStore_tests.cs`, 67 cases (64 `[Test]`, 3 `[TestCase]`), in the EditMode run of 2590 / 2590
**Code Review**: Complete — `/code-review` 2026-10-12 (`unity-specialist`, `qa-tester`): CHANGES REQUIRED, tests only; fixes applied (tests 48 → 67; the faulted task; the cancel-against-write lock and the registration disposal in the in-memory store); not re-run after the fixes
**Gates**: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — lean mode

**Files** (`src/ServerLogic/CharacterPersistence/`): `ICharacterRecordStore.cs`, `InMemoryCharacterRecordStore.cs`, `CharacterStubDefaults.cs`, `CharacterPersistenceService.cs`; the test file.

**Deviations (advisory, accepted by the user 2026-10-12)**
1. The eight unimplemented service methods return a task faulted with `NotSupportedException`; the criterion first said "throws" (reworded at `/code-review`, user decision).
2. Not listed in the story: `CharacterStubDefaults.CreateRecord` builds the stub record; a call turned away for an already-cancelled token leaves a pending fault control in place, and a second control replaces a pending one; an exception from the operation (the injected clock, for one) faults the task; the store's state is guarded by one lock, and a held call that ends cancelled has written nothing.
3. `CreateStub` does not validate its ids: `CharacterID.Invalid` is accepted. No criterion covers it.
4. Test methods are named `Type_Scenario` (the suite's majority style), not `test_…`.

**For later stories**
- Stories 006–008: `UpdateAsync` returns only a row count — the service computes the new version (expected + 1) itself; the seam has no typed store exception to map to `DatabaseError`, and timeout and cancellation are not told apart.
- Story 011: one transaction across `character_records` and `pending_purchases` needs new seam methods.
- Story 014 (Npgsql store): translate the unique violation on insert to `false`; do not write `created_at_utc` on update; write the stub values explicitly, not through column defaults; decide what a `BIGINT` `save_version` above the `uint` range does (the in-memory store wraps at `uint.MaxValue`, F-CP-2). Whether ADR-006's UPDATE writes name, account and class — the in-memory update replaces the whole row — was not confirmed.
- Authentication: `CreateStub` returns a plain `Task` and `ITickCompletionQueue.Track<T>` takes a `Task<T>` (epic design conflict 10). The stub's `CharacterName` is the empty string (design conflict 9).

**Tech debt logged**: none (user's choice)
