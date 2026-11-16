# Story 001: Spawn Config and ZoneSpawnTable Data

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-2 (SpawnPoint definition, 150 invariant, authoring convention), CR-MS-9 / F-MS-1 (tier delays), Tuning Knobs, OQ-MS-1
**Requirement**: `TR-ms-001`, `TR-ms-005` (knob data only)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**Readiness decisions (2026-11-16, user)**:
1. **Table home**: `ZoneDefinition` gets a serialized `_spawnTable` field (a reference to a separate `ZoneSpawnTable` asset) with a read-only `SpawnTable` property. Story 005 reads it through `ZoneDefinitionSlot`; Story 007 authors the asset and assigns it on `ZoneDefinition_StartingZone`.
2. **Bad entry → whole table fails**: any invalid entry (duplicate ID, unknown mob, out of bounds, NaN, negative or boss override) fails the whole table, like the CR-MS-2 cap rule. The validator checks **every** entry and logs each failure before returning false, so one run shows every authoring mistake.

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: Feature services and their configs live in `IronGrind.ServerLogic`; nothing here goes in `IronGrind.Foundation` (no client consumer).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: ScriptableObject assets with serialized private fields; Unity 6.3 rejects `[SerializeField]` on properties (fields only).

**Control Manifest Rules (this layer)**:
- Required: feature configs are `IronGrind.ServerLogic` — ADR-012
- Required (coding standards): gameplay values data-driven, never hardcoded; doc comments on public APIs
- Forbidden: none specific to this story

---

## Acceptance Criteria

- [ ] **AC-1 Config asset**: `MobSpawningConfig` (ScriptableObject, `src/ServerLogic/MobSpawning/`) holds `RESPAWN_DELAY_COMMON_TICKS` 60, `RESPAWN_DELAY_UNCOMMON_TICKS` 120, `RESPAWN_DELAY_RARE_TICKS` 300, `RESPAWN_DELAY_ELITE_TICKS` 600, `SPAWN_DENSITY_RETRY_INTERVAL_TICKS` 20 (defaults as constants in a `MobSpawningConfigValues` class, pattern of `EnemyAIConfigValues`).
- [ ] **AC-2 Config validation**: each knob is checked against its GDD safe range (Common [20, 300], Uncommon [60, 600], Rare [120, 1200], Elite [300, 3600], retry [10, 200]); the tier order Common < Uncommon < Rare < Elite holds (GDD: "Must be > `RESPAWN_DELAY_COMMON_TICKS`"). An invalid config logs one error naming the knob and value and is refused (validator returns false).
- [ ] **AC-3 Delay lookup**: `RespawnDelayForTier(MobTier)` returns the configured value for each of the 4 tiers; an undefined enum value logs an error and returns the Common delay.
- [ ] **AC-4 Spawn table asset**: `ZoneSpawnTable` (ScriptableObject) holds the zone's `ZoneTemplateID` and a `SpawnPointEntry[]` (`SpawnPointID: uint`, `MobTypeID`, `Position: Vector3`, `RespawnDelayOverrideTicks: int`). `ZoneDefinition` gains a serialized `_spawnTable` field and a read-only `SpawnTable` property (null when not assigned); existing `ZoneDefinition` tests still pass.
- [ ] **AC-5 Table validation (CR-MS-2)**: a table with more than `MAX_MOBS_PER_ZONE` (`ZoneSlotPool.MAX_MOBS_PER_ZONE`, not redefined) entries fails with `SpawnTableOverCapacity` logged; exactly 150 passes. The table also fails when any entry has: a duplicate `SpawnPointID`; a `MobTypeID` unknown to `IMobDefinitionRegistry`; a position outside the walkable bounds on any axis (inclusive: a point exactly on `WalkableBoundsMin` / `Max` passes; bounds passed in as two `Vector3`s, from `ZoneDefinition`); NaN / Infinity in a position; a negative override; or the table's `ZoneTemplateID` differs from the zone's. **Every** failing entry is logged (with its `SpawnPointID` and reason) before the validator returns false. An empty table is valid (logs one warning).
- [ ] **AC-6 Boss guard (OQ-MS-1)**: an entry with `RespawnDelayOverrideTicks > 0` logs `BossSpawnPointNotSupported` with its `SpawnPointID` and fails the table (decision 2) until OQ-MS-1 is decided. The override field stays in the schema.
- [ ] **AC-7 Assembly**: all new types are in `IronGrind.ServerLogic`; the ADR-012 boundary test still passes.

---

## Implementation Notes

- Follow `src/ServerLogic/EnemyAI/EnemyAIConfig.cs` / `EnemyAIConfigValues.cs` / `MobDefinitionValidator.cs` for structure (serialized private fields, a separate pure validator class testable without assets).
- Validation is a pure function over the entries plus the registry and bounds, so Story 005 can call it at T-1 and tests can call it without a scene.
- `MAX_MOBS_PER_ZONE`: use `ZoneSlotPool.MAX_MOBS_PER_ZONE` (as `NavigationConfigValues` does). Bounds: `ZoneDefinition.WalkableBoundsMin` / `WalkableBoundsMax`; the validator takes them as parameters, not the asset.
- Table home: `ZoneDefinition._spawnTable` (decision 1). Do not assign it on `ZoneDefinition_StartingZone` here (Story 007); tests build tables and definitions in code (`ScriptableObject.CreateInstance`).
- **Performance**: load-time only (zone T-1); no per-tick cost. No performance budget applies.

---

## Out of Scope (additions at readiness)

- Loading `MobSpawningConfig` at startup (which S2 loader, asset path) — Story 006, alongside the spawner build at S5

---

## Out of Scope

- Story 005: calling the validator at T-1, NavMesh validation, failing the zone
- Story 007: the starting-zone spawn table content
- Boss / event spawn mechanic (OQ-MS-1)

---

## QA Test Cases

*Lean mode (QL-STORY-READY skipped): written at `/create-stories` 2026-11-16; refine at `/story-readiness`.*

1. **Defaults (AC-1)**: a fresh config passes validation and returns 60 / 120 / 300 / 600 / 20.
2. **Range boundaries (AC-2)**: each knob at its min and max passes; min − 1 and max + 1 fail with one error naming the knob.
3. **Tier order (AC-2)**: Uncommon = Common (both in range) fails; Common 60, Uncommon 61 passes.
4. **Lookup (AC-3)**: each tier returns its value; `(MobTier)99` logs an error and returns Common.
5. **Cap (AC-5)**: 150 entries pass; 151 fail with `SpawnTableOverCapacity` (`LogAssert`).
6. **Bad entries (AC-5)**: duplicate ID, unknown MobTypeID (fake registry returns null), position 1 m outside bounds on X, then on Y, then on Z, negative override, NaN position, wrong `ZoneTemplateID`: each fails the table and logs the entry's `SpawnPointID`.
7. **All errors reported (AC-5, decision 2)**: a table with 3 different bad entries → 3 error logs (`LogAssert` each), returns false.
8. **Bounds inclusive (AC-5)**: a point exactly on `WalkableBoundsMin` and one exactly on `WalkableBoundsMax` → pass.
9. **Empty table (AC-5)**: passes with one warning.
10. **Boss guard (AC-6)**: override 200 → table fails with `BossSpawnPointNotSupported`.
11. **ZoneDefinition field (AC-4)**: a new `ZoneDefinition` has `SpawnTable == null`; after assigning a table, the property returns it.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_ConfigAndTable_tests.cs` — must exist and pass

**Status**: [x] Created — 43/43 passing (2026-11-16)

---

## Dependencies

- Depends on: None (Enemy AI registry and `MobTier` exist)
- Unlocks: Story 002, Story 005, Story 007

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 7/7 passing (AC-1..AC-6 by `MobSpawning_ConfigAndTable_tests.cs`, AC-7 by `AssemblyBoundary_Tests`)
**Deviations** (advisory):
1. The config refuses out-of-range knobs (as the story says); Enemy AI's config clamps instead, so the two patterns differ on purpose.
2. A table over 150 points fails at once without per-entry errors.
3. Code review additions: a null entries list, a null registry and non-finite or inverted bounds each fail once before any entry is checked.
4. `ZoneInstancing` ↔ `MobSpawning` namespaces reference each other (same assembly, no asset cycle).
5. Internal test setters: `ZoneDefinition.SetSpawnTableForTests`, `ZoneSpawnTable.SetForTests`, `MobSpawningConfig.SetForTests`.
6. For Story 007: `SpawnPointEntry` serializes the mob type as `uint _mobTypeId` (`MobTypeID` is not Unity-serializable); `ZoneSpawnTable.Entries` is an `IReadOnlyList`.
**Tech debt**: none.
**Test Evidence**: Logic — `tests/EditMode/MobSpawning/MobSpawning_ConfigAndTable_tests.cs` 43/43; batch EditMode `MobSpawning|EnemyAI|ZoneDefinition|AssemblyBoundary` 572/572, 0 `error CS` (2026-11-16 18:15:54Z).
**Code Review**: Complete — CHANGES REQUIRED (0 blocking) → all fixes and suggestions applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
