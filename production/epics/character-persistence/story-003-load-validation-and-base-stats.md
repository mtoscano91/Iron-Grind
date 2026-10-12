# Story 003: `LoadCharacter` — Validation, Account Check, Base Stats, Timeout

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (3 open decisions, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 steps 1, 2, 3 and 11; CR-CP-8 and F-CP-1 (the 10-second load timeout); AC-CP-1 to AC-CP-6, AC-CP-23.
**Requirement**: `TR-cp-003`, `TR-cp-008`

**ADR Governing Implementation**: ADR-006; ADR-011 Decisions 1 and 3.
**ADR Decision Summary**: `LoadCharacter` is called from tick code and tracked by `ITickCompletionQueue`; the persistence layer never reads or writes live game state after its first `await`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the returned task, in the same statement, to `ITickCompletionQueue.Track`; this applies to `LoadCharacter` — ADR-011
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011
- Guardrail: a task still incomplete after `PERSISTENCE_WATCHDOG_TICKS` (200 = 10 s) is cancelled by the queue — ADR-011

---

## Acceptance Criteria

- [ ] **AC-CP-2**: `LoadCharacter` with `CharacterID.Invalid` returns `Code = CorruptRecord` and makes no store call.
- [ ] **AC-CP-4**: an id with no record returns `CharacterNotFound`.
- [ ] **AC-CP-3**: a record whose `AccountID` differs from `ownerAccountID` returns `AccountMismatch`, and no stat is set.
- [ ] **Range validation (step 3)**: a record with any of — Level outside [1, 60]; Experience < 0; HeldFreePoints < 0; STR / DEX / VIT / INT outside [1, 999]; MaxHP or MaxMP outside [1, 99999]; AttackPower, Defense or MagicDefense < 0; any of the five float stats ≤ 0 — returns `CorruptRecord`, and `SetBaseStat` is not called once. One test per bound, at the bound and one beyond it.
- [ ] **AC-CP-6**: after a successful load, `GetBaseStat` returns the record's value for each of the 20 base-stat fields.
- [ ] **`SetBaseStat` rejects**: if Character Stats rejects a value, the load returns `CorruptRecord`.
- [ ] **AC-CP-1**: a valid id with a matching account returns `Code = Success` and the record's `LastZoneID`.
- [ ] **AC-CP-5 (CR-CP-8)**: with the store delayed beyond `CHARACTER_LOAD_TIMEOUT_SECONDS`, the load completes with `DatabaseError`; the test uses an injected time source or a short injected timeout, not a 10-second wait.
- [ ] **Store fault**: a store call that faults gives `DatabaseError`, not an exception out of the task.
- [ ] **AC-CP-23**: a freshly created stub loads through the same path as any other record (no branch on `SaveVersion == 0` or `LastZoneID == 0`).
- [ ] **A failed load leaves no state behind**: after any non-Success code, no base stat of that character has been set (the load is all-or-nothing up to this story's steps).

---

## Implementation Notes

- **ADR-011 Decision 3 and the load.** The fetch is the first `await`; steps 3 onward mutate game state (Character Stats). Those steps therefore cannot run inside the persistence task after the `await`: they run on the tick thread, in the completion callback. The shape that follows is a fetch that returns the record, and an "apply" step run from the callback. How `LoadCharacter`'s single `Task<CharacterLoadResult>` of CR-CP-1 fits that is open decision 1.
- Character Stats is keyed by `EntityID` (`CharacterStats.SetBaseStat(EntityID, StatID, ...)`), not `CharacterID`: open decision 2.
- The order inside step 3 is fixed: validate every field, then set all 20.

**Open decisions for `/story-readiness`**

1. **One task or fetch-then-apply.** Either `LoadCharacter` stays one method and its implementation marshals the apply step back to the tick (through the completion queue), or the service exposes the fetch and a synchronous `ApplyLoadedRecord` that the caller invokes from its callback. ADR-011 says the interface is unchanged; the second shape adds a method. This decision shapes Stories 004 and 005 too.
2. **`CharacterID` → `EntityID`.** Who maps them at load time, when no entity exists yet for a character that is still connecting.
3. **Which 20 fields.** CR-CP-2 lists Level, Experience, HeldFreePoints, four attributes, five int stats, five float stats: 17, plus CurrentHP and CurrentMP makes 19. Read `character-stats.md` for the twentieth before the criterion is final.

---

## Out of Scope

- Story 004: steps 5–10 (inventory, gold, at-cap, clamp, client events)
- Story 005: step 4 (equipment)
- Story 014: the same behaviour against PostgreSQL
- The Authentication call site (no implementation yet)

---

## QA Test Cases

- **Invalid id** — Given `CharacterID.Invalid`, When loaded, Then `CorruptRecord` and the store's call count is 0.
- **Not found / mismatch** — Given an empty store → `CharacterNotFound`; Given a record of account 7 loaded for account 8 → `AccountMismatch` and no `SetBaseStat` call.
- **Ranges** — Given each field at its lowest valid, highest valid and first invalid value, Then valid → Success, invalid → `CorruptRecord` with zero `SetBaseStat` calls.
- **Stats restored** — Given a record with distinct values per field, When loaded, Then `GetBaseStat` matches each.
- **Timeout** — Given a store that never completes and a short injected timeout, Then `DatabaseError`.
- **Fault** — Given a store that throws, Then `DatabaseError` and the task is not faulted.
- **Stub** — Given `CreateStub` then `LoadCharacter`, Then Success with MaxHP 400 and AttackPower 30 (AC-CP-22's second half).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadCore_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002
- Unlocks: Stories 004, 005
