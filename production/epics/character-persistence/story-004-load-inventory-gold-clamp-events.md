# Story 004: `LoadCharacter` — Inventory, Gold, At-Cap, HP/MP Clamp

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-15 (COMPLETE WITH NOTES; see Completion Notes)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-3 steps 5 to 8; F-CP-4 (the clamp); EC-CP-2; AC-CP-10, AC-CP-25. `design/gdd/inventory-system.md` — Persistence and Load Edge Cases (a refused import is never an empty inventory). `design/gdd/death-and-respawn.md` — EC-DR-2 (dead-on-load). `design/gdd/leveling-system.md` — at-cap derived from `GetBaseStat(Level) == 60` (line 383). Steps 9–10 and AC-CP-8 / AC-CP-9 moved out (readiness decision 2).
**Requirement**: `TR-cp-003`
*(Epic placeholder: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness`, as for Stories 001–003.)*

**ADR Governing Implementation**: ADR-011 Decision 3; ADR-010 (direct calls on injected interfaces).
**ADR Decision Summary**: the load restores state by calling each owning system's restore API on the tick thread, from `ApplyLoadedRecord`; it computes no game value.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — plain C#, no engine API (`LogAssert` in tests only).

**Control Manifest Rules (this layer)**:
- Required: cross-system communication is a direct call on an injected interface — ADR-010
- Forbidden: a central EventBus / MessageBus — ADR-010
- Forbidden: reading or writing live game state after the first `await` in the persistence layer — ADR-011 (all of this story runs in the synchronous `ApplyLoadedRecord`)

---

## Readiness decisions (2026-10-15)

1. **Gold restore: a narrow interface.** New `ICurrencyStateRestore { void RestoreCharacter(CharacterID charId, uint balance, uint version); }` in `src/ServerLogic/Currency/`, implemented by `CurrencySystem`: sets `_balances[charId]` and `_versions[charId]`, raises no `OnGoldSync`, writes no transaction. `ICurrencyService.RegisterCharacter` is not used (it resets the version to 0, `CurrencySystem.cs:95`), and `ICurrencyService` is not changed (four test fakes implement it). This is an edit to the Currency System made from this story (approved 2026-10-15).
2. **Steps 9–10 move out.** `ApplyLoadedRecord` sends nothing to the client. The session handshake already carries gold and stats (`networking-session.md:30`, CR-NET-6; `SessionHandshakeData` has `GoldVersion`, `Level`, `CurrentHp`, `CurrentMp`, `HeldFreePoints`), `StatSnapshotEvent` has no wire type or message id in code (OQ-NC-SER-2), and persistence has no client id. AC-CP-8 (`GoldSyncEvent`) and AC-CP-9 (stat snapshot) belong to the handshake work in Networking / Authentication — epic design conflict 12.
3. **`ApplyLoadedRecord` returns `CharacterLoadCode`.** `Success`, or a new value `InventoryNotRestored = 5` when `ImportSnapshot` returns false ("retry or reject the login" — the database worked; the Item Database was not ready). Story 005 returns `ItemDatabaseMiss` through the same value. On any code other than `Success` (and `ItemDatabaseMiss`, Story 005) the caller discards the entity: Story 003's stat writes have already happened. Epic design conflict 13 (a CR-CP-1 enum value).
4. **At-cap: no call.** Leveling stores no at-cap flag; it is derived from `GetBaseStat(Level) == 60`, which Story 003 already writes. Step 7 is verified, not implemented.

---

## Acceptance Criteria

- [ ] **Inventory (step 5)**: the 20 dense slots are translated to an `InventorySnapshot` of `InventorySnapshotEntry(slotIndex, itemId, quantity, enhancementLevel)` entries, skipping slots whose item is `ItemID.Invalid`, and passed to `IInventoryService.ImportSnapshot(record.CharacterID, snapshot)`. A record with items in slots 0, 7 and 19 gives a three-entry snapshot with those indices, counts and levels; an empty bag gives an empty snapshot, not null.
- [ ] **Refused import**: if `ImportSnapshot` returns false, `ApplyLoadedRecord` returns `InventoryNotRestored`; gold is not restored and CurrentHP / CurrentMP are not written. (The test expects the Inventory System's server error with `LogAssert`.)
- [ ] **Gold (step 6)**: `ICurrencyStateRestore.RestoreCharacter` is called with the record's `GoldBalance` and `GoldVersion`; afterwards `ICurrencyService.GetBalance` returns the balance, a following `TrySpendGold` succeeds against that version, and `OnGoldSync` was raised 0 times by the restore.
- [ ] **AC-CP-10 (step 7)**: after applying a Level-60 record, `AddExperience` raises zero `OnLevelUp` events and Level stays 60; after applying a Level-59 record, a grant that crosses the threshold still levels up.
- [ ] **CurrentHP / CurrentMP restore (step 8; moved from Story 003)**: written here through `SetCurrentHP` / `SetCurrentMP`, after the gold step.
- [ ] **Dead-on-load (step 8, EC-DR-2)**: a record with `CurrentHP <= 0` loads with `CurrentHP` = the effective MaxHP (`GetEffectiveStat(MaxHP)`). Edge: a negative saved HP.
- [ ] **AC-CP-25 / F-CP-4 (step 8)**: `CurrentHP` ends in [0, MaxHP] and `CurrentMP` in [0, MaxMP], using the effective maxima as they stand after the equipment step; saved 300 of 400 loads as 300; saved 650 with a MaxHP of 500 loads as 500, with no error logged. Same for MP. The clamp runs on every load.
- [ ] **Order**: inside `ApplyLoadedRecord`, after Story 003's writes: inventory import, gold restore, HP/MP. Recording test doubles of `IInventoryService` and `ICurrencyStateRestore` show the sequence.
- [ ] **Return value**: a record that applies fully returns `Success`; Story 003's tests keep passing against the new return type.
- [ ] **Doc comments** on `ICurrencyStateRestore`, `RestoreCharacter`, the new enum value and `ApplyLoadedRecord`'s return value.

---

## Implementation Notes

- `IInventoryService.ImportSnapshot(CharacterID, InventorySnapshot?)` and `InventorySnapshot(IReadOnlyList<InventorySnapshotEntry>)` / `InventorySnapshot.Empty` exist. `ImportSnapshot` returns false (with a server error, nothing changed) only for a null snapshot or an Item Database that is not ready; individual bad entries are dropped with warnings and the import still returns true.
- `SetCurrentHP` / `SetCurrentMP` already clamp to [0, effective max] (`CharacterStats.cs:789`), so F-CP-4 needs no code of its own; only the dead-on-load substitution does.
- The service gains `IInventoryService` and `ICurrencyStateRestore` as constructor dependencies (a wider constructor beside the existing ones; Story 003's tests keep compiling).
- Adding `InventoryNotRestored = 5` changes `CharacterLoadCode`: update Story 001's contract test that pins the enum values.
- `ApplyLoadedRecord` changes from `void` to `CharacterLoadCode`.

**Authorized beyond `src/ServerLogic/CharacterPersistence/`**: `src/ServerLogic/Currency/ICurrencyStateRestore.cs` (new), `src/ServerLogic/Currency/CurrencySystem.cs` (implements it), `tests/EditMode/CharacterPersistence/CharacterPersistence_Contract_tests.cs` (enum value).

---

## Out of Scope

- Story 003: steps 1–3 and 11
- Story 005: step 4 (equipment); AC-CP-24 (the clamp after an item miss) is there
- Steps 9–10 and AC-CP-8 / AC-CP-9 (the `GoldSyncEvent` and `StatSnapshotEvent` sends) — the session handshake, Networking / Authentication (design conflict 12)
- The `SessionReady` message and the client side
- The caller that discards the entity on a failure code (Authentication / zone spawn)

---

## QA Test Cases

- **Dense to sparse** — Given items in slots 0, 7, 19 with counts and levels, When applied, Then `ImportSnapshot` receives exactly three entries with those values. Edge: an empty bag gives an empty snapshot, not null.
- **Refused import** — Given `ImportSnapshot` returns false, Then `InventoryNotRestored`, the currency restore was not called, CurrentHP is unchanged.
- **Gold restore** — Given balance 1,250 and version 9, Then `GetBalance` is 1,250, a spend succeeds, and `OnGoldSync` fired 0 times during the restore.
- **At-cap** — Level 60 → `AddExperience` gives zero level-up events; Level 59 → a crossing grant levels up.
- **Dead-on-load** — Given `CurrentHP` 0 and MaxHP 400, Then 400. Edge: negative saved HP.
- **Clamp** — 300 / 400 → 300; 650 / 500 → 500; MP likewise.
- **Order** — the recorded call order is import, gold restore; HP/MP written after both.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadRestore_tests.cs` — must exist and pass.

**Status**: [x] Created — 20 cases (12 `[Test]`, 8 `[TestCase]`), EditMode 2701 / 2701 (batch mode, 2026-10-15)

---

## Dependencies

- Depends on: Story 003 (Complete)
- Unlocks: Story 016

---

## Completion Notes

**Completed**: 2026-10-15
**Criteria**: 10/10 passing (nine by automated test; "Doc comments" by reading)
**Test Evidence**: Integration — `tests/EditMode/CharacterPersistence/CharacterPersistence_LoadRestore_tests.cs`, 20 cases (12 `[Test]`, 8 `[TestCase]`); also `tests/EditMode/Currency/Currency_StateRestore_tests.cs` (4) and 7 new cases in `CharacterPersistence_LoadCore_tests.cs`; EditMode 2701 / 2701 (batch mode)
**Code Review**: Complete — `/code-review` 2026-10-15 (`unity-specialist`, `qa-tester`): CHANGES REQUIRED; "fix all" applied and re-run
**Gates**: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — lean mode

**Files**: `src/ServerLogic/Currency/ICurrencyStateRestore.cs` (new), `src/ServerLogic/Currency/CurrencySystem.cs` (`RestoreCharacter`), `src/ServerLogic/CharacterPersistence/CharacterLoadCode.cs` (`InventoryNotRestored = 5`), `src/ServerLogic/CharacterPersistence/CharacterPersistenceService.cs` (5-argument constructor; `ApplyLoadedRecord` returns `CharacterLoadCode`; inventory, gold, HP / MP; gold-cap and NaN checks in `IsInRange`); tests: the LoadRestore file, `CharacterPersistenceTestFakes.cs` (shared `RecordingInventory`), `Currency_StateRestore_tests.cs`; edited: the Story 001 contract test (enum pin), the Story 003 LoadCore file (SetUp, the stat-event control counted by `StatID`, gold-cap and NaN cases; `ApplyLoadedRecord_LeavesCurrentHpAndMpUntouched` deleted — its intent is now the HP/MP-at-import probe).

**Deviations (advisory, accepted by the user 2026-10-15)**
1. CR-CP-3 steps 9–10 and AC-CP-8 / AC-CP-9 not implemented — moved to the session handshake (readiness decision 2; epic design conflict 12).
2. `CharacterLoadCode.InventoryNotRestored = 5` is not in CR-CP-1 (design conflict 13).
3. Beyond CR-CP-3 step 3: `GoldBalance > CurrencySystem.GOLD_CAP` and a NaN `CurrentHP` / `CurrentMP` give `CorruptRecord` (found at code review).
4. A refused import, or an exception part-way through `ApplyLoadedRecord`, leaves the stat writes and the Leveling registration in place; nothing rolls back — the caller discards the entity.
5. `RestoreCharacter` does not clamp the balance; it relies on its caller (the load rejects above-cap records first).
6. Dead-on-load against an effective MaxHP that differs from the base is not exercised (no gear before Story 005) — recorded in Story 005 as a test obligation.

**For later stories**
- Story 005: insert step 4 between the stat writes and the inventory import inside `ApplyLoadedRecord`; return `ItemDatabaseMiss` through its return value; the dead-on-load test obligation above.
- Authentication / zone spawn (the caller): track `LoadCharacter`'s task, then call `ApplyLoadedRecord` and discard the entity on any code other than `Success` / `ItemDatabaseMiss`; deliver gold and stats through the session handshake (AC-CP-8 / AC-CP-9).
- GDD: lines for epic design conflicts 12 and 13.

**Tech debt logged**: none (user's choice)
