# Story 002: Record Store Seam, In-Memory Store, `CreateStub`

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (3 open decisions, listed below)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-9 (`CreateStub` writes a complete Level 1 record; no first-login path in `LoadCharacter`), F-CP-3 (the Level 1 values), F-CP-2 (`SaveVersion` + 1 on every write, the `WHERE SaveVersion = @expected` predicate), AC-CP-22, AC-CP-23.
**Requirement**: `TR-cp-009`, `TR-cp-013`

**ADR Governing Implementation**: ADR-006 (Decision 2: explicit SQL through Dapper; the service owns the per-character queue, the database owns the version predicate); ADR-011 Decision 3.
**ADR Decision Summary**: the service is `CharacterPersistenceService : ICharacterPersistence`; what talks to PostgreSQL sits behind it. This story names that boundary so the service can be built and tested with no database.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C#.

**Control Manifest Rules (this layer)**:
- Required: `ICharacterPersistence` implementations use `ConfigureAwait(false)` on every `await` — ADR-011
- Required: every `TaskCompletionSource` in the persistence layer and in test fakes uses `TaskCreationOptions.RunContinuationsAsynchronously` — ADR-011
- Forbidden: SQLite for the persistence store — ADR-006

---

## Acceptance Criteria

*"The store" is `ICharacterRecordStore`: the seam between the service and the database. "The in-memory store" is its implementation over a dictionary, used by EditMode tests and by a server run with no database.*

- [ ] **Store seam**: `ICharacterRecordStore` has async methods to fetch a record by `CharacterID` (a miss is a result, not an exception), to insert a new record, and to update a record under a version predicate. Every method takes a `CancellationToken`.
- [ ] **Version predicate (F-CP-2)**: an update with the expected `SaveVersion` writes the record with `SaveVersion` + 1 and a new `LastSavedAtUtc`, and reports one row affected; an update with any other expected version writes nothing and reports zero rows; an update for an id that does not exist reports zero rows.
- [ ] **Isolation**: the in-memory store keeps and returns copies — changing a record after it was written, or after it was fetched, does not change what the store holds.
- [ ] **Faults for tests**: the in-memory store can be told to delay, to fail or to never complete its next operation, and to change a row's `SaveVersion` behind the caller's back (the "separate connection" of AC-CP-19).
- [ ] **`CreateStub` (CR-CP-9, F-CP-3)**: writes one record for the given account, character and class with Level 1, Experience 0, HeldFreePoints 0, STR / DEX / VIT / INT 10, MaxHP 400, MaxMP 220, AttackPower 30, Defense 20, MagicDefense 4, CritChance 0.065, CritMultiplier 1.5, AttackRange 3.0, AttackSpeedMultiplier 1.030, MovementSpeed 5.0, CurrentHP 400, CurrentMP 220, GoldBalance 0, GoldVersion 0, LastZoneID 0, all gear and inventory slots empty, SaveVersion 0. The values are the same for every class.
- [ ] **AC-CP-22 (store half)**: after `CreateStub`, the record exists in the store with those values.
- [ ] **Stub for an existing id**: `CreateStub` for a `CharacterID` that already has a record does not overwrite it (the outcome is open decision 2).
- [ ] **Level 1 values are not literals in the service**: they come from one named source (open decision 1).

---

## Implementation Notes

- The seam is this epic's own choice, taken from the epic's "Known Gaps" item 3: ADR-006 shows `CharacterPersistenceService` holding an `NpgsqlDataSource` directly. Putting the SQL behind `ICharacterRecordStore` changes no ADR decision; Story 014 writes the Npgsql implementation.
- CR-CP-6 makes every save one transaction: the store's update is the unit; the service never composes two store calls for one save.
- `CreateStub`'s `Task` (no result type in CR-CP-1) carries failure as an exception or a fault — say which in the doc comment.

**Open decisions for `/story-readiness`**

1. **Source of the Level 1 values.** F-CP-3 says Character Stats owns them and this system "stores them verbatim". Read them from Character Stats' own constants if they exist there, or define them here with a test that compares the two.
2. **`CreateStub` for an existing id.** CR-CP-1 gives `CreateStub` no result code. Throw, or complete without writing and log.
3. **OQ-CP-4** (GDD): a `CHARACTER_STUB_TIMEOUT_SECONDS` constant — define one here or leave the caller's token as the only bound.

---

## Out of Scope

- Story 003: `LoadCharacter` (the second half of AC-CP-22 and AC-CP-23)
- Story 006: the per-character queue
- Story 011: the purchase methods of the store
- Story 014: the Npgsql store

---

## QA Test Cases

- **Version predicate** — Given a record at version 4, When updated with expected 4, Then one row, version 5; When updated again with expected 4, Then zero rows and the record is unchanged. Edge: unknown id → zero rows.
- **Isolation** — Given a record written, When the caller changes its gear array, Then a fetch returns the written values.
- **Faults** — Given "fail next", Then the next call's task faults and the one after succeeds; Given "delay", Then the task completes only when released.
- **CreateStub values** — Given a new account and character, When `CreateStub` completes, Then every field equals the table above. Edge: each `ClassType` value gives the same stats.
- **Existing id** — Given a record with GoldBalance 500, When `CreateStub` is called for its id, Then GoldBalance is still 500.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_RecordStore_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 006, 007, 008, 011, 014
