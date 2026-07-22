namespace IronGrind.Networking
{
    /// <summary>
    /// Issues the CCR-1 per-connection <c>SequenceNumber</c> envelope value (CR-NET-7.1): one
    /// monotonically increasing counter per connection, shared across <b>every</b> message type sent
    /// by that endpoint — never a separate counter per message type. One instance of this class
    /// belongs to exactly one connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Shared-by-construction:</b> this class exposes exactly one method, <see cref="IssueNext"/> —
    /// there is no per-message-type overload to misuse, no keyed dictionary of counters. Calling
    /// <see cref="IssueNext"/> for a <c>HeartbeatMessage</c> and then again for an
    /// <c>RttProbeEcho</c> (or any other message type) on the same instance necessarily returns
    /// sequential values, which is the entire CCR-1 guarantee (AC-CCR-02).
    /// </para>
    /// <para>
    /// Starts at 1 per CCR-1 (<c>0</c> = uninitialized, must never appear in a valid message). On
    /// wraparound from <see cref="uint.MaxValue"/> the next issued value is <c>1</c>, not <c>0</c> —
    /// consumers must compare with <see cref="StaleDiscardComparer.IsNewerVersion"/>, never raw
    /// <c>&gt;</c>, so this wraparound is handled correctly (CR-NET-7.5).
    /// </para>
    /// <para>
    /// <b>No production call site yet:</b> no live message-send/dispatch layer exists in this
    /// codebase yet to construct one instance per real connection and call <see cref="IssueNext"/>
    /// from it — the same class of forward-dependency gap this epic has repeatedly logged (see
    /// <c>docs/tech-debt-register.md</c>). This class exists now because CCR-1's shared-counter
    /// invariant needed a real, reusable, testable implementation (not just a test double) once a
    /// wiring story arrives; see the tech-debt register for the tracked forward-dependency entry.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var counter = new ConnectionSequenceCounter();
    /// uint heartbeatSeq = counter.IssueNext();   // 1
    /// uint rttProbeEchoSeq = counter.IssueNext(); // 2 — same shared counter, not a fresh per-type one
    /// </code>
    /// </example>
    public sealed class ConnectionSequenceCounter
    {
        private uint _next;

        /// <summary>Initializes a new <see cref="ConnectionSequenceCounter"/>, starting at <c>1</c> per CCR-1.</summary>
        public ConnectionSequenceCounter()
        {
            _next = 1u;
        }

        /// <summary>
        /// Test-only seam: constructs a counter pre-seeded to <paramref name="startingNext"/> instead
        /// of the production default of <c>1</c>, so wraparound-boundary behavior can be tested
        /// without iterating billions of calls. Internal — visible to
        /// <c>IronGrind.Foundation.EditModeTests</c> via <c>InternalsVisibleTo</c>
        /// (<c>src/Foundation/AssemblyInfo.cs</c>), matching this codebase's existing test-seam
        /// convention (e.g. <see cref="NetworkTestObserver.RecordOutboundMessage"/>).
        /// </summary>
        internal ConnectionSequenceCounter(uint startingNext)
        {
            _next = startingNext;
        }

        /// <summary>
        /// Returns the next sequence number for this connection and advances the counter. Never
        /// returns <c>0</c> — wraps from <see cref="uint.MaxValue"/> back to <c>1</c>.
        /// </summary>
        public uint IssueNext()
        {
            uint issued = _next;
            _next = _next == uint.MaxValue ? 1u : _next + 1;
            return issued;
        }
    }
}
