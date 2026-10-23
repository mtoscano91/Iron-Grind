# Story 004: ZoneDefinition, ZoneSceneRegistry and Bounds Validation (CR-ZI-10)

> **Epic**: Zone Instancing
> **Status**: Complete (2026-10-23) — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 5 hours (confirmed at `/story-readiness` 2026-10-23: registry, loader change, two assets and the scene edit)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-1 (`ZoneTemplateID`), CR-ZI-10, T-3, AC-ZI-5
**Requirement**: `TR-zi-010`, `TR-zi-017` *(epic placeholders — stay placeholders until `/architecture-review`, as Stories 001–003)*

**ADR Governing Implementation**: ADR-009: Scene / Zone-Load Management (+ Amendment 1)
**ADR Decision Summary**: a `ZoneDefinition` ScriptableObject holds the walkable bounds, the town respawn point and the baked `NavMeshData`; `ZoneSceneRegistry` maps `ZoneTemplateID` → scene; startup S6 loads the scene, adds the NavMesh data, initialises navigation and validates bounds; a failure is T-3.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: `NavMesh.AddNavMeshData` / `NavMeshDataInstance` and `SceneManager.LoadSceneAsync` are post-cutoff-sensitive — check `docs/engine-reference/unity/` before use. `[SerializeField]` is fields only (6.3 compile error on properties).

**Control Manifest Rules (this layer)**:
- Required: one zone per server process; NavMesh is process-global — ADR-002
- Required: `ZoneDefinition` and the registry are server-side (`IronGrind.ServerLogic`); server-only scenes stay out of client build profiles — ADR-012
- Forbidden: running multiple zones in one process — ADR-002

---

## Acceptance Criteria

- [ ] `enum ZoneTemplateID : byte { StartingZone = 0 }`. `-zoneTemplate` takes the enum **name** (`StartingZone`; parsed case-sensitive, numeric text rejected). `ZoneSceneRegistry : ScriptableObject` holds entries of (`ZoneTemplateID`, scene name, `ZoneDefinition`); `UnitySceneLoader.BeginLoad` resolves the value through it. An unknown name or a template with no entry → the loader reports done and not succeeded (S-fail at S6), with one error log line naming the template value; no scene load starts.
- [ ] `ZoneDefinition : ScriptableObject` with `[SerializeField] private` fields only and read-only properties: template id, walkable bounds min / max (`Vector3`), town respawn point and an "authored" flag, `NavMeshData` (may be null).
- [ ] **Bounds rule (CR-ZI-10, Decision 1)**: valid only when every coordinate of min and max has absolute value ≤ 250.0 (inclusive), every coordinate is finite (no NaN / ±Infinity), and min ≤ max on every axis.
- [ ] **AC-ZI-5** (this story's half): with a crafted `ZoneDefinition` whose bound exceeds ±250 m on one axis, startup S6 fails validation: `IZoneStateOwner.MarkClosedInvalidBounds` is called exactly once, an error log `[ZoneInstancing] ZoneBoundsExceedEncoding: …` naming the axis and value is written, `MarkActive` and `RegisterZone` are never called (fakes). A ±250.0 m bound passes; 250.01 m fails. The real Loading → Closed transition and its `ZoneDefinitionLoadFailed` alert (GDD T-3 row) arrive with Story 014's real `IZoneStateOwner`.
- [ ] The new `ZoneDefinitionBoundsValidator` replaces `StandInZoneBoundsValidator` in `ZoneServerHost`. It reads the definition from a shared `ZoneDefinitionSlot` that the loader fills on a successful load; the `Validate(ZoneID)` call site is unchanged. No definition in the slot → `false` and an error log `[ZoneInstancing] ZoneDefinitionLoadFailed: no zone definition loaded`.
- [ ] `IZoneScene.AddNavMeshData()` calls `NavMesh.AddNavMeshData` with the loaded definition's `NavMeshData` and returns its instance. A definition with no `NavMeshData` keeps the Story 045 `[StandIn] no zone NavMesh data` log (once) and returns `default` — not T-3 (Decision 2).
- [ ] `ZoneServerHost` gets `[SerializeField] private ZoneSceneRegistry _registry`, set in `ZoneServer.unity` to a registry asset whose `StartingZone` entry maps to scene `ZoneTemplate_Empty` and a `ZoneDefinition` with bounds ±50 m, no NavMesh and "authored" false. The scene's `_editorArgs` change from `-zoneTemplate ZoneTemplate_Empty` to `-zoneTemplate StartingZone`, and the Story 045 local verification run still reaches Active.

---

## Implementation Notes

*Derived from ADR-009 Amendment 1:*

- S6 order is fixed by ADR-009 A1: scene load → `AddNavMeshData` → navigation init → bounds validation. Do not reorder; the composition (`ZoneServerComposition`) already calls `_boundsValidator.Validate(_zoneId)` after `AddNavMeshData`.
- The validator gets the loaded definition through `ZoneDefinitionSlot` (a small shared holder the host creates and passes to both `UnitySceneLoader` and `ZoneDefinitionBoundsValidator`), so the `Validate(ZoneID)` call site does not change.
- Asset delivery: the registry is a `[SerializeField]` reference on the host in `ZoneServer.unity`; no `Resources.Load` (synchronous, rejected by ADR-009 Alternatives); Addressables stay post-MVP.
- Crafted test assets are created with `ScriptableObject.CreateInstance` in the test (fields set through a test-only/internal initialiser or `SerializedObject`), not committed under `Assets/` (epic gap 5). Only the StartingZone registry and definition assets are committed, under `Assets/data/Zones/`.
- NavMesh cleanup: the instance from `AddNavMeshData` must be removed at teardown with `NavMesh.RemoveNavMeshData`; that belongs to the Navigation epic's real `IZoneNavigation` (teardown step 3). The StartingZone definition has no NavMesh, so nothing leaks in the verification run. Tests remove any instance they create in `TearDown`. **Note for the Navigation epic (code review 2026-10-23):** its real `IZoneNavigation` story must remove the instance with `NavMesh.RemoveNavMeshData` at teardown step 3 and on leaving Play mode in the Editor (with domain reload off, added NavMesh data can persist between Play sessions).

**Decisions (`/story-readiness` 2026-10-23, user: "yes, apply all")**

1. Bounds rule: each coordinate's absolute value ≤ 250.0 inclusive; NaN / ±Infinity and min > max on any axis are invalid.
2. A definition with no `NavMeshData`: keep the stand-in log and return `default`, not T-3, while the Navigation epic is missing — otherwise the empty verification zone cannot reach Active.
3. Bounds shape: `Vector3` min / max checked on all three axes. **Deviation from the ADR-009 code sketch** (`Rect _walkableBoundsXZ`): the position encoding (CR-NET-7.2) caps every axis, so Y is checked too. Record it in the Completion Notes; no ADR rule changes.
4. `-zoneTemplate` carries the `ZoneTemplateID` name (ADR-009: "the gateway passes `ZoneTemplateID`"), resolved to a scene in `BeginLoad`; the unknown-name failure is S-fail at S6, not S0.

---

## Out of Scope

- Story 005: `GetTownRespawnPoint` over the definition
- The Navigation epic: the real `IZoneNavigation`
- Authoring the real StartingZone content (Level Design)

---

## QA Test Cases

- **AC-ZI-5** — Given a definition with max.x = 251; When the composition runs S6 (fake scene seam over the real validator); Then `MarkClosedInvalidBounds` once, `ZoneBoundsExceedEncoding` logged naming x, no `MarkActive`, no `RegisterZone`.
- **Boundary** — ±250.0 on every axis → valid; −250.01 on z → invalid; 250.01 on y → invalid.
- **Malformed** — NaN on one coordinate → invalid; +Infinity → invalid; min.x > max.x → invalid; empty slot → invalid with `ZoneDefinitionLoadFailed`.
- **Registry** — unknown name (`Forest`), numeric text (`0`) and a known enum value with no entry → done, not succeeded, one error log; `StartingZone` → its scene name and definition.
- **NavMesh** — a definition with `NavMeshData` → `AddNavMeshData` returns a valid instance that a fake navigation receives (removed in `TearDown`); a definition without → `default` and one `[StandIn] no zone NavMesh data` log. **Engine check first:** `NavMesh.AddNavMeshData(new NavMeshData())` returns a valid instance in EditMode; if it does not, move this case to the verification run and record it.
- **Verification run** — `ZoneServer.unity` with `-zoneTemplate StartingZone` reaches Active (Story 045 procedure).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_ZoneDefinitionBounds_tests.cs` — must exist and pass; the Story 045 local verification run still reaches Active.

**Status**: [x] Created and passing — ZoneDefinitionBounds 58/58, ZoneAssets 4/4 (batch EditMode 3701 total, 0 failed, 70 skipped Npgsql, 2026-10-23); Editor verification run with `-zoneTemplate StartingZone` reached Active (Editor.log 09:27)

---

## Files

- New: `src/ServerLogic/ZoneInstancing/ZoneTemplateID.cs`, `ZoneDefinition.cs`, `ZoneSceneRegistry.cs`, `ZoneDefinitionSlot.cs`, `ZoneDefinitionBoundsValidator.cs` (+ `.meta`)
- Changed: `src/ServerLogic/ZoneServer/Unity/UnitySceneLoader.cs` (registry lookup, slot fill, real `AddNavMeshData`), `src/ServerLogic/ZoneServer/ZoneServerHost.cs` (`_registry` field, slot, real validator), `Assets/Scenes/Server/ZoneServer.unity` (registry reference, `_editorArgs`). Folder case: the project folder is `Assets/data` (lowercase).
- New assets: `Assets/data/Zones/ZoneSceneRegistry.asset`, `Assets/data/Zones/ZoneDefinition_StartingZone.asset` (+ `.meta`)
- Tests: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_ZoneDefinitionBounds_tests.cs` (+ folder `.meta`)

---

## Dependencies

- Depends on: None (uses the existing composition and Story 001's T-3 only through `IZoneStateOwner`)
- Unlocks: Story 005, Story 014

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 7/7 passing (six by automated test, AC-2 "fields only" by reading plus compile; AC-7 also by the Editor verification run). None deferred.
**Deviations** (advisory, user: "Complete, notes only"):
1. Bounds are `Vector3` min / max checked on all three axes, not the ADR-009 sketch's `Rect _walkableBoundsXZ` (Decision 3); no ADR rule changes.
2. NaN / ±Infinity and min > max log `ZoneDefinitionLoadFailed`; only a finite coordinate beyond ±250 m logs `ZoneBoundsExceedEncoding`. Check order: finite → range → min ≤ max.
3. `ZoneDefinitionSlot` is filled when `LoadSceneAsync` returns non-null, not on completion (the validator only runs after a successful load). `BeginLoad` is single use.
4. Extra API: `[CreateAssetMenu]` on both ScriptableObjects; public `ZoneSceneRegistry.Entry` struct; public static `ZoneDefinitionBoundsValidator.AreBoundsValid`; internal `Initialize` test initialisers; the registry rejects an entry whose definition has another `TemplateId` (error log), first match wins, name parsed once (digits, signs, whitespace, commas rejected).
5. A null registry is allowed in `UnitySceneLoader` (every load fails); a null slot throws.
6. The AC-ZI-5 composition tests reuse the Networking test fakes (`SuBackend`, `SuNav`, `ZsClose`, ...); changes there can break them.
7. `TR-zi-010` and `TR-zi-017` remain placeholders until `/architecture-review`.
8. Assets live under `Assets/data/Zones/` (lowercase, the project's existing folder).
**Scope**: `IZoneBoundsValidator.cs` (one doc line) and `tests/PlayMode/Networking/ZoneServer_UnitySeams_tests.cs` (constructor calls only; PlayMode suite not run) touched outside the Files list; `ZoneInstancing_ZoneAssets_tests.cs` added at code review.
**Notes for other stories**:
- Story 005: `ZoneDefinition.TownRespawnPoint` and `TownRespawnPointAuthored` are in place (the StartingZone asset has authored = false).
- Story 014: the real `IZoneStateOwner` owns the Loading → Closed transition and the T-3 `ZoneDefinitionLoadFailed` alert; this story proves only the `MarkClosedInvalidBounds` call.
- Navigation epic: remove the `AddNavMeshData` instance with `NavMesh.RemoveNavMeshData` at teardown step 3 and on Editor Play-mode exit.
- Verification runs need the portable PostgreSQL started (PowerShell `Start-Process`) and the Editor launched from PowerShell with `IRONGRIND_PG_PASSWORD` set for that process; otherwise startup fails at S4.
**Test Evidence**: Integration — `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_ZoneDefinitionBounds_tests.cs`, `ZoneInstancing_ZoneAssets_tests.cs`; Editor verification run (Editor.log 2026-10-23 09:27).
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all 12 applied (`3ed5601`).
