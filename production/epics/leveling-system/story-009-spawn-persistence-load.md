# Story 009: Spawn Initialization & Persistence Load

> **Epic**: Leveling System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — spawn/load fires once per session start, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-27** [BLOCKING]: `InitializeAtL1(entity, ClassType.Warrior)` → Level=1, all primary attributes=10, Experience=0, heldFreePoints=0, F-3–F-9 computed at ×1.0. Zero `OnLevelUp` and zero `OnExperienceThresholdCrossed` firings.
- [x] **AC-LS-28** [BLOCKING]: Load from persistence (`SetBaseStat` calls restoring a L35 Warrior) → zero `OnLevelUp`, zero `OnExperienceThresholdCrossed`, no F-3–F-9 recompute triggered.
- [x] **AC-LS-29** [BLOCKING]: `heldFreePoints` persistence round-trip is lossless — `GetLevelingState` (save) then `RestoreLevelingState` (load) preserves the exact value; not recoverable from `GetBaseStat` (no `StatID.heldFreePoints` exists).
- [x] **AC-LS-30** [BLOCKING]: Corrupted `heldFreePoints` on load — `500` (above L60 Warrior max of 59) clamps to `59`; `-5` clamps to `0`; error logged in each case.
- [x] **AC-LS-51** [BLOCKING]: Corrupted `Level` on load — `Level=0` clamps to `1`; `Level=70` clamps to `60`; error logged in each case; no `IndexOutOfRangeException`; `LevelTierMultiplier` and AtCap state derive correctly from the clamped value.

---

## Implementation Notes

*Derived from CR-6, EC-LS-38, and the atomicity recovery invariant note (leveling-system.md):*

- **CR-6.1**: the Class System calls `LevelingSystem.InitializeAtL1(EntityID, ClassType)` after allocating the `CharacterStats` instance. NOT a level-up — `OnLevelUp` must not fire. `ClassType` is cached per-entity here and reused for all subsequent auto-alloc reads via `IClassRegistry` — it is never re-queried at level-up (see Story 002's dependency on this cache).
- **CR-6.2 — Init sequence** (within `BeginStatTransaction()`/`EndStatTransaction()` per Class System SA-2 — that transaction wrapping is the Class System's responsibility, this story's code just needs to be transaction-safe when called inside one):
  1. `SetBaseStat(Level, 1)`
  2. `SetBaseStat` STR=DEX=VIT=INT=10
  3. Evaluate F-3–F-9 at ×1.0, write via `SetBaseStat`
  4. `SetBaseStat(CurrentHP, MaxHP)`, `SetBaseStat(CurrentMP, MaxMP)`
  5. `heldFreePoints = 0`, `SetBaseStat(Experience, 0)`
- **CR-6.3 — Load path**: Character Persistence restores base stats via `SetBaseStat()` directly — `OnExperienceThresholdCrossed` must NOT trigger during load (same discipline as spawn: `SetBaseStat` bypasses `AddExperience` entirely, so the threshold-crossed path never engages). The Leveling System's only action on load is `RestoreLevelingState(EntityID, {heldFreePoints: savedValue})`. `LevelTierMultiplier` is always derived on demand from `GetBaseStat(Level)` — it is NEVER stored, on save or load.
- **Corrupted-value tamper defense (EC-LS-38, and the unnamed "Atomicity recovery invariant" note directly following EC-LS-36)** — required because this is a live MMORPG and save data can be corrupted or tampered:
  - `Level` outside `[1,60]` on load: clamp to nearest bound, log error. Required because an unclamped `Level=70` causes CR-2.9 to access `XpThreshold[71]`, beyond the 62-entry array — `IndexOutOfRangeException`.
  - `heldFreePoints` exceeding `(Level−1) × freePointsPerLevel[class]`: clamp to the max legal value for the (already-clamped) `Level`, log error.
  - **Partial-write crash recovery**: a crash between writing `Level` and writing `heldFreePoints` (Story 003's CR-2.8 atomicity requirement) can produce a state where neither value individually exceeds ITS OWN max but the pair is inconsistent. On load, additionally enforce `heldFreePoints <= (Level−1) × freePointsPerLevel[class]` using the (clamped) `Level` — if violated, clamp `heldFreePoints` down and log a warning. The inverse crash (heldFreePoints written, Level not advanced) is accepted as a safe-fail undercount, not corrected further.
- Interface note: `GetLevelingState(EntityID) → {heldFreePoints}` (save) and `RestoreLevelingState(EntityID, {heldFreePoints})` (load) are this story's only serialization surface — the GDD itself marks the real Character Persistence integration as "Not yet designed" for this specific pair of methods (Character Persistence's own epic doesn't exist yet). Implement and test these two methods against a test-local mock persistence caller, matching this project's established forward-dependency pattern.

---

## Out of Scope

*Handled by neighbouring stories:*

- The actual `ClassType`/`IClassRegistry` resolution mechanics beyond caching the value — Class System epic (not yet created)
- `XpThreshold` table population itself — Story 010

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_SpawnPersistenceLoad_tests.cs`

- **AC-LS-27**: Given freshly allocated CharacterStats, When `InitializeAtL1(entity, Warrior)`, Then Level=1, all attrs=10, Experience=0, heldFreePoints=0, zero level-up/threshold events.
- **AC-LS-28**: Given a L35 Warrior restored via SetBaseStat, When `RestoreLevelingState(entity, {heldFreePoints:7})`, Then zero events, no recompute, heldFreePoints readable as 7.
- **AC-LS-29**: Given a Healer L30 heldFreePoints=14, When save-then-load round-trips, Then 14 is preserved and not derivable from GetBaseStat.
- **AC-LS-30**: Given corrupted heldFreePoints=500 (L60 Warrior) and separately -5, When RestoreLevelingState runs, Then 500→59 and -5→0, errors logged.
- **AC-LS-51**: Given Level=0 (Case A) and Level=70 (Case B) restored, When RestoreLevelingState runs, Then Case A→1, Case B→60, errors logged, no exception, LevelTierMultiplier/AtCap derive correctly.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_SpawnPersistenceLoad_tests.cs` — must exist and pass

**Status**: [x] Created — `tests/EditMode/LevelingSystem/LevelingSystem_SpawnPersistenceLoad_tests.cs`, 11 tests. No live Unity Editor available this session — statically verified only (not executed by the Unity Test Runner).

---

## Dependencies

- Depends on: Story 002 (F-3–F-9 recompute at ×1.0, shared formula set), Character Stats Stories 001–007 (Complete)
- Unlocks: All other stories implicitly rely on `InitializeAtL1` for their test fixtures' starting state

---

## Completion Notes

**Completed**: 2026-09-24
**Criteria**: 5/5 passing
**Deviations**: None. GDD-vs-reality note (not a deviation, an already-established fix applied consistently): CR-6.2 step 4's literal `SetBaseStat(CurrentHP, MaxHP)`/`SetBaseStat(CurrentMP, MaxMP)` text is implemented as `SetCurrentHP`/`SetCurrentMP` instead — `CurrentHP`/`CurrentMP` live in separate dictionaries, not the `SetBaseStat` int array, the same fix Story 002 already applied to `ExecuteLevelUpSequence`'s CR-2.7 block. Verified true by both code reviewers via direct `CharacterStats.cs` reading, not assumed.
**Test Evidence**: Integration — `tests/EditMode/LevelingSystem/LevelingSystem_SpawnPersistenceLoad_tests.cs`, 11 tests (5 AC-mapped + 4 code-review-suggested boundary/fallback tests + 1 strengthened AC-LS-29 assertion). No live Unity Editor available this session — static verification only (hand-traced arithmetic, regex-vs-log-string checks), same disposition as every other story in this epic.
**Code Review**: Complete — lean self-performed review (`unity-specialist` + `qa-tester`, parallel). Both APPROVED WITH SUGGESTIONS, 0 BLOCKING. unity-specialist's 1 Required Change (a doc-only precondition note on `RestoreLevelingState` about the classType cache needing re-registration before a persistence-load call in a fresh session) and all 4 of qa-tester's test-coverage suggestions were applied.
