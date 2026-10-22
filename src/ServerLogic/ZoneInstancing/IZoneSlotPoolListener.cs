namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Receiver of the two slot-pool events that other zone systems must act on
    /// (<c>zone-instancing.md</c> CR-ZI-2, EC-ZI-12). A direct call on an injected interface per
    /// ADR-010. Zone Instancing Story 014 forwards the first to
    /// <see cref="ZoneLifecycle.OnOccupiedPlayerSlotsChanged"/> and maps the second to
    /// <see cref="ZoneLifecycle.RequestFatal"/>.
    /// </summary>
    /// <remarks>
    /// Called on the main thread from inside <see cref="ZoneSlotPool"/> after the pool's state is
    /// final, so a re-entrant call into the pool sees a consistent pool. Implementations must not
    /// throw — catch and log inside the listener; the pool does not roll back a mutation whose
    /// notification failed.
    /// </remarks>
    public interface IZoneSlotPoolListener
    {
        /// <summary>
        /// Called once after every successful player allocation or release.
        /// </summary>
        /// <param name="occupied">The new occupied player slot count.</param>
        /// <param name="serverTick">The server tick read from the injected tick source.</param>
        void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick);

        /// <summary>
        /// Called once, on the first allocation attempt that reaches ID minting after
        /// <see cref="uint.MaxValue"/> has been issued (EC-ZI-12). An attempt that fails earlier
        /// (foreign zone, full pool) raises nothing; later failed attempts do not call it again.
        /// </summary>
        /// <param name="serverTick">The server tick of the failed attempt.</param>
        void OnEntityIDCounterExhausted(uint serverTick);
    }
}
