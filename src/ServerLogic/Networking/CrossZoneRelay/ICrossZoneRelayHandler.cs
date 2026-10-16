using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Delivers one relayed body to this zone's clients (ADR-015 Decision 7). Production:
    /// <see cref="ZoneClientRelayHandler"/>, which calls <see cref="IZoneClientBroadcastSink.EnqueueToAllClients"/>
    /// not cap-exempt. Tick thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// handler.OnRelayed(ServerBroadcast_Enhancement9.MessageTypeId, body);
    /// </code>
    /// </example>
    public interface ICrossZoneRelayHandler
    {
        /// <summary>Handles one relayed message.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="body">The body; valid only during the call.</param>
        void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body);
    }
}
