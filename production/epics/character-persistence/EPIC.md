# Epic: Character Persistence

> **Layer**: Core
> **GDD**: design/gdd/character-persistence.md
> **Architecture Module**: Character Persistence — `CharacterPersistenceService : ICharacterPersistence` (async), PostgreSQL via Npgsql and Dapper
> **Status**: Ready (created 2026-10-12; 16 stories written 2026-10-12, none started — 10 Not Started pending `/story-readiness`, 6 Blocked)
> **Stories**: 16 stories created 2026-10-12 (001–016)
> **Manifest Version**: 2026-10-09

## Overview

Character Persistence is the server-side save and load layer that makes character state durable across sessions. On successful authentication it loads the character record of the authenticated `CharacterID` and restores base stats to Character Stats, equipment to the Equipment System, the bag to the Inventory System and the gold balance to the Currency System. Writes go the other way: irreversible outcomes (level-ups, enhancements, respecs) are written at once under the Commit-Before-Broadcast contract (Networking Core CR-NET-5), and the rest of the session state is written when the session ends — on clean logout, on zone teardown, or after the 300-second session TTL that follows a disconnect. The system computes no game value: it serializes and restores what other systems own. The module is one interface, `ICharacterPersistence`, and one implementation over PostgreSQL; tick code never awaits it (ADR-011).

This epic is what unblocks Enhancement Story 011 (the real `SaveIrreversibleOutcome` and its failure protocol, CR-CP-5) and, through it, Enhancement Story 015.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-006: Persistence Layer | PostgreSQL is the only store, reached through Npgsql and Dapper; one transaction per save with a `save_version` predicate; a per-`CharacterID` write queue in the service; `pending_purchases` as a separate table; write budget ≤ 50 ms P95; no Entity Framework, no SQLite | MEDIUM — Npgsql and Dapper under IL2CPP on a headless Linux build (`architecture.md`); the ADR's own knowledge risk is LOW |
| ADR-011: Asynchronous Persistence in the Server Tick Loop | Tick code starts a `Task` and hands it to `ITickCompletionQueue`; it never awaits or blocks. The persistence layer copies what it needs before its first `await`, uses `ConfigureAwait(false)` on every `await`, and touches no live game state afterwards | LOW |
| ADR-012: Server/Client Assembly Boundary | `IronGrind.ServerLogic` is the only assembly that may reference Npgsql and Dapper; they are added to its precompiled references when this code is written | HIGH (build verification items of that ADR) |
| ADR-010: Event and Messaging Architecture | A direct call on an injected interface; `ICharacterPersistence` keeps its grandfathered name | LOW |
| ADR-001: Purchase Transaction Integrity | `PendingPurchase` storage and lifecycle (CR-CP-12) | LOW |

## GDD Requirements

*The TR registry (`docs/architecture/tr-registry.yaml`) has no entry for this system; the IDs below are this epic's placeholders, as in the other epics.*

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-cp-001 | CR-CP-1 — the `ICharacterPersistence` async interface (`LoadCharacter`, `CreateStub`, `SaveSession`, `SaveIrreversibleOutcome`) and its result types | ADR-006 ✅, ADR-011 ✅ |
| TR-cp-002 | CR-CP-2 — the character record schema: 23 logical fields, some not persisted | ADR-006 ✅ (Amendment 1: `inventory_slots` entries carry `enhancement_level`) |
| TR-cp-003 | CR-CP-3 — the load sequence, in its exact order (stat initialisation before equipment re-registration) | ADR-006 ✅ (the interface); the order is a design rule |
| TR-cp-004 | CR-CP-4 — save triggers: all saves are event-driven, no periodic save | ADR-011 ✅ |
| TR-cp-005 | CR-CP-5 — the `SaveIrreversibleOutcome` failure protocol: caller rollback, session preservation, disconnect | ADR-011 ✅ |
| TR-cp-006 | CR-CP-6 — one transaction per save; `SaveVersion` + 1 on every write; zero rows affected → `ConcurrencyConflict`; no partial write, no retry | ADR-006 ✅ |
| TR-cp-007 | CR-CP-7 — at most one save in flight per `CharacterID`; a second call is queued | ADR-006 ✅ |
| TR-cp-008 | CR-CP-8 — `CHARACTER_LOAD_TIMEOUT_SECONDS = 10` on `LoadCharacter` | ADR-006 ✅ |
| TR-cp-009 | CR-CP-9 — `CreateStub` writes a complete Level 1 record at registration; no first-login path in `LoadCharacter` | ADR-006 ✅ (the interface) |
| TR-cp-010 | CR-CP-10 — the `SaveSession` step sequence: steps 1–6 gather in-memory state, step 7 commits atomically | ADR-006 ✅, ADR-011 ✅ (copy before the first `await`) |
| TR-cp-011 | CR-CP-11 — CR-CP-5 applies to `SaveIrreversibleOutcome` failures only; `SaveSession` failures follow CR-NET-5.5 | ADR-011 ✅ |
| TR-cp-012 | CR-CP-12 — `PendingPurchase` in its own `pending_purchases` table, outside the character record | ADR-001 ✅, ADR-006 ✅ |
| TR-cp-013 | F-CP-2 — `SaveVersion` increment on every write | ADR-006 ✅ |

No requirement was found that needs a new ADR.

## Known Gaps at Creation (2026-10-12)

`/create-stories` and `/story-readiness` must take these into account.

1. **No code exists.** `ICharacterPersistence` is named in comments of four files (`INetworkTestObserver.cs`, `CommitBeforeBroadcastSequencer.cs`, `EnhancementRequestDeduplicator.cs`, `ITickCompletionQueue.cs`) and defined nowhere in `src/` or `tests/`.
2. **Npgsql and Dapper are not in the project** (not in `Packages/manifest.json`, nothing under `Assets/`). ADR-006 Verification Required 1–3 are open: Npgsql on a headless Linux server build, Dapper's `link.xml` needs under IL2CPP, the connection pool under the headless process lifecycle.
3. **ADR-006 requires a real PostgreSQL instance for AC-CP-13 to AC-CP-31, "no mock substitution"**, and two latency / concurrency gates against a local PostgreSQL container. No database is set up in the project, and the testing standard says unit tests call no database. The work therefore falls in two parts:
   - **Plain C#, EditMode-testable**: the interface and its types, the load sequence (CR-CP-3), the `SaveSession` step sequence (CR-CP-10), the per-character write queue (CR-CP-7), the failure protocols (CR-CP-5, CR-CP-11), with the store behind a seam.
   - **The Npgsql / Dapper store**: SQL migrations under `tools/db/migrations/`, the implementation, and the real-database tests — gated on importing the two libraries and on a PostgreSQL instance.
   The first part alone gives Enhancement Story 011 the contract it waits for.
4. **Three callers are not implemented**: Authentication (`LoadCharacter`, `CreateStub` — an epic exists), Zone Instancing (`SaveSession(ZoneClosed)` — no epic), Death & Respawn (no epic). Their call sites are reached through seams until then.
5. **Open questions of the GDD, none marked blocking**: OQ-CP-1 (a dedicated `DisconnectReason.PersistenceFailure`; the GDD recommends it), OQ-CP-3 (an action: close OQ-NC-SER-2 in `networking-wire-protocol.md`), OQ-CP-4 (a timeout constant for `CreateStub`; its source is unspecified today).
6. **Reverse references not verified**: the GDD asks that Character Stats, Equipment System, Inventory System, Currency System and Leveling System be checked for their reference back to this system before implementation. Not checked when this epic was written.

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/character-persistence.md` (AC-CP-1 to AC-CP-31) are verified
- AC-CP-13 to AC-CP-31 pass against a real PostgreSQL instance (ADR-006 Validation Criteria), with the write-latency and concurrency gates
- All Logic and Integration stories have passing test files in `tests/`
- ADR-006 Verification Required 1–3 are recorded as run, with evidence in `production/qa/evidence/`

## Stories

*Part A (001–011) is plain C# and needs no database; Part B (012–016) is the PostgreSQL side. Stories 001, 002, 006, 007, 008 and 009 are the path to Enhancement Story 011.*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [The Persistence Contract — `ICharacterPersistence`, Result Types, `CharacterRecord`](story-001-persistence-contract.md) | Logic | Not Started (2026-10-12; `/story-readiness` pending, 3 open decisions) | ADR-006, ADR-011, ADR-012 |
| 002 | [Record Store Seam, In-Memory Store, `CreateStub`](story-002-record-store-and-create-stub.md) | Logic | Not Started (2026-10-12; 3 open decisions) | ADR-006, ADR-011 |
| 003 | [`LoadCharacter` — Validation, Account Check, Base Stats, Timeout](story-003-load-validation-and-base-stats.md) | Integration | Not Started (2026-10-12; 3 open decisions) | ADR-006, ADR-011 |
| 004 | [`LoadCharacter` — Inventory, Gold, At-Cap, HP/MP Clamp, Client Events](story-004-load-inventory-gold-clamp-events.md) | Integration | Not Started (2026-10-12; 4 open decisions) | ADR-011, ADR-010 |
| 005 | [`LoadCharacter` — Equipment Re-registration](story-005-load-equipment-reregistration.md) | Integration | Blocked (the Equipment System has no code; 3 open decisions) | ADR-011, ADR-010 |
| 006 | [Per-Character Save Queue](story-006-per-character-save-queue.md) | Logic | Not Started (2026-10-12; 2 open decisions) | ADR-006, ADR-011 |
| 007 | [`SaveSession` — the Step Sequence](story-007-save-session.md) | Integration | Not Started (2026-10-12; 5 open decisions) | ADR-011, ADR-006 |
| 008 | [`SaveIrreversibleOutcome`](story-008-save-irreversible-outcome.md) | Integration | Not Started (2026-10-12; 2 open decisions) | ADR-006, ADR-011 |
| 009 | [The Irreversible-Outcome Persist Step and the Level-Up Path](story-009-irreversible-persist-step-and-level-up.md) | Integration | Not Started (2026-10-12; 3 open decisions) | ADR-011, ADR-010 |
| 010 | [Session-End Triggers — Clean Logout and TTL Expiry](story-010-session-end-triggers.md) | Integration | Not Started (2026-10-12; 3 open decisions) | ADR-011 |
| 011 | [`PendingPurchase` Lifecycle on the Store Seam](story-011-pending-purchase-lifecycle.md) | Logic | Not Started (2026-10-12; 3 open decisions; no caller yet) | ADR-001, ADR-006 |
| 012 | [Npgsql and Dapper in the Project](story-012-npgsql-and-dapper-in-the-project.md) | Integration | Blocked (the libraries and a PostgreSQL instance are the user's decision) | ADR-006, ADR-012 |
| 013 | [SQL Migrations for `character_records` and `pending_purchases`](story-013-sql-migrations.md) | Config/Data | Blocked (needs a PostgreSQL instance to run) | ADR-006 |
| 014 | [The Npgsql Character Record Store](story-014-npgsql-character-record-store.md) | Integration | Blocked (012, 013) | ADR-006, ADR-011 |
| 015 | [The Npgsql Pending-Purchase Store](story-015-npgsql-pending-purchase-store.md) | Integration | Blocked (012–014) | ADR-006, ADR-001 |
| 016 | [The Real-Database Verification Run](story-016-real-database-verification-run.md) | Integration | Blocked (012–015 and Part A) | ADR-006 |

**Acceptance criteria owned outside this epic**: AC-CP-27 (enhancement commit before broadcast) — Enhancement Story 011. AC-CP-28 (respec failure with its compensating `AddGold`) and AC-CP-29 (item consumption) — the Respec and Consumable Use systems, which have no code.

### Design conflicts found while writing the stories (2026-10-12)

Each is an open decision in the story named; several need a line in the GDD.

1. **`SaveSession`'s step order against ADR-011.** CR-CP-10 step 1 acquires the save slot, which can wait, before steps 2–6 read live state; ADR-011 Decision 3 requires the snapshot before the first `await`. A queued save then holds an expected `SaveVersion` taken before its turn: AC-CP-21 expects two queued writes to give N + 1 and N + 2, AC-CP-20 expects the second of two to conflict. — Stories 006, 007.
2. **What `SaveIrreversibleOutcome` writes is not stated.** CR-CP-10 gives the step sequence of `SaveSession` only. — Story 008.
3. **Gear slots on save.** The Interactions table says "reads `GearSlots` directly from record — Equipment System does not provide a save API"; CR-CP-10 step 3 says "from Equipment System". The Equipment System has no code. — Stories 005, 007.
4. **`LoadCharacter` against ADR-011.** Steps 3 onward mutate game state after the fetch, which is the first `await`; they must run on the tick thread, in the completion callback. Whether the interface stays one method is open. — Story 003.
5. **Mocks named by the acceptance criteria**: `IEventBus` (forbidden by ADR-010), `ISavePersistence`, `ISaveCoordinator`, `ICriticalAlertService`, `ISessionConfiguration`. None exists; the stories restate the criteria against the completion queue, the coordinator, the mutation gate and the record store seam. — Stories 006, 009, 010.
6. **Types and APIs not in code**: `AccountID`, `ZoneID`, `ClassType`, `StatSnapshotEvent`; a critical-alert service; a Currency restore call and a balance-with-version read (`GetBalance` returns the balance only); Character Stats is keyed by `EntityID`, not `CharacterID`. — Stories 001, 003, 004, 005, 007.
7. **A timed-out write has an unknown outcome**, and the TTL-expiry save that CR-CP-5 relies on carries an expected version that may be one behind. — Story 008.
8. **Gold in two places during a purchase** (the Currency System's memory and `character_records.gold_balance`), and whether a purchase advances `save_version`. — Stories 011, 015.

## Next Step

Run `/story-readiness production/epics/character-persistence/story-001-persistence-contract.md`. Work through the stories in order — each story's `Depends on:` line says what must be done first.
