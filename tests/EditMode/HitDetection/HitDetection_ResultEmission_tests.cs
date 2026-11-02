using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

namespace IronGrind.Tests.EditMode.HitDetection
{
    /// <summary>
    /// EditMode tests for Hit Detection Story 004: <see cref="HitResultEmitter"/> routing and emission
    /// (CR-HD-10, CR-HD-11, AC-HD-11a, AC-HD-11b, EC-HD-6). The composition order is covered by the wiring list in
    /// <c>ZoneServer_CompositionWiring_tests.cs</c>.
    /// </summary>
    [TestFixture]
    internal sealed class HitDetection_ResultEmission_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_A = 11u;
        private const uint CLIENT_B = 12u;
        private const uint CLIENT_C = 13u;
        private const uint TICK = 4242u;
        private const uint NEVER_ISSUED_ENTITY = 900000u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick = TICK;

            public uint ServerTickNumber => Tick;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Call
        {
            public readonly uint ClientId;
            public readonly RUBatchCategory Category;
            public readonly ushort MessageTypeId;
            public readonly byte[] Body;

            public Call(uint clientId, RUBatchCategory category, ushort messageTypeId, byte[] body)
            {
                ClientId = clientId;
                Category = category;
                MessageTypeId = messageTypeId;
                Body = body;
            }
        }

        private sealed class FakeCollector : IRUBatchCollector
        {
            public readonly List<Call> Calls = new List<Call>();

            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                Calls.Add(new Call(clientId, category, messageTypeId, body.ToArray()));
            }

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent) { }
        }

        // Records who the emitter addressed, then forwards to the real step.
        private sealed class TeeCollector : IRUBatchCollector
        {
            public readonly List<uint> Clients = new List<uint>();
            public IRUBatchCollector Inner;

            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                Clients.Add(clientId);
                Inner.Enqueue(clientId, category, messageTypeId, body);
            }

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent)
            {
                Inner.EnqueueDamageEvent(clientId, in damageEvent);
            }
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
            public bool Record = true;
            public int Count;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Count++;
                if (Record)
                {
                    Packets.Add(new Sent(clientId, message.ToArray()));
                }
            }
        }

        private FakeTicks _ticks;
        private ZoneSlotPool _pool;
        private FakeCollector _collector;
        private HitResultEmitter _emitter;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private RUBatchOutboundStep _step;
        private HitResultEmitter _realEmitter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, _ticks, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _collector = new FakeCollector();
            _emitter = new HitResultEmitter(_pool, _collector, _ticks);

            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_A);
            _outbox.AddClient(CLIENT_B);
            _outbox.AddClient(CLIENT_C);
            _step = new RUBatchOutboundStep(_seam, _outbox, MAX_PLAYERS);
            _realEmitter = new HitResultEmitter(_pool, _step, _ticks);
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        private EntityID AddMob()
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID id));
            return id;
        }

        private static HitCheckResult Decode(byte[] body)
        {
            byte[] sub = new byte[BatchSubMessageFraming.SubMessageHeaderSize + body.Length];
            BatchSubMessageFraming.WriteHeader(sub, (ushort)body.Length, HitCheckResult.MessageTypeId);
            Array.Copy(body, 0, sub, BatchSubMessageFraming.SubMessageHeaderSize, body.Length);
            Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(sub, out HitCheckResult decoded, out _));
            return decoded;
        }

        private static List<HitCheckResult> DecodePacket(byte[] packet)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out ServerMessageEnvelope envelope, out ushort count));
            Assert.AreEqual(RUBatchWriter.TICK_BATCH_RU, envelope.MessageTypeId);
            var results = new List<HitCheckResult>();
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                byte[] sub = new byte[packet.Length - offset];
                Array.Copy(packet, offset, sub, 0, sub.Length);
                Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(sub, out HitCheckResult decoded, out int bytesRead));
                results.Add(decoded);
                offset += bytesRead;
            }

            Assert.AreEqual(packet.Length, offset);
            return results;
        }

        [TestCase(HitResult.Hit)]
        [TestCase(HitResult.MissOutOfRange)]
        [TestCase(HitResult.MissFacing)]
        public void test_hit_emission_player_attacker_gets_one_message_with_matching_fields(HitResult result)
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID target = AddMob();

            _emitter.Emit(attacker, target, result);

            Assert.AreEqual(1, _collector.Calls.Count);
            Call call = _collector.Calls[0];
            Assert.AreEqual(CLIENT_A, call.ClientId);
            Assert.AreEqual(RUBatchCategory.HitCheckResult, call.Category);
            Assert.AreEqual(HitCheckResult.MessageTypeId, call.MessageTypeId);
            Assert.AreEqual(new HitCheckResult(attacker, target, result, TICK), Decode(call.Body));
        }

        [Test]
        public void test_hit_emission_monster_attacker_notifies_only_the_defending_player()
        {
            EntityID mob = AddMob();
            EntityID defender = AddPlayer(CLIENT_A);
            AddPlayer(CLIENT_B);

            _emitter.Emit(mob, defender, HitResult.MissFacing);

            Assert.AreEqual(1, _collector.Calls.Count);
            Assert.AreEqual(CLIENT_A, _collector.Calls[0].ClientId);
            Assert.AreEqual(new HitCheckResult(mob, defender, HitResult.MissFacing, TICK), Decode(_collector.Calls[0].Body));
        }

        [Test]
        public void test_hit_emission_third_player_in_the_zone_gets_nothing()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            AddPlayer(CLIENT_B);
            EntityID mob = AddMob();

            _realEmitter.Emit(attacker, mob, HitResult.Hit);
            _step.Flush(TICK);

            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(CLIENT_A, _seam.Packets[0].ClientId);
            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == CLIENT_B));
            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == CLIENT_C));
        }

        [Test]
        public void test_hit_emission_neither_entity_holds_a_player_slot_queues_nothing_and_does_not_throw()
        {
            EntityID mobA = AddMob();
            EntityID mobB = AddMob();

            Assert.DoesNotThrow(() => _emitter.Emit(mobA, mobB, HitResult.Hit));

            Assert.AreEqual(0, _collector.Calls.Count);
        }

        [Test]
        public void test_hit_emission_player_who_left_the_zone_attacking_a_mob_queues_nothing()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID mob = AddMob();
            Assert.IsTrue(_pool.ReleasePlayer(attacker));

            Assert.DoesNotThrow(() => _emitter.Emit(attacker, mob, HitResult.Hit));

            Assert.AreEqual(0, _collector.Calls.Count);
        }

        [Test]
        public void test_hit_emission_ghost_player_with_a_slot_but_no_client_gets_no_packet_and_nothing_throws()
        {
            EntityID ghost = AddPlayer(CLIENT_A);
            EntityID live = AddPlayer(CLIENT_B);
            EntityID mob = AddMob();
            _outbox.RemoveClient(CLIENT_A);
            var tee = new TeeCollector { Inner = _step };
            var emitter = new HitResultEmitter(_pool, tee, _ticks);

            Assert.DoesNotThrow(() => emitter.Emit(ghost, mob, HitResult.Hit));
            Assert.DoesNotThrow(() => emitter.Emit(live, mob, HitResult.Hit));
            Assert.DoesNotThrow(() => _step.Flush(TICK));

            CollectionAssert.AreEqual(new[] { CLIENT_A, CLIENT_B }, tee.Clients);
            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(CLIENT_B, _seam.Packets[0].ClientId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_hit_emission_player_attacks_player_only_the_attacker_is_notified()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID defender = AddPlayer(CLIENT_B);

            _emitter.Emit(attacker, defender, HitResult.Hit);

            Assert.AreEqual(1, _collector.Calls.Count);
            Assert.AreEqual(CLIENT_A, _collector.Calls[0].ClientId);
        }

        [Test]
        public void test_hit_emission_mob_gone_from_both_pools_still_reports_to_the_target_player()
        {
            EntityID defender = AddPlayer(CLIENT_B);
            var despawnedMob = new EntityID(NEVER_ISSUED_ENTITY);

            _emitter.Emit(despawnedMob, defender, HitResult.MissOutOfRange);

            Assert.AreEqual(1, _collector.Calls.Count);
            Assert.AreEqual(CLIENT_B, _collector.Calls[0].ClientId);
            Assert.AreEqual(new HitCheckResult(despawnedMob, defender, HitResult.MissOutOfRange, TICK), Decode(_collector.Calls[0].Body));
        }

        [Test]
        public void test_hit_emission_two_attackers_in_one_tick_each_get_their_own_message()
        {
            EntityID first = AddPlayer(CLIENT_A);
            EntityID second = AddPlayer(CLIENT_B);
            EntityID mob = AddMob();

            _realEmitter.Emit(first, mob, HitResult.Hit);
            _realEmitter.Emit(second, mob, HitResult.MissFacing);
            _step.Flush(TICK);

            Assert.AreEqual(2, _seam.Packets.Count);
            Sent a = _seam.Packets.Find(p => p.ClientId == CLIENT_A);
            Sent b = _seam.Packets.Find(p => p.ClientId == CLIENT_B);
            CollectionAssert.AreEqual(new[] { new HitCheckResult(first, mob, HitResult.Hit, TICK) }, DecodePacket(a.Data));
            CollectionAssert.AreEqual(new[] { new HitCheckResult(second, mob, HitResult.MissFacing, TICK) }, DecodePacket(b.Data));
        }

        [Test]
        public void test_hit_emission_end_to_end_flushed_packet_decodes_to_the_emitted_result()
        {
            EntityID mob = AddMob();
            EntityID defender = AddPlayer(CLIENT_C);

            _realEmitter.Emit(mob, defender, HitResult.MissOutOfRange);
            _step.Flush(TICK);

            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(CLIENT_C, _seam.Packets[0].ClientId);
            Assert.IsTrue(BatchHeaderCodec.TryRead(_seam.Packets[0].Data, out ServerMessageEnvelope envelope, out _));
            Assert.AreEqual(TICK, envelope.ServerTickNumber);
            CollectionAssert.AreEqual(new[] { new HitCheckResult(mob, defender, HitResult.MissOutOfRange, TICK) },
                DecodePacket(_seam.Packets[0].Data));
        }

        [Test]
        public void test_hit_emission_tick_is_read_at_call_time()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID mob = AddMob();

            _emitter.Emit(attacker, mob, HitResult.Hit);
            _ticks.Tick = TICK + 3u;
            _emitter.Emit(attacker, mob, HitResult.Hit);

            Assert.AreEqual(2, _collector.Calls.Count);
            Assert.AreEqual(TICK, Decode(_collector.Calls[0].Body).ServerTick);
            Assert.AreEqual(TICK + 3u, Decode(_collector.Calls[1].Body).ServerTick);
        }

        [Test]
        public void test_hit_emission_invalid_attacker_is_logged_and_nothing_is_queued()
        {
            EntityID defender = AddPlayer(CLIENT_A);
            LogAssert.Expect(LogType.Error, new Regex("InvalidEntityId"));

            Assert.DoesNotThrow(() => _emitter.Emit(EntityID.Invalid, defender, HitResult.Hit));

            Assert.AreEqual(0, _collector.Calls.Count);
        }

        [Test]
        public void test_hit_emission_invalid_target_is_logged_and_nothing_is_queued()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            LogAssert.Expect(LogType.Error, new Regex("InvalidEntityId"));

            Assert.DoesNotThrow(() => _emitter.Emit(attacker, EntityID.Invalid, HitResult.Hit));

            Assert.AreEqual(0, _collector.Calls.Count);
        }

        [Test]
        public void test_hit_emission_invalid_ids_log_at_most_one_error_per_tick()
        {
            EntityID attacker = AddPlayer(CLIENT_A);
            LogAssert.Expect(LogType.Error, new Regex("InvalidEntityId"));

            _emitter.Emit(attacker, EntityID.Invalid, HitResult.Hit);
            _emitter.Emit(EntityID.Invalid, attacker, HitResult.Hit);
            LogAssert.NoUnexpectedReceived();

            _ticks.Tick = TICK + 1u;
            LogAssert.Expect(LogType.Error, new Regex("InvalidEntityId"));
            _emitter.Emit(attacker, EntityID.Invalid, HitResult.Hit);

            Assert.AreEqual(0, _collector.Calls.Count);
        }

        [Test]
        public void test_hit_emission_unmapped_result_value_does_not_throw_and_is_sent_as_given()
        {
            const byte UNMAPPED = 99;
            const int RESULT_OFFSET = 8;
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID mob = AddMob();

            Assert.DoesNotThrow(() => _emitter.Emit(attacker, mob, (HitResult)UNMAPPED));

            Assert.AreEqual(1, _collector.Calls.Count);
            Assert.AreEqual(UNMAPPED, _collector.Calls[0].Body[RESULT_OFFSET]);
        }

        [Test]
        public void test_hit_emission_emit_allocates_nothing()
        {
            _seam.Record = false;
            EntityID attacker = AddPlayer(CLIENT_A);
            EntityID defender = AddPlayer(CLIENT_B);
            EntityID mob = AddMob();
            _realEmitter.Emit(attacker, mob, HitResult.Hit);
            _realEmitter.Emit(mob, defender, HitResult.MissFacing);
            _step.Flush(TICK);

            Assert.That(() =>
            {
                _realEmitter.Emit(attacker, mob, HitResult.Hit);
                _realEmitter.Emit(mob, defender, HitResult.MissFacing);
            }, Is.Not.AllocatingGCMemory());

            _step.Flush(TICK + 1u);
            Assert.AreEqual(4, _seam.Count);
        }

        [Test]
        public void test_hit_emission_constructor_null_pool_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new HitResultEmitter(null, _collector, _ticks));
        }

        [Test]
        public void test_hit_emission_constructor_null_collector_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new HitResultEmitter(_pool, null, _ticks));
        }

        [Test]
        public void test_hit_emission_constructor_null_tick_source_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new HitResultEmitter(_pool, _collector, null));
        }
    }
}
