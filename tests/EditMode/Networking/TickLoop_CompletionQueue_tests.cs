using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 030 — the tick completion queue
    /// (<see cref="TickCompletionQueue"/>, ADR-011 Decision 2) including the integration with
    /// <see cref="ServerTickLoop"/>. Deterministic: no async tests, no sleeps, no real threads; the
    /// tick-thread check uses an injected fake thread-id provider, and every
    /// <see cref="TaskCompletionSource{TResult}"/> uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>.
    /// </summary>
    [TestFixture]
    internal sealed class TickLoop_CompletionQueue_Tests
    {
        private const int WATCHDOG_TICKS = 3;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const uint TRACK_TICK = 10u;
        private const int TICK_THREAD_ID = 1;
        private const int OTHER_THREAD_ID = 2;

        private int _threadId;

        [SetUp]
        public void SetUp()
        {
            _threadId = TICK_THREAD_ID;
        }

        private int ReadThreadId() => _threadId;

        private TickCompletionQueue CreateQueue(int watchdogTicks = WATCHDOG_TICKS, uint initialTick = 0u) =>
            new TickCompletionQueue(watchdogTicks, initialTick, ReadThreadId);

        private static TaskCompletionSource<T> NewSource<T>() =>
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Recorder<T>
        {
            internal readonly List<TickTaskResult<T>> Results = new List<TickTaskResult<T>>();
            internal void OnResult(TickTaskResult<T> result) => Results.Add(result);
        }

        // =========================================================================================
        // Completed / already complete / order / incomplete
        // =========================================================================================

        [Test]
        public void Drain_TaskCompleted_InvokesCallbackOnceWithValue()
        {
            // Arrange
            const int value = 42;
            TickCompletionQueue queue = CreateQueue();
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            Assert.AreEqual(1, queue.InFlightCount);

            // Act
            source.SetResult(value);
            queue.Drain(1u);
            queue.Drain(2u);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count, "The callback must run exactly once.");
            Assert.AreEqual(TickTaskStatus.Completed, recorder.Results[0].Status);
            Assert.AreEqual(value, recorder.Results[0].Value);
            Assert.IsNull(recorder.Results[0].Error);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void Track_TaskAlreadyComplete_HandledByNextDrainNotInsideTrack()
        {
            // Arrange
            const int value = 7;
            TickCompletionQueue queue = CreateQueue();
            var recorder = new Recorder<int>();

            // Act
            queue.Track(Task.FromResult(value), null, recorder.OnResult);
            int callsAfterTrack = recorder.Results.Count;
            queue.Drain(1u);

            // Assert
            Assert.AreEqual(0, callsAfterTrack, "Track must never invoke the callback.");
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Completed, recorder.Results[0].Status);
            Assert.AreEqual(value, recorder.Results[0].Value);
        }

        [Test]
        public void Drain_SeveralTasksComplete_RunsCallbacksInTrackOrder()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var order = new List<string>();
            var sourceA = NewSource<int>();
            var sourceB = NewSource<int>();
            var sourceC = NewSource<int>();
            queue.Track(sourceA.Task, null, r => order.Add("A"));
            queue.Track(sourceB.Task, null, r => order.Add("B"));
            queue.Track(sourceC.Task, null, r => order.Add("C"));

            // Act — complete in a different order than tracked.
            sourceC.SetResult(3);
            sourceA.SetResult(1);
            sourceB.SetResult(2);
            queue.Drain(1u);

            // Assert
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, order);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void Drain_TaskIncomplete_NoCallbackAndStaysInFlight()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue(LONG_WATCHDOG_TICKS);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);

            // Act
            queue.Drain(1u);
            queue.Drain(2u);
            queue.Drain(3u);

            // Assert (the long watchdog is nowhere near elapsed)
            Assert.AreEqual(0, recorder.Results.Count);
            Assert.AreEqual(1, queue.InFlightCount);

            // Act — complete and drain.
            source.SetResult(1);
            queue.Drain(4u);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        // =========================================================================================
        // Fault / cancellation / throwing callback / reentrant Track
        // =========================================================================================

        [Test]
        public void Drain_TaskFaulted_ReportsFaultedWithSameException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            var failure = new InvalidOperationException("db down");
            queue.Track(source.Task, null, recorder.OnResult);

            // Act
            source.SetException(failure);
            queue.Drain(1u);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Faulted, recorder.Results[0].Status);
            Assert.AreSame(failure, recorder.Results[0].Error);
            Assert.AreEqual(default(int), recorder.Results[0].Value);
        }

        [Test]
        public void Drain_TaskCanceled_ReportsCanceledWithTaskCanceledException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);

            // Act
            source.SetCanceled();
            queue.Drain(1u);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Canceled, recorder.Results[0].Status);
            Assert.IsInstanceOf<TaskCanceledException>(recorder.Results[0].Error);
            Assert.AreEqual(default(int), recorder.Results[0].Value);
        }

        [Test]
        public void Drain_CallbackThrows_LogsErrorAndRemainingCallbacksStillRun()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorder = new Recorder<int>();
            queue.Track(Task.FromResult(1), null, r => throw new InvalidOperationException("callback boom"));
            queue.Track(Task.FromResult(2), null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex(@"\[TickCompletionQueue\] Completion callback threw"));

            // Act
            TestDelegate drain = () => queue.Drain(1u);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(1, recorder.Results.Count, "The second callback must still run.");
            Assert.AreEqual(2, recorder.Results[0].Value);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void Drain_CallbackTracksAnotherTask_NewTaskHandledOnNextDrainOnly()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorderB = new Recorder<int>();
            queue.Track(Task.FromResult(1), null, r => queue.Track(Task.FromResult(2), null, recorderB.OnResult));

            // Act
            queue.Drain(1u);
            int callsAfterFirstDrain = recorderB.Results.Count;
            int inFlightAfterFirstDrain = queue.InFlightCount;
            queue.Drain(2u);

            // Assert
            Assert.AreEqual(0, callsAfterFirstDrain, "A task tracked during a Drain must not be handled in that same Drain.");
            Assert.AreEqual(1, inFlightAfterFirstDrain);
            Assert.AreEqual(1, recorderB.Results.Count);
            Assert.AreEqual(2, recorderB.Results[0].Value);
        }

        // =========================================================================================
        // Watchdog
        // =========================================================================================

        [Test]
        public void Drain_WatchdogExpires_CancelsTokenAlertsAndReportsTimedOutOnce()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            var cancellation = new CancellationTokenSource();
            queue.Track(source.Task, cancellation, recorder.OnResult);

            // Act / Assert — nothing before the deadline (10 + 3 = 13).
            queue.Drain(TRACK_TICK + 1u);
            queue.Drain(TRACK_TICK + 2u);
            Assert.AreEqual(0, recorder.Results.Count);
            Assert.IsFalse(cancellation.IsCancellationRequested);

            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            Assert.IsTrue(cancellation.IsCancellationRequested, "The watchdog must cancel the token.");
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
            Assert.IsInstanceOf<TimeoutException>(recorder.Results[0].Error);
            StringAssert.Contains(WATCHDOG_TICKS.ToString(), recorder.Results[0].Error.Message);
            Assert.AreEqual(default(int), recorder.Results[0].Value);
            Assert.AreEqual(0, queue.InFlightCount, "A timed-out task no longer counts in flight.");

            queue.Drain(TRACK_TICK + WATCHDOG_TICKS + 1u);
            Assert.AreEqual(1, recorder.Results.Count, "The callback must run only once.");
            cancellation.Dispose();
        }

        [Test]
        public void Drain_WatchdogWithNullCancellation_ReportsTimedOutWithoutException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var recorder = new Recorder<int>();
            queue.Track(NewSource<int>().Task, null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));

            // Act
            TestDelegate drain = () => queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
        }

        [Test]
        public void Drain_CancelThrows_LogsErrorAndStillReportsTimedOut()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var recorder = new Recorder<int>();
            var cancellation = new CancellationTokenSource();
            cancellation.Dispose(); // Cancel() on a disposed source throws ObjectDisposedException.
            queue.Track(NewSource<int>().Task, cancellation, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("Failed to cancel the token"));
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));

            // Act
            TestDelegate drain = () => queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
        }

        [Test]
        public void Drain_TaskCompletesOnWatchdogTick_ReportsCompletedNotTimedOut()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS - 1u);

            // Act — completes right before the tick on which the watchdog would fire; no log expected.
            source.SetResult(5);
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Completed, recorder.Results[0].Status);
            Assert.AreEqual(5, recorder.Results[0].Value);
        }

        [Test]
        public void Drain_WatchdogAcrossTickWraparound_FiresAtTickOne()
        {
            // Arrange — tracked at uint.MaxValue - 1 with watchdog 3: deadline wraps to tick 1.
            TickCompletionQueue queue = CreateQueue(WATCHDOG_TICKS, uint.MaxValue - 1u);
            var recorder = new Recorder<int>();
            queue.Track(NewSource<int>().Task, null, recorder.OnResult);

            // Act / Assert — not yet due at MaxValue and 0.
            queue.Drain(uint.MaxValue);
            queue.Drain(0u);
            Assert.AreEqual(0, recorder.Results.Count);

            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(1u);

            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
        }

        [Test]
        public void Drain_TimedOutTaskFaultsLater_LogsAndRunsNoSecondCallback()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Act
            source.SetException(new InvalidOperationException("late failure"));
            LogAssert.Expect(LogType.Error, new Regex("late completion of a timed-out task"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS + 1u);
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS + 2u); // entry already dropped: no further log

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void Drain_TimedOutTaskCompletesLater_LogsAndRunsNoSecondCallback()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Act
            source.SetResult(9);
            LogAssert.Expect(LogType.Error, new Regex("late completion of a timed-out task"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS + 1u);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
        }

        // =========================================================================================
        // Tick thread / allocation
        // =========================================================================================

        [Test]
        public void Track_OffTickThread_ThrowsInvalidOperationException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            _threadId = OTHER_THREAD_ID;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => queue.Track(Task.FromResult(1), null, new Recorder<int>().OnResult));
        }

        [Test]
        public void Drain_OffTickThread_ThrowsInvalidOperationException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            _threadId = OTHER_THREAD_ID;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => queue.Drain(1u));
        }

        [Test]
        public void DrainOnShutdown_OffTickThread_ThrowsInvalidOperationException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            _threadId = OTHER_THREAD_ID;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => queue.DrainOnShutdown(TimeSpan.Zero));
        }

        [Test]
        public void Drain_EmptyQueue_DoesNotAllocate()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(1u); // warm-up / JIT

            // Act / Assert
            Assert.That(() => queue.Drain(2u), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Drain_OneIncompleteTaskInFlight_DoesNotAllocate()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue(LONG_WATCHDOG_TICKS);
            queue.Track(NewSource<int>().Task, null, new Recorder<int>().OnResult);
            queue.Drain(1u); // warm-up / JIT

            // Act / Assert
            Assert.That(() => queue.Drain(2u), Is.Not.AllocatingGCMemory());
            Assert.AreEqual(1, queue.InFlightCount);
        }

        // =========================================================================================
        // Shutdown
        // =========================================================================================

        [Test]
        public void DrainOnShutdown_AllTasksFinishedOneFaulted_BothCallbacksRunNoExceptionEscapes()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var failure = new InvalidOperationException("write failed");
            var faulted = NewSource<int>();
            var succeeded = NewSource<int>();
            var faultedRecorder = new Recorder<int>();
            var succeededRecorder = new Recorder<int>();
            queue.Track(faulted.Task, null, faultedRecorder.OnResult);
            queue.Track(succeeded.Task, null, succeededRecorder.OnResult);
            faulted.SetException(failure);
            succeeded.SetResult(8);

            // Act
            TestDelegate shutdown = () => queue.DrainOnShutdown(TimeSpan.FromSeconds(1));

            // Assert
            Assert.DoesNotThrow(shutdown);
            Assert.AreEqual(1, faultedRecorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Faulted, faultedRecorder.Results[0].Status);
            Assert.AreSame(failure, faultedRecorder.Results[0].Error);
            Assert.AreEqual(1, succeededRecorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Completed, succeededRecorder.Results[0].Status);
            Assert.AreEqual(8, succeededRecorder.Results[0].Value);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void DrainOnShutdown_OneTaskIncomplete_LogsItNoCallbackAndHandlesTheFinishedOne()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var incompleteRecorder = new Recorder<int>();
            var finishedRecorder = new Recorder<int>();
            queue.Track(NewSource<int>().Task, null, incompleteRecorder.OnResult);
            queue.Track(Task.FromResult(4), null, finishedRecorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("still incomplete at shutdown"));

            // Act
            TestDelegate shutdown = () => queue.DrainOnShutdown(TimeSpan.Zero);

            // Assert
            Assert.DoesNotThrow(shutdown);
            Assert.AreEqual(0, incompleteRecorder.Results.Count, "An incomplete task gets no callback.");
            Assert.AreEqual(1, finishedRecorder.Results.Count);
            Assert.AreEqual(4, finishedRecorder.Results[0].Value);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void DrainOnShutdown_TaskCanceled_ReportsCanceled()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            source.SetCanceled();

            // Act
            TestDelegate shutdown = () => queue.DrainOnShutdown(TimeSpan.Zero);

            // Assert
            Assert.DoesNotThrow(shutdown);
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.Canceled, recorder.Results[0].Status);
            Assert.IsInstanceOf<TaskCanceledException>(recorder.Results[0].Error);
        }

        [Test]
        public void DrainOnShutdown_NegativeOrInfiniteTimeout_ThrowsArgumentOutOfRange()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Track(NewSource<int>().Task, null, new Recorder<int>().OnResult);

            // Act / Assert — the wait must be bounded.
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.DrainOnShutdown(TimeSpan.FromMilliseconds(-5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.DrainOnShutdown(Timeout.InfiniteTimeSpan));
            Assert.AreEqual(1, queue.InFlightCount, "A rejected call must not touch the queue.");
        }

        [Test]
        public void DrainOnShutdown_OversizedTimeout_IsCappedAndFinishedTaskIsHandled()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorder = new Recorder<int>();
            queue.Track(Task.FromResult(6), null, recorder.OnResult);

            // Act
            TestDelegate shutdown = () => queue.DrainOnShutdown(TimeSpan.MaxValue);

            // Assert
            Assert.DoesNotThrow(shutdown);
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(6, recorder.Results[0].Value);
        }

        [Test]
        public void DrainOnShutdown_CallbackTracksAnotherTask_AlertsAndDropsItWithoutCallback()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorderB = new Recorder<int>();
            queue.Track(Task.FromResult(1), null, r => queue.Track(Task.FromResult(2), null, recorderB.OnResult));
            LogAssert.Expect(LogType.Error, new Regex("tracked by a completion callback during the shutdown drain"));

            // Act
            queue.DrainOnShutdown(TimeSpan.Zero);

            // Assert
            Assert.AreEqual(0, recorderB.Results.Count);
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void DrainOnShutdown_TimedOutTaskStillIncomplete_AlertsOnce()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var recorder = new Recorder<int>();
            queue.Track(NewSource<int>().Task, null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);
            LogAssert.Expect(LogType.Error, new Regex("timed-out task never completed before shutdown"));

            // Act
            queue.DrainOnShutdown(TimeSpan.Zero);
            queue.DrainOnShutdown(TimeSpan.Zero); // already dropped: no second alert

            // Assert
            Assert.AreEqual(1, recorder.Results.Count, "Only the TimedOut callback ran.");
        }

        [Test]
        public void DrainOnShutdown_TimedOutTaskCompletedLate_LogsLateCompletionNoSecondCallback()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            var recorder = new Recorder<int>();
            queue.Track(source.Task, null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);
            source.SetResult(3);
            LogAssert.Expect(LogType.Error, new Regex("late completion of a timed-out task"));

            // Act
            queue.DrainOnShutdown(TimeSpan.Zero);

            // Assert
            Assert.AreEqual(1, recorder.Results.Count);
            Assert.AreEqual(TickTaskStatus.TimedOut, recorder.Results[0].Status);
        }

        // =========================================================================================
        // Reentrancy / mixed order / constructor guard
        // =========================================================================================

        [Test]
        public void Drain_CallbackCallsDrain_IsRejectedLoggedAndRemainingCallbacksStillRun()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorder = new Recorder<int>();
            queue.Track(Task.FromResult(1), null, r => queue.Drain(2u));
            queue.Track(Task.FromResult(2), null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex(@"Completion callback threw[\s\S]*must not be called from a completion callback"));

            // Act
            TestDelegate drain = () => queue.Drain(1u);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(1, recorder.Results.Count, "The second task must be handled exactly once.");
            Assert.AreEqual(0, queue.InFlightCount);
        }

        [Test]
        public void Drain_CallbackCallsDrainOnShutdown_IsRejectedLoggedAndRemainingCallbacksStillRun()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            var recorder = new Recorder<int>();
            queue.Track(Task.FromResult(1), null, r => queue.DrainOnShutdown(TimeSpan.Zero));
            queue.Track(Task.FromResult(2), null, recorder.OnResult);
            LogAssert.Expect(LogType.Error, new Regex(@"Completion callback threw[\s\S]*must not be called from a completion callback"));

            // Act
            TestDelegate drain = () => queue.Drain(1u);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(1, recorder.Results.Count);
        }

        [Test]
        public void Drain_MixedTimedOutCompletedAndIncomplete_HandlesEachInTrackOrder()
        {
            // Arrange — T1, C, T2 tracked at tick 10 (deadline 13); I tracked at tick 12 (deadline 15).
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var order = new List<string>();
            var completed = NewSource<int>();
            queue.Track(NewSource<int>().Task, null, r => order.Add("T1:" + r.Status));
            queue.Track(completed.Task, null, r => order.Add("C:" + r.Status));
            queue.Track(NewSource<int>().Task, null, r => order.Add("T2:" + r.Status));
            queue.Drain(TRACK_TICK + 2u);
            queue.Track(NewSource<int>().Task, null, r => order.Add("I:" + r.Status));
            completed.SetResult(1);
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));

            // Act
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Assert
            CollectionAssert.AreEqual(new[] { "T1:TimedOut", "C:Completed", "T2:TimedOut" }, order);
            Assert.AreEqual(1, queue.InFlightCount, "Only the later-tracked incomplete task is still in flight.");
        }

        [Test]
        public void Drain_CallbackThrowsForTimedOutResult_LogsAndLateCompletionIsStillObserved()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();
            queue.Drain(TRACK_TICK);
            var source = NewSource<int>();
            queue.Track(source.Task, null, r => throw new InvalidOperationException("callback boom"));
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL infrastructure alert"));
            LogAssert.Expect(LogType.Error, new Regex("Completion callback threw"));

            // Act
            TestDelegate drain = () => queue.Drain(TRACK_TICK + WATCHDOG_TICKS);

            // Assert
            Assert.DoesNotThrow(drain);
            Assert.AreEqual(0, queue.InFlightCount);

            source.SetResult(1);
            LogAssert.Expect(LogType.Error, new Regex("late completion of a timed-out task"));
            queue.Drain(TRACK_TICK + WATCHDOG_TICKS + 1u);
        }

        [Test]
        public void Constructor_WatchdogBelowOne_ThrowsArgumentOutOfRange()
        {
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateQueue(watchdogTicks: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateQueue(watchdogTicks: -1));
        }

        // =========================================================================================
        // Arguments / constants
        // =========================================================================================

        [Test]
        public void Track_NullTask_ThrowsArgumentNullException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => queue.Track<int>(null, null, new Recorder<int>().OnResult));
        }

        [Test]
        public void Track_NullCallback_ThrowsArgumentNullException()
        {
            // Arrange
            TickCompletionQueue queue = CreateQueue();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => queue.Track(Task.FromResult(1), null, null));
        }

        [Test]
        public void Constants_HaveSpecifiedValues()
        {
            // Assert
            Assert.AreEqual(200, TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS);
            Assert.AreEqual(16, TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER);
        }

        // =========================================================================================
        // Integration with ServerTickLoop
        // =========================================================================================

        [Test]
        public void Drain_RegisteredFirstOnTickLoop_CallbackRunsBeforeGameLogicOnTickAfterCompletion()
        {
            // Arrange
            var tickLoop = new ServerTickLoop();
            TickCompletionQueue queue = CreateQueue();
            var order = new List<string>();
            var source = NewSource<int>();
            queue.Track(source.Task, null, r => order.Add("callback"));
            tickLoop.RegisterTickDriven(dt => queue.Drain(tickLoop.ServerTickNumber));
            tickLoop.RegisterTickDriven(dt => order.Add("game"));
            tickLoop.AdvanceTick();

            // Act
            source.SetResult(1);
            tickLoop.AdvanceTick();

            // Assert
            CollectionAssert.AreEqual(new[] { "game", "callback", "game" }, order);
        }
    }
}
