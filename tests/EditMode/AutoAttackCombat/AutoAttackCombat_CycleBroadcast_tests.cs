using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 008: <see cref="CycleTimerBroadcastStep"/> (the per-tick 0x0103
    /// CycleBroadcast packet) over a real <see cref="AutoAttackCombatSystem"/>, <see cref="ZoneSlotPool"/> and
    /// <see cref="ClientMessageOutbox"/>. The flush order inside the composition is covered by
    /// <c>MovementSystem_PipelineWiring_tests.cs</c>.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_CycleBroadcast_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint FIRST_CLIENT = 11u;
        private const int MAX_PLAYERS = 50;
        private const int MAX_MOBS = 4;
        private const int SMALL_POOL = 4;
        private const int CYCLE = 20;
        private const int FROZEN_TICKS = 5;
        private const float TICK_DELTA = 0.05f;
        private const uint FIRST_TICK = 1u;
        private const int FLUSH_COUNT = 3;
        private const int MAX_PACKET_BYTES = 512;
        private const int FULL_FRACTION = 10000;
        private const int ACTIVE_FRACTION = 2500;
        private const int OVERFLOW_PLAYERS = 51;
        private const float NO_SPEED_CHANGE = 1.0f;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeValidity : IAutoAttackTargetValidity
        {
            public bool IsValidTarget(EntityID target)
            {
                return true;
            }
        }

        private sealed class UnitAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                return NO_SPEED_CHANGE;
            }
        }

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;
            public readonly NetworkChannel Channel;

            public Sent(uint clientId, byte[] data, NetworkChannel channel)
            {
                ClientId = clientId;
                Data = data;
                Channel = channel;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();
            public readonly HashSet<uint> ThrowFor = new HashSet<uint>();
            public bool Record = true;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                if (ThrowFor.Contains(clientId))
                {
                    throw new InvalidOperationException("seam-failure");
                }

                if (Record)
                {
                    Packets.Add(new Sent(clientId, message.ToArray(), channel));
                }
            }
        }

        private ZoneSlotPool _pool;
        private FakeZoneState _positions;
        private FakeDamageApplier _sink;
        private AutoAttackCombatSystem _combat;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private CycleTimerBroadcastStep _step;
        private uint _nextClient;
        private uint _tick;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static EntityID MobA => new EntityID(AutoAttackTestFakes.MOB_A_RAW);

        [SetUp]
        public void SetUp()
        {
            Build(SMALL_POOL);
        }

        private void Build(int maxPlayers)
        {
            _nextClient = FIRST_CLIENT;
            _tick = 0u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), maxPlayers, MAX_MOBS);
            _positions = new FakeZoneState();
            _sink = new FakeDamageApplier();
            _combat = new AutoAttackCombatSystem(
                _pool, new FakeValidity(), new UnitAttackSpeed(), _positions, new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), _sink,
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), default(ZoneID));
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _step = new CycleTimerBroadcastStep(_combat, _pool, _seam, _outbox);
        }

        // Allocates a player in the pool; registers its client with the outbox unless it is meant to be a ghost.
        private EntityID AddPlayer(bool registerClient = true)
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            if (registerClient)
            {
                _outbox.AddClient(client);
            }

            return id;
        }

        private static uint ClientOf(int index)
        {
            return FIRST_CLIENT + (uint)index;
        }

        private void RunTicks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _tick++;
                _sink.Tick = _tick;
                _combat.Run(_tick, TICK_DELTA);
            }
        }

        private void Activate(EntityID player)
        {
            _combat.SetAutoAttack(player, true);
            _combat.SetTarget(player, MobA);
        }

        private List<Sent> PacketsTo(uint client)
        {
            return _seam.Packets.FindAll(p => p.ClientId == client);
        }

        private static List<CycleTimerBroadcast> Decode(byte[] packet, out ServerMessageEnvelope envelope)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out envelope, out ushort count));
            var entries = new List<CycleTimerBroadcast>();
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadCycleTimerBroadcast(
                    new ReadOnlySpan<byte>(packet, offset, packet.Length - offset), out CycleTimerBroadcast decoded, out int read));
                entries.Add(decoded);
                offset += read;
            }

            Assert.AreEqual(packet.Length, offset, "packet holds only CycleTimerBroadcast sub-messages");
            return entries;
        }

        private static CycleTimerBroadcast EntryFor(List<CycleTimerBroadcast> entries, EntityID id)
        {
            int index = entries.FindIndex(e => e.EntityId == id);
            Assert.GreaterOrEqual(index, 0, "entry for " + id);
            return entries[index];
        }

        [TestCase(0, 20, 0)]
        [TestCase(5, 20, 2500)]
        [TestCase(20, 20, 10000)]
        [TestCase(60, 20, 10000)]
        [TestCase(10, 0, 0)]
        [TestCase(10, -3, 0)]
        [TestCase(-4, 20, 0)]
        public void test_normalize_cycle_timer_maps_timer_over_cycle_to_0_to_10000(int timer, int cycleTicks, int expected)
        {
            Assert.AreEqual(expected, CycleTimerBroadcastStep.NormalizeCycleTimer(timer, cycleTicks));
        }

        [Test]
        public void test_flush_three_ticks_sends_three_cycle_packets_numbered_by_tick()
        {
            AddPlayer();

            for (uint t = FIRST_TICK; t < FIRST_TICK + FLUSH_COUNT; t++)
            {
                _step.Flush(t);
            }

            List<Sent> sent = PacketsTo(ClientOf(0));
            Assert.AreEqual(FLUSH_COUNT, sent.Count);
            for (int i = 0; i < FLUSH_COUNT; i++)
            {
                Decode(sent[i].Data, out ServerMessageEnvelope env);
                Assert.AreEqual(FIRST_TICK + (uint)i, env.ServerTickNumber);
            }
        }

        [Test]
        public void test_flush_packet_uses_cycle_type_unreliable_channel_and_increasing_sequence()
        {
            AddPlayer();

            _step.Flush(FIRST_TICK);
            _step.Flush(FIRST_TICK + 1u);

            List<Sent> sent = PacketsTo(ClientOf(0));
            Decode(sent[0].Data, out ServerMessageEnvelope first);
            Decode(sent[1].Data, out ServerMessageEnvelope second);
            Assert.AreEqual(CycleBroadcastPacketWriter.TICK_BATCH_UU_CYCLE, first.MessageTypeId);
            Assert.AreEqual(NetworkChannel.Unreliable, sent[0].Channel);
            Assert.AreEqual(first.SequenceNumber + 1u, second.SequenceNumber);
        }

        [Test]
        public void test_flush_with_mobs_in_pool_sends_no_mob_entries()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID mob));

            _step.Flush(FIRST_TICK);

            Assert.AreEqual(2, _seam.Packets.Count);
            foreach (Sent s in _seam.Packets)
            {
                List<CycleTimerBroadcast> entries = Decode(s.Data, out _);
                Assert.AreEqual(2, entries.Count);
                Assert.IsFalse(entries.Exists(e => e.EntityId == mob));
                Assert.IsTrue(entries.Exists(e => e.EntityId == p1));
                Assert.IsTrue(entries.Exists(e => e.EntityId == p2));
            }
        }

        [Test]
        public void test_flush_each_receiver_packet_holds_both_players_with_their_own_values()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            Activate(p1);
            RunTicks(FROZEN_TICKS);

            _step.Flush(FIRST_TICK);

            for (int client = 0; client < 2; client++)
            {
                List<CycleTimerBroadcast> entries = Decode(PacketsTo(ClientOf(client))[0].Data, out _);
                Assert.AreEqual(2, entries.Count, "client " + client);
                Assert.AreEqual(ACTIVE_FRACTION, EntryFor(entries, p1).CycleTimer, "p1 in client " + client);
                Assert.AreEqual(0, EntryFor(entries, p2).CycleTimer, "p2 in client " + client);
            }
        }

        [Test]
        public void test_flush_released_player_is_absent_and_remaining_players_still_receive()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            EntityID p3 = AddPlayer();
            Assert.IsTrue(_pool.ReleasePlayer(p2));

            Assert.DoesNotThrow(() => _step.Flush(FIRST_TICK));

            Assert.AreEqual(2, _seam.Packets.Count);
            Assert.AreEqual(0, PacketsTo(ClientOf(1)).Count);
            foreach (Sent s in _seam.Packets)
            {
                List<CycleTimerBroadcast> entries = Decode(s.Data, out _);
                Assert.AreEqual(2, entries.Count);
                Assert.IsFalse(entries.Exists(e => e.EntityId == p2));
                Assert.IsTrue(entries.Exists(e => e.EntityId == p1));
                Assert.IsTrue(entries.Exists(e => e.EntityId == p3));
            }
        }

        [Test]
        public void test_flush_empty_zone_sends_nothing_and_does_not_throw()
        {
            Assert.DoesNotThrow(() => _step.Flush(FIRST_TICK));

            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_flush_failed_send_still_consumes_the_sequence_number()
        {
            AddPlayer();
            AddPlayer();
            _step.Flush(FIRST_TICK);
            _seam.ThrowFor.Add(ClientOf(1));
            LogAssert.Expect(LogType.Error, new Regex(@"\[CycleTimerBroadcastStep\] SendThrew: clientId=" + ClientOf(1)));
            _step.Flush(FIRST_TICK + 1u);
            _seam.ThrowFor.Clear();

            _step.Flush(FIRST_TICK + 2u);

            List<Sent> sent = PacketsTo(ClientOf(1));
            Assert.AreEqual(2, sent.Count);
            Decode(sent[0].Data, out ServerMessageEnvelope before);
            Decode(sent[1].Data, out ServerMessageEnvelope after);
            Assert.AreEqual(before.SequenceNumber + 2u, after.SequenceNumber);
        }

        [Test]
        public void test_flush_more_players_than_the_cap_logs_writer_and_step_errors_per_client_and_sends_nothing()
        {
            Build(OVERFLOW_PLAYERS);
            for (int i = 0; i < OVERFLOW_PLAYERS; i++)
            {
                AddPlayer();
            }

            for (int i = 0; i < OVERFLOW_PLAYERS; i++)
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[CycleBroadcastPacketWriter\] CycleBroadcastNeverDroppedOverflow \(CRITICAL\): clientId=" + ClientOf(i) + ","));
                LogAssert.Expect(LogType.Error, new Regex(@"\[CycleTimerBroadcastStep\] SendThrew: clientId=" + ClientOf(i) + " "));
            }

            Assert.DoesNotThrow(() => _step.Flush(FIRST_TICK));

            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_flush_lone_player_receives_a_packet_with_only_its_own_entry()
        {
            EntityID p1 = AddPlayer();

            _step.Flush(FIRST_TICK);

            List<Sent> sent = PacketsTo(ClientOf(0));
            Assert.AreEqual(1, sent.Count);
            List<CycleTimerBroadcast> entries = Decode(sent[0].Data, out _);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(p1, entries[0].EntityId);
        }

        [Test]
        public void test_flush_idle_player_with_frozen_timer_is_sent_with_the_frozen_fraction()
        {
            EntityID p1 = AddPlayer();
            Activate(p1);
            RunTicks(FROZEN_TICKS);
            _combat.SetAutoAttack(p1, false);
            int frozen = _combat.GetActionTimer(p1);
            Assert.Greater(frozen, 0, "precondition: timer frozen above zero");
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p1));

            _step.Flush(FIRST_TICK);

            CycleTimerBroadcast entry = EntryFor(Decode(PacketsTo(ClientOf(0))[0].Data, out _), p1);
            Assert.AreEqual(frozen * FULL_FRACTION / _combat.GetCycleTicks(p1), entry.CycleTimer);
        }

        [Test]
        public void test_flush_never_activated_idle_player_is_sent_with_zero()
        {
            EntityID p1 = AddPlayer();
            Assert.AreEqual(0, _combat.GetCycleTicks(p1), "precondition: never sampled, so the cycleTicks guard is reached");

            _step.Flush(FIRST_TICK);

            Assert.AreEqual(0, EntryFor(Decode(PacketsTo(ClientOf(0))[0].Data, out _), p1).CycleTimer);
        }

        [Test]
        public void test_flush_active_player_entry_follows_timer_over_cycle()
        {
            EntityID p1 = AddPlayer();
            Activate(p1);
            RunTicks(FROZEN_TICKS);

            _step.Flush(FIRST_TICK);

            Assert.AreEqual(CYCLE, _combat.GetCycleTicks(p1));
            Assert.AreEqual(FROZEN_TICKS * FULL_FRACTION / CYCLE, EntryFor(Decode(PacketsTo(ClientOf(0))[0].Data, out _), p1).CycleTimer);
        }

        [Test]
        public void test_flush_fifty_players_sends_512_byte_packets_without_error()
        {
            Build(MAX_PLAYERS);
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                AddPlayer();
            }

            _step.Flush(FIRST_TICK);

            Assert.AreEqual(MAX_PLAYERS, _seam.Packets.Count);
            foreach (Sent s in _seam.Packets)
            {
                Assert.AreEqual(MAX_PACKET_BYTES, s.Data.Length);
                Assert.AreEqual(MAX_PLAYERS, Decode(s.Data, out _).Count);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_flush_ghost_player_appears_in_others_packets_but_receives_nothing()
        {
            EntityID p1 = AddPlayer();
            EntityID ghost = AddPlayer(registerClient: false);

            Assert.DoesNotThrow(() => _step.Flush(FIRST_TICK));

            Assert.AreEqual(0, PacketsTo(ClientOf(1)).Count);
            List<CycleTimerBroadcast> entries = Decode(PacketsTo(ClientOf(0))[0].Data, out _);
            Assert.AreEqual(p1, EntryFor(entries, p1).EntityId);
            Assert.AreEqual(ghost, EntryFor(entries, ghost).EntityId);
        }

        [Test]
        public void test_flush_throwing_seam_logs_one_error_per_failing_client_and_others_still_receive()
        {
            AddPlayer();
            AddPlayer();
            AddPlayer();
            _seam.ThrowFor.Add(ClientOf(1));
            LogAssert.Expect(LogType.Error, new Regex(@"\[CycleTimerBroadcastStep\] SendThrew: clientId=" + ClientOf(1)));

            Assert.DoesNotThrow(() => _step.Flush(FIRST_TICK));

            Assert.AreEqual(1, PacketsTo(ClientOf(0)).Count);
            Assert.AreEqual(0, PacketsTo(ClientOf(1)).Count);
            Assert.AreEqual(1, PacketsTo(ClientOf(2)).Count);
        }

        [Test]
        public void test_flush_after_warm_up_allocates_no_gc_memory_for_fifty_players()
        {
            Build(MAX_PLAYERS);
            for (int i = 0; i < MAX_PLAYERS; i++)
            {
                AddPlayer();
            }

            _seam.Record = false;
            _step.Flush(FIRST_TICK);

            Assert.That(() => _step.Flush(FIRST_TICK + 1u), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_constructor_null_argument_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CycleTimerBroadcastStep(null, _pool, _seam, _outbox));
            Assert.Throws<ArgumentNullException>(() => new CycleTimerBroadcastStep(_combat, null, _seam, _outbox));
            Assert.Throws<ArgumentNullException>(() => new CycleTimerBroadcastStep(_combat, _pool, null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new CycleTimerBroadcastStep(_combat, _pool, _seam, null));
        }
    }
}
