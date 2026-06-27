# ADR-006: Persistence Layer — PostgreSQL + Npgsql + Dapper

## Status
Accepted (2026-06-27)

## Date
2026-06-27

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.4) |
| **Domain** | Core / Persistence (server-side) |
| **Knowledge Risk** | LOW — persistence layer runs in server-side .NET; no Unity engine APIs involved |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/current-best-practices.md` |
| **Post-Cutoff APIs Used** | None — Npgsql and Dapper are .NET ecosystem libraries, not Unity APIs |
| **Verification Required** | (1) Confirm Npgsql Unity package compatibility on headless Linux server build; (2) Confirm Dapper IL2CPP `link.xml` requirements if server uses IL2CPP scripting backend; (3) Verify Npgsql connection pool behaviour under Linux headless Unity process lifecycle |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-004 (NGO networking library, Accepted ✓) — library selection is complete; hosting/persistence explicitly deferred by ADR-004 to this ADR |
| **Enables** | Hosting Backend ADR — the persistence technology choice constrains hosting topology (PostgreSQL server must be low-latency / co-located with game server to satisfy the ≤50ms write budget) |
| **Blocks** | Character Persistence implementation; NPC Shop `PendingPurchase` implementation (ADR-001); Enhancement System implementation; Leveling System implementation; Consumable Use System implementation — all depend on `SaveIrreversibleOutcome` which requires an Accepted persistence ADR |
| **Ordering Note** | Hosting Backend ADR must be authored after this ADR and must honour the co-location constraint in Decision 5. |

## Context

### Problem Statement

`character-persistence.md` defines a complete behavioural contract for the persistence layer (`ICharacterPersistence` interface, optimistic concurrency via `SaveVersion`, event-driven saves, a 23-field character schema) but defers the concrete technology choice to this ADR. Two other ADRs also block on this decision:

- **ADR-001 OQ-ADR1-1** — Should `PendingPurchase` records be stored co-located with character state or in a separate transaction log?
- **ADR-004 OQ-ADR4-2 (OQ-NET-5)** — What is the persistence write latency budget within `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` (200ms)?

No implementation of Character Persistence, NPC Shop, Enhancement System, Leveling System, or Consumable Use System may begin until this ADR is Accepted.

### Constraints

- C# async throughout — all `ICharacterPersistence` methods are `async Task<>` with `CancellationToken` (CR-CP-1)
- Single atomic DB transaction per save (CR-CP-6) — no partial writes
- At-most-one in-flight write per `CharacterID` at any moment (CR-CP-7) — enforced by the service layer, not the DB
- `SaveIrreversibleOutcome` must commit before any outcome broadcast (CR-NET-5 / CR-ENH-11)
- Commit-before-broadcast round-trip budget: `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` = 200ms
- Server is a headless Unity Linux build (Mono or IL2CPP scripting backend — confirmed by Hosting Backend ADR)
- Fixed, stable schema (23 fields) — no need for dynamic query generation or change tracking

### Requirements

- Must support `WHERE save_version = @expected` optimistic concurrency check via rows-affected count
- Must surface rows-affected = 0 as a distinct `ConcurrencyConflict` result code (CR-CP-6)
- Must support a shared DB transaction between `PendingPurchase` INSERT and `TrySpendGold` UPDATE (ADR-001 Decision 2)
- Must provide async query API compatible with .NET `CancellationToken`
- Must fit within ≤50ms P95 write latency budget on a co-located DB server
- Must support connection pooling without per-request connection overhead

## Decision

**The project adopts PostgreSQL as the persistence database, accessed via Npgsql (the canonical C# async PostgreSQL driver) with Dapper as the micro-ORM layer for SQL mapping.**

### 1. Database: PostgreSQL

PostgreSQL is the sole persistence store for character records and `PendingPurchase` records. It provides full ACID compliance, async support via Npgsql, and scales horizontally to multi-zone server deployments where multiple zone server processes may load or save different characters concurrently.

SQLite was rejected (see Alternatives) primarily because WAL mode's single-writer-per-file limit conflicts with the expected topology of multiple zone server processes requiring concurrent character saves.

### 2. Query Strategy: Dapper Micro-ORM

All persistence SQL is written as explicit parameterised SQL, mapped to C# types using Dapper extension methods on `NpgsqlConnection`. Dapper adds a thin layer of parameter binding and result mapping with no change-tracking overhead and minimal reflection footprint.

```csharp
// Optimistic-concurrency UPDATE — core pattern for all saves
var rowsAffected = await conn.ExecuteAsync(
    @"UPDATE character_records
      SET level = @Level, experience = @Experience, held_free_points = @HeldFreePoints,
          str = @Str, dex = @Dex, vit = @Vit, int_stat = @Int,
          max_hp = @MaxHp, max_mp = @MaxMp, attack_power = @AttackPower,
          defense = @Defense, magic_defense = @MagicDefense,
          crit_chance = @CritChance, crit_multiplier = @CritMultiplier,
          attack_range = @AttackRange, attack_speed_multiplier = @AttackSpeedMultiplier,
          movement_speed = @MovementSpeed, current_hp = @CurrentHp, current_mp = @CurrentMp,
          gold_balance = @GoldBalance, gold_version = @GoldVersion, last_zone_id = @LastZoneId,
          gear_slots = @GearSlotsJson::jsonb, inventory_slots = @InventorySlotsJson::jsonb,
          save_version = save_version + 1, last_saved_at_utc = NOW()
      WHERE character_id = @CharacterId AND save_version = @ExpectedVersion",
    record, transaction: tx, commandTimeout: PERSISTENCE_WRITE_TIMEOUT_SECONDS);

if (rowsAffected == 0) return CharacterSaveResult.ConcurrencyConflict;
```

Entity Framework Core is explicitly banned for this system (see Forbidden Patterns).

### 3. PendingPurchase Storage: Separate Table, Same DB (Resolves ADR-001 OQ-ADR1-1)

`PendingPurchase` records are stored in a dedicated `pending_purchases` table in the same PostgreSQL database as character records. The `PendingPurchase` INSERT and the `TrySpendGold` gold-debit UPDATE execute inside the **same `NpgsqlTransaction`**, providing atomicity — if either operation fails, both roll back.

```csharp
// ADR-001 Decision 2: PendingPurchase INSERT and TrySpendGold UPDATE share one transaction
await using var tx = await conn.BeginTransactionAsync(ct);
await conn.ExecuteAsync(InsertPendingPurchaseSql, pendingRecord, transaction: tx);
await conn.ExecuteAsync(UpdateGoldBalanceSql, goldParams, transaction: tx);
await tx.CommitAsync(ct);
```

A separate table (rather than co-located columns in `character_records`) ensures:
- The character save path and the purchase transaction path are independent SQL statements — each can be issued and optimised separately
- `PendingPurchase` records can be queried and reconciled independently during reconnect (ADR-001 Decision 4)
- No nullable PendingPurchase columns bloat the character row for the common case (no in-flight purchase)
- A future `PendingSell` record (ADR-001 OQ-ADR1-2) can be added as a third table without altering `character_records`

### 4. Write Latency Budget: ≤50ms (Resolves OQ-NET-5 / OQ-ADR4-2)

The persistence write budget for `SaveIrreversibleOutcome` is **≤50ms P95 latency**, measured from Npgsql command issue to `rowsAffected` return. This satisfies `ENHANCEMENT_PROCESS_LATENCY_MAX_MS = 200ms` when combined with mobile RTT (≤150ms P95 for the target region) and server validation (< 5ms).

Latency thresholds:

| Constant | Value | Action |
|----------|-------|--------|
| `PERSISTENCE_WRITE_WARNING_MS` | 50 | Log Warning |
| `PERSISTENCE_WRITE_CRITICAL_MS` | 100 | Log Critical alert |
| `PERSISTENCE_WRITE_TIMEOUT_SECONDS` | 5 | Hard CancellationToken timeout — prevents a hung DB from holding a session slot |

The ≤50ms budget requires the PostgreSQL server to be **co-located with the game server** (same host or same-datacenter rack). This becomes a binding constraint on the Hosting Backend ADR.

### 5. Connection Pooling

Npgsql's built-in connection pool is used. No external pooler (PgBouncer) is required at MVP scale (10–50 simultaneous active characters per zone server). The pool is configured via the connection string:

```
Host=localhost;Database=irongrind;Username=srv;Password=<secret>;
Maximum Pool Size=20;Connection Idle Lifetime=60;
```

`Maximum Pool Size=20` provides headroom for concurrent zone-tick processing and background saves without exhausting PostgreSQL connection slots.

### Architecture Diagram

```
Game Server Process (Unity Headless, Linux)
│
├── CharacterPersistenceService : ICharacterPersistence
│   ├── NpgsqlDataSource (connection pool, Max Pool Size=20)
│   ├── Dapper SQL helpers (explicit parameterised SQL)
│   └── Per-CharacterID write-queue (CR-CP-7 invariant — one write in flight)
│
└── PostgreSQL Server (same datacenter / co-located — see Decision 4)
    ├── character_records      (23 persisted fields + save_version + timestamps)
    └── pending_purchases      (FK → character_records.character_id)
```

### Key Interfaces

#### character_records DDL

```sql
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
    last_zone_id            INTEGER      NOT NULL DEFAULT 0,
    gear_slots              JSONB        NOT NULL DEFAULT '[]',
    inventory_slots         JSONB        NOT NULL DEFAULT '[]',
    save_version            BIGINT       NOT NULL DEFAULT 0,
    created_at_utc          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_saved_at_utc       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
```

> **Type mapping notes**: C# `uint` fields (CharacterID, AccountID, GoldBalance, GoldVersion, SaveVersion) map to `BIGINT` to hold the full unsigned 32-bit range without overflow. `int_stat` avoids the SQL reserved word `INT`. `gear_slots` JSONB encodes `[{"item_id": N, "enhancement_level": M}, null, ...]` (7 slots, null = empty). `inventory_slots` JSONB encodes `[{"item_id": N, "count": C}, null, ...]` (20 slots, combining the GDD's parallel `InventorySlots[]` + `InventoryItemCounts[]` arrays — Character Persistence translates at load/save boundaries per CR-CP-3 step 5 and CR-CP-10 step 4).

#### pending_purchases DDL

```sql
CREATE TABLE pending_purchases (
    id          BIGSERIAL    PRIMARY KEY,
    char_id     BIGINT       NOT NULL REFERENCES character_records(character_id),
    request_id  BIGINT       NOT NULL,
    item_id     BIGINT       NOT NULL,
    quantity    INTEGER      NOT NULL,
    total_cost  BIGINT       NOT NULL,
    state       SMALLINT     NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_pending_purchases_char_id ON pending_purchases(char_id);
```

`state` maps to `PendingPurchaseState` enum: `GoldDebited = 0`, `Refunded = 1`, `Completed = 2` (ADR-001 Decision 2).

#### IL2CPP link.xml requirement

If the server uses IL2CPP scripting backend, the following `link.xml` entry is required to prevent stripping of Npgsql and Dapper reflection paths:

```xml
<assembly fullname="Npgsql" preserve="all" />
<assembly fullname="Dapper" preserve="all" />
```

DDL migration scripts are maintained in `tools/db/migrations/` and applied before server deployment.

## Alternatives Considered

### Alternative 1: SQLite (Microsoft.Data.Sqlite, WAL mode)
- **Description**: Embedded, zero-config, single-file DB. WAL mode allows one concurrent writer + multiple readers.
- **Pros**: Zero infrastructure — the DB is a file alongside the server binary. No external server to manage.
- **Cons**: WAL mode limits to one writer per file. In a multi-zone-server deployment where each zone server saves different characters concurrently, all writers serialise through a single file lock. A zone server crash holding the write lock can leave the WAL in a state requiring a recovery journal step. Migrating from SQLite to PostgreSQL post-launch is a non-trivial data migration.
- **Rejection Reason**: The `ICharacterPersistence` interface cleanly abstracts the DB, so dev-environment simplicity does not require SQLite — a local Docker PostgreSQL instance achieves zero-external-team-dependency. SQLite's single-writer limit creates a horizontal scaling ceiling that must be removed before any multi-server deployment.

### Alternative 2: SQLite for dev / PostgreSQL for production
- **Description**: Two concrete `ICharacterPersistence` implementations — SQLite for local development, PostgreSQL for staging and production.
- **Pros**: Zero setup for local dev; production gets PostgreSQL durability.
- **Cons**: Maintains two implementations that must stay in sync. Subtle SQL dialect differences (JSONB vs TEXT, `RETURNING` clause availability) create test-vs-production divergence. The dev convenience benefit is achievable via a single `docker compose up postgres` command.
- **Rejection Reason**: Dual-implementation maintenance cost outweighs the convenience. Dev uses the same PostgreSQL image via Docker from day one.

### Alternative 3: Entity Framework Core
- **Description**: Full ORM with schema migrations, LINQ queries, and change tracking.
- **Pros**: Declarative schema management, auto-generated migrations, C# LINQ composition.
- **Cons**: Heavy reflection footprint requiring extensive `link.xml` entries for IL2CPP. Change tracking overhead is wasted when every save is a full-record UPDATE (no partial dirty tracking benefit). EF Core migrations introduce deployment risk for a stable, rarely-changing schema.
- **Rejection Reason**: Overkill for a 23-field fixed schema with a handful of query patterns. IL2CPP risk not justified.

### Alternative 4: Raw ADO.NET (no micro-ORM)
- **Description**: Direct `NpgsqlCommand` construction with manual parameter binding and `NpgsqlDataReader` result mapping.
- **Pros**: Zero additional dependency; maximum transparency.
- **Cons**: Manual mapping of 23 fields in the load path increases bug surface with no performance advantage over Dapper. Dapper's parameter binding replaces the same boilerplate.
- **Rejection Reason**: Dapper provides meaningful developer-experience improvement with negligible overhead.

## Consequences

### Positive
- `WHERE character_id = @id AND save_version = @expected` maps to a single async `UPDATE` with rows-affected check — the CR-CP-6 optimistic concurrency pattern is idiomatic and zero-overhead.
- Npgsql's built-in connection pool eliminates per-request connection overhead.
- PostgreSQL JSONB columns for `gear_slots` and `inventory_slots` allow the character record to be saved and loaded as a single row with no join tables — `SaveSession` is one UPDATE statement.
- `PendingPurchase` INSERT and gold-debit UPDATE share one `NpgsqlTransaction`, satisfying ADR-001 Decision 2 atomicity.
- `ICharacterPersistence` (CR-CP-1) isolates all call sites from the DB technology — a future DB migration requires only a new concrete implementation.

### Negative
- Requires a running PostgreSQL server in all environments. Mitigated by a one-command Docker Compose setup.
- Npgsql and Dapper add two external dependencies that must be maintained across Unity version upgrades.
- If the server uses IL2CPP, `link.xml` entries are required; omitting them causes runtime `TypeLoadException` in production.

### Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| PostgreSQL write latency exceeds 50ms P95 due to hosting topology (DB not co-located) | Medium | High | Hosting Backend ADR must enforce co-location constraint. CI integration test asserts P95 write latency ≤ 50ms against a local PostgreSQL instance. |
| Npgsql or Dapper IL2CPP incompatibility if server uses IL2CPP | Low | High | Verify `link.xml` requirements before first IL2CPP server build. Use Mono for Linux headless server builds (typical) to avoid AOT issues. |
| `gear_slots` / `inventory_slots` JSONB deserialization adds latency at load | Low | Low | Dapper maps JSONB columns as `string`; `System.Text.Json` deserialization of fixed-size arrays (7 gear, 20 inventory) is fast. Profile at integration test milestone. |
| PostgreSQL connection pool exhaustion under burst zone load | Low | Medium | Monitor active connections in soak test. Pool size of 20 is configurable; increase if profiling indicates saturation. |

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| `character-persistence.md` | CR-CP-1 — `ICharacterPersistence` async interface | `CharacterPersistenceService : ICharacterPersistence` implemented over Npgsql + Dapper; the interface contract is unchanged |
| `character-persistence.md` | CR-CP-6 — single DB transaction per save; `WHERE save_version = @expected` predicate; rows-affected = 0 → `ConcurrencyConflict` | One `NpgsqlTransaction` per save; `ExecuteAsync` rows-affected check provides the conflict signal |
| `character-persistence.md` | CR-CP-7 — at-most-one write in flight per CharacterID | Enforced by the service-layer per-CharacterID async queue; the DB provides no independent enforcement |
| `character-persistence.md` | CR-CP-8 — `CHARACTER_LOAD_TIMEOUT_SECONDS = 10` load timeout | `CancellationToken` propagated to all Npgsql async methods; hard-timeout at 10s for `LoadCharacter` |
| `character-persistence.md` | F-CP-2 — `SaveVersion` increment on every write | `save_version = save_version + 1` in the UPDATE statement; `BIGINT` column holds the full `uint` range |
| `enhancement-system.md` | CR-ENH-11 — DB transaction commits before any result message is sent | `SaveIrreversibleOutcome` awaits `ExecuteAsync` + rows-affected check before returning; callers do not send result messages until the method returns `Success` |
| `npc-shop.md` / ADR-001 | OQ-ADR1-1 — PendingPurchase storage location | **Resolved**: dedicated `pending_purchases` table in the same PostgreSQL database; INSERT shares one transaction with the TrySpendGold UPDATE |
| `networking-core.md` / ADR-004 | OQ-NET-5 / OQ-ADR4-2 — persistence write latency budget | **Resolved**: ≤50ms P95 write budget; `PERSISTENCE_WRITE_WARNING_MS = 50`, `PERSISTENCE_WRITE_CRITICAL_MS = 100` |

## Performance Implications
- **CPU**: Dapper parameter binding and result mapping < 0.1ms per call — negligible vs DB round-trip
- **Memory**: Npgsql pool of 20 connections at ~50KB each = ~1MB pool footprint per zone server process
- **Load Time**: First `LoadCharacter` after cold start pays pool initialisation cost (first TCP connection). Subsequent loads reuse pooled connections
- **Network (server-internal)**: DB queries travel loopback or LAN within the co-location constraint — sub-millisecond transport contributes negligibly to the 50ms budget

## Migration Plan

Greenfield project — no existing persistence layer. Schema is applied fresh:
1. `tools/db/migrations/001_create_character_records.sql` — `CREATE TABLE` + indexes
2. `tools/db/migrations/002_create_pending_purchases.sql` — `CREATE TABLE` + FK + index

Migrations are applied manually before server deployment for MVP. Automated migration tooling (e.g., DbUp) is out of scope until the Hosting Backend ADR defines the deployment pipeline.

## Validation Criteria
- **AC-CP-13 through AC-CP-31** (`character-persistence.md`) drive the full test plan; all must pass against a real PostgreSQL instance — no mock substitution for the persistence integration tests
- **Write latency CI gate**: integration test asserts P95 `SaveIrreversibleOutcome` latency ≤ 50ms against a local PostgreSQL Docker container for 100 consecutive saves
- **Concurrency CI gate**: AC-CP-21 confirms zero parallel writes for the same CharacterID across 50 concurrent Task invocations
- **Rows-affected check**: AC-CP-19 / AC-CP-20 confirm `ConcurrencyConflict` is returned when a concurrent writer increments `save_version` between the read and the UPDATE

## Related Decisions
- ADR-001: Purchase Transaction Integrity — `PendingPurchase` record; OQ-ADR1-1 resolved by Decision 3
- ADR-004: Networking Library (NGO) — OQ-ADR4-2 / OQ-NET-5 resolved by Decision 4
- Hosting Backend ADR (future) — constrained by Decision 4: PostgreSQL must be co-located with the game server
- `character-persistence.md` — full behavioural spec this ADR implements
