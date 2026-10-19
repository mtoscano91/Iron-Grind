# Story 014: The Npgsql Character Record Store

> **Epic**: Character Persistence
> **Status**: Complete (2026-10-19, `/story-done` COMPLETE WITH NOTES) — written 2026-10-12; unblocked 2026-10-19 (Story 012, the libraries and a PostgreSQL instance, and Story 013, the schema applied to the dev database `irongrind`, are Complete); `/story-readiness` 2026-10-19: NEEDS WORK (7 gaps) → fixed.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-16
> **Estimate**: 10 hours (confirmed 2026-10-19; was 8 h provisional — adds the shared store-contract fixture and the slot JSON codec)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-6 (one transaction per save; the version predicate; zero rows → `ConcurrencyConflict`), F-CP-2, CR-CP-8, CR-CP-9.
**Requirement**: `TR-cp-006`, `TR-cp-013`

**ADR Governing Implementation**: ADR-006 — Decision 2 (explicit parameterised SQL through Dapper), Decision 4 (latency thresholds, the 5-second timeout), the type-mapping notes, Amendments 1 and 2; ADR-011 Decision 3; ADR-012 Decision 1 (tests name no Npgsql type).
**ADR Decision Summary**: one `NpgsqlTransaction` per save, an `ExecuteAsync` rows-affected check as the conflict signal, the `CancellationToken` passed to every Npgsql async call.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**:
- Npgsql 8.0.9 and Dapper 2.1.66, as recorded by Story 012 in `docs/engine-reference/unity/plugins/npgsql-dapper.md` and `docs/server-libraries.md`. Do not write Npgsql or Dapper calls from memory — check members against the pinned versions (that file lists the members confirmed in `Npgsql.xml`).
- **IL2CPP:** Dapper's default mapping emits IL at runtime, which IL2CPP cannot run (`npgsql-dapper.md` "IL2CPP risk"). This story is verified under Mono / the Editor only. The choice between a Mono server, Dapper.AOT or plain Npgsql commands stays with the epic (Known Gap 2, likely an ADR-006 amendment) and does not block this story. The slot columns use a hand-written codec, so they add no reflection.
- Avoid the synchronous `NpgsqlConnection.Wait(int)` (known issue under Unity Mono, same file); this store has no reason to call it.

**Control Manifest Rules (this layer)**:
- Required: `ConfigureAwait(false)` on every `await`, including `await using` and `await foreach`; if an awaited call cannot be trusted not to capture the context, the body runs through `Task.Run` after the snapshot — ADR-011
- Required: the store touches only the copy it was given and the database — ADR-011
- Forbidden: Entity Framework Core — ADR-006
- Guardrail: warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006

Manifest 2026-10-16 reviewed: its additions since 2026-10-09 are the ADR-015 relay rules; none apply to this store.

---

## Acceptance Criteria

*Every criterion runs against a real PostgreSQL instance with the Story 013 schema (ADR-006: "no mock substitution"), except the source searches and the in-memory half of the contract fixture.*

- [x] **Implements the seam**: `NpgsqlCharacterRecordStore : ICharacterRecordStore` in `src/ServerLogic/CharacterPersistence/`. Story 002's store tests (`CharacterPersistence_RecordStore_tests.cs`, 60 tests) move into an abstract contract fixture with one subclass per store: all of them pass against `InMemoryCharacterRecordStore`, and every test that does not assert the injected clock's exact value passes against the Npgsql store (fetch, insert, duplicate insert → `false`, the version predicate, isolation of the returned copy). For the Npgsql store the timestamp assertions are "set, and later than the previous value", since the database stamps `NOW()`.
- [x] **Round trip**: a record with distinct values in all 23 fields, full gear and inventory arrays (with enhancement levels), `uint` fields (CharacterID, AccountID, GoldBalance, GoldVersion, LastZoneID, SaveVersion) at `uint.MaxValue`, and a 26-byte UTF-8 name with multi-byte characters is inserted, fetched and equal field for field; each `float` comes back as the same `float` (e.g. CritChance 0.065f). Also with all slots empty.
- [x] **Update is one statement in one transaction** with `WHERE character_id = @id AND save_version = @expected`, sets `save_version = save_version + 1` and `last_saved_at_utc = NOW()`; rows affected is returned to the service.
- [x] **AC-CP-19 (real)**: with the row's `save_version` changed by a second connection before the update, zero rows are affected and nothing else in the row changes.
- [x] **Cancellation and timeout**: the token reaches every Npgsql and Dapper async call. An already-cancelled token ends the call without writing. With the row locked by a second connection's open transaction, a save through `CharacterPersistenceService` with an injected `writeTimeout` of 1 s (production default `PERSISTENCE_WRITE_TIMEOUT_SECONDS` = 5) ends within 3 s with `DatabaseError`, and the row is unchanged after the lock is released.
- [x] **Slot codec**: the `gear_slots` / `inventory_slots` JSON is written and parsed by a hand-written codec (no reflection, no serialiser library). It writes the ADR-006 shapes (7 and 20 entries, `null` = empty); on read, a missing `enhancement_level` is 0 (Amendment 1), and any other shape (wrong length, wrong type, unknown key, malformed JSON) throws `CharacterRecordCorruptException`, as does a `BIGINT` column read back outside the `uint` range. `LoadCharacter` catches that exception before its generic catch and returns `CorruptRecord` (every other store fault stays `DatabaseError`). The codec is unit-tested without a database; the service mapping by one service test with a fake store. *(User decision at `/dev-story` 2026-10-19: the service previously mapped every store fault to `DatabaseError`.)*
- [x] **No context capture**: a source search of the store and codec files finds no `await` without `ConfigureAwait(false)`.
- [x] **Pool**: the store takes an `NpgsqlDataSource` it never owns or disposes; it opens and returns a connection per operation and holds none between calls.
- [x] **No connection string in the store**: the store reads no configuration; a source search of `src/` finds no `Password=` or `Host=` literal.

---

## Implementation Notes

- The SQL is explicit text in this file (ADR-006 Decision 2); Dapper maps parameters and rows. `BIGINT` columns hold the `uint` fields (read as `long`, range-checked back to `uint`); `REAL` maps to `float`.
- Insert: a duplicate `character_id` returns `false` with nothing written (the seam's contract) — e.g. `INSERT ... ON CONFLICT (character_id) DO NOTHING` and rows affected.
- **Where the database tests live (decided 2026-10-19):** `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlRecordStore_tests.cs`, following Story 012's round-trip test: `[Explicit]` and `[Category("Database")]`, `Assert.Ignore` when `IRONGRIND_DEV_PG` is not set, run with `-testFilter` (`tools/postgres/README.md`). The test assembly names no Npgsql type (ADR-012 Decision 1; Story 041's boundary test): it builds the store through an `internal` ServerLogic factory taking a connection string, whose signature names no Npgsql type, returning the store and an `IDisposable` that owns the data source (`InternalsVisibleTo` already exists). Each test works on its own `character_id` range and deletes its rows. CI has no instance: the gate is a local run, and a skipped run is not evidence.
- **The slot JSON (decided 2026-10-19):** the hand-written codec above, passed as a JSONB text parameter (`::jsonb` cast or `NpgsqlDbType.Jsonb`) and read as text. `System.Text.Json` is in `Assets/Plugins/Server/` but is reflection-based, so it is not used here.
- **The connection string (decided 2026-10-19):** not this store's concern. Building the `NpgsqlDataSource` from the environment (ADR-007: `Password=[env]`), its pool size, and its warm-up belong to the composition root — see the EPIC's Known Gap 7 (ADR-006 and ADR-007 disagree on the pool). Tests read `IRONGRIND_DEV_PG`.
- The 60 Story 002 tests move, they are not rewritten: the in-memory subclass keeps every current assertion. *(At implementation: 63 test cases; 27 run against both stores, 36 stay in-memory only — exact-clock values, the fake's fault controls, and the `SaveVersion` wrap.)*
- **Write timeout cancels the store (user decision at `/dev-story` 2026-10-19).** The first database run showed that the service's write timeout only raced a `Task.Delay`: the store got the caller's token alone, so a timed-out UPDATE kept waiting on the lock and committed after it was released. `CharacterPersistenceService.RaceAsync` now gives the store the caller's token linked with `CancelAfter(writeTimeout)` (ADR-006 Decision 4, "hard CancellationToken timeout"), disposed when the call ends; a store that ends cancelled by that token is still reported as a `TimeoutException`. The race and the unknown-outcome marking stay (a cancel can lose to a commit). The test that models a commit winning against the timeout now uses a store that ignores the token.
- **`SaveVersion` wrap — open, not in this story.** The in-memory store wraps `uint.MaxValue + 1` to 0; in the `BIGINT` column `save_version + 1` becomes 4294967296, and the next fetch is `CorruptRecord`. Unreachable in practice (4 billion saves of one character), but the two stores disagree. Recorded as tech debt at `/story-done`; options: `(save_version + 1) % 4294967296` in the UPDATE (an ADR-006 amendment), or treat the maximum as a conflict.

**Performance**: N/A for this story's criteria — the ≤ 50 ms P95 latency and concurrency gates are Story 016's. This story passes the token so the service's 5 s timeout holds.

---

## Out of Scope

- Story 015: `pending_purchases`
- Story 016: the acceptance run and the latency and concurrency gates
- The service's own logic (Stories 003–010), which does not change — except one `catch (CharacterRecordCorruptException)` in `LoadCharacter` (Slot codec criterion) and the write-timeout token in `RaceAsync` (below)
- Building the `NpgsqlDataSource`, its pool settings (EPIC Known Gap 7) and its warm-up — the composition root
- The IL2CPP decision for Dapper (EPIC Known Gap 2)

---

## QA Test Cases

- **Shared store suite** — Given the contract fixture, Then all 60 tests pass for the in-memory store, and every non-clock test passes for the Npgsql store; the Npgsql timestamp variants pass.
- **Round trip** — Given the extreme record above, Then every field is equal after a fetch. Edges: all slots empty; a 26-byte name with multi-byte characters; `uint.MaxValue` in every `uint` field.
- **Conflict from a second connection** — Then zero rows, row unchanged.
- **Cancelled token** — Given a cancelled token, Then the call ends and nothing is written.
- **Timeout** — Given a row locked by another transaction and `writeTimeout` = 1 s, Then the save ends within 3 s with `DatabaseError`; the row is unchanged.
- **Codec** — Given each malformed shape (6 or 8 gear entries, a string `item_id`, an unknown key, truncated JSON), Then a failure; Given an entry without `enhancement_level`, Then level 0.
- **Source** — Then every `await` in the store and codec files is followed by `ConfigureAwait(false)`; `src/` has no `Password=` / `Host=` literal; the test assembly references no Npgsql type.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlRecordStore_tests.cs` passing against the local PostgreSQL 16.15 (run with `IRONGRIND_DEV_PG` set; result recorded in the Completion Notes), plus the contract fixture and codec tests passing in the normal EditMode suite. A run with no instance (tests ignored) does not count.

**Status**: [x] Created and passing 2026-10-19 (database run 159/159)

---

## Dependencies

- Depends on: Story 012 (Complete), Story 013 (Complete), Story 002 (Complete)
- Unlocks: Stories 015, 016

---

## Completion Notes
**Completed**: 2026-10-19
**Criteria**: 9/9 passing (none deferred)
**Deviations** (advisory, all user decisions, recorded above): `LoadCharacter` maps `CharacterRecordCorruptException` to `CorruptRecord`; the service's write timeout now cancels the store's token (`RaceAsync` linked token; found by the first database run, where a timed-out UPDATE committed after the lock was released; ADR-006 Decision 4); a negative inventory count is rejected on encode (the in-memory store still accepts it, pinned by a test); Story 002's file had 63 test cases, not 60. Open divergence: `SaveVersion` wrap at `uint.MaxValue` — TD-075, pinned by a database test.
**Test Evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlRecordStore_tests.cs` against the local PostgreSQL 16.15 with `IRONGRIND_DEV_PG` set for the batch process only: 159/159 (Npgsql contract 29, store 35, codec 95), 2026-10-19 20:31Z. Full EditMode suite 20:30Z: 3322 total, 3257 passed, 0 failed, 65 skipped (64 Explicit database tests + 1 existing skip).
**Code Review**: Complete — `/code-review` (unity-specialist + qa-tester) CHANGES REQUIRED, 7 required items and all suggestions applied ("fix all"): dispose failures never replace a committed result; `Assets/link.xml` preserves the Dapper-only DTOs; `RaceAsync` hardening; codec split; stronger timestamp, pool, mid-flight cancel, reclassification and range tests; test-only ids ≥ 9,000,000. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
**Scope**: also touched `CharacterPersistenceService.cs`, `Assets/link.xml`, `docs/tech-debt-register.md` (TD-075), Story 002's test file (now the shared contract fixture), the LoadCore / SaveIrreversible / SaveSession tests and the shared fakes.
**Estimate**: 10 h estimated; larger in practice (the timeout fix and the review round).
