# Story 002: ZoneID allocator

> **Epic**: Gateway
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 2 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-1; AC-ZI-16
**Requirement**: `TR-zi-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decisions 3 and 5)
**ADR Decision Summary**: The gateway allocates a ZoneID with `nextval('zone_id_seq')` when it starts a zone process and passes it as `-zoneId`; IDs are never reused, not even across gateway restarts. The `gateway` role has `USAGE` on `zone_id_seq` (migration 004).

**Engine**: .NET 10 (gateway) | **Risk**: LOW
**Engine Notes**: N/A — no Unity API. Npgsql 10.0.3 + Dapper 2.1.89 (pinned in Authentication Story 004).

**Control Manifest Rules (this layer)**:
- Required: the gateway connects as `gateway` with its own pool; `USAGE` on `zone_id_seq` — ADR-016 Decision 5
- Required: Database tests carry `[Category("Database")]`; CI filters `TestCategory!=Database` — Authentication Story 003

---

## Acceptance Criteria

- [ ] `IZoneIdAllocator.AllocateAsync(CancellationToken)` → `ZoneID`, implemented over `nextval('zone_id_seq')` on the gateway pool
- [ ] AC-ZI-16: on a test database, 100 allocations, then a new allocator instance (simulated restart) allocates one more — all 101 values distinct and non-zero, and the 101st is greater than every earlier one
- [ ] A value of 0 or above `uint.MaxValue` from the sequence is an error (logged, exception) — never returned as a ZoneID
- [ ] A database failure surfaces as an exception the caller (Story 007) can treat as a failed start; no partial state

---

## Implementation Notes

*Derived from ADR-016 Decision 5:*

- Put it beside `NpgsqlAccountRepository` (`gateway/IronGrind.Gateway/Auth/` or a new `Routing/` folder) and reuse the gateway's data source registration from `GatewayHost`.
- Test fixture: reuse `AuthTestDatabase` (gateway tests) — it already creates a throwaway database with migration 004 and the gateway role.
- `zone_id_seq` is `bigint` by default — cast and range-check to `uint`.

---

## Out of Scope

- Story 007: deciding when to allocate (zone start)
- Story 008: passing the ID to the launched process

---

## QA Test Cases

- **AC-2 (AC-ZI-16)**: Uniqueness across restart
  - Given: a test database from `AuthTestDatabase`
  - When: allocator A allocates 100, is disposed; allocator B allocates 1
  - Then: 101 distinct non-zero values; B's value > max(A's)
- **AC-4**: Failure
  - Given: an allocator whose connection string points at a stopped server or a role without `USAGE`
  - Then: `AllocateAsync` throws; no ZoneID returned

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `gateway/IronGrind.Gateway.Tests/Gateway_ZoneIdAllocator_tests.cs` (`[Category("Database")]`) — must pass against the portable dev PostgreSQL

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (migration 004 and the gateway role exist — Authentication Story 003)
- Unlocks: Story 007, Story 009
