# ADR-012: Server/Client Assembly Boundary

## Status
Accepted (2026-10-08)

## Date
2026-10-08. Amended 2026-10-08 after `architecture-review-2026-10-08.md` (CONCERNS): B1–B3 and C1–C7 — Decision 1 (server-only DLLs), Decision 4 (custom messages), Decision 6 (checks 1 and 3), new Decision 7 (what the client reads), Migration Plan steps 2, 4 and 5. Amended again 2026-10-08 after `architecture-review-2026-10-08-rereview.md` (no blocker): N1–N5 — Decision 6 (Item Database types on the shared allow-list; `src/DevHarness/` in check 3), Decision 7 (class data), Migration Plan steps 2 and 5, GDD Requirements Addressed, Risks. Accepted the same day. Corrected 2026-10-08 (no decision change): Decision 7 "When" — the step 1 UI move needs `LevelingService.GetLevelTierMultiplier` made `public`.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Core / Scripting (assembly definitions, build configuration) |
| **Knowledge Risk** | HIGH — Unity 6.x is past the LLM training cutoff, and `docs/engine-reference/unity/` has no page on assembly definitions or Dedicated Server builds. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `modules/networking.md` (nothing on this topic). Unity 6.3 manual, read 2026-10-08: [Assembly Definition Inspector reference](https://docs.unity3d.com/6000.3/Documentation/Manual/class-AssemblyDefinitionImporter.html), [Assembly Definition file format](https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definition-file-format.html), [Build for Dedicated Server](https://docs.unity3d.com/6000.3/Documentation/Manual/dedicated-server-build.html). |
| **Post-Cutoff APIs Used** | None in code. Build configuration only: asmdef `defineConstraints` with the `\|\|` operator; the `UNITY_SERVER` scripting symbol; the Dedicated Server build subtarget (`-standaloneBuildSubtarget Server`). |
| **Verification Required** | (1) A client (iOS or desktop standalone, non-server) player build contains no `IronGrind.ServerLogic` assembly. (2) A Linux Dedicated Server IL2CPP build contains it and runs; the Linux Dedicated Server module and the Linux IL2CPP toolchain are installed on the build machine. (3) Whether `UNITY_SERVER` is defined in the Editor while a Dedicated Server build profile is active — the constraints below are written to be correct either way. (4) Whether the asmdef platform list offers a Dedicated Server entry — not relied on. (5) The mechanism the client-binary scan uses to list the player's managed assemblies and types (Decision 6, check 2): `IPostBuildPlayerScriptDLLs` and the output file locations are from the engine specialist's memory of Unity 2021–6.0, not from the 6.3 manual. (6) `[MovedFrom]` behaviour for `[SerializeReference]` data when a type changes assembly (Migration Plan checklist) — same caveat. (7) How a precompiled DLL (Npgsql, Dapper — ADR-006) is kept out of a client player: a define constraint on the plugin importer or another import setting; not checked against the 6.3 manual. Also whether a `link.xml` entry for an assembly that is absent from the build is ignored (Decision 1). (8) Whether a `MonoBehaviour` in an assembly constrained to `UNITY_EDITOR` (not an Editor-platform assembly) can be attached to a GameObject in a scene (Decision 7, manual harness). |
| **Specialist Validation** | `unity-specialist`, 2026-10-08: no blocking issue. The specialist had no documentation access in that session; its points are recorded here as from memory and are listed under Verification Required where the decision leans on them. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-007 (hosting backend — the server is a Unity 6.3 IL2CPP Linux headless build); ADR-004 (NGO — gameplay messages over `CustomMessagingManager`, Decision 4 here); ADR-010 (event messaging — Tier 1 injection and enqueue-on-receive, Decision 4 here). All Accepted. |
| **Enables** | The server RNG injection ADR (OQ-DC-2) can name the assembly its types live in. ADR-013 (inbound request dispatch and tick order) can name where handlers are registered. |
| **Blocks** | Damage Calculation Story 006 (Server Assembly Isolation Scan); Currency System Group G (Server Assembly Isolation) — unblocked by acceptance, but Currency is eighth in the move order (Migration Plan step 2); every later per-system move story. |
| **Ordering Note** | ADR-004 (NGO): no `NetworkBehaviour` exists in `src/` yet. Decision 4 fixes where they go before the first one is written. |

## Context

### Problem Statement
All 234 source files compile into one assembly, `IronGrind.Foundation` (`src/Foundation/IronGrind.Foundation.asmdef`): server-authoritative logic (damage formulas, loot rolls, enhancement odds, currency mutation), types both sides need (ids, wire messages, item definitions) and client UI code. A client build therefore ships every formula, drop table reader and validation rule.

Four GDDs require the opposite. `damage-calculation.md` Core Rule 1: the resolver and its types "reside in a `ServerLogic.asmdef` assembly excluded from the client build via Unity platform constraints. Using `[Server]` attribute alone is insufficient — it ships code to the client binary. The ADR specifying the complete server/client assembly boundary must be authored before implementation begins." `hit-detection.md` and `enemy-ai.md` place those systems in `ServerLogic.asmdef`. `currency-system.md` Group G asks for the same isolation. Damage Calculation Stories 001–005 and Currency were built in `IronGrind.Foundation` by explicit user decision, each recording the missing ADR as a deviation.

### Constraints
- Solo developer: one Unity project, one build pipeline per target. No second project.
- The EditMode suite (1995 tests) runs in the Editor with any build target active, and in batch mode. Server-only code must stay compilable and testable there.
- The server is an IL2CPP Linux headless build (ADR-007); the client is IL2CPP iOS. IL2CPP managed stripping removes unused code, but "unused" is decided by the linker, not by design, so it is not a guarantee.
- `src/` and `tests/` are local Unity packages (`com.irongrind.src`, `com.irongrind.tests`); assemblies are defined by `.asmdef` files inside them.
- No `NetworkBehaviour` exists in `src/` today. 38 assets reference `ItemDefinition`, which stays in `Foundation`. No saved asset references a UI script (`LevelingHudController` and the manual harness are added to a scene object by the Editor menu commands in `Assets/Editor/HudBootstrap.cs`).
- "Foundation" names two things in this project. In this ADR it always means the shared assembly `IronGrind.Foundation`. The Foundation *layer* of the control manifest (persistence, networking substrate) is a layering term; most of that layer's code is server-only and will live in `IronGrind.ServerLogic`.

### Requirements
- A server-only type cannot be compiled into a client player, by construction, not by convention.
- Client-side code cannot reference a server-only type: the mistake is a compile error.
- The rule for "which assembly does this type go in" is decidable by a programmer without asking.
- The boundary is checked automatically, both without a build (every test run) and against a real client build.
- Existing code moves system by system, with the suite green after each move.

## Decision

### Decision 1 — Three assemblies

| Assembly | Folder | In client build | In server build | May reference |
|---|---|---|---|---|
| `IronGrind.Foundation` (exists) | `src/Foundation/` | yes | yes | Unity engine assemblies and approved packages only |
| `IronGrind.ServerLogic` (new) | `src/ServerLogic/` | **no** | yes | `IronGrind.Foundation`; server-only precompiled DLLs on a named list (below) |
| `IronGrind.Client` (new) | `src/Client/` | yes | **no** | `IronGrind.Foundation` |

"Approved packages" for `Foundation` includes `Unity.Netcode.Runtime` once ADR-004's package is installed (it is not in `Packages/manifest.json` today): NGO's IL post-processing only rewrites assemblies that reference it, and the `NetworkBehaviour` shells of Decision 4 live there. `ServerLogic` references it only if it declares NGO types.

**Server-only DLLs.** `IronGrind.ServerLogic` is the only assembly that may reference Npgsql and Dapper (ADR-006). The list of such DLLs is short and named: today those two, added to the asmdef's precompiled references when ADR-006's code is written. Each DLL must itself be absent from a client player, by the same condition as the assembly (`UNITY_SERVER || UNITY_EDITOR`); the import setting that does this is Verification Required 7. ADR-006's `link.xml` (`preserve="all"` for both) is project-wide: it must not be what brings the DLLs into a client build, so the client-binary scan (Decision 6, check 2) also fails if `Npgsql` or `Dapper` is in the client's assembly list. No DLL and no `link.xml` is on disk yet.

`IronGrind.Foundation` keeps its name and becomes the shared assembly. `IronGrind.ServerLogic` is the assembly the GDDs call `ServerLogic.asmdef`. Neither `ServerLogic` nor `Client` references the other; `Foundation` references neither. Namespaces do not change when a file moves (`IronGrind.DamageCalculation` stays `IronGrind.DamageCalculation`): a move is a folder and assembly change only.

### Decision 2 — Exclusion by define constraint

```json
// src/ServerLogic/IronGrind.ServerLogic.asmdef
{
    "name": "IronGrind.ServerLogic",
    "references": [ "IronGrind.Foundation" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_SERVER || UNITY_EDITOR" ]
}

// src/Client/IronGrind.Client.asmdef
{
    "name": "IronGrind.Client",
    "references": [ "IronGrind.Foundation" ],
    "autoReferenced": false,
    "defineConstraints": [ "!UNITY_SERVER || UNITY_EDITOR" ]
}
```

- A client player is compiled without `UNITY_SERVER` and without `UNITY_EDITOR`, so `IronGrind.ServerLogic` is not compiled and cannot be in the binary. A Dedicated Server player is compiled with `UNITY_SERVER`, so it has `ServerLogic` and not `Client`.
- In the Editor `UNITY_EDITOR` is always defined, so both assemblies compile whatever build profile is active, and the test assembly can reference both.
- `autoReferenced: false` keeps `Assembly-CSharp` (anything under `Assets/`) from seeing either assembly by accident. `IronGrind.Foundation` also moves to `autoReferenced: false` as part of the first move story; anything under `Assets/` that needs project code gets its own asmdef with explicit references.
- The platform include/exclude lists are not used for this boundary: the manual lists no Dedicated Server platform name for asmdefs, and the server and a desktop client can share a platform.
- The constraint is `UNITY_SERVER || UNITY_EDITOR`, not bare `UNITY_SERVER` as `enemy-ai.md`, `hit-detection.md` and `navigation-pathfinding.md` write it: the bare form removes the assembly from the Editor, and with it the EditMode suite and every Editor tool that uses server code. Those GDDs are corrected in Migration Plan step 5.
- `#if UNITY_SERVER` inside files is not the boundary. It remains legal for small differences inside `Foundation`, but a type that must not ship to the client is moved, not wrapped.

### Decision 3 — Classification rule

Ask in this order; the first "yes" decides.

1. **Does the type reference UI Toolkit, UGUI, rendering, audio, input, or exist only to present?** → `IronGrind.Client`.
2. **Does it compute, validate or mutate authoritative game state, or hold a formula, a probability, a tuning value, a roll, or a rule the player must not be able to read or replace?** → `IronGrind.ServerLogic`. This covers resolvers and services (`DamageCalculator`, `CurrencySystem`, `EnhancementService`, `LootAuctionService`, `GroundItemService`), their configs (`DamageCalculationConfig`, `EnhancementConfig`), server state machines, the tick loop, persistence coordination, RPC guards and server-side validation.
3. **Do both sides need it to talk to each other or to describe the same thing?** → `IronGrind.Foundation`. This covers ids (`EntityID`, `ItemID`, `CharacterID`), enums and readonly structs that appear in wire messages or events the client receives, wire message schemas, and static definitions the client displays (`ItemDefinition`, `EquipmentData`).
4. **Otherwise** → `IronGrind.ServerLogic`. Server-only is the default for game logic; a type earns a place in `Foundation` by having a named client consumer.

Result types follow their producer unless the client receives them. `DamageResult` and `DamageContext` are `ServerLogic` (the GDD names them); what the client gets is the wire message built from them.

Rule 2 has one narrow exception, set out in Decision 7: a formula whose result the game shows the player anyway, and which the client must evaluate to draw a screen, is `Foundation`, with the client consumer named.

An interface lives with its consumer. An interface that server code calls and server code implements (`IEquippedWeaponQuery`, `IEnhancementBonusProvider`) is `ServerLogic`. An interface declared so that code in `Foundation` can call into server logic without referencing it (Decision 4) is `Foundation`, and its signature must not carry a server-only type.

### Decision 4 — Networked behaviours and assets

- A `NetworkBehaviour` that sits on a prefab clients also instantiate must exist in both builds, so it lives in `IronGrind.Foundation` and stays a thin shell: it declares what NGO needs on the object and hands server-side calls to an interface declared in `Foundation` and implemented in `ServerLogic`. The implementation is supplied by the server composition root (constructor or `Initialize` injection, ADR-010 Tier 1); on the client the interface is not supplied.
- Gameplay messages do not travel as RPCs or `NetworkVariable`s: ADR-004 Decision 4 sends them through `CustomMessagingManager`. The message schemas and the code that reads and writes them are `Foundation` (both sides serialize them). Server-side handlers for inbound messages are registered by the server composition root, in `ServerLogic`; client-side handlers by the client composition root, in `Client`. A server handler does not execute the request: it validates the envelope and enqueues it for the tick (ADR-010 Decision 5). Which component dispatches the queue and in what tick order is ADR-013, not this ADR.
- Where an RPC is used at all, its body ships in the client binary whatever its target: only its execution is server-side. It must contain nothing but enqueueing through a `Foundation` interface — never a rule, a formula or a constant. The same holds for any message-handling code placed in `Foundation`.
- A server-only `NetworkBehaviour` must not be added to a prefab the client also instantiates: NGO addresses behaviours by their index on the `NetworkObject`, so both sides need the same component list.
- A `MonoBehaviour`, `ScriptableObject` or `[SerializeReference]` type in `IronGrind.ServerLogic` must not appear in any scene, prefab, Addressables group or asset included in a client build. The Editor compiles `ServerLogic`, so such a reference serializes without error and becomes a missing script at client runtime. Enforced by Decision 6, check 3. Server-only components are created in code by the server composition root, or live in server-only scenes, and those scenes are not in the client build profile's scene list.
- The reverse also holds: a `MonoBehaviour` or `ScriptableObject` in `IronGrind.Client` must not appear in a scene or asset the Dedicated Server build loads (zone scenes, ADR-009), where it would be the missing script. Presentation components are added by the client composition root or live in client-only scenes.
- The server composition root (the code that constructs services and wires them) and its bootstrap component are in `IronGrind.ServerLogic`. The client's are in `IronGrind.Client`.
- Server-only *data* (drop tables, tuning assets) is a content question, not an assembly question, and is out of scope here: see Risks.

### Decision 5 — Tests and internals

- The existing test assembly `IronGrind.Foundation.EditModeTests` adds references to `IronGrind.ServerLogic` and `IronGrind.Client`. It is Editor-only, where both compile. One test assembly is kept; it is not split by this ADR.
- Each new assembly has its own `AssemblyInfo.cs` with `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")`.
- `InternalsVisibleTo` between production assemblies is forbidden. If `ServerLogic` needs a member of a `Foundation` type, that member becomes public, or the type is in the wrong assembly.

### Decision 6 — Automated checks

Three checks, all required.

1. **Boundary test (no build needed, runs with the EditMode suite).** A test in `tests/EditMode/Architecture/` that:
   - reads the three `.asmdef` files and asserts the names, the two define-constraint strings exactly, `autoReferenced: false`, and that the reference lists match Decision 1 (for `ServerLogic`: `IronGrind.Foundation`, plus precompiled references drawn only from the named server-only DLL list);
   - by reflection, asserts **per type** that every type defined in the assembly `IronGrind.Foundation` is on one of two lists kept in the test:
     - the **shared allow-list** — types that belong in `Foundation` by Decision 3 rule 3 or Decision 7. It starts with `EntityID`, `ItemID`, `StatID` (namespace `IronGrind.CharacterStats`), `CharacterID`, `GoldTransactionReason` (namespace `IronGrind.Currency`) and every type in `src/Foundation/ItemDatabase/`: the definitions (`ItemDefinition`, `EquipmentData`, `ConsumableData`, `ScrollData`, `StatModifierEntry` and the enums they use), the lookup (`IItemDatabase`, `ItemDatabase`), the validator (`ItemDefinitionValidator` and its result types) and the seed (`ItemDatabaseSeeder`, `MvpItemRecordData`). Item Database is static definition data that both sides load to describe the same items, so the whole folder is shared and has no move story; its client consumers are the item tooltip and inventory screens. An entry is added only with its client consumer named in a comment beside it;
     - the **not-yet-moved list** — every other type in `Foundation` on the day the test is written, grouped by system. It only shrinks: each move story deletes its system's entries, and the test fails if an entry names a type that is no longer in `Foundation`. When it is empty it is removed;
   - asserts that no type in `IronGrind.Foundation` or `IronGrind.Client` has a field, property, parameter or base type from `IronGrind.ServerLogic`.
   This is the gate that fails on the day a file is put in the wrong folder: a new type in `Foundation` is on neither list, so the default is "not allowed there", which is Decision 3 rule 4. The assertion is by type and not by namespace because namespaces do not follow assemblies (Decision 1): shared ids and wire enums sit in system namespaces whose services are server-only. Compiler-generated and nested types are matched through their declaring type. The "no `ServerLogic` type in a `Foundation` or `Client` signature" assertion repeats what the compiler already enforces through the asmdef references and is kept as a cheap guard. The `.asmdef` files are located through `CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName` and the package's resolved path, never a hard-coded path: a `file:` package has no real `Packages/com.irongrind.src/` folder on disk.
2. **Client-binary scan (needs a client build; satisfies AC-DC-I-01).** A build-pipeline step under `tools/` that, for a client player build, lists the managed assemblies that went into the player and the type names in them, and fails if `IronGrind.ServerLogic` or a server-only DLL (`Npgsql`, `Dapper`) is present or if any type name on the forbidden list (`DamageCalculator`, `DamageResult`, `DamageContext`, and each moved system's names) appears. The step must be shown to fail when a forbidden type is deliberately placed in `Foundation`. Intended mechanism, to be confirmed on a real build (Verification Required 5): an `IPostBuildPlayerScriptDLLs` callback, which sees the player's script DLLs after compilation and before IL2CPP conversion, reads the assembly list and the type names with a metadata reader; a secondary scan of the built output (`ScriptingAssemblies.json`, `global-metadata.dat`) covers what survived stripping. Not to be used: `BuildReport.packedAssets` (assets, not assemblies), `CompilationPipeline.GetAssemblies` (reflects the Editor's active target, not the build in progress), and the `Library/Bee` intermediate folders (internal layout).
3. **Content check, both directions (needs a build).** A build-time step (`IPreprocessBuildWithReport` plus a dependency scan of the active build profile's scenes and Addressables groups) that fails a client build if any asset going into it depends on a script whose file is under `src/ServerLogic/`, and fails a Dedicated Server build if any asset going into it depends on a script under `src/Client/`. Both halves also fail on a script under `src/DevHarness/` (Decision 7): that assembly is in no player, and `HudBootstrap`'s menu command adds the harness to a scene the user then saves. The client half is written with the first story that puts a `MonoBehaviour` or `ScriptableObject` in `ServerLogic`; until then that assembly holds plain C# only. The server half is written with the first server build pipeline; `src/Client/` holds `MonoBehaviour`s from Story 006 on, and until that pipeline exists the rule in Decision 4 is kept by review.

Until a client build exists in the pipeline, check 1 is the automated gate and AC-DC-I-01 stays open.

### Decision 7 — What the client reads

The HUD is the first client code, and today it calls server types directly: `LevelingService` (about 40 references in the seven UI files) and the `CharacterStats` class. Both are `ServerLogic` by Decision 3 rule 2. `IronGrind.Client` cannot reference them, so the client reads a model that lives in `Foundation`.

- **Read-only views.** `Foundation` declares read-only interfaces for the local player's state — stats (current and effective values, change notification) and leveling (level, experience, held free points). `Client` presenters depend on these and on nothing else for state. On a client the implementation is a mirror filled from the state and event messages the server sends (`networking-wire-protocol.md`; ADR-010 for events); it is `Client` code. Names are fixed in the story that introduces them (working names: `ILocalPlayerStatsView`, `ILocalPlayerLevelingView`).
- **Requests.** An action the player takes on a screen (respec) goes out through a request interface declared in `Foundation` (working name `IRespecRequestSender`) whose client implementation sends the message. The client never calls `TryApplyRespec`, `RegisterPlayerEntity` or `AttachCharacterStats`; the server validates and applies, and the result comes back as state.
- **Display formulas.** A formula the client must evaluate to draw a screen, and whose result the game shows the player anyway, lives in a static class in `Foundation`. Today that is the experience threshold for a level, the level tier multiplier, and the derived-stat preview on the respec screen (`LevelingFormulaPreview` already wraps the last one for the UI). There is one copy: `ServerLogic` calls the `Foundation` class and does not keep its own. Each formula there has its client consumer named, as for any `Foundation` type. The class data those formulas read is shared for the same reason: `ClassDefinition` (auto-allocation and free points per level, both shown on the respec screen), `IClassRegistry` and the stand-in `ClassRegistry` are `Foundation`, on the shared allow-list, with `RespecScreenPresenter` and `LevelingFormulaPreview` as consumers. They are per-class definitions both sides load, like item definitions; the Class System epic takes them over when it is written. A formula that decides an outcome the player does not see computed — damage, crit, drop rolls, enhancement odds — is never in this class.
- **Events the client receives.** `LevelUpEventArgs` and `ILevelingEventBroadcaster` are `Foundation` by Decision 3 rule 3: the level-up overlay consumes them. `NotifyExperienceCrossedThreshold` is a server call and goes with `LevelingService`.
- **Manual harness.** `LevelingHudManualTestHarness` constructs a real `LevelingService` and `CharacterStats` to drive the HUD without a server. It cannot be `Client`. It moves to its own assembly under `src/DevHarness/` (`IronGrind.DevHarness`, define constraint `UNITY_EDITOR`, references all three, in no player build). It stops being available in development device builds; a device harness, if wanted, is driven through the read-only views with fake data. `HudBootstrap`'s Editor asmdef then references `IronGrind.DevHarness` as well, since its menu command adds the harness. Whether a `MonoBehaviour` in such an assembly can be added to a scene object is Verification Required 8; if it cannot, the harness becomes an Editor window or a play-mode Editor script that drives the views.
- **When.** The views, the request interface, the display-formula class and the harness move are part of the Leveling move story; the stats view is completed in the Character Stats move story. Migration step 1 moves the seven UI files unchanged — `LevelingService` and `CharacterStats` are still in `Foundation` then. One `Foundation` member has to be widened for them to compile from another assembly: `LevelingService.GetLevelTierMultiplier` is `internal static` and is called by `LevelUpOverlayPresenter` and `RespecScreenPresenter`; it becomes `public` in step 1 (Decision 5), and moves to the display-formula class with the Leveling move story. The same pattern applies to every later screen (inventory, enhancement, loot): a view and a request interface in `Foundation`, no service reference in `Client`.

### Architecture Diagram

```
                 ┌──────────────────────────────┐
                 │  IronGrind.Foundation        │   client build: yes
                 │  ids, wire types, enums,     │   server build: yes
                 │  item definitions,           │
                 │  NetworkBehaviour shells,    │
                 │  bridging interfaces,        │
                 │  client views, display       │
                 │  formulas (Decision 7)       │
                 └──────────────▲───────▲───────┘
                     references │       │ references
        ┌───────────────────────┴──┐  ┌─┴────────────────────────┐
        │ IronGrind.ServerLogic    │  │ IronGrind.Client         │
        │ UNITY_SERVER||UNITY_EDITOR│  │ !UNITY_SERVER||UNITY_EDITOR│
        │ formulas, services,      │  │ UI Toolkit, presenters,  │
        │ configs, tick loop,      │  │ client composition root  │
        │ server composition root  │  │                          │
        └───────────▲──────────────┘  └────────────▲─────────────┘
                    │        no reference          │
                    └──────────── ✕ ───────────────┘
                    both referenced by IronGrind.Foundation.EditModeTests (Editor only)
```

### Key Interfaces
The contracts are the two `.asmdef` files in Decision 2, the reference table in Decision 1, and the classification rule in Decision 3. The only runtime API this ADR calls for is the client read model of Decision 7 (read-only views, request interfaces, display-formula class); their signatures are written in the Leveling move story.

## Alternatives Considered

### Alternative 1: One assembly, `#if UNITY_SERVER || UNITY_EDITOR` around server files
- **Description**: Keep `IronGrind.Foundation`; wrap each server-only file in a preprocessor block.
- **Pros**: No new assemblies, no file moves, no reference changes.
- **Cons**: The boundary is per file and by discipline: one missing `#if` ships a formula. Client code in the same assembly can reference server types freely in the Editor and only fails at client build time. Nothing to scan for at the assembly level.
- **Rejection Reason**: It does not make the mistake a compile error, and the GDD asks for an assembly.

### Alternative 2: Rely on IL2CPP managed stripping or the `[Server]` attribute
- **Description**: Leave everything together; trust the linker to drop code the client never calls, and NGO attributes to block execution.
- **Pros**: Zero work.
- **Cons**: Stripping keeps anything reachable, and reachability is not a design property; `[Server]` blocks execution, not shipping.
- **Rejection Reason**: `damage-calculation.md` Core Rule 1 rejects it explicitly.

### Alternative 3: Separate server project or precompiled server DLL
- **Description**: Build server logic outside the client project and consume shared types as a package.
- **Pros**: The strongest isolation; the client project never sees server source.
- **Cons**: Two projects or a DLL build step, duplicated settings, slower iteration, and the EditMode suite splits in two.
- **Rejection Reason**: Cost out of proportion for a solo project when a define-constrained assembly gives the same guarantee for the binary.

### Alternative 4: Platform include/exclude lists on the asmdef
- **Description**: Exclude the server assembly from client platforms by platform name.
- **Pros**: The wording the GDD uses ("platform constraints").
- **Cons**: The Unity 6.3 manual lists no Dedicated Server platform name for asmdefs, and the server and a desktop client build share a platform (Linux, Windows), so a platform list cannot tell them apart.
- **Rejection Reason**: Not verifiable, and wrong for any desktop client. The define constraint expresses the real condition.

### Alternative 5: Multiplayer Roles / content selection (Dedicated Server package)
- **Description**: Unity's first-party tooling tags GameObjects and components as client or server and strips them per build role.
- **Pros**: Handles scene and prefab content, which an assembly boundary does not.
- **Cons**: It works on serialized content; it does not stop code from being compiled into a player. Its state in Unity 6.3 is not confirmed from the manual.
- **Rejection Reason**: Not a substitute for the code boundary. To be evaluated as a complement in the follow-up decision on server-only content.

## Consequences

### Positive
- A client build cannot contain server logic: the assembly is not compiled for it.
- Client code that reaches for a server type fails to compile in the Editor, immediately.
- The server build drops client UI code the same way.
- The rule is checked on every test run, not only at build time.

### Negative
- Every existing system needs a move story; until each is done, that system still ships in the client.
- `Foundation` types lose access to members they reached as same-assembly `internal`; some members become public.
- Files that mix server logic and shared types have to be split before they can move: `Networking` (102 files, classified file by file), and also Leveling, Character Stats and the UI, where the HUD is rewritten against the read model of Decision 7.
- The display formulas of Decision 7 (experience threshold, tier multiplier, derived-stat preview) ship in the client. Accepted: the player sees their results.
- The manual HUD harness no longer runs in development device builds (Decision 7).
- Currency Group G, which asked for this ADR, is served late: Currency is eighth of nine in the move order.
- One more place to look for a file: folder now encodes build membership.

### Risks
- **A `ServerLogic` script referenced by a shipped asset** shows up as a missing script on the client, with no compile error. Mitigation: Decision 4 and Decision 6, check 3.
- **`#if UNITY_SERVER` inside `Foundation`** would make EditMode results depend on the active build profile, if the Editor defines the symbol under a server profile. Mitigation: no such block in code under test; if one becomes necessary, the suite must pass under both a client and a Dedicated Server profile.
- **A type that changes assembly breaks name-based references.** `[SerializeReference]` data stores the assembly name, and so does any persistence or lookup using assembly-qualified names. Mitigation: the per-move checklist in the Migration Plan. Today the only `[SerializeReference]` types are `EquipmentData`, `ConsumableData` and `ScrollData`, which stay in `Foundation`, and `src/` has no `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use.
- **A future PlayMode test assembly** that references `ServerLogic` fails to compile for a client player. Mitigation: keep it Editor-only or give it the same define constraint.
- **Server-only data still ships.** A drop table stored as an asset in the client's content is readable even if the code that rolls it is not. Mitigation: out of scope here; record a follow-up decision on server-only content (Addressables groups excluded from the client) before Loot Table or Enemy AI data is authored as assets.
- **`UNITY_EDITOR` in the constraint** means the Editor never proves the client build compiles without `ServerLogic`. Mitigation: the boundary test's reflection check forbids `Foundation` and `Client` from referencing `ServerLogic` types; the first real client build is a validation gate.
- **Wrong classification of a result or config type** that a client turns out to need. Mitigation: move it to `Foundation` with a named consumer (Decision 3, rule 3); the boundary test's shared allow-list makes the change deliberate.
- **The not-yet-moved list hides a misplaced type** until its system moves: a type on it is in `Foundation` and passes. Mitigation: the list is written once, only shrinks, and a new type is on neither list.
- **The display-formula class grows into a second home for rules.** Mitigation: each entry names its client consumer and must be a value the player is shown; the class is on the shared allow-list type by type, so an addition is visible in review.
- **Engine behaviour not confirmed from the manual** (Verification Required 3–8). Mitigation: the constraints are correct whether or not `UNITY_SERVER` is defined in the Editor; the binary scan is fixed on a real build; items 6–8 are each checked in the first story that exercises them (a `[SerializeReference]` move, the first Npgsql import, the harness move).

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| damage-calculation.md | Core Rule 1: resolver and its types in a `ServerLogic.asmdef` assembly excluded from the client build; `[Server]` alone is insufficient; the ADR must exist before implementation | Decision 1 and 2 define `IronGrind.ServerLogic` and exclude it by define constraint; Decision 3 places `DamageCalculator`, `DamageResult`, `DamageContext` there |
| damage-calculation.md | AC-DC-I-01: automated inspection of the client assembly finds no Damage Calculation type | Decision 6, check 2 (client-binary scan), with check 1 as the per-test-run gate |
| currency-system.md | Group G: server assembly isolation (AC-CS-G-01) | Same assembly and rule; `CurrencySystem` is `ServerLogic` by Decision 3 rule 2. AC-CS-G-01 is met by Decision 6 check 1 (not in `Foundation`) and check 2 (not in a client build) |
| enemy-ai.md | AC-AI-21: `ServerLogic` references no client-only assembly | Decision 1 reference table, asserted by Decision 6 check 1 |
| hit-detection.md | Hit Detection is called server-side only (assembly: `ServerLogic.asmdef`) | The system is authored directly in `src/ServerLogic/` |
| enemy-ai.md | Enemy AI runs server-side only (`ServerLogic.asmdef`) | The system is authored directly in `src/ServerLogic/` |
| navigation-pathfinding.md | Navigation/Pathfinding executes within `ServerLogic.asmdef` (line 22) | The system is authored directly in `src/ServerLogic/` |
| enemy-ai.md, hit-detection.md, navigation-pathfinding.md | `defineConstraints: ["UNITY_SERVER"]` | Decision 2 uses `UNITY_SERVER \|\| UNITY_EDITOR` instead, for the reason given there; GDD wording corrected in Migration Plan step 5 |
| currency-system.md | Rule 10: excluded from client builds "via Unity platform constraints" | Decision 2: define constraint, not platform list (Alternative 4) |

## Performance Implications
- **CPU**: None at runtime. Assembly boundaries do not change IL2CPP call cost in any way that matters at 20 Hz.
- **Memory**: Slightly smaller client binary (no server logic) and server binary (no UI code).
- **Load Time**: None measurable.
- **Network**: None.
- **Editor**: Three assemblies instead of one shortens recompiles after a change in `ServerLogic` or `Client`; a change in `Foundation` still recompiles all three.

## Migration Plan
1. **Damage Calculation Story 006 (first move, proves the pattern)**: create `src/ServerLogic/` with its asmdef and `AssemblyInfo.cs`; create `src/Client/` with its asmdef and move the seven `src/Foundation/UI/` files; set `autoReferenced: false` on all three, after giving every script under `Assets/` that uses project code its own asmdef with explicit references (today: `Assets/Editor/HudBootstrap.cs`, which uses `IronGrind.UI.LevelingSystem` and so needs an Editor asmdef referencing `IronGrind.Client`); add both references to the test asmdef; move `src/Foundation/DamageCalculation/` (git move, keep `.meta` files and GUIDs); write the boundary test (Decision 6, check 1). The move forces its dependencies: `DamageCalculator` takes `CharacterStats`, `IItemDatabase` and `IEnhancementBonusProvider`. `ServerLogic` may reference types still in `Foundation`, so the move compiles today; those types move with their own systems later.
2. **One move story per system.** `Foundation` cannot reference `ServerLogic`, so a system moves only after every system that uses its server types has moved: users first, the most depended-on last. Order, from the reference graph of `src/Foundation/` (review 2026-10-08):
   1. Damage Calculation (step 1; nothing references it)
   2. Loot Table
   3. Enhancement
   4. NPC Interaction (`INpcInteractionSessions` is used by Enhancement)
   5. Inventory (`IInventoryService` and inventory types are used by Enhancement and Loot Table)
   6. Leveling (with the client read model of Decision 7; the UI stops referencing `LevelingService`)
   7. Networking, server part (`ServerTickLoop`, `CommitBeforeBroadcastSequencer`, `ILootDropSink` are used by Inventory, Loot Table and NPC Interaction) — classified file by file first; wire schemas and ids stay
   8. Currency, Group G (`ICurrencyService`, `CurrencySystem`, `GoldMutationResult` are used by Inventory, Leveling, Loot Table and Networking)
   9. Character Stats (referenced by every other system; completes the stats view of Decision 7)

   Item Database is not in the list: its folder stays in `Foundation` whole (Decision 6, shared allow-list).

   Each story re-checks the graph before it starts (a grep for the system's type names outside its folder) and takes the next system in the list whose users have all moved. Each story deletes its system's entries from the boundary test's not-yet-moved list, adds what stays shared to the allow-list, and leaves the suite green. Checklist for every move: the `.cs.meta` GUIDs are preserved (move the file and its `.meta` together); any moved type that is serialized through `[SerializeReference]` or stored by name gets `[MovedFrom(true, sourceAssembly: "IronGrind.Foundation")]` and its assets are re-saved; `src/` is searched for `Type.GetType`, `AssemblyQualifiedName` and `TypeNameHandling`.
3. **Client-binary scan** (Decision 6, check 2) with the first client build pipeline; it closes AC-DC-I-01.
4. **New systems** (Hit Detection, Enemy AI, Navigation/Pathfinding, Auto-Attack Combat, Status Effects) are written in `src/ServerLogic/` from their first story.
5. After the ADR is Accepted: `/create-control-manifest update` to add the assembly rules; correct the GDD wording in one authoring pass (wording only, no status change):
   - `damage-calculation.md` Core Rule 1 — "platform constraints" → define constraint; `ServerLogic.asmdef` → `IronGrind.ServerLogic`; AC-DC-I-01 type name `DamageCalculation` → `DamageCalculator`
   - `currency-system.md` Rule 10 — "platform constraints" → define constraint; AC-CS-G-01 (line 309) — "confirmed via `UnityEditor.Compilation.CompilationPipeline` or assembly definition file inspection" → the boundary test (Decision 6, check 1) and the client-binary scan (check 2); `ServerLogic.asmdef` → `IronGrind.ServerLogic`
   - `enemy-ai.md:27`, `hit-detection.md:33`, `navigation-pathfinding.md:22` — `defineConstraints: ["UNITY_SERVER"]` → `["UNITY_SERVER || UNITY_EDITOR"]`; `ServerLogic.asmdef` → `IronGrind.ServerLogic`
   - `enemy-ai.md:478` AC-AI-21 — "no type from a client-only namespace" → no type from the `IronGrind.Client` assembly (the boundary is by assembly, not by namespace)

Until a system's move story is done, its code is still in the client build. That window is accepted; no client build is distributed before the moves are complete.

## Validation Criteria
- The boundary test exists, passes, and fails when a type that is on neither list is placed in `src/Foundation/` (shown once, by hand, in Story 006, with a `IronGrind.DamageCalculation` type).
- The EditMode suite passes after each move story, and the not-yet-moved list is shorter after each one.
- After the Leveling move story, no file under `src/Client/` names `LevelingService`; after the Character Stats move story, none names the `CharacterStats` class.
- A client player build's assembly list has no `IronGrind.ServerLogic`, `Npgsql` or `Dapper`, and the scan step fails on a deliberately misplaced type.
- A Linux Dedicated Server IL2CPP build contains `IronGrind.ServerLogic`, does not contain `IronGrind.Client`, and starts.

## Related Decisions
- ADR-004 (NGO) — `NetworkBehaviour` placement and `CustomMessagingManager` handler registration, Decision 4.
- ADR-006 (persistence) — Npgsql and Dapper are server-only DLLs referenced by `ServerLogic` alone, Decision 1.
- ADR-007 (hosting backend) — the server build this assembly ships in.
- ADR-005 (HUD framework), ADR-008 (combat UI framework) — screens built on them read the views of Decision 7, never a service.
- ADR-009 (scene/zone loading) — zone scenes the server loads must hold no `Client` script, Decision 4.
- ADR-013 (inbound request dispatch and tick order, not yet written) — owns the dispatcher that Decision 4 leaves open.
- ADR-010 (event messaging) — Tier 1 injection is how `Foundation` shells reach `ServerLogic` implementations; event argument structs the client receives are `Foundation`.
- `design/gdd/damage-calculation.md` Core Rule 1, AC-DC-I-01; `design/gdd/currency-system.md` Group G; `design/gdd/hit-detection.md`; `design/gdd/enemy-ai.md`.
- `docs/tech-debt-register.md` TD-002 (local packages and asmdef wiring).
