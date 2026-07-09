using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Per-client, per-tick priority-path (R-OD / "Path 1") message queue (CR-NET-7.7, Story 006).
    /// Enqueue messages any time during a tick via <see cref="Enqueue"/>; the (not-yet-built) tick
    /// loop (Story 009) calls <see cref="Flush"/> exactly once per tick to obtain that tick's
    /// capture. One instance of this class belongs to exactly one client connection — this is a
    /// genuinely stateful queue, not a stateless codec utility like its sibling types in this
    /// folder (<see cref="WireIdCodec"/>, <see cref="WireEnumCodec"/>, <see cref="MessageEnvelopeCodec"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Transport-agnostic (Engine Notes):</b> this class contains pure C# queue/data-structure
    /// logic only. It does not reference NGO or any concrete <c>NetworkDelivery</c> value — that
    /// binding is explicitly deferred to implementation-time engine verification against
    /// <c>docs/engine-reference/unity</c> and is out of scope for this story.
    /// </para>
    /// <para>
    /// <b>Two exemption categories exist per CR-NET-7.7 — only one is implemented here:</b>
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b>Enhancement-path exemption</b> (<c>EnhancementRequestReceived</c> + outcome broadcast):
    /// a <i>priority-ordering</i> mechanism <i>within</i> the fixed <see cref="PRIORITY_PATH_CAP"/>-slot
    /// cap, not a capacity-increasing one. Pass <c>isExempt: true</c> to <see cref="Enqueue"/> for
    /// these messages. They always occupy the front of the tick's <see cref="Flush"/> output in
    /// enqueue order and are never deferred. If the cap would otherwise be exceeded, the
    /// <i>oldest</i> already-queued non-exempt message for that tick is displaced to the next
    /// tick's deferred backlog (see AC-NC-35) — this is the only exemption behavior this story's
    /// three blocking ACs exercise, and the only one implemented by <see cref="Flush"/>.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Bulk-transfer exemption</b> (<c>0xF000–0xFFFF</c> fragment range): a connection-phase,
    /// one-time transmission that is described as "never subject to the 8-cap" and is a fully
    /// separate transmission path from this per-tick priority queue, not additive capacity within
    /// it. <b>This class does not model bulk-transfer at all</b> — there is no bulk-transfer
    /// parameter on <see cref="Enqueue"/>, and none of this story's blocking ACs exercise it. A
    /// future story that implements the bulk-transfer transmission path should not route it
    /// through this queue.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Documentation inconsistency flagged and resolved</b> (the second such resolution in
    /// this epic, after Story 005's AC-NC-36 numbering correction): the story's Implementation
    /// Notes give the formula
    /// <c>PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued</c>, which read
    /// literally says the per-tick flush count <i>grows</i> by however many exempt messages are
    /// queued (e.g. 8 non-exempt + 1 exempt = 9 total flushed, nothing displaced). This directly
    /// contradicts the story's own <b>AC-NC-35</b> (BLOCKING), which explicitly and repeatedly
    /// (both in the AC text and independently again in the QA Test Cases section) describes
    /// <i>displacement</i>: at 8-cap capacity, adding 1 exempt message still results in only 8
    /// total messages flushed that tick (the exempt one plus 7 of the original 8 non-exempt, with
    /// the oldest bumped to the next tick) — not 9. Resolution adopted by this implementation:
    /// the <c>PathCapacity_effective</c> formula most likely describes the bulk-transfer case (or
    /// a different metric this story does not need to implement), not the enhancement-path case.
    /// <see cref="Flush"/> implements AC-NC-35's explicit displacement behavior, and the
    /// <c>PathCapacity_effective</c> formula is <i>not</i> implemented literally by this class.
    /// </para>
    /// <para>
    /// <b>Flush ordering rule</b> (needed to satisfy AC-NC-34/AC-NC-35/AC-CCR-05 together — not
    /// spelled out verbatim in the story, but the only consistent reading of "preserving relative
    /// emission order within the deferred set" combined with AC-NC-35's displacement rule). Each
    /// <see cref="Flush"/> call combines, in this priority order, then takes the first
    /// <see cref="PRIORITY_PATH_CAP"/>:
    /// </para>
    /// <list type="number">
    /// <item><description>Exempt messages enqueued this tick, in enqueue order — always included, always first.</description></item>
    /// <item><description>Non-exempt messages carried over (deferred) from a previous tick's overflow, in original relative order.</description></item>
    /// <item><description>Non-exempt messages newly enqueued this tick, in enqueue order.</description></item>
    /// </list>
    /// <para>
    /// If exempt messages this tick would otherwise push the non-exempt baseline (groups 2+3,
    /// capped at <see cref="PRIORITY_PATH_CAP"/>) over capacity, the displacement evicts from the
    /// <i>front</i> (oldest) of that baseline — per AC-NC-35 — not the tail. Evicted items become
    /// the highest-priority entries of the next tick's deferred backlog, ahead of anything that
    /// was already waiting beyond the cap. Whatever does not fit becomes the new deferred backlog
    /// for the next <see cref="Flush"/> call, preserving relative order. Exempt messages
    /// themselves are never deferred.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var queue = new PriorityPathQueue&lt;string&gt;();
    /// for (int i = 1; i &lt;= 12; i++)
    /// {
    ///     queue.Enqueue($"Msg#{i}", isExempt: false);
    /// }
    ///
    /// IReadOnlyList&lt;QueuedMessage&lt;string&gt;&gt; tickN = queue.Flush(tickNumber: 100);   // Msg#1..Msg#8
    /// IReadOnlyList&lt;QueuedMessage&lt;string&gt;&gt; tickN1 = queue.Flush(tickNumber: 101);  // Msg#9..Msg#12
    /// </code>
    /// </example>
    public sealed class PriorityPathQueue<T>
    {
        /// <summary>
        /// Non-exempt R-OD message ceiling per destination client per tick (CR-NET-7.7 tuning
        /// knob; safe range [4,16]). Deferred (never dropped) excess is carried to the next
        /// <see cref="Flush"/> call. Changing this value requires a recompile — it is not read
        /// from external config, matching this folder's other wire-protocol structural constants
        /// (e.g. <see cref="ServerMessageEnvelope.WireSize"/>).
        /// </summary>
        public const int PRIORITY_PATH_CAP = 8;

        private readonly List<QueuedMessage<T>> _pendingExempt = new List<QueuedMessage<T>>();
        private readonly List<QueuedMessage<T>> _pendingNonExempt = new List<QueuedMessage<T>>();
        private List<QueuedMessage<T>> _deferredNonExempt = new List<QueuedMessage<T>>();

        /// <summary>
        /// Enqueues <paramref name="message"/> for the current (not-yet-flushed) tick.
        /// </summary>
        /// <param name="message">The message payload to enqueue.</param>
        /// <param name="isExempt">
        /// <see langword="true"/> for the enhancement-path exemption (queue-jumps to the front of
        /// this tick's <see cref="Flush"/> output — see class remarks). <see langword="false"/>
        /// for an ordinary non-exempt message subject to <see cref="PRIORITY_PATH_CAP"/>.
        /// </param>
        /// <example>
        /// <code>
        /// queue.Enqueue("DamageEvent#42", isExempt: false);
        /// queue.Enqueue("EnhancementOutcome#7", isExempt: true);
        /// </code>
        /// </example>
        public void Enqueue(T message, bool isExempt)
        {
            if (isExempt)
            {
                _pendingExempt.Add(new QueuedMessage<T>(message, true));
            }
            else
            {
                _pendingNonExempt.Add(new QueuedMessage<T>(message, false));
            }
        }

        /// <summary>
        /// Captures this tick's flush: up to <see cref="PRIORITY_PATH_CAP"/> <i>non-exempt</i>
        /// messages, plus all exempt messages pending this tick (exempt admission is uncapped —
        /// see class remarks), ordered per the class-level Flush ordering rule. Intended to be
        /// called exactly once per tick by the tick loop (Story 009); calling it more or less
        /// often than once per tick does not corrupt state, but breaks the "once per tick"
        /// contract the ordering rule assumes.
        /// </summary>
        /// <param name="tickNumber">
        /// The server tick this flush corresponds to. The queue's ordering logic does not depend
        /// on tick numbers being contiguous or even monotonically increasing — only on
        /// <see cref="Flush"/> being called once per real tick, in real tick order — so this
        /// parameter is accepted for the tick loop's own bookkeeping/logging rather than consumed
        /// by the algorithm itself.
        /// </param>
        /// <returns>
        /// This tick's messages, in the class-level Flush ordering rule's priority order. The
        /// non-exempt portion never exceeds <see cref="PRIORITY_PATH_CAP"/>, but exempt messages
        /// are added on top uncapped — if more than <see cref="PRIORITY_PATH_CAP"/> exempt messages
        /// are pending in a single tick (an unrate-limited condition this queue does not itself
        /// guard against; upstream business logic is responsible for that), the total returned can
        /// exceed <see cref="PRIORITY_PATH_CAP"/>. Never <see langword="null"/>; empty if nothing
        /// was queued or deferred.
        /// </returns>
        /// <example>
        /// <code>
        /// // AC-NC-35: queue already at 8 non-exempt for this tick; one exempt enqueued before flush.
        /// for (int i = 1; i &lt;= 8; i++) queue.Enqueue($"NonExempt#{i}", isExempt: false);
        /// queue.Enqueue("EnhancementOutcome", isExempt: true);
        ///
        /// IReadOnlyList&lt;QueuedMessage&lt;string&gt;&gt; tickN = queue.Flush(tickNumber: 100);
        /// // tickN[0].Payload == "EnhancementOutcome"
        /// // tickN[1..7] == "NonExempt#2".."NonExempt#8" (NonExempt#1 displaced)
        ///
        /// IReadOnlyList&lt;QueuedMessage&lt;string&gt;&gt; tickN1 = queue.Flush(tickNumber: 101);
        /// // tickN1[0].Payload == "NonExempt#1"
        /// </code>
        /// </example>
        public IReadOnlyList<QueuedMessage<T>> Flush(uint tickNumber)
        {
            // Step 1: baseline non-exempt assignment, as if no exempt messages existed this tick —
            // oldest-first FIFO: previously-deferred backlog first, then this tick's new
            // non-exempt enqueues, in original relative order.
            var combinedNonExempt = new List<QueuedMessage<T>>(_deferredNonExempt.Count + _pendingNonExempt.Count);
            combinedNonExempt.AddRange(_deferredNonExempt);
            combinedNonExempt.AddRange(_pendingNonExempt);

            int baselineCount = Math.Min(PRIORITY_PATH_CAP, combinedNonExempt.Count);

            // Step 2: exempt messages always claim the front of this tick's flush and are never
            // deferred. If the baseline non-exempt assignment would otherwise fill the tick to
            // capacity, exempt messages evict from the FRONT (oldest) of the baseline assignment —
            // not the tail — per AC-NC-35's explicit displacement narrative (see class remarks).
            int exemptCount = _pendingExempt.Count;
            int availableForNonExempt = Math.Max(0, PRIORITY_PATH_CAP - exemptCount);
            int evictedCount = Math.Max(0, baselineCount - availableForNonExempt);

            var result = new List<QueuedMessage<T>>(exemptCount + (baselineCount - evictedCount));
            result.AddRange(_pendingExempt);
            for (int i = evictedCount; i < baselineCount; i++)
            {
                result.Add(combinedNonExempt[i]);
            }

            // Step 3: build the next tick's deferred backlog — evicted front items first (they
            // were already queued before this tick's exempt collision, so they retain seniority
            // over anything not yet considered), followed by whatever was already beyond baseline
            // capacity. Relative order is preserved throughout.
            var nextDeferred = new List<QueuedMessage<T>>(evictedCount + (combinedNonExempt.Count - baselineCount));
            for (int i = 0; i < evictedCount; i++)
            {
                nextDeferred.Add(combinedNonExempt[i]);
            }
            for (int i = baselineCount; i < combinedNonExempt.Count; i++)
            {
                nextDeferred.Add(combinedNonExempt[i]);
            }

            _deferredNonExempt = nextDeferred;
            _pendingExempt.Clear();
            _pendingNonExempt.Clear();

            return result;
        }
    }
}
