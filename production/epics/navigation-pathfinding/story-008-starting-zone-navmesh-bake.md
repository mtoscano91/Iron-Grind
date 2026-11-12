# Story 008: Starting-Zone Floor and NavMesh Bake

> **Epic**: Navigation / Pathfinding
> **Status**: Ready
> **Layer**: Core
> **Type**: Config/Data
> **Manifest Version**: 2026-11-06
> **Estimate**: 1–2 hours

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

- [ ] `Assets/Scenes/Zones/ZoneTemplate_Empty.unity` (the starting zone's scene — confirm the mapping in the zone registry first) has a flat placeholder floor covering the walkable bounds of `ZoneDefinition_StartingZone` (x and z from −50 to 50), at y = 0, marked as the placeholder in its name (for example `PlaceholderGround`), with a `NavMeshSurface`.
- [ ] The surface is baked in the Editor; the `NavMeshData` asset is saved under `Assets/NavMesh/` (CR-NAV-2 naming: `{ZoneTemplateID}.asset`, template id 0 → decide the readable name at readiness and note it in the GDD) and assigned to `ZoneDefinition_StartingZone._bakedNavMesh`.
- [ ] **Smoke**: start the zone server (Editor or the existing local run) → no "NavMesh stand-in" log from `UnitySceneLoader`; the instance passed to `IZoneNavigation.Initialize` is valid; `Movement.SetNavMeshAvailable(true)`; the existing end-to-end login to zone entry still works.
- [ ] Movement's player position validation (`NavMesh.SamplePosition`) accepts a point on the floor and rejects one outside the bounds (spot check with the existing Movement tests or the smoke run; record which).

---

## Implementation Notes

- The floor is a stand-in for Level Design. Do not add other geometry. The town respawn point (0, 0, 0) and every future spawn point must be on it.
- Agent settings for the bake: Unity defaults (radius 0.5, height 2) unless the GDD or Movement specifies otherwise; record the values used in the smoke note.
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
- **Smoke — zone start**: Setup: run the zone server as in the Authentication Story 017 local run. Verify: log has no NavMesh stand-in line; startup reaches S10. Pass: both.
- **Smoke — login regression**: Setup: log in through the gateway to zone entry (Story 017 flow). Pass: the player enters the zone as before.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: smoke check pass recorded in `production/qa/smoke-2026-MM-DD.md` (date of the run), with the screenshot

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (can be done first)
- Unlocks: Stories 009, 010, 011
