# Story 008: Starting-Zone Floor and NavMesh Bake

> **Epic**: Navigation / Pathfinding
> **Status**: Complete
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-11-06
> **Estimate**: 1–2 hours
> **Readiness**: 2026-11-12 NEEDS WORK → fixes 1–5 applied → Ready

## Context

**GDD**: `design/gdd/navigation-pathfinding.md` — CR-NAV-2 (no runtime baking), Dependencies (Movement shares the asset); `design/gdd/movement-system.md` CR-MOV-7
**Requirement**: `TR-nav-009` (bake half; the fixture half is Story 011)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-009: Scene / Zone-Load Management (Amendment 1); ADR-002
**ADR Decision Summary**: Each `ZoneDefinition` carries its baked `NavMeshData`; the scene loader adds it at S6 (`UnitySceneLoader.AddNavMeshData`). Meshes are baked in the Editor; never at runtime.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (AI Navigation package 2.0 `NavMeshSurface` workflow, past the training cutoff — check `docs/engine-reference/unity/modules/navigation.md` and the package manual)
**Engine Notes**: `com.unity.ai.navigation` 2.0.10 is already in `Packages/manifest.json`. Bake with a `NavMeshSurface` component (not the legacy Navigation window) and keep the generated `NavMeshData` asset.

**Control Manifest Rules (this layer)**:
- Forbidden: runtime NavMesh baking on the server
- Required: one zone per process (one mesh loaded per process)

---

## Acceptance Criteria

- [ ] `Assets/Scenes/Zones/ZoneTemplate_Empty.unity` (the starting zone's scene; `ZoneSceneRegistry` maps template 0 → `ZoneTemplate_Empty` with `ZoneDefinition_StartingZone`, confirmed at readiness 2026-11-12) has a flat placeholder floor covering the walkable bounds of `ZoneDefinition_StartingZone` (x and z from −50 to 50), at y = 0, marked as the placeholder in its name (for example `PlaceholderGround`), with a `NavMeshSurface`.
- [ ] The surface is baked in the Editor; the `NavMeshData` asset is saved as `Assets/NavMesh/Zone0_StartingZone.asset` (CR-NAV-2 naming `Assets/NavMesh/Zone{ZoneTemplateID}_{Name}.asset`, decided at readiness 2026-11-12) and assigned to `ZoneDefinition_StartingZone._bakedNavMesh`.
- [ ] After the bake the `NavMeshSurface` component is left **disabled** in the saved scene, so only `UnitySceneLoader.AddNavMeshData` adds the data at S6 (an enabled surface adds its own data in `OnEnable`, giving two overlapping copies). Verified in the smoke run: in Play mode the NavMesh holds one copy (`NavMesh.CalculateTriangulation()` vertex count equals the count with the data added once, or the overlay shows no doubled mesh; record which).
- [ ] **Smoke**: start the zone server (Editor or the existing local run) → the log has no `[StandIn] no zone NavMesh data` line (from `UnitySceneLoader`) and no `[Movement] NavMesh unavailable` error; startup reaches S10; the existing end-to-end login to zone entry still works. (The host still passes `StandInZoneNavigation`, which ignores the instance until Story 010, so its validity is observed through these two log lines.)
- [ ] Movement's player position validation (`UnityNavMeshSampler` over `NavMesh.SamplePosition`) on the real mesh, in the smoke run: the player moves on the floor (the committed position changes), and moving toward a ±50 edge stops near the baked mesh edge at ±49.333 (the agent radius 0.5 plus voxel rounding, measured by the bake tool at dev-story 2026-11-12), so within 0.7 of the floor edge. Record both in the smoke note. (The EditMode Movement tests use `INavMeshSampler` fakes and cannot cover this.) **Closed 2026-11-12 (user decision) on bake-time evidence:** the client cannot send movement yet (`LoggingMovementIntentSink`), so the bake tool's real `NavMesh.SamplePosition` checks on the asset (same call and radius as `UnityNavMeshSampler`) stand in; the in-game movement check is carried to Story 009 and the first story that sends client movement intents.

---

## Implementation Notes

- The floor is a stand-in for Level Design. Do not add other geometry. The town respawn point (0, 0, 0) and every future spawn point must be on it.
- Agent settings for the bake: Unity defaults (radius 0.5, height 2) unless the GDD or Movement specifies otherwise; record the values used in the smoke note.
- **Bake tool (dev-story 2026-11-12, user choice):** `Assets/Editor/Navigation/StartingZoneNavMeshBake.cs` (Editor-only assembly `IronGrind.NavMeshBakeTools`) does the whole bake: it reuses or creates the floor and the surface, bakes with the surface enabled, writes `Assets/NavMesh/Zone0_StartingZone.asset` directly (a re-bake copies into the existing asset, so the GUID is kept), assigns the definition, disables the surface and saves. Run it from the menu `IronGrind/Navigation/Bake Starting Zone NavMesh` or in batch mode with `-executeMethod IronGrind.EditorTools.Navigation.StartingZoneNavMeshBake.BakeBatch`. Do not use the Inspector Bake button, which saves next to the scene and leaves the surface enabled.
- Bake settings used: agent type 0 (radius 0.5, height 2, slope 45, climb 0.75), geometry PhysicsColliders, collect All; the floor is a Unity Plane sized from the definition's x/z walkable bounds (today scale 10 × 1 × 10 at the origin), always at y = 0; the menu item refuses in Play mode and offers to save modified scenes first; a re-bake keeps the asset GUID (verified at code review 2026-11-12 by a bound change 50 → 40 → 50: edge moved to 39.333 and back, final asset byte-identical to the smoke-tested one); the surface sits at the origin because the loader adds the data with no offset.
- Performance: no per-frame impact expected — the mesh is baked in the Editor and added once at S6; queries already run through the existing Movement and Navigation paths.
- Server build: the asset is referenced by the `ZoneDefinition`, so it ships through the existing reference; confirm it is in the server build content (Story 009 checks the build).
- Commit the scene, the asset and their `.meta` files; leave out unrelated Editor-generated changes (`ProjectSettings/*`, URP assets) as with earlier stories.

---

## Out of Scope

- Story 009: server build config (`link.xml`, frame-rate pin)
- Story 011: the 150-agent fixture
- Level design of the real starting zone

---

## QA Test Cases

- **Manual check — asset assigned**: Setup: open `ZoneDefinition_StartingZone`. Verify: `Baked Nav Mesh` field references the new asset. Pass: non-null.
- **Manual check — mesh coverage**: Setup: open the scene, show the NavMesh overlay. Verify: the blue mesh covers the floor to within the agent radius of each edge. Pass: covers (screenshot in the smoke note).
- **Manual check — surface disabled**: Setup: open the saved scene. Verify: the `NavMeshSurface` component's checkbox is off and its data field references `Zone0_StartingZone`. Pass: both.
- **Smoke — single mesh copy**: Setup: Play mode after S6. Verify: one copy of the mesh (triangulation count or overlay, as recorded). Pass: one copy.
- **Smoke — zone start**: Setup: run the zone server as in the Authentication Story 017 local run. Verify: log has no `[StandIn] no zone NavMesh data` line and no `[Movement] NavMesh unavailable` error; startup reaches S10. Pass: all three.
- **Smoke — real SamplePosition**: Setup: logged-in player in the zone. Verify: moving on the floor changes the position; pushing toward an edge stops near x or z = ±49.333 (within 0.7 of ±50). Pass: both.
- **Smoke — login regression**: Setup: log in through the gateway to zone entry (Story 017 flow). Pass: the player enters the zone as before.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: smoke check pass recorded in `production/qa/smoke-2026-MM-DD.md` (date of the run), with the screenshot

**Status**: [x] Created — `production/qa/smoke-2026-11-12-navigation-008.md` (PASS), screenshots `production/qa/evidence/nav-008-navmesh.png`, `nav-008-navmesh-edge.png`

---

## Dependencies

- Depends on: None (can be done first)
- Unlocks: Stories 009, 010, 011

---

## Completion Notes

**Completed**: 2026-11-12
**Criteria**: 5/5 passing (AC5 on bake-time `SamplePosition` evidence; in-game movement carried to Story 009, user decision)
**Deviations** (advisory, no tech debt):
- AC5 movement check carried: the client has only `LoggingMovementIntentSink`, so no movement reaches the zone yet.
- AC5 edge value: the bake insets 0.667 (agent radius 0.5 + voxel rounding), mesh edge ±49.333; AC text corrected at dev-story.
- The AI Navigation overlay did not draw the loader-added runtime mesh (inconclusive); AC3 rests on the disabled surface + no stand-in line.
- Server-build inclusion of the asset left to Story 009 (as the implementation notes say).
- Out of the original file list, by decision: Editor bake tool `Assets/Editor/Navigation/StartingZoneNavMeshBake.cs` + `IronGrind.NavMeshBakeTools.asmdef` (dev-story), GDD CR-NAV-2 naming (readiness), Story 009 carries.
**Test Evidence**: Config/Data — `production/qa/smoke-2026-11-12-navigation-008.md` (PASS, signed off), screenshots `production/qa/evidence/nav-008-navmesh.png`, `nav-008-navmesh-edge.png`
**Code Review**: Complete — unity-specialist + own pass, fixes 1–6 applied; re-bake path verified (GUID kept, 50 → 40 → 50, final asset byte-identical to the smoke-tested one). Lean: QL-TEST-COVERAGE + LP-CODE-REVIEW skipped.
