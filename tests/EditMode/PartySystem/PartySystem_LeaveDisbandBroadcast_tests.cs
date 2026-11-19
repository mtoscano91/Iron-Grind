using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 008: leave, disband and the state broadcast (AC-1 leave, AC-2 disband,
    /// AC-3 auto-disband, AC-4 Disbanding rules, AC-5 broadcast, AC-6 same tick as a kill, AC-7 death rows, AC-8
    /// intake). Time is explicit ticks.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_LeaveDisbandBroadcast_Tests
    {
        private const uint TICK = 1000u;
        private const uint DISBAND_TICKS = 200u;
        private const int LEVEL_LEADER = 30;
        private const int LEVEL_OUT_OF_GATE = 36;
        private const int XP_BASE = 1000;
        private const int XP_PARTY_OF_3 = 800;
        private const int XP_PARTY_OF_2 = 900;
        private const uint ENTITY_BASE = 1000u;
        private const uint A = 1u;
        private const uint B = 2u;
        private const uint C = 3u;
        private const uint D = 4u;
        private const uint X = 10u;
        private const uint UNKNOWN = 99u;
        private const uint CLIENT_A = 7u;
        private const uint INTAKE_SENDER = 50u;
        private const int ABSENT = -1;
        private const uint FIRST_QUEUE_CHARACTER = 1000u;
        private const uint OVERFLOW_CHARACTER = 5000u;
        private const uint LATE_CLEANUP_OFFSET = 205u;
        private const int BYTE_MAX = 255;
        private const int LEVEL_OVER_BYTE = 300;
        private const int LEVEL_NEGATIVE = -5;
        private const int FLUSHES_AFTER_FREE = 3;

        private enum Kind
        {
            State,
            Disbanded,
            Result,
        }

        private sealed class Event
        {
            public Kind Kind;
            public CharacterID Recipient;
            public uint PartyId;
            public PartyStateUpdate Update;
            public PartyInviteResultCode Code;
            public uint Target;
        }

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            private readonly Dictionary<EntityID, int> _levels = new Dictionary<EntityID, int>();

            public void Set(EntityID entity, int level) => _levels[entity] = level;

            public void Unset(EntityID entity) => _levels.Remove(entity);

            public bool TryGetLevel(EntityID entity, out int level) => _levels.TryGetValue(entity, out level);
        }

        private sealed class FakeNames : ICharacterNameLookup
        {
            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                name = string.Empty;
                return false;
            }
        }

        private sealed class XpRecorder : IAutoAttackExperienceSink
        {
            public readonly List<(EntityID Entity, int Amount)> Calls = new List<(EntityID, int)>();

            public void AddExperience(EntityID attacker, int amount) => Calls.Add((attacker, amount));
        }

        /// <summary>One shared event log for both outbound seams, so the order across messages is checkable.</summary>
        private sealed class RecordingSinks : IPartyStateMessageSink, IPartyInviteMessageSink
        {
            public readonly List<Event> Events = new List<Event>();

            public void SendStateUpdate(CharacterID recipient, in PartyStateUpdate message)
                => Events.Add(new Event { Kind = Kind.State, Recipient = recipient, PartyId = message.PartyId, Update = message });

            public void SendDisbanded(CharacterID recipient, PartyID partyId)
                => Events.Add(new Event { Kind = Kind.Disbanded, Recipient = recipient, PartyId = partyId.RawValue });

            public void SendInviteReceived(CharacterID recipient, in PartyInviteReceived message) { }

            public void SendInviteResult(CharacterID recipient, PartyInviteResultCode code, CharacterID target)
                => Events.Add(new Event { Kind = Kind.Result, Recipient = recipient, Code = code, Target = target.RawValue });

            public int IndexOf(Kind kind, uint recipient)
            {
                for (int i = 0; i < Events.Count; i++)
                {
                    if (Events[i].Kind == kind && Events[i].Recipient == Ch(recipient)) return i;
                }

                return ABSENT;
            }

            public int Count(Kind kind, uint recipient)
            {
                int n = 0;
                foreach (Event e in Events)
                {
                    if (e.Kind == kind && e.Recipient == Ch(recipient)) n++;
                }

                return n;
            }

            public int Count(Kind kind)
            {
                int n = 0;
                foreach (Event e in Events)
                {
                    if (e.Kind == kind) n++;
                }

                return n;
            }

            public PartyStateUpdate LastState(uint recipient)
            {
                for (int i = Events.Count - 1; i >= 0; i--)
                {
                    if (Events[i].Kind == Kind.State && Events[i].Recipient == Ch(recipient)) return Events[i].Update;
                }

                throw new InvalidOperationException("no state update to " + recipient);
            }
        }

        private PartyRegistry _registry;
        private FakeLevels _levels;
        private RecordingSinks _sinks;
        private PartyXpEligibility _eligibility;
        private PartyBroadcastStep _broadcast;
        private PartyMembershipStep _membership;

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(ENTITY_BASE + n);

        private static PartySystemConfigValues Config() => PartySystem_InviteFlow_Tests.Config();

        [SetUp]
        public void SetUp()
        {
            _registry = new PartyRegistry(Config());
            _levels = new FakeLevels();
            _sinks = new RecordingSinks();
            _eligibility = new PartyXpEligibility(_registry, _levels, Config());
            _broadcast = new PartyBroadcastStep(_registry, _levels, _eligibility, _sinks);
            _membership = new PartyMembershipStep(_registry, _sinks, _sinks, _broadcast, Config());
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        private void Reg(uint n, int level = LEVEL_LEADER)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(n), En(n), out _));
            _levels.Set(En(n), level);
        }

        /// <summary>Registers the leader and the members, builds the party and drains the first broadcast.</summary>
        private PartyID MakeParty(uint leader, params uint[] members)
        {
            Reg(leader);
            foreach (uint member in members)
            {
                Reg(member);
            }

            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(leader), out PartyID id));
            foreach (uint member in members)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(id, Ch(member)));
            }

            _broadcast.Flush();
            _sinks.Events.Clear();
            return id;
        }

        private void Run(uint tick)
        {
            _membership.ApplyQueued(tick);
            _broadcast.Flush();
        }

        private PartyState Party(PartyID id)
        {
            Assert.IsTrue(_registry.TryGetParty(id, out PartyState party), "party " + id + " exists");
            return party;
        }

        private PartyState PartyOf(uint n) => Party(_registry.GetPartyID(Ch(n)));

        private bool IsSolo(uint n) => PartyOf(n).IsSoloParty;

        // ── AC-1 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_leave_member_gets_solo_id_and_others_get_list_without_leaver()
        {
            PartyID party = MakeParty(A, B, C);
            uint lastSeq = PartyOf(A).SequenceId;
            _membership.EnqueueLeave(Ch(B));

            Run(TICK);

            PartyID soloB = _registry.GetPartyID(Ch(B));
            Assert.AreNotEqual(PartyID.Uninitialized, soloB);
            Assert.AreNotEqual(party, soloB);
            Assert.AreNotEqual(_registry.GetPartyID(Ch(A)), soloB);
            Assert.AreEqual(party, _registry.GetPartyID(Ch(A)));
            Assert.AreEqual(3, _sinks.Events.Count);
            foreach (uint remaining in new[] { A, C })
            {
                Assert.AreEqual(1, _sinks.Count(Kind.State, remaining));
                PartyStateUpdate update = _sinks.LastState(remaining);
                Assert.AreEqual(2, update.MemberCount);
                Assert.AreEqual(A, update.Member0.CharacterId);
                Assert.AreEqual(C, update.Member1.CharacterId);
                Assert.AreEqual(0u, update.Member2.CharacterId);
                Assert.Greater(update.SequenceId, lastSeq);
            }

            PartyStateUpdate solo = _sinks.LastState(B);
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.IsTrue(solo.IsSoloParty);
            Assert.AreEqual(1, solo.MemberCount);
            Assert.AreEqual(soloB.RawValue, solo.PartyId);
            Assert.AreEqual(B, solo.Member0.CharacterId);
            Assert.AreEqual(En(B).RawValue, solo.Member0.EntityId);
            Assert.AreEqual(LEVEL_LEADER, solo.Member0.Level);
            Assert.AreEqual(PartyMemberStatus.Online, solo.Member0.Status);
            Assert.Greater(solo.SequenceId, 0u);
        }

        [Test]
        public void test_leave_disband_leave_from_solo_player_changes_nothing_and_sends_nothing()
        {
            Reg(A);
            PartyID before = _registry.GetPartyID(Ch(A));
            _membership.EnqueueLeave(Ch(A));

            Run(TICK);

            Assert.AreEqual(before, _registry.GetPartyID(Ch(A)));
            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_leave_is_queued_until_the_step_applies_it()
        {
            PartyID party = MakeParty(A, B, C);

            _membership.EnqueueLeave(Ch(B));

            Assert.AreEqual(1, _membership.QueuedCount);
            Assert.AreEqual(party, _registry.GetPartyID(Ch(B)));
            Assert.AreEqual(0, _sinks.Events.Count);
        }

        // ── AC-2 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_leader_disband_sends_disbanded_then_cleans_up_at_exactly_200_ticks()
        {
            PartyID party = MakeParty(A, B, C);
            Assert.AreEqual((int)DISBAND_TICKS, Config().DisbandTimeoutTicks);
            _membership.EnqueueDisband(Ch(A));

            Run(TICK);

            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(3, _sinks.Count(Kind.Disbanded));
            foreach (Event e in _sinks.Events)
            {
                Assert.AreEqual(Kind.Disbanded, e.Kind);
                Assert.AreEqual(party.RawValue, e.PartyId);
            }

            Assert.AreEqual(0, _sinks.Count(Kind.State));
            _sinks.Events.Clear();

            Run(TICK + DISBAND_TICKS - 1u);

            Assert.AreEqual(0, _sinks.Events.Count);
            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);

            Run(TICK + DISBAND_TICKS);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(3, _sinks.Count(Kind.State));
            foreach (uint member in new[] { A, B, C })
            {
                Assert.IsTrue(IsSolo(member));
                Assert.IsTrue(_sinks.LastState(member).IsSoloParty);
                Assert.AreEqual(_registry.GetPartyID(Ch(member)).RawValue, _sinks.LastState(member).PartyId);
            }
        }

        [Test]
        public void test_leave_disband_disband_notice_precedes_the_solo_update_for_every_recipient()
        {
            MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);

            Run(TICK + DISBAND_TICKS);

            foreach (uint member in new[] { A, B, C })
            {
                Assert.Less(_sinks.IndexOf(Kind.Disbanded, member), _sinks.IndexOf(Kind.State, member),
                    "PartyDisbanded before the solo update for " + member);
            }
        }

        [Test]
        public void test_leave_disband_non_leader_disband_answers_not_leader_with_target_zero_and_changes_nothing()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(B));

            Run(TICK);

            Assert.AreEqual(1, _sinks.Events.Count);
            Assert.AreEqual(Kind.Result, _sinks.Events[0].Kind);
            Assert.AreEqual(Ch(B), _sinks.Events[0].Recipient);
            Assert.AreEqual(PartyInviteResultCode.NotLeader, _sinks.Events[0].Code);
            Assert.AreEqual(0u, _sinks.Events[0].Target);
            Assert.AreEqual(PartyLifecycleState.Active, Party(party).State);
            Assert.AreEqual(0, _membership.PendingCleanupCount);
        }

        [Test]
        public void test_leave_disband_second_disband_during_disbanding_changes_nothing_and_keeps_the_cleanup_tick()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();
            _membership.EnqueueDisband(Ch(A));

            Run(TICK + 50u);

            Assert.AreEqual(0, _sinks.Events.Count);
            Assert.AreEqual(1, _membership.PendingCleanupCount);
            Run(TICK + DISBAND_TICKS);
            Assert.IsFalse(_registry.TryGetParty(party, out _));
        }

        [Test]
        public void test_leave_disband_disband_from_solo_player_is_ignored()
        {
            Reg(A);
            _membership.EnqueueDisband(Ch(A));

            Run(TICK);

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_disband_notice_goes_to_online_and_dead_but_not_ghost_or_out_of_zone()
        {
            PartyID party = MakeParty(A, B, C, D);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
            _registry.SetMemberStatus(Ch(D), PartyMemberStatus.OutOfZone);
            _broadcast.Flush();
            _sinks.Events.Clear();
            _membership.EnqueueDisband(Ch(A));

            Run(TICK);

            Assert.AreEqual(4, Party(party).MemberCount);
            foreach (Event e in _sinks.Events)
            {
                Assert.AreEqual(party.RawValue, e.PartyId);
            }

            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, A));
            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, B));
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded, C));
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded, D));
        }

        // ── AC-3 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_leave_that_leaves_one_member_disbands_at_once_with_notice_then_solo_update()
        {
            PartyID party = MakeParty(A, B);
            _membership.EnqueueLeave(Ch(B));

            Run(TICK);

            Assert.IsFalse(_registry.TryGetParty(party, out _), "no wait in Disbanding");
            Assert.IsTrue(IsSolo(A));
            Assert.IsTrue(IsSolo(B));
            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, A));
            Assert.AreEqual(party.RawValue, _sinks.Events[_sinks.IndexOf(Kind.Disbanded, A)].PartyId);
            Assert.Less(_sinks.IndexOf(Kind.Disbanded, A), _sinks.IndexOf(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded, B));
            foreach (Event e in _sinks.Events)
            {
                Assert.AreNotEqual(party.RawValue, e.Kind == Kind.State ? e.PartyId : 0u, "no update for the freed party");
            }

            Assert.AreEqual(0, _membership.PendingCleanupCount);
        }

        // ── AC-4 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_disbanding_party_sends_no_state_update_while_waiting()
        {
            MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();

            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            Run(TICK + 1u);
            Run(TICK + DISBAND_TICKS - 1u);

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_member_leaving_a_disbanding_party_gets_solo_id_and_update_at_once()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();
            _membership.EnqueueLeave(Ch(C));

            Run(TICK + 1u);

            Assert.IsTrue(IsSolo(C));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            Assert.AreEqual(1, _sinks.Events.Count);
            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(2, Party(party).MemberCount);
        }

        [Test]
        public void test_leave_disband_leave_that_leaves_one_member_of_a_disbanding_party_finishes_cleanup_without_second_notice()
        {
            PartyID party = MakeParty(A, B);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();
            _membership.EnqueueLeave(Ch(B));

            Run(TICK + 1u);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.IsTrue(IsSolo(A));
            Assert.IsTrue(IsSolo(B));
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(0, _membership.PendingCleanupCount);
        }

        [Test]
        public void test_leave_disband_kill_during_disbanding_still_pays_every_member()
        {
            PartyID party = MakeParty(A, B, C);
            var xp = new XpRecorder();
            var distributor = new PartyExperienceDistributor(xp, _registry, _eligibility, Config());
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);

            distributor.AddExperience(En(A), XP_BASE);

            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(3, xp.Calls.Count);
            Assert.AreEqual(XP_PARTY_OF_3, xp.Calls[0].Amount);
        }

        [Test]
        public void test_leave_disband_invite_from_the_leader_of_a_disbanding_party_is_rejected_not_leader()
        {
            MakeParty(A, B, C);
            Reg(X);
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();

            invites.HandleInvite(En(A), Ch(X), TICK + 1u);

            Assert.AreEqual(1, _sinks.Events.Count);
            Assert.AreEqual(Ch(A), _sinks.Events[0].Recipient);
            Assert.AreEqual(PartyInviteResultCode.NotLeader, _sinks.Events[0].Code);
            Assert.AreEqual(X, _sinks.Events[0].Target);
            Assert.IsFalse(invites.HasPending(Ch(X)));
        }

        [Test]
        public void test_leave_disband_accept_into_a_party_that_began_disbanding_answers_expired_to_the_target()
        {
            PartyID party = MakeParty(A, B, C);
            Reg(X);
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            invites.HandleInvite(En(A), Ch(X), TICK);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();

            invites.HandleResponse(En(X), Ch(A), true, TICK + 1u);

            Assert.AreEqual(1, _sinks.Count(Kind.Result, X));
            Assert.AreEqual(PartyInviteResultCode.Expired, _sinks.Events[0].Code);
            Assert.AreEqual(A, _sinks.Events[0].Target);
            Assert.IsTrue(IsSolo(X));
            Assert.AreEqual(3, Party(party).MemberCount);
            Assert.IsFalse(invites.HasPending(Ch(X)), "the Expired answer still resolves the pending invite");
        }

        // ── AC-5 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_join_and_cursor_change_in_one_tick_send_one_update_with_the_current_sequence()
        {
            PartyID party = MakeParty(A, B);
            Reg(C);
            uint lastSeq = PartyOf(A).SequenceId;
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            invites.HandleInvite(En(A), Ch(C), TICK);
            invites.HandleResponse(En(C), Ch(A), true, TICK);
            _registry.AdvanceRrNextIndex(party);
            _sinks.Events.Clear();

            Run(TICK);

            foreach (uint member in new[] { A, B, C })
            {
                Assert.AreEqual(1, _sinks.Count(Kind.State, member));
            }

            PartyStateUpdate update = _sinks.LastState(A);
            Assert.AreEqual(3, update.MemberCount);
            Assert.AreEqual(C, update.Member2.CharacterId);
            Assert.AreEqual(PartyOf(A).SequenceId, update.SequenceId);
            Assert.Greater(update.SequenceId, lastSeq);
            Assert.AreEqual(1, _sinks.Count(Kind.State, C), "several changes share one message");
            Assert.AreEqual(PartyOf(A).RrNextIndex, update.RrNextIndex);
        }

        [Test]
        public void test_leave_disband_idle_tick_sends_nothing()
        {
            MakeParty(A, B, C);

            Run(TICK);
            Run(TICK + 1u);

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_snapshot_carries_slot_level_status_entity_and_xp_eligibility()
        {
            Reg(A);
            Reg(B, LEVEL_OUT_OF_GATE);
            Reg(C);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(A), out PartyID party));
            _registry.TryAddMember(party, Ch(B));
            _registry.TryAddMember(party, Ch(C));
            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Dead);

            _broadcast.Flush();

            PartyStateUpdate update = _sinks.LastState(A);
            Assert.AreEqual(party.RawValue, update.PartyId);
            Assert.IsFalse(update.IsSoloParty);
            Assert.AreEqual(0, update.LeaderIndex);
            Assert.AreEqual(A, update.Member0.CharacterId);
            Assert.AreEqual(En(A).RawValue, update.Member0.EntityId);
            Assert.AreEqual(LEVEL_LEADER, update.Member0.Level);
            Assert.AreEqual(PartyMemberStatus.Online, update.Member0.Status);
            Assert.IsTrue(update.Member0.XpEligible);
            Assert.AreEqual(LEVEL_OUT_OF_GATE, update.Member1.Level);
            Assert.IsFalse(update.Member1.XpEligible, "outside the level gate");
            Assert.AreEqual(PartyMemberStatus.Dead, update.Member2.Status);
            Assert.IsFalse(update.Member2.XpEligible, "Dead is not eligible");
            Assert.AreEqual(0u, update.Member3.CharacterId);
        }

        [Test]
        public void test_leave_disband_update_goes_to_online_and_dead_members_but_not_ghost_or_out_of_zone()
        {
            PartyID party = MakeParty(A, B, C, D);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
            _registry.SetMemberStatus(Ch(D), PartyMemberStatus.OutOfZone);

            _broadcast.Flush();

            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(0, _sinks.Count(Kind.State, C));
            Assert.AreEqual(0, _sinks.Count(Kind.State, D));
            Assert.AreEqual(party.RawValue, _sinks.LastState(A).PartyId);
            Assert.AreEqual(4, _sinks.LastState(A).MemberCount, "Ghost and OutOfZone members stay in the snapshot");
            Assert.AreEqual(PartyMemberStatus.Ghost, _sinks.LastState(A).Member2.Status);
            Assert.AreEqual(PartyMemberStatus.OutOfZone, _sinks.LastState(A).Member3.Status);
        }

        [Test]
        public void test_leave_disband_a_story_007_accept_is_broadcast_to_old_and_new_members()
        {
            MakeParty(A, B);
            Reg(C);
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            invites.HandleInvite(En(A), Ch(C), TICK);
            invites.HandleResponse(En(C), Ch(A), true, TICK);
            _sinks.Events.Clear();

            Run(TICK);

            Assert.AreEqual(3, _sinks.Count(Kind.State));
            Assert.AreEqual(3, _sinks.LastState(C).MemberCount);
        }

        [Test]
        public void test_leave_disband_party_created_by_an_accept_is_broadcast_to_both_members()
        {
            Reg(A);
            Reg(B);
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            invites.HandleInvite(En(A), Ch(B), TICK);
            invites.HandleResponse(En(B), Ch(A), true, TICK);

            Run(TICK);

            Assert.AreEqual(2, _sinks.Count(Kind.State));
            Assert.IsFalse(_sinks.LastState(A).IsSoloParty);
            Assert.AreEqual(2, _sinks.LastState(B).MemberCount);
        }

        // ── AC-6 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_leaver_receives_the_same_tick_kill_xp_then_is_removed()
        {
            MakeParty(A, B, C);
            var xp = new XpRecorder();
            var distributor = new PartyExperienceDistributor(xp, _registry, _eligibility, Config());
            _membership.EnqueueLeave(Ch(B));

            distributor.AddExperience(En(A), XP_BASE);
            Run(TICK);
            distributor.AddExperience(En(A), XP_BASE);

            Assert.AreEqual(5, xp.Calls.Count);
            Assert.AreEqual((En(A), XP_PARTY_OF_3), xp.Calls[0]);
            Assert.AreEqual((En(B), XP_PARTY_OF_3), xp.Calls[1]);
            Assert.AreEqual((En(C), XP_PARTY_OF_3), xp.Calls[2]);
            Assert.AreEqual((En(A), XP_PARTY_OF_2), xp.Calls[3]);
            Assert.AreEqual((En(C), XP_PARTY_OF_2), xp.Calls[4]);
            Assert.IsTrue(IsSolo(B));
        }

        // ── AC-7 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_leave_disband_dead_member_receives_cursor_updates_two_then_zero()
        {
            PartyID party = MakeParty(A, B, C);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _registry.SetRrNextIndexForTests(party, 1);
            _broadcast.Flush();
            _sinks.Events.Clear();

            _registry.AdvanceRrNextIndex(party);
            Run(TICK);
            byte first = _sinks.LastState(B).RrNextIndex;
            _registry.AdvanceRrNextIndex(party);
            Run(TICK + 1u);
            byte second = _sinks.LastState(B).RrNextIndex;

            Assert.AreEqual(2, first);
            Assert.AreEqual(0, second);
            Assert.AreEqual(2, _sinks.Count(Kind.State, B));
            Assert.AreEqual(PartyMemberStatus.Dead, _sinks.LastState(B).Member1.Status);
        }

        [Test]
        public void test_leave_disband_party_disbanding_while_a_member_is_dead_gives_it_a_solo_id_and_both_messages()
        {
            MakeParty(A, B, C);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _broadcast.Flush();
            _sinks.Events.Clear();
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);

            Run(TICK + DISBAND_TICKS);
            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Online);

            Assert.IsTrue(IsSolo(B), "IsSoloParty on respawn");
            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, B));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.Less(_sinks.IndexOf(Kind.Disbanded, B), _sinks.IndexOf(Kind.State, B));
            Assert.AreEqual(PartyMemberStatus.Dead, _sinks.LastState(B).Member0.Status);
        }

        // ── AC-8 ───────────────────────────────────────────────────────────────────────────

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<InboundRequestDescriptor> Descriptors = new List<InboundRequestDescriptor>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
                => Descriptors.Add(descriptor);

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private PartyLeaveDisbandClientRequestHandlers NewHandlers()
            => new PartyLeaveDisbandClientRequestHandlers(_registry, _membership);

        /// <summary>The sender is registered and sits in a party of three (A, the sender, B); the sender leads it or not.</summary>
        private InboundDispatchHarness NewIntakeHarness(bool senderLeads = false)
        {
            Assert.AreEqual(PartyRegistryResult.Ok,
                _registry.RegisterPlayer(Ch(INTAKE_SENDER), InboundDispatchHarness.EntityOf(CLIENT_A), out _));
            Reg(A);
            Reg(B);
            CharacterID leader = senderLeads ? Ch(INTAKE_SENDER) : Ch(A);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(leader, out PartyID party));
            foreach (CharacterID member in new[] { Ch(A), Ch(INTAKE_SENDER), Ch(B) })
            {
                if (member != leader)
                {
                    Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, member));
                }
            }

            _broadcast.Flush();
            _sinks.Events.Clear();
            var harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            PartyInboundRegistration.RegisterLeaveAndDisband(harness.Dispatcher, NewHandlers());
            harness.Dispatcher.Seal();
            harness.AddClient(CLIENT_A);
            return harness;
        }

        [Test]
        public void test_leave_disband_registration_declares_both_descriptors_unheld_body_zero_and_unlimited()
        {
            var recorder = new RecordingDispatcher();

            PartyInboundRegistration.RegisterLeaveAndDisband(recorder, NewHandlers());

            Assert.AreEqual(2, recorder.Descriptors.Count);
            Assert.AreEqual(0xE175, recorder.Descriptors[0].MessageTypeId);
            Assert.AreEqual(RpcTypeTag.PartyLeaveRequest, recorder.Descriptors[0].RpcTypeTag);
            Assert.AreEqual(0xE176, recorder.Descriptors[1].MessageTypeId);
            Assert.AreEqual(RpcTypeTag.PartyDisbandRequest, recorder.Descriptors[1].RpcTypeTag);
            Assert.AreEqual(0, recorder.Descriptors[0].MaxBodyBytes);
            Assert.AreEqual(0, recorder.Descriptors[1].MaxBodyBytes);
            Assert.IsFalse(recorder.Descriptors[0].HeldDuringIrreversibleWrite);
            Assert.IsFalse(recorder.Descriptors[1].HeldDuringIrreversibleWrite);
            Assert.AreEqual(13, (int)RpcTypeTag.PartyLeaveRequest);
            Assert.AreEqual(14, (int)RpcTypeTag.PartyDisbandRequest);
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.PartyLeaveRequest));
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.PartyDisbandRequest));
        }

        [Test]
        public void test_leave_disband_empty_leave_body_through_the_dispatcher_queues_and_then_applies_the_leave()
        {
            InboundDispatchHarness harness = NewIntakeHarness();

            harness.SendRequest(CLIENT_A, PartyLeaveRequest.MessageTypeId, 1u, new byte[0]);
            harness.Dispatcher.DispatchTick(TICK);

            Assert.AreEqual(1, _membership.QueuedCount);
            Assert.IsFalse(IsSolo(INTAKE_SENDER), "not applied before the step runs");
            _membership.ApplyQueued(TICK);
            Assert.IsTrue(IsSolo(INTAKE_SENDER));
            Assert.AreEqual(2, PartyOf(A).MemberCount);
        }

        [Test]
        public void test_leave_disband_empty_disband_body_through_the_dispatcher_queues_and_then_starts_the_disband()
        {
            InboundDispatchHarness harness = NewIntakeHarness(true);

            harness.SendRequest(CLIENT_A, PartyDisbandRequest.MessageTypeId, 1u, new byte[0]);
            harness.Dispatcher.DispatchTick(TICK);

            Assert.AreEqual(1, _membership.QueuedCount);
            _membership.ApplyQueued(TICK);
            Assert.AreEqual(PartyLifecycleState.Disbanding, PartyOf(INTAKE_SENDER).State);
            Assert.AreEqual(1, _membership.PendingCleanupCount);
        }

        [Test]
        public void test_leave_disband_leave_sent_through_the_dispatcher_reaches_the_sink_after_apply_and_flush()
        {
            InboundDispatchHarness harness = NewIntakeHarness();

            harness.SendRequest(CLIENT_A, PartyLeaveRequest.MessageTypeId, 1u, new byte[0]);
            harness.Dispatcher.DispatchTick(TICK);
            Run(TICK);

            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(2, _sinks.LastState(A).MemberCount);
            Assert.AreEqual(1, _sinks.Count(Kind.State, INTAKE_SENDER));
            Assert.IsTrue(_sinks.LastState(INTAKE_SENDER).IsSoloParty);
        }

        [Test]
        public void test_leave_disband_one_byte_leave_body_is_rejected_at_intake_before_the_handler()
        {
            InboundDispatchHarness harness = NewIntakeHarness();
            LogAssert.Expect(LogType.Warning, new Regex("body longer than MaxBodyBytes"));

            harness.SendRequest(CLIENT_A, PartyLeaveRequest.MessageTypeId, 1u, new byte[1]);
            harness.Dispatcher.DispatchTick(TICK);

            Assert.AreEqual(0, _membership.QueuedCount);
        }

        [Test]
        public void test_leave_disband_one_byte_disband_body_is_rejected_at_intake_before_the_handler()
        {
            InboundDispatchHarness harness = NewIntakeHarness();
            LogAssert.Expect(LogType.Warning, new Regex("body longer than MaxBodyBytes"));

            harness.SendRequest(CLIENT_A, PartyDisbandRequest.MessageTypeId, 1u, new byte[1]);
            harness.Dispatcher.DispatchTick(TICK);

            Assert.AreEqual(0, _membership.QueuedCount);
        }

        [Test]
        public void test_leave_disband_request_from_an_unregistered_sender_is_ignored_silently()
        {
            PartyLeaveDisbandClientRequestHandlers handlers = NewHandlers();
            var context = new InboundRequestContext(CLIENT_A, En(UNKNOWN), Ch(UNKNOWN), PartyLeaveRequest.MessageTypeId,
                TICK, TICK, false);

            handlers.HandleLeave(in context, ReadOnlySpan<byte>.Empty);
            handlers.HandleDisband(in context, ReadOnlySpan<byte>.Empty);
            Run(TICK);

            Assert.AreEqual(0, _membership.QueuedCount);
            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_duplicate_requests_in_one_tick_collapse_into_one()
        {
            MakeParty(A, B, C);

            _membership.EnqueueLeave(Ch(B));
            _membership.EnqueueLeave(Ch(B));

            Assert.AreEqual(1, _membership.QueuedCount);
        }

        // ── Queue, level clamp, broadcast and same-tick edge cases ─────────────────────────

        private void FillQueueToCapacity()
        {
            for (uint i = 0; i < PartyMembershipStep.REQUEST_CAPACITY / 2; i++)
            {
                _membership.EnqueueLeave(Ch(FIRST_QUEUE_CHARACTER + i));
                _membership.EnqueueDisband(Ch(FIRST_QUEUE_CHARACTER + i));
            }
        }

        [Test]
        public void test_leave_disband_queue_overflow_drops_requests_with_one_warning_and_the_flag_stays_latched()
        {
            FillQueueToCapacity();
            Assert.AreEqual(PartyMembershipStep.REQUEST_CAPACITY, _membership.QueuedCount);
            LogAssert.Expect(LogType.Warning, new Regex("request queue full"));

            _membership.EnqueueLeave(Ch(OVERFLOW_CHARACTER));
            _membership.EnqueueDisband(Ch(OVERFLOW_CHARACTER));

            Assert.AreEqual(PartyMembershipStep.REQUEST_CAPACITY, _membership.QueuedCount);

            // Pinned: the warning flag is never reset, so a later overflow (after ApplyQueued) logs nothing.
            _membership.ApplyQueued(TICK);
            Assert.AreEqual(0, _membership.QueuedCount);
            FillQueueToCapacity();
            _membership.EnqueueLeave(Ch(OVERFLOW_CHARACTER));
            Assert.AreEqual(PartyMembershipStep.REQUEST_CAPACITY, _membership.QueuedCount);
        }

        [Test]
        public void test_leave_disband_leave_and_disband_from_the_same_character_are_two_queued_entries()
        {
            MakeParty(A, B, C);

            _membership.EnqueueLeave(Ch(A));
            _membership.EnqueueDisband(Ch(A));

            Assert.AreEqual(2, _membership.QueuedCount);
        }

        [Test]
        public void test_leave_disband_dedupe_resets_after_apply_queued()
        {
            MakeParty(A, B, C);
            _membership.EnqueueLeave(Ch(B));
            _membership.ApplyQueued(TICK);
            Assert.AreEqual(0, _membership.QueuedCount);

            _membership.EnqueueLeave(Ch(B));
            _membership.EnqueueLeave(Ch(B));

            Assert.AreEqual(1, _membership.QueuedCount);
        }

        [TestCase(LEVEL_OVER_BYTE, BYTE_MAX)]
        [TestCase(LEVEL_NEGATIVE, 0)]
        [TestCase(BYTE_MAX, BYTE_MAX)]
        public void test_leave_disband_snapshot_level_is_clamped_to_a_byte(int level, int expected)
        {
            Reg(A, level);
            Reg(B);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(A), out PartyID party));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(B)));

            _broadcast.Flush();

            Assert.AreEqual(expected, _sinks.LastState(B).Member0.Level);
        }

        [Test]
        public void test_leave_disband_snapshot_level_is_zero_when_the_level_is_unreadable()
        {
            Reg(A);
            Reg(B);
            _levels.Unset(En(A));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(A), out PartyID party));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(B)));

            _broadcast.Flush();

            Assert.AreEqual(0, _sinks.LastState(B).Member0.Level);
        }

        [Test]
        public void test_leave_disband_solo_update_is_dropped_when_the_leaver_joins_a_real_party_before_the_flush()
        {
            MakeParty(A, B, C);
            var invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            _membership.EnqueueLeave(Ch(B));
            _membership.ApplyQueued(TICK);
            invites.HandleInvite(En(A), Ch(B), TICK);
            invites.HandleResponse(En(B), Ch(A), true, TICK);
            Assert.IsFalse(IsSolo(B));
            _sinks.Events.Clear();

            _broadcast.Flush();

            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.IsFalse(_sinks.LastState(B).IsSoloParty);
            Assert.AreEqual(3, _sinks.LastState(B).MemberCount);
        }

        [Test]
        public void test_leave_disband_apply_queued_alone_sends_the_notice_and_the_flush_sends_the_solo_state()
        {
            MakeParty(A, B);
            _membership.EnqueueLeave(Ch(B));

            _membership.ApplyQueued(TICK);

            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, A));
            Assert.AreEqual(0, _sinks.Count(Kind.State));
            _broadcast.Flush();
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.IsTrue(_sinks.LastState(A).IsSoloParty);
            Assert.IsTrue(_sinks.LastState(B).IsSoloParty);
        }

        [Test]
        public void test_leave_disband_leader_leave_then_disband_in_one_tick_ignores_the_disband()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueLeave(Ch(A));
            _membership.EnqueueDisband(Ch(A));

            Run(TICK);

            Assert.IsTrue(IsSolo(A));
            Assert.AreEqual(PartyLifecycleState.Active, Party(party).State);
            Assert.AreEqual(2, Party(party).MemberCount);
            Assert.AreEqual(0, _membership.PendingCleanupCount);
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded));
            Assert.AreEqual(0, _sinks.Count(Kind.Result));
            Assert.AreEqual(3, _sinks.Count(Kind.State));
            Assert.AreEqual(2, _sinks.LastState(B).MemberCount);
            Assert.IsTrue(_sinks.LastState(A).IsSoloParty);
        }

        [Test]
        public void test_leave_disband_leader_disband_then_leave_in_one_tick_leaves_a_disbanding_pair_with_cleanup_pending()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            _membership.EnqueueLeave(Ch(A));

            Run(TICK);

            Assert.IsTrue(IsSolo(A));
            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(2, Party(party).MemberCount);
            Assert.AreEqual(3, _sinks.Count(Kind.Disbanded));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State));
            Assert.AreEqual(1, _membership.PendingCleanupCount);

            _sinks.Events.Clear();
            Run(TICK + DISBAND_TICKS);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(0, _membership.PendingCleanupCount);
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded));
            Assert.AreEqual(2, _sinks.Count(Kind.State));
        }

        [Test]
        public void test_leave_disband_leader_leaving_a_disbanding_party_of_three_resolos_the_rest_at_cleanup()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();
            _membership.EnqueueLeave(Ch(A));

            Run(TICK + 1u);

            Assert.IsTrue(IsSolo(A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Events.Count);
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded));
            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(2, Party(party).MemberCount);
            _sinks.Events.Clear();

            Run(TICK + DISBAND_TICKS);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            Assert.AreEqual(0, _sinks.Count(Kind.State, A));
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded));
            Assert.IsTrue(IsSolo(B));
            Assert.IsTrue(IsSolo(C));
        }

        [Test]
        public void test_leave_disband_two_members_leaving_a_party_of_three_in_one_tick_frees_it_with_solo_updates()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueLeave(Ch(B));
            _membership.EnqueueLeave(Ch(C));

            Run(TICK);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            Assert.AreEqual(3, _sinks.Count(Kind.State));
            foreach (Event e in _sinks.Events)
            {
                if (e.Kind == Kind.State)
                {
                    Assert.AreNotEqual(party.RawValue, e.PartyId);
                    Assert.IsTrue(e.Update.IsSoloParty);
                }
            }
        }

        [Test]
        public void test_leave_disband_two_leaves_in_one_tick_from_four_send_each_remaining_member_one_update()
        {
            PartyID party = MakeParty(A, B, C, D);
            uint lastSeq = Party(party).SequenceId;
            _membership.EnqueueLeave(Ch(B));
            _membership.EnqueueLeave(Ch(C));

            Run(TICK);

            foreach (uint remaining in new[] { A, D })
            {
                Assert.AreEqual(1, _sinks.Count(Kind.State, remaining));
                Assert.AreEqual(2, _sinks.LastState(remaining).MemberCount);
                Assert.Greater(_sinks.LastState(remaining).SequenceId, lastSeq);
            }

            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
        }

        [Test]
        public void test_leave_disband_a_freed_party_is_forgotten_and_later_flushes_send_nothing()
        {
            MakeParty(A, B);
            MakeParty(C, D);
            _membership.EnqueueLeave(Ch(B));
            Run(TICK);
            _membership.EnqueueDisband(Ch(C));
            Run(TICK + 1u);
            Run(TICK + 1u + DISBAND_TICKS);
            _sinks.Events.Clear();

            for (uint i = 1; i <= FLUSHES_AFTER_FREE; i++)
            {
                Run(TICK + DISBAND_TICKS + 1u + i);
            }

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_status_only_change_sends_one_update_with_a_higher_sequence()
        {
            PartyID party = MakeParty(A, B, C, D);
            _registry.SetMemberStatus(Ch(C), PartyMemberStatus.Ghost);
            _registry.SetMemberStatus(Ch(D), PartyMemberStatus.OutOfZone);
            _broadcast.Flush();
            _sinks.Events.Clear();
            uint lastSeq = Party(party).SequenceId;

            _registry.SetMemberStatus(Ch(B), PartyMemberStatus.Dead);
            _broadcast.Flush();

            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(0, _sinks.Count(Kind.State, C));
            Assert.AreEqual(0, _sinks.Count(Kind.State, D));
            Assert.Greater(_sinks.LastState(A).SequenceId, lastSeq);
            Assert.AreEqual(PartyMemberStatus.Dead, _sinks.LastState(A).Member1.Status);
        }

        [Test]
        public void test_leave_disband_new_party_first_broadcast_matches_the_party_sequence_and_is_not_repeated()
        {
            Reg(A);
            Reg(B);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(A), out PartyID party));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(party, Ch(B)));

            _broadcast.Flush();

            Assert.AreEqual(2, _sinks.Count(Kind.State));
            Assert.AreEqual(Party(party).SequenceId, _sinks.LastState(A).SequenceId);
            Assert.AreEqual(Party(party).SequenceId, _sinks.LastState(B).SequenceId);
            _sinks.Events.Clear();

            _broadcast.Flush();

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_leave_disband_cleanup_runs_late_when_no_tick_lands_exactly_on_the_due_tick()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();

            Run(TICK + LATE_CLEANUP_OFFSET);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(3, _sinks.Count(Kind.State));
            Assert.AreEqual(0, _membership.PendingCleanupCount);
        }

        [Test]
        public void test_leave_disband_sequence_id_strictly_increases_across_three_ticked_updates()
        {
            PartyID party = MakeParty(A, B, C);
            uint previous = Party(party).SequenceId;
            var seen = new List<uint>();

            for (uint i = 1; i <= 3u; i++)
            {
                _registry.AdvanceRrNextIndex(party);
                Run(TICK + i);
                seen.Add(_sinks.LastState(A).SequenceId);
            }

            Assert.Greater(seen[0], previous);
            Assert.Greater(seen[1], seen[0]);
            Assert.Greater(seen[2], seen[1]);
        }

        [Test]
        public void test_leave_disband_non_leader_disband_on_a_disbanding_party_is_ignored_silently()
        {
            PartyID party = MakeParty(A, B, C);
            _membership.EnqueueDisband(Ch(A));
            Run(TICK);
            _sinks.Events.Clear();
            _membership.EnqueueDisband(Ch(B));

            Run(TICK + 1u);

            Assert.AreEqual(0, _sinks.Events.Count);
            Assert.AreEqual(PartyLifecycleState.Disbanding, Party(party).State);
            Assert.AreEqual(1, _membership.PendingCleanupCount);
        }
    }

    /// <summary>
    /// EditMode tests for <see cref="OutboxPartyStateMessageSink"/> over a real <see cref="ZoneSlotPool"/>,
    /// <see cref="ClientMessageOutbox"/> and <see cref="PartyRegistry"/> (Party System Story 008).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_LeaveDisbandBroadcast_Sink_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_P = 11u;
        private const uint CLIENT_Q = 12u;
        private const uint CLIENT_NOT_IN_OUTBOX = 13u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint TICK = 4242u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint CH_P = 1u;
        private const uint CH_Q = 2u;
        private const uint CH_NO_SLOT = 3u;
        private const uint CH_NO_CLIENT = 4u;
        private const uint CH_UNREGISTERED = 99u;
        private const uint UNPOOLED_ENTITY_RAW = 999999u;
        private const uint PARTY_RAW = 321u;
        private const uint SEQUENCE = 17u;
        private const byte LEVEL = 31;
        private const uint ENTITY_RAW = 555u;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;

            public Sent(uint clientId, byte[] data)
            {
                ClientId = clientId;
                Data = data;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Packets.Add(new Sent(clientId, message.ToArray()));
            }
        }

        private ZoneSlotPool _pool;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private PartyRegistry _registry;
        private OutboxPartyStateMessageSink _sink;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_P);
            _outbox.AddClient(CLIENT_Q);
            _registry = new PartyRegistry(PartySystem_InviteFlow_Tests.Config());
            RegisterPooled(CH_P, CLIENT_P);
            RegisterPooled(CH_Q, CLIENT_Q);
            RegisterPooled(CH_NO_CLIENT, CLIENT_NOT_IN_OUTBOX);
            Assert.AreEqual(PartyRegistryResult.Ok,
                _registry.RegisterPlayer(new CharacterID(CH_NO_SLOT), new EntityID(UNPOOLED_ENTITY_RAW), out _));
            _sink = new OutboxPartyStateMessageSink(_registry, _pool, _outbox);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        private void RegisterPooled(uint character, uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(character), new AccountID(client + ACCOUNT_OFFSET),
                client, out _, out EntityID entity));
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(new CharacterID(character), entity, out _));
        }

        private List<Sent> PacketsTo(uint client)
        {
            var result = new List<Sent>();
            foreach (Sent packet in _seam.Packets)
            {
                if (packet.ClientId == client)
                {
                    result.Add(packet);
                }
            }

            return result;
        }

        private static ushort TypeOf(Sent packet)
        {
            Assert.IsTrue(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope));
            return envelope.MessageTypeId;
        }

        private static byte[] BodyOf(Sent packet)
        {
            return packet.Data.AsSpan(ServerMessageEnvelope.WireSize).ToArray();
        }

        private static PartyStateUpdate Update()
        {
            var slot = new PartyMemberSlot(CH_P, ENTITY_RAW, LEVEL, PartyMemberStatus.Online, true);
            return new PartyStateUpdate(PARTY_RAW, SEQUENCE, true, 1, 0, 0, slot, default, default, default);
        }

        [Test]
        public void test_leave_disband_sink_state_update_is_enqueued_to_owning_client_only()
        {
            PartyStateUpdate message = Update();

            _sink.SendStateUpdate(new CharacterID(CH_P), in message);
            _outbox.Flush(TICK);

            List<Sent> toP = PacketsTo(CLIENT_P);
            Assert.AreEqual(1, toP.Count);
            Assert.AreEqual(0xE170, TypeOf(toP[0]));
            Assert.IsTrue(PartyStateUpdate.TryRead(BodyOf(toP[0]), out PartyStateUpdate decoded, out bool unknown));
            Assert.IsFalse(unknown);
            Assert.AreEqual(PARTY_RAW, decoded.PartyId);
            Assert.AreEqual(SEQUENCE, decoded.SequenceId);
            Assert.AreEqual(LEVEL, decoded.Member0.Level);
            Assert.AreEqual(0, PacketsTo(CLIENT_Q).Count);
        }

        [Test]
        public void test_leave_disband_sink_disbanded_is_enqueued_to_owning_client_only()
        {
            _sink.SendDisbanded(new CharacterID(CH_Q), new PartyID(PARTY_RAW));
            _outbox.Flush(TICK);

            List<Sent> toQ = PacketsTo(CLIENT_Q);
            Assert.AreEqual(1, toQ.Count);
            Assert.AreEqual(0xE171, TypeOf(toQ[0]));
            Assert.IsTrue(PartyDisbanded.TryRead(BodyOf(toQ[0]), out PartyDisbanded decoded));
            Assert.AreEqual(PARTY_RAW, decoded.PartyId);
            Assert.AreEqual(0, PacketsTo(CLIENT_P).Count);
        }

        [TestCase(CH_UNREGISTERED)]
        [TestCase(CH_NO_SLOT)]
        [TestCase(CH_NO_CLIENT)]
        public void test_leave_disband_sink_unresolvable_recipient_enqueues_nothing_and_does_not_throw(uint character)
        {
            PartyStateUpdate message = Update();
            var recipient = new CharacterID(character);

            Assert.DoesNotThrow(() => _sink.SendStateUpdate(recipient, in message));
            Assert.DoesNotThrow(() => _sink.SendDisbanded(recipient, new PartyID(PARTY_RAW)));
            _outbox.Flush(TICK);

            Assert.AreEqual(0, _seam.Packets.Count);
        }
    }
}
