# Story 007: Starting-Zone Spawn Table Content

> **Epic**: Mob Spawning
> **Status**: Ready
> **Layer**: Feature
> **Type**: Config/Data
> **Estimate**: 2–3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-2 authoring convention, Player Fantasy (depth gradient: weak near the gate, strong at the edge; variants spatially separated), F-MS-2 (cycle time, informational), OQ-MS-1 (no boss points)
**Requirement**: `TR-ms-008` (content part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-002: NavMesh Service Execution Contract (every point must pass CR-NAV-13 on the starting zone's baked NavMesh)
**ADR Decision Summary**: Spawn points are validated against the NavMesh at zone load; off-mesh points are excluded.

**Epic scope (2026-11-16)**: content split by system — Enemy AI owns the roster (MobTypeIDs 1–4: Bandicoot Common, Wild Bandicoot Uncommon, Bandicoot Scavenger Rare, Giant Bandicoot Elite); this story authors only the spawn table. The 16 further roster types in the GDD are out of scope.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: asset authoring only; positions must lie on `ZoneDefinition_StartingZone`'s baked NavMesh.

**Control Manifest Rules (this layer)**:
- Required (coding standards): gameplay values data-driven; balance values link to their rationale

---

## Acceptance Criteria

- [ ] **AC-1 Asset**: a `ZoneSpawnTable` asset for the starting zone's `ZoneTemplateID`, assigned to `ZoneDefinition_StartingZone`'s `_spawnTable` field (Story 001 decision 1).
- [ ] **AC-2 Story 001 validation**: the asset passes the table validator (≤ 150 points, unique IDs, known MobTypeIDs 1–4 only, inside the zone's walkable bounds, override 0 on every point).
- [ ] **AC-3 NavMesh**: every point passes CR-NAV-13 on the baked NavMesh (0 excluded) — checked with `SpawnPointValidator` in a PlayMode or Editor test against the real mesh.
- [ ] **AC-4 Layout (CR-MS-2 convention)**: points in clusters of ≥ 4 within a 15 m radius; Common clusters nearest the town respawn point and higher tiers progressively farther (each tier's mean distance from the town point greater than the previous tier's); about 40% of points Common. Point count and per-tier split are recorded in the Completion Notes with the rationale (density vs the 150 cap and the MVP player count).
- [ ] **AC-5 Tier clearance**: the minimum distance from any Common point to any Rare or Elite point is greater than the larger tier's `AggroRange` (roster row), so a player fighting in the Common band is not aggroed by a Rare / Elite mob standing at its spawn point. The measured minimum distances are recorded in the Completion Notes.
- [ ] **AC-6 Smoke check**: a smoke check entry (`production/qa/smoke-2026-11-*.md` or the story's evidence) shows the zone loads with the table and the expected point count spawns at T-2.

---

## Implementation Notes

- Town respawn point: `ZoneDefinition` `_townRespawnPoint` (starting zone asset). Walkable bounds: `_walkableBoundsMin/Max`.
- A small Editor or EditMode helper that prints per-tier counts and mean distances is enough evidence for AC-4; keep any such helper under `tests/` or an Editor folder, not in `src/ServerLogic`.
- If the baked NavMesh is too small for a readable gradient, record it and ask before enlarging the scene.

---

## Out of Scope

- New mob types / roster rows (Enemy AI data)
- Boss or event points (OQ-MS-1)
- Spawn VFX / audio (presentation epics)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Validator (AC-2)**: load the shipped asset in EditMode, run the Story 001 validator against the shipped roster registry and starting-zone bounds → passes, no log above Info.
2. **NavMesh (AC-3)**: PlayMode with the starting-zone scene → `SpawnPointValidator.Validate` returns valid = total.
3. **Gradient (AC-4)**: per-tier mean distance from the town point strictly increases Common → Elite; every point has ≥ 3 other points within 15 m; Common share 35–45%.
4. **Smoke (AC-6)**: zone host load → T-2 spawn count equals the point count.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: smoke check pass (`production/qa/smoke-*.md`), plus the shipped-asset tests in `tests/EditMode/MobSpawning/MobSpawning_StartingZoneTable_tests.cs` (validator, gradient) and the NavMesh check in PlayMode

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 005
- Unlocks: Story 008
