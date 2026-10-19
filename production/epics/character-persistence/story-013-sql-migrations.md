# Story 013: SQL Migrations for `character_records` and `pending_purchases`

> **Epic**: Character Persistence
> **Status**: Ready — written 2026-10-12; unblocked 2026-10-19 (Story 012 Complete; a local PostgreSQL 16.15 runs on the development machine, portable binaries, `tools/postgres/README.md` "Without Docker"; Docker is not available there); `/story-readiness` 2026-10-19: NEEDS WORK (7 gaps) → fixed.
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-10-16
> **Estimate**: 2.5 hours (confirmed 2026-10-19; includes the ADR-006 Amendment 2 column type)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-2 (the record's fields), CR-CP-9 (the Level 1 defaults), CR-CP-12.
**Requirement**: `TR-cp-002`, `TR-cp-012`

**ADR Governing Implementation**: ADR-006 — "Key Interfaces" (the two DDL statements and their type-mapping notes), Amendment 1 (`inventory_slots` entries carry `enhancement_level`), Amendment 2 (`last_zone_id` is `BIGINT`, 2026-10-19), Migration Plan (`tools/db/migrations/001_create_character_records.sql`, `002_create_pending_purchases.sql`, applied by hand for MVP).
**ADR Decision Summary**: one table per record kind; C# `uint` fields map to `BIGINT`; gear and inventory slots are JSONB columns.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: PostgreSQL is the sole persistence store — ADR-006
- Forbidden: SQLite for the persistence store — ADR-006

---

## Acceptance Criteria

- [ ] **Files**: `tools/db/migrations/001_create_character_records.sql` and `002_create_pending_purchases.sql` exist and hold the DDL of ADR-006 "Key Interfaces" (as amended), column for column, with the index `ix_pending_purchases_char_id`.
- [ ] **No other indexes**: `001` creates no index besides the primary key (no `account_id` index — see Implementation Notes).
- [ ] **Defaults**: every column default of `character_records` equals the Level 1 value of CR-CP-9 / F-CP-3 (or `'[]'` / `NOW()` for the JSONB and timestamp columns); `last_zone_id` is `BIGINT DEFAULT 0` (ADR-006 Amendment 2).
- [ ] **`inventory_slots` shape**: a comment in the first script states the JSONB entry shape of ADR-006 Amendment 1 (item, count, enhancement level) and of `gear_slots`.
- [ ] **Applies cleanly**: both scripts run in order with `psql -v ON_ERROR_STOP=1` against a newly created empty scratch database (e.g. `irongrind_migration_check`, dropped afterwards) with no error; `002` alone, on another fresh scratch database, fails because `character_records` does not exist.
- [ ] **Dev database**: both scripts are then applied to the development database `irongrind` (`tools/postgres/README.md`), so Stories 014 and 015 have the tables.
- [ ] **A note on how to apply them** is in `tools/db/README.md`: the order, the `psql -v ON_ERROR_STOP=1 -f` command with the portable `psql.exe` path, the scratch-database check, and that the scripts are not re-runnable (plain `CREATE TABLE`).

---

## Implementation Notes

- Copy the DDL from the ADR; do not redesign it. If a column of CR-CP-2 has no counterpart in the DDL, stop and report it.
- The ADR leaves automated migration tooling out of scope until the hosting ADR defines the deployment pipeline.
- **No index on `account_id`** (decided at `/story-readiness` 2026-10-19). `LoadCharacter` looks up by `character_id`, the primary key. Authentication CR-AUTH-11 step 1 resolves `AccountID → CharacterID` "from the account-character mapping", whose store no GDD or ADR defines yet; whoever designs that store adds an index if it reads `character_records` by account. `001` therefore has no index statements, although the ADR Migration Plan says "`CREATE TABLE` + indexes".
- `last_zone_id` is `BIGINT` per ADR-006 Amendment 2 (2026-10-19) — `ZoneID` wraps `uint`. Use the amended DDL.
- `character_name VARCHAR(26)` counts characters, not the GDD's 26 UTF-8 bytes; the database is looser than the rule and the byte limit stays enforced in code. No change.
- `psql.exe` is `%LOCALAPPDATA%\IronGrind\pgsql16\bin\psql.exe`; start the server as in `tools/postgres/README.md` "Without Docker".

**Performance**: N/A — DDL only, no runtime path.

---

## Out of Scope

- Story 014: the SQL statements the store runs
- Automated migrations
- The account store and any `account_id` lookup index

---

## QA Test Cases

- **Smoke** — Setup: a newly created scratch database. Verify: run `001` then `002` with `ON_ERROR_STOP=1`. Pass: no error; `\d character_records` shows the 31 columns of the amended ADR DDL with their types (`last_zone_id bigint`); `\d pending_purchases` shows the foreign key and `ix_pending_purchases_char_id`; `\di` lists no index other than the two primary keys and that one.
- **Order** — Setup: another fresh scratch database. Verify: run `002` alone. Pass: it fails with `relation "character_records" does not exist`, and no `pending_purchases` table is left.
- **Defaults** — Verify: insert a row giving only `character_id`, `account_id`, `character_name`, `class_type`, then read it back. Pass: every column equals the CR-CP-9 table (Level 1, Experience 0, HeldFreePoints 0, STR/DEX/VIT/INT 10, MaxHP 400, MaxMP 220, AttackPower 30, Defense 20, MagicDefense 4, CritChance 0.065, CritMultiplier 1.5, AttackRange 3.0, AttackSpeedMultiplier 1.030, MovementSpeed 5.0, CurrentHP 400, CurrentMP 220, GoldBalance 0, GoldVersion 0, LastZoneID 0, SaveVersion 0); `gear_slots` and `inventory_slots` are `[]`; both timestamps are set. REAL values are compared as `real` literals (e.g. `crit_chance = 0.065::real`).
- **Dev apply** — Verify: run both scripts on `irongrind`. Pass: no error; both tables exist.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: a smoke check recorded in `production/qa/smoke-[date]-character-persistence-013.md` (the four cases above, with the `psql` output).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: a PostgreSQL instance to run against (Story 012, open decision 2) — available since 2026-10-19 (Story 012 Complete; portable PostgreSQL 16.15, see its evidence file)
- Unlocks: Stories 014, 015
