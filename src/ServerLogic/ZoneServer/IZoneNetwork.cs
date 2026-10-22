using System;
using IronGrind.Networking;

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

        /// <summary>
        /// Startup S5: creates the seam through which the client message outbox sends bytes
        /// (Story 045: <c>NgoClientMessageSender(networkManager)</c>). Called once; builds no connection.
        /// </summary>
        /// <returns>The send seam.</returns>
        IClientMessageSendSeam CreateSendSeam();

        /// <summary>
        /// Startup S5: creates the inbound transport shell that feeds NGO messages to the dispatcher and reports
        /// connects and disconnects to the outbox. Called once, after the dispatcher is sealed; the composition
        /// starts it at S8 and stops it at teardown step 6.
        /// </summary>
        /// <param name="intake">Receives every inbound message (the dispatcher).</param>
        /// <param name="dispatcher">The sealed dispatcher (the same object as <paramref name="intake"/>).</param>
        /// <param name="clientListener">Told of connects and disconnects (the outbox).</param>
        /// <returns>The transport, not yet started.</returns>
        IZoneInboundTransport CreateInboundTransport(
            IInboundMessageIntake intake,
            IInboundRequestDispatcher dispatcher,
            IClientConnectionListener clientListener);
    }
}
