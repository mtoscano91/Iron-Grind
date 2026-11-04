# Story 007: Cast Execution (NotifySkillUsed, BaseDamage, Damage, Kill, Facing)

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 5 hours (4 + 1 for `IMobKillReporter`, readiness 2026-11-04)

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-3 to CR-SK-6, F-SK-1, F-SK-2, CR-SK-4 (`DamageBaseStat`); `design/gdd/hit-detection.md` — CR-HD-7, AC-HD-19 (b), OQ-HD-4; `design/gdd/leveling-system.md` — CR-1.1
**Requirement**: `TR-sk-005`

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-013: Server Random Provider
**ADR Decision Summary**: ADR-010: damage, XP and the auto-attack timer reset are direct calls on injected interfaces (note 2026-10-30); `OnMobDied` is the Tier 2 event for loot, party credit and respawn. ADR-013: crit rolls happen inside Damage Calculation; the Skill System draws no random numbers.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls for damage and XP
- Forbidden: an XP subscriber on `OnMobDied`
- Forbidden: `UnityEngine.Random` or `System.Random` outside `IRandomProvider`

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [x] **AC-SK-31**: `NotifySkillUsed(caster)` is called exactly once per accepted cast (10 casts → 10 calls), after V-9 and before `DamageCalculation`.
- [x] **AC-SK-30**: after an accepted cast, the caster's auto-attack `_actionTimer` is 0 immediately after `NotifySkillUsed` (checked against the real `AutoAttackCombatSystem`, any prior value in [0, `CycleTicks`]).
- [x] **AC-SK-03 / AC-SK-32**: no rejected cast calls `NotifySkillUsed` (one case per rejection code, V-0 to V-9).
- [x] **AC-SK-20 / AC-SK-21**: a `PhysicalDamage` skill calls `DamageCalculation` with `DamageContext.PhysicalSkill`; a `MagicalDamage` skill with `DamageContext.MagicalSkill`.
- [x] **AC-SK-22 / AC-SK-24**: physical BaseDamage = `floor(AP × DamageMultiplier)`; AP 650 × 1.5 → 975.
- [x] **AC-SK-23 / AC-SK-25 / AC-SK-37**: magical BaseDamage = `floor(INT × DamageMultiplier)`; INT 246 × 1.5 → 369; `StatID.AttackPower` is never read on this path.
- [x] **CR-SK-4 override**: `DamageBaseStat = Strength` reads `StatID.Strength` only (Divine Strike, Smite Armor, Judgment).
- [x] **CR-SK-6**: on `IsKill`, the calls run in exactly this order: `GetXPAward(target)`, `AddExperience(caster, xp)`, `ApplyDamage(target, DamageDealt)`. On no kill, `ApplyDamage` only.
- [x] **AC-SK-16 / CR-SK-13**: a miss (`DamageDealt == 0`) refunds no mana: mana after = mana before − effective cost.
- [x] **Decision B**: skill damage is applied through the inner damage applier only. No `DamageEvent` or `SelfDamageEvent` is sent for a skill; clients get skill damage from `SkillCastResult` (Story 009).
- [x] **Decision C / AC-HD-19 (b)**: an accepted `SingleHostile` or `SingleFriendly` cast whose target is not the caster calls `IAutoAttackFaceOutput.Face` once with direction target − caster (XZ), which updates the caster's `FacingAngle` and sends `AutoFaceEvent`; the emitter skips a zero direction or a failed override (it does not suppress an unchanged angle). `Self` casts, rejected casts and a cast on the caster's own entity do not change facing.
- [x] Non-damage skills (`InstantHeal`, `ApplyBuff`, `ApplyDebuff` with no damage flag) call `NotifySkillUsed` but never `DamageCalculation`.
- [x] **Decision D**: a skill kill calls `IMobKillReporter.ReportKill(caster, target, deathPosition)` exactly once, after `ApplyDamage`; with the real `AutoAttackCombatSystem` one `OnMobDied` fires with `KillerEntityId == caster`. No kill → no `ReportKill`.
- [x] **Decision E**: an execution step that throws after V-9 leaves mana at before − effective cost; no cooldown write and no `SendAccepted` follow; the exception is logged.

---

## Implementation Notes

- **Timer reset**: `IAutoAttackSkillUseSink.NotifySkillUsed(EntityID)` exists (`src/ServerLogic/AutoAttackCombat/IAutoAttackSkillUseSink.cs`), implemented by `AutoAttackCombatSystem` (Auto-Attack Story 005). Inject it.
- **Stats**: `ISkillStatSource.GetStat(EntityID, StatID)` with an adapter over `CharacterStats.GetEffectiveStat` (`StatID.AttackPower = 5`, `Intelligence = 3`, `Strength = 0`). Map `DamageStat` to `StatID` in one switch. Floor with integer math: `(int)Math.Floor(stat * (double)multiplier)`.
- **Damage**: `DamageCalculator.Calculate(int baseDamage, EntityID attacker, EntityID target, DamageContext context)` returns `DamageResult` (`src/ServerLogic/DamageCalculation/DamageCalculator.cs:89`). Inject it behind `ISkillDamageCalculator`. **Mob HP**: as in Auto-Attack Story 004 (option 1 at its readiness), mobs have no `CharacterStats` HP, so composition (Story 011) uses a stand-in and tests use a fake calculator that returns `IsKill`. Real mob health comes with Enemy AI.
- **Kill sequence**: reuse the Auto-Attack seam shapes: an XP-award seam (`IAutoAttackXpAward`-like; composition uses the stand-in) and `IAutoAttackExperienceSink` / `CharacterStatsExperienceSink`. Do not share code with `AutoAttackCombatSystem` (its Story 004 note).
- **`ApplyDamage`**: **Decision B (2026-11-02)**: call the inner `IAutoAttackDamageApplier` (`StandInAutoAttackDamageApplier` in production today), not the `DamageOutputApplier` wrapper, which would also send `DamageEvent` / `SelfDamageEvent`. Combat UI shows skill damage from `SkillCastResult`.
- **`OnMobDied` on a skill kill**: ADR-010 Decision 3 makes `ICombatService.OnMobDied` the death event for loot, party credit and respawn, and `AutoAttackCombatSystem` raises it. A skill kill must raise it too, or Enemy AI will miss skill kills. Settle at `/story-readiness`. Recommended: a combat-owned `IMobKillReporter.ReportKill(killer, target, position)` that `AutoAttackCombatSystem` implements and that raises the same event. Record the choice in ADR-010's notes.
- **Facing**: **Decision C (2026-11-02)**. Use `IAutoAttackFaceOutput.Face(EntityID attacker, Vector3 direction)` (`AutoFaceEmitter` in production), which updates facing through Movement's override and sends `AutoFaceEvent` (Auto-Attack Story 009). Direction = target − caster on XZ; a zero vector skips. "When the angle changes": check what `AutoFaceEmitter` already does for an unchanged angle and match it. Write the rule into `hit-detection.md` OQ-HD-4 (resolved: targeted skill casts face the target, `Self` casts do not) and tick AC-HD-19 (b) in the Hit Detection epic's carried items.
- **Order within a successful cast**: V-9 → `NotifySkillUsed` → facing → BaseDamage → `DamageCalculation` → kill sequence or `ApplyDamage` → [Story 008 effects] → cooldown (Story 006). Facing before damage keeps a later Hit Detection facing check consistent; skills do not call `CheckHit` at MVP (no range check beyond V-8).
- **Story 006 carry (2026-11-04)**: Story 006 built `SkillCastExecutor` with an optional `ISkillExecutionStep` between V-9 and the cooldown write; plug this story's steps in there (a composite step if Story 008 adds its own). **Open, settle at `/story-readiness`**: if the execution step throws after mana is deducted, the mana stays spent (`ISkillManaSink` has no refund), and no cooldown or `SendAccepted` follows. Decide between refund and accepted loss, and test it. `RecordingExecution` in `SkillSystem_ManaCooldown_tests.cs` already calls a fake `NotifySkillUsed`.

### Readiness decisions (2026-11-04, user)

- **Decision D — skill kill raises `OnMobDied`**: add a combat-owned `IMobKillReporter.ReportKill(EntityID killer, EntityID target, Vector3 deathPosition)` in `src/ServerLogic/AutoAttackCombat/`, implemented by `AutoAttackCombatSystem`. It resolves the mob type (unknown type → `Debug.LogError` and skip, same rule as `ResolveKill`) and raises the same `OnMobDied` through `RaiseMobDied`. Skill kill sequence: `GetXPAward → AddExperience → ApplyDamage → ReportKill`. Read the death position from `ISkillPositionSource` before `ApplyDamage` (the applier may remove the mob). Recorded in ADR-010 (note 2026-11-04). Supersedes the "Settle at `/story-readiness`" line in the `OnMobDied` note above.
- **Decision E — execution throws after mana deduction**: accepted loss. Mana stays spent, the exception is logged, no cooldown write, no `SendAccepted`. No refund API on `ISkillManaSink`. A throw is a server bug, not a game rule. Supersedes the "Open" line in the Story 006 carry above.

---

## Out of Scope

- Story 008: buff, debuff and heal application
- Story 009: `SkillCastResult` wire output
- Real mob health, `KillEvent`, despawn: Enemy AI

---

## Performance

At most one damage calculation, one stat read and one facing call per accepted cast; no allocation (`Is.Not.AllocatingGCMemory()` over a kill-producing cast).

---

## QA Test Cases

Use one call-recording fake across the skill-use sink, stat source, damage calculator, XP seams, applier and face output, so order is asserted across interfaces.

- **AC-SK-31**: 10 accepted casts → 10 `NotifySkillUsed`; each before its `Calculate`.
- **AC-SK-30**: real `AutoAttackCombatSystem` with the timer at 8 → cast → timer 0.
- **AC-SK-03 / AC-SK-32**: `[TestCase]` per rejection code → zero `NotifySkillUsed`.
- **AC-SK-20 / AC-SK-21**: the context argument per flag.
- **AC-SK-24 / AC-SK-25**: AP 650 × 1.5 → 975; INT 246 × 1.5 → 369; floor case AP 651 × 1.5 → 976 (976.5 floored).
- **AC-SK-23 / AC-SK-37**: the magical path never reads `AttackPower` (recording stat fake).
- **Strength override**: `DamageBaseStat = Strength`, STR 100 × 1.2 → 120; AP and INT not read.
- **Kill order**: `IsKill` true → award, add, apply, report; false → apply only.
- **Decision D (real combat)**: real `AutoAttackCombatSystem` as reporter, skill kill → one `OnMobDied`, `KillerEntityId == caster`, `DeathPosition` = position read before apply.
- **Decision E**: throwing step → mana before − cost, no cooldown expiry, no `SendAccepted`.
- **AC-SK-16**: fake returns a miss → mana dropped by the cost, no refund.
- **Decision B**: a skill hit → zero `DamageEvent` / `SelfDamageEvent` (recording outbox and batch).
- **Decision C**: hostile cast on a mob → one `Face` toward it; friendly cast on another player → one `Face`; `Self` cast → none; rejected cast → none.
- **Non-damage skill**: a heal → `NotifySkillUsed`, no `Calculate`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Execution_tests.cs` — must exist and pass

**Status**: [x] Created

---

## Dependencies

- Depends on: Story 006
- Unlocks: Story 008

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 14/14 passing, none deferred
**Deviations** (advisory, no tech debt):
- Readiness decisions D and E: a skill kill raises `OnMobDied` through the combat-owned `IMobKillReporter.ReportKill` (`AutoAttackCombatSystem` implements it; ADR-010 note 2026-11-04); a throw in the execution step is an accepted loss (mana spent, no cooldown, no `SendAccepted`; the executor logs caster and skill id, then the exception).
- A result with `FinalDamage` ≤ 0 returns after `Calculate`: no `ApplyDamage`, no XP, no report (matches the auto path; a zero-damage `IsKill` awards nothing). Narrower than CR-SK-6's "no kill → `ApplyDamage` only"; mana stays spent (AC-SK-16).
- The damage context is derived from the effect flags (CR-SK-5); `SkillDefinition.DamageContext` is not read; Physical|Magical takes the physical path.
- Death position Y = 0 (`ISkillPositionSource` is XZ only); `Vector3.zero` when the target position is unknown.
- Decision E partial state: a throw between `AddExperience` and `Apply` leaves the XP awarded; no rollback.
- `OnMobDied` subscribers must not cast skills (they run before the cooldown write); documented on `IMobKillReporter`.
- Fail-safe guards: `ComputeBaseDamage` (NaN or ≤0 → 0, saturates `int.MaxValue`); `MapStat` logs an error for an unmapped `DamageStat` and reads AttackPower.
- Decision B is asserted structurally (the step takes only the inner applier).
**Carried**: Story 011 wires `SkillDamageExecutionStep` and adds the zero-`DamageEvent` test through the production inner applier (note written in Story 011); Story 012 checks `DamageContext` agrees with the damage flags at load (note written in Story 012). Hit Detection AC-HD-19 (b) closed (EPIC.md carried table).
**Test Evidence**: Logic: `tests/EditMode/SkillSystem/SkillSystem_Execution_tests.cs` (41 [Test] + 22 [TestCase]); full EditMode run 6241 / 6112 passed / 0 failed / 129 skipped, started 2026-11-04 13:48:09Z (one comment-only test edit afterwards).
**Code Review**: Complete (`/code-review` 2026-11-04: CHANGES REQUIRED, then "fix all", fixed and re-verified)
