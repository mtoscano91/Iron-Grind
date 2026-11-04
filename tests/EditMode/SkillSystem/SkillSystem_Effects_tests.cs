using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode tests for Skill System Story 008 - effect application: buffs, debuffs, instant heal and the execution
    /// outcome seam (CR-SK-7, F-SK-3, EC-SK-4, EC-SK-9, AC-SK-26, 27, 29, 41; TR-sk-006). The effects step runs against a
    /// recording <see cref="ISkillEffectSink"/>; ordering tests share one call log with the real damage step.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_Effects_Tests
    {
        private const int MAX_PLAYERS = 4;
        private const uint TICK = 100;
        private const uint ID_A = 1001;
        private const uint ID_MOB = 2001;
        private const uint SKILL_MAIN = 101;
        private const uint BUFF_RAW = 7u;
        private const uint BUFF_DURATION = 100u;
        private const int MANA_MAIN = 100;
        private const int COOLDOWN_MAIN = 20;
        private const float RANGE = 10f;
        private const float MULT_1_5 = 1.5f;
        private const float HEAL_MULT_2 = 2.0f;
        private const float HEAL_MULT_HALF = 0.5f;
        private const int INT_STAT = 246;
        private const int HEAL_246 = 492;
        private const int AP = 650;
        private const int HIT_DAMAGE = 55;
        private const int SENTINEL = -1;
        private const string LOG_CALCULATE = "Calculate";
        private const string LOG_EFFECT = "ApplyEffect";
        private const string LOG_HEAL = "ApplyHeal";

        private static readonly EntityID PLAYER_A = new EntityID(ID_A);
        private static readonly EntityID MOB = new EntityID(ID_MOB);

        // ---- fakes ----

        private sealed class RecEffectSink : ISkillEffectSink
        {
            public List<string> Log;
            public ApplyResult EffectResult = ApplyResult.Success;
            public ApplyResult HealResult = ApplyResult.Success;
            public int EffectCalls;
            public int HealCalls;
            public EntityID LastEffectTarget;
            public EntityID LastEffectCaster;
            public BuffID LastBuffId;
            public EntityID LastHealTarget;
            public float LastHealAmount = SENTINEL;

            public ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition)
            {
                EffectCalls++;
                LastEffectTarget = target;
                LastEffectCaster = caster;
                LastBuffId = definition.BuffId;
                Log?.Add(LOG_EFFECT);
                return EffectResult;
            }

            public ApplyResult ApplyInstantHeal(EntityID entity, float amount)
            {
                HealCalls++;
                LastHealTarget = entity;
                LastHealAmount = amount;
                Log?.Add(LOG_HEAL);
                return HealResult;
            }
        }

        private sealed class FakeStats : ISkillStatSource
        {
            public int Int = INT_STAT;
            public int Ap = AP;

            public int GetStat(EntityID entity, StatID stat)
            {
                return stat == StatID.Intelligence ? Int : Ap;
            }
        }

        private sealed class NoopSkillUse : IAutoAttackSkillUseSink
        {
            public void NotifySkillUsed(EntityID casterEntityID) { }
        }

        private sealed class FakePositions : ISkillPositionSource
        {
            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                x = 0f;
                z = 0f;
                return true;
            }
        }

        private sealed class RecCalc : ISkillDamageCalculator
        {
            public List<string> Log;
            public DamageResult Result = Dmg(HIT_DAMAGE, false, false);

            public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
            {
                Log?.Add(LOG_CALCULATE);
                return Result;
            }
        }

        private sealed class NoopXp : IAutoAttackXpAward
        {
            public int GetXPAward(EntityID target) => 0;
        }

        private sealed class NoopExperience : IAutoAttackExperienceSink
        {
            public void AddExperience(EntityID attacker, int amount) { }
        }

        private sealed class CountingApplier : IAutoAttackDamageApplier
        {
            public int Calls;

            public void Apply(EntityID attacker, EntityID target, in DamageResult result) { Calls++; }
        }

        private sealed class NoopFace : IAutoAttackFaceOutput
        {
            public void Face(EntityID attacker, Vector3 direction) { }
        }

        private sealed class NoopReporter : IMobKillReporter
        {
            public void ReportKill(EntityID killer, EntityID target, Vector3 deathPosition) { }
        }

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            public bool TryGetSlotIndex(EntityID id, out int index)
            {
                index = id.RawValue == ID_A ? 0 : -1;
                return index >= 0;
            }
        }

        private sealed class FakeLevels : ISkillLevelSource
        {
            public int GetLevel(EntityID entity) => 1;
        }

        private sealed class NullUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private sealed class FakeConfig : ISkillSystemConfigSource
        {
            public SkillSystemConfigValues Values { get; set; } =
                SkillSystemConfigValues.Create(1.0f, SkillSystemConfigValues.CAST_STALE_TICK_TOLERANCE_DEFAULT);
        }

        private sealed class RecMana : ISkillManaSink
        {
            public readonly List<int> Calls = new List<int>(8);

            public bool ConsumeMana(EntityID entity, int cost)
            {
                Calls.Add(cost);
                return true;
            }
        }

        private sealed class RecOutput : ISkillCastOutput
        {
            public int AcceptedCount;
            public int RejectionCount;
            public SkillExecutionOutcome LastOutcome = new SkillExecutionOutcome { DamageDealt = SENTINEL, HealAmount = SENTINEL };

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
            {
                RejectionCount++;
            }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks, in SkillExecutionOutcome outcome)
            {
                AcceptedCount++;
                LastOutcome = outcome;
            }
        }

        // ---- fixture state ----

        private readonly List<string> _log = new List<string>(64);
        private RecEffectSink _sink;
        private FakeStats _stats;
        private RecCalc _calc;
        private CountingApplier _applier;
        private SkillDamageExecutionStep _damageStep;
        private SkillEffectExecutionStep _effectStep;
        private CompositeSkillExecutionStep _composite;

        private SkillDatabase _database;
        private SkillSystemService _service;
        private RecMana _mana;
        private RecOutput _output;

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _sink = new RecEffectSink { Log = _log };
            _stats = new FakeStats();
            _calc = new RecCalc { Log = _log };
            _applier = new CountingApplier();
            _damageStep = new SkillDamageExecutionStep(
                new NoopSkillUse(), _stats, new FakePositions(), _calc, new NoopXp(), new NoopExperience(),
                _applier, new NoopFace(), new NoopReporter());
            _effectStep = new SkillEffectExecutionStep(_sink, _stats);
            _composite = new CompositeSkillExecutionStep(_damageStep, _effectStep);
            _mana = new RecMana();
            _output = new RecOutput();
        }

        [TearDown]
        public void TearDown()
        {
            if (_database != null)
            {
                Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        private static DamageResult Dmg(int finalDamage, bool isCrit, bool isKill)
        {
            return new DamageResult(finalDamage, 0, finalDamage, isCrit, isKill, DamageContext.PhysicalSkill, false);
        }

        private static SkillDefinition Skill(
            SkillEffectType effect,
            TargetConstraint constraint = TargetConstraint.SingleHostile,
            float healMultiplier = 0f)
        {
            return new SkillDefinition
            {
                ID = SKILL_MAIN,
                UnlockLevel = 1,
                RangeUnits = RANGE,
                ManaCost = MANA_MAIN,
                CooldownTicks = COOLDOWN_MAIN,
                TargetConstraint = constraint,
                SkillEffectType = effect,
                DamageMultiplier = MULT_1_5,
                DamageBaseStat = DamageStat.AttackPower,
                HealMultiplier = healMultiplier,
                BuffID = (BuffID)BUFF_RAW,
                BuffDefinition = new BuffDefinition((BuffID)BUFF_RAW, default, BUFF_DURATION, 1f, 0f),
            };
        }

        private static ValidatedSkillCast MakeCast(in SkillDefinition def, EntityID target)
        {
            var request = new SkillCastRequest(ID_A, def.ID, 0u, 0u);
            return new ValidatedSkillCast(PLAYER_A, target, in def, 0, TICK, in request);
        }

        // Runs one step against a fresh zeroed outcome and returns it.
        private static SkillExecutionOutcome Run(ISkillExecutionStep step, in SkillDefinition def, EntityID target)
        {
            ValidatedSkillCast cast = MakeCast(in def, target);
            var outcome = default(SkillExecutionOutcome);
            step.Execute(in cast, ref outcome);
            return outcome;
        }

        // Real executor over a real skill store: mana, execution, cooldown write, accepted report.
        private SkillCastExecutor BuildExecutor(ISkillExecutionStep step, in SkillDefinition def)
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(0, new ClassDefinition(0, 0, 0, 0, 0, new[] { def.ID }));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(new[] { def });
            var slots = new FakeSlotIndex();
            _service = new SkillSystemService(registry, _database, new FakeLevels(), slots, new NullUnlockOutput(), MAX_PLAYERS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            return new SkillCastExecutor(_mana, new FakeConfig(), _service, _output, step);
        }

        private uint Cooldown()
        {
            Assert.That(_service.TryGetSkill(PLAYER_A, 0, out SkillInstance skill), Is.True);
            return skill.CooldownExpiryTick;
        }

        // ---- CR-SK-7 ----

        [TestCase(SkillEffectType.ApplyDebuff)]
        [TestCase(SkillEffectType.ApplyBuff)]
        public void test_effects_buff_or_debuff_calls_apply_effect_once_with_authored_definition(SkillEffectType effect)
        {
            SkillDefinition def = Skill(effect);

            Run(_effectStep, in def, MOB);

            Assert.That(_sink.EffectCalls, Is.EqualTo(1));
            Assert.That(_sink.LastEffectTarget, Is.EqualTo(MOB));
            Assert.That(_sink.LastEffectCaster, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.LastBuffId, Is.EqualTo((BuffID)BUFF_RAW));
            Assert.That(_sink.HealCalls, Is.EqualTo(0));
        }

        // ---- AC-SK-26 ----

        [Test]
        public void test_effects_damage_plus_debuff_records_calculate_before_apply_effect()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);

            Run(_composite, in def, MOB);

            Assert.That(_log, Is.EqualTo(new[] { LOG_CALCULATE, LOG_EFFECT }));
        }

        [Test]
        public void test_effects_damage_plus_instant_heal_runs_damage_before_heal()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.InstantHeal, healMultiplier: HEAL_MULT_2);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(_log, Is.EqualTo(new[] { LOG_CALCULATE, LOG_HEAL }));
            Assert.That(outcome.DamageDealt, Is.EqualTo(HIT_DAMAGE));
            Assert.That(outcome.HealAmount, Is.EqualTo(HEAL_246));
        }

        // ---- AC-SK-27 / EC-SK-4 ----

        [Test]
        public void test_effects_kill_skips_apply_effect_but_damage_is_still_applied()
        {
            _calc.Result = Dmg(HIT_DAMAGE, false, true);
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(_sink.EffectCalls, Is.EqualTo(0));
            Assert.That(_applier.Calls, Is.EqualTo(1));
            Assert.That(outcome.DamageDealt, Is.EqualTo(HIT_DAMAGE));
            Assert.That(outcome.IsKill, Is.True);
        }

        // ---- miss ----

        [Test]
        public void test_effects_miss_still_calls_apply_effect_once()
        {
            _calc.Result = Dmg(0, false, false);
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(outcome.DamageDealt, Is.EqualTo(0));
            Assert.That(outcome.IsKill, Is.False);
            Assert.That(_sink.EffectCalls, Is.EqualTo(1));
            Assert.That(_sink.LastEffectTarget, Is.EqualTo(MOB));
        }

        [Test]
        public void test_effects_kill_flag_with_zero_damage_is_no_kill_and_still_applies()
        {
            _calc.Result = Dmg(0, false, true);
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(outcome.IsKill, Is.False, "nothing was applied, so the target is not dead");
            Assert.That(_sink.EffectCalls, Is.EqualTo(1));
        }

        // ---- AC-SK-29 / F-SK-3 ----

        [TestCase(INT_STAT, HEAL_MULT_2, HEAL_246)]
        [TestCase(1, HEAL_MULT_HALF, 0)]
        public void test_effects_instant_heal_passes_floor_of_int_times_multiplier_even_when_zero(int intelligence, float multiplier, int expected)
        {
            _stats.Int = intelligence;
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, TargetConstraint.SingleFriendly, multiplier);

            SkillExecutionOutcome outcome = Run(_effectStep, in def, PLAYER_A);

            Assert.That(_sink.HealCalls, Is.EqualTo(1), "called even when the amount is 0");
            Assert.That(_sink.LastHealTarget, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.LastHealAmount, Is.EqualTo((float)expected));
            Assert.That(outcome.HealAmount, Is.EqualTo(expected));
            Assert.That(_sink.EffectCalls, Is.EqualTo(0));
        }

        [TestCase(100, float.NaN, 0)]
        [TestCase(100, float.PositiveInfinity, 0)]
        [TestCase(0, float.PositiveInfinity, 0)]
        [TestCase(100, -1f, 0)]
        [TestCase(-100, 2f, 0)]
        [TestCase(100, 0f, 0)]
        [TestCase(int.MaxValue, 5f, int.MaxValue)]
        [TestCase(INT_STAT, HEAL_MULT_2, HEAL_246)]
        [TestCase(1, HEAL_MULT_HALF, 0)]
        public void test_effects_compute_heal_amount_boundaries(int intelligence, float multiplier, int expected)
        {
            Assert.That(SkillEffectExecutionStep.ComputeHealAmount(intelligence, multiplier), Is.EqualTo(expected));
        }

        // ---- AC-SK-41 ----

        [Test]
        public void test_effects_self_buff_applies_on_the_caster()
        {
            SkillDefinition def = Skill(SkillEffectType.ApplyBuff, TargetConstraint.Self);

            Run(_effectStep, in def, PLAYER_A);

            Assert.That(_sink.EffectCalls, Is.EqualTo(1));
            Assert.That(_sink.LastEffectTarget, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.LastEffectCaster, Is.EqualTo(PLAYER_A));
        }

        // ---- non-buff skills ----

        [Test]
        public void test_effects_pure_damage_skill_makes_no_effect_or_heal_calls()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(_sink.EffectCalls, Is.EqualTo(0));
            Assert.That(_sink.HealCalls, Is.EqualTo(0));
            Assert.That(outcome.HealAmount, Is.EqualTo(0));
            Assert.That(outcome.DamageDealt, Is.EqualTo(HIT_DAMAGE));
        }

        // ---- EC-SK-9 / EC-SK-4 through the real executor ----

        [TestCase(ApplyResult.CapacityExceeded)]
        [TestCase(ApplyResult.EffectSlotsFull)]
        public void test_effects_capacity_exceeded_leaves_cast_accepted_with_mana_and_cooldown_paid(ApplyResult result)
        {
            _sink.EffectResult = result;
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);
            SkillCastExecutor executor = BuildExecutor(_composite, in def);
            ValidatedSkillCast cast = MakeCast(in def, MOB);

            executor.Accept(in cast);

            AssertAcceptedDespiteEffectFailure();
            Assert.That(_output.LastOutcome.DamageDealt, Is.EqualTo(HIT_DAMAGE), "the result still carries the damage dealt");
        }

        [Test]
        public void test_effects_invalid_target_leaves_cast_accepted_with_mana_and_cooldown_paid()
        {
            _sink.EffectResult = ApplyResult.InvalidTarget;
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff);
            SkillCastExecutor executor = BuildExecutor(_composite, in def);
            ValidatedSkillCast cast = MakeCast(in def, MOB);

            executor.Accept(in cast);

            AssertAcceptedDespiteEffectFailure();
        }

        private void AssertAcceptedDespiteEffectFailure()
        {
            Assert.That(_sink.EffectCalls, Is.EqualTo(1), "no retry");
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(_output.RejectionCount, Is.EqualTo(0));
            Assert.That(_mana.Calls, Is.EqualTo(new[] { MANA_MAIN }));
            Assert.That(Cooldown(), Is.EqualTo(TICK + COOLDOWN_MAIN));
        }

        // ---- outcome seam ----

        [Test]
        public void test_effects_outcome_heal_skill_reaches_send_accepted_with_heal_amount_492()
        {
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, TargetConstraint.Self, HEAL_MULT_2);
            SkillCastExecutor executor = BuildExecutor(_composite, in def);
            ValidatedSkillCast cast = MakeCast(in def, PLAYER_A);

            executor.Accept(in cast);

            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(_output.LastOutcome.HealAmount, Is.EqualTo(HEAL_246));
            Assert.That(_output.LastOutcome.DamageDealt, Is.EqualTo(0));
        }

        [Test]
        public void test_effects_outcome_damage_skill_reaches_send_accepted_with_calculator_result()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true, true);
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage);
            SkillCastExecutor executor = BuildExecutor(_composite, in def);
            ValidatedSkillCast cast = MakeCast(in def, MOB);

            executor.Accept(in cast);

            Assert.That(_output.LastOutcome.DamageDealt, Is.EqualTo(HIT_DAMAGE));
            Assert.That(_output.LastOutcome.IsCrit, Is.True);
            Assert.That(_output.LastOutcome.IsKill, Is.True);
            Assert.That(_output.LastOutcome.HealAmount, Is.EqualTo(0));
        }

        [Test]
        public void test_effects_outcome_without_execution_step_is_zeroed()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage);
            SkillCastExecutor executor = BuildExecutor(null, in def);
            ValidatedSkillCast cast = MakeCast(in def, MOB);

            executor.Accept(in cast);

            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(_output.LastOutcome.DamageDealt, Is.EqualTo(0));
            Assert.That(_output.LastOutcome.IsCrit, Is.False);
            Assert.That(_output.LastOutcome.IsKill, Is.False);
            Assert.That(_output.LastOutcome.HealAmount, Is.EqualTo(0));
        }

        // ---- delivered heal and kill skip (code review 2026-11-04) ----

        [Test]
        public void test_effects_kill_skips_heal_and_reports_zero_heal()
        {
            _calc.Result = Dmg(HIT_DAMAGE, false, true);
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.InstantHeal, healMultiplier: HEAL_MULT_2);

            SkillExecutionOutcome outcome = Run(_composite, in def, MOB);

            Assert.That(_sink.HealCalls, Is.EqualTo(0), "a target this cast killed gets no heal");
            Assert.That(outcome.HealAmount, Is.EqualTo(0));
            Assert.That(outcome.IsKill, Is.True);
        }

        [TestCase(ApplyResult.InvalidTarget)]
        [TestCase(ApplyResult.InvalidAmount)]
        public void test_effects_rejected_heal_keeps_cast_accepted_and_reports_zero_heal(ApplyResult result)
        {
            _sink.HealResult = result;
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, TargetConstraint.SingleFriendly, HEAL_MULT_2);
            SkillCastExecutor executor = BuildExecutor(_effectStep, in def);
            ValidatedSkillCast cast = MakeCast(in def, PLAYER_A);

            executor.Accept(in cast);

            Assert.That(_sink.HealCalls, Is.EqualTo(1), "called once, no retry");
            Assert.That(_sink.LastHealAmount, Is.EqualTo((float)HEAL_246));
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(_output.RejectionCount, Is.EqualTo(0));
            Assert.That(_output.LastOutcome.HealAmount, Is.EqualTo(0), "only a delivered heal is reported");
        }

        [Test]
        public void test_effects_buff_plus_heal_applies_buff_then_heal()
        {
            SkillDefinition def = Skill(SkillEffectType.ApplyBuff | SkillEffectType.InstantHeal, TargetConstraint.SingleFriendly, HEAL_MULT_2);

            SkillExecutionOutcome outcome = Run(_effectStep, in def, PLAYER_A);

            Assert.That(_log, Is.EqualTo(new[] { LOG_EFFECT, LOG_HEAL }));
            Assert.That(_sink.EffectCalls, Is.EqualTo(1));
            Assert.That(_sink.HealCalls, Is.EqualTo(1));
            Assert.That(outcome.HealAmount, Is.EqualTo(HEAL_246));
        }

        [Test]
        public void test_effects_reused_step_targets_each_cast_target()
        {
            SkillDefinition def = Skill(SkillEffectType.ApplyDebuff);

            Run(_effectStep, in def, MOB);
            Assert.That(_sink.LastEffectTarget, Is.EqualTo(MOB));

            Run(_effectStep, in def, PLAYER_A);
            Assert.That(_sink.LastEffectTarget, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.EffectCalls, Is.EqualTo(2));
        }

        // ---- adapter over the real StatusEffectsService ----

        [Test]
        public void test_effects_adapter_passes_through_to_real_status_effects_service()
        {
            var layer = new global::IronGrind.Tests.EditMode.StatusEffects.RecordingBuffModifierLayer();
            var slots = new global::IronGrind.Tests.EditMode.StatusEffects.DictionaryEntitySlotIndex();
            slots.Add(PLAYER_A, 0);
            var service = new StatusEffectsService(
                layer, layer, slots, new global::IronGrind.Tests.EditMode.StatusEffects.FakeEntityDeathSource());
            var adapter = new StatusEffectsSkillEffectSink(service);
            var definition = new BuffDefinition(
                (BuffID)BUFF_RAW, new[] { new StatModifier(StatID.AttackPower, AP, 0f) }, BUFF_DURATION, 0f, 0f);

            Assert.That(adapter.ApplyEffect(PLAYER_A, PLAYER_A, in definition), Is.EqualTo(ApplyResult.Success));
            Assert.That(service.TryGetActiveEffect(PLAYER_A, (BuffID)BUFF_RAW, out _), Is.True);
            Assert.That(adapter.ApplyInstantHeal(MOB, HEAL_246), Is.EqualTo(ApplyResult.InvalidTarget), "unknown entity");
            Assert.That(adapter.ApplyInstantHeal(PLAYER_A, 0f), Is.EqualTo(ApplyResult.InvalidAmount));
            Assert.That(adapter.ApplyInstantHeal(PLAYER_A, HEAL_246), Is.EqualTo(ApplyResult.Success));
        }

        // ---- constructors ----

        [Test]
        public void test_effects_composite_null_arguments_throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CompositeSkillExecutionStep(null, _effectStep));
            Assert.Throws<System.ArgumentNullException>(() => new CompositeSkillExecutionStep(_damageStep, null));
        }

        [Test]
        public void test_effects_effect_step_and_adapter_null_arguments_throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SkillEffectExecutionStep(null, _stats));
            Assert.Throws<System.ArgumentNullException>(() => new SkillEffectExecutionStep(_sink, null));
            Assert.Throws<System.ArgumentNullException>(() => new StatusEffectsSkillEffectSink(null));
        }

        // ---- no allocation ----

        [Test]
        public void test_effects_buff_cast_does_not_allocate()
        {
            _sink.Log = null;
            _calc.Log = null;
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff | SkillEffectType.InstantHeal, healMultiplier: HEAL_MULT_2);
            ValidatedSkillCast cast = MakeCast(in def, MOB);
            var outcome = default(SkillExecutionOutcome);
            _composite.Execute(in cast, ref outcome);

            Assert.That(() => _composite.Execute(in cast, ref outcome), Is.Not.AllocatingGCMemory());
        }
    }
}
