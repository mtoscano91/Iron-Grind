using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode tests for Skill System Story 010 - <see cref="SkillCooldownSnapshot"/> codec and
    /// <see cref="SkillCooldownSnapshotResponder"/> (CR-SK-12, CR-SK-23, AC-SK-09, AC-SK-44), over the real
    /// <see cref="SkillSystemService"/> and <see cref="ClientMessageOutbox"/> with a recording send seam. One
    /// integration test runs the real <see cref="ZoneEntryCompletion"/> to prove the entry order.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_CooldownSnapshot_Tests
    {
        private const byte CLASS_FULL = 0;
        private const uint ID_BASE = 100u;
        private const uint CLIENT = 11u;
        private const uint CLIENT_NOT_IN_OUTBOX = 13u;
        private const uint TICK = 4242u;
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 8;
        private const int SLOT_A = 0;
        private const int COOLDOWN_SLOT = 2;
        private const uint COOLDOWN_TICK = 1200u;
        private const int SHORT_COUNT = 3;
        private const int LEVEL_MAX = 60;
        private static readonly int[] UNLOCK_LEVELS = { 1, 1, 5, 15, 20, 25, 35, 42, 50, 58 };

        private static readonly EntityID PLAYER_A = new EntityID(1001);
        private static readonly EntityID UNKNOWN = new EntityID(3001);

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeLevels : ISkillLevelSource
        {
            public int Level = LEVEL_MAX;

            public int GetLevel(EntityID entity) => Level;
        }

        private sealed class NoOpUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
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

        private sealed class RecordingInner : IZoneEntryResponder
        {
            private readonly List<string> _log;
            public int ReadyCalls;
            public int SnapshotCalls;

            public RecordingInner(List<string> log) => _log = log;

            public void SendSessionReady(uint clientId)
            {
                ReadyCalls++;
                _log.Add("ready:" + clientId);
            }

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
            {
                SnapshotCalls++;
                _log.Add("inner-cooldown");
            }
        }

        // Logs the snapshot step after the decorator returns, so the shared log shows the call order.
        private sealed class OrderSpy : IZoneEntryResponder
        {
            private readonly IZoneEntryResponder _next;
            private readonly List<string> _log;

            public OrderSpy(IZoneEntryResponder next, List<string> log)
            {
                _next = next;
                _log = log;
            }

            public void SendSessionReady(uint clientId) => _next.SendSessionReady(clientId);

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
            {
                _next.SendSkillCooldownSnapshot(clientId, entityId);
                _log.Add("cooldown:" + clientId);
            }
        }

        private sealed class LogOutbox : IClientMessageOutbox
        {
            private readonly List<string> _log;

            public LogOutbox(List<string> log) => _log = log;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                _log.Add("frag:" + messageTypeId);
            }
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

        private sealed class FakeParty : IPartyZonePresence
        {
            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = PartyID.Uninitialized;
                return false;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId) { }
        }

        private SkillDatabase _database;
        private ClassRegistry _registry;
        private FakeLevels _levels;
        private FakeSlotIndex _slots;
        private SkillSystemService _service;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private List<string> _log;
        private RecordingInner _inner;
        private SkillCooldownSnapshotResponder _responder;

        [SetUp]
        public void SetUp()
        {
            _registry = new ClassRegistry();
            _levels = new FakeLevels();
            _slots = new FakeSlotIndex();
            _slots.Map(PLAYER_A, SLOT_A);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT);
            _log = new List<string>();
            _inner = new RecordingInner(_log);
        }

        [TearDown]
        public void TearDown()
        {
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        // Registers CLASS_FULL with skillCount skills (ids ID_BASE + index), builds service and responder, spawns PLAYER_A.
        private void BuildAndSpawn(int skillCount)
        {
            var ids = new uint[skillCount];
            var defs = new SkillDefinition[skillCount];
            for (int i = 0; i < skillCount; i++)
            {
                ids[i] = ID_BASE + (uint)i;
                defs[i] = new SkillDefinition { ID = ids[i], UnlockLevel = UNLOCK_LEVELS[i] };
            }

            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);
            _service = new SkillSystemService(_registry, _database, _levels, _slots, new NoOpUnlockOutput(), MAX_PLAYERS);
            _responder = new SkillCooldownSnapshotResponder(_inner, _service, _outbox);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
        }

        private SkillCooldownSnapshot SendAndDecode(uint clientId, EntityID entity)
        {
            _responder.SendSkillCooldownSnapshot(clientId, entity);
            _outbox.Flush(TICK);
            return DecodeOnlyPacket(clientId);
        }

        // Asserts the seam holds exactly one packet, to clientId, carrying a SkillCooldownSnapshot, and decodes it.
        private SkillCooldownSnapshot DecodeOnlyPacket(uint clientId)
        {
            Assert.That(_seam.Packets.Count, Is.EqualTo(1));
            Sent packet = _seam.Packets[0];
            Assert.That(packet.ClientId, Is.EqualTo(clientId));
            Assert.That(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope), Is.True);
            Assert.That(envelope.MessageTypeId, Is.EqualTo(SkillCooldownSnapshot.MessageTypeId));
            ReadOnlySpan<byte> body = packet.Data.AsSpan(ServerMessageEnvelope.WireSize);
            Assert.That(SkillCooldownSnapshotCodec.TryReadBody(body, out SkillCooldownSnapshot message), Is.True);
            return message;
        }

        // ---- Codec ----

        [Test]
        public void test_cooldown_snapshot_message_type_id_and_sizes_match_wire_spec()
        {
            Assert.That(SkillCooldownSnapshot.MessageTypeId, Is.EqualTo((ushort)0xE150));
            Assert.That(SkillCooldownSnapshot.EntryCount, Is.EqualTo(10));
            Assert.That(SkillCooldownSnapshot.BodySize, Is.EqualTo(80));
        }

        [Test]
        public void test_cooldown_snapshot_codec_round_trips_ten_entries()
        {
            var message = new SkillCooldownSnapshot();
            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                message.SetEntry(i, new SkillCooldownEntry(ID_BASE + (uint)i, 0x01020300u + (uint)i));
            }

            var buffer = new byte[SkillCooldownSnapshot.BodySize];
            int written = SkillCooldownSnapshotCodec.WriteBody(buffer, in message);

            Assert.That(written, Is.EqualTo(80));
            Assert.That(SkillCooldownSnapshotCodec.TryReadBody(buffer, out SkillCooldownSnapshot read), Is.True);
            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.That(read.GetEntry(i), Is.EqualTo(message.GetEntry(i)), "entry " + i);
            }

            // Little-endian, skill id before expiry, 8-byte stride: entry 0 = id 100, expiry 0x01020300; entry 1 id 101 at byte 8.
            Assert.That(new[] { buffer[0], buffer[1], buffer[2], buffer[3] }, Is.EqualTo(new byte[] { (byte)ID_BASE, 0, 0, 0 }));
            Assert.That(new[] { buffer[4], buffer[5], buffer[6], buffer[7] }, Is.EqualTo(new byte[] { 0x00, 0x03, 0x02, 0x01 }));
            Assert.That(new[] { buffer[8], buffer[9], buffer[10], buffer[11] }, Is.EqualTo(new byte[] { (byte)(ID_BASE + 1), 0, 0, 0 }));
            Assert.That(new[] { buffer[12], buffer[13], buffer[14], buffer[15] }, Is.EqualTo(new byte[] { 0x01, 0x03, 0x02, 0x01 }));
        }

        [TestCase(79)]
        [TestCase(81)]
        [TestCase(0)]
        public void test_cooldown_snapshot_codec_wrong_body_length_is_rejected(int length)
        {
            bool ok = SkillCooldownSnapshotCodec.TryReadBody(new byte[length], out SkillCooldownSnapshot message);

            Assert.That(ok, Is.False);
            Assert.That(message.GetEntry(0), Is.EqualTo(default(SkillCooldownEntry)));
        }

        [Test]
        public void test_cooldown_snapshot_codec_short_destination_throws()
        {
            var message = new SkillCooldownSnapshot();

            Assert.Throws<ArgumentException>(() => SkillCooldownSnapshotCodec.WriteBody(new byte[79], in message));
        }

        // ---- Responder ----

        [Test]
        public void test_cooldown_snapshot_slot_two_on_cooldown_entries_match_by_index_and_id()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, COOLDOWN_SLOT, COOLDOWN_TICK), Is.True);

            SkillCooldownSnapshot snapshot = SendAndDecode(CLIENT, PLAYER_A);

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                SkillCooldownEntry entry = snapshot.GetEntry(i);
                Assert.That(entry.SkillId, Is.EqualTo(ID_BASE + (uint)i), "id " + i);
                Assert.That(entry.CooldownExpiryTick, Is.EqualTo(i == COOLDOWN_SLOT ? COOLDOWN_TICK : 0u), "expiry " + i);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_snapshot_locked_skill_carries_zero_expiry_even_with_a_stored_value()
        {
            _levels.Level = 1; // slots 0 and 1 unlock at level 1; slot 2 (level 5) stays locked
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, COOLDOWN_SLOT, COOLDOWN_TICK), Is.True);

            SkillCooldownSnapshot snapshot = SendAndDecode(CLIENT, PLAYER_A);

            Assert.That(snapshot.GetEntry(COOLDOWN_SLOT), Is.EqualTo(new SkillCooldownEntry(ID_BASE + COOLDOWN_SLOT, 0u)));
        }

        [Test]
        public void test_cooldown_snapshot_never_cast_skills_carry_zero_expiry()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);

            SkillCooldownSnapshot snapshot = SendAndDecode(CLIENT, PLAYER_A);

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.That(snapshot.GetEntry(i).CooldownExpiryTick, Is.EqualTo(0u), "entry " + i);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_snapshot_short_class_pads_remaining_entries_with_zeros()
        {
            BuildAndSpawn(SHORT_COUNT);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, 1, COOLDOWN_TICK), Is.True);

            SkillCooldownSnapshot snapshot = SendAndDecode(CLIENT, PLAYER_A);

            for (int i = 0; i < SHORT_COUNT; i++)
            {
                Assert.That(snapshot.GetEntry(i).SkillId, Is.EqualTo(ID_BASE + (uint)i), "id " + i);
            }

            Assert.That(snapshot.GetEntry(1).CooldownExpiryTick, Is.EqualTo(COOLDOWN_TICK));
            for (int i = SHORT_COUNT; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.That(snapshot.GetEntry(i), Is.EqualTo(new SkillCooldownEntry(0u, 0u)), "padding " + i);
            }
        }

        [Test]
        public void test_cooldown_snapshot_unknown_entity_sends_ten_zero_entries_and_one_warning_without_throwing()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);
            LogAssert.Expect(LogType.Warning, new Regex("no skill store"));

            SkillCooldownSnapshot snapshot = default;
            Assert.DoesNotThrow(() => snapshot = SendAndDecode(CLIENT, UNKNOWN));

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.That(snapshot.GetEntry(i), Is.EqualTo(default(SkillCooldownEntry)), "entry " + i);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_snapshot_zero_skill_class_with_record_sends_ten_zero_entries_without_warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("has no skills")); // spawn warns once (EC-SK-12)
            BuildAndSpawn(0);

            SkillCooldownSnapshot snapshot = SendAndDecode(CLIENT, PLAYER_A);

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.That(snapshot.GetEntry(i), Is.EqualTo(default(SkillCooldownEntry)), "entry " + i);
            }

            LogAssert.NoUnexpectedReceived(); // no "no skill store" warning: the record exists
        }

        [Test]
        public void test_cooldown_snapshot_unknown_entity_twice_warns_once_and_sends_both_snapshots()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);
            LogAssert.Expect(LogType.Warning, new Regex("no skill store"));

            _responder.SendSkillCooldownSnapshot(CLIENT, UNKNOWN);
            _responder.SendSkillCooldownSnapshot(CLIENT, UNKNOWN);
            _outbox.Flush(TICK);

            Assert.That(_seam.Packets.Count, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_snapshot_client_not_in_outbox_sends_nothing_and_logs_nothing()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);

            Assert.DoesNotThrow(() => _responder.SendSkillCooldownSnapshot(CLIENT_NOT_IN_OUTBOX, PLAYER_A));
            _outbox.Flush(TICK);

            Assert.That(_seam.Packets, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_snapshot_responder_delegates_session_ready_once_and_never_forwards_the_snapshot()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);

            _responder.SendSessionReady(CLIENT);
            _responder.SendSkillCooldownSnapshot(CLIENT, PLAYER_A);

            Assert.That(_inner.ReadyCalls, Is.EqualTo(1));
            Assert.That(_inner.SnapshotCalls, Is.EqualTo(0));
        }

        [Test]
        public void test_cooldown_snapshot_responder_constructor_null_argument_throws()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);

            Assert.Throws<ArgumentNullException>(() => new SkillCooldownSnapshotResponder(null, _service, _outbox));
            Assert.Throws<ArgumentNullException>(() => new SkillCooldownSnapshotResponder(_inner, null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new SkillCooldownSnapshotResponder(_inner, _service, null));
        }

        // ---- Entry order (integration) ----

        [Test]
        public void test_cooldown_snapshot_zone_entry_order_is_zone_state_then_session_ready_then_cooldown_snapshot()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);
            var zone = new ZoneID(ZONE_RAW);
            var pool = new ZoneSlotPool(zone, new FakeTicks(), new FakePoolListener());
            var state = new FakeState();
            var zoneOutbox = new LogOutbox(_log);
            var snapshotBuilder = new ZoneSnapshotBuilder(zone, pool, new FakeMobs(), state, new FakeTicks(), zoneOutbox);
            var roster = new ZoneTickRoster(pool, new FakeTicks());
            IZoneEntryResponder responder = new OrderSpy(_responder, _log);
            var completion = new ZoneEntryCompletion(zone, pool, new ZoneConnectionCharacterDirectory(), snapshotBuilder,
                responder, new FakeParty(), state, zoneOutbox, roster);

            Assert.IsTrue(pool.TryAllocatePlayer(zone, new CharacterID(500u), new AccountID(5500u), CLIENT, out byte slot, out EntityID id));
            state.Players[id] = new ZonePlayerStateView(1, 2, 3, 0, 0, 0, 0, 0, 100, 100, 50, 50, 7, false, 0, 0, "Ayla", 0u);
            _slots.Map(id, SLOT_A + 1);
            _service.OnEntitySpawned(id, ClassType.Warrior);
            Assert.That(_service.SetCooldownForTesting(id, COOLDOWN_SLOT, COOLDOWN_TICK), Is.True);

            completion.OnPlayerAdmitted(CLIENT, slot, id);
            _outbox.Flush(TICK);

            int firstFrag = _log.FindIndex(e => e.StartsWith("frag:", StringComparison.Ordinal));
            int ready = _log.IndexOf("ready:" + CLIENT);
            int cooldown = _log.IndexOf("cooldown:" + CLIENT);
            Assert.That(firstFrag, Is.GreaterThanOrEqualTo(0), string.Join(",", _log));
            Assert.That(ready, Is.GreaterThan(firstFrag), string.Join(",", _log));
            Assert.That(cooldown, Is.GreaterThan(ready), string.Join(",", _log));
            int lastZoneFrag = _log.LastIndexOf("frag:" + ZoneStateSnapshotFragment.MessageTypeId);
            Assert.That(lastZoneFrag, Is.GreaterThanOrEqualTo(0), string.Join(",", _log));
            Assert.That(lastZoneFrag, Is.LessThan(ready), string.Join(",", _log));
            Assert.That(_inner.SnapshotCalls, Is.EqualTo(0));

            // The one packet is the joiner's snapshot, carrying the store's cooldown (built in SendSkillCooldownSnapshot).
            SkillCooldownSnapshot sent = DecodeOnlyPacket(CLIENT);
            Assert.That(sent.GetEntry(COOLDOWN_SLOT), Is.EqualTo(new SkillCooldownEntry(ID_BASE + COOLDOWN_SLOT, COOLDOWN_TICK)));
        }

        [Test]
        public void test_cooldown_snapshot_session_ready_alone_enqueues_nothing()
        {
            BuildAndSpawn(SkillSystemService.MAX_SKILLS_PER_ENTITY);

            _responder.SendSessionReady(CLIENT);
            _outbox.Flush(TICK);

            Assert.That(_seam.Packets, Is.Empty);
        }
    }
}
