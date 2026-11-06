# Database migrations

The PostgreSQL schema of ADR-006 (Character Persistence) and ADR-016 (accounts), as hand-applied SQL scripts. Automated migration
tooling is out of scope until the Hosting Backend ADR defines the deployment pipeline (ADR-006 Migration Plan).

| Order | Script | Creates |
|-------|--------|---------|
| 1 | `migrations/001_create_character_records.sql` | `character_records` |
| 2 | `migrations/002_create_pending_purchases.sql` | `pending_purchases` (foreign key to `character_records`) + `ix_pending_purchases_char_id` |
| 3 | `migrations/003_pending_purchases_request_unique.sql` | unique index `ux_pending_purchases_char_request` on `pending_purchases (char_id, request_id)` |
| 4 | `migrations/004_create_accounts.sql` | sequences `account_id_seq`, `character_id_seq`, `zone_id_seq`; `accounts`; foreign key `fk_character_records_account`; indexes `ix_character_records_account_id` and `ux_character_records_name_lower` |

Run them **in that order**; `002` fails without `001`, `003` runs after `002` and fails without it, `004` fails without `001`. They are **not
re-runnable** (plain `CREATE TABLE` / `CREATE SEQUENCE` / `CREATE UNIQUE INDEX`): run each once per database. A schema change is a new numbered script, never an edit to an applied one.

Nothing records which scripts a database has had (no version table while migrations are manual). To check, run
`\dt` in `psql`: `character_records` means `001` ran, `pending_purchases` means `002` ran. For `003`, run `\di ux_pending_purchases_char_request` in `psql` (or
`SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_pending_purchases_char_request'`): a row means `003` ran. For `004`, `\dt accounts` shows the table and `\ds` lists
the three sequences (`account_id_seq`, `character_id_seq`, `zone_id_seq`).

## Applying them

Start the development server first (`tools/postgres/README.md`). With the portable binaries, `psql.exe` is
`%LOCALAPPDATA%\IronGrind\pgsql16\bin\psql.exe`. From the repository root, in PowerShell:

```
$psql = "$env:LOCALAPPDATA\IronGrind\pgsql16\bin\psql.exe"
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/001_create_character_records.sql
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/002_create_pending_purchases.sql
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/003_pending_purchases_request_unique.sql
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/004_create_accounts.sql
```

`ON_ERROR_STOP=1` makes `psql` stop at the first error and exit non-zero; `-1` (`--single-transaction`) runs the
whole script as one transaction, so a failure leaves nothing behind and the script can simply be run again. `psql` asks for the password, or reads it
from `PGPASSWORD` / a `pgpass` file.

### Dev databases with existing rows (before applying 004)

`004` adds a foreign key and a unique index over existing `character_records` rows, so a dev database that already holds characters needs cleanup first, or the script fails (and, being one transaction, changes nothing):

- Every `character_records.account_id` must have an account row, and `004` creates `accounts` empty in the same transaction as the foreign key. So any existing character row makes `004` fail: truncate first, as `irongrind`:

  ```
  TRUNCATE pending_purchases, character_records;
  ```

- Because `character_records` is then empty, the case-insensitive unique index has nothing to collide with, and the new sequences can start at 1 without hitting an existing `character_id` — no `setval` is needed.

Dev databases only; the migration itself stays verbatim.

## Gateway role

The gateway (ADR-016) connects as its own role `gateway` with narrow rights. `roles/gateway_role.sql` creates it. Run it as `postgres` (`irongrind` has no `CREATEROLE`), after `004`, against each database that should be usable by the gateway:

```
& $psql -h 127.0.0.1 -U postgres -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/roles/gateway_role.sql
& $psql -h 127.0.0.1 -U postgres -d postgres -c "\password gateway"
```

The file holds no password; set it with `\password gateway`. Roles are cluster-wide, so the `CREATE ROLE` is guarded and the script can run on a second database (for example the throwaway check database) without failing. The grants are per database and safe to repeat.

Grants: all rights on `accounts`, `account_id_seq` and `character_id_seq`; `USAGE` on `zone_id_seq`; `INSERT` and `SELECT (character_id, account_id, last_zone_id)` on `character_records`; nothing else. The script relies on PUBLIC's default `CONNECT` on the database and `USAGE` on schema `public`.

Known gap (tech debt, user decision 2026-11-06): zones still connect as `irongrind`, the database owner, so ADR-016's "zone roles get no rights on `accounts`" is not met yet. A separate least-privilege `zone` role is later hosting/deploy work.

### Gateway Database tests

`gateway/IronGrind.Gateway.Tests/Db_Migration004_tests.cs` checks `004` and the grants as role `gateway`. Set `IRONGRIND_GATEWAY_DB` to an Npgsql connection string for that role (for example `Host=127.0.0.1;Port=5432;Database=irongrind;Username=gateway;Password=...`; never committed), then run:

```
dotnet test gateway/IronGrind.Gateway.sln --filter "TestCategory=Database"
```

Without the variable the tests are ignored. Each test rolls its transaction back. CI excludes the category (`TestCategory!=Database`): it has no PostgreSQL.

`Auth_AccountRepository_Registration_tests.cs` commits rows and **TRUNCATEs** `accounts`, `character_records` and `pending_purchases`, so it needs a dedicated test database, `irongrind_auth_test`: create it as `postgres` with `OWNER irongrind`, apply migrations 001-004 as `irongrind`, then apply `tools/db/roles/gateway_role.sql` as `postgres`. Set two variables for it: `IRONGRIND_GATEWAY_DB` (role `gateway`, `Database=irongrind_auth_test`) and `IRONGRIND_GATEWAY_TEST_ADMIN_DB` (role `irongrind`, `Database=irongrind_auth_test`; used only by the fixture for the reset and row checks). Never point either at `irongrind`: the fixture fails if the database name is `irongrind`. `Db_Migration004_tests` can run against `irongrind_auth_test` too.

The Story 012 Database cases (`Gateway_LogRedaction_tests.cs`: stored `password_hash_phc` starts with `$argon2id$`, locked-account handler time under 10 ms) use the same two variables and the same `irongrind_auth_test` database; they run the real gateway host over HTTP and TRUNCATE like the repository tests.

## Checking a script change first

Before applying to `irongrind`, run the scripts on a throwaway database (as `postgres`, or a role with `CREATEDB`):

```
& $psql -h 127.0.0.1 -U postgres -d postgres -c "CREATE DATABASE irongrind_migration_check"
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/001_create_character_records.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/002_create_pending_purchases.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/003_pending_purchases_request_unique.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/004_create_accounts.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/roles/gateway_role.sql
& $psql -h 127.0.0.1 -U postgres -d postgres -c "DROP DATABASE irongrind_migration_check"
```
