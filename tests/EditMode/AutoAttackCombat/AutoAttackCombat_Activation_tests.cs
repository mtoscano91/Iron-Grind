using System;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 001: <see cref="AutoAttackCombatSystem"/> activation, timer and
    /// deactivation (Rules 1-4, 23; AC-12 to AC-15; EC-1 to EC-4, EC-9, EC-27 to EC-30).
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_Activation_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint NEVER_ISSUED_ENTITY = 900000u;
        private const uint PLAYER_TARGET_RAW = 5003u;
        private const uint DEAD_MOB_RAW = 5004u;
        private const uint FOREIGN_MOB_RAW = 5005u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int TIMER_TICKS = 12;
        private const int FROZEN_TICKS = 100;
        private const int OFF_TICKS = 60;
        private const int OTHER_TICKS = 3;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        // Every id is valid unless added to Rejected.
        private sealed class FakeValidity : IAutoAttackTargetValidity
        {
            public readonly HashSet<EntityID> Rejected = new HashSet<EntityID>();

            public bool IsValidTarget(EntityID target)
            {
                return !Rejected.Contains(target);
            }
        }

        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float Multiplier = 1.0f;

            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                return Multiplier;
            }
        }

        private ZoneSlotPool _pool;
        private FakeValidity _validity;
        private FakeAttackSpeed _attackSpeed;
        private AutoAttackCombatSystem _combat;
        private uint _nextClient;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static EntityID MobA => new EntityID(AutoAttackTestFakes.MOB_A_RAW);

        private static EntityID MobB => new EntityID(AutoAttackTestFakes.MOB_B_RAW);

        [SetUp]
        public void SetUp()
        {
            _nextClient = 11u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _validity = new FakeValidity();
            _attackSpeed = new FakeAttackSpeed();
            _combat = NewCombat(_pool, _validity, _attackSpeed);
        }

        private static AutoAttackCombatSystem NewCombat(ZoneSlotPool pool, IAutoAttackTargetValidity validity, IAttackSpeedSource attackSpeed)
        {
            return new AutoAttackCombatSystem(
                pool, validity, attackSpeed, new FakeZoneState(), new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), new FakeDamageApplier());
        }

        private EntityID AddPlayer()
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        private void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _combat.Run((uint)i, 0.05f);
            }
        }

        // Toggle ON, target selected, then N Run calls: CombatActive with timer == n.
        private EntityID ActivePlayerAtTimer(int n)
        {
            EntityID p = AddPlayer();
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            Ticks(n);
            return p;
        }

        // Same, then toggle OFF: Idle with the timer frozen at n and the target kept.
        private EntityID IdlePlayerFrozenAt(int n)
        {
            EntityID p = ActivePlayerAtTimer(n);
            _combat.SetAutoAttack(p, false);
            return p;
        }

        [Test]
        public void test_ctor_null_pool_throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCombat(null, _validity, _attackSpeed));
        }

        [Test]
        public void test_ctor_null_validity_throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCombat(_pool, null, _attackSpeed));
        }

        [Test]
        public void test_ctor_null_attack_speed_throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCombat(_pool, _validity, null));
        }

        // AC-12, EC-1
        [Test]
        public void test_toggle_on_without_target_stays_idle()
        {
            EntityID p = AddPlayer();

            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // AC-14: with no target, toggle ON leaves the frozen timer; the next selection resets it (Rule 3).
        [Test]
        public void test_toggle_on_without_target_keeps_frozen_timer_until_selection()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);
            _combat.SetTarget(p, EntityID.Invalid);
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));

            _combat.SetAutoAttack(p, true);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));

            _combat.SetTarget(p, MobB);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // AC-12
        [Test]
        public void test_target_with_toggle_off_stays_idle()
        {
            EntityID p = AddPlayer();

            _combat.SetTarget(p, MobA);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p));
        }

        // AC-12
        [TestCase(true)]
        [TestCase(false)]
        public void test_target_and_toggle_in_either_order_activates(bool toggleFirst)
        {
            EntityID p = AddPlayer();

            if (toggleFirst)
            {
                _combat.SetAutoAttack(p, true);
                _combat.SetTarget(p, MobA);
            }
            else
            {
                _combat.SetTarget(p, MobA);
                _combat.SetAutoAttack(p, true);
            }

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // AC-13
        [Test]
        public void test_idle_with_frozen_timer_does_not_advance_over_100_ticks()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);

            Ticks(FROZEN_TICKS);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
        }

        // AC-14, Rule 2, EC-27 to EC-30
        [Test]
        public void test_toggle_off_freezes_timer_and_toggle_on_resets_it()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            _combat.SetAutoAttack(p, false);
            Ticks(OFF_TICKS);
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));

            _combat.SetAutoAttack(p, true);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        // AC-15, Rule 3
        [TestCase(true)]
        [TestCase(false)]
        public void test_new_target_selection_resets_timer_including_same_target(bool sameTarget)
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));

            _combat.SetTarget(p, sameTarget ? MobA : MobB);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        // Rules 4, 23, EC-2, EC-9: 0 = target loss, 1 = deselect, 2 = toggle OFF
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void test_deactivation_enters_idle_and_freezes_timer(int cause)
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            if (cause == 0)
            {
                _validity.Rejected.Add(MobA);
                Ticks(1);
            }
            else if (cause == 1)
            {
                _combat.SetTarget(p, EntityID.Invalid);
            }
            else
            {
                _combat.SetAutoAttack(p, false);
            }

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
        }

        // EC-3
        [Test]
        public void test_deselect_and_reselect_in_one_tick_is_fresh_activation()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            _combat.SetTarget(p, EntityID.Invalid);
            _combat.SetTarget(p, MobA);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // EC-4
        [Test]
        public void test_toggle_off_and_on_in_one_tick_is_fresh_activation()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            _combat.SetAutoAttack(p, false);
            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // Rule 1: a player, a dead mob and a mob in another zone are all rejected by the validity check.
        [TestCase(PLAYER_TARGET_RAW)]
        [TestCase(DEAD_MOB_RAW)]
        [TestCase(FOREIGN_MOB_RAW)]
        public void test_rejected_target_selection_leaves_idle(uint rejectedRaw)
        {
            EntityID p = AddPlayer();
            var rejected = new EntityID(rejectedRaw);
            _validity.Rejected.Add(rejected);
            _combat.SetAutoAttack(p, true);

            _combat.SetTarget(p, rejected);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
        }

        [Test]
        public void test_active_run_advances_timer_by_one_per_tick()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_target_invalid_at_timer_12_run_goes_idle_without_advance_and_clears_target()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            _validity.Rejected.Add(MobA);

            Ticks(1);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
        }

        [Test]
        public void test_toggle_on_while_on_changes_nothing()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);

            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        [Test]
        public void test_toggle_off_while_off_changes_nothing()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);

            _combat.SetAutoAttack(p, false);

            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p));
        }

        [Test]
        public void test_target_loss_keeps_toggle_so_next_valid_selection_activates()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            _validity.Rejected.Add(MobA);
            Ticks(1);

            _combat.SetTarget(p, MobB);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_deselect_keeps_toggle_so_next_valid_selection_activates()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            _combat.SetTarget(p, EntityID.Invalid);
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));

            _combat.SetTarget(p, MobB);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        [Test]
        public void test_toggle_off_keeps_target_and_toggle_on_reactivates_with_timer_zero()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);
            Assert.AreEqual(MobA, _combat.GetTarget(p));

            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_toggle_on_with_stored_target_no_longer_valid_clears_target_and_stays_idle()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);
            _validity.Rejected.Add(MobA);

            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));

            // The toggle stayed ON: a valid selection alone activates.
            _combat.SetTarget(p, MobB);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        // Rule 3
        [Test]
        public void test_selection_while_toggle_off_sets_timer_to_zero_and_stays_idle()
        {
            EntityID p = IdlePlayerFrozenAt(TIMER_TICKS);

            _combat.SetTarget(p, MobB);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(MobB, _combat.GetTarget(p));
        }

        [Test]
        public void test_rejected_selection_in_combat_goes_idle_freezes_timer_and_clears_target()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            _validity.Rejected.Add(MobB);

            _combat.SetTarget(p, MobB);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
        }

        [Test]
        public void test_release_then_new_occupant_starts_idle_timer_zero_toggle_off()
        {
            EntityID old = ActivePlayerAtTimer(TIMER_TICKS);
            Assert.IsTrue(_pool.TryGetPlayerSlot(old, out byte oldSlot));

            _combat.ReleasePlayer(old);
            Assert.IsTrue(_pool.ReleasePlayer(old));
            EntityID fresh = AddPlayer();
            Assert.IsTrue(_pool.TryGetPlayerSlot(fresh, out byte freshSlot));
            Assert.AreEqual(oldSlot, freshSlot, "the LIFO free-list hands the same slot back");

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(fresh));
            Assert.AreEqual(0, _combat.GetActionTimer(fresh));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(fresh));

            // Run leaves the released slot alone.
            Ticks(OTHER_TICKS);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(fresh));
            Assert.AreEqual(0, _combat.GetActionTimer(fresh));

            // Toggle must be OFF: a valid selection alone does not activate.
            _combat.SetTarget(fresh, MobA);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(fresh));
        }

        [Test]
        public void test_entity_without_slot_is_ignored_by_every_entry_point()
        {
            EntityID p = ActivePlayerAtTimer(TIMER_TICKS);
            var unknown = new EntityID(NEVER_ISSUED_ENTITY);

            Assert.DoesNotThrow(() =>
            {
                _combat.SetTarget(unknown, MobA);
                _combat.SetAutoAttack(unknown, true);
                _combat.ReleasePlayer(unknown);
                _combat.SetTarget(EntityID.Invalid, MobA);
                _combat.SetAutoAttack(EntityID.Invalid, true);
                _combat.ReleasePlayer(EntityID.Invalid);
            });

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(unknown));
            Assert.AreEqual(0, _combat.GetActionTimer(unknown));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(unknown));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_two_players_do_not_affect_each_other()
        {
            EntityID a = ActivePlayerAtTimer(TIMER_TICKS);
            EntityID b = AddPlayer();

            _combat.SetAutoAttack(b, true);
            _combat.SetTarget(b, MobB);
            Ticks(OTHER_TICKS);
            _combat.SetAutoAttack(a, false);
            _combat.ReleasePlayer(a);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(b));
            Assert.AreEqual(OTHER_TICKS, _combat.GetActionTimer(b));
            Assert.AreEqual(MobB, _combat.GetTarget(b));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(a));
            Assert.AreEqual(0, _combat.GetActionTimer(a));
        }

        [Test]
        public void test_run_advances_active_player_and_leaves_frozen_player_alone()
        {
            EntityID a = IdlePlayerFrozenAt(TIMER_TICKS);
            EntityID b = AddPlayer();
            _combat.SetAutoAttack(b, true);
            _combat.SetTarget(b, MobB);

            Ticks(OTHER_TICKS);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(a));
            Assert.AreEqual(TIMER_TICKS, _combat.GetActionTimer(a));
            Assert.AreEqual(MobA, _combat.GetTarget(a));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(b));
            Assert.AreEqual(OTHER_TICKS, _combat.GetActionTimer(b));
        }

        [Test]
        public void test_toggling_targeting_and_run_for_max_players_allocate_nothing()
        {
            var players = new EntityID[MAX_PLAYERS];
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                players[i] = AddPlayer();
            }

            // Size the fake's set up front so the Add of MobA inside the constraint cannot grow it.
            _validity.Rejected.Add(MobA);
            _validity.Rejected.Add(MobB);
            _validity.Rejected.Remove(MobA);

            Assert.That(() =>
            {
                for (int i = 0; i < players.Length; i++)
                {
                    _combat.SetAutoAttack(players[i], true);
                    _combat.SetTarget(players[i], MobA);
                }

                _combat.Run(1u, 0.05f);
                _combat.Run(2u, 0.05f);

                for (int i = 0; i < players.Length; i++)
                {
                    _combat.SetAutoAttack(players[i], false);
                    _combat.SetAutoAttack(players[i], true);
                    _combat.SetTarget(players[i], EntityID.Invalid);
                }

                _combat.Run(3u, 0.05f);

                // Invalid-target branches: a rejected selection, and a target lost by polling in Run.
                _combat.SetTarget(players[0], MobB);
                for (int i = 1; i < players.Length; i++)
                {
                    _combat.SetTarget(players[i], MobA);
                }

                _validity.Rejected.Add(MobA);
                _combat.Run(4u, 0.05f);
            }, Is.Not.AllocatingGCMemory());

            // Both invalid branches ran: every player ends idle with no target.
            for (int i = 0; i < players.Length; i++)
            {
                Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(players[i]));
                Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(players[i]));
            }
        }
    }
}
