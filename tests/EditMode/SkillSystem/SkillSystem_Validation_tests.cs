using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 005 - validation sequence V-0, V-3 to V-8 (CR-SK-17, CR-SK-19,
    /// EC-SK-5/8/10, AC-SK-01/02/03/12/32/34/39/40/41; TR-sk-004). Mana is not read or written by the step (it has no
    /// mana dependency); the "unchanged" assertions cover every cooldown, the next stage and NotifySkillUsed-free flow.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_Validation_Tests
    {
        private const int MAX_PLAYERS = 50;
        private const uint TICK = 100;
        private const uint COOLDOWN_TICKS = 20;
        private const uint ID_A = 1001;
        private const uint ID_B = 1002;
        private const uint ID_MOB = 2001;
        private const uint ID_DEAD_MOB = 2002;
        private const uint ID_ABSENT = 9999;
        private const uint SKILL_HOSTILE = 101;
        private const uint SKILL_FRIENDLY = 102;
        private const uint SKILL_SELF = 103;
        private const uint SKILL_LOCKED = 104;
        private const uint SKILL_NO_RANGE = 105;
        private const uint SKILL_HEALER = 201;
        private const uint SKILL_BAD_RANGE_NAN = 106;
        private const uint SKILL_BAD_RANGE_NEGATIVE = 107;
        private const uint SKILL_UNKNOWN = 99;
        private const uint ID_C = 1003;
        private const byte CLASS_WARRIOR = 0;
        private const byte CLASS_HEALER = 1;
        private const float RANGE = 10f;
        private const float RANGE_SELF = 5f;
        private const float DELTA = 0.05f;
        private const float FAR = 500f;
        private const int LOCKED_UNLOCK_LEVEL = 30;
        private const int REJECTED_CASTS = 10;
        private const float RANGE_NEGATIVE = -1f;
        private const uint ID_CASTER_PASS = 3001;
        private const uint ID_CASTER_FAR = 3002;
        private const uint ID_CASTER_LOCKED = 3003;
        private const int ALLOCATION_CASTERS = 3;
        private const string UNKNOWN_WARNING_TEXT = "unknown skill id";
        private const string RANGE_WARNING_TEXT = "invalid RangeUnits";
        private const string POSITION_WARNING_TEXT = "position unavailable";
        private const string FAULT_ERROR_PATTERN = "threw InvalidOperationException";

        private static readonly EntityID PLAYER_A = new EntityID(ID_A);
        private static readonly EntityID PLAYER_B = new EntityID(ID_B);
        private static readonly EntityID MOB = new EntityID(ID_MOB);

        private SkillDatabase _database;
        private ClassRegistry _registry;
        private FakeSlotIndex _slots;
        private FakeLevels _levels;
        private FakeAlive _alive;
        private FakeSilence _silence;
        private FakeTargets _targets;
        private FakePositions _positions;
        private RecordingOutput _output;
        private RecordingSink _sink;
        private SkillSystemService _service;
        private SkillCastIntake _intake;
        private SkillCastStep _step;
        private readonly List<string> _warnings = new List<string>();

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(uint id, int slot) { _map[id] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
        }

        private sealed class FakeLevels : ISkillLevelSource
        {
            public int Level = 1;

            public int GetLevel(EntityID entity) => Level;
        }

        private sealed class FakeAlive : ISkillCasterAliveSource
        {
            public readonly HashSet<uint> Dead = new HashSet<uint>();

            public bool IsAlive(EntityID entity) => !Dead.Contains(entity.RawValue);
        }

        private sealed class FakeSilence : ISkillSilenceSource
        {
            public readonly HashSet<uint> Silenced = new HashSet<uint>();

            public bool IsSilenced(EntityID entity) => Silenced.Contains(entity.RawValue);
        }

        private sealed class FakeTargets : ISkillTargetSource
        {
            public readonly Dictionary<uint, (SkillTargetKind Kind, bool Alive)> Entries =
                new Dictionary<uint, (SkillTargetKind, bool)>();

            public int Queries;

            public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
            {
                Queries++;
                if (Entries.TryGetValue(target.RawValue, out var entry))
                {
                    kind = entry.Kind;
                    isAlive = entry.Alive;
                    return true;
                }

                kind = default;
                isAlive = false;
                return false;
            }
        }

        private sealed class FakePositions : ISkillPositionSource
        {
            public readonly Dictionary<uint, (float X, float Z)> Entries = new Dictionary<uint, (float, float)>();

            public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
            {
                if (Entries.TryGetValue(entity.RawValue, out var p))
                {
                    x = p.X;
                    z = p.Z;
                    return true;
                }

                x = 0f;
                z = 0f;
                return false;
            }
        }

        private sealed class RecordingOutput : ISkillCastOutput
        {
            public readonly List<(EntityID Caster, uint Skill, SkillCastRejectionCode Code)> Sent =
                new List<(EntityID, uint, SkillCastRejectionCode)>();

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code)
            {
                Sent.Add((caster, request.SkillId, code));
            }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks, in SkillExecutionOutcome outcome) { }
        }

        private sealed class CountingOutput : ISkillCastOutput
        {
            public int Count;

            public void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code) { Count++; }

            public void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks, in SkillExecutionOutcome outcome) { }
        }

        private sealed class RecordingSink : ISkillCastValidatedSink
        {
            public readonly List<ValidatedSkillCast> Accepted = new List<ValidatedSkillCast>();

            public readonly HashSet<uint> ThrowForCaster = new HashSet<uint>();

            public void Accept(in ValidatedSkillCast cast)
            {
                if (ThrowForCaster.Contains(cast.Caster.RawValue))
                {
                    throw new System.InvalidOperationException("sink fault");
                }

                Accepted.Add(cast);
            }
        }

        private sealed class CountingSink : ISkillCastValidatedSink
        {
            public int Count;

            public void Accept(in ValidatedSkillCast cast) { Count++; }
        }

        private sealed class FakeZoneState : IZoneEntityStateSource
        {
            public ZonePlayerStateView Player;
            public ZoneMobStateView Mob;

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = Player;
                return id.RawValue == ID_A;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = Mob;
                return id.RawValue == ID_MOB;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _warnings.Clear();
            Application.logMessageReceived += OnLog;

            _slots = new FakeSlotIndex();
            _slots.Map(ID_A, 0);
            _slots.Map(ID_B, 1);
            _levels = new FakeLevels();
            _alive = new FakeAlive();
            _silence = new FakeSilence();
            _targets = new FakeTargets();
            _positions = new FakePositions();
            _output = new RecordingOutput();
            _sink = new RecordingSink();

            _targets.Entries[ID_A] = (SkillTargetKind.Player, true);
            _targets.Entries[ID_B] = (SkillTargetKind.Player, true);
            _targets.Entries[ID_MOB] = (SkillTargetKind.Mob, true);
            _targets.Entries[ID_DEAD_MOB] = (SkillTargetKind.Mob, false);
            _positions.Entries[ID_A] = (0f, 0f);
            _positions.Entries[ID_B] = (3f, 0f);
            _positions.Entries[ID_MOB] = (5f, 0f);

            var defs = new[]
            {
                Def(SKILL_HOSTILE, TargetConstraint.SingleHostile, RANGE, 1),
                Def(SKILL_FRIENDLY, TargetConstraint.SingleFriendly, RANGE, 1),
                Def(SKILL_SELF, TargetConstraint.Self, RANGE_SELF, 1),
                Def(SKILL_LOCKED, TargetConstraint.SingleHostile, RANGE, LOCKED_UNLOCK_LEVEL),
                Def(SKILL_NO_RANGE, TargetConstraint.SingleHostile, 0f, 1),
                Def(SKILL_HEALER, TargetConstraint.SingleFriendly, RANGE, 1),
                Def(SKILL_BAD_RANGE_NAN, TargetConstraint.SingleHostile, float.NaN, 1),
                Def(SKILL_BAD_RANGE_NEGATIVE, TargetConstraint.SingleHostile, RANGE_NEGATIVE, 1),
            };
            _registry = new ClassRegistry();
            _registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0,
                new[] { SKILL_HOSTILE, SKILL_FRIENDLY, SKILL_SELF, SKILL_LOCKED, SKILL_NO_RANGE, SKILL_BAD_RANGE_NAN, SKILL_BAD_RANGE_NEGATIVE }));
            _registry.RegisterClass(CLASS_HEALER, new ClassDefinition(0, 0, 0, 0, 0, new[] { SKILL_HEALER }));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);

            _service = new SkillSystemService(_registry, _database, _levels, _slots, new NullUnlockOutput(), MAX_PLAYERS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _service.OnEntitySpawned(PLAYER_B, ClassType.Healer);
            _intake = new SkillCastIntake(_slots, MAX_PLAYERS);
            _step = new SkillCastStep(_intake, _service, _database, _alive, _silence, _targets, _positions, _output, _sink);
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (_database != null)
            {
                Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        private sealed class NullUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                _warnings.Add(condition);
            }
        }

        private int Warnings(string text)
        {
            int count = 0;
            for (int i = 0; i < _warnings.Count; i++)
            {
                if (_warnings[i].Contains(text))
                {
                    count++;
                }
            }

            return count;
        }

        private static SkillDefinition Def(uint id, TargetConstraint constraint, float range, int unlockLevel)
        {
            return new SkillDefinition
            {
                ID = id,
                UnlockLevel = unlockLevel,
                RangeUnits = range,
                TargetConstraint = constraint,
                CooldownTicks = (int)COOLDOWN_TICKS,
            };
        }

        private void Cast(EntityID caster, uint skill, uint target, uint tick = TICK)
        {
            _intake.Submit(caster, new SkillCastRequest(caster.RawValue, skill, target, 0u));
            _step.Run(tick, DELTA);
        }

        private uint[] Cooldowns(EntityID owner)
        {
            var result = new uint[SkillSystemService.MAX_SKILLS_PER_ENTITY];
            int count = _service.GetSkillCount(owner);
            for (int i = 0; i < count; i++)
            {
                _service.TryGetSkill(owner, i, out SkillInstance skill);
                result[i] = skill.CooldownExpiryTick;
            }

            return result;
        }

        private void AssertRejected(SkillCastRejectionCode code)
        {
            Assert.That(_output.Sent.Count, Is.EqualTo(1), "one rejection");
            Assert.That(_output.Sent[0].Code, Is.EqualTo(code));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(0), "next stage not reached");
        }

        // ---- one rejection per code: code, cooldowns unchanged, next stage (and so NotifySkillUsed) not reached ----

        [TestCase(SkillCastRejectionCode.CasterNotAlive)]
        [TestCase(SkillCastRejectionCode.InvalidSkill)]
        [TestCase(SkillCastRejectionCode.SkillLocked)]
        [TestCase(SkillCastRejectionCode.Silenced)]
        [TestCase(SkillCastRejectionCode.OnCooldown)]
        [TestCase(SkillCastRejectionCode.InvalidTarget)]
        [TestCase(SkillCastRejectionCode.OutOfRange)]
        [TestCase(SkillCastRejectionCode.ServerError)]
        public void test_validation_each_rejection_code_reports_it_and_changes_nothing(SkillCastRejectionCode code)
        {
            uint skill = SKILL_HOSTILE;
            uint target = ID_MOB;
            switch (code)
            {
                case SkillCastRejectionCode.CasterNotAlive: _alive.Dead.Add(ID_A); break;
                case SkillCastRejectionCode.InvalidSkill: skill = 0u; break;
                case SkillCastRejectionCode.SkillLocked: skill = SKILL_LOCKED; break;
                case SkillCastRejectionCode.Silenced: _silence.Silenced.Add(ID_A); break;
                case SkillCastRejectionCode.OnCooldown: _service.SetCooldownForTesting(PLAYER_A, 0, TICK + COOLDOWN_TICKS); break;
                case SkillCastRejectionCode.InvalidTarget: target = 0u; break;
                case SkillCastRejectionCode.OutOfRange: _positions.Entries[ID_MOB] = (FAR, 0f); break;
                case SkillCastRejectionCode.ServerError: _positions.Entries.Remove(ID_MOB); break;
            }

            _service.SetCooldownForTesting(PLAYER_A, 4, TICK - 10);   // an unrelated, already-expired cooldown
            uint[] before = Cooldowns(PLAYER_A);

            Cast(PLAYER_A, skill, target);

            AssertRejected(code);
            Assert.That(Cooldowns(PLAYER_A), Is.EqualTo(before), "cooldowns unchanged");
        }

        [Test]
        public void test_validation_rejection_is_reported_for_the_pending_caster()
        {
            Cast(PLAYER_A, SKILL_LOCKED, ID_MOB);

            Assert.That(_output.Sent[0].Caster, Is.EqualTo(PLAYER_A));
            Assert.That(_output.Sent[0].Skill, Is.EqualTo(SKILL_LOCKED));
        }

        // ---- order ----

        [Test]
        public void test_validation_v3_and_v7_failing_reports_invalid_skill_and_never_queries_the_target()
        {
            Cast(PLAYER_A, SKILL_UNKNOWN, 0u);

            AssertRejected(SkillCastRejectionCode.InvalidSkill);
            Assert.That(_targets.Queries, Is.EqualTo(0));
        }

        [Test]
        public void test_validation_v0_runs_before_v3()
        {
            _alive.Dead.Add(ID_A);

            Cast(PLAYER_A, 0u, 0u);

            AssertRejected(SkillCastRejectionCode.CasterNotAlive);
        }

        [Test]
        public void test_validation_v5_runs_before_v5b_and_v6()
        {
            _silence.Silenced.Add(ID_A);
            _service.TryFindSlot(PLAYER_A, new SkillID(SKILL_LOCKED), out int lockedSlot);
            _service.SetCooldownForTesting(PLAYER_A, lockedSlot, TICK + COOLDOWN_TICKS);

            Cast(PLAYER_A, SKILL_LOCKED, ID_MOB);

            AssertRejected(SkillCastRejectionCode.SkillLocked);
        }

        [Test]
        public void test_validation_v5b_runs_before_v6()
        {
            _silence.Silenced.Add(ID_A);
            _service.SetCooldownForTesting(PLAYER_A, 0, TICK + COOLDOWN_TICKS);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            AssertRejected(SkillCastRejectionCode.Silenced);
        }

        [Test]
        public void test_validation_v7_runs_before_v8()
        {
            _positions.Entries[ID_MOB] = (FAR, 0f);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_ABSENT);

            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        // ---- V-3 ----

        [Test]
        public void test_validation_v3_id_zero_is_invalid_skill_without_a_warning()
        {
            Cast(PLAYER_A, 0u, ID_MOB);

            AssertRejected(SkillCastRejectionCode.InvalidSkill);
            Assert.That(Warnings(UNKNOWN_WARNING_TEXT), Is.EqualTo(0));
        }

        [Test]
        public void test_validation_v3_unknown_id_is_invalid_skill_with_one_warning()
        {
            Cast(PLAYER_A, SKILL_UNKNOWN, ID_MOB);

            AssertRejected(SkillCastRejectionCode.InvalidSkill);
            Assert.That(Warnings(UNKNOWN_WARNING_TEXT), Is.EqualTo(1));
        }

        // ---- V-4 ----

        [Test]
        public void test_validation_v4_healer_casting_a_warrior_skill_is_invalid_skill()
        {
            Cast(PLAYER_B, SKILL_HOSTILE, ID_MOB);

            AssertRejected(SkillCastRejectionCode.InvalidSkill);
            Assert.That(_output.Sent[0].Caster, Is.EqualTo(PLAYER_B));
        }

        // ---- V-5 (AC-SK-12) ----

        [Test]
        public void test_validation_v5_locked_skill_stays_locked_and_registers_no_cooldown()
        {
            uint[] before = Cooldowns(PLAYER_A);

            Cast(PLAYER_A, SKILL_LOCKED, ID_MOB);

            AssertRejected(SkillCastRejectionCode.SkillLocked);
            Assert.That(Cooldowns(PLAYER_A), Is.EqualTo(before));
        }

        // ---- V-5b ----

        [Test]
        public void test_validation_v5b_production_stand_in_never_silences()
        {
            Assert.That(new NoSilenceSource().IsSilenced(PLAYER_A), Is.False);
            Assert.That(new NoSilenceSource().IsSilenced(EntityID.Invalid), Is.False);
        }

        // ---- V-6 (AC-SK-07 boundary) ----

        [Test]
        public void test_validation_v6_cast_one_tick_before_expiry_is_on_cooldown()
        {
            _service.SetCooldownForTesting(PLAYER_A, 0, TICK + COOLDOWN_TICKS);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB, TICK + COOLDOWN_TICKS - 1);

            AssertRejected(SkillCastRejectionCode.OnCooldown);
        }

        [Test]
        public void test_validation_v6_cast_at_expiry_tick_passes()
        {
            _service.SetCooldownForTesting(PLAYER_A, 0, TICK + COOLDOWN_TICKS);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB, TICK + COOLDOWN_TICKS);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
        }

        // ---- V-7 ----

        [Test]
        public void test_validation_v7_target_zero_is_invalid_target()
        {
            Cast(PLAYER_A, SKILL_HOSTILE, 0u);
            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        [Test]
        public void test_validation_v7_dead_target_is_invalid_target()
        {
            Cast(PLAYER_A, SKILL_HOSTILE, ID_DEAD_MOB);
            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        [Test]
        public void test_validation_v7_target_not_in_zone_is_invalid_target()
        {
            Cast(PLAYER_A, SKILL_HOSTILE, ID_ABSENT);
            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        [Test]
        public void test_validation_v7_hostile_skill_on_a_player_is_invalid_target_ac_sk_39()
        {
            Cast(PLAYER_A, SKILL_HOSTILE, ID_B);
            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        [Test]
        public void test_validation_v7_friendly_skill_on_a_mob_is_invalid_target_ac_sk_40()
        {
            Cast(PLAYER_A, SKILL_FRIENDLY, ID_MOB);
            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        [Test]
        public void test_validation_v7_friendly_skill_on_another_player_passes()
        {
            Cast(PLAYER_A, SKILL_FRIENDLY, ID_B);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted[0].Target, Is.EqualTo(PLAYER_B));
        }

        // ---- Self (CR-SK-19, AC-SK-41) ----

        [TestCase(0u)]
        [TestCase(ID_MOB)]
        public void test_validation_self_skill_ignores_the_submitted_target_and_targets_the_caster(uint submittedTarget)
        {
            Cast(PLAYER_A, SKILL_SELF, submittedTarget);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            Assert.That(_sink.Accepted[0].Target, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.Accepted[0].Caster, Is.EqualTo(PLAYER_A));
        }

        [Test]
        public void test_validation_self_skill_does_not_modify_the_stored_request()
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_SELF, ID_MOB, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_sink.Accepted[0].Target, Is.EqualTo(PLAYER_A));
            Assert.That(_output.Sent.Count, Is.EqualTo(0));
        }

        // ---- V-8 ----

        [TestCase(9.99f, false)]
        [TestCase(10f, false)]
        [TestCase(10.01f, true)]
        public void test_validation_v8_range_boundary(float distance, bool expectOutOfRange)
        {
            _positions.Entries[ID_MOB] = (distance, 0f);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            if (expectOutOfRange)
            {
                AssertRejected(SkillCastRejectionCode.OutOfRange);
            }
            else
            {
                Assert.That(_output.Sent.Count, Is.EqualTo(0));
                Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void test_validation_v8_distance_uses_both_x_and_z()
        {
            _positions.Entries[ID_MOB] = (6f, 8f);   // exactly 10

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
        }

        [Test]
        public void test_validation_v8_range_zero_skips_the_check_for_a_far_target()
        {
            _positions.Entries[ID_MOB] = (FAR, FAR);

            Cast(PLAYER_A, SKILL_NO_RANGE, ID_MOB);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
        }

        [Test]
        public void test_validation_v8_large_y_gap_within_xz_range_passes_through_the_zone_adapter()
        {
            var zone = new FakeZoneState
            {
                Player = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, 100, 100, 50, 50, 1, false, 0, 0, "a", 0u),
                Mob = new ZoneMobStateView(500, 20000, 0, 0, 0, 0, 0, 0, 100, 100, false, 0),   // x 5.0, y 200.0, z 0
            };
            var step = new SkillCastStep(_intake, _service, _database, _alive, _silence, _targets,
                new ZoneSkillPositionSource(zone), _output, _sink);
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));

            step.Run(TICK, DELTA);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
        }

        // ---- valid request / intake ----

        [Test]
        public void test_validation_valid_request_reaches_the_next_stage_with_its_context()
        {
            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            ValidatedSkillCast accepted = _sink.Accepted[0];
            Assert.That(accepted.Caster, Is.EqualTo(PLAYER_A));
            Assert.That(accepted.Target, Is.EqualTo(MOB));
            Assert.That(accepted.Definition.ID, Is.EqualTo(SKILL_HOSTILE));
            Assert.That(accepted.SkillSlot, Is.EqualTo(0));
            Assert.That(accepted.CurrentTick, Is.EqualTo(TICK));
        }

        [Test]
        public void test_validation_valid_request_does_not_change_any_cooldown()
        {
            uint[] before = Cooldowns(PLAYER_A);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            Assert.That(Cooldowns(PLAYER_A), Is.EqualTo(before));
        }

        [Test]
        public void test_validation_clear_all_runs_after_the_step()
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));

            _step.Run(TICK, DELTA);
            _step.Run(TICK + 1, DELTA);

            Assert.That(_intake.TryGetPending(0, out _), Is.False);
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1), "second tick found nothing to process");
        }

        [Test]
        public void test_validation_two_players_are_validated_in_slot_order()
        {
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HEALER, ID_A, 0u));
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(2));
            Assert.That(_sink.Accepted[0].Caster, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.Accepted[1].Caster, Is.EqualTo(PLAYER_B));
        }

        [Test]
        public void test_validation_constructor_rejects_a_null_collaborator()
        {
            Assert.Throws<System.ArgumentNullException>(() => new SkillCastStep(
                _intake, _service, _database, _alive, null, _targets, _positions, _output, _sink));
        }

        // ---- mixed slots ----

        [Test]
        public void test_validation_rejection_in_slot_zero_does_not_stop_a_pass_in_slot_one()
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_LOCKED, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HEALER, ID_A, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_output.Sent.Count, Is.EqualTo(1));
            Assert.That(_output.Sent[0].Caster, Is.EqualTo(PLAYER_A));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            Assert.That(_sink.Accepted[0].Caster, Is.EqualTo(PLAYER_B));
        }

        [Test]
        public void test_validation_pass_in_slot_zero_does_not_stop_a_rejection_in_slot_one()
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HOSTILE, ID_MOB, 0u));   // V-4: a healer

            _step.Run(TICK, DELTA);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            Assert.That(_sink.Accepted[0].Caster, Is.EqualTo(PLAYER_A));
            Assert.That(_output.Sent.Count, Is.EqualTo(1));
            Assert.That(_output.Sent[0].Caster, Is.EqualTo(PLAYER_B));
            Assert.That(_output.Sent[0].Code, Is.EqualTo(SkillCastRejectionCode.InvalidSkill));
        }

        // ---- per-request isolation (ADR-014) ----

        [Test]
        public void test_validation_throwing_request_in_slot_zero_still_processes_slot_one_and_clears_the_intake()
        {
            _sink.ThrowForCaster.Add(ID_A);
            LogAssert.Expect(LogType.Error, new Regex(FAULT_ERROR_PATTERN));
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HEALER, ID_A, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            Assert.That(_sink.Accepted[0].Caster, Is.EqualTo(PLAYER_B));
            Assert.That(_output.Sent.Count, Is.EqualTo(0), "a throwing request gets no rejection");
            Assert.That(_intake.TryGetPending(0, out _), Is.False, "ClearAll ran");
            Assert.That(_intake.TryGetPending(1, out _), Is.False, "ClearAll ran");
        }

        [Test]
        public void test_validation_two_throwing_requests_in_one_run_log_one_error()
        {
            _sink.ThrowForCaster.Add(ID_A);
            _sink.ThrowForCaster.Add(ID_B);
            LogAssert.Expect(LogType.Error, new Regex(FAULT_ERROR_PATTERN));   // a second error would fail the test
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HEALER, ID_A, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(0));
            Assert.That(_intake.TryGetPending(0, out _), Is.False);
            Assert.That(_intake.TryGetPending(1, out _), Is.False);
        }

        [Test]
        public void test_validation_fault_log_resets_each_run()
        {
            _sink.ThrowForCaster.Add(ID_A);
            LogAssert.Expect(LogType.Error, new Regex(FAULT_ERROR_PATTERN));
            LogAssert.Expect(LogType.Error, new Regex(FAULT_ERROR_PATTERN));

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);
            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB, TICK + 1);

            Assert.That(_sink.Accepted.Count, Is.EqualTo(0));
        }

        // ---- V-8 fail-closed paths ----

        [Test]
        public void test_validation_v8_missing_caster_position_is_server_error_with_one_warning()
        {
            _positions.Entries.Remove(ID_A);

            Cast(PLAYER_A, SKILL_HOSTILE, ID_MOB);

            AssertRejected(SkillCastRejectionCode.ServerError);
            Assert.That(Warnings(POSITION_WARNING_TEXT), Is.EqualTo(1));
        }

        [Test]
        public void test_validation_v8_missing_positions_for_two_casts_in_one_run_warn_once()
        {
            _positions.Entries.Remove(ID_MOB);
            _positions.Entries.Remove(ID_B);
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_HOSTILE, ID_MOB, 0u));   // target position missing
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_HEALER, ID_A, 0u));      // caster position missing

            _step.Run(TICK, DELTA);

            Assert.That(_output.Sent.Count, Is.EqualTo(2));
            Assert.That(_output.Sent[0].Code, Is.EqualTo(SkillCastRejectionCode.ServerError));
            Assert.That(_output.Sent[1].Code, Is.EqualTo(SkillCastRejectionCode.ServerError));
            Assert.That(Warnings(POSITION_WARNING_TEXT), Is.EqualTo(1));
        }

        [Test]
        public void test_validation_self_cast_with_no_position_data_passes()
        {
            _positions.Entries.Clear();

            Cast(PLAYER_A, SKILL_SELF, 0u);

            Assert.That(_output.Sent.Count, Is.EqualTo(0));
            Assert.That(_sink.Accepted.Count, Is.EqualTo(1));
            Assert.That(Warnings(POSITION_WARNING_TEXT), Is.EqualTo(0));
        }

        [TestCase(SKILL_BAD_RANGE_NAN)]
        [TestCase(SKILL_BAD_RANGE_NEGATIVE)]
        public void test_validation_v8_malformed_range_is_server_error_with_one_warning_and_no_cooldown_change(uint skill)
        {
            uint[] before = Cooldowns(PLAYER_A);

            Cast(PLAYER_A, skill, ID_MOB);

            AssertRejected(SkillCastRejectionCode.ServerError);
            Assert.That(Cooldowns(PLAYER_A), Is.EqualTo(before));
            Assert.That(Warnings(RANGE_WARNING_TEXT), Is.EqualTo(1));
            Assert.That(Warnings(RANGE_WARNING_TEXT + " on skill id " + skill), Is.EqualTo(1), "names the skill id");
        }

        [Test]
        public void test_validation_v8_malformed_range_warns_once_per_run_and_again_next_run()
        {
            var playerC = new EntityID(ID_C);
            _slots.Map(ID_C, 2);
            _service.OnEntitySpawned(playerC, ClassType.Warrior);
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_BAD_RANGE_NAN, ID_MOB, 0u));
            _intake.Submit(playerC, new SkillCastRequest(ID_C, SKILL_BAD_RANGE_NEGATIVE, ID_MOB, 0u));

            _step.Run(TICK, DELTA);
            Assert.That(_output.Sent.Count, Is.EqualTo(2));
            Assert.That(Warnings(RANGE_WARNING_TEXT), Is.EqualTo(1));

            Cast(PLAYER_A, SKILL_BAD_RANGE_NAN, ID_MOB, TICK + 1);
            Assert.That(Warnings(RANGE_WARNING_TEXT), Is.EqualTo(2));
        }

        // ---- Self vs the two alive sources (Story 011 note) ----

        [Test]
        public void test_validation_self_skill_with_caster_absent_from_target_source_is_invalid_target()
        {
            // V-0 reads the caster-alive source and V-7 reads the target source (different sources); in the
            // composition both are the same zone, so this only pins the current behaviour. Story 011 revisits it.
            _targets.Entries.Remove(ID_A);

            Cast(PLAYER_A, SKILL_SELF, 0u);

            AssertRejected(SkillCastRejectionCode.InvalidTarget);
        }

        // ---- unknown-id throttle (EC-SK-8) ----

        [Test]
        public void test_validation_unknown_id_twice_in_one_tick_warns_once()
        {
            _intake.Submit(PLAYER_A, new SkillCastRequest(ID_A, SKILL_UNKNOWN, ID_MOB, 0u));
            _intake.Submit(PLAYER_B, new SkillCastRequest(ID_B, SKILL_UNKNOWN, ID_MOB, 0u));

            _step.Run(TICK, DELTA);

            Assert.That(_output.Sent.Count, Is.EqualTo(2));
            Assert.That(Warnings(UNKNOWN_WARNING_TEXT), Is.EqualTo(1));
        }

        [Test]
        public void test_validation_unknown_id_again_next_tick_warns_again()
        {
            Cast(PLAYER_A, SKILL_UNKNOWN, ID_MOB);
            Cast(PLAYER_A, SKILL_UNKNOWN, ID_MOB, TICK + 1);

            Assert.That(Warnings(UNKNOWN_WARNING_TEXT), Is.EqualTo(2));
        }

        // ---- allocation ----

        [Test]
        public void test_validation_tick_with_ten_rejected_casts_does_not_allocate()
        {
            var slots = new FakeSlotIndex();
            for (int i = 0; i < REJECTED_CASTS; i++)
            {
                slots.Map(ID_A + 100u + (uint)i, i);
            }

            var service = new SkillSystemService(_registry, _database, _levels, slots, new NullUnlockOutput(), MAX_PLAYERS);
            var intake = new SkillCastIntake(slots, MAX_PLAYERS);
            var output = new CountingOutput();
            var step = new SkillCastStep(intake, service, _database, _alive, _silence, _targets, _positions, output, _sink);
            var casters = new EntityID[REJECTED_CASTS];
            for (int i = 0; i < REJECTED_CASTS; i++)
            {
                casters[i] = new EntityID(ID_A + 100u + (uint)i);
                service.OnEntitySpawned(casters[i], ClassType.Warrior);
            }

            void Tick()
            {
                for (int i = 0; i < REJECTED_CASTS; i++)
                {
                    intake.Submit(casters[i], new SkillCastRequest(casters[i].RawValue, SKILL_LOCKED, ID_MOB, 0u));
                }

                step.Run(TICK, DELTA);
            }

            Tick();   // warm up
            Assert.That(output.Count, Is.EqualTo(REJECTED_CASTS));

            Assert.That(Tick, Is.Not.AllocatingGCMemory());
            Assert.That(output.Count, Is.EqualTo(REJECTED_CASTS * 2));
        }

        [Test]
        public void test_validation_tick_with_pass_range_rejection_and_locked_rejection_does_not_allocate()
        {
            var slots = new FakeSlotIndex();
            slots.Map(ID_CASTER_PASS, 0);
            slots.Map(ID_CASTER_FAR, 1);
            slots.Map(ID_CASTER_LOCKED, 2);
            var service = new SkillSystemService(_registry, _database, _levels, slots, new NullUnlockOutput(), MAX_PLAYERS);
            var intake = new SkillCastIntake(slots, MAX_PLAYERS);
            var output = new CountingOutput();
            var sink = new CountingSink();
            var step = new SkillCastStep(intake, service, _database, _alive, _silence, _targets, _positions, output, sink);
            var casters = new EntityID[ALLOCATION_CASTERS];
            casters[0] = new EntityID(ID_CASTER_PASS);
            casters[1] = new EntityID(ID_CASTER_FAR);
            casters[2] = new EntityID(ID_CASTER_LOCKED);
            _positions.Entries[ID_CASTER_PASS] = (0f, 0f);
            _positions.Entries[ID_CASTER_FAR] = (FAR, 0f);
            _positions.Entries[ID_CASTER_LOCKED] = (0f, 0f);
            for (int i = 0; i < ALLOCATION_CASTERS; i++)
            {
                service.OnEntitySpawned(casters[i], ClassType.Warrior);
            }

            void Tick()
            {
                intake.Submit(casters[0], new SkillCastRequest(ID_CASTER_PASS, SKILL_HOSTILE, ID_MOB, 0u));
                intake.Submit(casters[1], new SkillCastRequest(ID_CASTER_FAR, SKILL_HOSTILE, ID_MOB, 0u));
                intake.Submit(casters[2], new SkillCastRequest(ID_CASTER_LOCKED, SKILL_LOCKED, ID_MOB, 0u));
                step.Run(TICK, DELTA);
            }

            Tick();   // warm up
            Assert.That(sink.Count, Is.EqualTo(1), "one pass");
            Assert.That(output.Count, Is.EqualTo(2), "V-8 and V-5 rejections");

            Assert.That(Tick, Is.Not.AllocatingGCMemory());
            Assert.That(sink.Count, Is.EqualTo(2));
            Assert.That(output.Count, Is.EqualTo(4));
        }
    }
}
