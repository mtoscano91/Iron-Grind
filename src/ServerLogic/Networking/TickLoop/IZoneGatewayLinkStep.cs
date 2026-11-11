namespace IronGrind.Networking
{
    /// <summary>
    /// Tick step 1b of <see cref="ZoneTickPipeline"/> (Gateway Story 011, ADR-016 Decision 3): drains the gateway link
    /// inbox (<c>Admit</c>, <c>Evict</c>, <c>Reconnect</c>) into the zone's handlers at the start of the tick, right
    /// after the cross-zone relay drain and before the request dispatch.
    /// </summary>
    public interface IZoneGatewayLinkStep
    {
        /// <summary>Drains the inbox. One bad message must not stop the drain.</summary>
        /// <param name="currentTick">The tick being run; passed to every handler.</param>
        void Drain(uint currentTick);
    }
}
