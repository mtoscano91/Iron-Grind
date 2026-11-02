# Story 006: Mana Cost (V-9) and Cooldown Registration

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-2 (V-9), CR-SK-8, CR-SK-10, CR-SK-11, CR-SK-13, CR-SK-16, F-SK-4, EC-SK-2, EC-SK-13
**Requirement**: `TR-sk-007`, `TR-sk-008`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order; ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: ADR-014: the cast resolves in phase 3, and `serverCurrentTick` is the tick being run. ADR-012: mana and cooldown state are authoritative, `IronGrind.ServerLogic`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven (`SkillSystemConfig`), never hardcoded
- Guardrail: no allocation per tick

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] **F-SK-4**: `effectiveManaCost = Ceil(ManaCost × MANA_COST_MULTIPLIER)`. **AC-SK-17**: 100 × 0.75 → exactly 75 passed to `ConsumeMana`. **AC-SK-18**: 101 × 1.5 → exactly 152.
- [ ] **AC-SK-19**: the multiplier is read from `SkillSystemConfig` at cast time; changing the config instance between two casts changes the second cost without rebuilding the service.
- [ ] **V-9 / EC-SK-2**: `ConsumeMana` false → `InsufficientMana`; no cooldown registered, `NotifySkillUsed` not called, skill stays Ready.
- [ ] **AC-SK-15**: on success, mana is deducted (by the effective cost) before any damage or effect step runs. A test seam between V-9 and execution observes the mana value.
- [ ] **CR-SK-8**: after execution completes, `CooldownExpiryTick = serverCurrentTick + CooldownTicks`, on every accepted cast, hit or miss.
- [ ] **AC-SK-06**: after a cast at tick N writes `N + 20`, a cast at N + 1 is rejected `OnCooldown`. **AC-SK-07**: a cast at exactly `N + 20` passes V-6.
- [ ] **AC-SK-08**: casting skill A leaves skill B's `CooldownExpiryTick` unchanged (no global cooldown, CR-SK-11).
- [ ] **AC-SK-05**: a V-6 rejection changes neither mana, nor cooldowns, nor the auto-attack timer.
- [ ] `CooldownTicks == 0` → the skill is castable again on the next tick (States table: Executing → Ready).
- [ ] `ManaCost == 0` → `ConsumeMana` is called with 0, or skipped (document which); the cast is never rejected for mana.

---

## Implementation Notes

- **`ConsumeMana`** is `CharacterStats.ConsumeMana(EntityID, float cost)` (`CharacterStats.cs:749`); the GDD writes `int`. Compute the effective cost as an `int` with `(int)Math.Ceiling(ManaCost * (double)multiplier)`. The `double` avoids float error at boundaries like 101 × 1.5; AC-SK-18's 152 is the check. Pass it as a float. Seam: `ISkillManaSink.ConsumeMana(EntityID, int)` with an adapter over `CharacterStats`, and a call-recording fake for AC-SK-17 / AC-SK-18.
- **Order of a successful cast** (CR-SK-2 to CR-SK-8): V-9 consumes mana → [Story 007: `NotifySkillUsed`, damage] → [Story 008: effects] → cooldown write. Leave clear extension points (private methods, or an internal `ISkillExecution` seam) so Stories 007 and 008 add their steps without reordering.
- **Cooldown write**: `uint` addition of `serverCurrentTick + (uint)CooldownTicks`. At 20 Hz, `uint` tick wrap is about 6.8 years; no wrap handling (note it in a comment, as the tick-source docs do).
- **AC-SK-15 seam**: the GDD flags it as a testability concern. A recording fake shared by the mana sink and the execution seam, asserting the call order (mana, then execution), satisfies it.
- **EC-SK-13** (crash between mana and cooldown): no persistence of cooldowns, so nothing to do; cooldowns are not saved by Character Persistence. Confirm and note.
- Accepted casts go to `ISkillCastOutput` (Story 009) with `newCooldownTicks = CooldownTicks`, after the cooldown write.

---

## Out of Scope

- Story 007: `NotifySkillUsed`, BaseDamage, Damage Calculation and the kill sequence (AC-SK-16, the no-refund-on-miss criterion, needs the damage path)
- Story 009: `SkillCooldownUpdate` on the wire

---

## QA Test Cases

- **AC-SK-17 / AC-SK-18**: `[TestCase(100, 0.75f, 75)]`, `[TestCase(101, 1.5f, 152)]`, `[TestCase(50, 0.5f, 25)]`, `[TestCase(200, 2.0f, 400)]`, `[TestCase(1, 0.5f, 1)]` → the intercepted `ConsumeMana` argument.
- **AC-SK-19**: cast with config A (1.0) → 100; swap to config B (1.5) on the same service → 150.
- **EC-SK-2**: fake mana returns false → `InsufficientMana`, cooldown 0, no `NotifySkillUsed`, skill castable next tick once mana is available.
- **AC-SK-15**: a recording fake shows `ConsumeMana` before the first execution call.
- **CR-SK-8**: cast at tick 1000 with `CooldownTicks` 40 → `CooldownExpiryTick == 1040`.
- **AC-SK-06 / AC-SK-07**: cast at N (CD 20), N + 1 → `OnCooldown`, N + 19 → `OnCooldown`, N + 20 → accepted.
- **AC-SK-08**: two skills, cast A → B's expiry still 0; cast B → A's unchanged.
- **AC-SK-05**: V-6 rejection → four assertions (code, mana, cooldowns, auto-attack timer through a fake `IAutoAttackSkillUseSink`).
- **Zero cooldown**: `CooldownTicks` 0 → cast on N and N + 1 both accepted.
- **No allocation**: a successful cast inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_ManaCooldown_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Stories 007, 009
