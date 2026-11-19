using System;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.PartySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 005: party XP distribution (AC-1 seam and pass-through, AC-2 split,
    /// AC-3 ineligible members, AC-4 capped member, AC-5 call-time ordering and Disbanding).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_XpDistribution_Tests
    {
        private const int GATE = 5;
        private const int XP_BASE = 1000;
        private const int XP_N1 = 1000;
        private const int XP_N2 = 900;
        private const int XP_N3 = 800;
        private const int XP_N4 = 700;
        private const int LEVEL_LEADER = 30;
        private const int LEVEL_GAP_5 = 35;
        private const int LEVEL_GAP_6 = 36;
        private const int LEVEL_LEADER_HIGH = 56;
        private const int LEVEL_CAP = 60;
        private const uint UNKNOWN_ENTITY_RAW = 424242u;

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            private readonly Dictionary<EntityID, int> _levels = new Dictionary<EntityID, int>();

            public void Set(EntityID entity, int level) => _levels[entity] = level;

            public bool TryGetLevel(EntityID entity, out int level) => _levels.TryGetValue(entity, out level);
        }

        private sealed class RecordingSink : IAutoAttackExperienceSink
        {
            public readonly List<(EntityID Entity, int Amount)> Calls = new List<(EntityID, int)>();

            public void AddExperience(EntityID attacker, int amount) => Calls.Add((attacker, amount));
        }

        private sealed class Fixture
        {
            public PartyRegistry Registry;
            public FakeLevels Levels;
            public RecordingSink Sink;
            public PartyExperienceDistributor Distributor;
        }

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(1000u + n);

        private static PartySystemConfigValues Config()
        {
            return new PartySystemConfigValues(
                PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT,
                PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT,
                GATE,
                PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT,
                PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT,
                PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT,
                PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT,
                PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT);
        }

        private static Fixture NewFixture()
        {
            var fx = new Fixture
            {
                Registry = new PartyRegistry(Config()),
                Levels = new FakeLevels(),
                Sink = new RecordingSink(),
            };
            var eligibility = new PartyXpEligibility(fx.Registry, fx.Levels, Config());
            fx.Distributor = new PartyExperienceDistributor(fx.Sink, fx.Registry, eligibility, Config());
            return fx;
        }

        private static PartyID Register(Fixture fx, uint n, int level = LEVEL_LEADER)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, fx.Registry.RegisterPlayer(Ch(n), En(n), out PartyID solo));
            fx.Levels.Set(En(n), level);
            return solo;
        }

        /// <summary>Registers characters 1..count at the leader level, creates a party led by 1, joins the rest.</summary>
        private static PartyID MakeParty(Fixture fx, int count)
        {
            for (uint n = 1; n <= (uint)count; n++)
            {
                Register(fx, n);
            }

            Assert.AreEqual(PartyRegistryResult.Ok, fx.Registry.TryCreateParty(Ch(1), out PartyID party));
            for (uint n = 2; n <= (uint)count; n++)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, fx.Registry.TryAddMember(party, Ch(n)));
            }

            return party;
        }

        /// <summary>Records each award; the first award it sees runs a one-shot callback (to simulate a level-up).</summary>
        private sealed class LevelUpOnFirstAwardSink : IAutoAttackExperienceSink
        {
            public readonly List<(EntityID Entity, int Amount)> Calls = new List<(EntityID, int)>();
            public Action OnFirstAward;

            public void AddExperience(EntityID attacker, int amount)
            {
                Calls.Add((attacker, amount));
                Action once = OnFirstAward;
                OnFirstAward = null;
                once?.Invoke();
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Pass-through and award paths must never log; only the zero-eligible tests expect a line.
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertCall(Fixture fx, int index, uint member, int amount)
        {
            Assert.AreEqual(En(member), fx.Sink.Calls[index].Entity, "entity at call " + index);
            Assert.AreEqual(amount, fx.Sink.Calls[index].Amount, "amount at call " + index);
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_xp_distribution_unknown_killer_entity_passes_through_once()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 2);
            var unknown = new EntityID(UNKNOWN_ENTITY_RAW);

            fx.Distributor.AddExperience(unknown, XP_BASE);

            Assert.AreEqual(1, fx.Sink.Calls.Count);
            Assert.AreEqual(unknown, fx.Sink.Calls[0].Entity);
            Assert.AreEqual(XP_BASE, fx.Sink.Calls[0].Amount);
        }

        [Test]
        public void test_xp_distribution_solo_killer_passes_through_once_with_full_amount()
        {
            Fixture fx = NewFixture();
            Register(fx, 1);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(1, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_BASE);
        }

        [Test]
        public void test_xp_distribution_dead_solo_killer_still_gets_full_amount()
        {
            Fixture fx = NewFixture();
            Register(fx, 1);
            fx.Registry.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(1, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_BASE);
        }

        [Test]
        public void test_xp_distribution_distributor_is_assignable_to_the_combat_sink_interface()
        {
            Assert.IsTrue(typeof(IAutoAttackExperienceSink).IsAssignableFrom(typeof(PartyExperienceDistributor)));
        }

        // ---------------------------------------------------------------- AC-2

        [Test]
        public void test_xp_distribution_one_eligible_member_gets_full_amount()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 2);
            fx.Registry.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(1, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N1);
        }

        [Test]
        public void test_xp_distribution_two_eligible_members_get_900_each_in_join_order()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 2);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        [Test]
        public void test_xp_distribution_three_eligible_members_get_800_each_in_join_order()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);

            fx.Distributor.AddExperience(En(2), XP_BASE);

            Assert.AreEqual(3, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N3);
            AssertCall(fx, 1, 2, XP_N3);
            AssertCall(fx, 2, 3, XP_N3);
        }

        [Test]
        public void test_xp_distribution_four_eligible_members_get_700_each_in_join_order()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 4);

            fx.Distributor.AddExperience(En(4), XP_BASE);

            Assert.AreEqual(4, fx.Sink.Calls.Count);
            for (uint n = 1; n <= 4; n++)
            {
                AssertCall(fx, (int)n - 1, n, XP_N4);
            }
        }

        [Test]
        public void test_xp_distribution_dead_killer_gets_nothing_and_others_split()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Registry.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            fx.Distributor.AddExperience(En(2), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 3, XP_N2);
        }

        [Test]
        public void test_xp_distribution_killer_outside_level_gate_gets_nothing_and_others_split()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Levels.Set(En(2), LEVEL_GAP_6);

            fx.Distributor.AddExperience(En(2), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 3, XP_N2);
        }

        [Test]
        public void test_xp_distribution_dead_leader_gets_nothing_and_others_split()
        {
            // Arrange: the leader's status is not part of the gate; the others are still measured from its level.
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Registry.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            // Act
            fx.Distributor.AddExperience(En(2), XP_BASE);

            // Assert
            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 2, XP_N2);
            AssertCall(fx, 1, 3, XP_N2);
        }

        [TestCase(999, 2, 899)]
        [TestCase(1, 4, 1)]
        [TestCase(1001, 3, 801)]
        public void test_xp_distribution_amount_rounds_per_formula(int xpBase, int members, int expectedEach)
        {
            // Arrange (boundary values: the exact numbers are the point)
            Fixture fx = NewFixture();
            MakeParty(fx, members);

            // Act
            fx.Distributor.AddExperience(En(1), xpBase);

            // Assert
            Assert.AreEqual(members, fx.Sink.Calls.Count);
            for (uint n = 1; n <= (uint)members; n++)
            {
                AssertCall(fx, (int)n - 1, n, expectedEach);
            }
        }

        [Test]
        public void test_xp_distribution_level_up_during_award_does_not_change_recipients_or_amount()
        {
            // Arrange: party of 3 at levels 30 / 30 / 25 (member 3 exactly at the gate). The leader is paid first and
            // "levels up" to 31 inside that award, which would push member 3 to a gap of 6 on a second read.
            var registry = new PartyRegistry(Config());
            var levels = new FakeLevels();
            var sink = new LevelUpOnFirstAwardSink();
            var eligibility = new PartyXpEligibility(registry, levels, Config());
            var distributor = new PartyExperienceDistributor(sink, registry, eligibility, Config());
            var fx = new Fixture { Registry = registry, Levels = levels };
            MakeParty(fx, 3);
            levels.Set(En(3), LEVEL_LEADER - GATE);
            sink.OnFirstAward = () => levels.Set(En(1), LEVEL_LEADER + 1);

            // Act
            distributor.AddExperience(En(1), XP_BASE);

            // Assert: all three paid the N = 3 amount
            Assert.AreEqual(3, sink.Calls.Count);
            for (uint n = 1; n <= 3; n++)
            {
                Assert.AreEqual(En(n), sink.Calls[(int)n - 1].Entity);
                Assert.AreEqual(XP_N3, sink.Calls[(int)n - 1].Amount);
            }
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_xp_distribution_out_of_zone_member_is_excluded_and_n_reduced()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Registry.SetMemberStatus(Ch(3), PartyMemberStatus.OutOfZone);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        [Test]
        public void test_xp_distribution_ghost_member_is_excluded_and_n_reduced()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Registry.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 3, XP_N2);
        }

        [Test]
        public void test_xp_distribution_dead_member_is_excluded_and_n_reduced()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Registry.SetMemberStatus(Ch(3), PartyMemberStatus.Dead);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        [Test]
        public void test_xp_distribution_level_gap_6_is_excluded_and_gap_5_is_included()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 3);
            fx.Levels.Set(En(2), LEVEL_GAP_5);
            fx.Levels.Set(En(3), LEVEL_GAP_6);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        [Test]
        public void test_xp_distribution_all_ineligible_makes_no_calls_and_logs_one_zero_award_line()
        {
            Fixture fx = NewFixture();
            PartyID party = MakeParty(fx, 2);
            fx.Registry.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);
            fx.Registry.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            LogAssert.Expect(LogType.Log, "[PartyXp] party " + party + ": 0 eligible, 0 XP awarded");
            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(0, fx.Sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_xp_distribution_all_ineligible_by_ghost_and_out_of_zone_logs_zero_award_line()
        {
            // Arrange
            Fixture fx = NewFixture();
            PartyID party = MakeParty(fx, 2);
            fx.Registry.SetMemberStatus(Ch(1), PartyMemberStatus.Ghost);
            fx.Registry.SetMemberStatus(Ch(2), PartyMemberStatus.OutOfZone);
            LogAssert.Expect(LogType.Log, "[PartyXp] party " + party + ": 0 eligible, 0 XP awarded");

            // Act
            fx.Distributor.AddExperience(En(2), XP_BASE);

            // Assert
            Assert.AreEqual(0, fx.Sink.Calls.Count);
        }

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_xp_distribution_level_60_member_counts_toward_n_and_gets_a_call()
        {
            Fixture fx = NewFixture();
            MakeParty(fx, 4);
            for (uint n = 1; n <= 3; n++)
            {
                fx.Levels.Set(En(n), LEVEL_LEADER_HIGH);
            }

            fx.Levels.Set(En(4), LEVEL_CAP);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(4, fx.Sink.Calls.Count);
            for (uint n = 1; n <= 4; n++)
            {
                AssertCall(fx, (int)n - 1, n, XP_N4);
            }
        }

        // ---------------------------------------------------------------- AC-5

        [Test]
        public void test_xp_distribution_member_removed_after_the_kill_keeps_the_award_already_made()
        {
            Fixture fx = NewFixture();
            PartyID party = MakeParty(fx, 2);

            fx.Distributor.AddExperience(En(1), XP_BASE);
            Assert.AreEqual(PartyRegistryResult.Ok, fx.Registry.RemoveMember(party, Ch(2), out PartyID _));

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        [Test]
        public void test_xp_distribution_kill_after_member_removal_excludes_the_removed_member()
        {
            // Arrange: first kill pays both, then member 2 leaves (member 1 stays in a real party of 1).
            Fixture fx = NewFixture();
            PartyID party = MakeParty(fx, 2);
            fx.Distributor.AddExperience(En(1), XP_BASE);
            Assert.AreEqual(PartyRegistryResult.Ok, fx.Registry.RemoveMember(party, Ch(2), out PartyID _));

            // Act
            fx.Distributor.AddExperience(En(1), XP_BASE);

            // Assert: first kill unchanged; second kill pays only member 1, N = 1
            Assert.AreEqual(3, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
            AssertCall(fx, 2, 1, XP_N1);
        }

        [Test]
        public void test_xp_distribution_disbanding_party_still_awards_normally()
        {
            Fixture fx = NewFixture();
            PartyID party = MakeParty(fx, 2);
            fx.Registry.SetStateForTests(party, PartyLifecycleState.Disbanding);
            Assert.IsTrue(fx.Registry.TryGetParty(party, out PartyState state));
            Assert.AreEqual(PartyLifecycleState.Disbanding, state.State);

            fx.Distributor.AddExperience(En(1), XP_BASE);

            Assert.AreEqual(2, fx.Sink.Calls.Count);
            AssertCall(fx, 0, 1, XP_N2);
            AssertCall(fx, 1, 2, XP_N2);
        }

        // ---------------------------------------------------------------- constructor

        [Test]
        public void test_xp_distribution_constructor_rejects_null_arguments()
        {
            Fixture fx = NewFixture();
            var eligibility = new PartyXpEligibility(fx.Registry, fx.Levels, Config());

            Assert.Throws<ArgumentNullException>(() =>
                new PartyExperienceDistributor(null, fx.Registry, eligibility, Config()));
            Assert.Throws<ArgumentNullException>(() =>
                new PartyExperienceDistributor(fx.Sink, null, eligibility, Config()));
            Assert.Throws<ArgumentNullException>(() =>
                new PartyExperienceDistributor(fx.Sink, fx.Registry, null, Config()));
        }
    }
}
