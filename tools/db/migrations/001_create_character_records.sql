-- 001_create_character_records.sql
-- The character record table of ADR-006 "Key Interfaces" (as amended), applied by hand for MVP
-- (ADR-006 Migration Plan; see tools/db/README.md). Character Persistence Story 013.
-- Not re-runnable: plain CREATE TABLE, fails if the table exists.
--
-- Type mapping: the C# uint fields (CharacterID, AccountID, GoldBalance, GoldVersion, LastZoneID,
-- SaveVersion) are BIGINT so the full unsigned 32-bit range fits (ADR-006 Amendment 2 for
-- last_zone_id). int_stat avoids the reserved word INT.
--
-- Defaults are the Level 1 values of CR-CP-9 / F-CP-3 (character-persistence.md);
-- last_zone_id 0 is ZoneID.Invalid, the first-login sentinel.
--
-- JSONB slot shapes (null = empty slot):
--   gear_slots      : 7 entries,  [{"item_id": N, "enhancement_level": M}, null, ...]
--   inventory_slots : 20 entries, [{"item_id": N, "count": C, "enhancement_level": M}, null, ...]
--                     (ADR-006 Amendment 1; a missing enhancement_level key reads as 0)
--
-- No index besides the primary key: LoadCharacter reads by character_id. An account_id index is
-- left to the account store's design (Story 013 Implementation Notes).

CREATE TABLE character_records (
    character_id            BIGINT       PRIMARY KEY,
    account_id              BIGINT       NOT NULL,
    character_name          VARCHAR(26)  NOT NULL,
    class_type              SMALLINT     NOT NULL,
    level                   INTEGER      NOT NULL DEFAULT 1,
    experience              INTEGER      NOT NULL DEFAULT 0,
    held_free_points        INTEGER      NOT NULL DEFAULT 0,
    str                     INTEGER      NOT NULL DEFAULT 10,
    dex                     INTEGER      NOT NULL DEFAULT 10,
    vit                     INTEGER      NOT NULL DEFAULT 10,
    int_stat                INTEGER      NOT NULL DEFAULT 10,
    max_hp                  INTEGER      NOT NULL DEFAULT 400,
    max_mp                  INTEGER      NOT NULL DEFAULT 220,
    attack_power            INTEGER      NOT NULL DEFAULT 30,
    defense                 INTEGER      NOT NULL DEFAULT 20,
    magic_defense           INTEGER      NOT NULL DEFAULT 4,
    crit_chance             REAL         NOT NULL DEFAULT 0.065,
    crit_multiplier         REAL         NOT NULL DEFAULT 1.5,
    attack_range            REAL         NOT NULL DEFAULT 3.0,
    attack_speed_multiplier REAL         NOT NULL DEFAULT 1.030,
    movement_speed          REAL         NOT NULL DEFAULT 5.0,
    current_hp              REAL         NOT NULL DEFAULT 400,
    current_mp              REAL         NOT NULL DEFAULT 220,
    gold_balance            BIGINT       NOT NULL DEFAULT 0,
    gold_version            BIGINT       NOT NULL DEFAULT 0,
    last_zone_id            BIGINT       NOT NULL DEFAULT 0,
    gear_slots              JSONB        NOT NULL DEFAULT '[]',
    inventory_slots         JSONB        NOT NULL DEFAULT '[]',
    save_version            BIGINT       NOT NULL DEFAULT 0,
    created_at_utc          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_saved_at_utc       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
