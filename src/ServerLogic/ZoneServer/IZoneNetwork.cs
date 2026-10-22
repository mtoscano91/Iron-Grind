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
        /// <summary>
        /// Startup S1: creates the NGO object as a root object outside the zone scene with
        /// <c>EnableSceneManagement = false</c>.
        /// </summary>
        void PrepareObject();

        /// <summary>Startup S8: sets the tick rate, <c>Application.targetFrameRate</c> and vSync before start.</summary>
        /// <param name="tickRate">NGO <c>NetworkConfig.TickRate</c>.</param>
        /// <param name="targetFrameRate">Application target frame rate.</param>
        /// <param name="vSyncCount">Quality vSync count.</param>
        void Configure(uint tickRate, int targetFrameRate, int vSyncCount);

        /// <summary>Startup S8: <c>NetworkManager.StartServer()</c>.</summary>
        /// <returns>False when NGO could not start; startup then fails at S8.</returns>
        bool StartServer();

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
