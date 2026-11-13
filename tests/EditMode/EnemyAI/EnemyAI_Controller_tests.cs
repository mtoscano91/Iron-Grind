using IronGrind.CharacterStats;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode unit tests for Enemy AI Story 003: MobController core (CR-AI-13, CR-AI-14, F-AI-3, AC-AI-16, CR-AI-7 facing).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_Controller_Tests
    {
        private sealed class FakeNav : INavigationProvider
        {
            public int Calls;
            public void SetDestination(EntityID agent, Vector3 target) { Calls++; }
            public void SetSpeed(EntityID agent, float speed) { Calls++; }
            public void Stop(EntityID agent) { Calls++; }
            public void Resume(EntityID agent) { Calls++; }
            public bool IsPathStale(EntityID agent) { Calls++; return false; }
            public Vector3 GetCurrentVelocity(EntityID agent) { Calls++; return Vector3.zero; }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public float HP = 100f;
            public float GetCurrentHP(EntityID entityId) => HP;
            public float GetCurrentMP(EntityID entityId) => 0f;
            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private FakeNav _nav;
        private FakeStats _stats;
        private MobController _mob;

        private static MobDefinition Definition()
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: 10,
                baseDefense: 5, attackRange: 2f, moveSpeed: 3.5f, aggroRange: 8f, leashRange: 20f,
                attackWindUpTicks: 12, attackRecoveryTicks: 6, attackCooldownTicks: 10, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
        }

        [SetUp]
        public void SetUp()
        {
            _nav = new FakeNav();
            _stats = new FakeStats();
            _mob = new MobController(new EntityID(7u), Definition(), Vector3.zero, _nav, _stats,
                new HitDetector(HitDetectionConfig.Default));
        }

        [Test]
        public void test_controller_construction_starts_dormant_with_clean_state()
        {
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.IsFalse(_mob.PendingReturn);
            Assert.IsFalse(_mob.HasAttackedOnce);
            Assert.IsNull(_mob.TargetEntityID);
            Assert.IsFalse(_mob.IsEnraged);
            Assert.AreEqual(-1L, _mob.DeadEnteredTick);
            Assert.AreEqual(0, _mob.DeadEntryCount);
            Assert.AreEqual(0, _mob.FacingAngle);
            Assert.AreEqual(-1L, _mob.LastAttackExecutionTick);
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void test_controller_tick_hp_at_or_below_zero_dormant_becomes_dead(float hp)
        {
            _stats.HP = hp;
            _mob.Tick(42u);
            Assert.AreEqual(MobState.Dead, _mob.State);
            Assert.AreEqual(0, _nav.Calls);
            Assert.AreEqual(42L, _mob.DeadEnteredTick);
            Assert.AreEqual(1, _mob.DeadEntryCount);
        }

        [Test]
        public void test_controller_tick_tiny_positive_hp_stays_dormant()
        {
            _stats.HP = 0.001f;
            _mob.Tick(1u);
            Assert.AreEqual(MobState.Dormant, _mob.State);
        }

        [Test]
        public void test_controller_tick_already_dead_does_not_reenter()
        {
            _stats.HP = 0f;
            _mob.Tick(5u);
            _mob.Tick(6u);
            Assert.AreEqual(1, _mob.DeadEntryCount);
            Assert.AreEqual(5L, _mob.DeadEnteredTick);
            Assert.AreEqual(0, _nav.Calls);
        }

        [Test]
        public void test_controller_tick_hp_restored_after_dead_stays_dead()
        {
            // Arrange
            _stats.HP = 0f;
            _mob.Tick(5u);
            _stats.HP = 50f;

            // Act
            _mob.Tick(6u);

            // Assert
            Assert.AreEqual(MobState.Dead, _mob.State);
            Assert.AreEqual(1, _mob.DeadEntryCount);
            Assert.AreEqual(5L, _mob.DeadEnteredTick);
            Assert.AreEqual(0, _nav.Calls);
        }

        [Test]
        public void test_controller_enter_state_after_dead_is_noop()
        {
            // Arrange
            _mob.ForceStateForTest(MobState.Dead, 3u);

            // Act
            _mob.ForceStateForTest(MobState.Dead, 4u);
            _mob.ForceStateForTest(MobState.Dormant, 5u);

            // Assert
            Assert.AreEqual(MobState.Dead, _mob.State);
            Assert.AreEqual(1, _mob.DeadEntryCount);
            Assert.AreEqual(3L, _mob.DeadEnteredTick);
        }

        [Test]
        public void test_controller_tick_hp_zero_while_winding_up_becomes_dead_without_navigation()
        {
            _mob.ForceStateForTest(MobState.WindingUp);
            Assert.AreEqual(MobState.WindingUp, _mob.State, "precondition");
            _stats.HP = 0f;
            _mob.Tick(9u);
            Assert.AreEqual(MobState.Dead, _mob.State);
            Assert.AreEqual(0, _nav.Calls);
        }

        [Test]
        public void test_controller_pending_return_cleared_only_on_dormant_entry()
        {
            _mob.PendingReturn = true;
            _mob.ForceStateForTest(MobState.Returning);
            Assert.IsTrue(_mob.PendingReturn);
            _mob.ForceStateForTest(MobState.Pursuing);
            Assert.IsTrue(_mob.PendingReturn);
            _mob.ForceStateForTest(MobState.WindingUp);
            Assert.IsTrue(_mob.PendingReturn);
            _mob.ForceStateForTest(MobState.Dormant);
            Assert.IsFalse(_mob.PendingReturn);
        }

        [TestCase((short)0, 0f, 0f, 1f)]
        [TestCase((short)900, 1f, 0f, 0f)]
        [TestCase((short)1800, 0f, 0f, -1f)]
        [TestCase((short)2700, -1f, 0f, 0f)]
        [TestCase((short)450, 0.7071f, 0f, 0.7071f)]
        public void test_controller_facing_to_forward_matches_convention(short facing, float x, float y, float z)
        {
            Vector3 v = MobController.FacingToForward(facing);
            Assert.AreEqual(x, v.x, 0.001f);
            Assert.AreEqual(y, v.y, 0.001f);
            Assert.AreEqual(z, v.z, 0.001f);
        }

        [Test]
        public void test_controller_facing_to_forward_3599_points_nearly_plus_z()
        {
            Assert.Greater(MobController.FacingToForward(3599).z, 0.9999f);
        }

        [TestCase(0f, 0f, 1f, 0)]
        [TestCase(1f, 0f, 0f, 900)]
        [TestCase(0f, 0f, -1f, 1800)]
        [TestCase(-1f, 0f, 0f, 2700)]
        [TestCase(0f, 50f, 1f, 0)]
        public void test_controller_bearing_to_facing_cardinals_and_y_ignored(float dx, float dy, float dz, int expected)
        {
            int f = MobController.BearingToFacing(Vector3.zero, new Vector3(dx, dy, dz), 1234);
            Assert.AreEqual(expected, f);
        }

        [Test]
        public void test_controller_bearing_to_facing_wraps_near_360_to_zero()
        {
            float rad = 359.97f * Mathf.Deg2Rad;
            int f = MobController.BearingToFacing(Vector3.zero, new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)), 1234);
            Assert.AreEqual(0, f);
        }

        [Test]
        public void test_controller_bearing_to_facing_small_negative_wraps_to_3599()
        {
            // Atan2 gives -0.1 deg -> raw -1 -> NormalizeFacing -> 3599.
            float rad = -0.1f * Mathf.Deg2Rad;
            int f = MobController.BearingToFacing(Vector3.zero, new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)), 1234);
            Assert.AreEqual(3599, f);
        }

        [Test]
        public void test_controller_bearing_to_facing_zero_length_keeps_previous()
        {
            Assert.AreEqual(1234, MobController.BearingToFacing(Vector3.one, new Vector3(1f, 9f, 1f), 1234));
        }

        [Test]
        public void test_controller_write_facing_out_of_range_clamps_and_logs_error()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("facing 3600"));
            _mob.WriteFacing(3600);
            Assert.AreEqual(3599, _mob.FacingAngle);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("facing -1"));
            _mob.WriteFacing(-1);
            Assert.AreEqual(0, _mob.FacingAngle);
        }

        [TestCase(0)]
        [TestCase(3599)]
        public void test_controller_write_facing_in_range_stores_without_error(int value)
        {
            _mob.WriteFacing(value);
            Assert.AreEqual(value, _mob.FacingAngle);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(MobState.Dormant)]
        [TestCase(MobState.Pursuing)]
        [TestCase(MobState.WindingUp)]
        [TestCase(MobState.AttackExecution)]
        [TestCase(MobState.Recovering)]
        [TestCase(MobState.Returning)]
        public void test_controller_tick_live_state_stub_keeps_state(MobState state)
        {
            // Arrange
            _mob.ForceStateForTest(state);
            _mob.PendingReturn = true;
            if (state == MobState.Dormant)
            {
                _mob.PendingReturn = false;
            }

            bool pendingBefore = _mob.PendingReturn;

            // Act
            _mob.Tick(3u);

            // Assert
            Assert.AreEqual(state, _mob.State);
            Assert.AreEqual(pendingBefore, _mob.PendingReturn);
            Assert.AreEqual(0, _nav.Calls);
        }
    }
}
