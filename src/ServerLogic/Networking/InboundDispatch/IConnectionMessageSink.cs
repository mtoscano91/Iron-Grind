using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Receives the connection-level messages of one type (ADR-014 Decisions 1 and 2, step 5).
    /// </summary>
    /// <example>
    /// <code>
    /// public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan&lt;byte&gt; body)
    /// {
    ///     _lastHeartbeatTick[clientId] = _tickSource.ServerTickNumber; // records only
    /// }
    /// </code>
    /// </example>
    public interface IConnectionMessageSink
    {
        /// <summary>
        /// Called from <c>TryAccept</c>, inside the receive callback. Records only; <paramref name="body"/>
        /// is not valid after return. <paramref name="senderEntityId"/> is <see cref="EntityID.Invalid"/>
        /// for a type that does not carry it.
        /// </summary>
        void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body);
    }
}
