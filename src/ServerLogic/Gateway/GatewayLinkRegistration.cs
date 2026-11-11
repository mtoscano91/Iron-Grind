using System;
using System.Net;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneServer;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The real <see cref="IGatewayRegistration"/> (Gateway Story 011, S10): starts the gateway link on the loopback
    /// zone-link port with a registration callback built from immutable values only. The callback runs on the link
    /// thread, so it reads nothing but the zone id, template id, UDP port and capacity captured here and the pump's
    /// published <see cref="GatewayLinkPump.Snapshot"/>; it touches no Unity object.
    /// </summary>
    internal sealed class GatewayLinkRegistration : IGatewayRegistration
    {
        private readonly IGatewayLinkChannel _channel;
        private readonly GatewayLinkPump _pump;
        private readonly int _linkPort;
        private readonly Func<byte> _templateId;

        /// <summary>Creates the registration.</summary>
        /// <param name="channel">The link channel to start.</param>
        /// <param name="pump">Source of the live-account snapshot and the capacity.</param>
        /// <param name="linkPort">The gateway's zone-link port (config <c>GatewayZoneLinkPort</c>).</param>
        /// <param name="templateId">Reads the zone template id; called once, on the calling thread, at S10 (the definition is loaded by S6).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public GatewayLinkRegistration(IGatewayLinkChannel channel, GatewayLinkPump pump, int linkPort, Func<byte> templateId)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pump = pump ?? throw new ArgumentNullException(nameof(pump));
            _templateId = templateId ?? throw new ArgumentNullException(nameof(templateId));
            _linkPort = linkPort;
        }

        /// <inheritdoc/>
        public void RegisterZone(ZoneID zone, ushort port)
        {
            byte template = _templateId();
            ushort capacity = (ushort)_pump.Capacity;
            _pump.RefreshSnapshot();
            GatewayLinkPump pump = _pump;
            _channel.Start(new IPEndPoint(IPAddress.Loopback, _linkPort),
                () => new RegisterZoneMessage(zone, template, port, capacity, pump.Snapshot));
        }
    }
}
