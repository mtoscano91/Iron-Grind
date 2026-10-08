# ADR-012: Server/Client Assembly Boundary

## Status
Proposed

## Date
2026-10-08

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3) |
| **Domain** | Core / Scripting (assembly definitions, build configuration) |
| **Knowledge Risk** | HIGH — Unity 6.x is past the LLM training cutoff, and `docs/engine-reference/unity/` has no page on assembly definitions or Dedicated Server builds. |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `breaking-changes.md`, `deprecated-apis.md`, `modules/networking.md` (nothing on this topic). Unity 6.3 manual, read 2026-10-08: [Assembly Definition Inspector reference](https://docs.unity3d.com/6000.3/Documentation/Manual/class-AssemblyDefinitionImporter.html), [Assembly Definition file format](https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definition-file-format.html), [Build for Dedicated Server](https://docs.unity3d.com/6000.3/Documentation/Manual/dedicated-server-build.html). |
| **Post-Cutoff APIs Used** | None in code. Build configuration only: asmdef `defineConstraints` with the `\|\|` operator; the `UNITY_SERVER` scripting symbol; the Dedicated Server build subtarget (`-standaloneBuildSubtarget Server`). |
| **Verification Required** | (1) A client (iOS or desktop standalone, non-server) player build contains no `IronGrind.ServerLogic` assembly. (2) A Linux Dedicated Server IL2CPP build contains it and runs; the Linux Dedicated Server module and the Linux IL2CPP toolchain are installed on the build machine. (3) Whether `UNITY_SERVER` is defined in the Editor while a Dedicated Server build profile is active — the constraints below are written to be correct either way. (4) Whether the asmdef platform list offers a Dedicated Server entry — not relied on. (5) The mechanism the client-binary scan uses to list the player's managed assemblies and types (Decision 6, check 2): `IPostBuildPlayerScriptDLLs` and the output file locations are from the engine specialist's memory of Unity 2021–6.0, not from the 6.3 manual. (6) `[MovedFrom]` behaviour for `[SerializeReference]` data when a type changes assembly (Migration Plan checklist) — same caveat. |
| **Specialist Validation** | `unity-specialist`, 2026-10-08: no blocking issue. The specialist had no documentation access in that session; its points are recorded here as from memory and are listed under Verification Required where the decision leans on them. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-007 (hosting backend — the server is a Unity 6.3 IL2CPP Linux headless build), Accepted. |
| **Enables** | The server RNG injection ADR (OQ-DC-2) can name the assembly its types live in. |
| **Blocks** | Damage Calculation Story 006 (Server Assembly Isolation Scan); Currency System Group G (Server Assembly Isolation); every later per-system move story. |
| **Ordering Note** | ADR-004 (NGO): no `NetworkBehaviour` exists in `src/` yet. Decision 4 fixes where they go before the first one is written. |

## Context

### Problem Statement
All 233 source files compile into one assembly, `IronGrind.Foundation` (`src/Foundation/IronGrind.Foundation.asmdef`): server-authoritative logic (damage formulas, loot rolls, enhancement odds, currency mutation), types both sides need (ids, wire messages, item definitions) and client UI code. A client build therefore ships every formula, drop table reader and validation rule.

Four GDDs require the opposite. `damage-calculation.md` Core Rule 1: the resolver and its types "reside in a `ServerLogic.asmdef` assembly excluded from the client build via Unity platform constraints. Using `[Server]` attribute alone is insufficient — it ships code to the client binary. The ADR specifying the complete server/client assembly boundary must be authored before implementation begins." `hit-detection.md` and `enemy-ai.md` place those systems in `ServerLogic.asmdef`. `currency-system.md` Group G asks for the same isolation. Damage Calculation Stories 001–005 and Currency were built in `IronGrind.Foundation` by explicit user decision, each recording the missing ADR as a deviation.

### Constraints
- Solo developer: one Unity project, one build pipeline per target. No second project.
- The EditMode suite (1995 tests) runs in the Editor with any build target active, and in batch mode. Server-only code must stay compilable and testable there.
- The server is an IL2CPP Linux headless build (ADR-007); the client is IL2CPP iOS. IL2CPP managed stripping removes unused code, but "unused" is decided by the linker, not by design, so it is not a guarantee.
- `src/` and `tests/` are local Unity packages (`com.irongrind.src`, `com.irongrind.tests`); assemblies are defined by `.asmdef` files inside them.
- No `NetworkBehaviour`, scene or prefab references any type in `src/` today, except the seven UI files.

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
| `IronGrind.ServerLogic` (new) | `src/ServerLogic/` | **no** | yes | `IronGrind.Foundation` |
| `IronGrind.Client` (new) | `src/Client/` | yes | **no** | `IronGrind.Foundation` |

"Approved packages" for `Foundation` includes `Unity.Netcode.Runtime` once ADR-004's package is installed (it is not in `Packages/manifest.json` today): NGO's IL post-processing only rewrites assemblies that reference it, and the `NetworkBehaviour` shells of Decision 4 live there. `ServerLogic` references it only if it declares NGO types.

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
- `#if UNITY_SERVER` inside files is not the boundary. It remains legal for small differences inside `Foundation`, but a type that must not ship to the client is moved, not wrapped.

### Decision 3 — Classification rule

Ask in this order; the first "yes" decides.

1. **Does the type reference UI Toolkit, UGUI, rendering, audio, input, or exist only to present?** → `IronGrind.Client`.
2. **Does it compute, validate or mutate authoritative game state, or hold a formula, a probability, a tuning value, a roll, or a rule the player must not be able to read or replace?** → `IronGrind.ServerLogic`. This covers resolvers and services (`DamageCalculator`, `CurrencySystem`, `EnhancementService`, `LootAuctionService`, `GroundItemService`), their configs (`DamageCalculationConfig`, `EnhancementConfig`), server state machines, the tick loop, persistence coordination, RPC guards and server-side validation.
3. **Do both sides need it to talk to each other or to describe the same thing?** → `IronGrind.Foundation`. This covers ids (`EntityID`, `ItemID`, `CharacterID`), enums and readonly structs that appear in wire messages or events the client receives, wire message schemas, and static definitions the client displays (`ItemDefinition`, `EquipmentData`).
4. **Otherwise** → `IronGrind.ServerLogic`. Server-only is the default for game logic; a type earns a place in `Foundation` by having a named client consumer.

Result types follow their producer unless the client receives them. `DamageResult` and `DamageContext` are `ServerLogic` (the GDD names them); what the client gets is the wire message built from them.

An interface lives with its consumer. An interface that server code calls and server code implements (`IEquippedWeaponQuery`, `IEnhancementBonusProvider`) is `ServerLogic`. An interface declared so that code in `Foundation` can call into server logic without referencing it (Decision 4) is `Foundation`, and its signature must not carry a server-only type.

### Decision 4 — Networked behaviours and assets

- A `NetworkBehaviour` that sits on a prefab clients also instantiate must exist in both builds, so it lives in `IronGrind.Foundation` and stays a thin shell: it declares RPCs and network variables and forwards server-side calls to an interface declared in `Foundation` and implemented in `ServerLogic`. The implementation is supplied by the server composition root (constructor or `Initialize` injection, ADR-010 Tier 1); on the client the interface is not supplied.
- An RPC body ships in the client binary whatever its target: only its execution is server-side. It must contain nothing but forwarding to a `Foundation` interface — never a rule, a formula or a constant.
- A server-only `NetworkBehaviour` must not be added to a prefab the client also instantiates: NGO addresses behaviours by their index on the `NetworkObject`, so both sides need the same component list.
- A `MonoBehaviour`, `ScriptableObject` or `[SerializeReference]` type in `IronGrind.ServerLogic` must not appear in any scene, prefab, Addressables group or asset included in a client build. The Editor compiles `ServerLogic`, so such a reference serializes without error and becomes a missing script at client runtime. Enforced by Decision 6, check 3. Server-only components are created in code by the server composition root, or live in server-only scenes, and those scenes are not in the client build profile's scene list.
- The server composition root (the code that constructs services and wires them) and its bootstrap component are in `IronGrind.ServerLogic`. The client's are in `IronGrind.Client`.
- Server-only *data* (drop tables, tuning assets) is a content question, not an assembly question, and is out of scope here: see Risks.

### Decision 5 — Tests and internals

- The existing test assembly `IronGrind.Foundation.EditModeTests` adds references to `IronGrind.ServerLogic` and `IronGrind.Client`. It is Editor-only, where both compile. One test assembly is kept; it is not split by this ADR.
- Each new assembly has its own `AssemblyInfo.cs` with `InternalsVisibleTo("IronGrind.Foundation.EditModeTests")`.
- `InternalsVisibleTo` between production assemblies is forbidden. If `ServerLogic` needs a member of a `Foundation` type, that member becomes public, or the type is in the wrong assembly.

### Decision 6 — Automated checks

Three checks, all required.

1. **Boundary test (no build needed, runs with the EditMode suite).** A test in `tests/EditMode/Architecture/` that:
   - reads the three `.asmdef` files and asserts the names, the two define-constraint strings exactly, `autoReferenced: false`, and that the reference lists match Decision 1;
   - by reflection, asserts that every type in a namespace on the server-only list (starting with `IronGrind.DamageCalculation`; each move story adds its namespace) is defined in the assembly named `IronGrind.ServerLogic`, and that no type in `IronGrind.Foundation` or `IronGrind.Client` has a field, property, parameter or base type from `IronGrind.ServerLogic`.
   This is the gate that fails on the day a file is put in the wrong folder. The namespace-to-assembly assertion is the one that matters; the "no `ServerLogic` type in a `Foundation` or `Client` signature" assertion repeats what the compiler already enforces through the asmdef references and is kept as a cheap guard. The `.asmdef` files are located through `CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName` and the package's resolved path, never a hard-coded path: a `file:` package has no real `Packages/com.irongrind.src/` folder on disk.
2. **Client-binary scan (needs a client build; satisfies AC-DC-I-01).** A build-pipeline step under `tools/` that, for a client player build, lists the managed assemblies that went into the player and the type names in them, and fails if `IronGrind.ServerLogic` is present or if any type name on the forbidden list (`DamageCalculator`, `DamageResult`, `DamageContext`, and each moved system's names) appears. The step must be shown to fail when a forbidden type is deliberately placed in `Foundation`. Intended mechanism, to be confirmed on a real build (Verification Required 5): an `IPostBuildPlayerScriptDLLs` callback, which sees the player's script DLLs after compilation and before IL2CPP conversion, reads the assembly list and the type names with a metadata reader; a secondary scan of the built output (`ScriptingAssemblies.json`, `global-metadata.dat`) covers what survived stripping. Not to be used: `BuildReport.packedAssets` (assets, not assemblies), `CompilationPipeline.GetAssemblies` (reflects the Editor's active target, not the build in progress), and the `Library/Bee` intermediate folders (internal layout).
3. **Client content check (needs a client build).** A build-time step (`IPreprocessBuildWithReport` plus a dependency scan of the client build profile's scenes and Addressables groups) that fails if any asset going into a client build depends on a script whose file is under `src/ServerLogic/`. It is written with the first story that puts a `MonoBehaviour` or `ScriptableObject` in `ServerLogic`; until then the assembly holds plain C# only and the check has nothing to find.

Until a client build exists in the pipeline, check 1 is the automated gate and AC-DC-I-01 stays open.

### Architecture Diagram

```
                 ┌──────────────────────────────┐
                 │  IronGrind.Foundation        │   client build: yes
                 │  ids, wire types, enums,     │   server build: yes
                 │  item definitions,           │
                 │  NetworkBehaviour shells,    │
                 │  bridging interfaces         │
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
No runtime API is created. The contracts are the two `.asmdef` files in Decision 2, the reference table in Decision 1, and the classification rule in Decision 3.

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
- Files that mix server logic and shared types have to be split before they can move (expected in `Networking`, 102 files).
- One more place to look for a file: folder now encodes build membership.

### Risks
- **A `ServerLogic` script referenced by a shipped asset** shows up as a missing script on the client, with no compile error. Mitigation: Decision 4 and Decision 6, check 3.
- **`#if UNITY_SERVER` inside `Foundation`** would make EditMode results depend on the active build profile, if the Editor defines the symbol under a server profile. Mitigation: no such block in code under test; if one becomes necessary, the suite must pass under both a client and a Dedicated Server profile.
- **A type that changes assembly breaks name-based references.** `[SerializeReference]` data stores the assembly name, and so does any persistence or lookup using assembly-qualified names. Mitigation: the per-move checklist in the Migration Plan. Today the only `[SerializeReference]` types are `EquipmentData`, `ConsumableData` and `ScrollData`, which stay in `Foundation`, and `src/` has no `Type.GetType`, `AssemblyQualifiedName` or `TypeNameHandling` use.
- **A future PlayMode test assembly** that references `ServerLogic` fails to compile for a client player. Mitigation: keep it Editor-only or give it the same define constraint.
- **Server-only data still ships.** A drop table stored as an asset in the client's content is readable even if the code that rolls it is not. Mitigation: out of scope here; record a follow-up decision on server-only content (Addressables groups excluded from the client) before Loot Table or Enemy AI data is authored as assets.
- **`UNITY_EDITOR` in the constraint** means the Editor never proves the client build compiles without `ServerLogic`. Mitigation: the boundary test's reflection check forbids `Foundation` and `Client` from referencing `ServerLogic` types; the first real client build is a validation gate.
- **Wrong classification of a result or config type** that a client turns out to need. Mitigation: move it to `Foundation` with a named consumer (Decision 3, rule 3); the boundary test's namespace list makes the change deliberate.
- **Engine behaviour not confirmed from the manual** (Verification Required 3–5). Mitigation: the constraints are correct whether or not `UNITY_SERVER` is defined in the Editor; the binary scan is fixed on a real build.

## GDD Requirements Addressed

| GDD System | Requirement | How This ADR Addresses It |
|------------|-------------|--------------------------|
| damage-calculation.md | Core Rule 1: resolver and its types in a `ServerLogic.asmdef` assembly excluded from the client build; `[Server]` alone is insufficient; the ADR must exist before implementation | Decision 1 and 2 define `IronGrind.ServerLogic` and exclude it by define constraint; Decision 3 places `DamageCalculator`, `DamageResult`, `DamageContext` there |
| damage-calculation.md | AC-DC-I-01: automated inspection of the client assembly finds no Damage Calculation type | Decision 6, check 2 (client-binary scan), with check 1 as the per-test-run gate |
| currency-system.md | Group G: server assembly isolation | Same assembly and rule; `CurrencySystem` is `ServerLogic` by Decision 3 rule 2 |
| hit-detection.md | Hit Detection is called server-side only (assembly: `ServerLogic.asmdef`) | The system is authored directly in `src/ServerLogic/` |
| enemy-ai.md | Enemy AI runs server-side only (`ServerLogic.asmdef`) | The system is authored directly in `src/ServerLogic/` |

## Performance Implications
- **CPU**: None at runtime. Assembly boundaries do not change IL2CPP call cost in any way that matters at 20 Hz.
- **Memory**: Slightly smaller client binary (no server logic) and server binary (no UI code).
- **Load Time**: None measurable.
- **Network**: None.
- **Editor**: Three assemblies instead of one shortens recompiles after a change in `ServerLogic` or `Client`; a change in `Foundation` still recompiles all three.

## Migration Plan
1. **Damage Calculation Story 006 (first move, proves the pattern)**: create `src/ServerLogic/` with its asmdef and `AssemblyInfo.cs`; create `src/Client/` with its asmdef and move the seven `src/Foundation/UI/` files; set `autoReferenced: false` on all three, after giving every script under `Assets/` that uses project code its own asmdef with explicit references (today: `Assets/Editor/HudBootstrap.cs`, which uses `IronGrind.UI.LevelingSystem` and so needs an Editor asmdef referencing `IronGrind.Client`); add both references to the test asmdef; move `src/Foundation/DamageCalculation/` (git move, keep `.meta` files and GUIDs); write the boundary test (Decision 6, check 1). The move forces its dependencies: `DamageCalculator` takes `CharacterStats`, `IItemDatabase` and `IEnhancementBonusProvider`. `ServerLogic` may reference types still in `Foundation`, so the move compiles today; those types move with their own systems later.
2. **One move story per system**, in dependency order so that nothing in `Foundation` is left referencing a moved type: Currency (Group G), Enhancement, Loot Table, Inventory, Leveling, Character Stats, NPC Interaction, then Networking (classified file by file first). Each story adds its namespace to the boundary test and leaves the suite green. Checklist for every move: the `.cs.meta` GUIDs are preserved (move the file and its `.meta` together); any moved type that is serialized through `[SerializeReference]` or stored by name gets `[MovedFrom(true, sourceAssembly: "IronGrind.Foundation")]` and its assets are re-saved; `src/` is searched for `Type.GetType`, `AssemblyQualifiedName` and `TypeNameHandling`.
3. **Client-binary scan** (Decision 6, check 2) with the first client build pipeline; it closes AC-DC-I-01.
4. **New systems** (Hit Detection, Enemy AI, Auto-Attack Combat, Status Effects) are written in `src/ServerLogic/` from their first story.
5. After the ADR is Accepted: `/create-control-manifest update` to add the assembly rules; correct `damage-calculation.md` wording ("platform constraints" → define constraint; `ServerLogic.asmdef` → `IronGrind.ServerLogic`).

Until a system's move story is done, its code is still in the client build. That window is accepted; no client build is distributed before the moves are complete.

## Validation Criteria
- The boundary test exists, passes, and fails when a `IronGrind.DamageCalculation` type is placed in `src/Foundation/` (shown once, by hand, in Story 006).
- The EditMode suite passes after each move story.
- A client player build's assembly list has no `IronGrind.ServerLogic`, and the scan step fails on a deliberately misplaced type.
- A Linux Dedicated Server IL2CPP build contains `IronGrind.ServerLogic`, does not contain `IronGrind.Client`, and starts.

## Related Decisions
- ADR-004 (NGO) — `NetworkBehaviour` placement, Decision 4.
- ADR-007 (hosting backend) — the server build this assembly ships in.
- ADR-010 (event messaging) — Tier 1 injection is how `Foundation` shells reach `ServerLogic` implementations; event argument structs the client receives are `Foundation`.
- `design/gdd/damage-calculation.md` Core Rule 1, AC-DC-I-01; `design/gdd/currency-system.md` Group G; `design/gdd/hit-detection.md`; `design/gdd/enemy-ai.md`.
- `docs/tech-debt-register.md` TD-002 (local packages and asmdef wiring).
