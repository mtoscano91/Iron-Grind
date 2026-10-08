using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Default <see cref="ITickCompletionQueue"/> (ADR-011 Decision 2, Networking Core Story 030).
    /// Reads <see cref="Task.IsCompleted"/> and <see cref="Task.Status"/> only; installs no
    /// continuation and depends on no synchronization context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entries are kept in track order and handled entries are removed without reordering the rest.
    /// A callback may call <see cref="Track{T}"/>; an entry added during a <see cref="Drain"/> is
    /// handled by a later one. The watchdog deadline is stored as <c>trackedAtTick + watchdogTicks</c>
    /// and compared with <see cref="StaleDiscardComparer.IsTickExpired"/>, so <c>uint</c> wraparound
    /// is safe. A task that is complete on the same <see cref="Drain"/> as its deadline is reported
    /// as <see cref="TickTaskStatus.Completed"/>.
    /// </para>
    /// <para>
    /// Timed-out entries move to a separate list; when such a task later completes it is logged, its
    /// <see cref="Task.Exception"/> is read (so the fault is observed) and it is dropped without a callback.
    /// </para>
    /// <para>
    /// Not wired in: nothing in production constructs this class yet (no zone bootstrap exists).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var queue = new TickCompletionQueue();
    /// queue.Track(task, cts, OnWriteFinished);
    /// queue.Drain(tickLoop.ServerTickNumber);
    /// </code>
    /// </example>
    public sealed class TickCompletionQueue : ITickCompletionQueue
    {
        private const string LOG_PREFIX = "[TickCompletionQueue]";

        private readonly List<Entry> _inFlight = new List<Entry>();
        private readonly List<Entry> _timedOut = new List<Entry>();
        private readonly int _watchdogTicks;
        private readonly Func<int> _currentThreadId;
        private readonly int _tickThreadId;
        private uint _lastDrainTick;
        private bool _isDraining;

        /// <summary>Creates a queue bound to the calling thread (the tick thread).</summary>
        /// <param name="watchdogTicks">Watchdog length in ticks; at least 1. Defaults to <see cref="TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS"/>.</param>
        /// <param name="initialTick">The tick used for <see cref="Track{T}"/> before the first <see cref="Drain"/>.</param>
        /// <param name="currentThreadId">Thread id provider, called once here and on every member call. Defaults to the managed thread id; tests inject a fake.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="watchdogTicks"/> is below 1.</exception>
        public TickCompletionQueue(
            int watchdogTicks = TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS,
            uint initialTick = 0u,
            Func<int> currentThreadId = null)
        {
            if (watchdogTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(watchdogTicks), "The watchdog must be at least 1 tick.");
            }

            _watchdogTicks = watchdogTicks;
            _lastDrainTick = initialTick;
            _currentThreadId = currentThreadId ?? DefaultThreadId;
            _tickThreadId = _currentThreadId();
        }

        /// <inheritdoc/>
        public int InFlightCount => _inFlight.Count;

        /// <inheritdoc/>
        public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (onCompletedOnTick == null)
            {
                throw new ArgumentNullException(nameof(onCompletedOnTick));
            }

            AssertTickThread();
            uint deadline = unchecked(_lastDrainTick + (uint)_watchdogTicks);
            _inFlight.Add(new Entry<T>(task, cancellation, deadline, onCompletedOnTick));
        }

        /// <summary>
        /// Handles every tracked task that has completed or whose watchdog has expired, in track
        /// order. Never throws for a task or callback failure; throws
        /// <see cref="InvalidOperationException"/> if called off the tick thread or from a completion
        /// callback (the outer call catches and logs the latter). Allocates nothing when no task is
        /// handled.
        /// </summary>
        /// <param name="currentTick">The current server tick.</param>
        public void Drain(uint currentTick)
        {
            AssertTickThread();
            AssertNotDraining();
            _lastDrainTick = currentTick;

            _isDraining = true;
            try
            {
                ObserveLateCompletions();
                DrainInFlight(currentTick);
            }
            finally
            {
                _isDraining = false;
            }
        }

        /// <inheritdoc/>
        public void DrainOnShutdown(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The shutdown wait must be bounded: the timeout cannot be negative or infinite.");
            }

            AssertTickThread();
            AssertNotDraining();

            _isDraining = true;
            try
            {
                WaitForTrackedTasks(timeout);
                ObserveLateCompletions();
                AlertTimedOutTasksLeftAtShutdown();
                DeliverOrAlertInFlightAtShutdown(timeout);
                AlertTasksTrackedDuringShutdown();
            }
            finally
            {
                _isDraining = false;
            }
        }

        private static int DefaultThreadId() => Environment.CurrentManagedThreadId;

        private void DrainInFlight(uint currentTick)
        {
            int remaining = _inFlight.Count; // entries tracked by callbacks during this pass are appended past this bound
            int index = 0;
            while (index < remaining)
            {
                Entry entry = _inFlight[index];
                Task task = entry.Task;

                if (task.IsCompleted)
                {
                    _inFlight.RemoveAt(index);
                    remaining--;
                    Deliver(entry, StatusOf(task));
                }
                else if (StaleDiscardComparer.IsTickExpired(currentTick, entry.DeadlineTick))
                {
                    _inFlight.RemoveAt(index);
                    remaining--;
                    HandleTimeout(entry);
                }
                else
                {
                    index++;
                }
            }
        }

        // Waits on in-flight and timed-out tasks alike: a timed-out write may still be running, and
        // this is its last chance to finish before the process exits.
        private void WaitForTrackedTasks(TimeSpan timeout)
        {
            int inFlightCount = _inFlight.Count;
            int count = inFlightCount + _timedOut.Count;
            if (count == 0)
            {
                return;
            }

            var tasks = new Task[count];
            for (int i = 0; i < inFlightCount; i++)
            {
                tasks[i] = _inFlight[i].Task;
            }

            for (int i = inFlightCount; i < count; i++)
            {
                tasks[i] = _timedOut[i - inFlightCount].Task;
            }

            double totalMilliseconds = timeout.TotalMilliseconds;
            int milliseconds = totalMilliseconds >= int.MaxValue ? int.MaxValue : (int)totalMilliseconds;

            try
            {
                // The single blocking wait on a task in the whole server (ADR-011 Decision 2, Shutdown).
                Task.WaitAll(tasks, milliseconds);
            }
            catch (AggregateException)
            {
                // Faulted or cancelled tasks; each is reported through its own callback afterwards.
            }
        }

        private void AlertTimedOutTasksLeftAtShutdown()
        {
            for (int i = 0; i < _timedOut.Count; i++)
            {
                Debug.LogError($"{LOG_PREFIX} CRITICAL infrastructure alert: a timed-out task never completed before shutdown. " +
                    "The outcome of the operation is unknown.");
            }

            _timedOut.Clear();
        }

        private void DeliverOrAlertInFlightAtShutdown(TimeSpan timeout)
        {
            int remaining = _inFlight.Count; // entries tracked by callbacks during this pass stay behind it
            for (int handled = 0; handled < remaining; handled++)
            {
                Entry entry = _inFlight[0];
                _inFlight.RemoveAt(0);

                if (entry.Task.IsCompleted)
                {
                    Deliver(entry, StatusOf(entry.Task));
                }
                else
                {
                    Debug.LogError($"{LOG_PREFIX} CRITICAL infrastructure alert: a tracked task is still incomplete at shutdown " +
                        $"(waited up to {timeout.TotalMilliseconds:F0} ms). No callback will run; the outcome of the operation is unknown.");
                }
            }
        }

        private void AlertTasksTrackedDuringShutdown()
        {
            if (_inFlight.Count == 0)
            {
                return;
            }

            Debug.LogError($"{LOG_PREFIX} CRITICAL infrastructure alert: {_inFlight.Count} task(s) were tracked by a completion callback " +
                "during the shutdown drain. They are not waited on and get no callback; the outcome of each operation is unknown.");
            _inFlight.Clear();
        }

        private void AssertNotDraining()
        {
            if (_isDraining)
            {
                throw new InvalidOperationException(
                    "TickCompletionQueue.Drain and DrainOnShutdown must not be called from a completion callback.");
            }
        }

        private static TickTaskStatus StatusOf(Task task)
        {
            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    return TickTaskStatus.Completed;
                case TaskStatus.Canceled:
                    return TickTaskStatus.Canceled;
                default:
                    return TickTaskStatus.Faulted;
            }
        }

        private void AssertTickThread()
        {
            if (_currentThreadId() != _tickThreadId)
            {
                throw new InvalidOperationException(
                    "TickCompletionQueue must be used from the thread it was created on (the tick thread).");
            }
        }

        private void Deliver(Entry entry, TickTaskStatus status)
        {
            try
            {
                entry.Deliver(status, _watchdogTicks);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} Completion callback threw for a {status} task: {ex}");
            }
        }

        private void HandleTimeout(Entry entry)
        {
            if (entry.Cancellation != null)
            {
                try
                {
                    entry.Cancellation.Cancel();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} Failed to cancel the token of a timed-out task: {ex}");
                }
            }

            Debug.LogError($"{LOG_PREFIX} CRITICAL infrastructure alert: a tracked task was still incomplete " +
                $"{_watchdogTicks} ticks after it was tracked (watchdog). Reporting TimedOut; the outcome of the operation is unknown.");

            _timedOut.Add(entry);
            Deliver(entry, TickTaskStatus.TimedOut);
        }

        private void ObserveLateCompletions()
        {
            int index = 0;
            while (index < _timedOut.Count)
            {
                Task task = _timedOut[index].Task;
                if (!task.IsCompleted)
                {
                    index++;
                    continue;
                }

                Exception observed = task.Exception; // reading it marks the fault as observed
                Debug.LogError($"{LOG_PREFIX} late completion of a timed-out task (status {task.Status})" +
                    (observed != null ? $": {observed.GetBaseException()}" : string.Empty) + ". Ignored; no callback.");
                _timedOut.RemoveAt(index);
            }
        }

        private abstract class Entry
        {
            internal readonly Task Task;
            internal readonly CancellationTokenSource Cancellation;
            internal readonly uint DeadlineTick;

            protected Entry(Task task, CancellationTokenSource cancellation, uint deadlineTick)
            {
                Task = task;
                Cancellation = cancellation;
                DeadlineTick = deadlineTick;
            }

            internal abstract void Deliver(TickTaskStatus status, int watchdogTicks);

            protected Exception BuildError(TickTaskStatus status, int watchdogTicks)
            {
                switch (status)
                {
                    case TickTaskStatus.Completed:
                        return null;
                    case TickTaskStatus.Faulted:
                        return Task.Exception.GetBaseException();
                    case TickTaskStatus.Canceled:
                        return new TaskCanceledException(Task);
                    default:
                        return new TimeoutException($"The task did not complete within {watchdogTicks} ticks (persistence watchdog).");
                }
            }
        }

        private sealed class Entry<T> : Entry
        {
            private readonly Task<T> _typedTask;
            private readonly Action<TickTaskResult<T>> _callback;

            internal Entry(Task<T> task, CancellationTokenSource cancellation, uint deadlineTick, Action<TickTaskResult<T>> callback)
                : base(task, cancellation, deadlineTick)
            {
                _typedTask = task;
                _callback = callback;
            }

            internal override void Deliver(TickTaskStatus status, int watchdogTicks)
            {
                // Result is read only once the task is known to be RanToCompletion.
                T value = status == TickTaskStatus.Completed ? _typedTask.Result : default;
                _callback(new TickTaskResult<T>(status, value, BuildError(status, watchdogTicks)));
            }
        }
    }
}
