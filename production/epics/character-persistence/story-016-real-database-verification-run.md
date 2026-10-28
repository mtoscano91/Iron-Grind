# Story 016: The Real-Database Verification Run

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-28 (COMPLETE WITH NOTES; see Completion Notes). Written 2026-10-12; `/story-readiness` 2026-10-28: NEEDS WORK (8 gaps) → fixed → Ready.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 10 hours (a new Database fixture of about 20 tests plus the evidence run; the Part A suites stay unchanged)

## Context

**GDD**: `design/gdd/character-persistence.md` — AC-CP-13 to AC-CP-31, and AC-CP-26 (the end-to-end criterion).
**Requirement**: the epic's Definition of Done

**ADR Governing Implementation**: ADR-006 Validation Criteria — "AC-CP-13 through AC-CP-31 … all must pass against a real PostgreSQL instance — no mock substitution"; the write-latency gate (P95 `SaveIrreversibleOutcome` ≤ 50 ms over 100 consecutive saves against a local PostgreSQL); the concurrency gate (AC-CP-21 over 50 concurrent task invocations); the rows-affected check (AC-CP-19 / AC-CP-20). Both gates run on the developer's machine until CI has a PostgreSQL service (ADR-006 note 2026-10-28).
**ADR Decision Summary**: the acceptance of the persistence layer is measured on the database it ships with.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: the run is in the Editor (batch mode or Test Runner, "Database" category); say which in the evidence document.

**Control Manifest Rules (this layer)**:
- Guardrail: persistence write — warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006
- Guardrail: the result of a persistence call is handled by `Drain` on the first tick after its task completes — ADR-011
- Guardrail: database connections — at most 10 pooled per zone process — ADR-006 Amendment 3. The concurrency gate's 50 invocations go through the per-character write queue, so they never hold more than one connection at a time for that character; the test asserts nothing about the pool.

---

## Acceptance Criteria

*Recorded in `production/qa/evidence/character-persistence-016-database-verification.md`, each item PASS / FAIL / NOT RUN with its observation. A FAIL is recorded and escalated; an item NOT RUN blocks `/story-done`. Every test below is in `CharacterPersistence_RealDatabase_tests.cs`, `[Explicit]` and `[Category("Database")]`, and runs against the local PostgreSQL 16.15 (`tools/postgres/README.md`) with `IRONGRIND_DEV_PG` set.*

- [x] **Part A against PostgreSQL (curated)**: for each of AC-CP-13, 14, 16, 17, 18, 20, 22, 23, 24, 25, 30, 31 one test drives `CharacterPersistenceService` with `NpgsqlCharacterRecordStore` and passes the same assertion as its Part A counterpart (named in the test's doc comment). AC-CP-14's write failure comes from a store decorator that throws after the Npgsql call is issued. AC-CP-27, 28 and 29 are owned by Enhancement Story 011 and the Respec / Consumable Use systems, are NOT RUN here by design, and do not block this story; the evidence document says so.
- [x] **AC-CP-15 (real)**: save the character on a clean logout, then load it with a new `CharacterPersistenceService`, a new `NpgsqlCharacterRecordStore` and new in-memory systems over the same database (the "fresh process state"): all 23 persisted fields match the values before logout; `SaveVersion` = N + 1; `CreatedAtUtc` unchanged.
- [x] **AC-CP-19 (real)**: a second `NpgsqlConnection`, opened by the test, runs `UPDATE … SET save_version = save_version + 1` on the row before `SaveIrreversibleOutcome` writes → `ConcurrencyConflict`, one `Critical` log, the row keeps the value the separate connection gave it.
- [x] **AC-CP-21 — concurrency gate** (ADR-006's 50 concurrent invocations; it supersedes the GDD's two-call / 500 ms form, which Story 006 already covers with the in-memory store): 50 `SaveIrreversibleOutcome` calls for one `CharacterID` are started without awaiting in between. Observed: every call returns `Success` or `ConcurrencyConflict`, none faults; the stored `save_version` = start + the number of successes; a store decorator records the start and end of each update, and no two of those intervals overlap.
- [x] **Latency gate**: 100 consecutive awaited `SaveIrreversibleOutcome` calls. A timing decorator around `NpgsqlCharacterRecordStore` records a `Stopwatch` from just before the store's update call to its return (the rows-affected result). Asserts P95 ≤ 50 ms; logs one parsable line `LATENCY_GATE n=100 p50=… p95=… p99=… max=… ms`. The evidence records the figure, the machine, and that PostgreSQL ran locally on it.
- [x] **AC-CP-26**: a character with a +9 weapon equipped through a real `EquipmentService` (built in the test; the zone composition root does not wire it yet — Story 005 carry-over) is saved with `SaveSession`; every in-memory system and the service are discarded and built again, and the character is loaded: the weapon is in the same gear slot at +9.
- [x] **AC-CP-5 (real)** *(revised at `/dev-story` 2026-10-28)*: the test's second connection holds `LOCK TABLE character_records IN ACCESS EXCLUSIVE MODE` in an open transaction (a row lock does not block the store's plain `SELECT`). `LoadCharacter`, called with a `CHARACTER_LOAD_TIMEOUT_SECONDS` (10 s) token, returns `DatabaseError` after at least 10 s and within 12 s of the call — the caller's token ends it (CR-CP-8), under `CONNECTING_TIMEOUT_SECONDS` (30 s). The store's 5 s command timeout does not end the blocked fetch (TD-084). The evidence records the elapsed time and which timeout ended the load.

---

## Implementation Notes

- **No suite swap (decided 2026-10-28).** The Part A suites construct `InMemoryCharacterRecordStore` in their setup and stay that way. This story adds one Database fixture with a curated test per criterion, using shared helpers from `CharacterPersistenceTestFakes.cs` where they fit.
- **Isolation**: each test creates its own `CharacterID` (a stub via `CreateStub`) and deletes its rows in teardown, so tests do not depend on order or on earlier runs.
- The latency gate measures the database round trip, not the tick: it says nothing about the 20 Hz loop, which ADR-011 keeps away from the write.
- "8 hours later" in AC-CP-26 is a restart (new instances over the same database), not a wait.
- The AC-CP-5 delay is a table lock, not the GDD's `pg_sleep(10.5)`: the test cannot change the store's SQL. The first run (2026-10-28) showed the store's 5 s command timeout does not end the blocked fetch; the 10 s caller token does (user decision: accept, TD-084).

**Decisions (`/story-readiness` 2026-10-28)**

1. **Where it runs**: the developer's machine only, against the portable PostgreSQL 16.15. ADR-006's "CI gate" wording is deferred: CI has no PostgreSQL service, and the gates become CI gates when one is added (ADR-006 Validation Criteria, note 2026-10-28).
2. **AC-CP-26 gear**: closed by Story 005 (Complete 2026-10-28); the test builds a real `EquipmentService`.

---

## Out of Scope

- A headless Linux build run (Story 012's verification items and the epic's Definition of Done)
- Load tests beyond the two gates
- Running the Part A suites against both stores (`TestFixtureSource` over every suite) — decided against 2026-10-28
- A PostgreSQL service in CI

---

## QA Test Cases

- **Curated Part A** — Given the Npgsql store, When each listed criterion's scenario runs, Then its Part A assertion holds.
- **Fresh state reload** — Save, rebuild everything, load → 23 fields equal; version N + 1; `CreatedAtUtc` unchanged.
- **Separate connection conflict** — external version bump → `ConcurrencyConflict`, one Critical log.
- **Concurrency** — 50 invocations → no overlapping update intervals; final version = start + successes.
- **Latency** — 100 saves → P95 ≤ 50 ms; one `LATENCY_GATE` line.
- **Restart** — +9 weapon saved, everything rebuilt, loaded → same slot, +9.
- **Slow query** — table lock held past the 10 s load timeout → `DatabaseError` between 10 s and 12 s.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/CharacterPersistence/CharacterPersistence_RealDatabase_tests.cs` (+ `.meta`), `[Explicit]`, `[Category("Database")]`
- `production/qa/evidence/character-persistence-016-database-verification.md` — every item PASS or FAIL with its observation, the `LATENCY_GATE` line, the machine, the PostgreSQL version, and how it was run; nothing NOT RUN other than AC-CP-27/28/29.

**Status**: [x] Created 2026-10-28 — 18/18 passed; evidence doc written

---

## Dependencies

- Depends on: Stories 003, 004, 005, 007, 008, 009, 010, 012, 013, 014, 015 — all Complete
- Unlocks: the epic's Definition of Done

---

## Completion Notes

**Completed**: 2026-10-28
**Criteria**: 7/7 passing (AC-CP-27, 28, 29 NOT RUN by design — owned by other stories)
**Deviations** (advisory):
1. AC-CP-5 revised at `/dev-story` (user decision): the store's 5 s command timeout does not end a lock-blocked async fetch; the 10 s caller token does (CR-CP-8) → `DatabaseError` at 10 006 ms. Logged as TD-084.
2. AC-CP-14's injected failure is a real UPDATE with a version no row matches, then a throw (Part A fails before any write).
3. AC-CP-16 calls `SaveSession(SessionTTLExpiry)` directly instead of driving `SessionEndCoordinator`.
4. AC-CP-21 and AC-CP-5 collect logs with a `LogCapture` and assert on them instead of letting Unity fail on each error.
5. The control manifest's "ADRs Covered" header does not list the ADR-006 note of 2026-10-28 (left for the next regeneration).
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_RealDatabase_tests.cs` (18 tests, 18/18 against PostgreSQL 16.15); `production/qa/evidence/character-persistence-016-database-verification.md` (`LATENCY_GATE … p95=1.22 ms`, `CONCURRENCY_GATE n=50 successes=50 conflicts=0`). Full EditMode 4588 / 4459 passed / 0 failed / 129 skipped.
**Code Review**: Complete — `/code-review` 2026-10-28 APPROVED WITH SUGGESTIONS, all applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
