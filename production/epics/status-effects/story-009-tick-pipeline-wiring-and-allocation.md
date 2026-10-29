# Story 009: Tick Pipeline Wiring, Mob Targets and Allocation Budget

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready. AC-SE-24 is tested against a test-only stand-in: no Auto-Attack Combat code exists yet.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours (was 4: Character Stats construction, pool mob lookup and per-entry phase-3 isolation added at readiness)

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-5, CR-SE-12, CR-SE-15; AC-SE-20, 24, 25
**Requirement**: `TR-se-004` (expiry removes the modifier and subscribers see the updated value — AC-SE-24), `TR-se-003` (the two public entry points stay the only route to the buff layer — the composition hands Character Stats only to the Status Effects adapters). CR-SE-15 (no per-tick allocation) has no TR entry.

**ADR Governing Implementation**: ADR-014 (tick order: game systems in phase 3 of `ZoneTickPipeline` step 3, in the order the composition root lists); ADR-012 (composition root in `IronGrind.ServerLogic`); ADR-009 Amendment 1 (the composition root is the only place that constructs server systems); ADR-010 (owned objects disposed in the teardown Dispose pass)
**ADR Decision Summary**: the composition root registers Status Effects' tick in phase 3, after Zone Instancing and before where Auto-Attack Combat will go; nothing self-schedules. ADR-014 does not specify how a failure inside phase 3 is handled; this story adds per-entry isolation (no ADR change).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `GC.GetTotalMemory(false)` delta and `GC.GetAllocatedBytesForCurrentThread()` behave the same under Mono in the Editor; the allocation test runs in EditMode.

**Control Manifest Rules (this layer)**:
- Required: `ZoneTickPipeline` step 3 runs ADR-002 phases 1–4; ticking game systems (… combat, status effects …) run in phase 3 in an order the composition root lists explicitly — ADR-014
- Required: the server composition root is `ZoneServerComposition` (plain C#, `IronGrind.ServerLogic`) — ADR-009 Amendment 1, ADR-012
- Guardrail: 60 fps client / 20 Hz server tick; no per-tick heap allocation in the Status Effects pass (CR-SE-15)

---

## Acceptance Criteria

- [ ] **Character Stats construction (user decision, readiness 2026-10-29)**: `ZoneServerComposition` builds one `CharacterStats` per zone at S5 (`ConstructObjects`): `new CharacterStats(new LevelingService(XpThresholdTable.Values))`, held in an internal field (internal getter) for later systems to take. No production code populates player or mob base stats yet (out of scope); tests populate them with `SetBaseStat` / `SetCurrentHP`.
- [ ] **Mob slot lookup (user decision)**: new public `ZoneSlotPool.TryGetMobSlot(EntityID id, out ushort mobSlot)` over the existing `_mobIndex`, mirroring `TryGetPlayerSlot` (false and 0 for an unknown or released id; no allocation). New production `ZoneSlotPoolEntityIndex : IEntitySlotIndex` (`src/ServerLogic/StatusEffects/`): player → its player slot; mob → `GetOwlIndexForMobSlot(mobSlot)` (range [50, 199] with default sizes); neither → false. Tests: a player, a mob, an unknown id, a released player and a released mob slot.
- [ ] **Wiring**: after `ZoneInstancingWiring` is built, the composition builds `StatusEffectsService` from `CharacterStatsBuffModifierLayer`, `CharacterStatsResourceSink`, `ZoneSlotPoolEntityIndex(wiring.Pool)` and `CharacterStatsDeathSource` over the zone's `CharacterStats`; calls `wiring.Pool.SetReleaseListener(service)` (Story 008); registers the service with the Dispose pass (ADR-010 — `Dispose` unsubscribes the death handler). The service implements `IZoneSimulationStep` (`Run(currentTick, deltaTime)` calls `OnServerTick()`; both arguments unused) and is appended to the phase-3 list, which is exactly `[ZoneInstancingWiring, StatusEffectsService]`. Test via the internal `SimulationSteps` seam: order and types; after the Dispose pass a death event no longer reaches the service; a pool release calls `ClearEntity` (the listener is set). Story 014's composition tests still pass.
- [ ] **AC-SE-24 — order before combat (user decision: test-only stand-in)**: no production placeholder. The test appends a recording `IZoneSimulationStep` after Status Effects through `SimulationSteps`. A +100 AttackPower buff expiring on tick N: the stand-in reads `GetEffectiveStat(AttackPower)` on tick N and sees the post-expiry value. The Auto-Attack Combat story appends its real entry after Status Effects and keeps this test.
- [ ] **AC-SE-20 — mobs**: a mob allocated in `ZoneSlotPool` with Character Stats populated → `ApplyEffect(mob, …)` → `Success`; effective stat reflects the modifier; expiry removes it (CR-SE-12 — Character Stats already gives mobs a buff layer).
- [ ] **AC-SE-25 — no allocation**: 200 entities × 8 effects (mixed modifier and regen effects, some expiring this tick, after a warm-up tick) → one `OnServerTick()` allocates 0 bytes, measured with `GC.GetAllocatedBytesForCurrentThread()` before/after (more reliable than `GC.GetTotalMemory`; the GDD's method is also asserted if stable). Character Stats calls inside the pass count — if `CharacterStats` itself allocates (e.g. `Dictionary` growth on first use), the warm-up tick absorbs it; any remaining allocation is reported, not hidden. A deliberate allocation added in a test double is detected (the measurement works).
- [ ] **Exception isolation (user decision: per-entry)**: `ZoneSimulationStep.Run` wraps each entry in its own `try`/`catch`; a failed entry logs one error `[ZoneSimulationStep] EntryFailed: index=N type=TYPE tick=N - ExceptionType: message` (built only in the catch; the log call itself guarded like `ZoneTickPipeline.LogStepFailed`) and the next entry still runs. Tests: a throwing layer fake makes Status Effects throw → error logged, the stand-in after it still runs the same tick, the next tick processes normally (the Story 006 bracket closed: no transaction left open); the success path of `Run` still allocates nothing.

---

## Implementation Notes

- Construction goes in `RealBackend.BuildOutboundAndPipeline` (`ZoneServerComposition.cs`), right after `_owner._simulationSteps.Add(wiring)` and before `new ZoneSimulationStep(...)`. Use the existing `Observe(...)` calls for each new object, matching the surrounding code.
- Register the service with the composition's Dispose pass (the "Appends an object to the Dispose pass" helper); the pass disposes in reverse order.
- `TryGetMobSlot` is a Zone Instancing change (small, flagged by Story 008). `ZoneSlotPoolEntityIndex` replaces the Story 008 test resolver's mob-slot recording; the Story 008 tests can keep their test resolver.
- `ZoneSimulationStep` isolation changes a Zone Server class: keep it allocation-free on the success path; the existing `ZoneSimulationStep` tests must still pass.
- If `CharacterStats.ApplyRegen` / `RemoveBuffModifier` allocate on the hot path (they use `Dictionary` lookups — lookups do not allocate; first-use creation does), the warm-up tick covers it. A real allocation found in Character Stats is a finding for the completion notes, not something to hide with a looser assertion.
- The real `IEquipmentService` wiring gap from Character Persistence 005 is unrelated; do not fix it here.

---

## Out of Scope

- Auto-Attack Combat itself and any production combat placeholder; buff-state replication to clients (TR-se-006); Skill System calls.
- Populating player base stats at zone admission or mob base stats at spawn (owners: the admission/applier path and the mob spawn path — later stories).
- The `OnEntityRespawned` caller (Death & Respawn, CR-DR-11a).
- Handing the zone `CharacterStats` to other systems (they take the internal getter in their own stories).

---

## QA Test Cases

- **Character Stats** — composition builds exactly one `CharacterStats`; the service's adapters use it.
- **Index** — player slot; mob → OWL index; unknown id → false; released player and released mob → false.
- **Wiring** — phase-3 list is `[ZoneInstancingWiring, StatusEffectsService]`; release listener set (a pool release clears the entity's effects); after the Dispose pass a death event is not handled.
- **AC-SE-24** — test-only stand-in after Status Effects reads the post-expiry AttackPower on the expiry tick.
- **AC-SE-20** — mob buff applied, reflected, expired.
- **AC-SE-25** — 200 × 8 load, warm-up tick, then 0 bytes over one tick; a deliberate allocation in a test double is detected.
- **Isolation** — throwing layer fake: `EntryFailed` logged, the stand-in still runs that tick, next tick processes normally with no open transaction; success-path `Run` allocates 0 bytes.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_PipelineWiring_tests.cs` — must exist and pass (plus the `ZoneSimulationStep` isolation and `TryGetMobSlot` tests in their systems' test folders)

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 005–008 (Complete); Zone Instancing Story 014 (Complete)
- Unlocks: the Skill System epic (callers of `ApplyEffect` / `ApplyInstantHeal`); Auto-Attack Combat appends its phase-3 entry after Status Effects
