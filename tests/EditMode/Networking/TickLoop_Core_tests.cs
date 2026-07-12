using System.Text.RegularExpressions;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 009 — the fixed 20Hz server tick loop
    /// (<see cref="ServerTickLoop"/>). Covers AC-NC-04 (tick-count cadence), AC-NC-05 (Beat-cadence
    /// dispatch via a generic tick-driven delegate), AC-TICK-1 (EC-NET-10 tick drift: no
    /// compensation, and the 25ms/60s performance-alert monitor), and AC-TICK-2 (generic TTL-timer
    /// boundary). All tests are synchronous and deterministic — <see cref="ServerTickLoop.AdvanceTick"/>
    /// is called directly in a loop; no <c>Thread.Sleep</c>, no real wall-clock waits, no real
    /// 10-second test runtime.
    /// </summary>
    /// <remarks>
    /// <b>AC-NC-04's literal "±2 for jitter" tolerance is not applicable at this layer, by design:</b>
    /// the ±2 jitter allowance in the GDD/story text describes a real wall-clock 10-second server
    /// runtime, where frame-timing variance in the real host loop can cause a tick to be scheduled
    /// slightly early or late relative to the 10-second wall-clock window. This story's dispatch
    /// logic is synchronous and driven by direct, deterministic method calls with no wall-clock
    /// component at all — there is no jitter source for this layer to exhibit. Asserting an exact
    /// count of 200 (not "198-202") is therefore still a faithful proof of the AC: it pins the
    /// underlying tick-counting invariant ("N calls to <see cref="ServerTickLoop.AdvanceTick"/>
    /// produce exactly N <see cref="INetworkTestObserver.OnTickCompleted"/> callbacks") that the
    /// real wall-clock-driven loop (a future NGO-integration story) will rely on; the ±2 tolerance
    /// applies only to *that* future story's own real-time test, not to this one.
    /// </remarks>
    [TestFixture]
    internal sealed class TickLoop_Core_Tests
    {
        // =========================================================================================
        // AC-NC-04: 200 tick iterations complete for 200 AdvanceTick calls, verified via
        // OnTickCompleted callback count.
        // =========================================================================================

        [Test]
        public void AdvanceTick_CalledTwoHundredTimes_FiresOnTickCompletedExactlyTwoHundredTimes()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            var observer = new NetworkTestObserver();

            // Act
            for (int i = 0; i < 200; i++)
            {
                tickLoop.AdvanceTick(observer: observer);
            }

            // Assert
            Assert.AreEqual(200, observer.TickCompletedCalls.Count, "200 AdvanceTick calls must fire OnTickCompleted exactly 200 times.");
            Assert.AreEqual(200u, tickLoop.ServerTickNumber, "ServerTickNumber must be exactly 200 after 200 AdvanceTick calls.");
            Assert.AreEqual(200u, observer.TickCompletedCalls[199], "The final OnTickCompleted call must report tick 200.");
        }

        [Test]
        public void AdvanceTick_CalledRepeatedly_OnTickCompletedReportsSequentialTickNumbers()
        {
            // Arrange — proactive coverage: each OnTickCompleted call must report the exact tick
            // number that just completed, in strictly increasing order, not merely the right count.
            var tickLoop = new ServerTickLoop();
            var observer = new NetworkTestObserver();

            // Act
            for (int i = 0; i < 5; i++)
            {
                tickLoop.AdvanceTick(observer: observer);
            }

            // Assert
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4, 5 }, observer.TickCompletedCalls, "OnTickCompleted must report sequential tick numbers 1..5.");
        }

        // =========================================================================================
        // AC-NC-05: a COMBAT_ACTIVE-style entity's _cycleTimer advances by FIXED_DELTA_TIME each
        // tick, and a Beat event fires every 20 ticks (1.0s at 20Hz).
        // =========================================================================================

        [Test]
        public void RegisteredWarriorLikeFixture_TwentyTicksAdvanced_FiresBeatAtExactlyTickTwentyAndNotBefore()
        {
            // Arrange — a minimal test-double "Warrior-like" fixture: a mutable _cycleTimer plus a
            // registered tick-driven delegate advancing it by the fixed deltaTime, firing a "Beat"
            // when it crosses CycleDuration. AC-NC-05 does not require the real AutoAttackCombat
            // system (out of scope) — only that the generic tick-driven dispatch mechanism fires a
            // registered callback at the correct cadence.
            var tickLoop = new ServerTickLoop();
            const float cycleDuration = 1.0f;
            float cycleTimer = 0f;
            int beatCount = 0;
            uint beatFiredAtTick = 0;

            tickLoop.RegisterTickDriven(deltaTime =>
            {
                cycleTimer += deltaTime;
                if (cycleTimer >= cycleDuration)
                {
                    beatCount++;
                    beatFiredAtTick = tickLoop.ServerTickNumber;
                    cycleTimer -= cycleDuration;
                }
            });

            // Act — advance 19 ticks: must NOT have fired yet.
            for (int i = 0; i < 19; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert — not yet due at tick 19 (19 * 0.05s = 0.95s < 1.0s).
            Assert.AreEqual(0, beatCount, "Beat must not fire before 20 ticks have elapsed (0.95s < 1.0s CycleDuration).");

            // Act — the 20th tick.
            tickLoop.AdvanceTick();

            // Assert — fires at exactly tick 20 (20 * 0.05s = 1.0s == CycleDuration).
            Assert.AreEqual(1, beatCount, "Beat must fire exactly once by tick 20 (1.0s at 20Hz).");
            Assert.AreEqual(20u, beatFiredAtTick, "Beat must fire on tick 20, not before or after.");
        }

        [Test]
        public void RegisteredWarriorLikeFixture_SixtyTicksAdvanced_FiresBeatEveryTwentyTicksThreeTimes()
        {
            // Arrange — proves recurrence, not just the first Beat: over 60 ticks (3.0s), Beat fires
            // at ticks 20, 40, and 60.
            var tickLoop = new ServerTickLoop();
            const float cycleDuration = 1.0f;
            float cycleTimer = 0f;
            var beatFiredAtTicks = new System.Collections.Generic.List<uint>();

            tickLoop.RegisterTickDriven(deltaTime =>
            {
                cycleTimer += deltaTime;
                if (cycleTimer >= cycleDuration)
                {
                    beatFiredAtTicks.Add(tickLoop.ServerTickNumber);
                    cycleTimer -= cycleDuration;
                }
            });

            // Act
            for (int i = 0; i < 60; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert
            CollectionAssert.AreEqual(new uint[] { 20, 40, 60 }, beatFiredAtTicks, "Beat must recur every 20 ticks: at ticks 20, 40, and 60 over a 60-tick run.");
        }

        [Test]
        public void RegisteredWarriorLikeFixture_ObservedViaOnTickCompleted_TwentyCallbacksElapseBetweenBeats()
        {
            // Arrange — AC-NC-05's literal verification method: "verified by counting
            // OnTickCompleted callbacks between consecutive Beat-resolved events."
            var tickLoop = new ServerTickLoop();
            var observer = new NetworkTestObserver();
            const float cycleDuration = 1.0f;
            float cycleTimer = 0f;
            var beatFiredAfterTickCompletedCount = new System.Collections.Generic.List<int>();

            tickLoop.RegisterTickDriven(deltaTime =>
            {
                cycleTimer += deltaTime;
                if (cycleTimer >= cycleDuration)
                {
                    // Recorded before this tick's OnTickCompleted fires (tick-driven delegates run
                    // before the observer hook within AdvanceTick), so add 1 to represent the count
                    // once this tick's own OnTickCompleted call is included.
                    beatFiredAfterTickCompletedCount.Add(observer.TickCompletedCalls.Count + 1);
                    cycleTimer -= cycleDuration;
                }
            });

            // Act
            for (int i = 0; i < 40; i++)
            {
                tickLoop.AdvanceTick(observer: observer);
            }

            // Assert — exactly 20 OnTickCompleted callbacks elapse between consecutive Beats.
            Assert.AreEqual(2, beatFiredAfterTickCompletedCount.Count, "Two Beats must fire over 40 ticks.");
            Assert.AreEqual(20, beatFiredAfterTickCompletedCount[0], "The first Beat must resolve after exactly 20 OnTickCompleted callbacks.");
            Assert.AreEqual(20, beatFiredAfterTickCompletedCount[1] - beatFiredAfterTickCompletedCount[0], "Exactly 20 OnTickCompleted callbacks must elapse between consecutive Beats.");
        }

        // =========================================================================================
        // AC-TICK-1 (EC-NET-10): tick drift never causes compensation; sustained >25ms average
        // drift over a 1200-tick (60s-equivalent) window logs a performance alert.
        // =========================================================================================

        [Test]
        public void AdvanceTick_GivenALongActualTickDuration_ServerTickNumberAdvancesByExactlyOneNeverMore()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();

            // Act — a single tick that took far longer than the 50ms fixed interval.
            tickLoop.AdvanceTick(actualTickDurationSeconds: 0.5f);

            // Assert — ServerTickNumber still advances by exactly 1, never 2+, regardless of how long the tick took.
            Assert.AreEqual(1u, tickLoop.ServerTickNumber, "ServerTickNumber must advance by exactly 1 per AdvanceTick call, even when the tick ran long — the loop never runs multiple ticks to compensate.");
        }

        [Test]
        public void AdvanceTick_ManyConsecutiveLongTicks_ServerTickNumberAdvancesByExactlyOnePerCallNeverCompensates()
        {
            // Arrange — proactive coverage: repeated long ticks must never accumulate into a
            // multi-tick "catch-up" advance.
            var tickLoop = new ServerTickLoop();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: 0.2f); // 200ms, 4x the fixed 50ms interval
            }

            // Assert
            Assert.AreEqual(10u, tickLoop.ServerTickNumber, "10 AdvanceTick calls, however long each took, must produce exactly ServerTickNumber == 10 — never more.");
        }

        [Test]
        public void AdvanceTick_SustainedDriftAboveThresholdOverFullWindow_LogsPerformanceAlert()
        {
            // Arrange — EC-NET-10: average drift over a DRIFT_WINDOW_TICKS (1200-tick, 60s-equivalent)
            // window exceeding 25ms must log a performance alert. Use a constant per-tick duration of
            // 100ms (50ms drift), comfortably above the 25ms threshold.
            var tickLoop = new ServerTickLoop();
            const float actualTickDurationSeconds = 0.1f; // 50ms average drift over the window

            LogAssert.Expect(LogType.Warning, new Regex(@"\[ServerTickLoop\] TickDriftAlert"));

            // Act
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: actualTickDurationSeconds);
            }

            // Assert — LogAssert.Expect above fails the test if the warning never fired; no further
            // assertion is required, but pin ServerTickNumber too for good measure.
            Assert.AreEqual((uint)ServerTickLoop.DRIFT_WINDOW_TICKS, tickLoop.ServerTickNumber, "ServerTickNumber must still equal exactly the number of AdvanceTick calls, even under sustained drift.");
        }

        [Test]
        public void AdvanceTick_BelowThresholdAverageDriftOverFullWindow_DoesNotLogPerformanceAlert()
        {
            // Arrange — average drift below the 25ms threshold must never log an alert. Use a
            // constant per-tick duration of 60ms (10ms drift), comfortably below 25ms.
            var tickLoop = new ServerTickLoop();
            const float actualTickDurationSeconds = 0.06f; // 10ms average drift over the window

            // Act
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: actualTickDurationSeconds);
            }

            // Assert — no unexpected log messages (including no TickDriftAlert warning) were emitted.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AdvanceTick_DefaultActualTickDuration_NeverLogsPerformanceAlertAcrossManyTicks()
        {
            // Arrange — proactive coverage: the default actualTickDurationSeconds (== FIXED_DELTA_TIME,
            // i.e. "no drift") must never trigger a false-positive alert, even across a full window
            // and beyond — the common case for tests/call sites that don't care about drift.
            var tickLoop = new ServerTickLoop();

            // Act
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS + 50; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // AC-TICK-2: a generic tick-registered TTL timer's release callback fires within one tick
        // boundary (≤50ms, i.e. exactly at the expiry tick), proven via a mock timer.
        // =========================================================================================

        [Test]
        public void RegisterTtlTimer_AdvancedToExactlyExpiryTick_FiresReleaseCallbackAtThatTickAndNotBefore()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            bool released = false;
            uint releasedAtTick = 0;
            const uint expiryTick = 10u;
            tickLoop.RegisterTtlTimer(expiryTick, onExpired: () =>
            {
                released = true;
                releasedAtTick = tickLoop.ServerTickNumber;
            });

            // Act — advance to one tick before expiry.
            for (int i = 0; i < 9; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert — must NOT fire one tick early (established boundary-testing rigor for this project).
            Assert.IsFalse(released, "The TTL release callback must not fire before its expiry tick is reached.");

            // Act — the expiry tick itself.
            tickLoop.AdvanceTick();

            // Assert — fires at exactly the expiry tick boundary.
            Assert.IsTrue(released, "The TTL release callback must fire on the tick its expiry is reached.");
            Assert.AreEqual(expiryTick, releasedAtTick, "The TTL release callback must fire at exactly the expiry tick, not later.");
        }

        [Test]
        public void RegisterTtlTimer_ExpiryTickAlreadyInThePast_FiresOnVeryNextAdvanceTickCallNotSilentlyDropped()
        {
            // Arrange — a timer registered for an expiry tick already at-or-before the current
            // ServerTickNumber must not be silently dropped; it fires on the very next AdvanceTick.
            var tickLoop = new ServerTickLoop();
            for (int i = 0; i < 5; i++)
            {
                tickLoop.AdvanceTick(); // ServerTickNumber is now 5
            }

            bool released = false;
            tickLoop.RegisterTtlTimer(expiryTick: 2u, onExpired: () => released = true); // already "in the past"

            // Act
            tickLoop.AdvanceTick(); // ServerTickNumber becomes 6

            // Assert
            Assert.IsTrue(released, "A TTL timer registered with an already-past expiry tick must fire on the very next AdvanceTick call, not be silently dropped.");
        }

        [Test]
        public void RegisterTtlTimer_FiresExactlyOnceEvenIfAdvanceTickContinuesPastExpiry()
        {
            // Arrange — proactive coverage: a one-shot TTL timer must not re-fire on subsequent ticks.
            var tickLoop = new ServerTickLoop();
            int releaseCount = 0;
            tickLoop.RegisterTtlTimer(expiryTick: 3u, onExpired: () => releaseCount++);

            // Act
            for (int i = 0; i < 10; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert
            Assert.AreEqual(1, releaseCount, "A TTL timer's release callback must fire exactly once, never again on later ticks.");
        }

        [Test]
        public void RegisterTtlTimer_MultipleTimersWithDifferentExpiries_EachFiresIndependentlyAtItsOwnBoundary()
        {
            // Arrange — proactive coverage: multiple simultaneously-registered TTL timers must not
            // interfere with each other.
            var tickLoop = new ServerTickLoop();
            var releasedAtTicks = new System.Collections.Generic.List<(string name, uint tick)>();

            tickLoop.RegisterTtlTimer(expiryTick: 5u, onExpired: () => releasedAtTicks.Add(("A", tickLoop.ServerTickNumber)));
            tickLoop.RegisterTtlTimer(expiryTick: 5u, onExpired: () => releasedAtTicks.Add(("B", tickLoop.ServerTickNumber))); // same expiry as A
            tickLoop.RegisterTtlTimer(expiryTick: 8u, onExpired: () => releasedAtTicks.Add(("C", tickLoop.ServerTickNumber)));

            // Act
            for (int i = 0; i < 10; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert — all three fire, each at its own correct expiry tick.
            Assert.AreEqual(3, releasedAtTicks.Count, "All three registered TTL timers must fire exactly once each.");
            Assert.AreEqual(2, releasedAtTicks.FindAll(r => r.tick == 5u).Count, "Both timers A and B, sharing expiry tick 5, must fire at tick 5.");
            Assert.AreEqual(1, releasedAtTicks.FindAll(r => r.tick == 8u).Count, "Timer C must fire at tick 8.");
            CollectionAssert.Contains(releasedAtTicks.ConvertAll(r => r.name), "A");
            CollectionAssert.Contains(releasedAtTicks.ConvertAll(r => r.name), "B");
            CollectionAssert.Contains(releasedAtTicks.ConvertAll(r => r.name), "C");
        }

        // =========================================================================================
        // Proactive edge-case coverage: multiple simultaneously-registered tick-driven delegates,
        // ServerTickNumber wraparound, and constant/tuning-knob pinning.
        // =========================================================================================

        [Test]
        public void RegisterTickDriven_MultipleDelegatesRegistered_EachFiresIndependentlyEveryTickWithoutInterference()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            int firstCount = 0;
            int secondCount = 0;
            float firstAccumulated = 0f;

            tickLoop.RegisterTickDriven(dt => firstCount++);
            tickLoop.RegisterTickDriven(dt => secondCount++);
            tickLoop.RegisterTickDriven(dt => firstAccumulated += dt);

            // Act
            for (int i = 0; i < 7; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert — all three delegates fired every tick, independently.
            Assert.AreEqual(7, firstCount, "Every registered tick-driven delegate must fire once per AdvanceTick call.");
            Assert.AreEqual(7, secondCount, "A second independently-registered delegate must also fire once per AdvanceTick call, unaffected by the first.");
            Assert.AreEqual(7 * ServerTickLoop.FIXED_DELTA_TIME, firstAccumulated, 0.0001f, "Each delegate must receive FIXED_DELTA_TIME as its deltaTime argument.");
        }

        [Test]
        public void UnregisterTickDriven_RemovesDelegate_StopsFiringOnSubsequentTicksWithoutAffectingOthers()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            int removedCount = 0;
            int remainingCount = 0;
            System.Action<float> removedHandler = dt => removedCount++;

            tickLoop.RegisterTickDriven(removedHandler);
            tickLoop.RegisterTickDriven(dt => remainingCount++);

            // Act
            tickLoop.AdvanceTick();
            bool wasRemoved = tickLoop.UnregisterTickDriven(removedHandler);
            tickLoop.AdvanceTick();

            // Assert
            Assert.IsTrue(wasRemoved, "UnregisterTickDriven must report true when a matching registration was found.");
            Assert.AreEqual(1, removedCount, "The unregistered delegate must not fire again after being unregistered.");
            Assert.AreEqual(2, remainingCount, "A still-registered delegate must be unaffected by another delegate's unregistration.");
        }

        [Test]
        public void ServerTickNumber_AdvancedPastUintMaxValue_WrapsSafelyAndRemainsUsableWithStaleDiscardComparer()
        {
            // Arrange — boundary coverage near uint.MaxValue, reusing StaleDiscardComparer's own
            // wraparound guarantee (the same proactive coverage pattern established for
            // HeartbeatActivityTracker in Story 008). Seed ServerTickNumber near uint.MaxValue via
            // the constructor rather than advancing billions of real ticks.
            var tickLoop = new ServerTickLoop(initialTickNumber: uint.MaxValue - 2);

            // Act — advance 5 ticks: MaxValue-2 -> MaxValue-1 -> MaxValue -> 0 -> 1 -> 2 (wraps).
            for (int i = 0; i < 5; i++)
            {
                tickLoop.AdvanceTick();
            }

            // Assert — ServerTickNumber wrapped to 2, and a TTL timer registered against a
            // pre-wraparound expiry tick is still correctly evaluated as expired via StaleDiscardComparer.
            Assert.AreEqual(2u, tickLoop.ServerTickNumber, "ServerTickNumber must wrap around uint.MaxValue via ordinary unchecked uint arithmetic.");
            Assert.IsTrue(StaleDiscardComparer.IsTickExpired(tickLoop.ServerTickNumber, expiryTick: uint.MaxValue - 1), "A pre-wraparound expiry tick must still correctly compare as expired after ServerTickNumber wraps, via StaleDiscardComparer.");
        }

        [Test]
        public void RegisterTtlTimer_ExpiryTickNearUintMaxValueWraparoundBoundary_FiresAtExactWrappedBoundaryNotBefore()
        {
            // Arrange — a TTL timer whose expiry tick is itself past the uint.MaxValue wraparound
            // boundary, seeded via the constructor.
            var tickLoop = new ServerTickLoop(initialTickNumber: uint.MaxValue - 1);
            bool released = false;
            const uint expiryTick = 2u; // wraps past uint.MaxValue: (MaxValue - 1) + 3 ticks = 2
            tickLoop.RegisterTtlTimer(expiryTick, onExpired: () => released = true);

            // Act — advance 2 ticks: MaxValue-1 -> MaxValue -> 0. Not yet at expiry (2).
            tickLoop.AdvanceTick();
            tickLoop.AdvanceTick();
            Assert.IsFalse(released, "The TTL timer must not fire before its wrapped expiry boundary is reached.");

            // Act — advance 1 more tick: 0 -> 1. Still not at expiry (2).
            tickLoop.AdvanceTick();
            Assert.IsFalse(released, "The TTL timer must not fire one tick before its wrapped expiry boundary.");

            // Act — advance 1 more tick: 1 -> 2. Exactly at the wrapped expiry boundary.
            tickLoop.AdvanceTick();

            // Assert
            Assert.IsTrue(released, "The TTL timer must fire at exactly its wrapped expiry boundary, proving wraparound-safe comparison via StaleDiscardComparer.");
        }

        [Test]
        public void TICK_RATE_HZ_And_FIXED_DELTA_TIME_MatchCrNet2()
        {
            // Assert — pins the CR-NET-2 tuning-knob values so a future edit cannot silently drift them.
            Assert.AreEqual(20, ServerTickLoop.TICK_RATE_HZ, "TICK_RATE_HZ must be 20 per CR-NET-2.");
            Assert.AreEqual(0.05f, ServerTickLoop.FIXED_DELTA_TIME, 0.0001f, "FIXED_DELTA_TIME must be 1.0/20 = 0.05s per CR-NET-2.");
            Assert.AreEqual(1200, ServerTickLoop.DRIFT_WINDOW_TICKS, "DRIFT_WINDOW_TICKS must be 60 seconds expressed in ticks at 20Hz (1200).");
            Assert.AreEqual(0.025f, ServerTickLoop.DRIFT_ALERT_THRESHOLD_SECONDS, 0.0001f, "DRIFT_ALERT_THRESHOLD_SECONDS must be 25ms per EC-NET-10.");
        }

        [Test]
        public void Constructor_DefaultsToTickNumberZero()
        {
            // Assert
            var tickLoop = new ServerTickLoop();
            Assert.AreEqual(0u, tickLoop.ServerTickNumber, "A freshly-constructed ServerTickLoop must start at ServerTickNumber 0 by default.");
        }

        // =========================================================================================
        // Code-review follow-up (Story 009): regression test for a real bug found during review —
        // the tick-driven dispatch loop originally iterated the live mutable list by index, so a
        // delegate unregistering an earlier sibling mid-tick shifted the list and silently skipped
        // the next not-yet-invoked delegate. Fixed via a snapshotted dispatch count plus deferred
        // removal (see ServerTickLoop.UnregisterTickDriven/AdvanceTick remarks). Also covers the
        // exception-propagation contract, ArgumentNullException guards, UnregisterTickDriven's
        // false-return path, TTL reentrancy, and the drift-alert exclusive-threshold boundary — all
        // gaps identified by the unity-specialist and qa-tester code-review passes.
        // =========================================================================================

        [Test]
        public void RegisterTickDriven_DelegateUnregistersEarlierSiblingDuringDispatch_LaterDelegateStillFires()
        {
            // Arrange — three delegates: `first` (to be unregistered), a second delegate that
            // unregisters `first` when it runs, and `last`, which must still fire this same tick
            // despite the resulting list shift.
            var tickLoop = new ServerTickLoop();
            bool firstInvoked = false;
            bool lastInvoked = false;
            System.Action<float> first = dt => firstInvoked = true;

            tickLoop.RegisterTickDriven(first);
            tickLoop.RegisterTickDriven(dt => tickLoop.UnregisterTickDriven(first));
            tickLoop.RegisterTickDriven(dt => lastInvoked = true);

            // Act
            tickLoop.AdvanceTick();

            // Assert — all three fire on the tick the unregistration happens (first runs before
            // being unregistered; last must not be skipped by the shift).
            Assert.IsTrue(firstInvoked, "The delegate being unregistered mid-tick must still fire this tick — it runs before its own unregistration is requested.");
            Assert.IsTrue(lastInvoked, "A delegate registered after the one performing the unregistration must still fire this same tick — it must not be silently skipped by the list shift.");

            // Act — a second tick proves the unregistration actually took effect afterward.
            firstInvoked = false;
            lastInvoked = false;
            tickLoop.AdvanceTick();

            // Assert
            Assert.IsFalse(firstInvoked, "The unregistered delegate must not fire on a later tick.");
            Assert.IsTrue(lastInvoked, "The remaining delegates must continue firing normally on subsequent ticks.");
        }

        [Test]
        public void AdvanceTick_TickDrivenDelegateThrows_SkipsSubsequentDelegatesButLeavesNoCorruptedState()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            bool afterThrowInvoked = false;
            System.Action<float> throwingDelegate = dt => throw new System.InvalidOperationException("simulated tick-driven delegate failure");
            tickLoop.RegisterTickDriven(throwingDelegate);
            tickLoop.RegisterTickDriven(dt => afterThrowInvoked = true);

            // Act & Assert — the exception propagates out of AdvanceTick; the delegate registered
            // after the throwing one must not fire for that same call.
            Assert.Throws<System.InvalidOperationException>(() => tickLoop.AdvanceTick());
            Assert.IsFalse(afterThrowInvoked, "A delegate registered after the one that throws must not fire for that same AdvanceTick call.");
            Assert.AreEqual(1u, tickLoop.ServerTickNumber, "ServerTickNumber must still have advanced by exactly 1 even though a tick-driven delegate threw.");

            // Act — remove the throwing delegate and advance again: proves the earlier exception
            // left no corrupted internal dispatch state (e.g. a stuck dispatch-in-progress flag
            // would silently break all future unregistrations or dispatch passes).
            tickLoop.UnregisterTickDriven(throwingDelegate);
            tickLoop.AdvanceTick();

            // Assert
            Assert.IsTrue(afterThrowInvoked, "Once the throwing delegate is removed, the remaining delegate must fire normally on a later tick.");
            Assert.AreEqual(2u, tickLoop.ServerTickNumber, "ServerTickNumber must continue advancing normally after a tick that threw.");
        }

        [Test]
        public void RegisterTickDriven_NullCallback_ThrowsArgumentNullException()
        {
            var tickLoop = new ServerTickLoop();
            Assert.Throws<System.ArgumentNullException>(() => tickLoop.RegisterTickDriven(null));
        }

        [Test]
        public void RegisterTtlTimer_NullCallback_ThrowsArgumentNullException()
        {
            var tickLoop = new ServerTickLoop();
            Assert.Throws<System.ArgumentNullException>(() => tickLoop.RegisterTtlTimer(expiryTick: 10u, onExpired: null));
        }

        [Test]
        public void UnregisterTickDriven_DelegateNeverRegistered_ReturnsFalseAndDoesNotThrow()
        {
            var tickLoop = new ServerTickLoop();
            System.Action<float> neverRegistered = dt => { };

            bool result = tickLoop.UnregisterTickDriven(neverRegistered);

            Assert.IsFalse(result, "UnregisterTickDriven must return false when no matching registration exists.");
        }

        [Test]
        public void RegisterTtlTimer_CallbackRegistersNewTtlTimerDuringExpiryLoop_NewTimerEvaluatedOnLaterTickNotSameOne()
        {
            // Arrange — proves the documented reentrancy guarantee: a TTL callback that registers a
            // new TTL timer during the same AdvanceTick's expiry pass does not have that new timer
            // evaluated within that same call, even if its expiry tick is already in the past.
            var tickLoop = new ServerTickLoop();
            bool secondTimerFired = false;
            tickLoop.RegisterTtlTimer(expiryTick: 3u, onExpired: () =>
            {
                tickLoop.RegisterTtlTimer(expiryTick: 2u, onExpired: () => secondTimerFired = true);
            });

            // Act — advance to the first timer's expiry tick (3); its callback registers the second.
            tickLoop.AdvanceTick();
            tickLoop.AdvanceTick();
            tickLoop.AdvanceTick();

            // Assert — the second timer must not have fired within this same AdvanceTick call.
            Assert.IsFalse(secondTimerFired, "A TTL timer registered from within another TTL callback during the same AdvanceTick call must not fire within that same call, even if its expiry tick is already in the past.");

            // Act — the very next AdvanceTick call must fire it.
            tickLoop.AdvanceTick();

            // Assert
            Assert.IsTrue(secondTimerFired, "The reentrant-registered TTL timer must fire on the very next AdvanceTick call.");
        }

        [Test]
        public void AdvanceTick_DriftJustBelowThresholdOverFullWindow_DoesNotLogPerformanceAlert()
        {
            // Arrange — the drift alert comparison is strict `>` (ServerTickLoop.RecordDriftSample),
            // so drift just under the 25ms threshold must not alert. Uses 24.9ms average drift,
            // close to but safely below the threshold to avoid floating-point boundary flakiness.
            var tickLoop = new ServerTickLoop();
            const float actualTickDurationSeconds = ServerTickLoop.FIXED_DELTA_TIME + 0.0249f;

            // Act
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: actualTickDurationSeconds);
            }

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AdvanceTick_DriftJustAboveThresholdOverFullWindow_LogsPerformanceAlert()
        {
            // Arrange — drift just over the 25ms threshold must alert, proving the "no alert" side
            // of the boundary is exclusive (comparison is strict `>`, not `>=`).
            var tickLoop = new ServerTickLoop();
            const float actualTickDurationSeconds = ServerTickLoop.FIXED_DELTA_TIME + 0.0251f;

            LogAssert.Expect(LogType.Warning, new Regex(@"\[ServerTickLoop\] TickDriftAlert"));

            // Act
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: actualTickDurationSeconds);
            }
        }

        [Test]
        public void AdvanceTick_TwoConsecutiveDriftWindows_SecondWindowEvaluatedIndependentlyOfFirst()
        {
            // Arrange — proves the drift accumulator actually resets after evaluating a window
            // (ServerTickLoop.RecordDriftSample resets its running sum/count once DRIFT_WINDOW_TICKS
            // samples accumulate) rather than silently carrying a stale sum/count into the next
            // window. First window: sustained high drift (must alert once). Second window: zero
            // drift (must not alert) — if the accumulator failed to reset, residual sum from the
            // first window could still push the second window's average over the threshold.
            var tickLoop = new ServerTickLoop();
            const float highDriftDuration = ServerTickLoop.FIXED_DELTA_TIME + 0.1f; // 100ms average drift, well above threshold

            LogAssert.Expect(LogType.Warning, new Regex(@"\[ServerTickLoop\] TickDriftAlert"));

            // Act — first window: sustained high drift, must alert exactly once.
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(actualTickDurationSeconds: highDriftDuration);
            }

            // Act — second window: no drift at all.
            for (int i = 0; i < ServerTickLoop.DRIFT_WINDOW_TICKS; i++)
            {
                tickLoop.AdvanceTick(); // default actualTickDurationSeconds == FIXED_DELTA_TIME, i.e. zero drift
            }

            // Assert — no further unexpected log messages beyond the single expected alert above.
            LogAssert.NoUnexpectedReceived();
        }
    }
}
