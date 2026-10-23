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
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 007 — <see cref="ZoneSnapshotBuilder"/> (F-ZI-1, AC-ZI-8,
    /// CR-ZI-8 step 6). Uses a real <see cref="ClientMessageOutbox"/> with a recording send seam and a real
    /// <see cref="NetworkTestObserver"/>. Ticks are set programmatically.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_SnapshotAssembly_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT = 42u;
        private const int ENVELOPE = ServerMessageEnvelope.WireSize;
        private const int PAYLOAD_MAX = ZoneStateSnapshotFragment.MaxPayloadBytes;
        private const int AVERAGE_NAME_BYTES = 11;
        private const int WORST_NAME_BYTES = EntityState.MaxNameBytes;
        private static readonly Regex SKIPPED_LOG = new Regex("SnapshotEntitySkipped");

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick;
            public uint ServerTickNumber => Tick;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeStateSource : IZoneEntityStateSource
        {
            public readonly Dictionary<EntityID, ZonePlayerStateView> Players = new Dictionary<EntityID, ZonePlayerStateView>();
            public readonly Dictionary<EntityID, ZoneMobStateView> Mobs = new Dictionary<EntityID, ZoneMobStateView>();

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view) => Players.TryGetValue(id, out view);

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view) => Mobs.TryGetValue(id, out view);
        }

        private FakeTicks _ticks;
        private FakeStateSource _source;
        private ZoneSlotPool _pool;
        private ZoneMobRoster _roster;
        private OutboxRecordingSendSeam _sink;
        private ClientMessageOutbox _outbox;
        private NetworkTestObserver _observer;
        private ZoneSnapshotBuilder _builder;
        private uint _nextCharacter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks { Tick = 100u };
            _source = new FakeStateSource();
            _pool = new ZoneSlotPool(Zone, _ticks, new FakeListener());
            _roster = new ZoneMobRoster(_pool, _ticks);
            _sink = new OutboxRecordingSendSeam();
            _outbox = new ClientMessageOutbox(_sink);
            _outbox.AddClient(CLIENT);
            _observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            _outbox.SetTestObserver(_observer);
            _builder = new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, _ticks, _outbox);
            _nextCharacter = 1000u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private EntityID AddPlayer(string name, ZoneSessionState state = ZoneSessionState.Connected, uint respawnTick = 0u)
        {
            uint n = _nextCharacter++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u), n + 9000u,
                out _, out EntityID id));
            _pool.SetSessionState(id, state);
            _source.Players[id] = new ZonePlayerStateView(1, 2, 3, 4, 5, 6, 7, 8, 90, 100, 40, 50, 12, true, 2500, 3, name, respawnTick);
            return id;
        }

        private EntityID AddMob(uint type = 5u)
        {
            EntityID id = _roster.TryAddMob(Zone, new MobTypeID(type), Vector3.zero);
            Assert.AreNotEqual(EntityID.Invalid, id);
            _source.Mobs[id] = new ZoneMobStateView(-1, -2, -3, 4, 5, 6, 7, 8, 30, 60, false, 0);
            return id;
        }

        private void Populate(int players, int mobs, int nameBytes)
        {
            string name = new string('n', nameBytes);
            for (int i = 0; i < players; i++)
            {
                AddPlayer(name);
            }

            for (int i = 0; i < mobs; i++)
            {
                AddMob();
            }
        }

        // Flushes the outbox and decodes every sent fragment (header + payload copy), in send order.
        private List<(ZoneStateSnapshotFragment Header, byte[] Payload)> SendAndCollect()
        {
            _builder.SendSnapshot(CLIENT);
            _outbox.Flush(_ticks.Tick);

            var result = new List<(ZoneStateSnapshotFragment, byte[])>();
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                var body = new ReadOnlySpan<byte>(send.Bytes, ENVELOPE, send.Bytes.Length - ENVELOPE);
                Assert.IsTrue(ZoneStateSnapshotFragmentCodec.TryReadBody(body, out ZoneStateSnapshotFragment header,
                    out ReadOnlySpan<byte> payload));
                result.Add((header, payload.ToArray()));
            }

            return result;
        }

        private static byte[] Concat(List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments)
        {
            var all = new List<byte>();
            foreach ((ZoneStateSnapshotFragment _, byte[] payload) in fragments)
            {
                all.AddRange(payload);
            }

            return all.ToArray();
        }

        private static List<EntityState> Walk(byte[] stream)
        {
            var entries = new List<EntityState>();
            int offset = 0;
            while (offset < stream.Length)
            {
                Assert.IsTrue(EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(stream, offset, stream.Length - offset),
                    out EntityState state, out int consumed), "entry at offset " + offset + " did not decode");
                entries.Add(state);
                offset += consumed;
            }

            return entries;
        }

        private List<EntityState> SnapshotEntries() => Walk(Concat(SendAndCollect()));

        // ----- AC-ZI-8 --------------------------------------------------------------------------

        [Test]
        public void test_snapshot_ac_zi_8_average_names_50_players_150_mobs_sends_17_fragments()
        {
            Populate(50, 150, AVERAGE_NAME_BYTES);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            Assert.AreEqual(17, _observer.GetOutboundMessageCount(CLIENT, ZoneStateSnapshotFragment.MessageTypeId));
            Assert.AreEqual(17, fragments.Count);
            Assert.AreEqual(50 * 64 + 150 * 35, Concat(fragments).Length);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_snapshot_ac_zi_8_worst_case_names_50_players_150_mobs_sends_19_fragments()
        {
            Populate(50, 150, WORST_NAME_BYTES);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            Assert.AreEqual(19, _observer.GetOutboundMessageCount(CLIENT, ZoneStateSnapshotFragment.MessageTypeId));
            Assert.AreEqual(19, fragments.Count);
            Assert.AreEqual(50 * 77 + 150 * 35, Concat(fragments).Length);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- empty zone -----------------------------------------------------------------------

        [Test]
        public void test_snapshot_empty_zone_sends_one_fragment_with_empty_payload()
        {
            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            Assert.AreEqual(1, fragments.Count);
            Assert.AreEqual(1, fragments[0].Header.TotalFragments);
            Assert.AreEqual(0, fragments[0].Header.FragmentIndex);
            Assert.AreEqual(0, fragments[0].Payload.Length);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- player fields --------------------------------------------------------------------

        [Test]
        public void test_snapshot_dead_player_respawn_ticks_remaining_is_respawn_tick_minus_tick()
        {
            AddPlayer("dead", ZoneSessionState.Dead, 140u);

            EntityState entry = SnapshotEntries()[0];

            Assert.IsTrue(entry.IsInDeadState);
            Assert.AreEqual(40u, entry.RespawnTicksRemaining);
        }

        [Test]
        public void test_snapshot_dead_player_past_respawn_tick_clamps_remaining_to_zero()
        {
            AddPlayer("dead", ZoneSessionState.Dead, 140u);
            _ticks.Tick = 150u;

            EntityState entry = SnapshotEntries()[0];

            Assert.IsTrue(entry.IsInDeadState);
            Assert.AreEqual(0u, entry.RespawnTicksRemaining);
        }

        [Test]
        public void test_snapshot_respawning_player_is_flagged_dead()
        {
            AddPlayer("resp", ZoneSessionState.Respawning, 130u);

            EntityState entry = SnapshotEntries()[0];

            Assert.IsTrue(entry.IsInDeadState);
            Assert.AreEqual(30u, entry.RespawnTicksRemaining);
        }

        [Test]
        public void test_snapshot_connected_player_is_not_dead_and_remaining_is_zero()
        {
            AddPlayer("live", ZoneSessionState.Connected, 140u);

            EntityState entry = SnapshotEntries()[0];

            Assert.IsFalse(entry.IsInDeadState);
            Assert.AreEqual(0u, entry.RespawnTicksRemaining);
            Assert.IsFalse(entry.IsGhost);
        }

        [Test]
        public void test_snapshot_disconnected_session_active_player_is_ghost()
        {
            AddPlayer("ghost", ZoneSessionState.Disconnected_SessionActive);

            EntityState entry = SnapshotEntries()[0];

            Assert.IsTrue(entry.IsGhost);
            Assert.IsFalse(entry.IsInDeadState);
        }

        [Test]
        public void test_snapshot_player_entry_carries_all_player_fields()
        {
            EntityID id = AddPlayer("Hero");

            EntityState entry = SnapshotEntries()[0];

            Assert.AreEqual(EntityState.CreatePlayer(id, 1, 2, 3, 4, 5, 6, 7, 90, 100, true, false, 2500, 8, "Hero", 12, 40, 50, 3, false, 0u), entry);
        }

        // ----- order and mob fields -------------------------------------------------------------

        [Test]
        public void test_snapshot_players_first_in_roster_order_then_mobs_in_roster_order()
        {
            EntityID p1 = AddPlayer("a");
            EntityID m1 = AddMob(11u);
            EntityID p2 = AddPlayer("b");
            EntityID m2 = AddMob(22u);

            List<EntityState> entries = SnapshotEntries();

            var expected = new List<EntityID>(_pool.GetPlayerEntityIDsInZone(Zone));
            expected.AddRange(_pool.GetMobEntityIDsInZone(Zone));
            Assert.AreEqual(4, entries.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(expected[i], entries[i].EntityId, "entry " + i);
            }

            CollectionAssert.AreEquivalent(new[] { p1, p2 }, new[] { entries[0].EntityId, entries[1].EntityId });
            Assert.AreEqual(EntityType.Player, entries[1].Type);
            Assert.AreEqual(EntityType.Mob, entries[2].Type);
            Assert.AreEqual(EntityType.Mob, entries[3].Type);
            Assert.AreEqual(11, entries[2].EntityId == m1 ? entries[2].MobTypeId : entries[3].MobTypeId);
            Assert.AreEqual(22, entries[2].EntityId == m2 ? entries[2].MobTypeId : entries[3].MobTypeId);
        }

        // ----- version and split ----------------------------------------------------------------

        [Test]
        public void test_snapshot_version_is_the_build_tick_on_every_fragment()
        {
            _ticks.Tick = 777u;
            Populate(50, 150, AVERAGE_NAME_BYTES);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            Assert.AreEqual(17, fragments.Count);
            for (int i = 0; i < fragments.Count; i++)
            {
                Assert.AreEqual(777u, fragments[i].Header.SnapshotVersion, "fragment " + i);
                Assert.AreEqual(17, fragments[i].Header.TotalFragments);
                Assert.AreEqual(i, fragments[i].Header.FragmentIndex);
            }
        }

        [Test]
        public void test_snapshot_contiguous_split_full_fragments_then_concatenation_walks_every_entry()
        {
            Populate(50, 150, AVERAGE_NAME_BYTES);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            for (int i = 0; i < fragments.Count - 1; i++)
            {
                Assert.AreEqual(PAYLOAD_MAX, fragments[i].Payload.Length, "fragment " + i);
            }

            Assert.AreEqual(8450 - 16 * PAYLOAD_MAX, fragments[fragments.Count - 1].Payload.Length);

            List<EntityState> entries = Walk(Concat(fragments));
            var expected = new List<EntityID>(_pool.GetPlayerEntityIDsInZone(Zone));
            expected.AddRange(_pool.GetMobEntityIDsInZone(Zone));
            Assert.AreEqual(200, entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Assert.AreEqual(expected[i], entries[i].EntityId, "entry " + i);
            }
        }

        // ----- seam miss ------------------------------------------------------------------------

        [Test]
        public void test_snapshot_seam_miss_skips_the_entity_with_one_warning_and_counts_from_actual_bytes()
        {
            Populate(50, 150, AVERAGE_NAME_BYTES);
            var players = new List<EntityID>(_pool.GetPlayerEntityIDsInZone(Zone));
            var mobs = new List<EntityID>(_pool.GetMobEntityIDsInZone(Zone));
            _source.Players.Remove(players[3]);
            _source.Players.Remove(players[4]);
            _source.Mobs.Remove(mobs[0]);
            LogAssert.Expect(LogType.Warning, SKIPPED_LOG);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            // 48 players x 64 + 149 mobs x 35 = 8287 bytes -> ceil(8287 / 504) = 17.
            byte[] stream = Concat(fragments);
            Assert.AreEqual(48 * 64 + 149 * 35, stream.Length);
            Assert.AreEqual(17, fragments.Count);
            List<EntityState> entries = Walk(stream);
            Assert.AreEqual(197, entries.Count);
            foreach (EntityState e in entries)
            {
                Assert.AreNotEqual(players[3], e.EntityId);
                Assert.AreNotEqual(players[4], e.EntityId);
                Assert.AreNotEqual(mobs[0], e.EntityId);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_snapshot_seam_miss_on_every_entity_still_sends_one_empty_fragment()
        {
            AddPlayer("x");
            _source.Players.Clear();
            LogAssert.Expect(LogType.Warning, SKIPPED_LOG);

            List<(ZoneStateSnapshotFragment Header, byte[] Payload)> fragments = SendAndCollect();

            Assert.AreEqual(1, fragments.Count);
            Assert.AreEqual(0, fragments[0].Payload.Length);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- construction ---------------------------------------------------------------------

        [Test]
        public void test_snapshot_builder_constructor_rejects_null_arguments_and_invalid_zone()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotBuilder(Zone, null, _roster, _source, _ticks, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotBuilder(Zone, _pool, null, _source, _ticks, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotBuilder(Zone, _pool, _roster, null, _ticks, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, _ticks, null));
            Assert.Throws<ArgumentException>(() => new ZoneSnapshotBuilder(ZoneID.Invalid, _pool, _roster, _source, _ticks, _outbox));
        }

        // ----- allocation -----------------------------------------------------------------------

        [Test]
        public void test_snapshot_second_call_at_50_players_150_mobs_does_not_allocate()
        {
            Populate(50, 150, AVERAGE_NAME_BYTES);
            var counting = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(counting);
            outbox.AddClient(CLIENT);
            var builder = new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, _ticks, outbox);
            builder.SendSnapshot(CLIENT);
            outbox.Flush(1u);
            counting.Count = 0;

            Assert.That(() =>
            {
                builder.SendSnapshot(CLIENT);
                outbox.Flush(2u);
            }, Is.Not.AllocatingGCMemory(), "the steady snapshot path allocated");

            Assert.AreEqual(17, counting.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_allocation_measure_seam_miss_path_reports_allocation()
        {
            // Positive control: the warning path builds a string, so the GC.Alloc count must move.
            Populate(2, 0, AVERAGE_NAME_BYTES);
            _source.Players.Remove(new List<EntityID>(_pool.GetPlayerEntityIDsInZone(Zone))[0]);
            LogAssert.Expect(LogType.Warning, SKIPPED_LOG);

            Assert.That(() => { _builder.SendSnapshot(CLIENT); },
                Is.AllocatingGCMemory(),
                "the GC.Alloc measure did not register an allocation");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
