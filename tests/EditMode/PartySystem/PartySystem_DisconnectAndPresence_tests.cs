using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 009: disconnect, reconnect grace and zone presence (AC-1 Ghost, AC-2
    /// grace, AC-3 reconnect, AC-4 slot release, AC-5 zone presence, AC-6 solo registration, AC-7 invite
    /// cancellation, AC-8 status precedence) over a real <see cref="ZoneSlotPool"/>. Time is explicit ticks.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_DisconnectAndPresence_Tests
    {
        private const uint T0 = 1000u;
        private const uint GRACE = 100u;
        private const uint ZONE_RAW = 7u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint CLIENT_OFFSET = 500u;
        private const int MAX_PLAYERS = 32;
        private const int MAX_MOBS = 4;
        private const int LEVEL = 30;
        private const uint A = 1u;
        private const uint B = 2u;
        private const uint C = 3u;
        private const uint D = 4u;
        private const int ALLOC_PARTIES = 4;
        private const int ALLOC_TICKS = 100;

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

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => T0;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeLevels : IPartyMemberLevelSource
        {
            public bool TryGetLevel(EntityID entity, out int level)
            {
                level = LEVEL;
                return true;
            }
        }

        private sealed class FakeNames : ICharacterNameLookup
        {
            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                name = string.Empty;
                return false;
            }
        }

        private sealed class FakeSource : IPartySessionStateSource
        {
            private readonly Dictionary<EntityID, bool> _disconnected = new Dictionary<EntityID, bool>();

            public void Set(EntityID entity, bool disconnected) => _disconnected[entity] = disconnected;

            public bool TryIsDisconnected(EntityID entity, out bool disconnected)
                => _disconnected.TryGetValue(entity, out disconnected);
        }

        private sealed class RecordingSinks : IPartyStateMessageSink, IPartyInviteMessageSink
        {
            public readonly List<Event> Events = new List<Event>();

            public void SendStateUpdate(CharacterID recipient, in PartyStateUpdate message)
                => Events.Add(new Event { Kind = Kind.State, Recipient = recipient, Update = message });

            public void SendDisbanded(CharacterID recipient, PartyID partyId)
                => Events.Add(new Event { Kind = Kind.Disbanded, Recipient = recipient, PartyId = partyId.RawValue });

            /// <summary>When set, delivering an invite throws (a failing outbound seam).</summary>
            public bool ThrowOnInviteReceived;

            public void SendInviteReceived(CharacterID recipient, in PartyInviteReceived message)
            {
                if (ThrowOnInviteReceived) throw new InvalidOperationException("boom");
            }

            public void SendInviteResult(CharacterID recipient, PartyInviteResultCode code, CharacterID target)
                => Events.Add(new Event { Kind = Kind.Result, Recipient = recipient, Code = code, Target = target.RawValue });

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

            /// <summary>Updates (to anyone) in which the character's slot has the given status.</summary>
            public int CountShowing(uint character, PartyMemberStatus status)
            {
                int n = 0;
                foreach (Event e in Events)
                {
                    if (e.Kind == Kind.State && StatusIn(e.Update, character) == status) n++;
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
        private RecordingSinks _sinks;
        private FakeLevels _levels;
        private PartyXpEligibility _eligibility;
        private PartyBroadcastStep _broadcast;
        private PartyMembershipStep _membership;
        private PartyInviteService _invites;
        private FakeSource _source;
        private PartyPresenceStep _presence;
        private ZoneSlotPool _pool;
        private PartyPlayerEntryListener _entry;
        private PartyZonePresence _zonePresence;
        private readonly Dictionary<uint, EntityID> _entities = new Dictionary<uint, EntityID>();

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static PartySystemConfigValues Config() => PartySystem_InviteFlow_Tests.Config();

        private static PartyMemberStatus? StatusIn(PartyStateUpdate update, uint character)
        {
            foreach (PartyMemberSlot slot in new[] { update.Member0, update.Member1, update.Member2, update.Member3 })
            {
                if (slot.CharacterId == character) return slot.Status;
            }

            return null;
        }

        [SetUp]
        public void SetUp()
        {
            _entities.Clear();
            _registry = new PartyRegistry(Config());
            _sinks = new RecordingSinks();
            _levels = new FakeLevels();
            _eligibility = new PartyXpEligibility(_registry, _levels, Config());
            _broadcast = new PartyBroadcastStep(_registry, _levels, _eligibility, _sinks);
            _membership = new PartyMembershipStep(_registry, _sinks, _sinks, _broadcast, Config());
            _invites = new PartyInviteService(_sinks, _registry, _levels, new FakeNames(), Config());
            _source = new FakeSource();
            _presence = new PartyPresenceStep(_registry, _source, _invites, Config());
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _pool.SetReleaseListener(new PartySlotReleaseListener(_registry, _invites, _membership, _presence));
            _entry = new PartyPlayerEntryListener(_registry, _pool);
            _zonePresence = new PartyZonePresence(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        private EntityID Join(uint n)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, Ch(n), new AccountID(n + ACCOUNT_OFFSET), n + CLIENT_OFFSET,
                out byte slot, out EntityID entity));
            _entities[n] = entity;
            _source.Set(entity, false);
            _entry.OnPlayerEntering(slot, entity);
            return entity;
        }

        private PartyID MakeParty(uint leader, params uint[] members)
        {
            Join(leader);
            foreach (uint member in members)
            {
                Join(member);
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

        private void Disc(uint n, bool disconnected) => _source.Set(_entities[n], disconnected);

        private void Step(uint tick)
        {
            _presence.Poll(tick);
            _broadcast.Flush();
        }

        private void StepRange(uint from, uint to)
        {
            for (uint t = from; t <= to; t++) Step(t);
        }

        private PartyState Party(PartyID id)
        {
            Assert.IsTrue(_registry.TryGetParty(id, out PartyState party), "party exists");
            return party;
        }

        private CharacterID LeaderOf(PartyID id)
        {
            PartyState party = Party(id);
            return party.GetMemberAtIndex(party.LeaderIndex);
        }

        // ── AC-1 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_leader_stays_online_through_grace_then_ghost_and_leadership_moves()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(A, true);

            StepRange(T0, T0 + GRACE - 1u);

            Assert.AreEqual(0, _sinks.Count(Kind.State), "no update during the grace");
            Assert.AreEqual(0, _sinks.CountShowing(A, PartyMemberStatus.Ghost));
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(Ch(A), LeaderOf(party));

            Step(T0 + GRACE);

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(Ch(B), LeaderOf(party));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            PartyStateUpdate update = _sinks.LastState(B);
            Assert.AreEqual(PartyMemberStatus.Ghost, update.Member0.Status);
            Assert.AreEqual(1, update.LeaderIndex);
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            Assert.AreEqual(PartyMemberStatus.Ghost, _sinks.LastState(C).Member0.Status);
            Assert.AreEqual(2, _sinks.Count(Kind.State), "B and C only");
            Assert.AreEqual(0, _sinks.Count(Kind.State, A), "a Ghost receives no update");
            Assert.IsFalse(_eligibility.IsXpEligible(party, Ch(A)));
        }

        [Test]
        public void test_disconnect_presence_non_leader_ghost_keeps_the_leader()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(B, true);

            StepRange(T0, T0 + GRACE);

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(Ch(A), LeaderOf(party));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            PartyStateUpdate update = _sinks.LastState(C);
            Assert.AreEqual(PartyMemberStatus.Ghost, update.Member1.Status);
            Assert.AreEqual(0, update.LeaderIndex);
        }

        [Test]
        public void test_disconnect_presence_leader_ghost_with_every_other_member_ghost_does_not_transfer()
        {
            PartyID party = MakeParty(A, B);
            Disc(B, true);
            StepRange(T0, T0 + GRACE);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Disc(A, true);
            LogAssert.Expect(LogType.Log, new Regex("leadership not transferred"));

            StepRange(T0 + GRACE + 1u, T0 + 2u * GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(Ch(A), LeaderOf(party));
        }

        // ── AC-2 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_reconnect_inside_grace_never_shows_ghost_and_next_disconnect_gets_fresh_grace()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(A, true);
            StepRange(T0, T0 + 98u);
            Disc(A, false);
            StepRange(T0 + 99u, T0 + 149u);

            Assert.AreEqual(0, _sinks.CountShowing(A, PartyMemberStatus.Ghost));
            Assert.AreEqual(Ch(A), LeaderOf(party));
            Assert.AreEqual(0, _presence.TrackedCount);

            Disc(A, true);
            StepRange(T0 + 150u, T0 + 249u);
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));

            Step(T0 + 250u);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
        }

        // ── AC-3 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_ghost_reconnect_is_online_same_index_and_not_leader_again()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(A, true);
            StepRange(T0, T0 + GRACE);
            _sinks.Events.Clear();
            Disc(A, false);

            Step(T0 + GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(0, Party(party).IndexOf(Ch(A)));
            Assert.AreEqual(Ch(B), LeaderOf(party));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(PartyMemberStatus.Online, _sinks.LastState(B).Member0.Status);
        }

        [Test]
        public void test_disconnect_presence_dead_member_ghosts_and_returns_to_dead_on_reconnect()
        {
            MakeParty(A, B, C);
            var sink = new PartyMemberStatusSink(_registry);
            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Dead);
            Disc(B, true);

            StepRange(T0, T0 + GRACE);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Disc(B, false);
            Step(T0 + GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.Dead, _registry.GetMemberStatus(Ch(B)));
        }

        // ── AC-4 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_release_of_one_of_three_sends_one_update_without_it_and_no_solo_update()
        {
            PartyID party = MakeParty(A, B, C);

            Assert.IsTrue(_pool.ReleasePlayer(_entities[C]));
            _broadcast.Flush();

            Assert.IsFalse(_registry.IsRegistered(Ch(C)));
            Assert.AreEqual(2, Party(party).MemberCount);
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(2, _sinks.LastState(A).MemberCount);
            Assert.AreEqual(0, _sinks.Count(Kind.State, C));
            Assert.AreEqual(2, _sinks.Count(Kind.State));
        }

        [Test]
        public void test_disconnect_presence_release_of_the_leader_of_three_transfers_leadership()
        {
            PartyID party = MakeParty(A, B, C);

            _pool.ReleasePlayer(_entities[A]);
            _broadcast.Flush();

            Assert.AreEqual(Ch(B), LeaderOf(party));
            Assert.AreEqual(0, _sinks.LastState(B).LeaderIndex);
        }

        [Test]
        public void test_disconnect_presence_release_from_a_party_of_two_disbands_with_notice_and_solo_update()
        {
            PartyID party = MakeParty(A, B);

            _pool.ReleasePlayer(_entities[B]);
            _broadcast.Flush();

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(1, _sinks.Count(Kind.Disbanded, A));
            Assert.AreEqual(party.RawValue, _sinks.Events[0].PartyId);
            Assert.AreEqual(1, _registry.PartyCount);
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.IsTrue(_sinks.LastState(A).IsSoloParty);
            Assert.AreEqual(0, _sinks.Count(Kind.State, B));
            Assert.IsFalse(_registry.IsRegistered(Ch(B)));
        }

        [Test]
        public void test_disconnect_presence_all_ghost_party_of_two_is_cleaned_up_as_slots_expire()
        {
            MakeParty(A, B);
            Disc(A, true);
            Disc(B, true);
            LogAssert.Expect(LogType.Log, new Regex("leadership not transferred"));
            StepRange(T0, T0 + GRACE);
            Assert.AreEqual(1, _registry.PartyCount, "one real party");

            _pool.ReleasePlayer(_entities[A]);
            _broadcast.Flush();

            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded, B), "a Ghost gets no notice");
            Assert.AreEqual(1, _registry.PartyCount, "B is solo now");
            _pool.ReleasePlayer(_entities[B]);
            Assert.AreEqual(0, _registry.PartyCount);
            Assert.AreEqual(0, _presence.TrackedCount);
        }

        [Test]
        public void test_disconnect_presence_release_of_a_solo_player_frees_its_party_and_unknown_entity_is_a_no_op()
        {
            Join(A);
            Assert.AreEqual(1, _registry.PartyCount);

            _pool.ReleasePlayer(_entities[A]);
            new PartySlotReleaseListener(_registry, _invites, _membership, _presence).BeforeRelease(new EntityID(999999u));

            Assert.AreEqual(0, _registry.PartyCount);
            Assert.IsFalse(_registry.IsRegistered(Ch(A)));
        }

        // ── AC-5 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_try_get_formal_party_is_true_only_for_a_real_party()
        {
            PartyID party = MakeParty(A, B);
            Join(C);

            Assert.IsTrue(_zonePresence.TryGetFormalParty(Ch(A), out PartyID found));
            Assert.AreEqual(party, found);
            Assert.IsFalse(_zonePresence.TryGetFormalParty(Ch(C), out PartyID solo));
            Assert.AreEqual(PartyID.Uninitialized, solo);
            Assert.IsFalse(_zonePresence.TryGetFormalParty(Ch(99u), out _));
        }

        [Test]
        public void test_disconnect_presence_zone_exit_sets_out_of_zone_and_entry_clears_it()
        {
            PartyID party = MakeParty(A, B, C);

            _zonePresence.NotifyZoneExit(party, _entities[B]);
            _broadcast.Flush();

            Assert.AreEqual(PartyMemberStatus.OutOfZone, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
            Assert.AreEqual(PartyMemberStatus.OutOfZone, _sinks.LastState(A).Member1.Status);
            Assert.IsFalse(_eligibility.IsXpEligible(party, Ch(B)));
            _sinks.Events.Clear();

            _zonePresence.NotifyZoneEntry(party, _entities[B], Zone);
            _broadcast.Flush();

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(1, _sinks.Count(Kind.State, A));
        }

        [Test]
        public void test_disconnect_presence_zone_notifications_for_unknown_party_or_entity_do_nothing()
        {
            PartyID party = MakeParty(A, B);
            uint sequence = Party(party).SequenceId;

            _zonePresence.NotifyZoneExit(new PartyID(99999u), _entities[A]);
            _zonePresence.NotifyZoneExit(party, new EntityID(999999u));
            _zonePresence.NotifyZoneEntry(new PartyID(99999u), _entities[A], Zone);

            Assert.AreEqual(sequence, Party(party).SequenceId);
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));
        }

        [Test]
        public void test_disconnect_presence_zone_teardown_leaves_no_parties_and_no_presence_entries()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(C, true);
            Step(T0);
            Assert.AreEqual(1, _presence.TrackedCount);

            foreach (uint n in new[] { A, B, C })
            {
                _zonePresence.NotifyZoneExit(party, _entities[n]);
            }

            foreach (uint n in new[] { A, B, C })
            {
                Assert.IsTrue(_pool.ReleasePlayer(_entities[n]));
            }

            _broadcast.Flush();

            Assert.AreEqual(0, _registry.PartyCount);
            Assert.AreEqual(0, _presence.TrackedCount);
            Assert.AreEqual(0, _sinks.Count(Kind.Disbanded, C), "a Ghost gets no notice");
        }

        [Test]
        public void test_disconnect_presence_zone_exit_with_another_partys_id_does_nothing()
        {
            PartyID first = MakeParty(A, B);
            PartyID second = MakeParty(C, D);
            uint sequence = Party(first).SequenceId;

            _zonePresence.NotifyZoneExit(second, _entities[A]);

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(sequence, Party(first).SequenceId);
        }

        // ── AC-6 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_entry_registers_a_solo_party()
        {
            Join(A);

            Assert.IsTrue(_registry.IsRegistered(Ch(A)));
            Assert.AreNotEqual(PartyID.Uninitialized, _registry.GetPartyID(Ch(A)));
            Assert.IsTrue(Party(_registry.GetPartyID(Ch(A))).IsSoloParty);
        }

        [Test]
        public void test_disconnect_presence_second_entry_for_the_same_character_warns_once_and_changes_nothing()
        {
            EntityID entity = Join(A);
            PartyID before = _registry.GetPartyID(Ch(A));
            LogAssert.Expect(LogType.Warning, new Regex("registering"));

            _entry.OnPlayerEntering(0, entity);

            Assert.AreEqual(before, _registry.GetPartyID(Ch(A)));
            Assert.AreEqual(1, _registry.PartyCount);
        }

        [Test]
        public void test_disconnect_presence_token_reconnect_rebind_keeps_the_registration()
        {
            EntityID entity = Join(A);
            PartyID before = _registry.GetPartyID(Ch(A));

            Assert.IsTrue(_pool.RebindClient(entity, 4242u));

            Assert.AreEqual(before, _registry.GetPartyID(Ch(A)));
            Assert.AreEqual(1, _registry.PartyCount);
        }

        // ── AC-7 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_released_target_tells_the_connected_inviter_expired_once()
        {
            Join(A);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[D]);

            Assert.AreEqual(1, _sinks.Count(Kind.Result, A));
            Assert.AreEqual(PartyInviteResultCode.Expired, _sinks.Events[0].Code);
            Assert.AreEqual(D, _sinks.Events[0].Target);
            Assert.IsFalse(_invites.HasPending(Ch(D)));
        }

        [Test]
        public void test_disconnect_presence_released_inviter_removes_the_entry_and_tells_the_target_nothing()
        {
            Join(A);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[A]);

            Assert.AreEqual(0, _sinks.Events.Count);
            Assert.IsFalse(_invites.HasPending(Ch(D)));
        }

        [Test]
        public void test_disconnect_presence_released_inviter_promotes_the_queued_invite()
        {
            Join(A);
            Join(C);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _invites.HandleInvite(_entities[C], Ch(D), T0);
            Assert.AreEqual(1, _invites.GetQueuedCount(Ch(D)));

            _pool.ReleasePlayer(_entities[A]);

            Assert.IsTrue(_invites.HasPending(Ch(D)));
            Assert.AreEqual(0, _invites.GetQueuedCount(Ch(D)));
        }

        [Test]
        public void test_disconnect_presence_invites_survive_the_grace_and_are_cancelled_at_ghost()
        {
            MakeParty(A, B);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _sinks.Events.Clear();
            Disc(A, true);

            StepRange(T0, T0 + GRACE - 1u);
            Assert.IsTrue(_invites.HasPending(Ch(D)), "kept within the grace");

            Step(T0 + GRACE);

            Assert.IsFalse(_invites.HasPending(Ch(D)));
            Assert.AreEqual(0, _sinks.Count(Kind.Result));
        }

        // ── AC-8 ───────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_status_is_derived_ghost_over_out_of_zone_over_dead()
        {
            MakeParty(A, B);
            var sink = new PartyMemberStatusSink(_registry);

            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Dead);
            _registry.SetGhost(Ch(B), true);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));

            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Online);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)), "respawn while Ghost");

            _registry.SetGhost(Ch(B), false);
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));

            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Dead);
            _registry.SetOutOfZone(Ch(B), true);
            Assert.AreEqual(PartyMemberStatus.OutOfZone, _registry.GetMemberStatus(Ch(B)));
            _registry.SetOutOfZone(Ch(B), false);
            Assert.AreEqual(PartyMemberStatus.Dead, _registry.GetMemberStatus(Ch(B)));
        }

        // ── Allocation ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_poll_allocates_nothing_with_four_parties_of_four()
        {
            uint next = 1u;
            for (int p = 0; p < ALLOC_PARTIES; p++)
            {
                MakeParty(next, next + 1u, next + 2u, next + 3u);
                next += 4u;
            }

            _presence.Poll(T0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint t = 1; t <= ALLOC_TICKS; t++)
            {
                _presence.Poll(T0 + t);
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ── Production adapter ─────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_session_machine_source_reads_connected_and_ignores_missing_sessions()
        {
            var sessions = new ConnectionStateMachine();
            var source = new SessionMachinePartySessionStateSource(_pool, sessions);
            EntityID entity = Join(A);
            Assert.IsFalse(source.TryIsDisconnected(entity, out _), "no session yet");

            uint account = A + ACCOUNT_OFFSET;
            sessions.EnterConnecting(account, T0);
            sessions.CompleteAuthSuccess(account, A, T0);

            Assert.IsTrue(source.TryIsDisconnected(entity, out bool disconnected));
            Assert.IsFalse(disconnected);
            Assert.IsFalse(source.TryIsDisconnected(new EntityID(999999u), out _), "no slot");
        }

        // ── Real session machine ───────────────────────────────────────────────────────────

        private const uint SESSION_CONNECT_TICK = 100u;
        private const uint SESSION_ACTIVE_TICK = 290u;
        private const uint SESSION_TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TICKS = 60u;
        private const uint CONNECTING_TICKS = 200u;

        private static uint Acc(uint n) => n + ACCOUNT_OFFSET;

        private static void ConnectSession(ConnectionStateMachine sm, uint n)
        {
            sm.EnterConnecting(Acc(n), SESSION_CONNECT_TICK);
            sm.CompleteAuthSuccess(Acc(n), n, SESSION_CONNECT_TICK);
        }

        [Test]
        public void test_disconnect_presence_session_machine_source_maps_every_session_state()
        {
            var sm = new ConnectionStateMachine();
            var source = new SessionMachinePartySessionStateSource(_pool, sm);
            foreach (uint n in new[] { A, B, C, D }) Join(n);
            ConnectSession(sm, A);
            ConnectSession(sm, C);
            sm.EvaluateTimeouts(SESSION_TIMEOUT_TICK, HEARTBEAT_TICKS, CONNECTING_TICKS);
            Assert.IsTrue(sm.TryGetSessionExpiryTick(Acc(C), out uint expiry));
            sm.EnterReconnecting(Acc(C), expiry);
            sm.EnterConnecting(Acc(B), SESSION_TIMEOUT_TICK);
            sm.CompleteAuthSuccess(Acc(B), B, SESSION_TIMEOUT_TICK);
            Assert.IsTrue(sm.TryGetSessionState(Acc(A), out SessionState a));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, a);
            Assert.IsTrue(sm.TryGetSessionState(Acc(C), out SessionState c));
            Assert.AreEqual(SessionState.Reconnecting, c);

            Assert.IsTrue(source.TryIsDisconnected(_entities[A], out bool aDisc));
            Assert.IsTrue(aDisc);
            Assert.IsTrue(source.TryIsDisconnected(_entities[C], out bool cDisc));
            Assert.IsTrue(cDisc);
            Assert.IsTrue(source.TryIsDisconnected(_entities[B], out bool bDisc));
            Assert.IsFalse(bDisc);

            sm.EnterConnecting(Acc(D), SESSION_TIMEOUT_TICK);
            Assert.IsTrue(sm.TryGetSessionState(Acc(D), out SessionState d));
            Assert.AreEqual(SessionState.Connecting, d);
            Assert.IsFalse(source.TryIsDisconnected(_entities[D], out _), "Connecting is ignored");
        }

        [Test]
        public void test_disconnect_presence_poll_through_the_real_session_machine_ghosts_a_reconnecting_member_at_100_ticks()
        {
            var sm = new ConnectionStateMachine();
            MakeParty(A, B, C);
            foreach (uint n in new[] { A, B, C }) ConnectSession(sm, n);
            sm.RecordInboundActivity(Acc(A), SESSION_ACTIVE_TICK);
            sm.RecordInboundActivity(Acc(C), SESSION_ACTIVE_TICK);
            sm.EvaluateTimeouts(SESSION_TIMEOUT_TICK, HEARTBEAT_TICKS, CONNECTING_TICKS);
            Assert.IsTrue(sm.TryGetSessionExpiryTick(Acc(B), out uint expiry));
            sm.EnterReconnecting(Acc(B), expiry);
            var presence = new PartyPresenceStep(_registry, new SessionMachinePartySessionStateSource(_pool, sm),
                _invites, Config());

            for (uint t = T0; t < T0 + GRACE; t++) presence.Poll(t);
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)), "not Ghost at +99");

            presence.Poll(T0 + GRACE);

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(A)));
        }

        // ── Leader and release edge cases ──────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_leader_ghost_with_another_ghost_hands_leadership_to_the_connected_member()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(B, true);
            StepRange(T0, T0 + GRACE);
            _sinks.Events.Clear();
            Disc(A, true);

            StepRange(T0 + GRACE + 1u, T0 + 2u * GRACE + 1u);

            Assert.AreEqual(Ch(C), LeaderOf(party));
            Assert.AreEqual(1, _sinks.Count(Kind.State, C));
            Assert.AreEqual(1, _sinks.Count(Kind.State));
            Assert.AreEqual(2, _sinks.LastState(C).LeaderIndex);
        }

        [Test]
        public void test_disconnect_presence_release_of_a_ghost_leader_with_another_ghost_picks_the_connected_member()
        {
            PartyID party = MakeParty(A, B, C);
            _registry.SetGhost(Ch(A), true);
            _registry.SetGhost(Ch(B), true);

            _pool.ReleasePlayer(_entities[A]);

            Assert.AreEqual(Ch(C), LeaderOf(party));
        }

        [Test]
        public void test_disconnect_presence_release_during_the_grace_forgets_the_record_and_later_polls_are_silent()
        {
            MakeParty(A, B, C);
            Disc(C, true);
            Step(T0);
            Assert.AreEqual(1, _presence.TrackedCount);
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[C]);
            Assert.AreEqual(0, _presence.TrackedCount);
            _broadcast.Flush();
            _sinks.Events.Clear();
            StepRange(T0 + 1u, T0 + GRACE + 5u);

            Assert.AreEqual(0, _sinks.Events.Count);
        }

        [Test]
        public void test_disconnect_presence_grace_survives_the_tick_counter_wrapping()
        {
            PartyID party = MakeParty(A, B, C);
            uint since = uint.MaxValue - 10u;
            Disc(B, true);

            for (uint i = 0; i < GRACE; i++) Step(unchecked(since + i));
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)), "not Ghost at since + 99");

            Step(unchecked(since + GRACE));

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(Ch(A), LeaderOf(party));
        }

        [Test]
        public void test_disconnect_presence_reconnecting_member_regains_leadership_when_the_leader_and_the_rest_were_ghost()
        {
            PartyID party = MakeParty(A, B);
            Disc(B, true);
            Disc(A, false);
            Step(T0);
            Disc(A, true);
            LogAssert.Expect(LogType.Log, new Regex("leadership not transferred"));
            StepRange(T0 + 1u, T0 + GRACE + 50u);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(Ch(A), LeaderOf(party));
            _sinks.Events.Clear();
            Disc(B, false);

            Step(T0 + GRACE + 51u);

            Assert.AreEqual(Ch(B), LeaderOf(party));
            Assert.AreEqual(1, _sinks.Count(Kind.State, B));
            Assert.AreEqual(1, _sinks.Count(Kind.State));
        }

        // ── Status flags across transitions ────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_member_resoloed_while_ghost_is_online_and_connected_again()
        {
            PartyID party = MakeParty(A, B);
            _registry.SetGhost(Ch(B), true);

            _pool.ReleasePlayer(_entities[A]);

            Assert.IsFalse(_registry.TryGetParty(party, out _));
            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));
            Assert.IsTrue(_registry.IsMemberConnected(Ch(B)));
        }

        [Test]
        public void test_disconnect_presence_ghost_and_out_of_zone_members_are_online_after_a_leader_disband_but_dead_stays()
        {
            MakeParty(A, B, C);
            _registry.SetGhost(Ch(B), true);
            _registry.SetDead(Ch(C), true);
            _membership.EnqueueDisband(Ch(A));
            _membership.ApplyQueued(T0);

            _membership.ApplyQueued(T0 + (uint)Config().DisbandTimeoutTicks);

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));
            Assert.AreEqual(PartyMemberStatus.Dead, _registry.GetMemberStatus(Ch(C)));
        }

        [Test]
        public void test_disconnect_presence_out_of_zone_member_returns_to_out_of_zone_after_a_ghost_reconnect()
        {
            PartyID party = MakeParty(A, B, C);
            _zonePresence.NotifyZoneExit(party, _entities[B]);
            Disc(B, true);
            StepRange(T0, T0 + GRACE);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Disc(B, false);

            Step(T0 + GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.OutOfZone, _registry.GetMemberStatus(Ch(B)));
        }

        [Test]
        public void test_disconnect_presence_zone_entry_while_ghost_keeps_ghost_until_reconnect()
        {
            PartyID party = MakeParty(A, B, C);
            Disc(B, true);
            StepRange(T0, T0 + GRACE);

            _zonePresence.NotifyZoneEntry(party, _entities[B], Zone);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Disc(B, false);
            Step(T0 + GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));
        }

        [Test]
        public void test_disconnect_presence_respawn_while_ghost_stays_ghost_and_reconnect_is_online()
        {
            MakeParty(A, B, C);
            var sink = new PartyMemberStatusSink(_registry);
            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Dead);
            Disc(B, true);
            StepRange(T0, T0 + GRACE);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));

            sink.SetMemberStatus(_entities[B], PartyMemberDeathStatus.Online);
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(B)));
            Disc(B, false);
            Step(T0 + GRACE + 1u);

            Assert.AreEqual(PartyMemberStatus.Online, _registry.GetMemberStatus(Ch(B)));
        }

        // ── Invites ────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_released_target_notifies_the_pending_and_every_queued_inviter_once()
        {
            Join(A);
            Join(C);
            Join(B);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _invites.HandleInvite(_entities[C], Ch(D), T0);
            _invites.HandleInvite(_entities[B], Ch(D), T0);
            Assert.AreEqual(2, _invites.GetQueuedCount(Ch(D)));
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[D]);

            foreach (uint inviter in new[] { A, C, B })
            {
                Assert.AreEqual(1, _sinks.Count(Kind.Result, inviter));
            }

            Assert.AreEqual(3, _sinks.Events.Count);
            foreach (Event e in _sinks.Events)
            {
                Assert.AreEqual(PartyInviteResultCode.Expired, e.Code);
                Assert.AreEqual(D, e.Target);
            }

            Assert.IsFalse(_invites.HasPending(Ch(D)));
            Assert.AreEqual(0, _invites.GetQueuedCount(Ch(D)));
        }

        [Test]
        public void test_disconnect_presence_ghost_queued_inviter_is_not_notified_when_the_target_vanishes()
        {
            Join(A);
            MakeParty(C, B);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _invites.HandleInvite(_entities[C], Ch(D), T0);
            _registry.SetGhost(Ch(C), true);
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[D]);

            Assert.AreEqual(1, _sinks.Count(Kind.Result, A));
            Assert.AreEqual(0, _sinks.Count(Kind.Result, C));
            Assert.AreEqual(1, _sinks.Events.Count);
        }

        [Test]
        public void test_disconnect_presence_promoted_invite_after_the_inviter_is_released_times_out_at_its_deadline()
        {
            const uint inviteTick = 2000u;
            uint timeout = (uint)Config().InviteTimeoutTicks;
            Join(A);
            Join(C);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), inviteTick);
            _invites.HandleInvite(_entities[C], Ch(D), inviteTick);
            _sinks.Events.Clear();

            _pool.ReleasePlayer(_entities[A]);

            Assert.IsTrue(_invites.HasPending(Ch(D)), "C's invite promoted");
            Assert.AreEqual(0, _invites.GetQueuedCount(Ch(D)));
            _invites.Tick(inviteTick + timeout - 1u);
            Assert.AreEqual(0, _sinks.Count(Kind.Result));

            _invites.Tick(inviteTick + timeout);

            Assert.AreEqual(1, _sinks.Count(Kind.Result, C));
            Assert.AreEqual(PartyInviteResultCode.Expired, _sinks.Events[0].Code);
            Assert.AreEqual(D, _sinks.Events[0].Target);
            Assert.IsFalse(_invites.HasPending(Ch(D)));
        }

        [Test]
        public void test_disconnect_presence_cancel_for_an_unrelated_character_changes_nothing()
        {
            Join(A);
            Join(D);
            _invites.CancelFor(Ch(99u));
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _sinks.Events.Clear();

            _invites.CancelFor(Ch(99u));

            Assert.AreEqual(0, _sinks.Events.Count);
            Assert.IsTrue(_invites.HasPending(Ch(D)));
            Assert.AreEqual(1, _invites.PendingCount);
        }

        // ── Failure isolation ──────────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_a_throwing_invite_cancel_keeps_the_ghost_the_transfer_and_the_other_parties()
        {
            PartyID first = MakeParty(A, B);
            PartyID second = MakeParty(5u, 6u);
            Join(C);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _invites.HandleInvite(_entities[C], Ch(D), T0);
            _sinks.ThrowOnInviteReceived = true;
            Disc(A, true);
            Disc(6u, true);
            Step(T0);
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            LogAssert.Expect(LogType.Error, new Regex("threw"));

            Step(T0 + GRACE);

            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(A)));
            Assert.AreEqual(Ch(B), LeaderOf(first));
            Assert.AreEqual(PartyMemberStatus.Ghost, _registry.GetMemberStatus(Ch(6u)));
            Assert.AreEqual(2, Party(second).MemberCount);
        }

        [Test]
        public void test_disconnect_presence_a_throwing_invite_sink_on_release_still_unregisters_the_player()
        {
            MakeParty(A, B);
            Join(C);
            Join(D);
            _invites.HandleInvite(_entities[A], Ch(D), T0);
            _invites.HandleInvite(_entities[C], Ch(D), T0);
            Disc(A, true);
            Step(T0);
            Assert.AreEqual(1, _presence.TrackedCount);
            _sinks.ThrowOnInviteReceived = true;
            LogAssert.Expect(LogType.Error, new Regex("release listener threw"));

            _pool.ReleasePlayer(_entities[A]);

            Assert.IsFalse(_registry.IsRegistered(Ch(A)));
            Assert.AreEqual(0, _presence.TrackedCount);
            Assert.IsTrue(Party(_registry.GetPartyID(Ch(B))).IsSoloParty, "the auto-disband ran");
        }

        // ── Entry without a slot ───────────────────────────────────────────────────────────

        [Test]
        public void test_disconnect_presence_entry_for_an_entity_without_a_pool_slot_warns_and_registers_nothing()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no pool slot"));

            _entry.OnPlayerEntering(0, new EntityID(999999u));

            Assert.AreEqual(0, _registry.PartyCount);
        }
    }
}
