# Story 002: ApplyEffect Validation

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28: NEEDS WORK (2 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-2, CR-SE-3 (`ApplyEffect`), F-SE-1 validation, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6; AC-SE-03, 04, 06, 07, 08, 10, 17
**Requirement**: `TR-se-002`, `TR-se-003`

**ADR Governing Implementation**: ADR-012 (assembly placement)
**ADR Decision Summary**: the service is server-only, in `IronGrind.ServerLogic`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Performance**: not on the per-tick path; one scan of up to `MAX_ACTIVE_BUFFS_PER_ENTITY` (8) rows and one occupancy read per call. No allocation per call (the arrays are created once in the constructor).

**Control Manifest Rules (this layer)**:
- Required: server-authoritative core logic in `IronGrind.ServerLogic` — ADR-012
- Required: dependencies injected through constructor interfaces (coding standards: DI over singletons)

---

## Acceptance Criteria

*From GDD `design/gdd/status-effects.md`, scoped to this story. "No change" means: no `IBuffModifierLayer` call (Story 003's seam; a recording fake here), no row created, occupancy unchanged.*

- [ ] **Check order (EC-SE-6, extended)**: `ApplyEffect(target, caster, def)` checks, in this order, returning the first failure with no change: (1) definition shape — `NoOpDefinition` (F-SE-2: `ModifierCount == 0` and both regen rates `== 0`), `InvalidDuration` (`DurationTicks == 0` or `> MAX_DURATION_TICKS`), `InvalidStat` (any populated modifier on `Level`, `Experience`, `CurrentHP` or `CurrentMP`); (2) dead target — `InvalidTarget` while the entity's effects are `PendingRemoval` (EC-SE-2, EC-SE-3); (3) row availability — `EffectSlotsFull` when all `MAX_ACTIVE_BUFFS_PER_ENTITY` rows are taken by other `BuffID`s; (4) modifier capacity — `CapacityExceeded` when `OccupiedSlots + def.ModifierCount > 32`. A refresh of an existing `BuffID` skips (3) and (4) (Story 004 handles it).
- [ ] **AC-SE-06**: `ModifierCount 0`, both regen `0` → `NoOpDefinition`, no change.
- [ ] **AC-SE-07**: `DurationTicks 0` → `InvalidDuration`; `DurationTicks 1` → `Success`. Also `6001` → `InvalidDuration` (authoring ceiling, F-SE-1).
- [ ] **AC-SE-08**: four calls, one modifier each on `Level`, `Experience`, `CurrentHP`, `CurrentMP` → `InvalidStat` each, no change. A prohibited stat at index 2 of a 3-modifier definition also → `InvalidStat` with no modifier applied.
- [ ] **AC-SE-10**: 8 distinct `BuffID`s applied → 9th distinct `BuffID` → `EffectSlotsFull` even though `OccupiedSlots + ModifierCount ≤ 32`.
- [ ] **AC-SE-03**: 30 single-modifier effects — rejected by the row limit first, so the GDD's set-up cannot be built literally; the test instead fills 30 occupied slots with rows that fit the 8-row limit (e.g. 4 × 7 + 1 × 2 = 30 on 5 rows), then a 3-modifier buff → `CapacityExceeded`, no change, existing effects unchanged. *(GDD AC-SE-03's "30 single-stat active effects" conflicts with EC-SE-6's 8-row limit; the intent — 30 slots + 3 > 32 — is tested.)*
- [ ] **AC-SE-04**: 4 effects × 7 modifiers (`OccupiedSlots` 28) → 5-modifier buff → `CapacityExceeded`; 4-modifier buff → `Success` (28 + 4 = 32). `OccupiedSlots` is the per-entity sum of `ModifierCount` (F-SE-3), kept in the occupancy array, not a row count.
- [ ] **AC-SE-17**: after the entity's death has been recorded (Story 007 wires the event; here the test calls the internal `MarkEntityDead(entity)` seam), `ApplyEffect` → `InvalidTarget`, no change.
- [ ] **Unknown entity**: a target whose `EntityID` has no slot index (resolver returns false) → `InvalidTarget`, no change.

---

## Implementation Notes

- `StatusEffectsService` (or similar) owns `ActiveEffect[MAX_ENTITY_SLOTS * MAX_ACTIVE_BUFFS_PER_ENTITY]` and `byte[MAX_ENTITY_SLOTS]` occupancy, both allocated once in the constructor (CR-SE-15).
- Define `IBuffModifierLayer` in this story (`src/ServerLogic/StatusEffects/IBuffModifierLayer.cs`) with the signature Story 003 specifies: `bool TryAddBuffModifier(EntityID, StatID, BuffModifierEntry)` and `void RemoveBuffModifier(EntityID, StatID, BuffID)`. Inject it through the service constructor; this story never calls it (every check here rejects before any call), so the tests use a recording fake that asserts zero calls. Story 003 adds the production adapter over `CharacterStats` and the calls.
- Entity → slot index: inject an `IEntitySlotIndex` with `bool TryGetSlotIndex(EntityID id, out int index)` (index in `[0, MAX_ENTITY_SLOTS)`). The production implementation (Story 009) reads `ZoneSlotPool`: players by `TryGetPlayerSlot`, mobs by their OWL index (`GetOwlIndexForMobSlot`) — the scheme CR-SE-15 names. Tests use a dictionary fake.
- The dead flag is a per-slot `bool[MAX_ENTITY_SLOTS]` set by the death path (Story 007) and cleared when the slot is cleaned (Story 008).
- Character Stats' own capacity is 32 per **stat** per entity (`CharacterStats.BuffModifierCapacity`); the GDD's guard is 32 per **entity**. The GDD's guard is stricter and stays; note it in a code comment.

---

## Out of Scope

- Calling Character Stats, rollback, attribution (Story 003); refresh (004); ticking (005); the death event subscription (007).

---

## QA Test Cases

- **Order** — Given a definition that is both NoOp-shaped and aimed at a dead entity, Then `NoOpDefinition` (shape first). Given a dead entity with 8 rows full, Then `InvalidTarget`. Given 8 rows full and capacity also exceeded, Then `EffectSlotsFull`.
- **AC-SE-06/07/08/10/03/04/17** — as listed; every rejection asserts zero seam calls, no new row, occupancy unchanged.
- **Edges** — `DurationTicks` 1 and 6000 pass, 0 and 6001 fail; a prohibited stat at the last populated index; capacity exactly 32 passes, 33 fails.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Validation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004
