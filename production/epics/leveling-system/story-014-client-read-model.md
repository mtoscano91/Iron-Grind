# Story 014: Client Read Model — the HUD Stops Referencing `LevelingService`

> **Epic**: Leveling System
> **Status**: Complete (2026-10-08)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 5 hours

## Context

**GDD**: `design/gdd/leveling-system.md` — the server owns leveling: XP crossing, the level-up loop, auto-allocation and respec are validated and applied by `LevelingService` (CR-2.9, CR-2.10, CR-4.3, CR-4.4). The HUD shows results the player is allowed to see: level, XP bar, the tier multiplier, the respec preview (AC-LS-46, AC-LS-47, AC-LS-48).
**Requirement**: none registered — the requirement is ADR-012's. `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 7 (what the client reads: read-only views, requests, display formulas, the manual harness), Decision 3 rule 3 and the Decision 7 exception to rule 2, Decision 6 check 1 (shared allow-list), Migration Plan step 2 item 6 ("Leveling (with the client read model of Decision 7; the UI stops referencing `LevelingService`)").
**ADR Decision Summary**: `IronGrind.Client` cannot reference `IronGrind.ServerLogic`. Before `LevelingService` can move there, the six UI files in `src/Client/` must stop naming it. They read through interfaces declared in `Foundation`, send the respec through a request interface declared in `Foundation`, and evaluate display formulas from one static class in `Foundation` that the server also calls. The manual test harness, which builds a real `LevelingService`, leaves `Client` for its own Editor-only assembly.

**This story is the first half of ADR-012's Leveling step.** It changes code and leaves every Leveling type in `Foundation`. Story 015 (not yet written) is the mechanical move of the server types. ADR-012 Decision 7 puts both in "the Leveling move story"; they are split here so that the refactor is verified on a green suite before any file changes assembly.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — one engine behaviour is unconfirmed: ADR-012 Verification Required 8, whether a `MonoBehaviour` in an assembly with define constraint `UNITY_EDITOR` (not an Editor-platform assembly) can be added to a scene object and runs in Play mode.
**Engine Notes**:
- `src/` is a local package (`src/package.json`); a new folder `src/DevHarness/` with its own `.asmdef` is picked up like `src/Client/` was. Unity generates `.meta` files for every new file and folder; they are committed.
- The asmdef for the harness: name `IronGrind.DevHarness`, `defineConstraints: ["UNITY_EDITOR"]`, `autoReferenced: false`, no `includePlatforms`/`excludePlatforms`, references `IronGrind.Foundation`, `IronGrind.ServerLogic`, `IronGrind.Client`.
- Move the harness file with `git mv` together with its `.cs.meta` (GUID `997022cf9e3e1187bd460a62da5c1b83`). No scene, prefab or asset under `Assets/` references that GUID today (checked 2026-10-08), so nothing needs re-saving.
- `Mathf.FloorToInt` and `Mathf.Min` are used by the formulas; `IronGrind.Foundation` has engine references, so the display-formula class may use them as the service does today.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Presentation layer)**:
- Required: a type is added to `IronGrind.Foundation` only with its client consumer named on the boundary test's shared allow-list.
- Required: a `Client` presenter depends on a view and a request interface in `Foundation`, never on a service.
- Required: one copy of a display formula; `ServerLogic` code calls the `Foundation` class.
- Forbidden: `InternalsVisibleTo` between production assemblies; a formula that decides an outcome the player does not see computed (damage, crit, drop rolls, enhancement odds) in the display-formula class.

---

## What the Client Uses Today (checked 2026-10-08)

| File in `src/Client/UI/LevelingSystem/` | Server type use (code, not comments) |
|---|---|
| `LevelUpOverlayPresenter.cs` | field and constructor parameter `LevelingService`; `OnLevelUp` subscribe and unsubscribe; `LevelingService.GetLevelTierMultiplier` twice |
| `RespecScreenPresenter.cs` | field and constructor parameter `LevelingService`; `GetHeldFreePoints`; `TryApplyRespec`; `LevelingService.GetLevelTierMultiplier` once; `LevelingFormulaPreview` three times |
| `LevelingHudController.cs` | `Initialize` parameter `LevelingService`, passed to the two presenters |
| `LevelingFormulaPreview.cs` | none in code. It holds a second copy of the derived-stat formula that `LevelingService.RecomputeDerivedStats` also holds (the seven expressions are textually identical today) |
| `PlayerResourceClusterPresenter.cs` | none (`CharacterStats` and a threshold list only) |
| `LevelingHudManualTestHarness.cs` | constructs `LevelingService`, `CharacterStats`, `ClassRegistry`; calls `AttachCharacterStats`, `RegisterPlayerEntity`, `InitializeAtL1` |

`CharacterStats` stays a direct dependency of the presenters in this story: it is still in `Foundation`, and ADR-012 Decision 7 completes the stats view in the Character Stats move story.

---

## Design

### New types in `IronGrind.Foundation` (folder `src/Foundation/LevelingSystem/`, namespace `IronGrind.LevelingSystem`)

1. **`LevelingDisplayFormulas`** — `public static class`. The one copy of:
   - `float GetLevelTierMultiplier(int level)` — moved from `LevelingService` (body unchanged).
   - `DerivedStats ComputeDerivedStats(int strength, int dexterity, int vitality, int intelligence, float tier)` and the `readonly struct DerivedStats` it returns (the seven values) — moved from `LevelingFormulaPreview.ComputeDerivedStatsPreview` / `DerivedStatsPreview` (expressions unchanged).
   - `int GetAutoAllocIncrement(ClassDefinition def, StatID stat)` and `int GetRespecFloor(int level, int autoAllocIncrement)` — moved from `LevelingFormulaPreview`.
   - Every member's doc comment names its client consumer and the GDD rule it comes from.
2. **`ILocalPlayerLevelingView`** — `public interface`: `int GetHeldFreePoints(EntityID entityId);`. It carries no event: level-up notification is already `ILevelingEventBroadcaster.OnLevelUp`, which ADR-012 Decision 7 keeps in `Foundation`.
3. **`IRespecRequestSender`** — `public interface`: `void RequestRespec(EntityID entityId, IReadOnlyDictionary<StatID, int> newTotals);`. Doc comment: the implementation may throw if the request is rejected synchronously (the harness adapter does, because it calls the service directly); a networked client implementation sends a message and the result arrives as state. The presenter keeps its `try`/`catch`.

### Changes in `IronGrind.Foundation` server code (still in `Foundation` after this story)

- `LevelingService.GetLevelTierMultiplier` is deleted; its four internal call sites call `LevelingDisplayFormulas.GetLevelTierMultiplier`.
- `LevelingService.RecomputeDerivedStats` keeps reading the four primaries and writing the seven derived stats, but gets the values from `LevelingDisplayFormulas.ComputeDerivedStats` instead of computing them inline.
- No other change to `LevelingService`. It does **not** implement `ILocalPlayerLevelingView` or `IRespecRequestSender`: a server class does not carry the client's view.

### Changes in `IronGrind.Client`

- `LevelUpOverlayPresenter`: depends on `ILevelingEventBroadcaster` (not `LevelingService`); tier multiplier from `LevelingDisplayFormulas`.
- `RespecScreenPresenter`: depends on `ILocalPlayerLevelingView` and `IRespecRequestSender`; formulas from `LevelingDisplayFormulas`.
- `LevelingHudController.Initialize`: the `LevelingService` parameter is replaced by three parameters — `ILevelingEventBroadcaster`, `ILocalPlayerLevelingView`, `IRespecRequestSender`.
- `LevelingFormulaPreview.cs` is deleted (with its `.meta`); its members now live in `LevelingDisplayFormulas`.
- After this story no file under `src/Client/` names `LevelingService`.

### New assembly `IronGrind.DevHarness` (`src/DevHarness/`)

- `IronGrind.DevHarness.asmdef` as in Engine Notes.
- `LevelingHudManualTestHarness.cs` moves to `src/DevHarness/UI/LevelingSystem/` (namespace unchanged, GUID unchanged). Its `#if UNITY_EDITOR || DEVELOPMENT_BUILD` guard is removed: the assembly constraint replaces it, and the harness is no longer available in development device builds (ADR-012 Decision 7 accepts this).
- A small adapter in the same folder, `HarnessLevelingAdapter`, implements `ILocalPlayerLevelingView` and `IRespecRequestSender` over the harness's `LevelingService` (`GetHeldFreePoints` → the service; `RequestRespec` → `TryApplyRespec`). The harness passes the service itself as the `ILevelingEventBroadcaster`.
- `Assets/Editor/IronGrind.HudEditorTools.asmdef` adds a reference to `IronGrind.DevHarness` (its menu command adds the harness component).

---

## Acceptance Criteria

- [x] **One copy of each display formula**: `LevelingDisplayFormulas` exists in `src/Foundation/LevelingSystem/` with the four members above. `LevelingService` contains no tier table and no derived-stat arithmetic (it calls the class). `src/Client/UI/LevelingSystem/LevelingFormulaPreview.cs` no longer exists.
- [x] **Formula results unchanged**: for the same inputs, `ComputeDerivedStats` returns the values the service computed before. Proven by the existing Leveling tests passing with no change to any expected value, and by a new unit test (see QA Test Cases) that pins the seven outputs for fixed inputs at tiers 1.0, 1.2, 1.5 and 2.0, including the `MaxMP` cap of 9999.
- [x] **The client names no leveling service**: a grep of `src/Client/` for `LevelingService` as a whole word finds no code line (doc comments that explain the history may mention it). `LevelingHudController.Initialize` takes the three interfaces.
- [x] **Harness out of the client assembly**: `LevelingHudManualTestHarness.cs` is under `src/DevHarness/`, its `.cs.meta` GUID is unchanged, and `IronGrind.DevHarness.asmdef` has define constraint `UNITY_EDITOR`, `autoReferenced: false`, and references exactly `IronGrind.Foundation`, `IronGrind.ServerLogic`, `IronGrind.Client`.
- [x] **Nothing depends on the harness assembly**: none of the `Foundation`, `ServerLogic` and `Client` asmdefs references `IronGrind.DevHarness`. Covered by a new boundary test.
- [x] **Boundary lists updated**: `SharedAllowList` gains `IronGrind.LevelingSystem.LevelingDisplayFormulas`, `IronGrind.LevelingSystem.ILocalPlayerLevelingView` and `IronGrind.LevelingSystem.IRespecRequestSender`, each with its consumer named (28 → 31). `NotYetMovedList` is unchanged (127): no type moves in this story.
- [x] **Tier-multiplier tests retargeted, not weakened**: the 11 call sites in `tests/EditMode/LevelingSystem/LevelingSystem_TierAutoAllocFormulaVerification_tests.cs` call `LevelingDisplayFormulas.GetLevelTierMultiplier`; no assertion or expected value changes.
- [x] **Harness still works (manual, ADR-012 Verification Required 8)**: in the Editor, `Tools/HUD/Bootstrap HUD Scaffolding` then `Tools/HUD/Add Leveling Manual Test Harness` adds the component; in Play mode the five debug buttons behave as before (normal level-up, tier transition, consecutive +3, L60, respec screen opens and a respec commits). If the component cannot be added or does not run, stop: ADR-012 Decision 7 names the fallback (an Editor window or play-mode Editor script that drives the views), and that is a change of design to confirm with the user, not to improvise.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning; the total is read from a results file. Expected: 2003 plus the tests this story adds.

---

## Implementation Notes

1. **Order of work**, so the suite can be run between steps:
   1. Add `LevelingDisplayFormulas` (with the new unit test) and the two interfaces; add their three entries to `SharedAllowList`.
   2. Point `LevelingService` at the formulas class; delete `LevelingService.GetLevelTierMultiplier`; retarget the tier test file.
   3. Refactor the three `Client` files; delete `LevelingFormulaPreview.cs` and its `.meta`.
   4. Create `src/DevHarness/` with the asmdef; `git mv` the harness and its `.meta`; write the adapter; add the asmdef reference in `Assets/Editor/IronGrind.HudEditorTools.asmdef`; add the boundary test for the new asmdef.
2. **`SharedAllowList` entries** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`), in the "Class definitions shown by the respec flow" area or a new "Leveling: client read model (ADR-012 Decision 7)" group:
   - `LevelingDisplayFormulas` — consumer: `LevelUpOverlayPresenter`, `RespecScreenPresenter`
   - `ILocalPlayerLevelingView` — consumer: `RespecScreenPresenter`
   - `IRespecRequestSender` — consumer: `RespecScreenPresenter`
   The existing comments on `ClassDefinition`, `IClassRegistry`, `ClassRegistry` and `StatID` name `LevelingFormulaPreview` as a consumer; change that name to `LevelingDisplayFormulas`' callers (`RespecScreenPresenter`) since the file is deleted.
3. **`DerivedStats` is a nested or top-level type?** Make it a nested `public readonly struct` of `LevelingDisplayFormulas`, as `DerivedStatsPreview` is nested today: the boundary test matches nested types through their declaring type, so no extra list entry is needed.
4. **Doc comments**: several doc comments in `src/` and `tests/` name `LevelingService.GetLevelTierMultiplier` or `LevelingFormulaPreview`. Update the ones in files this story edits; a grep at the end must find no code reference to either name.
5. **`ILevelingEventBroadcaster` and `LevelUpEventArgs`** are still on `NotYetMovedList` after this story. They go to the shared allow-list in Story 015, which is where each Leveling type is classified.
6. **Asmdef test** (`AssemblyBoundary_tests.cs`): add one test, `test_dev_harness_asmdef_is_editor_only_and_unreferenced`, using the existing `LoadAsmdef` helper: the constraint is exactly `UNITY_EDITOR`, `autoReferenced` is false, the reference list is the three assemblies, and none of the three references it back. This is the first edit to the test logic since Loot Table Story 014; keep it to the one test and one constant.
7. **Naming**: ADR-012 calls the interface names "working names … fixed in the story that introduces them". This story fixes `ILocalPlayerLevelingView`, `IRespecRequestSender` and `LevelingDisplayFormulas`.

Other rules:
- No performance impact expected — `RecomputeDerivedStats` gains one static call returning a struct; no allocation is added. The presenters' call counts do not change.

---

## Out of Scope

- Moving any Leveling type to `IronGrind.ServerLogic` (Story 015).
- A stats view (`ILocalPlayerStatsView`): the presenters keep `CharacterStats` until the Character Stats move story.
- The networked client implementations of the two interfaces (a mirror filled from wire messages; a sender that writes the respec request message). No wire codec exists for them yet.
- The experience-threshold table: `PlayerResourceClusterPresenter` already receives a threshold list and names no server type. Whether `XpThresholdTable` stays shared is decided in Story 015's classification.
- A device (non-Editor) harness.
- New HUD features or any visual change.

---

## QA Test Cases

**New file**: `tests/EditMode/LevelingSystem/LevelingSystem_DisplayFormulas_tests.cs`

- **test_compute_derived_stats_tier_1_matches_pinned_values** — Given STR 10, DEX 10, VIT 10, INT 10, tier 1.0; Then MaxHP 400, MaxMP 220, AttackPower 30, Defense 20, MagicDefense 4, CritChance 0.065, AttackSpeedMultiplier 1.03 (floats within 1e-6).
- **test_compute_derived_stats_at_each_tier** — the same primaries at tiers 1.2, 1.5 and 2.0; expected values computed by hand in the test's constants from the GDD formulas, with the floor applied.
- **test_compute_derived_stats_max_mp_is_capped_at_9999** — Given INT high enough that `(100 + INT × 12) × tier` exceeds 9999; Then MaxMP is 9999.
- **test_get_respec_floor_and_auto_alloc_increment** — floor is `10 + (level − 1) × increment` for level 1 and level 60; the increment is read from the matching `ClassDefinition` field for each of the four primaries and is 0 for any other `StatID`.

**Existing, retargeted**: `LevelingSystem_TierAutoAllocFormulaVerification_tests.cs` (tier boundaries at 19/20, 39/40, 59/60) — same assertions through `LevelingDisplayFormulas`.

**Existing, unchanged**: the other Leveling test files. *They are the proof that `RecomputeDerivedStats` writes the same values after the refactor.*

**New boundary test**: `test_dev_harness_asmdef_is_editor_only_and_unreferenced` (Implementation Notes 6).

**Manual**: the harness walkthrough in the acceptance criteria.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/LevelingSystem/LevelingSystem_DisplayFormulas_tests.cs` and the new boundary test, passing.
- The full EditMode suite result with the total, from a results file.
- The user's confirmation of the manual harness walkthrough (Verification Required 8), recorded in the completion notes.

**Status**: [x] Complete — 2008 of 2008 passed (Test Runner results file, 2026-10-08); harness walkthrough confirmed by the user

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Damage Calculation Story 006 (Complete — `src/Client/`, the three asmdefs, the boundary test); Leveling Story 013 (Complete — the HUD files being refactored); Leveling Stories 001–012 (Complete — the service and its tests).
- Unlocks: Leveling Story 015 (move `LevelingService` and the other server types to `IronGrind.ServerLogic`), which cannot compile until no `Client` file names `LevelingService`.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 9/9 passing
- One copy of each display formula: `LevelingDisplayFormulas` holds the tier multiplier, the derived stats, the auto-alloc increment and the respec floor. `LevelingService` calls it for all four; `LevelingService.GetLevelTierMultiplier` and the service's private `GetAutoAllocIncrement` are deleted. `LevelingFormulaPreview.cs` is deleted.
- Formula results unchanged: every existing Leveling test passes with no expected value changed; 4 new tests pin the outputs (tiers 1.0, 1.2, 1.5, 2.0, the `MaxMP` cap, floors and increments).
- The client names no leveling service: the only remaining mention of `LevelingService` under `src/Client/` is a `//` comment in `RespecScreenPresenter.cs`.
- Harness out of the client assembly: `src/DevHarness/UI/LevelingSystem/LevelingHudManualTestHarness.cs`, GUID unchanged; `IronGrind.DevHarness.asmdef` as specified, with `HarnessLevelingAdapter` beside it.
- Nothing depends on the harness assembly: `test_dev_harness_asmdef_is_editor_only_and_unreferenced` passes.
- Boundary lists: `SharedAllowList` 28 → 31; `NotYetMovedList` unchanged at 127.
- Tier-multiplier tests retargeted (11 call sites), no assertion changed.
- Suite: 2008 total, 2008 passed, 0 failed, 0 skipped (2003 + 4 formula tests + 1 boundary test), read from `TestResults.xml` (run 15:50). The Editor log shows no compile error and no compiler warning, including after the later harness and bootstrap edits below, which no test covers.
- Harness walkthrough: confirmed by the user in Play mode — the component is on `HUD_Root`, the five debug buttons are visible and work, and the HUD behaves as before.

**ADR-012 Verification Required 8 — confirmed on Unity 6.3**: a `MonoBehaviour` in an assembly whose only restriction is the define constraint `UNITY_EDITOR` (no platform list) can be added to a scene object with `AddComponent` and runs in Play mode. The ADR's fallback (an Editor window or play-mode Editor script) is not needed. The ADR and the control manifest still list this item as pending; update them in the next authoring pass.

**Found during the walkthrough**: the buttons were first reported missing. They were being drawn: the row sits in the bottom strip of the Game view, which is cropped when the Game view's Scale is above its minimum. The same check showed the row was 1504 px wide on a 1366 px screen, so it never wrapped and the last button ran off the right edge. Not caused by this story (the row code was unchanged), but fixed here.

**Deviations** (all in the manual harness and its Editor menu command; none in production code):
- `LevelingHudManualTestHarness`: the button row gained `right = 8` so it wraps (the last button now sits on a second line); `Start` logs one line when the row is added, and one line with the row's on-screen rectangle after layout.
- `Assets/Editor/HudBootstrap.cs`: `AddManualTestHarness` now checks the component is on `HUD_Root` after `AddComponent` and logs an error instead of reporting success if it is not. The story listed only the asmdef under `Assets/Editor/`.

**Test Evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_DisplayFormulas_tests.cs` (4 tests); `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (+1 test); full EditMode suite 2008/2008; the user's walkthrough.
**Code Review**: Line-by-line review of the agent's diff by the orchestrator, which found and removed a leftover private copy of the auto-alloc lookup and an inline respec-floor expression in `LevelingService`. No specialist panel was run.
