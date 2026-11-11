using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Registers the zone with the gateway as the last startup step (S10, ADR-009 Amendment 1).
    /// The running zone uses the real <c>GatewayLinkRegistration</c> (Gateway Story 011), which starts the gateway link;
    /// <see cref="StandInGatewayRegistration"/> is for tests only.
    /// </summary>
    /// <example>
    /// <code>
    /// gateway.RegisterZone(zone, 7777);
    /// </code>
    /// </example>
    public interface IGatewayRegistration
    {
        /// <summary>Announces that the zone is routable.</summary>
        /// <param name="zone">The running zone.</param>
        /// <param name="port">The port clients connect to.</param>
        void RegisterZone(ZoneID zone, ushort port);
    }
}
