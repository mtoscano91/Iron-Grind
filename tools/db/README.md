# Database migrations

The PostgreSQL schema of ADR-006 (Character Persistence), as hand-applied SQL scripts. Automated migration
tooling is out of scope until the Hosting Backend ADR defines the deployment pipeline (ADR-006 Migration Plan).

| Order | Script | Creates |
|-------|--------|---------|
| 1 | `migrations/001_create_character_records.sql` | `character_records` |
| 2 | `migrations/002_create_pending_purchases.sql` | `pending_purchases` (foreign key to `character_records`) + `ix_pending_purchases_char_id` |
| 3 | `migrations/003_pending_purchases_request_unique.sql` | unique index `ux_pending_purchases_char_request` on `pending_purchases (char_id, request_id)` |

Run them **in that order**; `002` fails without `001`, `003` runs after `002` and fails without it. They are **not
re-runnable** (plain `CREATE TABLE` / `CREATE UNIQUE INDEX`): run each once per database. A schema change is a new numbered script, never an edit to an applied one.

Nothing records which scripts a database has had (no version table while migrations are manual). To check, run
`\dt` in `psql`: `character_records` means `001` ran, `pending_purchases` means `002` ran. For `003`, run `\di ux_pending_purchases_char_request` in `psql` (or
`SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_pending_purchases_char_request'`): a row means `003` ran.

## Applying them

Start the development server first (`tools/postgres/README.md`). With the portable binaries, `psql.exe` is
`%LOCALAPPDATA%\IronGrind\pgsql16\bin\psql.exe`. From the repository root, in PowerShell:

```
$psql = "$env:LOCALAPPDATA\IronGrind\pgsql16\bin\psql.exe"
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/001_create_character_records.sql
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/002_create_pending_purchases.sql
& $psql -h 127.0.0.1 -U irongrind -d irongrind -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/003_pending_purchases_request_unique.sql
```

`ON_ERROR_STOP=1` makes `psql` stop at the first error and exit non-zero; `-1` (`--single-transaction`) runs the
whole script as one transaction, so a failure leaves nothing behind and the script can simply be run again. `psql` asks for the password, or reads it
from `PGPASSWORD` / a `pgpass` file.

## Checking a script change first

Before applying to `irongrind`, run the scripts on a throwaway database (as `postgres`, or a role with `CREATEDB`):

```
& $psql -h 127.0.0.1 -U postgres -d postgres -c "CREATE DATABASE irongrind_migration_check"
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/001_create_character_records.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/002_create_pending_purchases.sql
& $psql -h 127.0.0.1 -U postgres -d irongrind_migration_check -1 -v ON_ERROR_STOP=1 -f tools/db/migrations/003_pending_purchases_request_unique.sql
& $psql -h 127.0.0.1 -U postgres -d postgres -c "DROP DATABASE irongrind_migration_check"
```
