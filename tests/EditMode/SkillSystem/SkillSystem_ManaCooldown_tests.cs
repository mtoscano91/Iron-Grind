using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using System.Text.RegularExpressions;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 006 - V-9 mana cost and cooldown registration (CR-SK-2, CR-SK-8,
    /// CR-SK-11, CR-SK-13, F-SK-4, EC-SK-2, AC-SK-05..08, AC-SK-15, AC-SK-17..19; TR-sk-007, TR-sk-008). The tests drive
    /// <see cref="SkillCastStep"/> and <see cref="SkillCastExecutor"/> together so V-6 is real.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_ManaCooldown_Tests
    {
        private const int MAX_PLAYERS = 4;
        private const uint TICK = 100;
        private const uint ID_A = 1001;
        private const uint ID_B = 1002;
        private const uint ID_UNKNOWN = 9999;
        private const uint ID_MOB = 2001;
        private const uint SKILL_MAIN = 101;
        private const uint SKILL_OTHER = 102;
        private const uint SKILL_FREE = 103;
        private const uint SKILL_NO_COOLDOWN = 104;
        private const int SLOT_MAIN = 0;
        private const int SLOT_OTHER = 1;
        private const int MANA_MAIN = 100;
        private const int MANA_OTHER = 10;
        private const int COOLDOWN_MAIN = 20;
        private const int COOLDOWN_OTHER = 40;
        private const float RANGE = 10f;
        private const float MOB_DISTANCE = 5f;
        private const float DELTA = 0.05f;
        private const float MULTIPLIER_NEUTRAL = 1.0f;
        private const float MULTIPLIER_HIGH = 1.5f;
        private const uint ALLOCATION_WARMUP_TICK = 100;
        private const uint ALLOCATION_MEASURED_TICK = 1000;
        private const float MP_MAX = 100f;
        private const int MP_SPENT = 30;
        private const int MP_TOO_MUCH = 101;
        private const string LOG_MANA_PREFIX = "mana:";
        private const string LOG_EXECUTE = "execute";
        private const string LOG_ACCEPTED = "accepted";

        private static readonly EntityID PLAYER_A = new EntityID(ID_A);
        private static readonly EntityID PLAYER_B = new EntityID(ID_B);

        private SkillDatabase _database;
        private SkillSystemService _service;
        private SkillCastIntake _intake;
        private SkillCastStep _step;
        private SkillCastExecutor _executor;
        private FakeMana _mana;
        private FakeConfig _config;
        private RecordingOutput _output;
        private RecordingExecution _execution;
        private FakeAutoAttack _autoAttack;
        private FakeSlotIndex _slots;
        private readonly List<string> _log = new List<string>();

        // ---- fakes ----

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
            public bool IsAlive(EntityID entity) => true;
        }

        private sealed class FakeTargets : ISkillTargetSource
        {
            public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
            {
                kind = target.RawValue == ID_MOB ? SkillTargetKind.Mob : SkillTargetKind.Player;
                isAlive = true;
                return true;
            }
        }

        private sealed class FakePositions : ISkillPositionSource
        {
            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                x = entity.RawValue == ID_MOB ? MOB_DISTANCE : 0f;
                z = 0f;
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
                SkillSystemConfigValues.Create(MULTIPLIER_NEUTRAL, SkillSystemConfigValues.CAST_STALE_TICK_TOLERANCE_DEFAULT);

            public void SetMultiplier(float multiplier)
            {
                Values = SkillSystemConfigValues.Create(multiplier, SkillSystemConfigValues.CAST_STALE_TICK_TOLERANCE_DEFAULT);
            }
        }

        private sealed class FakeMana : ISkillManaSink
        {
            public readonly List<int> Calls = new List<int>();
            public List<string> Log;
            public bool Result = true;
            public bool Record = true;
            public int CallCount;

            public bool ConsumeMana(EntityID entity, int cost)
            {
                CallCount++;
                if (Record)
                {
                    Calls.Add(cost);
                    Log?.Add(LOG_MANA_PREFIX + cost);
                }

                return Result;
            }
        }

        private sealed class RecordingExecution : ISkillExecutionStep
        {
            public SkillSystemService Service;
            public List<string> Log;
            public IAutoAttackSkillUseSink AutoAttack;
            public bool Throw;
            public int Calls;
            public uint CooldownSeenDuringExecution = uint.MaxValue;

            public void Execute(in ValidatedSkillCast cast)
            {
                Calls++;
                Log?.Add(LOG_EXECUTE);
                if (Throw && cast.Caster.RawValue == ID_A)
                {
                    throw new System.InvalidOperationException("execution fault");
                }

                AutoAttack?.NotifySkillUsed(cast.Caster);
                Service.TryGetSkill(cast.Caster, cast.SkillSlot, out SkillInstance skill);
                CooldownSeenDuringExecution = skill.CooldownExpiryTick;
            }
        }

        private sealed class RecordingOutput : ISkillCastOutput
        {
            public SkillSystemService Service;
            public List<string> Log;
            public readonly List<SkillCastRejectionCode> Rejections = new List<SkillCastRejectionCode>();
            public readonly List<int> AcceptedCooldownTicks = new List<int>();
            public readonly List<EntityID> RejectedCasters = new List<EntityID>();
            public readonly List<uint> RejectedSkillIds = new List<uint>();
            public readonly List<EntityID> AcceptedCasters = new List<EntityID>();
            public readonly List<uint> AcceptedSkillIds = new List<uint>();
            public uint CooldownSeenAtAccepted = uint.MaxValue;
            public int AcceptedCount;

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
            {
                Rejections.Add(code);
                RejectedCasters.Add(caster);
                RejectedSkillIds.Add(request.SkillId);
            }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks)
            {
                AcceptedCount++;
                AcceptedCasters.Add(caster);
                AcceptedSkillIds.Add(request.SkillId);
                AcceptedCooldownTicks.Add(newCooldownTicks);
                Log?.Add(LOG_ACCEPTED);
                Service.TryFindSlot(caster, new SkillID(request.SkillId), out int slot);
                Service.TryGetSkill(caster, slot, out SkillInstance skill);
                CooldownSeenAtAccepted = skill.CooldownExpiryTick;
            }
        }

        private sealed class CountingOutput : ISkillCastOutput
        {
            public int Rejections;
            public int Accepted;

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code) { Rejections++; }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks) { Accepted++; }
        }

        private sealed class FakeAutoAttack : IAutoAttackSkillUseSink
        {
            public int Notified;

            public void NotifySkillUsed(EntityID casterEntityID) { Notified++; }
        }

        // ---- fixture ----

        [SetUp]
        public void SetUp()
        {
            _log.Clear();
            _slots = new FakeSlotIndex();
            _config = new FakeConfig();
            _mana = new FakeMana { Log = _log };
            _autoAttack = new FakeAutoAttack();
            Build(
                Def(SKILL_MAIN, MANA_MAIN, COOLDOWN_MAIN),
                Def(SKILL_OTHER, MANA_OTHER, COOLDOWN_OTHER),
                Def(SKILL_FREE, 0, COOLDOWN_MAIN),
                Def(SKILL_NO_COOLDOWN, 0, 0));
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

        private static SkillDefinition Def(uint id, int manaCost, int cooldownTicks)
        {
            return new SkillDefinition
            {
                ID = id,
                UnlockLevel = 1,
                RangeUnits = RANGE,
                TargetConstraint = TargetConstraint.SingleHostile,
                ManaCost = manaCost,
                CooldownTicks = cooldownTicks,
            };
        }

        // (Re)builds the whole pipeline around the given definitions, keeping the shared fakes.
        private void Build(params SkillDefinition[] defs)
        {
            if (_database != null)
            {
                Object.DestroyImmediate(_database);
            }

            var ids = new uint[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                ids[i] = defs[i].ID;
            }

            var registry = new ClassRegistry();
            registry.RegisterClass(0, new ClassDefinition(0, 0, 0, 0, 0, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);

            _service = new SkillSystemService(registry, _database, new FakeLevels(), _slots, new NullUnlockOutput(), MAX_PLAYERS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _service.OnEntitySpawned(PLAYER_B, ClassType.Warrior);
            _intake = new SkillCastIntake(_slots, MAX_PLAYERS);

            _output = new RecordingOutput { Service = _service, Log = _log };
            _execution = new RecordingExecution { Service = _service, Log = _log, AutoAttack = _autoAttack };
            _executor = new SkillCastExecutor(_mana, _config, _service, _output, _execution);
            _step = new SkillCastStep(_intake, _service, _database, new FakeAlive(), new NoSilenceSource(),
                new FakeTargets(), new FakePositions(), _output, _executor);
        }

        private void Cast(uint skill, uint tick)
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, skill, ID_MOB, 0u));
            _step.Run(tick, DELTA);
        }

        private uint Cooldown(int slot)
        {
            Assert.That(_service.TryGetSkill(PLAYER_A, slot, out SkillInstance skill), Is.True);
            return skill.CooldownExpiryTick;
        }

        // ---- F-SK-4 / AC-SK-17 / AC-SK-18 ----

        [TestCase(100, 0.75f, 75)]
        [TestCase(101, 1.5f, 152)]
        [TestCase(50, 0.5f, 25)]
        [TestCase(200, 2.0f, 400)]
        [TestCase(1, 0.5f, 1)]
        [TestCase(100, 1.1f, 110)]
        [TestCase(100, 1.2f, 120)]
        [TestCase(30, 0.7f, 21)]
        public void test_manacooldown_effective_cost_is_ceiling_of_cost_times_multiplier(int manaCost, float multiplier, int expected)
        {
            Build(Def(SKILL_MAIN, manaCost, COOLDOWN_MAIN));
            _config.SetMultiplier(multiplier);

            Cast(SKILL_MAIN, TICK);

            Assert.That(_mana.Calls, Is.EqualTo(new[] { expected }));
            Assert.That(SkillCastExecutor.ComputeEffectiveManaCost(manaCost, multiplier), Is.EqualTo(expected));
        }

        [Test]
        public void test_manacooldown_config_swapped_between_casts_changes_second_cost_on_same_service()
        {
            _config.SetMultiplier(MULTIPLIER_NEUTRAL);
            Cast(SKILL_MAIN, TICK);
            _config.SetMultiplier(MULTIPLIER_HIGH);
            Cast(SKILL_MAIN, TICK + COOLDOWN_MAIN);

            Assert.That(_mana.Calls, Is.EqualTo(new[] { MANA_MAIN, 150 }));
        }

        [Test]
        public void test_manacooldown_effective_cost_saturates_at_int_max()
        {
            Assert.That(SkillCastExecutor.ComputeEffectiveManaCost(int.MaxValue, 2.0f), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void test_manacooldown_negative_authored_cost_is_zero_and_never_consumes()
        {
            Build(Def(SKILL_MAIN, -5, COOLDOWN_MAIN));
            _mana.Result = false;

            Cast(SKILL_MAIN, TICK);

            Assert.That(SkillCastExecutor.ComputeEffectiveManaCost(-5, MULTIPLIER_HIGH), Is.EqualTo(0));
            Assert.That(_mana.CallCount, Is.EqualTo(0));
            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
        }

        // ---- V-9 / EC-SK-2 ----

        [Test]
        public void test_manacooldown_insufficient_mana_rejects_and_changes_nothing()
        {
            _mana.Result = false;

            Cast(SKILL_MAIN, TICK);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { SkillCastRejectionCode.InsufficientMana }));
            Assert.That(_output.AcceptedCount, Is.EqualTo(0), "no SendAccepted");
            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(0u), "no cooldown registered");
            Assert.That(_execution.Calls, Is.EqualTo(0), "no execution");
            Assert.That(_autoAttack.Notified, Is.EqualTo(0), "the fake execution notifies on Execute, which never ran");
            Assert.That(_output.RejectedCasters, Is.EqualTo(new[] { PLAYER_A }));
            Assert.That(_output.RejectedSkillIds, Is.EqualTo(new[] { SKILL_MAIN }));
        }

        [Test]
        public void test_manacooldown_accepted_cast_carries_caster_and_request_and_notifies_once()
        {
            Cast(SKILL_MAIN, TICK);

            Assert.That(_output.AcceptedCasters, Is.EqualTo(new[] { PLAYER_A }));
            Assert.That(_output.AcceptedSkillIds, Is.EqualTo(new[] { SKILL_MAIN }));
            Assert.That(_autoAttack.Notified, Is.EqualTo(1));
        }

        [Test]
        public void test_manacooldown_mana_failure_after_cooldown_rejects_with_cooldown_unchanged()
        {
            Cast(SKILL_MAIN, TICK);
            _mana.Result = false;

            Cast(SKILL_MAIN, TICK + COOLDOWN_MAIN);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { SkillCastRejectionCode.InsufficientMana }));
            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(TICK + COOLDOWN_MAIN), "cooldown unchanged by the failed cast");
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
        }

        [Test]
        public void test_manacooldown_on_cooldown_is_checked_before_mana()
        {
            Cast(SKILL_MAIN, TICK);
            _mana.Result = false;

            Cast(SKILL_MAIN, TICK + 1);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { SkillCastRejectionCode.OnCooldown }));
            Assert.That(_mana.CallCount, Is.EqualTo(1), "V-6 rejects before V-9 touches mana");
        }

        [Test]
        public void test_manacooldown_insufficient_mana_skill_castable_next_tick_once_mana_available()
        {
            _mana.Result = false;
            Cast(SKILL_MAIN, TICK);
            _mana.Result = true;

            Cast(SKILL_MAIN, TICK + 1);

            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(TICK + 1 + COOLDOWN_MAIN));
        }

        [Test]
        public void test_manacooldown_zero_mana_skill_skips_consume_and_is_accepted()
        {
            _mana.Result = false;

            Cast(SKILL_FREE, TICK);

            Assert.That(_mana.CallCount, Is.EqualTo(0));
            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
        }

        // ---- AC-SK-15 ----

        [Test]
        public void test_manacooldown_accepted_cast_orders_mana_then_execution_then_cooldown_then_accepted()
        {
            Cast(SKILL_MAIN, TICK);

            Assert.That(_log, Is.EqualTo(new[] { LOG_MANA_PREFIX + MANA_MAIN, LOG_EXECUTE, LOG_ACCEPTED }));
            Assert.That(_execution.CooldownSeenDuringExecution, Is.EqualTo(0u), "cooldown is written after execution");
            Assert.That(_output.CooldownSeenAtAccepted, Is.EqualTo(TICK + COOLDOWN_MAIN), "cooldown is written before SendAccepted");
        }

        // ---- CR-SK-8 ----

        [Test]
        public void test_manacooldown_cast_at_tick_1000_with_cooldown_40_expires_at_1040()
        {
            const uint castTick = 1000;

            Cast(SKILL_OTHER, castTick);

            Assert.That(Cooldown(SLOT_OTHER), Is.EqualTo(castTick + COOLDOWN_OTHER));
            Assert.That(Cooldown(SLOT_OTHER), Is.EqualTo(1040u));
        }

        [Test]
        public void test_manacooldown_accepted_cast_reports_cooldown_ticks()
        {
            Cast(SKILL_MAIN, TICK);

            Assert.That(_output.AcceptedCooldownTicks, Is.EqualTo(new[] { COOLDOWN_MAIN }));
        }

        // ---- AC-SK-06 / AC-SK-07 ----

        [Test]
        public void test_manacooldown_recast_is_on_cooldown_until_exactly_expiry_tick()
        {
            Cast(SKILL_MAIN, TICK);
            Cast(SKILL_MAIN, TICK + 1);
            Cast(SKILL_MAIN, TICK + COOLDOWN_MAIN - 1);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { SkillCastRejectionCode.OnCooldown, SkillCastRejectionCode.OnCooldown }));
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));

            Cast(SKILL_MAIN, TICK + COOLDOWN_MAIN);

            Assert.That(_output.AcceptedCount, Is.EqualTo(2), "a cast at exactly N + 20 passes V-6");
        }

        // ---- AC-SK-08 ----

        [Test]
        public void test_manacooldown_cooldowns_are_independent_per_skill()
        {
            Cast(SKILL_MAIN, TICK);

            Assert.That(Cooldown(SLOT_OTHER), Is.EqualTo(0u));

            Cast(SKILL_OTHER, TICK);

            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(TICK + COOLDOWN_MAIN), "casting B leaves A unchanged");
            Assert.That(Cooldown(SLOT_OTHER), Is.EqualTo(TICK + COOLDOWN_OTHER));
        }

        // ---- AC-SK-05 ----

        [Test]
        public void test_manacooldown_v6_rejection_changes_no_mana_cooldown_or_auto_attack_timer()
        {
            uint expiry = TICK + COOLDOWN_MAIN;
            _service.SetCooldownForTesting(PLAYER_A, SLOT_MAIN, expiry);

            Cast(SKILL_MAIN, TICK);

            Assert.That(_output.Rejections, Is.EqualTo(new[] { SkillCastRejectionCode.OnCooldown }));
            Assert.That(_mana.CallCount, Is.EqualTo(0), "mana untouched");
            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(expiry));
            Assert.That(Cooldown(SLOT_OTHER), Is.EqualTo(0u));
            Assert.That(_autoAttack.Notified, Is.EqualTo(0), "the fake execution notifies on Execute, which never ran");
        }

        // ---- zero cooldown ----

        [Test]
        public void test_manacooldown_zero_cooldown_skill_is_castable_on_consecutive_ticks()
        {
            Cast(SKILL_NO_COOLDOWN, TICK);
            Cast(SKILL_NO_COOLDOWN, TICK + 1);

            Assert.That(_output.Rejections, Is.Empty);
            Assert.That(_output.AcceptedCount, Is.EqualTo(2));
            Assert.That(Cooldown(3), Is.EqualTo(TICK + 1), "the expiry written is the second cast's tick");
        }

        [Test]
        public void test_manacooldown_negative_cooldown_fails_safe_to_zero()
        {
            Build(Def(SKILL_MAIN, MANA_MAIN, -5));

            Cast(SKILL_MAIN, TICK);

            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(TICK));
            Assert.That(_output.AcceptedCooldownTicks, Is.EqualTo(new[] { 0 }));
        }

        // ---- executor wiring ----

        [Test]
        public void test_manacooldown_null_execution_still_writes_cooldown_and_sends_accepted()
        {
            var executor = new SkillCastExecutor(_mana, _config, _service, _output);
            var step = new SkillCastStep(_intake, _service, _database, new FakeAlive(), new NoSilenceSource(),
                new FakeTargets(), new FakePositions(), _output, executor);

            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_MAIN, ID_MOB, 0u));
            step.Run(TICK, DELTA);

            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(TICK + COOLDOWN_MAIN));
            Assert.That(_output.AcceptedCount, Is.EqualTo(1));
        }

        [Test]
        public void test_manacooldown_executor_null_required_arguments_throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SkillCastExecutor(null, _config, _service, _output));
            Assert.Throws<System.ArgumentNullException>(() => new SkillCastExecutor(_mana, null, _service, _output));
            Assert.Throws<System.ArgumentNullException>(() => new SkillCastExecutor(_mana, _config, null, _output));
            Assert.Throws<System.ArgumentNullException>(() => new SkillCastExecutor(_mana, _config, _service, null));
        }

        [Test]
        public void test_manacooldown_throwing_execution_writes_no_cooldown_and_next_caster_is_processed()
        {
            _execution.Throw = true;
            // Decision E (Story 007): the executor catches and logs the exception itself.
            LogAssert.Expect(LogType.Error, new Regex("execution failed"));
            LogAssert.Expect(LogType.Exception, new Regex("execution fault"));
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_MAIN, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_MAIN, ID_MOB, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(0u), "caster A: no cooldown after the fault");
            Assert.That(_output.AcceptedCasters, Is.EqualTo(new[] { PLAYER_B }), "no SendAccepted for A, B still processed");
            Assert.That(_service.TryGetSkill(PLAYER_B, SLOT_MAIN, out SkillInstance skillB), Is.True);
            Assert.That(skillB.CooldownExpiryTick, Is.EqualTo(TICK + COOLDOWN_MAIN));
        }

        // ---- SetCooldownExpiry ----

        [Test]
        public void test_manacooldown_set_cooldown_unknown_entity_returns_false_and_writes_nothing()
        {
            bool written = _service.SetCooldownExpiry(new EntityID(ID_UNKNOWN), SLOT_MAIN, TICK);

            Assert.That(written, Is.False);
            Assert.That(Cooldown(SLOT_MAIN), Is.EqualTo(0u));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void test_manacooldown_set_cooldown_out_of_range_slot_returns_false_and_writes_nothing(int slot)
        {
            bool written = _service.SetCooldownExpiry(PLAYER_A, slot, TICK);

            Assert.That(written, Is.False);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(Cooldown(i), Is.EqualTo(0u));
            }
        }

        // ---- no allocation ----

        [Test]
        public void test_manacooldown_successful_cast_does_not_allocate()
        {
            var mana = new FakeMana { Record = false };
            var output = new CountingOutput();
            var execution = new RecordingExecution { Service = _service };
            var executor = new SkillCastExecutor(mana, _config, _service, output, execution);
            var step = new SkillCastStep(_intake, _service, _database, new FakeAlive(), new NoSilenceSource(),
                new FakeTargets(), new FakePositions(), output, executor);
            var request = new SkillCastRequest(ID_A, SKILL_MAIN, ID_MOB, 0u);

            _intake.Submit(PLAYER_A, request);
            step.Run(ALLOCATION_WARMUP_TICK, DELTA);
            _intake.Submit(PLAYER_A, request);

            Assert.That(() => step.Run(ALLOCATION_MEASURED_TICK, DELTA), Is.Not.AllocatingGCMemory());
            Assert.That(output.Accepted, Is.EqualTo(2));
        }

        // ---- CharacterStatsSkillManaSink ----

        [Test]
        public void test_manacooldown_adapter_deducts_mana_when_sufficient()
        {
            var stats = CharacterStatsFixture.Create();
            var entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.MaxMP, (int)MP_MAX);
            stats.SetCurrentMP(entity, MP_MAX);

            bool consumed = new CharacterStatsSkillManaSink(stats).ConsumeMana(entity, MP_SPENT);

            Assert.That(consumed, Is.True);
            Assert.That(stats.GetCurrentMP(entity), Is.EqualTo(MP_MAX - MP_SPENT));
        }

        [Test]
        public void test_manacooldown_adapter_deducts_when_mana_exactly_equals_cost()
        {
            var stats = CharacterStatsFixture.Create();
            var entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.MaxMP, (int)MP_MAX);
            stats.SetCurrentMP(entity, MP_SPENT);

            bool consumed = new CharacterStatsSkillManaSink(stats).ConsumeMana(entity, MP_SPENT);

            Assert.That(consumed, Is.True);
            Assert.That(stats.GetCurrentMP(entity), Is.EqualTo(0f));
        }

        [Test]
        public void test_manacooldown_adapter_refuses_without_writing_when_insufficient()
        {
            var stats = CharacterStatsFixture.Create();
            var entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.MaxMP, (int)MP_MAX);
            stats.SetCurrentMP(entity, MP_MAX);

            bool consumed = new CharacterStatsSkillManaSink(stats).ConsumeMana(entity, MP_TOO_MUCH);

            Assert.That(consumed, Is.False);
            Assert.That(stats.GetCurrentMP(entity), Is.EqualTo(MP_MAX));
        }

        [Test]
        public void test_manacooldown_adapter_null_stats_throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new CharacterStatsSkillManaSink(null));
        }
    }
}
