using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// EditMode tests for Party System Story 007: the invite flow (AC-1 send, AC-2 rejects, AC-3 accept, AC-4
    /// decline and expiry, AC-5 retry, AC-6 queue, AC-7 repeats and strays, AC-8 intake). Time is explicit ticks.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_InviteFlow_Tests
    {
        private const int GATE = 5;
        private const int LEVEL_LEADER = 30;
        private const int LEVEL_BELOW_RANGE = 24;
        private const int LEVEL_LOW_EDGE = 25;
        private const int LEVEL_HIGH_EDGE = 35;
        private const int LEVEL_ABOVE_RANGE = 36;
        private const int LEVEL_LEADER_RAISED = 40;
        private const uint TICK_START = 500u;
        private const uint TICK_ARRIVAL = 490u;
        private const uint TICK_RESPONSE = 550u;
        private const uint TICK_NEAR_MAX = uint.MaxValue - 100u;
        private const uint TIMEOUT = 1200u;
        private const uint RETRY = 100u;
        private const uint ENTITY_BASE = 1000u;
        private const uint A = 1u;
        private const uint B = 2u;
        private const uint C = 3u;
        private const uint D = 4u;
        private const uint E = 5u;
        private const uint F = 6u;
        private const uint X = 10u;
        private const uint Y = 11u;
        private const uint UNKNOWN = 99u;
        private const uint FIRST_QUEUED_SENDER = 20u;
        private const uint INVITER_BASE = 100u;
        private const uint TARGET_BASE = 200u;
        private const int PARTY_SIZE = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT;
        private const int MEMBERS_PAIR = 2;
        private const int MEMBERS_TRIO = 3;
        private const int QUEUE_CAPACITY = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT * 2;
        private const int RECIPIENTS_BEYOND_POOL = 40;
        private const int RECIPIENTS_FEW = 3;
        private const string NAME_A = "Alyx";
        private const uint CLIENT_A = 7u;
        private const uint INTAKE_SENDER = 50u;
        private const int ROW_NON_LEADER_UNKNOWN_TARGET = 0;
        private const int ROW_UNKNOWN_TARGET_FULL_PARTY = 1;
        private const int ROW_OTHER_PARTY_FULL_PARTY = 2;
        private const int ROW_FULL_PARTY_OUT_OF_RANGE = 3;

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            private readonly Dictionary<EntityID, int> _levels = new Dictionary<EntityID, int>();

            public void Set(EntityID entity, int level) => _levels[entity] = level;

            public void Remove(EntityID entity) => _levels.Remove(entity);

            public bool TryGetLevel(EntityID entity, out int level) => _levels.TryGetValue(entity, out level);
        }

        private sealed class FakeNames : ICharacterNameLookup
        {
            public readonly Dictionary<CharacterID, string> Names = new Dictionary<CharacterID, string>();

            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                if (Names.TryGetValue(id, out name))
                {
                    return true;
                }

                name = string.Empty;
                return false;
            }
        }

        private sealed class RecordingSink : IPartyInviteMessageSink
        {
            public readonly List<(CharacterID Recipient, PartyInviteReceived Message)> Received =
                new List<(CharacterID, PartyInviteReceived)>();

            public readonly List<(CharacterID Recipient, PartyInviteResultCode Code, CharacterID Target)> Results =
                new List<(CharacterID, PartyInviteResultCode, CharacterID)>();

            public void SendInviteReceived(CharacterID recipient, in PartyInviteReceived message)
                => Received.Add((recipient, message));

            public void SendInviteResult(CharacterID recipient, PartyInviteResultCode code, CharacterID target)
                => Results.Add((recipient, code, target));

            public int ReceivedBy(uint recipient)
            {
                int n = 0;
                foreach ((CharacterID r, PartyInviteReceived _) in Received)
                {
                    if (r == Ch(recipient)) n++;
                }

                return n;
            }

            public PartyInviteResultCode LastResultTo(uint recipient)
            {
                for (int i = Results.Count - 1; i >= 0; i--)
                {
                    if (Results[i].Recipient == Ch(recipient)) return Results[i].Code;
                }

                throw new InvalidOperationException("no result to " + recipient);
            }

            public int ResultsTo(uint recipient)
            {
                int n = 0;
                foreach (var r in Results)
                {
                    if (r.Recipient == Ch(recipient)) n++;
                }

                return n;
            }

            public void Clear()
            {
                Received.Clear();
                Results.Clear();
            }
        }

        private PartyRegistry _registry;
        private FakeLevels _levels;
        private FakeNames _names;
        private RecordingSink _sink;
        private PartyInviteService _service;

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(ENTITY_BASE + n);

        internal static PartySystemConfigValues Config()
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

        [SetUp]
        public void SetUp()
        {
            _registry = new PartyRegistry(Config());
            _levels = new FakeLevels();
            _names = new FakeNames();
            _sink = new RecordingSink();
            _service = new PartyInviteService(_sink, _registry, _levels, _names, Config());
            _names.Names[Ch(A)] = NAME_A;
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

        private void MakeParty(uint leader, params uint[] members)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryCreateParty(Ch(leader), out PartyID id));
            foreach (uint member in members)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(id, Ch(member)));
            }
        }

        private void Invite(uint sender, uint target, uint tick = TICK_START)
            => _service.HandleInvite(En(sender), Ch(target), tick);

        private void Respond(uint responder, uint inviter, bool accepted, uint tick = TICK_START)
            => _service.HandleResponse(En(responder), Ch(inviter), accepted, tick);

        private bool IsRealParty(uint n)
        {
            return _registry.TryGetParty(_registry.GetPartyID(Ch(n)), out PartyState p) && !p.IsSoloParty;
        }

        /// <summary>Exactly one result in total, to <paramref name="recipient"/>, with the code and target field.</summary>
        private void AssertOnlyResult(uint recipient, PartyInviteResultCode code, uint target, int expectedBanners = 0)
        {
            Assert.AreEqual(1, _sink.Results.Count, "exactly one result in total");
            Assert.AreEqual(Ch(recipient), _sink.Results[0].Recipient);
            Assert.AreEqual(code, _sink.Results[0].Code);
            Assert.AreEqual(Ch(target), _sink.Results[0].Target);
            Assert.AreEqual(expectedBanners, _sink.Received.Count, "banner count");
        }

        // ── AC-1 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_valid_invite_delivers_banner_and_sent_without_creating_party()
        {
            Reg(A);
            Reg(X);
            int partiesBefore = _registry.PartyCount;

            Invite(A, X);

            Assert.AreEqual(Ch(X), _sink.Received[0].Recipient);
            PartyInviteReceived msg = _sink.Received[0].Message;
            Assert.AreEqual(A, msg.InviterCharacterId);
            Assert.AreEqual(En(A).RawValue, msg.InviterEntityId);
            Assert.AreEqual(NAME_A, msg.InviterName);
            Assert.AreEqual(LEVEL_LEADER, msg.InviterLevel);
            Assert.AreEqual(TICK_START + TIMEOUT, msg.InviteExpiryTick);
            AssertOnlyResult(A, PartyInviteResultCode.Sent, X, 1);
            Assert.IsFalse(IsRealParty(A));
            Assert.AreEqual(partiesBefore, _registry.PartyCount);
        }

        [Test]
        public void test_invite_flow_missing_name_sends_empty_string()
        {
            Reg(B);
            Reg(X);

            Invite(B, X);

            Assert.AreEqual(string.Empty, _sink.Received[0].Message.InviterName);
        }

        [Test]
        public void test_invite_flow_invite_from_unregistered_sender_entity_is_silent()
        {
            Reg(X);

            _service.HandleInvite(En(UNKNOWN), Ch(X), TICK_START);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.AreEqual(0, _sink.Received.Count);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        // ── AC-2 ───────────────────────────────────────────────────────────────────────────

        [TestCase(LEVEL_BELOW_RANGE, PartyInviteResultCode.LevelRange)]
        [TestCase(LEVEL_LOW_EDGE, PartyInviteResultCode.Sent)]
        [TestCase(LEVEL_HIGH_EDGE, PartyInviteResultCode.Sent)]
        [TestCase(LEVEL_ABOVE_RANGE, PartyInviteResultCode.LevelRange)]
        public void test_invite_flow_level_gate_is_inclusive_at_five(int targetLevel, PartyInviteResultCode expected)
        {
            Reg(A);
            Reg(X, targetLevel);

            Invite(A, X);

            AssertOnlyResult(A, expected, X, expected == PartyInviteResultCode.Sent ? 1 : 0);
            Assert.AreEqual(expected == PartyInviteResultCode.Sent, _service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_level_checked_against_leader_not_sender_member()
        {
            Reg(A);
            Reg(B);
            Reg(X, LEVEL_ABOVE_RANGE);
            MakeParty(A, B);

            Invite(A, X);

            AssertOnlyResult(A, PartyInviteResultCode.LevelRange, X);
        }

        [Test]
        public void test_invite_flow_unknown_target_answers_target_not_found()
        {
            Reg(A);

            Invite(A, UNKNOWN);

            AssertOnlyResult(A, PartyInviteResultCode.TargetNotFound, UNKNOWN);
            Assert.IsFalse(_service.HasPending(Ch(UNKNOWN)));
        }

        [Test]
        public void test_invite_flow_non_leader_member_sender_answers_not_leader()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            MakeParty(A, B);

            Invite(B, X);

            AssertOnlyResult(B, PartyInviteResultCode.NotLeader, X);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_full_party_answers_party_full()
        {
            Reg(A);
            Reg(B);
            Reg(C);
            Reg(D);
            Reg(X);
            MakeParty(A, B, C, D);

            Invite(A, X);

            AssertOnlyResult(A, PartyInviteResultCode.PartyFull, X);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_target_in_real_party_answers_already_in_party()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Reg(Y);
            MakeParty(B, Y);

            Invite(A, Y);

            AssertOnlyResult(A, PartyInviteResultCode.AlreadyInParty, Y);
            Assert.IsFalse(_service.HasPending(Ch(Y)));
        }

        [Test]
        public void test_invite_flow_self_invite_answers_already_in_party()
        {
            Reg(A);

            Invite(A, A);

            AssertOnlyResult(A, PartyInviteResultCode.AlreadyInParty, A);
            Assert.IsFalse(_service.HasPending(Ch(A)));
        }

        [Test]
        public void test_invite_flow_own_member_answers_already_in_party()
        {
            Reg(A);
            Reg(B);
            MakeParty(A, B);

            Invite(A, B);

            AssertOnlyResult(A, PartyInviteResultCode.AlreadyInParty, B);
            Assert.IsFalse(_service.HasPending(Ch(B)));
        }

        [Test]
        public void test_invite_flow_target_with_unreadable_level_answers_target_not_found()
        {
            Reg(A);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.RegisterPlayer(Ch(X), En(X), out _));

            Invite(A, X);

            AssertOnlyResult(A, PartyInviteResultCode.TargetNotFound, X);
        }

        [Test]
        public void test_invite_flow_leader_with_unreadable_level_answers_level_range()
        {
            Reg(A);
            Reg(X);
            _levels.Remove(En(A));

            Invite(A, X);

            AssertOnlyResult(A, PartyInviteResultCode.LevelRange, X);
        }

        [TestCase(ROW_NON_LEADER_UNKNOWN_TARGET, PartyInviteResultCode.NotLeader)]
        [TestCase(ROW_UNKNOWN_TARGET_FULL_PARTY, PartyInviteResultCode.TargetNotFound)]
        [TestCase(ROW_OTHER_PARTY_FULL_PARTY, PartyInviteResultCode.AlreadyInParty)]
        [TestCase(ROW_FULL_PARTY_OUT_OF_RANGE, PartyInviteResultCode.PartyFull)]
        public void test_invite_flow_send_check_order_with_two_failures(int row, PartyInviteResultCode expected)
        {
            uint sender = A;
            uint target = UNKNOWN;
            switch (row)
            {
                case ROW_NON_LEADER_UNKNOWN_TARGET:
                    Reg(A);
                    Reg(B);
                    MakeParty(A, B);
                    sender = B;
                    break;
                case ROW_UNKNOWN_TARGET_FULL_PARTY:
                    Reg(A);
                    Reg(B);
                    Reg(C);
                    Reg(D);
                    MakeParty(A, B, C, D);
                    break;
                case ROW_OTHER_PARTY_FULL_PARTY:
                    Reg(A);
                    Reg(B);
                    Reg(C);
                    Reg(D);
                    Reg(E);
                    Reg(F);
                    MakeParty(A, B, C, D);
                    MakeParty(E, F);
                    target = F;
                    break;
                default:
                    Reg(A);
                    Reg(B);
                    Reg(C);
                    Reg(D);
                    Reg(X, LEVEL_ABOVE_RANGE);
                    MakeParty(A, B, C, D);
                    target = X;
                    break;
            }

            Invite(sender, target);

            AssertOnlyResult(sender, expected, target);
        }

        // ── AC-3 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_accept_creates_party_with_inviter_as_leader()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();

            Respond(X, A, true);

            Assert.IsTrue(IsRealParty(A));
            Assert.AreEqual(_registry.GetPartyID(Ch(A)), _registry.GetPartyID(Ch(X)));
            IReadOnlyList<CharacterID> members = _registry.GetPartyMembers(_registry.GetPartyID(Ch(A)));
            Assert.AreEqual(MEMBERS_PAIR, members.Count);
            Assert.AreEqual(Ch(A), members[0]);
            Assert.AreEqual(Ch(X), members[1]);
            Assert.IsFalse(_service.HasPending(Ch(X)));
            Assert.AreEqual(0, _sink.Results.Count);
            Assert.AreEqual(0, _sink.Received.Count);
        }

        [Test]
        public void test_invite_flow_accept_when_party_ids_exhausted_answers_expired_and_resolves()
        {
            // Arrange: solo A invites X, then the party id counter is exhausted so TryCreateParty fails at accept.
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();
            _registry.SetNextPartyIdForTests(uint.MaxValue);
            LogAssert.Expect(LogType.Error, new Regex(@"\[PartyRegistry\] party id counter exhausted"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[PartyInvite\] accepted invite .* could not join: IdsExhausted"));

            // Act
            Respond(X, A, true);

            // Assert: X told Expired (inviter id), both still solo, slot resolved
            AssertOnlyResult(X, PartyInviteResultCode.Expired, A);
            Assert.IsFalse(IsRealParty(A));
            Assert.IsFalse(IsRealParty(X));
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_accept_into_existing_party_adds_member()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            MakeParty(A, B);
            Invite(A, X);
            _sink.Clear();

            Respond(X, A, true);

            Assert.AreEqual(MEMBERS_TRIO, _registry.GetPartyMembers(_registry.GetPartyID(Ch(A))).Count);
            Assert.AreEqual(0, _sink.Results.Count);
        }

        [Test]
        public void test_invite_flow_accept_after_party_filled_answers_party_full()
        {
            Reg(A);
            Reg(B);
            Reg(C);
            Reg(D);
            Reg(X);
            MakeParty(A, B, C);
            Invite(A, X);
            Assert.AreEqual(PartyRegistryResult.Ok, _registry.TryAddMember(_registry.GetPartyID(Ch(A)), Ch(D)));
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(X, PartyInviteResultCode.PartyFull, A);
            Assert.AreEqual(PARTY_SIZE, _registry.GetPartyMembers(_registry.GetPartyID(Ch(A))).Count);
            Assert.IsFalse(IsRealParty(X));
        }

        [Test]
        public void test_invite_flow_accept_after_leader_level_change_answers_level_range()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _levels.Set(En(A), LEVEL_LEADER_RAISED);
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(X, PartyInviteResultCode.LevelRange, A);
            Assert.IsFalse(IsRealParty(X));
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_accept_after_inviter_became_non_leader_answers_expired()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            MakeParty(B, A);
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(X, PartyInviteResultCode.Expired, A);
            Assert.IsFalse(IsRealParty(X));
        }

        [Test]
        public void test_invite_flow_accept_after_inviter_unregistered_answers_expired()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _registry.UnregisterPlayer(Ch(A), out _);
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(X, PartyInviteResultCode.Expired, A);
        }

        [Test]
        public void test_invite_flow_accept_after_target_joined_other_party_answers_already_in_party()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            MakeParty(B, X);
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(X, PartyInviteResultCode.AlreadyInParty, A);
            Assert.IsFalse(IsRealParty(A));
        }

        [Test]
        public void test_invite_flow_dead_target_receives_and_accepts()
        {
            Reg(A);
            Reg(X);
            _registry.SetMemberStatus(Ch(X), PartyMemberStatus.Dead);
            Invite(A, X);

            Respond(X, A, true);

            Assert.AreEqual(1, _sink.ReceivedBy(X));
            Assert.AreEqual(_registry.GetPartyID(Ch(A)), _registry.GetPartyID(Ch(X)));
            Assert.IsTrue(IsRealParty(X));
        }

        // ── AC-4 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_decline_notifies_inviter_and_frees_slot()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();

            Respond(X, A, false);

            AssertOnlyResult(A, PartyInviteResultCode.Declined, X);
            Assert.IsFalse(_service.HasPending(Ch(X)));
            Assert.IsFalse(IsRealParty(A));
            _sink.Clear();
            Invite(A, X);
            Assert.AreEqual(PartyInviteResultCode.Sent, _sink.LastResultTo(A));
        }

        [Test]
        public void test_invite_flow_decline_with_queue_answers_inviter_and_promotes_with_fresh_expiry()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            _sink.Clear();

            Respond(X, A, false, TICK_RESPONSE);

            AssertOnlyResult(A, PartyInviteResultCode.Declined, X, 1);
            Assert.AreEqual(Ch(X), _sink.Received[0].Recipient);
            Assert.AreEqual(B, _sink.Received[0].Message.InviterCharacterId);
            Assert.AreEqual(TICK_RESPONSE + TIMEOUT, _sink.Received[0].Message.InviteExpiryTick);
        }

        [Test]
        public void test_invite_flow_expiry_fires_at_timeout_tick_and_not_before()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();

            _service.Tick(TICK_START + TIMEOUT - 1u);
            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(X)));

            _service.Tick(TICK_START + TIMEOUT);
            AssertOnlyResult(A, PartyInviteResultCode.Expired, X, 1);
            Assert.IsFalse(_service.HasPending(Ch(X)));

            _sink.Clear();
            Invite(A, X, TICK_START + TIMEOUT);
            Assert.AreEqual(PartyInviteResultCode.Sent, _sink.LastResultTo(A));
        }

        [Test]
        public void test_invite_flow_expiry_wraps_past_uint_max_and_fires_on_time()
        {
            Reg(A);
            Reg(X);
            uint expiry = unchecked(TICK_NEAR_MAX + TIMEOUT);

            Invite(A, X, TICK_NEAR_MAX);

            Assert.AreEqual(expiry, _sink.Received[0].Message.InviteExpiryTick);
            Assert.Less(expiry, TICK_NEAR_MAX, "the expiry wrapped");
            _sink.Clear();
            _service.Tick(unchecked(expiry - 1u));
            Assert.AreEqual(0, _sink.Results.Count);
            _service.Tick(expiry);
            AssertOnlyResult(A, PartyInviteResultCode.Expired, X, 1);
        }

        [TestCase(RECIPIENTS_FEW)]
        [TestCase(RECIPIENTS_BEYOND_POOL)]
        public void test_invite_flow_many_recipients_expire_on_one_tick_each_answered_once(int count)
        {
            for (uint i = 0; i < count; i++)
            {
                Reg(INVITER_BASE + i);
                Reg(TARGET_BASE + i);
                Invite(INVITER_BASE + i, TARGET_BASE + i);
            }

            Assert.AreEqual(count, _service.PendingCount);
            _sink.Clear();

            _service.Tick(TICK_START + TIMEOUT);

            Assert.AreEqual(count, _sink.Results.Count);
            var seen = new HashSet<(uint Recipient, uint Target)>();
            foreach ((CharacterID recipient, PartyInviteResultCode code, CharacterID target) in _sink.Results)
            {
                Assert.AreEqual(PartyInviteResultCode.Expired, code);
                seen.Add((recipient.RawValue, target.RawValue));
            }

            Assert.AreEqual(count, seen.Count);
            for (uint i = 0; i < count; i++)
            {
                Assert.IsTrue(seen.Contains((INVITER_BASE + i, TARGET_BASE + i)), "result for pair " + i);
            }

            Assert.AreEqual(0, _service.PendingCount);
        }

        // ── AC-5 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_retry_resends_once_with_same_expiry()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);

            _service.Tick(TICK_START + RETRY - 1u);
            Assert.AreEqual(1, _sink.Received.Count);

            _service.Tick(TICK_START + RETRY);
            Assert.AreEqual(2, _sink.Received.Count);
            Assert.AreEqual(_sink.Received[0].Message.InviteExpiryTick, _sink.Received[1].Message.InviteExpiryTick);
            Assert.AreEqual(Ch(X), _sink.Received[1].Recipient);

            _service.Tick(TICK_START + RETRY + 1u);
            _service.Tick(TICK_START + TIMEOUT - 1u);
            Assert.AreEqual(2, _sink.Received.Count);
        }

        // ── AC-6 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_second_inviter_is_queued_without_second_banner()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);

            Invite(B, X);

            Assert.AreEqual(PartyInviteResultCode.Queued, _sink.LastResultTo(B));
            Assert.AreEqual(1, _sink.ReceivedBy(X));
            Assert.AreEqual(1, _service.GetQueuedCount(Ch(X)));
        }

        [Test]
        public void test_invite_flow_queue_advances_on_expiry_with_own_timer_and_retry()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            const uint expiryA = TICK_START + TIMEOUT;

            _service.Tick(expiryA);

            Assert.AreEqual(PartyInviteResultCode.Expired, _sink.LastResultTo(A));
            Assert.AreEqual(2, _sink.ReceivedBy(X));
            PartyInviteReceived second = _sink.Received[_sink.Received.Count - 1].Message;
            Assert.AreEqual(B, second.InviterCharacterId);
            Assert.AreEqual(expiryA + TIMEOUT, second.InviteExpiryTick);
            Assert.AreEqual(0, _service.GetQueuedCount(Ch(X)));
            Assert.AreEqual(1, _sink.ResultsTo(B), "the promoted sender got only its Queued answer, no Sent");
            Assert.AreEqual(PartyInviteResultCode.Queued, _sink.LastResultTo(B));

            _service.Tick(expiryA + RETRY);
            Assert.AreEqual(3, _sink.ReceivedBy(X));
        }

        [Test]
        public void test_invite_flow_promoted_invite_keeps_its_own_retry_and_expiry_timers()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            const uint expiryA = TICK_START + TIMEOUT;

            _service.Tick(expiryA);
            Assert.AreEqual(2, _sink.ReceivedBy(X), "the promotion tick neither retries nor expires the promoted invite");
            Assert.AreEqual(0, _sink.Results.FindAll(r => r.Code == PartyInviteResultCode.Expired && r.Recipient == Ch(B)).Count);

            _service.Tick(expiryA + RETRY - 1u);
            Assert.AreEqual(2, _sink.ReceivedBy(X));
            _service.Tick(expiryA + RETRY);
            Assert.AreEqual(3, _sink.ReceivedBy(X));

            _service.Tick(expiryA + TIMEOUT - 1u);
            Assert.AreEqual(1, _sink.ResultsTo(B));
            Assert.IsTrue(_service.HasPending(Ch(X)));
            _service.Tick(expiryA + TIMEOUT);
            Assert.AreEqual(PartyInviteResultCode.Expired, _sink.LastResultTo(B));
            Assert.AreEqual(2, _sink.ResultsTo(B));
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_queue_advances_on_decline_and_accept()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);

            Respond(X, A, false);

            Assert.AreEqual(2, _sink.ReceivedBy(X));
            Assert.AreEqual(1, _sink.ResultsTo(B), "the promoted sender got only its Queued answer, no Sent");
            Respond(X, B, true);
            Assert.IsTrue(IsRealParty(B));
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_accept_with_queue_answers_queued_senders_already_in_party()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            _sink.Clear();

            Respond(X, A, true);

            AssertOnlyResult(B, PartyInviteResultCode.AlreadyInParty, X);
            Assert.IsTrue(IsRealParty(A));
            Assert.IsFalse(_service.HasPending(Ch(X)));
            Assert.AreEqual(0, _service.GetQueuedCount(Ch(X)));
        }

        [Test]
        public void test_invite_flow_queued_invite_failing_revalidation_is_answered_and_next_delivered()
        {
            Reg(A);
            Reg(C);
            Reg(D);
            Reg(E);
            Reg(X);
            Invite(A, X);
            Invite(C, X);
            Invite(D, X);
            MakeParty(E, X);
            _sink.Clear();

            Respond(X, A, false);

            // X is now in a real party: both queued invites fail with AlreadyInParty, in queue order.
            Assert.AreEqual(3, _sink.Results.Count);
            Assert.AreEqual(Ch(A), _sink.Results[0].Recipient);
            Assert.AreEqual(PartyInviteResultCode.Declined, _sink.Results[0].Code);
            Assert.AreEqual(Ch(C), _sink.Results[1].Recipient);
            Assert.AreEqual(PartyInviteResultCode.AlreadyInParty, _sink.Results[1].Code);
            Assert.AreEqual(Ch(D), _sink.Results[2].Recipient);
            Assert.AreEqual(PartyInviteResultCode.AlreadyInParty, _sink.Results[2].Code);
            Assert.AreEqual(1, _sink.ResultsTo(C));
            Assert.AreEqual(1, _sink.ResultsTo(D));
            Assert.AreEqual(0, _sink.Received.Count);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_failed_queue_entry_does_not_block_next_delivery()
        {
            Reg(A);
            Reg(B);
            Reg(C);
            Reg(D);
            Reg(F);
            Reg(X);
            Invite(A, X);
            Invite(C, X);
            Invite(D, X);
            MakeParty(B, C, F);
            // C is now a non-leader member of a real party: its queued invite fails with NotLeader.

            Respond(X, A, false);

            Assert.AreEqual(PartyInviteResultCode.NotLeader, _sink.LastResultTo(C));
            Assert.AreEqual(2, _sink.ReceivedBy(X));
            Assert.AreEqual(D, _sink.Received[1].Message.InviterCharacterId);
            Assert.AreEqual(1, _sink.ResultsTo(D), "the promoted sender got only its Queued answer, no Sent");
            Assert.AreEqual(PartyInviteResultCode.Queued, _sink.LastResultTo(D));
            Assert.IsTrue(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_two_failing_queued_then_valid_third_is_delivered()
        {
            Reg(A);
            Reg(B);
            Reg(C);
            Reg(D);
            Reg(F);
            Reg(X);
            Invite(A, X);
            Invite(C, X);
            Invite(D, X);
            Invite(F, X);
            MakeParty(B, C, D);
            _sink.Clear();

            Respond(X, A, false, TICK_RESPONSE);

            Assert.AreEqual(3, _sink.Results.Count);
            Assert.AreEqual((Ch(A), PartyInviteResultCode.Declined, Ch(X)), _sink.Results[0]);
            Assert.AreEqual((Ch(C), PartyInviteResultCode.NotLeader, Ch(X)), _sink.Results[1]);
            Assert.AreEqual((Ch(D), PartyInviteResultCode.NotLeader, Ch(X)), _sink.Results[2]);
            Assert.AreEqual(1, _sink.Received.Count);
            Assert.AreEqual(F, _sink.Received[0].Message.InviterCharacterId);
            Assert.AreEqual(TICK_RESPONSE + TIMEOUT, _sink.Received[0].Message.InviteExpiryTick);

            _service.Tick(TICK_RESPONSE + RETRY);
            Assert.AreEqual(2, _sink.Received.Count, "the delivered invite has its own retry");
        }

        [Test]
        public void test_invite_flow_queue_full_declines_ninth_sender_with_one_warning()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            for (uint i = 0; i < QUEUE_CAPACITY; i++)
            {
                Reg(FIRST_QUEUED_SENDER + i);
                Invite(FIRST_QUEUED_SENDER + i, X);
                Assert.AreEqual(PartyInviteResultCode.Queued, _sink.LastResultTo(FIRST_QUEUED_SENDER + i));
            }

            uint overflow = FIRST_QUEUED_SENDER + QUEUE_CAPACITY;
            Reg(overflow);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "invite queue full"));

            Invite(overflow, X);

            Assert.AreEqual(PartyInviteResultCode.Declined, _sink.LastResultTo(overflow));
            Assert.AreEqual(Ch(X), _sink.Results[_sink.Results.Count - 1].Target);
            Assert.AreEqual(QUEUE_CAPACITY, _service.GetQueuedCount(Ch(X)));
            Assert.AreEqual(1, _sink.Received.Count, "only the pending banner was delivered");
        }

        [Test]
        public void test_invite_flow_queue_full_declined_sender_is_never_delivered_later()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            for (uint i = 0; i < QUEUE_CAPACITY; i++)
            {
                Reg(FIRST_QUEUED_SENDER + i);
                Invite(FIRST_QUEUED_SENDER + i, X);
            }

            uint overflow = FIRST_QUEUED_SENDER + QUEUE_CAPACITY;
            Reg(overflow);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "invite queue full"));
            Invite(overflow, X);

            Respond(X, A, false);
            for (uint i = 0; i < QUEUE_CAPACITY; i++)
            {
                Respond(X, FIRST_QUEUED_SENDER + i, false);
            }

            Assert.IsFalse(_service.HasPending(Ch(X)));
            Assert.AreEqual(1 + QUEUE_CAPACITY, _sink.Received.Count);
            foreach ((CharacterID _, PartyInviteReceived message) in _sink.Received)
            {
                Assert.AreNotEqual(overflow, message.InviterCharacterId);
            }
        }

        // ── AC-7 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_invite_flow_repeat_while_pending_answers_sent_without_new_banner()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);

            Invite(A, X);

            Assert.AreEqual(PartyInviteResultCode.Sent, _sink.LastResultTo(A));
            Assert.AreEqual(2, _sink.ResultsTo(A));
            Assert.AreEqual(1, _sink.ReceivedBy(X));
            Assert.AreEqual(1, _service.PendingCount);
            Assert.AreEqual(0, _service.GetQueuedCount(Ch(X)));
        }

        [Test]
        public void test_invite_flow_repeat_while_queued_answers_queued_with_one_entry()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);

            Invite(B, X);

            Assert.AreEqual(PartyInviteResultCode.Queued, _sink.LastResultTo(B));
            Assert.AreEqual(1, _service.GetQueuedCount(Ch(X)));
            Assert.AreEqual(1, _sink.ReceivedBy(X));
        }

        [Test]
        public void test_invite_flow_response_for_unknown_inviter_is_ignored_with_one_warning()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "response"));

            Respond(X, UNKNOWN, true);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(X)));
            Assert.IsFalse(IsRealParty(X));
        }

        [Test]
        public void test_invite_flow_response_with_no_pending_invite_is_ignored_with_one_warning()
        {
            Reg(A);
            Reg(X);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "response"));

            Respond(X, A, true);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsFalse(IsRealParty(A));
        }

        [Test]
        public void test_invite_flow_response_for_queued_not_delivered_inviter_is_ignored_with_one_warning()
        {
            Reg(A);
            Reg(B);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            _sink.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "response"));

            Respond(X, B, true);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(X)));
            Assert.AreEqual(1, _service.GetQueuedCount(Ch(X)));
            Assert.IsFalse(IsRealParty(B));
        }

        [Test]
        public void test_invite_flow_response_from_unregistered_target_is_ignored_with_one_warning()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _registry.UnregisterPlayer(Ch(X), out _);
            _sink.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "response"));

            Respond(X, A, true);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(X)), "the invite stays until it expires");
            Assert.IsFalse(IsRealParty(A));
        }

        // ── Unregistered participants while pending ────────────────────────────────────────

        [Test]
        public void test_invite_flow_target_unregistered_while_pending_ticks_through_retry_and_expiry()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _registry.UnregisterPlayer(Ch(X), out _);
            _sink.Clear();

            Assert.DoesNotThrow(() => _service.Tick(TICK_START + RETRY));
            Assert.DoesNotThrow(() => _service.Tick(TICK_START + TIMEOUT));

            AssertOnlyResult(A, PartyInviteResultCode.Expired, X, 1);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_inviter_unregistered_while_pending_ticks_through_retry_and_expiry()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _registry.UnregisterPlayer(Ch(A), out _);
            _sink.Clear();

            Assert.DoesNotThrow(() => _service.Tick(TICK_START + RETRY));
            Assert.DoesNotThrow(() => _service.Tick(TICK_START + TIMEOUT));

            AssertOnlyResult(A, PartyInviteResultCode.Expired, X, 1);
            Assert.IsFalse(_service.HasPending(Ch(X)));
        }

        [Test]
        public void test_invite_flow_queued_sender_unregistered_is_skipped_and_next_is_delivered()
        {
            Reg(A);
            Reg(B);
            Reg(C);
            Reg(X);
            Invite(A, X);
            Invite(B, X);
            Invite(C, X);
            _registry.UnregisterPlayer(Ch(B), out _);
            _sink.Clear();

            _service.Tick(TICK_START + TIMEOUT);

            Assert.AreEqual(Ch(A), _sink.Results[0].Recipient);
            Assert.AreEqual(PartyInviteResultCode.Expired, _sink.Results[0].Code);
            Assert.AreEqual(Ch(B), _sink.Results[1].Recipient);
            Assert.AreEqual(PartyInviteResultCode.Expired, _sink.Results[1].Code);
            Assert.AreEqual(2, _sink.Results.Count);
            Assert.AreEqual(1, _sink.Received.Count);
            Assert.AreEqual(C, _sink.Received[0].Message.InviterCharacterId);
            Assert.IsTrue(_service.HasPending(Ch(X)));
        }

        // ── AC-8 ───────────────────────────────────────────────────────────────────────────

        private InboundDispatchHarness NewIntakeHarness(out int[] handlerCalls)
        {
            // The harness client owns EntityOf(CLIENT_A); register that entity as the intake sender character.
            Assert.AreEqual(PartyRegistryResult.Ok,
                _registry.RegisterPlayer(Ch(INTAKE_SENDER), InboundDispatchHarness.EntityOf(CLIENT_A), out _));
            _levels.Set(InboundDispatchHarness.EntityOf(CLIENT_A), LEVEL_LEADER);
            Reg(X);
            var harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            var handlers = new PartyInviteClientRequestHandlers(_service);
            var calls = new int[2];
            handlerCalls = calls;
            harness.Dispatcher.Register(
                new InboundRequestDescriptor(PartyInviteRequest.MessageTypeId, RpcTypeTag.PartyInviteRequest, false,
                    (ushort)PartyInviteRequest.BodySize),
                (in InboundRequestContext context, ReadOnlySpan<byte> body) =>
                {
                    calls[0]++;
                    handlers.HandleInviteRequest(in context, body);
                });
            harness.Dispatcher.Register(
                new InboundRequestDescriptor(PartyInviteResponse.MessageTypeId, RpcTypeTag.PartyInviteResponse, false,
                    (ushort)PartyInviteResponse.BodySize),
                (in InboundRequestContext context, ReadOnlySpan<byte> body) =>
                {
                    calls[1]++;
                    handlers.HandleInviteResponse(in context, body);
                });
            harness.Dispatcher.Seal();
            harness.AddClient(CLIENT_A);
            return harness;
        }

        [Test]
        public void test_invite_flow_registration_declares_both_descriptors_unheld_with_codec_body_sizes()
        {
            var recorder = new RecordingDispatcher();

            PartyInboundRegistration.Register(recorder, new PartyInviteClientRequestHandlers(_service));

            Assert.AreEqual(2, recorder.Descriptors.Count);
            Assert.AreEqual(PartyInviteRequest.MessageTypeId, recorder.Descriptors[0].MessageTypeId);
            Assert.AreEqual(RpcTypeTag.PartyInviteRequest, recorder.Descriptors[0].RpcTypeTag);
            Assert.AreEqual(PartyInviteRequest.BodySize, recorder.Descriptors[0].MaxBodyBytes);
            Assert.AreEqual(PartyInviteResponse.MessageTypeId, recorder.Descriptors[1].MessageTypeId);
            Assert.AreEqual(RpcTypeTag.PartyInviteResponse, recorder.Descriptors[1].RpcTypeTag);
            Assert.AreEqual(PartyInviteResponse.BodySize, recorder.Descriptors[1].MaxBodyBytes);
            Assert.IsFalse(recorder.Descriptors[0].HeldDuringIrreversibleWrite);
            Assert.IsFalse(recorder.Descriptors[1].HeldDuringIrreversibleWrite);
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.PartyInviteRequest));
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.PartyInviteResponse));
        }

        [Test]
        public void test_invite_flow_registered_handlers_route_request_to_invite_and_response_to_response()
        {
            var recorder = new RecordingDispatcher();
            PartyInboundRegistration.Register(recorder, new PartyInviteClientRequestHandlers(_service));
            Reg(A);
            Reg(X);
            var requestBody = new byte[PartyInviteRequest.BodySize];
            new PartyInviteRequest(X).Write(requestBody);
            var requestContext = new InboundRequestContext(CLIENT_A, En(A), Ch(A), PartyInviteRequest.MessageTypeId,
                TICK_ARRIVAL, TICK_START, false);

            recorder.Handlers[0](in requestContext, requestBody);

            Assert.IsTrue(_service.HasPending(Ch(X)), "the first handler ran the invite path");
            Assert.IsFalse(IsRealParty(A));
            AssertOnlyResult(A, PartyInviteResultCode.Sent, X, 1);

            _sink.Clear();
            var responseBody = new byte[PartyInviteResponse.BodySize];
            new PartyInviteResponse(A, true).Write(responseBody);
            var responseContext = new InboundRequestContext(CLIENT_A, En(X), Ch(X), PartyInviteResponse.MessageTypeId,
                TICK_ARRIVAL, TICK_START, false);

            recorder.Handlers[1](in responseContext, responseBody);

            Assert.IsTrue(IsRealParty(A), "the second handler ran the response path");
            Assert.IsFalse(_service.HasPending(Ch(X)));
            Assert.AreEqual(0, _sink.Results.Count);
        }

        [Test]
        public void test_invite_flow_valid_body_through_dispatcher_reaches_service_on_dispatch_tick()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            var body = new byte[PartyInviteRequest.BodySize];
            new PartyInviteRequest(X).Write(body);
            harness.TickSource.Tick = TICK_ARRIVAL;

            harness.SendRequest(CLIENT_A, PartyInviteRequest.MessageTypeId, 1u, body);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(1, calls[0]);
            Assert.AreEqual(1, _sink.ReceivedBy(X));
            Assert.AreEqual(INTAKE_SENDER, _sink.Received[0].Message.InviterCharacterId);
            Assert.AreEqual(TICK_START + TIMEOUT, _sink.Received[0].Message.InviteExpiryTick);
            Assert.AreNotEqual(TICK_ARRIVAL + TIMEOUT, _sink.Received[0].Message.InviteExpiryTick);
            Assert.AreEqual(PartyInviteResultCode.Sent, _sink.LastResultTo(INTAKE_SENDER));
        }

        [Test]
        public void test_invite_flow_valid_response_through_dispatcher_reaches_service()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            Reg(A);
            Invite(A, INTAKE_SENDER);
            _sink.Clear();
            var body = new byte[PartyInviteResponse.BodySize];
            new PartyInviteResponse(A, true).Write(body);
            harness.TickSource.Tick = TICK_ARRIVAL;

            harness.SendRequest(CLIENT_A, PartyInviteResponse.MessageTypeId, 2u, body);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(1, calls[1]);
            Assert.IsTrue(IsRealParty(A));
            Assert.AreEqual(_registry.GetPartyID(Ch(A)), _registry.GetPartyID(Ch(INTAKE_SENDER)));
            Assert.IsFalse(_service.HasPending(Ch(INTAKE_SENDER)));
        }

        [Test]
        public void test_invite_flow_six_byte_invite_body_is_rejected_at_intake_before_handler()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            LogAssert.Expect(LogType.Warning, new Regex("body longer than MaxBodyBytes"));

            harness.SendRequest(CLIENT_A, PartyInviteRequest.MessageTypeId, 1u, new byte[PartyInviteRequest.BodySize + 2]);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(0, calls[0]);
            Assert.AreEqual(0, _sink.Results.Count);
        }

        [Test]
        public void test_invite_flow_six_byte_response_body_is_rejected_at_intake_before_handler()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            LogAssert.Expect(LogType.Warning, new Regex("body longer than MaxBodyBytes"));

            harness.SendRequest(CLIENT_A, PartyInviteResponse.MessageTypeId, 2u, new byte[PartyInviteResponse.BodySize + 1]);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(0, calls[1]);
            Assert.AreEqual(0, _sink.Results.Count);
        }

        [Test]
        public void test_invite_flow_three_byte_invite_body_passes_intake_but_handler_rejects_it()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "malformed body"));

            harness.SendRequest(CLIENT_A, PartyInviteRequest.MessageTypeId, 1u, new byte[PartyInviteRequest.BodySize - 1]);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(1, calls[0]);
            Assert.AreEqual(0, _sink.Results.Count);
            Assert.AreEqual(0, _sink.Received.Count);
        }

        [Test]
        public void test_invite_flow_four_byte_response_body_passes_intake_but_handler_drops_it()
        {
            InboundDispatchHarness harness = NewIntakeHarness(out int[] calls);
            Reg(A);
            Invite(A, INTAKE_SENDER);
            _sink.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "malformed body"));

            harness.SendRequest(CLIENT_A, PartyInviteResponse.MessageTypeId, 2u, new byte[PartyInviteResponse.BodySize - 1]);
            harness.Dispatcher.DispatchTick(TICK_START);

            Assert.AreEqual(1, calls[1]);
            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(INTAKE_SENDER)));
            Assert.IsFalse(IsRealParty(A));
        }

        [Test]
        public void test_invite_flow_response_with_bad_accepted_byte_is_rejected_by_handler()
        {
            Reg(A);
            Reg(X);
            Invite(A, X);
            _sink.Clear();
            var handlers = new PartyInviteClientRequestHandlers(_service);
            var body = new byte[PartyInviteResponse.BodySize];
            new PartyInviteResponse(A, true).Write(body);
            body[4] = 2;
            var context = new InboundRequestContext(CLIENT_A, En(X), Ch(X), PartyInviteResponse.MessageTypeId,
                TICK_ARRIVAL, TICK_START, false);
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(PartyInviteService.LOG_PREFIX) + "malformed body"));

            handlers.HandleInviteResponse(in context, body);

            Assert.AreEqual(0, _sink.Results.Count);
            Assert.IsTrue(_service.HasPending(Ch(X)));
        }

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<InboundRequestDescriptor> Descriptors = new List<InboundRequestDescriptor>();
            public readonly List<InboundRequestHandler> Handlers = new List<InboundRequestHandler>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
            {
                Descriptors.Add(descriptor);
                Handlers.Add(handler);
            }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }
    }

    /// <summary>
    /// EditMode tests for <see cref="OutboxPartyInviteMessageSink"/> over a real <see cref="ZoneSlotPool"/>,
    /// <see cref="ClientMessageOutbox"/> and <see cref="PartyRegistry"/> (Party System Story 007).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_InviteFlow_Sink_Tests
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
        private const uint INVITER_CHARACTER = 5u;
        private const uint INVITER_ENTITY = 77u;
        private const byte INVITER_LEVEL = 31;
        private const uint EXPIRY_TICK = 6000u;
        private const uint TARGET_CHARACTER = 6u;
        private const string INVITER_NAME = "Alyx";

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
        private OutboxPartyInviteMessageSink _sink;

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
            _sink = new OutboxPartyInviteMessageSink(_registry, _pool, _outbox);
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

        [Test]
        public void test_invite_flow_sink_received_is_enqueued_to_owning_client_only()
        {
            var message = new PartyInviteReceived(INVITER_CHARACTER, INVITER_ENTITY, INVITER_NAME, INVITER_LEVEL, EXPIRY_TICK);

            _sink.SendInviteReceived(new CharacterID(CH_P), in message);
            _outbox.Flush(TICK);

            List<Sent> toP = PacketsTo(CLIENT_P);
            Assert.AreEqual(1, toP.Count);
            Assert.AreEqual(0xE173, TypeOf(toP[0]));
            Assert.IsTrue(PartyInviteReceived.TryRead(BodyOf(toP[0]), out PartyInviteReceived decoded));
            Assert.AreEqual(INVITER_CHARACTER, decoded.InviterCharacterId);
            Assert.AreEqual(INVITER_ENTITY, decoded.InviterEntityId);
            Assert.AreEqual(INVITER_NAME, decoded.InviterName);
            Assert.AreEqual(INVITER_LEVEL, decoded.InviterLevel);
            Assert.AreEqual(EXPIRY_TICK, decoded.InviteExpiryTick);
            Assert.AreEqual(0, PacketsTo(CLIENT_Q).Count);
        }

        [Test]
        public void test_invite_flow_sink_result_is_enqueued_to_owning_client_only()
        {
            _sink.SendInviteResult(new CharacterID(CH_Q), PartyInviteResultCode.PartyFull, new CharacterID(TARGET_CHARACTER));
            _outbox.Flush(TICK);

            List<Sent> toQ = PacketsTo(CLIENT_Q);
            Assert.AreEqual(1, toQ.Count);
            Assert.AreEqual(0xE177, TypeOf(toQ[0]));
            Assert.IsTrue(PartyInviteResult.TryRead(BodyOf(toQ[0]), out PartyInviteResult decoded));
            Assert.AreEqual(PartyInviteResultCode.PartyFull, decoded.ResultCode);
            Assert.AreEqual(TARGET_CHARACTER, decoded.TargetCharacterId);
            Assert.AreEqual(0, PacketsTo(CLIENT_P).Count);
        }

        [TestCase(CH_UNREGISTERED)]
        [TestCase(CH_NO_SLOT)]
        [TestCase(CH_NO_CLIENT)]
        public void test_invite_flow_sink_unresolvable_recipient_enqueues_nothing_and_does_not_throw(uint character)
        {
            var message = new PartyInviteReceived(INVITER_CHARACTER, INVITER_ENTITY, INVITER_NAME, INVITER_LEVEL, EXPIRY_TICK);
            var recipient = new CharacterID(character);

            Assert.DoesNotThrow(() => _sink.SendInviteReceived(recipient, in message));
            Assert.DoesNotThrow(() =>
                _sink.SendInviteResult(recipient, PartyInviteResultCode.Sent, new CharacterID(TARGET_CHARACTER)));
            _outbox.Flush(TICK);

            Assert.AreEqual(0, _seam.Packets.Count);
        }
    }
}
