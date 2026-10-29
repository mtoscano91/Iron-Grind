using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode integration tests for Movement Story 004 - intent intake. The real
    /// <see cref="InboundRequestDispatcher"/> and <see cref="CrossCuttingRpcGuardChain"/> drive a
    /// <see cref="MovementIntentBuffer"/>. Two different drops are exercised and every test says which: the ADR-014
    /// intake <c>SequenceNumber</c> drop (Networking Core, envelope, per connection and type) or the F-MOV-7
    /// <c>TickNumber</c> drop (this story, body field, in the handler).
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_IntentIntake_Tests
    {
        private const uint CLIENT_A = 7u;
        private const uint CLIENT_B = 8u;
        private const int SLOT_A = 3;
        private const int SLOT_B = 4;
        private const int SLOT_UNKNOWN_ENTITY = 9;
        private const uint UNKNOWN_ENTITY_RAW = 999u;
        private const uint TICK_100 = 100u;
        private const int ALLOCATION_MESSAGE_COUNT = 50;
        private const short FULL = 32767;

        private static readonly EntityID EntityA = InboundDispatchHarness.EntityOf(CLIENT_A);
        private static readonly EntityID EntityB = InboundDispatchHarness.EntityOf(CLIENT_B);

        private InboundDispatchHarness _harness;
        private FakeIndex _index;
        private MovementIntentBuffer _buffer;
        private int _handlerCalls;
        private uint _sequence;

        private sealed class FakeIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
        }

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<InboundRequestDescriptor> Descriptors = new List<InboundRequestDescriptor>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) => Descriptors.Add(descriptor);

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        [SetUp]
        public void SetUp()
        {
            _index = new FakeIndex();
            _index.Map(EntityA, SLOT_A);
            _index.Map(EntityB, SLOT_B);
            _index.Map(new EntityID(UNKNOWN_ENTITY_RAW), SLOT_UNKNOWN_ENTITY);

            _harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            _buffer = new MovementIntentBuffer(_harness.TickSource, _index, MovementTuningValues.Defaults);

            // A counting wrapper in front of the buffer's handler, so "handler not called" is observable.
            _harness.Dispatcher.Register(
                new InboundRequestDescriptor(MovementIntentMessage.MessageTypeId, RpcTypeTag.MovementIntent, false,
                    (ushort)MovementIntentMessage.BodySize),
                (in InboundRequestContext context, ReadOnlySpan<byte> body) =>
                {
                    _handlerCalls++;
                    _buffer.HandleMovementIntent(in context, body);
                });
            _harness.Dispatcher.Seal();
            _harness.AddClient(CLIENT_A);
            _harness.AddClient(CLIENT_B);
            _sequence = 1u;
            _handlerCalls = 0;
        }

        private static MovementIntentMessage Msg(short dirX, short dirZ, short facing, uint tickNumber)
            => new MovementIntentMessage(dirX, dirZ, facing, tickNumber);

        private static byte[] BodyOf(in MovementIntentMessage message)
        {
            var body = new byte[MovementIntentMessage.BodySize];
            MovementIntentMessageCodec.WriteBody(body, in message);
            return body;
        }

        // Sets the server tick, sends every message with an increasing SequenceNumber, then runs the dispatch tick.
        private void Deliver(uint tick, uint client, params MovementIntentMessage[] messages)
        {
            _harness.TickSource.Tick = tick;
            foreach (MovementIntentMessage message in messages)
            {
                _harness.SendRequest(client, MovementIntentMessage.MessageTypeId, _sequence++, BodyOf(in message));
            }
            _harness.Dispatcher.DispatchTick(tick);
        }

        private void DeliverFrom(uint tick, uint client, uint senderRaw, MovementIntentMessage message)
        {
            _harness.TickSource.Tick = tick;
            _harness.SendRequestFrom(client, MovementIntentMessage.MessageTypeId, _sequence++, senderRaw, BodyOf(in message));
            _harness.Dispatcher.DispatchTick(tick);
        }

        private bool Take(int slot, out MovementIntentMessage intent) => _buffer.TryTakeIntent(slot, out intent);

        // ── Registration and tag ───────────────────────────────────────────────────────────

        [Test]
        public void MovementInboundRegistration_Register_UuNotHeldTenByteBoundOwnUnlimitedTag()
        {
            var recorder = new RecordingDispatcher();

            MovementInboundRegistration.Register(recorder, _buffer);

            Assert.AreEqual(1, recorder.Descriptors.Count);
            InboundRequestDescriptor descriptor = recorder.Descriptors[0];
            Assert.AreEqual(MovementIntentMessage.MessageTypeId, descriptor.MessageTypeId);
            Assert.AreEqual(RpcTypeTag.MovementIntent, descriptor.RpcTypeTag);
            Assert.AreEqual(9, (byte)RpcTypeTag.MovementIntent);
            Assert.IsFalse(descriptor.HeldDuringIrreversibleWrite);
            Assert.AreEqual(10, descriptor.MaxBodyBytes);
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.MovementIntent));
        }

        [Test]
        public void MovementInboundRegistration_Register_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => MovementInboundRegistration.Register(null, _buffer));
            Assert.Throws<ArgumentNullException>(() => MovementInboundRegistration.Register(new RecordingDispatcher(), null));
        }

        [Test]
        public void MovementIntentBuffer_Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new MovementIntentBuffer(null, _index, MovementTuningValues.Defaults));
            Assert.Throws<ArgumentNullException>(() => new MovementIntentBuffer(_harness.TickSource, null, MovementTuningValues.Defaults));
        }

        [Test]
        public void MovementInboundRegistration_RegisteredOnRealDispatcher_DeliversToBuffer()
        {
            // Arrange: a second harness registered only through MovementInboundRegistration.
            var harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            var buffer = new MovementIntentBuffer(harness.TickSource, _index, MovementTuningValues.Defaults);
            MovementInboundRegistration.Register(harness.Dispatcher, buffer);
            harness.Dispatcher.Seal();
            harness.AddClient(CLIENT_A);
            harness.TickSource.Tick = 5u;
            MovementIntentMessage sent = Msg(100, -200, 450, 5u);

            // Act
            harness.SendRequest(CLIENT_A, MovementIntentMessage.MessageTypeId, 1u, BodyOf(in sent));
            harness.Dispatcher.DispatchTick(5u);

            // Assert
            Assert.IsTrue(buffer.TryTakeIntent(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(100, got.DirX);
            Assert.AreEqual(-200, got.DirZ);
            Assert.AreEqual(450, got.FacingAngle);
        }

        [Test]
        public void CrossCuttingRpcGuardChain_MovementIntentTwiceOnOneTick_BothAccepted()
        {
            // Arrange: the guard chain alone, no rate limit for the tag (AC-MOV-28).
            var chain = new CrossCuttingRpcGuardChain();
            chain.RegisterEntityOwnership(CLIENT_A, EntityA);
            chain.MarkSessionReady(CLIENT_A);
            var descriptor = new InboundRpcDescriptor(CLIENT_A, EntityA, RpcTypeTag.MovementIntent, 10u);

            // Act / Assert
            Assert.AreEqual(RpcGuardResult.Accepted, chain.Evaluate(descriptor));
            Assert.AreEqual(RpcGuardResult.Accepted, chain.Evaluate(descriptor));
        }

        // ── AC-MOV-17 / EC-MOV-16: spoofed sender (existing guard chain, F-MOV-7 not involved) ──

        [Test]
        public void IntentIntake_Ac17_UnknownEntityId_GuardDropsBeforeHandler()
        {
            // Arrange
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex(@"from clientId=7 references unknown EntityID EntityID\(999\)"));

            // Act: client 7 sends EntityID 999, which no connection owns (Guard 1).
            DeliverFrom(10u, CLIENT_A, UNKNOWN_ENTITY_RAW, Msg(FULL, 0, 0, 10u));

            // Assert
            Assert.AreEqual(0, _handlerCalls, "The handler must not run.");
            Assert.IsFalse(Take(SLOT_A, out _));
            Assert.IsFalse(Take(SLOT_UNKNOWN_ENTITY, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_Ac17_EntityOwnedByAnotherClient_GuardDropsBeforeHandler()
        {
            // Arrange
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex(@"from clientId=7 references EntityID EntityID\(108\) owned by clientId=8"));

            // Act: client 7 sends client 8's EntityID (Guard 4).
            DeliverFrom(10u, CLIENT_A, EntityB.RawValue, Msg(FULL, 0, 0, 10u));

            // Assert
            Assert.AreEqual(0, _handlerCalls, "The handler must not run.");
            Assert.IsFalse(Take(SLOT_A, out _));
            Assert.IsFalse(Take(SLOT_B, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_UnknownConnection_DroppedByDispatcherHandlerNotCalled()
        {
            // Arrange: client 99 was never added.
            _harness.TickSource.Tick = 10u;
            MovementIntentMessage message = Msg(FULL, 0, 0, 10u);

            // Act
            bool accepted = _harness.SendRequest(99u, MovementIntentMessage.MessageTypeId, 1u, BodyOf(in message));
            _harness.Dispatcher.DispatchTick(10u);

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(0, _handlerCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_ConnectionWithoutCharacter_DroppedByDispatcherHandlerNotCalled()
        {
            // Arrange
            _harness.Directory.Remove(CLIENT_A);
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex(@"InboundRequestWithoutCharacter: clientId=7"));

            // Act
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 0, 10u));

            // Assert
            Assert.AreEqual(0, _handlerCalls);
            Assert.IsFalse(Take(SLOT_A, out _));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Slot mapping ───────────────────────────────────────────────────────────────────

        [Test]
        public void IntentIntake_OwnedEntityWithoutSlot_DroppedSilently()
        {
            // Arrange: the entity is owned and session-ready but has no slot (entry/exit window).
            var index = new FakeIndex();
            var buffer = new MovementIntentBuffer(_harness.TickSource, index, MovementTuningValues.Defaults);
            var context = new InboundRequestContext(CLIENT_A, EntityA, InboundDispatchHarness.CharacterOf(CLIENT_A),
                MovementIntentMessage.MessageTypeId, 10u, 10u, false);
            _harness.TickSource.Tick = 10u;
            byte[] body = BodyOf(Msg(FULL, 0, 0, 10u));

            // Act
            buffer.HandleMovementIntent(in context, body);

            // Assert
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                Assert.IsFalse(buffer.TryTakeIntent(slot, out _), "No slot may hold an intent.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_SlotIndexOutOfRange_DroppedSilently()
        {
            // Arrange: a slot index that reports slots outside the buffer (-1 and MAX_PLAYERS_PER_ZONE).
            var index = new FakeIndex();
            var buffer = new MovementIntentBuffer(_harness.TickSource, index, MovementTuningValues.Defaults);
            _harness.TickSource.Tick = 10u;
            byte[] body = BodyOf(Msg(FULL, 0, 0, 10u));
            var contextA = new InboundRequestContext(CLIENT_A, EntityA, InboundDispatchHarness.CharacterOf(CLIENT_A),
                MovementIntentMessage.MessageTypeId, 10u, 10u, false);
            var contextB = new InboundRequestContext(CLIENT_B, EntityB, InboundDispatchHarness.CharacterOf(CLIENT_B),
                MovementIntentMessage.MessageTypeId, 10u, 10u, false);
            index.Map(EntityA, -1);
            index.Map(EntityB, ZoneSlotPool.MAX_PLAYERS_PER_ZONE);

            // Act
            Assert.DoesNotThrow(() => buffer.HandleMovementIntent(in contextA, body));
            Assert.DoesNotThrow(() => buffer.HandleMovementIntent(in contextB, body));

            // Assert
            for (int slot = 0; slot < ZoneSlotPool.MAX_PLAYERS_PER_ZONE; slot++)
            {
                Assert.IsFalse(buffer.TryTakeIntent(slot, out _), "No slot may hold an intent.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_MalformedBody_DroppedWithoutWriteOrLog()
        {
            // Arrange: a 9-byte body straight into the handler (the dispatcher's own bound only rejects longer ones).
            var context = new InboundRequestContext(CLIENT_A, EntityA, InboundDispatchHarness.CharacterOf(CLIENT_A),
                MovementIntentMessage.MessageTypeId, 10u, 10u, false);
            _harness.TickSource.Tick = 10u;

            // Act
            _buffer.HandleMovementIntent(in context, new byte[MovementIntentMessage.BodySize - 1]);

            // Assert
            Assert.IsFalse(Take(SLOT_A, out _));
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-MOV-15 / F-MOV-7: TickNumber drop ───────────────────────────────────────────

        [Test]
        public void IntentIntake_Ac15_TickNumberAtToleranceBoundary97_Accepted()
        {
            // F-MOV-7 TickNumber drop: tick 100, tolerance 3, 100 - 3 = 97 is not below the bound.
            Deliver(TICK_100, CLIENT_A, Msg(FULL, 0, 0, 97u));

            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(97u, got.TickNumber);
        }

        [Test]
        public void IntentIntake_Ac15_TickNumberBelowBoundary96_DroppedAndPriorIntentUnchanged()
        {
            // F-MOV-7 TickNumber drop: the handler runs (the SequenceNumber is fresh) but discards the message.
            Deliver(TICK_100, CLIENT_A, Msg(111, 222, 333, 100u), Msg(FULL, 0, 0, 96u));

            Assert.AreEqual(2, _handlerCalls, "Both messages pass the intake and the guard chain.");
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(111, got.DirX, "The stale message must not replace the buffered intent.");
            Assert.AreEqual(100u, got.TickNumber);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_Ac15_OnlyStaleMessage_BufferReportsNoIntent()
        {
            Deliver(TICK_100, CLIENT_A, Msg(FULL, 0, 0, 96u));

            Assert.IsFalse(Take(SLOT_A, out _));
        }

        [Test]
        public void IntentIntake_Ac16_TicksZeroToThreeAcceptTickNumberZero_NoUnderflow()
        {
            // F-MOV-7 TickNumber drop at serverTick <= tolerance: everything is accepted.
            for (uint tick = 0u; tick <= 3u; tick++)
            {
                Deliver(tick, CLIENT_A, Msg(FULL, 0, 0, 0u));

                Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got), $"TickNumber 0 must be accepted at tick {tick}.");
                Assert.AreEqual(0u, got.TickNumber);
            }
        }

        [Test]
        public void IntentIntake_Ac16_TickFourDropsTickNumberZero()
        {
            // F-MOV-7 TickNumber drop: 0 < 4 - 3.
            Deliver(4u, CLIENT_A, Msg(FULL, 0, 0, 0u));

            Assert.IsFalse(Take(SLOT_A, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IntentIntake_TickNumberAheadOfServer_Accepted()
        {
            // F-MOV-7 discards only old ticks; a client ahead of the server is accepted.
            Deliver(TICK_100, CLIENT_A, Msg(FULL, 0, 0, 105u));

            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(105u, got.TickNumber);
        }

        [Test]
        public void IntentIntake_MaxTickNumberAtTickZero_Accepted()
        {
            Deliver(0u, CLIENT_A, Msg(FULL, 0, 0, uint.MaxValue));

            Assert.IsTrue(Take(SLOT_A, out _));
        }

        // ── AC-MOV-28 / EC-MOV-17: last wins ───────────────────────────────────────────────

        [Test]
        public void IntentIntake_Ac28_TwoSameTickMessagesRightThenUp_LastWins()
        {
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 0, 10u), Msg(0, FULL, 0, 10u));

            Assert.AreEqual(2, _handlerCalls, "No rate limit: both reach the handler.");
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(0, got.DirX);
            Assert.AreEqual(FULL, got.DirZ);
        }

        [Test]
        public void IntentIntake_Ac28_TwoSameTickMessagesReverseOrder_LastWins()
        {
            Deliver(10u, CLIENT_A, Msg(0, FULL, 0, 10u), Msg(FULL, 0, 0, 10u));

            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(FULL, got.DirX);
            Assert.AreEqual(0, got.DirZ);
        }

        [Test]
        public void IntentIntake_TwoClients_BufferedIndependentlyPerSlot()
        {
            Deliver(10u, CLIENT_A, Msg(1, 2, 3, 10u));
            Deliver(10u, CLIENT_B, Msg(4, 5, 6, 10u));

            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage a));
            Assert.IsTrue(Take(SLOT_B, out MovementIntentMessage b));
            Assert.AreEqual(1, a.DirX);
            Assert.AreEqual(4, b.DirX);
        }

        // ── Take semantics ─────────────────────────────────────────────────────────────────

        [Test]
        public void IntentIntake_NoMessageThisTick_TryTakeIntentFalse()
        {
            _harness.TickSource.Tick = 10u;

            Assert.IsFalse(Take(SLOT_A, out _));
        }

        [Test]
        public void IntentIntake_TakeClearsTheSlot_SecondTakeFalse()
        {
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 0, 10u));

            Assert.IsTrue(Take(SLOT_A, out _));
            Assert.IsFalse(Take(SLOT_A, out _));
        }

        [Test]
        public void IntentIntake_LeftoverWriteFromEarlierTick_TakeFalseAndSlotCleared()
        {
            // Arrange: written at tick 10, never taken.
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 0, 10u));

            // Act / Assert: tick 11 sees nothing, and the entry does not come back at tick 10.
            _harness.TickSource.Tick = 11u;
            Assert.IsFalse(Take(SLOT_A, out _));
            _harness.TickSource.Tick = 10u;
            Assert.IsFalse(Take(SLOT_A, out _), "The leftover entry must have been cleared.");
        }

        [Test]
        public void IntentIntake_StaleMessageAfterUntakenWrite_DoesNotRefreshLeftover()
        {
            // Arrange: written at tick 99, never taken.
            Deliver(99u, CLIENT_A, Msg(FULL, 0, 0, 99u));

            // Act: F-MOV-7 TickNumber drop at tick 100 (96 < 100 - 3); it must not re-tag the leftover with tick 100.
            Deliver(TICK_100, CLIENT_A, Msg(0, FULL, 0, 96u));

            // Assert
            Assert.AreEqual(2, _handlerCalls);
            Assert.IsFalse(Take(SLOT_A, out _), "The tick-99 leftover is not this tick's intent.");
        }

        [Test]
        public void IntentIntake_NewWriteAfterUntakenWrite_ReturnsNewMessage()
        {
            // Arrange: written at tick 10, never taken.
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 0, 10u));

            // Act
            Deliver(11u, CLIENT_A, Msg(0, FULL, 0, 11u));

            // Assert
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(0, got.DirX);
            Assert.AreEqual(FULL, got.DirZ);
            Assert.AreEqual(11u, got.TickNumber);
        }

        [Test]
        public void IntentIntake_TryTakeIntentSlotOutOfRange_ReturnsFalse()
        {
            Assert.IsFalse(Take(-1, out _));
            Assert.IsFalse(Take(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, out _));
        }

        // ── Facing ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void IntentIntake_FacingOutsideRange_WrappedNotDropped()
        {
            Deliver(10u, CLIENT_A, Msg(FULL, 0, 4000, 10u));
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage above));
            Assert.AreEqual(400, above.FacingAngle);

            Deliver(11u, CLIENT_A, Msg(FULL, 0, -1, 11u));
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage below));
            Assert.AreEqual(3599, below.FacingAngle);

            Deliver(12u, CLIENT_A, Msg(FULL, 0, 3600, 12u));
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage exact));
            Assert.AreEqual(0, exact.FacingAngle);
        }

        // ── ADR-014 intake SequenceNumber drop (Networking Core, not re-implemented here) ──

        [Test]
        public void IntentIntake_Adr014IntakeSequenceNumberDrop_StaleSequenceNeverReachesHandler()
        {
            // Arrange: sequence 5 accepted, then 5 again and 3 (not newer) are dropped at intake.
            _harness.TickSource.Tick = 10u;
            MovementIntentMessage first = Msg(111, 0, 0, 10u);
            MovementIntentMessage replay = Msg(222, 0, 0, 10u);

            // Act
            bool a = _harness.SendRequest(CLIENT_A, MovementIntentMessage.MessageTypeId, 5u, BodyOf(in first));
            bool b = _harness.SendRequest(CLIENT_A, MovementIntentMessage.MessageTypeId, 5u, BodyOf(in replay));
            bool c = _harness.SendRequest(CLIENT_A, MovementIntentMessage.MessageTypeId, 3u, BodyOf(in replay));
            _harness.Dispatcher.DispatchTick(10u);

            // Assert
            Assert.IsTrue(a);
            Assert.IsFalse(b);
            Assert.IsFalse(c);
            Assert.AreEqual(1, _handlerCalls);
            Assert.AreEqual(2, _harness.Dispatcher.StaleDropCount);
            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(111, got.DirX);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Allocation ─────────────────────────────────────────────────────────────────────

        [Test]
        public void IntentIntake_FiftyMessagesInOneTick_AllocatesNothingAfterWarmUp()
        {
            // Arrange: prebuilt messages with increasing sequence numbers for one connection.
            const uint TICK = 20u;
            var messages = new byte[ALLOCATION_MESSAGE_COUNT * 2][];
            for (int i = 0; i < messages.Length; i++)
            {
                MovementIntentMessage message = Msg((short)(i + 1), 0, 100, TICK);
                messages[i] = InboundMessageBuilder.Request(MovementIntentMessage.MessageTypeId, (uint)(i + 1),
                    EntityA.RawValue, BodyOf(in message));
            }

            // The harness's recording activity sink appends to a List per message; reserve it so only product code is measured.
            _harness.Activity.Calls.Capacity = messages.Length * 2;
            _harness.TickSource.Tick = TICK;
            for (int i = 0; i < ALLOCATION_MESSAGE_COUNT; i++)
            {
                _harness.Dispatcher.TryAccept(CLIENT_A, messages[i]);
            }
            _harness.Dispatcher.DispatchTick(TICK);
            Take(SLOT_A, out _);

            // Act / Assert
            Assert.That(() =>
            {
                for (int i = ALLOCATION_MESSAGE_COUNT; i < messages.Length; i++)
                {
                    _harness.Dispatcher.TryAccept(CLIENT_A, messages[i]);
                }
                _harness.Dispatcher.DispatchTick(TICK);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.IsTrue(Take(SLOT_A, out MovementIntentMessage got));
            Assert.AreEqual(messages.Length, got.DirX, "The last message wins.");
        }
    }
}
