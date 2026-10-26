# Story 015: The Npgsql Pending-Purchase Store

> **Epic**: Character Persistence
> **Status**: Complete (2026-10-26, `/story-done` COMPLETE WITH NOTES) — written 2026-10-12; `/story-readiness` 2026-10-26: NEEDS WORK (14 gaps) → fixed → Ready; implemented, reviewed (`/code-review` CHANGES REQUIRED → all fixes applied) and closed 2026-10-26.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours (was 4 h provisional: migration 003, the idempotent conflict path, the test trigger and the contract subclass were added at readiness)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-12 (PendingPurchase storage and lifecycle; "Gold authority (ADR-001 Amendment A2)": the store writes the caller's in-memory `goldBalance` / `goldVersion` as absolute values on `character_records`, without advancing `save_version`).
**Requirement**: `TR-cp-012` — "`PendingPurchase` in its own `pending_purchases` table, outside the character record".

**ADR Governing Implementation**: ADR-006 Decision 3 (the `pending_purchases` INSERT and the gold UPDATE in one `NpgsqlTransaction`; the DDL; the 2026-10-26 note); ADR-001 Decisions 2–4 and Amendment A2 with its addendum (in-memory gold authority, write queue, `OutcomeUnknown`, idempotent `BeginPurchase`). Both Accepted.
**ADR Decision Summary**: `BeginPurchase` is the only write that shares a transaction with another system's data; if either statement fails, both roll back. The gold UPDATE writes the caller's balance and version as given — no balance check, no `save_version` bump. A retry of `BeginPurchase` after an unknown outcome must find the first call's record instead of writing a second one.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: no Unity API. Npgsql 8.0.9 and Dapper 2.1.66 (pinned, `docs/engine-reference/unity/plugins/npgsql-dapper.md`), as Story 014: SQL as explicit text constants, Dapper binds parameters and maps rows. TD-073 (Dapper under IL2CPP) stays open and is not this story's to decide; follow Story 014's pattern so that whatever TD-073 decides applies to both stores at once. `PostgresException.SqlState` is the Npgsql way to tell a foreign-key violation (`23503`) from other errors.

**Control Manifest Rules (this layer, Manifest 2026-10-26)**:
- Required: `BeginPurchase` writes no balance check, never returns `InsufficientGold` / `ConcurrencyConflict`; an unknown character writes nothing — ADR-001 A2 (manifest lines 48, 52)
- Required: `BeginPurchase` is idempotent on (character, `RequestId`); the SQL store enforces it with a unique index (manifest line 55; the column is `char_id`, see Implementation Notes)
- Required: the purchase writes run through the per-character write queue — done by `CharacterPersistenceService` (Story 011); the store adds no queue of its own (manifest line 53)
- Required: the store takes the zone's `NpgsqlDataSource` and never creates or disposes one (manifest lines 79, 223)
- Required: `ConfigureAwait(false)` on every `await`, including `await using` — ADR-011 (manifest lines 90, 238)
- Forbidden: Entity Framework Core — ADR-006 (manifest line 207)

---

## Acceptance Criteria

*Against a real PostgreSQL instance with migrations 001–003 applied, as Story 014. The tests are `[Explicit]` and `[Category("Database")]` and are run with `-testFilter` (tools/postgres/README.md).*

- [x] **Where**: `NpgsqlCharacterRecordStore` implements `IPendingPurchaseStore` as well as `ICharacterRecordStore` (the service finds the purchase store as `store as IPendingPurchaseStore`, so it must be the same object); no new constructor parameter; `ICharacterRecordStore` and `IPendingPurchaseStore` unchanged.
- [x] **Shared suite**: a subclass of `PendingPurchaseStoreContractTests` (Story 011, 23 tests) passes unchanged against this store. It overrides `CreateStore`, `SeedCharacter`, `ReadCharacter` and `CleanUp` (deletes the test characters' `pending_purchases` rows, then their character rows, through `PgTestDatabase.DeleteCharacters`).
- [x] **Migration 003**: `tools/db/migrations/003_pending_purchases_request_unique.sql` adds `CREATE UNIQUE INDEX ux_pending_purchases_char_request ON pending_purchases (char_id, request_id)`; `tools/db/README.md` lists it as step 3 (runs after 002, not re-runnable, how to check it ran). It is applied to the dev database before the tests run.
- [x] **One transaction (Begin)**: `BeginPurchaseAsync` issues the INSERT and the gold UPDATE (`gold_balance = @goldBalance, gold_version = @goldVersion WHERE character_id = @charId`, no `save_version` change) in one `NpgsqlTransaction`. With a test trigger that makes the UPDATE raise for one test character, the call ends faulted, `pending_purchases` has no row for it, and the character row is unchanged.
- [x] **One transaction (Refund)**: `RefundPurchaseAsync` deletes the row and writes the gold in one transaction. With the same trigger, the call ends faulted and the purchase row is still there.
- [x] **Idempotent Begin**: a second `BeginPurchaseAsync` with the same (`char_id`, `request_id`) as an outstanding record returns that record's id and writes nothing (no second row, gold unchanged from the first call). Implemented with `INSERT … ON CONFLICT (char_id, request_id) DO NOTHING RETURNING id`; when no id comes back, the existing id is read and the gold UPDATE is skipped.
- [x] **Idempotent Begin under concurrency**: two `BeginPurchaseAsync` calls with the same (`char_id`, `request_id`) started together on two connections both return the same id; the table holds one row for that request; neither call faults.
- [x] **Generated id**: the `BIGSERIAL` id is returned, greater than 0, and increases from one new purchase to the next.
- [x] **Unknown character**: `BeginPurchaseAsync` for a `char_id` with no character row returns 0 and writes nothing — the foreign-key violation (`SqlState 23503`) is caught inside the store, the transaction rolled back, and no exception reaches the caller.
- [x] **Complete / Refund outcomes**: `CompletePurchaseAsync` and `RefundPurchaseAsync` return `Applied` when they delete the row, `NotFound` when no row has that id (including a second call), `OtherCharacter` when the row belongs to another character — and only `Applied` writes anything (a refund's gold included).
- [x] **`LoadOutstandingAsync`**: returns only that character's rows (all stored rows are `GoldDebited`: Completed and Refunded records are deleted, never stored), in ascending id order, as a new list, never null; the query filters on `char_id`, which `ix_pending_purchases_char_id` serves.
- [x] **Cancellation and faults**: the token reaches every Npgsql / Dapper call; a cancelled token ends the task cancelled; a database failure other than the foreign-key case ends the task faulted (Story 014's failure contract).

---

## Implementation Notes

- **Column name**: the table's column is `char_id` (ADR-006 DDL, migration 002). The manifest line 55 and the Story 011 review note say `(character_id, request_id)`; that means the character column of `pending_purchases`, i.e. `char_id`. Correct the manifest wording at its next regeneration (not in this story).
- **Idempotent conflict path under READ COMMITTED**: a second transaction's `INSERT … ON CONFLICT DO NOTHING` waits for the first transaction's row; if the first commits, it inserts nothing and the following `SELECT id … WHERE char_id = @c AND request_id = @r` sees the committed row; if the first rolls back, the second inserts. Skip the gold UPDATE whenever the INSERT returned no id.
- **Complete / Refund SQL**: `DELETE FROM pending_purchases WHERE id = @id AND char_id = @c RETURNING id`; when nothing is deleted, a `SELECT char_id … WHERE id = @id` decides `NotFound` versus `OtherCharacter`. The refund's gold UPDATE follows the DELETE in the same transaction. A refund whose character row is missing cannot happen (foreign key); if the UPDATE affects 0 rows anyway, throw (faulted task), as the in-memory store does.
- **Test trigger** (user decision 2026-10-26, instead of a production test hook): the test creates `BEFORE UPDATE ON character_records FOR EACH ROW WHEN (OLD.character_id = <test id>)` with a function that `RAISE EXCEPTION`s, and drops both in `TearDown` (also when the test fails). Test ids stay in the 9 000 000 range. The `irongrind` role owns the tables, so it may create the trigger.
- CR-CP-12 marks a completed or refunded record and deletes it; the mark is never observable, so the store only deletes.
- `RequestId` (`uint`) and the gold values (`uint`) map to `BIGINT` (ADR-006 type mapping notes).

---

## Out of Scope

- The NPC Shop's handlers and the buy / refund caller order (ADR-001 A2, NPC Shop epic)
- Reconnect reconciliation's caller (Networking Core session contract)
- The write queue, `OutcomeUnknown` mapping and retry — `CharacterPersistenceService`, Story 011
- TD-073 (Dapper under IL2CPP) and TD-082 (late-commit residual risk)
- The full real-database verification run — Story 016

---

## QA Test Cases

- **Shared suite** — Story 011's 23 contract tests against PostgreSQL.
- **Rollback (Begin)** — Given the UPDATE trigger on the test character, When `BeginPurchaseAsync`, Then the task faults, no `pending_purchases` row for it, gold and versions unchanged.
- **Rollback (Refund)** — Given an outstanding purchase and the trigger, When `RefundPurchaseAsync`, Then the task faults and the row is still there.
- **Retry** — Given an outstanding purchase for request R, When `BeginPurchaseAsync` again for R with other gold values, Then the same id, one row, the first call's gold.
- **Concurrent retry** — Given two connections, When both begin request R together, Then one id, one row, no fault.
- **Unknown character** — Given an unknown `char_id`, Then 0, no row, no exception.
- **Outcomes** — Complete twice → `Applied`, then `NotFound`; refund on another character's id → `OtherCharacter`, nothing written.
- **Migration 003** — the index exists (`pg_indexes`) and is unique.

---

## Performance

Off the tick: every call is an async store call that the service queues and tracks (ADR-011). Each write is one round trip of two or three statements in one transaction, inside ADR-006's ≤50 ms P95 write budget (`PERSISTENCE_WRITE_WARNING_MS`). No measurement in this story; Story 016 measures.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlPendingPurchase_tests.cs` (the contract subclass plus this story's own tests) — must exist and pass against the local PostgreSQL (`-testFilter`, batch mode); the run is recorded in the story's Completion Notes. The non-database EditMode suite must stay green.

**Status**: [x] Created and passing — 45 / 45 against the local PostgreSQL 16.15 (batch mode, `-testFilter`, 2026-10-26)

---

## Dependencies

- Depends on: Stories 011 (Complete 2026-10-26), 012, 013, 014 (Complete 2026-10-19)
- Unlocks: Story 016; the NPC Shop epic

---

## Completion Notes

**Completed**: 2026-10-26
**Criteria**: 12 / 12 passing (none deferred)
**Test Evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlPendingPurchase_tests.cs` — the contract subclass (Story 011's 23 tests) and 22 own tests (19 `[Test]` + 3 `[TestCase]`). Batch-mode runs 2026-10-26 after the review fixes: Database fixtures (`-testFilter` on both fixtures, local PostgreSQL 16.15, `IRONGRIND_DEV_PG`) **45 / 45**; EditMode suite 4122 passed / 0 failed / 111 skipped (Explicit fixtures), no `error CS`. No test rows, triggers or functions left in the database afterwards. Migration 003 applied to the dev database `irongrind` on 2026-10-26 (no duplicate `(char_id, request_id)` rows beforehand; index checked in `pg_indexes`).
**Code Review**: Complete — `/code-review` 2026-10-26 (unity-specialist CLEAN, qa-tester GAPS): CHANGES REQUIRED; all 9 items applied (one class `<remarks>` + six types in the Stripping list; `BeginPurchaseCoreAsync` split into `InsertOrFindPurchaseAsync` and a shared `WriteGoldAsync`, longest method 33 lines; `state = 0` filter; mid-call cancel tests for Complete / Refund / Load; 0-row gold-update tests; failing-DELETE test; corrupt-column Load tests; `pg_stat_activity` overlap check in the concurrent test). Two specialist warnings dismissed: purchase writes and saves are serialised by `CharacterPersistenceService`'s per-character write queue; a retry after Complete inserting a new purchase is the interface contract. `/code-review` was not re-run after the fixes. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
**Deviations** (advisory; no tech-debt entry, user's choice):
1. Beyond the story: `LoadOutstandingAsync` and the retry lookup match `state = 0` rows only. A retry whose conflicting row is in another state, or was deleted by a concurrent Complete / Refund between the INSERT and the lookup, faults with `InvalidOperationException`. The state case is tested (`NpgsqlPurchase_RowInAnotherState_IsNeitherLoadedNorMatchedByARetry`); the deletion race is untested by design (needs a forced interleaving).
2. Migration 003 is not in ADR-006's Migration Plan list (001 and 002 only) — add it at the next ADR-006 note.
3. Control manifest line 55 says `(character_id, request_id)`; the column is `char_id` — correct at the next regeneration.
4. TD-073 (Dapper under IL2CPP) unchanged; the three new Dapper types are in `Assets/link.xml`, which preserves them but does not make Dapper IL2CPP-safe.
