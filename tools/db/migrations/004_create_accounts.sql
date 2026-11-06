-- 004_create_accounts.sql
-- The account store of ADR-016 Decision 5: the three ID sequences (account, character, zone), the
-- accounts table, the foreign key from character_records.account_id, an index on it, and a
-- case-insensitive unique index on character_name. Authentication Story 003.
-- Applied by hand after 003 (see tools/db/README.md). Requires 001: it alters character_records.
-- Not re-runnable: plain CREATE statements, fails if any of the objects exists.
--
-- Run tools/db/roles/gateway_role.sql (as postgres) after this script to create the gateway role.
-- Dev databases with existing character rows need cleanup first (see tools/db/README.md).

CREATE SEQUENCE account_id_seq   AS BIGINT START 1 MINVALUE 1 MAXVALUE 4294967295 NO CYCLE;
CREATE SEQUENCE character_id_seq AS BIGINT START 1 MINVALUE 1 MAXVALUE 4294967295 NO CYCLE;
CREATE SEQUENCE zone_id_seq      AS BIGINT START 1 MINVALUE 1 MAXVALUE 4294967295 NO CYCLE;  -- amended 2026-11-06 (CR-ZI-1)

CREATE TABLE accounts (
    account_id           BIGINT       PRIMARY KEY,
    username_lower       VARCHAR(32)  NOT NULL UNIQUE,
    password_hash_phc    TEXT         NOT NULL,
    created_at_utc       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    is_banned            BOOLEAN      NOT NULL DEFAULT FALSE,
    login_failure_count  INTEGER      NOT NULL DEFAULT 0,
    lockout_until_utc    TIMESTAMPTZ  NULL
);

ALTER TABLE character_records
    ADD CONSTRAINT fk_character_records_account
    FOREIGN KEY (account_id) REFERENCES accounts (account_id);
CREATE INDEX ix_character_records_account_id ON character_records (account_id);
CREATE UNIQUE INDEX ux_character_records_name_lower ON character_records (lower(character_name));
