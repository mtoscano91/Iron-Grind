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
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 006: the <see cref="SetAutoAttack"/> wire type, its inbound handler,
    /// the <see cref="SetTarget"/> forwarding, the tick order (EC-8), the one-auto-per-tick rule (AC-16) and the slot
    /// release hook. The composition step order is asserted in <c>ZoneServer_CompositionWiring_tests.cs</c>.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_InputAndComposition_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint SEQUENCE = 1u;
        private const uint ENVELOPE_TICK = 1000u;
        private const uint SENDER_RAW = 501u;
        private const uint CLIENT_ONE = 11u;
        private const uint CLIENT_TWO = 12u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint DISPATCH_TICK = 8u;
        private const uint FIRST_PIPELINE_TICK = 1u;
        private const float DELTA = 0.05f;
        private const uint UNKNOWN_ENTITY_RAW = 999999u;
        private const byte MALFORMED_TWO = 2;
        private const byte MALFORMED_MAX = 255;
        private const ushort ID_RANGE_FIRST = 0xE000;
        private const ushort ID_RANGE_LAST = 0xE0FF;

        // AC-16: the run length is a whole number of attack cycles of the player (read from Combat), not a magic tick count.
        private const int AC16_CYCLE_COUNT = 3;

        // The rate-limit test sends this many toggles in one tick; it stays below MAX_INBOX_REQUESTS_PER_CONNECTION (64).
        private const int RATE_LIMIT_MESSAGE_COUNT = 50;

        // Ticks run for "never fires" checks where no cycle was ever sampled (so the cycle length is 0).
        private const int NEVER_FIRES_TICKS = 100;

        // Cycle length for a unit attack speed is well above this; tests that count ticks below one cycle assert it.
        private const int SHORT_RUN_TICKS = 5;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static EntityID MobA => new EntityID(AutoAttackTestFakes.MOB_A_RAW);

        private static EntityID MobB => new EntityID(AutoAttackTestFakes.MOB_B_RAW);

        // Records every call; forwards to Inner when set (so the real Combat can still run behind the recorder).
        private sealed class RecordingInputSink : IAutoAttackInputSink
        {
            public readonly List<KeyValuePair<EntityID, EntityID>> Targets = new List<KeyValuePair<EntityID, EntityID>>();
            public readonly List<KeyValuePair<EntityID, bool>> Toggles = new List<KeyValuePair<EntityID, bool>>();
            public IAutoAttackInputSink Inner;

            public void SetTarget(EntityID attacker, EntityID target)
            {
                Targets.Add(new KeyValuePair<EntityID, EntityID>(attacker, target));
                Inner?.SetTarget(attacker, target);
            }

            public void SetAutoAttack(EntityID attacker, bool enabled)
            {
                Toggles.Add(new KeyValuePair<EntityID, bool>(attacker, enabled));
                Inner?.SetAutoAttack(attacker, enabled);
            }
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        // Combat's own validity: mobs only (Rule 1 rejects a player target).
        private sealed class MobValidity : IAutoAttackTargetValidity
        {
            public bool IsValidTarget(EntityID target)
            {
                return target == new EntityID(AutoAttackTestFakes.MOB_A_RAW) || target == new EntityID(AutoAttackTestFakes.MOB_B_RAW);
            }
        }

        private sealed class UnitAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                return 1.0f;
            }
        }

        private sealed class NoOpOutbound : IZoneOutboundStep
        {
            public void Flush(uint currentTick) { }
        }

        // The dispatcher (real guard chain, real routing registry), the real pool and Combat, and a real ZoneTickPipeline.
        private sealed class Rig
        {
            public InboundDispatchHarness Harness;
            public ZoneSlotPool Pool;
            public AutoAttackCombatSystem Combat;
            public FakeDamageApplier Applier;
            public ZoneTickPipeline Pipeline;
            public TargetSlotTracker Tracker;

            // The tracker's valid-entity set: the zone's players and mobs (RFR-3a), kept live as players come and go.
            public List<EntityID> ValidIds;
            private uint _sequence = SEQUENCE;
            private uint _tick;

            public EntityID AddPlayer(uint client, bool sessionReady = true)
            {
                Assert.IsTrue(Pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET), client,
                    out _, out EntityID id));
                Assert.IsTrue(Harness.Dispatcher.AddConnection(client));
                Harness.GuardChain.RegisterEntityOwnership(client, id);
                if (sessionReady)
                {
                    Harness.GuardChain.MarkSessionReady(client);
                }

                Harness.Directory.Set(client, new CharacterID(client));
                ValidIds.Add(id);
                return id;
            }

            // Zone exit: the pool releases the slot (Combat's hook runs) and the entity leaves the valid set.
            public bool Release(EntityID id)
            {
                ValidIds.Remove(id);
                return Pool.ReleasePlayer(id);
            }

            public bool SendToggle(uint client, uint senderRaw, bool enabled)
            {
                var message = new byte[SetAutoAttack.WireSize];
                SetAutoAttackCodec.Write(message, _sequence++, ENVELOPE_TICK, senderRaw, enabled);
                return Harness.Dispatcher.TryAccept(client, message);
            }

            public bool SendBody(uint client, uint senderRaw, byte[] body)
            {
                byte[] message = InboundMessageBuilder.Request(SetAutoAttack.MessageTypeId, _sequence++, senderRaw, body);
                return Harness.Dispatcher.TryAccept(client, message);
            }

            public bool SendTarget(uint client, uint senderRaw, uint targetRaw)
            {
                var message = new byte[SetTarget.WireSize];
                SetTargetCodec.Write(message, _sequence++, ENVELOPE_TICK, senderRaw, targetRaw);
                return Harness.Dispatcher.TryAccept(client, message);
            }

            // One pipeline tick; the applier records the tick of every hit it receives.
            public void Tick(uint tick)
            {
                _tick = tick;
                Applier.Tick = tick;
                Harness.TickSource.Tick = tick;
                Pipeline.Tick(DELTA);
            }

            // The next tick number: 1, 2, 3, ...
            public uint Advance()
            {
                Tick(_tick + 1u);
                return _tick;
            }
        }

        // sinkOverride set: the handlers forward to it instead of the real Combat (Combat still runs in the pipeline).
        private static Rig NewRig(IAutoAttackInputSink sinkOverride = null)
        {
            var harness = new InboundDispatchHarness(false, null, MessageRoutingRegistry.TryGetEntry);
            var pool = new ZoneSlotPool(Zone, harness.TickSource, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            var applier = new FakeDamageApplier();
            var combat = new AutoAttackCombatSystem(
                pool, new MobValidity(), new UnitAttackSpeed(), new FakeZoneState(), new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), applier, new FakeXpAward(),
                new FakeExperienceSink(), new FakeMobTypeSource(), default(ZoneID));
            pool.SetReleaseListener(new CompositeZoneSlotReleaseListener(combat));

            IAutoAttackInputSink sink = sinkOverride ?? combat;
            var tracker = new TargetSlotTracker();
            var validZoneEntityIds = new List<EntityID> { MobA, MobB };
            NetworkingCoreInboundRegistration.RegisterCombatInput(harness.Dispatcher,
                new SetTargetRequestHandler(tracker, validZoneEntityIds, sink), new SetAutoAttackRequestHandler(sink));
            harness.Dispatcher.Seal();

            var pipeline = new ZoneTickPipeline(
                new TickCompletionQueue(), harness.Dispatcher,
                new ZoneSimulationStep(new List<IZoneSimulationStep> { combat }), new NoOpOutbound(), harness.TickSource);
            return new Rig
            {
                Harness = harness, Pool = pool, Combat = combat, Applier = applier, Pipeline = pipeline, Tracker = tracker,
                ValidIds = validZoneEntityIds
            };
        }

        // ---------------------------------------------------------------- Codec

        [TestCase(true)]
        [TestCase(false)]
        public void test_codec_round_trips_the_toggle(bool enabled)
        {
            var buffer = new byte[SetAutoAttack.WireSize];

            int written = SetAutoAttackCodec.Write(buffer, SEQUENCE, ENVELOPE_TICK, SENDER_RAW, enabled);
            bool ok = SetAutoAttackCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out bool decoded);

            Assert.AreEqual(SetAutoAttack.WireSize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(SetAutoAttack.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(enabled, decoded);
        }

        [Test]
        public void test_codec_body_is_exactly_one_byte_and_the_wire_size_adds_the_envelope()
        {
            Assert.AreEqual(1, SetAutoAttack.BodySize);
            Assert.AreEqual(ClientEntityMessageEnvelope.WireSize + SetAutoAttack.BodySize, SetAutoAttack.WireSize);
        }

        [TestCase(MALFORMED_TWO)]
        [TestCase(MALFORMED_MAX)]
        public void test_codec_rejects_a_body_byte_other_than_0_or_1(byte value)
        {
            bool ok = SetAutoAttackCodec.TryReadBody(new[] { value }, out bool enabled);

            Assert.IsFalse(ok);
            Assert.IsFalse(enabled);
        }

        [Test]
        public void test_codec_rejects_a_body_of_the_wrong_length()
        {
            Assert.IsFalse(SetAutoAttackCodec.TryReadBody(new byte[0], out _));
            Assert.IsFalse(SetAutoAttackCodec.TryReadBody(new byte[SetAutoAttack.BodySize + 1], out _));
        }

        [Test]
        public void test_codec_full_message_with_a_wrong_message_type_is_rejected()
        {
            // Well-formed in every other way: right length, valid body byte 1. Only the type id differs.
            var buffer = new byte[SetAutoAttack.WireSize];
            MessageEnvelopeCodec.Write(buffer, new ClientEntityMessageEnvelope(SetTarget.MessageTypeId, SEQUENCE, ENVELOPE_TICK, SENDER_RAW));
            buffer[ClientEntityMessageEnvelope.WireSize] = 1;

            bool ok = SetAutoAttackCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out bool enabled);

            Assert.IsFalse(ok);
            Assert.IsFalse(enabled);
            Assert.AreEqual(default(ClientEntityMessageEnvelope), envelope);
        }

        [TestCase(0)]
        [TestCase(SetAutoAttack.WireSize - 1)]
        [TestCase(SetAutoAttack.WireSize + 1)]
        public void test_codec_full_message_with_a_wrong_total_length_is_rejected(int length)
        {
            var buffer = new byte[Math.Max(length, SetAutoAttack.WireSize)];
            SetAutoAttackCodec.Write(buffer, SEQUENCE, ENVELOPE_TICK, SENDER_RAW, true);

            bool ok = SetAutoAttackCodec.TryRead(new ReadOnlySpan<byte>(buffer, 0, length), out _, out bool enabled);

            Assert.IsFalse(ok);
            Assert.IsFalse(enabled);
        }

        // ---------------------------------------------------------------- Registry

        [Test]
        public void test_routing_row_is_client_to_server_reliable_ordered_in_the_rod_range()
        {
            bool found = MessageRoutingRegistry.TryGetEntry(SetAutoAttack.MessageTypeId, out MessageRoutingEntry entry);

            Assert.IsTrue(found);
            Assert.AreEqual(nameof(SetAutoAttack), entry.MessageName);
            Assert.AreEqual(MessageDirection.ClientToServer, entry.Direction);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.GreaterOrEqual(SetAutoAttack.MessageTypeId, ID_RANGE_FIRST);
            Assert.LessOrEqual(SetAutoAttack.MessageTypeId, ID_RANGE_LAST);
            Assert.IsTrue(MessageRoutingRegistry.ValidateAndRoute(SetAutoAttack.MessageTypeId, true).WasClassified);
        }

        [Test]
        public void test_every_rpc_type_tag_is_accepted_by_the_guard_chain_so_each_has_a_gap()
        {
            foreach (RpcTypeTag tag in Enum.GetValues(typeof(RpcTypeTag)))
            {
                var chain = new CrossCuttingRpcGuardChain();
                var entity = new EntityID(SENDER_RAW);
                chain.RegisterEntityOwnership(CLIENT_ONE, entity);
                chain.MarkSessionReady(CLIENT_ONE);

                RpcGuardResult result = RpcGuardResult.RejectedUnknownEntity;
                Assert.DoesNotThrow(
                    () => result = chain.Evaluate(new InboundRpcDescriptor(CLIENT_ONE, entity, tag, DISPATCH_TICK)),
                    $"RpcTypeTag.{tag} must have a gap");
                Assert.AreEqual(RpcGuardResult.Accepted, result, $"RpcTypeTag.{tag}");
            }
        }

        [Test]
        public void test_set_auto_attack_has_no_rate_limit_many_in_one_tick_are_all_accepted_by_the_guard_chain()
        {
            var chain = new CrossCuttingRpcGuardChain();
            var entity = new EntityID(SENDER_RAW);
            chain.RegisterEntityOwnership(CLIENT_ONE, entity);
            chain.MarkSessionReady(CLIENT_ONE);
            var descriptor = new InboundRpcDescriptor(CLIENT_ONE, entity, RpcTypeTag.SetAutoAttack, DISPATCH_TICK);

            int accepted = 0;
            for (int i = 0; i < RATE_LIMIT_MESSAGE_COUNT; i++)
            {
                if (chain.Evaluate(descriptor) == RpcGuardResult.Accepted)
                {
                    accepted++;
                }
            }

            Assert.AreEqual(RATE_LIMIT_MESSAGE_COUNT, accepted);
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.SetAutoAttack));
        }

        [Test]
        public void test_set_auto_attack_many_toggles_in_one_tick_all_reach_the_sink_through_the_dispatcher()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);

            int accepted = 0;
            for (int i = 0; i < RATE_LIMIT_MESSAGE_COUNT; i++)
            {
                if (rig.SendToggle(CLIENT_ONE, player.RawValue, i % 2 == 0))
                {
                    accepted++;
                }
            }

            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.AreEqual(RATE_LIMIT_MESSAGE_COUNT, accepted, "every message is accepted into the inbox");
            Assert.AreEqual(RATE_LIMIT_MESSAGE_COUNT, sink.Toggles.Count, "none is rate limited at dispatch");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Dispatch and guards

        [TestCase(true)]
        [TestCase(false)]
        public void test_dispatch_set_auto_attack_reaches_combat_on_the_next_dispatch_tick(bool enabled)
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);

            bool accepted = rig.SendToggle(CLIENT_ONE, player.RawValue, enabled);
            int beforeDispatch = sink.Toggles.Count;
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.IsTrue(accepted);
            Assert.AreEqual(0, beforeDispatch, "applied only at the dispatch step");
            Assert.AreEqual(1, sink.Toggles.Count);
            Assert.AreEqual(player, sink.Toggles[0].Key);
            Assert.AreEqual(enabled, sink.Toggles[0].Value);
        }

        [Test]
        public void test_guard_a_non_owner_sender_is_rejected_as_not_owner_and_does_not_reach_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID own = rig.AddPlayer(CLIENT_ONE);
            EntityID other = rig.AddPlayer(CLIENT_TWO);
            Assume.That(own, Is.Not.EqualTo(other));
            LogAssert.Expect(LogType.Warning, new Regex(
                $@"\[CrossCuttingRpcGuardChain\] Evaluate: RPC tag=SetAutoAttack from clientId={CLIENT_ONE} references EntityID .*owned by clientId={CLIENT_TWO}"));

            bool accepted = rig.SendToggle(CLIENT_ONE, other.RawValue, true);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.IsTrue(accepted, "the intake accepts it; the guard chain drops it at dispatch");
            Assert.AreEqual(0, sink.Toggles.Count);
        }

        [Test]
        public void test_guard_a_client_that_is_not_session_ready_is_rejected_as_session_not_ready_and_does_not_reach_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE, sessionReady: false);
            LogAssert.Expect(LogType.Warning, new Regex(
                $@"\[CrossCuttingRpcGuardChain\] Evaluate: RPC tag=SetAutoAttack from clientId={CLIENT_ONE} arrived before SessionReady"));

            bool accepted = rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.IsTrue(accepted, "the intake accepts it; the guard chain drops it at dispatch");
            Assert.AreEqual(0, sink.Toggles.Count);
        }

        [TestCase(MALFORMED_TWO)]
        [TestCase(MALFORMED_MAX)]
        public void test_malformed_body_value_is_dropped_with_a_log_and_changes_no_state(byte value)
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SetAutoAttackRequestHandler\] InboundMessageMalformed"));

            rig.SendBody(CLIENT_ONE, player.RawValue, new[] { value });
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);
            rig.Combat.SetTarget(player, MobA);

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(player), "the toggle is still OFF");
        }

        // The dispatcher only drops a body LONGER than MaxBodyBytes (InboundRequestDispatcher.TryAcceptCore, body.Length >
        // type.MaxBodyBytes); a 0-length body passes intake and reaches the handler, which logs the warning.
        [Test]
        public void test_malformed_empty_body_passes_the_dispatcher_and_is_dropped_by_the_handler_with_a_log()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SetAutoAttackRequestHandler\] InboundMessageMalformed: .*body length 0"));

            bool accepted = rig.SendBody(CLIENT_ONE, player.RawValue, new byte[0]);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.IsTrue(accepted, "a 0-length body is not above MaxBodyBytes, so the intake accepts it");
            Assert.AreEqual(0, sink.Toggles.Count);
        }

        [Test]
        public void test_malformed_oversize_body_is_dropped_by_the_dispatcher_before_the_handler()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            LogAssert.Expect(LogType.Warning, new Regex(
                @"\[InboundRequestDispatcher\] InboundMessageMalformed: .*body longer than MaxBodyBytes of its type"));

            bool accepted = rig.SendBody(CLIENT_ONE, player.RawValue, new byte[SetAutoAttack.BodySize + 1]);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, sink.Toggles.Count);
        }

        // ---------------------------------------------------------------- SetTarget outcomes

        [Test]
        public void test_set_target_accepted_select_reaches_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);

            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.AreEqual(1, sink.Targets.Count);
            Assert.AreEqual(player, sink.Targets[0].Key);
            Assert.AreEqual(MobA, sink.Targets[0].Value);
        }

        [Test]
        public void test_set_target_accepted_deselect_reaches_combat_as_invalid()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);

            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.SendTarget(CLIENT_ONE, player.RawValue, EntityID.Invalid.RawValue);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.AreEqual(2, sink.Targets.Count);
            Assert.AreEqual(EntityID.Invalid, sink.Targets[1].Value);
        }

        [Test]
        public void test_set_target_self_target_is_rejected_and_does_not_reach_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            LogAssert.Expect(LogType.Warning, new Regex("SelfTargetAttempt"));

            rig.SendTarget(CLIENT_ONE, player.RawValue, player.RawValue);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.AreEqual(0, sink.Targets.Count);
        }

        [Test]
        public void test_set_target_entity_not_in_the_zone_is_rejected_and_does_not_reach_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            LogAssert.Expect(LogType.Warning, new Regex("InvalidTargetEntityId"));

            rig.SendTarget(CLIENT_ONE, player.RawValue, UNKNOWN_ENTITY_RAW);
            rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK);

            Assert.AreEqual(0, sink.Targets.Count);
        }

        // RFR-3a: the tracker accepts any entity present in the zone, a player included. Combat then rejects it (Rule 1:
        // a player is not a valid auto-attack target), so the attacker stays Idle and never fires.
        [Test]
        public void test_set_target_on_another_players_entity_is_accepted_by_the_tracker_and_rejected_by_combat()
        {
            var sink = new RecordingInputSink();
            Rig rig = NewRig(sink);
            sink.Inner = rig.Combat;
            EntityID attacker = rig.AddPlayer(CLIENT_ONE);
            EntityID other = rig.AddPlayer(CLIENT_TWO);

            Assert.IsTrue(rig.SendToggle(CLIENT_ONE, attacker.RawValue, true));
            Assert.IsTrue(rig.SendTarget(CLIENT_ONE, attacker.RawValue, other.RawValue));
            for (int i = 0; i < NEVER_FIRES_TICKS; i++)
            {
                rig.Advance();
            }

            Assert.AreEqual(1, sink.Targets.Count, "the tracker accepted it (no InvalidTargetEntityId) and forwarded it");
            Assert.AreEqual(other, sink.Targets[0].Value);
            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(attacker));
            Assert.AreEqual(EntityID.Invalid, rig.Combat.GetTarget(attacker));
            Assert.AreEqual(0, rig.Applier.Count, "never fires");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- EC-8 and AC-16 through the pipeline

        [TestCase(true)]
        [TestCase(false)]
        public void test_ec8_set_auto_attack_and_set_target_on_tick_n_apply_before_tick_n_fire_check(bool toggleFirst)
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);

            if (toggleFirst)
            {
                rig.SendToggle(CLIENT_ONE, player.RawValue, true);
                rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            }
            else
            {
                rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
                rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            }

            rig.Tick(FIRST_PIPELINE_TICK);

            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(player));
            Assert.AreEqual(MobA, rig.Combat.GetTarget(player));
            Assert.AreEqual(1, rig.Combat.GetActionTimer(player), "activated in the dispatch step, then advanced by tick N's Run");
        }

        [Test]
        public void test_ac16_at_most_one_auto_damage_per_player_per_tick_and_fires_are_a_full_cycle_apart()
        {
            Rig rig = NewRig();
            EntityID first = rig.AddPlayer(CLIENT_ONE);
            EntityID second = rig.AddPlayer(CLIENT_TWO);
            Assert.IsTrue(rig.SendToggle(CLIENT_ONE, first.RawValue, true));
            Assert.IsTrue(rig.SendTarget(CLIENT_ONE, first.RawValue, MobA.RawValue));
            Assert.IsTrue(rig.SendToggle(CLIENT_TWO, second.RawValue, true));
            Assert.IsTrue(rig.SendTarget(CLIENT_TWO, second.RawValue, MobB.RawValue));

            var firstFires = new List<uint>();
            var secondFires = new List<uint>();

            // One pipeline tick, then a direct Run of the same tick: the _lastRunTick guard must keep it from firing twice.
            void StepChecked(uint tick)
            {
                int before = rig.Applier.Count;
                rig.Tick(tick);
                rig.Combat.Run(tick, DELTA);
                int perFirst = 0;
                int perSecond = 0;
                for (int i = before; i < rig.Applier.Count; i++)
                {
                    if (rig.Applier.Attackers[i] == first)
                    {
                        perFirst++;
                        firstFires.Add(rig.Applier.Ticks[i]);
                    }
                    else if (rig.Applier.Attackers[i] == second)
                    {
                        perSecond++;
                        secondFires.Add(rig.Applier.Ticks[i]);
                    }
                }

                Assert.LessOrEqual(perFirst, 1, $"first player, tick {tick}");
                Assert.LessOrEqual(perSecond, 1, $"second player, tick {tick}");
            }

            StepChecked(FIRST_PIPELINE_TICK);
            int cycle = rig.Combat.GetCycleTicks(first);
            Assert.Greater(cycle, 0, "the cycle length was sampled at activation");
            uint lastTick = FIRST_PIPELINE_TICK + (uint)(cycle * AC16_CYCLE_COUNT) - 1u;
            for (uint tick = FIRST_PIPELINE_TICK + 1u; tick <= lastTick; tick++)
            {
                StepChecked(tick);
            }

            AssertFiresAreAFullCycleApart(firstFires, cycle, AC16_CYCLE_COUNT);
            AssertFiresAreAFullCycleApart(secondFires, cycle, AC16_CYCLE_COUNT);
        }

        private static void AssertFiresAreAFullCycleApart(List<uint> fires, int cycle, int expectedFires)
        {
            Assert.AreEqual(expectedFires, fires.Count, "one fire per cycle");
            for (int i = 1; i < fires.Count; i++)
            {
                Assert.Greater(fires[i], fires[i - 1], "fire ticks are strictly increasing");
                Assert.GreaterOrEqual(fires[i] - fires[i - 1], (uint)cycle, "fires are at least one cycle apart");
            }
        }

        // ---------------------------------------------------------------- Dead player (combat prohibited)

        [Test]
        public void test_prohibited_player_activated_through_the_dispatcher_never_fires_and_fires_on_the_first_check_after_the_clear()
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            rig.Combat.SetCombatProhibited(player, true);   // dead: the lock is set before any input

            Assert.IsTrue(rig.SendToggle(CLIENT_ONE, player.RawValue, true));
            Assert.IsTrue(rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue));
            rig.Advance();
            int cycle = rig.Combat.GetCycleTicks(player);
            Assert.Greater(cycle, 0);
            Assert.AreEqual(AutoAttackState.Prohibited, rig.Combat.GetState(player));

            int heldTicks = (cycle * 2) + 1;
            for (int i = 0; i < heldTicks; i++)
            {
                rig.Advance();
                Assert.AreEqual(AutoAttackState.Prohibited, rig.Combat.GetState(player));
            }

            Assert.AreEqual(0, rig.Applier.Count, "no auto over more than one cycle while prohibited");
            Assert.Greater(rig.Combat.GetActionTimer(player), cycle, "the timer keeps running under the lock");

            rig.Combat.SetCombatProhibited(player, false);
            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(player));
            rig.Advance();

            Assert.AreEqual(1, rig.Applier.Count, "the auto fires on the first check after the clear");
            Assert.AreEqual(player, rig.Applier.Attackers[0]);
        }

        // ---------------------------------------------------------------- Hardening

        [Test]
        public void test_select_then_deselect_while_active_goes_idle_freezes_the_timer_and_keeps_the_toggle_on()
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.Advance();
            rig.Advance();
            Assert.Greater(rig.Combat.GetCycleTicks(player), SHORT_RUN_TICKS);
            int timerBefore = rig.Combat.GetActionTimer(player);
            Assert.AreEqual(2, timerBefore);

            rig.SendTarget(CLIENT_ONE, player.RawValue, EntityID.Invalid.RawValue);
            rig.Advance();
            rig.Advance();

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(player));
            Assert.AreEqual(EntityID.Invalid, rig.Combat.GetTarget(player));
            Assert.AreEqual(timerBefore, rig.Combat.GetActionTimer(player), "frozen while idle");

            // The toggle is still ON: a fresh select reactivates without a SetAutoAttack, and the timer restarts from 0.
            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.Advance();

            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(player));
            Assert.AreEqual(MobA, rig.Combat.GetTarget(player));
            Assert.AreEqual(1, rig.Combat.GetActionTimer(player), "reset to 0 by the select, then advanced by the tick's Run");
        }

        [Test]
        public void test_set_auto_attack_true_twice_in_one_tick_does_not_reset_the_timer()
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.Advance();
            Assert.AreEqual(1, rig.Combat.GetActionTimer(player));

            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.Advance();

            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(player));
            Assert.AreEqual(2, rig.Combat.GetActionTimer(player), "a repeated ON is not a change; the timer keeps counting");
        }

        [Test]
        public void test_set_auto_attack_true_then_false_in_one_tick_ends_idle_and_the_toggle_is_off()
        {
            Rig rig = NewRig();
            EntityID player = rig.AddPlayer(CLIENT_ONE);
            rig.SendTarget(CLIENT_ONE, player.RawValue, MobA.RawValue);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(player), "toggle OFF with a stored target");

            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.SendToggle(CLIENT_ONE, player.RawValue, false);
            rig.Advance();
            rig.Advance();

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(player));
            Assert.AreEqual(0, rig.Combat.GetActionTimer(player), "activation reset it to 0, the OFF froze it there");
            Assert.AreEqual(MobA, rig.Combat.GetTarget(player), "an OFF keeps the target");

            // Net OFF: a later single ON is a real change and activates.
            rig.SendToggle(CLIENT_ONE, player.RawValue, true);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(player));
            Assert.AreEqual(1, rig.Combat.GetActionTimer(player));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void test_release_of_an_idle_player_with_the_toggle_off_leaves_the_slot_clean(bool hadSelectedTarget)
        {
            Rig rig = NewRig();
            EntityID leaving = rig.AddPlayer(CLIENT_ONE);
            if (hadSelectedTarget)
            {
                rig.SendTarget(CLIENT_ONE, leaving.RawValue, MobA.RawValue);
                rig.Advance();
            }

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(leaving));
            Assert.IsTrue(rig.Pool.TryGetPlayerSlot(leaving, out byte oldSlot));

            Assert.IsTrue(rig.Release(leaving));
            EntityID fresh = rig.AddPlayer(CLIENT_TWO);
            Assert.IsTrue(rig.Pool.TryGetPlayerSlot(fresh, out byte freshSlot));
            Assert.AreEqual(oldSlot, freshSlot);

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(fresh));
            Assert.AreEqual(0, rig.Combat.GetActionTimer(fresh));
            Assert.AreEqual(0, rig.Combat.GetCycleTicks(fresh));
            Assert.AreEqual(EntityID.Invalid, rig.Combat.GetTarget(fresh));

            // No inherited target: an ON alone does not activate the new occupant.
            rig.SendToggle(CLIENT_TWO, fresh.RawValue, true);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(fresh));
            Assert.AreEqual(0, rig.Combat.GetActionTimer(fresh));
        }

        [Test]
        public void test_set_auto_attack_queued_before_the_players_release_is_a_no_op_when_dispatched()
        {
            Rig rig = NewRig();
            EntityID leaving = rig.AddPlayer(CLIENT_ONE);
            Assert.IsTrue(rig.SendToggle(CLIENT_ONE, leaving.RawValue, true));   // queued, not yet dispatched
            Assert.IsTrue(rig.Release(leaving));
            EntityID fresh = rig.AddPlayer(CLIENT_TWO);                          // reuses the released slot

            Assert.DoesNotThrow(() => rig.Harness.Dispatcher.DispatchTick(DISPATCH_TICK));

            // The late ON must not have reached the slot's new occupant: with a target and no ON, it stays Idle.
            rig.SendTarget(CLIENT_TWO, fresh.RawValue, MobA.RawValue);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(fresh));
            Assert.AreEqual(0, rig.Combat.GetActionTimer(fresh));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Zone exit

        [Test]
        public void test_zone_exit_release_through_the_pool_resets_the_slot_for_the_next_occupant()
        {
            Rig rig = NewRig();
            EntityID leaving = rig.AddPlayer(CLIENT_ONE);
            rig.SendToggle(CLIENT_ONE, leaving.RawValue, true);
            rig.SendTarget(CLIENT_ONE, leaving.RawValue, MobA.RawValue);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.CombatActive, rig.Combat.GetState(leaving));
            Assert.IsTrue(rig.Pool.TryGetPlayerSlot(leaving, out byte oldSlot));

            Assert.IsTrue(rig.Release(leaving));
            EntityID fresh = rig.AddPlayer(CLIENT_TWO);
            Assert.IsTrue(rig.Pool.TryGetPlayerSlot(fresh, out byte freshSlot));
            Assert.AreEqual(oldSlot, freshSlot, "the new occupant reuses the released slot");
            rig.Advance();
            rig.Advance();

            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(fresh));
            Assert.AreEqual(0, rig.Combat.GetActionTimer(fresh), "Run does not advance the released slot");
            Assert.AreEqual(EntityID.Invalid, rig.Combat.GetTarget(fresh));

            rig.SendTarget(CLIENT_TWO, fresh.RawValue, MobA.RawValue);
            rig.Advance();
            Assert.AreEqual(AutoAttackState.Idle, rig.Combat.GetState(fresh), "the toggle is OFF for the new occupant");
        }
    }
}
