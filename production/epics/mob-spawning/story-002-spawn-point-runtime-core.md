# Story 002: Spawn Point Runtime Core — T-2 Fill and MobDied

> **Epic**: Mob Spawning
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/mob-spawning.md` — CR-MS-3 (runtime SpawnPoint), CR-MS-4 transitions 1, 2, 3, CR-MS-5 (MobEventBus), CR-MS-7 (T-2 sweep), Edge Cases (unknown / duplicate `MobDied`)
**Requirement**: `TR-ms-002`, `TR-ms-003`, `TR-ms-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Per-zone `MobEventBus` (`readonly struct` args, named-method subscriptions); Mob Spawning subscribes to `MobDied` and raises `MobSpawned`. Calls into Zone Instancing are direct calls on the injected `IZoneMobRoster`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C# in `IronGrind.ServerLogic`; `Vector3` only as data).

**Control Manifest Rules (this layer)**:
- Required: `readonly struct` event args (`MobSpawnedArgs`, `MobDiedArgs` already are) — ADR-010
- Forbidden: an `EventBus` class; class-typed event args; `async`/`await` in tick code — ADR-010, ADR-011
- Required: tick thread only; no yield between `GetMobCount` and `TryAddMob` (CR-MS-3)

---

## Acceptance Criteria

- [x] **AC-1 Runtime state (CR-MS-3)**: `SpawnPointState` enum {Uninitialized, Live, Dying, Respawning, Deferred, Cancelled}; a runtime `SpawnPoint` **struct** (`State`, `CurrentEntityID`, `TimerTick`, plus the authored entry index) in an array pre-allocated at `MAX_MOBS_PER_ZONE`. `CurrentEntityID` is `EntityID.Invalid` outside Live/Dying; `TimerTick` is 0 outside Dying/Respawning.
- [x] **AC-2 Construction**: `MobSpawningService` (name is the developer's choice) is built per zone from the zone's `ZoneID`, the validated spawn entries (Story 001), `IZoneMobRoster`, `MobEventBus`, `IMobDefinitionRegistry`, `MobSpawningConfigValues` (the validated struct from Story 001, not the `MobSpawningConfig` asset) and an `int deathLingerTicks` (the caller passes `EnemyAIConfig.DeathLingerTicks`); every point starts Uninitialized; it subscribes to `MobEventBus.OnMobDied` with a named method.
- [x] **AC-3 T-2 fill (AC-MS-2)**: `PopulateOnActive()` with 10 points and `GetMobCount = 0` calls `TryAddMob(zone, type, position)` exactly 10 times in declaration order, each preceded by `GetMobCount`; all 10 points Live with the returned EntityID; `MobSpawned(zone, id, type, position)` raised 10 times; no point left Uninitialized. A second call is a no-op (logs one warning).
- [x] **AC-4 Fill at cap (AC-MS-3, first half)**: roster pre-filled to 150 and 5 points → no `TryAddMob`; all 5 points Deferred and on the deferred list (the retry half is Story 003).
- [x] **AC-5 MobDied (AC-MS-4)**: `MobDied(zone, E)` for a Live point with `CurrentEntityID = E` → Dying, `TimerTick = DEATH_LINGER_TICKS` (from the Enemy AI config, not redefined), `CurrentEntityID` still E, no `RemoveMob`.
- [x] **AC-6 Unknown mob (AC-MS-11)**: `MobDied` for an EntityID that no Live point holds → no state change, no timer, no `RemoveMob`, no log above Info (expected for scripted mobs).
- [x] **AC-7 Duplicate (AC-MS-12)**: a second `MobDied` for a point already Dying → `TimerTick` unchanged.
- [x] **AC-8 Foreign zone**: `MobDied` carrying another ZoneID → ignored with one warning.
- [x] **AC-9 EntityID lookup**: finding the point for a `MobDied` EntityID does not allocate (an `EntityID → index` map filled on spawn, or a scan of ≤ 150 — developer's choice).
- [x] **AC-10 Invalid id during T-2 fill (AC-MS-13, T-2 half)**: `TryAddMob` returns `EntityID.Invalid` after a passing `GetMobCount` → point Deferred, added to the deferred list once, no `MobSpawned`, never Live with an Invalid id; the sweep continues with the next point. The `UnexpectedTryAddMobFailure` log and the respawn / retry paths are Story 003 AC-6.

---

## Implementation Notes

- `IZoneMobRoster` (`src/ServerLogic/ZoneInstancing/IZoneMobRoster.cs`): `TryAddMob` returns `EntityID.Invalid` at cap or on failure; `GetMobCount(zone)`; `RemoveMob(zone, id)`.
- `MobEventBus.RaiseMobSpawned(in MobSpawnedArgs)`; subscribers run in subscription order (runtime store before Enemy AI — Enemy AI Story 013 note), so raising synchronously from the T-2 sweep gives every new mob its stats and controller in the same call.
- `DEATH_LINGER_TICKS`: `EnemyAIConfig.DeathLingerTicks` (default 30). Pass the value in; do not read the Enemy AI config asset from Mob Spawning code.
- The deferred list is a `List<int>` of point indices, pre-allocated at 150 (CR-MS-6). This story only adds to it. Deliberate deviation from CR-MS-4 row 2 (which names `SpawnPointID`): indices give O(1) access to the point; behavior is the same. The `SpawnPointID` is still available through the entry index for logs.
- An invalid `TryAddMob` during the T-2 fill: Deferred only (AC-10), written so Story 003 can wrap it in the shared guard that adds `UnexpectedTryAddMobFailure`.
- **Performance**: `PopulateOnActive` runs ≤ 150 iterations, once per zone activation. It and the `MobDied` handler allocate nothing after construction (AC-9). No other per-tick cost in this story.

---

## Out of Scope

- Story 003: per-tick countdowns, Dying → Respawning, `RemoveMob`, respawn delay, deferred retry
- Story 004: `CancelAllSpawnTimers`, unsubscribe
- Story 005: loading the table at T-1 and NavMesh validation
- Story 006: calling `PopulateOnActive` from `MarkActive`

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-16; refine at `/story-readiness`. Use a fake `IZoneMobRoster` with a call log and a settable count, and a real `MobEventBus` with counting subscribers.*

1. **Initial state (AC-1, AC-2)**: after construction all points Uninitialized, EntityID Invalid, TimerTick 0; one `OnMobDied` subscriber added.
2. **T-2 fill (AC-3)**: 10 points, count 0 → call log alternates `GetMobCount`, `TryAddMob` ×10 in declaration order; 10 `MobSpawned` with matching id/type/position; all Live.
3. **Second fill (AC-3)**: `PopulateOnActive` again → no roster calls, one warning.
4. **Fill at cap (AC-4)**: count 150, 5 points → 0 `TryAddMob`, 5 Deferred, deferred list size 5, 0 `MobSpawned`.
5. **Partial cap**: count 148, 5 points → 2 Live, 3 Deferred (fake count increments on each add).
6. **MobDied (AC-5)**: Live point E → Dying, TimerTick 30, EntityID E, no `RemoveMob`.
7. **Unknown (AC-6)**: `MobDied` for an id never spawned → snapshot of all points unchanged; no `LogAssert` warning/error.
8. **Duplicate (AC-7)**: two `MobDied(E)` → TimerTick still 30.
9. **Foreign zone (AC-8)**: other ZoneID → unchanged, one warning.
10. **No allocation (AC-9)**: `MobDied` handling after warm-up → `GC.GetAllocatedBytesForCurrentThread` delta 0.
11. **Invalid id at T-2 (AC-10)**: 3 points, fake returns Invalid for the 2nd with count 0 → points 1 and 3 Live, point 2 Deferred, deferred list size 1, 2 `MobSpawned`, point 2 `CurrentEntityID` Invalid.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MobSpawning/MobSpawning_RuntimeCore_tests.cs` — must exist and pass

**Status**: [x] Created — 23/23 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 003, Story 005

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 10/10 passing (all by `tests/EditMode/MobSpawning/MobSpawning_RuntimeCore_tests.cs`, 23/23; batch EditMode 1208/1208)
**Deviations** (advisory):
1. The deferred list holds point indices, not `SpawnPointID`s (deliberate deviation from CR-MS-4 row 2, stated in Implementation Notes).
2. The constructor does more than AC-2 asks: it rejects `ZoneID.Invalid`, `deathLingerTicks <= 0` and more than 150 entries, and copies the entries into its own array.
3. AC-2 "one subscriber" is proved by behavior (a death moves the point to Dying; two services on one bus each react only to their own zone); `MobEventBus` exposes no handler count.
4. `IMobDefinitionRegistry` and `MobSpawningConfigValues` are injected but not used until Story 003.
**Test Evidence**: Logic: `tests/EditMode/MobSpawning/MobSpawning_RuntimeCore_tests.cs`
**Code Review**: Complete (unity-specialist + qa-tester; all fixes applied 2026-11-16). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
