namespace IronGrind.Networking
{
    /// <summary>
    /// Step 3 of <see cref="ZoneTickPipeline"/> (ADR-014 Decision 6): the simulation of one tick.
    /// Its implementation runs ADR-002 phases 1 to 4 in ADR-002's order. The game systems that tick
    /// (Enemy AI, combat, status effects, loot auction timers, the server-originated bag mutators
    /// retrying after <c>OnGateOpened</c>) run in phase 3, in an order the composition root lists
    /// explicitly. A system that ticks is called from here, never registered with
    /// <see cref="ServerTickLoop"/> on its own.
    /// </summary>
    /// <remarks>
    /// Called on the tick thread, once per tick, after the completion and request steps. May throw:
    /// <see cref="ZoneTickPipeline"/> logs the failure and still runs the outbound step.
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class ZoneSimulationStep : IZoneSimulationStep
    /// {
    ///     public void Run(uint currentTick, float deltaTime)
    ///     {
    ///         // ADR-002 phases 1 to 4; phase 3 calls each ticking system in the listed order.
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IZoneSimulationStep
    {
        /// <summary>Runs the simulation of one tick.</summary>
        /// <param name="currentTick">The tick being run; the same value every step of this tick receives.</param>
        /// <param name="deltaTime">The delta time the pipeline's <see cref="ZoneTickPipeline.Tick"/> was called with.</param>
        void Run(uint currentTick, float deltaTime);
    }
}
