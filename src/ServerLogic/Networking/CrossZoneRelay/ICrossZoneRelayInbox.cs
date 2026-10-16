using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Relayed messages waiting for the tick (ADR-015 Decision 3). Written by the listener thread
    /// (or the in-memory bus in tests), drained by tick step 1a.
    /// </summary>
    /// <example>
    /// <code>
    /// inbox.TryAdd(typeId, body);              // listener thread
    /// int n = inbox.Drain(currentTick, handler); // tick thread
    /// </code>
    /// </example>
    public interface ICrossZoneRelayInbox
    {
        /// <summary>Listener thread. Copies the body. Returns false if the entry was dropped (inbox full or body too long).</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="body">The body; valid only during the call.</param>
        /// <returns>True when queued.</returns>
        bool TryAdd(ushort messageTypeId, ReadOnlySpan<byte> body);

        /// <summary>Tick thread. Hands every entry present at the call to <paramref name="handler"/>, in arrival order.</summary>
        /// <param name="currentTick">The tick number, for the log line.</param>
        /// <param name="handler">Receives each entry; the span is valid only during its call.</param>
        /// <returns>The number of entries removed.</returns>
        int Drain(uint currentTick, ICrossZoneRelayHandler handler);
    }
}
