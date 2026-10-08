#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
namespace IronGrind.Networking
{
    /// <summary>
    /// Test/dev-build-only fault injection surface for the outbound message pipeline, sitting
    /// below game logic and above the physical transport (ADR-004 Decision 4 — the project owns
    /// serialization/envelope framing; this injector operates at that boundary, never inside NGO
    /// itself). Allows tests to reproduce packet loss, reordering, delay, and fragmentation drops
    /// deterministically, without relying on real network degradation.
    /// </summary>
    /// <remarks>
    /// <para><b>Release-build stripping</b> (<c>networking-test-harness.md</c>, Release-Build
    /// Stripping section): this interface, every concrete implementation, and every wiring/DI call
    /// site must compile only inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c>. IL2CPP
    /// silently strips types inside this guard from Player builds — a surviving reference produces
    /// a runtime type-load failure with no compile-time warning, so no production code path may
    /// reference this interface outside the guard. Story 002 owns the CI verification that no
    /// reference survives stripping.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ITransportFaultInjector faultInjector = NetworkingTestHarness.CreateTransportFaultInjector();
    /// faultInjector.DropNextOutbound(GoldSyncEvent.MessageTypeID, 2);
    /// // ... exercise the code path that sends two GoldSyncEvent messages ...
    /// faultInjector.Reset(); // clear all pending injections between test cases
    /// </code>
    /// </example>
    public interface ITransportFaultInjector
    {
        /// <summary>
        /// Drops the next <paramref name="count"/> outbound messages matching
        /// <paramref name="messageTypeId"/>. Pass <c>ushort.MaxValue</c> (this project's "any
        /// message type" sentinel — see <see cref="TransportFaultInjector.AnyMessageTypeId"/>) to
        /// drop the next <paramref name="count"/> messages regardless of type.
        /// </summary>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c> to match, or the "any" sentinel.</param>
        /// <param name="count">How many matching outbound messages to drop.</param>
        void DropNextOutbound(ushort messageTypeId, int count);

        /// <summary>
        /// Delays the next <paramref name="count"/> outbound messages matching
        /// <paramref name="messageTypeId"/> by <paramref name="delayMs"/> milliseconds.
        /// </summary>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c> to match.</param>
        /// <param name="count">How many matching outbound messages to delay.</param>
        /// <param name="delayMs">Delay to apply to each matching message, in milliseconds.</param>
        void DelayNextOutbound(ushort messageTypeId, int count, int delayMs);

        /// <summary>
        /// Reorders the next two outbound messages matching <paramref name="messageTypeId"/> —
        /// the second message is delivered before the first.
        /// </summary>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c> to match.</param>
        void ReorderNext(ushort messageTypeId);

        /// <summary>
        /// Drops fragment index <paramref name="fragmentIndex"/> of the next fragmented
        /// <c>ZoneStateSnapshot</c> transmission. Fragment indices are 0-based. Pass
        /// <c>ushort.MaxValue</c> (65535) to drop the last fragment regardless of
        /// <c>totalFragments</c> — required because <c>totalFragments</c> is not known before a
        /// snapshot begins transmitting (dynamic per CR-NET-7.3).
        /// </summary>
        /// <param name="fragmentIndex">0-based fragment index to drop, or <c>ushort.MaxValue</c> for "last fragment."</param>
        void DropSnapshotFragment(ushort fragmentIndex);

        /// <summary>
        /// Overrides the outbound <c>SequenceNumber</c> counter for this connection to
        /// <paramref name="value"/>. If called before any message has been emitted, applies
        /// immediately. If called mid-stream, takes effect atomically at the next tick
        /// boundary — never mid-tick.
        /// </summary>
        /// <param name="value">The value the outbound sequence counter will resume from.</param>
        void SetSequenceNumber(uint value);

        /// <summary>
        /// Resets all pending fault injections (drops, delays, reorders, fragment drops, and any
        /// pending sequence-number override) to a clean state.
        /// </summary>
        void Reset();
    }
}
#endif
