using System;
using System.Threading;
using System.Threading.Tasks;

namespace IronGrind.Networking
{
    /// <summary>
    /// Holds started persistence tasks together with a completion callback and hands each result
    /// back on the tick thread, at a known point of the tick (ADR-011 Decision 2). Tick-driven code
    /// never awaits a task: it passes the task to <see cref="Track{T}"/> and the next
    /// <see cref="Drain"/> after completion invokes the callback.
    /// </summary>
    /// <remarks>
    /// Not thread-safe by design: every member must be called from the thread the queue was
    /// created on (the tick thread) and throws <see cref="InvalidOperationException"/> otherwise.
    /// </remarks>
    /// <example>
    /// <code>
    /// var cts = new CancellationTokenSource();
    /// Task&lt;bool&gt; write = persistence.SaveIrreversibleOutcome(snapshot, cts.Token);
    /// queue.Track(write, cts, OnWriteFinished);
    /// // each tick, before game logic:
    /// queue.Drain(tickLoop.ServerTickNumber);
    /// </code>
    /// </example>
    public interface ITickCompletionQueue
    {
        /// <summary>
        /// Tracks a started task; the callback runs on the tick thread, in <see cref="Drain"/>, once the task has completed.
        /// A task that is already complete is handled by the next <see cref="Drain"/>, never inside this call.
        /// </summary>
        /// <typeparam name="T">The task's result type.</typeparam>
        /// <param name="task">The started task. Must not be null.</param>
        /// <param name="cancellation">
        /// Cancelled by the watchdog when the task times out. May be null. The caller owns it; the queue never disposes it.
        /// </param>
        /// <param name="onCompletedOnTick">One-shot callback. Must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> or <paramref name="onCompletedOnTick"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Called off the tick thread.</exception>
        void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick);

        /// <summary>
        /// Called once per tick before any game logic. Never throws for a task or callback failure;
        /// throws <see cref="InvalidOperationException"/> if called off the tick thread or from a
        /// completion callback.
        /// </summary>
        /// <param name="currentTick">The current server tick, used for the watchdog.</param>
        void Drain(uint currentTick);

        /// <summary>Number of tracked tasks not yet handled. Timed-out tasks are not counted.</summary>
        int InFlightCount { get; }

        /// <summary>
        /// Zone teardown step: one bounded blocking wait on all tracked tasks (timed-out ones included),
        /// then one last pass so every finished task (including failed ones) gets its callback. Tasks
        /// still incomplete after the timeout are logged and alerted and get no callback. A task tracked
        /// by a callback during this call is not waited on: it is alerted and dropped. The queue is
        /// empty afterwards. The only blocking wait on a task in the server.
        /// </summary>
        /// <param name="timeout">Upper bound of the wait. Not negative, not infinite; capped at <see cref="int.MaxValue"/> ms.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative (this includes <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>).</exception>
        /// <exception cref="InvalidOperationException">Called off the tick thread or from a completion callback.</exception>
        void DrainOnShutdown(TimeSpan timeout);
    }
}
