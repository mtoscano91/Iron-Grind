namespace IronGrind.Networking
{
    /// <summary>
    /// Step 4 of <see cref="ZoneTickPipeline"/> (ADR-014 Decision 6): the per-connection writers
    /// flush what the earlier steps produced. What flushes: the priority path, the R-U batch, the
    /// cycle broadcast and the position packet. The reply to a request received before the tick
    /// started leaves here, in the same tick.
    /// </summary>
    /// <remarks>
    /// Called on the tick thread, once per tick, last. May throw: <see cref="ZoneTickPipeline"/>
    /// logs the failure and the rest of <see cref="ServerTickLoop.AdvanceTick"/> still runs.
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class ZoneOutboundStep : IZoneOutboundStep
    /// {
    ///     public void Flush(uint currentTick)
    ///     {
    ///         // priority path, R-U batch, cycle broadcast, position packet, per connection.
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IZoneOutboundStep
    {
        /// <summary>Flushes the per-connection writers for one tick.</summary>
        /// <param name="currentTick">The tick being run; the same value every step of this tick receives.</param>
        void Flush(uint currentTick);
    }
}
