using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 010 — <see cref="ZoneEntryCompletion"/>, <see cref="ZoneTickRoster"/>,
    /// <see cref="ZoneConnectionCharacterDirectory"/> and the two stand-ins (CR-ZI-8 steps 6-10). Real pool, snapshot
    /// builder, roster, directory and <see cref="InboundRequestDispatcher"/>; fake outbox, responder, party and state
    /// source. The order test reads one shared call log. The second-handshake guard is tested in
    /// <c>ZoneInstancing_EntryAdmission_tests.cs</c> where the admission fakes live.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_EntryCompletion_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint EXISTING_CLIENT = 10u;
        private const uint JOINER_CLIENT = 11u;
        private const uint THIRD_CLIENT = 12u;
        private const uint PARTY_RAW = 77u;
        private const uint TICK = 100u;
        private const string EXISTING_NAME = "Brom";
        private const string JOINER_NAME = "Ayla";
        private const int EXISTING_LEVEL = 3;
        private const int JOINER_LEVEL = 7;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeMobs : IZoneMobRoster
        {
            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition) => EntityID.Invalid;

            public void RemoveMob(ZoneID zone, EntityID id) { }

            public int GetMobCount(ZoneID zone) => 0;

            public bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition)
            {
                type = default;
                spawnPosition = Vector3.zero;
                return false;
            }
        }

        private sealed class FakeState : IZoneEntityStateSource
        {
            public readonly Dictionary<EntityID, ZonePlayerStateView> Players = new Dictionary<EntityID, ZonePlayerStateView>();

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view) => Players.TryGetValue(id, out view);

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return false;
            }
        }

        /// <summary>Appends one entry per message to the shared log and keeps decoded PlayerJoinedZone messages.</summary>
        private sealed class FakeOutbox : IClientMessageOutbox
        {
            private readonly List<string> _log;
            public readonly List<KeyValuePair<uint, PlayerJoinedZone>> Joined = new List<KeyValuePair<uint, PlayerJoinedZone>>();
            public bool ThrowOnJoined;

            public FakeOutbox(List<string> log) => _log = log;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (messageTypeId == ZoneStateSnapshotFragment.MessageTypeId)
                {
                    _log.Add("frag:" + clientId);
                }
                else if (messageTypeId == PlayerJoinedZone.MessageTypeId)
                {
                    if (ThrowOnJoined)
                    {
                        throw new InvalidOperationException("enqueue failed");
                    }

                    Assert.IsTrue(PlayerJoinedZoneCodec.TryReadBody(body, out PlayerJoinedZone message));
                    Joined.Add(new KeyValuePair<uint, PlayerJoinedZone>(clientId, message));
                    _log.Add("joined:" + clientId);
                }
                else
                {
                    Assert.Fail("unexpected message type 0x" + messageTypeId.ToString("X4"));
                }
            }
        }

        private sealed class FakeResponder : IZoneEntryResponder
        {
            private readonly List<string> _log;
            public bool ThrowOnSessionReady;

            public FakeResponder(List<string> log) => _log = log;

            public void SendSessionReady(uint clientId)
            {
                if (ThrowOnSessionReady)
                {
                    throw new InvalidOperationException("session ready failed");
                }

                _log.Add("ready:" + clientId);
            }

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId) =>
                _log.Add("cooldown:" + clientId + ":" + entityId.RawValue);
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            private readonly List<string> _log;
            public bool InParty;
            public bool ThrowOnEntry;
            public readonly List<ZoneID> EntryZones = new List<ZoneID>();
            public int ExitCalls;

            public FakeParty(List<string> log) => _log = log;

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = InParty ? new PartyID(PARTY_RAW) : PartyID.Uninitialized;
                return InParty;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone)
            {
                if (ThrowOnEntry)
                {
                    throw new InvalidOperationException("party notify failed");
                }

                EntryZones.Add(zone);
                _log.Add("party:" + party.RawValue + ":" + entityId.RawValue);
            }

            public void NotifyZoneExit(PartyID party, EntityID entityId) => ExitCalls++;
        }

        private List<string> _log;
        private FakeOutbox _outbox;
        private FakeResponder _responder;
        private FakeParty _party;
        private FakeState _state;
        private ZoneSlotPool _pool;
        private ZoneConnectionCharacterDirectory _directory;
        private ZoneTickRoster _roster;
        private ZoneSnapshotBuilder _snapshot;
        private ZoneEntryCompletion _completion;
        private uint _nextCharacter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _outbox = new FakeOutbox(_log);
            _responder = new FakeResponder(_log);
            _party = new FakeParty(_log);
            _state = new FakeState();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener());
            _directory = new ZoneConnectionCharacterDirectory();
            _roster = new ZoneTickRoster(_pool, new FakeTicks());
            _snapshot = new ZoneSnapshotBuilder(Zone, _pool, new FakeMobs(), _state, new FakeTicks(), _outbox);
            _completion = new ZoneEntryCompletion(Zone, _pool, _directory, _snapshot, _responder, _party, _state, _outbox, _roster);
            _nextCharacter = 1000u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private static ZonePlayerStateView View(string name, int level, short x, short y, short z) =>
            new ZonePlayerStateView(x, y, z, 0, 0, 0, 0, 0, 100, 100, 50, 50, level, false, 0, 0, name, 0u);

        // Allocates a slot, optionally with state data; does not run the completion.
        private EntityID Allocate(uint clientId, string name, int level, bool withState, out byte slot, out CharacterID character)
        {
            uint n = _nextCharacter++;
            character = new CharacterID(n);
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, character, new AccountID(n + 5000u), clientId, out slot, out EntityID id));
            if (withState)
            {
                _state.Players[id] = View(name, level, (short)(n % 1000), (short)(-(int)(n % 100)), 300);
            }

            return id;
        }

        // An already-present player: allocated, with state, and admitted (so it sits in the zone).
        private EntityID AddExisting(uint clientId, string name = EXISTING_NAME, int level = EXISTING_LEVEL)
        {
            return Allocate(clientId, name, level, true, out _, out _);
        }

        private EntityID Admit(uint clientId, string name = JOINER_NAME, int level = JOINER_LEVEL, bool withState = true)
        {
            EntityID id = Allocate(clientId, name, level, withState, out byte slot, out _);
            _completion.OnPlayerAdmitted(clientId, slot, id);
            return id;
        }

        private static Regex Rx(string pattern) => new Regex(pattern);

        private List<string> NonFragmentLog()
        {
            var rest = new List<string>();
            foreach (string entry in _log)
            {
                if (!entry.StartsWith("frag:", StringComparison.Ordinal))
                {
                    rest.Add(entry);
                }
            }

            return rest;
        }

        // ----- order ----------------------------------------------------------------------------

        [Test]
        public void test_order_snapshot_then_session_ready_then_cooldown_then_party_then_player_joined()
        {
            AddExisting(EXISTING_CLIENT);
            _party.InParty = true;

            EntityID joiner = Admit(JOINER_CLIENT);

            Assert.IsTrue(_log.Count >= 5, "log: " + string.Join(",", _log));
            int fragments = 0;
            while (fragments < _log.Count && _log[fragments] == "frag:" + JOINER_CLIENT)
            {
                fragments++;
            }

            Assert.GreaterOrEqual(fragments, 1, "snapshot fragments come first, to the joiner");
            var expected = new List<string>
            {
                "ready:" + JOINER_CLIENT,
                "cooldown:" + JOINER_CLIENT + ":" + joiner.RawValue,
                "party:" + PARTY_RAW + ":" + joiner.RawValue,
                "joined:" + EXISTING_CLIENT,
            };
            Assert.AreEqual(expected, _log.GetRange(fragments, _log.Count - fragments));
            Assert.AreEqual(1, _party.EntryZones.Count);
            Assert.AreEqual(Zone, _party.EntryZones[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_solo_player_gets_no_party_notification_and_the_rest_of_the_order_is_unchanged()
        {
            AddExisting(EXISTING_CLIENT);
            _party.InParty = false;

            EntityID joiner = Admit(JOINER_CLIENT);

            Assert.AreEqual(0, _party.EntryZones.Count);
            Assert.AreEqual(new List<string>
            {
                "ready:" + JOINER_CLIENT,
                "cooldown:" + JOINER_CLIENT + ":" + joiner.RawValue,
                "joined:" + EXISTING_CLIENT,
            }, NonFragmentLog());
            Assert.AreEqual("frag:" + JOINER_CLIENT, _log[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_joiner_receives_no_player_joined_zone_and_every_other_player_gets_one()
        {
            AddExisting(EXISTING_CLIENT);
            AddExisting(THIRD_CLIENT, "Cade", 5);

            Admit(JOINER_CLIENT);

            Assert.AreEqual(2, _outbox.Joined.Count);
            var recipients = new HashSet<uint> { _outbox.Joined[0].Key, _outbox.Joined[1].Key };
            Assert.IsTrue(recipients.Contains(EXISTING_CLIENT));
            Assert.IsTrue(recipients.Contains(THIRD_CLIENT));
            Assert.IsFalse(recipients.Contains(JOINER_CLIENT));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_first_player_alone_in_the_zone_sends_no_player_joined_zone()
        {
            Admit(JOINER_CLIENT);

            Assert.IsEmpty(_outbox.Joined);
            Assert.AreEqual("frag:" + JOINER_CLIENT, _log[0]);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- stand-ins ------------------------------------------------------------------------

        [Test]
        public void test_stand_ins_log_once_each_per_instance_over_two_admissions()
        {
            _completion = new ZoneEntryCompletion(Zone, _pool, _directory, _snapshot, new LoggingZoneEntryResponder(),
                new NoPartyZonePresence(), _state, _outbox, _roster);
            LogAssert.Expect(LogType.Warning, LoggingZoneEntryResponder.NOT_IMPLEMENTED_MESSAGE);
            LogAssert.Expect(LogType.Warning, NoPartyZonePresence.NOT_IMPLEMENTED_MESSAGE);

            Admit(EXISTING_CLIENT, EXISTING_NAME, EXISTING_LEVEL);
            Admit(JOINER_CLIENT);

            Assert.AreEqual(1, _outbox.Joined.Count, "the second admission still reaches the first player");
            LogAssert.NoUnexpectedReceived();
        }

        // ----- PlayerJoinedZone data ------------------------------------------------------------

        [Test]
        public void test_player_joined_zone_carries_name_level_and_position_of_the_state_source()
        {
            AddExisting(EXISTING_CLIENT);

            EntityID joiner = Admit(JOINER_CLIENT);

            Assert.AreEqual(1, _outbox.Joined.Count);
            Assert.AreEqual(EXISTING_CLIENT, _outbox.Joined[0].Key);
            PlayerJoinedZone message = _outbox.Joined[0].Value;
            _state.Players.TryGetValue(joiner, out ZonePlayerStateView view);
            Assert.AreEqual(joiner, message.EntityId);
            Assert.AreEqual(JOINER_NAME, message.CharacterName);
            Assert.AreEqual(JOINER_LEVEL, message.Level);
            Assert.AreEqual(view.PosX, message.PosX);
            Assert.AreEqual(view.PosY, message.PosY);
            Assert.AreEqual(view.PosZ, message.PosZ);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_state_source_miss_warns_once_sends_no_player_joined_zone_and_still_activates_next_tick()
        {
            AddExisting(EXISTING_CLIENT);
            _party.InParty = true;
            LogAssert.Expect(LogType.Warning, Rx("SnapshotEntitySkipped"));
            LogAssert.Expect(LogType.Warning, Rx(@"\[ZoneEntryCompletion\] PlayerJoinedZoneSkipped"));

            EntityID joiner = Admit(JOINER_CLIENT, withState: false);

            Assert.IsEmpty(_outbox.Joined);
            Assert.Contains("ready:" + JOINER_CLIENT, _log, "steps 7 still run");
            Assert.AreEqual(1, _party.EntryZones.Count, "step 8 still runs");
            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out _), "the directory entry is added");
            Assert.IsFalse(_roster.IsInTickLoop(joiner));
            _roster.ActivatePending(TICK + 1u);
            Assert.IsTrue(_roster.IsInTickLoop(joiner));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- directory ------------------------------------------------------------------------

        [Test]
        public void test_directory_maps_the_connection_to_the_slot_character_after_admission()
        {
            EntityID id = Allocate(JOINER_CLIENT, JOINER_NAME, JOINER_LEVEL, true, out byte slot, out CharacterID character);
            Assert.IsFalse(_directory.TryGetCharacterId(JOINER_CLIENT, out _));

            _completion.OnPlayerAdmitted(JOINER_CLIENT, slot, id);

            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out CharacterID found));
            Assert.AreEqual(character, found);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_directory_remove_deletes_the_entry_and_unknown_remove_changes_nothing()
        {
            _directory.Add(JOINER_CLIENT, new CharacterID(5u));

            Assert.IsTrue(_directory.Remove(JOINER_CLIENT));
            Assert.IsFalse(_directory.TryGetCharacterId(JOINER_CLIENT, out CharacterID gone));
            Assert.AreEqual(CharacterID.Invalid, gone);
            Assert.IsFalse(_directory.Remove(JOINER_CLIENT));
            Assert.AreEqual(0, _directory.Count);
        }

        [Test]
        public void test_directory_add_for_a_mapped_connection_replaces_the_character()
        {
            _directory.Add(JOINER_CLIENT, new CharacterID(5u));

            _directory.Add(JOINER_CLIENT, new CharacterID(6u));

            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out CharacterID found));
            Assert.AreEqual(new CharacterID(6u), found);
            Assert.AreEqual(1, _directory.Count);
        }

        [Test]
        public void test_dispatcher_drops_requests_as_no_character_until_admission_then_dispatches_then_drops_after_remove()
        {
            var guard = new CrossCuttingRpcGuardChain();
            var routing = new InboundFakeRoutingTable();
            var handler = new InboundRecordingHandler();
            var dispatcher = new InboundRequestDispatcher(guard, new CharacterMutationGate(), _directory,
                new InboundRecordingActivitySink(), new InboundSettableTickSource(), routing.TryGetEntry, false);
            dispatcher.Register(InboundTestIds.PlainRod(), handler.Handle);
            dispatcher.Seal();
            Assert.IsTrue(dispatcher.AddConnection(JOINER_CLIENT));
            EntityID id = Allocate(JOINER_CLIENT, JOINER_NAME, JOINER_LEVEL, true, out byte slot, out _);
            guard.RegisterEntityOwnership(JOINER_CLIENT, id);
            guard.MarkSessionReady(JOINER_CLIENT);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne);

            Assert.IsTrue(dispatcher.TryAccept(JOINER_CLIENT, InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, 1u, id.RawValue, body)));
            LogAssert.Expect(LogType.Warning, Rx("InboundRequestWithoutCharacter"));
            dispatcher.DispatchTick(1u);
            Assert.AreEqual(0, handler.Calls.Count, "no character yet");

            _completion.OnPlayerAdmitted(JOINER_CLIENT, slot, id);
            Assert.IsTrue(dispatcher.TryAccept(JOINER_CLIENT, InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, 2u, id.RawValue, body)));
            dispatcher.DispatchTick(2u);
            Assert.AreEqual(1, handler.Calls.Count, "dispatched after admission");

            _directory.Remove(JOINER_CLIENT);
            Assert.IsTrue(dispatcher.TryAccept(JOINER_CLIENT, InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, 3u, id.RawValue, body)));
            LogAssert.Expect(LogType.Warning, Rx("InboundRequestWithoutCharacter"));
            dispatcher.DispatchTick(3u);
            Assert.AreEqual(1, handler.Calls.Count, "dropped again after Remove");
            LogAssert.NoUnexpectedReceived();
        }

        // ----- tick entry -----------------------------------------------------------------------

        [Test]
        public void test_entity_is_not_in_tick_loop_in_the_admission_tick_and_is_after_the_next_activation()
        {
            EntityID id = Admit(JOINER_CLIENT);

            Assert.IsFalse(_roster.IsInTickLoop(id));
            Assert.AreEqual(1, _roster.PendingCount);
            _roster.ActivatePending(TICK + 1u);

            Assert.IsTrue(_roster.IsInTickLoop(id));
            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(1, _roster.ActiveCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_activate_pending_in_the_admission_tick_keeps_the_entity_pending_until_the_next_tick()
        {
            // Admission completes in pipeline step 1 of TICK; step 3 of the same tick calls ActivatePending(TICK).
            EntityID id = Admit(JOINER_CLIENT);

            _roster.ActivatePending(TICK);

            Assert.IsFalse(_roster.IsInTickLoop(id));
            Assert.AreEqual(1, _roster.PendingCount);
            _roster.ActivatePending(TICK + 1u);
            Assert.IsTrue(_roster.IsInTickLoop(id));
            Assert.AreEqual(0, _roster.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_add_pending_twice_or_for_an_active_entity_keeps_a_single_entry()
        {
            EntityID id = Admit(JOINER_CLIENT);

            _roster.AddPending(id);
            Assert.AreEqual(1, _roster.PendingCount);
            _roster.ActivatePending(TICK + 1u);
            _roster.AddPending(id);

            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(1, _roster.ActiveCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_slot_released_before_activation_is_not_activated()
        {
            EntityID id = Admit(JOINER_CLIENT);
            Assert.IsTrue(_pool.ReleasePlayer(id));

            _roster.ActivatePending(TICK + 1u);

            Assert.IsFalse(_roster.IsInTickLoop(id));
            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(0, _roster.ActiveCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_deactivate_removes_an_active_entity_and_a_pending_one()
        {
            EntityID active = Admit(JOINER_CLIENT);
            _roster.ActivatePending(TICK + 1u);
            EntityID pending = Admit(THIRD_CLIENT, "Cade", 5);

            _roster.Deactivate(active);
            _roster.Deactivate(pending);
            _roster.ActivatePending(TICK + 2u);

            Assert.IsFalse(_roster.IsInTickLoop(active));
            Assert.IsFalse(_roster.IsInTickLoop(pending));
            Assert.AreEqual(0, _roster.ActiveCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_activate_pending_twice_is_idempotent()
        {
            EntityID id = Admit(JOINER_CLIENT);

            _roster.ActivatePending(TICK + 1u);
            _roster.ActivatePending(TICK + 2u);

            Assert.IsTrue(_roster.IsInTickLoop(id));
            Assert.AreEqual(1, _roster.ActiveCount);
        }

        // ----- failure --------------------------------------------------------------------------

        [Test]
        public void test_responder_throwing_logs_error_returns_normally_and_skips_party_joined_and_activation()
        {
            AddExisting(EXISTING_CLIENT);
            _party.InParty = true;
            _responder.ThrowOnSessionReady = true;
            EntityID id = Allocate(JOINER_CLIENT, JOINER_NAME, JOINER_LEVEL, true, out byte slot, out _);
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryCompletion\]"));

            Assert.DoesNotThrow(() => _completion.OnPlayerAdmitted(JOINER_CLIENT, slot, id));

            Assert.AreEqual(0, _party.EntryZones.Count);
            Assert.IsEmpty(_outbox.Joined);
            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out _),
                "the directory entry stays; the exit path (Story 013) removes it");
            _roster.ActivatePending(TICK + 1u);
            Assert.IsFalse(_roster.IsInTickLoop(id));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_party_notify_throwing_logs_error_and_skips_player_joined_and_activation()
        {
            AddExisting(EXISTING_CLIENT);
            _party.InParty = true;
            _party.ThrowOnEntry = true;
            EntityID id = Allocate(JOINER_CLIENT, JOINER_NAME, JOINER_LEVEL, true, out byte slot, out _);
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryCompletion\]"));

            Assert.DoesNotThrow(() => _completion.OnPlayerAdmitted(JOINER_CLIENT, slot, id));

            Assert.Contains("ready:" + JOINER_CLIENT, _log, "steps before the throw ran");
            Assert.IsEmpty(_outbox.Joined);
            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out _));
            Assert.AreEqual(0, _roster.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_outbox_throwing_in_step_9_logs_error_and_skips_activation()
        {
            AddExisting(EXISTING_CLIENT);
            AddExisting(THIRD_CLIENT, "Cade", 5);
            _outbox.ThrowOnJoined = true;
            EntityID id = Allocate(JOINER_CLIENT, JOINER_NAME, JOINER_LEVEL, true, out byte slot, out _);
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryCompletion\]"));

            Assert.DoesNotThrow(() => _completion.OnPlayerAdmitted(JOINER_CLIENT, slot, id));

            Assert.IsEmpty(_outbox.Joined);
            Assert.IsTrue(_directory.TryGetCharacterId(JOINER_CLIENT, out _));
            Assert.AreEqual(0, _roster.PendingCount);
            _roster.ActivatePending(TICK + 1u);
            Assert.IsFalse(_roster.IsInTickLoop(id));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unknown_entity_logs_error_and_returns_normally()
        {
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryCompletion\] admitted player"));

            Assert.DoesNotThrow(() => _completion.OnPlayerAdmitted(JOINER_CLIENT, 0, new EntityID(9999u)));

            Assert.IsEmpty(_log);
            Assert.AreEqual(0, _directory.Count);
            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(0, _party.EntryZones.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructor_rejects_invalid_zone_and_null_arguments()
        {
            Assert.Throws<ArgumentException>(() => new ZoneEntryCompletion(ZoneID.Invalid, _pool, _directory, _snapshot,
                _responder, _party, _state, _outbox, _roster));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryCompletion(Zone, null, _directory, _snapshot,
                _responder, _party, _state, _outbox, _roster));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryCompletion(Zone, _pool, _directory, _snapshot,
                _responder, null, _state, _outbox, _roster));
            Assert.Throws<ArgumentNullException>(() => new ZoneTickRoster(null, new FakeTicks()));
            Assert.Throws<ArgumentNullException>(() => new ZoneTickRoster(_pool, null));
        }
    }
}
