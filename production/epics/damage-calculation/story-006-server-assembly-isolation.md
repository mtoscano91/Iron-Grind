# Story 006: Server Assembly Isolation — First Move and Boundary Test

> **Epic**: Damage Calculation
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 5 hours

*Rewritten 2026-10-08 against ADR-012 (Accepted 2026-10-08). The earlier version of this story was Blocked on that ADR and carried no implementation notes. AC-DC-I-01 (the scan of a real client build) moved to Story 007, which waits for the first client build pipeline.*

## Context

**GDD**: `design/gdd/damage-calculation.md` — Core Rule 1 (Enforcement).
**Requirement**: `TR-dmg-001` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decisions 1, 2, 5, 6 (check 1) and Migration Plan step 1.
**ADR Decision Summary**: three assemblies — `IronGrind.Foundation` (shared, both builds), `IronGrind.ServerLogic` (define constraint `UNITY_SERVER || UNITY_EDITOR`), `IronGrind.Client` (define constraint `!UNITY_SERVER || UNITY_EDITOR`). This story is the first move: it creates the two new assemblies, moves Damage Calculation to `ServerLogic` and the seven UI files to `Client`, and writes the boundary test that runs with the EditMode suite.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (knowledge risk — assembly definitions and define constraints are not covered in `docs/engine-reference/unity/`; the constraint syntax was confirmed against the 6.3 manual in `architecture-review-2026-10-08.md`).
**Engine Notes**:
- A `file:` package has no real `Packages/com.irongrind.src/` folder on disk. Locate `.asmdef` files through `CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName`, never a hard-coded path.
- `UNITY_EDITOR` is always defined in the Editor, so both new assemblies compile there whatever build profile is active.
- Move every `.cs` together with its `.cs.meta` (`git mv` both) so GUIDs are preserved. Folder `.meta` files move with their folders.
- A live Editor is available (first compile 2026-09-24). Verify by running the EditMode suite in the Test Runner, or in batch mode when the Editor is closed — not by static reading alone.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary)**:
- Required: the two define-constraint strings exactly; all three assemblies `autoReferenced: false`; any script under `Assets/` that uses project code has its own asmdef with explicit references.
- Required: a move is a folder and assembly change only — namespaces do not change.
- Required: each new assembly has its own `AssemblyInfo.cs` with `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")`.
- Forbidden: `InternalsVisibleTo` between production assemblies; a bare `UNITY_SERVER` constraint; asmdef platform include/exclude lists for this boundary; a hard-coded asmdef path in the boundary test.

---

## Acceptance Criteria

- [ ] **Assemblies exist**: `src/ServerLogic/IronGrind.ServerLogic.asmdef` and `src/Client/IronGrind.Client.asmdef` exist with the names, references (`IronGrind.Foundation` only) and define constraints of ADR-012 Decision 2. `IronGrind.Foundation`, `IronGrind.ServerLogic` and `IronGrind.Client` are all `autoReferenced: false`.
- [ ] **Damage Calculation moved**: the five files of `src/Foundation/DamageCalculation/` are in `src/ServerLogic/DamageCalculation/`, namespace `IronGrind.DamageCalculation` unchanged, `.meta` GUIDs unchanged. No type of that namespace is defined in `IronGrind.Foundation`.
- [ ] **UI moved**: the seven files of `src/Foundation/UI/` are in `src/Client/UI/` (same sub-folders), namespaces unchanged, `.meta` GUIDs unchanged.
- [ ] **Editor script compiles**: `Assets/Editor/HudBootstrap.cs` is in an Editor-only asmdef that references `IronGrind.Client` explicitly, and both of its menu commands still work.
- [ ] **Boundary test exists and passes** (`tests/EditMode/Architecture/`), asserting what ADR-012 Decision 6 check 1 lists: asmdef names, the two constraint strings, `autoReferenced: false`, reference lists; every type in `IronGrind.Foundation` is on the shared allow-list or on the not-yet-moved list; no stale entry on either list; no `Foundation` or `Client` type has a field, property, parameter or base type from `ServerLogic`.
- [ ] **Boundary test detects a violation**: shown once, by hand — a throwaway type in namespace `IronGrind.DamageCalculation` placed under `src/Foundation/` makes the test fail; the type is then deleted. Recorded in the evidence note below.
- [ ] **Suite green**: the full EditMode suite passes after the move (1995 cases before this story, plus the new boundary tests), with zero compile errors and zero new warnings in all four assemblies.

---

## Implementation Notes

Work in this order; the project compiles after each numbered step.

1. **Editor asmdef for `HudBootstrap`** — `Assets/Editor/IronGrind.HudEditorTools.asmdef`: `includePlatforms: ["Editor"]`, references `IronGrind.Foundation` for now (changed in step 4), plus `Unity.InputSystem` and `UnityEngine.UI` (the script uses `UnityEngine.InputSystem.UI` and `UnityEngine.EventSystems`). `Assets/TutorialInfo/` uses no project code and stays in `Assembly-CSharp`.
2. **`autoReferenced: false` on `IronGrind.Foundation`.** Nothing else under `Assets/` uses project code (checked 2026-10-08).
3. **`src/ServerLogic/`** — asmdef as in ADR-012 Decision 2, `AssemblyInfo.cs` with `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")`. `git mv` `src/Foundation/DamageCalculation/` (five `.cs`, five `.cs.meta`, the folder `.meta`) to `src/ServerLogic/DamageCalculation/`. `DamageCalculator` takes `CharacterStats`, `IItemDatabase` and `IEnhancementBonusProvider`, which are still in `Foundation`: `ServerLogic` references `Foundation`, so this compiles. Nothing outside the folder references a Damage Calculation type in code (the Networking mention is a doc comment).
4. **`src/Client/`** — asmdef as in ADR-012 Decision 2, `AssemblyInfo.cs` as above. `git mv` `src/Foundation/UI/` to `src/Client/UI/`. Point the `HudBootstrap` asmdef at `IronGrind.Client` (keep `IronGrind.Foundation` only if the script names a `Foundation` type directly).
   - **One production change is needed, and ADR-012 does not mention it**: `LevelingService.GetLevelTierMultiplier` is `internal static` and is called from `LevelUpOverlayPresenter` (lines 132–133) and `RespecScreenPresenter` (line 230). Once those files are in another assembly the call does not compile. Make the method `public` (ADR-012 Decision 5: "that member becomes public"). Do not add `InternalsVisibleTo("IronGrind.Client")`. The tier multiplier is a display formula under Decision 7 and moves to the `Foundation` display-formula class in the Leveling move story; this story only widens the access.
   - The harness (`LevelingHudManualTestHarness`) stays in `Client` for now and keeps constructing `LevelingService` and `CharacterStats`, which are still in `Foundation`. Its move to `IronGrind.DevHarness` belongs to the Leveling move story.
   - If any other `internal` member turns out to be reached from a moved file, apply the same rule and list it under Deviations at `/story-done`.
5. **Test asmdef** — add `IronGrind.ServerLogic` and `IronGrind.Client` to `references` in `tests/EditMode/IronGrind.Foundation.EditModeTests.asmdef`. No test file moves.
6. **Boundary test** — `tests/EditMode/Architecture/AssemblyBoundary_tests.cs`, with the two lists in a separate file in the same folder (`AssemblyBoundaryLists.cs`) so move stories edit data, not test logic.
   - **Shared allow-list** (each entry with its client consumer in a comment): `EntityID`, `ItemID`, `StatID` (`IronGrind.CharacterStats`); `CharacterID`, `GoldTransactionReason` (`IronGrind.Currency`); every type in `src/Foundation/ItemDatabase/` — definitions, `StatModifierEntry`, enums, `IItemDatabase`, `ItemDatabase`, `ItemDefinitionValidator` and its result types, `ItemDatabaseSeeder`, `MvpItemRecordData`; `ClassDefinition`, `IClassRegistry`, `ClassRegistry` (`IronGrind.LevelingSystem`).
   - **Not-yet-moved list**: every other type defined in `IronGrind.Foundation` on the day the test is written, grouped by system in ADR-012's move order (Loot Table, Enhancement, NPC Interaction, Inventory, Leveling, Networking, Currency, Character Stats). Generate it by reflection once, paste it, and review it by hand; do not compute it at test time.
   - Compiler-generated and nested types are matched through their declaring type.
   - A type that is on a list but no longer in `Foundation` fails the test (stale entry), so each later move story must delete its entries.
   - Reference-list assertions read the asmdef JSON; type assertions use reflection over the three loaded assemblies.
7. **Show the test can fail** (acceptance criterion): add `src/Foundation/_BoundaryProbe.cs` with an empty class in namespace `IronGrind.DamageCalculation`, run the boundary test, record the failure message, delete the file and its `.meta`.

Other rules:
- Tests are deterministic and do no file I/O beyond reading the three asmdef files, which is the point of the test.
- Naming: test methods `test_[scenario]_[expected]`, as in the existing suites.
- Do not touch `link.xml`, the build pipeline, or any system other than Damage Calculation, the seven UI files and the one access change above.

---

## Out of Scope

- **AC-DC-I-01** — the scan of a real client build (ADR-012 Decision 6, check 2): Story 007, blocked on the first client build pipeline. Until then the boundary test is the automated gate.
- The content check (ADR-012 Decision 6, check 3): written with the first `MonoBehaviour` or `ScriptableObject` in `ServerLogic` (client half) and the first server build pipeline (server half).
- Moving any other system. Order and stories: ADR-012 Migration Plan step 2 (next: Loot Table).
- The client read model, the display-formula class and the `IronGrind.DevHarness` assembly (ADR-012 Decision 7): Leveling move story.
- The formula, crit and kill-detection behaviour (Stories 001–005).

---

## QA Test Cases

**File**: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (new).

- **Asmdef shape** — Given the three asmdef files located through `CompilationPipeline`; Then names are `IronGrind.Foundation`, `IronGrind.ServerLogic`, `IronGrind.Client`; `defineConstraints` are exactly `["UNITY_SERVER || UNITY_EDITOR"]` and `["!UNITY_SERVER || UNITY_EDITOR"]` (and empty for `Foundation`); `autoReferenced` is false on all three.
- **Reference lists** — `ServerLogic.references == ["IronGrind.Foundation"]` with no precompiled reference outside the named server-only DLL list (empty today); `Client.references == ["IronGrind.Foundation"]`; `Foundation.references` contains neither of the other two.
- **Every `Foundation` type is listed** — Given all types of the loaded `IronGrind.Foundation` assembly (nested and compiler-generated types resolved to their declaring type); Then each is on the allow-list or the not-yet-moved list; the failure message names the unlisted types.
- **No stale entry** — every name on either list resolves to a type that is in `IronGrind.Foundation`.
- **No type on both lists.**
- **Damage Calculation is server-only** — `DamageCalculator`, `DamageResult`, `DamageContext`, `DamageCalculationConfig`, `IEquippedWeaponQuery` are defined in `IronGrind.ServerLogic`; no type in namespace `IronGrind.DamageCalculation` is defined in `Foundation` or `Client`.
- **No `ServerLogic` type in a `Foundation` or `Client` signature** — fields, properties, method parameters and return types, base types and implemented interfaces.
- **Manual, once** — the probe of Implementation Note 7: the "every `Foundation` type is listed" case fails and names `_BoundaryProbe`; after deletion the suite is green.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` — must exist and pass.
- The full EditMode suite result after the move (Test Runner or batch-mode results XML): pass count and zero failures.
- `production/qa/evidence/damage-calculation-story-006-boundary-probe.md` — the failure message from the probe and the green run after its removal.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Stories 001, 002, 004, 005 (the types to move — Complete). Story 003 is not required: when it is implemented, its code is written in `src/ServerLogic/DamageCalculation/`.
- Unlocks: every later move story (ADR-012 Migration Plan step 2, next Loot Table); Story 007; new server-only systems can be authored in `src/ServerLogic/`.
