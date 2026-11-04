# Story 005: Validation Sequence (V-0, V-3 to V-8)

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-2, CR-SK-17, CR-SK-19, EC-SK-5, EC-SK-8, EC-SK-10
**Requirement**: `TR-sk-004`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: game systems that tick run in phase 3 in an order the composition root lists explicitly. The Skill System step resolves the stored requests there, after Auto-Attack Combat (epic Decision 3).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: narrow injected interfaces for every read from another system
- Guardrail: no allocation per tick; one failing step does not stop the tick

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [x] Checks run in the order V-0, V-3, V-4, V-5, V-5b, V-6, V-7, V-8 (V-9 is Story 006); the first failure emits its code and exits, and no later check runs.
- [x] **V-0**: a caster that is not alive → `CasterNotAlive`.
- [x] **AC-SK-34 / V-3**: `skillID == 0` or unknown → `InvalidSkill`, with a warning logged for an unknown non-zero id (EC-SK-8).
- [x] **V-4**: a skill of the other class → `InvalidSkill`.
- [x] **AC-SK-12 / V-5**: a locked skill → `SkillLocked`; no mana consumed, no cooldown registered.
- [x] **V-5b**: a silenced caster → `Silenced` (Decision A: production stand-in never silences).
- [x] **V-6**: `CooldownExpiryTick > serverTick` → `OnCooldown`. (The cooldown write is Story 006.)
- [x] **V-7**: `targetEntityID == 0`, a dead target or a target not in the zone → `InvalidTarget`. **AC-SK-39**: `SingleHostile` on a friendly (a player) → `InvalidTarget`. **AC-SK-40 / EC-SK-10**: `SingleFriendly` on a hostile (a mob) → `InvalidTarget`.
- [x] **AC-SK-41 (validation part) / CR-SK-19**: for a `Self` skill the target is replaced by the caster before V-7, so a submitted 0 or a hostile id passes V-7 and V-8.
- [x] **V-8**: `RangeUnits > 0` and `distanceSq(caster, target) > RangeUnits²` → `OutOfRange`; exactly at range passes; `RangeUnits == 0` skips the check.
- [x] **AC-SK-01 / AC-SK-02**: any rejection from V-0 to V-8 leaves mana and every `CooldownExpiryTick` unchanged.
- [x] **AC-SK-32 / AC-SK-03 (rejection side)**: no rejection calls `NotifySkillUsed`.
- [x] Every rejection is handed to the cast-output seam (Story 009) with its code; V-1 / V-2 drops never reach this step.

---

## Implementation Notes

- **Step**: `SkillCastStep : IZoneSimulationStep` (or `SkillSystemService` implementing it, as `AutoAttackCombatSystem` does). It walks the stored requests in player-slot order and validates each. Story 006 adds V-9, Story 007 adds execution.
- **Seams** (one interface each, a fake in tests, a production adapter where one exists):
  - V-0 alive: `IEntityDeathSource` exists in `ServerLogic/StatusEffects/` with `CharacterStatsDeathSource`. Reuse it, or add a skill-side wrapper. `ZoneSessionState` Dead / Respawning is not implemented (Death & Respawn has no epic); HP > 0 is the alive test at MVP. Note this for the Death & Respawn epic.
  - V-5b: `ISkillSilenceSource.IsSilenced(EntityID)`. **Decision A (2026-11-02):** the production stand-in `NoSilenceSource` returns false: `status-effects.md` defines no silence effect. Add a note to `status-effects.md` Open Questions that V-5b needs a silence query when one is designed.
  - V-7: target alive and in zone. Players: the zone pool's player list. Mobs: there are no real mobs yet. Use the Auto-Attack precedent, `StandInAutoAttackTargetValidity` over `wiring.Pool`, and a skill-side `ISkillTargetSource` that answers alive, in-zone and kind (player = friendly, mob = hostile; CR-SK-17: "friendly" is a party member or self). **Party check**: at MVP, treat any player as friendly unless the party system offers a query. Check `IPartyZonePresence` (ADR-010 note 2026-10-23) at readiness and record the choice.
  - V-8 positions: the `MovementZoneEntityStateSource` used by combat (positions are this tick's, since the step runs after Movement). Distance on the XZ plane, as Hit Detection does (Y ignored), unless the GDD says 3D. It says `distanceSq(caster, target)`: confirm at readiness, the recommendation is XZ like Hit Detection.
- **Readiness decisions (user, 2026-11-04)**:
  - **V-7 friendly / hostile**: any player in the zone is friendly, any mob is hostile. Do not query `IPartyZonePresence`: its only implementation, `NoPartyZonePresence`, reports every player as solo, so the GDD's "party member or self" (CR-SK-17) would make `SingleFriendly` self-only. This is a known MVP gap, carried to the Party System epic: narrow `SingleFriendly` to party members or self there.
  - **V-8 distance**: XZ plane, Y ignored, as Hit Detection does (AC-HD-4). `distanceSq = dx² + dz²` compared with `RangeUnits²`. Add a QA case with a large Y gap within XZ range → passes.
- `Self` resolution changes only the working copy of the target, not the stored request.
- Rejection reporting goes through `ISkillCastOutput` (declared here, with a recording fake; Story 009 implements it).
- **From Story 004 (code review 2026-11-02)**: read each kept request with `SkillCastIntake.TryGetPending(slot, out PendingSkillCast cast)` for `slot < Capacity`, use `cast.Caster` as the caster (do not resolve the entity again from the slot: a slot can change owner within a tick), and call `ClearAll()` once the step has run.

---

## Out of Scope

- Story 006: V-9 mana and the cooldown write
- Story 007: `NotifySkillUsed` and execution on success
- Story 009: the `SkillCastResult` wire output

---

## Performance

One pass over at most `MAX_PLAYERS_PER_ZONE` request slots per tick. Each check is an array read or an interface call; no allocation (assert with `Is.Not.AllocatingGCMemory()` over a tick with 10 rejected casts).

---

## QA Test Cases

One `[TestCase]` per rejection code, each with four assertions: the code, mana unchanged, all cooldowns unchanged, `NotifySkillUsed` not called.

- **Order**: a request failing both V-3 and V-7 → `InvalidSkill` only; a recording fake shows no V-7 query was made.
- **V-0**: dead caster → `CasterNotAlive`, even for an invalid skill id (V-0 runs first).
- **V-3**: id 0 → `InvalidSkill`, no warning; an unknown id 99 → `InvalidSkill` + one warning.
- **V-4**: a Healer casting a Warrior skill → `InvalidSkill`.
- **V-5 (AC-SK-12)**: locked → `SkillLocked`.
- **V-5b**: fake silenced → `Silenced`; production stand-in → never silenced.
- **V-6**: `CooldownExpiryTick = N + 20`, cast at N → `OnCooldown`; cast at `N + 20` → passes V-6 (AC-SK-07 boundary, value set through a test setter).
- **V-7**: target 0, dead target, target not in zone, AC-SK-39 (hostile skill on a player), AC-SK-40 (friendly skill on a mob) → `InvalidTarget`.
- **Self (AC-SK-41)**: submitted target 0 and a mob id → both pass V-7 and V-8, and the effective target is the caster.
- **V-8**: `[TestCase]` at range − 0.01 (pass), exactly at range (pass), range + 0.01 (`OutOfRange`); `RangeUnits == 0` with a far target → pass.
- **Valid request**: passes V-0 to V-8 and reaches the next stage (a recording fake for the Story 006 seam, or a flag).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Validation_tests.cs` — must exist and pass

**Status**: [x] Created and passing: `SkillSystem_Validation_tests.cs` 42 [Test] + 15 [TestCase] rows, plus `SkillSystem_ValidationAdapters_tests.cs` 18 [Test] (EditMode 6142 / 6013 / 0 failed / 129 skipped, 2026-11-04 12:48:49Z)

---

## Dependencies

- Depends on: Stories 002, 004
- Unlocks: Story 006

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 12/12 passing (all automated). AC-SK-01 mana half and AC-SK-32 / 03 are structural here: the step has no mana or `NotifySkillUsed` dependency. Story 006 adds the real mana assertion; Story 007 asserts no `NotifySkillUsed` on rejection.
**Deviations** (advisory, no tech debt):
- Readiness decisions (user): any player = friendly, any mob = hostile (Party System epic narrows `SingleFriendly` to party or self); V-8 on the XZ plane.
- V-4 is "skill in the caster's own slot list" (equivalent or stricter than the ClassType check).
- NaN / negative `RangeUnits` → `ServerError` (fail closed, checked before the Self / range-0 skip); missing V-8 position → `ServerError`; a throwing request is logged (one error per tick) and gets no rejection; warnings throttled once per tick.
- `SkillCastStep` takes the concrete `SkillCastIntake` and `SkillSystemService` (narrow read interfaces optional later).
- V-0 reads CharacterStats HP, V-7 reads zone-state HP: for one tick a Self caster can get `InvalidTarget` instead of `CasterNotAlive` (pinned by a test).
- Death & Respawn epic: `ZoneSessionState` Dead / Respawning not implemented; HP > 0 is the alive test.
**Carried to Story 006**: implement `ISkillCastValidatedSink` (V-9 `ConsumeMana`, cooldown write); add the real "mana unchanged on rejection" assertion.
**Carried to Story 009**: implement `ISkillCastOutput.SendRejection(caster, in request, code)`.
**Carried to Story 011**: register `SkillCastStep` after Auto-Attack Combat in the phase-3 order; production seams `CharacterStatsSkillCasterAliveSource`, `NoSilenceSource`, `ZoneSkillTargetSource`, `ZoneSkillPositionSource`; note the V-0 / V-7 alive-source split.
**Also written**: `design/gdd/status-effects.md` OQ-SE-3 (V-5b silence query, open, not blocking MVP).
**Test Evidence**: Logic: `tests/EditMode/SkillSystem/SkillSystem_Validation_tests.cs`, `tests/EditMode/SkillSystem/SkillSystem_ValidationAdapters_tests.cs`
**Code Review**: Complete (2026-11-04, CHANGES REQUIRED → fix all → fixed)
