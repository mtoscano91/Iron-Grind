# Story 015: Production Zone Entity State Source (TD-100)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-11-11 from quick spec `design/quick-specs/zone-entity-state-source-2026-11-11.md`; `/story-readiness` 2026-11-11: NEEDS WORK (6 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 5 hours (quick spec)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 step 6 (snapshot) and step 9 (`PlayerJoinedZone`); `design/gdd/networking-wire-protocol.md` — `EntityState` (`isInCombat` defined 2026-11-11)
**Quick Spec**: `design/quick-specs/zone-entity-state-source-2026-11-11.md` (rules 1–10)

> **CR-ZI-8 step 6**: "`ZoneStateSnapshot` assembled and emitted to joining client … Snapshot includes all current player **and** mob EntityState entries. … Player entries carry all EntityState fields. Mob entries carry `MobTypeID : ushort` in place of `characterName`, and omit character-specific fields (level, equipmentAppearanceFlags)."
> **CR-ZI-8 step 9**: "`PlayerJoinedZone` broadcast to all existing zone clients (R-OD)."
> **`EntityState.isInCombat`** (`networking-wire-protocol.md`, defined 2026-11-11): "true while the entity's auto-attack is engaged with a target (AutoAttackState CombatActive or OutOfRange); false when Idle or Prohibited."

Today neither step carries real data: the composition's stand-in source returns false for every entity, so the snapshot is empty and `PlayerJoinedZone` is never sent (TD-100).

**Requirement**: `TR-zi-007` (CR-ZI-8 steps 3–10), `TR-zi-008` (CR-ZI-8 step 6, EntityState entries) — defined in `production/epics/zone-instancing/EPIC.md`. **Not in `docs/architecture/tr-registry.yaml`**: known gap TD-098 (accepted 2026-11-11 at readiness; `/story-done`'s registry lookup will find nothing for them).
**Tech Debt**: closes **TD-100** (P1, before Authentication Story 017)

**ADR Governing Implementation**: ADR-009 Amendment 1 (the composition root is the only place that constructs server systems); ADR-004 (`ZoneStateSnapshotFragment`, `PlayerJoinedZone` carry `EntityState`); ADR-012 (`IronGrind.ServerLogic`)
**ADR Decision Summary**: the composition builds one production `IZoneEntityStateSource` that reads each field from the system that owns it; `MovementZoneEntityStateSource` keeps overwriting position.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (plain C#; `Mathf.FloorToInt` only)
**Engine Notes**: N/A — no post-cutoff engine API involved (plain C# over existing server systems; `Mathf.FloorToInt` is stable). Code constraint: no LINQ, boxing or string building per call (AC-7).

**Control Manifest Rules (this layer)**:
- Required: the composition root is the only place that constructs server systems — ADR-009 A1
- Required: dependencies through the constructor, no singletons — coding standards
- Required: `IronGrind.ServerLogic` — ADR-012
- Forbidden: float in authoritative wire state — CR-NET-7.2

---

## Acceptance Criteria

- [ ] **New source** (location decided at readiness 2026-11-11): `ZoneEntityStateSource : IZoneEntityStateSource` in `src/ServerLogic/ZoneServer/ZoneEntityStateSource.cs` — it reads from Character Stats, Equipment, Persistence and Auto-Attack, so it is a composition adapter, not core Zone Instancing (`ZoneInstancing/` stays free of combat dependencies; one assembly, so no asmdef change). Constructor-injected with `ZoneSlotPool`, `ZoneMobRoster`, Character Stats, `ICharacterNameLookup`, `IEquipmentService` and `AutoAttackCombatSystem` (or narrow interfaces over them); a null dependency → `ArgumentNullException` (rule 10). Main thread only.
- [ ] **AC-1 (players)**: the rules — the `EntityID` must be an occupied player slot (gives the `CharacterID`); `CurrentHp` / `CurrentMp` = `GetCurrentHP` / `GetCurrentMP` rounded **down** to `int`; `MaxHp`, `MaxMp`, `Level` = `GetEffectiveStat(entity, StatID.MaxHP / MaxMP / Level)`; `CharacterName` = `ICharacterNameLookup.TryGetCharacterName(CharacterID)`; `EquipmentAppearanceFlags` = `IEquipmentService.GetAppearanceFlags(CharacterID)`; position / rotation / facing left zero for `MovementZoneEntityStateSource` to overwrite. Test: an entered player "Brakka" with level 7, 80 / 100 HP and 30 / 40 MP appears in the joining client's snapshot with exactly those values and its appearance flags, and other clients receive `PlayerJoinedZone` with name "Brakka", level 7 and the player's position (Movement decorator).
- [ ] **AC-2 (never send a nameless player)**: when the name lookup returns false (record not applied yet, or released), `TryGetPlayerState` returns false; the builder skips the entry with its one existing warning, and no entry with an empty name is sent.
- [ ] **AC-3 (`isInCombat`)**: true when `AutoAttackCombatSystem.GetState(entity)` is `CombatActive` or `OutOfRange`; false for `Idle` and `Prohibited` — one test case per state, for players and for mobs.
- [ ] **AC-4 (rounding, cycle timer, respawn)**: current HP 79.6 is sent as 79 (`Mathf.FloorToInt`; no float in authoritative wire state, CR-NET-7.2); `CycleTimer` equals `CycleTimerBroadcastStep.NormalizeCycleTimer(GetActionTimer(entity), GetCycleTicks(entity))` for the same tick (the value `CycleTimerBroadcast` sends); `RespawnTick` is 0 (Death & Respawn does not exist yet).
- [ ] **AC-5 (mobs)**: the `EntityID` must be in `ZoneMobRoster` (`TryGetMob`); `CurrentHp` / `MaxHp`, `IsInCombat` and `CycleTimer` follow AC-1, AC-3, AC-4. Test: a roster mob appears in the snapshot with its current and max HP; an `EntityID` that is neither an occupied player slot nor a roster mob returns false from both methods.
- [ ] **AC-6 (composition, TD-100)**: `ZoneServerComposition` no longer constructs `LoggingZoneEntityStateSource` (its stand-in log line does not appear in a composition test); `ZoneServer_CompositionHandshake_tests.cs` also asserts `EntityState.characterName == "Brakka"` for the entered player, closing AC-AUTH-21 (e). `EntityStateSourceOverride` still replaces the inner source for tests. TD-100 marked RESOLVED in `docs/tech-debt-register.md`.
- [ ] **AC-7 (no regression, rule 10)**: existing `ZoneSnapshotBuilder`, `ZoneEntryCompletion` and Movement state-source tests pass; `TryGetPlayerState` / `TryGetMobState` allocate nothing per call (the suite's existing zero-allocation assertion).
- [ ] **GDD note**: `design/gdd/zone-instancing.md` CR-ZI-8 step 6 `EntityState` notes state rule 6 (`isInCombat`), matching `networking-wire-protocol.md` line ~1423.

---

## Implementation Notes

- **Construction order (decided at readiness 2026-11-11):** at `ZoneServerComposition.cs` ~1703 (`BuildOutboundAndPipeline`) the state source is built *before* `ZoneInstancingWiring` (slot pool, mob roster) and before `BuildAutoAttackCombat`. Character Stats, Equipment and `CharacterPersistenceService` (the `ICharacterNameLookup`) already exist there. Decision: follow the `ForwardingZoneEntryResponder` pattern — a new `ForwardingZoneEntityStateSource` (`src/ServerLogic/ZoneServer/`) is the inner source of `MovementZoneEntityStateSource`; it returns false until bound; the composition binds the real `ZoneEntityStateSource` right after `BuildAutoAttackCombat`, before S9 (no client can enter before Active, so nothing reads an unbound source in production). Binding twice throws. With `EntityStateSourceOverride` set, the override is the inner source and no forwarding source is built.
- `ZoneInstancingWiring.cs:346` keeps its `LoggingZoneEntityStateSource` default for wiring-only tests; only the composition default changes.
- Name: `ICharacterNameLookup.TryGetCharacterName` returns the cached session string — no allocation (Story 007 rule).
- HP / MP: `GetCurrentHP` / `GetCurrentMP` (float) → `Mathf.FloorToInt`; `MaxHP`, `MaxMP`, `Level` via `GetEffectiveStat(entity, StatID.*)`.
- Appearance: `IEquipmentService.GetAppearanceFlags(CharacterID)` (the `CharacterID` comes from the occupied player slot).
- Combat: `AutoAttackCombatSystem.GetState(EntityID)`; cycle timer from `GetActionTimer` / `GetCycleTicks` through `NormalizeCycleTimer` — the same value `CycleTimerBroadcast` sends.
- **Performance**: no allocation per call (AC-7, `Is.Not.AllocatingGCMemory()` as in `AutoAttackCombat_CycleBroadcast_tests.cs`). Called once per entity per snapshot build and once per `PlayerJoinedZone`, on the main thread, off the per-tick path (entry and `ZoneSnapshotRequest` only); each call is a fixed number of O(1) lookups. No separate budget — it sits inside the existing `ZoneSnapshotBuilder` cost (Story 007); no measurable tick impact expected.

---

## Out of Scope

- `RespawnTick` from Death & Respawn (rule 7 placeholder; that system takes the field over).
- TD-101 (zone-side session end inputs, `EvaluateTimeouts`).
- Relevance filtering of who receives `PlayerJoinedZone` (unchanged).

---

## QA Test Cases

- **AC-1..AC-5, AC-7** — unit tests on `ZoneEntityStateSource` with a real `ZoneSlotPool` / `ZoneMobRoster` and fakes (or real instances) for stats, name lookup, equipment and auto-attack; snapshot / `PlayerJoinedZone` assertions through `ZoneSnapshotBuilder` and `ZoneEntryCompletion` with the Movement decorator.
- **AC-6** — composition with the fake backend: handshake → load "Brakka" → snapshot `EntityState.characterName == "Brakka"`; no `LoggingZoneEntityStateSource` log line.
- **Allocation** — the suite's existing zero-allocation helper around both methods.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/ZoneInstancing/ZoneEntityStateSource_tests.cs` (new) — AC-1..AC-5, AC-7, plus the forwarding source (false until bound, binds once)
- `tests/EditMode/Networking/ZoneServer_CompositionHandshake_tests.cs` — AC-6 name assertion

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 007, 010, 014 (Complete); Gateway Story 012 (Complete); Character Stats, Equipment, Auto-Attack Combat, Movement (in the composition)
- Unlocks: Authentication Story 017 (with TD-101)
