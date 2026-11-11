using System;
using System.Net;
using IronGrind.ServerLogic.Gateway.Link;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The tick-facing surface of the zone's gateway link (Gateway Story 011): what the composition, the link outbox
    /// and the link pump need from <see cref="GatewayLinkClient"/>. A seam so EditMode tests drive the zone wiring with a
    /// fake and never open a socket. <see cref="GatewayLinkClient"/> is the only production implementation.
    /// </summary>
    internal interface IGatewayLinkChannel
    {
        /// <summary>Number of links established since start (the LinkUp counter).</summary>
        int LinkUpCount { get; }

        /// <summary>True while a link is established (the registration frame has been sent).</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Starts the link. <paramref name="registration"/> runs ON THE LINK THREAD once per connection and must be thread-safe.
        /// </summary>
        /// <param name="endpoint">The gateway's zone-link endpoint.</param>
        /// <param name="registration">Produces the <c>RegisterZone</c> for each connection.</param>
        void Start(IPEndPoint endpoint, Func<RegisterZoneMessage> registration);

        /// <summary>Queues one encoded zone-to-gateway frame; dropped while the link is down. Never blocks.</summary>
        /// <param name="frame">The full frame bytes.</param>
        void Enqueue(byte[] frame);

        /// <summary>Takes one decoded gateway-to-zone message from the inbox. Never blocks.</summary>
        /// <param name="message">The message when one was available.</param>
        /// <returns>True when a message was dequeued.</returns>
        bool TryDequeue(out GatewayToZoneMessage message);

        /// <summary>Stops the link. Idempotent.</summary>
        void Stop();
    }
}
