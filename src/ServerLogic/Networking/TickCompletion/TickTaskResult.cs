using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// How a tracked task ended, as reported to a completion callback of
    /// <see cref="ITickCompletionQueue"/> (ADR-011 Decision 2).
    /// </summary>
    public enum TickTaskStatus
    {
        /// <summary>The task ran to completion; <see cref="TickTaskResult{T}.Value"/> is valid.</summary>
        Completed,

        /// <summary>The task faulted; <see cref="TickTaskResult{T}.Error"/> carries the exception.</summary>
        Faulted,

        /// <summary>The task was cancelled; <see cref="TickTaskResult{T}.Error"/> is a <see cref="System.Threading.Tasks.TaskCanceledException"/>.</summary>
        Canceled,

        /// <summary>
        /// The task was still incomplete when the watchdog expired; <see cref="TickTaskResult{T}.Error"/>
        /// is a <see cref="TimeoutException"/>. The outcome of the underlying operation is unknown.
        /// </summary>
        TimedOut
    }

    /// <summary>
    /// The result of a tracked task, handed to the completion callback on the tick thread.
    /// </summary>
    /// <typeparam name="T">The task's result type.</typeparam>
    /// <example>
    /// <code>
    /// void OnSaved(TickTaskResult&lt;bool&gt; result)
    /// {
    ///     if (result.Status == TickTaskStatus.Completed &amp;&amp; result.Value) { /* success */ }
    ///     else { /* roll back; result.Error says why */ }
    /// }
    /// </code>
    /// </example>
    public readonly struct TickTaskResult<T>
    {
        /// <summary>Creates a result. Used by the queue; public so tests and fakes can construct one.</summary>
        /// <param name="status">How the task ended.</param>
        /// <param name="value">The task value; pass <c>default</c> unless <paramref name="status"/> is <see cref="TickTaskStatus.Completed"/>.</param>
        /// <param name="error">The failure; <see langword="null"/> when completed.</param>
        public TickTaskResult(TickTaskStatus status, T value, Exception error)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        /// <summary>How the task ended.</summary>
        public TickTaskStatus Status { get; }

        /// <summary>The task's value. Valid only when <see cref="Status"/> is <see cref="TickTaskStatus.Completed"/>; <c>default</c> otherwise.</summary>
        public T Value { get; }

        /// <summary>The failure. <see langword="null"/> when <see cref="Status"/> is <see cref="TickTaskStatus.Completed"/>.</summary>
        public Exception Error { get; }
    }
}
