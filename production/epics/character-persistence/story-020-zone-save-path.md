# Story 020: Zone Save Path

> **Epic**: Character Persistence
> **Status**: Complete (2026-11-04; readiness 2026-11-04; written 2026-11-04 as the second half of the TD-091 fix; closes TD-091)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (save triggers), CR-CP-7 (one write in flight per character), CR-CP-10 (the `SaveSession` step sequence; step 6 `LastZoneID`), EC-CP-1 (an unknown item is removed in memory and "the DB record is corrected on the next save"); `design/gdd/item-database.md` — EC-20 (callers clean up stale `ItemID`s); `design/gdd/zone-instancing.md` — the zone-close steps (step 2 `SaveSession(ZoneClosed)` for admitted, non-entering players)
**Requirement**: `TR-cp-004` (CR-CP-4), `TR-cp-007` (CR-CP-7), `TR-cp-010` (CR-CP-10)

**ADR Governing Implementation**: ADR-006 (persistence: per-character write queue, write timeout), ADR-009 (S2 static data; a failure ends startup), ADR-011 (async persistence in the tick loop), ADR-014 (the composition root builds server-only components in code, in a checked S5 order)
**ADR Decision Summary**: ADR-006: writes are serialised per `CharacterID` by the service's queue, each bounded by `PERSISTENCE_WRITE_TIMEOUT_SECONDS`. ADR-009: missing static data ends startup. ADR-011: the save copies live state before its first `await`. ADR-014: S5 builds every component in a fixed order that the construction-order test checks.

**Control Manifest** (2026-10-26): tick code passes every `ICharacterPersistence` task to `ITickCompletionQueue.Track` in the same statement (ADR-011); persistence copies live state before its first `await`, on the tick thread (ADR-011); every `await` uses `ConfigureAwait(false)` and every `TaskCompletionSource` (test fakes included) uses `TaskCreationOptions.RunContinuationsAsynchronously` (ADR-011); server-only components are created in code by the server composition root (ADR-012). A startup step that returns false makes the composition request `ZoneExitCode.StartupFailed` and run no later step (checked at readiness), so criterion 3's S5 failure ends startup.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; no Unity API beyond `Debug.Log*`. No post-cutoff API.

---

## Problem (TD-091, second half)

After Story 019 the zone restores bag and gear through the real Inventory and Equipment, but `BuildPersistence` (`ZoneServerComposition.cs`) still uses the six-service `CharacterPersistenceService` constructor: no zone locator, no write queue, so `SaveSession` throws at its wiring check and nothing a player does in a zone is saved. Story 019 also left four conditions for enabling saves (TD-091):

1. The S5 empty-item-database fallback (warning, tests only) must become a startup failure: with an empty database every bag is imported empty and would then be saved that way.
2. Session-end unregister (Inventory / Equipment) runs in `SessionEndCoordinator` only after a save, so it was unreachable.
3. An unknown bag item is skipped at import (one warning); the next save drops it from the record. **Decided by the GDD**: EC-CP-1 says the record is corrected on the next save, and item-database.md EC-20 makes callers clean up stale references. Bag items follow the same rule as gear.
4. Zone close started no save because the admitted player stayed "entering". Finding at drafting: "entering" ends when a `ZoneEntryAck` naming the zone is applied by `ZoneEntryReclamation.OnTick`, and the ack is already registered with the dispatcher (`ZoneInstancingWiring.cs` ~274). SessionReady is not needed for this.

---

## Acceptance Criteria

- [x] **Locator**: a new `ZoneServerCharacterZoneLocator : ICharacterZoneLocator` (`src/ServerLogic/ZoneServer/`) returns the zone's own `ZoneID` (the composition's `_zoneId`, set from the arguments at S0) for any character. Unit-tested.
- [x] **Save constructor**: S5 builds a `CharacterWriteQueue` and the locator and constructs `CharacterPersistenceService` with the ten-argument constructor: the clock is `() => DateTime.UtcNow` (the composition is the one place that reads the system clock; Character Persistence itself never does) and the timeout is `TimeSpan.FromSeconds(CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS)`. `Observe(nameof(CharacterWriteQueue))` and `Observe(nameof(ZoneServerCharacterZoneLocator))` are recorded before `CharacterPersistenceService`, and the construction-order list in `ZoneServer_CompositionWiring_tests.cs` is updated to match.
- [x] **Missing item database fails startup**: with no `ItemDatabaseOverride` and no `IZoneItemData.LoadedItemDatabase` from S2, S5 logs one error "[ZoneServer] S5: no item database from S2" and `ConstructObjects` returns false (startup fails at S5; no Inventory, Equipment or Persistence built). The empty-database fallback and its warning are removed. Test rigs that use fake S2 loaders set `ItemDatabaseOverride` (an empty ready database or `MvpItemFixture`); the `LogAssert.Expect` of the old warning in `ZoneServer_CompositionWiring_tests.cs` is replaced by a test of the failure.
- [x] **Zone close saves**: a player admitted through the composed zone whose `ZoneEntryAck` has been applied (no longer entering) gets exactly one store write at zone close (close step 2, `SessionEndReason.ZoneClosed`), with no "failed save start" log. The written record holds the bag (slot, item, quantity, enhancement level) from `ExportSnapshot`, the gear from `ExportSlots`, the gold, `LastZoneID` equal to the zone's `ZoneID`, and `SaveVersion` = loaded + 1. A still-entering player is skipped as before (0 writes for it).
- [x] **Clean logout unregisters**: `ZoneInstancing.SessionEnds.BeginCleanLogout` for an admitted player makes one write, reports `CharacterSaveResult.Success` to its callback (after the completion queue is drained), and afterwards neither Inventory nor Equipment holds the character (Story 019 deviation 1).
- [x] **Round trip**: a player admitted, whose bag is changed in the zone (e.g. an item added through `InventoryService`), logs out cleanly; a re-admission of the same character on a new connection, loaded from the in-memory store, restores the changed bag, the gear and the gear's stat modifiers in Character Stats.
- [x] **Unknown items are cleaned on the next save**: a record with one bag item and one gear item not in the database is admitted (the existing import warning and the `ItemDatabaseMiss` warning); the next save writes the record without both items and with everything else unchanged. `design/gdd/character-persistence.md` EC-CP-1 gets one line: a bag item the Item Database does not know is dropped at import (Inventory `ImportSnapshot`, one warning) and the record is corrected on the next save, as for gear.
- [x] **Cleanup**: Story 019's `test_zone_real_inventory_save_session_still_throws_before_any_write` and `test_zone_real_inventory_zone_close_writes_nothing` are replaced by the tests above; the TD-091 comments in `ZoneServerComposition.cs` (`BuildPersistence`, `BuildInventoryAndEquipment`) are updated; TD-091 in `docs/tech-debt-register.md` is marked Resolved (Story 020); the full EditMode suite passes.

---

## Implementation Notes

- The locator returns the zone's id for every character: a zone's persistence service only ever saves characters admitted to that zone, so no directory lookup is needed (the directory is also built later, in `BuildDispatcher`).
- `_zoneId` is set at S0 (`StartupReadArgsAndConfig`), before S5.
- Ending "entering" in a test: deliver a `ZoneEntryAck` for the zone through the rig's dispatcher (or `Reclamation.OnConnectionMessage`) and run one tick so `OnTick` applies it.
- `SaveSession` takes its snapshot on the tick thread before the queue (ADR-011); the write completes off-thread and its result comes back through the `ITickCompletionQueue`, so tests drain the queue before asserting the callback.
- Rigs that build the composed zone (shared `WiRig` users): `CharacterPersistence_ZoneApplier_tests.cs`, `CharacterPersistence_ZoneRealInventory_tests.cs`, `ZoneServer_CompositionWiring_tests.cs`, `ZoneServer_CompositionDatabase_tests.cs`, `AutoAttackCombat_InputAndComposition_tests.cs`, `HitDetection_ResultEmission_tests.cs`, `MovementSystem_PipelineWiring_tests.cs`, `SkillSystem_Composition_tests.cs`, `SkillSystem_ZoneRosterData_tests.cs`, `StatusEffects_PipelineWiring_tests.cs`. Check each for reliance on the fallback; prefer setting the override once in the shared rig.
- Performance: no per-tick cost; one write per session end.

---

## Out of Scope

- Wiring the client logout message to `BeginCleanLogout`, and a real ghost-session source for TTL expiry (session epic).
- Sending `SessionReady` in the zone.
- Loading `EnhancementConfig` from an asset at S2.
- Sending inventory or equipment changes to the client.
- A real-PostgreSQL run of the zone save path (the Npgsql store is covered by Story 016).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneSavePath_tests.cs` — must exist and pass
- `ZoneServer_CompositionWiring_tests.cs` — construction order and the missing-database startup failure

**Status**: [x] Created — `CharacterPersistence_ZoneSavePath_tests.cs` 11 tests, all passing (after `/code-review`: full EditMode 6512 / 6383 passed / 0 failed / 129 skipped, no `error CS`, started 2026-11-04 23:24:11Z)

---

## Dependencies

- Depends on: Story 019 (Complete), Story 017 (Complete), Story 010 (Complete)
- Unlocks: closes TD-091; progress saved in a zone

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 8/8 passing (all automated).
**Deviations** (advisory):
- In a live zone, clients still cannot trigger logout or session expiry: nothing calls `BeginCleanLogout` and the ghost-session source is a stand-in (session epic, out of scope; noted in TD-091). The tests call `ZoneInstancing.SessionEnds.BeginCleanLogout` directly and register the account in the zone's `ConnectionStateMachine` themselves.
- Finding: clean logout also frees the player's zone slot (the session-end release calls the zone's player-exit `ReleasePlayer`); the round-trip test asserts the slot is already gone.
- `CharacterPersistence_ZoneRealInventory_tests.cs` went from 12 to 9 tests: the empty-database fallback test and the two disabled-save tests no longer apply.
- Test-only addition: `WiRig` defaults `ItemDatabaseOverride` to an empty ready database (`WiRig.EmptyItemDatabase()`); tests of the S2 path set it back to null.
**Test Evidence**: Integration: `tests/EditMode/CharacterPersistence/CharacterPersistence_ZoneSavePath_tests.cs` (11). Full EditMode 6512 / 6383 passed / 0 failed / 129 skipped, no `error CS`, started 2026-11-04 23:24:11Z.
**Code Review**: Complete: `/code-review` APPROVED WITH SUGGESTIONS (unity-specialist CLEAN, qa-tester GAPS no blockers); all 4 suggestions applied. QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean mode).
**Resolves**: TD-091.
