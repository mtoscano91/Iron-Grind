using System;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 022 — <see cref="LastBeatServerTickTracker"/>'s
    /// CR-OWL-1 data structure and CR-OWL-4 slot allocation contract. Covers all 5 blocking ACs
    /// (AC-OWL-03, AC-OWL-04, AC-OWL-06a, AC-OWL-06b, AC-OWL-06c) plus guard/precondition coverage
    /// for every public method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Honest AC-terminology scoping (see <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/>'s
    /// own doc comment):</b> AC-OWL-03/AC-OWL-04's GDD text uses "wrapCorrectionActive," but both
    /// hold <c>signedAdjusted &lt; 0</c> as a given precondition rather than exercising it. Every
    /// assertion in this file against <see cref="LastBeatServerTickTracker.IsWithinWrapCorrectionWindow"/>
    /// is exercising the CR-OWL-1 window sub-check in isolation, not the full F-OWL-1 formula
    /// (Story 023's job).
    /// </para>
    /// <para>
    /// AC-OWL-03's two sub-cases (<c>ServerTickNumber = 0</c> and <c>= 1</c>) are deliberately two
    /// separate <c>[Test]</c> methods, not one <c>[TestCase]</c>-parameterized test — collapsing them
    /// would hide which specific tick was exercised on failure, per this story's own instruction.
    /// </para>
    /// <para>
    /// AC-OWL-06a/AC-OWL-06c's "ghost period" is simulated by simply not calling
    /// <see cref="LastBeatServerTickTracker.DeallocatePlayerSlot"/> between allocation and the
    /// simulated reconnect — no mock zone session manager is needed for these two ACs specifically,
    /// since <see cref="LastBeatServerTickTracker"/> itself has no method that would reset the slot
    /// during that window (see class remarks on the production class). This is the same
    /// "test proves preservation via absence of a call" pattern the story text itself specifies.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class OwlCompensation_SlotAllocation_Tests
    {
        private const uint PlayerEntityId = 555u;

        // =========================================================================================
        // AC-OWL-03: sentinel slot (never called RecordBeat) -> wrapCorrectionWindow sub-check is
        // false at ServerTickNumber = 0 and = 1, explicitly, in separate sub-cases.
        // =========================================================================================

        [Test]
        public void SentinelSlot_ServerTickZero_WindowSubCheckIsFalse()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId); // never RecordBeat-ed -> sentinel

            // Act
            bool result = tracker.IsWithinWrapCorrectionWindow(slot, serverTickNumber: 0u,
                maxWrapWindowTicks: LastBeatServerTickTracker.MAX_WRAP_WINDOW_TICKS);

            // Assert
            Assert.IsFalse(result,
                "AC-OWL-03: sentinel guard must short-circuit before the subtraction is evaluated at ServerTickNumber=0.");
        }

        [Test]
        public void SentinelSlot_ServerTickOne_WindowSubCheckIsFalse()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId); // never RecordBeat-ed -> sentinel

            // Act
            bool result = tracker.IsWithinWrapCorrectionWindow(slot, serverTickNumber: 1u,
                maxWrapWindowTicks: LastBeatServerTickTracker.MAX_WRAP_WINDOW_TICKS);

            // Assert
            Assert.IsFalse(result,
                "AC-OWL-03: sentinel guard must short-circuit before the subtraction is evaluated at ServerTickNumber=1 " +
                "(the startup false-positive proof: 1 - uint.MaxValue = 2 <= 2 would otherwise be true).");
        }

        // =========================================================================================
        // AC-OWL-04: T+2 boundary is inclusive (true); T+3 is exclusive (false).
        // =========================================================================================

        [Test]
        public void RecentBeat_ExactlyTwoTicksLater_WindowSubCheckIsTrue_InclusiveBoundary()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            const uint beatTick = 400u;
            tracker.RecordBeat(slot, beatTick);

            // Act
            bool result = tracker.IsWithinWrapCorrectionWindow(slot, serverTickNumber: beatTick + 2, maxWrapWindowTicks: 2);

            // Assert
            Assert.IsTrue(result, "AC-OWL-04: T+2 is the inclusive boundary — must be true.");
        }

        [Test]
        public void RecentBeat_ExactlyThreeTicksLater_WindowSubCheckIsFalse_ExclusiveBoundary()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            const uint beatTick = 400u;
            tracker.RecordBeat(slot, beatTick);

            // Act
            bool result = tracker.IsWithinWrapCorrectionWindow(slot, serverTickNumber: beatTick + 3, maxWrapWindowTicks: 2);

            // Assert
            Assert.IsFalse(result, "AC-OWL-04: T+3 is one tick past the window — must be false.");
        }

        // =========================================================================================
        // AC-OWL-06a: player slot index and last-beat tick survive a simulated ghost-period
        // reconnect cycle (no DeallocatePlayerSlot call in between).
        // =========================================================================================

        [Test]
        public void PlayerSlot_SimulatedReconnectCycle_SlotIndexAndLastBeatTick_ArePreserved()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int allocatedSlot = tracker.AllocatePlayerSlot(PlayerEntityId);

            // Act — Beats fire during the ghost period (Disconnected_SessionActive -> Reconnecting
            // -> Connected); no DeallocatePlayerSlot call happens anywhere in this test.
            tracker.RecordBeat(allocatedSlot, serverTickNumber: 100u);
            tracker.RecordBeat(allocatedSlot, serverTickNumber: 101u);
            tracker.RecordBeat(allocatedSlot, serverTickNumber: 102u);

            // Assert — slot index identical before/after the simulated reconnect.
            bool found = tracker.TryGetPlayerSlot(PlayerEntityId, out int slotAfterReconnect);
            Assert.IsTrue(found);
            Assert.AreEqual(allocatedSlot, slotAfterReconnect,
                "AC-OWL-06a: slot index must be identical before and after the reconnect cycle.");

            // Assert — LastBeatServerTick retains the most recent ghost-period Beat's tick.
            Assert.IsTrue(tracker.TryGetLastBeatTick(allocatedSlot, out uint lastBeatTick));
            Assert.AreEqual(102u, lastBeatTick,
                "AC-OWL-06a: must retain the value from the most recent Beat fired during the ghost period.");
        }

        // =========================================================================================
        // AC-OWL-06b: mob slot sanitization invariant — reallocated slot reads uint.MaxValue at the
        // moment of reallocation, before any new RecordBeat call.
        // =========================================================================================

        [Test]
        public void MobSlot_DeallocateThenReallocate_ReadsSentinel_BeforeAnyNewRecordBeat()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int originalSlot = tracker.AllocateMobSlot();
            tracker.RecordBeat(originalSlot, serverTickNumber: 900u);

            // Act
            tracker.DeallocateMobSlot(originalSlot);
            int reallocatedSlot = tracker.AllocateMobSlot();

            // Assert
            Assert.IsTrue(tracker.TryGetLastBeatTick(reallocatedSlot, out uint lastBeatTick));
            Assert.AreEqual(uint.MaxValue, lastBeatTick,
                "AC-OWL-06b: the sanitization invariant must reset the slot to uint.MaxValue before reallocation.");
        }

        // =========================================================================================
        // AC-OWL-06c: reads during the ghost period reflect the most recent Beat, tick by tick.
        // =========================================================================================

        [Test]
        public void PlayerSlot_GhostPeriodBeatsFiringAtIncreasingTicks_ReadsAlwaysReflectMostRecent()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocatePlayerSlot(PlayerEntityId);
            uint[] ghostPeriodTicks = { 200u, 205u, 210u, 215u };

            // Act & Assert — after each Beat, the read must reflect that Beat's tick, not a stale or
            // reset value.
            foreach (uint tick in ghostPeriodTicks)
            {
                tracker.RecordBeat(slot, tick);

                Assert.IsTrue(tracker.TryGetLastBeatTick(slot, out uint lastBeatTick));
                Assert.AreEqual(tick, lastBeatTick,
                    $"AC-OWL-06c: read immediately after RecordBeat({tick}) must reflect tick {tick}, not a stale value.");
            }
        }

        // =========================================================================================
        // Guard/precondition coverage.
        // =========================================================================================

        [Test]
        public void Constructor_EverySlot_InitializesToSentinel()
        {
            // Arrange
            var tracker = new LastBeatServerTickTracker();

            // Act & Assert
            for (int slot = 0; slot < tracker.TotalSlotCount; slot++)
            {
                Assert.IsTrue(tracker.TryGetLastBeatTick(slot, out uint lastBeatTick));
                Assert.AreEqual(uint.MaxValue, lastBeatTick, $"slot {slot} must initialize to the sentinel.");
            }
        }

        [Test]
        public void TotalSlotCount_EqualsPlayerRegionPlusMobRegion()
        {
            var tracker = new LastBeatServerTickTracker();

            Assert.AreEqual(ZoneBufferPool.MAX_PLAYERS_PER_ZONE + LastBeatServerTickTracker.MAX_MOBS_PER_ZONE,
                tracker.TotalSlotCount);
        }

        [Test]
        public void AllocateMobSlot_FirstAllocation_ReturnsPlayerRegionBoundary()
        {
            var tracker = new LastBeatServerTickTracker();

            int firstMobSlot = tracker.AllocateMobSlot();

            Assert.AreEqual(ZoneBufferPool.MAX_PLAYERS_PER_ZONE, firstMobSlot,
                "The first mob slot must sit immediately after the player region.");
        }

        [Test]
        public void RecordBeat_NegativeSlot_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordBeat(-1, serverTickNumber: 1u));
        }

        [Test]
        public void RecordBeat_SlotAtOrBeyondTotalSlotCount_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordBeat(tracker.TotalSlotCount, serverTickNumber: 1u));
        }

        [Test]
        public void TryGetLastBeatTick_OutOfRangeSlot_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.TryGetLastBeatTick(-1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.TryGetLastBeatTick(tracker.TotalSlotCount, out _));
        }

        [Test]
        public void IsWithinWrapCorrectionWindow_OutOfRangeSlot_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.IsWithinWrapCorrectionWindow(-1, serverTickNumber: 1u, maxWrapWindowTicks: 2u));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.IsWithinWrapCorrectionWindow(tracker.TotalSlotCount, serverTickNumber: 1u, maxWrapWindowTicks: 2u));
        }

        [Test]
        public void AllocatePlayerSlot_DoubleAllocation_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            tracker.AllocatePlayerSlot(PlayerEntityId);

            Assert.Throws<InvalidOperationException>(() => tracker.AllocatePlayerSlot(PlayerEntityId));
        }

        [Test]
        public void AllocatePlayerSlot_AllSlotsOccupied_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            for (uint i = 0; i < ZoneBufferPool.MAX_PLAYERS_PER_ZONE; i++)
            {
                tracker.AllocatePlayerSlot(i);
            }

            Assert.Throws<InvalidOperationException>(() => tracker.AllocatePlayerSlot((uint)ZoneBufferPool.MAX_PLAYERS_PER_ZONE));
        }

        [Test]
        public void DeallocatePlayerSlot_NeverAllocated_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<InvalidOperationException>(() => tracker.DeallocatePlayerSlot(PlayerEntityId));
        }

        [Test]
        public void DeallocatePlayerSlot_AlreadyDeallocated_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            tracker.AllocatePlayerSlot(PlayerEntityId);
            tracker.DeallocatePlayerSlot(PlayerEntityId);

            Assert.Throws<InvalidOperationException>(() => tracker.DeallocatePlayerSlot(PlayerEntityId));
        }

        [Test]
        public void TryGetPlayerSlot_UnallocatedPlayer_ReturnsFalse()
        {
            var tracker = new LastBeatServerTickTracker();
            bool found = tracker.TryGetPlayerSlot(PlayerEntityId, out int slot);

            Assert.IsFalse(found);
            Assert.AreEqual(0, slot);
        }

        [Test]
        public void AllocateMobSlot_RegionExhausted_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            for (int i = 0; i < LastBeatServerTickTracker.MAX_MOBS_PER_ZONE; i++)
            {
                tracker.AllocateMobSlot();
            }

            Assert.Throws<InvalidOperationException>(() => tracker.AllocateMobSlot());
        }

        [Test]
        public void DeallocateMobSlot_BelowMobRegion_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.DeallocateMobSlot(0)); // 0 is a player slot
        }

        [Test]
        public void DeallocateMobSlot_AtOrBeyondTotalSlotCount_ThrowsArgumentOutOfRangeException()
        {
            var tracker = new LastBeatServerTickTracker();
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.DeallocateMobSlot(tracker.TotalSlotCount));
        }

        [Test]
        public void DeallocateMobSlot_DoubleFree_ThrowsInvalidOperationException()
        {
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocateMobSlot();
            tracker.DeallocateMobSlot(slot);

            Assert.Throws<InvalidOperationException>(() => tracker.DeallocateMobSlot(slot));
        }

        [Test]
        public void DeallocateMobSlot_NeverAllocatedSlot_ThrowsInvalidOperationException()
        {
            // Code review finding: DeallocateMobSlot must reject an in-range mob slot that was
            // never returned by AllocateMobSlot at all (MobSlotState.NeverAllocated), symmetric
            // with DeallocatePlayerSlot's equivalent guard — otherwise the slot is pushed onto the
            // free list early and could later be handed out to two different mobs simultaneously
            // once the fresh-index counter independently reaches the same index.
            var tracker = new LastBeatServerTickTracker();
            int neverAllocatedMobSlot = ZoneBufferPool.MAX_PLAYERS_PER_ZONE; // in-range, but AllocateMobSlot was never called

            Assert.Throws<InvalidOperationException>(() => tracker.DeallocateMobSlot(neverAllocatedMobSlot));
        }

        // =========================================================================================
        // QA-suggested tests (code review, approved as-is).
        // =========================================================================================

        [Test]
        public void AllocateMobSlot_WithMultipleFreedSlotsInterleaved_ReusesInFreedOrder()
        {
            // Locks in the Queue<int> FIFO reuse order (previously unlocked/undocumented behavior):
            // allocate A, B, C in order; free A then B (C stays in use); the next two allocations
            // must return A then B, in the order they were freed.
            var tracker = new LastBeatServerTickTracker();
            int slotA = tracker.AllocateMobSlot();
            int slotB = tracker.AllocateMobSlot();
            int slotC = tracker.AllocateMobSlot();

            tracker.DeallocateMobSlot(slotA);
            tracker.DeallocateMobSlot(slotB);

            int firstReallocated = tracker.AllocateMobSlot();
            int secondReallocated = tracker.AllocateMobSlot();

            Assert.AreEqual(slotA, firstReallocated, "The first slot freed (A) must be the first reused.");
            Assert.AreEqual(slotB, secondReallocated, "The second slot freed (B) must be the second reused.");
            Assert.IsTrue(tracker.TryGetLastBeatTick(slotC, out _), "slotC must remain untouched (still in use).");
        }

        [Test]
        public void DeallocateMobSlot_LastPlayerSlotIndex_ThrowsArgumentOutOfRangeException()
        {
            // True adjacent-boundary case: the existing "below mob region" test uses slot=0, which
            // is far from the real boundary. This asserts the last valid player-region index (one
            // below the mob region's start) is still correctly rejected.
            var tracker = new LastBeatServerTickTracker();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                tracker.DeallocateMobSlot(ZoneBufferPool.MAX_PLAYERS_PER_ZONE - 1));
        }

        [Test]
        public void RecordBeat_OnFreedMobSlot_DoesNotThrowAndSilentlyOverwritesSentinel()
        {
            // Documents RecordBeat's lack of an ownership/state guard as a known, accepted
            // fragility (matching its doc comment's "raw API" framing) — this is not something
            // this story fixes; this test locks in current behavior so a future change to it is a
            // deliberate decision, not an accidental regression.
            var tracker = new LastBeatServerTickTracker();
            int slot = tracker.AllocateMobSlot();
            tracker.DeallocateMobSlot(slot); // sentinel-reset, returned to the free list

            // Act — RecordBeat is called directly on the freed slot, without reallocating it first.
            Assert.DoesNotThrow(() => tracker.RecordBeat(slot, serverTickNumber: 777u));

            // The slot is then reallocated (the free list returns this same index) and the
            // sentinel invariant is now violated — RecordBeat has no ownership/state guard to
            // prevent this.
            int reallocatedSlot = tracker.AllocateMobSlot();
            Assert.AreEqual(slot, reallocatedSlot, "Sanity check: the free list must return the same slot.");

            Assert.IsTrue(tracker.TryGetLastBeatTick(reallocatedSlot, out uint lastBeatTick));
            Assert.AreNotEqual(uint.MaxValue, lastBeatTick,
                "Known fragility: RecordBeat on a freed-but-not-yet-reallocated slot silently overwrites the sentinel.");
        }
    }
}
