using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The production <see cref="IServerWideBroadcaster"/> (ADR-015 Decision 1): checks the allowlist, enqueues the
    /// body for every client of this zone through <see cref="IZoneClientBroadcastSink"/> (not cap-exempt), and then
    /// publishes one relay envelope. A publish failure is logged and changes nothing; the local half does not
    /// depend on the publisher in any way. Tick thread only. Never throws.
    /// </summary>
    /// <remarks>
    /// The local half allocates nothing. The publisher (encoding, the bus or database) may allocate; that is a few
    /// messages per hour. A body longer than the type's allowlist <c>MaxBodyBytes</c> is refused like an unlisted
    /// type, because every receiver would drop it.
    /// </remarks>
    /// <example>
    /// <code>
    /// var broadcaster = new ServerWideBroadcaster(outbox, publisher, CrossZoneRelayAllowlist.CreateProduction());
    /// broadcaster.Broadcast(ServerBroadcast_Enhancement9.MessageTypeId, body);   // from the commit-success callback
    /// </code>
    /// </example>
    public sealed class ServerWideBroadcaster : IServerWideBroadcaster
    {
        private const string LOG_PREFIX = "[ServerWideBroadcaster]";

        private readonly IZoneClientBroadcastSink _sink;
        private readonly ICrossZoneRelayPublisher _publisher;
        private readonly CrossZoneRelayAllowlist _allowlist;

        /// <summary>Creates the broadcaster.</summary>
        /// <param name="sink">This zone's clients (the outbox); not null.</param>
        /// <param name="publisher">The relay transport; not null.</param>
        /// <param name="allowlist">The relay allowlist; not null.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ServerWideBroadcaster(IZoneClientBroadcastSink sink, ICrossZoneRelayPublisher publisher, CrossZoneRelayAllowlist allowlist)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        }

        /// <inheritdoc />
        public bool Broadcast(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            if (!_allowlist.TryGetMaxBodyBytes(messageTypeId, out int maxBody))
            {
                Debug.LogError($"{LOG_PREFIX} TypeNotAllowlisted: type=0x{messageTypeId:X4} - not sent locally and not published.");
                return false;
            }

            if (body.Length > maxBody)
            {
                Debug.LogError($"{LOG_PREFIX} BodyTooLong: type=0x{messageTypeId:X4} length={body.Length} max={maxBody} - not sent locally and not published.");
                return false;
            }

            try
            {
                _sink.EnqueueToAllClients(messageTypeId, body, false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} LocalDeliveryThrew: type=0x{messageTypeId:X4} - {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                _publisher.Publish(messageTypeId, body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} PublishFailed: type=0x{messageTypeId:X4} - {ex.GetType().Name}: {ex.Message}; other zones are not reached, local delivery is unaffected.");
            }

            return true;
        }
    }
}
