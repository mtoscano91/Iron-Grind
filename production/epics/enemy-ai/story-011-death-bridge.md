# Story 011: Death Bridge — OnMobDied → Loot → MobEventBus.MobDied

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Dead row, CR-AI-13, EC-AI-1, EC-AI-11, AC-AI-12a, AC-AI-19; `design/gdd/mob-spawning.md` CR-MS-5 (loot first, spawn lifecycle second)
**Requirement**: `TR-ai-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Combat's `ICombatService.OnMobDied` is the single kill event; XP is never awarded from an `OnMobDied` subscriber. Per-zone `MobEventBus` carries `MobSpawned` / `MobDied`.

**Epic decision 1 (2026-11-12)**: Enemy AI subscribes to Combat's `OnMobDied`, enters Dead, calls `ResolveMobDrop(tierShift)`, then raises `MobEventBus.MobDied`. A mob that reaches HP ≤ 0 with no killer (CR-AI-13, e.g. a status-effect tick) runs the same routine; a second call is ignored.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Forbidden: awarding XP from an `OnMobDied` subscriber; raising a mob-death event from anywhere but Combat — ADR-010 notes 2026-10-30, 2026-11-04
- Required: `readonly struct` event args — ADR-010

---

## Acceptance Criteria

- [ ] Dead-entry routine (one per mob, idempotent): state Dead → `ILootTableService.ResolveMobDrop(mob, TierShift)` → `MobEventBus.RaiseMobDied(zone, mob)` → hold `DEATH_LINGER_TICKS` → despawn (controller and runtime-store entry removed; roster removal per the Mob Spawning contract — readiness: who calls `IZoneMobRoster.RemoveMob`).
- [ ] Triggered by Combat `OnMobDied` (subscribed at construction, unsubscribed on dispose) **or** by the CR-AI-13 tick-start check; whichever comes first runs it, the other is a no-op (test both orders in one tick).
- [ ] Loot exception → logged, `MobDied` still raised (spawn lifecycle must not stall).
- [ ] **AC-AI-12a**: mob HP 0 during WindingUp → Dead at its next AI tick, no AttackExecution, no `CheckHit`.
- [ ] **AC-AI-19 / EC-AI-1**: in one simulation tick with the real step order, a status-effect tick takes the mob to 0 → the Enemy AI step marks it Dead without executing its attack.
- [ ] **EC-AI-11**: Enraged mob with no attacker → `ResolveMobDrop` still gets `EnragedLootQualityBonusTier`.
- [ ] No XP call anywhere in the routine.
- [ ] ADR-010 gets a dated note describing the bridge (epic DoD item).
- [ ] Tech debt: Loot Table is not wired into the zone — the zone composition (Story 013) passes a logging stand-in `ILootTableService`; logged as a TD entry (added with this epic's stories).

---

## Implementation Notes

- `ILootTableService.ResolveMobDrop(EntityID mobEntityID, int tierShift)` exists (`src/ServerLogic/LootTableSystem/ILootTableService.cs`); Loot reads mob info through `IMobInfoProvider` — implement it over the registry + runtime store if loot is wired later (note only).
- Navigation already listens to `MobEventBus.OnMobDied` to release the agent.

---

## Out of Scope

- Mob Spawning epic: respawn timers on `MobDied`
- Wiring the real Loot Table service into the zone

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_DeathBridge_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009
- Unlocks: Story 012, Story 013
