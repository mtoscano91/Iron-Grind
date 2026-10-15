# Story 003: `LoadCharacter` — Validation, Account Check, Base Stats, Timeout

> **Epic**: Character Persistence
> **Status**: Ready — `/story-readiness` 2026-10-15 (NEEDS WORK → decisions taken, see "Readiness decisions")
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 steps 1, 2, 3 and 11; CR-CP-8 (the 10-second load timeout); AC-CP-1 to AC-CP-6, AC-CP-23. `design/gdd/leveling-system.md` CR-6.3 (the load path restores `heldFreePoints` through `RestoreLevelingState`).
**Requirement**: `TR-cp-003`, `TR-cp-008`
*(Epic placeholders: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness` 2026-10-15, as for Stories 001 and 002.)*

**ADR Governing Implementation**: ADR-006; ADR-011 Decisions 1, 2 and 3.
**ADR Decision Summary**: `LoadCharacter` is called from tick code and tracked by `ITickCompletionQueue`; the persistence layer never reads or writes live game state after its first `await`. The writes to game state therefore happen in a separate synchronous step, run on the tick thread from the caller's completion callback.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — plain C#, no engine API.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the returned task, in the same statement, to `ITickCompletionQueue.Track`; this applies to `LoadCharacter` — ADR-011
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011
- Required: `ConfigureAwait(false)` on every `await`; `RunContinuationsAsynchronously` on every `TaskCompletionSource` — ADR-011 Decision 3
- Guardrail: a task still incomplete after `PERSISTENCE_WATCHDOG_TICKS` (200 = 10 s) is cancelled by the queue — ADR-011

---

## Readiness decisions (2026-10-15)

1. **Fetch, then apply.** `LoadCharacter(CharacterID, AccountID, CancellationToken)` keeps its CR-CP-1 signature and does steps 1–3 *validation only*: id check, fetch, account check, range validation — on the fetched record alone, which is not live state. `CharacterLoadResult` gains a field `CharacterRecord Record`, set only when `Code == Success` (`null` otherwise). A new synchronous method on the service, `void ApplyLoadedRecord(EntityID entityId, CharacterRecord record)`, performs the writes of step 3; the caller invokes it from its `Track` callback, on the tick thread. Stories 004 and 005 extend `ApplyLoadedRecord` with their steps (the CR-CP-3 order is kept inside it). The `ICharacterPersistence` method list is unchanged (ADR-011 Decision 3); `ApplyLoadedRecord` is declared on the service — whether it also goes on an interface is the implementer's call, recorded in the Completion Notes.
2. **The caller supplies the `EntityID`.** No production code allocates player `EntityID`s yet; the caller (Authentication / zone spawn) allocates one and passes it to `ApplyLoadedRecord`. Persistence keeps no `CharacterID → EntityID` map. Tests use a fixed `EntityID`.
3. **17 fields in this story, not 20.** The record has 19 fields to restore, not the GDD's 20 (design conflict 11 in the epic): Character Stats has 18 `StatID`s and `HeldFreePoints` is not one. This story restores:
   - 11 int stats through `CharacterStats.SetBaseStat`: Level, Experience, STR, DEX, VIT, INT, MaxHP, MaxMP, AttackPower, Defense, MagicDefense;
   - 5 float stats through `CharacterStats.SetBaseStatFloat`: CritChance, CritMultiplier, AttackRange, AttackSpeedMultiplier, MovementSpeed;
   - `HeldFreePoints` through `LevelingService.RegisterPlayerClassType(entityId, record.ClassType)` then `RestoreLevelingState(entityId, new LevelingStateSnapshot(record.HeldFreePoints))` (Leveling Story 009: the class-type cache must be registered first).
   - **CurrentHP and CurrentMP move to Story 004** (step 8): `SetCurrentHP` / `SetCurrentMP` clamp to the *effective* maximum, and before step 4 registers the gear modifiers that is the base maximum — a saved HP above base MaxHP would be cut on every login. CR-CP-3 step 8 already places the clamp after step 4.
4. **`SetBaseStat` never rejects.** `CharacterStats.SetBaseStat` is `void` and stores the value unchecked (`src/ServerLogic/CharacterStats/CharacterStats.cs:200`); CR-CP-3's "or any `SetBaseStat()` call rejects" cannot occur. The criterion is dropped — a deviation, recorded in design conflict 11. All-or-nothing follows from validating every field before the first write.
5. **The timeout is the caller's token.** CR-CP-8: `LoadCharacter` *receives* a token that fires after 10 s — in production that is the completion queue's watchdog (200 ticks = 10 s). The service starts no timer of its own. If `ct` is cancelled while the fetch is pending, the task completes with `DatabaseError` (not `Canceled`, not faulted) — the service must observe the token itself, since a store call need not honour it.

---

## Acceptance Criteria

- [ ] **AC-CP-2**: `LoadCharacter` with `CharacterID.Invalid` returns `Code = CorruptRecord` and makes no store call.
- [ ] **AC-CP-4**: an id with no record returns `CharacterNotFound`.
- [ ] **AC-CP-3**: a record whose `AccountID` differs from `ownerAccountID` returns `AccountMismatch` and `Record == null`.
- [ ] **Range validation (step 3)**: a record with any of — Level outside [1, 60]; Experience < 0; HeldFreePoints < 0; STR / DEX / VIT / INT outside [1, 999]; MaxHP or MaxMP outside [1, 99999]; AttackPower, Defense or MagicDefense < 0; any of the five float stats ≤ 0 — returns `CorruptRecord` and `Record == null`. One test per bound, at the bound (Success) and one beyond it (`CorruptRecord`).
- [ ] **AC-CP-1**: a valid id with a matching account returns `Code = Success`, the record's `LastZoneID`, and a non-null `Record` equal field-by-field to the stored one.
- [ ] **AC-CP-6 (17 fields)**: after `ApplyLoadedRecord(entity, result.Record)`, `GetBaseStat` returns the record's value for each of the 11 int stats, `GetBaseStatFloat` for each of the 5 float stats, and `LevelingService.GetHeldFreePoints` returns `HeldFreePoints`.
- [ ] **No game-state write on a failed load**: for every non-Success code, no Character Stats setter and no Leveling call has run for any entity (checked through `OnStatChanged` count 0 and `GetHeldFreePoints` unchanged); `LoadCharacter` itself never touches Character Stats or Leveling, on any path.
- [ ] **AC-CP-5 (CR-CP-8)**: with the store set to `NeverCompleteNext` and the test cancelling the `CancellationTokenSource` by hand, the task completes with `Code = DatabaseError` and `Status == RanToCompletion`; no wall-clock wait.
- [ ] **Store fault**: a store call that faults (`FailNext`) gives `DatabaseError`, and the task is not faulted.
- [ ] **AC-CP-23**: a freshly created stub (`CreateStub`) loads and applies through the same path as any other record — no branch on `SaveVersion == 0` or `LastZoneID == ZoneID.Invalid`; after apply, MaxHP = 400 and AttackPower = 30.
- [ ] **Doc comments** on `ApplyLoadedRecord` and the new `Record` field (coding standards).

---

## Implementation Notes

- `LoadCharacter`'s synchronous prefix is the id check; the fetch is the first `await` (`ConfigureAwait(false)`); validation runs after it on the fetched object only — allowed by ADR-011 Decision 3 because it is not live state.
- The token: race the fetch against `ct` (e.g. a `TaskCompletionSource` with `RunContinuationsAsynchronously` registered on `ct`), and map cancellation and store exceptions to `DatabaseError` inside a try/catch so the returned task never faults.
- `ApplyLoadedRecord` takes a record that `LoadCharacter` has already validated; it does not re-validate. Order inside it for this story: `RegisterPlayerClassType`, the 11 int stats, the 5 floats, `RestoreLevelingState`. Whether `LevelingService.RegisterPlayerEntity` is also needed for a player entity is to be checked against Leveling Story 009 at implementation.
- The service gains `CharacterStats` and `LevelingService` (or narrow seams over them) as constructor dependencies; `CreateStub` and the existing tests must keep compiling.
- AC-CP-11 (all step-3 writes before the first `AddEquipmentModifier`) is Story 005's.

---

## Out of Scope

- Story 004: steps 5–10 (inventory, gold, at-cap, **CurrentHP / CurrentMP restore and clamp**, client events)
- Story 005: step 4 (equipment)
- Story 014: the same behaviour against PostgreSQL
- The Authentication call site and `EntityID` allocation (no implementation yet)

---

## QA Test Cases

- **Invalid id** — Given `CharacterID.Invalid`, When loaded, Then `CorruptRecord` and the store's fetch count is 0.
- **Not found / mismatch** — Given an empty store → `CharacterNotFound`; Given a record of account 7 loaded for account 8 → `AccountMismatch`, `Record == null`.
- **Ranges** — Given each field at its lowest valid, highest valid and first invalid value, Then valid → Success, invalid → `CorruptRecord`, `Record == null`.
- **Stats restored** — Given a record with distinct values per field, When loaded and applied, Then each of the 17 getters matches.
- **No write on failure** — Given each failure code, Then `OnStatChanged` count 0 and held free points unchanged.
- **Timeout** — Given `NeverCompleteNext` and a token cancelled by the test, Then `DatabaseError`, task not faulted, not canceled.
- **Fault** — Given `FailNext(new Exception())`, Then `DatabaseError` and the task is not faulted.
- **Stub** — Given `CreateStub` then `LoadCharacter` + `ApplyLoadedRecord`, Then Success with MaxHP 400 and AttackPower 30 (AC-CP-22's second half).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadCore_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002 (both Complete)
- Unlocks: Stories 004, 005
