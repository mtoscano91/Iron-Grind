using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 001 — the zone lifecycle state machine
    /// (<see cref="ZoneLifecycle"/>, CR-ZI-2, T-1 to T-7). Covers AC-ZI-21 (idle close), AC-ZI-24
    /// (second quit), AC-ZI-25 (quit with no slot), AC-ZI-26 (quit while Loading), T-3 load timeout,
    /// T-4/T-5 drain, the one-close guarantee and the no-return rule. Ticks are advanced
    /// programmatically; no wall-clock time is used.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_Lifecycle_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const int IDLE_SECONDS = 60;
        private const int GRACE_SECONDS = 60;
        private const int LOAD_SECONDS = 30;
        private const uint IDLE_TICKS = IDLE_SECONDS * HZ;   // 1200
        private const uint GRACE_TICKS = GRACE_SECONDS * HZ; // 1200
        private const uint LOAD_TICKS = LOAD_SECONDS * HZ;   // 600

        private sealed class FakeListener : IZoneLifecycleListener
        {
            public readonly List<uint> DrainingTicks = new List<uint>();
            public readonly List<(ZoneCloseTransition Transition, uint Tick)> Closed = new List<(ZoneCloseTransition, uint)>();
            public readonly List<string> Calls = new List<string>();

            public void OnDraining(uint serverTick)
            {
                DrainingTicks.Add(serverTick);
                Calls.Add("Draining@" + serverTick);
            }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick)
            {
                Closed.Add((transition, serverTick));
                Calls.Add("Closed:" + transition + "@" + serverTick);
            }
        }

        private static ZoneLifecycle Create(out FakeListener listener, out NetworkTestObserver observer, uint loadingStart = 0u)
        {
            listener = new FakeListener();
            observer = new NetworkTestObserver();
            return new ZoneLifecycle(new ZoneID(ZONE_RAW), listener, loadingStart,
                IDLE_SECONDS, GRACE_SECONDS, LOAD_SECONDS, HZ, observer);
        }

        private static ZoneLifecycle CreateActive(out FakeListener listener, out NetworkTestObserver observer, uint activeTick = 0u)
        {
            ZoneLifecycle lifecycle = Create(out listener, out observer);
            lifecycle.MarkActive(activeTick);
            return lifecycle;
        }

        // ----- Construction ---------------------------------------------------------------------

        [Test]
        public void Constructor_Default_StartsLoadingAndDoesNotAcceptPlayers()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);

            Assert.AreEqual(ZoneState.Loading, lifecycle.State);
            Assert.IsFalse(lifecycle.AcceptsPlayers);
            Assert.AreEqual(0, lifecycle.OccupiedPlayerSlots);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void Constructor_GddDefaults_MatchKnobValues()
        {
            Assert.AreEqual(120, ZoneLifecycle.ZONE_IDLE_CLOSE_SECONDS);
            Assert.AreEqual(60, ZoneLifecycle.ZONE_DRAIN_GRACE_SECONDS);
            Assert.AreEqual(30, ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS);
        }

        [Test]
        public void Constructor_NullListener_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneLifecycle(new ZoneID(ZONE_RAW), null, 0u));
        }

        [Test]
        public void Constructor_InvalidZone_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ZoneLifecycle(ZoneID.Invalid, new FakeListener(), 0u));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_NonPositiveIdleSeconds_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneLifecycle(new ZoneID(ZONE_RAW), new FakeListener(), 0u, idleCloseSeconds: value));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_NonPositiveDrainGrace_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneLifecycle(new ZoneID(ZONE_RAW), new FakeListener(), 0u, drainGraceSeconds: value));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_NonPositiveLoadTimeout_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneLifecycle(new ZoneID(ZONE_RAW), new FakeListener(), 0u, loadTimeoutSeconds: value));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_NonPositiveTickRate_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneLifecycle(new ZoneID(ZONE_RAW), new FakeListener(), 0u, tickRateHz: value));
        }

        // ----- T-2 / T-3 ------------------------------------------------------------------------

        [Test]
        public void MarkActive_FromLoading_BecomesActiveAndAcceptsPlayers()
        {
            ZoneLifecycle lifecycle = Create(out _, out NetworkTestObserver observer);

            lifecycle.MarkActive(10u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.IsTrue(lifecycle.AcceptsPlayers);
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZONE_RAW, ZoneState.Loading, ZoneState.Active), observer.ZoneStateTransitionedCalls[0]);
        }

        [Test]
        public void OnTick_LoadingForTimeoutTicks_ClosesWithLoadFailed()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);

            lifecycle.OnTick(LOAD_TICKS - 1u);
            Assert.AreEqual(ZoneState.Loading, lifecycle.State);

            lifecycle.OnTick(LOAD_TICKS);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.LoadFailed, LOAD_TICKS), listener.Closed[0]);
        }

        [Test]
        public void OnTick_LoadTimeoutCountsFromLoadingStartTick()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _, loadingStart: 100u);

            lifecycle.OnTick(100u + LOAD_TICKS - 1u);
            Assert.AreEqual(ZoneState.Loading, lifecycle.State);

            lifecycle.OnTick(100u + LOAD_TICKS);

            Assert.AreEqual((ZoneCloseTransition.LoadFailed, 100u + LOAD_TICKS), listener.Closed[0]);
        }

        [Test]
        public void MarkActive_AtTickBeforeLoadTimeout_PreventsLoadFailure()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u); // keep the idle timer out of the picture

            lifecycle.MarkActive(LOAD_TICKS - 1u);
            lifecycle.OnTick(LOAD_TICKS);
            lifecycle.OnTick(LOAD_TICKS * 10u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void MarkInitFailed_FromLoading_ClosesWithLoadFailed()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out NetworkTestObserver observer);

            lifecycle.MarkInitFailed(5u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.LoadFailed, 5u), listener.Closed[0]);
            Assert.AreEqual((ZONE_RAW, ZoneState.Loading, ZoneState.Closed), observer.ZoneStateTransitionedCalls[0]);
        }

        [Test]
        public void MarkActive_AfterInitFailed_IsNoOpAndNeverActive()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);
            lifecycle.MarkInitFailed(5u);
            LogAssert.Expect(LogType.Warning, new Regex("ZoneLifecycle"));

            lifecycle.MarkActive(6u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
        }

        // ----- T-7 idle close (AC-ZI-21) --------------------------------------------------------

        [Test]
        public void OnTick_LastSlotReleasedThenIdleTicksElapse_ClosesExactlyOnceWithIdle()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 10u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 100u);

            lifecycle.OnTick(100u + IDLE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);

            lifecycle.OnTick(100u + IDLE_TICKS);
            lifecycle.OnTick(100u + IDLE_TICKS + 1u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Idle, 100u + IDLE_TICKS), listener.Closed[0]);
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_AllocationOneTickBeforeIdleExpiry_CancelsClose()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 0u);

            lifecycle.OnTick(IDLE_TICKS - 1u);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, IDLE_TICKS - 1u);
            lifecycle.OnTick(IDLE_TICKS);
            lifecycle.OnTick(IDLE_TICKS * 5u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_ReleaseAfterCancelledTimer_RestartsFromNewRelease()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 10u);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 500u); // cancels
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 800u); // restarts at 800

            lifecycle.OnTick(10u + IDLE_TICKS); // old deadline: must not fire
            Assert.AreEqual(ZoneState.Active, lifecycle.State);

            lifecycle.OnTick(800u + IDLE_TICKS);

            Assert.AreEqual((ZoneCloseTransition.Idle, 800u + IDLE_TICKS), listener.Closed[0]);
        }

        [Test]
        public void OnTick_ActiveWithNoSlotSinceMarkActive_ClosesAfterIdleTicksFromActivation()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _, activeTick: 50u);

            lifecycle.OnTick(50u + IDLE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Active, lifecycle.State);

            lifecycle.OnTick(50u + IDLE_TICKS);

            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Idle, 50u + IDLE_TICKS), listener.Closed[0]);
        }

        [Test]
        public void OnTick_ActiveWithOccupiedSlot_NeverClosesIdle()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(2, 1u);

            lifecycle.OnTick(IDLE_TICKS * 10u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_ActiveCountChangeWhileEmpty_DoesNotRestartIdleTimer()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);

            lifecycle.OnOccupiedPlayerSlotsChanged(0, 400u); // already empty; timer keeps running from tick 0
            lifecycle.OnTick(IDLE_TICKS);

            Assert.AreEqual((ZoneCloseTransition.Idle, IDLE_TICKS), listener.Closed[0]);
        }

        // ----- T-4 / T-5 ------------------------------------------------------------------------

        [Test]
        public void RequestQuit_ActiveWithSlots_DrainsAndNotifiesListenerOnce()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out NetworkTestObserver observer);
            lifecycle.OnOccupiedPlayerSlotsChanged(2, 1u);

            lifecycle.RequestQuit(100u);

            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            Assert.IsFalse(lifecycle.AcceptsPlayers);
            Assert.AreEqual(1, listener.DrainingTicks.Count);
            Assert.AreEqual(100u, listener.DrainingTicks[0]);
            Assert.AreEqual(0, listener.Closed.Count);
            Assert.AreEqual((ZONE_RAW, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[1]);
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_DrainingLastSlotReleasedBeforeGrace_ClosesDrainedOnThatTick()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(2, 1u);
            lifecycle.RequestQuit(100u);

            lifecycle.OnOccupiedPlayerSlotsChanged(1, 120u);
            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 140u);
            lifecycle.OnTick(100u + GRACE_TICKS);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Drained, 140u), listener.Closed[0]);
        }

        [Test]
        public void OnTick_DrainingWithSlotUntilGraceExpires_ClosesDrainedAtGraceDeadline()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 1u);
            lifecycle.RequestQuit(0u);

            lifecycle.OnTick(GRACE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Draining, lifecycle.State);

            lifecycle.OnTick(GRACE_TICKS);

            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Drained, GRACE_TICKS), listener.Closed[0]);
        }

        [Test]
        public void RequestQuit_ActiveWithNoSlots_DrainsThenClosesDrainedOnSameTick_AC_ZI_25()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out NetworkTestObserver observer);

            lifecycle.RequestQuit(77u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            CollectionAssert.AreEqual(new[] { "Draining@77", "Closed:Drained@77" }, listener.Calls);
            Assert.AreEqual(3, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZONE_RAW, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[1]);
            Assert.AreEqual((ZONE_RAW, ZoneState.Draining, ZoneState.Closed), observer.ZoneStateTransitionedCalls[2]);
        }

        [Test]
        public void RequestQuit_ActiveWithIdleTimerArmed_CancelsIdleTimer()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 0u); // idle armed, deadline IDLE_TICKS
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 5u);
            lifecycle.RequestQuit(10u);

            lifecycle.OnTick(IDLE_TICKS); // would be the idle deadline; Draining is still within grace

            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_DrainingCountRises_NeverReturnsToActive()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 1u);
            lifecycle.RequestQuit(10u);

            lifecycle.OnOccupiedPlayerSlotsChanged(5, 20u);

            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            Assert.IsFalse(lifecycle.AcceptsPlayers);
            Assert.AreEqual(5, lifecycle.OccupiedPlayerSlots);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        // ----- T-6 ------------------------------------------------------------------------------

        [Test]
        public void RequestQuit_SecondQuitWhileDraining_ClosesEmergency_AC_ZI_24()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 1u);
            lifecycle.RequestQuit(10u);

            lifecycle.RequestQuit(30u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Emergency, 30u), listener.Closed[0]);
            Assert.AreEqual(1, listener.DrainingTicks.Count);
        }

        [Test]
        public void RequestQuit_WhileLoading_ClosesEmergencyAndNeverBecomesActive_AC_ZI_26()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out NetworkTestObserver observer);

            lifecycle.RequestQuit(3u);
            LogAssert.Expect(LogType.Warning, new Regex("ZoneLifecycle"));
            lifecycle.MarkActive(4u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual((ZoneCloseTransition.Emergency, 3u), listener.Closed[0]);
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZONE_RAW, ZoneState.Loading, ZoneState.Closed), observer.ZoneStateTransitionedCalls[0]);
            Assert.AreEqual(0, listener.DrainingTicks.Count);
        }

        [Test]
        public void RequestFatal_FromLoading_ClosesEmergency()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);

            lifecycle.RequestFatal(8u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual((ZoneCloseTransition.Emergency, 8u), listener.Closed[0]);
        }

        [Test]
        public void RequestFatal_FromActive_ClosesEmergency()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(3, 1u);

            lifecycle.RequestFatal(9u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Emergency, 9u), listener.Closed[0]);
        }

        [Test]
        public void RequestFatal_FromDraining_ClosesEmergency()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(3, 1u);
            lifecycle.RequestQuit(5u);

            lifecycle.RequestFatal(9u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Emergency, 9u), listener.Closed[0]);
        }

        // ----- One close / terminal -------------------------------------------------------------

        [Test]
        public void AfterClosed_AllRequestsAreNoOpsAndOnClosedStaysOne()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out NetworkTestObserver observer);
            lifecycle.RequestFatal(10u);
            int transitions = observer.ZoneStateTransitionedCalls.Count;
            LogAssert.Expect(LogType.Warning, new Regex(@"RequestFatal ignored"));
            LogAssert.Expect(LogType.Warning, new Regex(@"RequestQuit ignored"));
            LogAssert.Expect(LogType.Warning, new Regex(@"MarkActive ignored"));
            LogAssert.Expect(LogType.Warning, new Regex(@"MarkInitFailed ignored"));

            lifecycle.RequestFatal(11u);
            lifecycle.RequestQuit(12u);
            lifecycle.MarkActive(13u);
            lifecycle.MarkInitFailed(14u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 15u);
            lifecycle.OnOccupiedPlayerSlotsChanged(4, 16u);
            lifecycle.OnTick(100000u);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual(0, listener.DrainingTicks.Count);
            Assert.AreEqual(transitions, observer.ZoneStateTransitionedCalls.Count);
        }

        [Test]
        public void MarkActive_WhenAlreadyActive_IsNoOp()
        {
            ZoneLifecycle lifecycle = CreateActive(out _, out NetworkTestObserver observer);
            LogAssert.Expect(LogType.Warning, new Regex("ZoneLifecycle"));

            lifecycle.MarkActive(20u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(1, observer.ZoneStateTransitionedCalls.Count);
        }

        [Test]
        public void MarkInitFailed_WhenActive_IsNoOp()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            LogAssert.Expect(LogType.Warning, new Regex("ZoneLifecycle"));

            lifecycle.MarkInitFailed(20u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        // ----- Join gate / occupancy / observer -------------------------------------------------

        [Test]
        public void AcceptsPlayers_IsTrueOnlyInActive()
        {
            ZoneLifecycle lifecycle = Create(out _, out _);
            Assert.IsFalse(lifecycle.AcceptsPlayers, "Loading");

            lifecycle.MarkActive(1u);
            Assert.IsTrue(lifecycle.AcceptsPlayers, "Active");

            lifecycle.OnOccupiedPlayerSlotsChanged(1, 2u);
            lifecycle.RequestQuit(3u);
            Assert.IsFalse(lifecycle.AcceptsPlayers, "Draining");

            lifecycle.RequestFatal(4u);
            Assert.IsFalse(lifecycle.AcceptsPlayers, "Closed");
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_Negative_Throws()
        {
            ZoneLifecycle lifecycle = CreateActive(out _, out _);

            Assert.Throws<ArgumentOutOfRangeException>(() => lifecycle.OnOccupiedPlayerSlotsChanged(-1, 5u));
        }

        [Test]
        public void OnOccupiedPlayerSlotsChanged_InLoading_RecordsCountWithoutTransition()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);

            lifecycle.OnOccupiedPlayerSlotsChanged(3, 1u);

            Assert.AreEqual(3, lifecycle.OccupiedPlayerSlots);
            Assert.AreEqual(ZoneState.Loading, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void MarkActive_WithSlotsAlreadyRecorded_DoesNotArmIdleTimer()
        {
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            lifecycle.MarkActive(1u);

            lifecycle.OnTick(IDLE_TICKS * 3u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void Observer_FullPath_SeesEachFromToTransitionInOrder()
        {
            ZoneLifecycle lifecycle = Create(out _, out NetworkTestObserver observer);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);

            lifecycle.MarkActive(1u);
            lifecycle.RequestQuit(2u);
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 3u);

            Assert.AreEqual(3, observer.ZoneStateTransitionedCalls.Count);
            Assert.AreEqual((ZONE_RAW, ZoneState.Loading, ZoneState.Active), observer.ZoneStateTransitionedCalls[0]);
            Assert.AreEqual((ZONE_RAW, ZoneState.Active, ZoneState.Draining), observer.ZoneStateTransitionedCalls[1]);
            Assert.AreEqual((ZONE_RAW, ZoneState.Draining, ZoneState.Closed), observer.ZoneStateTransitionedCalls[2]);
        }

        [Test]
        public void Listener_OnClosed_SeesStateAlreadyClosed()
        {
            var listener = new StateCapturingListener();
            var lifecycle = new ZoneLifecycle(new ZoneID(ZONE_RAW), listener, 0u);
            listener.Lifecycle = lifecycle;

            lifecycle.RequestFatal(1u);

            Assert.AreEqual(ZoneState.Closed, listener.StateAtClosed);
        }

        // ----- Code-review edge cases (2026-10-22) ----------------------------------------------

        [Test]
        public void ZoneState_Loading_IsValueZero()
        {
            Assert.AreEqual(0, (int)ZoneState.Loading);
        }

        [Test]
        public void OnTick_DrainingLastReleaseOnGraceDeadlineTick_ClosesDrainedOnce()
        {
            // Arrange
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 10u);
            lifecycle.RequestQuit(100u);

            // Act — release and grace expiry collide on the same tick.
            lifecycle.OnOccupiedPlayerSlotsChanged(0, 100u + GRACE_TICKS);
            lifecycle.OnTick(100u + GRACE_TICKS);

            // Assert
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Drained, 100u + GRACE_TICKS), listener.Closed[0]);
        }

        [Test]
        public void RequestQuit_OneTickBeforeIdleDeadline_ClosesDrainedNotIdle()
        {
            // Arrange — Active with no slot since tick 0, idle deadline IDLE_TICKS.
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnTick(IDLE_TICKS - 1u);

            // Act
            lifecycle.RequestQuit(IDLE_TICKS - 1u);
            lifecycle.OnTick(IDLE_TICKS);

            // Assert
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Drained, IDLE_TICKS - 1u), listener.Closed[0]);
        }

        [Test]
        public void OnTick_IdleDeadlineSkippedByTickJump_ReportsTickPassedIn()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);

            lifecycle.OnTick(IDLE_TICKS + 50u);

            Assert.AreEqual((ZoneCloseTransition.Idle, IDLE_TICKS + 50u), listener.Closed[0]);
        }

        [Test]
        public void OnTick_GraceDeadlineSkippedByTickJump_ReportsTickPassedIn()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            lifecycle.RequestQuit(100u);

            lifecycle.OnTick(100u + GRACE_TICKS + 50u);

            Assert.AreEqual((ZoneCloseTransition.Drained, 100u + GRACE_TICKS + 50u), listener.Closed[0]);
        }

        [Test]
        public void OnTick_TickEarlierThanPrevious_IsHarmlessAndDeadlineStillClosesOnce()
        {
            ZoneLifecycle lifecycle = CreateActive(out FakeListener listener, out _);

            lifecycle.OnTick(500u);
            lifecycle.OnTick(100u);
            Assert.AreEqual(ZoneState.Active, lifecycle.State);

            lifecycle.OnTick(IDLE_TICKS);
            lifecycle.OnTick(IDLE_TICKS + 1u);
            Assert.AreEqual(1, listener.Closed.Count);
        }

        [Test]
        public void MarkActive_AfterLoadDeadlineButBeforeAnyOnTick_StillActivates()
        {
            // Pinned behaviour: the load timeout is evaluated only in OnTick; a finished load wins.
            ZoneLifecycle lifecycle = Create(out FakeListener listener, out _);

            lifecycle.MarkActive(LOAD_TICKS + 100u);

            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            Assert.AreEqual(0, listener.Closed.Count);
        }

        [Test]
        public void OnClosed_ListenerReentersLifecycle_NoSecondClose()
        {
            // Arrange
            var listener = new ReenterOnClosedListener();
            var lifecycle = new ZoneLifecycle(new ZoneID(ZONE_RAW), listener, 0u, IDLE_SECONDS, GRACE_SECONDS, LOAD_SECONDS, HZ);
            listener.Lifecycle = lifecycle;
            lifecycle.MarkActive(0u);
            LogAssert.Expect(LogType.Warning, new Regex(@"RequestFatal ignored"));
            LogAssert.Expect(LogType.Warning, new Regex(@"RequestQuit ignored"));

            // Act
            lifecycle.OnTick(IDLE_TICKS);

            // Assert
            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.ClosedCount);
        }

        [Test]
        public void RequestQuit_ListenerReleasesLastSlotInsideOnDraining_ClosesDrainedOnce()
        {
            // Arrange
            var listener = new ReleaseOnDrainingListener();
            var lifecycle = new ZoneLifecycle(new ZoneID(ZONE_RAW), listener, 0u, IDLE_SECONDS, GRACE_SECONDS, LOAD_SECONDS, HZ);
            listener.Lifecycle = lifecycle;
            lifecycle.MarkActive(0u);
            lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);

            // Act
            lifecycle.RequestQuit(10u);

            // Assert
            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual(ZoneCloseTransition.Drained, listener.Closed[0]);
        }

        private sealed class ReenterOnClosedListener : IZoneLifecycleListener
        {
            public ZoneLifecycle Lifecycle;
            public int ClosedCount;

            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick)
            {
                ClosedCount++;
                Lifecycle.RequestFatal(serverTick);
                Lifecycle.RequestQuit(serverTick);
                Lifecycle.OnTick(serverTick + 1u);
                Lifecycle.OnOccupiedPlayerSlotsChanged(0, serverTick);
            }
        }

        private sealed class ReleaseOnDrainingListener : IZoneLifecycleListener
        {
            public ZoneLifecycle Lifecycle;
            public readonly List<ZoneCloseTransition> Closed = new List<ZoneCloseTransition>();

            public void OnDraining(uint serverTick)
            {
                Lifecycle.OnOccupiedPlayerSlotsChanged(0, serverTick);
            }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick)
            {
                Closed.Add(transition);
            }
        }

        [Test]
        public void RequestQuit_ListenerClosesZoneInsideOnDraining_OnClosedStillFiresOnce()
        {
            // Arrange — Active with no slot, so T-5 would fire on the T-4 tick; the listener re-enters
            // with RequestFatal from OnDraining.
            var listener = new FatalOnDrainingListener();
            var lifecycle = new ZoneLifecycle(new ZoneID(ZONE_RAW), listener, 0u, IDLE_SECONDS, GRACE_SECONDS, LOAD_SECONDS, HZ);
            listener.Lifecycle = lifecycle;
            lifecycle.MarkActive(0u);

            // Act
            lifecycle.RequestQuit(10u);

            // Assert — only the re-entrant close is reported; the same-tick T-5 is skipped.
            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, listener.Closed.Count);
            Assert.AreEqual(ZoneCloseTransition.Emergency, listener.Closed[0]);
        }

        private sealed class FatalOnDrainingListener : IZoneLifecycleListener
        {
            public ZoneLifecycle Lifecycle;
            public readonly List<ZoneCloseTransition> Closed = new List<ZoneCloseTransition>();

            public void OnDraining(uint serverTick)
            {
                Lifecycle.RequestFatal(serverTick);
            }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick)
            {
                Closed.Add(transition);
            }
        }

        private sealed class StateCapturingListener : IZoneLifecycleListener
        {
            public ZoneLifecycle Lifecycle;
            public ZoneState StateAtClosed = ZoneState.Loading;

            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick)
            {
                StateAtClosed = Lifecycle.State;
            }
        }
    }
}
