using System.Collections.Generic;
using System.Linq;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 002 — <see cref="INetworkTestObserver"/> and
    /// its recorder implementation <see cref="NetworkTestObserver"/>. Covers AC-NC-43 (the
    /// priority-path 8-message cap, via a test-only fixture — see
    /// <see cref="PriorityPathQueueFixture_Tests"/> below). AC-TC-01 and AC-TC-02 (release-build
    /// stripping verification) are CI-level checks, not EditMode-runtime-testable assertions — see
    /// <c>.github/workflows/tests.yml</c> and <c>tools/ci/check-test-harness-guards.sh</c>.
    /// </summary>
    [TestFixture]
    internal sealed class NetworkTestObserver_Tests
    {
        private NetworkTestObserver _observer;

        [SetUp]
        public void SetUp()
        {
            _observer = new NetworkTestObserver();
        }

        // -----------------------------------------------------------------------
        // Server-side capture: each callback records its exact arguments, in order.
        // -----------------------------------------------------------------------

        [Test]
        public void ServerSideCaptureCallbacks_EachInvokedOnce_RecordsExactArguments()
        {
            // Act
            _observer.OnServerDamageEventSerialized(1, 2, 42);
            _observer.OnServerSelfDamageEventSerialized(1, 1, 7);
            _observer.OnServerGoldSyncBatched(10, 500, 3);
            _observer.OnServerKillEventEmitted(1, 2, 99);
            _observer.OnServerCycleTimerBroadcastSerialized(5, 12, 1000);
            _observer.OnServerEnhancementOutcomeSerialized(10, 200, true, 6);
            _observer.OnRUBatchEntityHealthUpdates(clientId: 3, deliveredEntityIds: new List<uint> { 1, 2, 3 });

            // Assert
            Assert.AreEqual((1u, 2u, 42), _observer.ServerDamageEventSerializedCalls.Single());
            Assert.AreEqual((1u, 1u, 7), _observer.ServerSelfDamageEventSerializedCalls.Single());
            Assert.AreEqual((10u, 500u, 3u), _observer.ServerGoldSyncBatchedCalls.Single());
            Assert.AreEqual((1u, 2u, 99), _observer.ServerKillEventEmittedCalls.Single());
            Assert.AreEqual((5u, (ushort)12, 1000u), _observer.ServerCycleTimerBroadcastSerializedCalls.Single());
            Assert.AreEqual((10u, 200u, true, (byte)6), _observer.ServerEnhancementOutcomeSerializedCalls.Single());

            var ruBatchCall = _observer.RUBatchEntityHealthUpdatesCalls.Single();
            Assert.AreEqual(3u, ruBatchCall.clientId);
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, ruBatchCall.deliveredEntityIds);
        }

        [Test]
        public void OnRUBatchEntityHealthUpdates_CallerMutatesSourceListAfterward_RecordedCopyIsUnaffected()
        {
            // Arrange
            var source = new List<uint> { 1, 2, 3 };

            // Act
            _observer.OnRUBatchEntityHealthUpdates(clientId: 1, deliveredEntityIds: source);
            source.Add(999); // mutate the caller's own list after the callback returns

            // Assert — the recorded copy must not reflect the post-call mutation.
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, _observer.RUBatchEntityHealthUpdatesCalls.Single().deliveredEntityIds);
        }

        // -----------------------------------------------------------------------
        // Client-side capture: each callback records its exact arguments, in order.
        // -----------------------------------------------------------------------

        [Test]
        public void ClientSideCaptureCallbacks_EachInvokedOnce_RecordsExactArguments()
        {
            // Act
            _observer.OnClientDamageEventReceived(1, 2, 42);
            _observer.OnClientSelfDamageEventReceived(1, 1, 7);
            _observer.OnClientGoldSyncReceived(10, 500, 3);
            _observer.OnClientKillEventReceived(1, 2, 99);
            _observer.OnClientCycleTimerBroadcastReceived(5, 12);
            _observer.OnClientEnhancementOutcomeReceived(10, 200, true, 6);

            // Assert
            Assert.AreEqual((1u, 2u, 42), _observer.ClientDamageEventReceivedCalls.Single());
            Assert.AreEqual((1u, 1u, 7), _observer.ClientSelfDamageEventReceivedCalls.Single());
            Assert.AreEqual((10u, 500u, 3u), _observer.ClientGoldSyncReceivedCalls.Single());
            Assert.AreEqual((1u, 2u, 99), _observer.ClientKillEventReceivedCalls.Single());
            Assert.AreEqual((5u, (ushort)12), _observer.ClientCycleTimerBroadcastReceivedCalls.Single());
            Assert.AreEqual((10u, 200u, true, (byte)6), _observer.ClientEnhancementOutcomeReceivedCalls.Single());
        }

        // -----------------------------------------------------------------------
        // Zone entry capture.
        // -----------------------------------------------------------------------

        [Test]
        public void OnZoneGateOpened_Invoked_RecordsClientId()
        {
            // Act
            _observer.OnZoneGateOpened(clientId: 7);

            // Assert
            Assert.AreEqual(7u, _observer.ZoneGateOpenedCalls.Single());
        }

        // -----------------------------------------------------------------------
        // Session lifecycle capture: each callback records its exact arguments, in order.
        // -----------------------------------------------------------------------

        [Test]
        public void SessionLifecycleCaptureCallbacks_EachInvokedOnce_RecordsExactArguments()
        {
            // Act
            _observer.OnSessionStateTransitioned(1, SessionState.Connecting, SessionState.Connected, "AuthSuccess");
            _observer.OnSessionInvalidatedBySteal(1, priorCharacterId: 55);
            _observer.OnReAuthAttemptFailed(1, attemptNumber: 2, remainingAttempts: 1);
            _observer.OnPersistenceWriteCompleted(55, PersistenceWriteReason.SessionExpiry);
            _observer.OnGhostCombatTTLExpired(entityId: 9, disconnectTickNumber: 100, expiryTick: 500);
            _observer.OnGhostPromotionEventEmitted(characterId: 55);
            _observer.OnClientGhostPromotionEventReceived(characterId: 55);
            _observer.OnZoneStateTransitioned(zoneInstanceId: 3, ZoneState.Active, ZoneState.Draining);
            _observer.OnSessionHandshakeEmitted(
                characterId: 55, wasKilledWhileDisconnected: true, goldBalance: 1000, goldVersion: 4,
                level: 10, currentHp: 80, currentMp: 20, heldFreePoints: 2, classType: 1);
            _observer.OnSnapshotRetransmitAttempt(characterId: 55, attemptNumber: 1, maxAttempts: 5);

            // Assert
            Assert.AreEqual((1u, SessionState.Connecting, SessionState.Connected, "AuthSuccess"), _observer.SessionStateTransitionedCalls.Single());
            Assert.AreEqual((1u, 55u), _observer.SessionInvalidatedByStealCalls.Single());
            Assert.AreEqual((1u, 2, 1), _observer.ReAuthAttemptFailedCalls.Single());
            Assert.AreEqual((55u, PersistenceWriteReason.SessionExpiry), _observer.PersistenceWriteCompletedCalls.Single());
            Assert.AreEqual((9u, 100u, 500u), _observer.GhostCombatTTLExpiredCalls.Single());
            Assert.AreEqual(55u, _observer.GhostPromotionEventEmittedCalls.Single());
            Assert.AreEqual(55u, _observer.ClientGhostPromotionEventReceivedCalls.Single());
            Assert.AreEqual((3u, ZoneState.Active, ZoneState.Draining), _observer.ZoneStateTransitionedCalls.Single());
            Assert.AreEqual((55u, true, 1000, 4u, 10, 80, 20, 2, (byte)1), _observer.SessionHandshakeEmittedCalls.Single());
            Assert.AreEqual((55u, 1, 5), _observer.SnapshotRetransmitAttemptCalls.Single());
        }

        // -----------------------------------------------------------------------
        // Priority path capture (excluding OnPriorityPathMessageFlushed, covered by
        // AC-NC-43 in PriorityPathQueueFixture_Tests below).
        // -----------------------------------------------------------------------

        [Test]
        public void PriorityPathCaptureCallbacks_EachInvokedOnce_RecordsExactArguments()
        {
            // Act
            _observer.OnTickCompleted(tickNumber: 42);
            _observer.OnSkillGraceWindowEvaluated(entityId: 9, adjustedTimer: 0.35f, graceTriggers: true);
            _observer.OnStatSnapshotEmitted(entityId: 9);
            _observer.OnSkillUsedRateLimitRejected(entityId: 9);

            // Assert
            Assert.AreEqual(42u, _observer.TickCompletedCalls.Single());
            Assert.AreEqual((9u, 0.35f, true), _observer.SkillGraceWindowEvaluatedCalls.Single());
            Assert.AreEqual(9u, _observer.StatSnapshotEmittedCalls.Single());
            Assert.AreEqual(9u, _observer.SkillUsedRateLimitRejectedCalls.Single());
        }

        // -----------------------------------------------------------------------
        // Spy semantics: repeated invocations accumulate, in call order, they do not
        // overwrite a single slot.
        // -----------------------------------------------------------------------

        [Test]
        public void RepeatedInvocations_AccumulateInCallOrder_DoNotOverwrite()
        {
            // Act
            _observer.OnTickCompleted(1);
            _observer.OnTickCompleted(2);
            _observer.OnTickCompleted(3);

            // Assert
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, _observer.TickCompletedCalls);
        }

        [Test]
        public void Reset_ClearsAllRecordedCallsAndOutboundMessageCounts()
        {
            // Arrange — invoke every single callback once, so Reset() has something to clear
            // in all ~25 capture lists, not just a hand-picked few (code-review finding: a
            // partial-clear regression in Reset() must be catchable by this test).
            _observer.OnServerDamageEventSerialized(1, 2, 3);
            _observer.OnServerSelfDamageEventSerialized(1, 1, 3);
            _observer.OnServerGoldSyncBatched(1, 100, 1);
            _observer.OnServerKillEventEmitted(1, 2, 3);
            _observer.OnServerCycleTimerBroadcastSerialized(1, 5, 100);
            _observer.OnServerEnhancementOutcomeSerialized(1, 2, true, 1);
            _observer.OnRUBatchEntityHealthUpdates(1, new List<uint> { 1 });

            _observer.OnClientDamageEventReceived(1, 2, 3);
            _observer.OnClientSelfDamageEventReceived(1, 1, 3);
            _observer.OnClientGoldSyncReceived(1, 100, 1);
            _observer.OnClientKillEventReceived(1, 2, 3);
            _observer.OnClientCycleTimerBroadcastReceived(1, 5);
            _observer.OnClientEnhancementOutcomeReceived(1, 2, true, 1);

            _observer.OnZoneGateOpened(1);

            _observer.OnSessionStateTransitioned(1, SessionState.Connecting, SessionState.Connected, "AuthSuccess");
            _observer.OnSessionInvalidatedBySteal(1, 2);
            _observer.OnReAuthAttemptFailed(1, 1, 2);
            _observer.OnPersistenceWriteCompleted(1, PersistenceWriteReason.SessionExpiry);
            _observer.OnGhostCombatTTLExpired(1, 100, 200);
            _observer.OnGhostPromotionEventEmitted(1);
            _observer.OnClientGhostPromotionEventReceived(1);
            _observer.OnZoneStateTransitioned(1, ZoneState.Active, ZoneState.Draining);
            _observer.OnSessionHandshakeEmitted(1, false, 100, 1, 5, 50, 20, 1, 0);
            _observer.OnSnapshotRetransmitAttempt(1, 1, 3);

            _observer.OnPriorityPathMessageFlushed(1, 0x0101, 100);
            _observer.OnTickCompleted(1);
            _observer.OnSkillGraceWindowEvaluated(1, 0.5f, true);
            _observer.OnStatSnapshotEmitted(1);
            _observer.OnSkillUsedRateLimitRejected(1);

            _observer.RecordOutboundMessage(clientId: 1, messageTypeId: 0x0101);

            // Act
            _observer.Reset();

            // Assert — every capture list must be empty; a forgotten .Clear() call for any one
            // of them would fail this test.
            Assert.IsEmpty(_observer.ServerDamageEventSerializedCalls);
            Assert.IsEmpty(_observer.ServerSelfDamageEventSerializedCalls);
            Assert.IsEmpty(_observer.ServerGoldSyncBatchedCalls);
            Assert.IsEmpty(_observer.ServerKillEventEmittedCalls);
            Assert.IsEmpty(_observer.ServerCycleTimerBroadcastSerializedCalls);
            Assert.IsEmpty(_observer.ServerEnhancementOutcomeSerializedCalls);
            Assert.IsEmpty(_observer.RUBatchEntityHealthUpdatesCalls);

            Assert.IsEmpty(_observer.ClientDamageEventReceivedCalls);
            Assert.IsEmpty(_observer.ClientSelfDamageEventReceivedCalls);
            Assert.IsEmpty(_observer.ClientGoldSyncReceivedCalls);
            Assert.IsEmpty(_observer.ClientKillEventReceivedCalls);
            Assert.IsEmpty(_observer.ClientCycleTimerBroadcastReceivedCalls);
            Assert.IsEmpty(_observer.ClientEnhancementOutcomeReceivedCalls);

            Assert.IsEmpty(_observer.ZoneGateOpenedCalls);

            Assert.IsEmpty(_observer.SessionStateTransitionedCalls);
            Assert.IsEmpty(_observer.SessionInvalidatedByStealCalls);
            Assert.IsEmpty(_observer.ReAuthAttemptFailedCalls);
            Assert.IsEmpty(_observer.PersistenceWriteCompletedCalls);
            Assert.IsEmpty(_observer.GhostCombatTTLExpiredCalls);
            Assert.IsEmpty(_observer.GhostPromotionEventEmittedCalls);
            Assert.IsEmpty(_observer.ClientGhostPromotionEventReceivedCalls);
            Assert.IsEmpty(_observer.ZoneStateTransitionedCalls);
            Assert.IsEmpty(_observer.SessionHandshakeEmittedCalls);
            Assert.IsEmpty(_observer.SnapshotRetransmitAttemptCalls);

            Assert.IsEmpty(_observer.PriorityPathMessageFlushedCalls);
            Assert.IsEmpty(_observer.TickCompletedCalls);
            Assert.IsEmpty(_observer.SkillGraceWindowEvaluatedCalls);
            Assert.IsEmpty(_observer.StatSnapshotEmittedCalls);
            Assert.IsEmpty(_observer.SkillUsedRateLimitRejectedCalls);

            Assert.AreEqual(0, _observer.GetOutboundMessageCount(clientId: 1, messageTypeId: 0x0101));
        }

        // -----------------------------------------------------------------------
        // GetOutboundMessageCount query method (AC-ZI-8 consumer): defaults to 0,
        // increments via the internal RecordOutboundMessage seam, and is scoped
        // independently per (clientId, messageTypeId) pair.
        // -----------------------------------------------------------------------

        [Test]
        public void GetOutboundMessageCount_NeverRecorded_DefaultsToZero()
        {
            // Act & Assert
            Assert.AreEqual(0, _observer.GetOutboundMessageCount(clientId: 1, messageTypeId: 0x0101));
        }

        [Test]
        public void RecordOutboundMessage_MultipleCalls_AccumulatesCount()
        {
            // Act
            _observer.RecordOutboundMessage(clientId: 1, messageTypeId: 0x0101);
            _observer.RecordOutboundMessage(clientId: 1, messageTypeId: 0x0101);
            _observer.RecordOutboundMessage(clientId: 1, messageTypeId: 0x0101);

            // Assert
            Assert.AreEqual(3, _observer.GetOutboundMessageCount(clientId: 1, messageTypeId: 0x0101));
        }

        [Test]
        public void RecordOutboundMessage_DifferentClientOrMessageType_ScopedIndependently()
        {
            // Arrange
            _observer.RecordOutboundMessage(clientId: 1, messageTypeId: 0x0101);

            // Act & Assert — a different clientId, and a different messageTypeId for the same
            // client, must each read back as 0 — no cross-contamination between scopes.
            Assert.AreEqual(0, _observer.GetOutboundMessageCount(clientId: 2, messageTypeId: 0x0101), "Different clientId must not share the count.");
            Assert.AreEqual(0, _observer.GetOutboundMessageCount(clientId: 1, messageTypeId: 0x0102), "Different messageTypeId must not share the count.");
            Assert.AreEqual(1, _observer.GetOutboundMessageCount(clientId: 1, messageTypeId: 0x0101), "The original (clientId, messageTypeId) pair must be unaffected.");
        }
    }

    [TestFixture]
    internal sealed class NetworkingTestHarness_ObserverFactorySeam_Tests
    {
        // -----------------------------------------------------------------------
        // AC-TH-4-equivalent factory smoke test (see Story 001's
        // NetworkingTestHarness_FactorySeam_Tests) — confirms the guarded factory
        // pattern extends cleanly to INetworkTestObserver.
        // -----------------------------------------------------------------------

        [Test]
        public void CreateNetworkTestObserver_ReturnsUsableInstance()
        {
            // Act
            INetworkTestObserver observer = NetworkingTestHarness.CreateNetworkTestObserver();

            // Assert
            Assert.IsNotNull(observer);
            Assert.DoesNotThrow(() => observer.OnTickCompleted(1));
            Assert.AreEqual(0, observer.GetOutboundMessageCount(1, 0x0101));
        }
    }

    /// <summary>
    /// AC-NC-43 (priority-path 8-message cap): this fixture is a small, self-contained, TEST-ONLY
    /// stand-in for the real production priority-path queue. The real queue (with its actual
    /// 8-message-per-tick cap and enhancement-exempt front-of-queue insertion) is Story 006's job
    /// and does not exist yet — this fixture exists ONLY to drive
    /// <see cref="INetworkTestObserver.OnPriorityPathMessageFlushed"/> correctly for this one AC, so
    /// this story can prove the observer-capture contract the AC describes without depending on
    /// unbuilt production code. It intentionally models only what AC-NC-43 requires (an 8-per-tick
    /// cap, with any enhancement-exempt message always flushed first in the tick it is eligible for)
    /// and must NOT be reused as, or mistaken for, the production implementation.
    /// </summary>
    [TestFixture]
    internal sealed class PriorityPathQueueFixture_Tests
    {
        private const int CapPerTick = 8;

        /// <summary>A single test-only stand-in for an R-OD message queued on the priority path.</summary>
        private readonly struct QueuedTestMessage
        {
            public readonly uint CharacterId;
            public readonly ushort MessageTypeId;
            public readonly bool IsEnhancementExempt;

            public QueuedTestMessage(uint characterId, ushort messageTypeId, bool isEnhancementExempt)
            {
                CharacterId = characterId;
                MessageTypeId = messageTypeId;
                IsEnhancementExempt = isEnhancementExempt;
            }
        }

        /// <summary>
        /// Test-only stand-in for one tick's worth of priority-path flushing: at most
        /// <paramref name="capPerTick"/> messages are flushed from the front of
        /// <paramref name="pending"/>, with any enhancement-exempt message moved to the front of
        /// this tick's flush regardless of its position in <paramref name="pending"/>. Whatever
        /// does not fit remains in <paramref name="pending"/>, in its original relative order, for
        /// the next tick's flush.
        /// </summary>
        private static void FlushOneTick(List<QueuedTestMessage> pending, uint tickNumber, int capPerTick, INetworkTestObserver observer)
        {
            int enhancementIndex = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].IsEnhancementExempt)
                {
                    enhancementIndex = i;
                    break;
                }
            }

            var ordered = new List<QueuedTestMessage>(pending.Count);
            if (enhancementIndex >= 0)
            {
                ordered.Add(pending[enhancementIndex]);
            }
            for (int i = 0; i < pending.Count; i++)
            {
                if (i == enhancementIndex)
                    continue;

                ordered.Add(pending[i]);
            }

            int flushCount = System.Math.Min(capPerTick, ordered.Count);
            for (int i = 0; i < flushCount; i++)
            {
                observer.OnPriorityPathMessageFlushed(ordered[i].CharacterId, ordered[i].MessageTypeId, tickNumber);
            }

            pending.Clear();
            pending.AddRange(ordered.GetRange(flushCount, ordered.Count - flushCount));
        }

        [Test]
        public void FlushOneTick_TwelveMessagesNoEnhancementExempt_Flushes8ThenRemaining4InRelativeOrder()
        {
            // Arrange
            const uint CharacterId = 42u;
            const uint TickT = 1000u;
            var observer = new NetworkTestObserver();
            var pending = new List<QueuedTestMessage>();
            for (int i = 0; i < 12; i++)
            {
                pending.Add(new QueuedTestMessage(CharacterId, messageTypeId: (ushort)(0x0200 + i), isEnhancementExempt: false));
            }

            // Act
            FlushOneTick(pending, TickT, CapPerTick, observer);
            FlushOneTick(pending, TickT + 1, CapPerTick, observer);

            // Assert
            var tickTCalls = observer.PriorityPathMessageFlushedCalls.Where(c => c.tickNumber == TickT).ToList();
            var tickT1Calls = observer.PriorityPathMessageFlushedCalls.Where(c => c.tickNumber == TickT + 1).ToList();

            Assert.AreEqual(8, tickTCalls.Count, "Exactly 8 messages must flush at tick T.");
            Assert.AreEqual(4, tickT1Calls.Count, "Exactly 4 messages must flush at tick T+1.");
            Assert.AreEqual(12, observer.PriorityPathMessageFlushedCalls.Count, "No message may be dropped — all 12 flush across tick T and T+1.");

            CollectionAssert.AreEqual(
                new ushort[] { 0x0200, 0x0201, 0x0202, 0x0203, 0x0204, 0x0205, 0x0206, 0x0207 },
                tickTCalls.Select(c => c.messageTypeId).ToArray(),
                "Tick T must flush the first 8 messages in original relative (emission) order.");
            CollectionAssert.AreEqual(
                new ushort[] { 0x0208, 0x0209, 0x020A, 0x020B },
                tickT1Calls.Select(c => c.messageTypeId).ToArray(),
                "Tick T+1 must flush the deferred 4 messages in the same relative order they held in the tick-T queue.");
        }

        [Test]
        public void FlushOneTick_TwelveMessagesWithEnhancementExemptNotAtPosition0_EnhancementFlushesFirstInTickT_Remaining4DeferToTickT1()
        {
            // Arrange — 12 messages queued; the enhancement-exempt message sits at queue position 5
            // (deliberately not position 0), per AC-NC-43's scoping note in Story 002.
            const uint CharacterId = 42u;
            const uint TickT = 2000u;
            const int EnhancementQueuePosition = 5;
            const ushort EnhancementMessageTypeId = 0x0104; // stand-in for EnhancementOutcomeBroadcast.MessageTypeID

            var observer = new NetworkTestObserver();
            var pending = new List<QueuedTestMessage>();
            for (int i = 0; i < 12; i++)
            {
                bool isEnhancement = i == EnhancementQueuePosition;
                ushort messageTypeId = isEnhancement ? EnhancementMessageTypeId : (ushort)(0x0200 + i);
                pending.Add(new QueuedTestMessage(CharacterId, messageTypeId, isEnhancementExempt: isEnhancement));
            }

            // Act
            FlushOneTick(pending, TickT, CapPerTick, observer);
            FlushOneTick(pending, TickT + 1, CapPerTick, observer);

            // Assert
            var tickTCalls = observer.PriorityPathMessageFlushedCalls.Where(c => c.tickNumber == TickT).ToList();
            var tickT1Calls = observer.PriorityPathMessageFlushedCalls.Where(c => c.tickNumber == TickT + 1).ToList();

            Assert.AreEqual(8, tickTCalls.Count, "Exactly 8 messages must flush at tick T.");
            Assert.AreEqual(4, tickT1Calls.Count, "Exactly 4 messages must flush at tick T+1.");
            Assert.AreEqual(12, observer.PriorityPathMessageFlushedCalls.Count, "No message may be dropped — all 12 flush across tick T and T+1.");

            Assert.AreEqual(EnhancementMessageTypeId, tickTCalls[0].messageTypeId,
                "The enhancement-exempt message's callback must always be first in tick T, regardless of its position in emission order.");

            // The remaining 7 tick-T slots are filled by the other messages in their original
            // relative (emission) order, skipping the extracted enhancement message.
            CollectionAssert.AreEqual(
                new ushort[] { EnhancementMessageTypeId, 0x0200, 0x0201, 0x0202, 0x0203, 0x0204, 0x0206, 0x0207 },
                tickTCalls.Select(c => c.messageTypeId).ToArray());

            // The 4 messages deferred to tick T+1 keep their original relative order.
            CollectionAssert.AreEqual(
                new ushort[] { 0x0208, 0x0209, 0x020A, 0x020B },
                tickT1Calls.Select(c => c.messageTypeId).ToArray());
        }
    }
}
