using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 001 — the three fault/crash/config injection
    /// interfaces (<see cref="ITransportFaultInjector"/>, <see cref="IServerCrashInjector"/>,
    /// <see cref="IZoneTestConfigurator"/>) and their concrete implementations. Covers AC-TH-1
    /// through AC-TH-3. AC-TH-4 (compile-only-inside-guard, no production call sites outside the
    /// guard) is a structural property verified by this file's own successful compilation plus
    /// source inspection — full release-build stripping verification is Story 002's CI job
    /// (AC-TC-01/AC-TC-02), not a runtime-testable EditMode assertion.
    /// </summary>
    [TestFixture]
    internal sealed class TransportFaultInjector_Tests
    {
        private const ushort SomeMessageType = 0x0101;
        private const ushort AnotherMessageType = 0x0103;

        private TransportFaultInjector _injector;

        [SetUp]
        public void SetUp()
        {
            _injector = new TransportFaultInjector();
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: DropNextOutbound(msgType, 2) followed by two outbound sends of
        // that type results in zero delivered messages of that type (QA Test Case).
        // -----------------------------------------------------------------------

        [Test]
        public void DropNextOutbound_SpecificMessageType_DropsExactlyThatManyThenAllowsFurther()
        {
            // Arrange
            _injector.DropNextOutbound(SomeMessageType, 2);

            // Act & Assert — first two sends of this type are dropped, the third is not.
            Assert.IsTrue(_injector.TryConsumeDropDecision(SomeMessageType), "1st send must be dropped.");
            Assert.IsTrue(_injector.TryConsumeDropDecision(SomeMessageType), "2nd send must be dropped.");
            Assert.IsFalse(_injector.TryConsumeDropDecision(SomeMessageType), "3rd send must NOT be dropped — only 2 were requested.");
        }

        [Test]
        public void DropNextOutbound_AnyTypeSentinel_DropsNextMessageRegardlessOfType()
        {
            // Arrange
            _injector.DropNextOutbound(TransportFaultInjector.AnyMessageTypeId, 1);

            // Act & Assert
            Assert.IsTrue(_injector.TryConsumeDropDecision(AnotherMessageType), "Any-type sentinel must drop the next message of ANY type.");
            Assert.IsFalse(_injector.TryConsumeDropDecision(AnotherMessageType), "Only 1 drop was requested via the Any sentinel.");
        }

        [Test]
        public void DropNextOutbound_NonMatchingType_DoesNotConsumeUnrelatedPendingDrop()
        {
            // Arrange — a drop is pending for SomeMessageType only.
            _injector.DropNextOutbound(SomeMessageType, 1);

            // Act & Assert — a send of a different, non-Any type must not be dropped.
            Assert.IsFalse(_injector.TryConsumeDropDecision(AnotherMessageType));
            Assert.IsTrue(_injector.TryConsumeDropDecision(SomeMessageType), "The pending drop for SomeMessageType must still be available.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: DelayNextOutbound(messageTypeId, count, delayMs).
        // -----------------------------------------------------------------------

        [Test]
        public void DelayNextOutbound_MatchingType_QueuesDelayForEachCallInOrderThenStops()
        {
            // Arrange
            _injector.DelayNextOutbound(SomeMessageType, 2, 200);

            // Act & Assert
            Assert.IsTrue(_injector.TryConsumeDelay(SomeMessageType, out int delay1));
            Assert.AreEqual(200, delay1);

            Assert.IsTrue(_injector.TryConsumeDelay(SomeMessageType, out int delay2));
            Assert.AreEqual(200, delay2);

            Assert.IsFalse(_injector.TryConsumeDelay(SomeMessageType, out int delay3), "Only 2 delays were requested.");
            Assert.AreEqual(0, delay3);
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: ReorderNext(messageTypeId) — reorder the next two matching
        // messages, consumed once.
        // -----------------------------------------------------------------------

        [Test]
        public void ReorderNext_MatchingType_ReportsReorderOnceThenClears()
        {
            // Arrange
            _injector.ReorderNext(SomeMessageType);

            // Act & Assert
            Assert.IsTrue(_injector.TryConsumeReorder(SomeMessageType), "A pending reorder must be reported once.");
            Assert.IsFalse(_injector.TryConsumeReorder(SomeMessageType), "The reorder request must be consumed after the first report.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: DropSnapshotFragment(fragmentIndex) — specific index.
        // -----------------------------------------------------------------------

        [Test]
        public void DropSnapshotFragment_SpecificIndex_DropsThatFragmentOnlyOnce()
        {
            // Arrange
            _injector.DropSnapshotFragment(3);

            // Act & Assert
            Assert.IsTrue(_injector.TryConsumeSnapshotFragmentDrop(3, isLastFragment: false));
            Assert.IsFalse(_injector.TryConsumeSnapshotFragmentDrop(3, isLastFragment: false), "Already consumed once.");
            Assert.IsFalse(_injector.TryConsumeSnapshotFragmentDrop(5, isLastFragment: true), "A specific-index drop must not also trigger the last-fragment sentinel.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: DropSnapshotFragment(ushort.MaxValue) drops the last fragment
        // regardless of totalFragments.
        // -----------------------------------------------------------------------

        [Test]
        public void DropSnapshotFragment_MaxValueSentinel_DropsLastFragmentRegardlessOfActualIndex()
        {
            // Arrange
            _injector.DropSnapshotFragment(ushort.MaxValue);

            // Act & Assert — the "last fragment" is reported at whatever index turns out to be last
            // (17 here), because totalFragments is not known before transmission begins (CR-NET-7.3).
            Assert.IsTrue(_injector.TryConsumeSnapshotFragmentDrop(fragmentIndex: 17, isLastFragment: true));
            Assert.IsFalse(_injector.TryConsumeSnapshotFragmentDrop(fragmentIndex: 17, isLastFragment: true), "Already consumed once.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: SetSequenceNumber — called before any message is emitted
        // applies immediately (EC-NET-6 wraparound scenario: seed 3 below
        // uint.MaxValue, wraps past uint.MaxValue to 1 after 4 messages, skipping
        // 0 — Story 005's zero-skip fix to ConsumeNextSequenceNumber, CR-NET-7.5).
        // -----------------------------------------------------------------------

        [Test]
        public void SetSequenceNumber_CalledBeforeAnyMessageEmitted_AppliesImmediatelyAndWrapsSkippingZero()
        {
            // Arrange
            _injector.SetSequenceNumber(4294967293u);

            // Act & Assert — Story 005 (CR-NET-7.5): 0 is reserved as "uninitialized" and must
            // never appear in a valid message, so wraparound skips 0 and resumes at 1.
            Assert.AreEqual(4294967293u, _injector.ConsumeNextSequenceNumber());
            Assert.AreEqual(4294967294u, _injector.ConsumeNextSequenceNumber());
            Assert.AreEqual(4294967295u, _injector.ConsumeNextSequenceNumber());
            Assert.AreEqual(1u, _injector.ConsumeNextSequenceNumber(), "Sequence number must wrap from uint.MaxValue to 1, skipping 0 (CR-NET-7.5).");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: SetSequenceNumber — called mid-stream takes effect atomically
        // at the next tick boundary, never immediately.
        // -----------------------------------------------------------------------

        [Test]
        public void SetSequenceNumber_CalledMidStream_DoesNotApplyUntilNextTickBoundary()
        {
            // Arrange — emit one message first so the injector is "mid-stream."
            Assert.AreEqual(0u, _injector.ConsumeNextSequenceNumber());

            // Act
            _injector.SetSequenceNumber(500u);

            // Assert — override must NOT be visible before the tick boundary advances.
            Assert.AreEqual(1u, _injector.ConsumeNextSequenceNumber(), "Mid-stream override must not apply before the next tick boundary.");

            _injector.AdvanceTickBoundary();

            Assert.AreEqual(500u, _injector.ConsumeNextSequenceNumber(), "Override must apply atomically once the tick boundary advances.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1: Reset() clears all pending fault injections.
        // -----------------------------------------------------------------------

        [Test]
        public void Reset_ClearsAllPendingFaultInjections()
        {
            // Arrange
            _injector.DropNextOutbound(SomeMessageType, 3);
            _injector.DelayNextOutbound(SomeMessageType, 2, 100);
            _injector.ReorderNext(SomeMessageType);
            _injector.DropSnapshotFragment(1);
            _injector.DropSnapshotFragment(ushort.MaxValue);

            // Get the injector into "mid-stream" state, then register a pending
            // SequenceNumber override — this is the only kind of override Reset()
            // can meaningfully clear (an immediate, pre-stream override just IS the
            // counter, not a "pending" fault injection).
            Assert.AreEqual(0u, _injector.ConsumeNextSequenceNumber(), "First consume establishes the mid-stream baseline (counter now at 1).");
            _injector.SetSequenceNumber(999u); // mid-stream — becomes a pending override, not yet applied

            // Act
            _injector.Reset();

            // Assert
            Assert.IsFalse(_injector.TryConsumeDropDecision(SomeMessageType), "Reset must clear pending drops.");
            Assert.IsFalse(_injector.TryConsumeDelay(SomeMessageType, out _), "Reset must clear pending delays.");
            Assert.IsFalse(_injector.TryConsumeReorder(SomeMessageType), "Reset must clear pending reorders.");
            Assert.IsFalse(_injector.TryConsumeSnapshotFragmentDrop(1, isLastFragment: false), "Reset must clear pending fragment drops.");
            Assert.IsFalse(_injector.TryConsumeSnapshotFragmentDrop(50, isLastFragment: true), "Reset must clear the pending last-fragment sentinel.");

            _injector.AdvanceTickBoundary();
            Assert.AreEqual(1u, _injector.ConsumeNextSequenceNumber(), "Reset must clear the pending SequenceNumber override — the counter continues from where it was (1), not 999.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-1 / code-review finding: Reset() must restore the "clean state"
        // it documents — including the mid-stream flag. A SetSequenceNumber() call
        // immediately after Reset() must apply immediately, not defer to the next
        // tick boundary as if still mid-stream from before the Reset().
        // -----------------------------------------------------------------------

        [Test]
        public void SetSequenceNumber_CalledImmediatelyAfterReset_AppliesImmediately()
        {
            // Arrange — put the injector into "mid-stream" state, then Reset() it.
            Assert.AreEqual(0u, _injector.ConsumeNextSequenceNumber(), "Establish mid-stream state before Reset().");
            _injector.Reset();

            // Act — SetSequenceNumber right after Reset() must behave as if pre-stream again.
            _injector.SetSequenceNumber(777u);

            // Assert — applies immediately; no AdvanceTickBoundary() call needed.
            Assert.AreEqual(777u, _injector.ConsumeNextSequenceNumber(), "Reset() must restore the pre-stream flag so a subsequent SetSequenceNumber() applies immediately, not deferred.");
        }
    }

    [TestFixture]
    internal sealed class ServerCrashInjector_Tests
    {
        private ServerCrashInjector _crashInjector;

        [SetUp]
        public void SetUp()
        {
            _crashInjector = new ServerCrashInjector();
        }

        // -----------------------------------------------------------------------
        // AC-TH-2 / QA Test Case: crash fires before the outcome message is queued
        // — verified via a call-order assertion (crash callback invoked, then
        // assert no message was ever enqueued).
        // -----------------------------------------------------------------------

        [Test]
        public void RegisterCrashAt_MatchingStepSignaled_InvokesCrashSynchronouslyBeforeMessageEnqueue()
        {
            // Arrange
            _crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite);
            bool crashed = false;
            bool messageEnqueued = false;

            // Act — simulate the persistence-write call stack: commit, signal the step, and only
            // enqueue the outcome message if the crash callback did NOT fire.
            _crashInjector.SignalStepReached(CrashStep.AfterPersistenceWrite, onCrash: () => crashed = true);
            if (!crashed)
            {
                messageEnqueued = true;
            }

            // Assert
            Assert.IsTrue(crashed, "The crash callback must fire synchronously when the registered step is reached.");
            Assert.IsFalse(messageEnqueued, "No message may be enqueued after a crash fires at AfterPersistenceWrite.");
        }

        [Test]
        public void RegisterCrashAt_NonMatchingStepSignaled_DoesNotInvokeCrash()
        {
            // Arrange
            _crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite);
            bool crashed = false;

            // Act — a different step is reached; the registered step never occurs.
            _crashInjector.SignalStepReached(CrashStep.AfterOutcomeEmit, onCrash: () => crashed = true);

            // Assert
            Assert.IsFalse(crashed, "A non-matching step must not invoke the crash callback.");
        }

        [Test]
        public void ClearRegistered_AfterRegistration_NoCrashFiresOnSubsequentSignal()
        {
            // Arrange
            _crashInjector.RegisterCrashAt(CrashStep.AfterGhostCleanupPersistenceWrite);
            _crashInjector.ClearRegistered();
            bool crashed = false;

            // Act
            _crashInjector.SignalStepReached(CrashStep.AfterGhostCleanupPersistenceWrite, onCrash: () => crashed = true);

            // Assert
            Assert.IsFalse(crashed, "ClearRegistered must prevent any subsequent crash from firing.");
        }

        [Test]
        public void SignalStepReached_NoRegistrationYet_DoesNotThrowAndDoesNotInvokeCallback()
        {
            // Arrange — no RegisterCrashAt call has been made.
            bool crashed = false;

            // Act & Assert
            Assert.DoesNotThrow(() => _crashInjector.SignalStepReached(CrashStep.AfterTTLExpiryPersistenceWrite, onCrash: () => crashed = true));
            Assert.IsFalse(crashed);
        }

        [Test]
        public void SignalStepReached_NullCallback_DoesNotThrow()
        {
            // Arrange
            _crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWriteRespec);

            // Act & Assert — a null onCrash delegate must be treated as a safe no-op.
            Assert.DoesNotThrow(() => _crashInjector.SignalStepReached(CrashStep.AfterPersistenceWriteRespec, onCrash: null));
        }

        [Test]
        public void RegisterCrashAt_CalledTwice_SecondRegistrationReplacesFirst()
        {
            // Arrange
            _crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite);
            _crashInjector.RegisterCrashAt(CrashStep.AfterOutcomeEmit);
            bool crashedAtFirstStep = false;
            bool crashedAtSecondStep = false;

            // Act
            _crashInjector.SignalStepReached(CrashStep.AfterPersistenceWrite, onCrash: () => crashedAtFirstStep = true);
            _crashInjector.SignalStepReached(CrashStep.AfterOutcomeEmit, onCrash: () => crashedAtSecondStep = true);

            // Assert
            Assert.IsFalse(crashedAtFirstStep, "The first registration must be replaced by the second RegisterCrashAt call.");
            Assert.IsTrue(crashedAtSecondStep, "Only the most recently registered step must trigger a crash.");
        }
    }

    [TestFixture]
    internal sealed class ZoneTestConfigurator_Tests
    {
        private const uint ZoneA = 100u;
        private const uint ZoneB = 200u;
        private const uint SomeEntity = 42u;

        private ZoneTestConfigurator _zoneConfig;

        [SetUp]
        public void SetUp()
        {
            _zoneConfig = new ZoneTestConfigurator();
        }

        // -----------------------------------------------------------------------
        // AC-TH-3 / QA Test Case: SetZoneCapacity for instance A only leaves
        // instance B's capacity unaffected.
        // -----------------------------------------------------------------------

        [Test]
        public void SetZoneCapacity_TwoInstances_InstanceBUnaffectedByInstanceAOverride()
        {
            // Arrange & Act
            _zoneConfig.SetZoneCapacity(ZoneA, 10);

            // Assert
            Assert.AreEqual(10, _zoneConfig.GetZoneCapacityOverride(ZoneA));
            Assert.IsNull(_zoneConfig.GetZoneCapacityOverride(ZoneB), "Instance B must have no capacity override.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-3 / QA Test Case: Reset(A) does not affect B's overrides either.
        // -----------------------------------------------------------------------

        [Test]
        public void Reset_OnlyClearsGivenZoneInstance_OtherInstanceOverridesSurvive()
        {
            // Arrange
            _zoneConfig.SetZoneCapacity(ZoneA, 10);
            _zoneConfig.SetZoneEntryPoint(ZoneA, 1, 2, 3);
            _zoneConfig.SetZoneCapacity(ZoneB, 25);
            _zoneConfig.SetZoneEntryPoint(ZoneB, 400, 500, 600);

            // Act
            _zoneConfig.Reset(ZoneA);

            // Assert
            Assert.IsNull(_zoneConfig.GetZoneCapacityOverride(ZoneA), "Instance A's capacity override must be cleared.");
            Assert.IsNull(_zoneConfig.GetZoneEntryPointOverride(ZoneA), "Instance A's entry-point override must be cleared.");

            Assert.AreEqual(25, _zoneConfig.GetZoneCapacityOverride(ZoneB), "Instance B's capacity override must survive Reset(A).");
            Assert.AreEqual(((short)400, (short)500, (short)600), _zoneConfig.GetZoneEntryPointOverride(ZoneB), "Instance B's entry-point override must survive Reset(A).");
        }

        // -----------------------------------------------------------------------
        // AC-TH-3: SetZoneEntryPoint round-trips through the internal accessor.
        // -----------------------------------------------------------------------

        [Test]
        public void SetZoneEntryPoint_RoundTrips()
        {
            // Arrange & Act
            _zoneConfig.SetZoneEntryPoint(ZoneA, posX: 100, posY: -50, posZ: 250);

            // Assert
            Assert.AreEqual(((short)100, (short)-50, (short)250), _zoneConfig.GetZoneEntryPointOverride(ZoneA));
        }

        // -----------------------------------------------------------------------
        // AC-TH-3: GetCurrentZoneState defaults to Empty when never configured.
        // -----------------------------------------------------------------------

        [Test]
        public void GetCurrentZoneState_NeverConfigured_DefaultsToEmpty()
        {
            // Act & Assert
            Assert.AreEqual(ZoneState.Empty, _zoneConfig.GetCurrentZoneState(ZoneA));
        }

        [Test]
        public void GetCurrentZoneState_AfterSetZoneStateForTesting_ReturnsOverriddenState()
        {
            // Arrange
            _zoneConfig.SetZoneStateForTesting(ZoneA, ZoneState.Draining);

            // Act & Assert
            Assert.AreEqual(ZoneState.Draining, _zoneConfig.GetCurrentZoneState(ZoneA));
        }

        // -----------------------------------------------------------------------
        // AC-TH-3: GetEntityPosition defaults to (0,0,0) when never set.
        // -----------------------------------------------------------------------

        [Test]
        public void GetEntityPosition_NeverSet_DefaultsToZero()
        {
            // Act & Assert
            Assert.AreEqual(((short)0, (short)0, (short)0), _zoneConfig.GetEntityPosition(SomeEntity));
        }

        [Test]
        public void GetEntityPosition_AfterSetZoneEntryPointUsedAsRespawn_IsNotConflatedWithEntityPosition()
        {
            // Arrange — setting a zone entry point must not be confused with an entity's own
            // position; the two are stored independently, keyed by different ID spaces.
            _zoneConfig.SetZoneEntryPoint(ZoneA, 10, 20, 30);

            // Act & Assert
            Assert.AreEqual(((short)0, (short)0, (short)0), _zoneConfig.GetEntityPosition(SomeEntity), "Entity position must remain the default until explicitly set for that entity.");
        }

        // -----------------------------------------------------------------------
        // AC-TH-3: SetLastBeatServerTick / SetClientOWL — OWL compensation
        // injection points. Store-and-expose round trip.
        // -----------------------------------------------------------------------

        [Test]
        public void SetLastBeatServerTick_RoundTrips()
        {
            // Arrange & Act
            _zoneConfig.SetLastBeatServerTick(SomeEntity, 12345u);

            // Assert
            Assert.AreEqual(12345u, _zoneConfig.GetLastBeatServerTick(SomeEntity));
        }

        [Test]
        public void GetLastBeatServerTick_NeverSet_DefaultsToZero()
        {
            // Act & Assert
            Assert.AreEqual(0u, _zoneConfig.GetLastBeatServerTick(SomeEntity));
        }

        [Test]
        public void SetClientOWL_RoundTrips()
        {
            // Arrange & Act
            _zoneConfig.SetClientOWL(SomeEntity, 0.075f);

            // Assert
            Assert.AreEqual(0.075f, _zoneConfig.GetClientOWL(SomeEntity), 0.0001f);
        }

        [Test]
        public void GetClientOWL_NeverSet_DefaultsToZero()
        {
            // Act & Assert
            Assert.AreEqual(0f, _zoneConfig.GetClientOWL(SomeEntity), 0.0001f);
        }
    }

    [TestFixture]
    internal sealed class NetworkingTestHarness_FactorySeam_Tests
    {
        // -----------------------------------------------------------------------
        // AC-TH-4: NetworkingTestHarness is this project's guarded "DI registration
        // call site" (no central DI container exists — ADR-010 forbids one). These
        // tests confirm the factory produces working instances of each interface.
        // -----------------------------------------------------------------------

        [Test]
        public void CreateTransportFaultInjector_ReturnsUsableInstance()
        {
            // Act
            ITransportFaultInjector injector = NetworkingTestHarness.CreateTransportFaultInjector();

            // Assert
            Assert.IsNotNull(injector);
            Assert.DoesNotThrow(() => injector.Reset());
        }

        [Test]
        public void CreateServerCrashInjector_ReturnsUsableInstance()
        {
            // Act
            IServerCrashInjector injector = NetworkingTestHarness.CreateServerCrashInjector();

            // Assert
            Assert.IsNotNull(injector);
            Assert.DoesNotThrow(() => injector.ClearRegistered());
        }

        [Test]
        public void CreateZoneTestConfigurator_ReturnsUsableInstance()
        {
            // Act
            IZoneTestConfigurator configurator = NetworkingTestHarness.CreateZoneTestConfigurator();

            // Assert
            Assert.IsNotNull(configurator);
            Assert.DoesNotThrow(() => configurator.Reset(1u));
        }
    }
}
