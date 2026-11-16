# Story 011: Death Bridge — OnMobDied → Loot → MobEventBus.MobDied

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Dead row, CR-AI-13, EC-AI-1, EC-AI-11, AC-AI-12a, AC-AI-19; `design/gdd/mob-spawning.md` CR-MS-5 (loot first, spawn lifecycle second) and transition 4 (Mob Spawning calls `RemoveMob` when its own linger timer expires)
**Requirement**: `TR-ai-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Combat's `ICombatService.OnMobDied` is the single kill event; XP is never awarded from an `OnMobDied` subscriber. Per-zone `MobEventBus` carries `MobSpawned` / `MobDied`.

**Epic decision 1 (2026-11-12)**: Enemy AI subscribes to Combat's `OnMobDied`, enters Dead, calls `ResolveMobDrop(tierShift)`, then raises `MobEventBus.MobDied`. A mob that reaches HP ≤ 0 with no killer (CR-AI-13, e.g. a status-effect tick) runs the same routine; a second call is ignored.

**Readiness decisions (2026-11-16, user)**:
1. **Roster slot**: Enemy AI never calls `IZoneMobRoster.RemoveMob`; Mob Spawning owns it (mob-spawning.md transition 4). Enemy AI's despawn removes only its controller and runtime-store entry. No TD (nothing spawns mobs in production until the Mob Spawning epic).
2. **Character Stats leak**: no removal API in this story; logged as TD-109.
3. **Linger tick**: `MobDeathContext` has no tick, so the `OnMobDied` path records the tick of the system's last `Run` (Combat runs after Enemy AI in the same tick, phase 3 order Status Effects → Enemy AI → Auto-Attack → Skills).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (no engine API; plain C# in `IronGrind.ServerLogic`).

**Control Manifest Rules (this layer)**:
- Forbidden: awarding XP from an `OnMobDied` subscriber; raising a mob-death event from anywhere but Combat — ADR-010 notes 2026-10-30, 2026-11-04. *This rule means Combat's `ICombatService.OnMobDied`. Raising `MobEventBus.MobDied` from Enemy AI is required by CR-MS-5 and is not covered by it.*
- Required: `readonly struct` event args — ADR-010 (`MobDiedArgs` already is one)

---

## Acceptance Criteria

- [ ] **AC-1 Dead-entry routine** (one per mob, runs once): state Dead → `ILootTableService.ResolveMobDrop(mob, IsEnraged ? EnragedLootQualityBonusTier : 0)` → `MobEventBus.RaiseMobDied(new MobDiedArgs(zone, mob))`. After the linger (AC-3) the mob despawns: `EnemyAISystem.Remove(mob)` then `MobRuntimeStore.Remove(mob)`. `IZoneMobRoster.RemoveMob` is never called by Enemy AI.
- [ ] **AC-2 Triggers**: Combat `OnMobDied` (subscribed in the `EnemyAISystem` constructor, unsubscribed in `Dispose`) **or** the CR-AI-13 tick-start check; whichever comes first runs the routine, the other is a no-op. Tested in both cross-tick orders: (a) `OnMobDied` first, then the next `Run`'s HP check does nothing; (b) HP check first, then a late `OnMobDied` for the same mob does nothing. An `OnMobDied` for a foreign zone or an EntityID with no controller logs one warning and changes nothing.
- [ ] **AC-3 Linger**: a mob entering Dead through `OnMobDied` records the tick of the last `Run` (0 before the first `Run`) as `DeadEnteredTick`; the CR-AI-13 path records the current tick. The mob despawns at the first `Run` where `currentTick − DeadEnteredTick ≥ DeathLingerTicks`, not earlier. A Dead mob caches no velocity.
- [ ] **AC-4 Loot failure**: an exception from `ResolveMobDrop` is logged with the mob's EntityID and `MobDied` is still raised (spawn lifecycle must not stall); the mob still despawns after the linger.
- [ ] **AC-5 (AC-AI-12a)**: mob HP 0 during WindingUp → Dead at its next AI tick, no AttackExecution, no `CheckHit`.
- [ ] **AC-6 (AC-AI-19 / EC-AI-1)**: in one simulation tick run as a Status Effects step followed by the Enemy AI step, a status-effect tick takes a WindingUp mob (`windUpTicksRemaining = 1`) to 0 → the Enemy AI step marks it Dead without executing its attack (`CheckHit` not called).
- [ ] **AC-7 (EC-AI-11)**: Enraged mob dying with no attacker (CR-AI-13 path) → `ResolveMobDrop` gets `EnragedLootQualityBonusTier`; a non-Enraged mob gets 0.
- [ ] **AC-8 No XP**: the routine and the files it touches reference no XP / leveling API.
- [ ] **AC-9 No allocation**: `Run` allocates 0 bytes per tick, including a tick where a mob enters Dead and a tick where a mob despawns (measured after warm-up).
- [ ] **AC-10 ADR-010**: gets a dated note describing the bridge (epic DoD item).
- [ ] **AC-11 Tech debt**: Loot is not wired into the zone → existing TD-107 (Story 013 passes a logging stand-in). Mob stats left in Character Stats after despawn → TD-109 (logged at readiness).

---

## Implementation Notes

- `ILootTableService.ResolveMobDrop(EntityID mobEntityID, int tierShift)` exists (`src/ServerLogic/LootTableSystem/ILootTableService.cs`); a non-zero tierShift logs one warning there (OQ-AI-1) — expected. Loot reads mob info through `IMobInfoProvider` — implement it over the registry + runtime store if loot is wired later (note only, TD-107).
- `EnemyAISystem` gains `ICombatService` and `ILootTableService` constructor parameters. Story 013's composition must therefore construct Combat before Enemy AI (construction order is independent of the phase 3 step order).
- `MobController.OnDeadEntered` (Story 009 hook) and `DeadEntryCount` already make Dead terminal and counted; the routine needs the loot service and bus, which the controller does not hold — route it through the system (e.g. a callback or the system checking `DeadEntryCount`), developer's choice.
- `OnMobDied` subscribers run inside Combat's step (phase 3, after Enemy AI), so `EnemyAISystem` is not `_running` then. The despawn itself happens inside `Run`, so `Remove` is deferred to the end of `Run` (existing behaviour).
- Navigation already listens to `MobEventBus.OnMobDied` to release the agent (synchronously, phase 3 — allowed).
- Status Effects clears a dead entity's effects on its own (`StatusEffectsService.OnEntityDied`); Status Effects never raises `OnMobDied`, so status-effect kills reach Enemy AI only through CR-AI-13.
- Combat ignores hits on a mob with HP 0, so a lingering corpse is not re-killed.
- *(From Story 008, 2026-11-16.)* Despawn calls `MobRuntimeStore.Remove(id)`. `CharacterStats` has no per-entity removal, so a despawned mob's base stats and HP stay in memory (IDs are never reused: slow leak, not a correctness bug) → TD-109.

---

## Out of Scope

- Mob Spawning epic: `IZoneMobRoster.RemoveMob` (transition 4), respawn timers on `MobDied`
- Wiring the real Loot Table service into the zone (TD-107)
- A Character Stats per-entity removal API (TD-109)
- Zone composition wiring (Story 013)

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

1. **Routine order (AC-1)**: `OnMobDied` for a live mob → call log is exactly `ResolveMobDrop(mob, 0)` then `MobDied(zone, mob)`; controller State is Dead; `DeadEntryCount == 1`.
2. **CR-AI-13 path (AC-1, AC-2)**: set mob HP 0, `Run` → same call log, Dead, `DeadEnteredTick == currentTick`.
3. **Order (a) (AC-2)**: `OnMobDied`, then set HP 0 and `Run` → loot and `MobDied` called once each.
4. **Order (b) (AC-2)**: HP 0 and `Run`, then `OnMobDied` for the same mob → loot and `MobDied` called once each, no warning.
5. **Foreign zone / unknown mob (AC-2)**: `OnMobDied` with another ZoneID, and with an EntityID that has no controller → no loot, no `MobDied`, one warning each.
6. **Linger boundaries (AC-3)**: `DeathLingerTicks = 10`; Dead entered via `OnMobDied` after `Run(100)` → controller still present after `Run(109)`, removed after `Run(110)`; runtime store entry removed at the same time.
7. **Linger before first Run (AC-3)**: `OnMobDied` before any `Run` → `DeadEnteredTick == 0`.
8. **Loot throws (AC-4)**: throwing loot fake → error logged with the EntityID (`LogAssert`), `MobDied` still raised, mob despawns after the linger.
9. **Navigation release (AC-1)**: real `MobEventBus` with a counting `OnMobDied` subscriber standing in for Navigation → called once.
10. **AC-AI-12a (AC-5)**: WindingUp at `windUpTicksRemaining = 3`, HP 0 → next `Run`: Dead, `CheckHit` not called, no AttackExecution state seen.
11. **AC-AI-19 (AC-6)**: Status Effects step (real service or a step that drops HP to 0) then the Enemy AI step in one tick, mob at `windUpTicksRemaining = 1` → Dead, `CheckHit` not called.
12. **Enraged tier (AC-7)**: Enraged mob, HP 0 via CR-AI-13 (no attacker) → `ResolveMobDrop(mob, EnragedLootQualityBonusTier)`; non-Enraged → 0.
13. **Two mobs same tick (AC-1)**: two mobs die in one `Run` → two routines, controller order unaffected, both despawn on the same later tick.
14. **Dispose (AC-2)**: after `Dispose`, `OnMobDied` reaches no handler (loot not called).
15. **Zero allocation (AC-9)**: after warm-up, `GC.GetAllocatedBytesForCurrentThread` delta is 0 across a death tick and a despawn tick.
16. **No XP (AC-8)**: covered by code review (no XP API referenced); no runtime test.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_DeathBridge_tests.cs` — must exist and pass

**Status**: [x] Created — 20/20 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 009 (Complete)
- Unlocks: Story 012, Story 013

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 11/11 passing (AC-8 "no XP" verified by code review / grep, all others by automated tests)
**Deviations** (advisory):
1. QA 11 uses a Status Effects stand-in (the fake HP reader set to 0 just before the Enemy AI `Run`), not the real `StatusEffectsService`.
2. "`CheckHit` not called" is asserted on the `IMobAttackResolver` seam (`CheckHit` lives behind it).
3. `MobController` gained a read-only `WindUpTicksRemaining` (tests) and `DeathRoutineRan` (the once-only guard of the routine, set by `EnemyAISystem`).
4. Code review fix B1 (beyond the original brief): `HandleMobDied` clears the velocity the mob cached earlier in the same tick, or phase 4 moved the corpse one more tick (mutation-checked). S1: once the linger has elapsed, `Remove` and `MobRuntimeStore.Remove` both run unconditionally.
5. Not applied by decision: an HP guard on a spurious `OnMobDied` (Combat raises it only after HP 0); foreign-zone warning spam (Combat is per-zone). A late `OnMobDied` after despawn logs one warning (tested).
**Tech debt**: TD-107 (loot not wired, existing); TD-109 (despawned mob stats stay in Character Stats, logged at readiness).
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_DeathBridge_tests.cs` 20/20; batch EditMode 7771 / 7642 passed / 0 failed / 129 skipped (2026-11-16).
**Code Review**: Complete — CHANGES REQUIRED (B1) → all fixes and suggestions applied (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
