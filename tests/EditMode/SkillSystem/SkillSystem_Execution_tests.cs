using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.Events;
using IronGrind.HitDetection;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.AutoAttackCombat;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode tests for Skill System Story 007 - cast execution: NotifySkillUsed, BaseDamage, damage, kill sequence,
    /// facing, Decisions B to E (CR-SK-3..6, F-SK-1, F-SK-2, AC-SK-03, 16, 20..25, 30..32, 37; TR-sk-005).
    /// One shared call log records every cross-interface call so order is asserted across fakes.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_Execution_Tests
    {
        private const int MAX_PLAYERS = 4;
        private const uint TICK = 100;
        private const uint ID_A = 1001;
        private const uint ID_B = 1002;
        private const uint ID_MOB = 2001;
        private const uint SKILL_MAIN = 101;
        private const uint SKILL_OTHER = 102;
        private const int MANA_MAIN = 100;
        private const int COOLDOWN_MAIN = 20;
        private const float RANGE = 10f;
        private const float DELTA = 0.05f;
        private const float MULT_1_5 = 1.5f;
        private const float MULT_1_2 = 1.2f;
        private const float TARGET_X = 3f;
        private const float TARGET_Z = 4f;
        private const float MOVED_X = 99f;
        private const float FAR_X = 50f;
        private const int AP = 650;
        private const int AP_FLOOR = 651;
        private const int INT_STAT = 246;
        private const int STR = 100;
        private const int OTHER_STAT = 7777;
        private const int XP = 120;
        private const int HIT_DAMAGE = 55;
        private const int CAST_COUNT = 10;
        private const int TIMER_WARM_TICKS = 8;
        private const uint ZONE_RAW = 7u;
        private const uint MOB_RAW = 5001u;
        private const float POS_TOLERANCE = 1e-4f;
        private const float CASTER_X = 3f;
        private const float CASTER_Z = 4f;
        private const float FACE_TARGET_X = 10f;
        private const float FACE_TARGET_Z = -2f;
        private const uint UNKNOWN_CASTER = 9999u;
        private const int ADAPTER_BASE = 100;
        private const int ADAPTER_MAX_HP = 99999;
        private const float ADAPTER_CRIT_CHANCE = 0.75f;
        private const float ADAPTER_CRIT_MULTIPLIER = 1.5f;
        private const float ROLL_CRIT = 0.0f;
        private const int STAT_AP = 111;
        private const int STAT_INT = 222;
        private const int STAT_STR = 333;
        private const int UNMAPPED_STAT = 99;

        private static readonly EntityID PLAYER_A = new EntityID(ID_A);
        private static readonly EntityID PLAYER_B = new EntityID(ID_B);
        private static readonly EntityID MOB = new EntityID(ID_MOB);

        private enum CallKind { Mana, Notify, Face, Calculate, Award, Add, Apply, Report, Accepted }

        // ---- fakes ----

        private sealed class RecSkillUse : IAutoAttackSkillUseSink
        {
            public List<CallKind> Log;
            public int Calls;

            public void NotifySkillUsed(EntityID casterEntityID)
            {
                Calls++;
                Log.Add(CallKind.Notify);
            }
        }

        private sealed class RecStats : ISkillStatSource
        {
            public int Str = STR;
            public int Int = INT_STAT;
            public int Ap = AP;
            public int StrReads;
            public int IntReads;
            public int ApReads;

            public int GetStat(EntityID entity, StatID stat)
            {
                switch (stat)
                {
                    case StatID.Strength:
                        StrReads++;
                        return Str;
                    case StatID.Intelligence:
                        IntReads++;
                        return Int;
                    case StatID.AttackPower:
                        ApReads++;
                        return Ap;
                    default:
                        return OTHER_STAT;
                }
            }
        }

        private sealed class RecPositions : ISkillPositionSource
        {
            public EntityID Caster = PLAYER_A;
            public float CasterX;
            public float CasterZ;
            public float TargetX = TARGET_X;
            public float TargetZ = TARGET_Z;
            public bool CasterKnown = true;
            public bool TargetKnown = true;

            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                bool isCaster = entity == Caster;
                x = isCaster ? CasterX : TargetX;
                z = isCaster ? CasterZ : TargetZ;
                return isCaster ? CasterKnown : TargetKnown;
            }
        }

        private sealed class RecCalc : ISkillDamageCalculator
        {
            public List<CallKind> Log;
            public DamageResult Result = Dmg(HIT_DAMAGE, false);
            public int Calls;
            public int LastBase = -1;
            public DamageContext LastContext = (DamageContext)(-1);
            public EntityID LastAttacker;
            public EntityID LastTarget;

            public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
            {
                Calls++;
                LastBase = baseDamage;
                LastContext = context;
                LastAttacker = attackerId;
                LastTarget = targetId;
                Log.Add(CallKind.Calculate);
                return Result;
            }
        }

        private sealed class RecXp : IAutoAttackXpAward
        {
            public List<CallKind> Log;
            public int Award = XP;
            public EntityID LastTarget;

            public int GetXPAward(EntityID target)
            {
                LastTarget = target;
                Log.Add(CallKind.Award);
                return Award;
            }
        }

        private sealed class RecExp : IAutoAttackExperienceSink
        {
            public List<CallKind> Log;
            public EntityID LastAttacker;
            public int LastAmount = -1;

            public void AddExperience(EntityID attacker, int amount)
            {
                LastAttacker = attacker;
                LastAmount = amount;
                Log.Add(CallKind.Add);
            }
        }

        private sealed class RecApplier : IAutoAttackDamageApplier
        {
            public List<CallKind> Log;
            public RecPositions MoveOnApply;
            public int Calls;
            public EntityID LastAttacker;
            public EntityID LastTarget;
            public int LastDamage = -1;

            public void Apply(EntityID attacker, EntityID target, in DamageResult result)
            {
                Calls++;
                LastAttacker = attacker;
                LastTarget = target;
                LastDamage = result.FinalDamage;
                Log.Add(CallKind.Apply);
                if (MoveOnApply != null)
                {
                    MoveOnApply.TargetX = MOVED_X;
                }
            }
        }

        private sealed class RecFace : IAutoAttackFaceOutput
        {
            public List<CallKind> Log;
            public int Calls;
            public EntityID LastAttacker;
            public Vector3 LastDirection;

            public void Face(EntityID attacker, Vector3 direction)
            {
                Calls++;
                LastAttacker = attacker;
                LastDirection = direction;
                Log.Add(CallKind.Face);
            }
        }

        private sealed class RecReporter : IMobKillReporter
        {
            public List<CallKind> Log;
            public int Calls;
            public EntityID LastKiller;
            public EntityID LastTarget;
            public Vector3 LastPosition;

            public void ReportKill(EntityID killer, EntityID target, Vector3 deathPosition)
            {
                Calls++;
                LastKiller = killer;
                LastTarget = target;
                LastPosition = deathPosition;
                Log.Add(CallKind.Report);
            }
        }

        private sealed class ThrowingStep : ISkillExecutionStep
        {
            public void Execute(in ValidatedSkillCast cast)
            {
                throw new System.InvalidOperationException("step-fault");
            }
        }

        private sealed class ThrowingApplier : IAutoAttackDamageApplier
        {
            public void Apply(EntityID attacker, EntityID target, in DamageResult result)
            {
                throw new System.InvalidOperationException("apply-fault");
            }
        }

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            public bool TryGetSlotIndex(EntityID id, out int index)
            {
                index = id.RawValue == ID_A ? 0 : id.RawValue == ID_B ? 1 : -1;
                return index >= 0;
            }
        }

        private sealed class FakeLevels : ISkillLevelSource
        {
            public int GetLevel(EntityID entity) => 1;
        }

        private sealed class FakeAlive : ISkillCasterAliveSource
        {
            public bool Value = true;

            public bool IsAlive(EntityID entity) => Value;
        }

        private sealed class FakeSilence : ISkillSilenceSource
        {
            public bool Value;

            public bool IsSilenced(EntityID entity) => Value;
        }

        private sealed class FakeTargets : ISkillTargetSource
        {
            public bool TargetAlive = true;

            public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
            {
                kind = target.RawValue == ID_MOB ? SkillTargetKind.Mob : SkillTargetKind.Player;
                isAlive = TargetAlive;
                return true;
            }
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
            public List<CallKind> Log;
            public readonly List<int> Calls = new List<int>(16);
            public bool Result = true;

            public bool ConsumeMana(EntityID entity, int cost)
            {
                Calls.Add(cost);
                Log.Add(CallKind.Mana);
                return Result;
            }
        }

        private sealed class RecOutput : ISkillCastOutput
        {
            public List<CallKind> Log;
            public readonly List<SkillCastRejectionCode> Rejections = new List<SkillCastRejectionCode>(8);
            public int AcceptedCount;

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
            {
                Rejections.Add(code);
            }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks)
            {
                AcceptedCount++;
                Log.Add(CallKind.Accepted);
            }
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class AlwaysValid : IAutoAttackTargetValidity
        {
            public bool IsValidTarget(EntityID target) => true;
        }

        private sealed class UnitAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity) => 1.0f;
        }

        // ---- fixture state ----

        private readonly List<CallKind> _calls = new List<CallKind>(1024);
        private RecSkillUse _skillUse;
        private RecStats _stats;
        private RecPositions _positions;
        private RecCalc _calc;
        private RecXp _xp;
        private RecExp _exp;
        private RecApplier _applier;
        private RecFace _face;
        private RecReporter _reporter;
        private SkillDamageExecutionStep _step;

        private SkillDatabase _database;
        private SkillSystemService _service;
        private SkillCastIntake _intake;
        private SkillCastStep _castStep;
        private RecMana _mana;
        private RecOutput _output;
        private FakeAlive _alive;
        private FakeSilence _silence;
        private FakeTargets _targets;

        private AutoAttackCombatSystem _combat;
        private ZoneSlotPool _pool;
        private int _diedCount;
        private MobDeathContext _lastDeath;

        [SetUp]
        public void SetUp()
        {
            _calls.Clear();
            _diedCount = 0;
            _lastDeath = default;
            _skillUse = new RecSkillUse { Log = _calls };
            _stats = new RecStats();
            _positions = new RecPositions();
            _calc = new RecCalc { Log = _calls };
            _xp = new RecXp { Log = _calls };
            _exp = new RecExp { Log = _calls };
            _applier = new RecApplier { Log = _calls };
            _face = new RecFace { Log = _calls };
            _reporter = new RecReporter { Log = _calls };
            _mana = new RecMana { Log = _calls };
            _output = new RecOutput { Log = _calls };
            _alive = new FakeAlive();
            _silence = new FakeSilence();
            _targets = new FakeTargets();
            _step = NewStep(_skillUse, _reporter);
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

        private SkillDamageExecutionStep NewStep(IAutoAttackSkillUseSink skillUse, IMobKillReporter reporter)
        {
            return new SkillDamageExecutionStep(skillUse, _stats, _positions, _calc, _xp, _exp, _applier, _face, reporter);
        }

        private static DamageResult Dmg(int finalDamage, bool isKill)
        {
            return new DamageResult(finalDamage, 0, finalDamage, false, isKill, DamageContext.PhysicalSkill, false);
        }

        private static SkillDefinition Skill(
            SkillEffectType effect,
            DamageStat stat,
            float multiplier,
            TargetConstraint constraint = TargetConstraint.SingleHostile,
            uint id = SKILL_MAIN)
        {
            return new SkillDefinition
            {
                ID = id,
                UnlockLevel = 1,
                RangeUnits = RANGE,
                ManaCost = MANA_MAIN,
                CooldownTicks = COOLDOWN_MAIN,
                TargetConstraint = constraint,
                SkillEffectType = effect,
                DamageMultiplier = multiplier,
                DamageBaseStat = stat,
            };
        }

        private static SkillDefinition Physical(float multiplier = MULT_1_5)
        {
            return Skill(SkillEffectType.PhysicalDamage, DamageStat.AttackPower, multiplier);
        }

        // Runs the step directly with a validated cast.
        private void Execute(SkillDamageExecutionStep step, in SkillDefinition def, EntityID caster, EntityID target)
        {
            var request = new SkillCastRequest(caster.RawValue, def.ID, target.RawValue, 0u);
            var cast = new ValidatedSkillCast(caster, target, in def, 0, TICK, in request);
            step.Execute(in cast);
        }

        private void Run(in SkillDefinition def)
        {
            Execute(_step, in def, PLAYER_A, MOB);
        }

        private void BuildPipeline(ISkillExecutionStep execution, SkillDefinition[] defs, uint[] classIds)
        {
            if (_database != null)
            {
                Object.DestroyImmediate(_database);
            }

            var registry = new ClassRegistry();
            registry.RegisterClass(0, new ClassDefinition(0, 0, 0, 0, 0, classIds));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);
            var slots = new FakeSlotIndex();
            _service = new SkillSystemService(registry, _database, new FakeLevels(), slots, new NullUnlockOutput(), MAX_PLAYERS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _service.OnEntitySpawned(PLAYER_B, ClassType.Warrior);
            _intake = new SkillCastIntake(slots, MAX_PLAYERS);
            var executor = new SkillCastExecutor(_mana, new FakeConfig(), _service, _output, execution);
            _castStep = new SkillCastStep(_intake, _service, _database, _alive, _silence, _targets, _positions, _output, executor);
        }

        private void BuildPipeline(ISkillExecutionStep execution, params SkillDefinition[] defs)
        {
            var ids = new uint[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                ids[i] = defs[i].ID;
            }

            BuildPipeline(execution, defs, ids);
        }

        private void Submit(uint skill, uint target, uint tick = TICK)
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, skill, target, 0u));
            _castStep.Run(tick, DELTA);
        }

        private uint Cooldown(int slot)
        {
            Assert.That(_service.TryGetSkill(PLAYER_A, slot, out SkillInstance skill), Is.True);
            return skill.CooldownExpiryTick;
        }

        // Real Auto-Attack Combat with a pool-allocated player; the skill step resets/reports through it.
        private EntityID BuildRealCombat()
        {
            var zone = new ZoneID(ZONE_RAW);
            _pool = new ZoneSlotPool(zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, 4);
            _combat = new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), new UnitAttackSpeed(), new FakeZoneState(), new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), new FakeDamageApplier(), new FakeXpAward(),
                new FakeExperienceSink(), new FakeMobTypeSource(), zone, new FakeFaceOutput());
            _combat.OnMobDied += OnMobDiedRecord;
            Assert.IsTrue(_pool.TryAllocatePlayer(zone, new CharacterID(11u), new AccountID(1011u), 11u, out _, out EntityID id));
            _positions.Caster = id;
            return id;
        }

        private void OnMobDiedRecord(MobDeathContext context)
        {
            _lastDeath = context;
            _diedCount++;
        }

        // ---- AC-SK-31 ----

        [Test]
        public void test_execution_ten_casts_notify_ten_times_each_before_its_calculate()
        {
            SkillDefinition def = Physical();

            for (int i = 0; i < CAST_COUNT; i++)
            {
                Run(in def);
            }

            Assert.That(_skillUse.Calls, Is.EqualTo(CAST_COUNT));
        }

        [Test]
        public void test_execution_each_cast_logs_notify_before_its_own_calculate()
        {
            SkillDefinition def = Physical();

            for (int i = 0; i < CAST_COUNT; i++)
            {
                _calls.Clear();
                Run(in def);

                int notify = _calls.IndexOf(CallKind.Notify);
                int calculate = _calls.IndexOf(CallKind.Calculate);
                Assert.That(notify, Is.GreaterThanOrEqualTo(0), "cast " + i);
                Assert.That(calculate, Is.GreaterThan(notify), "cast " + i);
            }
        }

        // ---- AC-SK-30 ----

        [TestCase(0)]
        [TestCase(TIMER_WARM_TICKS)]
        public void test_execution_real_combat_timer_is_zero_after_cast(int warmTicks)
        {
            AssertTimerResetAfterCast(warmTicks);
        }

        [Test]
        public void test_execution_real_combat_timer_one_tick_before_cycle_is_zero_after_cast()
        {
            EntityID p = BuildRealCombat();
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, new EntityID(MOB_RAW));
            int cycle = _combat.GetCycleTicks(p);
            Assert.That(cycle, Is.GreaterThan(1), "precondition: a sampled cycle");

            AssertTimerResetAfterCast(cycle - 1);
        }

        private void AssertTimerResetAfterCast(int warmTicks)
        {
            EntityID p = BuildRealCombat();
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, new EntityID(MOB_RAW));
            for (int i = 1; i <= warmTicks; i++)
            {
                _combat.Run((uint)i, DELTA);
            }

            Assert.That(_combat.GetCycleTicks(p), Is.GreaterThan(warmTicks), "precondition: no swing fired yet");
            Assert.That(_combat.GetActionTimer(p), Is.EqualTo(warmTicks), "precondition: one increment per Run");
            SkillDamageExecutionStep step = NewStep(_combat, _reporter);
            SkillDefinition def = Physical();

            Execute(step, in def, p, new EntityID(MOB_RAW));

            Assert.That(_combat.GetActionTimer(p), Is.EqualTo(0));
        }

        // ---- AC-SK-03 / AC-SK-32: every rejection code V-0..V-9 ----

        [TestCase("V0", SkillCastRejectionCode.CasterNotAlive)]
        [TestCase("V3", SkillCastRejectionCode.InvalidSkill)]
        [TestCase("V4", SkillCastRejectionCode.InvalidSkill)]
        [TestCase("V5", SkillCastRejectionCode.SkillLocked)]
        [TestCase("V5b", SkillCastRejectionCode.Silenced)]
        [TestCase("V6", SkillCastRejectionCode.OnCooldown)]
        [TestCase("V7", SkillCastRejectionCode.InvalidTarget)]
        [TestCase("V8", SkillCastRejectionCode.OutOfRange)]
        [TestCase("V9", SkillCastRejectionCode.InsufficientMana)]
        public void test_execution_rejected_cast_never_notifies_or_faces(string check, SkillCastRejectionCode expected)
        {
            SkillDefinition main = Physical();
            SkillDefinition other = Skill(SkillEffectType.PhysicalDamage, DamageStat.AttackPower, MULT_1_5, id: SKILL_OTHER);
            uint skill = SKILL_MAIN;
            switch (check)
            {
                case "V0":
                    _alive.Value = false;
                    break;
                case "V3":
                    skill = 0u;
                    break;
                case "V4":
                    skill = SKILL_OTHER;
                    break;
                case "V5":
                    main.UnlockLevel = 5;
                    break;
                case "V5b":
                    _silence.Value = true;
                    break;
                case "V7":
                    _targets.TargetAlive = false;
                    break;
                case "V8":
                    _positions.TargetX = FAR_X;
                    break;
                case "V9":
                    _mana.Result = false;
                    break;
            }

            BuildPipeline(_step, new[] { main, other }, new[] { SKILL_MAIN });
            if (check == "V6")
            {
                _service.SetCooldownForTesting(PLAYER_A, 0, TICK + COOLDOWN_MAIN);
            }

            Submit(skill, ID_MOB);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { expected }));
            Assert.That(_skillUse.Calls, Is.EqualTo(0));
            Assert.That(_face.Calls, Is.EqualTo(0));
            Assert.That(_calc.Calls, Is.EqualTo(0));
        }

        // ---- AC-SK-20 / AC-SK-21 ----

        [Test]
        public void test_execution_physical_skill_uses_physical_skill_context()
        {
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calc.LastContext, Is.EqualTo(DamageContext.PhysicalSkill));
        }

        [Test]
        public void test_execution_magical_skill_uses_magical_skill_context()
        {
            SkillDefinition def = Skill(SkillEffectType.MagicalDamage, DamageStat.Intelligence, MULT_1_5);

            Run(in def);

            Assert.That(_calc.LastContext, Is.EqualTo(DamageContext.MagicalSkill));
        }

        // ---- AC-SK-22..25, AC-SK-37 ----

        [Test]
        public void test_execution_physical_base_damage_is_floor_of_attack_power_times_multiplier()
        {
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calc.LastBase, Is.EqualTo(975));
            Assert.That(_calc.LastAttacker, Is.EqualTo(PLAYER_A));
            Assert.That(_calc.LastTarget, Is.EqualTo(MOB));
        }

        [Test]
        public void test_execution_physical_base_damage_floors_976_point_5_to_976()
        {
            _stats.Ap = AP_FLOOR;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calc.LastBase, Is.EqualTo(976));
        }

        [Test]
        public void test_execution_magical_base_damage_uses_intelligence_and_never_reads_attack_power()
        {
            SkillDefinition def = Skill(SkillEffectType.MagicalDamage, DamageStat.Intelligence, MULT_1_5);

            Run(in def);

            Assert.That(_calc.LastBase, Is.EqualTo(369));
            Assert.That(_stats.IntReads, Is.EqualTo(1));
            Assert.That(_stats.ApReads, Is.EqualTo(0));
            Assert.That(_stats.StrReads, Is.EqualTo(0));
        }

        [Test]
        public void test_execution_strength_override_reads_only_strength()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage, DamageStat.Strength, MULT_1_2);

            Run(in def);

            Assert.That(_calc.LastBase, Is.EqualTo(120));
            Assert.That(_stats.StrReads, Is.EqualTo(1));
            Assert.That(_stats.ApReads, Is.EqualTo(0));
            Assert.That(_stats.IntReads, Is.EqualTo(0));
        }

        // ---- CR-SK-6 ----

        [Test]
        public void test_execution_kill_runs_notify_face_calculate_award_add_apply_report_in_order()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calls, Is.EqualTo(new[]
            {
                CallKind.Notify, CallKind.Face, CallKind.Calculate, CallKind.Award, CallKind.Add, CallKind.Apply, CallKind.Report,
            }));
            Assert.That(_xp.LastTarget, Is.EqualTo(MOB));
            Assert.That(_exp.LastAttacker, Is.EqualTo(PLAYER_A));
            Assert.That(_exp.LastAmount, Is.EqualTo(XP));
            Assert.That(_applier.LastAttacker, Is.EqualTo(PLAYER_A));
            Assert.That(_applier.LastTarget, Is.EqualTo(MOB));
            Assert.That(_applier.LastDamage, Is.EqualTo(HIT_DAMAGE));
            Assert.That(_reporter.LastKiller, Is.EqualTo(PLAYER_A));
            Assert.That(_reporter.LastTarget, Is.EqualTo(MOB));
        }

        [Test]
        public void test_execution_no_kill_applies_only_without_award_add_or_report()
        {
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calls, Is.EqualTo(new[] { CallKind.Notify, CallKind.Face, CallKind.Calculate, CallKind.Apply }));
            Assert.That(_reporter.Calls, Is.EqualTo(0));
        }

        // ---- Decision B ----

        [Test]
        public void test_execution_skill_hit_goes_through_inner_applier_only_and_nothing_else()
        {
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_applier.Calls, Is.EqualTo(1));
            Assert.That(_calls.Contains(CallKind.Accepted), Is.False, "the step emits no output of its own");
            Assert.That(_calls.Count, Is.EqualTo(4), "notify, face, calculate, apply: no damage event path exists in the step");
        }

        // ---- Decision C ----

        [Test]
        public void test_execution_hostile_cast_on_mob_faces_target_once()
        {
            _positions.CasterX = CASTER_X;
            _positions.CasterZ = CASTER_Z;
            _positions.TargetX = FACE_TARGET_X;
            _positions.TargetZ = FACE_TARGET_Z;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_face.Calls, Is.EqualTo(1));
            Assert.That(_face.LastAttacker, Is.EqualTo(PLAYER_A));
            var expected = new Vector3(FACE_TARGET_X - CASTER_X, 0f, FACE_TARGET_Z - CASTER_Z);
            Assert.That(Vector3.Distance(_face.LastDirection, expected), Is.LessThan(POS_TOLERANCE));
        }

        [Test]
        public void test_execution_friendly_cast_on_other_player_faces_target_once()
        {
            _positions.CasterX = CASTER_X;
            _positions.CasterZ = CASTER_Z;
            _positions.TargetX = FACE_TARGET_X;
            _positions.TargetZ = FACE_TARGET_Z;
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, DamageStat.AttackPower, 0f, TargetConstraint.SingleFriendly);

            Execute(_step, in def, PLAYER_A, PLAYER_B);

            Assert.That(_face.Calls, Is.EqualTo(1));
            Assert.That(_calls, Is.EqualTo(new[] { CallKind.Notify, CallKind.Face }));
            var expected = new Vector3(FACE_TARGET_X - CASTER_X, 0f, FACE_TARGET_Z - CASTER_Z);
            Assert.That(Vector3.Distance(_face.LastDirection, expected), Is.LessThan(POS_TOLERANCE));
        }

        [Test]
        public void test_execution_unknown_caster_position_skips_facing_but_still_runs_damage()
        {
            _positions.CasterKnown = false;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_face.Calls, Is.EqualTo(0));
            Assert.That(_calls, Is.EqualTo(new[] { CallKind.Notify, CallKind.Calculate, CallKind.Apply }));
        }

        [Test]
        public void test_execution_self_cast_does_not_face()
        {
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, DamageStat.AttackPower, 0f, TargetConstraint.Self);

            Execute(_step, in def, PLAYER_A, PLAYER_A);

            Assert.That(_face.Calls, Is.EqualTo(0));
            Assert.That(_skillUse.Calls, Is.EqualTo(1));
            Assert.That(_calc.Calls, Is.EqualTo(0), "a self heal never reaches the damage calculator");
        }

        [Test]
        public void test_execution_friendly_cast_on_caster_own_entity_does_not_face()
        {
            SkillDefinition def = Skill(SkillEffectType.InstantHeal, DamageStat.AttackPower, 0f, TargetConstraint.SingleFriendly);

            Execute(_step, in def, PLAYER_A, PLAYER_A);

            Assert.That(_face.Calls, Is.EqualTo(0));
        }

        [Test]
        public void test_execution_missing_position_skips_facing_but_still_runs_damage()
        {
            _positions.TargetKnown = false;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_face.Calls, Is.EqualTo(0));
            Assert.That(_calls, Is.EqualTo(new[] { CallKind.Notify, CallKind.Calculate, CallKind.Apply }));
        }

        // ---- non-damage ----

        [TestCase(SkillEffectType.InstantHeal)]
        [TestCase(SkillEffectType.ApplyBuff)]
        [TestCase(SkillEffectType.ApplyDebuff)]
        [TestCase(SkillEffectType.None)]
        public void test_execution_non_damage_skill_notifies_but_never_calculates(SkillEffectType effect)
        {
            SkillDefinition def = Skill(effect, DamageStat.AttackPower, MULT_1_5);

            Run(in def);

            Assert.That(_skillUse.Calls, Is.EqualTo(1));
            Assert.That(_calc.Calls, Is.EqualTo(0));
            Assert.That(_applier.Calls, Is.EqualTo(0));
            Assert.That(_stats.ApReads + _stats.IntReads + _stats.StrReads, Is.EqualTo(0));
        }

        // ---- Decision D ----

        [Test]
        public void test_execution_real_combat_skill_kill_raises_one_mob_died_with_caster_as_killer_and_pre_apply_position()
        {
            EntityID p = BuildRealCombat();
            _applier.MoveOnApply = _positions;
            _calc.Result = Dmg(HIT_DAMAGE, true);
            SkillDamageExecutionStep step = NewStep(_combat, _combat);
            SkillDefinition def = Physical();

            Execute(step, in def, p, new EntityID(MOB_RAW));

            Assert.That(_diedCount, Is.EqualTo(1));
            Assert.That(_lastDeath.KillerEntityId, Is.EqualTo(p));
            Assert.That(_lastDeath.MobEntityId, Is.EqualTo(new EntityID(MOB_RAW)));
            Assert.That(_lastDeath.MobTypeId, Is.EqualTo(new MobTypeID(FakeMobTypeSource.MOB_TYPE_RAW)));
            Assert.That(Vector3.Distance(_lastDeath.DeathPosition, new Vector3(TARGET_X, 0f, TARGET_Z)), Is.LessThan(POS_TOLERANCE));
            Assert.That(_positions.TargetX, Is.EqualTo(MOVED_X), "the applier moved the mob after the position was read");
        }

        [Test]
        public void test_execution_real_combat_no_kill_raises_no_mob_died()
        {
            EntityID p = BuildRealCombat();
            SkillDamageExecutionStep step = NewStep(_combat, _combat);
            SkillDefinition def = Physical();

            Execute(step, in def, p, new EntityID(MOB_RAW));

            Assert.That(_diedCount, Is.EqualTo(0));
        }

        [Test]
        public void test_execution_real_combat_unknown_mob_type_logs_error_and_raises_nothing()
        {
            var zone = new ZoneID(ZONE_RAW);
            var unknownTypes = new FakeMobTypeSource { Known = false };
            _pool = new ZoneSlotPool(zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, 4);
            var combat = new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), new UnitAttackSpeed(), new FakeZoneState(), new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), new FakeDamageApplier(), new FakeXpAward(),
                new FakeExperienceSink(), unknownTypes, zone, new FakeFaceOutput());
            combat.OnMobDied += OnMobDiedRecord;
            Assert.IsTrue(_pool.TryAllocatePlayer(zone, new CharacterID(11u), new AccountID(1011u), 11u, out _, out EntityID p));
            LogAssert.Expect(LogType.Error, new Regex("OnMobDied skipped"));

            combat.ReportKill(p, new EntityID(MOB_RAW), Vector3.zero);

            Assert.That(_diedCount, Is.EqualTo(0));
        }

        [Test]
        public void test_execution_kill_reports_pre_apply_xz_position_with_y_zero()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            _applier.MoveOnApply = _positions;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_reporter.Calls, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_reporter.LastPosition, new Vector3(TARGET_X, 0f, TARGET_Z)), Is.LessThan(POS_TOLERANCE));
        }

        [Test]
        public void test_execution_kill_with_unknown_target_position_reports_vector_zero()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            _positions.TargetKnown = false;
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_reporter.Calls, Is.EqualTo(1));
            Assert.That(_reporter.LastPosition, Is.EqualTo(Vector3.zero));
        }

        // ---- Decision E ----

        [Test]
        public void test_execution_throwing_step_leaves_mana_spent_and_writes_no_cooldown_or_accepted()
        {
            BuildPipeline(new ThrowingStep(), Physical());
            LogAssert.Expect(LogType.Error, new Regex("execution failed"));
            LogAssert.Expect(LogType.Exception, new Regex("step-fault"));

            Submit(SKILL_MAIN, ID_MOB);

            Assert.That(_mana.Calls, Is.EqualTo(new[] { MANA_MAIN }));
            Assert.That(Cooldown(0), Is.EqualTo(0u));
            Assert.That(_output.AcceptedCount, Is.EqualTo(0));
            Assert.That(_output.Rejections, Is.Empty);
        }

        [Test]
        public void test_execution_throw_after_add_experience_keeps_xp_and_skips_report_cooldown_and_accepted()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            var step = new SkillDamageExecutionStep(
                _skillUse, _stats, _positions, _calc, _xp, _exp, new ThrowingApplier(), _face, _reporter);
            BuildPipeline(step, Physical());
            LogAssert.Expect(LogType.Error, new Regex("execution failed"));
            LogAssert.Expect(LogType.Exception, new Regex("apply-fault"));

            Submit(SKILL_MAIN, ID_MOB);

            Assert.That(_exp.LastAmount, Is.EqualTo(XP), "XP stays awarded: no rollback (Decision E)");
            Assert.That(_reporter.Calls, Is.EqualTo(0));
            Assert.That(Cooldown(0), Is.EqualTo(0u));
            Assert.That(_output.AcceptedCount, Is.EqualTo(0));
            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_mana.Calls, Is.EqualTo(new[] { SkillCastExecutor.ComputeEffectiveManaCost(MANA_MAIN, 1.0f) }));
        }

        // ---- zero-damage results ----

        [Test]
        public void test_execution_miss_calls_calculate_but_never_applies_and_mana_stays_spent()
        {
            _calc.Result = Dmg(0, false);
            BuildPipeline(_step, Physical());

            Submit(SKILL_MAIN, ID_MOB);

            Assert.That(_calc.Calls, Is.EqualTo(1));
            Assert.That(_applier.Calls, Is.EqualTo(0));
            Assert.That(_mana.Calls, Is.EqualTo(new[] { MANA_MAIN }));
        }

        [Test]
        public void test_execution_kill_flag_with_zero_final_damage_awards_nothing_applies_nothing_reports_nothing()
        {
            _calc.Result = Dmg(0, true);
            SkillDefinition def = Physical();

            Run(in def);

            Assert.That(_calc.Calls, Is.EqualTo(1));
            Assert.That(_applier.Calls, Is.EqualTo(0));
            Assert.That(_exp.LastAmount, Is.EqualTo(-1));
            Assert.That(_reporter.Calls, Is.EqualTo(0));
            Assert.That(_calls, Is.EqualTo(new[] { CallKind.Notify, CallKind.Face, CallKind.Calculate }));
        }

        // ---- F-SK-1 boundaries ----

        [TestCase(100, float.NaN, 0)]
        [TestCase(0, float.PositiveInfinity, 0)]
        [TestCase(100, float.PositiveInfinity, 0)]
        [TestCase(100, -1f, 0)]
        [TestCase(100, 0f, 0)]
        [TestCase(int.MaxValue, 5f, int.MaxValue)]
        [TestCase(AP_FLOOR, MULT_1_5, 976)]
        public void test_execution_compute_base_damage_boundaries(int stat, float multiplier, int expected)
        {
            Assert.That(SkillDamageExecutionStep.ComputeBaseDamage(stat, multiplier), Is.EqualTo(expected));
        }

        [Test]
        public void test_execution_zero_multiplier_cast_calls_calculate_with_base_zero()
        {
            SkillDefinition def = Physical(0f);

            Run(in def);

            Assert.That(_calc.Calls, Is.EqualTo(1));
            Assert.That(_calc.LastBase, Is.EqualTo(0));
            Assert.That(_applier.Calls, Is.EqualTo(1), "the FinalDamage guard applies to the result, not the base");
        }

        // ---- context and stat edge cases ----

        [Test]
        public void test_execution_strength_with_magical_damage_is_magical_context_and_reads_only_strength()
        {
            SkillDefinition def = Skill(SkillEffectType.MagicalDamage, DamageStat.Strength, MULT_1_2);

            Run(in def);

            Assert.That(_calc.LastContext, Is.EqualTo(DamageContext.MagicalSkill));
            Assert.That(_stats.StrReads, Is.EqualTo(1));
            Assert.That(_stats.IntReads + _stats.ApReads, Is.EqualTo(0));
        }

        [Test]
        public void test_execution_physical_and_magical_flags_take_the_physical_path()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.MagicalDamage, DamageStat.AttackPower, MULT_1_5);

            Run(in def);

            Assert.That(_calc.LastContext, Is.EqualTo(DamageContext.PhysicalSkill));
        }

        [Test]
        public void test_execution_physical_with_apply_debuff_still_calculates()
        {
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage | SkillEffectType.ApplyDebuff, DamageStat.AttackPower, MULT_1_5);

            Run(in def);

            Assert.That(_calc.Calls, Is.EqualTo(1));
            Assert.That(_calc.LastContext, Is.EqualTo(DamageContext.PhysicalSkill));
        }

        [Test]
        public void test_execution_unmapped_damage_stat_logs_error_and_reads_attack_power()
        {
            LogAssert.Expect(LogType.Error, new Regex("unmapped DamageStat"));
            SkillDefinition def = Skill(SkillEffectType.PhysicalDamage, (DamageStat)UNMAPPED_STAT, MULT_1_5);

            Run(in def);

            Assert.That(_stats.ApReads, Is.EqualTo(1));
            Assert.That(_calc.LastBase, Is.EqualTo(975));
        }

        // ---- V-1 / V-2 ----
        // V-1 / V-2 run upstream of this pipeline, in SkillCastRequestHandler (Story 004; covered by its tests), so
        // SkillCastStep validates V-0 and V-3..V-9 only. Here: a caster without a player slot is dropped by the intake
        // with a warning, and an empty tick produces nothing. ServerError (7) is unreachable in these tests.

        [Test]
        public void test_execution_unknown_caster_is_dropped_without_rejection_notify_or_calculate()
        {
            BuildPipeline(_step, Physical());
            LogAssert.Expect(LogType.Warning, new Regex("no player slot"));
            _intake.Submit(new EntityID(UNKNOWN_CASTER), new SkillCastRequest(UNKNOWN_CASTER, SKILL_MAIN, ID_MOB, 0u));

            _castStep.Run(TICK, DELTA);

            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_skillUse.Calls, Is.EqualTo(0));
            Assert.That(_calc.Calls, Is.EqualTo(0));
            Assert.That(_mana.Calls, Is.Empty);
        }

        [Test]
        public void test_execution_tick_with_no_submitted_cast_produces_no_result()
        {
            BuildPipeline(_step, Physical());

            _castStep.Run(TICK, DELTA);

            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_output.AcceptedCount, Is.EqualTo(0));
            Assert.That(_skillUse.Calls, Is.EqualTo(0));
            Assert.That(_calc.Calls, Is.EqualTo(0));
        }

        // ---- AC-SK-16 ----

        [Test]
        public void test_execution_miss_refunds_no_mana_and_still_completes_the_cast()
        {
            _calc.Result = Dmg(0, false);
            BuildPipeline(_step, Physical());

            Submit(SKILL_MAIN, ID_MOB);

            Assert.That(_mana.Calls, Is.EqualTo(new[] { MANA_MAIN }), "one deduction, no refund call exists");
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(Cooldown(0), Is.EqualTo(TICK + COOLDOWN_MAIN));
        }

        // ---- pipeline integration ----

        [Test]
        public void test_execution_pipeline_kill_cast_orders_mana_then_execution_then_accepted()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            BuildPipeline(_step, Physical());

            Submit(SKILL_MAIN, ID_MOB);

            Assert.That(_calls, Is.EqualTo(new[]
            {
                CallKind.Mana, CallKind.Notify, CallKind.Face, CallKind.Calculate, CallKind.Award, CallKind.Add,
                CallKind.Apply, CallKind.Report, CallKind.Accepted,
            }));
        }

        // ---- constructor and adapters ----

        [Test]
        public void test_execution_step_null_arguments_throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(null, _stats, _positions, _calc, _xp, _exp, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, null, _positions, _calc, _xp, _exp, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, null, _calc, _xp, _exp, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, null, _xp, _exp, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, _calc, null, _exp, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, _calc, _xp, null, _applier, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, _calc, _xp, _exp, null, _face, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, _calc, _xp, _exp, _applier, null, _reporter));
            Assert.Throws<System.ArgumentNullException>(() => new SkillDamageExecutionStep(_skillUse, _stats, _positions, _calc, _xp, _exp, _applier, _face, null));
        }

        [Test]
        public void test_execution_stat_source_adapter_reads_effective_stat()
        {
            var stats = CharacterStatsFixture.Create();
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.AttackPower, STAT_AP);
            stats.SetBaseStat(entity, StatID.Intelligence, STAT_INT);
            stats.SetBaseStat(entity, StatID.Strength, STAT_STR);
            var source = new CharacterStatsSkillStatSource(stats);

            int ap = source.GetStat(entity, StatID.AttackPower);
            int intelligence = source.GetStat(entity, StatID.Intelligence);
            int strength = source.GetStat(entity, StatID.Strength);

            Assert.That(ap, Is.EqualTo(stats.GetEffectiveStat(entity, StatID.AttackPower)));
            Assert.That(intelligence, Is.EqualTo(stats.GetEffectiveStat(entity, StatID.Intelligence)));
            Assert.That(strength, Is.EqualTo(stats.GetEffectiveStat(entity, StatID.Strength)));
            Assert.That(ap, Is.Not.EqualTo(intelligence));
            Assert.That(intelligence, Is.Not.EqualTo(strength));
            Assert.That(ap, Is.Not.EqualTo(strength));
        }

        [Test]
        public void test_execution_damage_calculator_adapter_matches_direct_calculate()
        {
            var stats = CharacterStatsFixture.CreateWithLeveling(new IronGrind.Tests.EditMode.DamageCalculation.AllPlayersLevelingService());
            EntityID attacker = CharacterStatsFixture.PlayerEntityId;
            EntityID target = CharacterStatsFixture.MobEntityId;
            stats.SetBaseStat(target, StatID.MaxHP, ADAPTER_MAX_HP);
            stats.SetCurrentHP(target, ADAPTER_MAX_HP);
            stats.SetBaseStatFloat(attacker, StatID.CritChance, ADAPTER_CRIT_CHANCE);
            stats.SetBaseStatFloat(attacker, StatID.CritMultiplier, ADAPTER_CRIT_MULTIPLIER);
            var random = new IronGrind.Tests.EditMode.Randomness.ScriptedRandomProvider();
            var calculator = new DamageCalculator(
                stats,
                new IronGrind.Tests.EditMode.DamageCalculation.FakeEquippedWeaponQuery(),
                new IronGrind.Tests.EditMode.DamageCalculation.CountingItemDatabase(),
                new IronGrind.EnhancementSystem.EnhancementBonusProvider(IronGrind.EnhancementSystem.EnhancementConfig.Default),
                DamageCalculationConfig.Default,
                random);
            var adapter = new DamageCalculatorSkillAdapter(calculator);

            random.EnqueueFloat(ROLL_CRIT);
            DamageResult viaAdapter = adapter.Calculate(ADAPTER_BASE, attacker, target, DamageContext.PhysicalSkill);
            random.EnqueueFloat(ROLL_CRIT);
            DamageResult direct = calculator.Calculate(ADAPTER_BASE, attacker, target, DamageContext.PhysicalSkill);

            Assert.That(viaAdapter.FinalDamage, Is.GreaterThan(0));
            Assert.That(viaAdapter.FinalDamage, Is.EqualTo(direct.FinalDamage));
            Assert.That(viaAdapter.PhysicalDamage, Is.EqualTo(direct.PhysicalDamage));
            Assert.That(viaAdapter.ElementalDamage, Is.EqualTo(direct.ElementalDamage));
            Assert.That(viaAdapter.IsCrit, Is.EqualTo(direct.IsCrit));
            Assert.That(viaAdapter.IsKill, Is.EqualTo(direct.IsKill));
            Assert.That(viaAdapter.DamageContext, Is.EqualTo(DamageContext.PhysicalSkill));
        }

        [Test]
        public void test_execution_adapters_null_arguments_throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CharacterStatsSkillStatSource(null));
            Assert.Throws<System.ArgumentNullException>(() => new DamageCalculatorSkillAdapter(null));
        }

        // ---- no allocation ----

        [Test]
        public void test_execution_kill_cast_does_not_allocate()
        {
            _calc.Result = Dmg(HIT_DAMAGE, true);
            SkillDefinition def = Physical();
            var request = new SkillCastRequest(ID_A, SKILL_MAIN, ID_MOB, 0u);
            var cast = new ValidatedSkillCast(PLAYER_A, MOB, in def, 0, TICK, in request);
            _step.Execute(in cast);
            _calls.Clear();

            Assert.That(() => _step.Execute(in cast), Is.Not.AllocatingGCMemory());
        }
    }
}
