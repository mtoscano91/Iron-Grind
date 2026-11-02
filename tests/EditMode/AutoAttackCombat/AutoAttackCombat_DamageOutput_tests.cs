using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
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
    /// EditMode tests for Auto-Attack Combat Story 007: <see cref="AutoAttackDamageOutputEmitter"/> and
    /// <see cref="DamageOutputApplier"/> over the real <see cref="RUBatchOutboundStep"/> and <see cref="ClientMessageOutbox"/>
    /// (AC-CCR-04, AC-MCR-02, AC-HD-12, AC-HD-13).
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_DamageOutput_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_ATTACKER = 11u;
        private const uint CLIENT_BYSTANDER = 12u;
        private const uint CLIENT_GHOST = 13u;
        private const uint TICK = 4242u;
        private const int DAMAGE = 137;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int INTRACLASS_CAP = 27;

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
            public bool Record = true;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                if (Record)
                {
                    Packets.Add(new Sent(clientId, message.ToArray(), channel));
                }
            }
        }

        private sealed class RecordingApplier : IAutoAttackDamageApplier
        {
            private readonly List<string> _log;
            public int Count;

            public RecordingApplier(List<string> log)
            {
                _log = log;
            }

            public void Apply(EntityID attacker, EntityID target, in DamageResult result)
            {
                Count++;
                _log.Add("apply");
            }
        }

        private sealed class RecordingOutput : IAutoAttackDamageOutput
        {
            private readonly List<string> _log;
            public int Count;

            public RecordingOutput(List<string> log)
            {
                _log = log;
            }

            public void Emit(EntityID attacker, EntityID target, in DamageResult result)
            {
                Count++;
                _log.Add("emit");
            }
        }

        /// <summary>Collector fake whose damage-event intake throws, to drive the emitter's fault path.</summary>
        private sealed class ThrowingCollector : IRUBatchCollector
        {
            public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body) { }

            public void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent)
            {
                throw new InvalidOperationException("collector-failure");
            }
        }

        /// <summary>Inner applier that throws, to prove the decorator emits nothing after a failed apply.</summary>
        private sealed class ThrowingApplier : IAutoAttackDamageApplier
        {
            public void Apply(EntityID attacker, EntityID target, in DamageResult result)
            {
                throw new InvalidOperationException("applier-failure");
            }
        }

        private sealed class AlwaysValid : IAutoAttackTargetValidity
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
                return 1.0f;
            }
        }

        private const uint CLIENT_THIRD = 14u;
        private const int CYCLE = 20;
        private const float TICK_DELTA = 0.05f;
        private const float RANGE = 3.0f;
        private const float FAR_X = 5.0f;

        private FakeTicks _ticks;
        private ZoneSlotPool _pool;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private RUBatchOutboundStep _step;
        private AutoAttackDamageOutputEmitter _emitter;
        private EntityID _attacker;
        private EntityID _bystander;
        private EntityID _ghost;
        private EntityID _mob;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, _ticks, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_ATTACKER);
            _outbox.AddClient(CLIENT_BYSTANDER);
            // The ghost has a pool slot but its client is never added to the outbox.
            _step = new RUBatchOutboundStep(_seam, _outbox, MAX_PLAYERS);
            _emitter = new AutoAttackDamageOutputEmitter(_pool, Zone, _outbox, _step);
            _attacker = AddPlayer(CLIENT_ATTACKER);
            _mob = AddMob();
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

        private static DamageResult Hit(int damage = DAMAGE, bool isCrit = false, bool isKill = false)
        {
            return new DamageResult(damage, 0, damage, isCrit, isKill, DamageContext.PhysicalAuto, false);
        }

        private void FlushAll()
        {
            _outbox.Flush(TICK);
            _step.Flush(TICK);
        }

        private static bool IsSelfDamage(Sent p)
        {
            return MessageEnvelopeCodec.TryRead(p.Data, out ServerMessageEnvelope e) && e.MessageTypeId == SelfDamageEvent.MessageTypeId;
        }

        private static bool IsBatch(Sent p)
        {
            return MessageEnvelopeCodec.TryRead(p.Data, out ServerMessageEnvelope e) && e.MessageTypeId == RUBatchWriter.TICK_BATCH_RU;
        }

        private List<Sent> SelfPackets(uint client)
        {
            return _seam.Packets.FindAll(p => p.ClientId == client && IsSelfDamage(p));
        }

        private List<Sent> BatchPackets(uint client)
        {
            return _seam.Packets.FindAll(p => p.ClientId == client && IsBatch(p));
        }

        // Decodes a batch that holds only DamageEvent sub-messages.
        private static List<DamageEvent> DecodeDamageBatch(byte[] packet)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out _, out ushort count));
            var events = new List<DamageEvent>();
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(new ReadOnlySpan<byte>(packet, offset, packet.Length - offset), out DamageEvent decoded, out int read));
                events.Add(decoded);
                offset += read;
            }

            Assert.AreEqual(packet.Length, offset);
            return events;
        }

        [Test]
        public void test_damage_output_hit_attacker_gets_self_event_only_and_bystander_gets_damage_event_only()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult result = Hit();

            _emitter.Emit(_attacker, _mob, in result);
            FlushAll();

            Assert.AreEqual(1, SelfPackets(CLIENT_ATTACKER).Count);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, SelfPackets(CLIENT_ATTACKER)[0].Channel);
            Assert.AreEqual(0, BatchPackets(CLIENT_ATTACKER).Count);
            Assert.AreEqual(0, SelfPackets(CLIENT_BYSTANDER).Count);
            List<Sent> batches = BatchPackets(CLIENT_BYSTANDER);
            Assert.AreEqual(1, batches.Count);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, batches[0].Channel);
            Assert.AreEqual(1, DecodeDamageBatch(batches[0].Data).Count);
            Assert.AreEqual(2, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_final_damage_and_ids_match_the_result_on_both_messages()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult result = Hit(DAMAGE);

            _emitter.Emit(_attacker, _mob, in result);
            FlushAll();

            Assert.IsTrue(SelfDamageEventCodec.TryRead(SelfPackets(CLIENT_ATTACKER)[0].Data, out _, out DamageEvent self));
            DamageEvent broadcast = DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data)[0];
            var expected = new DamageEvent(_attacker, _mob, DAMAGE, false, DamageType.Physical);
            Assert.AreEqual(expected, self);
            Assert.AreEqual(expected, broadcast);
            Assert.AreEqual(result.FinalDamage, self.FinalDamage);
            Assert.AreEqual(result.FinalDamage, broadcast.FinalDamage);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void test_damage_output_crit_flag_and_physical_type_round_trip_on_both_messages(bool isCrit)
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult result = Hit(DAMAGE, isCrit);

            _emitter.Emit(_attacker, _mob, in result);
            FlushAll();

            Assert.IsTrue(SelfDamageEventCodec.TryRead(SelfPackets(CLIENT_ATTACKER)[0].Data, out _, out DamageEvent self));
            DamageEvent broadcast = DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data)[0];
            Assert.AreEqual(isCrit, self.IsCrit);
            Assert.AreEqual(isCrit, broadcast.IsCrit);
            Assert.AreEqual(DamageType.Physical, self.DamageType);
            Assert.AreEqual(DamageType.Physical, broadcast.DamageType);
        }

        [Test]
        public void test_damage_output_damage_event_is_written_before_hit_check_result_in_the_batch()
        {
            EntityID bystander = AddPlayer(CLIENT_BYSTANDER);
            var hitEmitter = new HitResultEmitter(_pool, _step, _ticks);
            hitEmitter.Emit(_mob, bystander, HitResult.Hit);
            DamageResult result = Hit();

            _emitter.Emit(_attacker, _mob, in result);
            FlushAll();

            byte[] packet = BatchPackets(CLIENT_BYSTANDER)[0].Data;
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out _, out ushort count));
            Assert.AreEqual(2, count);
            int offset = BatchHeaderCodec.HeaderSize;
            Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(new ReadOnlySpan<byte>(packet, offset, packet.Length - offset), out DamageEvent first, out int read));
            Assert.AreEqual(DAMAGE, first.FinalDamage);
            offset += read;
            Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(new ReadOnlySpan<byte>(packet, offset, packet.Length - offset), out HitCheckResult second, out _));
            Assert.AreEqual(HitResult.Hit, second.Result);
        }

        [Test]
        public void test_damage_output_miss_with_no_damage_sends_nothing()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult rejected = DamageResult.Rejected(DamageContext.PhysicalAuto);

            _emitter.Emit(_attacker, _mob, in rejected);
            FlushAll();

            // The combat system never calls Apply on a miss (Story 003 tests); this covers the emitter's own guard.
            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_kill_sends_the_same_two_messages_and_nothing_else()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult kill = Hit(DAMAGE, true, true);

            _emitter.Emit(_attacker, _mob, in kill);
            FlushAll();

            Assert.AreEqual(2, _seam.Packets.Count);
            Assert.AreEqual(1, SelfPackets(CLIENT_ATTACKER).Count);
            var expected = new DamageEvent(_attacker, _mob, DAMAGE, true, DamageType.Physical);
            Assert.IsTrue(SelfDamageEventCodec.TryRead(SelfPackets(CLIENT_ATTACKER)[0].Data, out _, out DamageEvent self));
            Assert.AreEqual(expected, self);
            List<DamageEvent> events = DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(expected, events[0]);
        }

        [Test]
        public void test_damage_output_solo_attacker_gets_only_the_self_event_and_no_batch()
        {
            DamageResult result = Hit();

            _emitter.Emit(_attacker, _mob, in result);
            FlushAll();

            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(1, SelfPackets(CLIENT_ATTACKER).Count);
            Assert.AreEqual(0, BatchPackets(CLIENT_ATTACKER).Count);
        }

        [Test]
        public void test_damage_output_ghost_client_gets_nothing_and_nothing_throws_or_logs()
        {
            AddPlayer(CLIENT_BYSTANDER);
            AddPlayer(CLIENT_GHOST);
            DamageResult result = Hit();

            Assert.DoesNotThrow(() => _emitter.Emit(_attacker, _mob, in result));
            Assert.DoesNotThrow(FlushAll);

            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == CLIENT_GHOST));
            Assert.AreEqual(1, BatchPackets(CLIENT_BYSTANDER).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_damage_output_ghost_attacker_sends_no_self_event_and_bystander_still_gets_the_broadcast()
        {
            EntityID ghost = AddPlayer(CLIENT_GHOST);
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult result = Hit();

            Assert.DoesNotThrow(() => _emitter.Emit(ghost, _mob, in result));
            Assert.DoesNotThrow(FlushAll);

            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == CLIENT_GHOST));
            Assert.AreEqual(0, SelfPackets(CLIENT_BYSTANDER).Count);
            Assert.AreEqual(1, BatchPackets(CLIENT_BYSTANDER).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_damage_output_unmapped_damage_context_is_logged_once_and_not_sent()
        {
            AddPlayer(CLIENT_BYSTANDER);
            var unmapped = new DamageResult(DAMAGE, 0, DAMAGE, false, false, (DamageContext)99, false);
            LogAssert.Expect(LogType.Error, new Regex("UnmappedDamageContext"));

            Assert.DoesNotThrow(() => _emitter.Emit(_attacker, _mob, in unmapped));
            Assert.DoesNotThrow(() => _emitter.Emit(_attacker, _mob, in unmapped));
            FlushAll();

            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_enqueue_still_refuses_the_damage_event_category()
        {
            LogAssert.Expect(LogType.Error, new Regex("MessageRefused"));

            _step.Enqueue(CLIENT_ATTACKER, RUBatchCategory.DamageEvent, DamageEvent.MessageTypeId, new byte[DamageEvent.BodySize]);
            FlushAll();

            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_more_than_the_intraclass_cap_keeps_the_newest_and_does_not_throw()
        {
            int total = INTRACLASS_CAP + 1;
            LogAssert.Expect(LogType.Warning, new Regex("DamageEventIntraclassOverflow"));

            for (int i = 0; i < total; i++)
            {
                var evt = new DamageEvent(_attacker, _mob, i + 1, false, DamageType.Physical);
                _step.EnqueueDamageEvent(CLIENT_BYSTANDER, in evt);
            }

            Assert.DoesNotThrow(FlushAll);

            List<DamageEvent> events = DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data);
            Assert.AreEqual(INTRACLASS_CAP, events.Count);
            Assert.AreEqual(2, events[0].FinalDamage);
            Assert.AreEqual(total, events[events.Count - 1].FinalDamage);
        }

        [Test]
        public void test_damage_output_beyond_the_storage_bound_drops_with_a_batch_full_warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("BatchFull"));
            LogAssert.Expect(LogType.Warning, new Regex("DamageEventIntraclassOverflow"));

            for (int i = 0; i <= RUBatchOutboundStep.RU_MAX_PENDING_MESSAGES; i++)
            {
                var evt = new DamageEvent(_attacker, _mob, i + 1, false, DamageType.Physical);
                _step.EnqueueDamageEvent(CLIENT_BYSTANDER, in evt);
            }

            Assert.DoesNotThrow(FlushAll);

            Assert.AreEqual(INTRACLASS_CAP, DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data).Count);
        }

        [Test]
        public void test_damage_output_dispatcher_body_for_attacker_throws_on_recipient_mismatch()
        {
            var evt = new DamageEvent(_attacker, _mob, DAMAGE, false, DamageType.Physical);
            byte[] body = new byte[DamageEvent.BodySize];

            Assert.Throws<ArgumentException>(() => SelfDamageEventDispatcher.WriteBodyForAttacker(body, _mob, in evt));
        }

        [Test]
        public void test_damage_output_dispatcher_body_matches_the_body_of_the_full_message()
        {
            var evt = new DamageEvent(_attacker, _mob, DAMAGE, true, DamageType.Physical);
            byte[] full = new byte[SelfDamageEvent.WireSize];
            byte[] body = new byte[DamageEvent.BodySize];

            SelfDamageEventCodec.Write(full, 1u, TICK, in evt);
            int written = SelfDamageEventDispatcher.WriteBodyForAttacker(body, _attacker, in evt);

            Assert.AreEqual(DamageEvent.BodySize, written);
            for (int i = 0; i < body.Length; i++)
            {
                Assert.AreEqual(full[ServerMessageEnvelope.WireSize + i], body[i], $"byte {i}");
            }
        }

        [Test]
        public void test_damage_output_emit_and_enqueue_damage_event_allocate_nothing()
        {
            _seam.Record = false;
            AddPlayer(CLIENT_BYSTANDER);
            AddPlayer(CLIENT_THIRD);
            _outbox.AddClient(CLIENT_THIRD);
            DamageResult result = Hit();
            var evt = new DamageEvent(_attacker, _mob, DAMAGE, false, DamageType.Physical);
            _emitter.Emit(_attacker, _mob, in result);
            _step.EnqueueDamageEvent(CLIENT_BYSTANDER, in evt);
            FlushAll();

            Assert.That(() =>
            {
                _emitter.Emit(_attacker, _mob, in result);
                _emitter.Emit(_attacker, _mob, in result);
            }, Is.Not.AllocatingGCMemory());
            FlushAll();
            Assert.That(() => _step.EnqueueDamageEvent(CLIENT_BYSTANDER, in evt), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_damage_output_decorator_calls_the_inner_applier_once_before_the_output()
        {
            var log = new List<string>();
            var inner = new RecordingApplier(log);
            var output = new RecordingOutput(log);
            var decorator = new DamageOutputApplier(inner, output);
            DamageResult result = Hit();

            decorator.Apply(_attacker, _mob, in result);

            Assert.AreEqual(1, inner.Count);
            Assert.AreEqual(1, output.Count);
            CollectionAssert.AreEqual(new[] { "apply", "emit" }, log);
        }

        private AutoAttackCombatSystem NewRealCombat(IAutoAttackDamageCalculator calc, IAutoAttackDamageApplier inner, FakeZoneState state, FakeHitEmitter hitEmitter)
        {
            return new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), new UnitAttackSpeed(), state, new HitDetector(HitDetectionConfig.Default),
                hitEmitter, new FakeStatSource { AttackRange = RANGE }, calc,
                new DamageOutputApplier(inner, _emitter),
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), Zone);
        }

        private static void FireOneCycle(AutoAttackCombatSystem combat, EntityID attacker, EntityID target)
        {
            combat.SetAutoAttack(attacker, true);
            combat.SetTarget(attacker, target);
            uint t = 0u;
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);
            }
        }

        // The combat system here uses a FakeHitEmitter, not the R-U batch, so a miss yields zero packets on the seam.
        [Test]
        public void test_damage_output_real_combat_miss_sends_nothing_and_never_applies()
        {
            AddPlayer(CLIENT_BYSTANDER);
            var inner = new RecordingApplier(new List<string>());
            var hitEmitter = new FakeHitEmitter();
            var state = new FakeZoneState { TargetX = AutoAttackTestFakes.Raw(FAR_X) };
            AutoAttackCombatSystem combat = NewRealCombat(new FakeDamageCalc { Result = Hit() }, inner, state, hitEmitter);

            FireOneCycle(combat, _attacker, new EntityID(AutoAttackTestFakes.MOB_A_RAW));
            FlushAll();

            Assert.AreEqual(HitResult.MissOutOfRange, hitEmitter.Results[0]);
            Assert.AreEqual(0, inner.Count);
            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_real_combat_rejected_damage_sends_nothing_and_never_applies()
        {
            AddPlayer(CLIENT_BYSTANDER);
            var inner = new RecordingApplier(new List<string>());
            var hitEmitter = new FakeHitEmitter();
            var calc = new FakeDamageCalc { Result = DamageResult.Rejected(DamageContext.PhysicalAuto) };
            AutoAttackCombatSystem combat = NewRealCombat(calc, inner, new FakeZoneState(), hitEmitter);

            FireOneCycle(combat, _attacker, new EntityID(AutoAttackTestFakes.MOB_A_RAW));
            FlushAll();

            Assert.AreEqual(HitResult.Hit, hitEmitter.Results[0]);
            Assert.AreEqual(1, calc.Calls);
            Assert.AreEqual(0, inner.Count);
            Assert.AreEqual(0, _seam.Packets.Count);
        }

        [Test]
        public void test_damage_output_real_combat_hit_sends_self_event_to_attacker_and_damage_event_to_bystander()
        {
            AddPlayer(CLIENT_BYSTANDER);
            var inner = new RecordingApplier(new List<string>());
            EntityID target = new EntityID(AutoAttackTestFakes.MOB_A_RAW);
            AutoAttackCombatSystem combat = NewRealCombat(new FakeDamageCalc { Result = Hit(DAMAGE) }, inner, new FakeZoneState(), new FakeHitEmitter());

            FireOneCycle(combat, _attacker, target);
            FlushAll();

            var expected = new DamageEvent(_attacker, target, DAMAGE, false, DamageType.Physical);
            Assert.AreEqual(1, inner.Count);
            Assert.AreEqual(1, SelfPackets(CLIENT_ATTACKER).Count);
            Assert.IsTrue(SelfDamageEventCodec.TryRead(SelfPackets(CLIENT_ATTACKER)[0].Data, out _, out DamageEvent self));
            Assert.AreEqual(expected, self);
            Assert.AreEqual(1, BatchPackets(CLIENT_BYSTANDER).Count);
            Assert.AreEqual(expected, DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data)[0]);
        }

        [Test]
        public void test_damage_output_emitter_fault_is_swallowed_and_logged_once()
        {
            AddPlayer(CLIENT_BYSTANDER);
            var faulty = new AutoAttackDamageOutputEmitter(_pool, Zone, _outbox, new ThrowingCollector());
            DamageResult result = Hit();
            LogAssert.Expect(LogType.Error, new Regex("EmitThrew"));

            Assert.DoesNotThrow(() => faulty.Emit(_attacker, _mob, in result));
            Assert.DoesNotThrow(() => faulty.Emit(_attacker, _mob, in result));
        }

        [Test]
        public void test_damage_output_decorator_propagates_an_inner_throw_and_emits_nothing()
        {
            var output = new RecordingOutput(new List<string>());
            var decorator = new DamageOutputApplier(new ThrowingApplier(), output);
            DamageResult result = Hit();

            Assert.Throws<InvalidOperationException>(() => decorator.Apply(_attacker, _mob, in result));

            Assert.AreEqual(0, output.Count);
        }

        [Test]
        public void test_damage_output_two_hits_in_one_tick_arrive_in_call_order_on_both_paths()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult first = Hit(10);
            DamageResult second = Hit(20);

            _emitter.Emit(_attacker, _mob, in first);
            _emitter.Emit(_attacker, _mob, in second);
            FlushAll();

            List<DamageEvent> events = DecodeDamageBatch(BatchPackets(CLIENT_BYSTANDER)[0].Data);
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(10, events[0].FinalDamage);
            Assert.AreEqual(20, events[1].FinalDamage);
            List<Sent> selfPackets = SelfPackets(CLIENT_ATTACKER);
            Assert.AreEqual(2, selfPackets.Count);
            Assert.IsTrue(SelfDamageEventCodec.TryRead(selfPackets[0].Data, out _, out DamageEvent firstSelf));
            Assert.IsTrue(SelfDamageEventCodec.TryRead(selfPackets[1].Data, out _, out DamageEvent secondSelf));
            Assert.AreEqual(10, firstSelf.FinalDamage);
            Assert.AreEqual(20, secondSelf.FinalDamage);
        }

        [Test]
        public void test_damage_output_attacker_released_before_emit_sends_no_self_event_and_bystander_still_gets_damage_event()
        {
            AddPlayer(CLIENT_BYSTANDER);
            Assert.IsTrue(_pool.ReleasePlayer(_attacker));
            DamageResult result = Hit();

            Assert.DoesNotThrow(() => _emitter.Emit(_attacker, _mob, in result));
            Assert.DoesNotThrow(FlushAll);

            Assert.AreEqual(0, SelfPackets(CLIENT_ATTACKER).Count);
            Assert.AreEqual(1, BatchPackets(CLIENT_BYSTANDER).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_damage_output_bystander_removed_between_emit_and_flush_gets_nothing_and_nothing_throws()
        {
            AddPlayer(CLIENT_BYSTANDER);
            DamageResult result = Hit();
            _emitter.Emit(_attacker, _mob, in result);

            _outbox.RemoveClient(CLIENT_BYSTANDER);
            Assert.DoesNotThrow(FlushAll);

            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == CLIENT_BYSTANDER));
            Assert.AreEqual(1, SelfPackets(CLIENT_ATTACKER).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_damage_output_constructors_reject_null_arguments()
        {
            var log = new List<string>();
            Assert.Throws<ArgumentNullException>(() => new DamageOutputApplier(null, new RecordingOutput(log)));
            Assert.Throws<ArgumentNullException>(() => new DamageOutputApplier(new RecordingApplier(log), null));
            Assert.Throws<ArgumentNullException>(() => new AutoAttackDamageOutputEmitter(null, Zone, _outbox, _step));
            Assert.Throws<ArgumentNullException>(() => new AutoAttackDamageOutputEmitter(_pool, Zone, null, _step));
            Assert.Throws<ArgumentNullException>(() => new AutoAttackDamageOutputEmitter(_pool, Zone, _outbox, null));
        }
    }
}
