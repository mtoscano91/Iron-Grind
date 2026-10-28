# Story 009: Tick Pipeline Wiring, Mob Targets and Allocation Budget

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean). AC-SE-24 is tested against a stand-in: no Auto-Attack Combat code exists yet.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-5, CR-SE-12, CR-SE-15; AC-SE-20, 24, 25
**Requirement**: `TR-se-003`, `TR-se-004` (placeholders — registry empty)

**ADR Governing Implementation**: ADR-014 (tick order: game systems in phase 3 of `ZoneTickPipeline` step 3, in the order the composition root lists); ADR-012 (composition root in `IronGrind.ServerLogic`)
**ADR Decision Summary**: the composition root registers Status Effects' tick in phase 3, listed before Auto-Attack Combat; nothing self-schedules.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `GC.GetTotalMemory(false)` delta and `GC.GetAllocatedBytesForCurrentThread()` behave the same under Mono in the Editor; the allocation test runs in EditMode.

**Control Manifest Rules (this layer)**:
- Required: `ZoneTickPipeline` step 3 runs ADR-002 phases 1–4; ticking game systems (… combat, status effects …) run in phase 3 in an order the composition root lists explicitly — ADR-014
- Required: the server composition root is `ZoneServerComposition` (plain C#, `IronGrind.ServerLogic`) — ADR-009 Amendment 1, ADR-012
- Guardrail: 60 fps client / 20 Hz server tick; no per-tick heap allocation in the Status Effects pass (CR-SE-15)

---

## Acceptance Criteria

- [ ] **Wiring**: `ZoneServerComposition` builds the Status Effects service with the real `CharacterStats` (layer, resource sink, death source), the zone's slot-index resolver over `ZoneSlotPool` (players: player slot; mobs: OWL index), and the departure hook of Story 008; its `OnServerTick` is listed in phase 3. Story 014's composition tests still pass.
- [ ] **AC-SE-24 — order before combat**: phase 3 lists Status Effects before the combat entry (today a placeholder/stand-in: a recording delegate registered where Auto-Attack Combat will go). Test: a +100 AttackPower buff expiring on tick N; the stand-in reads `GetEffectiveStat(AttackPower)` on tick N and sees the post-expiry value. When Auto-Attack Combat lands, its story replaces the stand-in and keeps this test.
- [ ] **AC-SE-20 — mobs**: a mob allocated in `ZoneSlotPool` with Character Stats populated → `ApplyEffect(mob, …)` → `Success`; effective stat reflects the modifier; expiry removes it (CR-SE-12 — Character Stats already gives mobs a buff layer).
- [ ] **AC-SE-25 — no allocation**: 200 entities × 8 effects (mixed modifier and regen effects, some expiring this tick, after a warm-up tick) → one `OnServerTick()` allocates 0 bytes, measured with `GC.GetAllocatedBytesForCurrentThread()` before/after (more reliable than `GC.GetTotalMemory`; the GDD's method is also asserted if stable). Character Stats calls inside the pass count — if `CharacterStats` itself allocates (e.g. `Dictionary` growth on first use), the warm-up tick absorbs it; any remaining allocation is reported, not hidden.
- [ ] **Exception isolation**: an exception inside the Status Effects pass is caught by the pipeline's phase handling (logged) and the next tick runs normally (the transaction bracket of Story 006 closed).

---

## Implementation Notes

- Read `ZoneTickPipeline` and `ZoneServerComposition` (Zone Instancing Story 014, Networking Core Story 045) for how phase 3 entries are listed; add the Status Effects entry and a named placeholder for combat if none exists.
- If `CharacterStats.ApplyRegen` / `RemoveBuffModifier` allocate on the hot path (they use `Dictionary` lookups — lookups do not allocate; first-use creation does), the warm-up tick covers it. A real allocation found in Character Stats is a finding for the completion notes, not something to hide with a looser assertion.
- The real `IEquipmentService` wiring gap from Character Persistence 005 is unrelated; do not fix it here.

---

## Out of Scope

- Auto-Attack Combat itself; buff-state replication to clients (TR-se-006); Skill System calls.

---

## QA Test Cases

- **Wiring** — composition builds; phase 3 order list contains Status Effects before the combat entry.
- **AC-SE-24** — stand-in reads the post-expiry AttackPower on the expiry tick.
- **AC-SE-20** — mob buff applied, reflected, expired.
- **AC-SE-25** — 200 × 8 load, warm-up tick, then 0 bytes over one tick; a deliberate allocation added in a test double is detected (the measurement works).
- **Isolation** — a throwing layer fake: error logged, next tick processes normally.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_PipelineWiring_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 005–008; Zone Instancing Story 014 (Complete)
- Unlocks: the Skill System epic (callers of `ApplyEffect` / `ApplyInstantHeal`)
