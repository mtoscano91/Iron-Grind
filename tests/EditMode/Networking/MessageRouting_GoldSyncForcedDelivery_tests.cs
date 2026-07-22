using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 026 — the MCR-4 <see cref="GoldSyncEvent"/>
    /// forced-delivery overflow policy (<see cref="GoldSyncForcedDeliveryTracker"/>,
    /// <see cref="GoldSyncForcedDeliveryCodec"/>, <see cref="GoldSyncEventForcedDelivery"/>). Covers
    /// AC-MCR-01 (3 consecutive R-U overflow drops escalate to a standalone R-OD forced delivery on
    /// the 4th tick, matching the authoritative balance) and AC-MCR-07 (forced delivery substituting
    /// for a normal R-U delivery for more than <c>FORCED_DELIVERY_CONSECUTIVE_TICKS</c> consecutive
    /// elapsed real ticks logs a <c>GoldSyncForcedDelivery</c> critical anomaly exactly once).
    /// </summary>
    /// <remarks>
    /// <b>Two-counter design, verified against F-MCR-1's own rate formula:</b> see
    /// <see cref="GoldSyncForcedDeliveryTracker"/>'s class remarks for the full derivation. The tests
    /// below deliberately include one that interleaves real drop ticks with real forced-delivery
    /// confirmations in the exact verified 4-tick cycle (3 drops + 1 confirm, repeating) to prove the
    /// anomaly counter advances 1:1 with real elapsed ticks — not 1:1 with forced-delivery fire
    /// events, which an earlier (corrected pre-implementation) draft of this class got wrong.
    /// </remarks>
    [TestFixture]
    internal sealed class MessageRouting_GoldSyncForcedDelivery_Tests
    {
        private static PendingSubMessage MakeSelfPositionFiller(int payloadLength)
            => new PendingSubMessage(RUBatchCategory.SelfPositionUpdate, messageTypeId: 0x0A30, new byte[payloadLength]);

        private static void DriveConsecutiveDrops(GoldSyncForcedDeliveryTracker tracker, CharacterID characterId, int totalDropCalls, NetworkTestObserver observer)
        {
            for (int i = 0; i < totalDropCalls; i++)
            {
                tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false, observer);
            }
        }

        // ===========================================================================================
        // AC-MCR-01: 3 consecutive real R-U overflow-drop ticks (genuine RUBatchWriter category
        // eviction, not a mocked drop), then tick T+3 emits a standalone R-OD forced-delivery message
        // whose decoded balance matches the server's authoritative balance exactly.
        // ===========================================================================================

        [Test]
        public void AC_MCR_01_ThreeConsecutiveRealOverflowDropTicks_TickFourEmitsStandaloneForcedDeliveryMatchingAuthoritativeBalance()
        {
            // Arrange
            var characterId = new CharacterID(555);
            var tracker = new GoldSyncForcedDeliveryTracker();
            var observer = new NetworkTestObserver();
            const uint authoritativeBalance = 9000u;

            // A single oversized SelfPositionUpdate filler sub-message pushes the real (untouched)
            // RUBatchWriter.MAX_MESSAGE_BODY_BYTES=512 cap just over budget once GoldSyncEvent's 17
            // bytes are added (12 header + 494 filler + 17 gold = 523 > 512); the real category-
            // eviction algorithm evicts GoldSyncEvent (removing exactly 17 bytes brings it to 506,
            // under budget) every tick this filler is present — genuine overflow, not a mocked drop.
            var filler = new List<PendingSubMessage> { MakeSelfPositionFiller(490) };

            // Act — ticks T, T+1, T+2: real R-U overflow drops GoldSyncEvent all three times.
            for (int tick = 0; tick < GoldSyncForcedDeliveryTracker.GOLD_MAX_CONSECUTIVE_DROP; tick++)
            {
                byte[] buffer = new byte[600];
                var goldSyncEvents = new List<GoldSyncEvent>
                {
                    new GoldSyncEvent(characterId, authoritativeBalance, version: (uint)(tick + 1), GoldTransactionReason.MonsterDrop)
                };

                LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] BatchOverflow"));
                RUBatchWriter.Write(
                    buffer, sequenceNumber: (uint)(tick + 1), tickNumber: (uint)(100 + tick),
                    damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: goldSyncEvents, otherSubMessages: filler,
                    clientIdForLogging: 1u, observer: observer);

                bool wasDelivered = observer.ServerGoldSyncBatchedCalls.Count > 0;
                Assert.IsFalse(wasDelivered, $"Tick {tick}: GoldSyncEvent must be genuinely overflow-dropped by the real RUBatchWriter category-eviction algorithm.");
                tracker.RecordRUDeliveryOutcome(characterId, wasDelivered, observer);
            }

            // Assert — after 3 consecutive real drops, forced delivery is required on the next tick.
            Assert.IsTrue(tracker.ShouldForceDelivery(characterId), "After 3 consecutive overflow-dropped ticks, forced R-OD delivery must be required on the next tick.");
            Assert.AreEqual(0, observer.ServerGoldSyncForcedDeliveryEmittedCalls.Count, "Forced delivery must not have been emitted before the 4th tick.");

            // Act — tick T+3: emit the standalone R-OD forced-delivery message, enqueue it exempt
            // (queue-jump ahead of ordinary non-exempt messages — PriorityPathQueue<T>'s only
            // available mechanism, per its own class remarks), flush, and decode on the "client."
            var priorityQueue = new PriorityPathQueue<GoldSyncEvent>();
            var forcedDeliveryEvent = new GoldSyncEvent(characterId, authoritativeBalance, version: 4u, GoldTransactionReason.MonsterDrop);
            byte[] forcedBuffer = new byte[GoldSyncEventForcedDelivery.WireSize];

            GoldSyncForcedDeliveryCodec.Write(forcedBuffer, 4u, 103u, in forcedDeliveryEvent, observer);
            priorityQueue.Enqueue(forcedDeliveryEvent, isExempt: true);
            tracker.ConfirmForcedDeliverySucceeded(characterId);

            IReadOnlyList<QueuedMessage<GoldSyncEvent>> flushed = priorityQueue.Flush(tickNumber: 103u);

            bool decoded = GoldSyncForcedDeliveryCodec.TryRead(forcedBuffer, out ServerMessageEnvelope envelope, out GoldSyncEvent clientDecoded);
            observer.OnClientGoldSyncForcedDeliveryReceived(clientDecoded.CharacterId.RawValue, clientDecoded.NewBalance, clientDecoded.Version);

            // Assert — standalone R-OD wire shape and tick number are correct.
            Assert.IsTrue(decoded);
            Assert.AreEqual(GoldSyncEventForcedDelivery.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(103u, envelope.ServerTickNumber, "Forced delivery must be authored on tick T+3.");

            // Assert — the client's decoded balance matches the server's authoritative balance
            // exactly, never a delta (AC-NC-19 invariant carried over unchanged).
            Assert.AreEqual(authoritativeBalance, clientDecoded.NewBalance, "Client-received balance must exactly match the server's authoritative balance — never a delta.");

            // Assert — forced delivery took the R-OD priority path, ahead of ordinary non-exempt messages.
            Assert.AreEqual(1, flushed.Count);
            Assert.AreEqual(forcedDeliveryEvent, flushed[0].Payload);
            Assert.IsTrue(flushed[0].IsExempt);

            // Assert — server/client observer hooks fired exactly once with matching values.
            Assert.AreEqual(1, observer.ServerGoldSyncForcedDeliveryEmittedCalls.Count);
            Assert.AreEqual((characterId.RawValue, authoritativeBalance, 4u, 103u), observer.ServerGoldSyncForcedDeliveryEmittedCalls[0]);
            Assert.AreEqual(1, observer.ClientGoldSyncForcedDeliveryReceivedCalls.Count);
            Assert.AreEqual((characterId.RawValue, authoritativeBalance, 4u), observer.ClientGoldSyncForcedDeliveryReceivedCalls[0]);
        }

        [Test]
        public void ShouldForceDelivery_TwoConsecutiveDrops_StillFalse()
        {
            // Arrange
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(1);

            // Act
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);

            // Assert
            Assert.AreEqual(2, tracker.GetConsecutiveDropCount(characterId));
            Assert.IsFalse(tracker.ShouldForceDelivery(characterId), "2 consecutive drops must not yet require forced delivery (GOLD_MAX_CONSECUTIVE_DROP=3).");
        }

        [Test]
        public void PriorityPathQueue_ForcedDeliveryEnqueuedExempt_PlacedAheadOfOrdinaryNonExempt()
        {
            // Arrange
            var characterId = new CharacterID(42);
            var queue = new PriorityPathQueue<GoldSyncEvent>();
            for (int i = 1; i <= 5; i++)
            {
                queue.Enqueue(new GoldSyncEvent(new CharacterID((uint)(100 + i)), newBalance: 10u, version: 1u, GoldTransactionReason.MonsterDrop), isExempt: false);
            }

            var forcedMessage = new GoldSyncEvent(characterId, newBalance: 777u, version: 4u, GoldTransactionReason.MonsterDrop);

            // Act — caller enqueues the forced-delivery message exempt (this class's job, not
            // PriorityPathQueue<T>'s or GoldSyncForcedDeliveryTracker's — see tracker class remarks).
            queue.Enqueue(forcedMessage, isExempt: true);
            IReadOnlyList<QueuedMessage<GoldSyncEvent>> flushed = queue.Flush(tickNumber: 500u);

            // Assert — forced delivery lands ahead of every ordinary non-exempt message.
            Assert.AreEqual(forcedMessage, flushed[0].Payload, "Forced delivery must occupy position 1, ahead of ordinary non-exempt messages.");
            Assert.IsTrue(flushed[0].IsExempt);
        }

        [Test]
        public void GoldSyncForcedDeliveryCodec_WriteThenTryRead_RoundTripsExactlyAndRejectsWrongMessageType()
        {
            // Arrange
            var original = new GoldSyncEvent(new CharacterID(3), newBalance: 4000u, version: 12u, GoldTransactionReason.CompensatingRefund);
            Span<byte> buffer = new byte[GoldSyncEventForcedDelivery.WireSize];

            // Act
            int written = GoldSyncForcedDeliveryCodec.Write(buffer, 9u, 500u, in original);

            // Assert — round trip.
            Assert.AreEqual(GoldSyncEventForcedDelivery.WireSize, written);
            Assert.IsTrue(GoldSyncForcedDeliveryCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out GoldSyncEvent decoded));
            Assert.AreEqual(GoldSyncEventForcedDelivery.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(9u, envelope.SequenceNumber);
            Assert.AreEqual(500u, envelope.ServerTickNumber);
            Assert.AreEqual(original, decoded, "Decoded GoldSyncEvent must equal the original exactly — round-trip must be unchanged, absolute balance never a delta (AC-NC-19).");

            // Assert — a buffer whose envelope carries the wrong MessageTypeId must fail gracefully.
            Span<byte> wrongTypeBuffer = new byte[GoldSyncEventForcedDelivery.WireSize];
            MessageEnvelopeCodec.Write(wrongTypeBuffer, new ServerMessageEnvelope(GoldSyncEvent.MessageTypeId, 9u, 500u));
            Assert.IsFalse(GoldSyncForcedDeliveryCodec.TryRead(wrongTypeBuffer, out _, out _));
        }

        // ===========================================================================================
        // Two-counter distinction (drop counter vs. anomaly elapsed-tick counter) — supporting
        // coverage pinning the corrected design.
        // ===========================================================================================

        [Test]
        public void ConfirmForcedDeliverySucceeded_ResetsConsecutiveDropCountOnly_NotElapsedAnomalyCounter()
        {
            // Arrange
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(21);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // armed: dropCount=3, elapsed=1
            Assert.IsTrue(tracker.ShouldForceDelivery(characterId));

            // Act
            tracker.ConfirmForcedDeliverySucceeded(characterId);

            // Assert
            Assert.AreEqual(0, tracker.GetConsecutiveDropCount(characterId), "Forced R-OD confirmation resets the drop counter (MCR-4).");
            Assert.IsFalse(tracker.ShouldForceDelivery(characterId));
            Assert.AreEqual(1, tracker.GetTicksSinceForcedDeliveryRequired(characterId),
                "Forced R-OD confirmation must NOT reset the anomaly elapsed-tick counter — only a normal R-U success does (MCR-4's own \"without a normal R-U delivery succeeding\" wording).");
        }

        [Test]
        public void RecordRUDeliveryOutcome_NormalDeliverySucceedsMidStreak_ResetsBothCounters()
        {
            // Arrange
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(13);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false);
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // armed, elapsed=1
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false); // elapsed=2
            Assert.AreEqual(2, tracker.GetTicksSinceForcedDeliveryRequired(characterId));

            // Act — a genuine normal R-U delivery success.
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: true);

            // Assert
            Assert.AreEqual(0, tracker.GetConsecutiveDropCount(characterId));
            Assert.AreEqual(0, tracker.GetTicksSinceForcedDeliveryRequired(characterId));
            Assert.IsFalse(tracker.ShouldForceDelivery(characterId));
        }

        // ===========================================================================================
        // AC-MCR-07: forced delivery substituting for normal R-U delivery for more than
        // FORCED_DELIVERY_CONSECUTIVE_TICKS consecutive elapsed real ticks logs a
        // GoldSyncForcedDelivery critical anomaly exactly once, with the correct characterId and
        // elapsed-tick count.
        // ===========================================================================================

        [Test]
        public void RecordRUDeliveryOutcome_ExactlyThresholdElapsedTicks_DoesNotYetLogAnomaly()
        {
            // Arrange — GOLD_MAX_CONSECUTIVE_DROP-1 (2) further calls beyond the arming call bring
            // elapsed to exactly FORCED_DELIVERY_CONSECUTIVE_TICKS (100): 3 calls arm at elapsed=1,
            // then 99 more calls reach elapsed=100 (total 102 calls).
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(9);
            var observer = new NetworkTestObserver();
            int totalCalls = (GoldSyncForcedDeliveryTracker.GOLD_MAX_CONSECUTIVE_DROP - 1) + GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS;

            // Act
            DriveConsecutiveDrops(tracker, characterId, totalCalls, observer);

            // Assert — MCR-4 requires MORE than the threshold, so exactly 100 must not yet anomaly-log.
            Assert.AreEqual(GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS, tracker.GetTicksSinceForcedDeliveryRequired(characterId));
            Assert.AreEqual(0, observer.GoldSyncForcedDeliveryAnomalyLoggedCalls.Count, "No anomaly yet at exactly the threshold — MCR-4 requires strictly MORE than it.");
        }

        [Test]
        public void RecordRUDeliveryOutcome_OneMoreThanThreshold_LogsAnomalyExactlyOnceWithCorrectCharacterIdAndCount()
        {
            // Arrange
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(11);
            var observer = new NetworkTestObserver();
            int totalCalls = (GoldSyncForcedDeliveryTracker.GOLD_MAX_CONSECUTIVE_DROP - 1) + GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS + 1;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[GoldSyncForcedDeliveryTracker\] GoldSyncForcedDelivery"));

            // Act
            DriveConsecutiveDrops(tracker, characterId, totalCalls, observer);

            // Assert — fires exactly once, at exactly 101 elapsed ticks, with the correct characterId.
            Assert.AreEqual(GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS + 1, tracker.GetTicksSinceForcedDeliveryRequired(characterId));
            Assert.AreEqual(1, observer.GoldSyncForcedDeliveryAnomalyLoggedCalls.Count, "Anomaly must fire exactly once at the crossing tick.");
            Assert.AreEqual(
                (characterId.RawValue, GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS + 1),
                observer.GoldSyncForcedDeliveryAnomalyLoggedCalls[0]);

            // Act — one more elevated tick must NOT re-fire the anomaly.
            tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false, observer);

            // Assert
            Assert.AreEqual(1, observer.GoldSyncForcedDeliveryAnomalyLoggedCalls.Count, "Anomaly must not re-fire on every subsequent tick while still elevated.");
        }

        [Test]
        public void RecordRUDeliveryOutcome_RealisticFourTickSustainedOverflowCycle_AnomalyFiresAtExactly101ElapsedTicksDespiteInterleavedConfirmations()
        {
            // Arrange — this is the exact scenario that surfaced the pre-implementation design bug:
            // a client in the verified F-MCR-1 4-tick sustained-overflow cycle (3 real R-U drop
            // ticks + 1 forced-delivery-confirm tick, repeating). An earlier draft checked the
            // anomaly threshold only inside the confirm call, which — under this exact cycle — would
            // skip past elapsed=101 entirely (fire-tick elapsed values follow 4k-2, which has no
            // integer solution at 101), firing 2 ticks late. Checking on every RecordRUDeliveryOutcome
            // call (this test's own call pattern) fixes that.
            var tracker = new GoldSyncForcedDeliveryTracker();
            var characterId = new CharacterID(77);
            var observer = new NetworkTestObserver();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[GoldSyncForcedDeliveryTracker\] GoldSyncForcedDelivery"));

            int confirmedForcedDeliveryCount = 0;
            int safetyTickBound = 0;

            // Act — every real tick's own R-U attempt is recorded (including fire ticks, since a
            // client in genuinely sustained overflow drops the normal R-U attempt every tick, fire or
            // not); whenever forced delivery is required, confirm it (mirroring the real per-tick
            // call order: RecordRUDeliveryOutcome first, then ShouldForceDelivery, then confirm).
            while (observer.GoldSyncForcedDeliveryAnomalyLoggedCalls.Count == 0)
            {
                safetyTickBound++;
                Assert.Less(safetyTickBound, 1000, "Safety bound — the anomaly must fire well before 1000 real ticks.");

                tracker.RecordRUDeliveryOutcome(characterId, wasDelivered: false, observer);

                if (tracker.ShouldForceDelivery(characterId))
                {
                    tracker.ConfirmForcedDeliverySucceeded(characterId);
                    confirmedForcedDeliveryCount++;
                }
            }

            // Assert — fires at exactly 101 elapsed ticks, matching the GDD's literal 1:1
            // real-elapsed-tick claim ("100 ticks (5 seconds at 20Hz)"), and only after genuinely
            // interleaving multiple forced-delivery confirmations (not a pure-drop-only path).
            Assert.AreEqual(1, observer.GoldSyncForcedDeliveryAnomalyLoggedCalls.Count);
            Assert.AreEqual(
                (characterId.RawValue, GoldSyncForcedDeliveryTracker.FORCED_DELIVERY_CONSECUTIVE_TICKS + 1),
                observer.GoldSyncForcedDeliveryAnomalyLoggedCalls[0]);
            Assert.Greater(confirmedForcedDeliveryCount, 0, "This test must genuinely interleave forced-delivery confirmations, not just accumulate pure drops.");

            // Assert — the anomaly must fire after exactly 103 real loop iterations (3 arming calls
            // + 100 further elapsed-tick calls = 103), NOT merely whenever the tracker's own counter
            // happens to report 101. This is the discriminating assertion: the historical bug this
            // test exists to catch (elapsed incrementing only inside ConfirmForcedDeliverySucceeded,
            // i.e. only on confirm/fire ticks) would still make every assertion above pass — just
            // after ~300+ iterations instead of 103, since GetTicksSinceForcedDeliveryRequired reads
            // back the tracker's own (possibly-buggy) counter either way. Pinning safetyTickBound
            // against the real per-tick call count is what actually distinguishes correct from buggy.
            Assert.AreEqual(103, safetyTickBound,
                "Under the corrected design, the anomaly must fire on the 103rd real RecordRUDeliveryOutcome call " +
                "(ticks 1-3 arm the regime, ticks 4-103 advance the elapsed counter to 101) — a different iteration " +
                "count here would indicate the elapsed counter is no longer advancing 1:1 with real ticks.");
        }

        // ===========================================================================================
        // IZoneTestConfigurator.SetBatchSizeLimit — direct coverage for the new test-harness method
        // itself (Story 026), matching SetZoneCapacity's own set/read-back/instance-scoping/reset
        // test precedent. Not wired to any production consumer yet — see TD-027.
        // ===========================================================================================

        [Test]
        public void SetBatchSizeLimit_SetThenGetThenReset_RoundTripsAndIsScopedToOneZoneInstance()
        {
            // Arrange
            var zoneConfig = new ZoneTestConfigurator();

            // Assert — unset reads back null.
            Assert.IsNull(zoneConfig.GetBatchSizeLimitOverride(7u), "Unset zone instance must read back null.");

            // Act
            zoneConfig.SetBatchSizeLimit(zoneInstanceId: 7u, maxBytes: 256);

            // Assert — round trip and instance scoping.
            Assert.AreEqual(256, zoneConfig.GetBatchSizeLimitOverride(7u));
            Assert.IsNull(zoneConfig.GetBatchSizeLimitOverride(8u), "A different zone instance must not see zone 7's override.");

            // Act
            zoneConfig.Reset(7u);

            // Assert — Reset clears the override for this zone instance only.
            Assert.IsNull(zoneConfig.GetBatchSizeLimitOverride(7u), "Reset must clear the override for this zone instance.");
        }
    }
}
