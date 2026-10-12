# Epic: Character Persistence

> **Layer**: Core
> **GDD**: design/gdd/character-persistence.md
> **Architecture Module**: Character Persistence — `CharacterPersistenceService : ICharacterPersistence` (async), PostgreSQL via Npgsql and Dapper
> **Status**: Ready (created 2026-10-12; no stories yet)
> **Stories**: Not yet created — run `/create-stories character-persistence`
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

## Next Step

Run `/create-stories character-persistence` to break this epic into implementable stories.
