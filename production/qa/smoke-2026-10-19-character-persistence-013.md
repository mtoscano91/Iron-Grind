# Smoke Check — Character Persistence Story 013 (SQL migrations)

**Date**: 2026-10-19 · **Story**: `production/epics/character-persistence/story-013-sql-migrations.md` · **Type**: Config/Data
**Server**: portable PostgreSQL 16.15 on 127.0.0.1:5432 (`tools/postgres/README.md` "Without Docker"); `psql` 16.15 with `-X -v ON_ERROR_STOP=1`.
**Scripts**: `tools/db/migrations/001_create_character_records.sql`, `002_create_pending_purchases.sql` (ADR-006 Key Interfaces, Amendments 1 and 2).

**Verdict: PASS (4/4)**

| Case | Setup | Result | Verdict |
|------|-------|--------|---------|
| 1 Smoke | New scratch DB `irongrind_migration_check` (as `postgres`) | `001` and `002` exit 0; `\d character_records` shows the 31 columns of the amended DDL with their types, `last_zone_id bigint`; `\d pending_purchases` shows the FK `pending_purchases_char_id_fkey` and `ix_pending_purchases_char_id`; `\di` lists only `character_records_pkey`, `pending_purchases_pkey`, `ix_pending_purchases_char_id` | PASS |
| 2 Order | New scratch DB `irongrind_migration_order` | `002` alone: `ERROR: relation "character_records" does not exist`, exit 3; 0 tables left in `public` | PASS |
| 3 Defaults | Row inserted with only `character_id`, `account_id`, `character_name`, `class_type` | Every column equals CR-CP-9 / F-CP-3 (single boolean check, REAL compared as `::real` literals: `t`); slots `[]`; timestamps set. Extra: `last_zone_id` accepts 4294967295 (uint max) | PASS |
| 4 Dev apply | `irongrind` as `irongrind` (neither table existed before) | `001` and `002` exit 0; `\dt` lists both tables, owner `irongrind` | PASS |

Both scratch databases were dropped afterwards. Passwords were read from `%LOCALAPPDATA%\IronGrind\secrets` into `PGPASSWORD` and are not in this output.

## psql output

```
### psql version
psql (PostgreSQL) 16.15

### Case 1 Smoke: fresh scratch DB, 001 then 002
NOTICE:  database "irongrind_migration_check" does not exist, skipping
DROP DATABASE
CREATE DATABASE
CREATE TABLE
001 exit=0
CREATE TABLE
CREATE INDEX
002 exit=0
                            Table "public.character_records"
         Column          |           Type           | Collation | Nullable |   Default   
-------------------------+--------------------------+-----------+----------+-------------
 character_id            | bigint                   |           | not null | 
 account_id              | bigint                   |           | not null | 
 character_name          | character varying(26)    |           | not null | 
 class_type              | smallint                 |           | not null | 
 level                   | integer                  |           | not null | 1
 experience              | integer                  |           | not null | 0
 held_free_points        | integer                  |           | not null | 0
 str                     | integer                  |           | not null | 10
 dex                     | integer                  |           | not null | 10
 vit                     | integer                  |           | not null | 10
 int_stat                | integer                  |           | not null | 10
 max_hp                  | integer                  |           | not null | 400
 max_mp                  | integer                  |           | not null | 220
 attack_power            | integer                  |           | not null | 30
 defense                 | integer                  |           | not null | 20
 magic_defense           | integer                  |           | not null | 4
 crit_chance             | real                     |           | not null | 0.065
 crit_multiplier         | real                     |           | not null | 1.5
 attack_range            | real                     |           | not null | 3.0
 attack_speed_multiplier | real                     |           | not null | 1.030
 movement_speed          | real                     |           | not null | 5.0
 current_hp              | real                     |           | not null | 400
 current_mp              | real                     |           | not null | 220
 gold_balance            | bigint                   |           | not null | 0
 gold_version            | bigint                   |           | not null | 0
 last_zone_id            | bigint                   |           | not null | 0
 gear_slots              | jsonb                    |           | not null | '[]'::jsonb
 inventory_slots         | jsonb                    |           | not null | '[]'::jsonb
 save_version            | bigint                   |           | not null | 0
 created_at_utc          | timestamp with time zone |           | not null | now()
 last_saved_at_utc       | timestamp with time zone |           | not null | now()
Indexes:
    "character_records_pkey" PRIMARY KEY, btree (character_id)
Referenced by:
    TABLE "pending_purchases" CONSTRAINT "pending_purchases_char_id_fkey" FOREIGN KEY (char_id) REFERENCES character_records(character_id)

                                       Table "public.pending_purchases"
   Column   |           Type           | Collation | Nullable |                    Default                    
------------+--------------------------+-----------+----------+-----------------------------------------------
 id         | bigint                   |           | not null | nextval('pending_purchases_id_seq'::regclass)
 char_id    | bigint                   |           | not null | 
 request_id | bigint                   |           | not null | 
 item_id    | bigint                   |           | not null | 
 quantity   | integer                  |           | not null | 
 total_cost | bigint                   |           | not null | 
 state      | smallint                 |           | not null | 0
 created_at | timestamp with time zone |           | not null | now()
Indexes:
    "pending_purchases_pkey" PRIMARY KEY, btree (id)
    "ix_pending_purchases_char_id" btree (char_id)
Foreign-key constraints:
    "pending_purchases_char_id_fkey" FOREIGN KEY (char_id) REFERENCES character_records(character_id)

                              List of relations
 Schema |             Name             | Type  |  Owner   |       Table       
--------+------------------------------+-------+----------+-------------------
 public | character_records_pkey       | index | postgres | character_records
 public | ix_pending_purchases_char_id | index | postgres | pending_purchases
 public | pending_purchases_pkey       | index | postgres | pending_purchases
(3 rows)

31

### Case 3 Defaults: insert required columns only, read back
INSERT 0 1
-[ RECORD 1 ]-----------+------------------------------
character_id            | 1
account_id              | 1
character_name          | SmokeTest
class_type              | 0
level                   | 1
experience              | 0
held_free_points        | 0
str                     | 10
dex                     | 10
vit                     | 10
int_stat                | 10
max_hp                  | 400
max_mp                  | 220
attack_power            | 30
defense                 | 20
magic_defense           | 4
crit_chance             | 0.065
crit_multiplier         | 1.5
attack_range            | 3
attack_speed_multiplier | 1.03
movement_speed          | 5
current_hp              | 400
current_mp              | 220
gold_balance            | 0
gold_version            | 0
last_zone_id            | 0
gear_slots              | []
inventory_slots         | []
save_version            | 0
created_at_utc          | 2026-10-19 16:33:45.899898-03
last_saved_at_utc       | 2026-10-19 16:33:45.899898-03

t
bigint|4294967295
UPDATE 1
uint max into last_zone_id exit=0
DROP DATABASE

### Case 2 Order: fresh scratch DB, 002 alone
NOTICE:  database "irongrind_migration_order" does not exist, skipping
DROP DATABASE
CREATE DATABASE
psql:tools/db/migrations/002_create_pending_purchases.sql:17: ERROR:  relation "character_records" does not exist
002 alone exit=3
0
DROP DATABASE

### Case 4 Dev apply: irongrind as irongrind
0
CREATE TABLE
001 exit=0
CREATE TABLE
CREATE INDEX
002 exit=0
               List of relations
 Schema |       Name        | Type  |   Owner   
--------+-------------------+-------+-----------
 public | character_records | table | irongrind
 public | pending_purchases | table | irongrind
(2 rows)

```

## Addendum — `-1` (single transaction), after `/code-review` 2026-10-19

The README commands now pass `-1`. Checked on a new scratch database (dropped afterwards): `001` and `002` with `-1` exit 0. Then, with `pending_purchases` dropped and an index named `ix_pending_purchases_char_id` planted on `character_records`, `002` with `-1` fails at its `CREATE INDEX` (`relation "ix_pending_purchases_char_id" already exists`, exit 3) and `pending_purchases` does not exist afterwards: the `CREATE TABLE` was rolled back. PASS.
