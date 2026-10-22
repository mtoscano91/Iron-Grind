# Story 004: ZoneDefinition, ZoneSceneRegistry and Bounds Validation (CR-ZI-10)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-1 (`ZoneTemplateID`), CR-ZI-10, T-3, AC-ZI-5
**Requirement**: `TR-zi-010`, `TR-zi-017` *(epic placeholders)*

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

- [ ] `enum ZoneTemplateID : byte { StartingZone = 0 }`; the `-zoneTemplate` startup value maps to a template through `ZoneSceneRegistry` (unknown value → S-fail, logged).
- [ ] `ZoneDefinition : ScriptableObject` with fields only: template id, walkable bounds min / max (`Vector3`), town respawn point and an "authored" flag, `NavMeshData`.
- [ ] **Bounds rule (CR-ZI-10)**: valid only when every walkable bound coordinate is within ±250 m on every axis.
- [ ] **AC-ZI-5**: with a crafted `ZoneDefinition` whose bound exceeds ±250 m on one axis, startup S6 fails validation: the zone goes Loading → Closed via T-3 (`MarkClosedInvalidBounds`), a `ZoneBoundsExceedEncoding` critical alert is logged, `MarkActive` and gateway registration are never called. A ±250.0 m bound passes; 250.01 m fails.
- [ ] The real validator replaces `StandInZoneBoundsValidator`; `IZoneScene.AddNavMeshData()` adds the template's `NavMeshData` instead of returning `default` (the Story 045 `[StandIn] no zone NavMesh data` path is removed or kept only for a definition with no NavMesh data — open decision 2).
- [ ] `ZoneTemplate_Empty.unity` (Story 045) has a `ZoneDefinition` registered so the local verification run still reaches Active.

---

## Implementation Notes

*Derived from ADR-009 Amendment 1:*

- S6 order is fixed by ADR-009 A1: scene load → `AddNavMeshData` → navigation init → bounds validation. Do not reorder; the composition (`ZoneServerComposition`) already calls `_boundsValidator.Validate(_zoneId)` after `AddNavMeshData`.
- The validator needs the loaded definition: pass it in through the constructor or a setter called after the load, without changing the `IZoneBoundsValidator` call site more than needed.
- Crafted test assets are created with `ScriptableObject.CreateInstance` in the test, not committed under `Assets/` (epic gap 5).

**Open decisions for `/story-readiness`**

1. "Half-extent ≤ 250 m": check each bound coordinate's absolute value (what the position encoding needs) — confirm.
2. A definition with no `NavMeshData`: T-3 with `ZoneDefinitionLoadFailed`, or the existing stand-in log while the Navigation epic is missing. The empty verification zone has no baked NavMesh.

---

## Out of Scope

- Story 005: `GetTownRespawnPoint` over the definition
- The Navigation epic: the real `IZoneNavigation`
- Authoring the real StartingZone content (Level Design)

---

## QA Test Cases

- **AC-ZI-5** — Given a definition with max.x = 251; When the composition runs S6; Then T-3, `ZoneBoundsExceedEncoding` logged, no `MarkActive`, no `RegisterZone`.
- **Boundary** — ±250.0 on every axis → valid; −250.01 on z → invalid.
- **Registry** — unknown template string → S-fail logged; `StartingZone` → its scene and definition.
- **NavMesh** — a definition with `NavMeshData` → `AddNavMeshData` returns a valid instance that navigation receives.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_ZoneDefinitionBounds_tests.cs` — must exist and pass; the Story 045 local verification run still reaches Active.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None (uses the existing composition and Story 001's T-3 only through `IZoneStateOwner`)
- Unlocks: Story 005, Story 014
