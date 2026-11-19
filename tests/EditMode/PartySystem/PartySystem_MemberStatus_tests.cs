using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 004: member status (AC-1), the death sink (AC-2), the change marker
    /// (AC-3), <c>GetPartyMembersWithStatus</c> (AC-4), XP eligibility (AC-5) and the leader reference (AC-6).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_MemberStatus_Tests
    {
        private const int GATE = 5;
        private const int BUFFER_LENGTH = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT;
        private const int LOOP_ITERATIONS = 1000;
        private const uint UNKNOWN_ENTITY_RAW = 424242u;
        private const uint UNKNOWN_PARTY_RAW = 9999u;

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            private readonly Dictionary<EntityID, int> _levels = new Dictionary<EntityID, int>();

            public void Set(EntityID entity, int level) => _levels[entity] = level;

            public void Clear(EntityID entity) => _levels.Remove(entity);

            public bool TryGetLevel(EntityID entity, out int level) => _levels.TryGetValue(entity, out level);
        }

        private sealed class FakeStatReader : IZoneEntityStatReader
        {
            public int Level;

            public float GetCurrentHP(EntityID entityId) => 0f;

            public float GetCurrentMP(EntityID entityId) => 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => statId == StatID.Level ? Level : 0;
        }

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(1000u + n);

        private static readonly PartyMemberStatusEntry SENTINEL =
            new PartyMemberStatusEntry(new CharacterID(77u), new EntityID(1077u), PartyMemberStatus.Dead);

        private static void FillSentinel(PartyMemberStatusEntry[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = SENTINEL;
            }
        }

        private static void AssertAllSentinel(PartyMemberStatusEntry[] array, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                Assert.AreEqual(SENTINEL.CharacterID, array[i].CharacterID, "character at " + i);
                Assert.AreEqual(SENTINEL.EntityID, array[i].EntityID, "entity at " + i);
                Assert.AreEqual(SENTINEL.Status, array[i].Status, "status at " + i);
            }
        }

        private static PartySystemConfigValues Config(int gate)
        {
            return new PartySystemConfigValues(
                PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT,
                PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT,
                gate,
                PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT,
                PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT,
                PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT,
                PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT,
                PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT);
        }

        private static PartyRegistry NewRegistry() => new PartyRegistry(Config(GATE));

        private static PartyID Register(PartyRegistry reg, uint n)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(n), En(n), out PartyID solo));
            return solo;
        }

        /// <summary>Registers characters 1..count, creates a party led by 1 and joins the rest.</summary>
        private static PartyID MakeParty(PartyRegistry reg, int count)
        {
            for (uint n = 1; n <= (uint)count; n++)
            {
                Register(reg, n);
            }

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            for (uint n = 2; n <= (uint)count; n++)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(n)));
            }

            return party;
        }

        private static uint SeqOf(PartyRegistry reg, PartyID id)
        {
            Assert.IsTrue(reg.TryGetParty(id, out PartyState state));
            return state.SequenceId;
        }

        private static (PartyXpEligibility Elig, FakeLevels Levels) NewEligibility(PartyRegistry reg, int gate = GATE)
        {
            var levels = new FakeLevels();
            return (new PartyXpEligibility(reg, levels, Config(gate)), levels);
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_member_status_enum_values_match_wire_and_underlying_type_is_byte()
        {
            Assert.AreEqual(0, (byte)PartyMemberStatus.Online);
            Assert.AreEqual(1, (byte)PartyMemberStatus.Ghost);
            Assert.AreEqual(2, (byte)PartyMemberStatus.OutOfZone);
            Assert.AreEqual(3, (byte)PartyMemberStatus.Dead);
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(PartyMemberStatus)));
        }

        [Test]
        public void test_member_status_new_player_is_online()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);

            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
        }

        [Test]
        public void test_member_status_unregistered_character_is_online_and_does_not_throw()
        {
            PartyRegistry reg = NewRegistry();

            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(55)));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(CharacterID.Invalid));
        }

        [Test]
        public void test_member_status_dead_solo_player_creating_party_stays_dead()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID _));

            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(1)));
        }

        [Test]
        public void test_member_status_dead_solo_player_joining_party_stays_dead()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Register(reg, 2);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(2)));

            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(2)));
        }

        [Test]
        public void test_member_status_removed_member_keeps_status_after_resolo()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Dead);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out PartyID _));
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(3), out PartyID _));

            Assert.AreEqual(PartyMemberStatus.Ghost, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(3)));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
        }

        [Test]
        public void test_member_status_unregister_clears_and_reregister_is_online()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(1), out PartyID _));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
            Register(reg, 1);

            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
        }

        [Test]
        public void test_member_status_unregister_removes_entity_lookup()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.IsTrue(reg.TryGetCharacter(En(1), out CharacterID found));
            Assert.AreEqual(Ch(1), found);

            reg.UnregisterPlayer(Ch(1), out PartyID _);

            Assert.IsFalse(reg.TryGetCharacter(En(1), out CharacterID after));
            Assert.AreEqual(CharacterID.Invalid, after);
        }

        [Test]
        public void test_member_status_set_for_unregistered_character_returns_false_and_stores_nothing()
        {
            PartyRegistry reg = NewRegistry();

            Assert.IsFalse(reg.SetMemberStatus(Ch(55), PartyMemberStatus.Dead));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(55)));
        }

        // ---------------------------------------------------------------- AC-2

        [Test]
        public void test_member_status_sink_dead_then_online_keeps_slot_index()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var sink = new PartyMemberStatusSink(reg);
            Assert.AreEqual(Ch(2), reg.GetMemberAtIndex(party, 1));

            sink.SetMemberStatus(En(2), PartyMemberDeathStatus.Dead);
            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(Ch(2), reg.GetMemberAtIndex(party, 1));
            Assert.AreEqual(3, reg.GetPartyMembers(party).Count);

            sink.SetMemberStatus(En(2), PartyMemberDeathStatus.Online);
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(Ch(2), reg.GetMemberAtIndex(party, 1));
        }

        [Test]
        public void test_member_status_sink_solo_player_dead_is_stored()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            var sink = new PartyMemberStatusSink(reg);

            sink.SetMemberStatus(En(1), PartyMemberDeathStatus.Dead);

            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(1)));
        }

        [Test]
        public void test_member_status_sink_unknown_and_invalid_entity_is_noop_without_log()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var sink = new PartyMemberStatusSink(reg);
            uint seq = SeqOf(reg, party);

            sink.SetMemberStatus(new EntityID(UNKNOWN_ENTITY_RAW), PartyMemberDeathStatus.Dead);
            sink.SetMemberStatus(EntityID.Invalid, PartyMemberDeathStatus.Dead);

            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(seq, SeqOf(reg, party));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_member_status_sink_out_of_range_status_on_online_member_is_noop_and_does_not_throw()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            var sink = new PartyMemberStatusSink(reg);

            Assert.DoesNotThrow(() => sink.SetMemberStatus(En(1), (PartyMemberDeathStatus)200));

            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_member_status_sink_null_registry_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyMemberStatusSink(null));
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_member_status_change_bumps_sequence_by_one_and_same_value_does_not()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            uint seq = SeqOf(reg, party);

            Assert.IsTrue(reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));

            Assert.IsFalse(reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));

            Assert.IsTrue(reg.SetMemberStatus(Ch(2), PartyMemberStatus.Online));
            Assert.AreEqual(seq + 2u, SeqOf(reg, party));
        }

        [Test]
        public void test_member_status_online_write_to_online_member_does_not_bump_sequence()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            uint seq = SeqOf(reg, party);

            Assert.IsFalse(reg.SetMemberStatus(Ch(1), PartyMemberStatus.Online));

            Assert.AreEqual(seq, SeqOf(reg, party));
        }

        [Test]
        public void test_member_status_solo_change_bumps_only_its_own_solo_party()
        {
            PartyRegistry reg = NewRegistry();
            PartyID soloA = Register(reg, 1);
            PartyID soloB = Register(reg, 2);
            uint seqA = SeqOf(reg, soloA);
            uint seqB = SeqOf(reg, soloB);

            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            Assert.AreEqual(seqA + 1u, SeqOf(reg, soloA));
            Assert.AreEqual(seqB, SeqOf(reg, soloB));
        }

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_member_status_members_with_status_fills_entries_and_empty_slot()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            Span<PartyMemberStatusEntry> buffer = new PartyMemberStatusEntry[BUFFER_LENGTH];

            int count = reg.GetPartyMembersWithStatus(party, buffer);

            Assert.AreEqual(3, count);
            Assert.AreEqual(Ch(1), buffer[0].CharacterID);
            Assert.AreEqual(En(1), buffer[0].EntityID);
            Assert.AreEqual(PartyMemberStatus.Online, buffer[0].Status);
            Assert.AreEqual(Ch(2), buffer[1].CharacterID);
            Assert.AreEqual(En(2), buffer[1].EntityID);
            Assert.AreEqual(PartyMemberStatus.Dead, buffer[1].Status);
            Assert.AreEqual(Ch(3), buffer[2].CharacterID);
            Assert.AreEqual(En(3), buffer[2].EntityID);
            Assert.AreEqual(PartyMemberStatus.Ghost, buffer[2].Status);
            Assert.AreEqual(CharacterID.Invalid, buffer[3].CharacterID);
            Assert.AreEqual(EntityID.Invalid, buffer[3].EntityID);
            Assert.AreEqual(PartyMemberStatus.Online, buffer[3].Status);
        }

        [Test]
        public void test_member_status_members_with_status_overwrites_stale_buffer_content()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];
            FillSentinel(array);

            int count = reg.GetPartyMembersWithStatus(party, array);

            Assert.AreEqual(2, count);
            for (int i = 2; i < array.Length; i++)
            {
                Assert.AreEqual(CharacterID.Invalid, array[i].CharacterID);
                Assert.AreEqual(EntityID.Invalid, array[i].EntityID);
                Assert.AreEqual(PartyMemberStatus.Online, array[i].Status);
            }
        }

        [Test]
        public void test_member_status_members_with_status_solo_party_returns_one()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];

            int count = reg.GetPartyMembersWithStatus(solo, array);

            Assert.AreEqual(1, count);
            Assert.AreEqual(Ch(1), array[0].CharacterID);
            Assert.AreEqual(CharacterID.Invalid, array[1].CharacterID);
        }

        [Test]
        public void test_member_status_members_with_status_unknown_party_returns_zero_and_leaves_buffer()
        {
            PartyRegistry reg = NewRegistry();
            MakeParty(reg, 2);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];
            FillSentinel(array);

            int count = reg.GetPartyMembersWithStatus(new PartyID(UNKNOWN_PARTY_RAW), array);

            Assert.AreEqual(0, count);
            AssertAllSentinel(array, 0, array.Length);
        }

        [Test]
        public void test_member_status_members_with_status_short_buffer_returns_zero_and_leaves_buffer()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var array = new PartyMemberStatusEntry[PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT - 1];
            FillSentinel(array);

            int count = reg.GetPartyMembersWithStatus(party, array);

            Assert.AreEqual(0, count);
            AssertAllSentinel(array, 0, array.Length);
        }

        [Test]
        public void test_member_status_members_with_status_loop_allocates_zero_bytes()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Dead);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];
            reg.GetPartyMembersWithStatus(party, array);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < LOOP_ITERATIONS; i++)
            {
                reg.GetPartyMembersWithStatus(party, array);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
        }

        // ---------------------------------------------------------------- AC-5

        private static PartyID LeaderThirty(PartyRegistry reg, FakeLevels levels, int memberCount, params int[] lv)
        {
            PartyID party = MakeParty(reg, memberCount);
            for (int i = 0; i < lv.Length; i++)
            {
                levels.Set(En((uint)(i + 1)), lv[i]);
            }

            return party;
        }

        [Test]
        public void test_xp_eligibility_gate_is_inclusive_on_both_sides()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 4, 30, 35, 25, 36);

            Assert.IsTrue(elig.IsXpEligible(party, Ch(1)), "leader");
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)), "35");
            Assert.IsTrue(elig.IsXpEligible(party, Ch(3)), "25");
            Assert.IsFalse(elig.IsXpEligible(party, Ch(4)), "36");
        }

        [Test]
        public void test_xp_eligibility_level_24_is_outside_the_gate()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 2, 30, 24);

            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));
        }

        [Test]
        public void test_xp_eligibility_gate_zero_allows_only_same_level()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg, 0);
            PartyID party = LeaderThirty(reg, levels, 3, 30, 30, 31);

            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(3)));
        }

        [Test]
        public void test_xp_eligibility_non_online_member_is_not_eligible()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 4, 30, 30, 30, 30);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(4), PartyMemberStatus.OutOfZone);

            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(3)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(4)));

            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Online);
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)));
        }

        [Test]
        public void test_xp_eligibility_member_level_unreadable_is_false_for_that_member_only()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 3, 30, 30, 30);
            levels.Clear(En(2));

            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(3)));
        }

        [Test]
        public void test_xp_eligibility_leader_level_unreadable_is_false_for_everyone()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 3, 30, 30, 30);
            levels.Clear(En(1));

            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(3)));
        }

        [Test]
        public void test_xp_eligibility_character_not_in_party_or_unknown_party_is_false()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 2, 30, 30);
            Register(reg, 9);
            levels.Set(En(9), 30);

            Assert.IsFalse(elig.IsXpEligible(party, Ch(9)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(55)));
            Assert.IsFalse(elig.IsXpEligible(new PartyID(UNKNOWN_PARTY_RAW), Ch(1)));
        }

        [Test]
        public void test_xp_eligibility_online_leader_is_eligible()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 2, 30, 50);

            Assert.IsTrue(elig.IsXpEligible(party, Ch(1)));
        }

        [Test]
        public void test_xp_eligibility_constructor_rejects_null_arguments()
        {
            PartyRegistry reg = NewRegistry();

            Assert.Throws<ArgumentNullException>(() => new PartyXpEligibility(null, new FakeLevels(), Config(GATE)));
            Assert.Throws<ArgumentNullException>(() => new PartyXpEligibility(reg, null, Config(GATE)));
        }

        // ---------------------------------------------------------------- AC-6

        [Test]
        public void test_xp_eligibility_leader_reference_follows_leader_index()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 4, 30, 35, 25, 40);

            reg.SetLeaderIndexForTests(party, 3);

            Assert.AreEqual(4, reg.GetPartyMembers(party).Count);
            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)), "30 vs 40");
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)), "35 vs 40");
            Assert.IsFalse(elig.IsXpEligible(party, Ch(3)), "25 vs 40");
            Assert.IsTrue(elig.IsXpEligible(party, Ch(4)), "leader");

            reg.SetLeaderIndexForTests(party, 0);

            Assert.IsTrue(elig.IsXpEligible(party, Ch(1)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(3)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(4)), "40 vs 30");
        }

        // ------------------------------------------------- production level source

        [Test]
        public void test_xp_eligibility_stat_reader_source_reads_level_and_treats_zero_or_invalid_as_unreadable()
        {
            var stats = new FakeStatReader { Level = 12 };
            var source = new StatReaderPartyMemberLevelSource(stats);

            Assert.IsTrue(source.TryGetLevel(En(1), out int level));
            Assert.AreEqual(12, level);

            stats.Level = 0;
            Assert.IsFalse(source.TryGetLevel(En(1), out int zero));
            Assert.AreEqual(0, zero);
            Assert.IsFalse(source.TryGetLevel(EntityID.Invalid, out int _));
        }

        [Test]
        public void test_xp_eligibility_stat_reader_source_null_reader_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new StatReaderPartyMemberLevelSource(null));
        }

        // ------------------------------------------------- review follow-ups

        [Test]
        public void test_member_status_duplicate_entity_is_refused_without_state_change()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            int partyCount = reg.PartyCount;

            PartyRegistryResult result = reg.RegisterPlayer(Ch(2), En(1), out PartyID id);

            Assert.AreEqual(PartyRegistryResult.EntityAlreadyRegistered, result);
            Assert.AreEqual(PartyID.Uninitialized, id);
            Assert.AreEqual(partyCount, reg.PartyCount);
            Assert.IsFalse(reg.IsRegistered(Ch(2)));
            Assert.IsTrue(reg.TryGetCharacter(En(1), out CharacterID owner));
            Assert.AreEqual(Ch(1), owner);
            // No id was burned: the next registration gets the id right after character 1's solo party.
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(3), En(3), out PartyID next));
            Assert.AreEqual(reg.GetPartyID(Ch(1)).RawValue + 1u, next.RawValue);
        }

        [Test]
        public void test_member_status_entity_can_be_reused_after_owner_unregisters()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            reg.UnregisterPlayer(Ch(1), out PartyID _);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(2), En(1), out PartyID _));

            Assert.IsTrue(reg.TryGetCharacter(En(1), out CharacterID owner));
            Assert.AreEqual(Ch(2), owner);
        }

        [Test]
        public void test_xp_eligibility_leader_index_out_of_range_is_false_and_does_not_throw()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 2, 30, 30);

            reg.SetLeaderIndexForTests(party, 99);
            Assert.DoesNotThrow(() => elig.IsXpEligible(party, Ch(1)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));

            reg.SetLeaderIndexForTests(party, -1);
            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)));
            Assert.IsFalse(elig.IsXpEligible(party, Ch(2)));
        }

        [Test]
        public void test_member_status_sink_out_of_range_status_on_dead_member_keeps_dead_and_sequence()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var sink = new PartyMemberStatusSink(reg);
            sink.SetMemberStatus(En(2), PartyMemberDeathStatus.Dead);
            uint seq = SeqOf(reg, party);

            Assert.DoesNotThrow(() => sink.SetMemberStatus(En(2), (PartyMemberDeathStatus)200));

            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(seq, SeqOf(reg, party));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_member_status_write_after_remove_bumps_only_the_new_solo_party()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out PartyID solo));
            uint oldSeq = SeqOf(reg, party);
            uint soloSeq = SeqOf(reg, solo);

            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            Assert.AreEqual(soloSeq + 1u, SeqOf(reg, solo));
            Assert.AreEqual(oldSeq, SeqOf(reg, party));
        }

        [Test]
        public void test_member_status_in_party_write_bumps_only_that_party()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            PartyID other = Register(reg, 9);
            uint partySeq = SeqOf(reg, party);
            uint otherSeq = SeqOf(reg, other);

            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);

            Assert.AreEqual(partySeq + 1u, SeqOf(reg, party));
            Assert.AreEqual(otherSeq, SeqOf(reg, other));
        }

        [Test]
        public void test_xp_eligibility_dead_leader_is_not_eligible_but_members_stay_eligible()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 3, 30, 30, 32);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Dead);

            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(3)));
        }

        [Test]
        public void test_xp_eligibility_out_of_zone_leader_is_not_eligible()
        {
            PartyRegistry reg = NewRegistry();
            var (elig, levels) = NewEligibility(reg);
            PartyID party = LeaderThirty(reg, levels, 2, 30, 30);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.OutOfZone);

            Assert.IsFalse(elig.IsXpEligible(party, Ch(1)));
            Assert.IsTrue(elig.IsXpEligible(party, Ch(2)));
        }

        [Test]
        public void test_member_status_dead_and_ghost_survive_disband_and_disbanded_id_reports_zero()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Dead);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];
            FillSentinel(array);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.DisbandParty(party));

            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(2)));
            Assert.AreEqual(PartyMemberStatus.Ghost, reg.GetMemberStatus(Ch(3)));
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));
            Assert.AreEqual(0, reg.GetPartyMembersWithStatus(party, array));
            AssertAllSentinel(array, 0, array.Length);
        }

        [Test]
        public void test_member_status_sink_after_unregister_is_noop_and_does_not_reregister()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            var sink = new PartyMemberStatusSink(reg);
            reg.UnregisterPlayer(Ch(1), out PartyID _);

            sink.SetMemberStatus(En(1), PartyMemberDeathStatus.Dead);

            Assert.IsFalse(reg.IsRegistered(Ch(1)));
            Assert.IsFalse(reg.TryGetCharacter(En(1), out CharacterID _));
            Assert.AreEqual(0, reg.PartyCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_member_status_sink_reregistered_character_old_entity_noop_new_entity_works()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            var sink = new PartyMemberStatusSink(reg);
            reg.UnregisterPlayer(Ch(1), out PartyID _);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(1), En(50), out PartyID _));

            sink.SetMemberStatus(En(1), PartyMemberDeathStatus.Dead);
            Assert.AreEqual(PartyMemberStatus.Online, reg.GetMemberStatus(Ch(1)));

            sink.SetMemberStatus(En(50), PartyMemberDeathStatus.Dead);
            Assert.AreEqual(PartyMemberStatus.Dead, reg.GetMemberStatus(Ch(1)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_member_status_members_with_status_over_long_buffer_leaves_tail_untouched()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH * 2];
            FillSentinel(array);

            int count = reg.GetPartyMembersWithStatus(party, array);

            Assert.AreEqual(2, count);
            Assert.AreEqual(Ch(1), array[0].CharacterID);
            Assert.AreEqual(CharacterID.Invalid, array[BUFFER_LENGTH - 1].CharacterID);
            AssertAllSentinel(array, BUFFER_LENGTH, array.Length);
        }

        [Test]
        public void test_member_status_members_with_status_full_party_fills_every_slot()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, BUFFER_LENGTH);
            reg.SetMemberStatus(Ch(4), PartyMemberStatus.OutOfZone);
            var array = new PartyMemberStatusEntry[BUFFER_LENGTH];

            int count = reg.GetPartyMembersWithStatus(party, array);

            Assert.AreEqual(BUFFER_LENGTH, count);
            for (uint n = 1; n <= (uint)BUFFER_LENGTH; n++)
            {
                Assert.AreEqual(Ch(n), array[n - 1].CharacterID);
                Assert.AreEqual(En(n), array[n - 1].EntityID);
            }

            Assert.AreEqual(PartyMemberStatus.OutOfZone, array[3].Status);
            Assert.AreEqual(PartyMemberStatus.Online, array[2].Status);
        }
    }
}
