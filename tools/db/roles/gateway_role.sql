-- gateway_role.sql
-- The `gateway` database role of ADR-016 Decision 5 (Authentication Story 003): the login/registration
-- gateway connects as this role with narrow rights.
-- Run as `postgres` AFTER migrations/004_create_accounts.sql, against the target database:
--   psql -U postgres -d <database> -1 -v ON_ERROR_STOP=1 -f tools/db/roles/gateway_role.sql
-- Then set the password (it is not in this file): `\password gateway` in psql.
--
-- Roles are cluster-wide, so the CREATE ROLE is guarded and the script runs on every database of the
-- cluster (e.g. irongrind and a throwaway check database). Grants are per database and idempotent.
-- Relies on PUBLIC's default CONNECT on the database and USAGE on schema public (no grant for either).
--
-- Known gap (tech debt, user decision 2026-11-06): zones still connect as `irongrind`, the database
-- owner, so ADR-016's "zone roles get no rights on accounts" is not met yet. A least-privilege `zone`
-- role is later hosting/deploy work.

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gateway') THEN
        CREATE ROLE gateway LOGIN;
    END IF;
END
$$;

GRANT ALL ON TABLE accounts TO gateway;
GRANT ALL ON SEQUENCE account_id_seq, character_id_seq TO gateway;
GRANT USAGE ON SEQUENCE zone_id_seq TO gateway;
GRANT INSERT ON character_records TO gateway;
GRANT SELECT (character_id, account_id, last_zone_id) ON character_records TO gateway;
