using System;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Seam over the NGO <c>NetworkManager</c> for the zone server composition (ADR-009 Amendment 1 Decision 5).
    /// Story 045 implements it over Unity. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// network.SubscribeTick(OnNetworkTick);
    /// // teardown: network.UnsubscribeTick(OnNetworkTick); ... network.Shutdown();
    /// </code>
    /// </example>
    public interface IZoneNetwork
    {
        /// <summary>Subscribes a handler to <c>NetworkTickSystem.Tick</c>.</summary>
        /// <param name="handler">A named method; pass the same delegate value to unsubscribe.</param>
        void SubscribeTick(Action handler);

        /// <summary>Removes a handler added by <see cref="SubscribeTick"/>.</summary>
        /// <param name="handler">The handler to remove.</param>
        void UnsubscribeTick(Action handler);

        /// <summary>Requests the NGO shutdown (<c>NetworkManager.Shutdown()</c>).</summary>
        void Shutdown();

        /// <summary>True while the manager exists and is not destroyed.</summary>
        bool IsAlive { get; }

        /// <summary>True while NGO has not finished a requested shutdown.</summary>
        bool ShutdownInProgress { get; }
    }
}
