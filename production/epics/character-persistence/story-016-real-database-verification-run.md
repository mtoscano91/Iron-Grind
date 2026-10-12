# Story 016: The Real-Database Verification Run

> **Epic**: Character Persistence
> **Status**: Blocked — written 2026-10-12. Blocked on Stories 012–015 and on every Part A story it exercises (003, 004, 005, 007–010).
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — AC-CP-13 to AC-CP-31, and AC-CP-26 (the end-to-end criterion).
**Requirement**: the epic's Definition of Done

**ADR Governing Implementation**: ADR-006 Validation Criteria — "AC-CP-13 through AC-CP-31 … all must pass against a real PostgreSQL instance — no mock substitution"; the write-latency gate (P95 `SaveIrreversibleOutcome` ≤ 50 ms over 100 consecutive saves against a local PostgreSQL container); the concurrency gate (AC-CP-21 over 50 concurrent task invocations); the rows-affected check (AC-CP-19 / AC-CP-20).
**ADR Decision Summary**: the acceptance of the persistence layer is measured on the database it ships with.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: the run is in the Editor unless a headless server build exists by then; say which in the evidence document.

**Control Manifest Rules (this layer)**:
- Guardrail: persistence write — warning ≥ 50 ms, critical ≥ 100 ms, hard timeout 5 s — ADR-006
- Guardrail: the result of a persistence call is handled by `Drain` on the first tick after its task completes — ADR-011

---

## Acceptance Criteria

*Recorded in `production/qa/evidence/character-persistence-016-database-verification.md`, each item PASS / FAIL / NOT RUN with its observation. A FAIL is recorded and escalated; an item NOT RUN blocks `/story-done`.*

- [ ] **The Part A suites against PostgreSQL**: the load, save, irreversible-outcome and session-end tests of Stories 003–010 run with `NpgsqlCharacterRecordStore` in place of the in-memory store and pass — this is AC-CP-13 to AC-CP-25 and AC-CP-30 / 31 "against a real PostgreSQL instance". AC-CP-27, 28 and 29 are listed with the story that owns each (Enhancement Story 011; the Respec and Consumable Use systems) and are NOT RUN here by design — the evidence document says so and they do not block this story.
- [ ] **AC-CP-15 (real)**: clean logout, then a load in a fresh process state: all 23 persisted fields match; `SaveVersion` N + 1; `CreatedAtUtc` unchanged.
- [ ] **AC-CP-19 (real)**: the version mismatch is made by a separate connection.
- [ ] **AC-CP-21 — concurrency gate**: 50 concurrent `SaveIrreversibleOutcome` invocations for one `CharacterID`: zero overlapping writes in the store's record, final `SaveVersion` = start + the number that succeeded.
- [ ] **Latency gate**: 100 consecutive `SaveIrreversibleOutcome` calls; the P95 from command issue to rows-affected is ≤ 50 ms. The figure, the machine and where PostgreSQL ran are recorded.
- [ ] **AC-CP-26**: a character with a +9 weapon equipped is saved, every in-memory system is discarded, and it is loaded again: the weapon is in the same gear slot at +9.
- [ ] **AC-CP-5 (real)**: a query delayed beyond the load timeout (a lock or `pg_sleep`) gives `DatabaseError`.

---

## Implementation Notes

- The suites of Part A should take the store as a parameter from the start, so this story adds a fixture, not a copy of the tests.
- The latency gate measures the database round trip, not the tick: it says nothing about the 20 Hz loop, which ADR-011 keeps away from the write.
- "8 hours later" in AC-CP-26 is a restart, not a wait.

**Open decisions for `/story-readiness`**

1. **Where this runs**: the developer's machine, CI, or both; and whether the latency gate is a CI gate (ADR-006 says so) on hardware that is not the production host.
2. **AC-CP-26 needs gear on save and load**: Story 005 and Story 007's gear criterion, both waiting on the Equipment System.

---

## Out of Scope

- A headless Linux build run (Story 012's verification items and the epic's Definition of Done)
- Load tests beyond the two gates

---

## QA Test Cases

- **Suite swap** — Given the Part A fixtures with the Npgsql store, Then they pass.
- **Concurrency** — 50 invocations → no overlap; versions consecutive.
- **Latency** — 100 saves → P95 ≤ 50 ms; the distribution is logged in one parsable line.
- **Restart** — Save, discard, load → +9 weapon in its slot.
- **Slow query** — Injected delay beyond the timeout → `DatabaseError`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `production/qa/evidence/character-persistence-016-database-verification.md` — every item PASS or FAIL with its observation, none NOT RUN other than the three criteria owned by other stories.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **Stories 012, 013, 014, 015 (blocking)**; Stories 003, 004, 005, 007, 008, 009, 010
- Unlocks: the epic's Definition of Done
