# Architecture Review Report — ADR-012 (lean re-review)

> **Date:** 2026-10-08
> **Engine:** Unity 6.3 LTS (6000.3)
> **ADR reviewed:** ADR-012 Server/Client Assembly Boundary (Proposed, amended in `54c7856`)
> **Mode:** `/architecture-review on ADR-012 only` — lean re-review after targeted fixes
> **Prior review:** `architecture-review-2026-10-08.md` (CONCERNS: B1–B3, C1–C8)
> **Verdict:** **ADR-012 has no blocking issue left and can be marked Accepted.** Five small items (N1–N5) are worth folding in first; only N2 changes what Story 006 writes. The project-wide verdict stays **CONCERNS** because of the items carried from 2026-10-07, which this ADR does not touch.

## Scope Note

- **Read in full:** amended ADR-012, `architecture-review-2026-10-08.md`.
- **Read in part:** ADR-004 (Decision 4), ADR-006 (`link.xml`), ADR-010 (Decision 5), the control manifest and the GDDs at every `ServerLogic` / `UNITY_SERVER` / "platform constraint" mention; Damage Calculation Story 006 header.
- **Source checks:** cross-folder type reference graph of `src/Foundation/` (declared type names per folder, matched as whole words in every other folder, comment and `using` lines then excluded by hand for the edges that matter); members the UI files call on `LevelingService` and `CharacterStats`; asset GUID references to the seven UI scripts; both `.asmdef` files; `Assets/Editor/HudBootstrap.cs`.
- **Not run:** the `unity-specialist` consultation (the amendment adds no engine claim beyond Verification Required 7 and 8, which stay open by design); the full traceability matrix (Phases 2–3); `tr-registry.yaml` left empty, as before. `docs/consistency-failures.md` does not exist.

---

## Prior Findings

| # | Status | Evidence |
|---|---|---|
| B1 — migration order | Closed | The nine-step order in Migration Plan step 2 holds against the code at every step: each system moves after every system that uses its server types in code. The references that run the other way are in doc comments only — NPC Interaction → `IPartyService`, Networking → `EnhancementOutcome`, Character Stats → `LevelingService`, Currency and Character Stats → `WireIdCodec`, Leveling → `GoldSyncEventArgs` and `PartyDisbandCoordinator`. `CharacterStats` takes `ILevelingService`, which is declared in its own folder, so Leveling can move before it. |
| B2 — what the client reads | Closed | Decision 7 covers every member the UI calls: `GetBaseStat` / `Subscribe` (stats view); `GetHeldFreePoints`, `OnLevelUp` (leveling view and events); `TryApplyRespec` (request interface); `GetExperienceThreshold`, `GetLevelTierMultiplier`, `RecomputeDerivedStats` (display formulas); `RegisterPlayerEntity`, `AttachCharacterStats`, `RestoreLevelingState`, `SetBaseStat`, `AddExperience` (manual harness only, which leaves `Client`). One trio is unclassified: N1. |
| B3 — boundary test | Closed | Per type, two lists, a new type is on neither. `EntityID`, `ItemID`, `StatID` are in `IronGrind.CharacterStats`; `CharacterID`, `GoldTransactionReason` in `IronGrind.Currency` — as the ADR states. One system has no exit from the not-yet-moved list: N2. |
| C1 — GDD constraint wording | Closed | Step 5 lists all five lines. |
| C2 — Navigation | Closed | In GDD Requirements Addressed and in step 4. |
| C3 — server-only DLLs | Closed | Decision 1 names the list, check 1 allows it, check 2 fails on `Npgsql` / `Dapper` in a client. The import setting is Verification Required 7. |
| C4 — custom messages | Closed | Matches ADR-004 Decision 4 (`CustomMessagingManager`, no RPC / `NetworkVariable` for gameplay state) and ADR-010 Decision 5 (enqueue, do not execute). Dispatcher left to ADR-013. |
| C5 — content check direction | Closed | Check 3 covers both builds; each half has a stated trigger. |
| C6 — Depends On | Closed | ADR-007, ADR-004, ADR-010, all Accepted. |
| C7 — "Foundation" | Closed | Constraints, last bullet. |
| C8 — stale "ADR-012" for inbound dispatch | Open | Outside the ADR: `architecture-review-2026-10-07.md` lines 103 and 216. The traceability index already says ADR-013. |

The inaccuracies noted in the prior review are fixed: 234 files; 38 assets reference `ItemDefinition`; no asset references a UI script (checked by GUID, all seven: 0). "1995 tests" matches the count of test attributes under `tests/`.

---

## New Items — not blocking

### N1 — `IClassRegistry`, `ClassDefinition`, `ClassRegistry` are not classified
- Used by `RespecScreenPresenter` (field, constructor, `TryGetClass`), `LevelingHudController` (constructor parameter) and `LevelingFormulaPreview.GetAutoAllocIncrement(ClassDefinition, StatID)`.
- `ClassDefinition` holds per-class tuning values (auto-allocation per level, free points per level): Decision 3 rule 2 sends it to `ServerLogic`. The respec screen shows those values, so the Decision 7 exception may apply. The ADR does not say.
- The validation criterion for the Leveling move checks only that no `Client` file names `LevelingService`.
- Needed by the Leveling move story (sixth), not by Story 006. One sentence in Decision 7.

### N2 — Item Database has no move story
- The shared allow-list starts with `ItemDefinition`, `EquipmentData`, `ConsumableData`, `ScrollData` "and the enums they use".
- Not on it, and not in the nine-step order: `ItemDatabase`, `IItemDatabase`, `StatModifierEntry`, `ItemDatabaseSeeder`, `ItemDefinitionValidator`, `MvpItemRecordData`.
- They go on the not-yet-moved list in Story 006 and no story removes them, so the list never becomes empty.
- `IItemDatabase` is taken by `DamageCalculator`, Enhancement, Inventory and Loot Table; a client that displays items needs a lookup too. The seeder, validator and record data are used only inside the folder.
- Either classify the six in Decision 6 (allow-list entries with a named consumer, or `ServerLogic`), or add Item Database as a tenth step.

### N3 — the content check does not cover `src/DevHarness/`
- `HudBootstrap`'s second menu command adds `LevelingHudManualTestHarness` to the HUD object in the active scene, and the first command's log tells the user to save the scene.
- Once the harness is in an assembly constrained to `UNITY_EDITOR`, a saved scene that holds it has a missing script in every player build.
- Check 3 scans for scripts under `src/ServerLogic/` (client build) and `src/Client/` (server build) only. Add `src/DevHarness/` to both halves.

### N4 — two acceptance criteria are not mapped
- `currency-system.md:309` AC-CS-G-01: "`CurrencySystem` class is present in `ServerLogic.asmdef` … absent from the client assembly manifest (confirmed via `UnityEditor.Compilation.CompilationPipeline` or assembly definition file inspection)". Decision 6 rules `CompilationPipeline.GetAssemblies` out for the build scan. The criterion is met in substance by check 1 (not in `Foundation` once off the not-yet-moved list) and check 2 (forbidden type name in a client build); the ADR's table names Group G but not this criterion.
- `enemy-ai.md:478` AC-AI-21: "no type from a client-only namespace is used in any Enemy AI file" — by namespace, where the ADR asserts by assembly.
- Add both to the step 5 wording pass.

### N5 — stale cross-reference
- Risks, last bullet: "Engine behaviour not confirmed from the manual (Verification Required 3–5)". There are eight entries now; 6, 7 and 8 are unconfirmed in the same way.

---

## Cross-ADR Conflicts

None. Nothing in the amendment contradicts ADR-004, 005, 006, 007, 008, 009, 010 or 011.

## ADR Dependency Order

Unchanged. ADR-012 depends on ADR-007, ADR-004, ADR-010 (all Accepted); no cycle. Damage Calculation Story 006 and Currency Group G stay Blocked until the status changes.

## Engine Compatibility

No new finding. The claims confirmed against the 6.3 manual in the prior review are unchanged. Verification Required 5–8 are from memory and are attached to the stories that will exercise them (first client build, first `[SerializeReference]` move, first Npgsql import, the harness move).

## GDD Revision Flags

None. Wording corrections only, after acceptance: the five lines in Migration Plan step 5, plus AC-CS-G-01 and AC-AI-21 (N4).

---

## Verdict

**ADR-012: ready for acceptance.** B1–B3 and C1–C7 are closed.

Suggested before the status changes, in one short authoring session: N1–N5 (about five sentences in the ADR). N2 is the one that affects Story 006.

Then: `/create-control-manifest update`, the GDD wording pass, the Story 006 rewrite against the two-list boundary test.

Carried forward from 2026-10-07, unchanged: P1 (inbound dispatch — ADR-013), C1, C2, P2–P5, G1, G2, the `architecture.md` refresh, the empty TR registry, and C8 above.
