using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.HitDetection;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 009 (Rules 20-22, AC-18, AC-19, AC-HD-18): the
    /// <see cref="AutoFaceEvent"/> codec, the combat arming rules, and <see cref="AutoFaceEmitter"/> over the real
    /// <see cref="MovementService"/>, <see cref="RUBatchOutboundStep"/> and <see cref="PositionBroadcastStep"/>.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_AutoFace_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_ATTACKER = 11u;
        private const uint CLIENT_BYSTANDER = 12u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int CYCLE = 20;
        private const float TICK_DELTA = 0.05f;
        private const short FULL = 32767;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick = 1u;
            public uint ServerTickNumber => Tick;
        }

        private sealed class NullPoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Sent
        {
            public uint ClientId;
            public byte[] Data;
        }

        private sealed class RecordingSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Messages = new List<Sent>();
            public bool Record = true;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                if (Record)
                {
                    Messages.Add(new Sent { ClientId = clientId, Data = message.ToArray() });
                }
            }
        }

        private sealed class FakeSpeeds : IMovementSpeedSource
        {
            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                speed = 5.0f;
                return true;
            }
        }

        private sealed class PassSampler : INavMeshSampler
        {
            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                hit = candidate;
                return true;
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

        private sealed class NoopApplier : IAutoAttackDamageApplier
        {
            public void Apply(EntityID attacker, EntityID target, in IronGrind.DamageCalculation.DamageResult result) { }
        }

        private FakeTicks _ticks;
        private ZoneSlotPool _pool;
        private ZoneSlotPoolEntityIndex _index;
        private MovementIntentBuffer _buffer;
        private MovementService _movement;
        private RecordingSeam _seam;
        private ClientMessageOutbox _outbox;
        private RUBatchOutboundStep _ruBatch;
        private PositionBroadcastStep _positionStep;
        private FakeZoneState _state;
        private FakeFaceOutput _faceFake;
        private AutoFaceEmitter _emitter;
        private EntityID _attacker;
        private EntityID _bystander;
        private EntityID _mob;
        private uint _t;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, _ticks, new NullPoolListener(), MAX_PLAYERS, MAX_MOBS);
            _index = new ZoneSlotPoolEntityIndex(_pool);
            _buffer = new MovementIntentBuffer(_ticks, _index, MovementTuningValues.Defaults);
            _movement = new MovementService(_buffer, new FakeSpeeds(), new PassSampler(), _index);
            _seam = new RecordingSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _ruBatch = new RUBatchOutboundStep(_seam, _outbox, MAX_PLAYERS);
            _positionStep = new PositionBroadcastStep(_movement, _pool, _seam, _outbox);
            _state = new FakeZoneState();
            _faceFake = new FakeFaceOutput();
            _emitter = new AutoFaceEmitter(_pool, Zone, _movement, _ruBatch);
            _attacker = AddPlayer(CLIENT_ATTACKER);
            _bystander = AddPlayer(CLIENT_BYSTANDER);
            _mob = new EntityID(AutoAttackTestFakes.MOB_A_RAW);
            _t = 0u;
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out byte slot, out EntityID id));
            _movement.SeedForTesting(slot, id, Vector3.zero, 0);
            _outbox.AddClient(client);
            return id;
        }

        private AutoAttackCombatSystem NewCombat(IAutoAttackFaceOutput face)
        {
            return new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), new UnitAttackSpeed(), _state, new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), new NoopApplier(),
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), Zone, face);
        }

        private void RunTicks(AutoAttackCombatSystem combat, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                combat.Run(++_t, TICK_DELTA);
            }
        }

        private void Activate(AutoAttackCombatSystem combat)
        {
            combat.SetAutoAttack(_attacker, true);
            combat.SetTarget(_attacker, _mob);
        }

        private void FlushAll()
        {
            _outbox.Flush(_t);
            _ruBatch.Flush(_t);
        }

        private void MovementTick()
        {
            _ticks.Tick = _t + 1;
            _movement.Run(_t + 1, TICK_DELTA);
        }

        private List<Sent> BatchesTo(uint client)
        {
            var result = new List<Sent>();
            foreach (Sent s in _seam.Messages)
            {
                if (s.ClientId == client && MessageEnvelopeCodec.TryRead(s.Data, out ServerMessageEnvelope e)
                    && e.MessageTypeId == RUBatchWriter.TICK_BATCH_RU)
                {
                    result.Add(s);
                }
            }

            return result;
        }

        private static byte[] Tail(byte[] data, int offset)
        {
            byte[] result = new byte[data.Length - offset];
            Array.Copy(data, offset, result, 0, result.Length);
            return result;
        }

        private AutoFaceEvent DecodeSingle(uint client)
        {
            List<Sent> batches = BatchesTo(client);
            Assert.AreEqual(1, batches.Count, "one R-U batch for client " + client);
            byte[] data = batches[0].Data;
            Assert.IsTrue(BatchHeaderCodec.TryRead(data, out _, out ushort count));
            Assert.AreEqual(1, count);
            Assert.IsTrue(BatchSubMessageCodec.TryReadAutoFaceEvent(Tail(data, BatchHeaderCodec.HeaderSize), out AutoFaceEvent evt, out int read));
            Assert.AreEqual(AutoFaceEvent.BatchSize, read);
            return evt;
        }

        private short FacingOf(EntityID id)
        {
            Assert.IsTrue(_movement.TryGetFacing(id, out short facing));
            return facing;
        }

        // ------------------------------------------------------------------ codec

        [Test]
        public void test_auto_face_codec_round_trip_preserves_entity_rotation_and_direction()
        {
            Vector3 dir = new Vector3(1f, 0f, 1f).normalized;
            var evt = new AutoFaceEvent(new EntityID(7u), Quaternion.LookRotation(dir, Vector3.up), dir);
            var buffer = new byte[AutoFaceEvent.BatchSize];

            int written = BatchSubMessageCodec.WriteAutoFaceEvent(buffer, in evt);
            Assert.AreEqual(AutoFaceEvent.BatchSize, written);
            Assert.AreEqual(18, AutoFaceEvent.BodySize);
            Assert.AreEqual((ushort)0x0307, AutoFaceEvent.MessageTypeId);
            Assert.IsTrue(BatchSubMessageCodec.TryReadAutoFaceEvent(buffer, out AutoFaceEvent decoded, out int read));

            Assert.AreEqual(AutoFaceEvent.BatchSize, read);
            Assert.AreEqual(evt.EntityId, decoded.EntityId);
            Assert.Less(Quaternion.Angle(evt.Rotation, decoded.Rotation), 0.1f);
            Assert.Less(Vector3.Distance(evt.Direction, decoded.Direction), 0.001f);
        }

        [Test]
        public void test_auto_face_codec_short_buffer_is_rejected()
        {
            Assert.IsFalse(BatchSubMessageCodec.TryReadAutoFaceEvent(new byte[AutoFaceEvent.BatchSize - 1], out _, out int read));
            Assert.AreEqual(0, read);
        }

        [Test]
        public void test_auto_face_codec_zero_quaternion_is_substituted_with_identity()
        {
            var evt = new AutoFaceEvent(new EntityID(7u), new Quaternion(0f, 0f, 0f, 0f), Vector3.forward);
            var body = new byte[AutoFaceEvent.BodySize];
            LogAssert.Expect(LogType.Warning, new Regex(@"EncodeRotation: near-zero"));

            BatchSubMessageCodec.WriteAutoFaceEventBody(body, in evt);

            Quaternion decoded = WireFixedPointCodec.DecodeRotation(new ReadOnlySpan<byte>(body, 4, 8));
            Assert.Less(Quaternion.Angle(Quaternion.identity, decoded), 0.1f);
        }

        [Test]
        public void test_auto_face_codec_zero_direction_is_substituted_with_unit_x()
        {
            var evt = new AutoFaceEvent(new EntityID(7u), Quaternion.identity, Vector3.zero);
            var body = new byte[AutoFaceEvent.BodySize];
            LogAssert.Expect(LogType.Warning, new Regex(@"EncodeDirection: near-zero"));

            BatchSubMessageCodec.WriteAutoFaceEventBody(body, in evt);

            Vector3 decoded = WireFixedPointCodec.DecodeDirection(new ReadOnlySpan<byte>(body, 12, 6));
            Assert.Less(Vector3.Distance(Vector3.right, decoded), 0.001f);
        }

        // ------------------------------------------------------------------ combat arming

        [Test]
        public void test_auto_face_first_auto_after_activation_calls_the_seam_once_toward_the_target()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);

            RunTicks(combat, CYCLE - 1);
            Assert.AreEqual(0, _faceFake.Count, "no snap before the first auto attempt");
            RunTicks(combat, 1);

            Assert.AreEqual(1, _faceFake.Count);
            Assert.AreEqual(_attacker, _faceFake.Attackers[0]);
            Assert.AreEqual(1f, _faceFake.Directions[0].x, 0.001f);
            Assert.AreEqual(0f, _faceFake.Directions[0].z, 0.001f);
        }

        [Test]
        public void test_auto_face_second_auto_does_not_call_the_seam()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);

            RunTicks(combat, CYCLE * 3);

            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_toggle_off_then_on_snaps_again()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            RunTicks(combat, CYCLE);

            combat.SetAutoAttack(_attacker, false);
            combat.SetAutoAttack(_attacker, true);
            RunTicks(combat, CYCLE);

            Assert.AreEqual(2, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_new_target_selection_snaps_again()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            RunTicks(combat, CYCLE);

            combat.SetTarget(_attacker, new EntityID(AutoAttackTestFakes.MOB_B_RAW));
            RunTicks(combat, CYCLE);

            Assert.AreEqual(2, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_toggle_on_without_target_then_target_snaps_on_first_auto()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            combat.SetAutoAttack(_attacker, true);
            RunTicks(combat, CYCLE);
            Assert.AreEqual(0, _faceFake.Count);

            combat.SetTarget(_attacker, _mob);
            RunTicks(combat, CYCLE);

            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_prohibited_first_auto_does_not_snap_until_the_prohibition_clears()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            combat.SetCombatProhibited(_attacker, true);

            RunTicks(combat, CYCLE * 2);
            Assert.AreEqual(0, _faceFake.Count);

            combat.SetCombatProhibited(_attacker, false);
            RunTicks(combat, 1);
            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_missing_target_position_keeps_the_snap_for_the_retry()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            _state.TargetKnown = false;
            RunTicks(combat, CYCLE + 2);
            Assert.AreEqual(0, _faceFake.Count);

            _state.TargetKnown = true;
            RunTicks(combat, 1);

            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_out_of_range_first_auto_snaps_and_the_later_in_range_hit_does_not()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            _state.TargetX = AutoAttackTestFakes.Raw(10f);
            Activate(combat);

            RunTicks(combat, CYCLE);
            Assert.AreEqual(1, _faceFake.Count, "the out-of-range miss is the first auto");

            _state.TargetX = AutoAttackTestFakes.Raw(1f);
            RunTicks(combat, CYCLE);
            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_release_then_fresh_activation_snaps_exactly_once()
        {
            // The armed flag is cleared in ReleasePlayer and in GoIdle as hygiene, but every activation path re-arms it,
            // so neither clear is observable through the public API. This test pins the observable contract: a slot's
            // new occupant gets exactly one snap, never a stale one plus its own.
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            combat.ReleasePlayer(_attacker);
            RunTicks(combat, CYCLE);
            Assert.AreEqual(0, _faceFake.Count, "released: no target, no auto, no snap");

            Activate(combat);
            RunTicks(combat, CYCLE * 2);

            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_target_selected_with_toggle_off_snaps_once_on_the_first_auto_after_toggle_on()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            combat.SetTarget(_attacker, _mob);
            RunTicks(combat, CYCLE * 2);
            Assert.AreEqual(0, _faceFake.Count, "toggle OFF: idle, no auto");

            combat.SetAutoAttack(_attacker, true);
            RunTicks(combat, CYCLE - 1);
            Assert.AreEqual(0, _faceFake.Count, "no snap before the first auto");
            RunTicks(combat, CYCLE * 2 + 1);

            Assert.AreEqual(1, _faceFake.Count);
        }

        [Test]
        public void test_auto_face_deselect_then_reselect_the_same_mob_snaps_again()
        {
            AutoAttackCombatSystem combat = NewCombat(_faceFake);
            Activate(combat);
            RunTicks(combat, CYCLE);
            Assert.AreEqual(1, _faceFake.Count);

            combat.SetTarget(_attacker, EntityID.Invalid);
            combat.SetTarget(_attacker, _mob);
            RunTicks(combat, CYCLE);

            Assert.AreEqual(2, _faceFake.Count);
        }

        // ------------------------------------------------------------------ pipeline, codec bytes, routing, drop order

        private sealed class NoOpDispatcher : IInboundRequestDispatcher
        {
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) { return true; }

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) { return 0; }
        }

        private sealed class NoOpOutbound : IZoneOutboundStep
        {
            public void Flush(uint currentTick) { }
        }

        [Test]
        public void test_auto_face_through_zone_tick_pipeline_the_override_changes_facing_on_the_next_tick()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            Activate(combat);
            var steps = new List<IZoneSimulationStep> { _movement, combat };   // Movement before Combat (Story 006)
            var pipeline = new ZoneTickPipeline(new TickCompletionQueue(), new NoOpDispatcher(),
                new ZoneSimulationStep(steps), new NoOpOutbound(), _ticks);

            for (int i = 0; i < CYCLE; i++)
            {
                _ticks.Tick = ++_t;
                pipeline.Tick(TICK_DELTA);
            }

            Assert.AreEqual((short)0, FacingOf(_attacker), "tick N: the first auto fired, the override is only queued");

            _ticks.Tick = ++_t;
            pipeline.Tick(TICK_DELTA);

            Assert.AreEqual((short)900, FacingOf(_attacker), "tick N+1: movement applied the override");
        }

        [Test]
        public void test_auto_face_write_produces_the_expected_little_endian_bytes()
        {
            var evt = new AutoFaceEvent(new EntityID(0x01020304u), Quaternion.identity, Vector3.right);
            var buffer = new byte[AutoFaceEvent.BatchSize];

            BatchSubMessageCodec.WriteAutoFaceEvent(buffer, in evt);

            Assert.AreEqual(new byte[] { 20, 0, 0x07, 0x03 }, new[] { buffer[0], buffer[1], buffer[2], buffer[3] }, "length 18+2, type 0x0307");
            Assert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01 }, new[] { buffer[4], buffer[5], buffer[6], buffer[7] });
            Assert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0, 0xFF, 0x7F }, new ArraySegment<byte>(buffer, 8, 8).ToArray(), "identity: w = 32767");
            Assert.AreEqual(new byte[] { 0xFF, 0x7F, 0, 0, 0, 0 }, new ArraySegment<byte>(buffer, 16, 6).ToArray(), "direction (1,0,0)");
        }

        [Test]
        public void test_auto_face_write_with_an_invalid_entity_id_throws()
        {
            var evt = new AutoFaceEvent(EntityID.Invalid, Quaternion.identity, Vector3.right);
            LogAssert.Expect(LogType.Warning, new Regex(@"InvalidIdZeroWrite"));

            Assert.Throws<InvalidOperationException>(() => BatchSubMessageCodec.WriteAutoFaceEventBody(new byte[AutoFaceEvent.BodySize], in evt));
        }

        [Test]
        public void test_auto_face_routing_registry_has_one_zone_wide_r_u_row()
        {
            int rows = 0;
            foreach (MessageRoutingEntry row in MessageRoutingRegistry.AllEntries)
            {
                if (row.MessageTypeId == AutoFaceEvent.MessageTypeId)
                {
                    rows++;
                }
            }

            Assert.AreEqual(1, rows);
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(AutoFaceEvent.MessageTypeId, out MessageRoutingEntry entry));
            Assert.AreEqual(nameof(AutoFaceEvent), entry.MessageName);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToAllZoneClients, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.ReliableUnorderedBatch, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
            Assert.AreEqual(10, (int)RUBatchCategory.AutoFaceEvent);
            Assert.AreEqual(9, (int)RUBatchCategory.HitCheckResult);
        }

        private const ushort TYPE_CAST = 0x0901;
        private const ushort TYPE_LOOT = 0x0902;

        private static ushort[] ReadTypeIds(byte[] buffer, int written)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort count));
            var ids = new ushort[count];
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(new ReadOnlySpan<byte>(buffer, offset, written - offset), out ushort bodySize, out ushort type));
                ids[i] = type;
                offset += BatchSubMessageFraming.SubMessageHeaderSize + bodySize;
            }

            Assert.AreEqual(written, offset);
            return ids;
        }

        private static List<PendingSubMessage> DropOrderBatch(int castPayloadBytes)
        {
            return new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.SkillCastResult, TYPE_CAST, new byte[castPayloadBytes]),
                new PendingSubMessage(RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, new byte[HitCheckResult.BodySize]),
                new PendingSubMessage(RUBatchCategory.AutoFaceEvent, AutoFaceEvent.MessageTypeId, new byte[AutoFaceEvent.BodySize]),
                new PendingSubMessage(RUBatchCategory.LootBidUpdate, TYPE_LOOT, new byte[116]),
            };
        }

        [Test]
        public void test_auto_face_batch_writer_orders_hit_check_then_auto_face_then_loot_bid()
        {
            var pending = DropOrderBatch(8);
            pending.Reverse();
            var buffer = new byte[ClientBufferSet.BufferSize];

            int written = RUBatchWriter.Write(buffer, 1u, 1u, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(), pending, 1u);

            CollectionAssert.AreEqual(
                new[] { TYPE_CAST, HitCheckResult.MessageTypeId, AutoFaceEvent.MessageTypeId, TYPE_LOOT }, ReadTypeIds(buffer, written));
        }

        [TestCase(360, TestName = "test_auto_face_overflow_drops_loot_bid_first_and_keeps_auto_face_and_hit_check")]
        [TestCase(478, TestName = "test_auto_face_overflow_drops_auto_face_second_and_keeps_hit_check")]
        public void test_auto_face_overflow_drop_order(int castPayloadBytes)
        {
            var buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] BatchOverflow"));

            int written = RUBatchWriter.Write(buffer, 1u, 1u, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(),
                DropOrderBatch(castPayloadBytes), 1u);

            Assert.LessOrEqual(written, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);
            ushort[] expected = castPayloadBytes == 360
                ? new[] { TYPE_CAST, HitCheckResult.MessageTypeId, AutoFaceEvent.MessageTypeId }
                : new[] { TYPE_CAST, HitCheckResult.MessageTypeId };
            CollectionAssert.AreEqual(expected, ReadTypeIds(buffer, written));
        }

        [Test]
        public void test_auto_face_combat_constructor_null_face_output_throws()
        {
            Assert.Throws<ArgumentNullException>(() => NewCombat(null));
        }

        // ------------------------------------------------------------------ emitter and movement

        [Test]
        public void test_auto_face_first_auto_override_applies_on_the_next_movement_tick()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            Activate(combat);
            RunTicks(combat, CYCLE);
            Assert.AreEqual((short)0, FacingOf(_attacker), "queued, not applied yet");

            MovementTick();

            Assert.AreEqual((short)900, FacingOf(_attacker));
        }

        [Test]
        public void test_auto_face_180_degree_turn_is_applied()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            _state.TargetX = 0;
            _state.TargetZ = AutoAttackTestFakes.Raw(-1f);
            Activate(combat);
            RunTicks(combat, CYCLE);

            MovementTick();

            Assert.AreEqual(1800, Math.Abs((int)FacingOf(_attacker)));
        }

        [Test]
        public void test_auto_face_second_auto_does_not_change_the_facing()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            Activate(combat);
            RunTicks(combat, CYCLE);
            MovementTick();
            short snapped = FacingOf(_attacker);

            _state.TargetX = 0;
            _state.TargetZ = AutoAttackTestFakes.Raw(-1f);
            RunTicks(combat, CYCLE);
            MovementTick();

            Assert.AreEqual(snapped, FacingOf(_attacker));
        }

        [Test]
        public void test_auto_face_same_xz_position_sends_no_override_and_no_event()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            _state.TargetX = 0;
            _state.TargetZ = 0;
            Activate(combat);

            RunTicks(combat, CYCLE);
            MovementTick();
            FlushAll();

            Assert.AreEqual((short)0, FacingOf(_attacker));
            Assert.AreEqual(0, BatchesTo(CLIENT_ATTACKER).Count);
            Assert.AreEqual(0, BatchesTo(CLIENT_BYSTANDER).Count);
        }

        [TestCase(0f, 5f, 0f)]
        [TestCase(float.NaN, 0f, 1f)]
        [TestCase(float.PositiveInfinity, 0f, 0f)]
        public void test_auto_face_emitter_degenerate_direction_does_nothing(float x, float y, float z)
        {
            _emitter.Face(_attacker, new Vector3(x, y, z));
            _ticks.Tick = 2u;
            _movement.Run(2u, TICK_DELTA);
            FlushAll();

            Assert.AreEqual((short)0, FacingOf(_attacker));
            Assert.AreEqual(0, _seam.Messages.Count);
        }

        [Test]
        public void test_auto_face_emitter_unregistered_entity_sends_no_event()
        {
            _emitter.Face(new EntityID(AutoAttackTestFakes.MOB_A_RAW), Vector3.right);
            FlushAll();

            Assert.AreEqual(0, _seam.Messages.Count);
        }

        [Test]
        public void test_auto_face_event_goes_to_the_attacker_and_to_a_bystander()
        {
            _emitter.Face(_attacker, new Vector3(3f, 2f, 0f));
            FlushAll();

            AutoFaceEvent forAttacker = DecodeSingle(CLIENT_ATTACKER);
            AutoFaceEvent forBystander = DecodeSingle(CLIENT_BYSTANDER);
            Assert.AreEqual(_attacker, forAttacker.EntityId);
            Assert.AreEqual(forAttacker.EntityId, forBystander.EntityId);
            Assert.Less(Vector3.Distance(Vector3.right, forBystander.Direction), 0.001f, "flattened to XZ and normalised");
            Assert.Less(Quaternion.Angle(Quaternion.LookRotation(Vector3.right, Vector3.up), forBystander.Rotation), 0.1f);
        }

        [Test]
        public void test_auto_face_position_packet_byte_count_is_the_same_with_and_without_a_facing_change()
        {
            _ticks.Tick = 1u;
            _movement.Run(1u, TICK_DELTA);
            _positionStep.Flush(1u);
            int before = PositionPacketLength(CLIENT_BYSTANDER);
            _seam.Messages.Clear();

            _emitter.Face(_attacker, Vector3.right);
            _ticks.Tick = 2u;
            _movement.Run(2u, TICK_DELTA);
            Assert.AreEqual((short)900, FacingOf(_attacker));
            _positionStep.Flush(2u);
            int after = PositionPacketLength(CLIENT_BYSTANDER);

            Assert.Greater(before, 0);
            Assert.AreEqual(before, after);
        }

        private int PositionPacketLength(uint client)
        {
            foreach (Sent s in _seam.Messages)
            {
                if (s.ClientId == client && MessageEnvelopeCodec.TryRead(s.Data, out ServerMessageEnvelope e)
                    && e.MessageTypeId == PositionPacketWriter.TICK_BATCH_UU)
                {
                    return s.Data.Length;
                }
            }

            return 0;
        }

        [Test]
        public void test_auto_face_client_movement_intent_still_moves_the_player_after_the_override()
        {
            AutoAttackCombatSystem combat = NewCombat(_emitter);
            Activate(combat);
            RunTicks(combat, CYCLE);
            MovementTick();

            uint intentTick = _t + 2;
            _ticks.Tick = intentTick;
            var body = new byte[MovementIntentMessage.BodySize];
            var message = new MovementIntentMessage(0, FULL, 0, intentTick);
            MovementIntentMessageCodec.WriteBody(body, in message);
            var context = new InboundRequestContext(CLIENT_ATTACKER, _attacker, new CharacterID(CLIENT_ATTACKER),
                MovementIntentMessage.MessageTypeId, intentTick, intentTick, false);
            _buffer.HandleMovementIntent(in context, body);
            Assert.IsTrue(_movement.TryGetPosition(_attacker, out Vector3 before));
            _movement.Run(intentTick, TICK_DELTA);

            Assert.IsTrue(_movement.TryGetPosition(_attacker, out Vector3 after));
            Assert.Greater(after.z, before.z, "the intent after the one-tick override moves the player along +Z");
        }

        [Test]
        public void test_auto_face_emit_allocates_nothing()
        {
            _emitter.Face(_attacker, Vector3.right);
            FlushAll();
            _seam.Record = false;
            _emitter.Face(_attacker, Vector3.right);
            FlushAll();

            Assert.That(() => _emitter.Face(_attacker, Vector3.back), Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------------ batch order

        [Test]
        public void test_auto_face_batch_writes_the_event_right_after_the_hit_check_result()
        {
            var hit = new HitCheckResult(_attacker, _mob, HitResult.Hit, 5u);
            var hitBody = new byte[HitCheckResult.BodySize];
            BatchSubMessageCodec.WriteHitCheckResultBody(hitBody, in hit);
            var face = new AutoFaceEvent(_attacker, Quaternion.identity, Vector3.forward);
            var faceBody = new byte[AutoFaceEvent.BodySize];
            BatchSubMessageCodec.WriteAutoFaceEventBody(faceBody, in face);

            _ruBatch.Enqueue(CLIENT_ATTACKER, RUBatchCategory.AutoFaceEvent, AutoFaceEvent.MessageTypeId, faceBody);
            _ruBatch.Enqueue(CLIENT_ATTACKER, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, hitBody);
            FlushAll();

            byte[] data = BatchesTo(CLIENT_ATTACKER)[0].Data;
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + HitCheckResult.BatchSize + AutoFaceEvent.BatchSize, data.Length);
            byte[] first = Tail(data, BatchHeaderCodec.HeaderSize);
            Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(first, out HitCheckResult decodedHit, out int read));
            Assert.AreEqual(hit, decodedHit);
            Assert.IsTrue(BatchSubMessageCodec.TryReadAutoFaceEvent(Tail(first, read), out AutoFaceEvent decodedFace, out _));
            Assert.AreEqual(_attacker, decodedFace.EntityId);
        }
    }
}
