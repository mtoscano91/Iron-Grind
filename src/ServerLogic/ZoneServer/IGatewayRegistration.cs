using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Registers the zone with the gateway as the last startup step (S10, ADR-009 Amendment 1).
    /// <see cref="StandInGatewayRegistration"/> until the gateway client exists.
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
