namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Minimal forward-dependency contract for the CR-4.1 two-phase-commit respec item handle,
    /// owned by the not-yet-built Inventory System. Story 007's own production code defines
    /// only this interface shape — the real reservation state machine (30-second TTL, real item
    /// state, DB persistence across server crash, reconnect recovery) belongs to a future
    /// Inventory System epic story; see that story's own GDD text (CR-4.1) for the full contract
    /// this interface is a narrow slice of.
    /// </summary>
    /// <remarks>
    /// <para><b>Lifecycle, per CR-4.1:</b> Phase 1 (owned by the Inventory System, not this
    /// epic) moves a respec item from active inventory into a reserved hold slot and returns an
    /// <see cref="IItemReservation"/> handle — but only after its own combat gate
    /// (<c>HasCombatTaggedEffect</c>) has cleared; a rejected gate never produces a handle at
    /// all. Phase 2 (<see cref="RespecTwoPhaseCommitCoordinator.ExecuteRespec"/>, this epic) then
    /// calls <see cref="Consume"/> on success or <see cref="Release"/> on any exception from
    /// <c>LevelingService.TryApplyRespec</c>.</para>
    /// <para><b>No TTL, no state tracking here.</b> This interface has no properties and no
    /// observable state — a concrete implementation (today: a test double; eventually: the real
    /// Inventory System) owns whatever bookkeeping it needs to answer "is this item currently
    /// reserved/consumed/released/active." Out of scope for this story — see its Out of Scope
    /// section.</para>
    /// </remarks>
    public interface IItemReservation
    {
        /// <summary>
        /// Permanently destroys the reserved respec item. Called by the Inventory System only
        /// after <c>LevelingService.TryApplyRespec</c> returns successfully (CR-4.1 Phase 2,
        /// success path). The item never returns to active inventory after this call.
        /// </summary>
        void Consume();

        /// <summary>
        /// Returns the reserved respec item to active inventory and clears the reservation.
        /// Called by the Inventory System when the combat gate rejects Phase 1 (item was never
        /// reserved — this method is not reached on that path), when any exception propagates
        /// out of <c>LevelingService.TryApplyRespec</c> during Phase 2, or when the 30-second
        /// reservation TTL expires without Phase 2 completing (TTL expiry itself is out of
        /// scope for this story — see its Out of Scope section).
        /// </summary>
        void Release();
    }
}
