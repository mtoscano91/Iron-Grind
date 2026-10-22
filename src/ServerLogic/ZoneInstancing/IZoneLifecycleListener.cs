namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Receiver of the two lifecycle events that other zone systems must act on
    /// (<c>zone-instancing.md</c> CR-ZI-2). A direct call on an injected interface rather than a C#
    /// event because there is one consumer and the order matters (the drain broadcast happens before
    /// teardown starts) — ADR-010.
    /// </summary>
    /// <remarks>
    /// Called from inside <see cref="ZoneLifecycle"/> after its state has already changed, so the
    /// listener may read <see cref="ZoneLifecycle.State"/> and sees the new value. Called on the main
    /// thread. Implementations must not throw — catch and log inside the listener; the lifecycle does
    /// not roll back a transition whose notification failed.
    /// </remarks>
    public interface IZoneLifecycleListener
    {
        /// <summary>
        /// T-4 (<c>Active → Draining</c>): called once when the zone starts draining. Zone Instancing
        /// Story 014 broadcasts <c>ZoneSessionEnded(grace)</c> from here.
        /// </summary>
        /// <param name="serverTick">The server tick on which the transition happened.</param>
        void OnDraining(uint serverTick);

        /// <summary>
        /// The zone reached <see cref="IronGrind.Networking.ZoneState.Closed"/> (T-3, T-5, T-6 or T-7).
        /// Called exactly once per <see cref="ZoneLifecycle"/>. Zone Instancing Story 014 maps it to
        /// the teardown request.
        /// </summary>
        /// <param name="transition">Which transition closed the zone.</param>
        /// <param name="serverTick">The server tick on which the transition happened.</param>
        void OnClosed(ZoneCloseTransition transition, uint serverTick);
    }
}
