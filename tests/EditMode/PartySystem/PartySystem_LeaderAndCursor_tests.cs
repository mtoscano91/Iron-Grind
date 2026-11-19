using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// EditMode tests for Party System Story 003: round-robin cursor (AC-1..AC-4), leader compaction (AC-5),
    /// leadership transfer (AC-6), the XP-ineligible notice (AC-7) and the IPartyService facade (AC-8).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_LeaderAndCursor_Tests
    {
        private const int GATE = 5;
        private const int LEVEL_BASE = 30;
        private const int LEVEL_FAR = LEVEL_BASE + 4 * GATE;
        private const int LOOP_ITERATIONS = 1000;
        private const uint UNKNOWN_PARTY_RAW = 9999u;
        private const string FROZEN_PATTERN = "round-robin cursor frozen";
        private const string NO_CANDIDATE_PATTERN = "leadership not transferred";

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            private readonly Dictionary<EntityID, int> _levels = new Dictionary<EntityID, int>();

            public void Set(EntityID entity, int level) => _levels[entity] = level;

            public bool TryGetLevel(EntityID entity, out int level) => _levels.TryGetValue(entity, out level);
        }

        private sealed class RecordingSink : IPartyXpIneligibleNoticeSink
        {
            public readonly List<(CharacterID Character, PartyID Party)> Notices =
                new List<(CharacterID, PartyID)>();

            public void Notify(CharacterID character, PartyID partyId) => Notices.Add((character, partyId));
        }

        private sealed class RecordingListener : IPartyLeaderChangeListener
        {
            public readonly List<string> Calls = new List<string>();

            public void OnLeaderChanging(PartyID partyId) => Calls.Add("changing");

            public void OnLeaderChanged(PartyID partyId) => Calls.Add("changed");
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

        private static PartyRegistry NewRegistry() => new PartyRegistry(Config());

        private static PartyID Register(PartyRegistry reg, uint n)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(n), En(n), out PartyID solo));
            return solo;
        }

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

        private static PartyState State(PartyRegistry reg, PartyID id)
        {
            Assert.IsTrue(reg.TryGetParty(id, out PartyState state));
            return state;
        }

        private static uint SeqOf(PartyRegistry reg, PartyID id) => State(reg, id).SequenceId;

        private static (PartyXpIneligibleNotifier Notifier, RecordingSink Sink, FakeLevels Levels) NewNotifier(
            PartyRegistry reg)
        {
            var levels = new FakeLevels();
            var sink = new RecordingSink();
            var elig = new PartyXpEligibility(reg, levels, Config());
            return (new PartyXpIneligibleNotifier(reg, elig, sink), sink, levels);
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_leader_cursor_advance_from_last_wraps_to_zero_and_read_is_pure()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetRrNextIndexForTests(party, 2);

            Assert.AreEqual(2, reg.GetRrNextIndex(party));
            Assert.AreEqual(2, reg.GetRrNextIndex(party));
            Assert.AreEqual(Ch(3), reg.GetMemberAtIndex(party, reg.GetRrNextIndex(party)));
            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_advance_from_zero_goes_to_one_and_bumps_sequence_once()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            uint seq = SeqOf(reg, party);

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(1, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-2

        [TestCase(PartyMemberStatus.Ghost)]
        [TestCase(PartyMemberStatus.Dead)]
        [TestCase(PartyMemberStatus.OutOfZone)]
        public void test_leader_cursor_advance_skips_non_online_member(PartyMemberStatus status)
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), status);

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(2, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_advance_with_only_start_slot_eligible_wraps_back_without_change_or_warning()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq, SeqOf(reg, party));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_leader_cursor_all_ghost_freezes_with_one_warning_and_no_sequence_bump()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetRrNextIndexForTests(party, 1);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);
            LogAssert.Expect(LogType.Warning,
                new Regex(FROZEN_PATTERN + ": " + Regex.Escape(party.ToString()) + " cursor 1"));

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(1, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_cursor_mixed_ineligible_statuses_freeze_with_one_warning()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.OutOfZone);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Dead);
            uint seq = SeqOf(reg, party);
            LogAssert.Expect(LogType.Warning, new Regex(FROZEN_PATTERN));

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq, SeqOf(reg, party));
        }

        // ---------------------------------------------------------------- AC-4

        [Test]
        public void test_leader_cursor_clamps_to_zero_when_last_slot_removed_at_cursor()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetRrNextIndexForTests(party, 3);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(4), out PartyID _));

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_below_new_count_is_kept_when_last_slot_removed()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetRrNextIndexForTests(party, 1);

            reg.RemoveMember(party, Ch(4), out PartyID _);

            Assert.AreEqual(1, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_two_with_first_slot_removed_is_not_clamped()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetRrNextIndexForTests(party, 2);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.AreEqual(2, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_clamp_via_unregister_uses_modulo_new_count()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetRrNextIndexForTests(party, 3);

            reg.UnregisterPlayer(Ch(2), out PartyID _);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_cursor_solo_party_is_zero_and_advance_is_noop()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            uint seq = SeqOf(reg, solo);

            reg.AdvanceRrNextIndex(solo);

            Assert.AreEqual(0, reg.GetRrNextIndex(solo));
            Assert.AreEqual(seq, SeqOf(reg, solo));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-5

        [Test]
        public void test_leader_compaction_removal_below_leader_decrements_index()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetLeaderIndexForTests(party, 2);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(2), out PartyID _);

            Assert.AreEqual(1, State(reg, party).LeaderIndex);
            Assert.AreEqual(Ch(3), reg.GetMemberAtIndex(party, State(reg, party).LeaderIndex));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_compaction_removal_above_leader_keeps_index_and_notifies_nobody()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            reg.SetLeaderIndexForTests(party, 1);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(3), out PartyID _);

            Assert.AreEqual(1, State(reg, party).LeaderIndex);
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
            Assert.IsEmpty(listener.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_compaction_leader_removed_via_remove_member_promotes_next_and_notifies_in_order()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.AreEqual(0, State(reg, party).LeaderIndex);
            Assert.AreEqual(Ch(2), reg.GetMemberAtIndex(party, 0));
            Assert.AreEqual(new[] { "changing", "changed" }, listener.Calls.ToArray());
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_compaction_leader_removed_via_unregister_promotes_next()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            reg.SetLeaderIndexForTests(party, 1);
            uint seq = SeqOf(reg, party);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(2), out PartyID former));

            Assert.AreEqual(party, former);
            Assert.AreEqual(0, State(reg, party).LeaderIndex);
            Assert.AreEqual(2, reg.GetPartyMembers(party).Count);
            Assert.AreEqual(new[] { "changing", "changed" }, listener.Calls.ToArray());
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_compaction_leader_at_zero_leaves_skips_ghost_successor()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.AreEqual(1, State(reg, party).LeaderIndex);
            Assert.AreEqual(Ch(3), reg.GetMemberAtIndex(party, 1));
        }

        [Test]
        public void test_leader_compaction_leader_leaves_with_all_others_ghost_index_zero_leads()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            reg.SetMemberStatus(Ch(3), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.AreEqual(0, State(reg, party).LeaderIndex);
            Assert.AreEqual(2, reg.GetPartyMembers(party).Count);
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
        }

        // ---------------------------------------------------------------- AC-6

        [Test]
        public void test_leader_transfer_skips_ghost_moves_to_lowest_other_and_bumps_sequence_once()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(2, State(reg, party).LeaderIndex);
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
            Assert.AreEqual(3, reg.GetPartyMembers(party).Count);
        }

        [Test]
        public void test_leader_transfer_without_candidate_returns_false_logs_info_and_keeps_leader()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            reg.SetMemberStatus(Ch(2), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);
            LogAssert.Expect(LogType.Log, new Regex(NO_CANDIDATE_PATTERN));

            Assert.IsFalse(reg.TransferLeadership(party));

            Assert.AreEqual(0, State(reg, party).LeaderIndex);
            Assert.AreEqual(seq, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_transfer_former_leader_online_again_does_not_regain_leadership()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Ghost);
            Assert.IsTrue(reg.TransferLeadership(party));
            Assert.AreEqual(1, State(reg, party).LeaderIndex);

            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Online);

            Assert.AreEqual(1, State(reg, party).LeaderIndex);
        }

        [TestCase(PartyMemberStatus.Dead)]
        [TestCase(PartyMemberStatus.OutOfZone)]
        public void test_leader_transfer_dead_or_out_of_zone_member_can_lead(PartyMemberStatus status)
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            reg.SetMemberStatus(Ch(2), status);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(1, State(reg, party).LeaderIndex);
        }

        [Test]
        public void test_leader_transfer_unknown_solo_and_freed_party_return_false()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 5);
            PartyID party = MakeParty(reg, 2);
            reg.DisbandParty(party);

            Assert.IsFalse(reg.TransferLeadership(new PartyID(UNKNOWN_PARTY_RAW)));
            Assert.IsFalse(reg.TransferLeadership(solo));
            Assert.IsFalse(reg.TransferLeadership(party));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_transfer_notifies_listener_in_order_only_on_success()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(new[] { "changing", "changed" }, listener.Calls.ToArray());
        }

        [Test]
        public void test_leader_removal_last_member_frees_party_without_transfer_or_listener()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            reg.RemoveMember(party, Ch(2), out PartyID _);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.IsFalse(reg.TryGetParty(party, out PartyState _));
            Assert.IsEmpty(listener.Calls);
        }

        [Test]
        public void test_leader_removal_without_listener_does_not_throw()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);

            Assert.DoesNotThrow(() => reg.RemoveMember(party, Ch(1), out PartyID _));
            Assert.DoesNotThrow(() => reg.TransferLeadership(party));
        }

        // ---------------------------------------------------------------- AC-7

        [Test]
        public void test_leader_notice_transfer_notifies_each_member_pushed_out_of_the_gate_once()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 4);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_FAR);
            levels.Set(En(3), LEVEL_BASE);
            levels.Set(En(4), LEVEL_FAR);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(2, sink.Notices.Count);
            Assert.AreEqual(Ch(1), sink.Notices[0].Character);
            Assert.AreEqual(Ch(3), sink.Notices[1].Character);
            Assert.AreEqual(party, sink.Notices[0].Party);
            Assert.AreEqual(4, reg.GetPartyMembers(party).Count);
        }

        [Test]
        public void test_leader_notice_leader_leaving_notifies_remaining_member_but_not_departed_leader()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 3);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_FAR);
            levels.Set(En(3), LEVEL_BASE);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            Assert.AreEqual(1, sink.Notices.Count);
            Assert.AreEqual(Ch(3), sink.Notices[0].Character);
        }

        [Test]
        public void test_leader_notice_leader_leaving_via_unregister_notifies_remaining_member()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 3);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_FAR);
            levels.Set(En(3), LEVEL_BASE);

            reg.UnregisterPlayer(Ch(1), out PartyID _);

            Assert.AreEqual(1, sink.Notices.Count);
            Assert.AreEqual(Ch(3), sink.Notices[0].Character);
            Assert.AreEqual(party, sink.Notices[0].Party);
        }

        [Test]
        public void test_leader_notice_member_still_eligible_after_change_is_not_notified()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 3);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_BASE + 1);
            levels.Set(En(3), LEVEL_BASE + 2);

            reg.TransferLeadership(party);

            Assert.IsEmpty(sink.Notices);
        }

        [Test]
        public void test_leader_notice_non_leader_removal_sends_nothing()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 3);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_FAR);
            levels.Set(En(3), LEVEL_BASE);

            reg.RemoveMember(party, Ch(3), out PartyID _);

            Assert.IsEmpty(sink.Notices);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_notice_member_already_ineligible_before_transfer_gets_no_notice()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 4);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_BASE + GATE - 1); // next leader, within the gate of the old leader
            levels.Set(En(3), LEVEL_FAR); // already out before, stays out
            levels.Set(En(4), LEVEL_BASE - GATE + 1); // in the gate of the old leader, out vs the new one

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(1, sink.Notices.Count);
            Assert.AreEqual(Ch(4), sink.Notices[0].Character);
        }

        [Test]
        public void test_leader_notice_new_leader_gets_no_notice_even_when_outside_gate_of_old_leader()
        {
            PartyRegistry reg = NewRegistry();
            var (_, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 2);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_BASE + GATE + 3);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.AreEqual(1, sink.Notices.Count);
            Assert.AreEqual(Ch(1), sink.Notices[0].Character);
        }

        [Test]
        public void test_leader_notice_unknown_party_and_changed_without_changing_send_nothing()
        {
            PartyRegistry reg = NewRegistry();
            var (notifier, sink, levels) = NewNotifier(reg);
            PartyID party = MakeParty(reg, 3);
            levels.Set(En(1), LEVEL_BASE);
            levels.Set(En(2), LEVEL_FAR);
            levels.Set(En(3), LEVEL_BASE);
            var unknown = new PartyID(UNKNOWN_PARTY_RAW);

            Assert.DoesNotThrow(() => notifier.OnLeaderChanging(unknown));
            Assert.DoesNotThrow(() => notifier.OnLeaderChanged(unknown));
            Assert.DoesNotThrow(() => notifier.OnLeaderChanged(party));

            Assert.IsEmpty(sink.Notices);
        }

        // ---------------------------------------------------------------- listener and cursor edge cases

        [Test]
        public void test_leader_removal_below_leader_with_cursor_clamp_bumps_sequence_once_and_no_listener()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            reg.SetLeaderIndexForTests(party, 3);
            reg.SetRrNextIndexForTests(party, 3);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(3), out PartyID _);

            Assert.AreEqual(2, State(reg, party).LeaderIndex);
            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
            Assert.IsEmpty(listener.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_removal_of_leader_with_cursor_clamp_notifies_once_and_bumps_sequence_once()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            reg.SetLeaderIndexForTests(party, 3);
            reg.SetRrNextIndexForTests(party, 3);
            uint seq = SeqOf(reg, party);

            reg.RemoveMember(party, Ch(4), out PartyID _);

            Assert.AreEqual(0, State(reg, party).LeaderIndex);
            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq + 1u, SeqOf(reg, party));
            Assert.AreEqual(new[] { "changing", "changed" }, listener.Calls.ToArray());
        }

        [Test]
        public void test_leader_cursor_one_member_forming_party_advance_stays_at_zero_without_bump_or_warning()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            uint seq = SeqOf(reg, party);

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq, SeqOf(reg, party));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_cursor_one_member_ghost_party_freezes_with_one_warning()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            reg.SetMemberStatus(Ch(1), PartyMemberStatus.Ghost);
            uint seq = SeqOf(reg, party);
            LogAssert.Expect(LogType.Warning, new Regex(FROZEN_PATTERN));

            reg.AdvanceRrNextIndex(party);

            Assert.AreEqual(0, reg.GetRrNextIndex(party));
            Assert.AreEqual(seq, SeqOf(reg, party));
        }

        [Test]
        public void test_leader_transfer_one_member_forming_party_returns_false_and_logs()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            LogAssert.Expect(LogType.Log, new Regex(NO_CANDIDATE_PATTERN));

            Assert.IsFalse(reg.TransferLeadership(party));

            Assert.AreEqual(0, State(reg, party).LeaderIndex);
        }

        [Test]
        public void test_leader_cursor_pin_removing_first_slot_does_not_shift_cursor()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 4);
            reg.SetRrNextIndexForTests(party, 2);

            reg.RemoveMember(party, Ch(1), out PartyID _);

            // No shift by design: the GDD only clamps, so the cursor now points at the former index-3 member.
            Assert.AreEqual(2, reg.GetRrNextIndex(party));
            Assert.AreEqual(Ch(4), reg.GetMemberAtIndex(party, 2));
        }

        [Test]
        public void test_leader_removal_non_leader_via_unregister_does_not_notify_listener()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);

            reg.UnregisterPlayer(Ch(3), out PartyID _);

            Assert.IsEmpty(listener.Calls);
            Assert.AreEqual(2, reg.GetPartyMembers(party).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_leader_listener_cleared_with_null_receives_no_calls_and_does_not_throw()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            var listener = new RecordingListener();
            reg.SetLeaderChangeListener(listener);
            reg.SetLeaderChangeListener(null);

            Assert.DoesNotThrow(() => reg.RemoveMember(party, Ch(1), out PartyID _));
            Assert.DoesNotThrow(() => reg.TransferLeadership(party));

            Assert.IsEmpty(listener.Calls);
        }

        [Test]
        public void test_leader_listener_second_registration_replaces_the_first()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 2);
            var first = new RecordingListener();
            var second = new RecordingListener();
            reg.SetLeaderChangeListener(first);
            reg.SetLeaderChangeListener(second);

            Assert.IsTrue(reg.TransferLeadership(party));

            Assert.IsEmpty(first.Calls);
            Assert.AreEqual(new[] { "changing", "changed" }, second.Calls.ToArray());
        }

        [Test]
        public void test_leader_notice_constructor_rejects_null_arguments()
        {
            PartyRegistry reg = NewRegistry();
            var elig = new PartyXpEligibility(reg, new FakeLevels(), Config());
            var sink = new RecordingSink();

            Assert.Throws<ArgumentNullException>(() => new PartyXpIneligibleNotifier(null, elig, sink));
            Assert.Throws<ArgumentNullException>(() => new PartyXpIneligibleNotifier(reg, null, sink));
            Assert.Throws<ArgumentNullException>(() => new PartyXpIneligibleNotifier(reg, elig, null));
        }

        // ---------------------------------------------------------------- AC-8

        [Test]
        public void test_leader_service_registry_is_a_party_service_and_reads_match()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            IPartyService service = reg;

            Assert.AreEqual(party, service.GetPartyID(Ch(2)));
            Assert.AreEqual(3, service.GetPartyMembers(party).Count);
            Assert.AreEqual(Ch(2), service.GetMemberAtIndex(party, 1));
            Assert.AreEqual(CharacterID.Invalid, service.GetMemberAtIndex(party, 3));
            Assert.AreEqual(0, service.GetRrNextIndex(party));
            service.AdvanceRrNextIndex(party);
            Assert.AreEqual(1, service.GetRrNextIndex(party));
        }

        [Test]
        public void test_leader_service_unknown_party_and_character_are_safe()
        {
            PartyRegistry reg = NewRegistry();
            IPartyService service = reg;
            var unknown = new PartyID(UNKNOWN_PARTY_RAW);

            Assert.AreEqual(PartyID.Uninitialized, service.GetPartyID(Ch(55)));
            Assert.AreEqual(0, service.GetPartyMembers(unknown).Count);
            Assert.AreEqual(CharacterID.Invalid, service.GetMemberAtIndex(unknown, 0));
            Assert.AreEqual(0, service.GetRrNextIndex(unknown));
            Assert.DoesNotThrow(() => service.AdvanceRrNextIndex(unknown));
            Assert.IsTrue(service.IsMemberConnected(Ch(55)));
        }

        [TestCase(PartyMemberStatus.Online, true)]
        [TestCase(PartyMemberStatus.Ghost, false)]
        [TestCase(PartyMemberStatus.OutOfZone, true)]
        [TestCase(PartyMemberStatus.Dead, true)]
        public void test_leader_service_is_member_connected_false_only_for_ghost(PartyMemberStatus status,
            bool expected)
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            reg.SetMemberStatus(Ch(1), status);

            Assert.AreEqual(expected, reg.IsMemberConnected(Ch(1)));
        }

        [Test]
        public void test_leader_service_cursor_read_and_advance_allocate_zero_bytes()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = MakeParty(reg, 3);
            reg.AdvanceRrNextIndex(party);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < LOOP_ITERATIONS; i++)
            {
                reg.GetRrNextIndex(party);
                reg.AdvanceRrNextIndex(party);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
        }
    }
}
