# Story 004: CurrentHP / CurrentMP Lifecycle — ApplyDamage, ApplyRegen, ConsumeMana, Death Boundary

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-003`, `TR-stats-004`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the resource pool APIs. `OnEntityDied` event wiring is governed by ADR-010 and implemented in Story 005 — this story implements the death detection logic and invokes the event; the event infrastructure is stubbed here and fully wired in Story 005.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `CurrentHP` and `CurrentMP` are `float` fields. `ApplyRegen(0.75f)` on `CurrentHP = 50.25f` must produce exactly `51.0f` — both values are exactly IEEE 754 representable. Do NOT use non-representable values like `50.3f + 0.7f` in tests — their IEEE 754 sum is approximately `50.9999...f` which may fail assertions on ARM IL2CPP.

**Control Manifest Rules (Foundation layer)**:
- Required: `ApplyDamage`, `ApplyRegen`, `ConsumeMana`, `ApplyManaRegen` are the only write paths for CurrentHP/CurrentMP — never via the modifier stack
- Forbidden: Never allow `AddBuffModifier` to target `StatID.CurrentHP` or `StatID.CurrentMP`
- Guardrail: Every write to CurrentHP/CurrentMP must enforce `clamp(value, 0.0, MaxHP/MaxMP)` synchronously in the same call

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-06** [BLOCKING]: `AddBuffModifier(EntityID, StatID.CurrentHP, +100 flat, ...)` returns error. `GetEffectiveStat(CurrentHP)` = **500.0f** (unchanged). Also test CurrentMP, Level, Experience — all must return error and remain unchanged.
- [ ] **AC-07** [BLOCKING]: CurrentHP=2500f, effective MaxHP=3000. `RemoveEquipmentModifier` reducing effective MaxHP to 2000 → `GetEffectiveStat(CurrentHP)` = **2000.0f** immediately (same call, no yield). Not 2500f (unreconciled), not 0f (dead).
- [ ] **AC-08** [BLOCKING]: CurrentHP=800f, MaxHP=1000. `SetBaseStat(MaxHP, 1200)` → `GetEffectiveStat(CurrentHP)` = **800.0f** (unchanged — no free HP from MaxHP increase; only `ApplyRegen` may increase CurrentHP).
- [ ] **AC-09** [BLOCKING]: CurrentHP=50f, `int diedCount=0`; subscribe `OnEntityDied`: `_ => diedCount++`. `ApplyDamage(50f)` → `GetEffectiveStat(CurrentHP)` = **0.0f**; `diedCount == 1` (exactly once — not zero, not twice).
- [ ] **AC-10** [BLOCKING]: CurrentHP=30f, `diedCount=0`. `ApplyDamage(500f)` → CurrentHP=**0.0f** (overkill clamped at 0); `diedCount == 1`.
- [ ] **AC-10b** [BLOCKING]: CurrentHP=0.0f (already dead), `diedCount=0`. `ApplyDamage(100f)` → CurrentHP=**0.0f** unchanged; `diedCount == 0` (event does not re-fire on a dead entity).
- [ ] **AC-11** [BLOCKING]: CurrentMP=40f, MaxMP=200. `ConsumeMana(100f)` → returns **false**; CurrentMP=**40.0f** unchanged.
- [ ] **AC-12** [BLOCKING]: CurrentMP=150f, MaxMP=200. `ConsumeMana(100f)` → returns **true**; CurrentMP=**50.0f**.
- [ ] **AC-12b** [BLOCKING]: CurrentHP=50.25f, MaxHP=100. `ApplyRegen(0.75f)` → CurrentHP=**51.0f** exactly (IEEE 754 representable values — assert delta = 0.0f, not epsilon tolerance).
- [ ] **AC-12c** [BLOCKING]: CurrentHP=99.5f, MaxHP=100. `ApplyRegen(5.0f)` → CurrentHP=**100.0f** (clamped at MaxHP; not 104.5f; no exception).
- [ ] **[NEW] AC-12d** [BLOCKING]: CurrentMP=50.25f, MaxMP=200. `ApplyManaRegen(0.75f)` → CurrentMP=**51.0f** exactly (same IEEE 754 reasoning as AC-12b).
- [ ] **[NEW] AC-12e** [BLOCKING]: CurrentMP=195.0f, MaxMP=200. `ApplyManaRegen(10.0f)` → CurrentMP=**200.0f** (clamped at MaxMP; not 205.0f; no exception).

---

## Implementation Notes

*Derived from GDD Rules 8–9, Edge Cases EC-05–EC-10:*

**`CurrentHP` and `CurrentMP` are never modified through the modifier stack.** They are protected `float` fields with dedicated write methods only. `AddBuffModifier` must reject `StatID.CurrentHP` and `StatID.CurrentMP`.

**`ApplyDamage(EntityID, float amount)`**: If `CurrentHP == 0.0f` (entity dead), return immediately — no-op (EC-08, TR-stats-004). Otherwise, subtract `amount`, clamp to `[0.0, MaxHP]`. If `CurrentHP` reaches 0.0 after clamping, invoke `OnEntityDied` (wired in Story 005 — stub the call here). Overkill clamps at 0.0; no negative HP stored.

**`ApplyRegen(EntityID, float amount)`**: Add `amount` to `CurrentHP`, clamp to `[0.0, MaxHP]`. Float precision preserved — no per-tick rounding.

**`ConsumeMana(EntityID, float cost) → bool`**: If `CurrentMP < cost`, return `false` and make no write. If `CurrentMP >= cost`, subtract `cost`, clamp to `[0.0, MaxMP]`, return `true`.

**`ApplyManaRegen(EntityID, float amount)`**: Add `amount` to `CurrentMP`, clamp to `[0.0, MaxMP]`.

**MaxHP decrease clamps CurrentHP immediately** (EC-05, EC-18): When `RemoveEquipmentModifier` causes effective `MaxHP` to drop below `CurrentHP`, reconcile within the same call — `CurrentHP = min(CurrentHP, newEffectiveMaxHP)`. No deferred reconciliation. Entity is not killed.

**MaxHP increase grants no HP** (EC-06): When `SetBaseStat(MaxHP, ...)` increases the base, `CurrentHP` remains unchanged. Only `ApplyRegen` may increase `CurrentHP`.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001**: Container, `GetBaseStat`, `SetBaseStat`
- **Story 002**: `GetEffectiveStat` — must be Done
- **Story 003**: Modifier add/remove — must be Done (required for AC-07 equipment removal test)
- **Story 005**: `OnStatChanged` and `OnEntityDied` event infrastructure — stub `OnEntityDied` call here; fully wire in Story 005

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs`

- **AC-06**: CurrentHP is not modifier-stack-eligible
  - Given: Entity with CurrentHP=500.0f, MaxHP=1000
  - When: `AddBuffModifier(EntityID, StatID.CurrentHP, +100 flat, 0 pct, 10 ticks, BuffID.TestBuff)`
  - Then: Call returns an error. `GetEffectiveStat(CurrentHP)` = 500.0f unchanged.
  - Edge cases: Also test CurrentMP, Level, Experience — all must return error and remain unchanged.

- **AC-07**: MaxHP decrease immediately clamps CurrentHP in the same call
  - Given: Entity with CurrentHP=2500.0f; equip modifier raising effective MaxHP to 3000
  - When: `RemoveEquipmentModifier` reducing effective MaxHP to 2000. Then `GetEffectiveStat(CurrentHP)` immediately (no yield, no frame boundary).
  - Then: Returns 2000.0f. Entity not dead. MaxHP=2000.
  - Edge cases: Remove equip that reduces MaxHP to exactly CurrentHP value → CurrentHP = new MaxHP, not further reduced.

- **AC-08**: MaxHP increase grants no free HP
  - Given: Entity with CurrentHP=800.0f, MaxHP=1000
  - When: `SetBaseStat(MaxHP, 1200)`, then `GetEffectiveStat(CurrentHP)`
  - Then: Returns 800.0f. MaxHP is now 1200.
  - Edge cases: Level-up that doubles MaxHP — CurrentHP must remain at pre-level value.

- **AC-09**: OnEntityDied fires exactly once on exact-damage kill
  - Given: Entity with CurrentHP=50.0f; `int diedCount=0`; subscribe `OnEntityDied`: `_ => diedCount++`
  - When: `ApplyDamage(EntityID, 50.0f)`
  - Then: `GetEffectiveStat(CurrentHP)` = 0.0f; `diedCount == 1`.
  - Edge cases: Verify CurrentHP == 0.0f exactly (not 0.001f due to float imprecision).

- **AC-10**: Overkill damage clamps at 0.0; OnEntityDied fires exactly once
  - Given: Entity with CurrentHP=30.0f; `diedCount=0`; subscribe `OnEntityDied`
  - When: `ApplyDamage(EntityID, 500.0f)`
  - Then: CurrentHP=0.0f; `diedCount==1`; no negative HP stored.
  - Edge cases: Damage exactly equal to CurrentHP (not overkill) → same assertion.

- **AC-10b**: ApplyDamage on dead entity is a no-op; OnEntityDied does not re-fire
  - Given: Entity with CurrentHP=0.0f; `diedCount=0`; subscribe `OnEntityDied`
  - When: `ApplyDamage(EntityID, 100.0f)`
  - Then: CurrentHP=0.0f unchanged; `diedCount==0`.
  - Edge cases: Call `ApplyDamage` three times on dead entity; `diedCount` remains 0 each time.

- **AC-11**: ConsumeMana returns false when mana is insufficient
  - Given: Entity with CurrentMP=40.0f, MaxMP=200
  - When: `bool result = ConsumeMana(EntityID, 100.0f)`
  - Then: `result == false`; CurrentMP = 40.0f.
  - Edge cases: Cost exactly = CurrentMP+1 → false; cost exactly = CurrentMP → true.

- **AC-12**: ConsumeMana succeeds and subtracts exactly the cost
  - Given: Entity with CurrentMP=150.0f, MaxMP=200
  - When: `ConsumeMana(EntityID, 100.0f)`
  - Then: Returns true; CurrentMP = 50.0f.
  - Edge cases: Consecutive calls reducing to near 0; final call at exactly remaining MP → true, result = 0.0f.

- **AC-12b**: ApplyRegen preserves IEEE 754 float precision
  - Given: Entity with CurrentHP=50.25f, MaxHP=100
  - When: `ApplyRegen(EntityID, 0.75f)`
  - Then: CurrentHP=51.0f exactly. Assert with delta=0.0f (not epsilon tolerance).
  - Note: Do NOT use 50.3f + 0.7f — sum is ~50.9999...f on ARM IL2CPP; will fail assertion.

- **AC-12c**: ApplyRegen clamped at MaxHP; no overflow
  - Given: Entity with CurrentHP=99.5f, MaxHP=100
  - When: `ApplyRegen(EntityID, 5.0f)`
  - Then: CurrentHP=100.0f (clamped). Not 104.5f. No exception.
  - Edge cases: `ApplyRegen(0.5f)` → exactly 100.0f (borderline clamp).

- **[NEW] AC-12d**: ApplyManaRegen preserves float precision
  - Given: Entity with CurrentMP=50.25f, MaxMP=200
  - When: `ApplyManaRegen(EntityID, 0.75f)`
  - Then: CurrentMP=51.0f exactly (same IEEE 754 reasoning as AC-12b).
  - Edge cases: `ApplyManaRegen` on entity at MaxMP → remains at MaxMP (see AC-12e).

- **[NEW] AC-12e**: ApplyManaRegen clamped at MaxMP
  - Given: Entity with CurrentMP=195.0f, MaxMP=200
  - When: `ApplyManaRegen(EntityID, 10.0f)`
  - Then: CurrentMP=200.0f (clamped). Not 205.0f. No exception.
  - Edge cases: `ApplyManaRegen(5.0f)` → exactly 200.0f (borderline clamp).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` — must exist and pass

**Status**: [x] `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` — 14 test methods

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003 must be Done
- Unlocks: Story 005 (event wiring for OnEntityDied)

---

## Completion Notes

**Completed**: 2026-06-29
**Criteria**: 12/12 passing
**Deviations**:
- ADVISORY: TR-stats-003 / TR-stats-004 not yet in tr-registry.yaml (pre-existing infrastructure gap — all stories in this epic share this note)
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` (14 tests)
**Code Review**: Complete — `/code-review` run this session; CHANGES REQUIRED initially; 4 blocking test gaps (T-01 AC-07 death-event assertion, T-02 AC-07 exact-boundary clamp, T-03 AC-10b triple-call, T-04 AC-11/12 exact-drain boundary) and 2 implementation fixes (B-01 negative-amount guard in ApplyRegen/ApplyManaRegen, B-02 OnEntityDied on MaxHP→0 reconciliation) applied before closure
