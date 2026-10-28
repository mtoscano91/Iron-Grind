# Character Persistence Story 016 — Real-Database Verification Run

> **Story**: `production/epics/character-persistence/story-016-real-database-verification-run.md`
> **Date**: 2026-10-28
> **Result**: PASS — 7/7 items PASS; AC-CP-27, 28, 29 NOT RUN by design (owned by other stories)

## Setup

| Item | Value |
|------|-------|
| Machine | Developer laptop, Windows 11 Home 10.0.26200, Intel Core i7-10510U @ 1.80 GHz, 16 GB RAM |
| PostgreSQL | 16.15 (portable EDB binaries, `%LOCALAPPDATA%\IronGrind`), local on the same machine, 127.0.0.1:5432 |
| Client | Npgsql 8.0.9 + Dapper 2.1.66 (`Assets/Plugins/Server/`), `NpgsqlCharacterRecordStore` |
| Engine | Unity 6000.3.10f1, **Editor batch mode** (`-batchmode -runTests -testPlatform EditMode -testFilter IronGrind.Tests.EditMode.CharacterPersistence.CharacterPersistence_RealDatabase_Tests`), `IRONGRIND_DEV_PG` set for the batch process only |
| Tests | `tests/EditMode/CharacterPersistence/CharacterPersistence_RealDatabase_tests.cs` — fixture `CharacterPersistence_RealDatabase_Tests`, `[Explicit]` `[Category("Database")]`, 18 tests, character ids 9_001_601–618 |
| Run | 18 total / 18 passed / 0 failed (final run after `/code-review` fixes) |
| CI | Not run in CI (no PostgreSQL service) — ADR-006 Validation Criteria note 2026-10-28 |

## Results

| Item | Result | Observation |
|------|--------|-------------|
| Part A against PostgreSQL (AC-CP-13, 14, 16, 17, 18, 20, 22, 23, 24, 25, 30, 31) | PASS | One test each (`RealDatabase_AcCpNN_…`), same assertion as the Part A counterpart named in its doc comment. Differences: AC-CP-14's failure is a real UPDATE with a version no row matches, then a throw (stored `save_version` asserted unchanged); AC-CP-16 calls `SaveSession(SessionTTLExpiry)` directly instead of driving `SessionEndCoordinator` (LastSavedAtUtc later than a backdated value, `SaveVersion` N + 1). |
| AC-CP-27, 28, 29 | NOT RUN (by design) | Owned by Enhancement Story 011 and the Respec / Consumable Use systems; do not block this story. |
| AC-CP-15 (real) | PASS | Save on clean logout; data source, store, service and in-memory systems rebuilt over the same database; all 20 base stats, gold balance + version, last zone, 7 gear slots (changed before logout), 20 inventory slots, CurrentHP / CurrentMP match; `SaveVersion` N + 1; `CreatedAtUtc` unchanged. |
| AC-CP-19 (real) | PASS | A second connection ran `UPDATE … SET save_version = save_version + 1`; `SaveIrreversibleOutcome` → `ConcurrencyConflict`, one Critical log, row keeps the bumped version and the stored level (the live level was raised first). |
| AC-CP-21 — concurrency gate | PASS | `CONCURRENCY_GATE n=50 successes=50 conflicts=0`. No call faulted or was cancelled; 50 recorded update intervals, none overlapping; final `save_version` = start + 50; no error logs. |
| Latency gate | PASS | `LATENCY_GATE n=100 p50=0.81 p95=1.22 p99=1.54 max=1.88 ms` (Stopwatch around the store's `UpdateAsync`, command issue to rows-affected). Limit P95 ≤ 50 ms. PostgreSQL local on the same laptop — not the production host. Earlier runs the same day: p95 0.97 and 1.08 ms. |
| AC-CP-26 | PASS | +9 sword equipped through a real `EquipmentService` in the weapon slot, `SaveSession`; everything incl. the data source discarded and rebuilt; load → stored and live weapon slot hold the sword at +9. |
| AC-CP-5 (real) | PASS | Second connection held `LOCK TABLE character_records IN ACCESS EXCLUSIVE MODE` in an open transaction. `LoadCharacter` with a 10 s token → `DatabaseError` at **10 006 ms**, 0 error logs. Ended by the caller's `CHARACTER_LOAD_TIMEOUT_SECONDS` token (CR-CP-8), under `CONNECTING_TIMEOUT_SECONDS` (30 s). The store's 5 s command timeout did **not** end the blocked fetch and PostgreSQL logged no cancel — TD-084. |

## Notes

- Full EditMode suite after the change: 4588 total / 4459 passed / 0 failed / 129 skipped (the 18 Explicit tests are skipped in the default run).
- Not covered here (epic Definition of Done, still open): ADR-006 Verification 1–3 on the headless Linux build.
