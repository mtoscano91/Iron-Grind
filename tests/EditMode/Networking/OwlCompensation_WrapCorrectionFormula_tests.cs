using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 023 — <see cref="OwlWrapCorrectionFormula.Evaluate"/>'s
    /// CR-OWL-2/F-OWL-1 formula. Covers AC-OWL-01, AC-OWL-02, AC-OWL-05, the AC-NC-29 cross-reference,
    /// the strict-`&gt;` grace-threshold boundary, and guard/precondition coverage. Every test uses a
    /// real <see cref="LastBeatServerTickTracker"/> instance (Story 022) — not a mock — since that
    /// composition is the actual production dependency this formula relies on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AC-NC-29 is not a separate test:</b> the root GDD's own Dependencies section says AC-NC-29
    /// "See <c>networking-owl-compensation.md</c> AC-OWL-01 and AC-OWL-02" — this file's
    /// <see cref="AC01_WrapCase_WrapCorrectionActive_AdjustedTimer_GraceTriggers"/> and
    /// <see cref="AC02_NoRecentBeat_OutsideWindow_NoWrapCorrection_NoGraceTrigger"/> already exercise
    /// the identical wrap-vs-no-recent-Beat contrast verbatim; writing a third duplicate test would
    /// add no new coverage.
    /// </para>
    /// <para>
    /// <b>AC-OWL-05 honest scoping:</b> the story's own AC-OWL-05 text says "when the server
    /// evaluates a <c>NotifySkillUsed</c> RPC" — but no RPC handler exists yet (explicitly out of
    /// scope; future Auto-Attack Combat epic). <see cref="AC05_ObserverSupplied_RecordsSingleCall_WithCorrectValues"/>
    /// proves the formula-to-observer wiring directly (<see cref="OwlWrapCorrectionFormula.Evaluate"/>
    /// calling <see cref="INetworkTestObserver.OnSkillGraceWindowEvaluated"/>), not a real
    /// RPC-triggered path — same honest-scoping idiom used throughout this epic's prior stories.
    /// </para>
    /// <para>
    /// The GDD's illustrative "slot 5"/"tick 400/401" values are not load-bearing — any slot/tick
    /// pair with the same 1-tick delta proves the same thing. These tests use whatever
    /// <see cref="LastBeatServerTickTracker.AllocatePlayerSlot"/> naturally returns.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class OwlCompensation_WrapCorrectionFormula_Tests
    {
        private const uint PlayerEntityId = 555u;
        private const float FloatTolerance = 1e-5f;

        // =========================================================================================
        // AC-OWL-01 / AC-NC-29 (wrap case)
        // =========================================================================================

        [Test]
        public void AC01_WrapCase_WrapCorrectionActive_AdjustedTimer_GraceTriggers()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            const uint beatTick = 400u;
            tracker.RecordBeat(slot, beatTick);

            // Act
            SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: beatTick + 1, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId);

            // Assert
            Assert.IsTrue(result.WrapCorrectionActive, "AC-OWL-01: 1 tick since the Beat, within the 2-tick window.");
            Assert.AreEqual(0.97f, result.AdjustedCycleTimer, FloatTolerance, "AC-OWL-01: 1.0 + (0.02 - 0.05) = 0.97.");
            Assert.IsTrue(result.GraceTriggers, "AC-OWL-01: 0.97 > 0.92 -> skill triggers.");
        }

        // =========================================================================================
        // AC-OWL-02 / AC-NC-29 (no-recent-Beat / outside-window case)
        // =========================================================================================

        [Test]
        public void AC02_NoRecentBeat_OutsideWindow_NoWrapCorrection_NoGraceTrigger()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            const uint currentTick = 401u;
            tracker.RecordBeat(slot, currentTick - 21); // 21 ticks ago -- outside the 2-tick window

            // Act
            SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: currentTick, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId);

            // Assert
            Assert.IsFalse(result.WrapCorrectionActive, "AC-OWL-02: 21 ticks since the Beat, outside the 2-tick window.");
            Assert.AreEqual(0f, result.AdjustedCycleTimer, FloatTolerance, "AC-OWL-02: max(0.02 - 0.05, 0) = 0.");
            Assert.IsFalse(result.GraceTriggers, "AC-OWL-02: 0 > 0.92 is false -> no grace trigger.");
        }

        // =========================================================================================
        // AC-OWL-05 (observer wiring proof -- see class remarks for honest scoping note)
        // =========================================================================================

        [Test]
        public void AC05_ObserverSupplied_RecordsSingleCall_WithCorrectValues()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            const uint beatTick = 400u;
            tracker.RecordBeat(slot, beatTick);
            var observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();

            // Act
            OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: beatTick + 1, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId, observer);

            // Assert
            Assert.AreEqual(1, observer.SkillGraceWindowEvaluatedCalls.Count,
                "AC-OWL-05: exactly one OnSkillGraceWindowEvaluated call.");
            var (entityId, adjustedTimer, graceTriggers) = observer.SkillGraceWindowEvaluatedCalls[0];
            Assert.AreEqual(PlayerEntityId, entityId);
            Assert.AreEqual(0.97f, adjustedTimer, FloatTolerance);
            Assert.IsTrue(graceTriggers);
        }

        // =========================================================================================
        // Grace-threshold boundary -- strict ">" per F-OWL-1's own worked example.
        // =========================================================================================

        [Test]
        public void Evaluate_AdjustedTimerExactlyAtGraceThreshold_GraceTriggersIsFalse_StrictComparison()
        {
            // Arrange -- no wrap needed; signedAdjusted lands exactly at baseGraceThreshold * cycleDuration.
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);

            // Act
            SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: 100u, maxWrapWindowTicks: 2u,
                cycleTimer: 0.97f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId);

            // Assert
            Assert.IsFalse(result.WrapCorrectionActive, "signedAdjusted = 0.92 > 0 -> no wrap needed.");
            Assert.AreEqual(0.92f, result.AdjustedCycleTimer, FloatTolerance);
            Assert.IsFalse(result.GraceTriggers, "0.92 > 0.92 is false -- '>' is strict, exact threshold does not trigger.");
        }

        [Test]
        public void Evaluate_AdjustedTimerJustAboveGraceThreshold_GraceTriggersIsTrue()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);

            // Act
            SkillGraceWindowResult result = OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: 100u, maxWrapWindowTicks: 2u,
                cycleTimer: 0.98f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId);

            // Assert
            Assert.AreEqual(0.93f, result.AdjustedCycleTimer, FloatTolerance);
            Assert.IsTrue(result.GraceTriggers, "0.93 > 0.92 -> skill triggers.");
        }

        // =========================================================================================
        // Guard/precondition coverage.
        // =========================================================================================

        [Test]
        public void Evaluate_ObserverOmitted_DoesNotThrow()
        {
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            tracker.RecordBeat(slot, 400u);

            Assert.DoesNotThrow(() => OwlWrapCorrectionFormula.Evaluate(
                tracker, slot, serverTickNumber: 401u, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId));
        }

        [Test]
        public void Evaluate_NullTracker_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => OwlWrapCorrectionFormula.Evaluate(
                null, slot: 0, serverTickNumber: 1u, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId));
        }

        [Test]
        public void Evaluate_OutOfRangeSlot_ThrowsArgumentOutOfRangeException()
        {
            // Delegated to LastBeatServerTickTracker.IsWithinWrapCorrectionWindow -- this test
            // proves the composition surfaces that guard rather than swallowing or duplicating it.
            var tracker = new LastBeatServerTickTracker();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => OwlWrapCorrectionFormula.Evaluate(
                tracker, slot: -1, serverTickNumber: 1u, maxWrapWindowTicks: 2u,
                cycleTimer: 0.02f, owlSeconds: 0.05f, cycleDuration: 1.0f, baseGraceThreshold: 0.92f,
                entityId: PlayerEntityId));
        }
    }
}
