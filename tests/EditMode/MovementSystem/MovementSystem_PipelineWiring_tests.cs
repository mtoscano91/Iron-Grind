using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode integration tests for Movement Story 009 - composition wiring and the per-tick position broadcast.
    /// Composition tests use <see cref="WiRig"/> (real backend over fakes). The NavMesh sampler in the composition is the
    /// real <see cref="UnityNavMeshSampler"/>, which finds no NavMesh in EditMode, so the movement-driven end-to-end
    /// test runs at <see cref="PositionBroadcastStep"/> + <see cref="MovementService"/> level with a pass-through sampler.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_PipelineWiring_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint BASE_CLIENT = 100u;
        private const int BASE_ATTACK = 20;
        private const short FULL = 32767;
        private const int PACKET_CAP = 35;

        private static readonly Regex PRNG_LINE = new Regex(@"^\[Random\] PRNG seed: -?\d+$");
        private static readonly Regex OVERFLOW_WARNING = new Regex(@"\[PositionBroadcastStep\] BatchOverflow");
        private static readonly Vector3 TownPoint = new Vector3(12f, 0f, -4f);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        // ------------------------------------------------------------------ fakes

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

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return TownPoint; }
        }

        private struct Sent
        {
            public uint ClientId;
            public byte[] Data;
            public NetworkChannel Channel;
        }

        private sealed class RecordingSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Messages = new List<Sent>();
            public readonly HashSet<uint> ThrowFor = new HashSet<uint>();
            public bool Record = true;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                if (ThrowFor.Contains(clientId))
                    throw new InvalidOperationException("seam failed");

                if (!Record)
                    return;

                Messages.Add(new Sent { ClientId = clientId, Data = message.ToArray(), Channel = channel });
            }
        }

        private sealed class AllocatingStep : IZoneOutboundStep
        {
            private readonly IZoneOutboundStep _inner;
            public object Sink;

            public AllocatingStep(IZoneOutboundStep inner) { _inner = inner; }

            public void Flush(uint currentTick)
            {
                Sink = new object[16];
                _inner.Flush(currentTick);
            }
        }

        // ------------------------------------------------------------------ step-level rig

        private ZoneSlotPool _pool;
        private FakeTicks _ticks;
        private ZoneSlotPoolEntityIndex _index;
        private MovementIntentBuffer _buffer;
        private MovementService _movement;
        private RecordingSeam _seam;
        private ClientMessageOutbox _outbox;
        private PositionBroadcastStep _step;
        private ZoneDefinition _definition;
        private readonly List<EntityID> _ids = new List<EntityID>();
        private readonly List<byte> _slots = new List<byte>();
        private readonly Dictionary<uint, Vector3> _positions = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, uint> _clientOf = new Dictionary<uint, uint>();

        [SetUp]
        public void SetUp()
        {
            _ids.Clear();
            _slots.Clear();
            _positions.Clear();
            _clientOf.Clear();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            _ticks = new FakeTicks();
            _index = new ZoneSlotPoolEntityIndex(_pool);
            _buffer = new MovementIntentBuffer(_ticks, _index, MovementTuningValues.Defaults);
            _movement = new MovementService(_buffer, new FakeSpeeds(), new PassSampler(), _index);
            _seam = new RecordingSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _step = new PositionBroadcastStep(_movement, _pool, _seam, _outbox);
        }

        [TearDown]
        public void TearDown()
        {
            if (_definition != null)
            {
                UnityEngine.Object.DestroyImmediate(_definition);
                _definition = null;
            }
        }

        private static Vector3 PositionOf(int n) { return new Vector3(n * 0.5f, 0f, -n * 1.0f); }

        private EntityID AddPlayer(int n, bool addToOutbox = true)
        {
            uint clientId = BASE_CLIENT + (uint)n;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID((uint)n), new AccountID((uint)n + 5000u), clientId,
                out byte slot, out EntityID id));
            Vector3 pos = PositionOf(n);
            _movement.SeedForTesting(slot, id, pos, 0);
            if (addToOutbox)
                _outbox.AddClient(clientId);

            _ids.Add(id);
            _slots.Add(slot);
            _positions[id.RawValue] = pos;
            _clientOf[id.RawValue] = clientId;
            return id;
        }

        private List<EntityID> SortedIds()
        {
            var sorted = new List<EntityID>(_ids);
            sorted.Sort((a, b) => a.RawValue.CompareTo(b.RawValue));
            return sorted;
        }

        private static ServerMessageEnvelope Decode(byte[] data, List<EntityPositionUpdate> entries)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(data, out ServerMessageEnvelope envelope, out ushort count));
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(BatchSubMessageCodec.TryReadEntityPositionUpdate(new ReadOnlySpan<byte>(data).Slice(offset),
                    out EntityPositionUpdate entry, out int read));
                entries.Add(entry);
                offset += read;
            }

            return envelope;
        }

        private static List<Sent> PositionPacketsTo(RecordingSeam seam, uint clientId)
        {
            var result = new List<Sent>();
            foreach (Sent s in seam.Messages)
            {
                if (s.ClientId == clientId)
                    result.Add(s);
            }

            return result;
        }

        private void AssertPacketHoldsOthers(EntityID receiver, List<EntityID> sortedAll, int expectedCount)
        {
            var expected = new List<EntityID>();
            foreach (EntityID id in sortedAll)
            {
                if (id != receiver && expected.Count < expectedCount)
                    expected.Add(id);
            }

            List<Sent> packets = PositionPacketsTo(_seam, _clientOf[receiver.RawValue]);
            Assert.AreEqual(1, packets.Count, "one packet for " + receiver);
            var entries = new List<EntityPositionUpdate>();
            ServerMessageEnvelope envelope = Decode(packets[0].Data, entries);
            Assert.AreEqual((ushort)0x0102, envelope.MessageTypeId);
            Assert.AreEqual(NetworkChannel.Unreliable, packets[0].Channel);
            Assert.AreEqual(expected.Count, entries.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i], entries[i].EntityId);
                Vector3 p = _positions[expected[i].RawValue];
                Assert.AreEqual((short)Mathf.RoundToInt(p.x * 100f), entries[i].PosX);
                Assert.AreEqual((short)Mathf.RoundToInt(p.y * 100f), entries[i].PosY);
                Assert.AreEqual((short)Mathf.RoundToInt(p.z * 100f), entries[i].PosZ);
            }
        }

        // ------------------------------------------------------------------ composition rig

        private static WiRig StartRig(ITownRespawnPointProvider respawn = null)
        {
            var rig = new WiRig();
            if (respawn != null)
                rig.Comp.RespawnProviderOverride = respawn;

            LogAssert.Expect(LogType.Log, PRNG_LINE);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private static EntityID AllocCompositionPlayer(ZoneSlotPool pool, uint n, uint clientId, out byte slot)
        {
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(n), new AccountID(n + 5000u), clientId,
                out slot, out EntityID id));
            return id;
        }

        private static BuffDefinition ApBuff()
        {
            return new BuffDefinition((BuffID)1u, new[] { new StatModifier(StatID.AttackPower, 10f, 0f) }, 100u, 0f, 0f);
        }

        // ------------------------------------------------------------------ composition

        [Test]
        public void test_composition_simulation_steps_are_instancing_then_status_effects_then_movement()
        {
            WiRig rig = StartRig();

            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            Assert.AreEqual(3, steps.Count);
            Assert.AreSame(rig.Comp.ZoneInstancing, steps[0]);
            Assert.AreSame(rig.Comp.StatusEffects, steps[1]);
            Assert.AreSame(rig.Comp.Movement, steps[2]);
            Assert.IsInstanceOf<MovementService>(steps[2]);
        }

        [Test]
        public void test_composition_movement_getters_set_and_inbound_registration_precedes_seal()
        {
            WiRig rig = StartRig();

            Assert.IsNotNull(rig.Comp.Movement);
            Assert.IsNotNull(rig.Comp.PositionBroadcast);
            int buffer = rig.Observed.IndexOf("MovementIntentBuffer");
            int seal = rig.Observed.IndexOf("Seal");
            Assert.GreaterOrEqual(buffer, 0);
            Assert.Less(buffer, seal, "the intent buffer (and its inbound registration) is built before the dispatcher is sealed");
            Assert.Less(rig.Observed.IndexOf("MovementService"), rig.Observed.IndexOf("PositionBroadcastStep"));
        }

        [Test]
        public void test_composition_entry_listener_is_movement_so_an_admitted_player_is_registered_at_the_respawn_point()
        {
            WiRig rig = StartRig(new FakeRespawn());
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            EntityID id = AllocCompositionPlayer(pool, 1u, 11u, out byte slot);
            ZoneEntryCompletion entry = rig.Comp.ZoneInstancing.EntryCompletion;

            Assert.Throws<InvalidOperationException>(() => entry.SetEntryListener(rig.Comp.Movement),
                "a listener is already set by the composition");

            // The stand-in state source of the wiring logs on the snapshot steps; only the registration is asserted.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                entry.OnPlayerAdmitted(11u, slot, id);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsTrue(rig.Comp.Movement.TryGetPosition(id, out Vector3 position));
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f);
            Assert.AreEqual(TownPoint.z, position.z, 1e-4f);
        }

        [Test]
        public void test_composition_release_runs_status_effects_and_movement()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            var stats = rig.Comp.ZoneCharacterStats;
            EntityID id = AllocCompositionPlayer(pool, 1u, 11u, out byte slot);
            stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            stats.SetBaseStat(id, StatID.Defense, 10);
            stats.SetBaseStat(id, StatID.MaxHP, 400);
            stats.SetBaseStat(id, StatID.MaxMP, 80);
            stats.SetCurrentHP(id, 100f);
            stats.SetCurrentMP(id, 40f);
            rig.Comp.Movement.SeedForTesting(slot, id, Vector3.zero, 0);
            Assert.AreEqual(ApplyResult.Success, rig.Comp.StatusEffects.ApplyEffect(id, id, ApBuff()));
            Assert.AreEqual(BASE_ATTACK + 10, stats.GetEffectiveStat(id, StatID.AttackPower));
            Assert.IsTrue(rig.Comp.Movement.TryGetPosition(id, out _));

            Assert.IsTrue(pool.ReleasePlayer(id));

            Assert.AreEqual(BASE_ATTACK, stats.GetEffectiveStat(id, StatID.AttackPower), "Status Effects cleared");
            Assert.IsFalse(rig.Comp.Movement.TryGetPosition(id, out _), "Movement unregistered");
        }

        [Test]
        public void test_composition_invalid_navmesh_instance_marks_movement_unavailable()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            EntityID id = AllocCompositionPlayer(pool, 1u, 11u, out byte slot);
            rig.Comp.Movement.SeedForTesting(slot, id, Vector3.zero, 0);
            rig.Comp.Movement.InjectFacingOverride(id, Vector3.right);
            LogAssert.Expect(LogType.Log, "[ZoneServer] ghost session TTL enumeration not implemented");
            LogAssert.Expect(LogType.Error, new Regex(@"\[Movement\] NavMesh unavailable"));

            rig.Net.FireTick();

            Assert.IsTrue(rig.Comp.Movement.TryGetFacing(id, out short facing));
            Assert.AreEqual((short)0, facing, "an available Movement would have applied the override facing (900)");
        }

        [Test]
        public void test_composition_tick_sends_each_client_the_other_players_position_packet()
        {
            WiRig rig = StartRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            EntityID p1 = AllocCompositionPlayer(pool, 1u, 11u, out byte slot1);
            EntityID p2 = AllocCompositionPlayer(pool, 2u, 12u, out byte slot2);
            rig.Comp.Movement.SeedForTesting(slot1, p1, new Vector3(1.234f, 0f, 2.5f), 0);
            rig.Comp.Movement.SeedForTesting(slot2, p2, new Vector3(-3f, 0f, 0.004f), 0);
            rig.Net.ClientListener.AddClient(11u);
            rig.Net.ClientListener.AddClient(12u);
            LogAssert.Expect(LogType.Log, "[ZoneServer] ghost session TTL enumeration not implemented");
            // The scene fake hands S6 a default (invalid) NavMesh instance, so Movement is unavailable: seeded
            // positions hold and are still broadcast.
            LogAssert.Expect(LogType.Error, new Regex(@"\[Movement\] NavMesh unavailable"));

            rig.Net.FireTick();

            var to11 = new List<EntityPositionUpdate>();
            var to12 = new List<EntityPositionUpdate>();
            int packets11 = 0;
            int packets12 = 0;
            foreach (var sent in rig.Net.Seam.Sent)
            {
                if (!BatchHeaderCodec.TryRead(sent.data, out ServerMessageEnvelope env, out _)
                    || env.MessageTypeId != PositionPacketWriter.TICK_BATCH_UU)
                    continue;

                Assert.AreEqual(NetworkChannel.Unreliable, sent.channel);
                Assert.AreEqual(rig.Comp.TickLoop.ServerTickNumber, env.ServerTickNumber);
                if (sent.client == 11u) { packets11++; Decode(sent.data, to11); }
                if (sent.client == 12u) { packets12++; Decode(sent.data, to12); }
            }

            Assert.AreEqual(1, packets11);
            Assert.AreEqual(1, packets12);
            Assert.AreEqual(1, to11.Count);
            Assert.AreEqual(p2, to11[0].EntityId);
            Assert.AreEqual((short)-300, to11[0].PosX);
            Assert.AreEqual((short)0, to11[0].PosZ);
            Assert.AreEqual(1, to12.Count);
            Assert.AreEqual(p1, to12[0].EntityId);
            Assert.AreEqual((short)123, to12[0].PosX);
            Assert.AreEqual((short)250, to12[0].PosZ);
        }

        // ------------------------------------------------------------------ PositionBroadcastStep

        [Test]
        public void test_broadcast_three_players_each_client_gets_the_two_others_sorted_unreliable()
        {
            AddPlayer(1);
            AddPlayer(2);
            AddPlayer(3);

            _step.Flush(5u);

            Assert.AreEqual(3, _seam.Messages.Count);
            List<EntityID> sorted = SortedIds();
            foreach (EntityID id in _ids)
                AssertPacketHoldsOthers(id, sorted, 2);
        }

        [Test]
        public void test_broadcast_sequence_numbers_come_from_the_outbox_counter_consecutively()
        {
            EntityID a = AddPlayer(1);
            AddPlayer(2);
            uint client = _clientOf[a.RawValue];
            Assert.IsTrue(_outbox.TryGetSequenceCounter(client, out ConnectionSequenceCounter counter));
            uint outboxMessage = counter.IssueNext();

            _step.Flush(5u);
            _step.Flush(6u);

            List<Sent> packets = PositionPacketsTo(_seam, client);
            Assert.AreEqual(2, packets.Count);
            var entries = new List<EntityPositionUpdate>();
            ServerMessageEnvelope first = Decode(packets[0].Data, entries);
            ServerMessageEnvelope second = Decode(packets[1].Data, entries);
            Assert.AreEqual(outboxMessage + 1u, first.SequenceNumber);
            Assert.AreEqual(outboxMessage + 2u, second.SequenceNumber);
            Assert.AreEqual(5u, first.ServerTickNumber);
            Assert.AreEqual(6u, second.ServerTickNumber);
        }

        [Test]
        public void test_broadcast_solo_player_gets_no_packet()
        {
            AddPlayer(1);

            _step.Flush(5u);

            Assert.AreEqual(0, _seam.Messages.Count);
            Assert.IsFalse(_step.IsOverflowing);
        }

        [Test]
        public void test_broadcast_client_unknown_to_the_outbox_is_skipped_silently()
        {
            EntityID known = AddPlayer(1);
            EntityID unknown = AddPlayer(2, addToOutbox: false);

            _step.Flush(5u);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, _seam.Messages.Count);
            Assert.AreEqual(_clientOf[known.RawValue], _seam.Messages[0].ClientId);
            var entries = new List<EntityPositionUpdate>();
            Decode(_seam.Messages[0].Data, entries);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(unknown, entries[0].EntityId, "the unknown client is still listed to the others");
        }

        [Test]
        public void test_broadcast_throwing_seam_for_one_client_logs_one_error_and_others_are_still_sent()
        {
            AddPlayer(1);
            EntityID middle = AddPlayer(2);
            AddPlayer(3);
            uint bad = _clientOf[middle.RawValue];
            _seam.ThrowFor.Add(bad);
            LogAssert.Expect(LogType.Error, new Regex(@"\[PositionBroadcastStep\] SendThrew: clientId=" + bad));

            _step.Flush(5u);

            Assert.AreEqual(2, _seam.Messages.Count);
            Assert.AreEqual(0, PositionPacketsTo(_seam, bad).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_overflow_37_players_each_receiver_gets_35_lowest_others_and_one_warning()
        {
            for (int n = 1; n <= 37; n++)
                AddPlayer(n);

            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);

            _step.Flush(5u);

            List<EntityID> sorted = SortedIds();
            foreach (EntityID id in _ids)
                AssertPacketHoldsOthers(id, sorted, PACKET_CAP);
            Assert.IsTrue(_step.IsOverflowing);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_second_overflowing_tick_logs_no_new_warning()
        {
            for (int n = 1; n <= 37; n++)
                AddPlayer(n);

            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);
            _step.Flush(5u);
            _step.Flush(6u);

            LogAssert.NoUnexpectedReceived();
            Assert.IsTrue(_step.IsOverflowing);
        }

        [Test]
        public void test_broadcast_overflow_ends_at_36_players_and_warns_again_when_it_returns_to_37()
        {
            for (int n = 1; n <= 37; n++)
                AddPlayer(n);

            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);
            _step.Flush(5u);
            EntityID last = _ids[36];
            _movement.BeforeRelease(last);

            _step.Flush(6u);

            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(_step.IsOverflowing, "36 players: every receiver has 35 others, no drops");

            _movement.SeedForTesting(_slots[36], last, PositionOf(37), 0);
            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);

            _step.Flush(7u);

            Assert.IsTrue(_step.IsOverflowing);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_end_to_end_intent_on_tick_n_moves_player_1_in_client_2s_packet_of_tick_n()
        {
            const uint TICK = 10u;
            EntityID p1 = AddPlayer(1);
            _movement.SeedForTesting(_slots[0], p1, Vector3.zero, 0);
            _positions[p1.RawValue] = Vector3.zero;
            EntityID p2 = AddPlayer(2);
            _ticks.Tick = TICK;
            var body = new byte[MovementIntentMessage.BodySize];
            var message = new MovementIntentMessage(0, FULL, 0, TICK);
            MovementIntentMessageCodec.WriteBody(body, in message);
            var context = new InboundRequestContext(_clientOf[p1.RawValue], p1, new CharacterID(1u),
                MovementIntentMessage.MessageTypeId, TICK, TICK, false);

            _buffer.HandleMovementIntent(in context, body);
            _movement.Run(TICK, ServerTickLoop.FIXED_DELTA_TIME);
            _step.Flush(TICK);

            List<Sent> packets = PositionPacketsTo(_seam, _clientOf[p2.RawValue]);
            Assert.AreEqual(1, packets.Count);
            var entries = new List<EntityPositionUpdate>();
            ServerMessageEnvelope envelope = Decode(packets[0].Data, entries);
            Assert.AreEqual(TICK, envelope.ServerTickNumber);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(p1, entries[0].EntityId);
            Assert.AreEqual((short)0, entries[0].PosX);
            Assert.AreEqual((short)25, entries[0].PosZ, "5.0 m/s x 0.05 s = 0.25 m = wire 25");
        }

        [Test]
        public void test_broadcast_tick_with_50_players_and_50_clients_allocates_nothing_after_warm_up()
        {
            for (int n = 1; n <= 50; n++)
                AddPlayer(n);

            _seam.Record = false;
            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);
            _step.Flush(1u); // warm-up; logs the overflow warning

            Assert.That(() => _step.Flush(2u), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "a 50 player overflowing broadcast tick allocated");
            Assert.IsTrue(_step.IsOverflowing);
        }

        [Test]
        public void test_broadcast_allocation_measurement_detects_a_deliberately_allocating_step()
        {
            for (int n = 1; n <= 50; n++)
                AddPlayer(n);

            _seam.Record = false;
            var allocating = new AllocatingStep(_step);
            LogAssert.Expect(LogType.Warning, OVERFLOW_WARNING);
            allocating.Flush(1u);

            Assert.That(() => allocating.Flush(2u), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "the GC.Alloc measure did not register the double's allocation");
        }

        // ------------------------------------------------------------------ ZoneDefinitionSlotRespawnPointProvider

        [Test]
        public void test_respawn_provider_null_slot_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneDefinitionSlotRespawnPointProvider(Zone, null));
        }

        [Test]
        public void test_respawn_provider_empty_slot_logs_error_returns_zero_and_builds_no_cache()
        {
            var provider = new ZoneDefinitionSlotRespawnPointProvider(Zone, new ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Error, new Regex(@"\[ZoneInstancing\] TownRespawnPointMissing"));

            Vector3 point = provider.GetTownRespawnPoint(Zone);

            Assert.AreEqual(Vector3.zero, point);
            Assert.AreEqual(0, provider.CacheBuildCount);
        }

        [Test]
        public void test_respawn_provider_filled_slot_builds_the_cache_once_and_reuses_it()
        {
            _definition = ScriptableObject.CreateInstance<ZoneDefinition>();
            _definition.Initialize(default(ZoneTemplateID), new Vector3(-50f, -5f, -50f), new Vector3(50f, 5f, 50f),
                TownPoint, true, null);
            var slot = new ZoneDefinitionSlot();
            var provider = new ZoneDefinitionSlotRespawnPointProvider(Zone, slot);
            slot.Set(_definition);

            Vector3 first = provider.GetTownRespawnPoint(Zone);
            Vector3 second = provider.GetTownRespawnPoint(Zone);

            Assert.AreEqual(TownPoint, first);
            Assert.AreEqual(TownPoint, second);
            Assert.AreEqual(1, provider.CacheBuildCount);
        }
    }
}
