# Story 013: SQL Migrations for `character_records` and `pending_purchases`

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; unblocked 2026-10-19: Story 012 is Complete and a local PostgreSQL 16.15 runs on the development machine (portable binaries, `tools/postgres/README.md` "Without Docker"; Docker is not available there). Run `/story-readiness` before starting.
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-10-09
> **Estimate**: 2 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-2 (the record's fields), CR-CP-9 (the Level 1 defaults), CR-CP-12.
**Requirement**: `TR-cp-002`, `TR-cp-012`

**ADR Governing Implementation**: ADR-006 — "Key Interfaces" (the two DDL statements and their type-mapping notes), Amendment 1 (`inventory_slots` entries carry `enhancement_level`), Migration Plan (`tools/db/migrations/001_create_character_records.sql`, `002_create_pending_purchases.sql`, applied by hand for MVP).
**ADR Decision Summary**: one table per record kind; C# `uint` fields map to `BIGINT`; gear and inventory slots are JSONB columns.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: PostgreSQL is the sole persistence store — ADR-006
- Forbidden: SQLite for the persistence store — ADR-006

---

## Acceptance Criteria

- [ ] **Files**: `tools/db/migrations/001_create_character_records.sql` and `002_create_pending_purchases.sql` exist and hold the DDL of ADR-006 "Key Interfaces", column for column, with the index `ix_pending_purchases_char_id`.
- [ ] **Defaults**: the column defaults of `character_records` equal the Level 1 values of CR-CP-9 / F-CP-3.
- [ ] **`inventory_slots` shape**: a comment in the first script states the JSONB entry shape of ADR-006 Amendment 1 (item, count, enhancement level) and of `gear_slots`.
- [ ] **Applies cleanly**: both scripts run in order against an empty database with no error; the second fails if the first has not run (the foreign key).
- [ ] **A note on how to apply them** is in `tools/db/README.md`.

---

## Implementation Notes

- Copy the DDL from the ADR; do not redesign it. If a column of CR-CP-2 has no counterpart in the DDL, stop and report it.
- The ADR leaves automated migration tooling out of scope until the hosting ADR defines the deployment pipeline.

**Open decisions for `/story-readiness`**

1. **An index on `account_id`.** The DDL has none; `LoadCharacter` looks up by `character_id` (the primary key), so none may be needed — confirm against the Authentication GDD's lookups.

---

## Out of Scope

- Story 014: the SQL statements the store runs
- Automated migrations

---

## QA Test Cases

- **Smoke** — Setup: an empty PostgreSQL database. Verify: run `001` then `002`. Pass: no error; `\d character_records` shows the columns of the ADR.
- **Order** — Setup: an empty database. Verify: run `002` alone. Pass: it fails on the missing table.
- **Defaults** — Verify: insert a row giving only the required columns. Pass: the row reads back MaxHP 400, MaxMP 220, AttackPower 30.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: a smoke check recorded in `production/qa/smoke-[date].md` (the three cases above).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: a PostgreSQL instance to run against (Story 012, open decision 2) — available since 2026-10-19 (Story 012 Complete; portable PostgreSQL 16.15, see its evidence file)
- Unlocks: Stories 014, 015
