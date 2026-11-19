# Story 012: End-to-End — Two Players Party Up and Share a Kill

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 5 h (readiness 2026-11-19: was 4 h)
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-4, CR-PS-5, F-PS-1, CR-PS-9
**Requirement**: `TR-party-012` (end-to-end)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The tick order from Story 011 is what this test runs.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: If PlayMode: test assembly has `InternalsVisibleTo` on ServerLogic (ADR-012 Decision 5 amendment).

**Control Manifest Rules (this layer)**:
- Required: deterministic tests (seeded `IRandomProvider`, no wall-clock assertions)

---

## Acceptance Criteria

- [x] **AC-1 Form**: in a started `ZoneServerComposition` with two admitted Level 1 players A and B (within ±5) and one roster mob, the tick sequence is: A sends `PartyInviteRequest` (0xE172, `TargetCharacterId` = B) → tick → B receives `PartyInviteReceived` (0xE173, decoded, `InviterCharacterId` = A) → B sends `PartyInviteResponse` (0xE174, `InviterCharacterId` = A, `Accepted` true) → tick → A and B each receive exactly one `PartyStateUpdate` (0xE170, decoded) listing [A, B] in join order, leader A, `IsSoloParty` false, same PartyID. Requests go through the inbound dispatcher, not service calls.
- [x] **AC-2 Share**: A kills the mob with one skill cast (fixed damage ≥ the mob's HP, see Implementation Notes) → A's and B's `Experience` (`ZoneCharacterStats.GetBaseStat(e, StatID.Experience)`) each rise by exactly F-PS-1 for N = 2: `Mathf.RoundToInt(0.9f × XP_base)`, where `XP_base` is the mob's `MobRuntimeStore` entry `Variant.EffectiveKillXP` read before the kill (fixture 10 → 9). The test asserts `XP_base` is below both players' L1 threshold, so neither levels up (`Level` stays 1). No other player's `Experience` changes.
- [x] **AC-3 Leave**: B sends `PartyLeaveRequest` (0xE175) → tick → the party auto-disbands (Story 008 AC-3): B receives its solo `PartyStateUpdate` (`IsSoloParty` true, 1 member); A receives `PartyDisbanded` (0xE171) and its solo `PartyStateUpdate`; A's and B's new PartyIDs are non-zero, differ from each other and from the old party id. A second mob (same type) is killed by A the same way → A's `Experience` rises by the full `XP_base`, B's by 0.
- [x] **AC-4 Clean run**: from the first invite to the end of AC-3, no `Error` / `Exception` log other than the expected ones (the `[Movement] NavMesh unavailable` error on the warm-up tick is expected with `LogAssert.Expect`; the error count uses a scoped counter over that window, not `LogAssert.NoUnexpectedReceived`, which a startup log trips — Story 011). The party step's per-tick cost is measured by probe steps inserted directly before and after `PartyTickStep` in `SimulationSteps` (Stopwatch ticks) over 200 ticks with the party formed, and logged once as `[PartySystem012] party step max=<ms> p95=<ms> (reference 0.1 ms)`. No timing assertion (manifest: no wall-clock assertions).

---

## Implementation Notes

- **Rig (readiness 2026-11-19): EditMode**, no PlayMode. Follow `tests/EditMode/SkillSystem/SkillSystem_Composition_tests.cs`: `WiRig`, `ClassRegistryOverride` + `SkillDatabaseOverride` (one hostile `PhysicalDamage` skill, unlock 1, in range), `RespawnProviderOverride`, `SkillDamageCalculatorOverride` (fixed damage), `Start`; `Admit` each player (pool slot, dispatcher connection, guard chain, directory, Level 1 record through `PersistenceService.ApplyLoadedRecord`, `EntryCompletion.OnPlayerAdmitted` — this also fires the party entry listener, so both are zone-present). Mob: `MobRoster.TryAddMob` + `MobEvents.RaiseMobSpawned` (store entry needed for the XP award). Inbound messages as the `SendCast` helper; outbound decoded from `rig.Net.Seam.Sent` sub-messages per client.
- **Kill by skill cast, not auto-attack (readiness G2):** the composition's `RandomProvider` is entropy-seeded (`ZoneServerComposition.cs:1892`, TD-110), so an auto-attack kill is not deterministic (crit rolls). `SkillDamageExecutionStep` calls the same `PartyExperienceDistributor` as Combat (Story 011 AC-2), so a skill kill with `SkillDamageCalculatorOverride` covers the party XP path deterministically. Do not use `EntityStateSourceOverride = AliveZoneState` if it hides the mob's death; if a stand-in state source is needed, it must report the mob dead after the kill.
- `XP_base` is read from the mob's runtime store entry, never a literal.
- Copy the needed helpers into the new test file (or a small `PartyEndToEndRig` in `tests/EditMode/PartySystem/`); do not change `SkillSystem_Composition_tests.cs`.

---

## Out of Scope

- Loot round-robin end to end (real loot not composed — moved out of the epic)
- Client presentation

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refined at `/story-readiness` 2026-11-19.*

- **AC-1**: invite → B gets one `PartyInviteReceived` naming A, A gets no `PartyInviteResult` error; response → exactly one `PartyStateUpdate` each to A and B, same PartyID, members [A, B], leader A, not solo; before the response neither has a non-solo update.
- **AC-2**: `Experience` before / after for A, B (and unchanged for any other player entity); delta = `RoundToInt(0.9 × XP_base)`; `XP_base` < L1 threshold asserted first; `Level` 1 after.
- **AC-3**: leave → B solo update; A `PartyDisbanded` + solo update; three PartyIDs distinct and non-zero; second kill → A + `XP_base`, B + 0.
- **AC-4**: scoped error count 0 over the window; `[PartySystem012]` line logged once with max and p95; no timing assert.
- **Not one test per AC required**: one scenario test that walks AC-1 → AC-3 is fine, with a separate perf test for AC-4 if that keeps the scenario readable.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_EndToEnd_tests.cs` (EditMode decided at readiness 2026-11-19) — must exist and pass

**Status**: [x] Created — 2 tests, all passing (2026-11-19)

---

## Dependencies

- Depends on: Story 011
- Unlocks: None (last story of the epic)

---

## Completion Notes
**Completed**: 2026-11-19
**Criteria**: 4/4 passing (all automated)
**Deviations**:
- Production defect found and fixed (user decision at `/story-done`): no `src/` path called `LevelingService.RegisterPlayerEntity`, so `CharacterStats.AddExperience` dropped every kill award for real players (solo and party). `CharacterPersistenceService.ApplyLoadedRecord` now registers the entity; regression test `CharacterPersistence_LoadRestore_tests.ApplyLoadedRecord_RegistersThePlayerEntity_SoAddExperienceIsCounted`; manual registrations removed from this test and two LoadRestore tests. No unregister on slot release (TD-115).
- Kill is faked (fixed killing skill damage + `DeathAwareZoneState`): the test proves the party XP wiring end to end, not mob HP depletion or death detection (readiness G2, TD-110).
- Error counter also counts warnings (stricter than AC-4).
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_EndToEnd_tests.cs` 2/2; full EditMode `-testCategory "!Database"` 8615/8615. `[PartySystem012] party step max=0.0220 p95=0.0024 (reference 0.1 ms)`.
**Code Review**: Complete (`/code-review` test file: R1 bystander + all suggestions applied). The one-line `CharacterPersistenceService` fix was not separately reviewed. Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
