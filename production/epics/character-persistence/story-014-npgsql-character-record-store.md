# Story 014: The Npgsql Character Record Store

> **Epic**: Character Persistence
> **Status**: Blocked — written 2026-10-12. Blocked on Story 012 (the libraries and a PostgreSQL instance) and Story 013 (the schema).
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-6 (one transaction per save; the version predicate; zero rows → `ConcurrencyConflict`), F-CP-2, CR-CP-8, CR-CP-9.
**Requirement**: `TR-cp-006`, `TR-cp-013`

**ADR Governing Implementation**: ADR-006 — Decision 2 (explicit parameterised SQL through Dapper), Decision 4 (latency thresholds, the 5-second timeout), Decision 5 (Npgsql's own pool, `Maximum Pool Size=20`), the type-mapping notes; ADR-011 Decision 3.
**ADR Decision Summary**: one `NpgsqlTransaction` per save, an `ExecuteAsync` rows-affected check as the conflict signal, the `CancellationToken` passed to every Npgsql async call.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: Npgsql and Dapper under Unity are unverified until Story 012 has run; take its recorded findings as the reference. Do not write Npgsql or Dapper calls from memory — read the API of the pinned versions.

**Control Manifest Rules (this layer)**:
- Required: `ConfigureAwait(false)` on every `await`, including `await using` and `await foreach`; if an awaited call cannot be trusted not to capture the context, the body runs through `Task.Run` after the snapshot — ADR-011
- Required: the store touches only the copy it was given and the database — ADR-011
- Forbidden: Entity Framework Core — ADR-006
- Guardrail: warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006

---

## Acceptance Criteria

*Every criterion runs against a real PostgreSQL instance with the Story 013 schema (ADR-006: "no mock substitution"). The tests are not EditMode unit tests: where they live and how they are skipped when no database is reachable is open decision 1.*

- [ ] **Implements the seam**: `NpgsqlCharacterRecordStore : ICharacterRecordStore`; the suite of Story 002's store tests passes against it unchanged (fetch, insert, the version predicate, isolation).
- [ ] **Round trip**: a record with distinct values in all 23 fields, full gear and inventory arrays (with enhancement levels), `uint` fields at `uint.MaxValue`, and a 26-byte UTF-8 name is inserted, fetched and equal field for field.
- [ ] **Update is one statement in one transaction** with `WHERE character_id = @id AND save_version = @expected`, sets `save_version = save_version + 1` and `last_saved_at_utc`; rows affected is returned to the service.
- [ ] **AC-CP-19 (real)**: with the row's `save_version` changed by a second connection before the update, zero rows are affected and nothing else in the row changes.
- [ ] **Cancellation and timeout**: a cancelled token ends the call; a query held by a lock beyond the write timeout ends with a failure the service maps to `DatabaseError`.
- [ ] **No context capture**: a source search of the file finds no `await` without `ConfigureAwait(false)`.
- [ ] **Pool**: the store takes an `NpgsqlDataSource`; it opens and returns a connection per operation and holds none between calls.
- [ ] **Connection string**: read from server configuration, never a literal with a password in source.

---

## Implementation Notes

- The SQL is explicit text in this file (ADR-006 Decision 2); Dapper maps parameters and rows.
- JSONB columns: the encoding of `gear_slots` and `inventory_slots` is the ADR's type-mapping note and Amendment 1. Under IL2CPP a reflection-based JSON serialiser is a stripping risk — decide the serialiser at readiness with Story 012's findings.
- `uint.MaxValue` in a `BIGINT` column, and a `float` in a `REAL` column (CritChance 0.065 does not round-trip as a decimal but must round-trip as the same `float`).

**Open decisions for `/story-readiness`**

1. **Where database tests live and when they run**: a separate test assembly or category, skipped (reported, not passed) when no instance is reachable; whether CI has an instance.
2. **The JSON serialiser** for the two JSONB columns.
3. **Where the connection string comes from** (environment, a server config file) — touches ADR-007's hosting decisions.

---

## Out of Scope

- Story 015: `pending_purchases`
- Story 016: the acceptance run and the latency and concurrency gates
- The service's own logic (Stories 003–010), which does not change

---

## QA Test Cases

- **Shared store suite** — Given the Npgsql store in place of the in-memory one, Then Story 002's store tests pass.
- **Round trip** — Given the extreme record above, Then every field is equal after a fetch. Edge: empty arrays; a name of 26 bytes with multi-byte characters.
- **Conflict from a second connection** — Then zero rows, row unchanged.
- **Timeout** — Given a row locked by another transaction, Then the call ends within the injected timeout with a failure.
- **Source** — Then every `await` in the file is followed by `ConfigureAwait(false)`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: the database test file named at readiness — must exist and pass against a PostgreSQL instance; a run with no instance does not count.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Story 012 (blocking)**, **Story 013 (blocking)**; Story 002
- Unlocks: Stories 015, 016
