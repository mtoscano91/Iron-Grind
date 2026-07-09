using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// A single message queued for delivery via <see cref="PriorityPathQueue{T}"/> — the priority
    /// (R-OD) path's per-tick queue element (CR-NET-7.7). Wraps an opaque payload of type
    /// <typeparamref name="T"/> together with the exemption flag it was enqueued with.
    /// </summary>
    /// <typeparam name="T">
    /// The message payload type. This story does not define a concrete message schema — no real
    /// <c>EnhancementOutcomeBroadcast</c> type exists yet in this codebase (that arrives with
    /// Story 011 and future Enhancement/Leveling epics) — so callers supply whatever payload type
    /// is appropriate at their call site.
    /// </typeparam>
    /// <remarks>
    /// <see langword="readonly struct"/> — no heap allocation for the wrapper itself; only the
    /// backing <see cref="System.Collections.Generic.List{T}"/> storage inside
    /// <see cref="PriorityPathQueue{T}"/> allocates.
    /// </remarks>
    /// <example>
    /// <code>
    /// var queue = new PriorityPathQueue&lt;string&gt;();
    /// queue.Enqueue("DamageEvent#1", isExempt: false);
    /// IReadOnlyList&lt;QueuedMessage&lt;string&gt;&gt; flushed = queue.Flush(tickNumber: 100);
    /// string firstPayload = flushed[0].Payload;
    /// </code>
    /// </example>
    public readonly struct QueuedMessage<T> : IEquatable<QueuedMessage<T>>
    {
        /// <summary>The enqueued message payload.</summary>
        public readonly T Payload;

        /// <summary>
        /// <see langword="true"/> if this message was enqueued via the enhancement-path exemption
        /// (queue-jumps to the front of a tick's flush rather than competing for one of the
        /// <see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/> non-exempt slots — see
        /// <see cref="PriorityPathQueue{T}"/> remarks for the full exemption model, including the
        /// separate bulk-transfer exemption this type does not model).
        /// </summary>
        public readonly bool IsExempt;

        /// <summary>Initializes a new <see cref="QueuedMessage{T}"/>.</summary>
        /// <example>
        /// <code>
        /// var queued = new QueuedMessage&lt;string&gt;("EnhancementOutcome#7", isExempt: true);
        /// </code>
        /// </example>
        public QueuedMessage(T payload, bool isExempt)
        {
            Payload = payload;
            IsExempt = isExempt;
        }

        /// <inheritdoc/>
        public bool Equals(QueuedMessage<T> other)
            => EqualityComparer<T>.Default.Equals(Payload, other.Payload) && IsExempt == other.IsExempt;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is QueuedMessage<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = Payload is null ? 0 : Payload.GetHashCode();
            h = (h * 397) ^ IsExempt.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both queued messages have identical field values.</summary>
        public static bool operator ==(QueuedMessage<T> left, QueuedMessage<T> right) => left.Equals(right);

        /// <summary>Returns true if the queued messages differ in payload or exemption flag.</summary>
        public static bool operator !=(QueuedMessage<T> left, QueuedMessage<T> right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"QueuedMessage(Payload={Payload}, IsExempt={IsExempt})";
    }
}
