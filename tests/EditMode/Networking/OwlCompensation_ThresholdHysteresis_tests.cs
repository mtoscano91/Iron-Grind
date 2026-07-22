using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 024 —
    /// <see cref="OwlThresholdHysteresisTracker.EvaluateOwlSample"/>'s CR-NET-8.3 hysteresis band.
    /// Covers AC-NC-31-HYSTERESIS's full trajectory, in-band silence, the first-sample-silent
    /// baseline rule, exact boundary values (with the float-precision proof from the class's own
    /// remarks), per-entity isolation, <see cref="OwlThresholdHysteresisTracker.TryGetCompensationState"/>,
    /// and the observer-omitted path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Boundary tests double as the float-precision proof:</b>
    /// <see cref="EnterThreshold_ExactLiteral0135_Triggers_FloatPrecisionProof"/> and
    /// <see cref="ExitThreshold_ExactLiteral0105_DoesNotTrigger_FloatPrecisionProof"/> exist
    /// specifically because <c>0.12f + 0.015f</c> (the computed <c>enterThreshold</c>) is one bit
    /// below the literal <c>0.135f</c>, while <c>0.12f - 0.015f</c> (the computed <c>exitThreshold</c>)
    /// is bit-identical to the literal <c>0.105f</c> — see the production class's remarks for the
    /// full trace. These are not flaky boundary tests; they assert the exact, deterministic IEEE-754
    /// behavior of these specific tuning-knob values.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class OwlCompensation_ThresholdHysteresis_Tests
    {
        private const uint EntityId = 555u;
        private const uint OtherEntityId = 777u;

        // =========================================================================================
        // AC-NC-31-HYSTERESIS — full trajectory: 80ms -> 150ms -> drop below 105ms.
        // =========================================================================================

        [Test]
        public void FullTrajectory_80To150ThenBelow105_EmitsExactlyTwice_InOrder_WithCorrectValues()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.08f, observer);   // baseline, stays true, silent
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f, observer);   // crosses 135ms entry -> flips false
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.09f, observer);   // drops below 105ms exit -> flips true

            // Assert
            Assert.AreEqual(2, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "AC-NC-31-HYSTERESIS: exactly two ConnectionQualityUpdate emissions across the trajectory.");
            Assert.AreEqual((EntityId, false), observer.ConnectionQualityUpdateEmittedCalls[0],
                "First emission: OWL rose above 135ms entry threshold -> compensation OFF.");
            Assert.AreEqual((EntityId, true), observer.ConnectionQualityUpdateEmittedCalls[1],
                "Second emission: OWL fell below 105ms exit threshold -> compensation back ON.");
        }

        // =========================================================================================
        // In-band trajectory produces zero emissions.
        // =========================================================================================

        [Test]
        public void InBandTrajectory_110Then125Then130_ProducesZeroEmissions()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act -- all three samples sit within the 105-135ms band; entity starts at the default
            // true/compensated state, which none of these samples leave.
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.110f, observer);
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.125f, observer);
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.130f, observer);

            // Assert
            Assert.AreEqual(0, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "In-band OWL fluctuation must never emit ConnectionQualityUpdate (hysteresis).");
        }

        // =========================================================================================
        // First-sample-for-an-entity does not emit, when it doesn't cross the threshold.
        // =========================================================================================

        [Test]
        public void FirstSampleForEntity_WithinDefaultBand_DoesNotEmit()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act -- brand-new entityId, single call, value would not cross enterThreshold relative
            // to the default true baseline.
            bool active = tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.08f, observer);

            // Assert
            Assert.IsTrue(active, "Default baseline is compensation-active (true).");
            Assert.AreEqual(0, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "Establishing the initial baseline is silent when it doesn't cross a threshold.");
        }

        /// <summary>
        /// Dedicated coverage for the approved "a first sample can still emit, if it itself crosses
        /// the threshold" design decision documented in
        /// <see cref="OwlThresholdHysteresisTracker"/>'s own class remarks. Previously this behavior
        /// was only incidentally exercised inside the per-entity-isolation test, whose name gave no
        /// signal it was the sole coverage of this specific approved decision -- a future edit to
        /// that test could have silently deleted the only proof of it. This test's only job is to
        /// prove: brand-new entity, one call, value already above the entry threshold -> the
        /// implicit default-true baseline IS a real transition and DOES emit.
        /// </summary>
        [Test]
        public void FirstSampleForEntity_AboveEntryThreshold_EmitsImmediately()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act -- the ONLY call ever made for this entity; its first sample already exceeds the
            // entry threshold, so the true(default) -> false transition is genuine and must emit.
            bool active = tracker.EvaluateOwlSample(OtherEntityId, owlSeconds: 0.15f, observer);

            // Assert
            Assert.IsFalse(active, "First-ever sample already exceeds the entry threshold -> OFF.");
            Assert.AreEqual(1, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "The default-to-degraded transition on a first sample must be reported -- it's the " +
                "only mechanism that ever informs a client whose first-ever OWL reading is already bad.");
            Assert.AreEqual((OtherEntityId, false), observer.ConnectionQualityUpdateEmittedCalls[0]);
        }

        // =========================================================================================
        // Exact boundary values -- strict '>' / '<', proven via float precision (see class remarks).
        // =========================================================================================

        [Test]
        public void EnterThreshold_ExactLiteral0135_Triggers_FloatPrecisionProof()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.05f, observer); // silent baseline, stays true

            // Act -- the literal 0.135f (0.135000005f) is one bit ABOVE the computed enterThreshold
            // (0.12f + 0.015f = 0.13499999f), so strict '>' evaluates true here.
            bool active = tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.135f, observer);

            // Assert
            Assert.IsFalse(active, "Literal 0.135f exceeds the computed enterThreshold under strict '>'.");
            Assert.AreEqual(1, observer.ConnectionQualityUpdateEmittedCalls.Count);
            Assert.AreEqual((EntityId, false), observer.ConnectionQualityUpdateEmittedCalls[0]);
        }

        /// <summary>
        /// Mutation-testing hole closer: <see cref="EnterThreshold_ExactLiteral0135_Triggers_FloatPrecisionProof"/>
        /// uses the literal <c>0.135f</c>, which is one ULP ABOVE the computed <c>enterThreshold</c>
        /// (<c>0.12f + 0.015f = 0.13499999f</c>) -- so that test cannot distinguish strict
        /// <c>&gt;</c> from <c>&gt;=</c>. This test computes the threshold directly and asserts
        /// equality does NOT trigger, which is the only way to catch a <c>&gt;=</c>-instead-of-<c>&gt;</c>
        /// mutation at the entry boundary. Structural mirror of
        /// <see cref="ExitThreshold_ExactLiteral0105_DoesNotTrigger_FloatPrecisionProof"/>.
        /// </summary>
        [Test]
        public void EnterThreshold_ExactComputedValue_DoesNotTrigger_FloatPrecisionProof()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.05f, observer); // silent baseline, stays true

            float enterThreshold = OwlThresholdHysteresisTracker.MAX_COMPENSATABLE_OWL_SECONDS
                + OwlThresholdHysteresisTracker.OWL_HYSTERESIS_BAND_SECONDS;

            // Act -- exactly the computed threshold value: equal is not greater than.
            bool active = tracker.EvaluateOwlSample(EntityId, owlSeconds: enterThreshold, observer);

            // Assert
            Assert.IsTrue(active, "owlSeconds == enterThreshold is not '>' enterThreshold -- stays ON.");
            Assert.AreEqual(0, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "No emission -- the entry threshold was not crossed strictly.");
        }

        [Test]
        public void ExitThreshold_ExactLiteral0105_DoesNotTrigger_FloatPrecisionProof()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.05f, observer);  // silent baseline, stays true
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.20f, observer); // well above entry -> flips false (1 emission so far)

            // Act -- the literal 0.105f (0.104999997f) is bit-identical to the computed exitThreshold
            // (0.12f - 0.015f = 0.104999997f), so strict '<' evaluates false here (equal, not less).
            bool active = tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.105f, observer);

            // Assert
            Assert.IsFalse(active, "Literal 0.105f equals, but is not less than, the computed exitThreshold.");
            Assert.AreEqual(1, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "No additional emission — the exit threshold was not crossed strictly.");
        }

        // =========================================================================================
        // Per-entity isolation.
        // =========================================================================================

        [Test]
        public void TwoEntities_IndependentlyTracked_OneFlippingDoesNotAffectTheOther()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f, observer);       // flips EntityId to false
            tracker.EvaluateOwlSample(OtherEntityId, owlSeconds: 0.08f, observer);  // OtherEntityId stays true

            // Assert
            Assert.IsTrue(tracker.TryGetCompensationState(EntityId, out bool entityActive));
            Assert.IsFalse(entityActive, "EntityId crossed the entry threshold and flipped OFF.");

            Assert.IsTrue(tracker.TryGetCompensationState(OtherEntityId, out bool otherActive));
            Assert.IsTrue(otherActive, "OtherEntityId's sample never crossed a threshold -- stays at default ON.");

            Assert.AreEqual(1, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "Only EntityId's flip should have emitted.");
            Assert.AreEqual((EntityId, false), observer.ConnectionQualityUpdateEmittedCalls[0]);
        }

        // =========================================================================================
        // Idempotency -- a repeated identical sample must not re-emit.
        // =========================================================================================

        /// <summary>
        /// Guards against a plausible mutation where the flip check compares the new sample to the
        /// previous sample <i>value</i> rather than to the previous <i>mode</i> -- which would
        /// re-emit on every repeated call once already flipped, instead of only on an actual
        /// ON&#8596;OFF mode change.
        /// </summary>
        [Test]
        public void RepeatedIdenticalSample_WhileAlreadyFlipped_DoesNotReemit()
        {
            // Arrange
            var tracker = new OwlThresholdHysteresisTracker();
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f, observer); // flips to false, 1 emission

            // Act -- repeat the identical sample while already flipped.
            bool active = tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f, observer);

            // Assert
            Assert.IsFalse(active, "Mode remains OFF -- unchanged by the repeat.");
            Assert.AreEqual(1, observer.ConnectionQualityUpdateEmittedCalls.Count,
                "No second emission -- the mode did not change on the repeated call.");
        }

        // =========================================================================================
        // TryGetCompensationState.
        // =========================================================================================

        [Test]
        public void TryGetCompensationState_NeverSampledEntity_ReturnsFalse()
        {
            var tracker = new OwlThresholdHysteresisTracker();

            bool found = tracker.TryGetCompensationState(EntityId, out bool compensationActive);

            Assert.IsFalse(found, "An entity with no recorded EvaluateOwlSample call has never been tracked.");
            Assert.IsFalse(compensationActive, "Default `out` value for the not-found case is false, not the true baseline.");
        }

        [Test]
        public void TryGetCompensationState_AfterSample_ReturnsTrueWithCurrentValue()
        {
            var tracker = new OwlThresholdHysteresisTracker();
            tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f); // flips to false

            bool found = tracker.TryGetCompensationState(EntityId, out bool compensationActive);

            Assert.IsTrue(found);
            Assert.IsFalse(compensationActive, "Reflects the current (post-flip) mode, not the original default.");
        }

        // =========================================================================================
        // Observer-omitted path.
        // =========================================================================================

        [Test]
        public void EvaluateOwlSample_ObserverOmitted_DoesNotThrow_EvenAcrossAFlip()
        {
            var tracker = new OwlThresholdHysteresisTracker();

            Assert.DoesNotThrow(() =>
            {
                tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.08f);
                tracker.EvaluateOwlSample(EntityId, owlSeconds: 0.15f); // a real flip, no observer supplied
            });
        }
    }
}
