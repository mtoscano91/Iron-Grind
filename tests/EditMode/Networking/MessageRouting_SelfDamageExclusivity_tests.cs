using System;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 027 — <c>SelfDamageEvent</c> vs
    /// <c>DamageEvent</c> delivery exclusivity (<see cref="SelfDamageEvent"/>,
    /// <see cref="SelfDamageEventCodec"/>, <see cref="SelfDamageEventDispatcher"/>,
    /// <see cref="SelfDamageSuppressionGate"/>, <see cref="SelfDamageRecipientGuard"/>,
    /// <see cref="CycleTimerInterpolator"/>). Covers all 7 blocking ACs: AC-NC-37, AC-MCR-02,
    /// AC-MCR-05, AC-MCR-08, AC-CCR-04, AC-CCR-07, AC-CCR-08.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No live per-client transport loop exists yet</b> (TD-028) — per-client "capture" lists in
    /// the exclusivity tests below are constructed by hand, the same structural-composition approach
    /// every prior story in this epic uses (see e.g. <c>MessageRouting_GoldSyncForcedDelivery_tests.cs</c>).
    /// </para>
    /// <para>
    /// <b>AC-CCR-07's EntityHealthUpdate half reuses <see cref="StaleDiscardComparer.IsTickExpired"/>
    /// for a purpose different from its original TTL-expiry design intent:</b> here it answers "has
    /// the candidate message's tick caught up to or passed what's already applied" (a freshness-
    /// acceptance gate), not "has a fixed future deadline been reached." The class's own doc comment
    /// scopes it generally enough to cover this ("any future versioned-state message... must route
    /// through IsNewerVersion or IsTickExpired"), so this is a legitimate reuse of the same
    /// wraparound-safe math for a different conceptual question — not a TTL check in disguise.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class MessageRouting_SelfDamageExclusivity_Tests
    {
        private static readonly EntityID AttackerEntityId = new EntityID(10);
        private static readonly EntityID TargetEntityId = new EntityID(20);

        // ===========================================================================================
        // AC-NC-37 / AC-CCR-04: exclusivity — attacker's capture has SelfDamageEvent only, target's
        // capture has DamageEvent only, same (attacker, target) pair, no overlap either direction.
        // ===========================================================================================

        [Test]
        public void AC_NC_37_AC_CCR_04_AttackerCaptureHasOnlySelfDamageEvent_TargetCaptureHasOnlyDamageEvent()
        {
            // Arrange
            var observer = new NetworkTestObserver();
            const int finalDamage = 55;
            var selfDamagePayload = new DamageEvent(AttackerEntityId, TargetEntityId, finalDamage, isCrit: false, DamageType.Physical);
            var zoneDamagePayload = new DamageEvent(AttackerEntityId, TargetEntityId, finalDamage, isCrit: false, DamageType.Physical);

            // Act — server emits SelfDamageEvent addressed ONLY to the attacker (singleton recipient,
            // structural via SelfDamageEventDispatcher — no filter of a zone-wide list exists here).
            Span<byte> selfDamageBuffer = new byte[SelfDamageEvent.WireSize];
            SelfDamageEventDispatcher.SerializeForAttacker(selfDamageBuffer, sequenceNumber: 1u, tickNumber: 500u, AttackerEntityId, in selfDamagePayload, observer);

            // Act — server emits DamageEvent into the R-U batch destined for all zone clients except
            // the attacker (Story 007's existing path, untouched). Only client B's capture receives it.
            Span<byte> zoneDamageBuffer = new byte[DamageEvent.BatchSize];
            BatchSubMessageCodec.WriteDamageEvent(zoneDamageBuffer, in zoneDamagePayload);
            observer.OnServerDamageEventSerialized(zoneDamagePayload.AttackerEntityId.RawValue, zoneDamagePayload.TargetEntityId.RawValue, zoneDamagePayload.FinalDamage);

            // Construct the two clients' captures explicitly — client A's capture never receives a
            // DamageEvent buffer at all (not filtered out — never added), client B's capture never
            // receives a SelfDamageEvent buffer at all.
            var clientACapture = new { SelfDamageEventBuffers = new[] { selfDamageBuffer.ToArray() }, DamageEventBuffers = Array.Empty<byte[]>() };
            var clientBCapture = new { SelfDamageEventBuffers = Array.Empty<byte[]>(), DamageEventBuffers = new[] { zoneDamageBuffer.ToArray() } };

            // Assert — client A's capture: exactly one SelfDamageEvent, correct pair, no DamageEvent.
            Assert.AreEqual(1, clientACapture.SelfDamageEventBuffers.Length);
            Assert.AreEqual(0, clientACapture.DamageEventBuffers.Length, "The attacker's client must never receive a DamageEvent for its own attack.");
            Assert.IsTrue(SelfDamageEventCodec.TryRead(clientACapture.SelfDamageEventBuffers[0], out ServerMessageEnvelope aEnvelope, out DamageEvent aDecoded));
            Assert.AreEqual(SelfDamageEvent.MessageTypeId, aEnvelope.MessageTypeId);
            Assert.AreEqual(AttackerEntityId, aDecoded.AttackerEntityId);
            Assert.AreEqual(TargetEntityId, aDecoded.TargetEntityId);

            // Assert — client B's capture: exactly one DamageEvent, same pair, no SelfDamageEvent.
            Assert.AreEqual(1, clientBCapture.DamageEventBuffers.Length);
            Assert.AreEqual(0, clientBCapture.SelfDamageEventBuffers.Length, "A non-attacker client must never receive a SelfDamageEvent.");
            Assert.IsTrue(BatchSubMessageCodec.TryReadDamageEvent(clientBCapture.DamageEventBuffers[0], out DamageEvent bDecoded, out _));
            Assert.AreEqual(AttackerEntityId, bDecoded.AttackerEntityId);
            Assert.AreEqual(TargetEntityId, bDecoded.TargetEntityId);

            // Assert — server-side observer hooks fired exactly once each, on the correct path.
            Assert.AreEqual(1, observer.ServerSelfDamageEventSerializedCalls.Count);
            Assert.AreEqual((AttackerEntityId.RawValue, TargetEntityId.RawValue, finalDamage), observer.ServerSelfDamageEventSerializedCalls[0]);
            Assert.AreEqual(1, observer.ServerDamageEventSerializedCalls.Count);
            Assert.AreEqual((AttackerEntityId.RawValue, TargetEntityId.RawValue, finalDamage), observer.ServerDamageEventSerializedCalls[0]);
        }

        [Test]
        public void SelfDamageEventDispatcher_RecipientDoesNotMatchPayloadAttacker_Throws()
        {
            // Arrange
            var wrongPayload = new DamageEvent(new EntityID(999), TargetEntityId, 10, isCrit: false, DamageType.Physical);
            byte[] buffer = new byte[SelfDamageEvent.WireSize];

            // Act & Assert — the recipient parameter and the payload's own AttackerEntityId must agree.
            Assert.Throws<ArgumentException>(() =>
                SelfDamageEventDispatcher.SerializeForAttacker(buffer, 1u, 500u, AttackerEntityId, in wrongPayload));
        }

        [Test]
        public void SelfDamageEventDispatcher_NoMethodAcceptsACollectionShapedRecipientParameter_StructurallySingleRecipientOnly()
        {
            // Regression test (code review, qa-tester): mirrors
            // SelfDamageSuppressionGate_NeverFallsBackToDamageEvent_StructurallyUnreachable's pattern.
            // EC-CCR-2 / MCR-2: "the recipient set for SelfDamageEvent must be constructed as a
            // singleton {attackerEntityId} — not derived from the zone-wide broadcast list." This test
            // pins that structural claim: no public static method on this class may accept an array or
            // any IEnumerable-implementing collection type as a parameter — the only representable
            // "recipient" shape is a single EntityID.
            var dispatcherMethods = typeof(SelfDamageEventDispatcher).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            Assert.Greater(dispatcherMethods.Length, 0, "Sanity check: the dispatcher must expose at least one public static method.");

            foreach (var method in dispatcherMethods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    Type parameterType = parameter.ParameterType;
                    bool isArray = parameterType.IsArray;
                    bool isCollection = typeof(System.Collections.IEnumerable).IsAssignableFrom(parameterType) && parameterType != typeof(string);

                    Assert.IsFalse(isArray,
                        $"{method.Name}'s parameter '{parameter.Name}' must not be an array — the recipient must be a single EntityID, never a filterable list (EC-CCR-2).");
                    Assert.IsFalse(isCollection,
                        $"{method.Name}'s parameter '{parameter.Name}' must not be a collection type — the recipient must be a single EntityID, never a filterable list (EC-CCR-2).");
                }
            }
        }

        [Test]
        public void SelfDamageEventCodec_WriteThenTryRead_RoundTripsExactlyAndRejectsWrongMessageType()
        {
            // Arrange
            var original = new DamageEvent(AttackerEntityId, TargetEntityId, finalDamage: 77, isCrit: true, DamageType.Magical);
            Span<byte> buffer = new byte[SelfDamageEvent.WireSize];

            // Act
            int written = SelfDamageEventCodec.Write(buffer, sequenceNumber: 3u, tickNumber: 900u, in original);

            // Assert — round trip.
            Assert.AreEqual(SelfDamageEvent.WireSize, written);
            Assert.IsTrue(SelfDamageEventCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out DamageEvent decoded));
            Assert.AreEqual(SelfDamageEvent.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(3u, envelope.SequenceNumber);
            Assert.AreEqual(900u, envelope.ServerTickNumber);
            Assert.AreEqual(original, decoded);

            // Assert — a buffer whose envelope carries the wrong MessageTypeId must fail gracefully.
            Span<byte> wrongTypeBuffer = new byte[SelfDamageEvent.WireSize];
            MessageEnvelopeCodec.Write(wrongTypeBuffer, new ServerMessageEnvelope(DamageEvent.MessageTypeId, 3u, 900u));
            Assert.IsFalse(SelfDamageEventCodec.TryRead(wrongTypeBuffer, out _, out _));
        }

        // ===========================================================================================
        // AC-MCR-02: SelfDamageEvent arrives within 2 tick periods (100ms, structural — no real
        // timer) of the triggering Beat, and finalDamage matches a test-local Damage Calculation
        // system stand-in value exactly.
        // ===========================================================================================

        [Test]
        public void AC_MCR_02_SelfDamageEventAuthoredWithinTwoTickPeriodsOfBeat_CarriesDamageCalculationStandInValueExactly()
        {
            // Arrange — test-local stand-in for what the (not-yet-implemented) Damage Calculation
            // system will eventually compute (Out of Scope: real Beat resolution / damage formulas).
            const uint triggerBeatTick = 1000u;
            const uint selfDamageAuthoredTick = 1002u; // 2 ticks after the Beat — within the 100ms/2-tick budget.
            const int damageCalculationStandInFinalDamage = 133;
            var observer = new NetworkTestObserver();
            var payload = new DamageEvent(AttackerEntityId, TargetEntityId, damageCalculationStandInFinalDamage, isCrit: false, DamageType.Physical);

            // Act
            Span<byte> buffer = new byte[SelfDamageEvent.WireSize];
            SelfDamageEventDispatcher.SerializeForAttacker(buffer, sequenceNumber: 1u, tickNumber: selfDamageAuthoredTick, AttackerEntityId, in payload, observer);
            bool decoded = SelfDamageEventCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out DamageEvent clientDecoded);
            observer.OnClientSelfDamageEventReceived(clientDecoded.AttackerEntityId.RawValue, clientDecoded.TargetEntityId.RawValue, clientDecoded.FinalDamage);

            // Assert — structural 100ms/2-tick-period proof: authored tick is within 2 ticks of the Beat.
            Assert.IsTrue(decoded);
            uint tickDelta = envelope.ServerTickNumber - triggerBeatTick;
            Assert.LessOrEqual(tickDelta, 2u, "SelfDamageEvent must be authored within 2 tick periods (100ms) of the triggering Beat.");

            // Assert — finalDamage matches the Damage Calculation system's (stand-in) computed value exactly.
            Assert.AreEqual(damageCalculationStandInFinalDamage, clientDecoded.FinalDamage);
            Assert.AreEqual(1, observer.ClientSelfDamageEventReceivedCalls.Count);
            Assert.AreEqual((AttackerEntityId.RawValue, TargetEntityId.RawValue, damageCalculationStandInFinalDamage), observer.ClientSelfDamageEventReceivedCalls[0]);
        }

        // ===========================================================================================
        // AC-MCR-05 / AC-MCR-08: 3 consecutive dropped CycleTimerBroadcast packets — interpolation
        // stays monotonically non-decreasing throughout the gap (never freezes, never resets to
        // zero), and converges within 10% of full cycle on the 4th packet's arrival.
        // ===========================================================================================

        [Test]
        public void AC_MCR_05_AC_MCR_08_ThreeConsecutiveDroppedPackets_InterpolationMonotonicNonDecreasing_ConvergesWithinTenPercentOnFourthPacket()
        {
            // Arrange
            var injector = new TransportFaultInjector();
            var interpolator = new CycleTimerInterpolator();
            injector.DropNextOutbound(CycleTimerBroadcast.MessageTypeId, 3);

            // Establish a real rate from two genuinely received samples (ticks 100, 101).
            interpolator.RecordReceivedSample(100u, 1000);
            interpolator.RecordReceivedSample(101u, 1200); // rate = 200/tick

            // Act — ticks 102-104: the fault injector reports these 3 as dropped; no RecordReceivedSample.
            ushort[] sampledDuringGap = new ushort[3];
            for (int i = 0; i < 3; i++)
            {
                uint tick = (uint)(102 + i);
                bool dropped = injector.TryConsumeDropDecision(CycleTimerBroadcast.MessageTypeId);
                Assert.IsTrue(dropped, $"Tick {tick}: this packet must be reported as dropped by the fault injector.");
                sampledDuringGap[i] = interpolator.Sample(tick);
            }

            // Assert — AC-MCR-08: monotonically non-decreasing throughout the drop window, never
            // frozen (each strictly increases here, since the underlying rate is positive) and never
            // reset to zero.
            Assert.Greater(sampledDuringGap[0], 0);
            Assert.Less(sampledDuringGap[0], sampledDuringGap[1]);
            Assert.Less(sampledDuringGap[1], sampledDuringGap[2]);

            // Act — the 4th packet finally arrives at tick 105 with a slightly-off-from-perfectly-
            // linear authoritative value (real gameplay rate is not perfectly constant tick-to-tick).
            const ushort authoritativeValueAtTick105 = 2050;
            bool fourthPacketDropped = injector.TryConsumeDropDecision(CycleTimerBroadcast.MessageTypeId);
            Assert.IsFalse(fourthPacketDropped, "Only 3 drops were requested — the 4th packet must arrive.");
            ushort extrapolatedJustBeforeFourthPacket = interpolator.Sample(105u);
            interpolator.RecordReceivedSample(105u, authoritativeValueAtTick105);

            // Assert — AC-MCR-05: extrapolated position differs from authoritative by <= 10% of full cycle.
            int diff = Math.Abs(extrapolatedJustBeforeFourthPacket - authoritativeValueAtTick105);
            int tolerance = (int)(CycleTimerInterpolator.CYCLE_FULL * 0.10);
            Assert.LessOrEqual(diff, tolerance, "Extrapolated position must converge to within 10% of full cycle on the 4th packet's arrival.");
        }

        [Test]
        public void CycleTimerInterpolator_FewerThanTwoSamples_ReturnsLatestOrZero()
        {
            var interpolator = new CycleTimerInterpolator();

            Assert.AreEqual(0, interpolator.Sample(100u), "No samples recorded yet must return 0, not a frozen garbage value.");

            interpolator.RecordReceivedSample(100u, 500);
            Assert.AreEqual(500, interpolator.Sample(105u), "Only one sample recorded — no rate to extrapolate from, so the latest received value is held.");
        }

        [Test]
        public void CycleTimerInterpolator_ReorderedStaleSampleAfterNewerOne_IsDiscarded_DoesNotPerturbSample()
        {
            // Regression test (code review, unity-specialist): CycleTimerBroadcast is delivered over
            // U-U, which ADR-004 Decision 2 documents as "best-effort, no retransmit, no ordering." A
            // reordered/stale sample arriving AFTER a genuinely newer one must be discarded, not
            // installed as _latest — otherwise Sample()'s extrapolation would move the charge bar
            // backward, violating AC-MCR-08's monotonic-non-decreasing guarantee.

            // Arrange — establish a real rate, then receive tick 105 (the newest real sample so far).
            var interpolator = new CycleTimerInterpolator();
            interpolator.RecordReceivedSample(100u, 1000);
            interpolator.RecordReceivedSample(101u, 1200); // rate = 200/tick
            interpolator.RecordReceivedSample(105u, 2010);
            ushort sampleBeforeReorderedPacket = interpolator.Sample(106u);

            // Act — a reordered, stale packet for tick 103 (older than the already-recorded tick 105)
            // arrives after the fact.
            interpolator.RecordReceivedSample(103u, 1800);
            ushort sampleAfterReorderedPacket = interpolator.Sample(106u);

            // Assert — the reordered/stale sample must not perturb Sample()'s output at all: the
            // extrapolated value at tick 106 is identical before and after the reordered packet
            // arrives, and in particular does NOT regress to a value based on the stale (103, 1800)
            // pair (which would extrapolate to something at or below 1800 by tick 106 — well below the
            // tick-105-based extrapolation).
            Assert.AreEqual(sampleBeforeReorderedPacket, sampleAfterReorderedPacket,
                "A reordered/stale sample must be silently discarded — it must not change Sample()'s output at all.");
            Assert.GreaterOrEqual(sampleAfterReorderedPacket, 2010,
                "The charge bar must not regress below the last genuinely newer real value (AC-MCR-08) after a stale reordered packet arrives.");
        }

        [Test]
        public void CycleTimerInterpolator_DuplicateSampleSameTick_IsDiscarded()
        {
            // Arrange
            var interpolator = new CycleTimerInterpolator();
            interpolator.RecordReceivedSample(100u, 1000);
            interpolator.RecordReceivedSample(101u, 1200);

            // Act — a duplicate delivery of the same tick (transport-layer retransmit/duplication).
            interpolator.RecordReceivedSample(101u, 9999); // must NOT overwrite _latest
            ushort sample = interpolator.Sample(102u);

            // Assert — extrapolation still uses the original (101, 1200) sample, not the duplicate's bogus value.
            Assert.AreEqual(1400, sample, "A same-tick duplicate must be discarded — IsNewerVersion is false at equality.");
        }

        // ===========================================================================================
        // AC-CCR-07: reordered R-U delivery uses IsNewerVersion (GoldSyncEvent) / IsTickExpired
        // (EntityHealthUpdate) — never a raw uint comparison. Both scenarios use wraparound values
        // specifically because a raw comparison gives the WRONG answer at wraparound, while the
        // wraparound-safe helper gives the right one — the sharpest possible proof that no raw
        // comparison path exists.
        // ===========================================================================================

        [Test]
        public void AC_CCR_07_GoldSyncEvent_WraparoundReordering_UsesIsNewerVersionNeverRawUintComparison()
        {
            // Arrange — appliedVersion is near uint.MaxValue; the genuinely newer candidate has
            // wrapped around to a small value.
            const uint appliedVersion = uint.MaxValue - 1; // 4294967294
            const uint candidateVersionWrapped = 1u; // 3 versions later, wrapped
            var characterId = new CharacterID(42);
            var candidate = new GoldSyncEvent(characterId, newBalance: 999u, candidateVersionWrapped, GoldTransactionReason.MonsterDrop);

            Span<byte> buffer = new byte[GoldSyncEvent.BatchSize];
            BatchSubMessageCodec.WriteGoldSyncEvent(buffer, in candidate);
            Assert.IsTrue(BatchSubMessageCodec.TryReadGoldSyncEvent(buffer, out GoldSyncEvent decoded, out _));

            // Act
            bool rawComparisonResult = decoded.Version > appliedVersion; // the forbidden path
            bool wraparoundSafeResult = StaleDiscardComparer.IsNewerVersion(current: appliedVersion, candidate: decoded.Version);

            // Assert — a raw uint comparison gets this wrong (treats the wrapped-forward value as
            // older); the required IsNewerVersion helper gets it right.
            Assert.IsFalse(rawComparisonResult, "Sanity check: raw uint comparison incorrectly treats the wrapped-forward version as NOT newer.");
            Assert.IsTrue(wraparoundSafeResult, "IsNewerVersion must correctly accept the wrapped-forward version as newer (AC-CCR-07a).");
        }

        /// <summary>
        /// Test-local literal stand-in for <c>EntityHealthUpdate</c> (no concrete class exists yet in
        /// this codebase — Out of Scope for this story). Shape matches MCR-2's description: a
        /// per-tick reliable health value carrying a <c>ServerTickNumber</c> for stale-discard.
        /// </summary>
        private readonly struct EntityHealthUpdateFixture
        {
            public readonly EntityID EntityId;
            public readonly int CurrentHp;
            public readonly uint ServerTickNumber;

            public EntityHealthUpdateFixture(EntityID entityId, int currentHp, uint serverTickNumber)
            {
                EntityId = entityId;
                CurrentHp = currentHp;
                ServerTickNumber = serverTickNumber;
            }
        }

        [Test]
        public void AC_CCR_07_EntityHealthUpdate_WraparoundReordering_UsesIsTickExpiredNeverRawUintComparison()
        {
            // Arrange — lastAppliedTick is near uint.MaxValue; the genuinely newer candidate's tick
            // has wrapped around to a small value (5 ticks later in wraparound terms).
            const uint lastAppliedTick = uint.MaxValue - 2; // 4294967293
            const uint candidateTickWrapped = 2u; // 5 ticks later, wrapped
            var lastApplied = new EntityHealthUpdateFixture(TargetEntityId, currentHp: 80, lastAppliedTick);
            var candidate = new EntityHealthUpdateFixture(TargetEntityId, currentHp: 55, candidateTickWrapped);

            // Act
            bool rawComparisonResult = candidate.ServerTickNumber > lastApplied.ServerTickNumber; // the forbidden path

            // IsTickExpired(candidateTick, lastAppliedTick) here means "has the candidate's tick
            // caught up to or passed what's already applied" — a freshness-acceptance gate, reusing
            // IsTickExpired's wraparound-safe math for a purpose distinct from its original TTL-expiry
            // design intent (see class remarks at the top of this file).
            bool wraparoundSafeResult = StaleDiscardComparer.IsTickExpired(currentTick: candidate.ServerTickNumber, expiryTick: lastApplied.ServerTickNumber);

            // Assert
            Assert.IsFalse(rawComparisonResult, "Sanity check: raw uint comparison incorrectly treats the wrapped-forward tick as NOT newer.");
            Assert.IsTrue(wraparoundSafeResult, "IsTickExpired must correctly accept the wrapped-forward candidate tick as caught-up/newer (AC-CCR-07b).");
        }

        // ===========================================================================================
        // AC-CCR-08: EntityPositionUpdate stream interrupted for 3 ticks then resumed — the resumed
        // packet alone yields a correct absolute position (already-self-contained per Story 007; this
        // proves it, not new production code).
        // ===========================================================================================

        [Test]
        public void AC_CCR_08_ThreeTickGapThenResume_ResumedEntityPositionUpdateAloneYieldsCorrectAbsolutePosition()
        {
            // Arrange
            var injector = new TransportFaultInjector();
            injector.DropNextOutbound(EntityPositionUpdate.MessageTypeId, 3);

            // Act — ticks T, T+1, T+2: dropped, no packet ever constructed for these.
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(injector.TryConsumeDropDecision(EntityPositionUpdate.MessageTypeId), $"Drop #{i + 1} must be reported.");
            }

            // Act — T+3: resumed packet, deliberately unrelated to any hypothetical prior position
            // (proving there is no dependency on dropped-packet data).
            Assert.IsFalse(injector.TryConsumeDropDecision(EntityPositionUpdate.MessageTypeId), "Only 3 drops were requested — the resumed packet must arrive.");
            var resumed = new EntityPositionUpdate(TargetEntityId, posX: -777, posY: 42, posZ: 3001);
            Span<byte> buffer = new byte[EntityPositionUpdate.BatchSize];
            BatchSubMessageCodec.WriteEntityPositionUpdate(buffer, in resumed);

            // Assert — the resumed packet alone decodes to the correct absolute position.
            Assert.IsTrue(BatchSubMessageCodec.TryReadEntityPositionUpdate(buffer, out EntityPositionUpdate decoded, out _));
            Assert.AreEqual(resumed, decoded, "The resumed packet must decode to a correct absolute position using only its own bytes — no delta-decoding dependency on the 3 dropped packets.");
        }

        // ===========================================================================================
        // EC-MCR-2: SelfDamageEvent suppression — non-arrival timeout and stale-on-arrival.
        // ===========================================================================================

        [Test]
        public void SelfDamageSuppressionGate_HasSuppressionWindowElapsed_BoundaryAtExactlyFiveTicks()
        {
            const uint triggerBeatTick = 1000u;

            Assert.IsFalse(SelfDamageSuppressionGate.HasSuppressionWindowElapsed(triggerBeatTick, currentTick: 1004u), "4 elapsed ticks must NOT yet trigger suppression.");
            Assert.IsTrue(SelfDamageSuppressionGate.HasSuppressionWindowElapsed(triggerBeatTick, currentTick: 1005u), "Exactly 5 elapsed ticks must trigger suppression (window boundary).");
            Assert.IsTrue(SelfDamageSuppressionGate.HasSuppressionWindowElapsed(triggerBeatTick, currentTick: 1006u), "More than 5 elapsed ticks must trigger suppression.");
        }

        [Test]
        public void SelfDamageSuppressionGate_IsStaleOnArrival_BoundaryAtExactlyFiveTicks()
        {
            // EC-MCR-2's second suppression rule uses a strict qualifier ("more than 5 ticks older"),
            // unlike its first rule ("within 5 tick periods," no strict qualifier) — the two boundary
            // ticks below are deliberately NOT symmetric with HasSuppressionWindowElapsed's own
            // boundary test. See SelfDamageSuppressionGate's class remarks for the full derivation;
            // this test would have caught the original off-by-one (IsStaleOnArrival wrongly suppressed
            // at exactly 5 ticks stale before the SUPPRESSION_WINDOW_TICKS + 1 fix).
            const uint eventServerTickNumber = 1000u;

            Assert.IsFalse(SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber, currentTick: 1004u), "An event only 4 ticks old on arrival must NOT be suppressed.");
            Assert.IsFalse(SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber, currentTick: 1005u), "An event exactly 5 ticks old on arrival must NOT yet be suppressed (GDD: 'more than 5 ticks').");
            Assert.IsTrue(SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber, currentTick: 1006u), "An event exactly 6 ticks old on arrival (more than 5) must be suppressed (boundary).");
            Assert.IsTrue(SelfDamageSuppressionGate.IsStaleOnArrival(eventServerTickNumber, currentTick: 1010u), "An event well more than 5 ticks old on arrival must be suppressed.");
        }

        [Test]
        public void SelfDamageSuppressionGate_NeverFallsBackToDamageEvent_StructurallyUnreachable()
        {
            // EC-MCR-2: "the client must not substitute a DamageEvent as a fallback... A DamageEvent
            // matching the attacker's own attackerEntityId will never arrive at the attacker's client,
            // making any such fallback path unreachable." This test documents/pins that no API on
            // SelfDamageSuppressionGate accepts a DamageEvent as an alternate data source — the
            // suppression gate only ever operates on tick numbers, never on a DamageEvent payload.
            var suppressionGateMethods = typeof(SelfDamageSuppressionGate).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            foreach (var method in suppressionGateMethods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    Assert.AreNotEqual(typeof(DamageEvent), parameter.ParameterType,
                        $"{method.Name} must never accept a DamageEvent parameter — the DamageEvent fallback path is structurally impossible (EC-MCR-2).");
                }
            }
        }

        // ===========================================================================================
        // EC-CCR-2: wrong-recipient defense.
        // ===========================================================================================

        [Test]
        public void SelfDamageRecipientGuard_MatchingRecipient_ReturnsTrueAndLogsNoAnomaly()
        {
            var observer = new NetworkTestObserver();

            bool shouldDisplay = SelfDamageRecipientGuard.ValidateRecipient(attackerEntityId: 7u, localPlayerEntityId: 7u, observer);

            Assert.IsTrue(shouldDisplay);
            Assert.AreEqual(0, observer.SelfDamageDirectionViolationLoggedCalls.Count);
        }

        [Test]
        public void SelfDamageRecipientGuard_MismatchedRecipient_ReturnsFalseAndLogsSelfDamageDirectionViolation()
        {
            var observer = new NetworkTestObserver();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[SelfDamageRecipientGuard\] SelfDamageDirectionViolation"));

            bool shouldDisplay = SelfDamageRecipientGuard.ValidateRecipient(attackerEntityId: 7u, localPlayerEntityId: 8u, observer);

            Assert.IsFalse(shouldDisplay, "A mismatched recipient must suppress display.");
            Assert.AreEqual(1, observer.SelfDamageDirectionViolationLoggedCalls.Count);
            Assert.AreEqual((7u, 8u), observer.SelfDamageDirectionViolationLoggedCalls[0]);
        }
    }
}
