# Architecture Review Report — ADR-012

> **Date:** 2026-10-08
> **Engine:** Unity 6.3 LTS (6000.3)
> **ADR reviewed:** ADR-012 Server/Client Assembly Boundary (Proposed, `2ae5c13`)
> **Mode:** `/architecture-review on ADR-012` (single ADR)
> **Prior review:** `architecture-review-2026-10-07.md` (CONCERNS, 11 ADRs)
> **Verdict:** **CONCERNS** — the core decision is sound and matches the Unity 6.3 manual; three parts of the ADR do not hold against the code as it stands and should be fixed before it is marked Accepted.

## Scope Note

- **Read in full:** ADR-012, `architecture-review-2026-10-07.md`, `architecture-traceability.md`, Damage Calculation Story 006, both `.asmdef` files, `Packages/manifest.json`.
- **Read in part:** ADR-004 (Decision), ADR-010 (Decisions 1–5); ADR-005, 006, 007, 008, 009, 011 and the control manifest by grep. GDDs at the lines ADR-012 cites and at every `ServerLogic.asmdef` / `UNITY_SERVER` mention.
- **Source checks:** `src/Foundation/` cross-folder reference graph (by `using` and by type name), `Assets/` scripts and asset GUID references, `ProjectSettings`.
- **Engine:** Unity 6.3 manual read directly on 2026-10-08 (Assembly Definition Inspector reference, asmdef file format, Build for Dedicated Server).
- **Not run:** the `unity-specialist` consultation; the full traceability matrix (Phases 2–3) — unchanged from 2026-10-07 except the new domain row. `tr-registry.yaml` left empty, as before.

---

## Blocking — fix in the ADR before Accepted

### B1 — Migration Plan step 2 orders the moves the wrong way
- The ADR: "in dependency order so that nothing in `Foundation` is left referencing a moved type: Currency (Group G), Enhancement, Loot Table, Inventory, Leveling, Character Stats, NPC Interaction, then Networking".
- `Foundation` cannot reference `ServerLogic`, so a system can move only after every system that uses its server types has moved. The listed order moves the most-depended-on system first.
- Evidence (`src/Foundation/`, files referencing the type from another folder):
  - `ICurrencyService` — Inventory 1, Leveling 1, Loot Table 2, Networking 2
  - `CurrencySystem` — Inventory 1, Loot Table 1
  - `GoldMutationResult` — Inventory 1, Loot Table 2, Networking 1
  - `IInventoryService` and inventory types — Enhancement, Loot Table
  - `ServerTickLoop`, `CommitBeforeBroadcastSequencer`, `ILootDropSink` (Networking) — Inventory, Loot Table, NPC Interaction
  - `INpcInteractionSessions` — Enhancement
  - Character Stats folder — referenced by every other system
- An order that fits this graph: Damage Calculation → Loot Table → Enhancement → NPC Interaction → Inventory → Leveling → Networking (server part) → Currency → Character Stats.
- Alternative: keep the listed order and leave each moved system's service interface in `Foundation` until its callers move. That contradicts Decision 3 ("an interface lives with its consumer") unless the ADR says it is temporary.

### B2 — The HUD depends on types Decision 3 sends to `ServerLogic`
- Migration step 1 moves the seven `src/Foundation/UI/` files to `IronGrind.Client`, which may reference `Foundation` only.
- Those files reference `LevelingService` about 40 times (`TryApplyRespec`, `RecomputeDerivedStats`, `RegisterPlayerEntity`, `GetLevelTierMultiplier`, `RestoreLevelingState`, …), `ILevelingEventBroadcaster`, `LevelUpEventArgs`, and the `CharacterStats` class by fully qualified name (7 places).
- By Decision 3 rule 2, `LevelingService` is `ServerLogic`. Step 1 compiles because both are still in `Foundation`; the Leveling and Character Stats move stories cannot complete.
- The ADR expects splits only in Networking. It needs to state what the client reads: a `Foundation` read model or interface fed by wire messages, or an explicit classification of `CharacterStats` and the leveling formulas the respec preview uses. ADR-005 and ADR-008 do not cover this.

### B3 — Decision 6 check 1 contradicts Decisions 1 and 3
- Check 1 asserts that every type in a namespace on the server-only list is defined in `IronGrind.ServerLogic`. Decision 1 says namespaces do not change when a file moves.
- Shared ids and wire enums live in system namespaces today:
  - `IronGrind.CharacterStats`: `EntityID`, `ItemID`, `StatID`
  - `IronGrind.Currency`: `CharacterID` (used by 12 Networking files), `GoldTransactionReason` (8 Networking files)
- Decision 3 rule 3 keeps these in `Foundation`, so the namespace assertion fails at the Currency move. Story 006 is unaffected: all five `IronGrind.DamageCalculation` types go to `ServerLogic`.
- The test's default is permissive (a server type in an unlisted namespace placed in `Foundation` passes), while Decision 3 rule 4 makes server-only the default.
- Fix: assert per type, and invert it — an allow-list of what may be in `Foundation`, everything else in a system namespace must be in `ServerLogic` or `Client`.

---

## Concerns — amend, not blocking

| # | Item | Detail |
|---|---|---|
| C1 | Three GDDs specify a different constraint | `enemy-ai.md:27`, `hit-detection.md:33`, `navigation-pathfinding.md:22`: `defineConstraints: ["UNITY_SERVER"]`. The ADR's `UNITY_SERVER \|\| UNITY_EDITOR` is correct (the bare form removes the assembly from the Editor and the EditMode suite). Migration step 5 corrects only `damage-calculation.md`; add these three and `currency-system.md` Rule 10 ("platform constraints"). |
| C2 | Navigation is missing | `navigation-pathfinding.md:22` places Navigation in `ServerLogic.asmdef`. It is absent from GDD Requirements Addressed and from Migration step 4. |
| C3 | Server-only DLLs (ADR-006) | Decision 1 lets `ServerLogic` reference `IronGrind.Foundation` only, and check 1 asserts the reference list exactly — no place for Npgsql and Dapper. ADR-006's `link.xml` `preserve="all"` is project-wide and would keep both in the iOS client if the DLLs are in that build. No DLL or `link.xml` is on disk yet. |
| C4 | Decision 4 vs ADR-004 / ADR-010 | ADR-004 Decision 4 sends gameplay messages over `CustomMessagingManager`, not RPCs or `NetworkVariable`; ADR-012 describes only `NetworkBehaviour` shells and does not say where message handlers are registered. "Forwards server-side calls" should be "enqueues" (ADR-010 Decision 5). The dispatcher decision (2026-10-07 P1) is still open. |
| C5 | Content check is one-directional | A `Client` `MonoBehaviour` in a zone scene the server loads (ADR-009) is a missing script on the server. Check 3 covers the client build only. |
| C6 | Depends On is incomplete | Lists ADR-007. Decision 4 relies on ADR-004 and ADR-010. Both Accepted; no ordering problem. |
| C7 | "Foundation" names two things | The shared assembly, and the Foundation layer of the control manifest (persistence, networking substrate), whose code will mostly live in `ServerLogic`. One sentence in the ADR. |
| C8 | Stale number | The 2026-10-07 review and the traceability index call the inbound-dispatch decision "ADR-012"; it is now ADR-013. |

---

## Cross-ADR Conflicts

None new. C3 and C4 are gaps in ADR-012 against ADR-006 and ADR-004, resolvable inside ADR-012. C1 and C2 of 2026-10-07 are unchanged.

## ADR Dependency Order

No cycle. ADR-012 depends on ADR-007 (Accepted) and, in fact, ADR-004 and ADR-010 (Accepted).

```
Level 3:  ADR-009  Scene/Zone-Load Management     → ADR-002, ADR-004, ADR-007
          ADR-012  Server/Client Assembly Boundary → ADR-007 (+ ADR-004, ADR-010)   [Proposed]
Level 4:  ADR-011  Async Persistence in the Tick  → ADR-006, ADR-007, ADR-010
```

Stories that reference ADR-012 (Damage Calculation Story 006, Currency Group G) stay Blocked while it is Proposed.

---

## Engine Compatibility

Engine: Unity 6.3 LTS (6000.3). ADRs with an Engine Compatibility section: 12 / 12. No deprecated API referenced. No stale version reference.

| ADR-012 claim | Unity 6.3 manual, 2026-10-08 |
|---|---|
| Define constraints support `!` and `\|\|`; every entry must be met; built-in symbols allowed | Confirmed (Assembly Definition Inspector reference) |
| `UNITY_SERVER` is defined for Dedicated Server builds; `-standaloneBuildSubtarget Server` | Confirmed (Build for Dedicated Server) |
| No Dedicated Server name in the asmdef platform list | Consistent — none listed; valid names come from `CompilationPipeline.GetAssemblyDefinitionPlatforms` |
| `UNITY_SERVER` in the Editor under a server profile | Not stated; the constraints are correct either way |
| `IPostBuildPlayerScriptDLLs` as the scan hook; `[MovedFrom]` for `[SerializeReference]`; Linux IL2CPP Dedicated Server build | Not confirmed — already under Verification Required |

`docs/engine-reference/unity/` has no page on assembly definitions or Dedicated Server builds.

## Claims Checked Against the Repository

Confirmed: seven UI files; 102 Networking files; five Damage Calculation files with no inbound reference; no `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` in `src/`; `[SerializeReference]` only on `EquipmentData`, `ConsumableData`, `ScrollData`; `Assets/Editor/HudBootstrap.cs` is the only script under `Assets/` using project code; NGO not in `Packages/manifest.json`; ADR-007 Accepted.

Inaccurate: "No … scene or prefab references any type in `src/` today, except the seven UI files" — 38 assets reference `ItemDefinition` (it stays in `Foundation`, so harmless), and no asset references `LevelingHudController`. The file count is 234, not 233.

## GDD Revision Flags

No GDD assumption conflicts with verified engine behaviour. Wording to correct once ADR-012 is Accepted (not a status change): `damage-calculation.md` Rule 1, `currency-system.md` Rule 10, `enemy-ai.md:27`, `hit-detection.md:33`, `navigation-pathfinding.md:22`.

## Architecture Document Coverage

`architecture.md` is still at 8 ADRs (2026-10-07 finding). It has no assembly layout; add it with the refresh.

---

## Verdict: CONCERNS

Required before ADR-012 is Accepted:

1. Rewrite Migration step 2 against the real reference graph (B1).
2. Decide what `IronGrind.Client` reads in place of `LevelingService` and `CharacterStats` (B2).
3. Respecify the boundary test per type, with a `Foundation` allow-list (B3).
4. Fold in C1–C7.

Then a lean re-review, acceptance, `/create-control-manifest update`, and the Story 006 rewrite.

Carried forward from 2026-10-07, unchanged: P1 (inbound dispatch, now ADR-013), C1, C2, P2–P5, G1, G2, `architecture.md` refresh, empty TR registry.
