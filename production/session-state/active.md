> **Commit status 2026-10-02:** everything in the 2026-10-02 extracts below is committed on `main` — `429cb28` (design: Item Database Amendment #4, Pass 5 review, Enhancement cross-document cleanup) and `939de49` (Stories 005–006, code, assets, TD-047 fix). The "nothing committed" notes in those extracts are superseded. Not pushed: `main` is ahead of `origin`, and the plaintext GitHub PAT in `origin`'s remote URL must be rotated before any push.

> **Commit status 2026-10-02 (later):** Loot Table Stories 002–010 are committed on `main` as `5d7354a` (89 files: loot code, tests, story files, `EPIC.md`, tech-debt register, this file). The "Uncommitted since `125e98f`" notes in the extracts below are superseded. Not pushed: `main` is 1 ahead of `origin`. PAT rotation still unconfirmed.

> **Push status 2026-10-02:** the user pushed `main` to `origin` (`108db3a..125e98f`) — `origin/main` is at `125e98f`. Notes below saying "not pushed" or "`main` is ahead of `origin`" are superseded. PAT rotation is still unconfirmed.

> **Commit and push status 2026-10-03 (later):** the winner-grace design amendment is committed as `74ad7f2`, and Stories 013 and 012 (code, tests, stories, `EPIC.md`, tech debt, this file) as `7910d2a`. The user asked for a push: `origin/main` is at `7910d2a` (`125e98f..7910d2a`). Every "Uncommitted" / "not committed" / "not pushed" note in the extracts below is superseded. Untracked and left alone: `bash.exe.stackdump` in the repo root (a Git Bash crash dump, not committed). PAT rotation is still unconfirmed.

> **Commit and push status 2026-10-07:** Inventory Story 010 (code, tests, story, `EPIC.md`, this file) is committed as `17e82b5`, and the user asked for a push: `origin/main` is at `17e82b5` (`7910d2a..17e82b5`). The "not committed" / "Uncommitted" notes in the 2026-10-07 extracts below are superseded. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.

> **Commit status 2026-10-03:** Story 011 and the Currency amendment are committed on `main` — `c2acf60` (design: `AuctionBid = 9`, Story 011 readiness decisions) and `c789a05` (Story 011 code, tests, `.meta` files, story, EPIC, tech debt). The "Uncommitted" notes in the 2026-10-03 extracts below are superseded. Not pushed: `main` is 3 ahead of `origin`. PAT rotation still unconfirmed. **Uncommitted since `c789a05`:** the winner-grace amendment and story changes listed in the extract directly below.

> **Commit status 2026-10-07 (later):** Enhancement Story 006 (code, tests, `.meta` files, story, `EPIC.md`, index, TD-058, this file) is committed as `687fa33`. The "not committed" / "Uncommitted" notes in the Story 006 extracts below are superseded. Not pushed: `main` is 3 ahead of `origin` (`feaa172`, `b932244`, `687fa33`). Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.

> **Push status 2026-10-07 (later):** the user asked for a push after Story 008: `origin/main` is at `faa134c` (`0faea44..faa134c`). "Not pushed" notes in the 2026-10-07 extracts below are superseded. PAT rotation is still unconfirmed.

> **Commit and push status 2026-10-07 (Damage Calculation Story 001):** the six Damage Calculation stories, `EPIC.md`, Story 001 code, tests, `.meta` files and this file are committed as `b6f9dfa`, and the user asked for a push: `origin/main` is at `b6f9dfa` (`addf77f..b6f9dfa`), level with `main`. The "not committed" / "Nothing committed" notes in the Damage Calculation extracts below are superseded. Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed. **Next:** Story 002 — `/story-readiness production/epics/damage-calculation/story-002-elemental-bonus-and-mitigation.md`, then `/dev-story`. Stories 003 and 006 stay blocked on ADRs not yet written (server RNG injection; server/client assembly boundary).

> **Commit and push status 2026-10-07 (Networking Core 030–031):** Story 030 (queue, gate, tests, `.meta` files, story, ADR-011 clarification) is committed as `c55e423`, and Story 031 (coordinator, failure protocol, sequencer change, tests, `.meta` files, story, `EPIC.md`, tech-debt register, this file) as `addf77f`. The user asked for a push: `origin/main` is at `addf77f` (`3345f44..addf77f`), level with `main`. The "not committed" / "Uncommitted" notes in the Story 030 and Story 031 extracts below are superseded. Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.

> **Commit status 2026-10-08 (ADR-012):** ADR-012 and this file are committed as `2ae5c13`. The "not committed" notes about ADR-012 below are superseded. Not pushed: `main` is 4 ahead of `origin` (`136bc10`, `1837762`, `56e8cce`, `2ae5c13`; `origin/main` is at `b6f9dfa`). Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.

> **ADR-012 reviewed 2026-10-08 — CONCERNS, stays Proposed (not committed):** report `docs/architecture/architecture-review-2026-10-08.md`; `architecture-traceability.md` updated. Uncommitted: those two files and this file. The "then the user marks it Accepted" note below is superseded: amend the ADR first. **Next (fresh authoring session):** amend ADR-012 for B1–B3 and C1–C7, then a lean re-review, then acceptance. Details in the extract "/architecture-review 2026-10-08" below. PAT rotation is still unconfirmed.

> **Enhancement Story 012 created 2026-10-08 (not committed):** `production/epics/enhancement-system/story-012-server-assembly-move.md` — move the 14 Enhancement files to `IronGrind.ServerLogic` (ADR-012 Migration Plan step 2, third in the order), Status Ready, Manifest Version 2026-10-08, estimate 1.5 h. All 14 types server-only; `EnhancementOutcome`, `EnhancementResultCode` and `PrestigeBand` are expected to return to `Foundation` with Story 010 or the enhancement screen. Lists: not-yet-moved 167 → 153; `ServerOnlyNamespaces` gains the namespace; no test-logic change. Checked at creation: the only outside code reference is `DamageCalculator` (already in `ServerLogic`); no `internal` member of another system reached; no serialized-by-name type. New criterion compared with Story 014: the suite total must be recorded. Blocked Stories 009–011 name `src/Foundation/EnhancementSystem/` paths that will be stale after the move. `EPIC.md` now 8/12. Story 014's closure is committed as `2fef5fc` (the "not committed" in the note below is superseded). Uncommitted: the story, the Enhancement `EPIC.md`, this file. **Next (fresh session):** `/story-readiness production/epics/enhancement-system/story-012-server-assembly-move.md`, then `/dev-story` — close the Editor first so the suite can run in batch mode. PAT rotation is still unconfirmed.

> **Loot Table Story 014 closed 2026-10-08 (not committed):** `/story-done` verdict COMPLETE WITH NOTES (no results file or confirmed total of 2003; implemented inline without a programmer agent; new test's failure path not probed). Story file Status Complete with Completion Notes; Loot Table `EPIC.md` Complete (14/14). The review fix is committed as `14d1304`. Uncommitted: the story file, `EPIC.md`, this file. `main` is 18 ahead of `origin` (not pushed). ADR-012 moves done: Damage Calculation, Loot Table (2 of 9). **Next (pick one, fresh session):** create the Enhancement move story (third in ADR-012's order; check first whether Networking's `EnhancementOutcome` mentions and `IEnhancementBonusProvider` usage from `src/ServerLogic/DamageCalculation` allow it); or `/architecture-decision` for server RNG injection (unblocks Damage Calculation Story 003) or ADR-013 inbound request dispatch (unblocks Enhancement Story 009). PAT rotation is still unconfirmed.

> **Story 014 review fix verified by the user 2026-10-08:** the user re-ran the EditMode suite in the Test Runner after the nested-namespace fix — all tests passed (user report; no results file; the total of 2003 was asked for and not stated). The "NOT yet re-run" in the note below is superseded. **Next:** `/story-done production/epics/loot-table-system/story-014-server-assembly-move.md`. PAT rotation is still unconfirmed.

> **Story 014 code review 2026-10-08 — APPROVED WITH SUGGESTIONS, fix applied (not committed, NOT yet re-run):** inline self-review, no specialist spawned. Fix: `test_server_only_namespaces_have_no_type_outside_server_logic` now also matches nested namespaces (new private helper `IsInNamespace` in `AssemblyBoundary_tests.cs`; doc line in `AssemblyBoundaryLists.cs`). The implementation is committed as `a17ddbf`. Editor still open, so **the user must re-run the EditMode Test Runner and confirm the total is 2003.** **Next:** that result, then `/story-done production/epics/loot-table-system/story-014-server-assembly-move.md`. PAT rotation is still unconfirmed.

> **Story 014 verified by the user 2026-10-08:** the user ran the EditMode suite in the Editor's Test Runner after the move — all tests passed (user report; no results file, total count not stated — expected 2003). The `[CharacterStats] AddEquipmentModifier ... at capacity (16)` error the user saw is declared with `LogAssert.Expect` in `CharacterStats_ModifierLifecycle_tests.cs` and is unrelated. The "NOT yet compiled or run" in the note below is superseded. **Next:** `/code-review`, then `/story-done production/epics/loot-table-system/story-014-server-assembly-move.md`. PAT rotation is still unconfirmed.

> **Loot Table Story 014 implemented 2026-10-08 (not committed, NOT yet compiled or run):** done inline in the main session, no programmer agent (one `git mv` and two test-file edits). `git mv` of `src/Foundation/LootTableSystem` (+ `.meta`) to `src/ServerLogic/` — 75 renames, no content change; `AssemblyBoundaryLists.cs` — 37 Loot Table entries deleted (not-yet-moved 204 → 167, shared 26 unchanged), new `ServerOnlyNamespaces` (Damage Calculation, Loot Table); `AssemblyBoundary_tests.cs` — `test_damage_calculation_is_server_only` split into `test_damage_calculation_types_are_in_server_logic` and the data-driven `test_server_only_namespaces_have_no_type_outside_server_logic` (8 tests now), unused constant removed. Graph and serialization greps re-run before the move: clean. The Editor was open (Unity.exe running), so no batch run: **the user must run the EditMode Test Runner — expected 2003/2003.** Readiness verdict was READY; the story file is committed as `b8947a3` (the "not committed" in the note below is superseded). **Next:** suite result from the user, then `/code-review` and `/story-done production/epics/loot-table-system/story-014-server-assembly-move.md`. PAT rotation is still unconfirmed.

> **Loot Table Story 014 created 2026-10-08 (not committed):** `production/epics/loot-table-system/story-014-server-assembly-move.md` — move the 37 Loot Table files to `IronGrind.ServerLogic` (ADR-012 Migration Plan step 2), Status Ready, Manifest Version 2026-10-08, estimate 2 h. Classification: all 37 types server-only (no client consumer today); the ids, enums and event-args structs may return to `Foundation` when the loot wire codecs or UI are written. Also adds a data-driven `ServerOnlyNamespaces` list to the boundary test. Checked at creation: no outside code reference, no `internal` member of another system reached, no serialized-by-name type. `EPIC.md` now 13/14 In Progress. Story 006's closure is committed as `f757d8c` (the "not committed" in the note below is superseded). Uncommitted: the story, the Loot Table `EPIC.md`, this file. **Next (fresh session):** `/story-readiness production/epics/loot-table-system/story-014-server-assembly-move.md`, then `/dev-story`. PAT rotation is still unconfirmed.

> **Damage Calculation Story 006 closed 2026-10-08 (not committed):** `/story-done` verdict COMPLETE WITH NOTES; story file Status Complete with Completion Notes; `EPIC.md` at 5/7. The review fixes are committed as `834c939`. Uncommitted: the story file, `EPIC.md`, this file. `main` is 14 ahead of `origin` (not pushed). **Next (pick one, fresh session):** create the Loot Table move story (ADR-012 Migration Plan step 2, second in the order); or `/architecture-decision` for server RNG injection (unblocks Story 003) or for ADR-013 inbound request dispatch (unblocks Enhancement Story 009). PAT rotation is still unconfirmed.

> **Story 006 verified by the user 2026-10-08:** after the review fixes the user ran the EditMode suite in the Editor's Test Runner — all tests passed (user report; no results file). Both `Tools/HUD` menu commands ran: two `[HudBootstrap]` log lines, `HUD_Root` shows `UIDocument`, `LevelingHudController` and `LevelingHudManualTestHarness` with no missing script. The `[PositionPacketWriter] BatchOverflow` warning the user saw is the expected log flood of `WireProtocol_BatchFraming_Tests` load fixtures, unrelated to this story. The "NOT yet re-run" in the note below is superseded. All acceptance criteria of Story 006 are met. **Next:** `/story-done production/epics/damage-calculation/story-006-server-assembly-isolation.md`. PAT rotation is still unconfirmed.

> **Story 006 code review 2026-10-08 — APPROVED WITH SUGGESTIONS, all five applied (not committed, NOT yet re-run):** inline review, no specialist or `qa-tester` spawned. Fixes: `AssemblyBoundary_tests.cs` — asserts empty `includePlatforms` / `excludePlatforms` on the three asmdefs, `LoadAsmdef` asserts the asset loaded (file-system fallback and `using System.IO` removed), `IsCompilerInfrastructure` now requires `[CompilerGenerated]` in every exempt namespace; `AssemblyBoundaryLists.cs` — id consumers name real `Client` files or are marked "planned"; doc comments corrected in `src/Client/UI/LevelingSystem/LevelingFormulaPreview.cs` and on `LevelingService.GetLevelTierMultiplier`. The Editor was open when the fixes were made (Unity.exe running, `Temp/UnityLockfile` 12:14), so no batch run: **the user must run the EditMode Test Runner** (expected 2002/2002; the narrowed exemption in `IsCompilerInfrastructure` is the change most likely to fail `test_every_foundation_type_is_listed`). The implementation itself is committed as `2032b4b` (the "not committed" in the note below is superseded). **Next:** suite result from the user, the two `Tools/HUD` menu commands, then `/story-done`. PAT rotation is still unconfirmed.

> **Damage Calculation Story 006 implemented 2026-10-08 (not committed; story still Ready, not closed):** see the extract "/dev-story 2026-10-08" directly below the status notes. Suite 2002/2002 in batch mode. One criterion not verified: the two `Tools/HUD` menu commands were not run (needs an interactive Editor). The readiness note is committed as `0cc1b33`. **Next:** `/code-review` on the changed files, then `/story-done production/epics/damage-calculation/story-006-server-assembly-isolation.md`. PAT rotation is still unconfirmed.

> **Story 006 readiness 2026-10-08:** `/story-readiness` verdict **READY** (lean mode, QL-STORY-READY skipped). Standing exception: `TR-dmg-001` is not in the empty TR registry, as for every story in these epics. The one gap found (no performance line) is fixed in the story. The story rewrite is committed as `4fbc855` (the "not committed" in the note below is superseded). **Next (fresh session):** `/dev-story production/epics/damage-calculation/story-006-server-assembly-isolation.md`. PAT rotation is still unconfirmed.

> **Damage Calculation Story 006 rewritten 2026-10-08 (not committed):** `story-006-server-assembly-isolation.md` is now "First Move and Boundary Test", Status **Ready**, Manifest Version 2026-10-08, against ADR-012 Migration Plan step 1. AC-DC-I-01 (scan of a real client build) was split into new `story-007-client-binary-scan.md` (Blocked — no client build pipeline). `EPIC.md` (4/7, ADR table, TR-dmg-001, stories table, next step) and `production/epics/index.md` (note) updated. **Found while writing it:** `LevelingService.GetLevelTierMultiplier` is `internal static` and is called from `LevelUpOverlayPresenter` and `RespecScreenPresenter`, so ADR-012 Decision 7's "step 1 moves the seven UI files unchanged — they compile" is wrong as written; Story 006 makes that one method `public` (Decision 5 rule). ADR-012 Decision 7 "When" and its Date line were corrected for this afterwards, at the user's request (no decision change). The control manifest and the GDD wording pass are committed as `4784d45` and `451a2c7` (the "not committed" in the two notes below is superseded). Uncommitted: the two story files, `EPIC.md`, `index.md`, this file. **Next (fresh session):** `/story-readiness production/epics/damage-calculation/story-006-server-assembly-isolation.md`, then `/dev-story`. PAT rotation is still unconfirmed.

> **GDD wording pass for ADR-012 done 2026-10-08 (not committed):** Migration Plan step 5 applied — wording only, no GDD status change, no review-log entry. `damage-calculation.md` Rule 1 and AC-DC-I-01 (`IronGrind.ServerLogic`, define constraint, ADR-012 named, type name `DamageCalculator`); `currency-system.md` Rule 10 and AC-CS-G-01 (boundary test + client-binary scan in place of `CompilationPipeline`); `enemy-ai.md` lines 10, 27 and AC-AI-21 (by assembly, not namespace); `hit-detection.md` lines 10, 33 and 226; `navigation-pathfinding.md` line 22; `design/registry/entities.yaml` (`HitResult` note). Propagation check: no `ServerLogic.asmdef`, bare `["UNITY_SERVER"]` or "platform constraints" left in `design/`. Not touched: the epic and story files that still describe the ADR as missing (`production/epics/damage-calculation/EPIC.md`, `story-006`, `production/epics/index.md`, `currency-system/EPIC.md`) — that is the Story 006 rewrite. Uncommitted: those five GDDs, `entities.yaml`, the control manifest, this file. **Next (fresh session):** rewrite Damage Calculation Story 006 against ADR-012 (two-list boundary test; Item Database folder on the allow-list), update the epic's ADR table, then `/story-readiness` and `/dev-story`. PAT rotation is still unconfirmed.

> **Control manifest 2026-10-08 (not committed):** `docs/architecture/control-manifest.md` updated for ADR-012 (Manifest Version 2026-10-08, ADRs covered 001–012; lean mode, TD-MANIFEST skipped). Added: Foundation block "Server/client assembly boundary" (17 required, 13 forbidden, 1 verification guardrail), Core/Feature placement rules, Presentation block "What the client reads", 2 cross-cutting constraints; the ADR-006 `link.xml` rule and the ADR-011 shutdown-drain rule each gained one clause. ADR-012's acceptance is committed as `23d71d7` (the "not committed" in the note below is superseded). Every story that embeds Manifest Version 2026-10-07 (8) or 2026-06-28 (97) now reads as stale in `/story-readiness`; story files were not touched. Uncommitted: the manifest and this file. **Next (fresh session each):** the GDD wording pass (ADR-012 Migration Plan step 5); rewrite Damage Calculation Story 006, then `/story-readiness` and `/dev-story`. PAT rotation is still unconfirmed.

> **ADR-012 Accepted 2026-10-08 (not committed):** N1–N5 of the re-review are folded into `docs/architecture/ADR-012-server-client-assembly-boundary.md` and its status is **Accepted (2026-10-08)**, by the user's instruction in the review session. Decisions made in the fold-in: the whole `src/Foundation/ItemDatabase/` folder stays shared (allow-list, no move story); `ClassDefinition`, `IClassRegistry`, `ClassRegistry` are `Foundation`; check 3 also scans `src/DevHarness/`; AC-CS-G-01 and AC-AI-21 are mapped and in the step 5 wording pass. `architecture-traceability.md` updated (12 Accepted). The re-review itself is committed as `673c274`; the "still Proposed" and "not committed" in the note below are superseded. Uncommitted: the ADR, the traceability index, this file. **Next (fresh session each):** `/create-control-manifest update`; the GDD wording pass (Migration Plan step 5); rewrite Damage Calculation Story 006 (its header still says the ADR does not exist), then `/story-readiness` and `/dev-story`. PAT rotation is still unconfirmed.

> **ADR-012 re-reviewed 2026-10-08 — no blocker, still Proposed (not committed):** report `docs/architecture/architecture-review-2026-10-08-rereview.md`; `architecture-traceability.md` updated. B1–B3 and C1–C7 are closed. The "Next: lean re-review" notes below are superseded. Uncommitted: those two files and this file. **Next (fresh authoring session):** fold N1–N5 into ADR-012 (N2 affects Story 006), then the user marks it Accepted; then `/create-control-manifest update`, the GDD wording pass, the Story 006 rewrite. Details in the extract "/architecture-review 2026-10-08 — ADR-012 lean re-review" below. PAT rotation is still unconfirmed.

> **Commit status 2026-10-08 (ADR-012 amendment):** the amended ADR-012 and this file are committed as `54c7856`. The "not committed" / "Uncommitted" notes about the amendment below are superseded. Not pushed: `main` is 6 ahead of `origin` (`origin/main` is at `b6f9dfa`). Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.

> **ADR-012 amended 2026-10-08 — still Proposed (not committed):** B1–B3 and C1–C7 of the review are written into `docs/architecture/ADR-012-server-client-assembly-boundary.md`. The review itself is committed as `78303e7` (the "not committed" in the note above is superseded). Uncommitted: the ADR and this file. `main` is 5 ahead of `origin` (not pushed). **Next (fresh review session):** lean re-review of ADR-012 against `architecture-review-2026-10-08.md` — verify B1–B3 and C1–C7 are closed, nothing else — then the user marks it Accepted. Details in the extract directly below. PAT rotation is still unconfirmed.

## Session Extract — /story-done 2026-10-08 — Loot Table Story 014
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/loot-table-system/story-014-server-assembly-move.md — Move Loot Table to the Server Assembly
- Notes: suite green by user report only (Editor open, no batch run, total not confirmed); implemented inline; no probe of the new test.
- Tech debt logged: None
- Next recommended: None identified (no Ready story). Next ADR-012 move is Enhancement — no story file yet.

## Session Extract — /story-done 2026-10-08
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/damage-calculation/story-006-server-assembly-isolation.md — Server Assembly Isolation: First Move and Boundary Test
- Notes: last suite run after the review fixes is a user report with no results file; one moved file had a doc comment edited; `TR-dmg-001` is a placeholder.
- Tech debt logged: None
- Next recommended: None identified in this epic (003 and 007 Blocked). Next ADR-012 move is Loot Table — no story file yet.

## Session Extract — /dev-story 2026-10-08 — Damage Calculation Story 006
- Story: production/epics/damage-calculation/story-006-server-assembly-isolation.md — Server Assembly Isolation: First Move and Boundary Test
- Agent: `unity-specialist` (single implementer; no second agent). Its test file did not compile (CS0104, `Assembly` ambiguous between `UnityEditor.Compilation` and `System.Reflection`); fixed with a `using` alias. One stale doc sentence in `LevelingService.GetLevelTierMultiplier` remarks corrected.
- Files changed: created `src/ServerLogic/IronGrind.ServerLogic.asmdef`, `src/ServerLogic/AssemblyInfo.cs`, `src/Client/IronGrind.Client.asmdef`, `src/Client/AssemblyInfo.cs`, `Assets/Editor/IronGrind.HudEditorTools.asmdef`, `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`, `tests/EditMode/Architecture/AssemblyBoundary_tests.cs`, plus the 10 `.meta` files Unity generated (one per new file, and `src/ServerLogic.meta`, `src/Client.meta`, `tests/EditMode/Architecture.meta`); modified `src/Foundation/IronGrind.Foundation.asmdef` (`autoReferenced: false`), `tests/EditMode/IronGrind.Foundation.EditModeTests.asmdef` (two references), `src/Foundation/LevelingSystem/LevelingService.cs` (`GetLevelTierMultiplier` internal → public); moved with `git mv` (27 renames, content unchanged) `src/Foundation/DamageCalculation/` → `src/ServerLogic/DamageCalculation/` and `src/Foundation/UI/` → `src/Client/UI/`.
- Test written: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (7 tests). Lists: 26 shared, 204 not yet moved; the grep-generated lists needed no correction.
- Results (Unity 6000.3.10f1 batch mode): 2002/2002 after the move; probe run 5 passed / 2 failed as intended; 2002/2002 after removing the probe. Evidence: `production/qa/evidence/damage-calculation-story-006-boundary-probe.md`.
- Blockers: None. Not verified: `HudBootstrap` menu commands (compile only).
- Next: /code-review on the files above, then /story-done production/epics/damage-calculation/story-006-server-assembly-isolation.md

## Session Extract — /architecture-review 2026-10-08 — ADR-012 lean re-review (not committed)
- Verdict: ADR-012 has no blocking issue and is ready for acceptance; project-wide **CONCERNS** unchanged (2026-10-07 items). `unity-specialist` not spawned; full traceability matrix not rebuilt.
- Requirements: domain level, unchanged — 12 ADR-backed domains, 5 covered, 7 partial or in conflict, 2 schema gaps.
- Closed: B1 (nine-step order checked against the code reference graph; the backward references are doc comments only), B2 (Decision 7 covers every `LevelingService` / `CharacterStats` member the UI calls), B3 (per type, two lists), C1–C7. Open: C8 (`architecture-review-2026-10-07.md` lines 103 and 216 still call inbound dispatch "ADR-012").
- N1: `IClassRegistry` / `ClassDefinition` / `ClassRegistry` are used by `RespecScreenPresenter`, `LevelingHudController` and `LevelingFormulaPreview` and are not classified in Decision 7 (Leveling move story).
- N2: Item Database has no move story — `ItemDatabase`, `IItemDatabase`, `StatModifierEntry`, `ItemDatabaseSeeder`, `ItemDefinitionValidator`, `MvpItemRecordData` are neither on the shared allow-list nor in the move order, so the not-yet-moved list never empties. Affects Story 006.
- N3: Decision 6 check 3 does not scan for `src/DevHarness/` scripts; `HudBootstrap` adds the harness to a scene the user is told to save.
- N4: AC-CS-G-01 (`currency-system.md:309`, names `CompilationPipeline`) and AC-AI-21 (`enemy-ai.md:478`, by namespace) are not mapped; add to the step 5 wording pass.
- N5: Risks, last bullet, cites "Verification Required 3–5"; there are eight.
- New TR-IDs registered: None. GDD revision flags: None (wording corrections only, after acceptance).
- Top ADR gaps: ADR-013 inbound request dispatch + tick order; ADR-009 Amendment 1; ADR-007 amendment (supervision).
- Report: docs/architecture/architecture-review-2026-10-08-rereview.md
- Damage Calculation Story 006 and Currency Group G stay Blocked until ADR-012 is Accepted.

## Session Extract — ADR-012 amendment 2026-10-08 (authoring session, not committed)
- User decisions: **B2** — client read model in `Foundation` (not "classify as shared", not a separate ADR); **B1** — reorder the moves by the reference graph (not temporary interfaces).
- B1: Migration step 2 is now Damage Calculation → Loot Table → Enhancement → NPC Interaction → Inventory → Leveling → Networking (server part) → Currency (Group G) → Character Stats; rule "users first, most depended-on last"; each story re-checks the graph by grep. Currency Group G is unblocked by acceptance but is eighth.
- B2: new **Decision 7 — What the client reads**. `LevelingService` and `CharacterStats` are `ServerLogic`. `Foundation` gets read-only views (working names `ILocalPlayerStatsView`, `ILocalPlayerLevelingView`), a request interface (`IRespecRequestSender`), and one static display-formula class (experience threshold, tier multiplier, derived-stat preview; single copy, `ServerLogic` calls it). `LevelUpEventArgs` / `ILevelingEventBroadcaster` stay `Foundation`. `LevelingHudManualTestHarness` → new `IronGrind.DevHarness` (`src/DevHarness/`, constraint `UNITY_EDITOR`); no longer in development device builds. Work lands in the Leveling move story (stats view finished in the Character Stats one); Story 006 still moves the seven UI files unchanged.
- B3: boundary test asserts per type — every type in `IronGrind.Foundation` is on a shared allow-list (ids, wire enums, Item Database definitions; each with its client consumer) or on a not-yet-moved list that only shrinks. A type on neither fails.
- C1–C7: all five GDD wording corrections listed in Migration step 5 (plus why bare `UNITY_SERVER` is wrong, in Decision 2); Navigation/Pathfinding added; `ServerLogic` may reference named server-only DLLs (Npgsql, Dapper), which the client-binary scan also rejects; Decision 4 covers `CustomMessagingManager` handlers (registered by the composition roots, enqueue only, dispatcher is ADR-013); content check is two-directional; Depends On adds ADR-004 and ADR-010; "Foundation" assembly vs layer sentence in Constraints.
- Also corrected: 234 files; the "no scene or prefab references" constraint.
- New under Verification Required: (7) how a precompiled DLL is kept out of a client player, and `link.xml` for an absent assembly; (8) a `MonoBehaviour` in a `UNITY_EDITOR`-constrained assembly on a scene object. Neither checked against the 6.3 manual.
- Not done: C8 (the 2026-10-07 review and `architecture-traceability.md` still call inbound dispatch "ADR-012"; it is ADR-013). Registry entries, control manifest, GDD wording, Story 006 rewrite — all wait for acceptance, as before; Story 006's rewrite must now use the two-list boundary test.
- Damage Calculation Story 006 and Currency Group G stay Blocked.

## Session Extract — /architecture-review 2026-10-08 — ADR-012 only (review committed as `78303e7`)
- Verdict: **CONCERNS** — core decision sound; define-constraint syntax, `UNITY_SERVER` and `-standaloneBuildSubtarget Server` confirmed against the Unity 6.3 manual. `unity-specialist` not spawned; full traceability matrix not rebuilt.
- Requirements: domain level, 12 ADR-backed domains — 5 covered, 7 partial or in conflict (new row: server/client code isolation, ADR-012 Proposed), 2 schema gaps unchanged.
- B1: Migration step 2 order is inverted. `ICurrencyService` / `CurrencySystem` / `GoldMutationResult` are used by Inventory, Loot Table, Leveling and Networking, so Currency cannot move first. Order that fits the graph: Damage Calculation → Loot Table → Enhancement → NPC Interaction → Inventory → Leveling → Networking → Currency → Character Stats.
- B2: the seven UI files (to `IronGrind.Client`) reference `LevelingService` ~40 times and the `CharacterStats` class; `Client` cannot reference `ServerLogic`. The ADR must define what the client reads.
- B3: the boundary test asserts by namespace, but `EntityID` / `ItemID` / `StatID` are in `IronGrind.CharacterStats` and `CharacterID` / `GoldTransactionReason` in `IronGrind.Currency`, and stay in `Foundation`. Assert per type with a `Foundation` allow-list. Story 006 itself is unaffected.
- Concerns: `enemy-ai.md:27`, `hit-detection.md:33`, `navigation-pathfinding.md:22` say `defineConstraints: ["UNITY_SERVER"]` (ADR's `|| UNITY_EDITOR` is right; add them to Migration step 5); navigation-pathfinding missing from the ADR; Npgsql/Dapper references and the project-wide `link.xml` (ADR-006); Decision 4 vs ADR-004 Decision 4 (`CustomMessagingManager`) and ADR-010 Decision 5 (enqueue); content check covers the client build only; Depends On omits ADR-004 and ADR-010; "Foundation" assembly vs Foundation layer; the inbound-dispatch decision is now ADR-013.
- New TR-IDs registered: None. GDD revision flags: None (wording corrections only, after acceptance).
- Top ADR gaps: ADR-012 amendments; ADR-013 inbound request dispatch + tick order; ADR-009 Amendment 1.
- Damage Calculation Story 006 and Currency Group G stay Blocked.

> **ADR-012 written 2026-10-08 (Proposed, not committed):** `docs/architecture/ADR-012-server-client-assembly-boundary.md`. Uncommitted: that file and this file. `main` is 3 ahead of `origin` (not pushed). PAT rotation still unconfirmed. **Next (fresh session):** `/architecture-review` on ADR-012, then the user marks it Accepted. Details in the extract "/architecture-decision 2026-10-08" below.

> **Commit status 2026-10-08 (Damage Calculation Story 005):** Story 005 (integration test, its folder and `.meta` files, story, `EPIC.md`, this file) is committed as `56e8cce`. The "not committed" / "Uncommitted" notes in the Story 005 extracts below are superseded, and so are the "Not pushed … ahead" counts in the two notes that follow. Not pushed: `main` is 3 ahead of `origin` (`136bc10`, `1837762`, `56e8cce`; `origin/main` is at `b6f9dfa`). Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed. **Next:** Damage Calculation has no Ready story (4/6). Write the server/client assembly boundary ADR (unblocks Story 006 and the Currency epic's Group G) and the server RNG injection ADR (unblocks Story 003) with `/architecture-decision`, in a fresh session.

> **Commit status 2026-10-08 (Damage Calculation Story 004):** Story 004 (code, tests, `.meta` file, story, `EPIC.md`, TD-062, this file) is committed as `1837762`. The "not committed" / "Uncommitted" notes in the Story 004 extracts below are superseded, and so is the "Not pushed … 1 ahead" part of the Story 002 note that follows. Not pushed: `main` is 2 ahead of `origin` (`136bc10`, `1837762`; `origin/main` is at `b6f9dfa`). Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed. **Next:** Story 005 — `/story-readiness production/epics/damage-calculation/story-005-kill-sequence-integration.md`, then `/dev-story`, in a fresh session; read TD-062 first.

> **Commit status 2026-10-08 (Damage Calculation Story 002):** Story 002 (code, tests, `.meta` files, story, `EPIC.md`, this file) is committed as `136bc10`. The "not committed" / "Uncommitted" notes in the Story 002 extracts below are superseded. Not pushed: `main` is 1 ahead of `origin` (`b6f9dfa`). Uncommitted: this note only. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed. **Next:** Story 004 — `/story-readiness production/epics/damage-calculation/story-004-kill-detection.md`, then `/dev-story`.

## Session Extract — /architecture-decision 2026-10-08 — ADR-012 Server/Client Assembly Boundary (Proposed, not committed)
- File: `docs/architecture/ADR-012-server-client-assembly-boundary.md`. Status **Proposed** (user choice). Lean mode: TD-ADR skipped. `unity-specialist` validation: no blocking issue, but it had no documentation access — its points are from memory and sit under Verification Required in the ADR.
- User decisions: alternative A (separate assemblies, exclusion by define constraint); three assemblies keeping `IronGrind.Foundation` as the shared one, plus new `IronGrind.ServerLogic` (`UNITY_SERVER || UNITY_EDITOR`) and `IronGrind.Client` (`!UNITY_SERVER || UNITY_EDITOR`); migration one story per system, Damage Calculation Story 006 first.
- Checked against the Unity 6.3 manual in this session: asmdef Define Constraints must all be met and support `!` and `||`; `UNITY_SERVER` is defined for Dedicated Server builds (Build Profiles or `-standaloneBuildSubtarget Server`). Not confirmed: a Dedicated Server entry in the asmdef platform list; `UNITY_SERVER` in the Editor under a server profile; `IPostBuildPlayerScriptDLLs` as the scan mechanism; `[MovedFrom]` for `[SerializeReference]` data.
- Decisions in the ADR: (1) three assemblies and who may reference whom; (2) the two define constraints, `autoReferenced: false` on all three; (3) a four-question classification rule, server-only as the default for game logic; (4) `NetworkBehaviour` shells in `Foundation` calling interfaces implemented in `ServerLogic`, nothing but forwarding in an RPC body, no `ServerLogic` script in client content; (5) one test assembly referencing all three, no `InternalsVisibleTo` between production assemblies; (6) three checks — boundary test in the EditMode suite, client-binary scan (closes AC-DC-I-01), client content check.
- **Not done, by user choice:**
  - Registry: five stances wait for acceptance — one `api_decisions` entry (the three assemblies and constraints) and four `forbidden_patterns` (server logic in `Foundation`/`Client`; `InternalsVisibleTo` between production assemblies; rules or constants in an RPC body; a `ServerLogic` script referenced by client content).
  - GDD wording: `damage-calculation.md` Core Rule 1 ("platform constraints" → define constraint; `ServerLogic.asmdef` → `IronGrind.ServerLogic`) and AC-DC-I-01 (type name `DamageCalculation` → `DamageCalculator`); `hit-detection.md` and `enemy-ai.md` (`ServerLogic.asmdef` → `IronGrind.ServerLogic`). One small authoring pass, with the triad and propagation check.
- After acceptance: add the registry entries; `/create-control-manifest update`; rewrite Damage Calculation Story 006 from the ADR's Migration Plan step 1 (creates both new assemblies, moves `src/Foundation/UI/` and `src/Foundation/DamageCalculation/`, gives `Assets/Editor/HudBootstrap.cs` an asmdef, writes the boundary test) and set it Ready; unblock Currency Group G. Story 006 and Group G stay Blocked while the ADR is Proposed.
- Follow-up decision recorded in the ADR's Risks: server-only *content* (drop tables and tuning stored as assets) is not covered by an assembly boundary; decide before Loot Table or Enemy AI data is authored as assets.
- Still to write: server RNG injection ADR (OQ-DC-2, unblocks Story 003) — it can name `IronGrind.ServerLogic` as its home; server tick ordering for damage resolution (OQ-DC-4, before Auto-Attack Combat).
- Next: fresh session → `/architecture-review` (never in the session that authored the ADR), then accept ADR-012.

## Session Extract — /story-done 2026-10-08 — Damage Calculation Story 005 (not committed)
- `/code-review` 2026-10-08: APPROVED WITH SUGGESTIONS (`unity-specialist` + `qa-tester`, nothing required). User said "fix all"; all 5 applied: the died handler also records the attacker's `Experience` (asserted 290 at death in the order test), new exact-lethal test (HP 500, base 500), a remark on bare `CharacterStats` / `DamageCalculation` names binding to namespaces in the Integration folders, re-entrance comment corrected (throws in the Editor, logs and skips in release), `FakeXpSource` moved to the end of the fixture. Test run after the fixes: 1995 / 1995.
- Verdict: COMPLETE WITH NOTES (4/4 criteria, all covered by automated tests). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
- Story: `production/epics/damage-calculation/story-005-kill-sequence-integration.md` — Kill Sequence Against Real Character Stats. Status → Complete, Completion Notes added. `EPIC.md` → In Progress (4/6 — 0 Ready, 2 Blocked).
- Tech debt logged: None.
- Uncommitted since `1837762`: the new test file and its folder (`tests/EditMode/Integration/DamageCalculation/` + two `.meta` files), the story, `EPIC.md`, this file. `main` is 2 ahead of `origin` (not pushed). `bash.exe.stackdump` stays untracked. PAT rotation still unconfirmed.
- Next recommended: no Ready story in Damage Calculation. Stories 003 and 006 each need an ADR: server RNG injection (OQ-DC-2) and the server/client assembly boundary — `/architecture-decision` for each, in a fresh session. Other epics without stories: authentication, status-effects, equipment-system (gate OQ-EQS-9). Still stale: `production/epics/index.md` Damage Calculation row.

## Session Extract — /dev-story 2026-10-08 — Damage Calculation Story 005 implemented (not committed)
- Story: `production/epics/damage-calculation/story-005-kill-sequence-integration.md` — Kill Sequence Against Real Character Stats (Integration). Status still Ready (set by `/story-done`).
- `/story-readiness` (lean): NEEDS WORK → fixed in the story with user approval: the Leveling-service note pointed at the default fixture stub (no entity is a player, TD-062); it now says `CreateWithLeveling(new AllPlayersLevelingService())` and attacker Level 1.
- Files changed: the story file only. No production code (as the story expects).
- Test written: `tests/EditMode/Integration/DamageCalculation/DamageCalculation_KillSequence_integration_tests.cs` — 7 `[Test]`; new folder, so the folder `.meta` and the file `.meta` must be committed.
- Test run (Unity batch mode, EditMode): 1994 / 1994 passed.
- Implemented by `gameplay-programmer`; file reviewed in the main session.
- Choices beyond the story: the `addExperience` marker is recorded by a named `StatChangedHandler` (`AddExperience` → `SetBaseStat` raises stat-changed outside a transaction); the recorder is cleared after arranging, because setup writes raise the same event; the `applyDamage` marker is appended just before `ApplyDamage`; the caller is split into `CalculateHit` / `RunCallerFollowUp` / `RunCaller`; one extra test (two targets killed in turn).
- Blockers: None.
- Next: `/code-review` on the test file, then `/story-done`. After that the epic has no Ready story: 003 needs the server RNG ADR, 006 the server/client assembly ADR.

## Session Extract — /story-done 2026-10-08 — Damage Calculation Story 004 (not committed)
- Verdict: COMPLETE WITH NOTES (10/10 criteria, all covered by automated tests; EditMode 1987 / 1987). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped; `/code-review` had already run.
- Story: `production/epics/damage-calculation/story-004-kill-detection.md` — Kill Detection and Dead-Entity Guard. Status → Complete, Completion Notes added. `EPIC.md` → In Progress (3/6).
- Tech debt logged: 1 item — TD-062 (`CharacterStatsFixture.Create()` makes `AddExperience` a no-op, so "Experience unchanged" assertions built on it cannot fail; audit the other fixtures before Story 005 or Auto-Attack Combat XP tests).
- Uncommitted since `136bc10`: Story 004 work — `DamageCalculator.cs`, new `DamageCalculation_KillDetection_tests.cs` (+ `.meta`), the two older Damage Calculation fixtures, `DamageCalculationTestFakes.cs`, the story, `EPIC.md`, `docs/tech-debt-register.md`, this file. `main` is 1 ahead of `origin` (`136bc10`, not pushed). `bash.exe.stackdump` stays untracked. PAT rotation still unconfirmed.
- Next recommended: Story 005 — Kill Sequence Against Real Character Stats (Integration): `/story-readiness production/epics/damage-calculation/story-005-kill-sequence-integration.md`, then `/dev-story`, in a fresh session. Look at TD-062 first — Story 005 is exactly the kind of XP test it warns about. Stories 003 and 006 stay blocked on ADRs not yet written.

## Session Extract — /dev-story 2026-10-08 — Damage Calculation Story 004 implemented (not committed)
- Story: `production/epics/damage-calculation/story-004-kill-detection.md` — Kill Detection and Dead-Entity Guard. Status still Ready (set by `/story-done`).
- `/story-readiness` (lean): NEEDS WORK → fixed in the story with user approval. User decision 2026-10-08: an unknown target (no stat record) keeps `Defense = 0`, its damage is computed, and the dead-entity guard applies (no HP record reads as 0 → `IsKill = false`, dev error). No new `CharacterStats` API. The story's Implementation Notes also gained the existing-fixture note and the two test fixes.
- Files changed: modified `src/Foundation/DamageCalculation/DamageCalculator.cs` (Step 10 in private `ResolveIsKill`, reads `GetCurrentHP`; guard is `currentHp <= 0f`); modified `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` and `DamageCalculation_Elemental_tests.cs` (target gets `MaxHP` and HP 99999 in `SetUp`); the story file.
- Test written: `tests/EditMode/DamageCalculation/DamageCalculation_KillDetection_tests.cs` (+ `.meta`) — 14 `[Test]`.
- Existing tests changed beyond setup (Story 001 fixture): `UnknownTarget_TreatedAsDefenseZero` expects the guard's error and asserts `IsKill == false`; the self-damage side-effect test sets and reads `Experience` on the attacker; `NormalHit_ElementalAndFlagsAreInactive` now uses target HP 1000 instead of 100 (374 damage on 100 HP is a kill now that Step 10 is live; assertions unchanged).
- Test run (Unity batch mode, EditMode): 1984 / 1984 passed (first run 1983 / 1984 — the `NormalHit` case above; fixed in the main session).
- Implemented by `gameplay-programmer`; diff reviewed in the main session; `unity-specialist` not spawned (plain C#, LOW risk).
- For `/code-review` and `/story-done`: the guard uses `<= 0f` where the story says `== 0f`; GDD writes `GetEffectiveStat(TargetID, CurrentHP)` but the code reads `GetCurrentHP` (GDD wording gap, as the story says); two boundary tests in the Story 001 fixture (99999 and 2^24 damage) now return `IsKill = true` and do not assert it. Deviation carried: server/client assembly ADR gate not met (Story 006).
- Blockers: None.
- **/code-review 2026-10-08: CHANGES REQUIRED** (main session + `unity-specialist` + `qa-tester`; 3 required, all in tests; 5 suggestions). User said "fix all"; all 8 applied:
  - Root finding: `CharacterStatsFixture.Create()` uses a leveling stub where no entity is a player, so `AddExperience` is a no-op and every "Experience unchanged" assertion was vacuous. New `AllPlayersLevelingService` in `DamageCalculationTestFakes.cs`; the Kill Detection and Story 001 fixtures now build stats with it, and the entity under test is set to Level 1 (XP gain is also a no-op at level 60).
  - Kill Detection fixture now 17 `[Test]`: two fixture controls (`AddExperience` really adds; `ApplyDamage` kill fires the died handler once), a NaN-HP test, K-01 gained HP and XP assertions, I-06 gained the HP assertion, `DEFENSE_900` constant.
  - Story 001 fixture: the two ceiling tests (99999 and 2^24 damage) assert `IsKill == true`. The self-damage test is back to setting and reading `Experience` on `Target` — in `Calculate(100, Target, Target, ...)` the target is the acting entity; the earlier story note asking for the attacker was wrong (corrected in the story).
  - `DamageCalculator.cs`: guard is `!(currentHp > 0f)` so a NaN HP also logs; most of the `Calculate` summary moved to `<remarks>`.
  - For Story 003: put Steps 1 and 8 in a private `ResolveCrit(..., out bool isCrit)`; the crit roll then happens before the dead-entity guard.
  - Test run after the fixes (Unity batch mode, EditMode): 1987 / 1987 passed, 0 inconclusive.
- Next: `/story-done production/epics/damage-calculation/story-004-kill-detection.md`. Then Story 005 (kill-sequence integration test).

## Session Extract — /story-done 2026-10-08 — Damage Calculation Story 002 (not committed)
- Verdict: COMPLETE WITH NOTES (11/11 criteria, all covered by automated tests; EditMode 1970 / 1970). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped; `/code-review` had already run.
- Story: `production/epics/damage-calculation/story-002-elemental-bonus-and-mitigation.md` — Elemental Bonus and Mitigation. Status → Complete, Completion Notes added. `EPIC.md` → In Progress (2/6).
- Tech debt logged: None (user did not ask for the per-hit dev `LogError` to be logged; it is in the Completion Notes).
- Stale, not touched: `production/epics/index.md` still shows the Damage Calculation row as "Not yet created".
- Uncommitted: Story 002 work — `DamageCalculator.cs`, new `IEquippedWeaponQuery.cs`, three test files (two new), three `.meta` files, the story, `EPIC.md`, this file. `bash.exe.stackdump` stays untracked. PAT rotation still unconfirmed.
- Next recommended: Story 004 — `/story-readiness production/epics/damage-calculation/story-004-kill-detection.md`, then `/dev-story`, in a fresh session. Stories 003 and 006 stay blocked on ADRs not yet written.

## Session Extract — /dev-story 2026-10-08 — Damage Calculation Story 002 implemented (not committed)
- Story: `production/epics/damage-calculation/story-002-elemental-bonus-and-mitigation.md` — Elemental Bonus and Mitigation. Status still Ready (set by `/story-done`). `/story-readiness` (lean; QL-STORY-READY skipped): READY, one accepted gap — `TR-dmg-004` is a placeholder (registry empty).
- Files changed: new `src/Foundation/DamageCalculation/IEquippedWeaponQuery.cs`; modified `src/Foundation/DamageCalculation/DamageCalculator.cs` (5-argument constructor, Steps 4–7, elemental fields of Step 11, private `ResolveElementalBonus`); modified `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` (constructor calls only); new `tests/EditMode/DamageCalculation/DamageCalculationTestFakes.cs` (`FakeEquippedWeaponQuery`, `CountingItemDatabase`, `RecordingBonusProvider`). Editor-generated `.meta` files for the three new files must be committed.
- Test written: `tests/EditMode/DamageCalculation/DamageCalculation_Elemental_tests.cs` — 16 `[Test]`.
- Test run (Unity batch mode, EditMode): 1964 / 1964 passed.
- Implemented by `gameplay-programmer`; diff reviewed in the main session; `unity-specialist` not spawned (plain C#, LOW risk).
- Choices beyond the story (for `/code-review` and `/story-done`): lookup uses `TryGetItem`; "not equipment" is detected by `EquipmentData == null`; a bonus of 0 or less skips Steps 5–6 (a negative provider value counts as 0); `ArgumentOutOfRangeException` from the bonus provider propagates uncaught (documented, tested); one extra test (provider throws) and one for "item is not equipment". Deviation carried from Story 001: server/client assembly ADR gate not met (Story 006).
- Blockers: None.
- **/code-review 2026-10-08: CHANGES REQUIRED** (main session + `unity-specialist` + `qa-tester`; 3 required, 9 suggestions). User said "fix all"; items 1–11 applied, item 12 (per-hit dev `LogError` for a bad weapon id) left as is:
  - `DamageCalculator.cs`: guards extracted to private `IsInvalidRequest`, Steps 5–6 to private `ResolveElementalMitigated` (`Calculate` now 42 lines with comments and blanks); `<returns>` tag added. No behaviour change.
  - Fakes: `FakeEquippedWeaponQuery` records the last entity of each query; `CountingItemDatabase.AddFoundButNull`; unused `TotalLookupCount` removed.
  - Tests: Elemental fixture now 22 `[Test]` (6 new: custom `KMagic`, custom `MinElementalFraction`, target without stat record, real provider at +5, provider returns negative, database returns true with a null item). Contract test asserts both queries were called once with the attacker; missing-item and non-equipment tests assert the level query was not called; `BASE_DAMAGE_100` constant.
  - Accepted gap: "MagicDefense not consulted" for a non-elemental weapon is not observable through the real `CharacterStats`.
  - Test run after the fixes (Unity batch mode, EditMode): 1970 / 1970 passed.
- Next: `/story-done production/epics/damage-calculation/story-002-elemental-bonus-and-mitigation.md`. Then Story 004 (kill detection); Story 003 stays blocked on the RNG ADR.

## Session Extract — /dev-story 2026-10-07 — Damage Calculation Story 001 implemented (not committed)
- Story: `production/epics/damage-calculation/story-001-result-types-physical-mitigation.md` — Result Types, Tuning Config, Physical Mitigation and Final Floor. Status still Ready (set by `/story-done`). `/story-readiness`: one gap only — TR-dmg IDs are placeholders (registry empty), accepted as on earlier epics.
- Files changed: new `src/Foundation/DamageCalculation/` (`DamageContext.cs`, `DamageResult.cs`, `DamageCalculationConfig.cs`, `DamageCalculator.cs`, Editor-generated `.meta` files and folder `.meta`). No existing file modified.
- Test written: `tests/EditMode/DamageCalculation/DamageCalculation_PhysicalMitigation_tests.cs` (+ `.meta`, folder `.meta`) — 15 `[Test]` + 12 `[TestCase]` = 27 cases. It reuses `CharacterStatsFixture.Create()` from `tests/EditMode/CharacterStats/TestHelpers/`.
- Test run (Unity batch mode, EditMode): 1927 / 1927 passed. The float-sensitive cases (100 × 0.05 → 5, 20 × 0.05 → 1) pass.
- Deviation to record at `/story-done`: server/client assembly ADR gate not met (user decision; tracked in Story 006). Three tests beyond the story's list (negative base damage, inactive fields, fractions of 1.0 accepted).
- Blockers: None.
- Next: `/code-review` on the four source files and the test file, then `/story-done`. Then Story 002.

## Session Extract — /create-stories damage-calculation 2026-10-07 (not committed)
- 6 stories written to `production/epics/damage-calculation/` (001–006); `EPIC.md` updated with the story table. Lean mode: QL-STORY-READY skipped; QA test cases written at story creation.
- Ready: 001 (types, config, physical mitigation, final floor), 002 (elemental path), 004 (kill detection), 005 (kill-sequence integration test). Blocked: 003 (crit — OQ-DC-2 RNG ADR), 006 (assembly isolation — server/client assembly ADR).
- User decision: the GDD requires the server/client assembly ADR before implementation; the four Ready stories are built in `IronGrind.Foundation` anyway (Currency precedent) and record the deviation. The epic cannot close until Story 006 is done.
- Decided at story creation where the GDD is silent (review at `/story-readiness`): class `DamageCalculator.Calculate(...)` in namespace `IronGrind.DamageCalculation`; "dev-build assert" = `Debug.LogError` under `UNITY_EDITOR || DEVELOPMENT_BUILD` (the ACs expect a returned result); `IEquippedWeaponQuery` seam for the unbuilt Equipment System; unknown / non-equipment weapon item → no elemental contribution + dev error; Step 10 reads `CharacterStats.GetCurrentHP` (float), not `GetEffectiveStat` (floored int).
- ADRs still to write: server/client assembly boundary; server RNG injection (OQ-DC-2); server tick ordering for damage resolution (OQ-DC-4, before Auto-Attack Combat).
- TR registry is still empty: TR-dmg-001..006 are placeholders in the stories.
- Next: `/story-readiness production/epics/damage-calculation/story-001-result-types-physical-mitigation.md` → `/dev-story`. Epics without stories: authentication, status-effects, equipment-system (gate OQ-EQS-9).

## Session Extract — /story-done 2026-10-07 — Networking Core Story 031 (not committed)
- Verdict: COMPLETE WITH NOTES (17/17 criteria, all covered by automated tests; EditMode 1900 / 1900). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped; `/code-review` had already run.
- Story: `production/epics/networking-core/story-031-irreversible-outcome-coordinator.md` — Irreversible-Outcome Coordinator and Shared Failure Protocol. Status → Complete, Completion Notes added. `EPIC.md` → Complete (31/31).
- Tech debt logged: 1 item — TD-061 (a throwing `isSuccess` runs neither deliver nor the failure protocol; fix before Enhancement Story 011).
- Uncommitted: Story 030 and Story 031 work (code, tests, `.meta` files, both story files, `EPIC.md`, ADR-011 clarification, tech-debt register, this file). `bash.exe.stackdump` stays untracked.
- Next recommended: no Ready story in Networking Core (31/31). No `production/sprints/` plan exists. Enhancement Stories 009 and 011 are the consumers of this work and remain blocked on other work (dispatcher / ADR-004 code, Character Persistence types). PAT rotation still unconfirmed.

## Session Extract — /dev-story 2026-10-07 — Networking Core Story 031 implemented (not committed)
- Story: `production/epics/networking-core/story-031-irreversible-outcome-coordinator.md` — Irreversible-Outcome Coordinator and Shared Failure Protocol. Status still Ready (set by `/story-done`).
- Files changed: new `src/Foundation/Networking/IrreversibleOutcome/` (`IIrreversibleOutcomeCoordinator.cs`, `IrreversibleOutcomeCoordinator.cs`, `IrreversibleOutcomeBeginResult.cs`, `IrreversibleWriteFailureProtocol.cs`, plus Editor-generated `.meta` files and the folder `.meta`); modified `src/Foundation/Networking/CommitBeforeBroadcast/CommitBeforeBroadcastSequencer.cs` (failure path routed through `IrreversibleWriteFailureProtocol.Run`; two doc-comment words changed so the forbidden-pattern grep is clean).
- Test written: `tests/EditMode/Networking/TickLoop_IrreversibleOutcomeCoordinator_tests.cs` (+ `.meta`) — 23 `[Test]` + 9 `[TestCase]` = 32 cases.
- Test run (Unity batch mode, EditMode): 1893 / 1893 passed; the 32 new cases and the 14 untouched `TickLoop_CommitBeforeBroadcast` tests pass. Forbidden-pattern grep over the five source files: no match.
- For `/code-review`: (1) `startWrite` and `queue.Track` are two statements, not one, because the story requires a null-task check before `Track`; if `Track` itself throws after the write started, the gate opens and the exception propagates with no rollback. (2) `using UnityEngine;` in the sequencer is now unused. (3) The sequencer's log message is built before revert runs (text unchanged).
- Not wired in: nothing in production constructs the coordinator (expected by the story).
- Blockers: None.
- **/code-review 2026-10-07: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, no blocking findings). User said "fix all"; all 9 suggestions applied (the three "For `/code-review`" points above are superseded):
  - `IrreversibleOutcomeCoordinator.Begin` split: new private `StartAndTrack` (Begin now under 40 lines). If `queue.Track` throws after the write started (off the tick thread), the failure protocol runs with cause "write started but could not be tracked", the token source is left undisposed, the gate opens and the exception is rethrown. The start-delegate exception is logged with `ToString()`.
  - `IIrreversibleOutcomeCoordinator.Begin` exception docs completed; unused `using UnityEngine;` removed from the sequencer; story Implementation Notes gained the "same statement" reading.
  - Tests: now 30 `[Test]` + 9 `[TestCase]` = 39 cases. Each failure test asserts its cause text; the failure helper asserts the gate is held inside every failure step; new tests for Track throwing, revert throwing inside `Begin`, `isSuccess` throwing, a write already complete at `Begin`, `Begin` again after success and after failure; the two-character test is split in two.
  - Test run after the fixes (Unity batch mode, EditMode): 1900 / 1900 passed.
- Next: `/story-done production/epics/networking-core/story-031-irreversible-outcome-coordinator.md`. Nothing committed. PAT rotation still unconfirmed.

## Session Extract — Networking Core Story 031 created 2026-10-07 (not committed)
- New story: `production/epics/networking-core/story-031-irreversible-outcome-coordinator.md` — Irreversible-Outcome Coordinator and Shared Failure Protocol (Logic, 4h, ADR-011 Decision 5, Migration step 2). **Status: Ready** (user approved; `/story-readiness` not run). 17 criteria. Manifest Version 2026-10-07. Lean mode: QL-STORY-READY skipped.
- Decisions where ADR-011 is silent (user approved): `Begin<TOutcome, TResult>` with a write delegate and an `isSuccess` predicate (no persistence types exist in `src/`); `Begin` on a closed gate returns `RejectedWriteInFlight` (review P2 — queueing stays open); a write that throws or returns null at start runs the failure protocol and returns `PersistenceFailed`; acknowledge/compute throwing opens the gate and propagates; the source is disposed in the callback except after `TimedOut`.
- `EPIC.md`: In Progress (30/31), Story 031 row added.
- `/story-readiness` run in the same session (lean; QL-STORY-READY skipped): **READY**, 20/20. Advisory: 4h estimate is tight (17 criteria, ~25 test cases, a sequencer refactor); the story does not say what `Begin` returns if a failure step throws on the write-did-not-start path (the gate opens, the exception propagates — by the no-catch rule).
- Next: `/dev-story` on Story 031, in a fresh session.

## Session Extract — /story-done 2026-10-07 (not committed)
- Verdict: COMPLETE WITH NOTES (17/17 criteria; lean mode, director gates skipped)
- Story: `production/epics/networking-core/story-030-tick-completion-queue-mutation-gate.md` — Tick Completion Queue and Character Mutation Gate. Status Complete; `EPIC.md` now Complete (30/30).
- Tech debt logged: 1 item — TD-060 (gate `OnGateOpened` stops at the first throwing subscriber; fix before Enhancement Story 009 or the Loot Table gate story).
- Uncommitted: story, `EPIC.md`, ADR-011 clarification, `docs/tech-debt-register.md`, six source files, two test files, their `.meta` files (two folder metas included), this file. `bash.exe.stackdump` untracked and left alone. PAT rotation still unconfirmed.
- Next recommended: None identified in a sprint plan (none exists). Follow-up per ADR-011 Migration step 2: write the `IrreversibleOutcomeCoordinator` story.

## Session Extract — /code-review 2026-10-07 (Networking Core Story 030; fixes applied, not committed)
- Reviewers: main session + `unity-specialist` + `qa-tester`. Verdict CHANGES REQUIRED (2 required, 7 suggestions); user said "fix all" for items 1–5 and 7–9.
- Fixed in `TickCompletionQueue.cs` / `ITickCompletionQueue.cs`: reentrancy guard (`Drain`/`DrainOnShutdown` from a callback throws `InvalidOperationException`, caught and logged by the outer call); `DrainOnShutdown` rejects a negative/infinite timeout (`ArgumentOutOfRangeException`) and caps at `int.MaxValue` ms; it now also waits on timed-out tasks, alerts the ones still incomplete, and alerts-and-drops tasks tracked by a callback during shutdown (queue empty afterwards); split into helpers (under 40 lines).
- Tests: queue fixture 25 → 37 (12 new; `Drain_TaskIncomplete…` rewritten to three drains; extra assertions). Gate fixture unchanged (7). Batch-mode EditMode run after the fixes: 1861/1861 passed.
- **Open, user decision**: review item 6 — `CharacterMutationGate.Open` stops at the first throwing `OnGateOpened` subscriber (as the story specifies). Not changed.
- Known and accepted: a timed-out task that never completes stays in `_timedOut` until shutdown; `Track` before the first `Drain` uses `initialTick`; `Cancel()` runs token registrations inline on the tick thread.
- Next: /story-done on the story.

## Session Extract — /dev-story 2026-10-07 (Networking Core Story 030; not committed)
- Story: `production/epics/networking-core/story-030-tick-completion-queue-mutation-gate.md` — Tick Completion Queue and Character Mutation Gate. Status still Ready (not closed).
- Files changed (all new): `src/Foundation/Networking/TickCompletion/` (`ITickCompletionQueue.cs`, `TickCompletionQueue.cs`, `TickTaskResult.cs`, `TickCompletionConstants.cs`), `src/Foundation/Networking/MutationGate/` (`ICharacterMutationGate.cs`, `CharacterMutationGate.cs`), plus Unity-generated `.meta` files (must be committed).
- Test written: `tests/EditMode/Networking/TickLoop_CompletionQueue_tests.cs` (25 tests), `TickLoop_CharacterMutationGate_tests.cs` (7 tests). Batch-mode EditMode run: 1849/1849 passed, 0 failed (Unity 6000.3.10f1).
- Implemented by `engine-programmer`; diff reviewed in the main session; `unity-specialist` not spawned (plain C#).
- Agent choices beyond the story: `TickTaskResult<T>` has a public constructor; `watchdogTicks < 1` throws `ArgumentOutOfRangeException`; `DrainOnShutdown` drops still-incomplete entries (InFlightCount 0 after) and does not cancel their tokens; a task tracked by a callback during `DrainOnShutdown` is left unhandled.
- Blockers: None
- Next: /code-review on the six source files and two test files, then /story-done on the story.

## Session Extract — Networking Core Story 030 readiness 2026-10-07 (not committed)

- `/story-readiness` on Story 030 (lean; QL-STORY-READY skipped): **READY**, 20/20 checks, 6 advisory notes.
- User approved all six drafts; applied to the story: `Error` contents per status, `cancellation` may be null and is caller-owned (`Cancel()` wrapped in try/catch), late-completion criterion reworded, thread-assert exception to "`Drain` never throws", new criterion "Runs before game logic when registered first" (now 17 criteria: 13 queue, 4 gate), two new test cases (null cancellation, cancel throws).
- ADR-011 Clarifications: new 2026-10-07 entry — `DrainOnShutdown(TimeSpan)` on `ITickCompletionQueue`; null `cancellation` allowed; caller owns the source.
- Next: `/dev-story` on Story 030 (requested by the user in the same session).

## Session Extract — Networking Core Story 030 created 2026-10-07 (same session as the review below; not committed)

- The review extract below is **committed** as `a1e6be0` ("architecture: review 2026-10-07 (CONCERNS)"), not pushed; its "not committed" / "Uncommitted" notes are superseded.
- New story: `production/epics/networking-core/story-030-tick-completion-queue-mutation-gate.md` — Tick Completion Queue and Character Mutation Gate (Logic, 4h, ADR-011 Decisions 2 and 4, Migration step 1). **Status: Ready** (user approved the draft; `/story-readiness` not run). 16 criteria (12 queue, 4 gate). Manifest Version 2026-10-07.
- User decisions: shutdown drain (`DrainOnShutdown(timeout)`) is in this story; `Open` on a gate that is not closed is a no-op with no event (ADR-011 is silent on it).
- Story choices: tick thread id through an injectable `Func<int>`; watchdog length a constructor parameter defaulting to `PERSISTENCE_WATCHDOG_TICKS`; `Debug.LogError` for alerts, as in `CommitBeforeBroadcastSequencer`; files under `src/Foundation/Networking/TickCompletion/` and `MutationGate/`; tests `tests/EditMode/Networking/TickLoop_CompletionQueue_tests.cs` and `TickLoop_CharacterMutationGate_tests.cs`.
- Networking Core `EPIC.md`: In Progress (29/30), ADR-011 added to Governing ADRs, row 030; `production/epics/index.md` row updated.
- **Uncommitted since `a1e6be0`:** the story file, Networking Core `EPIC.md`, `production/epics/index.md`, this file. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Next (fresh session):** `/story-readiness` then `/dev-story production/epics/networking-core/story-030-tick-completion-queue-mutation-gate.md`.

## Session Extract — /architecture-review 2026-10-07 (full, lean; not committed)

- Verdict: **CONCERNS** (advisory). 11 ADRs, all Accepted. Domain-level, as in the earlier passes; `unity-specialist` consultation skipped (user decision); TR registry left empty (user decision — 94 TR-IDs in the EPIC files are unregistered).
- Requirements: 11 ADR-backed domains — 5 covered, 6 partial or in conflict; 2 schema gaps.
- New TR-IDs registered: None.
- GDD revision flags: None from engine behaviour. Design follow-ups: `character-persistence.md` (hotbar assignments, respec reservation state not in the record), `class-system.md` SA-1 ("event bus" wording), `npc-shop.md` CR-SHOP-5 (synchronous purchase sequence), `loot-table-system.md` (TD-059, already logged).
- Conflicts: **C1** ADR-007 `Restart=always` vs ADR-009 zero-exit zone close (+ ZoneID reuse, two supervisors); **C2** teardown order spread over ADR-002, ADR-009, CR-ZI-12, ADR-010 (`Dispose()`), ADR-011 (drain) — ADR-009's list has neither the dispose nor the drain, and CR-ZI-12 step 2 starts `SaveSession` tasks after the tick loop stops.
- Partial: **P1** no decision owns the inbound request dispatcher or the full tick order (ADR-011 cites ADR-010 D5, which defines per-handler queues; blocks Story 009); **P2** second irreversible outcome while the gate is closed (`Close` throws); **P3** ADR-001 purchase flow not restated for ADR-011; **P4** ADR-009 startup lacks NGO start, static-data-ready (CR-LT-16), wiring; **P5** who supplies the expected `SaveVersion` for a queued write.
- Engine (unconfirmed, no specialist): `Process.Exit(0)` is not a .NET API (ADR-009, ADR-011, control manifest); `StartClient(ip, port)` signature in ADR-009 D4.
- Stale: `architecture.md` (8 ADRs, lists ADR-009/010 as missing, synchronous data flow 2); ADR-007 calls ADR-006 "Proposed"; ADR-006 Mono / pool size 20 / "future" Hosting ADR.
- `src/` scan clean: no `await`/`.Result`/`.Wait(`, no `EventBus`/`UnityEvent`, no lambda subscriptions, all 14 event arg types `readonly struct`.
- Top ADR gaps: (1) inbound request dispatch + tick order (ADR-012 or ADR-010 amendment); (2) ADR-009 Amendment 1 (teardown + startup + exit call); (3) ADR-007 amendment (supervision).  Then ADR-011 clarification (P2, P5), Character Persistence + ADR-006 Amendment 2 (hotbar, reservation), ADR-001 Amendment A2, `architecture.md` refresh.
- Written: `docs/architecture/architecture-review-2026-10-07.md` (new), `docs/architecture/architecture-traceability.md` (rewritten to 11 ADRs), this file. **Uncommitted since `3345f44`.** Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Next:** a story for `TickCompletionQueue` + `CharacterMutationGate` (ADR-011 Migration step 1) can still go first — it does not depend on the findings. The coordinator and the dispatcher do: write the dispatch decision (P1) and the ADR-011 clarification (P2, P5) before them. Each ADR in a fresh session.

## Session Extract — /create-control-manifest update 2026-10-07 (Manifest Version 2026-06-28 → 2026-10-07; not committed)

- Verdict: COMPLETE. `docs/architecture/control-manifest.md` updated incrementally — ADR-001 to ADR-010 rules untouched. Review mode lean — TD-MANIFEST skipped.
- Added (source ADR-011 unless noted): Foundation — 10 required (one from ADR-006 Amendment 1: `inventory_slots` entries carry `enhancement_level`), 7 forbidden, 4 guardrails, under a new group "Asynchronous persistence in the tick loop (ADR-011)"; Core — 1 forbidden; Feature — 2 required, 2 forbidden; Global cross-cutting — 1. Header: ADRs Covered now lists ADR-011.
- **ADR-011 clarified the same day (user approved; wording only, new "Clarifications" section):** the 5 s `PERSISTENCE_WRITE_TIMEOUT_SECONDS` of ADR-006 fires before the 10 s watchdog; the shutdown drain sits in ADR-009's teardown sequence before `Process.Exit(0)` (the text said "before `Application.Quit()`").
- **Side effect:** every story embeds `Manifest Version: 2026-06-28`. Open (not Complete) stories will get a staleness advisory at `/story-readiness` / `/story-done` until re-checked; none of the new rules applies to code already written except the no-`await` rule, which existing `src/` already satisfies (only the test crash injector and the sequencer's doc comments mention async).
- **Committed and pushed** (the user asked) in the commit titled "architecture: control manifest 2026-10-07 (ADR-011 rules); ADR-011 clarifications" — the commit after `4f8e3f3`; the "not committed" in this extract's heading is superseded. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Next:** `/architecture-review` in a fresh session. Then a story for `TickCompletionQueue` + `CharacterMutationGate` (ADR-011 Migration step 1; no epic owns it yet — Networking Core is the natural home).

## Session Extract — ADR-011 accepted 2026-10-07 (same session as the extract below; not committed)

- The user said "accept ADR-011". `docs/architecture/ADR-011-async-persistence-tick-loop.md` is **Accepted (2026-10-07)**; its six entries in `docs/registry/architecture.yaml` are `status: active`. Every "Proposed" in the extract below is superseded.
- The ADR's commit `978d0a6` was pushed first (`origin/main` at `978d0a6`); the "Not pushed" note below is superseded.
- Stories 009 and 011 are **still Blocked**, now only on: 009 — the inbound request dispatcher (Networking Core, next to ADR-004 code); 011 — Character Persistence (`SaveIrreversibleOutcome`), and TD-046 for its client-facing criteria. Story 011 can be developed against a fake `ICharacterPersistence` once someone decides to split it that way (`/story-readiness`). Enhancement `EPIC.md`, epics index, `enhancement-system.md` OQ-ENH-7 text, `systems-index.md` and TD-059 wording updated to say Accepted.
- Engine checks listed under the ADR's "Verification Required" (headless IL2CPP build, real Npgsql write, shutdown drain, SIGTERM) are still open — they gate shipping, not acceptance.
- **Committed and pushed** (the user asked) in the commit titled "architecture: ADR-011 Accepted" — the commit after `978d0a6`; the "not committed" in this extract's heading is superseded. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Next:** `/create-control-manifest update` (adds the no-`await`-in-tick-code and persistence-layer rules; changes the manifest version, which every open story embeds). `/architecture-review` in a fresh session. Buildable now with no other dependency: `TickCompletionQueue` and `CharacterMutationGate` (ADR-011 Migration step 1) — no story exists for them yet.

## Session Extract — /architecture-decision 2026-10-07 (ADR-011 Asynchronous Persistence in the Server Tick Loop — written as Proposed; not committed)

- File: `docs/architecture/ADR-011-async-persistence-tick-loop.md`, **Status: Proposed** (the user chose Proposed, not Accepted). Review mode lean — TD-ADR skipped. `unity-specialist` engine validation run: no blocking issue; its concerns are folded in (it could verify nothing from the engine reference files, which have no async content — the headless-build checks in the ADR are real gates).
- **Decision:** tick-driven code starts a persistence `Task` and never awaits or blocks on it; `ITickCompletionQueue.Drain` runs once per tick before game logic and handles results on the tick thread (watchdog `PERSISTENCE_WATCHDOG_TICKS` = 200); persistence implementations snapshot in the synchronous prefix and use `ConfigureAwait(false)`; a per-character `ICharacterMutationGate` is closed while an irreversible write is in flight; `IrreversibleOutcomeCoordinator` splits commit-before-broadcast across ticks, reusing the sequencer's failure protocol.
- **User decisions:** alternatives A/B/C with A (tick-drained queue) recommended and accepted; OQ-ENH-7 → dispatcher holds client requests + shared gate (`MAX_HELD_REQUESTS_PER_CHARACTER` = 16); a server-originated pickup at a closed gate is deferred and retried after the gate opens; all persistence calls from tick code go through the queue (the gate applies only to `SaveIrreversibleOutcome`).
- Registry (`docs/registry/architecture.yaml`): 6 entries, all `status: proposed` — interfaces `tick_completion_queue`, `character_mutation_gate`; budget `persistence_result_handling`; API decision `tick_code_async_persistence_consumption`; forbidden patterns `await_or_block_on_task_in_tick_code`, `persistence_await_capturing_unity_context`. Set them to active when the ADR is Accepted.
- Follow-ups done: `enhancement-system.md` — OQ-ENH-7 marked resolved, CR-ENH-18 last sentence points at ADR-011, "Last Updated" (wording only); `systems-index.md` row 15 note; Stories 009 and 011 — blocker text now names ADR-011, **both still Blocked** (009: ADR acceptance + the request dispatcher; 011: ADR acceptance + Character Persistence + TD-046 for client-facing criteria); Enhancement `EPIC.md` and `production/epics/index.md`; **TD-059** (Loot Table GDD has no "pickup deferred while the gate is closed" note; ADR-006 Mono vs ADR-007 IL2CPP wording).
- Not done: control manifest update (ADR is not Accepted); no code.
- **Committed on `main`** (the user asked) in the commit titled "architecture: ADR-011 asynchronous persistence in the server tick loop (Proposed)" — the commit after `faa134c`; the "not committed" in this extract's heading is superseded. Not pushed. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Next:** read ADR-011 and accept it (set Status and the six registry entries), then `/create-control-manifest update`. `/architecture-review` must run in a fresh session, never in this one.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 008 — COMPLETE WITH NOTES; Enhancement epic 8/11; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-008-scroll-source-restriction.md` — Scroll Exclusion Validator Rule (MVP). 7/7 criteria; test file 16 tests; EditMode 1817/1817. Review mode lean — QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
- Enhancement `EPIC.md`: In Progress (8/11; 0 Ready; 3 Blocked — 009 on OQ-ENH-7, 010 on TD-046, 011 on the async-persistence decision and Character Persistence); `production/epics/index.md` row updated.
- Deviation (the not-ready item database check added at code review): the user chose to **add one sentence to CR-LT-16** instead of a tech debt item. `design/gdd/loot-table-system.md` (CR-LT-16 + "Last Updated") and the `ALLOW_ENHANCEMENT_SCROLL_DROPS` note in `design/registry/entities.yaml` updated — wording that follows the code; no review pass run on it. No AC was added for it in the GDD (the story's tests cover it).
- Tech debt logged: None.
- Still true: no production caller of `LootTableRegistry.TryCreate` (no loot data loader) — the rule is enforced in tests only until one exists.
- **Committed on `main`** (the user asked) as the two commits after `757d1d2`: "design: CR-LT-16 — loot table validation needs a ready Item Database" (`loot-table-system.md`, `entities.yaml`) and "Enhancement System: Story 008 (scroll exclusion validator rule)" (code, tests, `.meta`, story, `EPIC.md`, index, this file). The "not committed" / "Uncommitted" notes in the three Story 008 extracts (this one and the two below) are superseded. Not pushed. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- Next recommended: none Ready in the Enhancement epic. To unblock 009–011: one architecture decision on asynchronous persistence in the tick loop (covers OQ-ENH-7), the wire-protocol authoring session for TD-046, and a Character Persistence epic.

## Session Extract — /code-review 2026-10-07 (Enhancement Story 008 — APPROVED WITH SUGGESTIONS; all five applied; 1817/1817; not committed)

- Lean self-review in the dev-story session (no specialists spawned). No required changes. ADR-010 compliant (`IItemDatabase` passed in; no bus, no singleton).
- **The user said "fix all" — S1–S5 applied by the orchestrator:**
  - S1: `LootTableValidator.Validate` throws `InvalidOperationException` when the switch is off and `itemDatabase.IsReady` is false (a not-ready database answers "not found" for every item, which would pass every scroll entry). Doc `<exception>` on `Validate` and `LootTableRegistry.TryCreate`. Three new tests (Validate / TryCreate throw; switch on does not throw). **Not in the story or the GDD** — a behaviour added at review; mention it in `/story-done`.
  - S2: the potion fixture has real `ConsumableData` (`EffectType.RestoreHP`).
  - S3: the `ScrollDropsAllowed` alias is gone — call sites pass `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS`.
  - S4: the four existing test files use `EmptyItemDatabase` from `LootTestFakes.cs`; the four `using IronGrind.Tests.EditMode.InventorySystem;` lines added by the dev-story agent are removed (KillResolution gained `using IronGrind.Tests.EditMode.LootTableSystem;`).
  - S5: the `TryCreate` rejection test also asserts the `ItemID`.
- New test file now 16 `[Test]`. EditMode 1817/1817 (Unity 6000.3.10f1 batch mode).
- Supersedes the "For `/code-review`" list and the 13 / 1814 figures in the dev-story extract below.
- **Uncommitted since `757d1d2`:** 3 source files, 4 existing test files, the new test file + `.meta`, this file.
- Next: `/story-done production/epics/enhancement-system/story-008-scroll-source-restriction.md`.

## Session Extract — /dev-story 2026-10-07 (Enhancement Story 008 implemented — 1814/1814 EditMode, not reviewed, not committed)

- Story: `production/epics/enhancement-system/story-008-scroll-source-restriction.md` — Scroll Exclusion Validator Rule (MVP). Status still Ready (`/story-done` not run). Same session as the design-review extract below (no `/clear` in between); that work is committed as `757d1d2`.
- One `gameplay-programmer` agent (src + tests), tight brief; diff reviewed by the orchestrator. No orchestrator edits to code.
- Source (`src/Foundation/LootTableSystem/`): `LootTableConstants.cs` (+`ALLOW_ENHANCEMENT_SCROLL_DROPS = false`), `LootTableValidator.cs` (`Validate(tables, IItemDatabase, bool)`, null database throws, scroll rule after the `DropChance` check), `LootTableRegistry.cs` (`TryCreate` passes both through).
- Tests: new `tests/EditMode/LootTableSystem/LootTable_ScrollExclusion_tests.cs` (13 `[Test]`, + `.meta` generated by the run); 13 call sites updated in `LootTable_DefinitionValidation_tests.cs` (×10), `LootTable_KillResolution_integration_tests.cs`, `LootTable_RoundRobin_integration_tests.cs`, `LootTable_GroundItemLifecycle_tests.cs`.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1814/1814 (was 1801).
- No production caller of `TryCreate` exists yet (no loot data loader) — to be said in the completion notes.
- For `/code-review`: (1) the potion fixture in the new tests has no `ConsumableData` (a record the Item Database validator would reject) — a real potion record would be truer; (2) the alias `ScrollDropsAllowed` in `LootTable_DefinitionValidation_tests.cs` is a PascalCase const and reads as "allowed" while its value is false; (3) the three integration/lifecycle sites pass `new StubItemDatabase()` although two of those files have their own fake item database.
- **Uncommitted since `757d1d2`:** everything above and this file.
- Next: `/code-review` on the changed files, then `/story-done`.

## Session Extract — /design-review 2026-10-07 (loot-table-system.md CR-LT-16 amendment, lean — NEEDS REVISION, 1 blocker, revised in the same session; not committed)

- Verdict: NEEDS REVISION — 1 blocking, 4 recommended. CR-LT-16 itself is sound; no primitive gaps, no convergence stall.
- **Blocker:** the new scroll criterion reused the ID AC-LT-25 (already the winner-grace criterion: Story 013, TR-loot-012, the grace tests, TD-056). **Renumbered to AC-LT-26.** Every "AC-LT-25" in the readiness extract below that refers to the scroll rule now reads AC-LT-26 in the files.
- **User chose "Revise now — blocker + R1–R4". Applied:**
  - `design/gdd/loot-table-system.md` — header status and two 2026-10-07 "Last Updated" entries; AC-LT-26 heading plus a two-entry clause; both "Consumables" drop rate rows exclude Enhancement Scrolls at MVP.
  - `design/gdd/enhancement-system.md` (dependency row, AC-ENH-24), `design/gdd/systems-index.md`, `design/registry/entities.yaml` (`ALLOW_ENHANCEMENT_SCROLL_DROPS` note, `revised: 2026-10-07`), Story 008, Enhancement `EPIC.md` — AC-LT-25 → AC-LT-26.
  - `design/gdd/item-database.md` — Loot Table rows of the Interactions and Dependencies tables name the `GetItem` → `ScrollData` read at table validation (wording only; header not touched).
  - `design/gdd/reviews/loot-table-system-review-log.md` — 2026-10-07 entry appended.
- Not applied (nice-to-have): a core rule defining startup loot table validation; whether an unknown `ItemID` in an entry is an error.
- Propagation check: no scroll-rule citation of AC-LT-25 is left outside this file's older extract and the review log's description of the blocker.
- **Committed on `main`** (the user asked for it at the end of this session) in the commit titled "design: Loot Table CR-LT-16 lean re-review — AC-LT-26 renumber, amendment Approved; Story 008 Ready" — the commit after `9725da4`; the "not committed" in this extract's heading is superseded. Not pushed. (The readiness extract's "Uncommitted since `687fa33`" note is also superseded — that work is `9725da4`.) Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- **Closing decision: the user accepted the revisions and marked the amendment Approved** (no further review pass). Status set to Approved in the GDD header, `systems-index.md` and the `enhancement-system.md` dependency row; review log has an "Approved by acceptance" note. **Story 008 is Ready** — story file, Enhancement `EPIC.md` (1 Ready, 3 Blocked) and `production/epics/index.md` updated.
- **Next:** `/dev-story production/epics/enhancement-system/story-008-scroll-source-restriction.md` (fresh session). The story cites AC-LT-26.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 008 — rewritten as a validator rule; CR-LT-16 design amendment applied; Blocked on a lean re-review; not committed)

- Story: `production/epics/enhancement-system/story-008-scroll-source-restriction.md`. First verdict NEEDS WORK: no production loot tables exist (no assets, no loader), `LootTableRegistry` cannot list its tables, so the test-only scan had nothing to scan. No consumable pool path exists (every `LootTableEntry` names one `ItemID`). "Scrolls identified by data" is already covered by `ItemDatabase_MvpRecords_tests.cs`.
- **User decisions:** enforce the restriction as a loot table validation rule; **the shop-only restriction is an MVP constraint only — post-MVP, scrolls should be a rare drop on some monsters.** Approved the draft with a switch.
- **Design amendment applied (authoring):**
  - `design/gdd/loot-table-system.md` — new CR-LT-16 (Enhancement Scroll Exclusion, MVP), knob `ALLOW_ENHANCEMENT_SCROLL_DROPS` (default `false`, "Data Validation Switches" table), AC-LT-25 (totals 21 BLOCKING / 5 ADVISORY / 26), Item Database interaction and dependency rows (`ScrollData` read at validation), Enhancement System added as a downstream dependent, status line "lean re-review pending".
  - `design/gdd/enhancement-system.md` — AC-ENH-24 reworded as an MVP constraint enforced by CR-LT-16; F-ENH-5 economy note says the figures assume bought scrolls; Loot Table System dependency row; new **OQ-ENH-9** (post-MVP scroll drops: economy re-validation, TK-ENH-9 prices, a rare-drop classification since a scroll is `GearTier.None` = Common under CR-LT-5, AC-ENH-24 retirement).
  - `design/gdd/npc-shop.md` — "sold exclusively here at MVP"; wrong CR-ENH-3 citation replaced by AC-ENH-24 / CR-LT-16.
  - `design/registry/entities.yaml` — four scroll notes (MVP wording, corrected citation, `revised: 2026-10-07`); new constant `ALLOW_ENHANCEMENT_SCROLL_DROPS`.
  - `design/gdd/systems-index.md` — Loot Table and Enhancement rows note the amendment.
- **Propagation check:** `CR-ENH-3` as "scroll source restriction" no longer cited anywhere (item-database.md's two CR-ENH-3 mentions are about tier match — correct). "NPC Shop only" wording: registry and npc-shop.md updated.
- **Story 008 rewritten** as "Scroll Exclusion Validator Rule (MVP)" (Logic, 3h, ADR-010): `LootTableValidator.Validate` / `LootTableRegistry.TryCreate` gain `IItemDatabase` and `allowEnhancementScrollDrops`; `LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS = false`; 13 existing test call sites change; new test file `tests/EditMode/LootTableSystem/LootTable_ScrollExclusion_tests.cs`. Status **Blocked** on the re-review. Enhancement `EPIC.md` (0 Ready, 4 Blocked) and `production/epics/index.md` updated.
- **Uncommitted since `687fa33`:** the five design files above, Story 008, Enhancement `EPIC.md`, `production/epics/index.md`, this file.
- **Next (fresh session, `/clear` first):** `/design-review design/gdd/loot-table-system.md --depth lean` (targeted amendment, same structure). If it passes: append the review log, set the GDD and systems-index status, set Story 008 to Ready, then `/dev-story` on it.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 006 — COMPLETE WITH NOTES; Enhancement epic 7/11; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-006-npc-interaction-session.md` — NPC Interaction Session. 9/9 criteria; test file 44 cases; EditMode 1801/1801.
- Enhancement `EPIC.md`: In Progress (7/11); `production/epics/index.md` row updated.
- Tech debt logged: **TD-058** — the four documentation mismatches offered since Story 005 (now confirmed by the user) plus the wire protocol's unimplemented `npcId` validation. The "offered three times and not confirmed" note in the Story 007 extract below is superseded.
- **Uncommitted since `b932244` (itself not pushed; `main` is 2 ahead of `origin`):** `src/Foundation/NpcInteraction/` (4 `.cs` + `.meta`, folder `.meta`; the interface moved from `EnhancementSystem/`), `EnhancementService.cs`, the NpcInteractionSession test file (+ `.meta`), `EnhancementTestDoubles.cs`, Story 006, Enhancement `EPIC.md`, `production/epics/index.md`, `docs/tech-debt-register.md`, this file.
- Next recommended: Story 008 — Scroll Source Restriction Scan (`story-008-*.md`, Logic, 1h; readiness must check whether production loot tables exist and whether a scroll is reachable through a consumable pool). It is the last Ready story; 009, 010 and 011 are Blocked.

## Session Extract — /code-review 2026-10-07 (Enhancement Story 006 — APPROVED WITH SUGGESTIONS; both applied; not committed)

- Lean self-review in the dev-story session (no specialists spawned). No required changes. ADR-010 compliant (interface dependencies, no bus, no callbacks).
- Suggestions, not applied: (S1) the constructor accepts `double.PositiveInfinity` as the lifetime, which gives a session that never expires — reject it with the NaN / non-positive guard and add one `[TestCase]` row; (S2) `AssertBeginRejectedNoSession` hardcodes the seeded scroll count and level (`STACK_OF_THREE`, `LEVEL_TWO`) instead of reading them before the act.
- Noted, no action: the tracker-only tests sit under the Enhancement test folder (story-mandated path) and could move to a `NpcInteraction` test folder when the NPC Shop epic starts.
- **Update (same day): the user said "fix all" — S1–S2 applied by the orchestrator:** the constructor rejects an infinite lifetime (`double.IsInfinity`), with a `double.PositiveInfinity` `[TestCase]` row (test renamed `Constructor_NonPositiveNaNOrInfiniteLifetime_…`); `AssertBeginRejectedNoSession` reads the scroll count and level before the act. Test file now 31 `[Test]` + 13 `[TestCase]` rows = 44 cases. EditMode 1801/1801.
- Next: `/story-done production/epics/enhancement-system/story-006-npc-interaction-session.md`.

## Session Extract — /dev-story 2026-10-07 (Enhancement Story 006 implemented — 1800/1800 EditMode, not reviewed, not committed)

- Story: `production/epics/enhancement-system/story-006-npc-interaction-session.md` — NPC Interaction Session. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- Two `gameplay-programmer` agents in parallel against one contract (src / tests). No orchestrator edits to code.
- Source: new folder `src/Foundation/NpcInteraction/` (namespace `IronGrind.NpcInteraction`) — `NpcInteractionSessionTracker.cs`, `ITownHubQuery.cs`, `NpcInteractionOpenResult.cs`; `INpcInteractionSessions.cs` (+ `.meta`) moved there with `git mv` from `EnhancementSystem/`; `EnhancementService.cs` gained one `using`.
- Tests: new `tests/EditMode/Integration/EnhancementSystem/Enhancement_NpcInteractionSession_integration_tests.cs` (31 `[Test]` + 12 `[TestCase]` rows = 43 cases); `EnhancementTestDoubles.cs` — `StubTownHubQuery`, `ManualClock`, one `using`. Five `.meta` files generated by the run (folder, three source files, test file).
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1800/1800 (was 1757).
- For `/code-review`: `Close` / `NotifyZoneTransition` / `NotifySessionEnded` have identical bodies (kept separate on purpose — three distinct callers); `story-003-attempt-validation.md` still names the interface's old location (historical).
- **Uncommitted since `b932244`:** everything above, Story 006, this file.
- Next: `/code-review` on the changed files, then `/story-done`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 006 — READY after four decisions; not committed)

- Story 007 (extracts below) is committed as `b932244` — **not pushed** (`main` is 2 ahead of `origin`: `feaa172`, `b932244`). Its "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-006-npc-interaction-session.md` — NPC Interaction Session (Integration, 3h). First verdict NEEDS WORK: four decisions deferred to readiness.
- **User decisions (approved the recommendations):** (1) tracker and `INpcInteractionSessions` live in a new `IronGrind.NpcInteraction` namespace (`src/Foundation/NpcInteraction/`) — the interface moves out of `IronGrind.EnhancementSystem`; (2) `Open` stores `npcId` and validates nothing, `IsActive` stays boolean, unknown-id rejection deferred to Story 010 / NPC authoring (OQ-NS-1 stays open); (3) expired from exactly T + 300 (active while `now < T + lifetime`); (4) lifetime is a constructor argument, production passes `CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS`, constant not re-homed. Clock seam: `Func<double>` seconds.
- **Applied to Story 006:** the decisions above, a performance line, "disconnect" → "session end" (CR-ENH-17 clears on session end, not on the disconnect), one added QA case (`npcId` stored, not validated).
- **Uncommitted:** Story 006, this file.
- Next: `/dev-story production/epics/enhancement-system/story-006-npc-interaction-session.md` (the user asked to go straight on).

## Session Extract — /story-done 2026-10-07 (Enhancement Story 007 — COMPLETE WITH NOTES; Enhancement epic 6/11; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-007-outcome-events-and-broadcast-trigger.md` — Outcome Events and +9 Broadcast Trigger. 7/7 criteria; test file 25 cases; EditMode 1757/1757.
- Enhancement `EPIC.md`: In Progress (6/11); `production/epics/index.md` row updated.
- Tech debt logged: None. **Offered three times now and not confirmed (the user's "yes" answered the status updates each time) — four small documentation mismatches:** (1) `enhancement-system.md` describes `ConfirmEnhancement` as one server step (code: `BeginAttempt` / `CompleteAttempt` / `RollBackAttempt`); (2) it says the restored scroll "refills the slot it emptied" (positions can swap); (3) it lists `OnPrestigeBandChange` as an Enhancement signal (belongs to Equipment); (4) the control manifest requires event args in a shared `IronGrind.Events` namespace that no code uses.
- **Uncommitted since `feaa172` (itself not pushed):** `EnhancementService.cs`, `EnhancementConstants.cs`, three event-args files (+ `.meta`), the OutcomeEvents test file (+ `.meta`), Story 007, Enhancement `EPIC.md`, `production/epics/index.md`, this file.
- Next recommended: Story 006 — NPC Interaction Session (`story-006-npc-interaction-session.md`, Integration, 3h; readiness must settle where the shared session type lives, the `npcId` meaning, the TTL boundary and the `SESSION_TTL_SECONDS` constant's home) or Story 008 — Scroll Source Restriction Scan (Logic, 1h; readiness must check whether production loot tables exist and whether a scroll is reachable through a consumable pool). After those, the epic's remaining three stories are all Blocked (009, 010, 011).

## Session Extract — /code-review 2026-10-07 (Enhancement Story 007 — APPROVED WITH SUGGESTIONS → "fix all" applied, 1757/1757 EditMode, not committed)

- Lean self-review in the dev-story session (no specialists spawned). No required changes; two suggestions, both applied by the orchestrator. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (S1) `RaiseSuccess` / `RaiseDestruction` / `RaiseBroadcast` replaced by one `private static void Raise<TArgs>(Action<TArgs> handlers, in TArgs args)`; (S2) new test `CompleteAttempt_ThrowingDestructionSubscriber_LogsExceptionAndReturnsNormalResult`.
- Test file: 16 `[Test]` + 9 `[TestCase]` rows = 25 cases. EditMode 1757/1757 (Unity 6000.3.10f1 batch mode).
- Noted, not a code issue: on the first IL2CPP server build, `Action<T>` over a struct may need a `link.xml` preservation entry (control manifest) — applies to these three events like every existing one.
- **Uncommitted since `feaa172` (which is itself not pushed):** `EnhancementService.cs`, `EnhancementConstants.cs`, three new event-args files (+ `.meta`), the OutcomeEvents test file (+ `.meta`), this file.
- Next: `/story-done production/epics/enhancement-system/story-007-outcome-events-and-broadcast-trigger.md`.

## Session Extract — /dev-story 2026-10-07 (Enhancement Story 007 implemented — 1756/1756 EditMode, not reviewed, not committed)

- Story 005 and the Story 007 rewrite (extracts below) are committed as `feaa172` — **not pushed** (`main` is 1 ahead of `origin`). Their "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-007-outcome-events-and-broadcast-trigger.md` — Outcome Events and +9 Broadcast Trigger. Status still Ready (`/story-done` not run).
- Two `gameplay-programmer` agents in parallel against one contract (src / tests). No orchestrator edits.
- Source: new `EnhancementSuccessEventArgs.cs`, `EnhancementDestructionEventArgs.cs`, `EnhancementBroadcastEventArgs.cs`; `EnhancementConstants.cs` (`SERVER_BROADCAST_LEVEL = 9`); `EnhancementService.cs` — events `OnEnhancementSuccess`, `OnEnhancementDestruction`, `OnEnhancementBroadcastLevelReached`, raised from `CompleteAttempt` after the unlock through `RaiseSuccess` / `RaiseDestruction` / `RaiseBroadcast` (each `try`/`catch` + `Debug.LogException`).
- Tests: new `tests/EditMode/EnhancementSystem/EnhancementSystem_OutcomeEvents_tests.cs` (15 `[Test]` + 9 `[TestCase]` rows = 24 cases). Four `.meta` files generated by the run.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1756/1756 (was 1732).
- For `/code-review`: the three `Raise…` helpers are identical apart from the type (one generic helper would do); a multicast delegate stops at the first throwing subscriber, so later subscribers of the same event are skipped (documented in the story as accepted, matches the Loot services).
- Next: `/code-review` on the changed files, then `/story-done`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 007 — READY after a rewrite; Story 005 work still uncommitted)

- The user said "continue" after Story 005's close-out; taken as "next story". **Story 005's work is NOT committed** (see the extract below for the file list).
- Story: `production/epics/enhancement-system/story-007-outcome-events-and-broadcast-trigger.md` — Outcome Events and +9 Broadcast Trigger. First verdict NEEDS WORK: written for the single-call design; three open questions.
- **User decisions (approved the recommendations):** `OnPrestigeBandChange` is not implemented here (belongs to the Equipment System; GDD rows to be corrected later); the +9 trigger carries `CharacterID` + `ItemID` + level, and Story 010 resolves the display names; event argument types go in `IronGrind.EnhancementSystem`, following the codebase (the manifest's shared `IronGrind.Events` namespace is used by no code — drift noted, not resolved).
- **Applied:** Story 007 rewritten — three events raised from `CompleteAttempt` after the unlock (`OnEnhancementSuccess`, `OnEnhancementDestruction`, `OnEnhancementBroadcastLevelReached`); nothing from `BeginAttempt`, `RollBackAttempt` or rejections; subscriber exceptions caught and logged with `Debug.LogException` (Loot services precedent); `EnhancementConstants.SERVER_BROADCAST_LEVEL = 9`. Enhancement `EPIC.md` row and open point updated.
- **Uncommitted:** everything listed in the Story 005 extract below, plus Story 007's rewrite.
- Next: `/dev-story production/epics/enhancement-system/story-007-outcome-events-and-broadcast-trigger.md` (or commit first).

## Session Extract — /story-done 2026-10-07 (Enhancement Story 005 — COMPLETE WITH NOTES; Enhancement epic 5/11; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-005-commit-and-rollback.md` — Rollback and the Pending Window. 11/11 criteria; test file 24 cases; EditMode 1732/1732.
- Enhancement `EPIC.md`: In Progress (5/11); `production/epics/index.md` row updated.
- Tech debt logged: None. Still offered and not confirmed: one entry noting that `enhancement-system.md` describes `ConfirmEnhancement` as one step (code has `BeginAttempt` / `CompleteAttempt` / `RollBackAttempt`) and says the restored scroll "refills the slot it emptied" (positions can swap).
- **Uncommitted since `0faea44`:** `EnhancementService.cs`; the Rollback test file (+ `.meta`) and `EnhancementTestDoubles.cs`; Stories 005, 007, 009, 010 and new Story 011; Enhancement `EPIC.md`; `production/epics/index.md`; this file.
- Next recommended: Story 007 — Outcome Events and +9 Broadcast Trigger (`story-007-outcome-events-and-broadcast-trigger.md`, Logic, 2h; both dependencies now Complete). Its readiness must settle: events are raised from `CompleteAttempt`; who owns `OnPrestigeBandChange`; how the player's display name reaches the +9 trigger. Stories 006 (NPC session, 3h) and 008 (scroll source scan, 1h) are also Ready. Blocked: 009 (OQ-ENH-7), 010 (TD-046), 011 (async-persistence decision, Character Persistence).

## Session Extract — /dev-story + /code-review 2026-10-07 (Enhancement Story 005 implemented — 1732/1732 EditMode; review APPROVED WITH SUGGESTIONS, suggestions not yet applied; not committed)

- Story: `production/epics/enhancement-system/story-005-commit-and-rollback.md` — Rollback and the Pending Window. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- Two `gameplay-programmer` agents in parallel against one contract (src / tests). No orchestrator edits.
- Source: `EnhancementService.cs` — `RollBackAttempt(charId)` with private `RestoreItem` / `RestoreScroll`; `try { item; scroll } finally { remove pending; UnlockSlot }`, then the `CriticalEnhancementWriteFailed` error; docs of the class, `IsAttemptInProgress`, `BeginAttempt`, `CompleteAttempt` updated.
- Tests: new `Enhancement_Rollback_integration_tests.cs` (20 `[Test]` + 4 `[TestCase]` rows = 24 cases, + `.meta`); `EnhancementTestDoubles.cs` — `FailForceInsert`, `FailPickup` on the decorator.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1732/1732 (was 1708).
- `/code-review` (lean self-review, requested while the test agent was running, done once the suite passed): no required changes. Suggestions, awaiting the user's answer: (S1) a subscriber exception during the item restore skips the scroll restore — both steps share one `try`, so the player would lose the scroll; run the scroll restore even if the item restore throws; (S2) on that exception path the `CriticalEnhancementWriteFailed` error is not logged; (S3) the tests match log prefixes only, while the criterion says the errors name the character, item, previous level and scroll.
- **Update (same day): the user said "fix all" — S1–S3 applied by the orchestrator:** `RollBackAttempt` nests the item restore in its own `try`/`finally` so the scroll restore always runs, and the `CriticalEnhancementWriteFailed` error is logged inside the outer `finally` (every path); the method's remarks updated; the three log expectations now also match the character, the sword's `ItemID`, a level and the scroll's `ItemID` (`WithIdentifiers`); the two exception tests expect the write-failed error, and the item-restore one asserts the scroll is still restored (renamed `…_ScrollStillRestored_…`). Test count unchanged (24 cases). EditMode 1732/1732.
- Next: `/story-done production/epics/enhancement-system/story-005-commit-and-rollback.md`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 005 — split; 005 READY as "Rollback and the Pending Window", new Story 011 Blocked; not committed)

- Story 004 (extracts below) is committed and pushed as `0faea44`; their "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-005-commit-and-rollback.md`. First verdict NEEDS WORK: written for the replaced single-call design; half implementable now (service rollback), half not (the commit caller).
- **User decision (approved the recommendation): split.** Story 005 rewritten as **Rollback and the Pending Window** (Integration, 3h, Ready): `RollBackAttempt(charId)`, rollback order, `CriticalEnhancementWriteFailed` / `CriticalEnhancementRollbackFailed` logs, AC-ENH-7, and the bag-state halves of AC-ENH-23, 34, 35. New **Story 011 — Commit Orchestration** (`story-011-commit-orchestration.md`, Blocked): one `SaveIrreversibleOutcome` call, failure codes → rollback, no result before or without a successful commit, client disconnect, AC-ENH-13.
- **Story 011 blockers:** (1) a decision, probably an ADR, on how tick-loop code consumes an asynchronous persistence call (existing `CommitBeforeBroadcastSequencer` is synchronous and would block the tick; also bears on OQ-ENH-7, level-up, respec, item consumption); (2) Character Persistence has no implementation or epic; (3) TD-046 for the client-facing criteria.
- **Recorded in Story 005:** after a destruction rollback the item goes to the lowest empty slot, so an emptied scroll slot with a lower index than the item's old slot swaps positions with it — contents correct, GDD's "refills the slot it emptied" not guaranteed; tests assert contents there.
- **Propagated:** Enhancement `EPIC.md` (4/11, table rows 005 and 011, order, AC allocation, open points, next step); `production/epics/index.md`; dependency lines and a dated note in Stories 007, 009, 010.
- Checked in code: `MoveFailReason.SourceLocked` exists; `ForceInsert` places in the lowest empty slot at the given level; `Pickup` tops up or fills; the Story 004 decorator already records `ForceInsert` and `Pickup`.
- **Uncommitted:** Stories 005, 007, 009, 010, new Story 011, Enhancement `EPIC.md`, `production/epics/index.md`, this file.
- Next: `/dev-story production/epics/enhancement-system/story-005-commit-and-rollback.md`.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 004 — COMPLETE WITH NOTES; Enhancement epic 4/10; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-004-attempt-sequence.md` — Attempt Sequence and Outcome Resolution. 12/12 criteria; test file 32 cases; EditMode 1708/1708.
- Enhancement `EPIC.md`: In Progress (4/10); `production/epics/index.md` row updated.
- Tech debt logged: None. **Offered, not confirmed by the user (their "yes" answered the status update):** an entry noting that `enhancement-system.md` still describes `ConfirmEnhancement` as one server step and does not mention `BeginAttempt` / `CompleteAttempt`.
- **Uncommitted since `18c76a5`:** `EnhancementService.cs`, `IInventoryService.cs` (doc comments), `EnhancementAttemptStart.cs`, `EnhancementAttemptResult.cs` (+ `.meta`); the AttemptSequence test file and `EnhancementTestDoubles.cs` (+ `.meta`), the Story 003 test file; Stories 004 and 005; Enhancement `EPIC.md`; `production/epics/index.md`; this file.
- Next recommended: Story 005 — Commit-Then-Deliver and Rollback needs a readiness pass that rewrites its criteria for the two-phase design and settles the commit orchestrator (possibly an ADR). Stories 006 (NPC Interaction Session, 3h) and 008 (Scroll Source Restriction Scan, 1h) do not depend on that. Story 007 (outcome events) depends on 005.

## Session Extract — /code-review 2026-10-07 (Enhancement Story 004 — CHANGES REQUIRED → "fix all" applied, 1708/1708 EditMode, not committed)

- Lean self-review in the dev-story session (no specialists spawned). One required change, two suggestions; all applied by the orchestrator. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (R1) `BeginAttempt` wraps everything after `LockSlot` in `try`/`catch`: any exception — including one thrown by an `OnInventoryChanged` subscriber, which `InventoryService` lets propagate — unlocks the item slot and is rethrown with nothing pending; `ApplyOutcome` no longer unlocks itself (the catch does). New test `BeginAttempt_InventorySubscriberThrowsDuringScrollConsumption_UnlocksItemNothingPendingAndRethrows`. (S1) the Story 003 test file uses the shared `StubNpcSessions` (its private nested copy is removed). (S2) remark on `EnhancementAttemptStart.Rejected`: `Outcome` holds the enum's zero value (`Success`) and means nothing on a rejection.
- Test file: 28 `[Test]` + 4 `[TestCase]` rows = 32 cases.
- **Test runs after the fixes:** first run 1707/1708 — the one failure was the known flaky wall-clock test `TickLoop_CommitBeforeBroadcast_Tests.Execute_PersistenceWriteTakes200ms_BroadcastNotInvokedBeforeAtLeast200msElapsed` (measured 199 ms against a 200 ms floor; already logged as TD-050; Networking code untouched by this story). Second run, no changes in between: 1708/1708.
- **Uncommitted:** `EnhancementService.cs`, `IInventoryService.cs` (doc comments), new `EnhancementAttemptStart.cs` / `EnhancementAttemptResult.cs` (+ `.meta`); new AttemptSequence test file and `EnhancementTestDoubles.cs` (+ `.meta`), the Story 003 test file; Stories 004 and 005; Enhancement `EPIC.md`; this file.
- Next: `/story-done production/epics/enhancement-system/story-004-attempt-sequence.md`.

## Session Extract — /dev-story 2026-10-07 (Enhancement Story 004 implemented — 1707/1707 EditMode, not reviewed, not committed)

- Story: `production/epics/enhancement-system/story-004-attempt-sequence.md` — Attempt Sequence and Outcome Resolution. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- Two `gameplay-programmer` agents in parallel against one API contract (src / tests). No orchestrator edits.
- Source: `EnhancementService.cs` (fifth ctor arg `System.Random`; real `IsAttemptInProgress`; `BeginAttempt`, `CompleteAttempt`, private `ApplyOutcome`; `Dictionary<CharacterID, PendingAttempt>` with a private nested `readonly struct`); new `EnhancementAttemptStart.cs`, `EnhancementAttemptResult.cs`; `IInventoryService.cs` — doc comments of `ConsumeItem` and `RemoveItem` corrected (no behaviour change).
- Tests: new `Enhancement_AttemptSequence_integration_tests.cs` (27 `[Test]` + 4 `[TestCase]` rows = 31 cases); new `EnhancementTestDoubles.cs` (`ScriptedRandom`, `StubNpcSessions`, `RecordingInventoryDecorator` forwarding all 22 methods and 2 events); `Enhancement_AttemptValidation_integration_tests.cs` — five constructor call sites gain `new System.Random(0)`, one test renamed. Four `.meta` files generated by the run.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1707/1707 (was 1676).
- For `/code-review`: an exception thrown between `LockSlot` and the pending record (e.g. from an `OnInventoryChanged` subscriber during `ConsumeItem`) would leave the item slot locked with nothing pending — only the `SetEnhancementLevel == false` path unlocks; `Outcome` on a rejected `EnhancementAttemptStart` reads `Success` (enum default 0) although it is documented as meaningless; the Story 003 test file keeps its own private `StubNpcSessions` next to the shared one.
- Next: `/code-review` on the changed files, then `/story-done`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 004 — READY after a rewrite; commit-seam decision; not committed)

- Story 003 (extracts below) is committed and pushed as `18c76a5`; their "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-004-attempt-sequence.md` — Attempt Sequence and Outcome Resolution. First verdict NEEDS WORK: the commit seam decides the story's API (ADR-006 says every persistence call is `async Task<>`; no ADR says where game logic resumes; the existing `CommitBeforeBroadcastSequencer` is synchronous).
- **User decision — two-phase, synchronous service:** `BeginAttempt(charId, itemSlot, scrollSlot)` does steps 2–6a and returns a rejection or a pending attempt; the caller commits; `CompleteAttempt(charId)` unlocks and returns the result. The service never calls persistence or sees a `Task`. `IsAttemptInProgress` is true while an attempt is pending.
- **Assistant's refinement of the chosen sketch (told to the user):** the failed-commit path is a separate `RollBackAttempt(charId)` in Story 005, not a `bool committed` flag on `CompleteAttempt` (the preview the user picked showed the flag).
- **User decision:** if `SetEnhancementLevel` fails after the scroll is consumed → unlock, no pending attempt, throw `InvalidOperationException`; scroll not restored.
- **Applied:** Story 004 rewritten (design-decision section, 12 criteria, notes, QA cases). AC-ENH-8 moved here from Story 005. The Story 003 test fixture is in the file list (constructor gains a `System.Random`). Two stale doc comments in `IInventoryService.cs` (`ConsumeItem`, `RemoveItem`) are to be corrected by this story (doc-only).
- **Propagated:** Story 005 has a dated note at the top of its Implementation Notes (its criteria must be rewritten at its readiness: commit orchestrator still open, `RollBackAttempt`, AC-ENH-8 moved out); Enhancement `EPIC.md` story table row 005 and the open-points line.
- Checked in code: `UnlockSlot` on an unlocked or cleared slot is a documented silent no-op; `ConsumeItem` skips locked stacks and fails without mutating; `RemoveItem` clears the slot and its lock.
- **Uncommitted:** Stories 004 and 005, Enhancement `EPIC.md`, this file.
- Next: `/dev-story production/epics/enhancement-system/story-004-attempt-sequence.md`.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 003 — COMPLETE WITH NOTES; Enhancement epic 3/10; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-003-attempt-validation.md` — Attempt Validation and Result Codes. 12/12 criteria; test file 37 cases; EditMode 1676/1676.
- Enhancement `EPIC.md`: In Progress (3/10); `production/epics/index.md` row updated.
- Tech debt logged: None.
- **Uncommitted since `c242009`:** `EnhancementService.cs`, `EnhancementResultCode.cs`, `EnhancementAttemptValidation.cs`, `INpcInteractionSessions.cs` (each + `.meta`); `tests/EditMode/Integration/EnhancementSystem/` (test file + `.meta`) and `tests/EditMode/Integration/EnhancementSystem.meta`; Story 003; Enhancement `EPIC.md`; `production/epics/index.md`; this file.
- Next recommended: Story 004 — Attempt Sequence and Outcome Resolution (`production/epics/enhancement-system/story-004-attempt-sequence.md`, Integration, 4h). Points for its readiness: it declares the consumer-side commit interface whose shape Story 005 still has open (async `Task<CharacterSaveResult>` vs the synchronous `CommitBeforeBroadcastSequencer`) — so the commit-seam decision is effectively needed at Story 004; what happens if an inventory call fails after the scroll was consumed; `UnlockSlot` behaviour on an already-cleared slot after a destruction. Stories 006 (NPC session) and 008 (scroll source scan) are also unblocked.

## Session Extract — /dev-story + /code-review 2026-10-07 (Enhancement Story 003 implemented — 1675/1675 EditMode; review APPROVED WITH SUGGESTIONS, suggestions not yet applied; not committed)

- Story: `production/epics/enhancement-system/story-003-attempt-validation.md` — Attempt Validation and Result Codes. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- Two `gameplay-programmer` agents in parallel against one API contract (src / tests). No orchestrator edits.
- Files created (src/Foundation/EnhancementSystem): `EnhancementResultCode.cs`, `INpcInteractionSessions.cs`, `EnhancementAttemptValidation.cs`, `EnhancementService.cs` (ctor with four injected dependencies, `IsAttemptInProgress` — always false until Story 004, `internal ValidateAttempt` with private `CheckItem` / `CheckScroll` / `IsInRange`). Tests: `tests/EditMode/Integration/EnhancementSystem/Enhancement_AttemptValidation_integration_tests.cs` (30 `[Test]` + 6 `[TestCase]` rows = 36 cases; namespace `IronGrind.Tests.EditMode.Integration.EnhancementSystem`). `.meta` files generated by the run (five files + the new test folder).
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1675/1675 (was 1639).
- `/code-review` (lean self-review, requested by the user while the agents were running, done once the suite passed): no required changes. Suggestions, awaiting the user's answer: (S1) `EnhancementAttemptValidation` is `public` but only an `internal` method returns it — make it `internal` until a public API needs it; (S2) the `EnhancementService` class summary says "Story 004 adds the commit" — Story 004 adds the attempt sequence, Story 005 the commit; (S3) no test pins `>=` for the max-level check (an item seeded above the cap).
- **Update (same day): the user said "fix all" — S1–S3 applied by the orchestrator:** `EnhancementAttemptValidation` is now `internal`; the class summary names Story 004 (sequence) and Story 005 (commit); new test `ValidateAttempt_SwordAboveMaxLevel_RejectedAtMaxLevel`. Test file: 31 `[Test]` + 6 `[TestCase]` rows = 37 cases. EditMode 1676/1676.
- Next: `/story-done production/epics/enhancement-system/story-003-attempt-validation.md`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 003 — READY after five fixes, not committed)

- Story 002 (extracts below) is committed and pushed as `c242009`; their "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-003-attempt-validation.md` — Attempt Validation and Result Codes. First verdict NEEDS WORK: five cases with no GDD code; position of the "attempt in progress" check; no observable for a valid request; slot indices must be range-checked before inventory reads (`GetSlot` / `IsSlotLocked` log an error out of range); enum encoding.
- **User decisions (approved the recommendations):** out-of-range item slot → `RejectedItemNotFound`, scroll slot → `RejectedScrollNotFound`; same slot twice needs no special rule; non-equipment item (no `EquipmentData`) → `RejectedNotUpgradeable` even if marked upgradeable; unresolvable item → `RejectedItemNotFound` + one server error; unregistered character → `RejectedItemNotFound`. Check order: NPC session → attempt in progress for the player → item exists → slot unlocked → upgradeable → not accessory → below max → scroll exists → tier match.
- **Applied to Story 003:** the decisions as criteria; `internal ValidateAttempt(...)` returning a `readonly struct` (`IsValid`, `RejectionCode`, `ItemId`, `CurrentLevel`, `ScrollItemId`); range-check note; `EnhancementResultCode : byte` in GDD order, values provisional until TD-046; nine QA cases added.
- Checked: the Item Database validator has no rule tying `IsUpgradeable` to equipment (MVP data sets it true for all 28 equipment records, false for potions and scrolls); `ItemDefinitionBuilder.Build` in the tests supports `equipmentData`, `isUpgradeable` and `scrollData`; the test assembly sees internals.
- **Uncommitted:** Story 003 and this file.
- Next: `/dev-story production/epics/enhancement-system/story-003-attempt-validation.md`.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 002 — COMPLETE; Enhancement epic 2/10; not committed)

- Verdict: COMPLETE. Story: `production/epics/enhancement-system/story-002-enhancement-bonus-provider.md` — Enhancement Bonus Provider. 9/9 criteria; test file 42 cases; EditMode 1639/1639.
- Enhancement `EPIC.md`: In Progress (2/10); `production/epics/index.md` row updated.
- Tech debt logged: None.
- **Uncommitted since `0e000d1`:** `IEnhancementBonusProvider.cs`, `EnhancementBonusProvider.cs` and the BonusProvider test file (each + `.meta`); Story 002; Enhancement `EPIC.md`; `production/epics/index.md`; this file.
- Next recommended: Story 003 — Attempt Validation and Result Codes (`production/epics/enhancement-system/story-003-attempt-validation.md`, Integration, 4h). Open points for its readiness: no code named for an out-of-range slot index, the same index for both slots, a non-equipment item in the item slot, an unresolvable item definition, an unregistered character. Story 008 (scroll source scan, 1h) is independent. The Equipment epic now has its bonus provider but still needs the OQ-EQS-9 decision before `/create-stories equipment-system`.

## Session Extract — /dev-story + /code-review 2026-10-07 (Enhancement Story 002 implemented — 1638/1638 EditMode; review APPROVED WITH SUGGESTIONS, suggestions not yet applied; not committed)

- Story: `production/epics/enhancement-system/story-002-enhancement-bonus-provider.md` — Enhancement Bonus Provider. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- One `gameplay-programmer` agent (source and tests against a fixed API contract). No orchestrator edits.
- Files created: `src/Foundation/EnhancementSystem/IEnhancementBonusProvider.cs`, `EnhancementBonusProvider.cs`; `tests/EditMode/EnhancementSystem/EnhancementSystem_BonusProvider_tests.cs` (14 `[Test]` + 27 `[TestCase]` rows = 41 cases). Three `.meta` files generated by the run.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1638/1638 (was 1597).
- `/code-review` (lean self-review, requested by the user while the agent was still running, done once the suite passed): no required changes. Suggestions, awaiting the user's answer: (S1) unused `using System.Collections.Generic;` in the test file; (S2) no test proves the elemental ceiling is read from the config (a custom-ceiling case); (S3) the test fixture rebuilds the default config by hand — use `EnhancementConfig.Default` for the default cases.
- **Update (same day): the user said "fix all" — S1–S3 applied by the orchestrator** (test file only): unused import removed; new `GetElementalBonus_CustomCeiling_ClampsToTheConfigValue`; `BuildProvider()` uses `EnhancementConfig.Default`, and `BuildCustomProvider(ironBonusPerLevel, elementalDamageCeiling)` copies the default config for the two config-driven tests. Test file: 15 `[Test]` + 27 `[TestCase]` rows = 42 cases. EditMode 1639/1639.
- Next: `/story-done production/epics/enhancement-system/story-002-enhancement-bonus-provider.md`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 002 — READY after three fixes, not committed)

- Story 001 (extracts below) is committed and pushed as `0e000d1`; their "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-002-enhancement-bonus-provider.md` — Enhancement Bonus Provider. First verdict NEEDS WORK: the invalid-input behaviour the story had deferred; `baseFlatBonus` is `int` in the GDD but `StatModifierEntry.FlatBonus` is `float` in code; a private `MaxElementalDamage = 9_999` already exists in `ItemDefinitionValidator`.
- **User decision (approved the recommendation):** level outside [0, cap] → `ArgumentOutOfRangeException`; `GearTier.None` / undefined → `ArgumentOutOfRangeException`; base not validated; a non-weapon elemental call returns 0 before any check.
- **Applied to Story 002:** new criterion, three implementation notes, four QA cases.
- **Not changed (offered, the user did not ask for it):** Story 001's note and the `ELEMENTAL_DAMAGE_CEILING` doc comment still say no Item Database constant exists in `src/` — it does, privately in the validator.
- **Uncommitted:** Story 002 and this file.
- Next: `/dev-story production/epics/enhancement-system/story-002-enhancement-bonus-provider.md`.

## Session Extract — /story-done 2026-10-07 (Enhancement Story 001 — COMPLETE WITH NOTES; Enhancement epic 1/10; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/enhancement-system/story-001-enhancement-config.md` — Enhancement Config. 9/9 criteria; test file 96 cases; EditMode 1597/1597.
- Enhancement `EPIC.md`: In Progress (1/10); `production/epics/index.md` row updated.
- Tech debt logged: None.
- **Uncommitted since `a8a3845`:** `src/Foundation/EnhancementSystem/` (4 files + `.meta`) and `src/Foundation/EnhancementSystem.meta`; `tests/EditMode/EnhancementSystem/` (1 file + `.meta`) and its folder `.meta`; Story 001; Enhancement `EPIC.md`; `production/epics/index.md`; this file.
- Next recommended: Story 002 — Enhancement Bonus Provider (`production/epics/enhancement-system/story-002-enhancement-bonus-provider.md`, Logic, 2h, depends only on 001; the Equipment epic needs it). Its open point for readiness: behaviour for a level outside the range, a negative base, or `GearTier.None` (note `EnhancementConfig.GetBonusPerLevel` already throws for `None`). Story 003 (attempt validation) is also unblocked.

## Session Extract — /code-review 2026-10-07 (Enhancement Story 001 — APPROVED WITH SUGGESTIONS → "fix all" applied, 1597/1597 EditMode, not committed)

- Lean self-review in the dev-story session (no specialists spawned). No required changes; five suggestions, all applied by the orchestrator. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (S1) swapped tuning-knob references corrected — glow threshold is TK-ENH-4, mid threshold is TK-ENH-5 (constant docs, parameter and property docs, one validation message); (S2) `Default` is built by a private `CreateDefault()` with the F-ENH-4 table as a local, so it no longer depends on static declaration order; (S3) the eight per-tier defaults moved to `EnhancementConstants` (`BONUS_PER_LEVEL_*`, `ELEMENTAL_BONUS_PER_LEVEL_*`); (S4) the range message uses the two probability constants; (S5) new NaN-rejection test, and the expected-scrolls test covers all ten F-ENH-5 rows.
- Test file: 23 `[Test]` methods + 73 `[TestCase]` rows = 96 cases. EditMode 1597/1597 (Unity 6000.3.10f1 batch mode).
- Left as is: the 14-argument positional constructor (only `Default` and tests call it); defaults are constants in code, as in the Loot Table and Leveling systems.
- **Uncommitted:** `src/Foundation/EnhancementSystem/` (4 files + `.meta`, folder `.meta`), `tests/EditMode/EnhancementSystem/` (1 file + `.meta`, folder `.meta`), Story 001, this file.
- Next: `/story-done production/epics/enhancement-system/story-001-enhancement-config.md`.

## Session Extract — /dev-story 2026-10-07 (Enhancement Story 001 implemented — 1590/1590 EditMode, not reviewed, not committed)

- Story: `production/epics/enhancement-system/story-001-enhancement-config.md` — Enhancement Config. Status still Ready (`/story-done` not run). Same session as the readiness extract below.
- One `gameplay-programmer` agent (source and tests against a fixed API contract). One fix by the orchestrator: the null-table `ArgumentNullException` names the constructor parameter `successProbabilities`.
- Files created: `src/Foundation/EnhancementSystem/` (new folder) — `EnhancementConstants.cs`, `PrestigeBand.cs`, `EnhancementOutcome.cs`, `EnhancementConfig.cs`; `tests/EditMode/EnhancementSystem/EnhancementSystem_Config_tests.cs` (22 `[Test]` methods + 67 `[TestCase]` rows = 89 cases). `.meta` files for both folders and all five files were generated by the run and must be committed.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1590/1590 (was 1501).
- For `/code-review`: validation also rejects a NaN probability (not in the story); per-tier bonus values and the elemental ceiling are not validated (the GDD gives no rule); `Default` depends on static initialisation order (the default table is declared above it); the four per-tier defaults are private constants in `EnhancementConfig`, not in `EnhancementConstants`.
- Next: `/code-review` on the five files, then `/story-done`.

## Session Extract — /story-readiness 2026-10-07 (Enhancement Story 001 — READY after four fixes, not committed)

- The ten stories of the extract below are committed and pushed as `a8a3845`; its "not committed" notes are superseded.
- Story: `production/epics/enhancement-system/story-001-enhancement-config.md` — Enhancement Config. First verdict NEEDS WORK: safe ranges enforced inconsistently; "is rejected" not observable; band mapping above the cap unspecified; `ElementalDamage_ceiling` listed as an Enhancement value although the registry says Item Database owns it.
- **User decisions (approved the recommended options):** tuning-knob safe ranges are guidance, not hard validation — only the probability constraints, threshold ordering and table length are enforced; a level above the cap maps to `HIGH` without throwing.
- **Applied to Story 001:** the four fixes (criteria, implementation note on the ceiling's owner, QA cases). Checked and fine: defaults match `entities.yaml`; the F-ENH-5 table recomputed from the formula matches the GDD; manifest version matches; tests fall under the existing EditMode test assembly.
- **Uncommitted:** Story 001 and this file.
- Next: `/dev-story production/epics/enhancement-system/story-001-enhancement-config.md`.

## Session Extract — /create-stories 2026-10-07 (Enhancement System — 10 stories written, not committed)

- The two epics of the extract below are committed and pushed as `99d3afb`; its "not committed" notes are superseded.
- **Written:** `production/epics/enhancement-system/story-001` to `story-010` (8 Ready, 2 Blocked); `EPIC.md` (Stories table, AC coverage 30 of 39, open points, Status "Ready (0/10)"); `production/epics/index.md` row. Lean mode — QL-STORY-READY skipped; QA test cases written by the assistant.
- Stories: 001 config / probability table / prestige band (Logic, 3h); 002 bonus provider (Logic, 2h — the Equipment epic needs this first); 003 attempt validation (Integration, 4h); 004 attempt sequence (Integration, 4h); 005 commit and rollback (Integration, 5h); 006 NPC interaction session (Integration, 3h); 007 outcome events and +9 trigger (Logic, 2h); 008 scroll source scan (Logic, 1h); 009 attempt exclusivity — **Blocked on OQ-ENH-7**; 010 client requests and result delivery — **Blocked on TD-046**.
- **Correction made:** the Enhancement `EPIC.md` committed in `99d3afb` wrongly listed the scroll records as "not in code". They are (Item Database Stories 005–006). Fixed in `EPIC.md`. The "code follow-up story needed" notes in `enhancement-system.md` (header, Required Upstream Amendments item 4) and `systems-index.md` (rows 11 and 23) are stale and NOT yet corrected — a small design-doc cleanup.
- **Open points left for `/story-readiness`** (recorded in the stories and the epic): Story 005 commit seam — GDD commit is `Task<CharacterSaveResult>`, the existing `CommitBeforeBroadcastSequencer` is synchronous; Story 007 — `OnPrestigeBandChange` has no sensible owner in Enhancement; Story 006 — where the shared NPC session type lives, `npcId` meaning; Stories 002–004 — inputs with no GDD rule; Story 008 — whether production loot tables exist and whether a scroll is reachable through a consumable pool; Story 010 — no request triggers `EnhancementStateUpdate`.
- AC owned elsewhere: AC-ENH-1 (Inventory Story 010, done), AC-ENH-2 (Character Persistence), AC-ENH-15/16/17/32 (Equipment epic), AC-ENH-25/26/31 (Enhancement UI).
- **Uncommitted:** the ten story files, the Enhancement `EPIC.md`, `production/epics/index.md`, this file.
- Next: `/story-readiness production/epics/enhancement-system/story-001-enhancement-config.md` then `/dev-story`. Equipment epic still needs the OQ-EQS-9 decision before `/create-stories equipment-system`.

## Session Extract — /create-epics 2026-10-07 (Equipment + Enhancement — 2 epics written, not committed)

- Scope chosen by the user: Equipment System + Enhancement System. Lean mode — PR-EPIC skipped. TR registry still empty, so TR-IDs are placeholders.
- **Written:** `production/epics/equipment-system/EPIC.md` (Core, 11 TRs, Status Ready with gate OQ-EQS-9 before `/create-stories`); `production/epics/enhancement-system/EPIC.md` (Feature, 11 TRs); `production/epics/index.md` (two rows, a note, layer coverage, date).
- **User decision:** create the Enhancement epic now although its GDD header says OQ-ENH-7 and TD-046 should close before `/create-epics`. The epic records this; TR-enh-007 (OQ-ENH-7) and TR-enh-009 (TD-046) are blocked, TR-enh-005/006/008/010 affected. Unblocked now: TR-enh-001 to 003 (bonus provider, level range and thresholds, probability table).
- Other blockers recorded in the Enhancement epic: Item Database Amendment #4 scroll records are not in code (no story yet); Character Persistence has no epic (`SaveIrreversibleOutcome`).
- Open for `/create-stories`: how `IEnhancementBonusProvider` reaches the Equipment epic (Enhancement's provider story first, or a consumer-side stub).
- Not changed: stale rows in `production/epics/index.md` for Inventory ("9 stories Ready") and Loot Table ("Not yet created") — both epics are Complete.
- **Uncommitted:** the two new epic folders, `production/epics/index.md`, this file.
- Next: decide OQ-EQS-9 (design), then `/create-stories equipment-system`; `/create-stories enhancement-system` can run now for the unblocked formula stories. Gates to close for the rest: OQ-ENH-7 (likely `/architecture-decision`) and TD-046 (wire-protocol authoring session + lean re-review).

## Session Extract — /story-done 2026-10-07 (Inventory Story 010 — COMPLETE WITH NOTES; Inventory epic 10/10; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/inventory-system/story-010-enhancement-level.md` — Per-Slot Enhancement Level. 23/23 criteria; new test file 57 methods (60 cases); EditMode 1501/1501.
- `EPIC.md`: Status Complete (10/10); GDD AC coverage 23 of 24 (AC-INV-11 deferred to the Inventory UI epic).
- Tech debt logged: None.
- **Uncommitted since `7910d2a`:** six files in `src/Foundation/InventorySystem/`; the new EnhancementLevel test file + `.meta`; eleven fixture-only test files (nine inventory, `LootTestFakes.cs`, the loot bag-full recovery integration test); Story 010; inventory `EPIC.md`; this file.
- Next recommended: commit when the user asks. Then: no Ready story exists in any epic — Equipment and Enhancement are unblocked on the inventory side but have no epics yet (`/create-epics`; Equipment still has pre-implementation gate OQ-EQS-9); Authentication, Damage Calculation and Status Effects have an `EPIC.md` but no stories (`/create-stories`); Item Database needs a code follow-up story for Amendment #4 (scrolls). Design side: TD-056 (Loot Table GDD wording) and `/design-system` for Inventory UI (#30). PAT rotation still unconfirmed.

## Session Extract — /code-review 2026-10-07 (Inventory Story 010 — APPROVED WITH SUGGESTIONS → "fix all" applied, 1501/1501 EditMode, not committed)

- Lean self-review in the dev-story session (no `unity-specialist` / `qa-tester` spawned). No required changes; six suggestions, all applied by the orchestrator. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (S1) the two clamp tests pin the warning's slot, character and levels (`ExpectClampWarning`); (S2) new test — locked slot whose item the Item Database cannot resolve is rejected; (S3) two new tests — `SeedSlotForTesting` throws for a level on an empty slot or on quantity > 1; (S4) `InventorySlot.EnhancementLevel` docs keyed on `StackLimit`, not quantity; (S5) `TryClaimSnapshotSlot` extracted, `ApplySnapshotEntry` is 33 lines; (S6) the three `SeedSlotForTesting` doc links name the 5-argument overload.
- New test file: 57 methods, 60 cases. EditMode 1501/1501 (Unity 6000.3.10f1 batch mode).
- Left as is (explained to the user): an invalid level on `MoveItemIn` returns the same `Failed` value as an invalid item; a same-item move with an unresolvable stack limit is a no-op when levels match.
- **Uncommitted:** six files in `src/Foundation/InventorySystem/`, the new test file + `.meta`, eleven fixture files, the story file, this file.
- Next: `/story-done production/epics/inventory-system/story-010-enhancement-level.md` — closes the Inventory epic (10/10).

## Session Extract — /dev-story 2026-10-07 (Inventory Story 010 implemented — 1498/1498 EditMode, not reviewed, not committed)

- Story: `production/epics/inventory-system/story-010-enhancement-level.md` — Per-Slot Enhancement Level. Status still Ready (`/story-done` not run). Run in the same session as the readiness extract below (no `/clear`).
- Two `gameplay-programmer` agents in parallel against one API contract (src / tests). One fix by the orchestrator: removed a pointless `LockSlot` on an empty slot from the empty-slot rejection test.
- Files changed (src/Foundation/InventorySystem): `InventoryService.cs` (third ctor arg `byte maxEnhancementLevel`, `SetEnhancementLevel`, `ValidatePlacementLevel`, `MergeSlots` split out of `ExecuteMove`, `NormalizeSnapshotLevel`, 5-arg `SeedSlotForTesting`, level on `RecordSlotChange` / `ValidateSlotContents`), `IInventoryService.cs` (`SetEnhancementLevel`; `MoveItemIn` / `ForceInsert` take a level), `InventorySlot.cs`, `SlotChange.cs`, `InventorySnapshotEntry.cs`, `MoveItemOutResult.cs` (ctor is now `(ItemID, byte, MoveItemOutCode)`).
- Tests: new `tests/EditMode/InventorySystem/InventorySystem_EnhancementLevel_tests.cs` (54 methods, 57 cases, + `.meta` generated by the run); fixture updates in the nine inventory test files (`MaxEnhancementLevel` constant, third ctor arg, `, 0` on 29 `MoveItemIn` / `ForceInsert` calls), `LootTestFakes.cs`, `LootTable_BagFullRecovery_integration_tests.cs`. No existing assertion changed.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1498/1498 (was 1441).
- For `/code-review`: an invalid-level `MoveItemIn` returns `MoveItemInResult.Failed` (same value as an invalid item); a call with both an unknown item and an over-max level logs only the level error; same-item move with an unresolvable `StackLimit` now is a no-op when levels match (it used to swap) and still logs the lookup error; `ApplySnapshotEntry` is about 40 lines; no test asserts that the clamp warning names the character and slot (prefix-only regex); `ConsumeItem` emptying a slot has no level test (it only empties stacks); two `SeedSlotForTesting` overloads may make existing `<see cref>` doc links ambiguous (CS0419 warning).
- Next: `/code-review` on the changed files, then `/story-done` — closes the Inventory epic (10/10).

## Session Extract — /story-readiness 2026-10-07 (Inventory Story 010 — READY after three file-list fixes, not committed)

- Story: `production/epics/inventory-system/story-010-enhancement-level.md` — Per-Slot Enhancement Level. The last open story of the Inventory epic (9/10); unlocks the Enhancement and Equipment epics.
- First verdict NEEDS WORK — the story predates the Loot Table code: `RecordingInventoryService` in `tests/EditMode/LootTableSystem/LootTestFakes.cs` implements `IInventoryService` and was not in the file list; `LootTable_BagFullRecovery_integration_tests.cs` line 213 constructs `InventoryService` and was not in the list; "eight" existing inventory test files is nine.
- **Applied to the story (user approved):** the three fixes — QA Test Cases file list, Test Evidence line, the constructor note in Implementation Notes. No criterion or rule changed.
- Checked and fine: ADR-010 and ADR-006 (Amendment 1) Accepted; manifest version 2026-06-28 matches; dependencies 001, 004–009 Complete; the implementation notes match the current `InventoryService.cs`. `TR-inv-004` is not in the registry (registry holds only commented examples — same as the earlier inventory stories).
- **Uncommitted:** the story file and this file.
- Next: `/dev-story production/epics/inventory-system/story-010-enhancement-level.md`. Other open items: TD-056 (Loot Table GDD wording pass + lean re-review); `/design-system` for Inventory UI (#30). PAT rotation still unconfirmed.

## Session Extract — /story-done 2026-10-03 (Story 012 — COMPLETE WITH NOTES; Loot Table epic 13/13; not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md` — Zone Teardown Loot Flush. 6/6 criteria; teardown test file 14 tests; EditMode 1441/1441.
- `EPIC.md`: Status Complete (13/13).
- Tech debt (user chose all three): TD-054 narrowed (partly done), TD-056 extended (teardown wording), TD-057 added (disposed auction service's unsubscription is not observable). Register: 52 items.
- **Uncommitted since `c789a05`:** the winner-grace design changes (GDDs, registry, review log, systems index); Stories 012 and 013 and `EPIC.md`; `docs/tech-debt-register.md`; `src/Foundation/LootTableSystem/` (8 modified files, new `LootTeardownCoordinator.cs` + `.meta`); loot tests (8 modified files, new AuctionWinnerGrace and ZoneTeardown test files + `.meta`); this file.
- Next recommended: commit when the user asks (PAT in the `origin` URL still unrotated — do not push before that). Then: the Loot Table epic is done; open items are TD-056 (one GDD authoring pass + lean re-review) and the next epic or `/design-system` for Inventory UI (#30).

## Session Extract — /code-review 2026-10-03 (Story 012 — APPROVED WITH SUGGESTIONS → "fix all" applied, 1441/1441 EditMode, not committed)

- Lean self-review in the dev-story session (no specialists spawned). No required changes; suggestions 1–4 applied. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (S1) a party read that throws at teardown → item despawned and the fallback `OnAuctionResolved` raised once (`AbandonAuctionAtTeardown`); (S2) test for "every payment failed → round-robin, then despawn"; (S3) test that a throwing auction step does not stop `DespawnAll` / `Clear()`; (S4) `ResolveAllForTeardown` logs one warning when skipped on re-entry. Teardown test file: 14 tests.
- **Left for `/story-done`:** S5 — TD-054 is only partly done (four other loot test files keep their own `StubPartyService`; `KillResolution` keeps its own `GoldCall` / `RecordingCurrencyService`): narrow the register entry, do not close it.
- The teardown "nobody wins" split (full-bag pass-over → direct despawn; otherwise round-robin first) was the assistant's rule. **User decisions 2026-10-03 (after an explanation):** in normal play keep the current rule (per-bidder 30-second graces, then rotation member with a fresh 120 seconds); at zone shutdown resolve at once as Story 012 implements it — no waiting, no change. The user's stated intent: no bids → the rare item follows normal drop logic (CR-LT-10, already the rule). The split is therefore accepted as implemented and pinned by tests.
- Next: `/story-done production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md` — closes the Loot Table epic (13/13).

## Session Extract — /dev-story 2026-10-03 (Story 012 implemented — 1438/1438 EditMode, not reviewed, not committed)

- Story: `production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md` — Zone Teardown Loot Flush. Status still Ready (`/story-done` not run).
- Three `gameplay-programmer` agents: source and fixture move in parallel (suite 1427/1427 on that), then the teardown tests against the real code.
- Files changed (src/Foundation/LootTableSystem): new `LootTeardownCoordinator.cs` (+ `.meta`); `ILootAuctionService.cs` + `LootAuctionService.cs` (`ResolveAllForTeardown`); `IGroundItemService.cs` + `GroundItemService.cs` (`DespawnAll`); `ILootTableService.cs` (`Clear()` on the interface).
- Tests: new `tests/EditMode/Integration/LootTableSystem/LootTable_ZoneTeardown_integration_tests.cs` (11 tests, + `.meta`). TD-054 done: `FakeEquipmentItemDatabase`, `GoldCall`, `RecordingCurrencyService`, `MutablePartyService` moved to `LootTestFakes.cs`; the AuctionResolution (23) and AuctionWinnerGrace (22) files use them — the register entry is not yet marked resolved.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1438/1438 (was 1427).
- Rule set by the assistant in the brief, not confirmed by the user: at teardown, nobody wins + at least one bidder passed over for a full bag → item despawned directly, no round-robin; nobody wins otherwise (zero valid bids, or every spend failed) → CR-LT-10 round-robin first, then `DespawnAll`.
- For `/code-review`: a party read that throws during the flush leaves that auction unresolved with no `OnAuctionResolved` (`DespawnAll` still removes the item) — mirrors `Tick`; the disposal-edge test asserts only that the disposed auction service resolves nothing; `Clear()` is verified through a counting stub, not the real `LootTableService`.
- Next: `/code-review` on the changed files, then `/story-done` — the last story of the Loot Table epic.

## Session Extract — /story-readiness 2026-10-03 (Story 012 — READY after fixes, not committed)

- First verdict NEEDS WORK: `ILootTableService.Clear()` does not exist on the interface (class only); the reserved-bidder-has-room case at teardown was unspecified; the dependency line still called Story 013 "Ready".
- **User decisions:** at teardown the reserved grace bidder is tried first (`HasFreeSlot` read again: room → charged and wins; full → skipped); Story 012 first moves the shared auction test rig and fakes into `LootTestFakes.cs` (closes TD-054); estimate raised from 2 to 4 hours.
- **Applied to Story 012:** the three fixes, both decisions, one new QA edge case (grace bidder with a freed slot at teardown wins).
- Tech debt TD-054 / TD-055 / TD-056 were added to `docs/tech-debt-register.md` at the user's request (51 items).
- Next: `/clear`, then `/dev-story production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md` — the last story of the Loot Table epic.

## Session Extract — /story-done 2026-10-03 (Story 013 — COMPLETE WITH NOTES, 1427/1427 EditMode, not committed)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/loot-table-system/story-013-auction-winner-grace.md` — Auction Winner Grace. 10/10 criteria; grace test file 22 tests. `EPIC.md`: 12 Complete, 1 Ready (012).
- **User decision:** a bidder who frees a slot on the deadline tick itself is disqualified (deadline checked first) — pinned by `Tick_ASlotFreesOnTheDeadlineTick_AIsStillDisqualifiedAndBWins`. This closes the open S4 item in the extract below.
- Tech debt logged: TD-054 (duplicated auction test rig), TD-055 (unregistered bidder gets a grace), TD-056 (GDD wording items) — added at the user's request after the report.
- **Uncommitted since `c789a05`:** the winner-grace design changes (GDDs, registry, review log, systems index), Story 013 (new file) and Story 012 edits, `EPIC.md`, 7 files in `src/Foundation/LootTableSystem/`, 7 modified test files, the new grace test file and its `.meta`.
- Next recommended: Story 012 — Zone Teardown Loot Flush (`production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md`), Ready; run `/story-readiness` first — it needs a way to end a running grace (pending state is private to `LootAuctionService`).

## Session Extract — /code-review 2026-10-03 (Story 013 — CHANGES REQUIRED → "fix all" applied, 1426/1426 EditMode, not committed)

- Lean self-review in the dev-story session (no `unity-specialist` / `qa-tester` spawned). One required change, eight suggestions. This supersedes the "For `/code-review`" list in the extract below.
- **Applied:** (R1) `SubmitBid_AuctionWaitingInAGrace_ReturnsWindowClosed` now uses a received tick inside the bid window; (S1) invariant error text is "had a free slot but the pickup failed" (service + three resolution tests); (S2) `LootAuctionService.Tick` reads the due list in place — no per-tick array — and a re-entered `Tick` returns at once (`_ticking`); (S3) `PendingResolution.SlotCheckDue` keeps an inventory change until `HasFreeSlot` is read, new test `Tick_PartyReadThrowsOnTheTickAfterASlotFrees_AStillWinsOnTheNextTick`; (S7) unused test constants removed. Grace test file: 21 tests.
- **Open — needs the user's answer:** S4 — a bidder who frees a slot on the deadline tick itself is disqualified (expiry is checked first); the GDD does not say which wins and no test pins it.
- **Left:** S5 (unregistered bidder gets a grace — real `HasFreeSlot` returns false + error); S6 (≈230 lines of rig and fakes duplicated between the two auction test files — tech debt candidate); S8 (Story 012 needs a way to end a running grace; the pending state is private).
- Next: `/story-done production/epics/loot-table-system/story-013-auction-winner-grace.md`, then Story 012.

## Session Extract — /dev-story 2026-10-03 (Story 013 implemented — 1425/1425 EditMode, not reviewed, not committed)

- Story: `production/epics/loot-table-system/story-013-auction-winner-grace.md` — Auction Winner Grace. Status still Ready (`/story-done` not run).
- Two `gameplay-programmer` agents in parallel against one API contract (src / tests). One fix by the orchestrator: the `StubPartyService` in the AuctionResolution test file lacked `IsMemberConnected` (compile error on the first run).
- Files changed (src/Foundation/LootTableSystem): `LootAuctionService.cs` (resumable resolution, `PendingResolution`, fifth ctor arg `IInventoryService`), `ILootAuctionService.cs` (docs), `IGroundItemService.cs` + `GroundItemService.cs` (`freshPickupWindow` on `AssignAuctionItem` / `AwardAuctionItem`, new `RaiseAuctionGraceBlocked`), `GroundItemAssignedEventArgs.cs` (`ExpiryTick`), `IPartyService.cs` (`IsMemberConnected`), `LootTableConstants.cs` (`AUCTION_WINNER_GRACE_TICKS`).
- Tests: new `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionWinnerGrace_integration_tests.cs` (20 tests, + `.meta` generated by the run); `LootTestFakes.cs` (`FreeSlotByCharacter`); six `StubPartyService` copies; AuctionBids and AuctionResolution rigs (fifth ctor arg, `FreeSlot = true`); three Story 011 bag-full-winner tests renamed to the invariant path.
- Test run: Unity 6000.3.10f1 batch mode, EditMode 1425/1425 passed (was 1405).
- Deviation from the story text: the "had a free slot but the pickup found the bag full" case logs `Debug.LogError` (GDD States table) — the story's Implementation Notes say "one warning". Assistant's choice, not confirmed by the user.
- For `/code-review`: the invariant error also fires when the award fails for a reason other than a full bag (message would be inaccurate); `TryGetBid` returns false once an auction has closed into a grace; an exception during a grace removes the item (existing recovery rule); real `InventoryService.HasFreeSlot` returns false + logs an error for an unregistered character, so such a bidder with enough gold would get a grace.
- Next: `/code-review` on the changed files, then `/story-done`; then Story 012 (teardown must end a running grace — the pending state is private to `LootAuctionService`).

## Session Extract — /story-readiness 2026-10-03 (Story 013 — READY after two text fixes, not committed)

- Run in the same session as the review extract below (no `/clear`). First verdict NEEDS WORK: unconfirmed party query; stale "wait for the third re-review" header note.
- **User decision:** `bool IsMemberConnected(CharacterID characterId)` on the loot module's `IPartyService` — false when the member's status is `Ghost`. Estimate stays 4 hours (the assistant thinks 5–6 is more likely).
- **Applied to Story 013:** header note; query confirmed; `Ghost` wording in the disconnected AC and close sequence; new "Test fixtures" note (six `StubPartyService` copies gain the method; `RecordingInventoryService.FreeSlot` needs a per-character override; `LootAuctionService` gains an `IInventoryService` constructor argument).
- **Next:** `/clear`, then `/dev-story production/epics/loot-table-system/story-013-auction-winner-grace.md`, then Story 012. The GDD wording fixes R1–R4 of re-review #3 are still open.

## Session Extract — /design-review 2026-10-03 (Loot Table, lean re-review #3 — APPROVED, 0 blocking, not committed)

- Fresh session after `/clear`. Both blockers of re-review #2 confirmed closed ("disconnected" source and bid-time-only check; `GroundItemAssigned.expiryTick`). No primitive gaps.
- **Written:** `systems-index.md` row 14 → Approved; `loot-table-system.md` header Status → Approved; review log entry appended. No rule text changed, no registry change.
- **This supersedes** the "Next: third lean pass" notes in the extracts below. Story 013's "do not start until that re-review passes" condition is now met.
- **Recommended, for a later authoring pass (none blocking):** R1 — `Disconnected` is not a `PartyMemberStatus` value (enum is `Online, Ghost, OutOfZone, Dead`; inside the 5-second window a member is `Online`), so CR-LT-9.1, the disconnect edge case, the Interactions row and AC-LT-25 should say `Ghost` only — this corrects the "party-system.md `MemberStatus` comment omits `Disconnected`" note below; R2 — no edge case for a party disband during an open auction or a grace (Story 011 code not checked); R3 — `GroundItemAssigned` recipients differ across the wire protocol, the channel contract and the Loot Interactions row; R4 — the paid-winner invariant path should state that `GroundItemAssigned` is sent; R5 — hud.md CR-HUD-15 / Loot HUD row use `GroundItemAssigned` for common drops; R6 — party-facing grace signal (deferred by the user).
- **Next:** `/story-readiness` then `/dev-story` for Story 013, then Story 012. The design changes since `c789a05` are still uncommitted.

## Session Extract — /design-review 2026-10-03 (Loot Table, lean re-review #2 — NEEDS REVISION, 2 blocking → both revised in-session, not re-reviewed, not committed)

- Run in the same session as the authoring extract below (no `/clear`). The 3 prior blockers were confirmed closed. Two new blockers: (1) "disconnected" had no defined source and a network blip during a grace cost the item; (2) the client could not learn the fresh `expiryTick` — `GroundItemAssigned` did not carry it.
- **User decision:** disconnected = party member status `Disconnected` / `Ghost`, checked only when a full-bag bid is tried; a running grace is not ended by a disconnect. This **supersedes** the two "assistant additions, NOT confirmed" in the extract below (mid-grace disconnect rule and the `NotifyClientDisconnected` push — both withdrawn).
- **Applied:** both blockers and R1–R5 — `loot-table-system.md` (CR-LT-9.1, CR-LT-12, CR-LT-15, `Claiming` row, Interactions, Dependencies, F-LT-2 `N` ∈ [1, 4], two edge cases, bid window trigger, AC-LT-25, header); `networking-wire-protocol.md` (`GroundItemAssigned` + `uint expiryTick`, body 12 bytes / 22 standalone); `networking-channel-contract.md`; `inventory-system.md` and `currency-system.md` headers; `entities.yaml` grace note; Story 013 (party status query `IsMemberConnected` — name is the assistant's; `ExpiryTick` on `GroundItemAssignedEventArgs`); review log (review entry + amendment entry); `systems-index.md` note (still Needs Revision).
- Propagation check done: no stale mid-grace disconnect wording, no `AuctionOpen`, no other reference to the old `GroundItemAssigned` size; no serializer for that message exists in `src/` yet (only the event args).
- **Left for a later pass (other documents):** party-system.md `MemberStatus` comment omits `Disconnected`; party-system.md does not list the Loot Table status read; hud.md CR-HUD-15 expects `GroundItemAssigned` for common drops, which only get `GroundItemSpawned`. Deferred by the user: party-facing grace signal.
- **Next:** `/clear`, then `/design-review design/gdd/loot-table-system.md --depth lean` (third pass on the amendment). On a pass: `/story-readiness` and `/dev-story` for Story 013, then Story 012.

## Session Extract — authoring 2026-10-03 (Loot Table winner-grace revision — blockers 1–3 and R2–R8 applied, not re-reviewed, not committed)

- This supersedes the "Next" and "user decision needed" notes in the review extract directly below.
- **User decisions:** balance read (`GetBalance`) before a grace — below the bid → disqualified at once; `N` = party size on the paying tick; no wire change — the client picks the discard modal mode from the item state (`Auctioning` → grace mode); a disconnected full-bag bidder gets no grace; a fallback after any grace gets a fresh TTL; R1 (party-facing grace signal) deferred. The four earlier assistant additions are confirmed (range [200, 1200], leaver disqualified, CR-LT-13.1 does not extend a grace, no new state).
- **Files edited (uncommitted):** `design/gdd/loot-table-system.md` (CR-LT-9, CR-LT-9.1, CR-LT-12, state table, Interactions, F-LT-2, edge cases, Dependencies, discard modal ×2, AC-LT-25, footer 20/5/25, header → Needs Revision), `networking-channel-contract.md` (line 71), `networking-wire-protocol.md` (`BagFullPickupBlocked` and `AuctionResolved` notes), `inventory-system.md`, `currency-system.md`, `design/registry/entities.yaml` (grace knob note), `story-013-auction-winner-grace.md` (new ACs and QA cases: cannot afford, disconnected, pool on the paying tick, fresh TTL; close sequence; connection-state note), loot `EPIC.md` (TR-loot-012), the review log (new amendment entry), this file. `systems-index.md` unchanged (still Needs Revision).
- **Assistant additions, NOT confirmed:** a bidder who disconnects during a grace is disqualified on the next tick; Story 013 adds `NotifyClientDisconnected` / `NotifyClientConnected` to `ILootAuctionService` (no connection query exists in the loot services today).
- Propagation check done: `N` wording, tick wording, the `BagFullPickupBlocked` meaning and the fresh-TTL rule are consistent across the loot GDD, the wire protocol, the channel contract, entities.yaml and Story 013. hud.md, party-system.md and the message-criticality doc need no change. Not touched: the stale "None of the above GDDs exist yet" footnote and other "Not Started" rows in `currency-system.md` Downstream Dependents (outside this pass).
- Story 013 stays Ready but carries a "do not start until the re-review passes" note. Story 012 depends on 013.
- **Next:** `/clear`, then `/design-review design/gdd/loot-table-system.md --depth lean`; on a pass: `/story-readiness` and `/dev-story` for Story 013, then Story 012.

## Session Extract — /design-review 2026-10-03 (Loot Table System, lean — NEEDS REVISION, 3 blocking)

- Lean re-review of both 2026-10-03 amendments. Story 011 amendment: no findings. CR-LT-9.1 winner grace: **3 blockers, 8 recommended** — full list in `design/gdd/reviews/loot-table-system-review-log.md` (top entry).
- **Blockers:** (1) a grace is granted before affordability is known — a full-bag bidder who cannot pay discards an item and is then disqualified with `InsufficientFunds`; proposed `GetBalance` check before the grace — **user decision needed**; (2) `N` for the pool split: CR-LT-9.1 says paying tick, the leaver edge case and F-LT-2 say `windowCloseTick` — **user confirmation needed**; (3) `BagFullPickupBlocked` has two meanings with no written client rule (V/A discard modal still dismisses on radius exit; `networking-channel-contract.md` line 71 says "explicit pickup attempts only").
- The five unconfirmed assistant additions are still unconfirmed; the reviewer's view on each is in the log.
- Files written this session (uncommitted): the review log entry, `design/gdd/systems-index.md` (Loot Table → Needs Revision), this file. The GDD itself was not edited — its header still reads `Status: Approved (2026-05-17)`.
- **Story 013 is marked Ready but should not start until blocker 1 is decided** (it would implement the slot-check-before-spend order as written). Story 012 depends on 013.
- **Next:** authoring session — decide blockers 1 and 2, apply 1–3 (GDD + `networking-channel-contract.md` + Story 013 if the rule changes), triad + propagation check; then `/clear` and `/design-review design/gdd/loot-table-system.md --depth lean`.

## Session Extract — /story-readiness 2026-10-03 (Loot Table Story 012 — NEEDS WORK → gaps drafted and applied; new design rule CR-LT-9.1; new Story 013)

- Story 012 verdict: NEEDS WORK (written before Story 011's code existed): no owner for the flush across the three loot services, no public entry for teardown resolution, an untestable Disposal criterion, incomplete dispose notes, stale dependency line, and an undecided full-bag-winner case at teardown.
- **User decision:** a winner whose bag is full gets a short time to free a slot; if not, the next bid wins. Confirmed: applies to every auction close; grace = **600 ticks**; no grace at zone teardown (full-bag bidder skipped at once); the bid is debited only when delivery is possible (no refund path).
- **Applied (authoring, not re-reviewed, not committed):**
  - `design/gdd/loot-table-system.md`: new **CR-LT-9.1** (winner grace); CR-LT-9 last sentence; CR-LT-12 (fresh TTL no longer names a bag-full winner); `Auctioning` state row; teardown edge case; AC-LT-12 clause → new **AC-LT-25**; AC-LT-19 teardown lines; tuning knob `AUCTION_WINNER_GRACE_TICKS`; discard modal; header.
  - `design/registry/entities.yaml`: `AUCTION_WINNER_GRACE_TICKS = 600`, range [200, 1200].
  - `design/gdd/networking-wire-protocol.md`: `BagFullPickupBlocked` is also sent for a grace (`remainingTicks` = grace remaining); no schema change.
  - `design/gdd/reviews/loot-table-system-review-log.md`: amendment entry.
  - New `production/epics/loot-table-system/story-013-auction-winner-grace.md` (Ready, `TR-loot-012` placeholder, 4 h). Story 012 revised: `LootTeardownCoordinator`, `ILootAuctionService.ResolveAllForTeardown`, `IGroundItemService.DespawnAll`, dispose order, two new ACs and QA cases, Disposal criterion reworded, depends on 013. `EPIC.md`: 11/13, order 013 then 012. Story 011 completion notes: superseded-in-part note. TD-051 narrowed.
- **Assistant's additions, NOT confirmed by the user** (listed in the review log): safe range [200, 1200]; a bidder who leaves the party during their grace is disqualified at once; `N` for the pool is the party size on the paying tick; CR-LT-13.1 does not extend a grace; the item stays `Auctioning` (no new state). Also the fresh-TTL-for-a-bag-full-winner decision confirmed earlier today is now moot.
- Propagation check: `CR-LT-9` / bag-full-winner wording is cited only in loot-table-system.md, the wire protocol (updated) and currency-system.md (names the debit only — still correct). hud.md and inventory-system.md do not describe the auction winner's bag-full case.
- Not done: `systems-index.md` untouched (loot GDD status unchanged — Approved, amendments pending re-review). Story 013 code does not exist; Story 011's code still has the old bag-full-winner behaviour.
- Next: `/clear`, then `/design-review design/gdd/loot-table-system.md --depth lean` (two pending amendments); then `/story-readiness` and `/dev-story` for Story 013; then Story 012.

## Session Extract — /story-done 2026-10-03 (Loot Table Story 011 — COMPLETE WITH NOTES)

- Verdict: COMPLETE WITH NOTES. Story: `production/epics/loot-table-system/story-011-auction-resolution-and-gold-pool.md` — Auction Resolution and Gold Pool. 6/6 criteria covered by passing tests.
- **Real test run:** Unity 6000.3.10f1 batch mode (Editor was closed), EditMode: **1405/1405 passed**, no compile errors — includes the 23 resolution tests, the edited Story 010 file and the byte-9 `AuctionBid` `TestCase`. The "NOT compiled or run" notes in the two extracts below are superseded.
- User decisions at close: mark Complete; log tech debt; **confirmed** the fresh TTL for a bag-full winner on an `expiryTick` close (caveat removed from the story).
- Tech debt logged: 3 items — TD-051 (no refund when a paying winner gets no item), TD-052 (lost pool share of an unregistered member), TD-053 (four untested paths).
- Files updated: story-011 (Status, Test Evidence, Completion Notes), loot `EPIC.md` (11/12), `docs/tech-debt-register.md`.
- Uncommitted: everything from this session (Story 011 code, tests, three new `.meta` files, story, EPIC, tech-debt register, this file) plus the Currency amendment files listed further below. PAT rotation still unconfirmed.
- Next recommended: `/story-readiness` then `/dev-story` for `production/epics/loot-table-system/story-012-zone-teardown-loot-flush.md` (last story of the epic; must flush `Auctioning` items — `DespawnAuctionItem` exists). Pending separately: lean re-review of `design/gdd/loot-table-system.md` (decide TD-051 there); Currency wording cleanup.

## Session Extract — /code-review 2026-10-03 (Loot Table Story 011 — CHANGES REQUIRED → "fix all" applied, NOT compiled or run, not re-reviewed)

- Verdict was CHANGES REQUIRED (unity-specialist + qa-tester + main review): (1) a throw after the bids were taken stranded the item in `Auctioning` and could keep the winner's gold; (2) a bag-full winner standing on the item got `OnBagFullPickupBlocked` twice. User said "fix all": both required changes and every suggestion are applied. This supersedes "Known gaps" (1) and (3) in the extract below.
- Source changes: `LootAuctionService.ResolveAuction` split into `FindWinner` / `DeliverToWinner` / `RecoverFailedResolution` (on a throw: item goes to a winner who already paid, otherwise it is despawned; `OnAuctionResolved` is always raised; **no refund**); auction-level error logs for an undelivered item and an empty round-robin slot; `PayPool` takes `memberCount`; new `IGroundItemService.DespawnAuctionItem`; `AwardAuctionItem` sets the real "assignee inside" flag before the pickup; `ReassignAuctionRecord` clears `WindowCloseTick`; doc fixes in both interfaces (despawn sources, tick-order remark, construction order).
- Tests: `LootTable_AuctionResolution_integration_tests.cs` now 22 `[Test]` (was 13: + blocked-notice-once, 4 failure paths, two auctions on one tick, tie-break across a tick wrap, equal bids by arrival, `Tick` after `Dispose`; + expiry-tick / `WindowCloseTick` / pickup-count assertions on two existing tests). `LootTable_AuctionBids_integration_tests.cs`: + `DespawnAuctionItem_AuctionWithABid_…` (restores coverage of the bid cleanup on despawn), one test renamed.
- **Re-run of `/code-review` (same day): APPROVED WITH SUGGESTIONS, 0 required; user said "fix all" and the four suggestions are applied (not compiled, not re-reviewed):** `RecoverFailedResolution` can no longer throw, so `OnAuctionResolved` is always raised; doc fixes in `IGroundItemService.Tick` remarks and `AuctionResolvedEventArgs.IsRoundRobinFallback`; new test `Tick_PartyMembersReadThrows_NothingChangesAndTheAuctionResolvesOnTheNextTick` (resolution file now 23 `[Test]`). Still untested: a throwing `OnAuctionResolved` subscriber, `Tick` cleaning up an auction whose item is gone, `AssignAuctionItem` with an invalid assignee.
- **Open user decisions (unanswered):** refund the bid when the winner's pickup throws or a step fails after payment (currently: no refund, documented); an unregistered party member's pool share is lost (not gold-neutral on that server-fault path); the bag-full winner's fresh TTL on an expiry close is still the assistant's extension.
- Still out of scope: Story 012 must flush `Auctioning` items at teardown (it can use `DespawnAuctionItem`); `ConcurrencyConflict` cannot be forced with the real `CurrencySystem`.
- Next: run the EditMode suite in the Editor; lean re-run of `/code-review`; `/story-done` for Story 011.

## Session Extract — /dev-story 2026-10-03 (Loot Table Story 011 — implemented, NOT compiled or run)

- Story: `production/epics/loot-table-system/story-011-auction-resolution-and-gold-pool.md` — Auction Resolution and Gold Pool. Status still Ready (closing is `/story-done`'s job).
- Files changed: `src/Foundation/LootTableSystem/LootAuctionService.cs`, `ILootAuctionService.cs`, `GroundItemService.cs`, `IGroundItemService.cs`; new `AuctionResolvedEventArgs.cs`, `GroundItemAssignedEventArgs.cs` (their `.meta` files do not exist yet — Unity creates them on next Editor open; commit them); `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionBids_integration_tests.cs` (4-argument constructor; two expiry tests rewritten because an `Auctioning` item no longer despawns at expiry).
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionResolution_integration_tests.cs` — 13 `[Test]`. **Not executed**; nothing compiled.
- Design calls made by the assistant (not in the story/ADR; confirm at `/code-review`): resolution lives in `LootAuctionService.Tick` (new `ICurrencyService` dependency; tracks open auctions via `OnGroundItemSpawned`); `GroundItemService.ExpireItems` skips `Auctioning` items and gains `AssignAuctionItem` / `AwardAuctionItem` + `OnGroundItemAssigned`; fallback with no member at the round-robin cursor despawns the item with an error; `AuctionResolvedEventArgs` carries `PartyId`.
- Known gaps: (1) if a party-service call throws after the bids are taken, the item stays `Auctioning` and untracked until zone teardown; (2) an auction item is only cleaned up if the tick loop calls `LootAuctionService.Tick` — Story 012 must flush `Auctioning` items; (3) no test for a throwing pickup in `AwardAuctionItem` or for the tie-break across a tick wrap (not in the story's QA list); (4) the bag-full-winner fresh TTL is still the assistant's extension of the user's decision.
- Blockers: None.
- Next: run the EditMode suite in the Editor (also covers the still-unexecuted byte-9 `TestCase`); `/code-review` on the files above; `/story-done` for Story 011; then Story 012.
- Uncommitted: all of the above plus everything listed in the extract below. PAT rotation still unconfirmed.

## Session Extract — /design-review 2026-10-03 (Currency System, lean — APPROVED)

- Verdict: APPROVED, 0 blocking, 8 recommended. The `AuctionBid = 9` amendment is verified consistent across currency-system.md, loot-table-system.md, networking-wire-protocol.md and `entities.yaml`. Review log entry appended; `systems-index.md` not touched (status unchanged, user choice).
- Recommended cleanup in currency-system.md (wording only, no design decision): (1) NPC Shop Interactions row still says "Inventory System calls `AddGold(ItemSell)`"; (2) `AdminAdjust` → `CompensatingRefund` in that row and EC-CS-5; (3) Overview, "primary and only" faucet line and the sell-back invariant row are stale vs sell-back and the auction; (4) Rule 6, F-CS-1/F-CS-2 sources, EC-CS-9 do not name Loot Table as a caller; (5) Rule 6 refund wording vs the auction's no-refund bag-full path; (6) Downstream Dependents "Not Started" statuses, the "none exist yet" footnote and OQ-CS-3 (resolved by networking-core.md). The header still says "lean re-review pending".
- Outside currency-system.md: CR-LT-9 undefined for `ConcurrencyConflict`/`CharacterNotFound` (Story 011 open point, goes to `/story-readiness`); character-persistence.md `RespecRefund` does not exist (needs a user decision).
- **Code follow-up done in the same session (user choice):** `AuctionBid = 9` added to `src/Foundation/Currency/GoldTransactionReason.cs`; `WireEnumCodec.GoldTransactionReasonMaxNamedValue` → `AuctionBid`, comments `{0..9, 255}`; `TestCase` for byte 9 added in `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs`. **Not executed** — no Test Runner run this session. Story 011 → Ready; loot `EPIC.md` updated (10 Complete, 2 Ready).
- **`/story-readiness` Story 011 (same session): NEEDS WORK → gaps closed, now READY.** The three open points were decided by the user and written to loot-table-system.md and story-011: (a) a bid from a character who left the party is skipped at resolution (CR-LT-9, Edge Cases); (b) any non-`Success` `TrySpendGold` result disqualifies the bidder, with a server error logged for `CharacterNotFound`/`ConcurrencyConflict` (CR-LT-9); (c) an item `Assigned` out of an auction closed at `expiryTick` gets `expiryTick = resolutionTick + GROUND_ITEM_TTL_TICKS` (CR-LT-12). **Unconfirmed:** (c) was decided for the round-robin fallback; applying it to a winner with a full bag is the assistant's extension. Story-011 gained three test cases; no new GDD ACs; no registry change. Loot review log has an amendment entry ("not yet re-reviewed").
- Next: run the EditMode suite in the Editor (expect previous count + 1); `/dev-story` for Story 011 after `/clear`; then 012. Pending lean re-review: `design/gdd/loot-table-system.md`. The Currency wording cleanup above is a separate authoring session.
- Uncommitted: the amendment files, the three code/test files, story-011, `EPIC.md`, both review logs, this entry. PAT rotation still unconfirmed.

## Session Extract — authoring 2026-10-03 (Currency amendment: `GoldTransactionReason.AuctionBid = 9` — written, not committed, not re-reviewed)

- Goal: unblock Loot Table Story 011 at the design level. **User decisions:** one new value `AuctionBid = 9` for the auction winner's `TrySpendGold`; the pool share stays `MonsterDrop`; changeset "as listed" (the two other inconsistencies below stay untouched).
- Files changed: `design/gdd/currency-system.md` (gold sinks bullet; Loot Table rows of the Interactions and Downstream Dependents tables; `GoldSyncEvent` `Reason` list → pointer to the enum; header); `design/gdd/loot-table-system.md` (CR-LT-9; new Currency row in Interactions; Currency row in Dependencies; AC-LT-13; header — no rule change); `design/gdd/networking-wire-protocol.md` (enum block: `AuctionBid = 9`, and `CompensatingRefund = 8` which was missing there; header); `design/registry/entities.yaml` (`AuctionBid=9`, revised 2026-10-03); `design/gdd/reviews/currency-system-review-log.md` (amendment entry); story-011 and loot `EPIC.md` (block note: design done, code pending).
- `systems-index.md` not touched: no status changed (Currency and Loot Table stay Approved).
- Propagation check: every `GoldTransactionReason` mention under `design/` read. networking-core.md, npc-shop.md, enhancement-system.md, inventory review log: consistent, no enum value list to update.
- **Found, not fixed (need a decision or a separate pass):** (1) currency-system.md Interactions row for NPC Shop and EC-CS-5 say the compensating refund uses `AdminAdjust`; npc-shop.md, ADR-001 and the code use `CompensatingRefund`. (2) character-persistence.md (irreversible-outcome rule and AC-CP-28) cites `GoldTransactionReason.RespecRefund`, which is not in the enum, the registry or the code.
- **Story 011 is still Blocked — on code now:** add `AuctionBid = 9` to `src/Foundation/Currency/GoldTransactionReason.cs`; `WireEnumCodec.GoldTransactionReasonMaxNamedValue` → `AuctionBid` and its `{0..8, 255}` comments → `{0..9, 255}`; add a round-trip `TestCase` for byte 9 in `WireProtocol_BatchFraming_tests.cs`. No existing test pins byte 9 as unknown. Then set story-011 to Ready (its three open points still go to `/story-readiness`).
- Next: (1) `/design-review design/gdd/currency-system.md --depth lean` in a fresh session; (2) the code follow-up above; (3) `/story-readiness` + `/dev-story` for Story 011, then 012.
- Uncommitted: the files above plus the 2026-10-02 "Commit status (later)" note in this file. `main` is 1 ahead of `origin` (`5d7354a`). PAT rotation still unconfirmed.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 010)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-010-rare-drop-auction-open-and-bids.md` — Rare Drop Auction: Open, Bid Validation, Broadcast. Status: Complete; loot `EPIC.md` row 010 Complete, epic In Progress (10/12).
- Criteria 7/7. Evidence: `LootTable_AuctionBids_integration_tests.cs`, 32 methods / 32 cases (18 from implementation, 14 from the code review); EditMode 1380/1380.
- Tech debt logged: TD-048 extended (auction rules not in the GDD; `PartyID` reuse question; **interim: a closed auction is not resolved and despawns at its expiry tick — must not ship before Story 011**) — no new entry.
- **The loot epic cannot go further in code:** Story 011 (Auction Resolution and Gold Pool) is Blocked — `TrySpendGold` needs a `GoldTransactionReason` for the auction debit and none exists (amend currency-system.md + the wire enum + entities.yaml; that is a design authoring session, then a lean re-review). Story 012 (Zone Teardown Loot Flush) depends on 011. Story 011 also has three open points recorded at story creation (a bid from a member who left; non-`InsufficientFunds` errors; lifetime of an item reassigned at `expiryTick`) and the "For Story 011" list in story-010.
- Next recommended: (1) commit Stories 002–010 (nine stories uncommitted); (2) an authoring session for the Currency GDD amendment to unblock Story 011; or (3) another epic — authentication, damage-calculation and status-effects have no stories yet.
- **Uncommitted since `125e98f`:** Stories 002–010 code and tests, story-002..010 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049, TD-050), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 010 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-010-rare-drop-auction-open-and-bids.md` — Rare Drop Auction: Open, Bid Validation, Broadcast. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-010 unregistered — same accepted gap; no performance note; one open point). **User decision:** a new `LootAuctionService` owns the bids and `SubmitBid`; `GroundItemService` keeps the item's state, party and window-close tick and gains `SpawnAuction`. Also settled: the spawn and bid-update events carry `PartyId` instead of a member list (auction: `IsAuction` true, `AssignedTo` invalid); the window opens on the tick the item leaves `Spawning`; eligibility by `GetPartyID(bidder) == the auction's party`; `LootBidResult` codes, only the late bid logs (info); an auctioned drop takes no round-robin turn; until Story 011 a closed auction just despawns at its expiry tick.
- Files changed: new `src/Foundation/LootTableSystem/LootAuctionService.cs`, `ILootAuctionService.cs`, `LootBidResult.cs`, `LootBidUpdateEventArgs.cs`; `GroundItemService.cs` (`SpawnAuction`, shared `SpawnRecord`, `Spawning` → `Auctioning` with `WindowCloseTick`); `IGroundItemService.cs` (+ `SpawnAuction`); `GroundItem.cs` (+ `PartyId`, `WindowCloseTick`); `GroundItemSpawnedEventArgs.cs` (+ `PartyId`); `LootDropDistributor.cs` (party ≥ 2 Rare drop → `SpawnAuction`, cursor untouched); `LootTableConstants.cs` (+ `AUCTION_WINDOW_TICKS = 600`); story-010 file.
- Tests: new `tests/EditMode/Integration/LootTableSystem/LootTable_AuctionBids_integration_tests.cs` — 18 methods, 18 cases. **Three Story 006 tests rewritten on purpose** in `LootTable_RoundRobin_integration_tests.cs` (Rare drop in a party of 2+ now opens an auction and takes no turn): `..._RareDropInPartyOfTwo_OpensAuctionWithoutConsumingARoundRobinTurn`, `..._CommonRareCommonInPartyOfThree_AuctionsTheRareOneWithoutTakingATurn`, `..._TwoRareDrops_ReadsPartySizeOnceAndOpensTwoAuctionsWithoutAdvancing`.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): first run failed to compile — `ILootAuctionService.cs` lacked `using IronGrind.Currency;` (CS0246 on `CharacterID`); added by hand. Second run: 1366/1366 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success with one compile slip (above). `LootAuctionService.cs` and the changed parts of the distributor and ground service were read; the new test file was not read line by line (left to the qa-tester review).
- **/code-review (2026-10-02): CHANGES REQUIRED → all fixed (user pre-approved "fix all"; no item changed a gameplay rule).** unity-specialist: no blockers, 5 warnings (most are Story 011 needs); qa-tester: TESTABLE, no required gaps. Applied in code: bid floor is `max(1, SellPriceGold)` (a bid of 0 is never accepted); a raise moves the bidder to the end of the bid list (arrival order of current bids); `Dispose` clears the bids and a disposed service rejects bids; comment on the cache-miss fault; five stale doc comments fixed. Tests: floor read from a non-GDD price (120); zero-price item; window measured from a late opening tick; raise stores its own tick and the update carries the raised amount; validation order (late + non-member → `WindowClosed`; below floor + non-member → `NotPartyMember`; below floor + lower than own bid → `BelowFloor`; late on an `Assigned` item → `NotAuctioning`, no log); cache miss; invalid bidder; `receivedTick` before the opening tick (pinned as accepted); wrapped window-close tick; two auctions with separate bid books; one of two auctions expiring; auction stays `Auctioning` after its window closes; despawn event count; "no log" asserted on silent rejections; three Story 006 tests tightened.
- Not done: the distributor does not fall back to round-robin if `SpawnAuction` returns `Invalid` (cannot happen with a valid item and party; it logs). All Story 011 hooks the reviewer listed are recorded in the story file, not built.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1380/1380 passed, 0 compile errors. `LootTable_AuctionBids_Integration_Tests`: 32 methods, 32 cases.
- Not re-reviewed after the fixes. Story 010 file updated (code-review notes and a "For Story 011" list).
- For TD-048 at close: the events carry `PartyId` instead of a recipient list (representation only); bid of 0 never accepted; a bid tick before the opening tick is accepted; `PartyID` reuse within a zone session must be ruled out by the Party System GDD.
- Blockers: None.
- Next: `/story-done` for Story 010. Then the epic stops: Story 011 is Blocked (needs a `GoldTransactionReason` for the auction debit — currency-system.md amendment), Story 012 depends on 011.
- **Uncommitted since `125e98f`:** Stories 002–010 code and tests, story-002..010 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049, TD-050), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 009)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-009-ttl-pause-on-background.md` — TTL Pause on App Background. Status: Complete; loot `EPIC.md` row 009 Complete, epic In Progress (9/12).
- Criteria 6/6. Evidence: `LootTable_TtlPause_integration_tests.cs`, 28 methods / 28 cases; EditMode 1348/1348 (second run; first run had the flaky Networking test).
- Tech debt logged: TD-048 extended (pause rules not in the GDD, behaviours left as built, stale client expiry tick); **TD-050 new** (user: "Log it as tech debt") — flaky wall-clock assertion in `TickLoop_CommitBeforeBroadcast_tests.cs`. Total items 48.
- Next recommended: Story 010 — `production/epics/loot-table-system/story-010-*.md` (Rare Drop Auction — Open, Bid Validation, Broadcast; depends on 006, Complete). Carry-overs for it: replace the `partySize >= 2` branch of `LootDropDistributor.BeginRareDrop`; `Spawn` hard-codes `isAuction = false` (prefer a separate `SpawnAuction`); expiry must resolve an auction instead of despawning it; on reassignment reset the inside flag, the bag-full flag and the pause budget; consider `GetPartySize(PartyID)`. Story 011 stays Blocked (needs a `GoldTransactionReason` for the auction debit — currency-system.md amendment); Story 012 depends on 010–011.
- **Uncommitted since `125e98f`:** Stories 002–009 code and tests, story-002..009 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049, TD-050), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 009 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-009-ttl-pause-on-background.md` — TTL Pause on App Background. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-009 unregistered — same accepted gap; no performance note; two open points). **User decisions:** (1) expiry while backgrounded — hold the despawn while pause budget remains: a bag-full item whose assignee is backgrounded expires at `expiryTick + min(currentTick − backgroundedAtTick, pauseBudgetRemaining)`; the foreground booking is unchanged (resolves the story's "open design point"); (2) "bag-full item" = a new sticky flag set when a pickup fails on a full bag, kept after the assignee leaves the radius (separate from Story 008's in-radius `Blocked`). Also settled: a foreground tick older than the background tick extends nothing; `Dispose` clears the background records; no new warning mechanism.
- Files changed: `src/Foundation/LootTableSystem/GroundItemService.cs` (`NotifyClientBackgrounded`, `NotifyClientForegrounded`, record flag `BagFull`, `IsExpired` helper in the expiry step); `IGroundItemService.cs` (+ the two methods, one `Tick` remark); story-009 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_TtlPause_integration_tests.cs` — 14 methods, 14 cases (recording fake).
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1334/1334 passed, 0 compile errors, first run.
- Agent: `gameplay-programmer` — success. The new service code was read (`NotifyClient*`, `IsExpired`); the new test file was not read line by line (left to the qa-tester review).
- **/code-review (2026-10-02): CHANGES REQUIRED → all fixed (user: "Fix all").** unity-specialist: no blockers, 1 warning; qa-tester: GAPS (1 required). **User decision: a disconnect ends the pause** — new `NotifyClientDisconnected(CharacterID, uint)` books the pause so far like a foreground and clears the record (a record that outlived the session let a later background be measured from the old tick). Also applied: allow-list of item states (hold: `Assigned`; booking: `Assigned` / `Claiming`); docs (`GroundItem.PauseBudgetRemaining`, class summary, `Tick` remarks incl. order-independence and the per-assignment reset for Story 010); the hold test now has the assignee outside the radius (it would have passed on Story 008's `Blocked` flag before). New tests: first pause longer than the budget; same-tick foreground; two bag-full items with different budgets; item bag-full only after the background started (pins the GDD-literal behaviour); non-`InventoryFull` failure is not bag-full; delivery after an extension; no second warning when the new threshold is already past; foreground exactly at the budget; foreground after the held item despawned; hold keyed on the item's own assignee; four disconnect tests.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): first run 1347/1348 — the one failure was `TickLoop_CommitBeforeBroadcast_Tests.Execute_PersistenceWriteTakes200ms_BroadcastNotInvokedBeforeAtLeast200msElapsed` (Networking; `Thread.Sleep(200)` vs `Stopwatch` read 199 ms; file untouched this session); second run 1348/1348, 0 compile errors. `LootTable_TtlPause_Integration_Tests`: 28 methods, 28 cases, green in both runs. **That Networking test is flaky (wall-clock assertion) — not logged as tech debt yet; proposed to the user.**
- Not re-reviewed after the fixes. Story 009 file updated (code-review notes; the open design point marked resolved).
- For TD-048 at close: the hold-while-backgrounded rule, the sticky bag-full definition and "disconnect ends the pause" are user decisions not in CR-LT-13.1; the whole background period counts even before the item was bag-full (GDD-literal); a held item past its stored expiry gets no blocked notice; after an extension clients still hold the spawn message's `expiryTick`.
- Blockers: None.
- Next: `/story-done` for Story 009; then Story 010 (auction open and bids). Story 011 is Blocked; Story 012 depends on 010–011.
- **Uncommitted since `125e98f`:** Stories 002–009 code and tests, story-002..009 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 008)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-008-bag-full-recovery.md` — Bag-Full Recovery. Status: Complete; loot `EPIC.md` row 008 Complete, epic In Progress (8/12).
- Criteria 6/6. Evidence: `LootTable_BagFullRecovery_integration_tests.cs`, 28 methods / 28 cases (16 from implementation, 12 from the code review); EditMode 1320/1320.
- Tech debt logged: TD-048 extended (stackable-room gap of the retry gate; notice and warning rules not in the GDD) — no new entry.
- Next recommended: Story 009 — `production/epics/loot-table-system/story-009-*.md` (TTL Pause on App Background; dependency 008 Complete). Known open point from story creation: CR-LT-13.1 does not say what happens when `expiryTick` passes while the client is still backgrounded — raise at readiness. Story 010 (auction open and bids) is also ready; Story 011 is Blocked (needs a `GoldTransactionReason` for the auction debit); Story 012 depends on 010–011.
- **Uncommitted since `125e98f`:** Stories 002–008 code and tests, story-002..008 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 008 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-008-bag-full-recovery.md` — Bag-Full Recovery: Blocked Notice, In-Radius Retry, Expiry Warning. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-009 unregistered — same accepted gap; no performance note; one open point). **User decision:** the in-radius retry gate is the GDD's — the assignee's inventory changed **and** `HasFreeSlot` is true (known gap for TD-048: a full bag can gain room for a stackable drop without a free slot; no in-radius retry then). Also settled: logic stays in `GroundItemService`, now `IDisposable`; `IItemDatabase` injected for `DisplayName` (empty if unknown); blocked notice only on a failed proximity entry with `InventoryFull`, never on a failed retry, never on the expiry tick; warning on exact equality with `EXPIRY_WARNING_TICKS`, `Assigned` items only, after the pickup step; the inventory handler only records the character and never throws.
- Files changed: new `src/Foundation/LootTableSystem/BagFullPickupBlockedEventArgs.cs`, `GroundItemExpiryWarningEventArgs.cs`; `GroundItemService.cs` (4-argument constructor, `IDisposable`, `Tick` = snapshot changes → collect triggers → retries → entry pickups → expiry warnings → expiry; record flag `Blocked`); `IGroundItemService.cs` (+ two events, `Tick` doc); `LootTableConstants.cs` (+ `EXPIRY_WARNING_TICKS = 600`); `LootTestFakes.cs` (+ `EmptyItemDatabase`); constructor calls in the lifecycle, round-robin and proximity-pickup test files (+ a fourth null-guard assertion); story-008 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_BagFullRecovery_integration_tests.cs` — 16 methods, 16 cases, against the REAL `InventoryService` (bag filled with `SeedSlotForTesting`).
- Test run (Unity 6000.3.10f1 batch mode, EditMode): first run failed to compile — the new test file lacked `using IronGrind.Tests.EditMode.LootTableSystem;` (CS0246 on `SettablePositionProvider`); added by hand. Second run: 1308/1308 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success with one compile slip (above). `GroundItemService.cs` diff reviewed; the new test file was not read line by line (left to the qa-tester review).
- **/code-review `GroundItemService.cs` + new files (2026-10-02): CHANGES REQUIRED → all fixed (user pre-approved "fix all"; no item changed a gameplay rule).** unity-specialist: no blockers, 2 warnings; qa-tester: GAPS (4 required). Applied: (1) **re-entrancy defect** — a `Tick` re-entered from inside a retry's `Pickup` cleared the shared scratch list holding the outer tick's entry pickups; `Tick` now takes both lists into arrays (`TakeScratch`) before any `HasFreeSlot` / `Pickup` call (+ regression test; not run against the unfixed code); (2) name lookup moved out of the record enumeration and wrapped in try/catch (+ test with a throwing database); (3) `IGroundItemService : IDisposable`, `Dispose` doc says do not tick afterwards; (4) `Tick` doc: retries before entries, expiry is step 4. Tests: `RemainingTicks` asserted against `EXPIRY_TICK − ENTRY_TICK`; the retry test checks the item is still on the ground between the discard and the tick; the swap test asserts the swap raised one inventory event; **the gate is now proven on the recording fake** (settable `FreeSlot`, `RaiseInventoryChanged`): no free slot → `HasFreeSlot` asked once, no `Pickup`; other character → nothing asked; failed retry → stays blocked, no second notice, retries on the next change. New tests also for: `Claiming` suppression via a re-entrant `Tick` on the threshold tick; warning for a blocked item; two blocked items and one freed slot; a change raised during a tick kept for the next; discard then leave; disconnect clears blocked; second failed entry raises a second notice.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1320/1320 passed, 0 compile errors. `LootTable_BagFullRecovery_Integration_Tests`: 28 methods, 28 cases.
- Not re-reviewed after the fixes. Story 008 file updated (code-review notes).
- Not done: merging the three per-tick record enumerations (reviewer advised against it); `Tick` after `Dispose` is documented, not guarded.
- For TD-048 at close: the stackable-room gap of the GDD's free-slot retry gate (user decision to keep the GDD rule); blocked-notice and warning rules settled at readiness that the GDD does not state (no notice on a failed retry or on the expiry tick; warning for `Assigned` items only).
- Blockers: None.
- Next: `/story-done` for Story 008; then Story 009 (TTL pause on app background).
- **Uncommitted since `125e98f`:** Stories 002–008 code and tests, story-002..008 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 007)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-007-proximity-pickup-and-drop-fate.md` — Proximity Pickup and Bag-Full Drop Fate. Status: Complete; loot `EPIC.md` row 007 Complete, epic In Progress (7/12).
- Criteria 6/6. Evidence: `LootTable_ProximityPickup_integration_tests.cs`, 21 methods / 25 cases; EditMode 1292/1292.
- Tech debt logged: TD-049 (new, at code review); TD-048 extended (pickup rules not in the GDD; no "item picked up" client message).
- Next recommended: Story 008 — `production/epics/loot-table-system/story-008-bag-full-recovery.md` (Blocked Notice, In-Radius Retry, Expiry Warning; dependency 007 Complete). Known constraints: no `Pickup` from inside the `OnInventoryChanged` handler; `GroundItemService` becomes `IDisposable`; the shared fake needs a way to raise its events. Story 010 is also ready (depends on 006).
- **Uncommitted since `125e98f`:** Stories 002–007 code and tests, story-002..007 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048, TD-049), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 007 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-007-proximity-pickup-and-drop-fate.md` — Proximity Pickup and Bag-Full Drop Fate. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-009 unregistered — same accepted gap; no performance note; two open points). **User decisions:** (1) the pickup logic lives in `GroundItemService` (not a separate service); (2) if the assignee enters the radius on the expiry tick, the pickup is attempted before expiry. Also settled: pickup only from `Assigned` (first tick after spawn at the earliest); unknown position = outside; `Pickup` is never called while enumerating records; tests use shared fakes. Story updated (6 criteria, performance note, QA case).
- Files changed: new `src/Foundation/LootTableSystem/ICharacterPositionProvider.cs`; `GroundItemService.cs` (constructor now takes `IInventoryService` + `ICharacterPositionProvider`; `Tick` = `CollectPickupTriggers` → `ProcessClaims` → `ExpireItems`; record flag `AssigneeInside`); `LootTableConstants.cs` (+ `PICKUP_RADIUS_UNITS = 2.0f`, boundary inclusive); `IGroundItemService.cs` (one doc sentence); new shared test helper `tests/EditMode/LootTableSystem/LootTestFakes.cs` (`RecordingInventoryService`, `SettablePositionProvider`); constructor calls updated in `LootTable_GroundItemLifecycle_tests.cs` and `LootTable_RoundRobin_integration_tests.cs`; story-007 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_ProximityPickup_integration_tests.cs` — 16 methods, 17 cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1284/1284 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. `GroundItemService.cs` diff reviewed; the new test file was not read line by line (left to the qa-tester review).
- **/code-review `GroundItemService.cs` + new files (2026-10-02): CHANGES REQUIRED → all fixed (user: "Fix all").** unity-specialist: no blockers, 2 warnings; qa-tester: GAPS (2 required assertions). **User decisions:** (1) when `Pickup` throws, the outcome is unknown (`InventoryService` commits, then raises `OnInventoryChanged`, and a subscriber exception propagates) → the ground item is removed and announced as despawned, exception + one server error logged — a drop can be lost, never duplicated; (2) the pickup distance stays 3D. Applied: the `TryClaim` catch; docs (`IGroundItemService.Tick` rewritten as three steps, `GroundItemState` comments, provider must not call back, 3D on the constant); fake members other than `Pickup` throw `NotSupportedException`. Tests: disconnect test asserts `ExpiryTick` and the despawn at expiry; expiry test pins the despawn to `expiryTick`; re-entry test asserts no call while outside; item position is no longer the origin; 3D boundary cases (5 `TestCase`s incl. height); despawn handler observes that the pickup call came first; wrong-reason error is one per entry; new tests for repeated entries, two items for one character, disconnect-inside then reconnect, no despawn after a successful pickup, a nested `Tick` at expiry not expiring the claim in flight, and the `Pickup`-throws rule (item gone, one despawn event, never retried).
- **TD-049 logged** (Code Debt): `InventoryService` should isolate subscriber exceptions in its event dispatch so a committed mutation always returns its result; needs an inventory-system.md decision. Total items 47.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1292/1292 passed, 0 compile errors. `LootTable_ProximityPickup_Integration_Tests`: 21 methods, 25 cases.
- Not re-reviewed after the fixes. Story 007 file updated (code-review notes, Story 008 and 010 notes).
- For Story 008: do not call `Pickup` from inside the `OnInventoryChanged` handler (it throws during a dispatch) — mark "retry pending" and pick up in the next `Tick`; `GroundItemService` becomes `IDisposable`; the shared fake's events never fire and need a raise hook.
- GDD gap for TD-048 at close: no message tells clients to remove the beacon after a successful pickup (`GroundItemDespawned` covers expiry and teardown only); the `Pickup`-throws rule and the 3D distance are user decisions not in the GDD.
- Blockers: None.
- Next: `/story-done` for Story 007; then Story 008 (bag-full recovery).
- **Uncommitted since `125e98f`:** Stories 002–007 code and tests, story-002..007 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 006)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-006-common-drop-round-robin.md` — Common Drop Round-Robin Assignment. Status: Complete; loot `EPIC.md` row 006 Complete, epic In Progress (6/12).
- Criteria 5/5. Evidence: `LootTable_RoundRobin_integration_tests.cs`, 22 methods / 24 cases; EditMode 1267/1267.
- Tech debt logged: TD-048 extended (round-robin rules not in the GDDs; Party GDD gaps incl. the 4 vs 6 slot inconsistency) — no new entry.
- Next recommended: Story 007 — `production/epics/loot-table-system/story-007-*.md` (Proximity Pickup and Bag-Full Drop Fate; dependency 006 Complete). It needs a claim API on `IGroundItemService`, `Tick` must not expire an item in `Claiming`, and it declares `ICharacterPositionProvider` consumer-side. Story 010 is also unlocked.
- **Uncommitted since `125e98f`:** Stories 002–006 code and tests, story-002..006 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 006 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-006-common-drop-round-robin.md` — Common Drop Round-Robin Assignment. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-008 unregistered — same accepted gap; no performance note; one open point). **User decision:** `GetMemberAtIndex` returning `CharacterID(0)` → one server error, that item is not spawned, `AdvanceRrNextIndex` is still called, remaining items processed. Also settled: new `LootDropDistributor` implements `ILootDropSink` and takes a `Func<uint>` tick source; `IGroundItemService` extracted; advance is called for solo parties too; existing party stubs gain the three new members. Story updated (5 criteria, performance note, QA case).
- Files changed: new `src/Foundation/LootTableSystem/LootDropDistributor.cs`, `IGroundItemService.cs` (+ `.meta`); `GroundItemService.cs` (implements the interface, no behaviour change); `IPartyService.cs` (+ `GetMemberAtIndex`, `GetRrNextIndex`, `AdvanceRrNextIndex`); `StubPartyService` in `LootTable_PartyTag_integration_tests.cs`, `LootTable_KillResolution_integration_tests.cs`, `LootTable_GroundItemLifecycle_tests.cs` (three members each, nothing else); story-006 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_RoundRobin_integration_tests.cs` — 11 methods, 11 cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1254/1254 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. Diff reviewed; no changes made.
- **/code-review `LootDropDistributor.cs` + siblings (2026-10-02): CHANGES REQUIRED → all fixed (user pre-approved "fix all"; no item changed a gameplay rule).** unity-specialist: no blockers, 5 warnings; qa-tester: GAPS (4 assertion-strength fixes). Applied: (1) per-item `try/catch` + `Debug.LogException` in `OnDropsResolved` (gold is already paid and the record cleared when the sink runs — remaining drops are still distributed; a failed item does not advance the cursor); (2) null member list = party size 0; (3) Rare branch rewritten as `partySize >= 2` (auction seam for Story 010) so size 0 can never open an auction; (4) `IGroundItemService` carries the full contract, `GroundItemService` uses `<inheritdoc/>`; (5) `IPartyService` docs: cursor domain, shared index domain, never throws; (6) a drop with `ItemID` 0 logs one error and consumes no round-robin turn. Tests: AC-LT-15 asserts `Classify == Rare` and the party-size read; CR-LT-6 asserts every read count; invalid-member asserts final cursor and lookup count; AC-LT-7 edges assert advance + resulting cursor; zero-error asserts on happy paths; inline counts replaced by constants; AAA comments. New tests: party sizes 1/2/4 for Common, mixed Common/Rare/Common list, two Rare drops (one size read), tick read once, invalid member on last item, cursor beyond member count, call order (valid and invalid), party call throwing, null member list, invalid item ID.
- Not done: no `GetPartySize(PartyID)` on `IPartyService` (the distributor still reads the member list once per call when a Rare item is present) — left for Story 010, which needs the size at drop time.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1267/1267 passed, 0 compile errors. `LootTable_RoundRobin_Integration_Tests`: 22 methods, 24 cases.
- Not re-reviewed after the fixes. Story 006 file updated (code-review notes, Story 010 notes).
- Party-side gaps for TD-048 at story close: `GetRrNextIndex` is not in party-system.md (a single "next round-robin member" call would be better); a member who becomes ineligible after the last advance still receives the next drop; CR-PS-7's "no loot is assigned" when all are ineligible is not observable through the interface; the invalid-member rule is a user decision not in the GDD; party-system.md says `MAX_PARTY_SIZE = 4` but also "6-slot member array" (line 71) and "slot index 5" (line 196).
- Blockers: None.
- Next: `/story-done` for Story 006; then Story 007 (proximity pickup and bag-full drop fate).
- **Uncommitted since `125e98f`:** Stories 002–006 code and tests, story-002..006 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 005)
- Verdict: COMPLETE WITH NOTES (user: "Yes, with tech debt")
- Story: `production/epics/loot-table-system/story-005-ground-item-lifecycle.md` — Ground Item Lifecycle and TTL Despawn. Status: Complete; loot `EPIC.md` row 005 Complete, epic In Progress (5/12).
- Criteria 8/8. Evidence: `LootTable_GroundItemLifecycle_tests.cs`, 23 methods / 23 cases; EditMode 1243/1243.
- Tech debt logged: TD-048 extended (`GroundItemSpawned` recipient wording; three ground item rules not in the GDD) — no new entry.
- Next recommended: Story 006 — `production/epics/loot-table-system/story-006-*.md` (Common Drop Round-Robin Assignment; dependency 005 Complete). It implements `ILootDropSink`, calls `GroundItemService.Spawn` once per drop, and should add an interface for `GroundItemService`. Known open point: party-system.md has no getter for `rrNextIndex`.
- **Uncommitted since `125e98f`:** Stories 002–005 code and tests, story-002..005 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 005 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-005-ground-item-lifecycle.md` — Ground Item Lifecycle and TTL Despawn. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-007 unregistered — same accepted gap; no performance note; three open points). **User decisions:** (1) Story 005 delivers the ground item service only — Story 006 implements `ILootDropSink`; (2) `OnGroundItemSpawned` is raised synchronously inside `Spawn`; (3) `Spawn` with `ItemID` 0 or `CharacterID` 0 → one server error, `GroundItemID.Invalid`, no record, no event. Also settled: position is `Vector3`; the record stores `SpawnTick`; `Tick` removes a record before raising its despawn event. Story updated (8 criteria, performance note, QA cases).
- Files changed: new `src/Foundation/LootTableSystem/GroundItemID.cs`, `GroundItemState.cs`, `GroundItem.cs` (read-only snapshot), `GroundItemSpawnedEventArgs.cs`, `GroundItemDespawnedEventArgs.cs`, `GroundItemService.cs` (`Spawn`, `Tick`, `TryGetGroundItem`, two events) + Unity `.meta` files; `LootTableConstants.cs` (+ `GROUND_ITEM_TTL_TICKS = 2400`, `GROUND_ITEM_TTL_PAUSE_CAP_TICKS = 1200`); story-005 file.
- Test written: `tests/EditMode/LootTableSystem/LootTable_GroundItemLifecycle_tests.cs` — 13 methods, 13 cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1233/1233 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. Diff reviewed; no changes made.
- **/code-review `GroundItemService.cs` + siblings (2026-10-02): CHANGES REQUIRED → all fixed (user: "fix all").** unity-specialist: no blockers, 2 warnings; qa-tester: GAPS (1 required). Required: (1) the zero-items test passed vacuously (drop chance 0) → real `DROP_CHANCE` 0.25 with a missing draw, plus a positive-control test (hitting draw → one spawn event); (2) a throwing spawn / despawn subscriber lost the ID or left expired items unannounced → both raises now catch and `Debug.LogException` (**user decision: catch-and-log, unlike Story 004's propagate**) + 2 tests. Suggestions applied: `Spawning` → `Assigned` uses wrap-safe `StaleDiscardComparer.IsNewerVersion` (+ test for an older tick); ID allocation skips live IDs after a `uint` wrap; `Tick` remarks (later stories must branch on `Claiming` / `Auctioning`; despawn order within a tick unspecified); tests for different expiry ticks, three items expiring on one tick, wrapped expiry tick, late first tick, item still `Spawning` at expiry, ID not consumed by an invalid `Spawn`.
- Not done: the `ToArray()` allocation on a tick where items expire stays (allowed by the story; none when nothing expires). No test for the ID wrap (needs 2^32 spawns). `GroundItemService` has no interface yet — add one when Story 006 becomes its first consumer.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1243/1243 passed, 0 compile errors. `LootTable_GroundItemLifecycle_Tests`: 23 methods, 23 cases.
- Not re-reviewed after the fixes. Story 005 file updated (code-review notes).
- GDD follow-up (for TD-048): the loot GDD state table says `GroundItemSpawned` is "broadcast to zone clients"; the wire protocol says a common drop's message goes to the assigned character only.
- Blockers: None.
- Next: `/story-done` for Story 005; then Story 006 (round-robin; implements `ILootDropSink`).
- **Uncommitted since `125e98f`:** Stories 002–005 code and tests, story-002..005 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 004)
- Verdict: COMPLETE WITH NOTES (user: "go ahead")
- Story: `production/epics/loot-table-system/story-004-kill-resolution-and-gold.md` — Kill Resolution and Gold Distribution. Status: Complete; loot `EPIC.md` row 004 Complete, epic In Progress (4/12).
- Criteria 8/8. Evidence: `LootTable_KillResolution_integration_tests.cs`, 20 methods / 20 cases; EditMode 1220/1220.
- Tech debt logged: TD-048 extended (empty-owner-party rule; `GetPartyMembers` contract) — no new entry.
- Next recommended: Story 005 — `production/epics/loot-table-system/story-005-ground-item-lifecycle.md` (dependencies 002 and 004 Complete). It implements `ILootDropSink` together with Story 006.
- **Uncommitted since `125e98f`:** Stories 002, 003 and 004 code and tests, story-002/003/004 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 004 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-004-kill-resolution-and-gold.md` — Kill Resolution and Gold Distribution. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-006 unregistered — accepted; no performance note; three open points). **User decision:** tag owner's party has no members at kill time → no drops, no gold, no PRNG draw, server warning, record cleared. Also settled: `IPartyService.GetPartyMembers` returns real members only (join order, no empty slots — Party GDD must confirm); `RecordDamage` is on `ILootTableService`; `ILootDropSink` is public; the zero-gold test uses a fixed-`Next` PRNG instead of a validation-bypassing table. Story updated (8 criteria, 9-step flow, log levels, QA cases). Story 004 does not use `LootEquipmentCache` — the "build after the Item Database is ready" rule applies from Story 006.
- Files changed: new `ILootTableService.cs`, `ILootDropSink.cs`, `LootTableService.cs`; `IPartyService.cs` (+ `GetPartyMembers`); `LootTable_PartyTag_integration_tests.cs` (stub gains `GetPartyMembers`); story-004 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` — 12 methods, 12 cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1212/1212 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. Untested by design (no QA case): failed `AddGold` result path; mob type with no table.
- **/code-review `LootTableService.cs` + siblings (2026-10-02): CHANGES REQUIRED → all fixed (user pre-approved "fix all"; no item changed a gameplay rule).** unity-specialist: flow correct, 3 warnings; qa-tester: TESTABLE with gaps. Required: (1) gold share guard `<= 0` (a negative draw cast to uint would wrap to a capped award); (2) record cleared before rolling/paying — at-most-once resolution if `AddGold` subscribers or the sink throw or re-enter; (3) test that the empty-party path clears the record. Suggestions applied: `ILootTableService.ClearMob`, `LootTableService.Clear()`; member count read once + snapshot requirement documented on `GetPartyMembers`; stale `IPartyService` doc fixed; tests for gold-draw bounds (`GoldMax + 1`) and draw order (roll before gold), failed `AddGold`, mob type without table, negative draw, sink throwing, `ClearMob`/`Clear`, 7 constructor null guards; warning counts asserted exactly (via `Application.logMessageReceived`); tierShift test compares PRNG draw order.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1220/1220 passed, 0 compile errors. `LootTable_KillResolution_Integration_Tests`: 20 methods, 20 cases.
- Not re-reviewed after the fixes. Story 004 file updated (record cleared at step 3; new API).
- Noted for Enemy AI (not loot's to fix): once a kill is resolved, a late `RecordDamage` on the dead mob would start a stray record — callers must not record damage on dead mobs, or must call `ClearMob` on despawn.
- Next: `/story-done` for Story 004; then Story 005 (ground item lifecycle).

## Session Extract — /story-done 2026-10-02 (Loot Table Story 003)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/loot-table-system/story-003-party-tag.md` — Party Tag: Damage Record, Threshold Lock, Fallback. Status: Complete; loot `EPIC.md` row 003 Complete, epic In Progress (3/12).
- Criteria 8/8. Evidence: `LootTable_PartyTag_integration_tests.cs`, 27 methods / 32 cases; EditMode 1200/1200.
- Tech debt logged: 1 item — TD-048 (Design Debt: F-LT-3 wording + registry `TAG_THRESHOLD_FRACTION` vs the code's per-mille constant; three readiness rules not in the GDD).
- Next recommended: Story 004 — `production/epics/loot-table-system/story-004-kill-resolution-and-gold.md` (dependencies 002 and 003 are both Complete). It must build `LootEquipmentCache` only after the Item Database is ready, and call `PartyTagTracker.ClearMob` after resolving a kill.
- **Uncommitted since `125e98f`:** Stories 002 and 003 code and tests, story-002/003 files, loot `EPIC.md`, `docs/tech-debt-register.md` (TD-048), this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 003 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-003-party-tag.md` — Party Tag: Damage Record, Threshold Lock, Fallback. Story file Status still `Ready`.
- Readiness: NEEDS WORK (TR-loot-003/004 unregistered — accepted; no performance note; three undefined `RecordDamage` behaviours). **User decisions:** a zero-damage hit records nothing; a fallback tie on both damage and first-damage tick goes to the party recorded first. Unknown mob / uninitialized `PartyID` → server error, nothing recorded. Story updated with the performance note, 3 new criteria (8 total), notes and QA cases.
- Files changed: new `src/Foundation/LootTableSystem/PartyID.cs`, `IPartyService.cs` (`GetPartyID` only), `MobInfo.cs`, `IMobInfoProvider.cs`, `LootTableConstants.cs` (`TAG_THRESHOLD_FRACTION` as `double` 0.33), `PartyTagTracker.cs` (`ComputeTagThreshold`, `RecordDamage`, `TryGetTagOwner`, `ClearMob`); story-003 file.
- Test written: `tests/EditMode/Integration/LootTableSystem/LootTable_PartyTag_integration_tests.cs` — 18 methods, 21 NUnit cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1189/1189 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. Diff reviewed; no changes made.
- **/code-review `PartyTagTracker.cs` + siblings (2026-10-02): CHANGES REQUIRED → all fixed ("fix all").** unity-specialist: logic correct, 3 warnings; qa-tester: TESTABLE. Required: MaxHP < 1 now logs a server error and the threshold clamps to 1 (+ tests). Suggestions applied: (1) threshold in exact integer arithmetic — `LootTableConstants.TAG_THRESHOLD_PERMILLE = 330`, `PERMILLE_DIVISOR`, `MIN_TAG_THRESHOLD` replace the `double` `TAG_THRESHOLD_FRACTION` (double is exact only for 0.33); (2) `PartyTagTracker.Clear()` for teardown + `ClearMob` obligation documented; (3) test that a party's second hit does not overwrite `firstDamageTick`; (4) `ClearMob` / `Clear` tests; (5) three constructor null-guard tests; (6) test methods renamed to `Subject_Scenario_Expected`; (7) `CreateRecord` helper extracted from `RecordDamage`. Also added: a test checking the threshold against exact integer math for every MaxHP 1–9999.
- Not done: no test for the saturating add — once a party's damage reaches the threshold the tag is locked, so the running total is no longer observable through the public API.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1200/1200 passed, 0 compile errors. `LootTable_PartyTag_Integration_Tests`: 27 methods, 32 cases.
- Not re-reviewed after the fixes. Story 003 file and loot `EPIC.md` open-items note updated for the integer threshold.
- **GDD / registry follow-up:** F-LT-3 wording (`Mathf.CeilToInt`, float) and entities.yaml `TAG_THRESHOLD_FRACTION` (float 0.33) no longer match the code's per-mille constant.
- Blockers: None.
- Next: `/story-done` for Story 003; then Story 004 (needs 002 + 003 — both then Complete).
- Uncommitted since `125e98f`: Story 002 and Story 003 code, tests, story files, loot `EPIC.md`, this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 002)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/loot-table-system/story-002-drop-roll-cache-and-tier-classification.md` — Drop Roll, Equipment Cache and Tier Classification. Status: Complete; loot `EPIC.md` row 002 Complete, epic In Progress (2/12).
- Criteria 5/5. Evidence: `LootTable_DropRoll_tests.cs`, 15 methods / 15 cases; EditMode 1168/1168.
- Tech debt logged: None
- Next recommended: Story 003 — `production/epics/loot-table-system/story-003-party-tag.md` (no dependency; carries the F-LT-3 `double` arithmetic). Then Story 004 (needs 002 + 003; must build `LootEquipmentCache` only after the Item Database is ready).
- **Uncommitted since `125e98f`:** Story 002 code and tests (`DropTier.cs`, `LootDropRoller.cs`, `LootEquipmentCache.cs`, `LootRandomFactory.cs`, `LootTable_DropRoll_tests.cs` + `.meta`), story-002 file, loot `EPIC.md`, this file.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 002 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-002-drop-roll-cache-and-tier-classification.md` — Drop Roll, Equipment Cache and Tier Classification. Story file Status still `Ready`.
- Readiness: NEEDS WORK on two items — TR-loot-001/002/005 not in the (empty) registry (accepted as before); missing performance note → added to the story at the user's request. User confirmed ("yes") the two interpretations: seed generated from entropy, logged, then `new System.Random(seed)`; an item not in the equipment cache classifies as Common.
- Files changed: new `src/Foundation/LootTableSystem/DropTier.cs`, `LootDropRoller.cs` (static `Roll(table, rng)`, double comparison, `NextDouble()` only), `LootEquipmentCache.cs` (one `GetItemsByCategory(Equipment)` call; `Classify`, `TryGetEquipment`), `LootRandomFactory.cs` (`CreateSeededFromEntropy(out seed)`, logs `[LootTable] PRNG seed: N`); story file (performance note).
- Test written: `tests/EditMode/LootTableSystem/LootTable_DropRoll_tests.cs` — 7 methods, 7 NUnit cases (AC-LT-1 ×3, AC-LT-2, AC-LT-6, seeding, zero-drop).
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1160/1160 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success, no deviations. Diff reviewed; no changes needed.
- **/code-review `LootDropRoller.cs` + siblings (2026-10-02): CHANGES REQUIRED → all fixed ("fix all").** unity-specialist: roller correct; one warning — a `LootEquipmentCache` built before the Item Database is ready stays empty and classifies every rare drop as Common. qa-tester: TESTABLE. Required: constructor now throws `InvalidOperationException` when `IsReady` is false, before querying (+ test). Suggestions applied: scripted-PRNG tests pinning `draw < chance` in double (0.5 vs 0.5 / 0.51; 0.0 vs 0.0; 0.99999999 vs 1.0), `TryGetEquipment` test, three null-argument tests, AC-LT-2 test classifies several items, `LootRandomFactory` remarks (game-logic thread only; seed replay same-runtime only).
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1168/1168 passed, 0 compile errors. `LootTable_DropRoll_Tests`: 15 methods, 15 cases.
- Not re-reviewed after the fixes.
- Blockers: None.
- Next: `/story-done` for Story 002; then Story 003 (party tag — no dependency), then 004. Story 004 must construct the cache only after the Item Database is ready.
- Uncommitted: the files above, the push-status note at the top of this file.

## Session Extract — /story-done 2026-10-02 (Loot Table Story 001)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/loot-table-system/story-001-loot-table-definitions-and-validation.md` — Loot Table Definitions and Startup Validation. Status: Complete; loot `EPIC.md` row 001 Complete, epic In Progress (1/12).
- Criteria 7/7. Evidence: `LootTable_DefinitionValidation_tests.cs`, 19 methods / 24 cases; EditMode 1153/1153.
- Tech debt logged: None
- Next recommended: Story 002 — `production/epics/loot-table-system/story-002-drop-roll-cache-and-tier-classification.md` (depends on 001, now Complete), or Story 003 — `story-003-party-tag.md` (no dependency). Both must be done before Story 004.
- **Committed 2026-10-02** in the commit titled "Loot Table System: stories 001-012 + Story 001 (definitions and validation)" (the one after `1cc511b`): the 12 loot story files, loot `EPIC.md`, `src/Foundation/LootTableSystem/`, `tests/EditMode/LootTableSystem/`, `CurrencySystem.cs` (`GOLD_CAP` public), this file. The "not committed" notes in the three loot extracts below are superseded. Not pushed.
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — /dev-story 2026-10-02 (Loot Table Story 001 — implemented, tests green, code-reviewed, not committed)

- Story: `production/epics/loot-table-system/story-001-loot-table-definitions-and-validation.md` — Loot Table Definitions and Startup Validation. Story file Status still `Ready`.
- Readiness: NEEDS WORK on one item only — `TR-loot-002` not in the (empty) `tr-registry.yaml`; accepted as before. Stories 001 and 002 corrected at readiness: the codebase has no explicit ID comparer classes (dictionaries use the default comparer).
- Files changed: new `src/Foundation/LootTableSystem/` — `MobTypeID.cs`, `LootTableEntry.cs`, `LootTableDefinition.cs`, `LootTableValidationIssue.cs`, `LootTableValidator.cs`, `LootTableRegistry.cs` (+ Unity `.meta` files); `src/Foundation/Currency/CurrencySystem.cs` (`GOLD_CAP` private → public const, doc comment only otherwise).
- Test written: `tests/EditMode/LootTableSystem/LootTable_DefinitionValidation_tests.cs` — 15 methods, 17 NUnit cases.
- Test run (Unity 6000.3.10f1 batch mode, EditMode): 1146/1146 passed, 0 compile errors.
- Agent: `gameplay-programmer` — success. Diff reviewed; `LootTableValidator.Validate` split into `ValidateGoldRange` / `ValidateEntries` helpers to stay under the complexity and length limits (no behaviour change, suite re-run after).
- **/code-review `LootTableValidator.cs` (2026-10-02): CHANGES REQUIRED → all fixed ("fix all").** unity-specialist: validator CLEAN; qa-tester: GAPS. Required: (1) `LootTableDefinition.Entries` exposed the backing array (castable and writable) → now a `ReadOnlyCollection` wrapper + test; (2) `MobTypeId` asserted in the gold-cap, empty-range and `DropChance` tests; (3) the two-errors case now goes through `LootTableRegistry.TryCreate`. Suggestions applied: NaN / ±Infinity `DropChance` cases, null-definition test, null-table-set test, pair-list-mutation test, order-independent assertions, `Validate` returns `IReadOnlyList`.
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1153/1153 passed, 0 compile errors. `LootTable_DefinitionValidation_Tests`: 19 methods, 24 cases.
- Not re-reviewed after the fixes.
- Blockers: None.
- Next: `/story-done` for Story 001; then Story 002 or 003 (003 has no dependency).

## Session Extract — /create-stories loot-table-system 2026-10-02 (12 stories written, not committed)

- **Written (user approved "Yes — write all 12"):** `production/epics/loot-table-system/story-001` … `story-012` and `EPIC.md` (stories table, TR-loot-006..011 placeholder rows, open-items list). Review mode lean — QL-STORY-READY skipped; QA cases written from the GDD ACs. All 24 ACs assigned.
- **Stories:** 001 definitions + validation (Logic) · 002 roll, cache, classification (Logic) · 003 party tag (Integration) · 004 kill resolution + gold (Integration) · 005 ground item lifecycle (Logic) · 006 round-robin (Integration) · 007 proximity pickup + drop fate · 008 bag-full recovery · 009 TTL pause · 010 auction open + bids · 011 auction resolution (**Blocked**) · 012 zone teardown.
- **User decisions:** (1) Story 011 Blocked until a new `GoldTransactionReason` value exists for the auction debit (currency-system.md + wire enum + entities.yaml amendment). (2) `ResolveMobDrop` accepts `tierShift` and does not apply it (enemy-ai.md OQ-AI-1 open).
- **Found while decomposing (recorded in EPIC.md and the stories, GDDs not edited):**
  - F-LT-3: `Mathf.CeilToInt(300 × 0.33f)` = 100 in single precision; AC-LT-4 expects 99. Verified numerically. Story 003 computes in `double` — loot GDD formula wording needs a fix.
  - CR-LT-1: default-seeded `System.Random` has no readable seed, yet the GDD wants the seed logged — Story 002 generates and logs a seed, then constructs `new System.Random(seed)`.
  - party-system.md has no getter for `rrNextIndex` (Story 006 declares one consumer-side).
  - CR-LT-13.1: behaviour undefined when `expiryTick` passes while still backgrounded (Story 009 — raise at readiness).
  - Story 011: three points the GDD leaves open (bid from a member who left; non-`InsufficientFunds` errors; lifetime of an item reassigned at `expiryTick`).
  - `IZoneScopedService` (required by the control manifest) does not exist in code.
- **No code exists for** Party System, Enemy AI / mob data, character positions, zone lifecycle, loot wire codecs — stories use consumer-side interfaces (`IPartyService`, `IMobInfoProvider`, `ICharacterPositionProvider`) and stubs.
- **Next:** `/story-readiness production/epics/loot-table-system/story-001-loot-table-definitions-and-validation.md` → `/dev-story`. Separately: amend currency-system.md for the auction reason (unblocks 011); fix F-LT-3 wording in loot-table-system.md.
- Epics still without stories: authentication, damage-calculation, status-effects.
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — TD-047 fix 2026-10-02 (ItemID serialization — fixed, tests green, assets re-seeded)

- **Goal:** user request "fix TD-047" — `_itemId` was not serialized into item `.asset` files.
- **Fix:** `ItemDefinition._itemId` and `EquipmentData._mergeResultItemID` (same defect, found while fixing) changed from `ItemID` to raw `uint`; the `ItemId` / `MergeResultItemID` properties wrap them. `ItemID` unchanged (still a `readonly struct`, per the GDD); no public API change.
- **Test:** new `tests/EditMode/ItemDatabase/ItemDatabase_Serialization_tests.cs` (4 tests; `Object.Instantiate` clone = serializer round-trip, no file I/O). Red before the fix (the 2 ID tests failed, 1127/1129), green after.
- **Runs (Unity 6000.3.10f1 batch mode):** EditMode 1129/1129, 0 compile errors. Seeder re-run: 38 records, validation 0/0/0; all 38 assets have `_itemId` matching their file name; no `.meta` changed.
- **Docs:** TD-047 marked RESOLVED in `docs/tech-debt-register.md`; addendum in `production/qa/smoke-2026-10-02-item-database.md`.
- **Not done:** no code review of this fix; no test reads the real `.asset` files from disk (add with the first asset loader).
- **Next:** commit this session's work (rotate the GitHub PAT in `origin`'s remote URL first), or `/create-stories` for authentication / damage-calculation / loot-table-system / status-effects.

## Session Extract — /story-done 2026-10-02 (Story 006 — Item Database epic Complete again)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-006-scroll-records-and-potion-value-alignment.md` — MVP Item Records: Scroll Records 35–38 and Potion Value Alignment. Status: Complete. `EPIC.md`: Status Complete, all 6 stories Complete, Overview count 34 → 38.
- Criteria 10/10. Evidence: `production/qa/smoke-2026-10-02-item-database.md`; EditMode 1125/1125 (re-run after the last seeder comment edit).
- Tech debt logged: 1 item — TD-047 (`_itemId` not serialized into item `.asset` files; fix before any asset-loading story).
- Amendment #4 code follow-up is done: Enhancement pre-implementation gate "Item Database amendment #4" and NPC Shop's scroll-record dependency are now satisfied in code. Still open for Enhancement: OQ-ENH-7, wire-protocol message set (TD-046).
- Stale wording left in design docs (not edited): systems-index.md row 11 "code follow-up story needed"; npc-shop.md line 577 "records are not in the implemented Item Database yet".
- Next recommended: None identified in this epic. Epics without stories: authentication, damage-calculation, loot-table-system, status-effects.
- Nothing committed. Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — /dev-story 2026-10-02 (Item Database Story 006 — implemented, seeded, smoke PASS, nothing committed)

- Story: `production/epics/item-database/story-006-scroll-records-and-potion-value-alignment.md` — MVP Item Records: Scroll Records 35–38 and Potion Value Alignment. Config/Data — no agent; edits made directly. Story file Status still `Ready`.
- Readiness: NEEDS WORK on one item only — `TR-itemdb-005` not in the (empty) `tr-registry.yaml`; accepted, same as Story 005.
- Files changed: `src/Foundation/ItemDatabase/MvpItemRecordData.cs` (scrolls 35–38 via `BuildScroll`; potions via `BuildPotionFamily` — magnitude 150/400/1000 and 100/280/700 → 80/220/500, cooldown 30 → 20/30/45, StackLimit 20/10/5 → 99; sell prices unchanged), `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` (menu label, doc comment, failure log line — no logic change), `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` (9 → 25 cases), `Assets/data/items/ITEM_ID_REGISTRY.txt`, all 34 existing `.asset` files (new `_scrollData` entry + reference IDs; potion values), 4 new scroll `.asset` + `.meta`.
- Test written: `ItemDatabase_MvpRecords_tests.cs` updated (Config/Data — smoke check is the required evidence).
- Runs (Unity 6000.3.10f1 batch mode): EditMode 1125/1125 passed, 0 compile errors; seeder via `-executeMethod` — 38 records created, validation 0 fatal / 0 errors / 0 warnings. One comment-only edit to the seeder was made after these runs.
- Evidence: `production/qa/smoke-2026-10-02-item-database.md` — PASS.
- GUIDs: pre-check found no references; after re-seed no tracked `.meta` under `Assets/data/items/` changed — the story's "new GUIDs" engine note did not materialize.
- **Found, not fixed (pre-existing since Story 004):** `_itemId` is not serialized into any item `.asset` (`ItemID` is a `readonly struct`, not `[Serializable]`). No loader reads the assets yet; the first one will get `ItemID(0)` on every record. Not in `docs/tech-debt-register.md`. Needs its own story / tech-debt entry before any asset-loading code.
- Blockers: None.
- Next: `/story-done` for Story 006 (no `/code-review` gate for Config/Data, though `MvpItemRecordData.cs` gained two small helpers). Then decide how to handle the `_itemId` serialization gap.

## Session Extract — /story-done 2026-10-02
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-005-scroll-data-and-validator-scroll-rules.md` — ScrollData Sub-Schema and Validator Scroll Rules. Story file Status: Complete; `EPIC.md` row 005 Complete (epic stays In Progress — 006 Ready).
- Criteria 6/6 (AC-42–46 + regression). Evidence: `ItemDatabase_Validator_Scroll_tests.cs`, 12 methods / 15 cases; EditMode 1109/1109.
- Notes: `TR-itemdb-006` unregistered (empty registry); code-review fixes not re-reviewed; Warning and Error test files touched outside the story's file table.
- Tech debt logged: None
- Next recommended: Story 006 — `production/epics/item-database/story-006-scroll-records-and-potion-value-alignment.md` (`/story-readiness` → `/dev-story`). Config/Data: needs the seeder re-run and a smoke report.
- Nothing committed. Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — /dev-story 2026-10-02 (Item Database Story 005 — implemented, tests green, code-reviewed, nothing committed)

- Story: `production/epics/item-database/story-005-scroll-data-and-validator-scroll-rules.md` — ScrollData Sub-Schema and Validator Scroll Rules. Story file Status still `Ready` (not closed).
- Readiness: NEEDS WORK on one item only — `TR-itemdb-006` not in the (empty) `tr-registry.yaml`; accepted, same condition as stories 001–004.
- Files changed: `src/Foundation/ItemDatabase/ScrollData.cs` (new, + `.meta`), `src/Foundation/ItemDatabase/ItemDefinition.cs`, `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs`, `tests/EditMode/ItemDatabase/TestHelpers/ItemDefinitionBuilder.cs`.
- Test written: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Scroll_tests.cs` (+ `.meta`) — 11 methods, 14 NUnit cases, AC-42–46.
- Test run (Unity 6000.3.10f1 batch mode, EditMode, 2026-10-02): 1106/1106 passed, 0 compile errors. ItemDatabase fixtures: Core 12, MvpRecords 9, Validator_Error 25, Validator_Scroll 14, Validator_Warning 11 — all passed.
- Agent: `engine-programmer` — success. Diff reviewed; corrected two rule-number comments in the validator (AC-44 → Rule 13 item 35, AC-45 → item 34) and removed an unused `using` in the new test file.
- **/code-review `ItemDefinitionValidator.cs` (2026-10-02): CHANGES REQUIRED → all fixed ("fix all").** unity-specialist: no blocking logic issue; qa-tester: TESTABLE. Required: `ValidateConsumable` complexity ~12 and `ValidateEquipment` ~44 lines → extracted `ValidateEquipmentRecordShape`, `ValidateConsumableSubSchemaChoice`, `ValidateScrollData` (no behaviour change). Suggestions applied: class remark on `Enum.IsDefined` for `GearSlot`/`GearTier`; `ItemDefinition.cs` doc example no longer uses `?.`; stale AC-35 comment in the Warning tests corrected; 3 tests added — `PotionSellPriceGoldZero_NoWarning` (Warning suite), `UndefinedItemCategory_ReturnsError` (Error suite), `ConsumableWithThreeFaults_ReturnsThreeErrors` (Scroll suite).
- Re-run after fixes (Unity 6000.3.10f1 batch mode, EditMode): 1109/1109 passed, 0 compile errors. ItemDatabase fixtures: Core 12, MvpRecords 9, Validator_Error 26, Validator_Scroll 15, Validator_Warning 12.
- Not re-reviewed after the fixes.
- Blockers: None.
- Next: `/story-done` for Story 005 (optionally re-run `/code-review` first); then `/story-readiness` → `/dev-story` for Story 006.

## Session Extract — /create-stories item-database 2026-10-02 (Amendment #4 follow-up — 2 stories written, nothing committed)

- **Goal:** break the Amendment #4 code follow-up into stories. Epic had 001–004 Complete; only AC-42–47 and the 34 → 38 count changes (AC-18/24/34) were uncovered.
- **Written (user approved "Yes — write both"):**
  - `production/epics/item-database/story-005-scroll-data-and-validator-scroll-rules.md` — Logic, Ready. AC-42–46. `ScrollData.cs`, `ItemDefinition._scrollData` (`[SerializeReference]`), validator: exactly-one rule, `ScrollData` on equipment, `TargetGearTier` None/undefined, zero-price warning scoped to equipment; `StackLimit = 0` and `EquipmentData != null` checks must also run for scrolls (today's early return skips them).
  - `production/epics/item-database/story-006-scroll-records-and-potion-value-alignment.md` — Config/Data, Ready, depends on 005. AC-47 + updated AC-18/24/34; records 35–38; potion values → magnitude 80/220/500, cooldown 20/30/45, `StackLimit` 99 (sources: consumable-use-system.md, entities.yaml); tests 34 → 38; seeder labels; `ITEM_ID_REGISTRY.txt`; re-seed + smoke check.
  - `EPIC.md` — Status Complete → In Progress; stories table rows 005/006; `TR-itemdb-005` revised to 38; `TR-itemdb-006` added (placeholder — tr-registry.yaml still empty); Definition of Done AC range → AC-47, 38 records. Overview paragraph still says 34 (not edited).
- **Review mode lean:** QL-STORY-READY skipped; QA test cases written from the GDD ACs.
- **Flagged in Story 006:** the seeder deletes and recreates assets, so re-seeding changes all item asset GUIDs — no references found in `src/`, `tests/`, `Assets/` today; re-check before re-seeding.
- **Found, not changed:** the GDD's `CooldownSeconds = 0` warning (Edge Cases) has no AC and is not implemented in `ItemDefinitionValidator`.
- **Next:** `/story-readiness production/epics/item-database/story-005-scroll-data-and-validator-scroll-rules.md` → `/dev-story`. Then Story 006.
- **Not committed.** Working tree also holds the Pass 5 review, the Amendment #4 authoring changes and the cross-document cleanup below.
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — Item Database lean re-review Pass 5 2026-10-02 (review session — APPROVED, nothing committed)

- **Goal:** `/design-review design/gdd/item-database.md --depth lean` on Amendment #4. **Verdict: APPROVED** — 0 blocking, 4 recommended.
- **Written:** review log entry (Pass 5) in `design/gdd/reviews/item-database-review-log.md`; "lean re-review pending" → "lean re-review Pass 5 Approved 2026-10-02" in the item-database.md header and systems-index.md row 11. No rule, value or registry change.
- **Recommended, not applied (item-database.md, wording / range only):**
  1. Scroll `StackLimit` knob safe range [1, 99] vs inventory-system.md `StackLimit` knob [10, 99] — align.
  2. "2 × 6 slots = 12 entries" stale in Rule 7 item 21, `EquipmentData` schema, the 3-entries edge case, AC-15, Authoring Budget knob — 7 slots, so 14 (still ≤ 16).
  3. OQ-8 still "provisional pending Enhancement System GDD" — resolvable (registry: ~955K expected DarkSteel +9 cost vs 270g); verify against F-ENH-5 before marking.
  4. Rule 8 item 26 and AC-23 say Consumable Use System is "not yet GDD'd"; Tuning Knobs intro cites removed F-4.
- **Nice-to-have:** AC-47 "at default tuning"; AC for `GetItemsByCategory(Consumable)` `Count == 10`; Rule 13 item 37 "at most one additional slot"; explicit validator rule for a Consumable with non-null `EquipmentData`.
- **Next:** code follow-up story in the item-database epic (see the authoring extract below — `ScrollData.cs`, validator rules, records 35–38, test counts 34 → 38, potion record values). Optionally a short authoring session for the 4 recommended wording fixes first.
- **Not committed.** Working tree holds this review, the Amendment #4 authoring changes and the cross-document cleanup below.
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — Item Database amendment #4 2026-10-02 (authoring session — AUTHORING COMPLETE, nothing committed)

- **Goal:** apply Item Database amendment #4 (Enhancement Scroll records, `ScrollData.TargetGearTier`) — an Enhancement pre-implementation gate and NPC Shop's OQ-NS-4.
- **Decisions (user):** (1) scroll = `ItemCategory.Consumable` with `ScrollData` set and `ConsumableData = null` (not a new category, not a new `EffectType`); (2) scroll `StackLimit = 99`; (3) stale-wording fixes in item-database.md included; (4) full changeset approved ("Apply with my answers").
- **item-database.md:** new Rule 13 (items 34–39: scroll definition, exactly one of `ConsumableData` / `ScrollData` per Consumable, `ScrollData != null` identifies a scroll, 4 records table, no effect / not usable by Consumable Use, `SellPriceGold = 0`); `ScrollData` schema table + `ItemDefinition.ScrollData` row; Rule 2/4/8/12 wording; 34 → 38 records (28 Equipment + 10 Consumable); 4 validator edge cases + scroll zero-price note; F-2 scope line; Interactions and Dependencies rows (Enhancement, NPC Shop, Consumable Use); Tuning Knobs (scroll `StackLimit`); AC-18/24/34 counts, AC-42–47 added (39 blocking / 5 advisory / 44 total); header. Wording only: Overview count (was "30"), AC-7 slot list (Ring, Necklace), OQ-4 and OQ-5 marked resolved.
- **Propagation:** enhancement-system.md (header gates 3 → 2, upstream amendment 4 ✅, dependency row, CR-ENH-15 step 2 names `ScrollData == null`); npc-shop.md (OQ-NS-4 resolved, header: no design gate open); consumable-use-system.md (note under Rule 4: items with null `ConsumableData` are not usable; Last Updated); systems-index.md (rows 11, 15, 23); entities.yaml (4 scroll entries: `stack_limit: 99`, note, revised date — not parser-validated, no Python on this machine).
- **Propagation check:** no stale "34 records" / "amendment #4 open" / "OQ-NS-4 open" reference left in design/ or docs/. `production/epics/item-database/` (EPIC TR-itemdb-005, stories 003/004) still says 34 — implementation history, to be superseded by the follow-up story, not edited.
- **Not committed.** Working tree also still holds the uncommitted cross-document cleanup below.
- **Next:** `/clear` → `/design-review design/gdd/item-database.md --depth lean` (targeted amendment, same structure). Then a code follow-up story in the item-database epic: `ScrollData.cs`, `ItemDefinition._scrollData` (`[SerializeReference]`), validator (exactly-one rule, `ScrollData` on equipment, `TargetGearTier` None, zero-price warning currently fires for every category — must not fire for scrolls), records 35–38 in `MvpItemRecordData`, test counts 34 → 38.
- **Found, not changed:**
  - Coded potion records disagree with the design: `MvpItemRecordData` has StackLimit 20/10/5, HP Small 150 HP, all cooldowns 30s; registry / Consumable Use GDD say StackLimit 99, HP Small 80, cooldowns 20/30/45s. Fold into the same follow-up story.
  - consumable-use-system.md Step 4(d) names no rejection reason, and wire-protocol `UseItemRejectedReason` lacks `RejectedInvalidTarget` (Step 4(e)).
  - consumable-use-system.md header still says "Designed — In Review" (index: Approved); its Item Database dependency row lists `StackLimit` inside `ConsumableData` (it is top-level).
  - entities.yaml Bronze scroll note says "not refunded on rejection" and cites CR-ENH-3 for the loot-table exclusion; CR-ENH-3 says a rejected attempt does not consume the scroll and does not mention loot tables.
- **Still open — Enhancement pre-implementation gates:** OQ-ENH-7, wire-protocol Enhancement message set (TD-046).
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — Enhancement cross-document cleanup 2026-10-02 (authoring session — wording only)

- **Goal:** close the "Still open — other documents" list left by the Enhancement System Pass 7 approval. User approved the full changeset ("Apply all"). No rule, formula or value changed.
- **npc-shop.md:** OQ-NS-6 marked RESOLVED 2026-10-02 (no pre-emption callback — CR-ENH-17 reads `NPCInteractionActive` only at `ConfirmEnhancement` validation); CR-SHOP-3 step 3 and the Enhancement System interactions row rewritten to match ("Shared flag", no event); status header now 1 pre-implementation gate (OQ-NS-4).
- **networking-wire-protocol.md:** `OpenNPCInteraction` note (line 980) no longer says the Enhancement System is notified before the flag is cleared.
- **item-database.md:** Rule 28 "destruction threshold" → destruction rule (CR-ENH-10, no threshold).
- **systems-index.md:** row 15 "Depends On" adds Character Persistence; row 23 (NPC Shop) gate count 2 → 1; risk-table row for Enhancement reworded from "destruction threshold" to success-rate tuning (F-ENH-4).
- **entities.yaml:** `IsAttemptInProgress` registered (source CR-ENH-18; consumer undecided — OQ-ENH-7). Not parser-validated (no Python on this machine); entry copies the `IEnhancementBonusProvider` layout.
- **Propagation check:** no remaining `OnNPCSessionPreempted` / "notified before" / `OQ-NS-4/6` references outside review logs (history, not edited). `game-concept.md` lines 231/254 still say "destruction threshold" — left as the original concept wording.
- **Not committed.** `main` is 13 commits ahead of `origin` (last push 2026-09-24).
- **Still open — Enhancement pre-implementation gates (each its own session):** OQ-ENH-7 (which layer holds requests during an attempt), Item Database amendment #4 (scroll records, `ScrollData.TargetGearTier` — also NPC Shop's remaining gate OQ-NS-4), wire-protocol Enhancement message set (TD-046, including the missing client → server selection request for `EnhancementStateUpdate`).
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — /design-review enhancement-system.md --depth lean 2026-10-01 (Pass 6 — NEEDS REVISION, then Revision Pass 3 applied)

- *(Correction 2026-10-02: everything in this extract was committed as `9464290`; the "Not committed" notes below are stale.)*

- **Review (Pass 6 lean):** all 5 Pass 5 blockers verified closed (apply-then-commit + caller-owned rollback, `GetElementalBonus` base parameter, result codes, AC-ENH-33..38). 2 new blockers: `RejectedItemEquipped` unreachable (AC-ENH-4 not runnable); AC-ENH-7 contradicted CR-ENH-18 (requests are held, not rejected).
- **Revision Pass 3 (same session, user decisions):** `RejectedItemEquipped` dropped (CR-ENH-4, enum, AC-ENH-4 → `RejectedItemNotFound`); AC-ENH-7 tests the Inventory lock directly; Enhancement NPC session gets the shop's 300s wall-clock lifetime (CR-ENH-17, new AC-ENH-39 — 39 ACs); `IsAttemptInProgress` true through `RESULT_*`; `outcome`/`newLevel` ignored on rejection; OQ-ENH-7 covers server-originated mutations; Player Fantasy "destruction threshold" wording removed.
- **Files modified:** `design/gdd/enhancement-system.md`, `design/gdd/reviews/enhancement-system-review-log.md`, `design/gdd/systems-index.md` (row 15 + summary line), this file. `design/registry/entities.yaml` — no change needed (no registered entity/formula/constant changed). Not committed.
- **Pass 7 lean re-review (same session, no `/clear`): APPROVED** — both Pass 6 blockers verified closed, 0 new blockers. Two wording-only AC fixes applied with approval (AC-ENH-7 → `MoveRequest` / `SourceLocked`; AC-ENH-39 → `CancelEnhancement`). GDD header, systems-index (row 15, counts 38 approved / 0 in review, Approved list) and review log updated.
- **Status:** Enhancement System = **Approved (Pass 7 lean, 2026-10-01)**. Not committed.
- **New open items from Pass 7:** no client → server selection request is defined for `EnhancementStateUpdate` (fold into TD-046); client behaviour when the NPC session expires with the Enhancement UI open (Enhancement UI GDD).
- **Still open — other documents (not edited):** npc-shop.md OQ-NS-6 (lines 57/146/583) + networking-wire-protocol.md line 980 still require a pre-emption callback that CR-ENH-17 says is not needed; "destruction threshold" wording in item-database.md Rule 28 and the systems-index risk table; systems-index "Depends On" for Enhancement omits Character Persistence; `IsAttemptInProgress` not in entities.yaml.
- **Pre-implementation gates (unchanged):** OQ-ENH-7 (which layer holds requests during an attempt — no reference to CR-ENH-18 exists outside the Enhancement GDD), Item Database amendment #4 (scroll records, `ScrollData.TargetGearTier`), wire-protocol Enhancement message set (TD-046).
- Reminder still outstanding: plaintext GitHub PAT in `origin`'s remote URL — rotate before any push.

## Session Extract — live Unity Editor session 2026-09-24 (Milestone: first successful compile)

- User opened the project in Unity Editor and hit Safe Mode due to a compile error — **the first live Editor session this whole project**, ending the "no live Unity Editor available" caveat that had applied to every single story up to this point (all prior verification was static C# reading + hand-derived arithmetic only).
- Root cause: `tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs` (`SetTargetCodec_TargetEntityIdZero_RoundTripsAsZero_NoExceptionThrown`, Networking Core epic, already committed in `0772226`, unrelated to this session's Leveling System work) declared `Span<byte> buffer` and captured it inside an `Assert.DoesNotThrow(() => ...)` lambda — `Span<T>` is a `ref struct` and C# forbids capturing ref structs in closures (CS8175). This exact same mistake had already been made and fixed once before in `WireProtocol_Envelope_Serialization_tests.cs` (which has its own explanatory comment: use `byte[]` instead, since arrays convert to `Span<byte>` implicitly at each call site without being captured) — this file just never got the same treatment.
- Fixed by applying the identical, already-established `byte[]`-instead-of-`Span<byte>` workaround. **Did a full repo-wide sweep** (grepped every `Span<byte>`/`ReadOnlySpan<byte>` local across `tests/` and `src/`, cross-referenced against every `Assert.DoesNotThrow`/`Throws`/`Catch` lambda site) before declaring it fixed — confirmed this was the ONLY instance of this bug pattern anywhere in the codebase, not a symptom of a wider problem.
- **Confirmed by the user: Unity now compiles cleanly, no longer in Safe Mode.**
- Significance: a live Editor is now available for the first time. Every prior story this session (all 12 completed Leveling System stories plus the full Networking Core epic) was verified only via static reading — none has ever been run through the real Unity Test Runner. Running the full EditMode suite now would be high-value: it's the first opportunity to catch anything static review missed.

## Session Extract — live EditMode Test Runner execution 2026-09-24 (first-ever real run — 3 real bugs found and fixed)

- User ran the EditMode Test Runner for real (Window > General > Test Runner, inside the now-compiling Editor) — **the first time any test in this codebase has ever actually executed**, not just been statically reviewed. Result: 3 failures, all in the pre-existing Networking Core epic (already committed in `0772226`), none in this session's Leveling System work.
- **Root cause 1 — `Registry_ContainsExactlyOneRowPerRealWireProtocolMessageTypeId_AC_MCR_04_AC_CCR_09`**: `MessageRoutingRegistry.cs`'s `BuildEntries()` was missing a row for `SetTarget` (`MessageTypeId = 0xE040`, Story 029) — a real, already-implemented wire message with no registry entry, violating the registry's own documented MCR-2 rule ("a new message requires a row here before its schema is accepted"). Confirmed via `design/gdd/networking-message-criticality.md` line 98 and `networking-channel-contract.md` line 118 that `SetTarget` was fully specified as Infrastructure/R-OD/C→S/P1 back when OQ-RFR-2 resolved (2026-05-19) — the GDD row existed, the registry row never got added. Verified via a full enumeration of every `public const ushort MessageTypeId` field in the assembly (10 total) against the registry's 9 existing rows that `SetTarget` was the ONLY orphan — not a symptom of a wider gap. Fixed by adding the missing row, following the exact pattern of the most similar existing entry (`SelfDamageEvent`: single-pillar R-OD, `PriorityPath` context, `isMcr3Exception: false`).
- **Root cause 2 — `TryValidateStatID_ValidMaxBoundaryByte16_...` and `TryValidateStatID_AdjacentOutOfRangeByte17_...`** (`WireProtocol_EntityIdEnumGuards_tests.cs`): both tests hardcoded boundary literals (16=max-valid, 17=one-past-max) that predate this session's `MagicDefense` insertion into `StatID` (Leveling System Story 002, Character Stats `StatID.cs`) — that insertion shifted every float-schema member +1 (`MovementSpeed` 16→17), which the **production** guard (`WireEnumCodec.StatIdMaxValue = (byte)StatID.MovementSpeed`) tracked automatically since it's computed from the enum, not hardcoded — so this was never a production bug. Only these two tests' literal probe values (and a same-file cosmetic comment, plus one stale comment in `WireEnumCodec.cs` itself) were stale. This is a genuine cross-epic propagation gap: a Leveling System change (this session, months after Networking Core shipped) silently invalidated two Networking Core test assumptions, and nothing caught it until real test execution. Fixed: renamed both tests to `...Byte17_...`/`...Byte18_...`, updated all literals/messages/regexes to 17/18, added an explanatory comment block noting the root cause for future readers, fixed the stale "(0-16)" and "0..16" comments in the same file and in `WireEnumCodec.cs`.
- All 3 fixes applied directly by me (small, mechanical, well-scoped — matching this session's established pattern for such fixes) rather than delegated, given the low complexity and the value of fixing them immediately while the failures were fresh in context.
- **Re-verified by the user via a real re-run: all 3 tests now pass.** First real (non-static) confirmation of a bug fix this entire session.
- Files modified: `src/Foundation/Networking/WireProtocol/MessageRoutingRegistry.cs`, `tests/EditMode/Networking/WireProtocol_EntityIdEnumGuards_tests.cs`, `src/Foundation/Networking/WireProtocol/WireEnumCodec.cs` (comment only).
- Not committed to git yet. Uncommitted work now spans the full Networking Core epic fixes above plus everything already noted uncommitted this session (Leveling System Stories 007-012, Character Stats fixes). Reminder still outstanding: plaintext GitHub PAT embedded in `origin`'s remote URL (see `project_exposed_git_credential` memory) — flag again before any push. Also still present: an untracked `bash.exe.stackdump` crash artifact in the repo root — not part of any story, flag before any commit/cleanup pass.
- Next: resume Story 013 (HUD/UI Display & Manual Verification) — the earlier feasibility caveat (no live Editor for screenshots/audio review) is now partially resolved since a live Editor exists, though actual screenshot/audio evidence + lead sign-off still needs the user's direct involvement, not something I can produce alone.

## Session Extract — /dev-story 2026-09-24 (Leveling System Story 013 — HUD/UI Display & Manual Verification — CODE COMPLETE, awaiting manual verification)

- **Real scope gap found and resolved with the user before implementation**: no UI Toolkit HUD scaffolding (`UIDocument`/`PanelSettings`/`HUD_Root` hierarchy) existed anywhere in the project — normally the not-yet-created HUD epic's job (systems-index #29, no epic/stories exist). Checked `design/ux/hud.md` (OQ-HUD-7's required artifact) and confirmed it's genuinely `Status: Complete` with exact XP bar/level badge specs (Row 5: 4dp height, `#C4912A`; Row 1: `Lv.##` Bold 13sp) — the GDD's own Open Questions section text just wasn't updated to reflect this, matching this project's repeated index/registry-staleness pattern. Presented the scope decision via `AskUserQuestion`: user chose "build minimal self-contained scaffolding now," same forward-dependency-stand-in pattern used all session, applied to infrastructure instead of just data contracts.
- Spawned `unity-ui-specialist` (routed via the File Extension Routing table for `.uxml`/`.uss`, not the generic Visual/Feel→gameplay-programmer row, given this story is overwhelmingly UI Toolkit work). First pass genuinely incomplete (not just an unreported-narration-fragment gap like past stories) — confirmed via direct filesystem check that core presenter logic and the Editor bootstrap script didn't exist yet. Resumed with an explicit gap list; second pass completed everything.
- **First UI Toolkit code ever written in this project.** Files created: `Assets/UI/HUD/HUD_Root.uxml`, `UI_RespecScreen.uxml`, `USS_HUD_Theme.uss` (+ .meta); `Assets/Editor/HudBootstrap.cs` (+ .meta) — idempotent Editor menu utility that programmatically creates the `PanelSettings` asset via `ScriptableObject.CreateInstance`/`AssetDatabase.CreateAsset` (never hand-authored YAML, per ADR-005's own HIGH knowledge-risk classification) plus the `UIDocument`/`EventSystem`/`InputSystemUIInputModule` scene wiring; `src/Foundation/UI/HudSafeAreaApplier.cs`, `src/Foundation/UI/LevelingSystem/{PlayerResourceClusterPresenter,LevelUpOverlayPresenter,RespecScreenPresenter,LevelingFormulaPreview,LevelingHudController,LevelingHudManualTestHarness}.cs` (all + .meta).
- I independently verified this thoroughly before reporting: all 13 new GUIDs unique repo-wide; 3 of 5 core C# files read in full and cross-checked against real `CharacterStats`/`LevelingService` APIs (`Subscribe`/`Unsubscribe` delegate signature, `EntityID` equality operators, `LevelingService.GetLevelTierMultiplier`'s `internal` accessibility — correctly called directly since UI code lives in the same `IronGrind.Foundation` assembly, `OnLevelUp`/`LevelUpEventArgs`); `LevelingFormulaPreview.cs`'s duplicated F-3–F9 and CR-4.3 floor formulas verified byte-for-byte identical to `LevelingService`'s real (private, hence duplicated) originals; UXML element names in both `.uxml` files cross-checked against all 3 presenters' `.Q<T>()` queries — exact matches throughout, including the `<ui:Instance>` template cross-query subtlety (UQuery traverses TemplateContainer boundaries); `HudBootstrap.cs`'s package dependencies (`com.unity.inputsystem` 1.18.0) confirmed actually installed via `Packages/manifest.json`; USS file syntactically clean, fill-bar pattern matches ADR-005 exactly (`transform-origin: left center`, no `border-radius` on the fill layer).
- Known, honestly-flagged deviations (agent's own report, all reasonable): `RecomputeDerivedStats`/`GetAutoAllocIncrement` are `private` on `LevelingService` (unlike the already-`internal` `GetLevelTierMultiplier`) — duplicated in `LevelingFormulaPreview.cs` with an explicit drift-risk warning rather than widening production code (out of scope this session); consecutive-level-up batching mechanism and tier-transition-across-a-batch resolution are the agent's own interpretation (story doesn't literally specify either), documented inline; "Commit disabled + Confirm-only modal" read as two sequential steps; floating level-up text scoped to local player only (world-space "nearby players" version is a different system/layer).
- Both `_levelUpChime`/`_tierTransitionChime` left unassigned — no `Assets/Audio/` exists in this project yet; audio review portion of AC-LS-46 is blocked on real asset delivery, not this story.
- **Required manual steps in the user's live Editor** (cannot be done by me): (1) let Unity finish importing the new files, (2) run `Tools > HUD > Bootstrap HUD Scaffolding`, save the scene, (3) run `Tools > HUD > Add Leveling Manual Test Harness`, (4) enter Play Mode and use the 5 on-screen debug buttons to trigger each AC scenario for screenshot/audio evidence.
- Test Evidence: Visual/Feel story, `production/qa/evidence/leveling-system-hud-ui-evidence.md` not yet created — genuinely cannot be produced without the user's screenshots/audio judgment/lead sign-off, explicitly deferred, not a gap in the code itself.
- **First real Editor compile attempt found 2 genuine bugs my static review missed** (both now fixed):
  1. **CS0118**: `CharacterStats` used bare in 4 files (`LevelingHudController.cs`, `PlayerResourceClusterPresenter.cs`, `RespecScreenPresenter.cs`, `LevelingHudManualTestHarness.cs`, 7 occurrences total) while `using IronGrind.CharacterStats;` was in scope — the namespace and the class share the exact same name, an already-known ambiguity in this codebase (hit and fixed once before, 2026-07-09 commit `116aeb1`, in a different file). Every other file in this session's Leveling System work correctly fully-qualifies it as `IronGrind.CharacterStats.CharacterStats`; the new UI code didn't. Fixed all 7.
  2. **Invalid UXML comments**: both `.uxml` files used literal `--` as an em-dash substitute inside `<!-- -->` comment blocks — XML forbids `--` anywhere in a comment body except the closing `-->`. Fixed all 5 instances across `HUD_Root.uxml`/`UI_RespecScreen.uxml` (left `--` alone in actual attribute text like `"MaxHP: --"`, which isn't inside a comment and is valid there).
  - This continues the pattern already seen twice this session (Networking Core's `Span<byte>`-in-lambda bug, the StatID boundary tests) — static review catches a great deal but genuinely can't substitute for real compilation, especially for something as post-cutoff-risky as first-ever UI Toolkit/UXML content.
- **Manual verification pass surfaced 3 more real bugs my static review missed** (all now fixed):
  1. Same `CharacterStats`-namespace-vs-class ambiguity as before, but freshly reintroduced in the new UI presenter files — 7 bare `CharacterStats` usages across 4 files, fully-qualified.
  2. Debug-harness buttons rendered too small (no explicit sizing) — added explicit padding/min-size/font-size.
  3. `PanelSettings.scaleMode = ConstantPhysicalSize` made everything render far smaller than authored, since the Editor Game view can't reliably detect DPI and silently falls back to a low scale factor. Switched to `ConstantPixelSize`.
  4. **Real respec-screen layout bug, found via "rainbow debugging" (temporary colored backgrounds on each container) after 2 rounds of screenshot-based diagnosis**: `.respec-pool-row`, `.respec-attributes-column`, `.respec-preview-column` all relied purely on nested-children auto-height (no explicit height of their own) — Unity 6.3's UI Toolkit failed to resolve this correctly across that nesting depth, causing all three to collapse toward zero height and render fully overlapping. Fixed with explicit `min-height` on each (40px/120px/150px respectively), confirmed by the user via screenshot that all rows became correctly visible and separated.
  5. **Second real bug found from the same diagnosis**: `<ui:Instance template="RespecScreenTemplate">` wraps the respec screen in a `TemplateContainer` that is NOT full-screen by default (follows normal flow, sized to content) — `RespecScreenRoot`'s own `position:absolute;inset:0` was resolving relative to that undersized container, not the actual screen, causing off-center/clipped rendering even after the min-height fix. Fixed by adding a `.respec-instance-fullscreen` class (`position:absolute;inset:0`) directly to the `<ui:Instance>` element.
  6. **Self-inflicted regression**: while writing the explanatory comment for fix #5 above, I reintroduced the exact same invalid-XML-comment bug (literal `--` inside a `<!-- -->` block) that I'd just fixed twice earlier in this same file — this broke `HUD_Root.uxml`'s import, causing `PlayerResourceClusterPresenter`'s constructor to throw `ArgumentNullException` on `xpBarFill` (element not found), which aborted `LevelingHudManualTestHarness.Start()` before it ever reached the debug-button-creation code — explaining why the ENTIRE HUD (level badge, XP bar, AND all 5 debug buttons) vanished, not just the respec screen. Fixed, then did a systematic sweep of both `.uxml` files' comment blocks specifically (not just eyeballing) to confirm no third instance exists.
- This session's UI Toolkit work is a strong illustration of why the "no live Unity Editor" caveat mattered on every prior story: purely static review (however careful) cannot substitute for actually running unfamiliar, post-cutoff-risk engine code — 2 genuine layout/config bugs and 2 rounds of the same XML-comment mistake only surfaced through the user's real manual testing and iterative screenshot-based debugging.
- **User confirmed: works correctly now** — full HUD (level badge, XP bar), all 5 debug buttons, and the respec screen all render and lay out correctly. First fully working UI Toolkit screen in this project.
- Not committed to git yet.
- **User confirmed all 3 ACs pass manual verification**: AC-LS-46 (normal level-up flash/hold, tier-transition identical intensity with longer 2.5s hold, consecutive +3 jump cuts straight to final level with no intermediate flicker), AC-LS-47 (L60 shows "MAX" badge + full static XP bar), AC-LS-48 (floor values never editable below their value via +/-, Commit stays disabled until pool fully allocated, Confirm-only modal with no Cancel) — all "looks great." This is the first Visual/Feel story this session actually verified via real Play Mode testing rather than static review + DEFERRED evidence.
- Audio portion of AC-LS-46 remains genuinely untested (no audio assets exist in the project yet) — not a gap in this story, explicitly out of reach until the audio team delivers clips.
- Code review: `unity-ui-specialist` returned **CHANGES REQUIRED** (first CHANGES-REQUIRED verdict this session) — found 2 genuine, previously-unflagged defects beyond the debugging-session fixes: (1) `HUD_Root`/`HUD_Overlay`/the respec `<ui:Instance>` wrapper were all full-screen elements missing `PickingMode.Ignore`, which would silently block ALL touch input project-wide the moment real touch gameplay exists beneath this HUD (ADR-005's own Validation Criteria #4); (2) `RespecScreenPresenter`'s per-stat +/- buttons were wired via unsubscribable lambda closures, creating a real double-fire leak on `LevelingHudController.Initialize()`'s own documented re-entrant contract (future respawn/reconnect). Plus: attr buttons under the 48×48dp touch minimum, a stale comment, missing `clearDepthStencil=false`. `qa-tester` returned APPROVED WITH SUGGESTIONS — confirmed the debug harness genuinely exercises real production code paths (traced the full `AddExperience`→`NotifyExperienceCrossedThreshold` chain), found the evidence file was never created, and flagged 2-3 AC-46/47 sub-clauses (XP-bar flash-reset, floating text, L59→60 transition moment) verified only via code-trace/end-state, not separately watched in the moment. Also flagged a zero-coverage drift risk in `LevelingFormulaPreview.cs` and an unimplemented "tap XP bar for tooltip" feature from the Implementation Notes.
- User chose "apply all" — all fixes applied: `hud-display-only` added to 8 elements across `HUD_Root.uxml`; `RespecScreenPresenter` refactored to use named per-stat delegates (unsubscribed in `Dispose()`) plus a `RequireElement<T>` null-check helper; `.respec-attr-button` bumped to 48×48dp (with matching row-height/column-min-height adjustments); stale `ConstantPhysicalSize` comment fixed; `clearDepthStencil=false` added to `HudBootstrap.cs`; `.respec-instance-fullscreen` renamed to the reusable `.fullscreen-instance` (**caught and fixed a self-inflicted class-name mismatch between the UXML and USS during this rename** — would have silently broken the fix); created `production/qa/evidence/leveling-system-hud-ui-evidence.md` documenting what was Play-Mode-confirmed vs. accepted via code-trace vs. genuinely out of reach (audio, tooltip).
- Did a third, explicit systematic sweep of both `.uxml` files' comment blocks (not just visual re-reading, given the mistake recurred twice already) — confirmed zero stray `--` remains anywhere.
- **User confirmed: everything works correctly** after the full round of code-review fixes. Story 013's implementation is now genuinely done — code-reviewed (CHANGES REQUIRED → all fixed), manually verified via real Play Mode testing (not just static review, a first for this session's only Visual/Feel story), and the evidence file exists.
- Next: `/story-done`.

## Session Extract — /story-done 2026-09-24 (Leveling System Story 013 — HUD/UI Display & Manual Verification — COMPLETE WITH NOTES)

- Verdict: COMPLETE WITH NOTES. All 3 ACs (AC-LS-46/47/48) passing, verified via real live Unity Editor Play Mode testing — the only Visual/Feel story this session verified this way rather than static review + deferred evidence. Story file marked Complete, all 3 AC checkboxes ticked, Completion Notes added. `EPIC.md` Story 013 row updated to Complete; epic-level status updated to "12/13 Complete — Story 010 Blocked on OQ-LS-7."
- Advisory deviations logged as tech debt: TD-036 (audio chimes pending real assets) and TD-037 (unimplemented XP-bar tap-tooltip feature from the Implementation Notes, not AC-gated).
- This story was the most involved of the session by far: first UI Toolkit code ever written in this project, a scope-gap decision (build minimal HUD scaffolding now vs. wait for a not-yet-created HUD epic — resolved with the user via `AskUserQuestion`), 2 rounds of implementation (first pass genuinely incomplete, resumed), a live debugging session with the user across ~10 screenshot round-trips (2 compile errors, a PanelSettings scale-mode issue, a genuine Unity 6.3 UI Toolkit nested-container auto-height layout bug, a `<ui:Instance>` full-screen sizing gap, and the same invalid-XML-comment mistake made and caught 3 times), and a lean code review that returned this session's only CHANGES REQUIRED verdict (2 genuine defects: a project-wide touch-input-blocking `PickingMode` gap, and a real button-handler leak in `RespecScreenPresenter`).
- Epic running total: 12 of 13 Leveling System stories now Complete. Story 010 remains the epic's only open item, Blocked on an external dependency (OQ-LS-7, mob XP rates owned by a not-yet-authored Economy/Mob Definition GDD) — not resolvable within this epic.
- No further stories ready to start in this epic. Next work would be: resolving OQ-LS-7 to unblock Story 010, or moving to a different epic/system entirely.

## Session Extract — OQ-LS-7 resolution 2026-09-24 (Story 010 unblocked; real cross-document conflict found and fixed across 4 Approved GDDs)

- User asked to resolve OQ-LS-7 to unblock Story 010. Investigation revealed this was NOT actually "unspecified" as the OQ's own text claimed — the real data schema (`MobDefinition.KillXP`/`EnragedKillXP`) already existed, fully Approved, in `enemy-ai.md`. The genuine blocker was a real, undiscovered **conflict between 4 already-Approved GDDs** about which system actually calls XP-award on a kill, only surfaceable by reading all of them together (the same pattern this whole session has repeatedly found via direct-source-reading rather than trusting inline cross-references):
  1. `leveling-system.md` CR-1.1 said "Damage Calculation calls AddExperience" — contradicted by `damage-calculation.md`'s own explicit "Option B" (Damage Calculation never calls it; the killer's controller does).
  2. `enemy-ai.md`'s `Dead`-state transition table AND its "Character Persistence" interactions subsection both claimed Enemy AI awards XP directly (`CharacterPersistence.AwardXP(...)`) on kill — if left as-is, this would have **double-awarded XP** every kill, since `ApplyDamage` (which triggers the `Dead` transition) is the LAST step in the killer's controller's own already-correct sequence, meaning XP would already be awarded by the time Enemy AI's Dead state even fires.
  3. `skill-system.md`'s CR-SK-6 already had the correct 3-step kill sequence (`GetXPAward → AddExperience → ApplyDamage`) but called `GetXPAward` with a 2-parameter signature (`casterEntityID, targetEntityID`) that contradicted the 1-parameter signature (`TargetID: int`) used everywhere else — found during the mandatory propagation check, not the original investigation.
  4. `auto-attack-combat.md` had NO kill-sequence specification at all — applied damage and checked `IsKill` only for an animation-state transition, never mentioning XP — despite `damage-calculation.md`'s own downstream-dependency table already stating this GDD "must call GetXPAward... then ApplyDamage." Also found during the propagation check.
- Presented the full conflict + a recommended resolution via `AskUserQuestion` before touching anything (design decision, not a unilateral call): `GetXPAward(TargetID): int` is a thin function reading `MobDefinition.KillXP`/`EnragedKillXP` via the already-Approved `IMobDefinitionRegistry` — no level-differential modifier exists at MVP (no penalty for farming below-level mobs). User approved. When the propagation check found items 3 and 4 above (expanding scope from 2 GDDs to 4), asked again via `AskUserQuestion` before proceeding — user approved fixing both.
- **Corrected 4 GDDs**: `leveling-system.md` (CR-1.1 + OQ-LS-7 marked Resolved with full spec + cross-team dependencies table), `enemy-ai.md` (removed the duplicate XP-award from the `Dead`-state table and rewrote the stale "Character Persistence" subsection), `skill-system.md` (CR-SK-6 signature fix), `auto-attack-combat.md` (added the missing kill-sequence to Rule 10 Step 3 + a new Leveling System dependency row) — plus a stale phantom "Rule 4a"/"Step 4" cross-reference in `damage-calculation.md` corrected to point at the real location.
- No `entities.yaml` changes needed — this resolved an open question about an existing field's consumption pattern, not a new entity/stat.
- **Story 010 unblocked**: Status changed from Blocked to Ready in both the story file and `EPIC.md`. One gate remains before AC-LS-31 can pass: economy-designer sign-off on the derived `XpThreshold` cumulative table (unchanged by this resolution, was always a separate requirement).
- Not committed to git yet.
- Next: `/story-readiness` on Story 010 to confirm, then `/dev-story` to implement — noting the economy-designer sign-off gate still applies to AC-LS-31.

## Session Extract — commit + push 2026-09-24

- Committed the session's uncommitted work as 2 logical commits (matching this repo's established one-unit-of-work-per-commit convention): `6440d27` (Leveling System Stories 009/011/012) and `e11681c` (Networking Core registry/test regressions found via the first live test run). Deliberately excluded `bash.exe.stackdump` (a crash artifact, not real content) from both commits — user then asked to delete it, done.
- Pushed both commits to `origin/main` (`99f62b7..e11681c`). Reminded the user again about the plaintext GitHub PAT in `origin`'s remote URL before pushing — user acknowledged, chose to proceed without rotating yet. **Per [[project_exposed_git_credential]], keep reminding every session until the user confirms rotation — this is not yet resolved.**
- Working tree fully clean after push.

## Session Extract — /story-done 2026-09-24 (Leveling System Story 012 — Party XP Detriment Integration — COMPLETE)

- Verdict: COMPLETE. Both ACs (AC-LS-33, EC-LS-32) passing, 0 deviations, zero production code touched by design. Story file marked Complete, both AC checkboxes ticked, Completion Notes added. `EPIC.md` Story 012 row updated to Complete.
- Review mode: lean — self-performed parallel review (unity-specialist APPROVED, qa-tester APPROVED WITH SUGGESTIONS, both 0 Required Changes) served as this story's review; all 3 qa-tester suggestions + 1 unity-specialist cosmetic suggestion applied before closing.
- Test count: 5 (`LevelingSystem_PartyXpDetrimentIntegration_tests.cs`) — epic total now 63 across 11 Complete stories (7+4+7+5+6+5+9+5+11+4+5).
- Next: Story 013 (HUD/UI Display & Manual Verification) — the last Ready story in this epic. Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story 2026-09-24 (Leveling System Story 012 — Party XP Detriment Integration — IMPLEMENTATION COMPLETE, awaiting code review)

- `/story-readiness` passed READY (15/17 checks — 1 informational systemic TR-ID gap as always, plus a noted-but-accepted "only 2 ACs" advisory since AC-LS-33 alone bundles 5 concrete sub-assertions and the story's own text justifies the narrow test-of-the-boundary scope). Independently cross-checked the story's inline F-PS-1 formula against `design/gdd/party-system.md` line 141 directly before starting — confirmed accurate, not stale.
- Spawned `gameplay-programmer`. **New tooling-gap variant this session**: the agent's task notification arrived as `status: failed` ("Agent stalled: no progress for 600s") rather than the usual mid-task-narration-fragment gap seen in prior stories — but checking the filesystem directly showed the test file and its `.meta` had already been fully and correctly written before the stall; the agent froze during its own final self-verification pass, not mid-write. Recovered by reading and independently verifying the artifact directly rather than resuming/re-spawning the agent — no work was lost, no retry needed.
- **Zero new production code, by design** — this story is a pure test-of-the-boundary story per its own Implementation Notes: `F-LS-2` (a GDD-superseded +30% party bonus) is explicitly NOT implemented; the real F-PS-1 detriment formula belongs to the not-yet-built Party System epic. Only a test-local `ComputePartyXpDeduction_TestOnly` stand-in was written, clearly marked non-production, feeding the real unmodified `CharacterStats.AddExperience(EntityID, int)`.
- Test count: 4 (`LevelingSystem_PartyXpDetrimentIntegration_tests.cs`, new file) — AC-LS-33 covered by 3 tests (party-size multipliers 1000/900/800/700 at N=1-4; the 333-at-N=4 rounding edge case, honestly noting in its own comment that this specific worked example doesn't distinguish `Mathf.RoundToInt` from truncation, matching the story's own example; the stand-in's output feeding the real `AddExperience` and confirming Experience increases by exactly the adjusted amount). EC-LS-32 covered by 1 reflection-based structural test asserting `AddExperience` has exactly 2 parameters (`entityId`:`EntityID`, `amount`:`int`) and none reference party size — a regression guard against CR-1.5 violations. I independently re-verified all 5 numeric values and the reflection assertions against the real `AddExperience` signature (`CharacterStats.cs:997`) myself — all correct.
- No live Unity Editor available this session — static verification only, same as every story this session.
- Not committed to git yet. (Noted in passing: an untracked `bash.exe.stackdump` crash artifact is sitting in the repo root — not this story's concern, flag before any future commit/cleanup pass.)
- Code review: unity-specialist fully **APPROVED** (0 BLOCKING, 0 Required Changes, 1 cosmetic suggestion) — independently confirmed via `git diff` that this story's only change is the new test file (the working-tree `LevelingService.cs` diff belongs entirely to Story 009, unrelated). qa-tester **APPROVED WITH SUGGESTIONS** (0 Required Changes, 3 actionable suggestions) — found two genuine testability gaps: (1) the story's own 333-at-N=4 worked example doesn't actually distinguish `Mathf.RoundToInt` from truncation (both give 233), proposed a real distinguishing case (1001 at N=4 → 701 rounds vs. 700 truncates); (2) the `AddExperience` boundary test claimed "no level-up occurs" in a comment but never asserted it, missing this epic's established zero-`OnLevelUp`-firings idiom. User chose "Apply all 4" via `AskUserQuestion`: added the 1001@N=4 distinguishing test, added the zero-OnLevelUp assertion, relaxed the two brittle exact-parameter-name asserts in the EC-LS-32 reflection test to type-only (the substring loop is the actual regression guard), and added a cosmetic `XP_PARTY_DEDUCTION_RATE` cross-reference comment. Test count now 5.
- Epic running total: 63 tests across 11 Complete-or-implemented stories (7+4+7+5+6+5+9+5+11+4+5).
- Next: `/story-done production/epics/leveling-system/story-012-party-xp-detriment-integration.md`. After that: Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /story-done 2026-09-24 (Leveling System Story 011 — Tier Multiplier & Auto-Alloc Formula Verification — COMPLETE)

- Verdict: COMPLETE (no notes — first story this session with a fully clean COMPLETE, no caveats beyond the standing "no live Unity Editor" disposition). All 3 ACs (AC-LS-34/35/36) passing, 0 deviations. Story file marked Complete, all 3 AC checkboxes ticked, Completion Notes added. `EPIC.md` Story 011 row updated to Complete.
- Review mode: lean — the self-performed parallel `/code-review` (unity-specialist + qa-tester, both fully APPROVED, 0 BLOCKING, 0 Required Changes from either) served as this story's review; both optional suggestions applied before closing.
- Test count: 4 (`LevelingSystem_TierAutoAllocFormulaVerification_tests.cs`) — epic total now 58 across 10 Complete stories (7+4+7+5+6+5+9+5+11+4).
- Next: Story 012 (Party XP Detriment Integration), Story 013 (HUD/UI Display & Manual Verification). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story 2026-09-24 (Leveling System Story 011 — Tier Multiplier & Auto-Alloc Formula Verification — IMPLEMENTATION COMPLETE, awaiting code review)

- `/story-readiness` passed READY (16/17 checks; same systemic TR-ID-registry gap as always, non-blocking). Independently re-verified AC-LS-35/36's F-LS-4 arithmetic by hand before starting — matched the story text exactly.
- Spawned `gameplay-programmer`; this one returned a clean final report on the first notification (no narration-fragment retry needed this time).
- **Only production change: one visibility widening, no logic change.** `LevelingService.GetLevelTierMultiplier` changed from `private static` to `internal static` — this project's `src/Foundation/AssemblyInfo.cs` already grants the EditMode test assembly `InternalsVisibleTo`, so this is the minimal way to let AC-LS-34 test the tier lookup table directly and in true isolation from any level-up sequence (exactly what the story's Implementation Notes call for), without adding new public API surface. Mirrors this file's existing `IsLevelingUpInProgress`/`TestOnly_...` test-observability-seam idiom.
- **Key design insight surfaced during context-loading (by me, before delegating)**: Story 011 depends on Story 009 not just for `InitializeAtL1`, but because AC-LS-35/36's "59 level-ups from a single `AddExperience` call" trick only works cleanly given Story 003's pre-existing CR-2.9 loop — no new machinery from Story 009 was actually needed for the level-up cascade itself (that's pure Story 002/003), but `InitializeAtL1` (Story 009) is what cleanly seeds the L1 starting state before the cascade. Verified the exact mechanism myself: an xpThresholds array with indices `[2..60]=0` makes `Experience=1` (set by one `AddExperience(entity, 1)` call) satisfy every threshold check through the entire climb, so the CR-2.9 loop's own `levelBefore>=60` break is the only thing that stops it — 59 level-ups, one call.
- Test count: 3 (`LevelingSystem_TierAutoAllocFormulaVerification_tests.cs`, new file) — one parameterized-by-hand test for AC-LS-34 (7 boundary assertions, L1/19/20/39/40/59/60 → 1.0/1.0/1.2/1.2/1.5/1.5/2.0, called directly against the now-internal lookup), one each for AC-LS-35 (Warrior) and AC-LS-36 (Healer). I independently re-verified all 3: the tier-boundary values against `GetLevelTierMultiplier`'s actual `if` chain, and the Warrior/Healer STR/DEX/VIT/INT/heldFreePoints snapshots against `ClassDefinition`'s auto-alloc fields — all correct, no discrepancies. Also confirmed the new `.meta` file's GUID (`dc4e69b4...`) is unique repo-wide.
- No live Unity Editor available this session — static verification only, same as every story this session.
- Not committed to git yet.
- Code review: both parallel reviewers (unity-specialist, qa-tester) returned fully **APPROVED** — 0 BLOCKING, 0 Required Changes from either, the first fully-clean pair this session with zero Required Changes on either side. unity-specialist independently hand-traced the 59-iteration cascade mechanism (confirmed no off-by-one, verified `InternalsVisibleTo`/asmdef assembly-name match by reading both files rather than assuming) and assessed the `internal`-visibility widening against this file's existing `IsLevelingUpInProgress`/`TestOnly_...` precedent (found it consistent, flagged as a minor non-blocking observation that it's the first *permanently unguarded* internal seam vs. the `#if`-gated `TestOnly_...` fields — acceptable given it's pure/stateless). qa-tester independently re-verified all 17 hand-derived values (7 tier multipliers + 10 stat/held-point snapshot values) against source and confirmed the AddExperience-cascade test is a genuine regression-catcher, not a tautology. 3 suggestions total (XML doc-comment upgrade, out-of-range boundary test, and a moot "update story checkboxes" note that's `/story-done`'s job). User chose "Apply both" for the 2 actionable ones via `AskUserQuestion`: upgraded `GetLevelTierMultiplier`'s comment to a full XML doc block, and added `GetLevelTierMultiplier_OutOfDocumentedRange_DegradesGracefully` (L0→1.0, L61→2.0). Test count now 4.
- Epic running total: 58 tests across 10 Complete-or-implemented stories (7+4+7+5+6+5+9+5+11+4).
- Next: `/story-done production/epics/leveling-system/story-011-tier-autoalloc-formula-verification.md`. After that: Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /story-done 2026-09-24 (Leveling System Story 009 — Spawn Initialization & Persistence Load — COMPLETE)

- Verdict: COMPLETE WITH NOTES (note: no live Unity Editor this session — static verification only). All 5 ACs (AC-LS-27/28/29/30/51) passing, 0 deviations. Story file marked Complete, all 5 AC checkboxes ticked, Completion Notes added. `EPIC.md` Story 009 row updated to Complete.
- Review mode: lean (`production/review-mode.txt`) — Phase 4b (QL-TEST-COVERAGE) and Phase 5 (LP-CODE-REVIEW) director gates both skipped; the earlier self-performed parallel `/code-review` (unity-specialist + qa-tester, both APPROVED WITH SUGGESTIONS, 0 BLOCKING) served as this story's review, all 5 findings applied.
- Test count: 11 (`LevelingSystem_SpawnPersistenceLoad_tests.cs`) — epic total now 54 across 9 Complete stories (7+4+7+5+6+5+9+5+11).
- Next: Story 011 (Formula Verification), Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story 2026-09-24 (Leveling System Story 009 — Spawn Initialization & Persistence Load — IMPLEMENTATION COMPLETE, awaiting code review)

- `/story-readiness` passed READY (16/17 checks; the one gap — `TR-lvl-010` absent from `docs/architecture/tr-registry.yaml` — is a project-wide condition, the registry is `requirements: []` and has never been populated by an `/architecture-review` run, so it's non-blocking and equally true of all 8 already-Complete stories in this epic).
- Spawned `gameplay-programmer` for implementation. First completion notification arrived with only a mid-task narration fragment (the recurring tooling gap seen multiple times before this session, e.g. Story 007) — resumed via `SendMessage` asking explicitly for the final report only; worked cleanly on retry.
- **Known GDD-vs-reality gap applied, not newly found**: CR-6.2 step 4 literally reads `SetBaseStat(CurrentHP, MaxHP)`/`SetBaseStat(CurrentMP, MaxMP)` — same discrepancy Story 002 already resolved elsewhere in this file (`CurrentHP`/`CurrentMP` live in separate dictionaries, not the `SetBaseStat` int array). Implemented as `SetCurrentHP`/`SetCurrentMP`, matching `ExecuteLevelUpSequence`'s existing CR-2.7 block exactly. I specified this fix directly in the implementation brief (derived it myself during context-loading) rather than letting the agent discover it independently, since it's already a closed question in this codebase.
- New production: `LevelingService.InitializeAtL1(EntityID, byte classType)` (CR-6.1/CR-6.2 — reuses the existing `RegisterPlayerClassType` as the canonical classType-caching path rather than duplicating it, reuses `RecomputeDerivedStats(entityId, 1.0f)` for F-3–F-9 at the literal ×1.0 tier); new `LevelingStateSnapshot` readonly struct (single `HeldFreePoints` field, mirrors `LevelUpEventArgs`'s small-payload idiom); `LevelingService.GetLevelingState`/`RestoreLevelingState` (CR-6.3 + EC-LS-38 Level clamp `[1,60]` + EC-LS-36 heldFreePoints clamp against `(clampedLevel-1)×freePointsPerLevel[class]` — Level clamp intentionally ordered first since the heldFreePoints max is derived from the already-clamped Level, which is also what makes this same guard catch the CR-2.8 partial-write-crash-recovery case with no separate detection logic).
- Test count: 7 (`LevelingSystem_SpawnPersistenceLoad_tests.cs`, new file) — one per AC (AC-LS-27, AC-LS-28, AC-LS-29) plus 2 each for AC-LS-30 and AC-LS-51 (high/low and Level=0/Level=70 cases split into separate methods, matching this epic's established one-scenario-per-method convention). I independently re-verified all 7 by direct code reading before accepting: hand-recomputed the F-3–F-9 arithmetic for AC-LS-27 myself (MaxHP=400, MaxMP=220, AttackPower=30, Defense=20, MagicDefense=4, CritChance=0.065, AttackSpeedMultiplier=1.03 — matches the agent's derivation), verified the 4 `LogAssert.Expect` regex patterns against the actual `Debug.LogError` message text produced by `RestoreLevelingState` (all 4 match), and confirmed both new `.meta` files have unique GUIDs (no collisions anywhere in the repo) in the same minimalist 2-line format as sibling files (e.g. `LevelUpEventArgs.cs.meta`).
- AC-LS-51's "LevelTierMultiplier/AtCap state derive correctly from the clamped value" requirement was proven without adding new public API — both Level-clamp tests call `GetExperienceThreshold` after the clamp and assert it returns the ordinary threshold (Level=1 case) or the CR-5.3 sentinel (Level=60 case, reusing Story 008's AC-LS-25 worked example exactly) — demonstrating correctness via the same existing code path rather than new logic.
- Epic running total: 50 tests across 9 Complete-or-implemented stories (7+4+7+5+6+5+9+5+7... — Story 009 not yet marked Complete, code review pending).
- No live Unity Editor available this session — static verification only (agent's own admission plus my independent re-verification), same as every story this session.
- Not committed to git yet.
- Code review: both parallel reviewers (unity-specialist, qa-tester) returned APPROVED WITH SUGGESTIONS — 0 BLOCKING from either. unity-specialist: 1 Required Change (doc-only) — `RestoreLevelingState` didn't document a real latent trap for the not-yet-built Character Persistence caller: `GetClassType` silently defaults to `0` for an entity whose classType wasn't re-registered this session (e.g. after a server restart), which would silently clamp a returning player's legitimate `heldFreePoints` toward 0. qa-tester: 0 Required Changes, 4 suggestions (exact-boundary clamp tests at `heldFreePoints==0`/`==maxHeld` and `Level==1`, an unregistered-classType fallback test, and an executable reflection check strengthening AC-LS-29's "not derivable from GetBaseStat" claim beyond a comment). Both reviewers independently verified the `LogAssert.Expect` regexes against the actual `Debug.LogError` strings (all 4 match) and independently re-derived the AC-LS-27 F-3–F-9 arithmetic (matches). User chose "Apply all 5" via `AskUserQuestion`. All 5 applied: added the precondition doc comment to `RestoreLevelingState` (plus a cross-reference note in the class-level remarks about the classType cache being in-memory/per-instance) and 4 new test methods (`RestoreLevelingState_LevelExactlyOne_NoClampNoError`, `RestoreLevelingState_HeldFreePointsExactlyZero_NoClampNoError`, `RestoreLevelingState_HeldFreePointsExactlyAtMax_NoClampNoError`, `RestoreLevelingState_UnregisteredClassType_HeldFreePointsClampsToZeroFallback`) plus a reflection-based `StatID` enum-name check added to the existing AC-LS-29 test. Test count now 11 (grep-verified) in `LevelingSystem_SpawnPersistenceLoad_tests.cs`.
- Epic running total: 54 tests across 9 Complete-or-implemented stories (7+4+7+5+6+5+9+5+11).
- Next: `/story-done production/epics/leveling-system/story-009-spawn-persistence-load.md` to verify ACs and mark Complete. After that: Story 011 (Formula Verification), Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story + /code-review + /story-done 2026-08-15 (Leveling System Story 008 — Level Cap Behavior — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist CLEAN + qa-tester CLEAN, both parallel, 0 BLOCKING + 0 Required Changes from either) served as this story's review.
- Story: `production/epics/leveling-system/story-008-level-cap-behavior.md` marked Complete, all 4 AC checkboxes ticked. `EPIC.md` Story 008 row updated to Complete.
- **Real finding, surfaced before implementation via direct source reading, not story-package trust**: despite the story reading as though only dedicated tests were needed for already-built behavior (matching CR-5.1/AC-LS-24's explicit "already implemented" framing), `CharacterStats.AddExperience` (Character Stats epic, already-Complete Story 006) had ZERO at-cap logic — AC-LS-23 genuinely required new production code. Fixed with a small, mechanical, spec-required guard (`if (GetBaseStat(Level) == 60) return;`), matching CR-5.2's own text exactly. AC-LS-24 (CR-2.1 guard) and AC-LS-26 (`AllocateFreePoint`) were confirmed already correct, needing only dedicated tests as the story anticipated.
- **AC-LS-25's sentinel contract confirmed real but unreachable through any current production call path** — both `AddExperience`'s new guard and `NotifyExperienceCrossedThreshold`'s own `levelBefore>=60` loop-break short-circuit before ever calling `GetExperienceThreshold` at Level 60. Tested directly against `GetExperienceThreshold` itself as a forward-looking safety contract (e.g. for Story 013's HUD). Confirmed independently by qa-tester via full grep of all `GetExperienceThreshold` call sites.
- qa-tester hand-traced the full AC-LS-26 arithmetic end-to-end (3 real consecutive level-ups from L57, driven by a single `AddExperience(300)` call, landing at Level 60 with STR=16/DEX=13/VIT=13/INT=10/heldFreePoints=3/AttackPower=84, then the free-point spend producing STR=17/AttackPower=88) — matched exactly, including the ×1.5-vs-×2.0 tier boundary that's this epic's most common source of miscalculation. I independently re-verified this arithmetic myself both before delegating implementation and after.
- Code review: 0 BLOCKING, 0 Required Changes from both reviewers — first fully-clean pair for a story with genuinely new production code this session. 2 actionable suggestions, both applied: an OQ-1 cross-reference note (the new guard's correctness depends on `Level` only being written via `ExecuteLevelUpSequence` — a pre-existing, unrelated write-ownership gap, not this story's problem) and a below-cap regression test (`CharacterStats_AddExperience_BelowLevelCap_StillProcessesNormally`) pinning the new guard's boundary directly.
- `GetXPToNextLevel` (CR-5.5, mentioned in Implementation Notes) confirmed genuinely unbuilt anywhere in the codebase via full-repo grep — correctly left out of scope per the story's own Out-of-Scope section (HUD-facing, assigned to Story 013). Not a gap in this story.
- Test count: 5 (`LevelingSystem_LevelCapBehavior_tests.cs`) — one per AC plus the review-driven regression test.
- No live Unity Editor available this session — static verification only, as it has been all session.
- Committed and pushed as part of the same push as Story 007 (see below) — actually: **not yet committed**, this story's changes remain uncommitted pending user instruction, same pattern as before.
- Next: Story 009 (Spawn Initialization & Persistence Load) is next in sequence — `Status: Ready`, no known blockers. After that: Story 011 (Formula Verification), Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story + /code-review + /story-done 2026-08-15 (Leveling System Story 007 — Respec Two-Phase Commit & Exception Safety — COMPLETE WITH NOTES)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — self-performed `/code-review` (unity-specialist CLEAN + qa-tester CLEAN, both parallel, 0 BLOCKING + 0 Required Changes from either) served as this story's review.
- Story: `production/epics/leveling-system/story-007-respec-two-phase-commit.md` marked Complete, AC-LS-18a/AC-LS-22 checkboxes ticked. AC-LS-18b remains unticked/Blocked on OQ-LS-3, exactly as pre-declared — not a gap. `EPIC.md` Story 007 row updated to Complete.
- **Major finding, surfaced before any code was written, via direct source reading not GDD trust**: `CharacterStats.RollbackStatTransaction()` (Character Stats epic, a DIFFERENT already-Complete Story 007) only discarded the deferred event queue — it explicitly preserved base stat writes, per its own doc comment and its own already-tested BLOCKING gate (`GetBaseStat(VIT)==54` after rollback, i.e. write preserved). This silently contradicted BOTH this story's AC-LS-22 ("all stats revert") and Class System's AC-CS-24 (different epic, same requirement) — nothing had ever called `RollbackStatTransaction()` in anger before this story, so the gap was latent since Character Stats Story 007 shipped. **User chose the root-cause fix over a local workaround** via `AskUserQuestion`: added real snapshot-and-restore to `RollbackStatTransaction()` itself (new `CaptureSnapshotBeforeWrite` helper called from `SetBaseStat`/`SetBaseStatFloat`/`SetCurrentHP`/`SetCurrentMP`, first-write-only dedup, restores across all three backing stores — int-schema array, float-schema array, and the separate `CurrentHP`/`CurrentMP` dictionaries — via raw writes, never through the public setters). Character Stats' own Story 007 file and gate test were updated in place with a cross-referencing revision note. Matches this epic's established pattern (Story 002's `MagicDefense` fix, Story 006's `SetCurrentHP`/`SetBaseStatFloat` transaction-awareness) of fixing shared Character Stats gaps at the root when the first real caller exposes them, always disclosed and user-approved.
- **Implementer self-caught a second bug while building the fix**: `EndStatTransaction()` didn't clear the new snapshot array on a successful commit — without the fix, a second transaction's snapshots would append after stale leftover entries from a prior committed transaction, corrupting the dedup scan. Fixed (`_snapshotCount = 0` added to `EndStatTransaction()`), independently verified correct.
- `TryApplyRespec` (Story 006's method) had its CR-4.4 steps 2-6 wrapped in try/catch (Story 007's actual job): unconditional `RollbackStatTransaction()` + rethrow, `EndStatTransaction()` never reached on the exception path. New `RespecTwoPhaseCommitCoordinator` (stateless static, mirrors `PartyDisbandCoordinator`'s shape) implements the CR-4.1 two-phase-commit sequence (gate → reserve → commit → Consume/Release) as a forward-dependency stand-in for the not-yet-built Inventory System, composed with new minimal `IItemReservation` interface.
- **Coordinator-quality gap this session**: the implementing agent's background-task notifications repeatedly arrived with only a mid-task narration fragment instead of a real final report (recurring tooling gap, seen before at Story 002) — required 2-3 rounds of `SendMessage` resumption with an explicit "stop investigating, output ONLY the final report in this format" instruction each time, both for the initial implementation and for the review-fix round. Worked cleanly each time once asked plainly; no information was ever lost, just delayed.
- Code review: both parallel reviewers (unity-specialist, qa-tester) returned fully CLEAN — 0 BLOCKING, 0 Required Changes — a first for a story this structurally significant in this epic. 10 combined non-blocking suggestions; user chose "apply all 10" via `AskUserQuestion`. All 10 applied: placeholder-value clarifying comment, `heldFreePoints` future-proofing assertion, new coordinator success-path test, new direct EC-LS-22 nested-transaction test (through `TryApplyRespec` itself, not just at the raw `CharacterStats` level — reproduced by opening a transaction externally before calling `TryApplyRespec`, no new hook needed), new EC-LS-23-broadening test (second injection seam `TestOnly_ThrowAfterRecomputeDerivedStats`, proves derived stats revert even after `RecomputeDerivedStats` already wrote new values), 4 null-guard tests, `Consume()` moved outside the `try` in the coordinator (so a hypothetical throwing `Consume()` doesn't get misrouted into `Release()`), `DEVELOPMENT_BUILD`-only overflow logging added to both dedup arrays (I independently broadened this to `UNITY_EDITOR || DEVELOPMENT_BUILD` post-review, matching the existing `IsFiringAndAssert` precedent in the same file, via a second `AskUserQuestion` — the original guard wouldn't have fired during normal EditMode test runs), and a doc-comment reword. Every fix independently re-verified by direct code reading (not just trusted from the implementing agent's report) before proceeding to `/story-done`.
- Test count: `LevelingSystem_RespecTwoPhaseCommit_tests.cs` — 9 tests (new file). `CharacterStats_Transaction_tests.cs` — 10 tests (1 flipped, 3 new this round for the Rollback fix, all pre-existing 6 untouched). Epic total now 43 across 7 Complete stories (7+4+7+5+6+5+9) plus the 4 new Character Stats tests in a different epic.
- Every test assertion hand-traced independently three times this session (implementer, qa-tester, coordinator) against the real formulas/code paths — including the specific proof that the AC-LS-22 test would genuinely FAIL under the old broken Rollback behavior (not pass vacuously): under the old "preserve writes" semantics, `GetBaseStat(Strength)` would still read 50 (not revert to 28) after the catch, since the write had already landed before the exception fired.
- No live Unity Editor available this session — static verification only, as it has been all session. This is now flagged in the story's own Test Evidence status and Completion Notes explicitly, not just in my own narration.
- TD-034 (Consume()/Release() ordering hardening, deferred until a real Inventory System exists) and TD-035 (TestOnly_ naming idiom, deferred until it recurs) logged.
- Not committed to git yet — everything since the `e2f8370` Networking Core + Core-layer-epics + Stories 001-006 push remains uncommitted (this story's changes: Character Stats fix, Leveling System fix, new files, both epics' story/EPIC files, tech-debt register).
- Next: Story 008 (Level Cap Behavior) is next in sequence — `Status: Ready`, no known blockers. After that: Story 009 (Spawn/Persistence Load), Story 011 (Formula Verification), Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story + /code-review 2026-08-11 (Leveling System Story 006 — Respec Core Commit Sequence — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 BLOCKING + 1 Required Change + 1 Suggestion, qa-tester 1 BLOCKING (same finding independently), both parallel) served as this story's review. All findings fixed, both reviewers confirmed 0 BLOCKING remaining after fixes (re-verified by the coordinator independently, not just trusted).
- Story: `production/epics/leveling-system/story-006-respec-commit-sequence.md` marked Complete, all 5 AC checkboxes ticked (AC-LS-17, 19, 20, 21, 52). `EPIC.md` Story 006 row updated to Complete.
- **Two real design-time gaps found and resolved before implementation, both via direct source verification, not GDD trust**: (1) the GDD's literal `TryApplyRespec(EntityID)` single-parameter signature couldn't carry the caller's new stat totals — resolved to `TryApplyRespec(EntityID, IReadOnlyDictionary<StatID,int>)`. (2) The GDD's CR-4.4 assumed an existing "EC-05" ambient HP/MP-vs-Max reconciliation mechanism in `CharacterStats` — verified via full-file reading that no such mechanism exists (the only HP-ceiling clamp logic was narrowly scoped inside `RemoveEquipmentModifier`). Resolved by having `TryApplyRespec` re-assert `SetCurrentHP(entityId, GetCurrentHP(entityId))`/`SetCurrentMP(...)` after `RecomputeDerivedStats`, letting those methods' own clamp do the reconciliation.
- **This required making `SetCurrentHP`/`SetCurrentMP` transaction-aware** (previously always fired `OnStatChanged` immediately, which would have broken AC-LS-17's single-batched-pass guarantee) — user approved via `AskUserQuestion`. Mirrors `SetBaseStat`'s existing `if (_transactionOpen) AddToDeferredDedup(...) else FireOnStatChanged(...)` pattern. Zero regression: `ExecuteLevelUpSequence` (the only other caller) never opens a transaction.
- **Code review surfaced a second, identically-shaped transaction-awareness gap in `SetBaseStatFloat`** (used by `RecomputeDerivedStats` for CritChance/AttackSpeedMultiplier) — harmless before this story since no prior `RecomputeDerivedStats` caller ever opened a transaction, but `TryApplyRespec` is the first that does. User chose to fix it properly (same pattern) over documenting it as an accepted deviation, via a second `AskUserQuestion`.
- **BLOCKING test bug, caught independently by both reviewers AND the coordinator via three different derivations**: the AC-LS-19 test's `heldFreePoints` assertions said 5 but its own setup (Warrior L14→L20 via `AddExperience`, with `xpThresholds[15..20]` all 50 and Experience pinned at 50) actually produces 6 level-ups — provable independently from the test's own Strength assertion (`36+6×2=48` only reconciles with 6, not 5). Fixed: both asserts and 3 doc comments corrected 5→6. This is a genuinely new/different bug from the epic's recurring "threshold-array sentinel sizing" bug (Stories 001-004) — the array bounds were correct here; only the hand-derived level-up count was wrong.
- New production: `LevelingService.TryApplyRespec` (new method, reuses Story 005's `RecomputeDerivedStats`, never touches `heldFreePoints`); `CharacterStats.SetCurrentHP`/`SetCurrentMP`/`SetBaseStatFloat` all made transaction-aware.
- Test count: 5 new (`LevelingSystem_RespecCommitSequence_tests.cs`) — epic total now 34 across 6 Complete stories (7+4+7+5+6+5).
- No live Unity Editor available this session — static verification only, as it has been all session. Every formula in every test was hand-traced against `RecomputeDerivedStats`/`GetLevelTierMultiplier` by the coordinator independently, not just trusted from the implementing agent or reviewers.
- Not committed to git yet — nothing in this epic has been committed since the Networking Core push.
- Next: Story 007 (Respec Two-Phase Commit & Exception Safety) is next — wraps this story's `TryApplyRespec` with item-reservation orchestration and exception-safety rollback (AC-LS-18b sub-case remains Blocked on OQ-LS-3). After that: Story 008 (Level Cap), Story 009 (Spawn/Persistence Load), Story 011 (Formula Verification), Story 012 (Party XP), Story 013 (HUD/UI). Story 010 stays Blocked on OQ-LS-7.

## Session Extract — /dev-story + /code-review 2026-07-22 (Leveling System Story 005 — Free Point Allocation — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 Required Change + qa-tester 1 Required Change, same finding independently, both parallel, 0 BLOCKING from either) served as this story's review.
- Story: `production/epics/leveling-system/story-005-free-point-allocation.md` marked Complete, all 6 AC checkboxes ticked. `EPIC.md` Story 005 row updated to Complete.
- This story's real work was replacing Story 003's `AllocateFreePoint` busy-check stub (which threw `NotImplementedException` for the non-busy path) with the real CR-3 allocation sequence — not writing a new method from scratch. Verified the busy-check guard stayed first and unmodified, and Story 003's own busy-check test still passes against the new code (content-verified, file shows zero diff).
- **Good design call**: rather than duplicating the CR-2.6 derived-stat recompute formula block between `ExecuteLevelUpSequence` (Story 002/004) and the new `AllocateFreePoint`, the implementing agent extracted a shared `RecomputeDerivedStats` private helper — avoiding the "two copies drift apart" risk. Both reviewers independently traced this extraction against the AC-LS-03/04/05 exact-firing-order tests to confirm it's behavior-preserving before accepting it.
- **First story in this epic where the recurring threshold-array sentinel bug (6 instances across Stories 001-004) did NOT recur** — the implementing agent proactively checked every array against the pattern before finalizing tests (per my explicit instruction this time, given the track record), and both parallel reviewers independently re-verified all 5 arrays anyway. Clean on the first pass — the proactive-check instruction worked.
- Both reviews converged on the exact same single finding (missing `.meta` file for the new test file) — fixed.
- **Recurring process note, now flagged 5 times by 5 different implementing agents**: `.claude/rules/test-standards.md`'s snake_case test-naming rule doesn't match this project's actual C# convention (PascalCase, used by 100% of ~36 tests across this epic and every pre-existing test in the repo). Correct call every time it's come up, but worth fixing the rule itself rather than continuing to re-litigate per story — flagged to the user as a suggested cleanup, not yet actioned.
- New production: `AllocateFreePoint`'s real implementation, `RecomputeDerivedStats` shared helper (also now used by `ExecuteLevelUpSequence`), 2 new `AllocateFreePointResult` enum values.
- Test count: 6 new (`LevelingSystem_FreePointAllocation_tests.cs`) — epic total now 29 across 5 Complete stories (7+4+7+5+6).
- No live Unity Editor available this session — static verification only, as it has been all session.
- Not committed to git yet.
- Next: Story 006 (Respec Core Commit Sequence) is next in sequence — depends on Story 002's F-3–F-9 recompute (can likely reuse `RecomputeDerivedStats` again, worth checking) and Character Stats' Transaction API (`BeginStatTransaction`/`EndStatTransaction`, already Complete). Given the pattern from Story 005 (clean when a proactive check was explicitly requested), continue explicitly instructing implementing agents to proactively check the threshold-array sentinel pattern before finalizing any new test that triggers a real level-up.

## Session Extract — /dev-story + /code-review 2026-07-22 (Leveling System Story 004 — Tier Transition From-Scratch Recompute & Raw-Write Ceilings — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 BLOCKING + 1 Required Change→both fixed + qa-tester 1 BLOCKING (same issue)→fixed, both parallel) served as this story's review.
- Story: `production/epics/leveling-system/story-004-tier-transition-recompute.md` marked Complete, all 5 AC checkboxes ticked. `EPIC.md` Story 004 row updated to Complete.
- **Despite the story's title suggesting a pure testing/proof story, one AC (AC-LS-54) genuinely needed new production code**: verified via direct code reading (grep for "CR-2.2a") that the XP-overshoot clamp for the L59→L60 transition was entirely missing from `ExecuteLevelUpSequence`. Added the one-line conditional clamp; the other 4 ACs (AC-LS-38/53/42/43) needed only dedicated tests, confirmed by reading the existing Story 002 code rather than assuming.
- **The single most significant finding of this epic so far**: unity-specialist's review caught that this story's own new tests (AC-LS-38, AC-LS-53) would throw `ArgumentOutOfRangeException` — their XP-threshold arrays were sized to end exactly at the tested level, with no sentinel value for the CR-2.9 loop's mandatory post-level-up re-check (a lookup pattern Story 003 introduced). The reviewer also caught that the IDENTICAL bug already existed in Story 002's own already-closed, already-twice-reviewed test file — a latent regression, since that test predates Story 003's loop and nothing had ever actually executed it (no live Unity Editor has been available all session).
- **I did not stop at fixing what the reviewers flagged — I independently audited every threshold array across all 4 test files in the epic** and found **3 more instances of the identical bug** beyond what either reviewer named: Story 002's AC-LS-04, AC-LS-05, AC-LS-06 tests, and Story 001's `CrossesThreshold` test. Fixed all 6 total instances (2 in this story + 4 in previously "Complete" stories) with the same pattern Story 003 already established: extend the array by one element, add a high sentinel value.
- This is a concrete demonstration of why static-only review has real limits: two full rounds of parallel self-performed code review (Stories 001-003) all rated these files clean, but none of them actually traced the interaction between Story 003's later loop addition and Stories 001/002's earlier test arrays sized before that loop existed. Elevated the "run a live Unity Editor" recommendation in this story's Test Evidence section accordingly — this isn't the first time this session a real gap was found only by reading code carefully (MagicDefense, SetCurrentHP), but it's the first time the gap was in TEST code across multiple already-closed stories simultaneously, which static per-story review is structurally weaker at catching.
- Also found (independently, hand-verified): a genuine GDD arithmetic error in `leveling-system.md`'s AC-LS-43 worked example (states 10,168; the correct value is 10,280) — logged as TD-033, non-blocking since the test only asserts the post-clamp value.
- Also fixed: missing `.meta` file for the new test file (both reviewers flagged this independently).
- New production: one addition to `LevelingService.ExecuteLevelUpSequence` (CR-2.2a XP clamp, ~4 lines).
- Test count: 5 new (`LevelingSystem_TierTransitionRecompute_tests.cs`) — epic total now 23 across 4 Complete stories (7+4+7+5). Test counts in the 3 other files unchanged by the array-sizing fixes (only array contents changed, not test structure).
- No live Unity Editor available this session — static verification only, as it has been all session. Given today's finding, treat this as a firmer blocker before calling any of these 4 stories launch-ready than previously stated.
- Not committed to git yet. Uncommitted work now spans: 6 new Core-layer epics + 13 Leveling System stories (planning) + Stories 001-004 fully implemented, including the Character Stats fixes (`MagicDefense`, `SetCurrentHP`/`SetCurrentMP`) and now also the cross-story test-array sentinel fixes.
- Next: Story 005 (Free Point Allocation) is next in sequence — note it will need to build out the REAL `AllocateFreePoint` (Stories 002/003 only built a busy-rejection stub that throws `NotImplementedException` for the real path). Given the pattern of real discoveries has now held for all 4 stories in this epic, continue budgeting extra context-load and code-review time — and consider proactively re-auditing threshold arrays in any NEW test file against the CR-2.9 loop's post-level-up lookup requirement before writing it, rather than discovering it again via review.

## Session Extract — /dev-story + /code-review 2026-07-22 (Leveling System Story 003 — Consecutive Level-Up & Re-Entrancy Guards — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 Required Change→fixed + qa-tester 1 Required Change→fixed + 1 Suggestion→applied, both parallel, 0 BLOCKING from either) served as this story's review.
- Story: `production/epics/leveling-system/story-003-consecutive-level-up-reentrancy.md` marked Complete, all 5 AC checkboxes ticked. `EPIC.md` Story 003 row updated to Complete.
- **This was the trickiest control-flow story in the epic so far** (re-entrancy + consecutive-level looping + deferred broadcast) — I asked the implementing agent to show me the full design/pseudocode before writing any code, not just before writing files, given the complexity.
- **The implementing agent found a fourth real gap this epic** (after Story 001's `ILevelingService` mismatch, Story 002's `MagicDefense`/`CurrentHP` fixes): the GDD's literal EC-LS-10/AC-LS-49 scenario ("a subscriber calls `AddExperience` from inside an `OnStatChanged` handler") is now structurally impossible — `CharacterStats`'s own pre-existing `_isFiring` guard (Story 005, already closed; a single class-wide flag, not scoped per-stat) throws `InvalidOperationException` on ANY write during ANY handler invocation. I independently verified this by reading `IsFiringAndAssert`'s source directly before approving anything. **Resolved by splitting AC-LS-49 into two tests**: (a) a regression test proving the GDD's literal scenario genuinely throws (confirms existing behavior, not assumed), (b) a test proving `LevelingService`'s own new `_levelingUpInProgress` guard handles the real, still-reachable case (a DIRECT reentrant `NotifyExperienceCrossedThreshold` call, bypassing `AddExperience`). This is a GDD documentation gap, not a bug — noted in the story's completion notes as something to raise with whoever owns `leveling-system.md`.
- **Bonus find, logged as TD-032**: while researching this codebase's existing broadcast-event precedents, the implementing agent noticed `CurrencySystem.OnGoldSync` (a different, unrelated, already-existing system) uses a bare `?.Invoke()` with no per-subscriber exception isolation — violating the same ADR-010/CR-2.10 discipline `OnLevelUp` correctly follows. Deliberately did not copy that flawed pattern. Not fixed (out of scope, different epic) — logged as tech debt with the exact fix pattern already established by `LevelingService.FireOnLevelUp` as the reference.
- Independently re-verified the `_isFiring` claim myself (read `IsFiringAndAssert`'s source directly) before approving the design, matching this session's established "verify before trusting agent claims" discipline for every non-trivial finding.
- New production: `LevelingService.NotifyExperienceCrossedThreshold` (rewritten — wraps Story 002's *unmodified* `ExecuteLevelUpSequence` in the real CR-2.9 loop), `ILevelingEventBroadcaster`/`LevelUpEventArgs` (new ADR-010 Tier-2 broadcast, ordinary ADR-010-style ` event Action<T>` + manual per-subscriber try/catch — NOT `CharacterStats`'s fixed-array pattern, simpler was sufficient), `AllocateFreePointResult` + a minimal `AllocateFreePoint` stub (busy-check only, throws `NotImplementedException` for the unimplemented Story 005 path — chosen over a fake-success/placeholder enum value to keep the scope boundary impossible to miss).
- Code review found and fixed 2 Required Changes: (1) unity-specialist caught 4 new files missing their Unity `.meta` companions — generated fresh GUIDs and added them myself; (2) qa-tester caught that `heldFreePoints` accumulation across MULTIPLE loop iterations within a single call wasn't directly tested (only single-iteration and cross-call accumulation were) — added an assertion to the existing 3-level AC-LS-08 test rather than a new test method. Also applied qa-tester's style suggestion (split a combined Warrior/Healer test into two, matching established one-scenario-per-method convention).
- Test count: 7 (`LevelingSystem_ConsecutiveLevelUpReentrancy_tests.cs`, up from 6 after the split) — independently grep-verified both before and after my fixes.
- Epic running total: 25 tests across 3 Complete stories (7+7+... wait: Story001=7, Story002=4, Story003=7 → 18 total; noting for accuracy — recount at next checkpoint).
- No live Unity Editor available this session — static verification only throughout.
- Not committed to git yet. Uncommitted work now spans: 6 new Core-layer epics + 13 Leveling System stories (planning) + Stories 001/002/003 fully implemented, including fixes to 2 already-closed Character Stats files (`StatID.cs`/`StatSchema.cs` for `MagicDefense`, `CharacterStats.cs` for `SetCurrentHP`/`SetCurrentMP`) — Story 003 itself did NOT touch Character Stats further, confirmed.
- Next: Story 004 (Tier Transition From-Scratch Recompute & Raw-Write Ceilings) is next in sequence — depends on Story 002's `ExecuteLevelUpSequence` (proven correct at the three tier boundaries). Given this epic has now surfaced FOUR real gaps/discoveries across its first three stories (one per story so far), continue budgeting extra context-load time — the pattern remains highly plausible for Story 004 too.

## Session Extract — /dev-story + /code-review 2026-07-22 (Leveling System Story 002 — Level-Up Sequence Core — COMPLETE)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist CLEAN + qa-tester CLEAN, both parallel, 0 BLOCKING from either) served as this story's review. Both independently found the same 1 trivial Required Change (a stale doc-comment reference to the removed `AttachLevelProvider` name, left over from the mid-implementation consolidation) — fixed.
- Story: `production/epics/leveling-system/story-002-level-up-sequence-core.md` marked Complete, all 4 AC checkboxes ticked. `EPIC.md` Story 002 row updated to Complete.
- **This story surfaced two MORE real gaps in already-closed Character Stats files** (bringing the epic's running total to three, after Story 001's `ILevelingService` mismatch) — both found by the implementing agent tracing real code before writing anything, both escalated via `AskUserQuestion` before any fix was applied, both resolved as small additive changes:
  1. **`MagicDefense` missing entirely from `StatID`**: `character-stats.md`'s own GDD (F-7) always required it; the enum never had it. **I applied this fix myself directly** (small, mechanical, well-scoped — not delegated): inserted `StatID.MagicDefense = 7`, renumbering 5 int-schema members + 5 float-schema members by +1. Verified safe via full-repo grep (no hardcoded numeric `StatID` literals anywhere) before applying, and confirmed no other file was affected after.
  2. **`SetBaseStat(CurrentHP/CurrentMP, ...)` silently broken**: `CurrentHP`/`CurrentMP` are stored in separate dictionaries, never in the `_statValues` array `SetBaseStat` writes to — a literal implementation of the GDD's CR-2.7 instruction would have silently failed AC-LS-06. The implementing agent found this, proposed 3 options, and I approved adding new additive `CharacterStats.SetCurrentHP`/`SetCurrentMP` methods (with two technical notes: clamp against `GetEffectiveStat` not `GetBaseStat`, matching `ApplyDamage`/`ApplyRegen`'s convention; fire `OnStatChanged` for consistency with this codebase's "every write is observable" pattern). Confirmed via `git diff` as a pure 48-line insertion, 0 deletions.
- **Design simplification caught mid-implementation**: Story 001's `AttachLevelProvider` delegate (deliberately narrow, added specifically to avoid a full `CharacterStats` back-reference) became redundant once Story 002 needed that full reference anyway for `SetBaseStat`/`SetBaseStatFloat`/`SetCurrentHP`/`SetCurrentMP`. Consolidated into one `AttachCharacterStats` reference; Story 001's test file updated to match rather than leaving two parallel wiring mechanisms — I explicitly asked the agent not to leave this undiscussed if they went that route, and they reported back on it clearly.
- **Independently re-verified two pieces of math myself** before approving each design round: the AC-LS-03 tier-multiplier distinction (AttackPower 127 at ×1.2 vs 106 at ×1.0 — recomputed by hand, matched), and confirmed via direct grep (not trusting the agent's claim) that the `MagicDefense`/`CurrentHP` fixes were genuinely safe before either was applied.
- One background-task tooling gap recurred this session (previously seen once, Story 023): a qa-tester completion notification arrived with only usage stats, no `<result>` content. Resolved the same way as before — sent a message asking the agent to restate its findings, which worked cleanly on retry, no information lost.
- New production: `LevelingService.ExecuteLevelUpSequence` (real CR-2.1–CR-2.7 sequence), `ClassDefinition`/`IClassRegistry`/`ClassRegistry` (new Class System forward-dependency mocks), `CharacterStats.SetCurrentHP`/`SetCurrentMP` (additive fix).
- Test count: 4 new (`LevelingSystem_LevelUpSequenceCore_tests.cs`) + Story 001's 7 (updated, not expanded) = 11 total in this epic so far. Independently grep-verified.
- No live Unity Editor available this session — static verification only.
- Not committed to git yet. This session's uncommitted changes now span: 6 new Core-layer epics + 13 Leveling System stories (planning), plus Stories 001 and 002 fully implemented (including 2 fixes to already-closed Character Stats files: `StatID.cs`/`StatSchema.cs` for `MagicDefense`, `CharacterStats.cs` for `SetCurrentHP`/`SetCurrentMP`).
- Next: Story 003 (Consecutive Level-Up & Re-Entrancy Guards) is the natural next pick — depends on Story 002's `ExecuteLevelUpSequence` (wraps it in the CR-2.9 loop) and Story 005 (tests `AllocateFreePoint` rejection, which doesn't exist yet — sequence 002→005→003, or Story 003 can stub it). Given this epic has now surfaced three real gaps in "closed" work across its first two stories, budget extra context-load time for Story 003 too — same pattern is plausible again.

## Session Extract — /dev-story story-002 in progress 2026-07-22 (Leveling System Story 002 — Level-Up Sequence Core)

- **Mid-implementation checkpoint** — Story 002's `/dev-story` context-load surfaced a third real discrepancy this epic (after Story 001's two): `character-stats.md`'s own GDD (F-7) always required a `MagicDefense` int-schema stat, range `[0,9999]`, written by the Leveling System on every level-up — but the already-Complete Character Stats `StatID` enum never had a `MagicDefense` member at all. Confirmed via full-repo grep that nothing hardcodes numeric `StatID` literals, so it was safe to fix.
- **User decision (AskUserQuestion)**: add `MagicDefense` to `StatID`/`StatSchema` now, rather than skip it or escalate. **Applied directly by me** (small, mechanical, well-scoped — not delegated to the implementing agent): inserted `StatID.MagicDefense = 7` into the int-schema block in `src/Foundation/CharacterStats/StatID.cs` (between `Defense` and the resource-pool group), which renumbers `CurrentHP`(8)/`MaxMP`(9)/`CurrentMP`(10)/`Level`(11)/`Experience`(12) and all 5 float-schema members (`CritChance` 12→13 through `MovementSpeed` 16→17) by +1. Added `MagicDefense` → `[0f, 9999f]` to `StatSchema.GetStatMin`/`GetStatMax`. Verified safe: `StatArraySize`/`FloatStatArraySize`/`FloatStatStart` are all computed from enum member references (not hardcoded numbers), and no test or production code anywhere hardcodes a raw numeric `StatID` literal — confirmed via grep before and after.
- Also corrected Story 002's own text (`production/epics/leveling-system/story-002-level-up-sequence-core.md`) to use the real `StatID` member names (`Strength`/`Dexterity`/`Vitality`/`Intelligence`, not the GDD's `STR`/`DEX`/`VIT`/`INT` abbreviations) throughout Implementation Notes and QA Test Cases, and to note the `MagicDefense`/`SetBaseStat` vs `CritChance`+`AttackSpeedMultiplier`/`SetBaseStatFloat` int-vs-float-schema distinction explicitly (the story's original formula list didn't distinguish which API each formula's output needs).
- Confirmed via grep that none of `IClassRegistry`, `ClassDefinition`, or `ITestableCallOrderObserver` (all referenced in Story 002's ACs) exist anywhere in this codebase — genuine forward dependencies, briefed the implementing agent to design minimal mocks/seams for each rather than treating them as blockers.
- Spawned `gameplay-programmer` (background) with the full corrected context (formulas, exact `StatID` names, sequence order, forward-dependency mocking guidance, explicit permission to stop and ask again if a third ambiguity surfaces — two rounds of real discoveries already happened on this epic, a third is plausible).
- Not committed to git yet (this includes the `StatID.cs`/`StatSchema.cs` fix, uncommitted alongside everything else this session).
- Next: awaiting the implementing agent's completion notification. After that: independent verification (grep test count, spot-check `MagicDefense` write correctness), then self-performed parallel code review (unity-specialist + qa-tester), then close the story per the established pattern.

## Session Extract — /story-readiness + /dev-story + /code-review 2026-07-22 (Leveling System Story 001 — XP Accumulation & Threshold-Crossed Notification)

- Verdict: COMPLETE. Review mode `lean` — self-performed `/code-review` (unity-specialist CLEAN + qa-tester CLEAN, both parallel, 0 BLOCKING + 0 Required Changes from either) served as this story's review.
- Story: `production/epics/leveling-system/story-001-xp-accumulation.md` marked Complete. `EPIC.md` Story 001 row updated to Complete.
- **`/story-readiness` first pass found real gaps**: missing `Estimate` field and missing performance note — turned out to be systemic across all 13 stories in this epic (I'd omitted both fields from the template while drafting the whole batch). Fixed across all 13 in one pass, plus a story-001-specific `ADR Governing Implementation: None` vs. Control-Manifest-Rules-cites-ADR-010 inconsistency. Second `/story-readiness` pass: READY.
- **`/dev-story`'s context-load phase surfaced a real, more serious problem**: `leveling-system.md`'s own CR-1 text (and this story, copied from it) described an `ILevelingSystemListener`/`RegisterListener()`/`OnExperienceThresholdCrossed` API. The ALREADY-COMPLETE, ALREADY-CLOSED Character Stats Story 006 implements a completely different, already-decided contract: `ILevelingService` (constructor-injected, methods `IsPlayerEntity`/`GetExperienceThreshold`/`NotifyExperienceCrossedThreshold`). Found by reading the real `CharacterStats.cs`/`ILevelingService.cs` source directly rather than trusting the GDD prose — confirmed via full-file grep that `AddExperience` also has a genuine gap vs. the GDD: AC-LS-40 requires "error logged" on `amount<=0`, but the real code has no logging call there at all.
- **User decision (AskUserQuestion)**: relax AC-LS-40 for this story (drop the log assertion) rather than modify Character Stats' closed code. Logged as **TD-031** (register now 31 items) with both resolution options (add the log to Character Stats later, or correct the GDD text) left open for a future session.
- Rewrote Story 001's Acceptance Criteria, Implementation Notes, and QA Test Cases in place to match the real `ILevelingService` contract before any code was written — left a clear pointer for Story 002/003 (which still reference the old "OnExperienceThresholdCrossed fires" terminology throughout) to substitute "`NotifyExperienceCrossedThreshold` is called" when they're picked up next.
- **Implementing agent (gameplay-programmer) found a second real problem I hadn't anticipated**: a genuine circular dependency — `CharacterStats`'s constructor requires `ILevelingService`, but `GetExperienceThreshold` needs to read `Level` back from `CharacterStats`. Agent proposed two resolutions and paused for a decision rather than guessing; I chose the narrower option (settable `Func<EntityID,int>` delegate field, `AttachLevelProvider`, wired post-construction) over a full `CharacterStats` back-reference, matching ADR-010's minimal-interface spirit. Also resolved: file placement (`src/Foundation/LevelingSystem/`, not a new `src/Core/` assembly — matches Networking Core's own precedent, avoids inventing an unreviewed architecture split without an ADR) and test-observability (a simple call-counter on the production stub, not a full observer-pattern seam, since Story 002 will replace the stub body anyway).
- Both parallel code reviewers independently re-verified the "is the wiring lambda a persistent-subscription violation of ADR-010 Decision 4?" question directly against the ADR text (not just the code's own comment) and confirmed it is not — a one-time startup delegate assignment, not an `event +=` with an unsubscribe lifecycle.
- New production: `LevelingService` (`src/Foundation/LevelingSystem/LevelingService.cs`) — first class in the Leveling System epic.
- Test count: 7 (`tests/EditMode/LevelingSystem/LevelingSystem_XpAccumulation_tests.cs`) — independently grep-verified, matches self-report. Genuinely integration-style per the story's requirement (real `CharacterStats` + real `LevelingService`, `CharacterStats` never mocked).
- No live Unity Editor available this session — verification is static only (grep counts, hand-read assertions, 2 parallel specialist reviews). Story file notes this explicitly.
- Not committed to git yet.
- Next: Story 002 (Level-Up Sequence Core) is the natural next pick — depends on this story's `LevelingService`/`AttachLevelProvider` wiring and must replace the `NotifyExperienceCrossedThreshold` stub with real CR-2 logic. Before starting it, remember the terminology correction (OnExperienceThresholdCrossed → NotifyExperienceCrossedThreshold) needs applying to Story 002/003's text too, same as was just done for Story 001.

## Session Extract — /create-stories leveling-system 2026-07-22

- Ran `/create-stories leveling-system`. Loaded epic + full GDD (48 core rules, 38 edge cases, ~50 ACs across 7 groups — unusually large/rigorous) + ADR-010 (confirmed Accepted on disk). Decomposed into 13 stories mirroring the GDD's own 7 AC groups plus a dedicated UI/manual-verification story. Presented full list via `AskUserQuestion`; user approved "write all 13." QL-STORY-READY gate skipped (lean mode). Updated `EPIC.md`'s Stories table and expanded its GDD Requirements table from 6 to 13 TR-lvl-XXX rows.
- **Story 010 (XP Threshold Formula & Table) written Blocked** on OQ-LS-7 (`GetXPAward` unspecified) — the GDD's own text says implementation must not start before this resolves. Confirmed the rest of the epic does NOT transitively depend on it: Story 008's sentinel/bounds test uses a small test-local fake `XpThreshold` array, not the real economy-signed-off table.
- **Story 007 (Respec Two-Phase Commit) written Ready but with its AC-LS-18b sub-case explicitly deferred** — BLOCKED on OQ-LS-3 (Status Effects GDD must define "combat-tagged" first). AC-LS-18a (the stub-based sub-case) is unblocked and in scope now. Story 007 also has no real Inventory System to call against yet (that epic exists but has no stories) — tests use a test-double standing in for the Inventory System's Phase 1/Phase 2 orchestration, matching this project's established Networking Core mock-provider precedent.
- **Two dependency-direction errors I caught and fixed while drafting**: (1) Story 008 originally listed "Unlocks: Story 010" — backwards, since AC-LS-25 only needs a small test-local array, not Story 010's blocked real table; fixed to explicitly state Story 008 does NOT depend on Story 010. (2) Story 003's AC-LS-10 tests `AllocateFreePoint` rejection, which Story 005 actually implements — Story 003 originally only listed Story 002 as a dependency; added Story 005.
- Multiple stories (002, 006, 011) flag `IClassRegistry`/`ClassDefinition` as a forward dependency — confirmed via `find src -iname "*ClassRegistry*"` that no implementation exists yet and Class System has no epic (Feature-layer, not yet in scope). Stories are written to mock this per the established pattern, not blocked by it.
- Story 013 (HUD/UI) cites ADR-005 (HUD UI Framework) — confirmed the file exists on disk (`docs/architecture/ADR-005-hud-ui-framework-ui-toolkit.md`) before referencing it.
- Not committed to git yet.
- Next: `/story-readiness production/epics/leveling-system/story-001-xp-accumulation.md` to start implementation, working roughly in dependency order (001→002→003/005→004→006→007→008→009→011→012→013; Story 010 stays parked until OQ-LS-7 resolves externally). This epic also unblocks Character Stats Story 008 once enough of it lands — check that story's own ACs against what's been implemented as stories close.

## Session Extract — /create-epics layer: core 2026-07-22 (post Networking Core epic completion + commit/push)

- Committed and pushed the full Networking Core epic (Stories 010-029, 216 files) as `0772226`, after independently reviewing `git status` for junk files (`bash.exe.stackdump`, `scratchpad-unity-log.txt` — deleted per user request, not committed) and running a secret scan on the staged diff (clean). Reminded the user again about the plaintext GitHub PAT embedded in `origin`'s remote URL (see `project_exposed_git_credential` memory) before pushing.
- User asked to "start the next epic." Investigated `production/epics/index.md` (found stale — claimed Character Stats/Item Database/Currency System stories were "Not yet created" when in fact 001-007 of Character Stats and all of Item Database/Currency System were already Complete per their own story files; the index just hadn't been updated). Fixed `character-stats/EPIC.md`'s own story table too (showed "Ready" for Stories 003-007 when their own files said "Complete" — propagation drift, not a real blocker).
- Per `systems-index.md`'s Dependency Map (the authoritative layer classification, per `epics/index.md`'s own note), the Core layer's 6 systems are: Authentication, Damage Calculation, Leveling System, Inventory System, Loot Table System, Status Effects/Buffs. Confirmed all 6 have zero epics yet and their Foundation-layer dependencies are all Complete (no epic-level blockers).
- Ran `/create-epics layer: core`. Presented all 6 candidate epics with ADR-coverage findings in one batched `AskUserQuestion` (user picked "All 6"). Wrote all 6 `EPIC.md` files + updated `production/epics/index.md`.
- **Two real architecture gaps surfaced during epic definition, not present in architecture.md's blanket LOW-risk characterization**:
  1. **Damage Calculation epic is blocked before Story 001**: the GDD's own Core Rules text explicitly states an ADR for the `ServerLogic.asmdef` server/client assembly boundary "must be authored before implementation begins" — no such ADR exists. This contradicts architecture.md's "🟢 LOW / by-design no ADR" summary for this system (that summary covers formula/gameplay risk, not the anti-cheat assembly-exclusion mechanism the GDD itself flags as blocking).
  2. **Authentication epic is partially blocked**: CR-AUTH-4 specifies a standalone .NET sidecar process (separate from the Unity IL2CPP binary) communicating via internal IPC — no Accepted ADR covers this, and its two primitive GDDs (`auth-wire-messages.md`, `auth-sidecar-ipc.md`) are both still Draft. Only sidecar/IPC-touching stories are affected; the AccountID/credential-contract stories are not blocked.
- Leveling System epic explicitly flagged as unblocking Character Stats' own Story 008 (currently Blocked) — recommended as a near-term priority pick within this new Core-layer batch.
- PR-EPIC producer gate skipped (lean mode, per `production/review-mode.txt`).
- Not committed to git yet.
- Next: run `/architecture-decision` for the Damage Calculation assembly-boundary ADR before that epic's Story 001. Then `/create-stories [epic-slug]` per Core-layer epic — Leveling System first (unblocks Character Stats Story 008), or user's choice among the other 5.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-22 (Networking Core Story 029 — SetTarget RPC & Target Slot Management — EPIC COMPLETE, all 29 stories)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — self-performed `/code-review` (unity-specialist 1 Required Change→fixed + qa-tester 0 findings, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-029-settarget-rpc-target-slot-management.md` marked Complete. `EPIC.md` Story 029 row updated to Complete. **This closes the entire Networking Core epic — all 29 stories (001–029) are now Complete.**
- `/story-readiness` found no blockers — clean pass (the only note: `TR-net-003` isn't in `tr-registry.yaml`, but that registry is empty project-wide across all 20 stories in this epic that reference `TR-net-XXX`, a pre-existing systemic gap never treated as a blocker; Story 029 also did NOT have the Type/Test-Evidence self-contradiction Stories 027/028 had, confirming that was isolated to those two).
- New production: `SetTarget`/`SetTargetCodec` (standalone C→S R-OD message, reuses the existing `ClientEntityMessageEnvelope` rather than inventing a new envelope type; `MessageTypeId = 0xE040`; deliberately bypasses `WireIdCodec.SerializeEntityId` for the `targetEntityId` body field since `0` = "deselect" is a legitimate wire value there, unlike every other EntityID field in the protocol — a real footgun I flagged explicitly to the implementing agent up front since it's the opposite of this codebase's default CR-NET-7.3 zero-write-guard convention), `SetTargetOutcome` (3-value enum), `TargetSlotTracker` (new sealed **stateful** per-client target-slot store — the first stateful class in this RFR cluster, unlike Story 028's stateless-static `RelevanceFilter`). `RpcTypeTag`/`CrossCuttingRpcGuardChain` (Story 010) extended with a `SetTarget` rate-limit bucket at gap=0 ticks (GDD's own Cross-Cutting Constraint 3: "All other RPCs: no rate limit specified at MVP" — verified this actually behaves as unconstrained against `StaleDiscardComparer.IsTickExpired`'s equality-is-expired semantics before committing to it). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnSelfTargetAttemptLogged`/`OnInvalidTargetEntityIdLogged`.
- **Design decisions I resolved before implementation** (given to the implementing engine-programmer agent as settled, not re-derived by it): (1) `TargetSlotTracker` does not call `CrossCuttingRpcGuardChain` itself — transport-boundary rejection and RFR-specific business validation stay separately layered, composed by the caller, matching `MobDeTargetingCoordinator`/`PartyDisbandCoordinator`'s established delegate-seam precedent; (2) AC-RFR-03's before-flush/after-flush timing claim needs no tick-boundary machinery — proven purely by call-ordering in the test, matching this epic's "structural ordering proof, not a real timer" idiom; (3) `validZoneEntityIds` is a plain caller-supplied collection, not an injected provider — no real zone entity registry exists yet (same forward-dependency treatment as `RelevanceFilter`'s `partyMembers`).
- **Process note**: made the exact coordination mistake this session's own history already warned about (Story 022's note: "don't relay design approval via a fresh `Agent` spawn — resume the same instance via `SendMessage`"). Repeated it here: spawned a fresh `Agent` to say "yes approved," which correctly refused (no memory of the plan it never saw). Corrected by using `SendMessage` to the original agent's ID. Re-noting this for future sessions since it recurred once already despite being documented.
- Code review found 0 BLOCKING + 1 Required Change, applied: unity-specialist caught that the self-target-before-zone-validity check order (RFR-5 checked before RFR-3a) was correctly implemented but not actually pinned by any test — the original AC-RFR-05 test's `validZoneEntityIds` fixture happened to include the client's own EntityID, so both possible check orderings would have produced the same passing result. Fixed by adding `ProcessSetTarget_SelfTargetAndNotInValidZone_RejectedAsSelfTarget_NotAsInvalidTarget`, which deliberately excludes self from `validZoneEntityIds` so only the correct order passes. qa-tester's parallel review found 0 findings — confirmed all 3 blocking ACs are genuinely (non-tautologically) proven, including AC-RFR-03's timing distinction, the one most susceptible to a trivially-passing test.
- Test count: 11 (`tests/EditMode/Networking/RelevanceFilter_SetTargetRpc_tests.cs`) — independently grep-verified by me both before and after the fix (10→11), matches self-report.
- **No live Unity Editor was open/available this session** (unlike Story 028) — verification is static only (grep-confirmed counts, hand-read assertions, 2 parallel specialist reviews). Story file's Test Evidence section notes this explicitly and recommends a live-Editor confirmation pass before treating this story as launch-ready.
- Tech debt: TD-030 logged (register now 30 items) — the combined "party-set change + `SetTarget` same-tick" ordering scenario from this story's own Implementation Notes is untestable until a real Party System exists; correctly deferred per qa-tester's proactive flag, not a gap in this story.
- Not committed to git — the entire epic (29 stories) remains uncommitted in the working tree, as it has been all session.
- Next: **The Networking Core epic's full story set (001–029) is now Complete.** No further stories are queued in this epic. Recommend: (1) a live Unity Editor full-suite run to confirm everything compiles and passes together, now that all 29 stories are code-complete; (2) `/team-qa sprint` or a milestone review before considering this epic launch-ready; (3) decide whether to commit this large uncommitted working tree, and whether to start the next epic (per `production/epics/`, check what other epics exist or need `/create-epics`).

## Session Extract — TD-029 resolution 2026-07-21 (post-Story-028, user-driven, interactive Unity Editor)

- **TD-029 fully resolved this session** — the user opened the Unity Editor themselves (colliding with a batchmode attempt, confirming they had it open independently) and walked through the 5 pre-existing test failures one at a time by pasting Test Runner output; each was diagnosed and fixed in `tests/EditMode/Networking/`, none required production code changes:
  1. **AC-GH-14**: test only expected `RecordFailedReAuthAttempt`'s transition, missed that `EnterReconnecting` (called immediately before it) also fires its own `OnSessionStateTransitioned` — fixed to expect both, in order.
  2. **AC-GH-10** and **AC-GH-11** (same bug, found second one proactively once the pattern was clear): both asserted `callOrder` *after* appending `"persist"` instead of before, so the assertion always saw its own just-added entry as an unexpected extra — fixed by reordering assert-then-append.
  3. **AC-GH-7** (found proactively, same `GhostCleanupSequencer` cluster predicted in the original TD-029 entry): passed `targetingMobIds: Array.Empty<uint>()` while asserting a `"deTarget"` callback still fired — `MobDeTargetingCoordinator.ProcessTTLExpiry` only invokes the callback once per array entry, so an empty array can never produce it — fixed by supplying a real mob ID.
  4. **Snapshot tuple mismatch** (Story 018): `Assert.AreEqual((9, 9, 9), stored.RespawnPosition)` compared a compiler-inferred `(int,int,int)` literal against the field's actual `(short,short,short)` type — two different `ValueTuple` types with identical `ToString()`, hence the puzzling "Expected: (9,9,9), But was: (9,9,9)" — fixed with explicit `short` casts.
- TD-029 updated in `docs/tech-debt-register.md` from "Backlog" to "Closed," with per-fix root causes documented for future reference.
- My original TD-029 prediction (that AC-GH-10/AC-GH-11/AC-GH-7 shared one root cause) was half right: AC-GH-10/AC-GH-11 did share the identical test bug; AC-GH-7 turned out to be a distinct, unrelated test-setup bug in the same file cluster, not the same root cause.
- Not committed to git.
- Next: pick up Story 029 (SetTarget RPC & Target Slot Management) when ready, or have the user's own open Editor do a final full-suite confirmation of these 5 fixes.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 028 — Relevance Filter Algorithm — FIRST LIVE UNITY EDITOR VERIFICATION THIS SESSION)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist 1 BLOCKING→fixed + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-028-relevance-filter-algorithm.md` marked Complete. `EPIC.md` Story 028 row updated to Complete (also corrected Type column Integration→Logic).
- **`/story-readiness` found the exact same Type/Test-Evidence self-contradiction Story 027 had** (header Logic + EditMode QA Test Cases vs. Test Evidence section demanding PlayMode/playtest) — same fix applied, TD-028 updated to note the recurrence across two consecutive stories (possibly a stale template pattern; Story 029 checked and does NOT have the issue, so not universal).
- **MAJOR SESSION EVENT: a live Unity 6.3.10f1 Editor became available in this environment for the first time**, discovered when the implementing agent used it to attempt a real test run. This is a first for this entire session — every prior story (025-027) was verified only statically. Found and fixed (with user approval) 3 pre-existing compile errors blocking ALL EditMode compilation, none related to Story 028: `SessionTokenStore.cs` (Story 016) used `RandomNumberGenerator.GetBytes(int)`, a .NET 6+ static overload Unity's runtime doesn't expose — replaced with `RandomNumberGenerator.Create().GetBytes(buffer)`; `SessionToken_NSCRT_tests.cs` (Story 016) was missing `using System.Security.Cryptography;`; `MessageRouting_SelfDamageExclusivity_tests.cs` (Story 027) captured a `Span<byte>` ref-struct local inside a lambda (C# forbids this) — changed to `byte[]`.
- New production: `EntityHealthUpdate`/`PartyMemberHealthUpdate` (new concrete R-U batch sub-message schemas, first implementation — previously only opaque `RUBatchCategory` entries; `MessageTypeId = 0x0304`/`0x0305`), `RelevanceFilter` (the RFR-2 algorithm itself, stateless static class, `MAX_PARTY_SIZE=4` new constant). `BatchSubMessageCodec.cs` extended with matching `Write*`/`TryRead*` trios for both new types. Party membership modeled as a plain `IReadOnlyList<PartyMemberHealthUpdate>` (no provider interface), matching `PartyDisbandCoordinator`'s established mock-provider precedent from Story 020. `EntityHealthUpdate`/`PartyMemberHealthUpdate` reused as dual wire-schema/domain-input types for `RelevanceFilter`'s parameters, matching `DamageEvent`/`GoldSyncEvent`'s established pattern.
- **Registry completeness gap discovered and fixed, spanning 3 stories**: this story's own 2 new message types needed `MessageRoutingRegistry` rows (per MCR-2's own rule). While adding them, running the real live-Editor completeness test (`Registry_ContainsExactlyOneRowPerRealWireProtocolMessageTypeId_AC_MCR_04_AC_CCR_09`, Story 025) for the first time revealed Story 026's `GoldSyncEventForcedDelivery` and Story 027's `SelfDamageEvent` were ALSO never registered — an invisible gap since this test could never execute before. Added all 4 rows, cross-referenced against MCR-2/CCR-3 GDD text directly. Code review caught one real error in my own additions: `PartyMemberHealthUpdate`'s direction was set to `MessageDirection.ServerToParty` (broadcast-identical-to-everyone semantic) when the GDD's literal "S→C (per-client for party)" requires `ServerToOwningClient` (per-client-tailored semantic, matching what `RelevanceFilter` actually implements) — fixed and re-verified via a full live test run.
- **TD-029 logged — a major, separate finding**: the first real test run surfaced 5 genuine, pre-existing failures in already-"Complete" Stories 018/020/021, undetected this entire session because nothing had ever compiled/run before. User explicitly chose to log as tech debt and continue rather than fix now. 3 of the 5 (AC-GH-10, AC-GH-11, AC-GH-14 pattern) likely share one root cause in `GhostCleanupSequencer`'s step-ordering (Stories 019-021's shared cleanup code) — full diagnostics captured in TD-029 for whoever picks it up. Confirmed via repeated live runs that all 5 are 100% unrelated to Story 028's own changes (725/730 passing both before and after Story 028's fixes, with the specific 5 failures identical throughout).
- Test count: 11 (`tests/EditMode/Networking/RelevanceFilter_HealthUpdateSets_tests.cs`) — independently verified via grep AND, for the first time this session, via genuine live-Editor pass/fail confirmation (not just static code reading).
- Tech debt: TD-029 logged (register now 29 items, "Bug"/High-impact category — distinct from this epic's usual "Test Debt"/Low-impact forward-dependency placeholders).
- Not committed to git.
- Next: Story 029 (SetTarget RPC & Target Slot Management), `Status: Ready`, no Type/Test-Evidence contradiction found. **Recommend running the live Unity Editor test suite as part of every future story's verification now that it's available** — static review alone already proved insufficient to catch the registry-direction bug and the 3 pre-existing compile errors this session.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 027 — SelfDamageEvent vs DamageEvent Delivery Exclusivity)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist 1 BLOCKING→fixed + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-027-selfdamageevent-damageevent-exclusivity.md` marked Complete. `EPIC.md` Story 027 row updated to Complete (also corrected its Type column from Integration to Logic, matching the story-file fix below).
- **`/story-readiness` found a real internal self-contradiction**: the story's header said `Type: Logic`, its QA Test Cases section pointed at an EditMode test file, but its own Test Evidence section separately declared `Story Type: Integration` requiring a `tests/PlayMode/...` test or documented playtest — the first story in this epic to name that tier at all, and inconsistent with every prior story. Asked the user (given this is a live MMORPG, the trade-off was worth surfacing rather than silently picking one): resolved by aligning this story with the established EditMode-composition precedent (matches the GDD's own AC-MCR-05/08 text naming `ITransportFaultInjector`/`INetworkTestObserver` as the expected mechanism), and logging **TD-028** as a separate, project-wide, unresolved question — whether this MMORPG needs a real PlayMode/multi-client Integration testing tier before launch. Not solved inside this story; needs a producer/technical-director decision.
- New production: `SelfDamageEvent`/`SelfDamageEventCodec`/`SelfDamageEventDispatcher` (the first real implementation of `SelfDamageEvent`, a standalone R-OD sibling of `DamageEvent`, mirroring Story 026's `GoldSyncEventForcedDelivery` shape; `MessageTypeId = 0xE030`), `SelfDamageSuppressionGate` (EC-MCR-2 non-arrival/stale-on-arrival suppression, reusing `StaleDiscardComparer.IsTickExpired`), `SelfDamageRecipientGuard` (EC-CCR-2 wrong-recipient client-side defense), `CycleTimerInterpolator` (the first client-render-oriented logic class in this codebase — linear extrapolation loss-tolerance for `CycleTimerBroadcast`, AC-MCR-05/08). `INetworkTestObserver`/`NetworkTestObserver` extended with one new callback (`OnSelfDamageDirectionViolationLogged`) — every other observer hook this story needed (`OnServerSelfDamageEventSerialized`, `OnClientSelfDamageEventReceived`, etc.) was already pre-built by Story 002, unused until now.
- **Design decision I resolved before implementation, given to the implementing agent as settled**: `SelfDamageEventCodec`'s body encoding is deliberately duplicated from (not factored out of) `BatchSubMessageCodec.WriteDamageEvent`, unlike Story 026's shared-body-method precedent for `GoldSyncEvent` — because this story's Out of Scope explicitly forbids touching `DamageEvent`'s existing R-U path, and the story already spans enough new files without adding another out-of-scope file to the diff. Duplication cost accepted as small since both paths already share the true error-prone primitives (`WireIdCodec`, `WireEnumCodec`, `BinaryPrimitives`).
- **A genuine off-by-one bug I caught myself by cross-referencing the GDD's exact wording**, before formal code review: `SelfDamageSuppressionGate.IsStaleOnArrival` reused `StaleDiscardComparer.IsTickExpired`'s native "true at equality" semantics, but the GDD's EC-MCR-2 text has two suppression rules with *different* boundary semantics — rule 1 ("within 5 tick periods... suppress") has no strict qualifier (inclusive, correct as originally implemented), rule 2 ("more than 5 ticks older... suppress") has an explicit strict qualifier that the inclusive `IsTickExpired` reuse violated at exactly 5 ticks stale. Both the implementation and its own test agreed with each other but disagreed with the GDD. Fixed via a `+1` adjustment to `IsStaleOnArrival`'s `expiryTick` computation, converting the helper's native `>=` into the strict `>` the GDD requires; both specialists independently re-derived this same asymmetry from the GDD text during formal code review and confirmed it correct.
- Code review found 1 BLOCKING + 2 Required Changes (including the boundary fix above, applied pre-review), plus 1 more found during formal review, all applied: (1) **BLOCKING**, unity-specialist, independently hand-traced and confirmed by me: `CycleTimerInterpolator.RecordReceivedSample` had no stale/out-of-order guard despite `CycleTimerBroadcast` being delivered over U-U (ADR-004: explicitly "no ordering") — a reordered stale packet could silently make the charge bar jump backward, violating AC-MCR-08. Fixed by guarding with `StaleDiscardComparer.IsNewerVersion`, matching this codebase's established discipline for every other reordering hazard. (2) qa-tester: `SelfDamageEventDispatcher`'s structural "singleton recipient only" claim had no test (unlike the parallel, already-existing reflection test for `SelfDamageSuppressionGate`'s analogous claim) — added a matching reflection-based structural test.
- Test count: 17 (`tests/EditMode/Networking/MessageRouting_SelfDamageExclusivity_tests.cs`) — went through TWO self-report discrepancies this story (agent initially claimed "16," actual was 14; after the 2 post-review fixes agent again said "16," actual was 17) — independently verified via grep at every single stage by me and separately by both specialists, converging consistently on the correct number each time.
- Tech debt: TD-028 logged (see above — project-wide PlayMode/Integration testing tier gap, register now 28 items).
- Not committed to git.
- Next: Story 028 (EntityHealthUpdate/PartyMemberHealthUpdate Relevance Filter Algorithm) or Story 029 (SetTarget RPC & Target Slot Management), both `Status: Ready`. Note: `EPIC.md`'s Story 028 row also lists Type as "Integration" — worth checking during that story's own `/story-readiness` pass for the same kind of self-contradiction this story had.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 026 — GoldSyncEvent Forced-Delivery Overflow Policy)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — all gates skipped; self-performed `/code-review` (unity-specialist CLEAN + qa-tester GAPS, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-026-goldsyncevent-forced-delivery-overflow.md` marked Complete. `EPIC.md` Story 026 row updated to Complete.
- `/story-readiness` found a real, concrete blocker before implementation: both blocking ACs (AC-MCR-01, AC-MCR-07) depend on `IZoneTestConfigurator.SetBatchSizeLimit(int)`, confirmed absent from the interface. Resolved (user-approved) as an in-scope addition to this story, matching `SetZoneCapacity`'s existing sibling pattern.
- New production: `GoldSyncEventForcedDelivery` (field-less marker struct owning provisional `MessageTypeId = 0xE020`), `GoldSyncForcedDeliveryCodec` (standalone R-OD envelope+body wire encoding, no batch framing), `GoldSyncForcedDeliveryTracker` (the core two-counter stateful tracker — see below). `BatchSubMessageCodec.cs` refactored (behavior-preserving) to share `GoldSyncEvent` body-encoding between the R-U batch path and the new standalone path. `IZoneTestConfigurator`/`ZoneTestConfigurator` extended with `SetBatchSizeLimit`; `INetworkTestObserver`/`NetworkTestObserver` extended with 3 new callbacks.
- **Key design work — a two-counter reading of MCR-4, caught and corrected twice before any code was written**: the implementing engine-programmer agent first proposed a design, then during my design review I traced it against F-MCR-1's own rate formula (`TICK_RATE_HZ ÷ (GOLD_MAX_CONSECUTIVE_DROP+1)` = 5/s) and the GDD's literal "100 ticks (5 seconds at 20Hz)" anomaly-threshold text, and found the second counter (`TicksSinceForcedDeliveryRequired`) was defined to increment only on forced-delivery-*fire* ticks rather than every real elapsed tick — a 4x timing bug (would take ~20s instead of 5s to trigger the anomaly). The agent applied the fix, then independently caught and fixed a SECOND bug during its own pre-implementation reasoning (checking the threshold only inside the confirm-call, which under the verified 4-tick cycle mathematically never lands on tick 101). I hand-traced the corrected state machine myself afterward and confirmed both fixes are correct.
- The implementing agent also independently corrected a mistaken claim of mine: I had told it `IZoneTestConfigurator.SetZoneCapacity` was "wired to a real production consumer" (based on a loose grep match); the agent checked `ZoneSessionStateMachine.cs`'s own doc comment and found it explicitly states the opposite. I re-verified this myself and confirmed the agent was right — `SetBatchSizeLimit`'s honest "not yet wired" documentation (TD-027) is consistent with `SetZoneCapacity`'s actual (also unwired) status, not a weaker analogy as I'd assumed.
- Code review found 0 BLOCKING + 2 Required Changes, both applied: (1) qa-tester caught that the specific test built to pin the corrected two-counter design (`RecordRUDeliveryOutcome_RealisticFourTickSustainedOverflowCycle_...`) only asserted against the tracker's own internal counter — tautological, since it would still pass under the exact historical bug (just after ~3x more loop iterations). I independently hand-traced this and confirmed it — added a discriminating `Assert.AreEqual(103, safetyTickBound, ...)` real-tick-count pin. The regression was still incidentally caught by two other, simpler tests in the file, so this wasn't a BLOCKING gap, just a real gap in the test purpose-built for the job. (2) unity-specialist caught a doc-comment inaccuracy (claimed to mirror `GhostEntityTracker`'s "no teardown method" discipline, but that class does have `RemoveGhost`, Story 021) — corrected to describe this tracker's stronger, permanent-by-design guarantee instead.
- Test count: 10 (`tests/EditMode/Networking/MessageRouting_GoldSyncForcedDelivery_tests.cs`) — independently verified via grep both before and after the fix, matches self-report, stable across the fix.
- Tech debt: TD-027 logged (`SetBatchSizeLimit` zero-production-call-sites forward-dependency gap, same class as TD-020/TD-026 — register now 27 items).
- Not committed to git.
- Next: Story 027 (SelfDamageEvent/DamageEvent Delivery Exclusivity), `Status: Ready`, depends on this story's now-closed registry work (Story 025) — no dependency on Story 026 itself.

## Session Extract — /story-readiness + /dev-story + /code-review + /story-done 2026-07-21 (Networking Core Story 025 — Message Criticality/Channel Routing Table & Unclassified-Message Fallback)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-STORY-READY/QL-TEST-COVERAGE/LP-CODE-REVIEW gates all skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-025-message-routing-table-unclassified-fallback.md` marked Complete. `EPIC.md` Story 025 row updated to Complete.
- `/story-readiness` found one real gap before implementation: AC-CCR-01's literal CI text-search wording ("Given the full codebase and docs...") was self-contradictory — the forbidden phrase "server SessionHandshake" necessarily appears in the GDD rule (`networking-channel-contract.md` CCR-2) that defines the prohibition, so a literal repo-wide grep could never pass. Fixed by scoping the AC to `src/`+`tests/` only, excluding `design/gdd/`/`production/epics/`, before implementation began.
- New production: `MessageRoutingRegistry` (`src/Foundation/Networking/WireProtocol/`) — the unified MCR-2/CCR-3 routing table (one `MessageRoutingEntry` struct satisfies both AC-MCR-04 and AC-CCR-09 by construction), plus `DesignPillar`, `NetworkChannel`, `MessageDirection`, `MessageDeliveryContext`, `MessageRoutingEntry`, `MessageRoutingResult`, `PendingSchemaDispatchException`, `BuildConfiguration` (establishes this codebase's first debug/release build-symbol distinction, `DEVELOPMENT_BUILD`), `ConnectionSequenceCounter` (CCR-1's shared per-connection counter primitive). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnUnclassifiedMessageTypeLogged`.
- Key design decisions resolved before implementation (proposed by the implementing engine-programmer agent, reviewed and approved): registry scoped only to the 5 message types that already exist as real classes in `WireProtocol/` (no centralized `MessageTypeID` enum exists in this codebase — each class carries its own `const ushort`); AC-MCR-04/CCR-09's CI check and AC-CCR-01's naming check both implemented as in-file C# reflection/text-search scanners under the existing blocking EditMode test job, reusing Story 008's `AC-AOT-1` scanner precedent rather than adding new `.github/workflows/tests.yml` jobs; `GoldSyncEvent` flagged as an MCR-3 exception despite being single-pillar in the MCR-2 table, resolving a real (harmless) looseness in the GDD's own AC-MCR-06 "multi-pillar" framing.
- Code review found 1 BLOCKING + 2 Required Changes, both applied: (1) **BLOCKING**, caught by qa-tester: the AC-CCR-01 real-tree scanner test scanned its own defining file, which necessarily declares the literal forbidden phrase (`ForbiddenPhrase = "server SessionHandshake"` plus fixture/assertion strings) — guaranteed to fail every run; fixed by excluding the scanner's own file via `[CallerFilePath]` (not a hardcoded string, survives renames), independently re-verified via grep that zero occurrences remain anywhere else under `src/`/`tests/`; (2) private field `MessageRoutingRegistry.Entries` renamed to `_entries` (naming-convention fix, all 4 use sites). 5 additional Suggestions from qa-tester (untested struct equality/hashing, untested exception message content, scanner blind-spot documentation, etc.) were surfaced but declined at user's explicit direction ("apply the required changes") — not logged as tech debt, noted in Completion Notes instead.
- Test count: 28 (`tests/EditMode/Networking/MessageRouting_CriticalityChannelTable_tests.cs`) — independently verified via grep both before and after the blocking fix, matches self-report exactly, stable across the fix (fix modified an existing test + added one non-test helper method, no new `[Test]`).
- Tech debt: TD-026 logged (`ConnectionSequenceCounter` zero-production-call-sites forward-dependency gap, same class as TD-020 — register now 26 items).
- Test Evidence section's original wording (new `.github/workflows/tests.yml` CI scripts) was stale against the actual implementation approach taken — corrected in the story file to describe the in-file-scanner approach actually used.
- Not committed to git.
- Next: Story 026 (GoldSyncEvent Forced-Delivery & Overflow Drop Policy) or Story 027 (SelfDamageEvent/DamageEvent Delivery Exclusivity) — both `Status: Ready`, both depend on this story's now-closed registry.

## Session Extract — /code-review + /story-done 2026-07-20 (Networking Core Story 024 — OWL Compensation sub-cluster CLOSED — all 3 stories complete)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester TESTABLE, both parallel) served as this story's review.
- Story: `production/epics/networking-core/story-024-owl-threshold-hysteresis-signal.md` marked Complete. `EPIC.md` Story 024 row updated to Complete. **OWL Compensation sub-cluster (022-024) is now fully closed.**
- New production: `OwlThresholdHysteresisTracker` (`src/Foundation/Networking/OwlCompensation/`) — sealed per-entity stateful class implementing CR-NET-8.3's ON/OFF hysteresis signal, deliberately not calling into Story 022's `LastBeatServerTickTracker` or Story 023's `OwlWrapCorrectionFormula` (this class only decides whether compensation is active at all). New constants `MAX_COMPENSATABLE_OWL_SECONDS = 0.12f`, `OWL_HYSTERESIS_BAND_SECONDS = 0.015f`. Also extended `INetworkTestObserver`/`NetworkTestObserver` with `OnConnectionQualityUpdateEmitted`, resolving a real observer-seam gap caught during `/story-readiness` (the story's own QA Test Cases needed an emission-count assertion with no existing hook).
- Key design decision resolved before implementation: a first-ever OWL sample for an entity that itself crosses the entry threshold DOES emit (no "was this entity previously tracked" suppression gate) — confirmed correct, not just defensible, since no other message in this codebase conveys initial connection-quality state at handshake; a session starting already degraded would otherwise never inform the client.
- A `>`/`<` boundary "conflict" in my own story-readiness/dev-story instructions turned out to be a non-issue — float32 precision on the exact GDD literals (0.135f vs computed 0.13499999f; 0.105f vs computed 0.104999997f, bit-identical) makes strict `>`/`<` produce exactly the required boundary behavior. Both specialists independently re-verified this via IEEE-754 tracing.
- Code review found 2 Required Changes + 2 Suggestions, all applied: (1) added `EnterThreshold_ExactComputedValue_DoesNotTrigger_FloatPrecisionProof` — the original entry-boundary test used a literal one ULP *above* the threshold, so it couldn't actually distinguish `>` from `>=` (only the exit-side test, using a bit-identical literal, proved strict comparison); (2) added `FirstSampleForEntity_AboveEntryThreshold_EmitsImmediately` — the approved first-sample-crossing design decision was previously only incidentally proven inside an unrelated-named test; (3) added `RepeatedIdenticalSample_WhileAlreadyFlipped_DoesNotReemit` (idempotency guard); (4) added a doc-comment `owlSeconds` non-negativity assumption note, mirroring Story 023's `OwlWrapCorrectionFormula` precedent.
- Test count: 12 (`tests/EditMode/Networking/OwlCompensation_ThresholdHysteresis_tests.cs`) — independently verified via grep, matches self-report exactly (no discrepancy, third story in a row without one after Story 022's two wrong self-counts).
- Tech debt: TD-025 logged (Story 002's exhaustive-reset test not extended to cover 3 newer observer callbacks, including this story's — register now 25 items).
- Not committed to git.
- Next: Networking Core epic continues past the now-closed OWL Compensation sub-cluster (022-024) to Story 025 (Message Criticality/Channel Routing Table & Unclassified-Message Fallback), `Status: Ready`.

## Session Extract — /code-review + /story-done 2026-07-20 (Networking Core Story 023 — second story of OWL Compensation sub-cluster)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist CLEAN + qa-tester TESTABLE, both parallel, zero blocking findings) served as this story's review — cleanest story in this epic to date.
- Story: `production/epics/networking-core/story-023-owl-wrap-correction-formula.md` marked Complete. `EPIC.md` Story 023 row updated to Complete.
- New production: `OwlWrapCorrectionFormula` (`src/Foundation/Networking/OwlCompensation/`) — stateless static `Evaluate()` method implementing CR-OWL-2/F-OWL-1, composing with Story 022's `LastBeatServerTickTracker.IsWithinWrapCorrectionWindow` rather than reimplementing the sentinel guard. New `SkillGraceWindowResult` readonly struct (matches `SessionHandshakeData`'s plain-data-bundle precedent).
- Uses the established optional-observer convention (`INetworkTestObserver observer = null`, guarded `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`, matching `MobDeTargetingCoordinator`'s precedent) to satisfy AC-OWL-05 — `OnSkillGraceWindowEvaluated` and its recorder list were pre-built by Story 002 specifically for this story.
- AC-NC-29 (cross-document citation from `networking-core.md`) handled as a documented cross-reference to AC-OWL-01/02's tests rather than a duplicate — independently verified by qa-tester reading the GDD source directly (`networking-core.md:488` explicitly says "See AC-OWL-01 and AC-OWL-02").
- Code review found 0 Required Changes + 1 Suggestion (doc-comment only, no new test needed): note the implicit non-negativity/positivity assumptions on `cycleTimer`/`owlSeconds`/`cycleDuration` in `Evaluate`'s doc comment. Applied directly by the orchestrator (not delegated) since it was a trivial one-line addition to an already-reviewed file with direct, current user approval.
- Test count: 8 (`tests/EditMode/Networking/OwlCompensation_WrapCorrectionFormula_tests.cs`) — independently verified via grep, matches self-report exactly (no discrepancy this time, unlike Story 022). All 4 blocking ACs (AC-OWL-01, AC-OWL-02, AC-OWL-05, AC-NC-29) COVERED.
- **Notable process friction this session**: the implementing agent refused to accept ANY `SendMessage`-relayed approval as consent to write — including a message explicitly quoting "Manuel, the project owner, confirming directly" — since the channel itself is always agent-to-agent and indistinguishable from fabrication, regardless of framing. Resolved only by instructing the agent to invoke `Write` directly and let the harness's own permission prompt reach the user without going through any relay. **Lesson for future stories**: if an implementing agent holds this line, don't keep re-wording `SendMessage` approvals — tell it to attempt the tool call directly instead.
- Also hit a minor tooling gap: one specialist's (qa-tester) background-task completion notification arrived without its `<result>` content (only usage stats) — resolved by sending it a message asking it to restate its findings, which worked cleanly on retry.
- Tech debt: None logged (only finding was documentation-only, already resolved in-line).
- Not committed to git.
- Next: Story 024 (OWL Threshold Suspension & Hysteresis Signal) — third and final story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, depends on this story's `OwlWrapCorrectionFormula`.

## Session Extract — /story-done 2026-07-20 (Networking Core Story 022 — first story of OWL Compensation sub-cluster)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist + qa-tester, parallel) served as this story's review.
- Story: `production/epics/networking-core/story-022-lastbeatserverstick-slot-allocation.md` marked Complete. `EPIC.md` Story 022 row updated to Complete.
- New production: `LastBeatServerTickTracker` (`src/Foundation/Networking/OwlCompensation/`) — sealed per-zone-instance class implementing CR-OWL-1's `LastBeatServerTick uint[]` data structure + CR-OWL-4's player/mob slot allocation contract. New constants `MAX_MOBS_PER_ZONE = 150`, `MAX_WRAP_WINDOW_TICKS = 2`.
- Key design resolution (mine, given to the implementing agent as settled): the GDD's AC-OWL-03/04 text names "wrapCorrectionActive," but that's the full F-OWL-1 formula owned by Story 023. This story only owns the sentinel-guarded tick-window sub-check — `IsWithinWrapCorrectionWindow()` implements just that, with an honest doc-comment explaining the scoping (same idiom as every prior story this epic).
- Code review found 1 Required Change + 3 Suggestions, all applied: (1) `DeallocateMobSlot` was missing a "never allocated" guard (only checked double-free) — unity-specialist traced a concrete double-allocation hazard (an early-freed never-issued index could later be handed out again via the independent fresh-index counter); fixed by collapsing the guard to `!= InUse`, symmetric with `DeallocatePlayerSlot`'s existing guard; (2) added `AllocateMobSlot_WithMultipleFreedSlotsInterleaved_ReusesInFreedOrder` (locks in FIFO free-list order); (3) added `DeallocateMobSlot_LastPlayerSlotIndex_ThrowsArgumentOutOfRangeException` (true adjacent-boundary test); (4) added `RecordBeat_OnFreedMobSlot_DoesNotThrowAndSilentlyOverwritesSentinel` (documents a known, accepted fragility — `RecordBeat` has no ownership guard).
- Test count: 27 (`tests/EditMode/Networking/OwlCompensation_SlotAllocation_tests.cs`) — independently verified via grep (two different patterns agreed) after the implementing agent's self-report was wrong twice in a row (claimed 25, then 23, actual 27 both before and after the fix round — pattern of unreliable self-counts continues from prior stories this epic).
- Process note: made a coordination error mid-session — tried to relay design approval via a fresh `Agent` spawn instead of `SendMessage` to the same agent instance; the fresh agent (correctly) refused to write without verifiable context, per this project's collaboration protocol. Corrected by resuming the actual agent via `SendMessage`.
- Tech debt: TD-024 logged (AC-OWL-06a/06c's ghost-period tests are structural, not integration-level — same forward-dependency class as TD-017 through TD-023; register now 24 items).
- Not committed to git.
- Next: Story 023 (OWL Wrap-Correction Compensation Formula) — second story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, depends on this story's `LastBeatServerTickTracker`.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 021 — Ghost Session cluster CLOSED — all 5 stories complete)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; unity-specialist (APPROVED) + qa-tester (GAPS, resolved) served as this story's review.
- Story: `production/epics/networking-core/story-021-ghost-cleanup-zone-crash-voluntary-dismissal.md` marked Complete. `EPIC.md` Story 021 row updated to Complete. **Ghost Session cluster (017-021) is now fully closed.**
- New production: `GhostEntityTracker.RemoveGhost` (CR-GH-10 step 4, fulfilling Story 017's own predicted need), `GhostCleanupSequencer.CompleteVoluntaryDismissalCleanup` (new sibling, hardcodes `GhostDismissed`), `GhostDismissalCoordinator` (CR-GH-12, inlines de-target via a new shared `MobDeTargetingCoordinator.IssueDeTargetCommands` helper to avoid mislabeling `OnGhostCombatTTLExpired`), `ZoneCrashCleanupHandler` (CR-GH-11, new `GhostZoneCrashSession` struct), 2 new `PersistenceWriteReason` values (`GhostDismissed=6`, `GhostZoneCrash=7`).
- Key story-readiness finding: AC-GH-14 and AC-GH-16 needed **no new production code** — both are composition tests against already-tested Story 012/013/014/019 methods (confirmed via `RecordFailedReAuthAttempt`'s own "TTL never reset" structural guarantee and `EvaluateJoinAttempt`'s own "ghost sessions count toward capacity" doc comment).
- Code review found 2 Required Changes + several Suggestions, all applied: (1) AC-GH-14 test now reads `ConnectionStateMachine.TryGetSessionExpiryTick` back rather than reusing a hardcoded constant (qa-tester catch); (2) added a `ZoneCrashCleanupHandler` partial-failure/atomicity test (mirroring Story 020's `PartyDisbandCoordinator` precedent); (3) extracted the shared de-target loop helper; (4) added a `RemoveGhost` third-branch coverage test; (5) logged TD-021/022/023 for accepted composition-test/ordering limitations.
- Test count: 24 (`tests/EditMode/Networking/GhostSession_Cleanup_Crash_Dismissal_tests.cs`) — independently verified via grep + brace-balance, matches agent self-report exactly. All 5 blocking ACs (AC-GH-10, AC-GH-11, AC-GH-14, AC-GH-16, AC-GH-20) COVERED.
- Tech debt: TD-021, TD-022, TD-023 logged (register now 23 items total). Cluster-wide tech debt spans TD-017 through TD-023 (7 items) — primarily composition-test causal-link weaknesses and forward-dependency gaps (no real Party System, AI subsystem, or persistence/restart layer exists yet).
- Not committed to git.
- Next: Networking Core epic continues with Story 022 (LastBeatServerTick Slot Allocation & Data Structure) — first story of the OWL Compensation sub-cluster (022-024), `Status: Ready`, no dependency on the now-closed Ghost Session cluster.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 020 — Ghost Session cluster, fourth story CLOSED)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` (unity-specialist + qa-tester, both succeeded this time) served as this story's review.
- Story: `production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md` marked Complete. `EPIC.md` Story 020 row updated to Complete.
- Code review found 2 Required Changes + 5 Suggestions; all applied except one (optional `preDisconnectXp >= 0` guard on `BeginTracking` — reviewer explicitly noted it matches existing precedent not to add, skipped and flagged to user):
  1. Applied: removed AC-GH-6's tautological `ZoneTestConfigurator` set-then-read-back assertion — rests solely on the party-roster proof now.
  2. Applied: honest documentation caveats added distinguishing AC-GH-7/12 (legitimate delegate-seam composition) from AC-GH-8 (materially weaker — no seam at all, two causally-independent checks).
  3. Applied: logged **TD-020** in `docs/tech-debt-register.md` for `GhostXpPoolTracker.ResolveFinalXp` having zero production call sites.
  4. Applied: added `ProcessPartyDisband_OneUntrackedMemberInList_ThrowsAfterProcessingEarlierMembers` (partial-failure/atomicity test).
  5. Applied: added `ResolveFinalXp_ThenEndTracking_SubsequentQueryThrows` + `ResolveFinalXp_IncludeShareTrue_AfterStopAccumulation_ReflectsFrozenPoolNotLaterAttempts`.
  6. Applied: sync-risk note added to `GhostXpPoolTracker.cs` class remarks (keep in step with `GhostEntityTracker`).
- Test count grew from 21 to 24 after fixes (verified via grep + brace-balance check on all 3 touched/new files).
- 5/5 blocking ACs (AC-GH-6, AC-GH-7, AC-GH-8, AC-GH-12, AC-GH-18) COVERED with full traceability. No BLOCKING deviations.
- Tech debt: TD-020 logged (total register now 20 items).
- Not committed to git.
- Next: Story 021 (Ghost Cleanup, Zone Crash & Voluntary Dismissal) — last story in the Ghost Session cluster, `Status: Ready`, depends on Stories 017-020 which are now all Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 020 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gap: all 5 ACs (AC-GH-6/7/8/12/18) dropped GDD "Pass condition" text — restored verbatim from `networking-ghost-session.md`. TR-net-006 registry gap noted as systemic, no new fix needed.
- Story: `production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md` — fourth story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted). Dependencies (017/018/019) all Complete.
- Implemented via spawned `engine-programmer` agent (full context package: story, GDD AC text, ADR-004, control manifest, plus a pre-worked design from my own research). Agent's first message truncated mid-sentence after finishing the two production files (same pattern as Story 018) — resumed via SendMessage, completed the test file cleanly on the second pass.
- New classes: `GhostXpPoolTracker` (stateful two-pool XP registry, mirrors `GhostEntityTracker`'s shape; `ResolveFinalXp(characterId, includePostDisconnectShare)` is the single CR-GH-9/9.1/9.2 forfeit-vs-restore decision point) and `PartyDisbandCoordinator` (stateless static, mirrors Story 019's `MobDeTargetingCoordinator` shape — fires `OnPartyDisbanded` then loops `StopAccumulation` per ghosted member). `INetworkTestObserver`/`NetworkTestObserver` extended with `OnPartyDisbanded(partyId, tickNumber)`, per the story's own Implementation Notes.
- AC-GH-7/AC-GH-12 compose with Story 018's already-tested `GhostCleanupSequencer` (HP-persistence) rather than duplicating it — same precedent as Story 019's AC-GH-9. AC-GH-8 calls the real, unmodified Story 013 `CompleteReAuthSuccess`. AC-GH-6 needed no new production code (absence proof via a test-local mock party roster + real `ZoneTestConfigurator` zone-state check).
- **Honest documentation note**: AC-GH-7/AC-GH-8's GDD pass-condition text references `OnSessionHandshakeEmitted` reporting `currentXp` — that callback has no XP field and was NOT extended (would touch Story 013's already-closed method/call sites). Resolved via `GhostXpPoolTracker.ResolveFinalXp` captured directly in each test's own closure instead — same idiom as Story 019's `MobDeTargetCommand` resolution. Documented in the test file's class remarks.
- Test count: 21 (`tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs`) — independently verified via direct grep count, matches the agent's own self-report exactly (no discrepancy this time). All 5 blocking ACs covered plus 12 null-guard/precondition-guard tests.
- Files updated: `src/Foundation/Networking/GhostSession/GhostXpPoolTracker.cs` (new), `src/Foundation/Networking/GhostSession/PartyDisbandCoordinator.cs` (new), `src/Foundation/Networking/TestHarness/INetworkTestObserver.cs` + `NetworkTestObserver.cs` (extended), `tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs` (new, 21 tests)
- Minor note flagged for code review: AC-GH-6's zone-state check (`SetZoneStateForTesting` → `GetCurrentZoneState`) is a set-then-read-back assertion, trivially true — not wrong, just weak, worth a look.
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above, then `/story-done production/epics/networking-core/story-020-ghost-reward-forfeit-policy.md`.

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 019 — Ghost Session cluster, third story CLOSED)

- Verdict: COMPLETE WITH NOTES. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW gates skipped; the self-performed `/code-review` served as this story's review (both specialist agents hit the session's API rate limit and failed).
- Story: `production/epics/networking-core/story-019-ghost-death-mob-detargeting.md` marked Complete. `EPIC.md` Story 019 row updated to Complete.
- Code review found 3 suggestions; user approved 2 (applied), declined 1 (symmetric test in Story 018's closed suite — marginal value):
  1. Applied: honest caveat added to `GhostSession_DeathDeTargeting_tests.cs` class remarks — AC-GH-17's ordering proof is structural/test-constructed, not system-arbitrated (same accepted limitation class as Story 018's AC-CGS-3).
  2. Applied: in-test comment on `..._AC_GH_4` noting the XP-shares=0 clause is correctly out of scope (Story 020 owns it), not silently skipped.
  3. Declined: symmetric test on Story 018's `HandleGhostDeathWhileReconnecting` rejecting `Disconnected_SessionActive` — not built.
- 4/4 blocking ACs (AC-GH-4, AC-GH-5, AC-GH-9, AC-GH-17) COVERED with full traceability. No BLOCKING deviations; TR-net-006 registry gap remains the only systemic advisory.
- No new tech debt entries logged — both advisory items resolved via in-code documentation only, matching the precedent set by Story 018's AC-CGS-3 (documented, not separately tracked in the register).
- Not committed to git.
- Next: Story 020 (Ghost Reward Forfeit Policy — Two-Pool XP & Party Slot Retention) or Story 021 (Ghost Cleanup, Zone Crash & Voluntary Dismissal) — both `Status: Ready`, both depend on Stories 017-019 which are now all Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 019 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) all 4 ACs (AC-GH-4/5/9/17) dropped GDD "Pass condition" text — restored; (2) no test-observability resolution existed for `MobDeTargetCommand` issuance (confirmed no observer callback for it) — resolved via delegate seam, same precedent as Story 018; (3) AC-GH-9 substantially overlaps Story 018's already-tested AC-CGS-1 — clarified this story's test should compose with `GhostCleanupSequencer.CompleteTTLExpiryCleanup`, not re-derive; (4) TR-net-006 (systemic); (5) the story's own "confirm GHOST_COMBAT_TTL constant with design lead" note was stale — pointed to the already-established epic-level interim resolution instead.
- Story: `production/epics/networking-core/story-019-ghost-death-mob-detargeting.md` — third story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted).
- **Design decisions resolved before implementation**: (a) new `ConnectionStateMachine.CompleteGhostDeathFromDisconnected` — the normal (non-racing) `Disconnected_SessionActive → Disconnected_SessionExpired` via "GhostDeath" row, complementing Story 018's `HandleGhostDeathWhileReconnecting` (the `Reconnecting`-race variant); derives `characterId` from the record (learned from Story 018's own code-review fix); (b) new stateless-static `MobDeTargetingCoordinator.ProcessTTLExpiry` (mirrors `GhostCleanupSequencer`'s shape) — fires `OnGhostCombatTTLExpired` then issues a caller-supplied `MobDeTargetCommand` delegate per targeting mob, satisfying the 250ms `DE_TARGET_DEADLINE_MS` budget structurally (zero simulated delay), same "structural, not measured timing" idiom as every prior story; (c) AC-GH-9's test composes directly with Story 018's `GhostCleanupSequencer.CompleteTTLExpiryCleanup` rather than duplicating its HP-persistence logic; (d) AC-GH-17 needs no new production code — proven via a test-constructed entity-ID list filtered strictly after both cleanup calls complete (structural ordering, no new observer hook for "zone tick delivered").
- **Process note**: the agent stopped once for a genuine approval checkpoint (not a truncation) with a fully worked-out plan, including its own judgment call on AC-GH-17's exact test mechanics (a `List<uint>`/`HashSet<uint>` filter proof). Reviewed and approved before it wrote anything.
- Test count: 10 (`tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs`) — independently verified by direct count; the implementing agent's own self-report claimed 12, which was inaccurate — all 4 blocking ACs still fully covered (AC-GH-4: 4 tests incl. a guard distinguishing this method from Story 018's Reconnecting-race method; AC-GH-5: 4 tests; AC-GH-9: 1 composition test; AC-GH-17: 1 ordering test).
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/GhostSession/MobDeTargetingCoordinator.cs` (new), `tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs` (new, 10 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above, then `/story-done production/epics/networking-core/story-019-ghost-death-mob-detargeting.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 018)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md` — Pre-Disconnect Snapshot & Write-Ordering (second story in the Ghost Session cluster, 017-021)
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-019 logged (no `CrashStep` for the CGS-3 snapshot-WAL write specifically); AC-CGS-2's `wasKilledWhileDisconnected` clause honestly documented as unverifiable at this layer; AC-CGS-3's "priority" honestly documented as caller-discipline-enforced, not system-arbitrated (no dispatcher exists yet).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 018 row → Complete.
- Tech debt logged this session: TD-019 (already reflected in the register before this closure — register now 19 items).
- Write-ordering foundation (`PreDisconnectSnapshotWal`, `GhostCleanupSequencer`, `ConnectionStateMachine.HandleGhostDeathWhileReconnecting`) now exists for Stories 019 and 021 to build on.
- Next recommended: Story 019 — Ghost Death & Mob De-Targeting — the natural next pick in the Ghost Session cluster (020/021 also `Status: Ready`).

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 018 — Ghost Session cluster continues)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) 3 of 4 ACs (AC-CGS-1/2/4) dropped GDD "Pass condition" text — restored; (2) AC-CGS-3 was missing a critical detail — the GDD's exact pass condition requires `fromState = Reconnecting`, not `Disconnected_SessionActive` (a death racing a reconnect already in progress) — restored with explicit callout; (3) AC-CGS-3's GDD test technique cites `ITransportFaultInjector` for injecting an inbound reconnect ACK — confirmed (same as Story 017's AC-GH-13) that interface is outbound-only, resolved by driving both code paths directly instead; (4) TR-net-006 (systemic); (5) added a performance note.
- Story: `production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md` — second story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted) — the ghost-specific instance of CR-NET-5's commit-before-broadcast principle (Story 011).
- **Design decisions resolved before implementation**: (a) new minimal `PreDisconnectSnapshot` struct (HP + respawn position only — the GDD's full 14-field CGS-3 list deferred, since Character Stats/Inventory/Buffs systems don't exist yet, same forward-dependency discipline as every prior story); (b) new `PreDisconnectSnapshotWal` class, idempotent write keyed on `(characterId, disconnectTickNumber)` per EC-CGS-2; (c) new stateless-static `GhostCleanupSequencer` (mirrors `CommitBeforeBroadcastSequencer`'s shape) with `CompleteTTLExpiryCleanup`/`CompleteGhostDeathCleanup`, deliberately NOT calling into Story 015's `ConnectionStateMachine.CompleteSessionActiveTTLExpiry` (which hardcodes the wrong `PersistenceWriteReason`) — a new ghost-specific parallel path instead; (d) a genuinely new `ConnectionStateMachine.HandleGhostDeathWhileReconnecting` method — the fifth `Reconnecting`-adjacent transition row, confirmed no prior story built this; (e) both `GhostExpiredEvent` and `ZoneSessionEnded` modeled as caller-supplied delegate seams (confirmed no observer callback exists for either, and none added).
- **Process note**: the implementing agent's response was truncated mid-edit (same recurring failure mode this session), but this time all 4 production files (3 new classes + the `ConnectionStateMachine` extension) were already fully and correctly written before the cutoff — only the test file was missing entirely. Independently verified every production file in full before doing anything else. Resumed the same agent (preserving context) rather than restarting fresh, and it completed the test file cleanly on the second pass.
- Test count: 16 (`tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs`) — all 4 blocking ACs covered (AC-CGS-4 gets 2 tests: pure ordering + the named `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` crash-durability scenario), plus WAL idempotency/query tests and null-guard/precondition tests for every new public method.
- **AC-CGS-3's test is the most structurally important one**: proves CGS-6's single-tick priority not via any synchronization primitive, but by calling `HandleGhostDeathWhileReconnecting` first, then attempting a real `ConnectionStateMachine.CompleteReAuthSuccess` call against the same account and asserting it throws `InvalidOperationException` (via `RequireState`, since the account is no longer `Reconnecting`) — independently verified this call order and every API signature involved.
- Files updated: `src/Foundation/Networking/GhostSession/{PreDisconnectSnapshot,PreDisconnectSnapshotWal,GhostCleanupSequencer}.cs` (new), `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` (new, 16 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — mechanically verified every claim (release-stripping, EC-CGS-2 idempotency logic, call orders against actual GDD source, no conflict with the 4 existing `Reconnecting`-adjacent methods, crash-simulation soundness). qa-tester: GAPS — found the mechanical correctness didn't fully match the AC's semantic claims: AC-CGS-2's `wasKilledWhileDisconnected` assertion was vacuous (set unconditionally in the test's own lambda, never reading production output); AC-CGS-3's test proves `RequireState`'s generic guard fires post-removal, not that the system arbitrates same-tick priority (no dispatcher/orchestration exists yet to arbitrate — an honest, non-contradictory complement to unity-specialist's "the code is mechanically correct" finding); a real design-consistency gap — `HandleGhostDeathWhileReconnecting` took `characterId` as an independent parameter instead of deriving it from the account's own registry record, unlike every sibling method, with no remarks justification for the deviation; EC-CGS-2's crash-recovery narrative wasn't actually exercised (confirmed no `CrashStep` enum value exists for the CGS-3 snapshot-WAL write specifically).
- All 4 suggestions fixed this session per user direction ("yes"): (1) fixed `HandleGhostDeathWhileReconnecting` to derive `characterId` from `record.CharacterId` (matching every sibling method), removing the mismatch risk structurally rather than just via a test; (2) removed the vacuous `wasKilledWhileDisconnectedWritten` assertion and documented honestly why that clause is unverifiable at this layer; added a reverse-order symmetry test (`CompleteReAuthSuccess_ThenHandleGhostDeathWhileReconnecting_SecondCallThrows`) plus honest doc-comment framing on both AC-CGS-3 tests clarifying what they can and cannot prove about "priority" absent a real dispatcher; (3) added `TryWriteSnapshot_TwoDifferentCharacters_NoCrossCharacterInterference`; (4) logged TD-019 (missing `CrashStep` for the CGS-3 snapshot-WAL write). Final test count: 18 (16 + 2 new).
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (`HandleGhostDeathWhileReconnecting` signature fix), `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` (+2 tests, 1 vacuous assertion removed, call sites updated for the signature change), `docs/tech-debt-register.md` (+TD-019; register now 19 items)
- Next: `/story-done production/epics/networking-core/story-018-pre-disconnect-snapshot-write-ordering.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 017)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-017-ghost-promotion-state-constraints.md` — Ghost Promotion & State Constraints (first story in the Ghost Session cluster, 017-021)
- 7/7 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-018 logged (AC-GH-2's freeze test tautological until a real Movement/Combat system exists); AC-GH-13's GDD pass-condition text imprecisely named `ITransportFaultInjector` (outbound-only) as the test technique — resolved via the new `ShouldRejectCommand` method, documented honestly.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 017 row → Complete.
- Tech debt logged this session: TD-018 (already reflected in the register before this closure — register now 18 items).
- `GhostEntityTracker` foundation now exists for Stories 018-021 to extend (pre-disconnect snapshot, ghost death/de-targeting, XP forfeit policy, cleanup/zone-crash/voluntary dismissal).
- Next recommended: Story 018 — Pre-Disconnect Snapshot & Write-Ordering — the natural next pick in the Ghost Session cluster (all of 018-021 are `Status: Ready`).

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 017 — Ghost Session cluster opens)

- `/story-readiness` verdict: NEEDS WORK → fixed directly in the story file. Gaps: (1) 4 of 7 ACs (AC-GH-1/2/13/15) omitted the GDD's precise "Pass condition" text naming exact observer callbacks/test techniques — restored; (2) AC-CGS-5's pass condition wants to assert the exact `EntityHealthUpdate.HP` wire value, but no `INetworkTestObserver` callback exposes it (`OnRUBatchEntityHealthUpdates` only reports entity IDs) — resolved by deriving expected HP indirectly (same pattern AC-GH-3 already uses) rather than adding a new callback; (3) TR-net-006 registry gap (systemic); (4) added a performance note.
- Story: `production/epics/networking-core/story-017-ghost-promotion-state-constraints.md` — first story in the Ghost Session cluster (017-021). ADR-004 governs it (Accepted); ADR-010 cross-checked and correctly excluded (Accepted, but doesn't govern network-boundary messages).
- `engine-programmer` implemented `GhostEntityTracker` (new, `src/Foundation/Networking/GhostSession/`) — deliberately decoupled from `ConnectionStateMachine` (no dependency, same precedent as `ZoneSessionStateMachine`), a sealed in-memory registry keyed by `characterId`. Composition with `ConnectionStateMachine.EvaluateTimeouts`'s heartbeat-timeout branch happens only in the test (no orchestration layer exists yet), same "test composes them" precedent as Story 014.
- **Agent surfaced a design checkpoint (not a failure this time)** — stopped cleanly to ask approval before writing, listing 8 judgment calls. Reviewed and approved all 8, independently re-verifying the most important one myself: the agent found that `ITransportFaultInjector`'s actual API (confirmed by me reading the interface directly) is outbound-fault-injection only, with no inbound-command-queuing capability — contradicting the GDD's own AC-GH-13 pass-condition text, which names it as the test technique. Resolved by using `ShouldRejectCommand` as the real assertion mechanism, `TransportFaultInjector` instantiated only as scaffolding to honor the named technique, documented honestly in both files rather than silently reinterpreted.
- **`ApplyDamage`'s design is the key structural proof for AC-CGS-5/CGS-1**: the method body never reads `IsGhost` at all — the absence of that branch is itself the "no client-side prediction ever applied to a ghost" proof. A dedicated test constructs both a ghosted and a never-ghosted character and asserts identical `ApplyDamage` output.
- Other judgment calls (all approved): `PromoteToGhost` throws on double-promotion (defensive-guard consistency, not AC-required) → no `ClearGhostState` needed since tests use fresh-instance isolation; `GetAttackQueueDepth`/damage-amount are trivial forward-dependency mocks (no Combat/Skill system exists yet, same treatment as Story 010/015's generic seams); no `GHOST_COMBAT_TTL` timer field (correctly out of scope, deferred to Stories 019/021); `EncodeIsGhostField` is a minimal static encoding-contract proof (0 bytes when false, 1 byte when true), not a full wire message type.
- Test count: 16 (`tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs`) — all 7 blocking ACs covered (AC-GH-1/2/3/13/15, AC-CGS-5 split into 2 tests, AC-GH-19 split into 2 tests) plus 7 null-guard/precondition-guard/query-default tests.
- Files updated: `src/Foundation/Networking/GhostSession/GhostEntityTracker.cs` (new), `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` (new, 16 tests)
- Independently verified both files in full (read completely, brace-balanced, cross-checked every `NetworkTestObserver` API reference) before reporting — this agent run completed cleanly, no truncation.
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — confirmed ADR-004 compliance, release-stripping guard correct, and independently traced `ApplyDamage`'s body to confirm no `IsGhost` read exists (the CGS-1 proof). qa-tester: GAPS — both specialists independently flagged the same dead-code issue (a `TransportFaultInjector` instance in the AC-GH-13 test that had no fault-injection method ever called on it, only a no-op `Reset()`); also found a missing negative-path test for `ShouldRejectCommand` (registered-but-not-ghosted case), and correctly pushed back on the AC-CGS-5 second test's framing — a single-input-pair equality test can't conclusively prove "no ghost-specific branch," only the direct code read can (which both specialists independently did and confirmed clean).
- All 4 suggestions fixed this session per user direction ("fix all 4 now"): (1) added `ShouldRejectCommand_RegisteredButNotGhosted_ReturnsFalse`; (2) removed the dead `TransportFaultInjector`/`Reset()` instantiation from the AC-GH-13 test; (3) strengthened the AC-CGS-5 second test with 4 input vectors (ordinary, zero-damage, damage-exceeds-HP/negative-result, both-zero) and reframed its doc comment as a regression guard, not "the structural proof" (the code-read of `ApplyDamage`'s body is the actual proof); (4) logged TD-018 (AC-GH-2's freeze test is honestly tautological given no Movement/Combat system exists yet — needs a companion test once one does). Final test count: 17 (16 + 1 new).
- Files updated: `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` (+1 test, dead-code removed, 1 test strengthened+reframed), `docs/tech-debt-register.md` (+TD-018; register now 18 items)
- Next: `/story-done production/epics/networking-core/story-017-ghost-promotion-state-constraints.md`.

## Session Extract — /story-done 2026-07-18 (Networking Core Story 016)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-016-session-token-nscrt.md` — Session Token Generation, Validation & Rotation (NSCRT)
- 8/8 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` (given extra scrutiny) served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); TD-017 logged (AC-TOK-5 character-state-preservation clause has no owner anywhere in the repo — correctly out of scope for this token-only class); process note that this story was implemented directly rather than via subagent after 3 consecutive delegation failures.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 016 row → Complete.
- Tech debt logged this session: TD-017 (already reflected in the register before this closure — register now 17 items).
- **This closes the last independent piece before the Ghost Session cluster (017-021) can begin in earnest** — all of its dependencies (Session Lifecycle cluster 012-015, plus this story) are now Complete.
- Next recommended: any of Story 017-021 (Ghost Session cluster, all `Status: Ready`) — Story 017 (Ghost Promotion & State Constraints) is the natural first pick as the cluster's opening story.

## Session Extract — /story-readiness + /dev-story (IN PROGRESS) 2026-07-18 (Networking Core Story 016)

- Story: `production/epics/networking-core/story-016-session-token-nscrt.md` — Session Token Generation, Validation & Rotation (NSCRT). No ADR applies (pure .NET BCL crypto). Independent of the now-Complete Session Lifecycle cluster; several prior stories (013, 015) already anticipated it via delegate seams.
- `/story-readiness` verdict was NEEDS WORK → fixed directly in the story file (3 gaps, all applied): (1) AC-TOK-4 restored the GDD's dropped clarification — do NOT attempt a direct old-vs-new token byte comparison (`OnSessionHandshakeEmitted` has no token field yet, deferred per OQ-NC-SER-2), prove rotation only via the old token's invalidation; (2) AC-TOK-8's "1,000 random tokens + timing p<0.05" requirement genuinely conflicts with `.claude/rules/test-standards.md`'s "no random seeds, no time-dependent assertions" rule — resolved in the story text itself: use a fixed/deterministic 1,000-token array (not runtime-random), and treat the timing-comparison assertion as a narrow, approved, one-AC-only exception to the general rule; (3) added a performance note (no per-tick impact, concurrency-safety not throughput is the actual concern).
- **This is the first genuinely concurrency-sensitive class in this codebase** — CR-TOK-8 requires thread-safety since reconnect handlers and TTL expiry can execute concurrently at the 20Hz tick boundary. Every prior Networking Core class (`ConnectionStateMachine`, `ZoneSessionStateMachine`, etc.) is plain-`Dictionary`-backed and implicitly single-threaded; this is new ground.
- **Design decision resolved before implementation** (given to the engine-programmer as settled): a single atomic `TryValidateAndRotate(accountId, presentedToken, newExpiresAtTick, out newToken, out newSessionId)` method using `ConcurrentDictionary<uint, ActiveTokenEntry>.TryUpdate`'s 3-argument compare-and-swap overload (CAS against the exact entry object read, not a blind write) — a losing CAS (lost race) is treated as a failed validation, which is exactly AC-TOK-6's contract ("only the first of two concurrent requests succeeds"). Requires `ActiveTokenEntry` to be an **immutable** class (all-readonly fields, replaced wholesale, not mutated in place) specifically so CAS reference-identity semantics work — a deliberate, documented departure from this epic's usual mutable-record convention (`ConnectionStateMachine.AccountSessionRecord` mutates in place; this one must not).
- **Process note — TWO consecutive agent failures on this story, more severe than prior truncations**: first `engine-programmer` spawn (foreground) did 24 tool calls of research/planning and got cut off mid-sentence ("Now drafting the test file.") with **zero files written to disk** — confirmed via `git status`/`find` (unlike Stories 013/014/015's truncations, which at least had complete file writes before the summary got cut). Resumed the same agent (via `SendMessage`, not a fresh `Agent` spawn, to preserve its research) with an explicit instruction to write immediately. On resume, the agent had the full design drafted but **refused to call Write without explicit user approval** — correctly applying this project's Collaboration Protocol (`CLAUDE.md`: "Agents MUST ask 'May I write this to [filepath]?'"), specifically declining to treat a coordinator/orchestrator message as that approval. Its refusal was correct behavior, not a bug.
- Proposed design (relayed to and approved by the actual user): class named `SessionTokenStore` (not `...Service` — matches this codebase's noun-first naming convention), in `src/Foundation/Networking/SessionToken/`, implementing `IssueToken`, `TryValidateAndRotate` (CAS-based, per above), `InvalidateToken`, `IsZeroToken` (plain comparison, not `FixedTimeEquals` — not a secret, no timing-attack surface), `IsTokenActive`. Test file `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs`, ~20 tests, including a real `Parallel.Invoke`-based concurrent race test for AC-TOK-6 (100 iterations, exactly one winner each) and a `Stopwatch`-based timing comparison for AC-TOK-8 using a fixed deterministic 1,000-wrong-token array.
- **THREE consecutive engine-programmer failures on this exact story** before it landed: (1) first spawn did 24 tool calls of research and got cut off mid-sentence with zero files written; (2) resumed that same agent — it had the full design ready but correctly refused to treat any relayed/coordinator message as the user's own file-write approval (per `CLAUDE.md`'s Collaboration Protocol — this was correct behavior on its part, not a bug); (3) a fresh second `Agent` spawn (to avoid the first instance's transcript baggage) was terminated immediately by the session hitting its own API/usage limit, before any tool call ran.
- **Given three failed delegation attempts, implemented this story directly** (Write tool, no further agent spawn) using the fully-resolved design already produced across the prior attempts — user approved proceeding. Both files written and independently verified (read in full, brace-balance checked, cross-referenced every API call — `RandomNumberGenerator.GetBytes`, `CryptographicOperations.FixedTimeEquals`, `ConcurrentDictionary.TryUpdate`'s 3-arg CAS overload — against real .NET BCL signatures, all explicitly prescribed by the story/GDD text itself so no invented API risk).
- Files created: `src/Foundation/Networking/SessionToken/SessionTokenStore.cs` (new — `IssueToken`, `TryValidateAndRotate` [atomic CAS-based validate+rotate, the concurrency-critical method], `InvalidateToken`, `IsZeroToken`, `IsTokenActive`; `ActiveTokenEntry` nested class deliberately immutable, not mutated in place like every other registry in this epic, because `TryUpdate`'s CAS is reference-identity-based); `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` (new, 15 tests) — all 8 blocking ACs covered, including a real `Parallel.Invoke`-based 100-iteration concurrency race test for AC-TOK-6 and a `Stopwatch`-based timing-comparison test for AC-TOK-8 using a fixed deterministic 1,000-wrong-token array (per the story's own resolved test-methodology note).
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story; this one in particular has never been compiled, so extra scrutiny in `/code-review` is warranted given no subagent verification happened either.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel, extra scrutiny requested given the self-implementation). unity-specialist: CLEAN — hand-traced the CAS correctness argument from first principles (reference-equality semantics of `ConcurrentDictionary.TryUpdate` for a class with no `Equals` override, per-key bucket lock serialization, argument order), ~97-98% confidence it's correct. qa-tester: GAPS — 3 missing edge-case tests, plus an important cross-story finding: AC-TOK-5's "character state is preserved" clause has no test anywhere in this repo and no other story owns it either (correctly out of scope for this token-only class).
- All 3 suggestions fixed this session per user direction ("fix all 3 now"): (1) added 3 tests — wrong-length token, `IssueToken` double-issue overwrite, cross-account isolation; (2) strengthened the AC-TOK-6 concurrency test with a 2-party `Barrier` forcing deterministic simultaneous execution instead of relying on scheduler-luck overlap; (3) logged TD-017 (AC-TOK-5 character-state-preservation ownership gap) in `docs/tech-debt-register.md` — register now 17 items. Final test count: 18 (15 + 3 new).
- Files updated: `tests/EditMode/Networking/SessionToken_NSCRT_tests.cs` (+3 tests, Barrier strengthening), `docs/tech-debt-register.md` (+TD-017)
- Next: `/story-done production/epics/networking-core/story-016-session-token-nscrt.md`.
- Not yet committed to git (nothing in this epic has been committed since Story 006).

## Session Extract — /story-done 2026-07-18 (Networking Core Story 015 — Session Lifecycle cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases
- 7/7 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Deviations: TR-net-006 registry gap (systemic); 2 forward-dependency scope limitations tracked as tech debt (TD-015 AC-NC-35(a), TD-016 AC-NC-42); AC-NC-16/34-CRASH's crash-durability proof accepted as a unit-level mock (mirrors Story 011's own precedent, not separately tracked).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 015 row → Complete.
- Tech debt logged this session: TD-015, TD-016 (both during `/code-review`, already reflected in the register before this closure — register now 16 items total).
- **The Session Lifecycle cluster (012-015) is now fully Complete** — `ConnectionStateMachine` (012/013/015) and `ZoneSessionStateMachine` (014) together form the full ST-NET-1/ST-NET-2 session/zone lifecycle layer.
- Next recommended: Story 016 — Session Token Generation, Validation & Rotation (NSCRT) (`production/epics/networking-core/story-016-*.md`) — independent, no ADR applies, `Status: Ready`. Several prior stories (013's `HandleReconnectSessionSteal`, 015's crash-recovery work) already anticipated this via delegate seams (`invalidateSessionToken`). Unlocks the Ghost Session cluster (017-021), all `Status: Ready` and depending on the now-Complete Session Lifecycle cluster.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 015 — Session Lifecycle cluster closing story)

- `/story-readiness` verdict: NEEDS WORK → proceeded to `/dev-story` per user instruction. Gaps found: (1) Test Evidence/QA Test Cases cited `tests/PlayMode/...` — same recurring PlayMode/EditMode mismatch as Stories 007/013, corrected to EditMode; (2) AC-NC-12's story text dropped the GDD's required `"TTLExpired"` trigger-string literal; (3) TR-net-006 not in registry (systemic, expected); (4) no performance-budget note. Also independently confirmed two things that could have been mismatches but weren't: `ITransportFaultInjector.DropSnapshotFragment(ushort.MaxValue)` really does mean "last fragment" per the real interface, and `IServerCrashInjector.CrashStep.AfterTTLExpiryPersistenceWrite`/`AfterPersistenceWrite` already exist pre-registered for exactly this story's crash scenarios — no design ambiguity to resolve, unlike Stories 011/013. Also confirmed the story's own "AC-NC-34-CRASH" suffix correctly disambiguates a genuine cross-doc collision with `networking-wire-protocol.md`'s own unrelated AC-NC-34 (same collision class as the already-logged AC-NC-38 case).
- Story: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — the closing story of the Session Lifecycle cluster (012-015), 7 blocking ACs spanning 4 distinct concerns.
- **Key design decision resolved before implementation** (to avoid breaking Stories 012/013's already-Complete, already-tested `ConnectionStateMachine` signatures): AC-NC-12 implemented as a NEW caller-driven method (`CompleteSessionActiveTTLExpiry`) on the same class, mirroring `EnterReconnecting`/`RecordFailedReAuthAttempt`'s shape (caller supplies `sessionExpiryTick` per-account, this class never computes/stores it) — rather than modifying `EvaluateTimeouts`'s existing signature/switch, which would have broken every existing Story 012/013 test call site.
- `engine-programmer` implemented 3 pieces: (1) `ConnectionStateMachine.CompleteSessionActiveTTLExpiry` (AC-NC-12) — extends the existing class; (2) `EnhancementRequestDeduplicator` (new, `src/Foundation/Networking/EnhancementRequestDedup/`) — generic EC-NET-9 dedup proof (per-character `LastEnhancementRequestID`-equivalent, no time window, crash-durability proven via constructing a fresh instance seeded from a mock store's post-crash-persisted value, mirroring `CommitBeforeBroadcastSequencer`'s Story 011 mock-outcome scope discipline) for AC-NC-16/27/34-CRASH; (3) `ZoneSnapshotReassemblyTracker` (new, `src/Foundation/Networking/ZoneSnapshotReassembly/`) — genuinely new fragment-reassembly timeout/retransmit-attempt/gate-open tracking for AC-NC-35/40, with `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS = 3` as a provisional public const (same precedent as Story 011's `SESSION_TTL_SECONDS` for an AC-pinned tunable value).
- **AC-NC-42 judgment call (documented, endorsed)**: added no new production code at all — `ConnectionStateMachine`'s existing `TryGetSessionState` already lets a caller guard an RPC's effect on current state, and EC-NET-6's "transition IS the authority boundary" is already true by construction (synchronous, single-dictionary, no concurrency). A wrapper method would only relocate the same one-line check with no real caller yet (no RPC-dispatch layer exists in this codebase). Proven via a 2-case parameterized test driving both legal orderings directly against the existing public API.
- **Process note**: the implementing agent's final response was truncated mid-sentence for the THIRD time this session (same failure mode as Stories 013 and 014's implementation runs). Did not trust the agent's self-report — independently read all 3 new/modified production files in full and cross-verified every internal API reference used in the test file (`ServerCrashInjector.SignalStepReached`, `TransportFaultInjector.TryConsumeSnapshotFragmentDrop`, `DisconnectType.Timeout`) against the real interface/enum definitions before reporting anything as complete. All correct, no partial writes found.
- Test count: 32 executed test cases (30 `[Test]` + 1 `[TestCase]`-parameterized method × 2 cases) across 4 fixtures in `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs`. All 7 blocking ACs covered.
- **Flagged for `/code-review`, not yet resolved**: the AC-NC-35(a) test only asserts `ITransportFaultInjector`'s own drop-tracking contract as a standalone sanity check (self-flagged by the agent in-comment) — it is not actually wired into `ZoneSnapshotReassemblyTracker`'s production code, since no real transport/snapshot-send path exists yet in this codebase for it to wire to. Worth a specialist opinion on whether this is acceptable scope (matching this epic's every-other-forward-dependency precedent) or needs tightening.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/EnhancementRequestDedup/EnhancementRequestDeduplicator.cs` (new), `src/Foundation/Networking/ZoneSnapshotReassembly/ZoneSnapshotReassemblyTracker.cs` (new), `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs` (new, 32 test cases)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — hand-verified the `ZoneSnapshotReassemblyTracker` re-baselining tick math line-by-line, confirmed the `EnhancementRequestDeduplicator` crash-simulation plumbing is a legitimate structural proof. qa-tester: GAPS — flagged that AC-NC-16/34-CRASH's crash simulation is a unit-level contract proof only (no real persistence/process boundary — acceptable interim, not permanent closure), AC-NC-42's test is documentation of a pattern rather than a regression gate (guard is a local function, no real RPC-dispatch layer exists to call into), AC-NC-35(a) is more disconnected from production code than self-flagged (fault injector and tracker are never actually wired together), plus 2 concrete missing tests and the still-stale story-file PlayMode path.
- All 4 suggestions fixed this session per user direction ("fix all 4 now"): (1) corrected story-015's stale `tests/PlayMode/...` references (both QA Test Cases and Test Evidence sections) to the actual EditMode path; (2) added `TryProcess_TwoIndependentCharacterInstances_SameRequestIdOnBothCommitsIndependently` proving `EnhancementRequestDeduplicator`'s per-character dedup scoping; (3) added `CompleteSessionActiveTTLExpiry_AccountReconnecting_Throws` closing the `Reconnecting`-state guard gap; (4) logged TD-015 (AC-NC-35(a) fault-injector/tracker disconnection) and TD-016 (AC-NC-42 synthetic-guard-only coverage) in `docs/tech-debt-register.md`, both explicitly flagging they must be revisited (not silently treated as closed) once the relevant forward-dependency stories land. Final test count: 34 (32 + 2 new).
- Files updated: `production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` (stale path fix), `tests/EditMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs` (+2 tests), `docs/tech-debt-register.md` (+TD-015, TD-016; register now 16 items)
- Next: `/story-done production/epics/networking-core/story-015-ttl-expiry-zone-crash-inflight-rpc.md` — this is the LAST story in the Session Lifecycle cluster (012-015).

## Session Extract — /story-done 2026-07-18 (Networking Core Story 014)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-014-zone-session-state-machine-capacity.md` — Zone Session State Machine & Capacity Enforcement (ST-NET-2)
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Story file corrected at closure (matching the AC-NC-37 precedent from Story 013): AC-NC-22's GDD-source note fixed (actually `networking-core.md`, not `networking-session.md`), AC-NC-41 expanded to its fuller current-GDD text (the story's original copy omitted the `OnPersistenceWriteCompleted` and `IZoneTestConfigurator` clauses).
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 014 row → Complete.
- Tech debt logged: None new (deviations are documentation-accuracy corrections and the same systemic TR-net-006 registry gap tracked since Story 001).
- Next recommended: Story 015 — TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases (`production/epics/networking-core/story-015-*.md`) — depends on Stories 012/013/014, all now Complete.

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 014)

- `/story-readiness` verdict: NEEDS WORK → proceeded to `/dev-story` per user instruction. Gaps found: (1) AC-NC-22 misattributed — story's Context section cites `networking-session.md` as the GDD, but AC-NC-22 (zone capacity overflow) is actually defined in `networking-core.md`'s "Zone Capacity" section; wording matches verbatim, just wrong source; (2) AC-NC-41 in the story is understated vs. the GDD's current text — missing the `OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)` and `IZoneTestConfigurator.GetCurrentZoneState` same-tick-boundary clauses; (3) TR-net-006 not in registry (systemic, expected); (4) no performance-budget note despite touching the tick-loop-driven zone bookkeeping (same gap class as Story 012).
- Story: `production/epics/networking-core/story-014-zone-session-state-machine-capacity.md` — Zone Session State Machine & Capacity Enforcement (ST-NET-2) — first story past the now-Complete Story 012/013 pair in the Session Lifecycle cluster (012-015)
- **Real design ambiguity resolved before implementation** (surfaced by my own due-diligence read of `IZoneTestConfigurator`/`ZoneTestConfigurator`, not by the agent): the GDD's current AC-NC-41 requires `IZoneTestConfigurator.GetCurrentZoneState` to reflect the transition, but the concrete `ZoneTestConfigurator` (Story 001) is a pure in-memory override stub with zero wiring to any real zone logic — its own doc comment defers that wiring to "a future story." Resolved: the new `ZoneSessionStateMachine` production class never touches `IZoneTestConfigurator` at all (a production class depending on a test-only, `#if`-guarded interface type would be backwards); its own `TryGetZoneState` is the sole production source of truth, and the AC-NC-41 test independently drives both the real state machine and `ZoneTestConfigurator.SetZoneStateForTesting` to prove they agree on the same tick.
- `engine-programmer` implemented `ZoneSessionStateMachine` (sealed, stateful class — the first zone-level registry in this codebase, structurally parallel to but deliberately decoupled from `ConnectionStateMachine`'s per-account registry) in `src/Foundation/Networking/ZoneSessionStateMachine/`. Per this story's Out of Scope boundary: never queries `ConnectionStateMachine` directly — every method that needs a player/session count takes it as a caller-supplied parameter (same delegate-seam/loose-coupling precedent as every prior story this epic). No real TTL timer — `CompleteTTLExpiryTeardown` is caller-driven, mirroring `ConnectionStateMachine.EvaluateTimeouts`'s caller-supplied-tick-counts precedent (Story 015 owns the real sweep).
- **Judgment call — split teardown method, not one unified `TransitionToClosed`**: `PersistenceWriteReason` is declared entirely inside the `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` guard, so a single method taking a caller-supplied reason parameter would fail to compile in Release. Resolved with two separate methods (`CompleteExplicitDisconnectTeardown` hardcoding `ExplicitDisconnect`, `CompleteTTLExpiryTeardown` hardcoding the pre-existing `ZoneClose` reason — which Story 002 had already pre-registered specifically for this row) — mirrors `ConnectionStateMachine`'s existing convention of hardcoded literal reasons for every GDD-determinate transition.
- **Process note**: the implementing agent's final response was truncated mid-sentence (mid-run cutoff, same failure mode as the Story 002 session). Did not trust the agent's self-report — independently read both the full 601-line implementation file and the full 601-line test file before reporting anything as complete. Both were fully correct, complete, and consistent with the resolved design decisions above; no partial writes or corruption found.
- Test count: 31 (`tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs`) — all 4 blocking ACs covered (AC-NC-14, AC-NC-22, AC-NC-24, AC-NC-41 using the fuller GDD text), plus the full remaining ST-NET-2 transition table and null/precondition guards on every new method.
- **Minor asymmetry flagged for `/code-review`, not yet fixed**: `EvaluatePlayerCountChange` validates `totalOccupiedSlots == 0` (throwing, directing the caller to `CompleteExplicitDisconnectTeardown` instead) only on the `Active`-state branch — the equivalent `Draining`-state combination (`hasAnyConnectedSession: false`, `totalOccupiedSlots: 0`, already `Draining`) silently no-ops instead of throwing. No AC exercises this combination; non-blocking.
- Files updated: `src/Foundation/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (new), `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` (new, 31 tests)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). unity-specialist: CLEAN — release-stripping guard verified correct on all 5 observer-taking methods; confirmed `EvaluateJoinAttempt`'s no-observer-parameter design is correct (no matching callback exists in `INetworkTestObserver`'s surface); confirmed the split-teardown-method design is a compile-time necessity, not overengineering. qa-tester: GAPS — found the AC-NC-41 `IZoneTestConfigurator` cross-check test was **tautological**: it hardcoded `ZoneState.Closed` directly into `SetZoneStateForTesting` independent of the state machine's actual result, so a real regression producing `Draining` instead of `Closed` would not have been caught. Both specialists also independently flagged the same `EvaluatePlayerCountChange` asymmetry (zero-occupied-slots guard only threw on the `Active` branch, not `Draining`).
- All 5 suggestions fixed this session per user direction ("fix all 5 now"): (1) fixed the tautological test — now drives `SetZoneStateForTesting` from the real `TryGetZoneState` result, renamed to `CompleteExplicitDisconnectTeardown_RealStateMirroredIntoZoneTestConfiguratorSeam_ReadsBackAsClosed`, with a doc comment explicitly scoping what it does/doesn't prove (a regression guard on this test's own mirroring + `TryGetZoneState`, not a production-integration proof — the real orchestration layer composing `ZoneSessionStateMachine` with `IZoneTestConfigurator` doesn't exist yet); (2) added a 2-cycle `Active↔Draining` regression test; (3) added the 2 missing wrong-state guard tests for `CompleteExplicitDisconnectTeardown` (registered-but-`Draining`, registered-but-`Closed`); (4) added a `CompleteTTLExpiryTeardown` empty-list success-path test; (5) made the `EvaluatePlayerCountChange` zero-occupied-slots guard symmetric across `Active`/`Draining` (restructured the `else if (Active)` branch into a unified `else` block with the zero-check first, applicable to both states) + added the matching `...FromDraining_Throws` test. Final test count: 36 (31 + 5 new; the tautology fix was a rewrite, not a new test).
- Files updated: `src/Foundation/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (guard symmetry fix + doc comments), `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` (+5 tests, 1 rewritten)
- Next: `/story-done production/epics/networking-core/story-014-zone-session-state-machine-capacity.md`

## Session Extract — /code-review + /story-done 2026-07-18 (Networking Core Story 013)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md` — Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth Limits
- `/code-review` (lean, unity-specialist + qa-tester parallel): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN — release-stripping guard verified correct on all 4 new methods (EnterReconnecting, CompleteReAuthSuccess, RecordFailedReAuthAttempt, HandleReconnectSessionSteal); EC-NET-7 in-place-mutation and TTL-never-written-on-failure claims verified directly against code, not just doc comments. qa-tester: TESTABLE — all 5 blocking ACs (AC-NC-11, AC-NC-13, AC-NC-CR64-RECONCILE, AC-NC-37, AC-NC-38-REAUTH) proven with real production code paths (real `CurrencySystem`, not mocked). One flagged item, not a code defect: the story's own AC-NC-37 checkbox text was a byte-for-byte duplicate of Story 012's `AC-NC-39-SESSION` (Connected-state steal) rather than describing the Reconnecting-state row this story's `HandleReconnectSessionSteal` actually implements and tests.
- All 3 suggestions fixed this session per user direction ("fix all 3 now"): (1) reworded AC-NC-37's checkbox text (both Acceptance Criteria and QA Test Cases sections) to describe the `Reconnecting`-state scenario actually tested; (2) added `CompleteReAuthSuccess_TwoPendingGoldDebitedPurchases_ReconcilesBothInOrderAndSumsRefund` — hardens the reconciliation loop against an off-by-one/early-break regression that the existing single-record test couldn't catch; (3) extended the AC-NC-11 handshake test to assert all 7 `SessionHandshakeData` pass-through fields (previously only 4 of 7 were asserted — `goldVersion`/`currentHp`/`currentMp`/`heldFreePoints`/`classType` were silently unchecked). Final test count: 27 (26 + 1 new).
- `/story-done`: 5/5 blocking ACs COVERED with full traceability. Review mode `lean` — QL-TEST-COVERAGE/LP-CODE-REVIEW phase-gates skipped, the already-run `/code-review` served as this story's review (consistent with every prior story this session).
- Deviations (both advisory, neither new): TR-net-006 not in the still-empty `tr-registry.yaml` (systemic gap, GDD used as source of truth directly); Story 016 (session token) dependency still `Status: Ready`, implemented against a delegate seam (`invalidateSessionToken`) per this epic's forward-dependency precedent.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 013 row → Complete.
- Tech debt logged: None new (both deviations are already-tracked systemic/precedent items, not new follow-up work).
- **Session Lifecycle cluster (012-015) status**: 012 and 013 now Complete. Story 014 (Zone Session State Machine & Capacity Enforcement, ADR-004) and Story 015 (TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases, ADR-004) both `Status: Ready`, unblocked.
- Next recommended: Story 014 — Zone Session State Machine & Capacity Enforcement (`production/epics/networking-core/story-014-*.md`) — next unstarted story in the Session Lifecycle cluster.

## Session Extract — /dev-story 2026-07-17 (Networking Core Story 011)

- Story: `production/epics/networking-core/story-011-commit-before-broadcast-pattern.md` — Commit-Before-Broadcast Generic Pattern — last story in the Tick Loop & Authority cluster (009-011)
- `engine-programmer` implemented `CommitBeforeBroadcastSequencer` (static `Execute<TOutcome>` ordering helper: validate → acknowledge → compute → persist → confirm → broadcast, never broadcast before confirm) in `src/Foundation/Networking/CommitBeforeBroadcast/`, against a generic mock "irreversible outcome" seam per the story's Out of Scope boundary — no real Enhancement/Respec/NPC Shop logic built.
- **Mid-implementation design checkpoint** (surfaced by the agent, verified independently before approving): the story's own text said to use "`IServerCrashInjector`'s delay/crash-at-step variants" — checked the actual interface, it only has `RegisterCrashAt(CrashStep)`/`ClearRegistered()`, no delay variant. Resolved by simulating delay/failure via the caller-supplied `persistOutcome` delegate instead (real `Stopwatch`-timed blocking for AC-NC-09/15's 200ms/500ms delays; `false` return for AC-CBB-1's failure case) — same class of story-text-vs-actual-API mismatch as Stories 005/009.
- **Real GDD inconsistency found and fixed**: CR-NET-5.5 (`networking-core.md`) defers to CR-CP-5 (`character-persistence.md`) as the authoritative write-failure protocol, but CR-CP-5's own comparison table (line 259) stated the alert-vs-session-preservation order backwards relative to CR-CP-5's own numbered protocol list (lines 174-182: revert→disconnect→**alert**→**preserve**). I independently verified this against the numbered list before approving the agent's resolution. Fixed the one line in the table; numbered list (authoritative) was already correct, untouched.
- `SESSION_TTL_SECONDS` (default 300, referenced across 5+ GDDs but never previously in code) introduced here for the first time, as a provisional `public const int` on `CommitBeforeBroadcastSequencer` — matches the `ServerTickLoop.TICK_RATE_HZ` precedent from Story 009. Real ownership belongs to a future session-lifecycle story (CR-NET-2).
- Added `OnCriticalInfrastructureAlertFired(uint clientId, string reason)` to `INetworkTestObserver`/`NetworkTestObserver` (interface + recorder + `Reset()` clear line, all verified consistent).
- **Agent-handling note (process, not content)**: my first attempt to resume the implementing agent after its design-checkpoint question used the `Agent` tool (spawning a fresh, context-less duplicate) instead of `SendMessage` (which resumes with full history). Caught before the duplicate wrote any files — stopped it via `TaskStop`, confirmed via `git status` that no conflicting changes landed, then correctly resumed the original agent via `SendMessage`. No file damage occurred; noting this so a future session doesn't repeat it.
- Test count: 13 (`tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs`) — 5 behavioral (happy path, reject path, 200ms delay, 500ms delay, write-failure protocol) + 8 null-argument-guard tests (one per required delegate parameter).
- All 4 blocking ACs (AC-NC-09, AC-NC-15, AC-CBB-1, AC-CBB-2) covered — see mapping in conversation; not yet independently re-verified by a second specialist pass (that's `/code-review`, not yet run this session).
- Files updated: `src/Foundation/Networking/CommitBeforeBroadcast/{CommitBeforeBroadcastSequencer,CommitBeforeBroadcastResult}.cs` (new), `tests/EditMode/Networking/TickLoop_CommitBeforeBroadcast_tests.cs` (new, 13 tests), `src/Foundation/Networking/TestHarness/{INetworkTestObserver,NetworkTestObserver}.cs` (new observer callback), `design/gdd/character-persistence.md` (1-line table-order fix)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story.
- Not yet committed to git — nothing in this epic has been committed since Story 006 (`3ab0fe6`); Stories 007-011 are all still uncommitted, per project convention (no commits without explicit user instruction).
- **Reminder carried forward**: the `origin` git remote still has a live-looking GitHub PAT embedded in plaintext in `.git/config` (first flagged during Story 010's session) — not yet rotated.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). Zero BLOCKING findings — the release-stripping guard around the new `observer` parameter was verified clean on first pass (correctly applying the Story 010 lesson). 4 suggestions surfaced, all fixed in this same session per user direction ("fix all 4 now"): (1) **exception-safety hardening** — qa-tester found that if `persistOutcome` throws instead of returning `false`, the entire CR-NET-5.5 write-failure protocol would silently never run; added a try/catch inside `Execute<TOutcome>` routing thrown exceptions into the identical failure-protocol path as a `false` return, plus a new regression test (`Execute_PersistOutcomeThrows_...`); (2) added a literal `Assert.AreEqual(300, ...)` check so a regression to `SESSION_TTL_SECONDS`'s tuned default wouldn't slip past a self-consistency-only check; (3) added a doc-comment sentence explaining the delegate-seam-vs-descriptor-struct shape difference from Story 010's `CrossCuttingRpcGuardChain`; (4) `.meta` file gap left alone (expected, no Editor in this sandbox). All 4 fixes independently re-verified by direct file read (not just trusting the agent's report) before closing. Final test count: 14 (13 + 1 new exception-path test).
- **`/story-done` result: COMPLETE WITH NOTES.** 4/4 blocking ACs COVERED with traceability. Review mode is `lean` (`production/review-mode.txt`) so the QL-TEST-COVERAGE and LP-CODE-REVIEW phase-gates were skipped — the `/code-review` pass already run served as this story's review, consistent with every prior story this session.
- **New finding during closure**: a genuine TR-ID collision — Story 009 and Story 011 both cite `Requirement: TR-net-002`, but `EPIC.md`'s own placeholder table defines that ID as Story 009's "20Hz tick" requirement, not Story 011's commit-before-broadcast pattern. Didn't block either story (both sourced requirements directly from the GDD, not the empty `tr-registry.yaml`). Logged as TD-014.
- Story 011 marked `Status: Complete` with Completion Notes; `EPIC.md` Story 011 row → Complete, plus the new TR-net-002 collision added to the epic's "Known blockers/inconsistencies" list.
- Tech debt logged: TD-013 (`SESSION_TTL_SECONDS` provisional ownership, resolve when Story 012/013 builds the real session registry), TD-014 (TR-net-002 collision, resolve when the TR registry is actually populated). Register updated: 12 → 14 items.
- **The Tick Loop & Authority cluster (009-011) is now fully Complete.**

## Session Extract — /story-done 2026-07-17 (Networking Core Story 011)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-011-commit-before-broadcast-pattern.md` — Commit-Before-Broadcast Generic Pattern
- Tech debt logged: 2 items (TD-013, TD-014)
- Next recommended: Story 012 — Player Connection State Machine — Core Transitions (`production/epics/networking-core/story-012-player-connection-state-machine-core-transitions.md`) — first story past the now-complete Tick Loop & Authority cluster; per `EPIC.md`, depends on Stories 004/006/007/009/010/011, all Complete.

## Session Extract — /story-readiness + /dev-story + /code-review 2026-07-17 (Networking Core Story 012)

- `/story-readiness` verdict: NEEDS WORK → both gaps minor and consistent with epic-wide precedent, user chose to proceed straight to `/dev-story` without fixing first. Gaps: (1) TR-net-006 not in the (empty) `tr-registry.yaml` — same systemic gap as every prior story; (2) no performance-budget note despite touching the tick path.
- Story: `production/epics/networking-core/story-012-connection-state-machine-core-transitions.md` — Player Connection State Machine — Core Transitions (the 6-row core, non-reconnect subset of ST-NET-1)
- `engine-programmer` implemented `ConnectionStateMachine` (sealed, stateful class — the first real per-account `SessionState` registry in this codebase) in `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs`, against injected triggers per ADR-004's own guidance (NGO connection-callback binding deferred to a future story).
- **Two design judgment calls surfaced and approved before coding**: (1) `OnSessionStateTransitioned`'s non-nullable `fromState` has no representation for the `— → Connecting` (brand-new-connection) transition — resolved by reusing `SessionState.Disconnected_SessionExpired` as the synthetic fromState (semantically accurate for the session-steal case; adding a 6th enum member would violate the control manifest's explicit "ST-NET-1 five states" rule); (2) no GDD/story-prescribed trigger string exists for explicit-disconnect — resolved as `"ExplicitDisconnect"`, mirroring the existing `PersistenceWriteReason.ExplicitDisconnect` enum member name.
- **Explicitly distinguished from Story 008's `HeartbeatActivityTracker`**: that class tracks *outbound* packets for heartbeat-send scheduling; this story needed its own *inbound*-silence tracking for AC-NC-10 (a different concern) — flagged in the brief specifically to prevent the two from being conflated, and the implementation correctly built its own tracking rather than misusing the existing class.
- **`/code-review` result: APPROVED WITH SUGGESTIONS** (unity-specialist + qa-tester, parallel). Zero BLOCKING findings — release-stripping guard verified clean across all 6 public methods + 1 private helper; dictionary-mutation-during-enumeration safety confirmed correct by hand-trace against actual `Dictionary<TKey,TValue>` semantics (deferred-removal-via-scratch-list correctly applies the lesson from Story 009's own code review). qa-tester found one real **test-quality gap** (not a production defect): the AC-NC-39-SESSION ordering test caught 3 of 4 possible adjacent-step reorderings but missed a swap between `OnPersistenceWriteCompleted` and `OnSessionStateTransitioned(SessionSteal)` — fixed with one added assertion. Also added, per user direction ("fix all 4 now"): 4 missing null-guard tests (both specialists independently found the same gap) and 2 parity/robustness tests (`RecordInboundActivity` no-op on a `Connecting` account — flagged as moderate-value since a regression here would silently defeat AC-NC-39-CONNECTING; `FailConnecting_AccountNotConnecting_Throws` for consistency with the other three `RequireState`-guarded methods). Final test count: 25 (19 original + 6 new — the ordering fix was an added assertion, not a new test method).
- All 4 blocking ACs (AC-NC-10, AC-NC-26, AC-NC-39-SESSION, AC-NC-39-CONNECTING) covered — both AC-NC-10 boundary directions (59/60/61 ticks) explicitly tested.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (new), `tests/EditMode/Networking/Session_ConnectionStateMachine_Core_tests.cs` (new, 25 tests)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story. Verified independently by direct file re-reads (not just agent self-report) after both the implementation and the review-fix passes.
- Not yet committed to git.
- Next: `/story-done production/epics/networking-core/story-012-connection-state-machine-core-transitions.md`

## Session Extract — /story-readiness + /dev-story 2026-07-18 (Networking Core Story 013)

- `/story-readiness` verdict: NEEDS WORK → proceeded straight to `/dev-story` per user instruction. Gaps found: (1) header `Type: Logic` contradicted Test Evidence's `Story Type: Integration` + a nonexistent `tests/PlayMode/...` path that also contradicted QA Test Cases' own EditMode path — same 3-way inconsistency pattern Story 007 hit; (2) TR-net-006 not in the (still-empty) registry — same systemic gap; (3) dependency Story 016 (session token) is `Status: Ready`, not Complete — user chose "proceed anyway," implemented against a delegate seam per this epic's forward-dependency precedent.
- Story: `production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md` — extends Story 012's `ConnectionStateMachine` with the 4 `Reconnecting`-state ST-NET-1 rows (not a new/parallel class).
- **Real discrepancy found and resolved before coding**: the story's own AC-NC-37 text is word-for-word the same `Connected`-state scenario Story 012 already tests as `AC-NC-39-SESSION` — but this story's scope is the `Reconnecting`-state additions, and its Out of Scope section explicitly excludes Story 012's territory. Resolved: implemented the GDD's distinct (and un-numbered in the GDD's own AC list) `Reconnecting → Disconnected_SessionExpired` session-steal row instead (new method `HandleReconnectSessionSteal`), documented in the class's `<remarks>`, did not duplicate Story 012's existing test.
- **Mid-implementation session-limit interruption**: the implementing agent hit its API session limit mid-task, right after finishing `TryGetSessionExpiryTick` (its second-to-last planned step). Rather than resuming blind, I independently verified every file it had touched before treating anything as complete: read `ConnectionStateMachine.cs` in full (all 5 new methods present, brace-balanced, correctly incorporating a requested clarification — see below), all 3 new supporting type files, and the full 26-test test file (spot-checked the two trickiest tests — the `HandleReconnectSessionSteal` ordering proof and the 3-attempt exhaustion test — both correct). Cross-verified real dependencies actually exist as assumed: `CharacterID(uint)` constructor, `CurrencySystem.RegisterCharacter`, `ICurrencyService.GetBalance`. Found exactly one piece genuinely left undone — the story-file `Type`/Test-Evidence documentation fix — and applied it directly myself rather than waiting on the rate-limited agent.
- **One design clarification requested and correctly incorporated before the interruption**: `RecordFailedReAuthAttempt` requires a fresh `EnterReconnecting` call before each failed attempt (matching the GDD's literal bounce-back transition table), which only works if `EnterReconnecting` mutates the *existing* `AccountSessionRecord` in place rather than replacing it — otherwise `ReauthFailureCount` would silently reset each cycle, defeating EC-NET-7. Verified in the actual code: `EnterReconnecting` does `record.State = ...; record.SessionExpiryTick = ...` on the existing object (not `_sessions[accountId] = new AccountSessionRecord{...}`), with an explicit doc-comment note added exactly as requested.
- Test count: 26 (`tests/EditMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs`) — all 5 ACs (AC-NC-11, AC-NC-13 a/b/none, AC-NC-CR64-RECONCILE, AC-NC-37, AC-NC-38-REAUTH) covered, plus null-guard and precondition-guard tests for each new method. Gold-reconciliation tests use a real `CurrencySystem` instance (Currency System is production code, not a forward dependency).
- NP-NEW-2 (TTL elapses mid-reauth) explicitly deferred to Story 015 — approved before implementation; no AC in this story tests it, and it overlaps Story 015's CR-NET-6.5 scope.
- Files updated: `src/Foundation/Networking/ConnectionStateMachine/ConnectionStateMachine.cs` (extended), `src/Foundation/Networking/ConnectionStateMachine/{PendingPurchaseRecord,RespecReservationStatus,SessionHandshakeData}.cs` (new, +`.meta`), `tests/EditMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs` (new, 26 tests), story file (`Type: Logic`→`Integration`, Test Evidence path corrected)
- Not yet run in a real Unity Editor — same sandbox limitation as every prior story.
- Not yet committed to git.
- Next: `/code-review` on the files above.

## Session Extract — /story-done 2026-07-17 (Networking Core Story 012)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-012-connection-state-machine-core-transitions.md` — Player Connection State Machine — Core Transitions
- 4/4 blocking ACs COVERED with traceability. Review mode `lean` — phase-gates skipped, the already-run `/code-review` served as this story's review.
- Story marked `Status: Complete` with Completion Notes; `EPIC.md` Story 012 row → Complete.
- Tech debt logged: None new (all 3 advisory deviations are either already-tracked systemic gaps — TR-net-006/TD-014 — or self-resolving design notes documented inline in the code, not new follow-up work).
- Next recommended: Story 013 — Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth (`production/epics/networking-core/story-013-connection-state-machine-reconnect-session-stealing.md`) — builds directly on Story 012's `ConnectionStateMachine`, depends on Story 012 (now Complete).

## Session Extract — /dev-story + /code-review + /story-done 2026-07-12 (Networking Core Story 010)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-010-cross-cutting-rpc-guards.md` — Cross-Cutting RPC Guards (EntityID Validity, Session-Ready, Rate Limiting) — next in the Tick Loop & Authority cluster (009-011)
- `/dev-story`: `engine-programmer` implemented `CrossCuttingRpcGuardChain` (4-stage guard pipeline: EntityID validity → session-ready → rate limit → ownership) in `src/Foundation/Networking/RpcGuards/`, against a generic `InboundRpcDescriptor`/`RpcTypeTag` carrier since `AllocateFreePointRequest`/`NotifySkillUsed` don't exist as concrete types yet (Leveling/Skill epics not started) — same forward-dependency pattern as Story 007/009. Two design judgment calls surfaced and approved before writing code: (1) `InboundRpcDescriptor` needed a `ClientId` field beyond the story's literal 3-field text, for the ownership/session-ready checks to have any meaning; (2) `RpcTypeTag` split into its own file (not nested) to match this codebase's one-enum-per-file convention.
- **Major finding during `/code-review`**: both unity-specialist and my own manual verification (by actually running `tools/ci/check-test-harness-guards.sh`, the real CI script built in Story 002 to enforce AC-TC-02) confirmed `CrossCuttingRpcGuardChain.Evaluate` referenced `INetworkTestObserver` in an unguarded production method signature — a genuine Release-Player-build compile-breaking defect, not just a style issue. **The identical defect was already present, undetected, in two already-"Complete" files**: `ServerTickLoop.cs` (Story 009) and `RUBatchWriter.cs` (Story 007) — both completely unguarded top-to-bottom. Per user direction, fixed all three in one pass (`#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` around the observer parameter + its usage in each), plus reworded 4 doc-comment mentions (including one in `PendingSubMessage.cs`) that tripped the CI script's text-only scanner without being real compile risks. Re-ran the CI script: now passes clean project-wide. Re-verified by a second unity-specialist pass (hand-traced the fix and the 2 new tests against the actual code) — confirmed complete and correct.
- qa-tester found 2 real coverage gaps (session-ready-checked-before-rate-limit specifically was never proven, only asserted in prose; `OnSkillUsedRateLimitRejected` never proven to NOT fire for `AllocateFreePoint` rejections) → both closed with new tests. Final test count: 23 (21 original + 2 gap-closing).
- All 4 blocking ACs (AC-NC-02, AC-NC-20, AC-NC-46, AC-NC-23) COVERED — full traceability table produced, no gaps. Rate-limit/ownership gate-ordering interaction (can a non-owner's rejected request corrupt the real owner's rate-limit bookkeeping?) specifically hand-traced by both specialists and confirmed correct — `_lastAcceptedTick` only ever writes after all 4 guards pass.
- Tech debt: None new logged (the release-stripping bug was found AND fixed in this same session, not left open — matches this project's established pattern of not tech-debt-logging bugs that are already resolved).
- **⚠️ Separately surfaced, unrelated to this story**: `git remote -v` shows the `origin` remote URL contains a live-looking GitHub Personal Access Token embedded in plaintext (`https://github_pat_...@github.com/...`). This is a credential exposure sitting in `.git/config` — flagged to the user directly in-conversation, recommend rotating/revoking the token and reconfiguring the remote to use a credential helper instead. Not acted upon (out of scope for this story), no file was written or read to extract/expose it further.
- Files updated: `src/Foundation/Networking/RpcGuards/{RpcGuardResult,InboundRpcDescriptor,RpcTypeTag,CrossCuttingRpcGuardChain}.cs` (new, +release-stripping fix), `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs` (new, 23 tests), `src/Foundation/Networking/TickLoop/ServerTickLoop.cs` (release-stripping fix), `src/Foundation/Networking/WireProtocol/RUBatchWriter.cs` (release-stripping fix), `src/Foundation/Networking/WireProtocol/PendingSubMessage.cs` (doc-comment reword), `production/epics/networking-core/story-010-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 010 → Complete)
- Not yet run in a real Unity Editor — no compiler available in this sandboxed session, same limitation as every prior story.
- Not yet committed to git (per project convention, committing requires explicit user instruction).
- Next recommended: Story 011 — Commit-Before-Broadcast Pattern (`production/epics/networking-core/story-011-commit-before-broadcast-pattern.md`) — last story in the Tick Loop & Authority cluster (009-011)

## Session Extract — /code-review + /story-done 2026-07-12 (Networking Core Story 009)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-009-fixed-20hz-server-tick-loop.md` — Fixed 20Hz Server Tick Loop — first story in the Tick Loop & Authority cluster (009-011)
- **Continuation context**: implementation (`src/Foundation/Networking/TickLoop/ServerTickLoop.cs`) and its test suite (`tests/EditMode/Networking/TickLoop_Core_tests.cs`) were already fully written and sitting uncommitted at the start of this session (a prior `/dev-story` run, evidenced by a stray `bash.exe.stackdump` — the session likely crashed before code review/story-done ran). Picked up directly at `/code-review`.
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): first pass found a real medium-severity bug — `AdvanceTick`'s tick-driven delegate dispatch loop indexed the live mutable `_tickDrivenDelegates` list, so a delegate unregistering an earlier sibling mid-tick would shift the list and silently skip a not-yet-invoked delegate for that tick (the same hazard the TTL registry's backward iteration was already written to avoid). Fixed via a snapshotted dispatch count + deferred-removal reentrancy guard (`_isDispatchingTickDriven` + `_pendingTickDrivenUnregistrations`, wrapped in try/finally so it's exception-safe too). Re-verified CLEAN by a second unity-specialist pass that hand-traced the fix and all 9 new tests against the actual code.
- 9 tests added closing gaps found across both review passes: Bug A regression, exception-propagation-doesn't-corrupt-state, `ArgumentNullException` guards on both Register methods, `UnregisterTickDriven` false-return path, TTL timer registering a new TTL timer during the expiry loop (reentrancy), drift-alert boundary exclusivity (just-below/just-above 25ms, avoiding an exact-25ms float-precision-risk test), and a two-window drift-accumulator-reset test. Final count: 25 test methods (16 original + 9 new).
- All 4 blocking ACs (AC-NC-04, AC-NC-05, AC-TICK-1, AC-TICK-2) COVERED — full traceability table produced, no gaps.
- Two theoretical edge cases flagged by unity-specialist but judged non-blocking (no code in this codebase exercises them): nested/reentrant `AdvanceTick` calls on the same instance, and calling `UnregisterTickDriven` twice mid-dispatch on a duplicate-instance registration. Added one doc-comment line on `AdvanceTick` noting the non-reentrancy expectation; no test/tech-debt entry needed.
- Deviations logged in story (non-blocking): (1) story's own Dependencies text ("tick loop calls their Flush methods" for Stories 006/007) was only half-accurate — this story builds only the generic `RegisterTickDriven` extension point, doesn't itself call `PriorityPathQueue.Flush`/`RUBatchWriter.Write`; corrected in the story file, matches the judgment-call writeup already in `ServerTickLoop.cs`'s class remarks. (2) `HeartbeatActivityTracker.cs` (Story 008's file) doc comment corrected — its claim that `TICK_RATE_HZ` "does not exist anywhere in this codebase yet" is now stale since this story introduces it (doc-only, no behavior change).
- Tech debt: None new (TR-net-002 registry gap is the same pre-existing systemic issue documented across every prior story)
- **Not yet run in a real Unity Editor** — no compiler available in this sandboxed session (same limitation as Stories 003/004/005/006/008). Recommend running the EditMode suite before treating this as fully closed.
- Files updated: `src/Foundation/Networking/TickLoop/ServerTickLoop.cs` (bug fix + doc corrections), `tests/EditMode/Networking/TickLoop_Core_tests.cs` (+9 tests), `production/epics/networking-core/story-009-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 009 → Complete)
- **Still uncommitted in git, along with Stories 007/008 before it** — nothing in this epic has been committed since Story 006 (commit `3ab0fe6`). Per project convention ("No commits without user instruction"), committing is a separate explicit step the user hasn't requested yet.
- Next recommended: Story 010 — Cross-Cutting RPC Guards (`production/epics/networking-core/story-010-cross-cutting-rpc-guards.md`) — next in the Tick Loop & Authority cluster (009-011)

## Session Extract — /story-done 2026-07-11 (Networking Core Story 008 — Wire Protocol Core cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-008-heartbeat-il2cpp-aot-guardrails.md` — Heartbeat Message & IL2CPP AOT Guardrails
- 3/3 ACs passing, verified by direct file review + 2 independent specialist passes (unity-specialist independently re-ran the AOT scanner's regexes against the real source tree by hand; qa-tester confirmed the AC-NC-38 test genuinely proves the full narrative, not a shortcut). **Not yet confirmed in a real Unity Editor run** — unlike Story 007, this one is still file-review-only; recommend a real Editor pass before treating it as fully closed.
- **Wire Protocol Core cluster (Stories 003-008) is now fully Complete** — the last foundational layer before Tick Loop & Authority (009-011).
- Tech debt: TD-011 extended (now covers 5 provisional MessageTypeIDs across Stories 007-008, including `HeartbeatMessage` = 0x0210)
- Also surfaced: a pre-existing cross-doc AC-ID collision (AC-NC-38 means unrelated things in `networking-wire-protocol.md` vs. `networking-session.md`) — logged in `EPIC.md`'s known-inconsistencies list, not a defect in this story
- Files updated: story-008 file (Status: Complete, ACs checked, Completion Notes), `EPIC.md` (Story 008 → Complete, +AC-NC-38 collision note), `docs/tech-debt-register.md` (TD-011 extended)
- Next recommended: Story 009 — Fixed 20Hz Server Tick Loop (`production/epics/networking-core/story-009-fixed-20hz-server-tick-loop.md`) — first story in the Tick Loop & Authority cluster (009-011); depends on Stories 001/002/006/007, all Complete

## Session Extract — /story-done 2026-07-11 (Networking Core Story 007)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — R-U/U-U Batch Framing, Buffer Pooling & Overflow Drop Policy — closes out the Wire Protocol Core cluster (003-008 minus 008)
- 4/4 ACs passing, confirmed in a **real Unity Editor Test Runner run** — first such confirmation in this epic (Stories 001-006 were closed on file-review-only verification, never actually compiled until this session)
- Tech debt logged: TD-011 (4 provisional MessageTypeIDs — resolve before Story 025-027 dispatch), TD-012 (GC allocations in `RUBatchWriter.Write` hot path — SUGGESTION not BLOCKING per unity-specialist)
- Files updated: story-007 file (Status: Complete, ACs checked, Completion Notes), `EPIC.md` (Story 007 → Complete), `docs/tech-debt-register.md` (+TD-011, TD-012)
- Next recommended: Story 008 — Heartbeat Message & IL2CPP AOT Guardrails (`production/epics/networking-core/story-008-heartbeat-message-il2cpp-aot-guardrails.md`) — last story in the Wire Protocol Core cluster

## Session Extract — Currency System test fixes from first real Unity Test Runner pass, 2026-07-11

- **Context**: user ran the full EditMode suite in a real Unity Editor for the first time this session (prompted by the Story 007 code review's recommendation). Result: 0 failures in Networking Core (Stories 001-007, first real compile — clean); a handful of failures in the already-"Complete" Currency System epic.
- **Root cause diagnosed from one pasted failure** (`AddGold_UnregisteredCharacter_DoesNotFireOnGoldSync`, "Unhandled log message" on `CurrencySystem`'s deliberate `Debug.LogError` for the `CharacterNotFound` guard): a systemic, mechanical test-authoring gap, NOT a production-code bug. `CurrencySystem.AddGold`/`TrySpendGold` correctly log `Debug.LogError` on their two guard-rejection paths (`CharacterNotFound`, `InvalidAmount` — by design, per the class's own doc comment: "caller bug, not a runtime condition"). Several tests exercise those exact guard paths without declaring `LogAssert.Expect` first, so Unity Test Framework auto-fails them on the unhandled log — same class of issue as the 2026-07-04 "first real compile" incident (passed code review, never actually run in Unity until now).
- **Grepped both files exercising `CharacterNotFound`/`InvalidAmount`, found 10 affected tests total, fixed all 10** (added `using System.Text.RegularExpressions;`/`using UnityEngine.TestTools;` + a `LogAssert.Expect(LogType.Error, new Regex(...))` immediately before the guard-triggering call, matching this project's own established convention already used correctly elsewhere — e.g. Story 007's `BufferPoolExhausted` test):
  - `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`: `AddGold_ZeroAmount_...`, `AddGold_UnregisteredCharacter_...`, `TrySpendGold_ZeroCost_...`, `TrySpendGold_UnregisteredCharacter_...` (4 tests)
  - `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs`: `AddGold_ZeroAmountOnRegisteredCharacter_...`, `TrySpendGold_ZeroCostOnRegisteredCharacter_...`, `AddGold_UnregisteredCharacter_...`, `TrySpendGold_UnregisteredCharacter_...`, `AddGold_UnregisteredCharacterWithZeroAmount_...`, `TrySpendGold_UnregisteredCharacterWithZeroCost_...` (6 tests)
  - Confirmed NOT affected (checked, no matching guard/log path): `Currency_AddGold_tests.cs`, `Currency_TrySpendGold_tests.cs`, `Currency_Concurrency_tests.cs`, `Currency_EdgeCases_tests.cs`, and `Currency_GoldSyncEvent_tests.cs`'s own `TrySpendGold_InsufficientBalance_...` test (InsufficientFunds guard does not log an error).
- No production code changed — this was purely a missing test-declaration gap. The underlying guard logic was already correct and already verified during Story 003's code review.
- **Follow-up compile error**: both fixed files used `LogType.Error` but only imported `using UnityEngine.TestTools;` (not `using UnityEngine;`, where `LogType` actually lives) — real `CS0246` compile error, caught from the generic Unity Test Runner message alone (no console text needed) since it was an obvious self-inflicted gap in the just-made edits. Fixed by adding `using UnityEngine;` to both files.
- **Verified: user re-ran the full EditMode suite in real Unity — all tests pass clean.** This is the first real compile+test confirmation for the entire Wire Protocol Core cluster (Stories 003-008) and closes out the Currency System regression from the missing `LogAssert.Expect` declarations.
- Files updated: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs`, `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs`

## Session Extract — /story-readiness + /dev-story 2026-07-11 (Networking Core Story 007)

- `/story-readiness` verdict: NEEDS WORK → fixed → proceeded. Two gaps found and fixed directly in the story file: (1) header `Type: Logic` contradicted the Test Evidence section's `Story Type: Integration` — corrected header to `Type: Integration` (AC-NC-21/AC-NC-33 both require multi-tick, multi-client load fixtures, which is Integration in nature); (2) QA Test Cases section named an EditMode test path while Test Evidence required PlayMode — reconciled to EditMode, consistent with every prior Networking Core story's deterministic in-process fixture pattern (no real PlayMode multiplayer session exists in this project).
- Story: `production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — R-U/U-U Batch Framing, Buffer Pooling & Overflow Drop Policy — last story in the Wire Protocol Core cluster (003-008)
- Files changed (all new): `src/Foundation/Networking/WireProtocol/{RUBatchCategory,PendingSubMessage,BatchSubMessageFraming,BatchHeaderCodec,DamageEvent,GoldSyncEvent,CycleTimerBroadcast,EntityPositionUpdate,BatchSubMessageCodec,RUBatchWriter,CycleBroadcastPacketWriter,PositionPacketWriter,ClientBufferSet,ZoneBufferPool}.cs`; `src/Foundation/Networking/WireProtocol/WireEnumCodec.cs` extended with `DecodeGoldTransactionReason` (substitute-and-continue, mirrors the existing `DecodeDamageType`/`DecodeDisconnectType` pattern — a deviation from the brief, which said a plain cast was fine; the implementing agent judged the existing codec pattern was a better fit and I agree)
- Test written: `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs` (21 test methods, 23 executed cases counting a 3-case `[TestCase]`) — all 4 blocking ACs covered (AC-NC-19 absolute-balance + observer hook, AC-NC-21 50-client/200-tick byte budget, AC-NC-33 100-tick 512-byte packet cap, AC-BUF-1 buffer pool exhaustion), plus proactive edge-case coverage (DamageEvent intra-class overflow, category-level atomic eviction, CycleBroadcast never-drop guard, Position packet highest-ID-first drop, all 4 codec round-trips + short-buffer guards)
- Key scoping discipline (verified, endorsed): only `DamageEvent` and `GoldSyncEvent` got concrete typed wire schemas (both owned by `networking-wire-protocol.md` itself); the other 7 R-U categories (EntityHealthUpdate, PartyMemberHealthUpdate, SelfPositionUpdate, SkillCastResult, SkillCooldownUpdate, LootBidUpdate, ConnectionQualityUpdate) are carried generically via `PendingSubMessage` (opaque pre-serialized payload + category tag) because their owning systems (Skill System, Party, Loot Table, Client-Side Prediction, Relevance Filter) don't exist in this codebase yet — mirrors Story 006's `PriorityPathQueue<T>` genericization precedent exactly.
- Judgment calls documented in code (both flagged prominently, both consistent with this epic's established pattern of resolving ambiguous GDD language explicitly rather than silently guessing): (1) the 8-step overflow drop order evicts whole categories atomically, not partial/interleaved per-message drops; (2) all 4 concrete sub-message `MessageTypeId` values (DamageEvent 0x0301, GoldSyncEvent 0x0520, CycleTimerBroadcast 0x0302, EntityPositionUpdate 0x0303) are provisional — not formally registered in ADR-004 (which only registers the 0x0100-0x01FF batch-header range); DamageEvent/GoldSyncEvent reused values already present unchanged in Story 004's doc-comment examples, Cycle/Position picked fresh adjacent values with no prior precedent. Flagged for future ADR-004 amendment or a dedicated MessageTypeID registry — a collision or renumbering after Story 025-027 (dispatch) exists would be a breaking wire change. (3) `GoldTransactionReason` out-of-range byte substitutes `Other` (not `AdminAdjust` as I'd originally briefed) — the implementing agent found `GoldTransactionReason.cs`'s own pre-existing doc comment already specifies "unknown byte values → `Other`, still apply the balance update" as the authoritative receiver contract, and correctly treated that over my guess.
- Tech debt / follow-ups: MessageTypeID registry gap (above) should be resolved before any other story starts assigning application-range IDs ad hoc; buffer pool has no real connection-lifecycle wiring yet (pure data structure, by design — a future story wires `ZoneBufferPool.TryAllocate`/`Release` to real NGO connect/disconnect events).
- Not yet run in the Unity Test Runner (no Editor invocation available in this sandboxed session) — implementation and tests were verified by direct file review (all ~22 files read in full), not by compiling. Recommend running the EditMode suite in Unity before/during `/story-done`.

## Session Extract — /code-review 2026-07-11 (Networking Core Story 007)

- Verdict: APPROVED WITH SUGGESTIONS
- `/code-review` ran with 2 specialists in parallel (unity-specialist, qa-tester). unity-specialist: no bugs found, verified all byte-size math/overflow logic/IEquatable implementations correct, IL2CPP-clean (no LINQ/boxing/Enum.IsDefined/ArrayPool.Shared). qa-tester: found 3 real coverage gaps → fixed → all closed.
- Gaps found and fixed: (1) `MaxSingleOpaqueSubMessagePayloadBytes` (496-byte) boundary guard had zero test coverage — added `RUBatchWriter_OpaqueSubMessagePayloadAtMaxBoundary_WritesSuccessfully` (496, succeeds) and `..._OneByteOverBoundary_ThrowsInvalidOperationExceptionAndLogsOversizedSubMessage` (497, throws); (2) no test proved the destination buffer stays untouched when `RUBatchWriter.Write` throws — extended the existing misrouted-category test with a `CollectionAssert.AreEqual(new byte[...], buffer)` check; (3) the two 200/100-tick load-fixture tests generated an unasserted flood of `Debug.LogWarning` (Position-packet overflow fires every tick/client by design) — wrapped both in `LogAssert.ignoreFailingMessages = true/false` to stop it masking a future genuine failure or tripping a stricter CI logging policy. Test count: 21 → 23 methods (25 executed cases).
- Non-blocking findings, logged as follow-ups (not fixed this pass): (a) `RUBatchWriter.Write` allocates several `List<T>`/array objects on every call (up to 1000 calls/sec at the documented 50-client/20Hz scenario) — contradicts the GDD's stated "zero GC on the hot path" rationale for buffer pooling; unity-specialist rated this SUGGESTION not BLOCKING (small short-lived Gen0 objects, mechanical fix), recommends threading pre-allocated scratch structures through as a follow-up, mirroring the `ClientBufferSet` pattern; (b) 4 provisional `MessageTypeID` values (`DamageEvent` 0x0301, `GoldSyncEvent` 0x0520, `CycleTimerBroadcast` 0x0302, `EntityPositionUpdate` 0x0303) are not formally registered anywhere except this story's own doc comments — both specialists agree this is acceptable-but-flagged, not an ADR violation, and should be resolved via a Networking ADR amendment or a dedicated ID registry before Story 025-027 (dispatch) or any client decoder is built against them.
- Tech debt logged: TD candidate — GC allocations in `RUBatchWriter.Write` hot path (not yet added to `docs/tech-debt-register.md`, flag for next docs pass); MessageTypeID registry gap (same).
- Files updated: `tests/EditMode/Networking/WireProtocol_BatchFraming_tests.cs` (+2 new tests, 2 tests hardened against log-flood flakiness, 1 test extended with an untouched-buffer assertion)
- Next: `/story-done production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md` — this is the last story in the Wire Protocol Core cluster (003-008)

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 006)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-006-priority-path-cap-two-path-delivery.md` — Priority-Path Cap & Two-Path Delivery Model
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — 2 doc-accuracy issues found and fixed (Flush's "up to PRIORITY_PATH_CAP" claim was false when exempt exceeds cap; misleading StaleDiscardComparer cross-reference), 1 coverage gap found and fixed (exempt-overflow test) → 6 test methods total
- **Third design-inconsistency resolution this epic**: story's `PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued` formula (additive) contradicted AC-NC-35's explicit displacement language. Implemented displacement (matching the AC). Verified correct THREE times independently: my own pre-review hand-trace, plus both unity-specialist and qa-tester independently re-traced from scratch and agreed.
- Tech debt: None new (bulk-transfer exemption and exempt-overflow behavior documented as explicit scope boundaries, not implemented — neither required by any AC)
- Files updated: `production/epics/networking-core/story-006-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 006 → Complete)
- Next recommended: Story 007 — R-U/U-U Batch Framing, Buffer Pooling & Overflow Policy (`production/epics/networking-core/story-007-batch-framing-buffer-pooling-overflow.md`) — last story in the Wire Protocol Core cluster (003-008), Type: Integration

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 005)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-005-version-sequencenumber-stale-discard.md` — Version/SequenceNumber Stale-Discard Helpers
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — both independently traced and confirmed the AC-NC-36 numbering resolution (see below); qa-tester found 2 boundary-coverage gaps (ordinary non-wrapping case, RFC-1982 ambiguous half-circle boundary) → fixed → 10 test methods (13 executions)
- **AC-NC-36 numbering corrected**: story's own AC text had the observed wraparound sequence off by one position relative to `ITransportFaultInjector.SetSequenceNumber`'s already-reviewed "resume from" contract. Two independent specialist reviews traced this from scratch and agreed the story text (not the code) had the error. Corrected in the story file itself (AC-NC-36, QA Test Cases) at closure.
- **Real bug fixed in already-committed Story 001 code**: `TransportFaultInjector.ConsumeNextSequenceNumber()` wrapped `uint.MaxValue` to `0`, contradicting CR-NET-7.5. Fixed to skip to `1`; the one affected existing Story 001 test was updated (not weakened).
- Tech debt logged: TD-010 (unseeded `_sequenceNumber` still defaults to `0` — same invariant, different vector; not fixed, no production send path consumes it yet)
- Files updated: `production/epics/networking-core/story-005-...md` (Status: Complete, ACs checked + corrected numbering, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 005 → Complete)
- Next recommended: Story 006 — Priority-Path Cap & Two-Path Delivery Model (`production/epics/networking-core/story-006-priority-path-cap-two-path-delivery.md`) — next in the Wire Protocol Core cluster

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 004)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-004-entityid-enum-wire-safety-guards.md` — EntityID/Enum Wire-Safety Guards
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — qa-tester found real gaps (missing `DisconnectType` valid-round-trip test, missing adjacent-boundary tests for `DamageType`/`DisconnectType`/`DisconnectReason`) → fixed → 20 test methods total
- **StatID blocker resolved, contrary to the story's own text** — verified directly before implementation that `StatID` is already `enum : byte` (0-16) in both `StatID.cs` and the Character Stats GDD; no workaround was needed or implemented
- **Real C# bug found and fixed**: `Span<byte>` (ref struct) cannot be captured inside a lambda closure — Story 003's test file (`WireProtocol_Envelope_Serialization_tests.cs`, already committed in `de7a15e`) had exactly this bug in `EncodePosition_BoundaryValue_DoesNotOverflowOrThrow`'s two `Assert.DoesNotThrow` lambdas. Found by the implementing subagent, confirmed, fixed (`Span<byte>` → `byte[]` for any buffer referenced inside a throw-assertion lambda), applied consistently in Story 004's own new test file. Undetected until now because this sandbox has no C# compiler — same class of issue as the "first real Unity compile" bugs from 2026-07-04.
- Deviation: `RawValue` property added to `EntityID`/`ItemID`/`CharacterID` (pre-existing structs, outside story's file list) — necessary for `WireIdCodec` to read the wrapped `uint` without reflection (CR-NET-7.3 forbids generic serializers)
- Tech debt logged: None new
- Files updated: `production/epics/networking-core/story-004-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 004 → Complete)
- Next recommended: Story 005 — Version/SequenceNumber Stale-Discard Helpers (`production/epics/networking-core/story-005-version-sequencenumber-stale-discard.md`) — next in the Wire Protocol Core cluster

## Session Extract — /code-review + /story-done 2026-07-09 (Networking Core Story 003)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md` — Message Envelope & Fixed-Point Primitive Serialization
- `/code-review` ran in lean mode with 2 specialists in parallel (unity-specialist, qa-tester): APPROVED WITH SUGGESTIONS — qa-tester found a real gap (no normal-case round-trip test for `EncodeDirection`, only the degenerate-zero-vector case) → fixed (added `EncodeDirection_WorkedValue_RoundTripsWithinTolerance`) → 21 test methods total
- Tech debt logged: TD-008 (no overflow guard on `critChance`/`attackSpeedMultiplier` — directed scope-discipline decision), TD-009 (no NaN/Infinity guard on position/rotation/direction encoders — flagged by unity-specialist)
- Files updated: `production/epics/networking-core/story-003-...md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 003 → Complete), `docs/tech-debt-register.md` (+TD-008, TD-009)
- Next recommended: Story 004 — EntityID/Enum Wire-Safety Guards (`production/epics/networking-core/story-004-entityid-enum-wire-safety-guards.md`) — next in the Wire Protocol Core cluster, builds directly on this story's `MessageEnvelopeCodec`

## Session Extract — /dev-story 2026-07-09 (Networking Core Story 003)

- Story: `production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md` — Message Envelope & Fixed-Point Primitive Serialization
- Pre-implementation: ran a scoped unity-specialist verification pass (not a full engine-risk spawn — story explicitly excludes the NGO send-API surface that makes ADR-004 HIGH risk) confirming `System.Buffers.Binary.BinaryPrimitives` + `Span<byte>`/`ReadOnlySpan<byte>` is IL2CPP-AOT-safe on Unity 6.3 (iOS ARM64 + Linux x64 server) — no generic instantiation, no reflection, no linker stripping risk.
- Files changed: `src/Foundation/Networking/WireProtocol/{ServerMessageEnvelope,ClientEntityMessageEnvelope,MessageEnvelopeCodec,WireFixedPointCodec}.cs` (all new)
- Test written: `tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs` (18 test methods — AC-WP-1, AC-NC-28 including boundary/degenerate-guard cases, AC-NC-03 all covered)
- Key judgment call (directed, not agent-initiated): `critChance`/`attackSpeedMultiplier` encoders intentionally ship with no clamp/log guard (plain round+cast) — CR-NET-7.2 documents a valid range for these two fields but the story's Implementation Notes only specify guards for cycleTimer/position/quaternion/direction. This leaves an unguarded `ushort` wraparound risk if a caller ever passes an out-of-range value — tracked below as a tech-debt candidate, not fixed in this story (would be scope creep).
- **New tech debt candidate (not yet added to docs/tech-debt-register.md — flag for next docs pass)**: `WireFixedPointCodec.EncodeCritChance`/`EncodeAttackSpeedMultiplier` have no overflow guard; an out-of-range input silently wraps via the `ushort` cast rather than clamping+logging like the other four encoders.
- TR registry gap: `TR-net-001` not found in `docs/architecture/tr-registry.yaml` (`requirements: []`, empty project-wide) — same pre-existing systemic gap documented across every prior epic; used the wire-protocol GDD's CR-NET-7.1/7.2 text directly as the source of truth instead.
- Verified all 4 source files + the test file directly (read in full) before reporting — implementation matches the story's encoder/guard spec exactly, naming conventions and doc-comment style match project precedent.
- Blockers: None. Not yet run in the Unity Test Runner (no Editor invocation available in this session) — recommend running the EditMode suite before `/story-done`.
- Not yet committed — this is fresh work, not part of the previously-authorized backlog commit; awaiting explicit go-ahead to commit and/or proceed to `/code-review`.
- Next: `/code-review src/Foundation/Networking/WireProtocol/ tests/EditMode/Networking/WireProtocol_Envelope_Serialization_tests.cs` then `/story-done production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md`

## Session Extract — /code-review + /story-done 2026-07-08 (Networking Core Story 002 — Test Harness cluster COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` — Test Harness: INetworkTestObserver + Release-Build Stripping
- `/code-review` ran with 3 specialists in parallel (unity-specialist, qa-tester, devops-engineer): CHANGES REQUIRED (`Reset()` test only asserted 3 of ~25 capture lists cleared) → fixed to assert all 25 → APPROVED WITH SUGGESTIONS
- Final test count: 13 test methods, all 3 blocking ACs covered; AC-TC-01/02 additionally backed by CI config (`.github/workflows/tests.yml` + `tools/ci/check-test-harness-guards.sh`)
- Tech debt logged: None new (TR-net-009 registry gap pre-existing; AC-TC-01's placeholder CI job tracked as a future follow-up, not tech debt — no real Player build pipeline exists in this repo yet to wire it to)
- **Test Harness cluster (Stories 001-002) is now Complete** — every other Networking Core story references these interfaces in its own tests
- Files updated: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 002 → Complete)
- Next recommended: Story 003 — Message Envelope & Fixed-Point Primitive Serialization (`production/epics/networking-core/story-003-message-envelope-fixed-point-serialization.md`) — first story in the Wire Protocol Core cluster (003-008)

## Session Extract — /dev-story 2026-07-08 (Networking Core Story 002)

- Story: `production/epics/networking-core/story-002-test-harness-observer-release-stripping.md` — Test Harness: INetworkTestObserver + Release-Build Stripping
- Files changed: `src/Foundation/Networking/TestHarness/{INetworkTestObserver,NetworkTestObserver}.cs` (new), `NetworkingTestHarness.cs` (added `CreateNetworkTestObserver()`), `.github/workflows/tests.yml` (2 new CI jobs), `tools/ci/check-test-harness-guards.sh` (new)
- Test written: `tests/EditMode/Networking/NetworkingTestHarness_Observer_tests.cs` (13 test methods — AC-NC-43 via a clearly-scoped test-only queue fixture, not Story 006's real queue)
- Process note: the implementing subagent's final response was truncated mid-sentence ("Let's validate the YAML syntax.") — no summary was received. Verified all files directly (Read every changed/created file) before reporting; everything was complete, correct, and consistent with the story's scope — no corruption or partial writes found.
- Key judgment calls (verified, endorsed): AC-TC-01 implemented as a documented non-blocking CI placeholder (this repo has no real IL2CPP Release Player build step yet — wiring one is out of scope, devops/build-infra concern); AC-TC-02 fully implemented as a real blocking CI check with a `--self-test` mode proving it isn't a no-op.
- Blockers: None
- Next: `/code-review src/Foundation/Networking/ tests/EditMode/Networking/ tools/ci/` then `/story-done production/epics/networking-core/story-002-test-harness-observer-release-stripping.md`

## Session Extract — /code-review + /story-done 2026-07-08 (Networking Core Story 001)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` — Test Harness: Fault/Crash/Zone-Config Injection
- `/code-review` verdict: CHANGES REQUIRED (`Reset()` wasn't clearing `_hasEmittedAnyMessage`, contradicting its "clean state" doc comment — found independently by qa-tester during the review pass) → fixed + regression test added → APPROVED WITH SUGGESTIONS
- Final test count: 31 test methods, all 4 blocking ACs covered
- Tech debt logged: None new (TR-net-009 registry gap is the same pre-existing systemic issue already documented across every epic)
- Files updated: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/networking-core/EPIC.md` (Story 001 → Complete), `src/Foundation/Networking/TestHarness/TransportFaultInjector.cs` (Reset() fix), `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs` (+1 test)
- Next recommended: Story 002 — Test Harness: INetworkTestObserver + Release-Build Stripping (`production/epics/networking-core/story-002-test-harness-observer-release-stripping.md`) — the last Test Harness story before the Wire Protocol Core cluster (003-008) can begin

## Session Extract — /dev-story 2026-07-08 (Networking Core Story 001)

- Story: `production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` — Test Harness: Fault/Crash/Zone-Config Injection
- Files changed: `src/Foundation/Networking/SessionState.cs` (new), `ZoneState.cs` (new), `src/Foundation/Networking/TestHarness/{ITransportFaultInjector,IServerCrashInjector,IZoneTestConfigurator,TransportFaultInjector,ServerCrashInjector,ZoneTestConfigurator,NetworkingTestHarness}.cs` (all new)
- Test written: `tests/EditMode/Networking/NetworkingTestHarness_FaultCrashConfig_tests.cs` (30 test methods across 4 fixtures, all 4 ACs covered — AC-TH-4 verified structurally, full CI stripping check deferred to Story 002)
- Notable judgment call (verified, endorsed): implementing agent replaced the GDD's literal `NetworkingTestHarness.RegisterInterfaces()` wording with a guarded static factory (`Create*` methods) since this project has no DI container and ADR-010 forbids a service-locator/EventBus singleton — same compile-guard effect, no architectural conflict
- Blockers: None. Known limitation: no `dotnet`/`csc` available in the sandboxed shell to run an automated compile check or the real Unity Test Runner — implementation was traced by hand against every test; recommend running the EditMode suite in Unity before/during `/story-done`
- Next: `/code-review src/Foundation/Networking/ tests/EditMode/Networking/` then `/story-done production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md`

## Session Extract — /create-stories networking-core 2026-07-08

- **29 stories written** to `production/epics/networking-core/` — the largest epic in the project (10 GDDs, ~130 ACs total)
- Research approach: 4 parallel Explore agents extracted structured AC/Formula/EdgeCase/Dependency summaries from the 9 sub-contract GDDs (root `networking-core.md` + ADR-004 read directly); synthesis and story decomposition done in main session
- 9 clusters: Test Harness (001-002, implement first — nearly everything else's tests depend on it), Wire Protocol Core (003-008), Tick Loop & Authority (009-011), Session Lifecycle (012-015), Session Token (016), Ghost Session (017-021), OWL Compensation (022-024), Message Routing (025-027), Relevance Filter (028-029)
- Scoping decision: Networking Core owns the envelope/channel/tick/session/ghost/OWL/relevance-filter/test-harness substrate only — every downstream system's specific message schema (NPC Shop, Loot, Party, Inventory, Equipment, Movement, Skill, etc.) is explicitly out of scope, left to each system's own future epic
- Explicitly deferred (blocked on unauthored/unapproved GDDs, or already covered): AC-NC-01 (Zone Instancing boundary), AC-NC-33b (Death & Respawn penalties), AC-NC-08a/08b + AC-NC-44 (Leveling schema-pending), AC-NC-39/wire SelfPositionUpdate (Client-Side Prediction not yet approved), AC-GH-EXP-1-4 (Visual/Feel playtest evidence), AC-NC-25 (already proven by existing Currency System tests)
- **3 cross-doc issues surfaced** (not fixed — flagged in EPIC.md for future propagation-check): (1) OQ-NET-1 BLOCKING — `HEARTBEAT_TIMEOUT_SECONDS` production default still undetermined; (2) AC-ID collision — root GDD and wire-protocol.md each independently define an unrelated "AC-NC-31"; (3) `GHOST_COMBAT_TTL_MINUTES` (session.md, 60s) vs `GHOST_COMBAT_TTL_MIN_S` (ghost-session.md F-GH-1 formula, 30s baseline) — two different constants for what reads as the same concept, Stories 019/021 chose the ghost-session formula as authoritative pending a design decision
- Also flagged: `StatID` enum cross-doc dependency (Character Stats GDD needs `enum:uint`→`enum:byte`, Story 004 workaround in place until then); ADR-004's engine-risk profiling gate (NGO `CustomMessagingManager`/`NetworkManager.ServerTime.Tick` headless-build verification) must pass before implementation sprint is greenlit, per EPIC.md's own pre-existing note
- Files updated: `production/epics/networking-core/story-001` through `story-029` (new), `production/epics/networking-core/EPIC.md` (Stories table + blockers section), `production/epics/index.md` (Networking Core row)
- Next recommended: `/story-readiness production/epics/networking-core/story-001-test-harness-fault-crash-zone-config-injection.md` before starting implementation; consider addressing the ADR-004 engine-risk profiling gate first since it's a pre-sprint blocker per the epic's own Definition of Done

## Session Extract — /code-review + /story-done 2026-07-08 (Currency Story 006 — Currency System epic COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — TransferGold Stub & Compensating Refund
- `/code-review` verdict: APPROVED WITH SUGGESTIONS (non-blocking: optional input-invariance regression test for the stub, optional explicit assertion on `NotImplemented`'s placeholder fields)
- Final test count: 2 test methods, both blocking ACs covered (AC-CS-E-02, AC-CS-E-03)
- Tech debt logged: None new (TR-currency-002 registry gap and GDD `AdminAdjust`/`CompensatingRefund` drift are both pre-existing/already documented, not new to this story)
- **Currency System epic is now fully Complete — all 6 stories Done** (`production/epics/currency-system/EPIC.md` updated to Status: Complete)
- Files updated: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/currency-system/EPIC.md` (Status: Complete, Story 006 → Complete)
- Next recommended: no other Foundation-layer epic has stories remaining in progress. Candidates: `/create-stories networking-core` (largest remaining Foundation epic, 10 sub-contracts, not yet story'd), or start Core-layer epics (`/create-epics layer: core`) for Leveling/Inventory/Loot Table (needed to unblock Character Stats Story 008, which is Blocked). Note: `production/epics/index.md` is stale (last updated 2026-06-27, predates all story creation) — consider refreshing it.

## Session Extract — /story-readiness + /dev-story 2026-07-08 (Currency Story 006)

- `/story-readiness` verdict: READY (2 advisory gaps noted, neither blocking): (1) TR-currency-002 not in `tr-registry.yaml` — same pre-existing empty-registry systemic gap as every prior Currency story; (2) new finding — `design/gdd/currency-system.md` EC-CS-5 still says the compensating refund uses `GoldTransactionReason.AdminAdjust`, which is stale — ADR-001 Decision 3 and `control-manifest.md` (line 29) both mandate the dedicated `CompensatingRefund` value instead. The story's own text already correctly specifies `CompensatingRefund`, so implementation followed the story/ADR/manifest, not the stale GDD line. GDD fix deferred as a follow-up propagation-check edit, not done this session.
- Story: `production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — TransferGold Stub & Compensating Refund
- Files changed: `src/Foundation/Currency/ICurrencyService.cs` (added `TransferGold` to interface), `src/Foundation/Currency/CurrencySystem.cs` (stub implementation, always `NotImplemented`, touches neither balance), `tests/EditMode/Currency/Currency_EdgeCases_tests.cs` (new, 2 tests)
- Test written: `tests/EditMode/Currency/Currency_EdgeCases_tests.cs` (2 test methods — AC-CS-E-02, AC-CS-E-03)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md` — this is the last story in the Currency System epic

## Session Extract — /code-review + /story-done 2026-07-08 (Currency Story 005)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/currency-system/story-005-goldsyncevent-emission.md` — GoldSyncEvent Emission
- `/code-review` ran once: CHANGES REQUIRED (missing test proving `OnGoldSync` fires exactly once on a successful retry after a forced `ConcurrencyConflict` — independently flagged by both unity-specialist and qa-tester) → fixed → APPROVED WITH SUGGESTIONS
- Final test count: 9 test methods, all 4 blocking ACs covered plus the retry-fires-once edge case
- Tech debt logged: None new (TR-currency-004 registry gap is the same pre-existing systemic issue already covered in EPIC.md, not new)
- Files updated: `production/epics/currency-system/story-005-goldsyncevent-emission.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/currency-system/EPIC.md` (Story 005 → Complete), `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs` (+1 test)
- Next recommended: Story 006 — TransferGold Stub & Compensating Refund (`production/epics/currency-system/story-006-transfergold-stub-and-compensating-refund.md`)

## Session Extract — /dev-story 2026-07-04 (Currency Story 005)
- Story: `production/epics/currency-system/story-005-goldsyncevent-emission.md` — GoldSyncEvent Emission
- Files changed: `src/Foundation/Currency/GoldSyncEventArgs.cs` (new), `src/Foundation/Currency/ICurrencyService.cs`, `src/Foundation/Currency/CurrencySystem.cs`
- Test written: `tests/EditMode/Currency/Currency_GoldSyncEvent_tests.cs` (8 test methods, all 4 ACs covered)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-005-goldsyncevent-emission.md`

*Updated: 2026-07-04*

## Current Status

**Task**: Character Stats epic Stories 001-007 complete (008 Blocked). Item Database epic Complete (4/4). Currency System epic — Story 001 Complete (1/6); Stories 002-006 Ready. Real Unity project verified working.
**Stage**: Pre-Production
**GDD Count**: 38 Approved, 0 In Review = 38 of 38 MVP complete (6 Presentation GDDs deferred)
**ADR Count**: 10 written (ADR-001 through ADR-010), all Accepted
**UX Specs**: `design/ux/hud.md` (complete), `design/ux/interaction-patterns.md` (28 patterns), `design/accessibility-requirements.md` (Standard tier)

## Session Extract — Real bug found on first-ever Unity compile 2026-07-04

- **Bug**: `CharacterStats.StatChangedHandler` and `CharacterStats.EntityDiedHandler` (Story 005) are delegate types nested inside the `CharacterStats` class, not top-level types in the `IronGrind.CharacterStats` namespace. `using IronGrind.CharacterStats;` only imports the namespace — it does not bring a class's nested types into unqualified scope. Test files referenced both bare (`StatChangedHandler handler = ...`, `new EntityDiedHandler(...)`), which compiles fine when *inside* the `CharacterStats` class (all production usages in `CharacterStats.cs` are fine) but fails with CS0246 from *outside* it.
- This is exactly what TD-006 predicted: a real, pre-existing defect from Story 005 that sat undetected through code review and `/story-done` sign-off because nothing had ever actually compiled in Unity until this session.
- **Fixed**: qualified all 9 bare usages in `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` and `tests/EditMode/CharacterStats/TestHelpers/StatEventRecorder.cs` as `IronGrind.CharacterStats.CharacterStats.StatChangedHandler`/`.EntityDiedHandler`, matching the file's existing fully-qualified pattern for `_stats` (same namespace/class name collision as `IronGrind.ItemDatabase.ItemDatabase`).
- Verified `BuffModifierEntry`/`EquipmentModifierEntry` do NOT have the same issue — both are top-level types in their own files, not nested in `CharacterStats`.
- **Second bug found on same compile pass**: CS0104 ambiguous reference `Object` between `System.Object` and `UnityEngine.Object` in `ItemDatabase_Core_tests.cs:39` (`Object.DestroyImmediate(def)`). Same latent bug pattern in 3 more files that all use the identical `[TearDown] Object.DestroyImmediate` convention: `ItemDatabase_MvpRecords_tests.cs`, `ItemDatabase_Validator_Error_tests.cs`, `ItemDatabase_Validator_Warning_tests.cs`. Fixed all 4 by qualifying as `UnityEngine.Object.DestroyImmediate(def)`. (2 of these files had no explicit `using System;` yet still needed the fix — Unity 6.3's project likely has implicit global usings enabled, making `System` ambient project-wide regardless of per-file usings.)
- **Third bug found on same compile pass**: CS0234 in `CharacterStatsFixture.cs` — `CharacterStats.StatSlotCount` (bare) failed because this file's own namespace, `IronGrind.Tests.EditMode.CharacterStats`, *also* ends in the segment "CharacterStats." That makes the bare identifier "CharacterStats" resolve to a namespace tail rather than the production class, so the compiler reports "StatSlotCount does not exist in the namespace" (CS0234, distinct from CS0246 — correctly diagnosing a namespace/type confusion, not a missing type). Fixed all 4 occurrences by fully qualifying as `IronGrind.CharacterStats.CharacterStats.StatSlotCount`. Verified via project-wide grep that no other bare `CharacterStats.Member` shorthand remains anywhere in the test suite — every reference is now fully qualified.
- **Fourth bug found on same compile pass**: CS0221 in `ItemDatabase_Core_tests.cs:130` — `(ItemCategory)999` is a compile-time error, not a runtime concern: `ItemCategory` is `byte`-backed and `999` overflows a byte, which C# rejects for constant enum casts (would need `unchecked`, which the story never intended). The story's own AC-19 text used "999" as its illustrative out-of-range value, and the test carried that same overflow bug through implementation and code review, undetected until real compilation. Fixed by using `255` instead (matching the pattern already correctly used elsewhere for `(GearSlot)255`/`(StatID)255` in the Story 002 validator tests) — updated both the cast and its matching `LogAssert.Expect` message. Grepped for other literal-999-to-enum-cast patterns project-wide; none found.
- **Fifth issue — a red herring, not a bug**: user reported console "warnings" after all 128 tests passed. Confirmed these are the intentional `Debug.LogError` calls from write-lock rejection and equipment-modifier capacity-overflow test scenarios (both documented, by-design production behavior), correctly declared via `LogAssert.Expect` so the tests pass despite the console still showing the red error line (LogAssert suppresses test failure, not console output). No fix needed.
- **MILESTONE: All 128 EditMode tests pass** — Character Stats (Stories 001-007) and Item Database (Stories 001-003) are the first code in this project's history to actually compile and execute in Unity, confirming the designs are sound beyond code review. TD-002 and TD-006 closed in `docs/tech-debt-register.md`.
- Remaining open item: Story 004 (MVP Item Records) — the `ItemDatabaseSeeder` menu tool hasn't been run yet; the 34 real `.asset` files and smoke-check evidence are still pending.

## Session Extract — /story-done 2026-07-04 (Story 004 — Item Database epic COMPLETE)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-004-mvp-item-records.md` — MVP Item Records — 34 Authored ScriptableObject Assets
- Seeder run in real Unity Editor: 0 fatal, 0 errors, 0 warnings across all 34 records. Smoke check recorded at `production/qa/smoke-2026-07-04-item-database.md`.
- **Item Database epic is now fully Complete** — all 4 stories Done (`production/epics/item-database/EPIC.md` updated)
- Tech debt logged: None new (deviations were advisory, already covered by TD-002/TD-006 closure notes)
- Next recommended: Character Stats Story 008 is Blocked (depends on Leveling System epic, not yet started). No other Foundation-layer epic has stories created yet. Candidates: `/create-stories currency-system` or `/create-stories networking-core` (both epics exist, Ready, per `production/epics/index.md`), or start the Leveling System epic to unblock Character Stats Story 008.

## Session Extract — /create-stories currency-system 2026-07-04

- 6 stories written to `production/epics/currency-system/`: Core Types & AddGold, TrySpendGold, Guards & State Machine, Concurrency Safety, GoldSyncEvent Emission, TransferGold Stub & Compensating Refund
- All 6 are Logic type, Foundation layer, scoped as an in-memory C# model (`CurrencySystem : ICurrencyService`) matching the CharacterStats/ItemDatabase precedent — real PostgreSQL persistence deferred to Character Persistence (not yet built)
- Explicitly scoped OUT: GDD Group G (ServerLogic.asmdef isolation — no server/client split exists yet), GDD Group I (session resync — needs Networking Core's handshake, not yet built)
- Key design decisions embedded in the stories: `CharacterID`/`GoldTransactionReason`/`GoldMutationResult`/`GoldMutationError` types sourced authoritatively from `design/registry/entities.yaml` (already pre-registered with exact enum values); Story 003 revises Stories 001-002's lazy-balance-creation into explicit `RegisterCharacter` registration (needed for `CharacterNotFound` to be meaningful); Story 004 uses a real optimistic CAS-with-retry pattern (not a single big lock) with an `internal` test seam (`TryCompareAndSwapSpend`) so `ConcurrencyConflict` can be tested deterministically per this project's test-standards rule, while final-state correctness under real concurrency is tested via `Task.WhenAll`
- Next recommended: `/story-readiness production/epics/currency-system/story-001-core-types-and-addgold.md`, or `/create-stories networking-core` if you want all epics story'd out before implementing

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 001)

- `/story-readiness` verdict: NEEDS WORK → fixed (all 6 stories were missing the `Estimate` header field — caught during this check, added: 3-4h/1-2h/2-3h/3-4h/2-3h/1-2h for stories 001-006) → READY
- Story: `production/epics/currency-system/story-001-core-types-and-addgold.md` — Core Types & AddGold (Cap-Safe Addition)
- Files changed: `src/Foundation/Currency/{CharacterID,GoldTransactionReason,GoldMutationError,GoldMutationResult,ICurrencyService,CurrencySystem}.cs` (new), `tests/EditMode/Currency/Currency_AddGold_tests.cs` (new, 6 tests)
- Process note: same approval-chain limitation as earlier this session — implementing subagent correctly refused a relayed "approved," so files were written directly by the orchestrator using the subagent's already-reviewed exact code.
- Test written: 6 tests covering AC-CS-A-01, A-04, A-05 (+ uint.MaxValue edge case), C-01, C-02
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-001-core-types-and-addgold.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 004)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-004-concurrency-safety.md` — Concurrency Safety (Thread-Safe Balance Mutation)
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — two independent specialist passes (Unity + qa-tester) scrutinized the concurrency correctness in depth, found no race conditions/deadlocks; doc-comment caveat added to RegisterCharacter (not lock-protected, not safe for concurrent re-registration); two lower-priority suggestions deferred (OR-assertion annotation, extra 3-way/interleaved-op test coverage beyond the 3 stated ACs)
- Final test count: 5 test methods, all 3 blocking ACs covered
- Tech debt logged: None
- Next recommended: Story 005 — GoldSyncEvent Emission (`production/epics/currency-system/story-005-goldsyncevent-emission.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 004)

- `/story-readiness` verdict: READY (no gaps)
- Story: `production/epics/currency-system/story-004-concurrency-safety.md` — Concurrency Safety (Thread-Safe Balance Mutation)
- Switched `CurrencySystem`'s internal storage from plain `Dictionary` to `ConcurrentDictionary` (`_balances`, new `_versions`, new `_locks`) for structural thread-safety across different characters, layered with a per-character `lock` (`GetLockFor`) for compound-operation atomicity. `AddGold` is a single atomic lock (never rejects on conflict). `TrySpendGold` is now a 2-attempt retry wrapper around new `internal TryCompareAndSwapSpend` (optimistic version-checked CAS), guard order extended to: CharacterNotFound → InvalidAmount → version mismatch (ConcurrencyConflict) → InsufficientFunds → success.
- Files changed: `src/Foundation/Currency/CurrencySystem.cs`, `src/Foundation/Currency/ICurrencyService.cs` (doc comments only), `tests/EditMode/Currency/Currency_Concurrency_tests.cs` (new, 5 tests — 2 real-concurrency via Task.WhenAll asserting only invariant final state, 3 deterministic via the internal CAS seam)
- Pre-existing 3 Currency test files unaffected (verified: only touch the public interface, ConcurrentDictionary preserves identical single-threaded semantics)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-004-concurrency-safety.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 003)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-003-guards-and-state-machine.md` — Input Guards & State Machine
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — guard-order-proving test added (real gap: no test proved CharacterNotFound is checked before InvalidAmount); double-register overwrite test and TryGetValue refactor deferred (both minor, not logged as tech debt — too low priority)
- Final test count: 13 test methods, all 10 blocking ACs covered
- Tech debt logged: None
- Next recommended: Story 004 — Concurrency Safety (`production/epics/currency-system/story-004-concurrency-safety.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 003)

- `/story-readiness` verdict: READY (no gaps)
- Story: `production/epics/currency-system/story-003-guards-and-state-machine.md` — Input Guards & State Machine
- This story revised Stories 001/002's guard-free behavior: added `RegisterCharacter` to `ICurrencyService`/`CurrencySystem`; `AddGold`/`TrySpendGold` now check `CharacterNotFound` then `InvalidAmount` before their formula guard. Critically required fixing 10 of 11 pre-existing Story 001/002 tests (added `RegisterCharacter` calls) so they kept exercising their original behavior instead of spuriously failing with `CharacterNotFound`; `GetBalance_CharacterNeverTouched_ReturnsZero` correctly left unregistered.
- Files changed: `src/Foundation/Currency/ICurrencyService.cs`, `src/Foundation/Currency/CurrencySystem.cs`, `tests/EditMode/Currency/Currency_AddGold_tests.cs` (6 tests patched), `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` (4 tests patched), `tests/EditMode/Currency/Currency_GuardsAndStateMachine_tests.cs` (new, 11 tests)
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-003-guards-and-state-machine.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 002)

- Verdict: COMPLETE
- Story: `production/epics/currency-system/story-002-tryspendgold.md` — TrySpendGold (Spend Guard)
- `/code-review` verdict: APPROVED (no required changes)
- Final test count: 4 test methods, all 3 blocking ACs + 1 boundary edge case covered
- Tech debt logged: None (deviations were None; TR-registry gap already documented as known epic-level tech debt in EPIC.md, not new)
- Next recommended: Story 003 — Input Guards & State Machine (`production/epics/currency-system/story-003-guards-and-state-machine.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Currency Story 002)

- `/story-readiness` verdict: NEEDS WORK → fixed → READY. Two gaps found: (1) stale `GetOrCreateBalance(charId)` reference in the story's F-CS-2 code sample — that helper no longer exists after Story 001's code-review simplification; fixed to `GetBalance(charId)`. (2) `TR-currency-001` not found in `docs/architecture/tr-registry.yaml` — registry's `requirements:` list is empty project-wide (systemic, pre-existing, affects every story including the already-Complete Story 001); accepted as known tech debt, not blocking.
- Story: `production/epics/currency-system/story-002-tryspendgold.md` — TrySpendGold (Spend Guard)
- Files changed: `src/Foundation/Currency/ICurrencyService.cs` (added `TrySpendGold` signature), `src/Foundation/Currency/CurrencySystem.cs` (implemented F-CS-2 guard-before-subtract), `tests/EditMode/Currency/Currency_TrySpendGold_tests.cs` (new, 4 tests)
- Test written: 4 tests covering AC-CS-A-02, A-03, E-01, plus cost==balance boundary edge case; failure-path tests explicitly assert balance unchanged via `GetBalance`, not just call failure
- Blockers: None
- Next: `/code-review src/Foundation/Currency/ tests/EditMode/Currency/` then `/story-done production/epics/currency-system/story-002-tryspendgold.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Currency Story 001)

- Verdict: COMPLETE (no deviations)
- Story: `production/epics/currency-system/story-001-core-types-and-addgold.md` — Core Types & AddGold (Cap-Safe Addition)
- `/code-review` verdict: APPROVED WITH SUGGESTIONS — both applied (added `GetBalance_CharacterNeverTouched_ReturnsZero` test; simplified `AddGold` to remove a redundant double-write via `GetOrCreateBalance`, now just calls `GetBalance` directly)
- Final test count: 7 test methods, all 5 blocking ACs covered
- Tech debt logged: None (clean verdict)
- Next recommended: Story 002 — TrySpendGold (Spend Guard) (`production/epics/currency-system/story-002-tryspendgold.md`)

## Session Extract — /story-done 2026-06-29

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Tech debt logged: None (3 advisory items in Completion Notes)
- Pre-Story-003 action: fix B-01 (add IsFloatStat guard to GetEffectiveStat / GetEffectiveStatFloat)
- Next recommended: Story 003 — Modifier lifecycle (story-003-modifier-lifecycle.md)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-004-resource-pools.md` — Resource Pools
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (5 edits: _currentHp/_currentMp fields, OnEntityDied stub, GetCurrentHP/MP accessors, GetEffectiveStat early-exit, RemoveEquipmentModifier MaxHP reconciliation, full method implementations)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` (12 test methods)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-004-resource-pools.md`

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Files changed:
  - `src/Foundation/CharacterStats/StatID.cs` — extended with float-schema stats (CritChance=12 through MovementSpeed=16)
  - `src/Foundation/CharacterStats/StatSchema.cs` — new file; schema routing, FloatStatArraySize=5, GetStatMin/GetStatMax
  - `src/Foundation/CharacterStats/CharacterStats.cs` — per-entity internal modifier dicts, FloatStatValues, GetBaseStatFloat/SetBaseStatFloat, GetEffectiveStat + GetEffectiveStatFloat with absent-stat guard
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — added SetFloatBaseStat, SetEquipmentModifiers, SetBuffModifiers, ClearModifiers
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — new file, 11 test methods covering AC-01, AC-02, AC-03, AC-04, AC-05, AC-24, AC-28a, AC-28b, AC-21, AC-30, AC-26
- Blockers: None

## Session Extract — /dev-story 2026-06-29 (Story 003)

- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — storage migrated to per-entity-per-stat nested dicts; AddBuffModifier, AddEquipmentModifier, RemoveBuffModifier, RemoveEquipmentModifier implemented with write-lock guard, duplicate-ID overwrite (no double-stack), capacity overflow (log+return); GetEffectiveStat/GetEffectiveStatFloat updated to query per-stat buckets
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — SetEquipmentModifiers and SetBuffModifiers updated to require StatID parameter; ClearModifiers unchanged
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — all fixture calls updated to pass appropriate StatID (no AC changes, test plumbing only)
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` — new file, 7 test methods covering AC-16, AC-17, AC-18, AC-19, AC-20, AC-22, capacity overflow
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-003-modifier-lifecycle.md
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-002-modifier-stack.md`

## Gate Check Session (2026-06-27)

All 4 prior blockers from the morning FAIL resolved in this session:

| Blocker | Resolution |
|---|---|
| No test framework | `/test-setup` → `tests/EditMode/`, `tests/PlayMode/`, `.github/workflows/tests.yml`, `tests/EditMode/SmokeTest.cs` |
| No architecture.md | `/create-architecture` → `docs/architecture/architecture.md` v1.0, TD sign-off |
| No accessibility requirements | `design/accessibility-requirements.md` — Standard tier committed |
| No interaction pattern library | `/ux-design patterns` → `design/ux/interaction-patterns.md`, 28 patterns |

Director panel (lean mode — all 4 run as PHASE-GATEs):
- Creative Director: CONCERNS (5 items)
- Technical Director: READY (2 tracked conditions)
- Producer: CONCERNS (4 items)
- Art Director: CONCERNS (4 items)

Gate report: `production/gate-checks/technical-setup-to-pre-production-2026-06-27.md`

## ADR Session — 2026-06-27

Both Foundation Required ADRs written (previously blocking Zone Instancing + Feature-layer stories):

| ADR | Title | Status | Domain | Engine Risk |
|-----|-------|--------|--------|-------------|
| ADR-009 | Scene/Zone-Load Management | Accepted | Core — Scene Management | HIGH |
| ADR-010 | Event/Messaging Architecture | Accepted | Core — C# Messaging | LOW |

Registry updated: 4 new forbidden patterns (`scene_handle_as_int`, `urp_setup_render_passes_loading_screen`, `central_event_bus`, `lambda_capture_persistent_subscription`) and 2 new interface contracts (`point_to_point_messaging`, `broadcast_messaging`).

## Top Priority Items for Pre-Production

1. **[IMMEDIATE]** Resolve party drop bonus contradiction (CD Concern 1 — HIGH) — amend game concept OR restore bonus
2. ~~Write Scene/Zone-Load Management ADR~~ ✓ DONE (ADR-009)
3. ~~Write Event/Messaging Architecture ADR~~ ✓ DONE (ADR-010)
4. ~~`/create-control-manifest`~~ ✓ DONE — `docs/architecture/control-manifest.md` v2026-06-28 (99 rules across 4 layers + global)
4b. ~~`/create-stories character-stats`~~ ✓ DONE — 8 stories written (001–007 Ready, 008 Blocked pending Leveling System); QA Lead gate passed with revisions; EPIC.md DoD updated to AC-01–AC-34
5. Name/license typeface (AD Concern 1 — before UI asset production)
6. Author 6 deferred MVP GDDs (Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding)

## Manual Step Pending

- Add `UNITY_LICENSE` to GitHub repository secrets (required for `.github/workflows/tests.yml` to pass)

## Revision Pass — Combat UI GDD (2026-06-20)

All 23 blocking items from the design-review specialist pass have been resolved.
Pending: systems-index update, review-log append, and Phase 5 closing widget.

| File | Change | Status |
|---|---|---|
| `design/gdd/combat-ui.md` | Status "In Design" → "In Review" (original triad) | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-9: bar state now persists server-side (1-bit barIsExpanded in SkillBarLayout) | ✓ Done |
| `design/gdd/combat-ui.md` | Bar States table: removed "or zone entry" from Expanded→Collapsed; Collapsed = "first zone entry" | ✓ Done |
| `design/gdd/combat-ui.md` | F-CUI-1: fixed uint underflow, NaN/div-zero, compile error; signed long subtraction | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-18: added Silenced (8) + CasterNotAlive (9) rejection codes | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-19: removed Painter2D mandate; implementation-defined renderer with perf AC | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-4: 5s timeout + re-request + fallback recovery path | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-5: zone entry now loads persisted barIsExpanded (not reset to Collapsed) | ✓ Done |
| `design/gdd/combat-ui.md` | Interactions table: removed stale OQ-CUI-3 reference from Notes | ✓ Done |
| `design/gdd/combat-ui.md` | Dependencies Pending Amendments: CR-SK-12 + hud.md marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | Visual Requirements cooldown arc: removed "via Painter2D"; added countdown number row | ✓ Done |
| `design/gdd/combat-ui.md` | UI Requirements: replaced Painter2D driver line with implementation-defined perf note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-2: 250ms → 200ms (matches CR-CUI-8) | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-8: added tick rate reference (40 ticks @ 20t/s = 2s), 60fps arc note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-16: rewritten to test corridorWidth < 140dp (not safe area width) | ✓ Done |
| `design/gdd/combat-ui.md` | Added AC-CUI-18 (bar state persistence), AC-CUI-19 (expand gating), AC-CUI-20 (Silenced visual) | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-3: marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-6: updated to reference Silenced (8) resolution; marked RESOLVED | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-2: added V-0 (CasterNotAlive liveness check) + V-5b (Silenced check) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-12: amended to require SkillCooldownUpdate with OnCooldown rejections (EC-CUI-4 guarantee) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-23: added explicit SkillCooldownUpdate delivery contract | ✓ Done |
| `design/registry/entities.yaml` | SkillCastRejectionCode: added Silenced=8, CasterNotAlive=9 | ✓ Done |
| `production/session-state/active.md` | This update | ✓ Done |

## Combat UI GDD Summary

**File**: `design/gdd/combat-ui.md`
**Status**: In Review — ready for `/design-review`
**Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

### All 11 Sections Written and Approved

1. Overview ✓ — Zone E skill bar, UI Toolkit (ADR-005), 10-skill/8-slot bar, CUS potbar separate, SkillCastRequest + SkillCooldownUpdate flow
2. Player Fantasy ✓ — "Instrument Panel with consequence tone"; potions do NOT reset auto-attack cadence
3. Detailed Design ✓ — CR-CUI-1 through CR-CUI-22 (bar structure, layout, expand/collapse, slot binding, casting, cooldown, device adaptation)
4. Formulas ✓ — F-CUI-1 (cooldown fraction), F-CUI-2 (Zone E left boundary), F-CUI-3 (chat corridor), F-CUI-4 (implementation constants)
5. Edge Cases ✓ — EC-CUI-1 through EC-CUI-10
6. Dependencies ✓ — upstream/downstream dependencies; pending amendments table
7. Tuning Knobs ✓ — 9 knobs in CombatUIConfig.asset
8. Visual/Audio Requirements ✓ — slot states, cooldown arc, animations, toasts, audio ownership boundaries
9. UI Requirements ✓ — Unity 6.3 implementation constraints (UI Toolkit, Painter2D, safe area, PickingMode)
10. Acceptance Criteria ✓ — AC-CUI-1 through AC-CUI-17
11. Open Questions ✓ — OQ-CUI-1 through OQ-CUI-7

### Key Design Decisions Locked

- **Bar layout**: 4 primary slots (S1-S4) + [+] toggle to expand to 8 (S5-S8 expand above); Zone E footprint = 292dp
- **Consumables**: Skills-only bar; CUS potbar is a separate UI element (Option B)
- **Skill binding**: Players choose any 8 of 10 class skills via in-bar long-press context menu
- **Cooldown wire format**: `cooldownExpiryTick` (absolute server tick) — CR-SK-12 amended
- **Cooldown renderer**: Painter2D `generateVisualContent` callback; no RenderTexture, no shader dependency
- **SE3 chat corridor**: Combat-collapse when corridorWidth < 140dp (CHAT_CORRIDOR_MIN_WIDTH_DP)
- **Auto-attack toggle**: HUD owns it in Zone D — NOT Combat UI
- **Expand direction**: Expand above (primary row fixed; party frame overlap accepted MVP)

## HUD Pre-Implementation Gates

| Gate | Status | Notes |
|---|---|---|
| OQ-HUD-1: UI framework ADR | **RESOLVED** — ADR-005 Proposed | UI Toolkit chosen |
| OQ-HUD-7: design/ux/hud.md | **RESOLVED** — spec complete 2026-06-20 | XP bar color `#C4912A` decided |
| OQ-HUD-8: Status Effects event interface | Open | Blocks buff tray implementation only |

## Open Items (non-blocking to Combat UI review)

| ID | Description | Owner | Priority |
|---|---|---|---|
| OQ-CUI-1 | CUS potbar position in Zone E | HUD + CUS GDD | Pre-impl |
| OQ-CUI-2 | Cooldown arc color | Art Director | Pre-impl |
| OQ-CUI-4 | Zone D auto-attack toggle spec | HUD GDD amendment | BLOCKING for HUD impl |
| OQ-CUI-6 | Status effect skill disable visual | Advisory | Low |
| OQ-CUI-7 | Expanded bar / party frame overlap — SE3 | Lead sign-off | Pre-impl |
| OQ-HUD-8 | Status Effects event interface | Must define before buff tray impl | Advisory |
| OQ-UX-HUD-1 | Charge bar obsolescence decision | Auto-Attack Combat + playtest | Advisory |
| OQ-UX-HUD-7 | Party chat input mode spec | Party Chat GDD | Advisory |

## Architecture Review — 2026-06-21

`/architecture-review full` completed. Verdict: **FAIL (advisory)**.

**Files written:**
- `docs/architecture/architecture-review-2026-06-21.md` — full report
- `docs/architecture/traceability-index.md` — domain-level coverage matrix

**Key findings:**

| Finding | Severity |
|---|---|
| Persistence Layer ADR missing — blocks ADR-001 + ADR-004 | 🔴 Blocking |
| ADR-001: 2 propagations never applied (character-persistence.md, networking-session.md) | 🔴 Blocking |
| ADR-005 still Proposed — auto-attack-combat.md + combat-ui.md already build on it | 🔴 Blocking |
| ADR-001 missing Engine Compatibility + ADR Dependencies sections | ⚠️ Template gap |
| ADR-002 link.xml uses wrong assembly (`UnityEngine` vs `UnityEngine.AIModule`) | 🔴 Build risk |
| ADR-005 `VisualElement.transform` "removed by 6.3" contradicts pinned reference (deprecated only) | 🟠 Factual |
| ADR-005 `ApplySafeArea` code references undefined `panelWidth`/`panelHeight` | 🟠 Compile error |
| Hosting Backend ADR missing | ❌ Gap |
| ADR-006 Combat UI framework missing | ❌ Gap |

**TR-registry:** Still empty — no per-TR IDs minted this pass (domain-level only).

## Gate Check Result — 2026-06-21

`/gate-check pre-production` → **FAIL**
Report: `production/gate-checks/pre-production-to-production-2026-06-21.md`

Director panel: CD NOT READY · TD NOT READY · PR NOT READY · AD CONCERNS
Artifacts: 5/16 present · Quality checks: 0/10 · Vertical Slice: AUTO-FAIL (does not exist)

Top blockers (dependency order):
1. **Persistence Layer ADR** — write via `/architecture-decision persistence-layer`
2. **ADR-001 propagations** — apply to character-persistence.md + networking-session.md
3. **ADR-005 → Accepted** — after on-device profiling + specialist fixes
4. **Hosting Backend ADR** — `/architecture-decision hosting-backend`
5. **ADR-006 Combat UI** — `/architecture-decision combat-ui-framework`
6. No control manifest → `/create-control-manifest` after ADRs
7. No epics/stories → `/create-epics`, `/create-stories`
8. 6 MVP GDDs not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding
9. No Vertical Slice build (enhancement destruction required; blind-tested)
10. No real playtests (0 of 3 required)

## ADR-006 — Persistence Layer (2026-06-27)

Written: `docs/architecture/ADR-006-persistence-layer.md` — Status: Proposed
**Decisions**: PostgreSQL + Npgsql + Dapper; PendingPurchase → separate table, same DB; ≤50ms write budget
**Resolved**: ADR-001 OQ-ADR1-1 (PendingPurchase storage), ADR-004 OQ-NET-5 / OQ-ADR4-2 (write latency)
**Registry**: 4 new stances added (character_record ownership, pending_purchase ownership, persistence_database API, EF Core forbidden)
**Constraint added**: Hosting Backend ADR must enforce PostgreSQL co-location with game server.

## ADR-001 Propagations Applied (2026-06-27)

| File | Change | Status |
|---|---|---|
| `design/gdd/networking-session.md` | CR-NET-6.4: inserted step 2 (PendingPurchase reconciliation before handshake emission) | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-1: added BeginPurchase/CompletePurchase/RefundPurchase/LoadOutstandingPurchases to ICharacterPersistence + PendingPurchaseResult enum + PendingPurchaseRecord struct | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-12: new rule — PendingPurchase storage and lifecycle (separate table, same DB, ADR-006 Decision 3) | ✓ Done |
| `design/gdd/character-persistence.md` | Interactions table: NPC Shop row added as upstream caller | ✓ Done |

## ADR-005 Promoted to Accepted (2026-06-27)

| Fix | Status |
|---|---|
| `ApplySafeArea`: added `panelSize = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height))` — removes undefined `panelWidth`/`panelHeight` references | ✓ Done |
| Constraints: corrected "`VisualElement.transform` removed effectively by 6.3" → "deprecated since 6.2, still compiles with warning in 6.3" (aligns with deprecated-apis.md) | ✓ Done |
| ADR Dependencies: updated Combat UI reference from ADR-006 → ADR-007 | ✓ Done |
| Status: Proposed → Accepted (2026-06-27) | ✓ Done |

Note: device profiling (iPhone SE 3rd gen, < 0.3ms HUD update) is a pre-implementation gate captured in ADR-005 Validation Criteria — not a pre-Accepted gate.

## ADR-007 — Hosting Backend (2026-06-27)

Written: `docs/architecture/ADR-007-hosting-backend.md` — Status: Proposed
**Decision**: Self-hosted Hetzner CPX41 VPS (Ubuntu 22.04 LTS) + co-located PostgreSQL (loopback, ~0.1ms)
**Zone server**: One Unity 6.3 IL2CPP headless process per active zone instance (systemd)
**Client connectivity**: Direct NGO UDP to public IP:port (no relay)
**Resolved**: ADR-004 OQ-ADR4-1 (hosting backend deferred), OQ-NET-5 (write latency)
**Registry**: 3 new stances added (game_server_hosting, zone_server_process_model, unity_relay_for_dedicated_server forbidden)
**Engine specialist correction**: `NetworkTransform.Update()` → `NetworkTransform.OnUpdate()` in NGO 6.3 — flagged as risk in ADR

## ADR-008 — Combat UI Framework (2026-06-27)

Written: `docs/architecture/ADR-008-combat-ui-framework.md` — Status: Proposed
**Decision**: Painter2D `generateVisualContent` for cooldown arcs; `SkillBarPresenter : MonoBehaviour` with `Queue<T>` decoupling; USS `transition: height 200ms` (GDD-locked, CR-CUI-8); Unity 6.0 event API names
**Resolved**: CR-CUI-19 (cooldown renderer implementation-defined → Painter2D chosen)
**Registry**: 4 new stances (combat_ui_arc_renderer, skill_bar_presenter_pattern, deprecated_ui_event_api_names, direct_visually_element_from_network_behaviour forbidden)
**Pre-sprint gate**: `performance-analyst` must validate 8× Painter2D arcs ≤0.3ms on iPhone SE 3rd gen before combat UI implementation begins (CR-CUI-19 blocking gate)

## ADR Count as of 2026-06-27

ADR-001 through ADR-008 written. All priority ADRs from architecture-review-2026-06-21 are now addressed.

## Foundation Epics — 2026-06-27

`/create-epics layer: foundation` complete. 4 epics written, index created.

| Epic Slug | Layer | GDD(s) | Status |
|---|---|---|---|
| character-stats | Foundation | design/gdd/character-stats.md | Ready |
| item-database | Foundation | design/gdd/item-database.md | Ready |
| currency-system | Foundation | design/gdd/currency-system.md | Ready |
| networking-core | Foundation | design/gdd/networking-core.md + 9 sub-contracts | Ready |

**Files written:**
- `production/epics/character-stats/EPIC.md`
- `production/epics/item-database/EPIC.md`
- `production/epics/currency-system/EPIC.md`
- `production/epics/networking-core/EPIC.md`
- `production/epics/index.md`

**Open items from this pass:**
1. `docs/architecture/tr-registry.yaml` is empty — populate before `/story-readiness` can run
2. `docs/architecture/architecture.md` Foundation module table still shows `⚠️` for ADR-009/ADR-010 — update to remove markers (both Accepted 2026-06-27)
3. Leveling/Inventory/Loot Table in architecture.md Foundation table → actually Core layer per systems-index; will appear in `/create-epics layer: core`

## Recommended Next Steps

1. **`/create-stories character-stats`** — first implementable stories (lowest risk, no engine surface)
2. **`/create-stories item-database`** — data layer stories
3. **`/create-stories currency-system`** — economy foundation stories
4. **`/create-stories networking-core`** — largest epic; 10 sub-contracts
5. **`/create-epics layer: core`** — Core layer epics after Foundation stories are underway
6. 6 MVP GDDs still not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding

## Prior Approved GDDs (37 total)

Character Stats, Item Database, Currency System, Class System, Leveling System, Auto-Attack Combat, Skill System, Damage Calculation, Networking Core, networking-session, networking-wire-protocol, networking-test-harness, networking-session-token, networking-ghost-session, networking-ghost-character-state, party-system, inventory-system, loot-table-system, networking-message-criticality, networking-channel-contract, networking-owl-compensation, networking-relevance-filter, status-effects, Authentication, Equipment System, Enhancement System, Enemy AI, Movement System, Death & Respawn, Zone Instancing, NPC Shop, Consumable Use System, Mob Spawning, Navigation/Pathfinding, Client-Side Prediction, Party Chat, **HUD**

## Session Extract — /architecture-review + promotions 2026-06-27
- Verdict: **PASS** (CONCERNS → PASS in same session)
- Requirements: domain-level — all 8 Foundation/Core domains covered, 0 coverage gaps, 0 cross-ADR conflicts
- ADRs: 8 / 8 Accepted (ADR-006, 007, 008 promoted to Accepted 2026-06-27)
- Cleanup applied: ADR-002 link.xml → UnityEngine.AIModule; ADR-008 sortingOrder=1 reserved; ADR-005 Combat-UI ADR# refs corrected; ADR-001 Engine Compat + ADR Deps backfilled
- GDD revision flags: None
- Remaining open items: ADR-001 OQ-ADR1-2 (SellRequest atomicity); ADR-004 OQ-ADR4-3 (CustomMessagingManager vs UTP wrapper); 6 MVP GDDs not yet authored
- Report: docs/architecture/architecture-review-2026-06-27.md

## Session Extract — /gate-check pre-production + /create-architecture 2026-06-27
- **Gate (Technical Setup → Pre-Production): FAIL** — report at production/gate-checks/technical-setup-to-pre-production-2026-06-27.md
- Gate blockers: (1) no test framework, (2) no master architecture doc, (3) no accessibility-requirements.md, (4) no interaction-patterns.md
- **Blocker #2 RESOLVED**: master architecture doc written → docs/architecture/architecture.md (TD sign-off: APPROVED WITH CONDITIONS 2026-06-27; LP feasibility skipped — lean mode)
- Traceability index renamed traceability-index.md → architecture-traceability.md (gate-expected filename)
- Required New ADRs surfaced (Foundation-first): (1) Scene/Zone-Load Management [HIGH], (2) Event/Messaging Architecture [LOW]; then (3) URP Render/VFX [HIGH], (4) Audio [LOW] — both blocked on unauthored GDDs
- **Remaining gate blockers**: /test-setup (tests/ + CI + example test); design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: clear remaining 3 gate blockers → write 2 Foundation Required ADRs → re-run /gate-check pre-production

## Session Extract — /test-setup 2026-06-27
- **Gate blocker #1 RESOLVED**: test framework scaffolded
- Files created: tests/README.md, tests/EditMode/README.md, tests/EditMode/SmokeTest.cs (example test), tests/PlayMode/README.md, tests/smoke/critical-paths.md, tests/evidence/.gitkeep, .github/workflows/tests.yml
- Framework: Unity Test Framework (NUnit, built-in) — EditMode (unit) + PlayMode (integration)
- CI: game-ci/unity-test-runner@v4, Unity 6000.3.10f1, runs on push to main + PRs
- One-time manual step required: add UNITY_LICENSE to GitHub repository secrets before first CI run
- **Remaining gate blockers**: design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: accessibility doc (pick tier) → /ux-design patterns → re-run /gate-check pre-production

## Session Extract — accessibility-requirements.md 2026-06-27
- **Gate blocker #3 RESOLVED**: design/accessibility-requirements.md written, tier committed
- Tier: **Standard** (user decision)
- Rationale: visual (item rarity, HP bars) + motor (touch targets, Rhythm Mastery timing) are primary barriers; Standard covers both; Comprehensive deferred (VoiceOver, mono audio, subtitle customization)
- Key Standard commitments: 44×44pt touch targets (Apple HIG), colorblind modes (Protanopia/Deuteranopia/Tritanopia), text size adjustment (chat + menus), timing window multiplier for skill activation (1×/1.5×/2×), motion reduction toggle, safe area compliance
- Open questions: Unity 6.3 UI Toolkit accessibility node support for VoiceOver; timing window client-side feasibility; minimum iOS version
- **Remaining gate blocker**: /ux-design patterns (design/ux/interaction-patterns.md)
- Next: /ux-design patterns → re-run /gate-check pre-production

## Session Extract — /ux-design patterns 2026-06-27
- **Gate blocker #4 RESOLVED**: design/ux/interaction-patterns.md written
- 28 patterns catalogued and formalized; 8 gaps identified for future screens
- Pattern categories: Input Controls, Gesture, Combat UI, Feedback, Data Display, Layout/Navigation, Chat
- Key patterns: Resource Bar, Touch Toggle, Expand/Collapse, Skill Slot, Cooldown Arc, Long-Press Context Menu, Toast Notification, Shake Feedback, Status Effect Icon, Party Frame, Target Frame, Loot Countdown Notification, Context-Adaptive Overlay, Safe Area Container, Tabbed Panel, Scrollable Item List, Item Row, Quantity Selector, Confirm Button with Spinner, Locked Item State, Destructive Confirmation Overlay, Risk Warning Badge, Outcome Animation, Persistent Chat Panel, Compose Button, Input Field with Validation, Character Counter, Keyboard-Slide Layout Shift
- **All 4 gate blockers now resolved** — ready to re-run /gate-check pre-production
- Next: /gate-check pre-production

## Session Extract — /story-done 2026-06-29
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Tech debt logged: None (2 advisory items in Completion Notes)
- Next recommended: Story 004 — Resource Pools (`production/epics/character-stats/story-004-resource-pools.md`)

## Session Extract — /story-done 2026-06-29 (Story 004)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-004-resource-pools.md` — CurrentHP / CurrentMP Lifecycle
- Tech debt logged: None (pre-existing TR-ID advisory)
- Next recommended: Story 005 — Events (`production/epics/character-stats/story-005-events.md`)


## Session Extract — /dev-story 2026-07-02 (Story 007)

- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (transaction fields + BeginStatTransaction/EndStatTransaction/RollbackStatTransaction/AddToDeferredDedup + SetBaseStat conditional), `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (new, 7 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (7 tests)
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-007-transaction-api.md

## Session Extract — /story-done 2026-07-02 (Story 006)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Tech debt logged: None (advisory: TR-stats-006 unregistered; AC-13/AC-15 deferred OQ-1 pending)
- Next recommended: Story 007 — Transaction API (`production/epics/character-stats/story-007-transaction-api.md`)

## Session Extract — /dev-story 2026-07-02

- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Files changed: `src/Foundation/CharacterStats/ILevelingService.cs` (new), `src/Foundation/CharacterStats/CharacterStats.cs` (constructor + AddExperience + OQ-1 TODO), `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` (NullLevelingService, CreateWithLeveling), `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (new, 6 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (6 tests — AC-14 × 3, NEW AC × 3)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-006-write-ownership.md`

## Session Extract — /story-done 2026-07-02

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Tech debt logged: None (4 advisory items in Completion Notes)
- Code review fixes applied: count snapshot in FireOnStatChanged/FireOnEntityDied; IsFiringAndAssert guards on Subscribe/Unsubscribe
- Next recommended: Story 006 — Write Ownership (`production/epics/character-stats/story-006-write-ownership.md`)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — replaced OnEntityDied stub with full event infrastructure (StatChangedHandler/EntityDiedHandler delegates, 16-slot fixed arrays, Subscribe/Unsubscribe, _isFiring guard, FireOnStatChanged/FireOnEntityDied); added IsFiringAndAssert guard + OnStatChanged firing to SetBaseStat, SetBaseStatFloat, AddBuffModifier, RemoveBuffModifier, AddEquipmentModifier, RemoveEquipmentModifier; added guard to ApplyDamage/ApplyRegen/ConsumeMana/ApplyManaRegen; replaced OnEntityDied?.Invoke with FireOnEntityDied in ApplyDamage and RemoveEquipmentModifier MaxHP path
  - `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` — replaced 5x `OnEntityDied += ...` with `Subscribe(_ => diedCount++)` (event syntax incompatible with fixed-array pattern)
  - `tests/EditMode/CharacterStats/TestHelpers/StatEventRecorder.cs` — added Subscribe/Unsubscribe wiring methods
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` (8 test methods — AC-29, AC-29 edge, AC-29b, AC-29b edge, Unsubscribe, Unsubscribe re-subscribe, OnEntityDied re-entrance, OnEntityDied read-permitted)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-005-events.md`

## Session Extract — /story-done 2026-07-02
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Tech debt logged: None (3 advisory items in Completion Notes)
- Next recommended: Story 008 is Blocked (depends on Leveling System epic)

## Session Extract — /code-review + /story-done 2026-07-04
- Story: `production/epics/item-database/story-001-core-types-and-runtime-database.md` — IItemDatabase Interface, Runtime Database, and Core Type Definitions
- Implementation was already committed at session start (`bd30009`, prior session) but had not been code-reviewed or closed
- `/code-review` verdict: APPROVED WITH SUGGESTIONS (Required Change: AC-2 test spec had an untested cross-ID no-aliasing edge case)
- Fixed: added `ItemDatabase_GetItem_DifferentIds_ReturnsDistinctReferences` to `tests/EditMode/ItemDatabase/ItemDatabase_Core_tests.cs`
- `/story-done` verdict: COMPLETE WITH NOTES — 11/11 ACs passing
- Tech debt logged: TD-001 (GetItemsByCategory encapsulation leak), TD-002 (missing .asmdef project-wide), TD-003 (untested duplicate-ID/null-entry Initialize() behavior) — new `docs/tech-debt-register.md` created
- Files updated: `production/epics/item-database/story-001-core-types-and-runtime-database.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 001 → Complete), `docs/tech-debt-register.md` (new file)
- Next recommended: Story 002 — Import Validator — Reject Rules (`production/epics/item-database/story-002-validator-error-rules.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 002)

- Story: `production/epics/item-database/story-002-validator-error-rules.md` — Import Validator — Reject Rules (Error Path)
- `/story-readiness` verdict: READY (17/17 checks passing)
- Files changed: `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs` (new, 378 lines — `ValidationSeverity`, `ValidationIssue`, `ValidationResult`, `ItemDefinitionValidator.ValidateRecord`/`ValidateBatch`), `src/Foundation/ItemDatabase/StatModifierEntry.cs` + `EquipmentData.cs` + `ConsumableData.cs` (each: added `#if UNITY_EDITOR internal CreateForTesting` seam)
- Test written: `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Error_tests.cs` (17 test methods, all 15 blocking ACs covered: AC-3,4,5,7,8,10,11,12,14,15,21,22,25,26,41)
- Correction mid-session: implementing agent initially added 4 out-of-scope Story 003 warning checks (negative FlatBonus, duplicate StatID, CooldownSeconds==0, equipment SellPriceGold==0) despite explicit instruction not to, and silently redesigned `ValidationResult` from the story's suggested `IsFatal`/`Errors`/`Warnings: IReadOnlyList<string>` shape to `Issues: IReadOnlyList<ValidationIssue>` + `ValidationSeverity` enum. User decision: stripped the warning logic (kept scope clean for Story 003), kept the new API shape (no downstream code depends on either shape yet).
- IL2CPP note: `StatID` membership check uses non-generic `(StatID[])Enum.GetValues(typeof(StatID))`, not the generic overload, per project engine-safety convention (no `.csproj` yet to confirm IL2CPP BCL surface).
- Blockers: None

## Session Extract — /code-review + /story-done 2026-07-04 (Story 002)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/item-database/story-002-validator-error-rules.md` — Import Validator — Reject Rules (Error Path)
- `/code-review` ran twice: CHANGES REQUIRED (missing 6 accept/boundary tests for AC-8, 14, 15, 21, 22, 25) → fixed → APPROVED WITH SUGGESTIONS
- Final test count: 25 test methods, all 15 blocking ACs covered with both reject and accept/boundary cases
- Tech debt logged: TD-004 (StatID/GearSlot validation rationale wording), TD-005 (undefined-ItemCategory and Equipment+ConsumableData cross-contamination paths untested)
- Files updated: `production/epics/item-database/story-002-validator-error-rules.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 002 → Complete), `docs/tech-debt-register.md` (TD-004, TD-005 added)
- Next recommended: Story 003 — Import Validator — Warning Rules (`production/epics/item-database/story-003-validator-warning-rules.md`)

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 003)

- Story: `production/epics/item-database/story-003-validator-warning-rules.md` — Import Validator — Warning Rules (Accept Path)
- `/story-readiness` verdict: NEEDS WORK → fixed (story's Implementation Notes + QA Test Cases referenced a stale `ValidationResult` API — `Warnings`/`Errors` list properties — that no longer exists after Story 002's approved redesign to `Issues`/`ValidationSeverity`; corrected in-file, then READY)
- Files changed: `src/Foundation/ItemDatabase/ItemDefinitionValidator.cs` (+48 lines — `GetTierBasePrice`, AC-35/AC-32/AC-17 warning checks, additive only), `tests/EditMode/ItemDatabase/ItemDatabase_Validator_Warning_tests.cs` (new, 9 tests)
- Test written: 9 tests covering AC-13 (2, no-op confirmation), AC-17 (2), AC-32 (3, incl. 9g/10g boundary), AC-35 (2)
- Process note: the implementing subagent correctly refused to treat a coordinator-relayed "user approved" as valid consent (per its own no-agent-can-authorize-writes constraint), creating a dead end since subagents have no direct channel to the user in this architecture. Resolved by applying the subagent's already-presented, user-approved diff directly via Edit/Write in the main session.
- Blockers: None
- Next: `/code-review src/Foundation/ItemDatabase/ tests/EditMode/ItemDatabase/` then `/story-done production/epics/item-database/story-003-validator-warning-rules.md`

## Session Extract — /code-review + /story-done 2026-07-04 (Story 003)

- Verdict: COMPLETE (no deviations)
- Story: `production/epics/item-database/story-003-validator-warning-rules.md` — Import Validator — Warning Rules (Accept Path)
- `/code-review` ran twice: CHANGES REQUIRED (missing SellPriceGold=11 boundary test, missing AC-32/AC-35 co-firing assertion) → fixed → APPROVED
- Also hardened `GetTierBasePrice`'s unreachable `default` branch to throw `ArgumentOutOfRangeException` instead of returning `0f` (Unity specialist suggestion — avoids a latent Infinity/NaN if a future GearTier value is added without updating the switch)
- Final test count: 11 test methods, all 4 ACs (13, 17, 32, 35) covered including boundary/co-firing/multi-tier cases
- Tech debt logged: None (verdict was clean, no advisory deviations)
- Files updated: `production/epics/item-database/story-003-validator-warning-rules.md` (Status: Complete, ACs checked, Completion Notes), `production/epics/item-database/EPIC.md` (Story 003 → Complete)
- Next recommended: Story 004 — MVP Item Records (`production/epics/item-database/story-004-mvp-item-records.md`) — Type: Config/Data, no programmer agent needed

## Session Extract — Unity project bootstrapping 2026-07-04

- User created the actual Unity project via Unity Hub: `C:\Users\Manuel Toscano\Claude-Code-Game-Studios\IronGrind\` (subfolder, as Unity Hub's wizard forces — not yet merged into repo root)
- **Version correction**: installed Editor is `6000.3.10f1`, confirmed LTS-labeled in Unity Hub. Project docs had incorrectly recorded Unity 6.3 LTS's internal version as `6000.4` (should be `6000.3` — Unity's internal numbering maps directly: 6.0→6000.0, 6.1→6000.1, 6.2→6000.2, 6.3→6000.3). Independently confirmed via WebFetch: `docs.unity3d.com/6000.3/.../UpgradeGuideUnity63.html` resolves and is titled "Upgrade to Unity 6.3"; the `/6000.4/` path was never real.
- Corrected `6000.4` → `6000.3` (and `.github/workflows/tests.yml`'s `unityVersion` → the exact confirmed `6000.3.10f1`) across 14 living documents: `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `.github/workflows/tests.yml`, `docs/architecture/control-manifest.md`, `architecture.md`, `architecture-traceability.md`, `docs/registry/architecture.yaml`, `tests/README.md`, `production/epics/index.md`, ADR-001, 002, 003, 004, 005, 006, 007, 009, 010.
- Deliberately left 3 point-in-time historical snapshots uncorrected (not revising history): `production/gate-checks/technical-setup-to-pre-production-2026-06-27.md`, `docs/architecture/architecture-review-2026-06-21.md`, `docs/architecture/architecture-review-2026-06-27.md`.
- **Merge completed**: `assets/` renamed to `Assets/` (case-corrected, plain `mv` since it was never git-tracked); Unity's generated `Assets/` content (InputSystem_Actions, Readme, Scenes/, Settings/, TutorialInfo/) merged alongside the existing `Assets/data/items/`; `ProjectSettings/` and `Packages/` moved to repo root; disposable `IronGrind/` subfolder (Library/Temp/Logs/UserSettings/.vscode/.csproj/.slnx — all Unity/IDE cache) deleted by the user after a Bash permission block on `rm -rf`.
- `.gitignore` already covered `Library/`/`Temp/`/`Logs/`/`UserSettings/`/`*.csproj`/`*.sln` — only added `*.slnx` (Unity 6's newer solution format) which was missing.
- Updated `.claude/docs/directory-structure.md` to document `Assets/`, `ProjectSettings/`, `Packages/` at repo root.
- Fixed lowercase `assets/` path references in the two places that are functionally live right now: `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` and `story-004-mvp-item-records.md`. Left ~40 other files (GDDs, entities.yaml) with stale lowercase paths for not-yet-authored assets — logged as **TD-007**, fix opportunistically per-system rather than in bulk.
- TD-006 updated: Unity project + folder casing now resolved; still open — `src/` needs wrapping as a local Unity package + `.asmdef` (TD-002) before it will actually compile in the new project, and the seeder hasn't been run yet.
- **`src/` wrapped as a local Unity package**: `src/package.json` (name `com.irongrind.src`), referenced from `Packages/manifest.json` via `file:../src`. Runtime asmdef `src/Foundation/IronGrind.Foundation.asmdef` (unrestricted platforms, matches existing file-level `#if UNITY_EDITOR` guards). Test asmdef `tests/EditMode/IronGrind.Foundation.EditModeTests.asmdef` (Editor-only, references the Foundation asmdef + TestRunner assemblies). `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")` added via `src/Foundation/AssemblyInfo.cs` so test seams stay reachable across the new assembly boundary. TD-002 marked resolved-pending-Editor-verification.
- **Two follow-up fixes after "no tests to show" in Test Runner**:
  1. Added missing `"optionalUnityReferences": ["TestAssemblies"]` to the EditMode test asmdef — this is the actual field Unity's "Tests" checkbox controls; having `UnityEngine.TestRunner`/`UnityEditor.TestRunner` as references alone isn't sufficient.
  2. **Root cause**: `tests/` was never registered with Unity at all — only `src/` was wired into `Packages/manifest.json`. `tests/EditMode/` sat at the repo root as a plain sibling folder, invisible to Unity's compiler (which only scans `Assets/` and registered `Packages/`). Fixed by making `tests/` its own local package too: `tests/package.json` (`com.irongrind.tests`), added to `Packages/manifest.json` via `file:../tests`.
- Next: user reloads Unity again, confirms Test Runner now discovers all `ItemDatabase_*_Tests` and `CharacterStats_*_Tests`. New `.meta` files Unity generates for `src/`/`tests/` package content should be committed (essential metadata, not cache). Then run the `ItemDatabaseSeeder` menu item and confirm Story 004's smoke check.

## Session Extract — /story-readiness + /dev-story 2026-07-04 (Story 004)

- Story: `production/epics/item-database/story-004-mvp-item-records.md` — MVP Item Records — 34 Authored ScriptableObject Assets
- `/story-readiness` verdict: READY (OQ-1/OQ-5 "unresolved" markers present but already owned in the story's Out of Scope section with concrete placeholders — not blocking)
- Structural decision: no Unity Editor available in this environment to author real `.asset` files. Chose an Editor seeder script (`ItemDatabaseSeeder.cs`, `[MenuItem]`) over hand-written YAML, per user's explicit choice — user must run it once in Unity.
- Files changed: `src/Foundation/ItemDatabase/ItemDefinition.cs` (extended `SetForTesting` with `description`/`iconAddress`, backward-compatible), `src/Foundation/ItemDatabase/MvpItemRecordData.cs` (new — shared 34-record data table), `src/Foundation/ItemDatabase/ItemDatabaseSeeder.cs` (new — Editor tool, manual run required), `assets/data/items/ITEM_ID_REGISTRY.txt` (new), `tests/EditMode/ItemDatabase/ItemDatabase_MvpRecords_tests.cs` (new, 8 tests)
- **Major discovery**: no Unity Editor project exists anywhere on disk for this repo (no `.meta`/`ProjectSettings`/`Packages`; `assets/` was empty before this story). Every "test evidence" claim across Stories 001-004 has only been code-reviewed, never actually run in Unity. Logged as **TD-006** (High impact, Large effort) — recommend addressing before any story is treated as fully verified.
- AC coverage: 6/6 blocking ACs (18, 24, 30, 31, 33, 34) covered via in-memory test against the shared data table; real `.asset` files + in-Editor smoke check remain a deferred manual step for the user.
- Blockers: Real Unity project needed to complete the manual seeder run + smoke check evidence file.
- Next: `/code-review src/Foundation/ItemDatabase/ tests/EditMode/ItemDatabase/`, then likely a project-level conversation about bootstrapping an actual Unity project before `/story-done` can reach a clean COMPLETE verdict.

## Session Extract — /story-done 2026-09-25 (Story 010 — XP Threshold Formula & Table — COMPLETE)

- Story: `production/epics/leveling-system/story-010-xp-threshold-formula-table.md` — the last story in the Leveling System epic. **Epic now 13/13 Complete.**
- Files changed: `src/Foundation/LevelingSystem/XpThresholdTable.cs` (new — 62-entry static `IReadOnlyList<int>` cumulative XP lookup table, index 0 unused/1-60 real/61 sentinel), `tests/EditMode/LevelingSystem/LevelingSystem_XpThresholdFormulaTable_tests.cs` (new — 4 tests: AC-LS-31 spot-checks, array-length guard, full independent double-precision recomputation; AC-LS-32 monotonicity).
- Implementation used a table pre-verified by the orchestrator (Node.js, two independent methods) before handoff to `gameplay-programmer`, who ran a third independent recomputation and a byte-level transcription check before shipping — the table has now been verified 4 separate times (2 pre-implementation, 1 during implementation, 1 during `unity-specialist` code review) with zero mismatches.
- Code review (lean): `unity-specialist` → APPROVED WITH SUGGESTIONS (non-blocking: `Values`'s `IReadOnlyList<int>` wrapper doesn't prevent downcast-mutation of the backing array; test's `MidpointRounding.AwayFromZero` doesn't exactly match `Mathf.RoundToInt`'s round-half-to-even, though no real value is affected). `qa-tester` → initially BLOCKING on a missing `Values.Count==62` array-length assertion; closed by adding `XpThreshold_ArrayLength_IsExactly62Entries`.
- **First real Unity Test Runner execution of this story's tests** (Editor has been available since 2026-09-24): user confirmed all 4 tests pass.
- **Economy-designer sign-off** (AC-LS-31's explicit gate) — spawned `economy-designer` to render pacing judgment (not arithmetic re-verification, already done 4x). Verdict: **SIGN-OFF GRANTED WITH NOTES** on the array/curve itself (smooth monotonic decay toward R=1.1198 across all 59 transitions, no cliffs; L1→L2 hook pace and L60 total order-of-magnitude both sound for an indie MVP MMORPG). But found AC-LS-31's bundled "205h cap-time anchor validated against realistic mob XP rates, per-tier-hours estimate" sub-clause is **currently unsatisfiable**, not just undone — the GDD's own "Est. Kill Time" column diverges from its own stated flat-200-XP/min methodology by a factor that *grows with level* (5.2x at L10 up to 6.8x at L60/L59), and `MobDefinition.KillXP` has no populated values anywhere in the repo (no zone-XP GDD exists either). User approved splitting this sub-clause out of Story 010's gate (matches the story's own existing Out-of-Scope carve-out for mob-XP tuning) rather than blocking the story on data that can't exist yet.
- Propagation fixes applied to `design/gdd/leveling-system.md`: AC-LS-31's worked example corrected (`≈62,728`→`65,824`, matching the 2026-09-24 F-LS-1/EC-LS-35 correction that had not yet propagated here), the F-LS-1 cumulative summary table's stale L20 row corrected (`12,172`/`~74,900` → `11,961`/`~77,800`, with a correction footnote noting the other 6 rows have NOT been independently re-verified and may carry the same imprecision — that's TD-038's job), and AC-LS-31's text split to carve out the now-descoped 205h/per-tier sub-clause with a pointer to TD-039.
- **TD-039 logged**: the Est. Kill Time column's methodology-divergence finding (distinct from and more severe than TD-038's already-fixed 13% total correction) — Medium impact, Backlog, blocked on real mob-XP data existing.
- Story 010's own two stale leftovers (flagged during `/story-readiness`, fixed before code review): Estimate line and Test Evidence status line, both updated to reflect actual completion.
- `production/epics/leveling-system/EPIC.md` updated: 13/13 Complete. Two residual open items noted as tech debt, not epic blockers: OQ-LS-3 (Story 007's AC-LS-18b sub-case) and TD-039. Character Stats Story 008 (Integration) flagged as the epic's remaining Definition-of-Done item to confirm/close.
- Tech debt logged: TD-039 (new). Verdict: **COMPLETE**.
- **Standing reminder carried forward**: plaintext GitHub PAT in `origin`'s remote URL (`.git/config`) still not rotated — keep flagging every session until the user confirms rotation. Nothing in this session's work has been committed to git yet — Story 013's full implementation, the 4-GDD OQ-LS-7 conflict resolution, and now Story 010's implementation + GDD propagation fixes are all uncommitted.
- Next recommended: with the Leveling System epic now fully closed, confirm/close Character Stats Story 008 (Integration — Leveling ↔ Character Stats), then commit and push this session's accumulated work (subject to explicit user instruction, per this project's "no commits without user instruction" collaboration protocol).

## Session Extract — /story-readiness 2026-09-25 (Character Stats Story 008 — NEEDS WORK → fixed → READY)

- Story: `production/epics/character-stats/story-008-integration-leveling.md` — Integration: Leveling ↔ Character Stats. Readiness verdict NEEDS WORK (6 gaps); user said "fix all", all applied.
- **Real design error found and fixed (GDD + story):** AC-27a/b/c attributed Warrior Tank's full +2 VIT/level to auto-alloc. Actual Warrior template = +1 VIT auto; "Tank" = +1 free point spent on VIT via `AllocateFreePoint` (which recomputes F-3–F-9 at current tier, `LevelingService.cs:494`). Rewrote each sub-case with two observation points: post-level-up (1368 / 2910 / 5480) and post-free-point (1392 / 2940 / 5520 — unchanged headline values). AC-27b's ordering-violation value was also wrong: 2790 → correct 2880 (`(200+86×20)×1.5`). Corrected in `design/gdd/character-stats.md` AC-27 (with a dated build note) and in the story.
- Other story fixes: Status Blocked → Ready; dependencies marked satisfied; API names corrected (`InitializeEntity` → `InitializeAtL1`; constructor-injected `ICharacterStats` → `AttachCharacterStats`); test path moved to `tests/EditMode/Integration/CharacterStats/...` (top-level `tests/Integration/` has no asmdef); Estimate 2–3h added; overlap note vs AC-LS-27/38/53/54/43.
- `production/epics/character-stats/EPIC.md`: Story 008 → Ready.
- Registry: `design/registry/entities.yaml` has no AC-27 values — no change needed. Propagation check: clean (persistence AC-30's 2,940@VIT=88 remains consistent).
- Uncommitted: GDD, story, EPIC.md, this file. Standing reminder: plaintext PAT in `origin` remote URL still unrotated; untracked `bash.exe.stackdump` in repo root.
- Next: `/dev-story production/epics/character-stats/story-008-integration-leveling.md` (fresh session recommended — one job per session).

## Session Extract — /dev-story 2026-09-25 (Character Stats Story 008)
- Story: `production/epics/character-stats/story-008-integration-leveling.md` — Integration: Leveling ↔ Character Stats
- Files changed: `tests/EditMode/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs` (new, 360 lines, 5 [Test] methods — AC-31, AC-27a, AC-27b, AC-27c, AC-34). No `src/` changes — all behavior already existed.
- Implemented by `engine-programmer`; no story/code discrepancies found; FloorToInt float-precision checked (e.g. 1140*1.2f ≈ 1368.00005 → 1368).
- Test design notes: AC-31 asserts MaxHP==0 pre-spawn (CharacterStats' unset sentinel); AC-27b seeds L39 MaxHP=2304 to show the tier spike; AC-34 seeds 3 held free points via `RestoreLevelingState` (Level pinned at 60, no level-up available) and walks INT 407→408 (9992)→409 (9999, raw 10016)→[set 499]→500 (9999, raw 12200), then direct SetBaseStat(MaxMP,10016) reads 10016.
- Not yet executed — user must run the EditMode Test Runner (new folder will get .meta files from Unity; commit them).
- Blockers: None
- Next: user runs tests → `/code-review tests/EditMode/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs` → `/story-done production/epics/character-stats/story-008-integration-leveling.md`

## Session Extract — /code-review 2026-09-25 (Character Stats Story 008)
- User ran the new integration tests in the real Test Runner: 5/5 pass (pre-review).
- `/code-review`: unity-specialist CLEAN; qa-tester TESTABLE, no blocking gaps. Verdict: APPROVED WITH SUGGESTIONS. User said "fix all":
  1. Applied — held-free-point bookkeeping asserts: `GetHeldFreePoints` 1→0 around each AC-27a/b/c VIT spend; AC-34 asserts 3 seeded, 0 after 3 spends, and a 4th spend returns `RejectedNoFreePoints` with INT/MaxMP unchanged.
  2. Logged as TD-040 instead of fixed — the duplicated `CreateClassRegistry`/class-type constants span 10 test files (not 2 as the reviewer assumed); consolidating would edit 9 out-of-scope Leveling test files.
  3. Applied — removed the non-asserted STR/DEX seeds from the AC-27 tests and updated the comment.
- File now 370 lines, still 5 [Test] methods. **Needs a re-run in the Test Runner** before `/story-done`.
- Next: user re-runs EditMode tests → `/story-done production/epics/character-stats/story-008-integration-leveling.md`

## Session Extract — /story-done 2026-09-25 (Character Stats Story 008)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-008-integration-leveling.md` — Integration: Leveling ↔ Character Stats. 5/5 ACs; user re-ran full EditMode suite after review fixes: **821/821 pass**.
- **Character Stats epic now 8/8 Complete.** All 4 Foundation epics (Character Stats, Item Database, Currency, Networking Core) and the Leveling System (Core) epic are Complete.
- Files updated: story (Status Complete, ACs checked, Completion Notes), `production/epics/character-stats/EPIC.md` (Complete; stale DoD test path fixed), `production/epics/leveling-system/EPIC.md` (Story 008 dependency lines closed), `production/epics/index.md` (Character Stats + Leveling rows → Complete; Leveling row said "Not yet created").
- Tech debt logged: TD-040 (duplicated class-registry test setup across 10 files) — during code review.
- No sprint plan / sprint-status.yaml exists — nothing to update there.
- Uncommitted: all of this session's work (GDD AC-27 fix, story 008, test file + Unity .meta files, EPIC/index updates, TD register, active.md). PAT-in-remote reminder still open; `bash.exe.stackdump` still untracked.
- Next recommended: remaining Core epics with no stories yet — Inventory System, Loot Table System, Status Effects (all "Ready"); Damage Calculation blocked on server/client assembly ADR; Authentication has a partial ADR gap. Run `/create-stories [epic-slug]` for the chosen one.

## Session Extract — /create-stories 2026-09-25 (Inventory System epic)
- Verdict: COMPLETE — 9 stories written to `production/epics/inventory-system/` (all Logic, all Ready): 001 slot container/core types/read API, 002 atomic pickup, 003 bag-full notification + 30s dedup, 004 slot locks + RemoveItem, 005 discard, 006 move/merge/swap, 007 Equipment interface, 008 sell + consume, 009 snapshot save/load.
- Review mode lean → QL-STORY-READY skipped; QA test cases authored directly from GDD ACs.
- Coverage: 17/18 GDD blocking ACs. AC-INV-11 (UI — tap consumable opens detail view) deferred to a future Inventory UI epic (GDD + `design/ux/inventory-screen.md` unauthored). AC-INV-10 covered inventory-side only.
- Governing ADRs: ADR-010 cited for `OnInventoryChanged`/`OnInventoryFull` + Tier 1 calls; ADR-006 for Story 009 storage shape. Epic's own "no ADR" stands for slot logic.
- Open questions parked for `/story-readiness`: Story 001 zero-alloc event payload shape vs ADR-010 (unity-specialist); Story 006 consumable-onto-different-item (default swap → GDD Rule 7.20 line); smaller confirmations in 004 (RemoveItem on unlocked), 007 (ForceInsert dedup), 008 (consume locked stacks), 009 (no events on load).
- Code location: `src/Foundation/InventorySystem/`; tests `tests/EditMode/InventorySystem/InventorySystem_[feature]_tests.cs`.
- Updated: `production/epics/inventory-system/EPIC.md` (story table, coverage/deferral notes), `production/epics/index.md`.
- Next: `/story-readiness production/epics/inventory-system/story-001-slot-container-core-types.md`

## Session Extract — /story-readiness 2026-09-25 (Inventory Story 001 — NEEDS WORK → fixed → READY)
- 2 gaps: unresolved event-payload question + missing performance note. User said "fix all".
- Payload DECIDED via unity-specialist consult: `SlotChange` readonly struct + `InventoryChangedEventArgs` readonly struct wrapping a reused service-owned 20-entry buffer + Count, struct enumerator (alloc-free foreach), `_isDispatching` guard throws `InvalidOperationException` on re-entrant mutation, `finally` resets flag+count. Valid-only-during-dispatch contract. Rejected: 20 inline entries, per-slot events, ReadOnlySpan member.
- Story 001 updated: implementation note (sketch + contract), performance note, AC wording, new re-entrancy QA test case. EPIC.md open question (1) marked resolved.
- Next: `/dev-story production/epics/inventory-system/story-001-slot-container-core-types.md`

## Session Extract — /dev-story 2026-09-25 (Inventory System Story 001 — Slot Container, Core Types & Read API)
- Story: production/epics/inventory-system/story-001-slot-container-core-types.md — Slot Container, Core Types & Read API
- Files changed: src/Foundation/InventorySystem/{IInventoryService,InventorySystem,InventorySlot,SlotChange,InventoryChangedEventArgs}.cs (new), tests/EditMode/InventorySystem/InventorySystem_SlotContainer_tests.cs (new, 32 tests)
- Test written: tests/EditMode/InventorySystem/InventorySystem_SlotContainer_tests.cs — NOT yet run in Test Runner (static review only)
- Orchestrator decisions: RegisterCharacter(CharacterID) bootstrap seam (mirrors Currency); GetSlot returns InventorySlot (Empty + LogError on bad index/unregistered); internal seams SeedSlotForTesting / RecordSlotChange / EmitInventoryChanged; private ThrowIfDispatching guard that all future mutation entry points (Stories 002, 004-008) must call first.
- gameplay-programmer hit its turn limit after writing all files; orchestrator reviewed and fixed tests directly: recorder now copies values out during dispatch (previously retained args past dispatch, violating the type's own validity contract); local-function seeding subscriber replaced with a named method.
- RESOLVED (user decision 2026-09-25): concrete class renamed `InventorySystem` → `InventoryService` (file InventoryService.cs, log tag [InventoryService]) to avoid the class/namespace collision (TD-006 class). Namespace stays `IronGrind.InventorySystem`, matching the Leveling precedent (LevelingSystem ns / LevelingService class). Story 001 + Story 009 docs updated to the new name.
- Blockers: None
- Next: run EditMode Test Runner, then /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem then /story-done production/epics/inventory-system/story-001-slot-container-core-types.md

## Session Extract — /code-review + "fix all" 2026-09-25 (Inventory Story 001)
- Rename test-regex bug (space dropped after [InventoryService] tag, 9 failures) fixed; user then ran /code-review → CHANGES REQUIRED (unity-specialist + qa-tester).
- Applied all required changes + suggestions:
  - New `InventoryConstants.cs` — INVENTORY_SLOT_COUNT moved off the concrete class (interface-only consumers can read it).
  - `RecordSlotChange(CharacterID, slot, item, qty)` — pending buffer now bound to one character (_pendingCharacterId); validates slot range / negative qty / phantom via shared `ValidateSlotContents` (also used by SeedSlotForTesting).
  - New internal `DiscardPendingChanges()` — mutation stories 002/004-008 MUST call it when aborting after recording. Contract documented on class remarks.
  - `EmitInventoryChanged`: no-op when nothing pending; character mismatch discards pending then throws.
  - `Enumerator.Current` bounds-checked (InvalidOperationException).
  - OnInventoryChanged docs: throwing subscriber skips later subscribers.
  - Tests: removed dead TearDown; +13 test cases (45 total) — validation, cross-character binding, discard, zero-change emit, throwing subscriber reset, two subscribers, enumerator guard.
- NOT yet run in Test Runner.
- Next: user re-runs EditMode tests → /story-done production/epics/inventory-system/story-001-slot-container-core-types.md

## Session Extract — /story-done 2026-09-25
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/inventory-system/story-001-slot-container-core-types.md — Slot Container, Core Types & Read API (45 tests passing, live Editor)
- EPIC.md: row 001 → Complete; epic header Ready → In Progress
- Tech debt logged: None (advisory deviations are design refinements, recorded in Completion Notes)
- Next recommended: production/epics/inventory-system/story-002-atomic-pickup.md — must follow the Story 001 mutation-seam contract (ThrowIfDispatching → validate → RecordSlotChange → Emit; DiscardPendingChanges on abort)
- Uncommitted: all Inventory Story 001 files + earlier uncommitted Leveling/Networking fixes; PAT-in-remote reminder still outstanding; bash.exe.stackdump still untracked in repo root

## Session Extract — /story-readiness 2026-09-25 (Inventory Story 002)
- Verdict: NEEDS WORK → gaps filled (user-approved) in production/epics/inventory-system/story-002-atomic-pickup.md
- Decisions: PickupResult readonly struct + PickupFailReason {None, InventoryFull, InvalidQuantity, UnknownItem, CharacterNotRegistered} (Story 003 notifies only on InventoryFull); InventoryService(IItemDatabase) ctor, parameterless removed, Story 001 test SetUp update in scope; StubItemDatabase test helper; qty > 99 accepted; lock guard added in 002 (tested in 004); binds to Story 001 seam contract.
- Next: /story-readiness re-check (expect READY) → /dev-story production/epics/inventory-system/story-002-atomic-pickup.md
- /story-readiness re-check (Story 002): one further gap (missing performance note) + estimate advisory → both applied (user-approved): perf bullet added (zero-alloc plan buffer, ≤40 array reads), estimate 3–4h → 4–5h. Story 002 now READY.
- Next: /dev-story production/epics/inventory-system/story-002-atomic-pickup.md

## Session Extract — /dev-story 2026-09-26 (Inventory System Story 002 — Atomic Pickup)
- Story: production/epics/inventory-system/story-002-atomic-pickup.md — Atomic Pickup Resolution & Stack Limits (Status → In Progress)
- Files changed: src/Foundation/InventorySystem/{PickupFailReason,PickupResult}.cs (new), IInventoryService.cs (+Pickup), InventoryService.cs (ctor(IItemDatabase), reused _pickupPlan int[20], Pickup plan-then-commit), tests/EditMode/InventorySystem/StubItemDatabase.cs (new), InventorySystem_SlotContainer_tests.cs (SetUp passes stub), InventorySystem_AtomicPickup_tests.cs (new, 23 cases; reuses ItemDefinitionBuilder)
- Implemented directly by orchestrator (no programmer subagent) — deviation from skill routing; noted.
- Additions beyond spec: StackLimit < 1 → LogError + UnknownItem (so bad data never shows bag-full UI); per-slot RecordSlotChange before write so a seam violation throws before any mutation.
- Lock guard (Rule 5.12) in Step 1 — untested until Story 004.
- NOT yet run in Test Runner.
- Next: user runs EditMode tests → /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done production/epics/inventory-system/story-002-atomic-pickup.md
- Compile fix: missing `using IronGrind.Currency;` (CharacterID) in the AtomicPickup test file → Safe Mode; fixed. User then ran EditMode Test Runner: ALL tests pass (23 new + Story 001 regression).
- Next: /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done production/epics/inventory-system/story-002-atomic-pickup.md

## Session Extract — /code-review + "fix all" 2026-09-26 (Inventory Story 002)
- /code-review: unity-specialist CLEAN, qa-tester TESTABLE (GAPS: suggestions only). Verdict APPROVED WITH SUGGESTIONS. User said "fix all"; applied:
  1. Pickup() split into TryGetStackLimit / PlanPartialStacks / PlanEmptySlots / CommitPickupPlan (was ~75 lines, CC>10).
  2. `#nullable enable` on InventoryService.cs; OnInventoryChanged declared `Action<...>?`.
  3. CommitPickupPlan remarks document the no-mid-loop-throw invariants.
  4. Story Implementation Notes: guard 3b (StackLimit < 1 → UnknownItem) recorded.
  5. PickupResult.Fail: UnityEngine.Debug.Assert(reason != None).
  6. +5 tests (28 total): two-character isolation (added _eventCharacterIds capture), StackLimit 0 → UnknownItem, two partial stacks + spill in one event, Step1+Step2 then remainder fail, seeded stack above limit skipped.
- NOT yet re-run in Test Runner.
- Next: user re-runs EditMode tests → /story-done production/epics/inventory-system/story-002-atomic-pickup.md
- User re-ran EditMode suite after review fixes: ALL tests pass (28 pickup cases + Story 001 regression). Next: /story-done production/epics/inventory-system/story-002-atomic-pickup.md

## Session Extract — /story-done 2026-09-26
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/inventory-system/story-002-atomic-pickup.md — Atomic Pickup Resolution & Stack Limits (10/10 ACs, 28 tests passing live)
- EPIC.md: row 002 → Complete
- Tech debt logged: None (advisory deviations recorded in Completion Notes)
- Next recommended: production/epics/inventory-system/story-003-*.md — Bag-full notification + 30s dedup (Ready, 2h, depends only on 002; notify only on PickupFailReason.InventoryFull)
- Uncommitted: Inventory Story 002 only (Story 001 and earlier work were committed in 66a6996 and before); PAT-in-remote reminder outstanding; bash.exe.stackdump untracked

## Session Extract — /story-readiness 2026-09-26 (Inventory Story 003 — NEEDS WORK → fixed → READY)
- 6 gaps found; user said "fix all, option A". Applied to production/epics/inventory-system/story-003-bag-full-notification.md:
  1. Time source DECIDED (option A): server ticks via injected `Func<uint>` current-tick provider; `InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS = 30 * ServerTickLoop.TICK_RATE_HZ` (600); expiry via `StaleDiscardComparer.IsTickExpired` (wraparound-safe, boundary-inclusive). QA cases rewritten in ticks (0/200/599/600) + wraparound case.
  2. Event contract: `InventoryFullEventArgs` readonly struct { CharacterID } + `event Action<InventoryFullEventArgs> OnInventoryFull` on IInventoryService.
  3. Ctor → `InventoryService(IItemDatabase, Func<uint>)`; Story 001 + 002 test SetUp updates in scope.
  4. Semantics: OnInventoryFull dispatch uses the same _isDispatching guard; dedup state updated before dispatch; RegisterCharacter clears dedup; InvalidQuantity/UnknownItem/CharacterNotRegistered never fire (new ACs + tests).
  5. Shared internal `NotifyInventoryFull(CharacterID)` helper — Story 007 ForceInsert MUST reuse it (Story 007 dedup question marked RESOLVED; EPIC.md open-questions line updated).
  6. Performance note added. Estimate 2h → 3h.
- Next: /dev-story production/epics/inventory-system/story-003-bag-full-notification.md (fresh session recommended)

## Session Extract — /dev-story 2026-09-26 (Inventory Story 003 — Bag-Full Notification)
- Story: production/epics/inventory-system/story-003-bag-full-notification.md (Status → In Progress)
- Files changed: src/Foundation/InventorySystem/InventoryFullEventArgs.cs (new), InventoryConstants.cs (+BAG_FULL_DEDUP_WINDOW_TICKS = 30 * ServerTickLoop.TICK_RATE_HZ), IInventoryService.cs (+OnInventoryFull), InventoryService.cs (ctor(IItemDatabase, Func<uint>), _bagFullWindowExpiry dict, internal NotifyInventoryFull, Pickup calls it on InventoryFull + clears window on success, RegisterCharacter clears window); tests: InventorySystem_BagFullNotification_tests.cs (new, 13 tests), SlotContainer + AtomicPickup SetUp/ctor test updated for new ctor.
- Implemented directly by orchestrator (same deviation as Story 002).
- NOT yet run in Test Runner.
- Next: user runs EditMode tests → /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done production/epics/inventory-system/story-003-bag-full-notification.md
- User ran EditMode suite: ALL tests pass (13 new bag-full tests + Story 001/002 suites with new ctor). Next: /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done

## Session Extract — /code-review + "fix all" 2026-09-26 (Inventory Story 003)
- /code-review: unity-specialist CLEAN, qa-tester TESTABLE (suggestions only). Verdict APPROVED WITH SUGGESTIONS. User said "fix all":
  1. +4 tests (17 total): multi-unit partial plan then no room fires; no-subscriber dispatch doesn't throw; different item inside window suppressed; successful pickup of a different item resets window.
  2. TD-041 logged: Inventory → Networking dependency (StaleDiscardComparer, TICK_RATE_HZ) — move to a neutral shared namespace.
  3. TD-042 logged: InventoryService never purges per-character dictionaries (no UnregisterCharacter) — resolve with Story 009 / persistence logout path.
- NOT yet re-run in Test Runner.
- Next: user re-runs EditMode tests → /story-done production/epics/inventory-system/story-003-bag-full-notification.md
- User re-ran EditMode suite after review fixes: ALL tests pass (17 bag-full tests + Story 001/002 suites). Next: /story-done production/epics/inventory-system/story-003-bag-full-notification.md

## Session Extract — /story-done 2026-09-26
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/inventory-system/story-003-bag-full-notification.md — Bag-Full Notification & 30-Second Dedup Window (7/7 ACs, 17 tests passing live)
- EPIC.md: row 003 → Complete (epic 3/9)
- Tech debt logged: 2 (TD-041, TD-042 — during code review)
- Next recommended: production/epics/inventory-system/story-004-slot-locks.md — Slot Locks & RemoveItem (Ready, 2h; also owns the Story 002 locked-slot pickup-guard test)
- Uncommitted: Inventory Story 003 (src, tests, .meta, story, EPIC.md, tech-debt register, active.md); PAT-in-remote reminder outstanding; bash.exe.stackdump untracked

## Session Extract — /story-readiness 2026-09-26 (Inventory Story 004 — Slot Locks & RemoveItem)

- Verdict NEEDS WORK → all gaps fixed in `production/epics/inventory-system/story-004-slot-locks.md` (user approved "fix all"): `RemoveItem` allowed on unlocked slots (Enhancement CR-ENH-15 step 4 needs it), `void` signatures, unregistered-charId guard, perf note, Story 002 lock guard noted as already implemented (test only). Added 2 ACs + 2 QA cases.
- **Open cross-GDD conflict (owner: Enhancement GDD, not blocking Story 004):** scrolls are stackable Consumables but CR-ENH-15 step 4 consumes them via `RemoveItem`, which clears the whole stack. Proposed: `ConsumeItem(scrollId, 1)`. Resolve in a separate Enhancement GDD authoring session before the Enhancement epic.
- Advisory (epic-wide): `TR-inv-*` IDs not in `tr-registry.yaml`.
- Next: `/dev-story production/epics/inventory-system/story-004-slot-locks.md`.

## Session Extract — /dev-story 2026-09-26
- Story: production/epics/inventory-system/story-004-slot-locks.md — Slot Locks & RemoveItem
- Files changed: src/Foundation/InventorySystem/IInventoryService.cs, src/Foundation/InventorySystem/InventoryService.cs
- Test written: tests/EditMode/InventorySystem/InventorySystem_SlotLocks_tests.cs (18 tests) — static-verified only, NOT yet run in Unity Test Runner
- Decision: RemoveItem on empty in-range slot is silent (no log, no event); Lock/Unlock call ThrowIfDispatching per the class's mutation-seam contract
- Blockers: None
- Next: run EditMode suite in Editor, then /code-review then /story-done

## Session Extract — /code-review 2026-09-26 (Inventory Story 004)
- Verdict APPROVED WITH SUGGESTIONS (unity-specialist CLEAN; qa-tester GAPS). User said "fix all" → applied all 5: added re-entrancy tests for LockSlot/UnlockSlot/RemoveItem, RemoveItem on locked multi-unit stack, RegisterCharacter re-registration clears locks, all-20-slots check in out-of-range RemoveItem tests, RemoveItem doc wording (scroll consumption).
- SlotLocks test file now 23 tests — still static-verified only; needs a real EditMode Test Runner pass.
- Next: run EditMode suite in Editor, then /story-done production/epics/inventory-system/story-004-slot-locks.md

## Session Extract — /story-done 2026-09-26
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/inventory-system/story-004-slot-locks.md — Slot Locks & RemoveItem (23 tests passing in live Test Runner)
- Tech debt logged: 1 item (TD-043 — scroll-stack vs RemoveItem cross-GDD conflict)
- Next recommended: Inventory Story 005 — Discard (production/epics/inventory-system/story-005-discard.md); run /story-readiness first. Nothing committed yet.

## Session Extract — /story-readiness 2026-09-27 (Inventory Story 005 — Discard)
- Verdict NEEDS WORK → user said "fix all, option A"; all 7 gaps applied to production/epics/inventory-system/story-005-discard.md:
  1. Signature `DiscardResult Discard(CharacterID charId, int slotIndex, int quantity)`; unregistered charId guard.
  2. Option A: out-of-range slot + unregistered charId → `SlotEmpty` (wire enum has no InvalidSlot; no GDD change).
  3. Validation order aligned to wire GDD: range → registered → empty → lock → quantity (lock/empty order observationally identical).
  4. `DiscardResult` readonly struct mirrors PickupResult (Succeeded / Fail + Debug.Assert); `DiscardFailReason : byte {None, SlotLocked, InvalidQuantity, SlotEmpty}` mirrors wire enum 1:1. Future wire message must use a different name (e.g. DiscardResultMessage).
  5. Mutation-seam contract + re-entrancy AC/test added.
  6. Performance note (O(1)).
  7. Logging: client-caused rejections silent; out-of-range → warning; unregistered → error.
  - Estimate 2h → 2.5h; +2 ACs, +2 QA cases.
- Next: /dev-story production/epics/inventory-system/story-005-discard.md

## Session Extract — /dev-story 2026-09-27
- Story: production/epics/inventory-system/story-005-discard.md — Discard (Server-Side Validation & Mutation)
- Files changed: src/Foundation/InventorySystem/DiscardFailReason.cs (new), src/Foundation/InventorySystem/DiscardResult.cs (new), src/Foundation/InventorySystem/IInventoryService.cs (Discard + docs), src/Foundation/InventorySystem/InventoryService.cs (Discard impl + seam-contract remark)
- Test written: tests/EditMode/InventorySystem/InventorySystem_Discard_tests.cs (19 tests after /code-review "fix all": +6 tests, +1 no-event assert, ADR-010 D5 doc wording fix in IInventoryService.Discard) — statically reviewed, NOT yet run in live Test Runner
- Blockers: None. (gameplay-programmer agent hit its turn limit after writing all files but before reporting; orchestrator reviewed output directly.)
- /code-review: APPROVED WITH SUGGESTIONS (Unity CLEAN, QA TESTABLE w/ gaps) — all suggestions applied.
- Next: run EditMode Test Runner, then /story-done production/epics/inventory-system/story-005-discard.md

## Session Extract — /story-done 2026-09-27
- Verdict: COMPLETE
- Story: production/epics/inventory-system/story-005-discard.md — Discard (Server-Side Validation & Mutation)
- Tech debt logged: None
- Next recommended: production/epics/inventory-system/story-006-move-merge-swap.md (Ready). Work not yet committed.

## Session Extract — /story-readiness 2026-09-27 (Inventory Story 006)
- Committed Story 005 as ef0671d.
- Story 006 readiness: NEEDS WORK → fixed → READY. Decisions (user, 2026-09-27): different-item move = swap (any category); same-ItemID merge onto full dest = no-op success, no event; empty source / out-of-range / unregistered → MoveFailReason.InvalidSlot (warning / error / no log). AC-INV-9 reasons = SourceLocked / DestLocked (wire enum).
- Files edited: production/epics/inventory-system/story-006-move-merge-swap.md (ACs, impl notes, QA cases, estimate 3h, ADR-010 D5 wording); design/gdd/inventory-system.md (Rule 7.20 clarification, AC-INV-9 reasons, Last Updated); production/epics/inventory-system/EPIC.md (open question 2 resolved). entities.yaml: no change (rule clarifications only). Not committed.
- Next: /dev-story production/epics/inventory-system/story-006-move-merge-swap.md

## Session Extract — /dev-story 2026-09-27 (Inventory Story 006)
- Story: production/epics/inventory-system/story-006-move-merge-swap.md — Slot Move (Merge, Swap & Relocate)
- Files changed: src/Foundation/InventorySystem/MoveFailReason.cs (new), src/Foundation/InventorySystem/MoveResult.cs (new), src/Foundation/InventorySystem/IInventoryService.cs (Move + docs), src/Foundation/InventorySystem/InventoryService.cs (Move + private ExecuteMove/SwapSlots; relocate = swap with Empty)
- Test written: tests/EditMode/InventorySystem/InventorySystem_MoveMergeSwap_tests.cs (18 tests passing live; +6 after /code-review "fix all" = 24, not yet re-run)
- /code-review: APPROVED WITH SUGGESTIONS — all applied: TryGetStackLimit now takes a caller name for its log prefix (Pickup/Move); +6 tests (both-locked, slot 19, two characters, post-no-op buffer, reverse merge, no OnInventoryFull).
- Uncommitted from readiness: GDD Rule 7.20 + AC-INV-9, EPIC.md open question, story-006 rewrite.
- /code-review: CHANGES REQUIRED → all applied: swapped MoveItemOut/MoveItemIn doc summaries fixed; decision (user) MoveItemOut rejects qty>1 stacks with SlotEmpty + error; TryPlaceSingle → PlaceInLowestEmptySlot; MoveItemOutResult fully-qualifies UnityEngine.Debug; +7 tests.
- Next: EditMode Test Runner → /story-done

## Session Extract — /story-done 2026-09-27 (Inventory Story 006)
- Verdict: COMPLETE
- Story: production/epics/inventory-system/story-006-move-merge-swap.md — Slot Move (Merge, Swap & Relocate)
- Tech debt logged: None
- Next recommended: production/epics/inventory-system/story-007-equipment-interface.md (readiness check next)

## Session Extract — /story-readiness + /dev-story 2026-09-27 (Inventory Story 007)
- Committed Story 006 as cddbcc7.
- Story 007 readiness: NEEDS WORK → fixed → READY. Decisions (user, 2026-09-27): successful MoveItemIn/ForceInsert do NOT reset the bag-full dedup window (GDD Rule 4.10 literal); MoveItemIn/ForceInsert reject Invalid/unknown/DB-not-ready items with a server error, no category check. No GDD change needed. Informational flag for Equipment epic: CR-EQS-8 same-tick ForceInsert-after-MoveItemIn always fails too.
- Files changed: production/epics/inventory-system/story-007-equipment-interface.md (rewritten ACs/notes/QA), src/Foundation/InventorySystem/MoveItemOutCode.cs, MoveItemOutResult.cs, MoveItemInResult.cs (new), IInventoryService.cs + InventoryService.cs (MoveItemOut, MoveItemIn, ForceInsert, private TryPlaceSingle)
- Test written: tests/EditMode/InventorySystem/InventorySystem_EquipmentInterface_tests.cs (28 passing live after CS0246 using fix; +7 after /code-review "fix all" = 35, not yet re-run)
- /code-review: CHANGES REQUIRED → all applied: swapped MoveItemOut/MoveItemIn doc summaries fixed; decision (user) MoveItemOut rejects qty>1 stacks with SlotEmpty + error; TryPlaceSingle → PlaceInLowestEmptySlot; MoveItemOutResult fully-qualifies UnityEngine.Debug; +7 tests.
- Next: EditMode Test Runner → /story-done

## Session Extract — /story-done 2026-09-27 (Inventory Story 007)
- Verdict: COMPLETE
- Story: production/epics/inventory-system/story-007-equipment-interface.md — Equipment System Interface
- Tech debt logged: 1 (TD-044 — Equipment GDD CR-EQS-8 ForceInsert retry after same-tick MoveItemIn failure is unreachable)
- Next recommended: production/epics/inventory-system/story-008-sell-and-consume.md (Ready; run /story-readiness)

## Session End — 2026-09-27
- Inventory Stories 005, 006, 007 complete and committed this session (005 ef0671d, 006 cddbcc7, 007 = this commit).
- Resume with: /story-readiness production/epics/inventory-system/story-008-sell-and-consume.md, then Story 009 (resolve TD-042 alongside it).
- Open reminders: rotate the plaintext GitHub PAT in .git/config `origin` before any push; TD-043 (scroll stack vs RemoveItem) and TD-044 (CR-EQS-8) must be resolved before the Enhancement / Equipment epics start.

## Session Extract — /story-readiness 2026-10-01 (Inventory Story 008 — NEEDS WORK → fixed → READY)
- Decisions (user, 2026-10-01): (1) `SellItem` gains a `quantity` parameter — partial-stack sells supported. Inventory GDD ("entire stack only") conflicted with Approved npc-shop.md CR-SHOP-7/8 and the wire `SellRequest.quantity`; Inventory GDD was the stale side. (2) `ConsumeItem` skips locked slots (not decremented, not counted).
- Files edited: design/gdd/inventory-system.md (Last Updated, Rule 5.12, Interactions rows for NPC Shop + Consumable Use, 2 new cross-system edge cases, Dependencies row, AC-INV-16 full + partial); production/epics/inventory-system/story-008-sell-and-consume.md (rewritten: signatures with charId, `SellItemResult`/`SellItemFailReason`, `ConsumeItemResult`/`ConsumeItemFailReason`, guards, logging, re-entrancy AC, perf note, estimate 2h → 3h); EPIC.md (open question 008 resolved). entities.yaml: no change (interface signature only; no SellItem/ConsumeItem entries there).
- Propagation check: networking-wire-protocol.md already has `SellRequest.quantity` (consistent). consumable-use-system.md calls `ConsumeItem(itemId, 1)` (consistent). NOT edited, needs user approval: npc-shop.md lines 101/143/286/561 still carry the "⚠️ Interface change required" warning, now stale; currency-system.md line 53 + OQ-CS-1 text says Inventory System owns the `AddGold(ItemSell)` call, contradicting Inventory + NPC Shop GDDs (NPC Shop calls it).
- Not committed.
- Next: /dev-story production/epics/inventory-system/story-008-sell-and-consume.md, then Story 009 (resolve TD-042 alongside it).
- Open reminders: rotate the plaintext GitHub PAT in .git/config `origin` before any push; TD-043 and TD-044 before the Enhancement / Equipment epics.
- Propagation follow-up (user approved 2026-10-01): npc-shop.md — 4 stale "Interface change required" notes on `SellItem(…, quantity)` marked resolved + Last Updated; currency-system.md — 3 spots (line 53 note, downstream-dependents row, OQ-CS-1 revision) corrected so NPC Shop, not Inventory, owns `AddGold(ItemSell)` + Last Updated. Wording only; no entities.yaml or systems-index change. Not committed.
- Noticed, not changed: currency-system.md's dependents table still lists NPC Shop (#23) as "Not Started" (npc-shop.md is Approved), and its Interactions row for NPC Shop (line ~81) lists no ItemSell `AddGold` call.

## Session Extract — /dev-story 2026-10-01 (Inventory Story 008 — NPC Shop Sell & Consumable Use)
- Story: production/epics/inventory-system/story-008-sell-and-consume.md (Status → In Progress)
- Files changed: src/Foundation/InventorySystem/SellItemFailReason.cs, SellItemResult.cs, ConsumeItemFailReason.cs, ConsumeItemResult.cs (new, + .meta); IInventoryService.cs (+SellItem, +ConsumeItem, docs); InventoryService.cs (SellItem, ConsumeItem, private HasSufficientUnlockedQuantity / CommitConsume)
- Test written: tests/EditMode/InventorySystem/InventorySystem_SellAndConsume_tests.cs (32 tests, + .meta) — statically reviewed by orchestrator (diff, usings vs Discard suite, [Test] count, 5 new GUIDs unique repo-wide); NOT yet run in live Test Runner
- Implemented by gameplay-programmer subagent (tight brief; clean first-pass report, no deviations from story).
- Blockers: None
- Uncommitted: Story 008 code + tests, plus the readiness edits (3 GDDs, story, EPIC.md).
- Next: user runs EditMode Test Runner → /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done production/epics/inventory-system/story-008-sell-and-consume.md

## Session Extract — /code-review + "fix all" 2026-10-01 (Inventory Story 008)
- User ran EditMode suite after /dev-story: ALL tests pass (32 new + existing inventory suites).
- /code-review: unity-specialist CLEAN (0 required, 2 doc suggestions); qa-tester GAPS (1 required: "dedup window not reset" AC clause unverified; 7 test suggestions). Verdict CHANGES REQUIRED (test-only). User said "fix all"; applied all 10:
  1. +2 tests: SellItem / ConsumeItem success does not reset the bag-full dedup window (full bag → blocked pickup → partial sell/consume → second blocked pickup → still 1 notification).
  2–7. +7 tests: locked stack between two unlocked; exact total across two stacks; three stacks; ConsumeItem guard order ×2 (quantity before registration, registration before ItemID.Invalid); SellItem range-before-registration; partial sell then full sell of remainder.
  8. Both re-entrancy tests now assert the exception message contains "mutated synchronously".
  9. CommitConsume remarks: throw-safety reasoning (mirrors CommitPickupPlan).
  10. IInventoryService.ConsumeItem remarks: ItemID.Invalid short-circuit stated explicitly, matching the implementation.
- Test file now 41 tests — the 9 new tests and 2 strengthened ones are NOT yet run in the live Test Runner.
- Next: user re-runs EditMode suite → /story-done production/epics/inventory-system/story-008-sell-and-consume.md

## Session Extract — /story-done 2026-10-01 (Inventory Story 008)
- User re-ran EditMode suite after review fixes: ALL tests pass (41 sell/consume tests + existing suites).
- Verdict: COMPLETE
- Story: production/epics/inventory-system/story-008-sell-and-consume.md — NPC Shop Sell & Consumable Use Interfaces (14/14 ACs)
- EPIC.md: row 008 → Complete (epic 8/9)
- Tech debt logged: None
- Next recommended: production/epics/inventory-system/story-009-snapshot-save-load.md — InventorySnapshot Save/Load & Load Validation (Ready; run /story-readiness; resolve TD-042 alongside it)
- Uncommitted: Story 008 (src, tests, .meta, story, EPIC.md), readiness GDD edits (inventory-system.md, npc-shop.md, currency-system.md), active.md. PAT-in-remote reminder outstanding before any push.

## Session Extract — /story-readiness 2026-10-01 (Inventory Story 009 — NEEDS WORK → fixed → READY)
- Story 008 committed as 5d5d60b (not pushed).
- Decisions (user, 2026-10-01): (1) implement the snapshot per the Approved Inventory GDD ({SlotIndex, ItemId, Quantity}); the missing per-item EnhancementLevel (Enhancement GDD expects it on inventory slots + in save/load; never propagated to Inventory GDD / ADR-006 / character-persistence.md / code) logged as TD-045 — dedicated design session before the Enhancement and Equipment epics. (2) Story 009 includes `UnregisterCharacter` (closes TD-042 at /story-done). (3) Quantity > StackLimit loads as-is with a warning. (4) Import fires no events (confirmed).
- Story rewritten: `InventorySnapshot` (sealed class, IReadOnlyList) + `InventorySnapshotEntry` (readonly struct); `ExportSnapshot` (read; unregistered → error + empty); `bool ImportSnapshot` (dispatch guard; null / Item DB not ready → error, false, nothing changed; otherwise reset-like-RegisterCharacter then apply; per-entry warnings); `UnregisterCharacter` (idempotent, dispatch guard). 19 ACs, 22 QA bullets, estimate 3.5h, +Story 003 dependency.
- Files edited: production/epics/inventory-system/story-009-snapshot-save-load.md, EPIC.md (open question 009 resolved), docs/tech-debt-register.md (+TD-045, 43 items). No GDD or entities.yaml change (implementation-defined load edge cases recorded in the story only — candidate follow-up: add SlotIndex ≥ 20, ItemId 0, over-limit and DB-not-ready rules to the Inventory GDD's Persistence and Load Edge Cases). Not committed.
- Next: /dev-story production/epics/inventory-system/story-009-snapshot-save-load.md

## Session Extract — /dev-story 2026-10-01 (Inventory Story 009 — InventorySnapshot Save/Load & Load-Time Validation)
- Story: production/epics/inventory-system/story-009-snapshot-save-load.md (Status → In Progress)
- Files changed: src/Foundation/InventorySystem/InventorySnapshot.cs, InventorySnapshotEntry.cs (new, + .meta); IInventoryService.cs (+ExportSnapshot, +ImportSnapshot, +UnregisterCharacter, RegisterCharacter docs); InventoryService.cs (the three methods + private ApplySnapshotEntry; class remarks: persistence scope, lifecycle, mutation-seam exception)
- Test written: tests/EditMode/InventorySystem/InventorySystem_SnapshotSaveLoad_tests.cs (29 tests, + .meta) — statically reviewed by orchestrator (diff, usings, [Test] count, 3 new GUIDs unique, log regexes vs ItemID.ToString format); NOT yet run in live Test Runner
- Implemented by gameplay-programmer subagent (tight brief; clean first-pass report). ImportSnapshot reuses RegisterCharacter for its reset.
- Blockers: None
- Uncommitted: Story 009 code + tests, readiness edits (story, EPIC.md, tech-debt register TD-045).
- Next: user runs EditMode Test Runner → /code-review src/Foundation/InventorySystem tests/EditMode/InventorySystem → /story-done production/epics/inventory-system/story-009-snapshot-save-load.md (close TD-042 there)

## Session Extract — /code-review + "fix all" 2026-10-01 (Inventory Story 009)
- User ran EditMode suite after /dev-story: ALL tests pass (29 new snapshot tests + existing suites).
- /code-review: unity-specialist CLEAN (0 required, 2 suggestions); qa-tester GAPS (0 tautologies; 10 uncovered sub-clauses + warning-strictness gap). Verdict APPROVED WITH SUGGESTIONS. User said "fix all" (given before the reports arrived; treated as standing approval); applied:
  1. +10 tests (39 total): null snapshot + DB not ready → null error only; ItemId 0 + Quantity 0 → ItemId-0 warning only; unknown item + Quantity 0 → quantity warning only; refused import leaves locks + dedup window untouched; empty snapshot import returns true and clears bag + locks; second character unaffected by import; over-limit quantity exported as-is; exported snapshot not a live view; post-unregister IsSlotLocked/HasItem/Pickup all report unregistered; unregister → register gives clean bag.
  2. `LogAssert.NoUnexpectedReceived()` added to the 9 existing warning-expectation tests (exactly the expected warnings, nothing else).
  3. `ImportSnapshot` parameter annotated `InventorySnapshot?` in InventoryService.cs and (scoped `#nullable enable`/`restore`) in IInventoryService.cs; param doc notes null is tolerated defensively.
  4. Snapshot type docs now cref `IInventoryService.Export/ImportSnapshot` (7 crefs) instead of the concrete class; UnregisterCharacter interface remarks reflowed.
- Not testable via public API (noted, not a gap in behaviour): UnregisterCharacter's removal of the dedup window alone — RegisterCharacter/ImportSnapshot also clear it on re-registration.
- Source changed (annotation + docs only, no behaviour) → needs recompile; the 10 new tests are NOT yet run in the live Test Runner.
- Next: user re-runs EditMode suite → /story-done production/epics/inventory-system/story-009-snapshot-save-load.md (close TD-042)

## Session Extract — /story-done 2026-10-01 (Inventory Story 009 — INVENTORY SYSTEM EPIC COMPLETE, 9/9)
- User re-ran EditMode suite after review fixes: ALL tests pass (39 snapshot tests + existing suites).
- Verdict: COMPLETE WITH NOTES
- Story: production/epics/inventory-system/story-009-snapshot-save-load.md — InventorySnapshot Save/Load & Load-Time Validation (19/19 ACs)
- EPIC.md: row 009 → Complete; epic Status → Complete (9/9); Next Step rewritten (TD-043/044/045 before Enhancement + Equipment epics; AC-INV-11 deferred to Inventory UI epic).
- Tech debt: TD-042 RESOLVED (UnregisterCharacter); TD-045 open (per-item EnhancementLevel; wording corrected — Enhancement GDD is Approved per systems-index, its own header still says "In Design").
- GDD (user approved): inventory-system.md Persistence and Load Edge Cases +5 rules (SlotIndex ≥ 20, ItemId 0, over-limit loads as-is, load refused before Item DB ready, load fires no events) + Last Updated. entities.yaml: no change. Propagation note, NOT edited: character-persistence.md (In Review) should state that a refused inventory load must retry/fail the login, never be treated as an empty bag; and that logout calls `UnregisterCharacter`.
- Noticed, not changed: GDD header statuses stale vs systems-index for enhancement-system.md ("In Design" vs Approved) and consumable-use-system.md ("Designed — In Review" vs Approved).
- Committed with this entry (Story 009). Not pushed. PAT-in-remote reminder outstanding before any push.
- Next: no stories left in this epic. Options: design session for TD-045 (+TD-043/044) before the Enhancement/Equipment epics; or /create-stories for the next Core-layer epic; or /sprint-plan (the session hook keeps flagging no production planning).

## Session Extract — Design session TD-045 / TD-043 / TD-044 (2026-10-01) — IN PROGRESS (authoring)
- Story 009 committed as fef6d67 (not pushed). Inventory epic was 9/9.
- Decisions (user, 2026-10-01):
  1. TD-045: enhancement level is a PER-SLOT field `EnhancementLevel: byte` on the inventory slot (not item instances). Non-zero only on a single equipment item; stacks always 0. Travels on move/swap; resets when a slot empties; pickups start at 0. `MoveItemOutResult` gains the level; `MoveItemIn`/`ForceInsert` take a level. New `SetEnhancementLevel(slotIndex, level)` — Enhancement System only, slot must be locked. Change event + snapshot + ADR-006 JSON + persistence schema + wire slot messages gain the level.
  2. TD-043: Enhancement consumes a scroll via `ConsumeItem(scrollItemId, 1)` (not RemoveItem).
  3. TD-044: CR-EQS-8 → abort swap, restore modifiers, keep old item equipped, return inventory error; no ForceInsert retry, no empty-slot outcome.
  4. Invalid saved level on load: above MAX → clamp to MAX; non-zero on Quantity > 1 → 0; warn; item always loads.
  5. Same-ItemID equipment moved onto each other swaps (Rule 7.20 already says so; code currently no-ops). CR-EQS-4 same-item guard compares ItemID AND level. Wire: EquipRequest gains inventory slot index; MoveResult/EquipResult gain level bytes.
- Approved changeset (all): inventory-system.md, equipment-system.md, enhancement-system.md, character-persistence.md, ADR-006 (dated amendment), networking-wire-protocol.md, entities.yaml, new Inventory Story 010 + EPIC.md (epic reopens 9/10), tech-debt register (TD-043/044 resolved; TD-045 resolved in design, code pending Story 010; log new TD for wire EnhancementAttemptRequest mismatch + missing client inventory-sync message), session state.
- Progress checklist: [x] inventory-system.md (14 edits, AC-INV-17..20, BLOCKING 22)  [x] equipment-system.md (struct level, CR-EQS-4/5/6/7/8/11/12/14/15, AC-EQS-8/16/17 revised, AC-EQS-28/29, OQ-EQS-8)  [x] enhancement-system.md  [x] character-persistence.md  [x] ADR-006 (Amendment 1)  [x] wire protocol  [x] entities.yaml  [x] Story 010 + EPIC.md  [x] tech-debt register (TD-043/044 resolved, TD-045 resolved in design, TD-046 logged)  [x] propagation check
- After: lean /design-review on inventory, equipment, enhancement (separate sessions).
- AUTHORING COMPLETE 2026-10-01 (nothing committed). Files changed: design/gdd/inventory-system.md, equipment-system.md, enhancement-system.md, character-persistence.md, networking-wire-protocol.md; docs/architecture/ADR-006-persistence-layer.md (Amendment 1); design/registry/entities.yaml (InventorySlotRecord + InventorySnapshot added; EquipmentSlotEntry, EquipResult, MAX_ENHANCEMENT_LEVEL revised); production/epics/inventory-system/story-010-enhancement-level.md (new, Ready, 5h) + EPIC.md (In Progress 9/10); docs/tech-debt-register.md (TD-043/044 RESOLVED; TD-045 resolved in design, code pending Story 010; TD-046 logged — wire EnhancementAttemptRequest vs slot-based ConfirmEnhancement, and no client inventory slot-update message).
- Found during authoring: equipment-system.md body had never received Enhancement upstream amendment #2 (slot entry EnhancementLevel, GetFlatBonus modifier registration, GetEquippedWeaponEnhancementLevel) although entities.yaml recorded it on 2026-05-22 — applied now. PrestigeBand source slot was unstated — recorded Weapon slot as the working rule + OQ-EQS-8 (confirm at re-review).
- Propagation check done: (a) kept "23 logical fields" in character-persistence.md (levels live inside the existing inventory-slots field / JSONB column) so ADR-006 / ADR-007 / architecture.md "23-field" wording stays correct; (b) all 6 `ForceInsert(MergeResultItemID)` references now pass level 0; (c) no remaining `RemoveItem(scroll…)`; (d) MoveResult size references updated (wire GDD + inventory OQ-INV-6).
- NOT changed, flag to user: systems-index.md (no "revised — lean re-review pending" marker on the three Approved GDDs); GDD header statuses stale vs index (enhancement "In Design", consumable-use "Designed — In Review"); damage-calculation.md calls `GetEquippedWeaponID(AttackerID)` while equipment-system.md names it `GetEquippedWeaponItemID()` (pre-existing naming mismatch); networking-channel-contract.md EquipRequest row does not mention the new inventorySlot validation.
- Next (one job per session): /design-review design/gdd/inventory-system.md --depth lean; then equipment-system.md; then enhancement-system.md; then /story-readiness production/epics/inventory-system/story-010-enhancement-level.md → /dev-story. Commit the design changeset when the user asks. PAT-in-remote reminder outstanding before any push (2 unpushed commits: 5d5d60b, fef6d67).

## Session Extract — /design-review inventory-system.md --depth lean 2026-10-01 (NEEDS REVISION → revised in-session → APPROVED)
- Design changeset committed earlier as e1c486d (main is 10 commits ahead of origin; not pushed).
- Lean re-review (no agents; reviewer authored the amendments the same session — noted in the log). 8/8 sections; dependency GDDs exist except Inventory UI GDD + design/ux/inventory-screen.md (pre-existing).
- Blockers fixed: (1) level invariant re-keyed from `Quantity = 1` to `StackLimit = 1` (Rule 1.4, 5.14a, 8.24a, load rule) — a stackable item at quantity 1 could otherwise hold a level and carry it into a stack; (2) `MAX_ENHANCEMENT_LEVEL` declared as a soft upstream dependency + cross-system knob, reverse reference added in enhancement-system.md.
- Recommended applied: Rule 7.20 no-op sentence scoped to stackable items; DECISION (user): moving an item onto an identical one (same ItemID + level, StackLimit 1) is a no-op success with no event; AC-INV-21/22 added (24 blocking ACs); `GetSlot` named as the read interface; header status refreshed.
- Files edited (uncommitted): design/gdd/inventory-system.md, design/gdd/enhancement-system.md (one dependency-row sentence), design/registry/entities.yaml (InventorySlotRecord + InventorySnapshot notes), design/gdd/systems-index.md (row 12: Approved, revised + lean re-reviewed 2026-10-01; dependency note), design/gdd/reviews/inventory-system-review-log.md (new entry), production/epics/inventory-system/story-010-enhancement-level.md (ACs/notes/QA aligned: StackLimit check, identical-item no-op, AC-INV-21/22), EPIC.md (AC counts 24 / 23 of 24).
- Next: /design-review design/gdd/equipment-system.md --depth lean (fresh session), then enhancement-system.md; then /story-readiness + /dev-story for Inventory Story 010.

## Session Extract — /design-review equipment-system.md --depth lean 2026-10-01 (NEEDS REVISION → revised in-session → lean re-review pending)
- Lean review of the TD-044/TD-045 amendments (no agents). 8/8 sections. Verdict NEEDS REVISION: 5 blocking, 5 recommended. User chose "revise now", scope "blockers + in-file recommended".
- Blockers fixed: (1) CR-EQS-4/13 now read the incoming item via `Inventory.GetSlot(slotIndex)` (read-only) — added to the Equipment interface rows in equipment-system.md and inventory-system.md; (2) `GetSlotEnhancementLevel(GearSlot): byte` added (per-slot level read; weapon getter is the Weapon-slot case); (3) `EquipResult` → wire mapping table added, `EquipFailReason.InventoryError = 6` added to networking-wire-protocol.md, `NoChange` = success/None; (4) Dependencies + Bidirectionality refreshed (Persistence saves `{ItemID, EnhancementLevel}`, Damage Calc + Persistence Approved, Enhancement downstream row); (5) DECISION (user): OQ-EQS-8 resolved — PrestigeBand comes from the Weapon slot only; AC-EQS-30 added.
- Recommended applied: CR-EQS-7 null-item path zeroes the level; AC-EQS-31 (step 7 rollback keeps level); F-EQS-4 rewritten to the F-ENH-3 result (old estimate-based derivation removed; boundary overlap = accepted decision per F-ENH-3); `Equip(GearSlot, ItemID, inventorySlotIndex)` / `Unequip(GearSlot)` signatures stated; AC-EQS-2 Iron value 18 → 25; AC-EQS-11 setup states weapon level; AC-EQS-25 retagged BLOCKING (Persistence GDD exists); AC-EQS-29 stub note; OQ-EQS-1/5/7 refreshed.
- New open question: OQ-EQS-9 — wire `ItemLocked`(4) / `ItemNotInInventory`(5) have no `EquipResult` member and CR-EQS-5 step 2 names no result code; decide owner layer before /create-stories for the Equipment epic.
- NOT changed (out of approved scope, flag again): damage-calculation.md `GetEquippedWeaponID(AttackerID)` naming/entity-parameter drift; character-persistence.md line 314 `GetFlatBonus(level, gearTier, isWeapon)` wrong signature, and load step 4 `CorruptRecord` on over-max gear level vs Inventory's clamp; systems-index design-order list (line ~120) still shows Equipment's original 3 dependencies.
- Files edited (uncommitted): design/gdd/equipment-system.md, inventory-system.md (2 interface rows), networking-wire-protocol.md (EquipFailReason + EquipResult prose), enhancement-system.md (CR-ENH-12 contract-mapping note; AC-ENH-15/16/17/32 setup wording), design/registry/entities.yaml (EquipmentSlotEntry, EquipResult, equipmentAppearanceFlags notes), design/gdd/systems-index.md (row 13 → Needs Revision, re-review pending), design/gdd/reviews/equipment-system-review-log.md (Pass 6 entry).
- Propagation check done: `EquipFailReason` referenced only in wire-protocol, channel-contract (generic mention, still correct) and equipment-system.md — no code yet; `GetEquipmentSlotState` / OQ-EQS-8 referenced only in equipment-system.md + the enhancement note; no remaining "from ItemIDs alone" claim except the dated correction note.
- Next: /clear → /design-review design/gdd/equipment-system.md --depth lean (verify fixes) → then enhancement-system.md lean review → /story-readiness + /dev-story for Inventory Story 010. PAT-in-remote reminder still outstanding before any push.

## Session Extract — /design-review equipment-system.md --depth lean 2026-10-01 (Pass 7 — APPROVED, 1 pre-implementation gate)
- Lean verify pass (no agents) on the Pass 6 fixes. 8/8 sections. All 5 Pass 6 blockers verified closed against inventory-system.md, enhancement-system.md, networking-wire-protocol.md and the registry. Arithmetic re-derived for AC-EQS-2/11/29/30 and the F-EQS-4 table — correct. Verdict APPROVED: 0 blocking, 6 recommended.
- Registry fixed at approval (design/registry/entities.yaml): `MoveItemOutResult` fields now `{ItemId, EnhancementLevel, Code}` and its stale "Inventory currently returns ItemID" note replaced; `EquipResult.values` gains `Success`.
- Status writes: equipment-system.md header → Approved (Pass 7 lean); systems-index.md row 13 → Approved with the OQ-EQS-9 gate; review log Pass 7 entry appended.
- Pre-implementation gate: OQ-EQS-9 (which layer produces wire `ItemLocked` / `ItemNotInInventory`; CR-EQS-13 stale-render guard and CR-EQS-5 step 2 name no `EquipResult` code; EC-EQS-4 "Item is locked" vs CR-EQS-6 step 7 `InventoryError`). Resolve before /create-stories for the Equipment epic, then add an AC for the stale-render guard.
- Recommended, NOT applied (review session — no authoring): AC-EQS-25 assert saved level used on load; AC-EQS-8 setup name the inventory slot of the second ItemA; EC-EQS-4 stale `MoveItemOut` note; EC-EQS-10 "concurrent write race" vs CR-EQS-8 single-threaded tick; OQ-EQS-6 looks closable (wire-protocol: zone join carries flags in ZoneStateSnapshotFragment); cross-document drift unchanged — character-persistence.md line 314 `GetFlatBonus` signature, line 312 "no save API" vs line 244, `CorruptRecord` vs Inventory clamp; damage-calculation.md `GetEquippedWeaponID(AttackerID)`; networking-wire-protocol.md line 964 "future Enhancement outcome must emit AppearanceChangedEvent" (now impossible); systems-index design-order line ~120.
- Files edited (uncommitted): design/registry/entities.yaml, design/gdd/equipment-system.md (status line only), design/gdd/systems-index.md (row 13), design/gdd/reviews/equipment-system-review-log.md, this file.
- Next: /clear → /design-review design/gdd/enhancement-system.md --depth lean → /story-readiness + /dev-story for Inventory Story 010. PAT-in-remote reminder still outstanding before any push.

## Session Extract — /design-review enhancement-system.md --depth lean 2026-10-01 (Pass 5 — NEEDS REVISION, 5 blockers; review session only, GDD not edited)

- Verdict: **NEEDS REVISION** (5 blocking, 9 recommended, scope L). Full blocker/recommended list is in `design/gdd/reviews/enhancement-system-review-log.md` (top entry) — read that, not this summary, when authoring.
- Verified OK: Inventory contract (`SetEnhancementLevel` / `ConsumeItem(scrollItemID, 1)` / `RemoveItem` / lock calls) bidirectional with inventory-system.md; Equipment hand-off and Weapon-slot-only PrestigeBand; F-ENH-3 and F-ENH-5 arithmetic re-derived; scroll prices match npc-shop.md.
- Blockers: (1) destruction not in the committed record — `RemoveItem` deferred until after commit, but `SaveIrreversibleOutcome` saves the live bag; **needs a user design decision** (remove-before-commit + Inventory restore call, vs explicit outcome passed to Character Persistence); (2) `GetElementalBonus(level, gearTier, isWeapon)` has no base parameter — AC-ENH-20 unsatisfiable, +0 elemental weapons would deal 0 (pre-existing; propagates to damage-calculation.md, character-persistence.md, item-database.md Rule 19, entities.yaml; no code implements the interface yet); (3) step 6 "non-destructive failure" clause contradicts CR-ENH-9/10; (4) step 4 failure code unnamed + no `LOCKED → IDLE` transition; (5) no AC for stack N → N−1 or success-path rollback.
- Pre-implementation gates (not blockers): Item Database amendment #4 still unapplied (scroll records, `ScrollData.TargetGearTier`, scroll `StackLimit`); wire-protocol Enhancement message set differs from this GDD's; npc-shop OQ-NS-6 callback undefined.
- Files edited (uncommitted): design/gdd/systems-index.md (row 15 → Needs Revision; counts 38→37 approved, 1 needs revision; Approved / In Review lists), design/gdd/reviews/enhancement-system-review-log.md, this file. enhancement-system.md itself untouched (its header still says "Lean re-review pending" / "In Design" — fix in the authoring session).
- Next: /clear → authoring session for enhancement-system.md (decide blocker 1 first; batch edits per section; triad + propagation check across inventory / character-persistence / damage-calculation / item-database / entities.yaml) → /clear → /design-review design/gdd/enhancement-system.md --depth lean. Inventory Story 010 does not depend on these blockers. PAT-in-remote reminder still outstanding before any push.

## Session Extract — Authoring session enhancement-system.md Revision Pass 2 (2026-10-01) — AUTHORING COMPLETE (nothing committed)

- Decisions (user): (1) Blocker 1 → apply the outcome to the bag BEFORE the commit (step 6a `SetEnhancementLevel` / `RemoveItem`, step 6b `SaveIrreversibleOutcome(EnhancementResult)`); rollback on a failed commit = `ForceInsert(itemID, previousLevel)` (destruction) or `SetEnhancementLevel(previousLevel)` (success) + `PickupRequest(CharacterID, scrollItemID, 1)`. (2) Blocker 2 → `GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)` returns the full clamped F-ENH-2 value. (3) Scope = blockers + in-file recommended. (4) Approved changeset includes new CR-ENH-18 (attempt exclusivity, `IsAttemptInProgress`, OQ-ENH-7) and "a client disconnect never aborts an attempt" (CR-ENH-11 / EC-ENH-2).
- Approved files: enhancement-system.md, inventory-system.md, damage-calculation.md, character-persistence.md, item-database.md, entities.yaml, systems-index.md, enhancement review log, this file.
- Progress: [x] enhancement-system.md header, Core Rules (CR-ENH-7/11/12/15/17/18), states, Interactions, F-ENH-2  [x] Edge Cases (EC-ENH-2/6), Dependencies, UI (result codes), ACs (AC-ENH-9/10/11/15/16/17/20/21/23/32 revised, AC-ENH-33..38 added — 38 total), Open Questions (OQ-ENH-3/6 resolved, OQ-ENH-7/8 added)  [x] propagation files  [x] entities.yaml (`IEnhancementBonusProvider`, F-ENH-2)  [x] systems-index row 15 + review log (Revision Pass 2 entry)  [x] propagation check
- Files edited (uncommitted): design/gdd/enhancement-system.md; inventory-system.md (Enhancement interface rows list the rollback `ForceInsert` / `PickupRequest`; OQ-INV-4 resolved; header note — no rule change, Story 010 unaffected); damage-calculation.md (Step 4, Interactions + Dependencies rows, OQ-DC-1 note, header); character-persistence.md (CR-CP-5 Enhancement rollback sentence; `IEnhancementBonusProvider` signatures on the interactions row — also fixes the wrong `GetFlatBonus` signature flagged in earlier sessions); item-database.md (Rule 19 clause); design/registry/entities.yaml; design/gdd/systems-index.md (row 15: Needs Revision — revised, lean re-review pending, gate OQ-ENH-7); design/gdd/reviews/enhancement-system-review-log.md; this file.
- Propagation check result: no stale `GetElementalBonus(level, gearTier, isWeapon)` remains in the approved files. One stale reference found outside the approved list — design/gdd/equipment-system.md line 120 (elemental-weapon rule prose) — fixed with user approval (signature phrase + header note; Equipment stays Approved Pass 7, no rule change). entities.yaml was not parser-validated (no Python on this machine); edits are inside existing quoted strings.
- Not changed, flag again: npc-shop.md OQ-NS-6 (answerable from CR-ENH-17 "In-flight attempts"); wire-protocol Enhancement message set + line 964 (TD-046); Item Database amendment #4 (scroll records); item-database.md open question on elemental scaling (line ~593, stale); damage-calculation.md `GetEquippedWeaponID(AttackerID)` naming drift; character-persistence.md header Status "In Review" vs index Approved, load step 4 `CorruptRecord` vs Inventory clamp; systems-index design-order line ~120; damage-calculation.md / inventory-system.md / character-persistence.md / item-database.md carry no "revised" marker in systems-index (contract-row edits only).
- New pre-implementation gate: OQ-ENH-7 (which layer holds a character's inventory-mutating requests while `IsAttemptInProgress` is true — session dispatcher vs each system; applies to every caller-owned rollback behind `SaveIrreversibleOutcome`).
- Next: /clear → /design-review design/gdd/enhancement-system.md --depth lean (verify Pass 5 blockers closed) → /story-readiness + /dev-story for Inventory Story 010. Commit the design changeset when the user asks (main is ahead of origin; PAT-in-remote reminder still outstanding before any push).

## Session Extract — /code-review 2026-10-07 — Damage Calculation Story 001 (APPROVED WITH SUGGESTIONS, all 7 applied, not committed)
- Reviewers: unity-specialist (no blocking, 4 warnings), qa-tester (GAPS, none blocking). No required changes.
- Applied ("fix all"): `DamageResult.Rejected(context)` factory + named arguments + doc that FinalDamage 0 means rejected; `DamageCalculationConfig` gains a 4th constructor parameter `int maxBaseDamage` (default 99999, valid [1, `MAX_BASE_DAMAGE_LIMIT` = 2^24]) and rejects infinite `kMagic`; `DamageCalculator` rejects `baseDamage > MaxBaseDamage` with a dev error ("BaseDamage must be at most"); test file: 21 new cases (48 total), `AssertRejected` helper, named config constants, null-guarded TearDown.
- Tests: full EditMode suite in batch mode, 1948 / 1948 passed.
- Record at `/story-done`: (a) assembly ADR gate not met (user decision, Story 006); (b) `MaxBaseDamage` is a config value beyond the story's three knobs — Stories 002–005 must use the 4-argument config constructor; (c) unknown target is treated as Defense 0 (pinned by a test; whether it should be an error is open for Story 004).
- Open, outside this story: GDD says BaseDamage / AttackPower range is [1, 9999] (`damage-calculation.md:143`, `:265`); `StatSchema.cs:86` clamps AttackPower to 99999. One of them is out of date.
- Next: `/story-done production/epics/damage-calculation/story-001-result-types-physical-mitigation.md`, then Story 002.

## Session Extract — /story-done 2026-10-07
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/damage-calculation/story-001-result-types-physical-mitigation.md` — Result Types, Tuning Config, Physical Mitigation and Final Floor. `EPIC.md` → In Progress (1/6).
- Tech debt logged: None (user chose notes in stories). Notes added to Stories 002, 004, 005 (4-argument config constructor; unknown-target question in 004) and to `EPIC.md` (AttackPower 9999 vs 99999 mismatch, `MaxBaseDamage` not in GDD Tuning Knobs).
- Nothing committed: Damage Calculation stories, `EPIC.md`, source, tests, `.meta` files, this file.
- Next recommended: Story 002 — Elemental Bonus and Mitigation, `production/epics/damage-calculation/story-002-elemental-bonus-and-mitigation.md` (`/story-readiness`, then `/dev-story`).

## Session Extract — /dev-story 2026-10-08 (Enhancement Story 012)
- Story: production/epics/enhancement-system/story-012-server-assembly-move.md — Move Enhancement to the Server Assembly
- `/story-readiness`: READY (lean mode, QL-STORY-READY skipped); the story's counts and reference-graph claims were checked against the repo.
- Files changed: `src/Foundation/EnhancementSystem/` → `src/ServerLogic/EnhancementSystem/` (14 `.cs`, 14 `.cs.meta`, folder `.meta`; 29 git renames, 0 lines changed, staged by `git mv`); `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (`NotYetMovedList` 167 → 153, `ServerOnlyNamespaces` gains `IronGrind.EnhancementSystem`; `SharedAllowList` unchanged at 26).
- Test written: none new — the existing boundary tests cover the move through the edited lists.
- Graph and serialization re-checks at implementation time: only `DamageCalculator.cs` references Enhancement types from outside the folder; no serialization attribute or by-name type lookup in the folder, so no `[MovedFrom]`.
- **Not verified: the EditMode suite has not been run after the move.** The Editor was open (`Unity.exe`, `Temp/UnityLockfile`), so batch mode was not possible. Expected total 2003. The "Suite green" criterion is open until a total is recorded.
- Nothing committed. Untracked: the story file, `bash.exe.stackdump`. PAT rotation is still unconfirmed.
- Next: suite result (user's Test Runner total, or batch mode with the Editor closed), then /code-review and /story-done.

## Session Extract — /story-done 2026-10-08 (Enhancement Story 012)
- Verdict: COMPLETE
- Story: production/epics/enhancement-system/story-012-server-assembly-move.md — Move Enhancement to the Server Assembly
- Suite after the move: 2003/2003 passed, read from the Editor Test Runner results file (`%USERPROFILE%/AppData/LocalLow/DefaultCompany/IronGrind/TestResults.xml`); no compile error or warning in the Editor log. The "suite not run" note in the /dev-story extract above is superseded.
- `/code-review`: APPROVED, no changes. `EPIC.md` updated to 9/12.
- Tech debt logged: None
- Nothing committed: 29 staged renames, plus `AssemblyBoundaryLists.cs`, the story file (untracked), `EPIC.md` and this file. PAT rotation is still unconfirmed.
- Next recommended: the NPC Interaction move story (fourth in ADR-012's order) — not yet written; `/create-stories` for it, then `/story-readiness`.

## Session Extract — 2026-10-08 (Enhancement Story 012 committed; Story 013 created)
- Story 012 committed as `f9d067d` (29 renames, `AssemblyBoundaryLists.cs`, story, `EPIC.md`, this file). The "Nothing committed" note in the /story-done extract above is superseded. Not pushed; PAT rotation is still unconfirmed.
- Created: production/epics/enhancement-system/story-013-npc-interaction-server-assembly-move.md — Move NPC Interaction to the Server Assembly (ADR-012 step 2, fourth in the order). Placed in the Enhancement epic because the folder was written by Enhancement Story 006 and no NPC Interaction epic exists. `EPIC.md` is 9/13 with the new row.
- Facts checked at creation: 4 types in `src/Foundation/NpcInteraction/`; only outside user is `EnhancementService` (in `ServerLogic`); no `internal`, no serialization attributes; expected lists 153 → 149; expected suite total 2003.
- Uncommitted: Story 013 file (untracked), `EPIC.md`, this file.
- Next: /story-readiness production/epics/enhancement-system/story-013-npc-interaction-server-assembly-move.md, then /dev-story.

## Session Extract — /dev-story 2026-10-08 (Enhancement Story 013)
- Story: production/epics/enhancement-system/story-013-npc-interaction-server-assembly-move.md — Move NPC Interaction to the Server Assembly
- `/story-readiness`: READY (lean mode); the story's claims were re-checked against the repo.
- Files changed: `src/Foundation/NpcInteraction/` → `src/ServerLogic/NpcInteraction/` (4 `.cs`, 4 `.cs.meta`, folder `.meta`; 9 git renames, 0 lines changed, staged by `git mv`); `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (`NotYetMovedList` 153 → 149, `ServerOnlyNamespaces` gains `IronGrind.NpcInteraction`; `SharedAllowList` unchanged at 26).
- Test written: none new — the existing boundary tests cover the move through the edited lists.
- Graph and serialization re-checks at implementation time: only `EnhancementService.cs` references NPC Interaction types from outside the folder; no serialization attribute, by-name type lookup or `UnityEngine` use, so no `[MovedFrom]`.
- **Not verified: the EditMode suite has not been run after this move.** The Editor was open; `TestResults.xml` is still the 12:54 run from Story 012. Expected total 2003.
- Nothing committed: the 9 staged renames, the list file, the Story 013 file (untracked), `EPIC.md`, this file. PAT rotation is still unconfirmed.
- Next: user runs the Test Runner (total is read from `TestResults.xml`), then /code-review tests/EditMode/Architecture/AssemblyBoundaryLists.cs and /story-done.

## Session Extract — /story-done 2026-10-08 (Enhancement Story 013)
- Verdict: COMPLETE
- Story: production/epics/enhancement-system/story-013-npc-interaction-server-assembly-move.md — Move NPC Interaction to the Server Assembly
- Suite after the move: 2003/2003 passed, read from `TestResults.xml` (13:04 run); no compile error or warning. The "suite not run" note in the /dev-story extract above is superseded.
- Code review: inline only, no specialist panel. `EPIC.md` is 10/13.
- Tech debt logged: None
- Nothing committed: 9 staged renames, `AssemblyBoundaryLists.cs`, the Story 013 file (untracked), `EPIC.md`, this file. PAT rotation is still unconfirmed.
- Next recommended: the Inventory move story (fifth in ADR-012's order) — not yet written. It is larger than the last two: 22 types, and some are likely client-facing (snapshot and result types), so it needs a per-type classification.

## Session Extract — 2026-10-08 (Enhancement Story 013 committed; Inventory Story 011 created)
- Enhancement Story 013 committed as `7e6b3f4`. The "Nothing committed" note in the /story-done extract above is superseded. Not pushed; PAT rotation is still unconfirmed.
- Created: production/epics/inventory-system/story-011-server-assembly-move.md — Move Inventory to the Server Assembly (ADR-012 step 2, fifth in the order). The Inventory `EPIC.md` goes back to In Progress (10/11).
- **Classification decision in the story, to confirm at /story-readiness:** 20 of the 22 types move; `DiscardFailReason` and `MoveFailReason` stay in `Foundation` on the shared allow-list as planned-consumer wire enums (ADR-012 Decision 3 rule 3; `networking-wire-protocol.md` defines both, and the code mirrors them value for value). Consequences: the folder is split, `IronGrind.InventorySystem` is not added to `ServerOnlyNamespaces`, `src/ServerLogic/InventorySystem.meta` is a new Unity-generated file to commit, and the two kept files get a doc-comment-only edit (crefs to moved types become `<c>` text). Alternative not taken: move all 22 as the Enhancement story did.
- Facts checked at creation: only `EnhancementService`, `GroundItemService`, `LootAuctionService` use the types from outside the folder; no `internal` member or type of another system is used (Inventory shared an assembly with Networking, so this was not compiler-checked before); no serialization attributes; expected lists `NotYetMovedList` 149 → 127, `SharedAllowList` 26 → 28; expected suite total 2003.
- Uncommitted: the Story 011 file (untracked), `production/epics/inventory-system/EPIC.md`, this file.
- Next: /story-readiness production/epics/inventory-system/story-011-server-assembly-move.md, then /dev-story.

## Session Extract — /dev-story 2026-10-08 (Inventory Story 011)
- Story: production/epics/inventory-system/story-011-server-assembly-move.md — Move Inventory to the Server Assembly. The user said "continue" after the classification was put to them: 20 types move, `DiscardFailReason` and `MoveFailReason` stay in `Foundation`.
- Files changed: 20 `.cs` + 20 `.cs.meta` from `src/Foundation/InventorySystem/` to `src/ServerLogic/InventorySystem/` (40 git renames, 0 lines changed, staged); `DiscardFailReason.cs` and `MoveFailReason.cs` (three doc-comment crefs to moved types became `<c>` text, nothing else); `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (`NotYetMovedList` 149 → 127, `SharedAllowList` 26 → 28 with the two wire enums, `ServerOnlyNamespaces` unchanged, its doc comment now names Inventory).
- Test written: none new — the existing boundary tests cover the move through the edited lists.
- Re-checks at implementation time: only the three `ServerLogic` services use Inventory types from outside the folder; no serialization attribute or by-name type lookup.
- **Not verified: the EditMode suite has not been run after this move.** The Editor was open at 15:35. Expected total 2003.
- **Pending file:** `src/ServerLogic/InventorySystem.meta` does not exist yet; Unity generates it on the next refresh and it must be staged with the commit.
- Nothing committed. PAT rotation is still unconfirmed.
- Next: user runs the Test Runner (total read from `TestResults.xml`), then /story-done and commit.

## Session Extract — /story-done 2026-10-08 (Inventory Story 011)
- Verdict: COMPLETE
- Story: production/epics/inventory-system/story-011-server-assembly-move.md — Move Inventory to the Server Assembly
- Suite after the move: 2003/2003 passed, read from `TestResults.xml` (15:36 run); no compile error or warning. `src/ServerLogic/InventorySystem.meta` was generated and is staged. The "not verified" and "pending file" notes in the /dev-story extract above are superseded.
- Code review: inline only, no specialist panel. Inventory `EPIC.md` is Complete (11/11).
- Tech debt logged: None
- Nothing committed: 40 staged renames, the new folder `.meta` (staged), the two enum files, `AssemblyBoundaryLists.cs`, the Story 011 file (untracked), the Inventory `EPIC.md`, this file. PAT rotation is still unconfirmed.
- ADR-012 move order status: Damage Calculation, Loot Table, Enhancement, NPC Interaction, Inventory done (5 of 9). `NotYetMovedList` is 127 (Leveling 8, Networking 108, Currency 5, Character Stats 6).
- Next recommended: the Leveling move story — not yet written, and not a mechanical move: ADR-012 Decision 7 makes it carry the client read-only views, the respec request interface, the display-formula class and the `LevelingHudManualTestHarness` move to `src/DevHarness/`. Start it in a fresh session.

> **Commit status 2026-10-08 (Inventory Story 011):** committed on `main` with the message "Inventory: Story 011 complete (moved to IronGrind.ServerLogic)" (40 renames, the new folder `.meta`, the two enum files, `AssemblyBoundaryLists.cs`, story, `EPIC.md`, this file). The "Nothing committed" note in the /story-done extract above is superseded. Not pushed. Untracked and left alone: `bash.exe.stackdump`. PAT rotation is still unconfirmed. **Next:** write the Leveling move story (ADR-012 Decision 7 scope) in a fresh session.
