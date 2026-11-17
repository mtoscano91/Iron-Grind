# Story 007: Starting-Zone Spawn Table Content

> **Epic**: Mob Spawning
> **Status**: Complete
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

- [ ] **AC-1 Asset**: `Assets/data/Zones/ZoneSpawnTable_StartingZone.asset` (a `ZoneSpawnTable`, `_templateId` 0 = StartingZone), assigned to `ZoneDefinition_StartingZone`'s `_spawnTable` field (Story 001 decision 1).
- [ ] **AC-2 Story 001 validation**: the asset passes the table validator:
  - ≤ 150 points, unique IDs, known MobTypeIDs 1–4 only, override 0 on every point;
  - inside the zone's walkable bounds (±50 m);
  - authoring margin (readiness 2026-11-16): every point has y = 0 and |x|, |z| ≤ 47 m, at least 2 m inside the baked NavMesh edge (≈ ±49.3 m).
- [ ] **AC-3 NavMesh**: every point passes CR-NAV-13 on the baked NavMesh (0 excluded), checked with `SpawnPointValidator` in a PlayMode test against the real mesh.
- [ ] **AC-4 Layout (CR-MS-2 convention, sized for this zone; readiness 2026-11-16)**:
  - **Count**: 48 points (20 Common = 42%, 14 Uncommon, 9 Rare, 5 Elite) in 11 clusters (5 Common, 3 Uncommon, 2 Rare of 4 and 5, 1 Elite).
  - **Why not 150**: the GDD's ~60-of-150 Common figure assumes zones up to ±250 m. This zone is 100 × 100 m, and 150 points would be about one mob per 67 m² against 6–9 m aggro ranges. 48 gives about one mob per 200 m², well under the 150 cap.
  - **Cluster rule**: every point has at least 3 other points within 15 m.
  - **Gradient**: measured from the zone origin (0, 0, 0). That is the current town respawn point default; `_townRespawnPointAuthored` is 0, and if Death & Respawn later authors a real town point, this table may need re-checking. Each tier's mean distance from the origin is strictly greater than the previous tier's, Common → Elite.
  - **Common share**: 35–45%.
  - **Record**: the per-tier counts and mean distances go in the Completion Notes.
- [ ] **AC-5 Tier clearance**: the minimum distance from any Common point to any Rare or Elite point is greater than the larger tier's `AggroRange` (Elite: 9 m). A player fighting in the Common band is then not aggroed by a Rare or Elite mob standing at its spawn point. The measured minimum distances are recorded in the Completion Notes.
- [ ] **AC-6 Smoke check (readiness option a)**:
  - **Log line**: after the T-2 sweep, the composition logs one info line: `[MobSpawning] zone <id>: <live> of <total> points Live, <deferred> Deferred`. It comes from a small public read-only count on `MobSpawningService` or the composition; the zone's only Mob Spawning log on a clean start.
  - **Automated test**: an EditMode test asserts the line.
  - **Smoke file**: `production/qa/smoke-2026-11-*-mob-spawning-007.md` records a real zone host start (server build plus backend, run by the user as in `smoke-2026-11-13-navigation-010.md`). The zone reaches Active with no S6 failure, and the zone log shows `48 of 48 points Live, 0 Deferred`.

---

## Implementation Notes

- Town respawn point: `ZoneDefinition` `_townRespawnPoint` (starting zone asset). Walkable bounds: `_walkableBoundsMin/Max`.
- A small Editor or EditMode helper that prints per-tier counts and mean distances is enough evidence for AC-4; keep any such helper under `tests/` or an Editor folder, not in `src/ServerLogic`.
- If the baked NavMesh is too small for a readable gradient, record it and ask before enlarging the scene.
- **Authoring (readiness 2026-11-16)**: write the asset YAML directly; no Editor tool.
  - Use the serialized `ZoneSpawnTable` fields: `_templateId`, `_entries` with `_spawnPointId`, `_mobTypeId`, `_position`, `_respawnDelayOverrideTicks`.
  - Write a new `.meta` with a fresh GUID, and point `ZoneDefinition_StartingZone`'s `_spawnTable` at `{fileID: 11400000, guid: <that GUID>, type: 2}`.
  - The EditMode tests load the shipped assets through `AssetDatabase`, which proves they import.
- **Suggested layout**: Common clusters on a ring of about 10–18 m around the origin, keeping a clear radius of about 4 m at the origin so players don't enter on top of a mob. Uncommon at about 22–30 m, Rare at about 33–38 m, Elite in one corner at about 42–46 m (diagonal). Then check AC-5 clearance: Common to Rare/Elite > 9 m.
- **Log line (AC-6)**: a tiny code change in a Config/Data story, accepted at readiness because it is the smoke check's only observable and Story 008 reuses it.

---

## Out of Scope

- New mob types / roster rows (Enemy AI data)
- Boss or event points (OQ-MS-1)
- Spawn VFX / audio (presentation epics)
- Pursuit and leash overlap (accepted at readiness 2026-11-16): a pursuing Elite (leash 18 m) can still be pulled into the Common band; AC-5 covers only mobs standing at their spawn points
- Authoring a real town respawn point (Death & Respawn)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Validator (AC-1, AC-2)**:
   - Load `ZoneDefinition_StartingZone` in EditMode; its `SpawnTable` is the shipped table, with `TemplateId` StartingZone.
   - Run the Story 001 validator against the shipped `MobRoster` registry and the definition's bounds: passes, nothing logged above Info.
   - Every point has y = 0 and |x|, |z| ≤ 47.
2. **NavMesh (AC-3)**: in PlayMode, load the baked mesh with `AssetDatabase.LoadAssetAtPath<NavMeshData>` and add it with `NavMesh.AddNavMeshData`, as `Navigation_UnityAdapter_tests.cs` does. `SpawnPointValidator.Validate` then returns valid = total = 48.
3. **Gradient (AC-4)**:
   - Exactly 48 points, split 20 / 14 / 9 / 5.
   - Per-tier mean distance from the origin strictly increases Common → Elite.
   - Every point has at least 3 others within 15 m.
4. **Clearance (AC-5)**: min distance from Common to Rare and Common to Elite are each > 9 m.
5. **Log line (AC-6)**: an EditMode composition rig (pattern of `MobSpawning_Composition_tests.cs`) with a 5-point table, after start, logs exactly `[MobSpawning] zone <id>: 5 of 5 points Live, 0 Deferred`.
6. **Smoke (AC-6)**: a real host start logs `48 of 48 points Live, 0 Deferred`, with no S6 failure.

---

## Test Evidence

**Story Type**: Config/Data
**Required evidence**: the smoke check note (`production/qa/smoke-2026-11-*-mob-spawning-007.md`), plus:
- shipped-asset tests in `tests/EditMode/MobSpawning/MobSpawning_StartingZoneTable_tests.cs` (QA 1, 3, 4);
- the log-line test in `tests/EditMode/MobSpawning/MobSpawning_Composition_tests.cs` (QA 5);
- the NavMesh check in `tests/PlayMode/MobSpawning/MobSpawning_StartingZoneNavMesh_tests.cs` (QA 2).

**Status**: [x] Created — EditMode 9 cases + log-line test, PlayMode 1 test, smoke PASS

---

## Dependencies

- Depends on: Story 001, Story 005
- Unlocks: Story 008

---

## Completion Notes
**Completed**: 2026-11-17
**Criteria**: 6/6 passing (none deferred)
**Layout (AC-4, AC-5)**: 48 points in 11 clusters (`Assets/data/Zones/ZoneSpawnTable_StartingZone.asset`):

| Tier | Points | Clusters | Mean distance from origin | Min distance from Common | AggroRange |
|---|---|---|---|---|---|
| Common | 20 (41.7%) | 5 | 12.91 m | — | 6 m |
| Uncommon | 14 | 3 | 26.32 m | — | 7 m |
| Rare | 9 | 2 (5 + 4) | 35.63 m | 18.51 m | 8 m |
| Elite | 5 | 1 (corner) | 44.30 m | 28.84 m | 9 m |

Rationale: the zone is 100 × 100 m (bounds ±50, NavMesh edge ≈ ±49.3). The GDD's ~60-of-150 Common figure assumes zones up to ±250 m; 48 points is about one mob per 200 m² against 6–9 m aggro, well under the 150 cap. Every point at y 0, |x|,|z| ≤ 34, override 0.
**Deviations**:
- Advisory: a small code change in a Config/Data story (accepted at readiness): `MobSpawningService.LiveCount` (internal) and the composition's `PopulateMobSpawner()` logging `[MobSpawning] zone <id>: <live> of <total> points Live, <deferred> Deferred` once after T-2.
- Advisory: the gradient is measured from the zone origin because no town respawn point is authored (`_townRespawnPointAuthored` 0); re-check this layout if Death & Respawn authors one.
**Test Evidence**: Config/Data: smoke `production/qa/smoke-2026-11-17-mob-spawning-007.md` (PASS: real host start, `48 of 48 points Live, 0 Deferred`, idle-close teardown, quit(0)); plus `tests/EditMode/MobSpawning/MobSpawning_StartingZoneTable_tests.cs` (9 cases), the log-line test in `MobSpawning_Composition_tests.cs`, `tests/PlayMode/MobSpawning/MobSpawning_StartingZoneNavMesh_tests.cs` (48/48 on the real mesh). EditMode `!Database` 7834/7834; PlayMode 20/20.
**Code Review**: Complete (`/code-review` 2026-11-17: APPROVED)
