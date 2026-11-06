# Story 003: Migration 004 and the `gateway` database role

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 4 h

## Context

**GDD**: `design/gdd/authentication.md` — CR-AUTH-3, CR-AUTH-8 step 4, CR-AUTH-12, CR-AUTH-13 (unique name index)
**Requirement**: `TR-auth-002` (counters persisted, never reused)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 5; Migration Plan step 5), with ADR-006 (note 2026-11-06)
**ADR Decision Summary**: Migration `004_create_accounts.sql` adds `account_id_seq`, `character_id_seq`, `zone_id_seq`, the `accounts` table, the FK from `character_records.account_id`, an index on it, and a case-insensitive unique index on `character_name`. The gateway connects as its own `gateway` role with narrow rights.

**Engine**: none (PostgreSQL 16) | **Risk**: LOW
**Engine Notes**: Dev server is the portable PostgreSQL 16.15 (`tools/postgres/README.md`); migrations are applied by hand with `psql -1 -v ON_ERROR_STOP=1` (`tools/db/README.md`).

**Control Manifest Rules (this layer)**:
- Required: migration `tools/db/migrations/004_create_accounts.sql` exactly as ADR-016 Decision 5 — source: ADR-016 Decision 5
- Required: role `gateway` — all rights on `accounts` and the account and character sequences, `USAGE` on `zone_id_seq`, `INSERT` and `SELECT (character_id, account_id, last_zone_id)` on `character_records`, nothing else; zone roles get no rights on `accounts` — ADR-016 Decision 5, ADR-006 note
  - *Readiness 2026-11-06 (user decision):* there is no zone role today — zones connect as `irongrind`, which owns the database and will own `accounts`. This story does not create a zone role; log the gap as tech debt at `/story-done` (separate least-privilege `zone` role, later hosting/deploy work).
- Forbidden: editing an applied migration (a change is a new numbered script) — `tools/db/README.md`

---

## Acceptance Criteria

- [ ] `tools/db/migrations/004_create_accounts.sql` contains ADR-016 Decision 5's DDL verbatim (three sequences, `accounts`, `fk_character_records_account`, `ix_character_records_account_id`, `ux_character_records_name_lower`)
- [ ] A separate script `tools/db/roles/gateway_role.sql`, run as `postgres` after 004 (`irongrind` has no `CREATEROLE`), creates the `gateway` role (`LOGIN`; no password in the file — set it with `\password gateway` or pass it as a `psql -v` variable) with exactly the grants above
- [ ] `tools/db/README.md`: row 4 in the table, the apply command, how to check it ran, and the dev-database note (character rows whose `account_id` has no account must be given account rows or truncated first; case-insensitive name collisions must be cleaned before the unique index — *code review 2026-11-06: `accounts` starts empty in the same transaction as the FK, so any existing character row fails 004; the README says truncate, after which no name collision or `setval` is possible* — dev databases only, the migration itself stays verbatim)
- [ ] Applied to the dev database `irongrind` and to a throwaway check database; both succeed
- [ ] A gateway `Database`-category test, as the `gateway` role, proves: `nextval('account_id_seq')` works; `SELECT password_hash_phc FROM accounts` works; `SELECT` of any `character_records` column outside the three granted ones (e.g. `character_name`) fails with `42501` (insufficient privilege); `UPDATE character_records` fails with `42501`
- [ ] Inserting a `character_records` row with an `account_id` that has no account fails (`23503`); two character names differing only by case fail (`23505` on `ux_character_records_name_lower`)
- [ ] Every Database test runs inside a transaction it rolls back (no rows left in the dev database); IDs come from `nextval`, and the "no account" case uses `4294967295` (never issued in dev), not a small literal
- [ ] `.github/workflows/tests.yml` `gateway-tests` runs `dotnet test … --filter "TestCategory!=Database"` (CI has no PostgreSQL); the README says how to run the Database category locally

---

## Implementation Notes

*Derived from ADR-016 Decision 5 and Migration Plan step 5:*

- Copy the DDL from ADR-016 Decision 5; do not re-derive types. Sequences are `BIGINT` with `MAXVALUE 4294967295` so values fit the `uint` IDs.
- Check `001_create_character_records.sql` for the real column names (`character_name`, `class_type`, `last_zone_id`, `account_id`) before writing the grants; if a name differs from ADR-016, stop and report — do not silently adapt.
- Gateway connection string comes from config (environment variable / user secrets), never committed. The variable is `IRONGRIND_GATEWAY_DB` (document it next to `IRONGRIND_DEV_PG`); verify `.env*` files by size only (memory: bang commands).
- The gateway test project needs `Npgsql` for this test — Story 004 pins versions. If Story 004 is not done yet, do Story 004's package-pinning step first (that AC belongs to 004).
- `[Category("Database")]` on every test that needs PostgreSQL. With no connection string set, those tests `Assert.Ignore` with a clear message rather than fail.
- Start PostgreSQL via PowerShell `Start-Process` (a Bash-started server dies — memory: portable postgres).

---

## Out of Scope

- Story 006/007: the repository code that uses the table
- Gateway epic: `zone_id_seq` use by the zone launcher
- A separate least-privilege `zone` database role — zones keep connecting as `irongrind` (tech debt; user decision 2026-11-06)

---

## QA Test Cases

- **AC-1**: Migration applies
  - Given: a fresh check database with 001–003 applied
  - When: 004 is applied with `-1 -v ON_ERROR_STOP=1`
  - Then: exit 0; `\d accounts` shows the seven columns; the three indexes/constraint exist
  - Edge cases: applying 004 twice fails on the second run (not re-runnable) and leaves nothing changed
- **AC-2**: Grants
  - Given: the `gateway` role
  - When: it runs `nextval` on all three sequences, `SELECT` on `accounts`, `SELECT character_id, account_id, last_zone_id FROM character_records`, then `SELECT character_name …`, `UPDATE character_records …`, `DELETE FROM character_records …`
  - Then: the first group succeeds; `zone_id_seq` allows `nextval` (USAGE); the second group fails with SQLSTATE `42501`
  - Edge cases: `setval('zone_id_seq', …)` fails (USAGE only)
- **AC-3**: Constraints
  - Given: an account row whose `account_id` comes from `nextval('account_id_seq')`, inside a transaction rolled back at the end
  - When: inserting a character with `account_id = 4294967295` (no account); then two characters `"Grim"` and `"grim"` for that account
  - Then: `23503`; then `23505` with constraint name `ux_character_records_name_lower`

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Db_Migration004_tests.cs` (`[Category("Database")]`), run locally against the dev PostgreSQL — result recorded in Completion Notes

**Status**: [x] Created — 14 Database cases pass against dev PostgreSQL (2026-11-06)

---

## Dependencies

- Depends on: None (package pins from Story 004 if done first — see notes)
- Unlocks: Story 006, Story 007

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 8/8 passing
**Deviations**: (advisory) no least-privilege zone DB role — zones connect as `irongrind`, owner of `accounts`, so ADR-016's "zone roles get no rights on accounts" is unmet (TD-094, user decision 2026-11-06). README dev note says truncate instead of `setval` (004 cannot apply over existing character rows).
**Test Evidence**: `gateway/IronGrind.Gateway.Tests/Db_Migration004_tests.cs` — 14 Database cases pass as role `gateway` against `irongrind` and the throwaway check DB; full gateway suite 194/194, 0 warnings. Applied: 004 as `irongrind`, `roles/gateway_role.sql` as `postgres`; 004 second run fails (exit 3). Role password in `%LOCALAPPDATA%\IronGrind\secrets\gateway.pw`.
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all 5 applied
