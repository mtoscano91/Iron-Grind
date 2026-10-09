using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Registration, connection set and tick entry point of the inbound request dispatcher
    /// (ADR-014 Decisions 3 and 4). Implemented by <see cref="InboundRequestDispatcher"/>, which
    /// also implements <see cref="IInboundMessageIntake"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// dispatcher.Register(descriptor, handler);
    /// dispatcher.Seal();
    /// dispatcher.AddConnection(clientId);
    /// dispatcher.DispatchTick(tickLoop.ServerTickNumber);
    /// </code>
    /// </example>
    public interface IInboundRequestDispatcher
    {
        /// <summary>Registers a request type and its handler (ADR-014 Decision 3). Throws after <see cref="Seal"/> and on invalid descriptors.</summary>
        /// <param name="descriptor">The request type.</param>
        /// <param name="handler">The handler; not null.</param>
        void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler);

        /// <summary>Registers a connection-level type and its sink (ADR-014 Decision 3). Throws after <see cref="Seal"/> and on invalid descriptors.</summary>
        /// <param name="descriptor">The connection-level type.</param>
        /// <param name="sink">The sink; not null.</param>
        void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink);

        /// <summary>Closes the type table and allocates per-connection state (ADR-014 Decision 3).</summary>
        void Seal();

        /// <summary>Adds a connection; releases removed connections first when called outside a pass. False = no free slot or already known.</summary>
        /// <param name="clientId">The connection.</param>
        bool AddConnection(uint clientId);

        /// <summary>Marks a connection removed; frees nothing; safe to call inside a pass (ADR-014 Decision 4).</summary>
        /// <param name="clientId">The connection.</param>
        void RemoveConnection(uint clientId);

        /// <summary>Runs Pass A and Pass B (ADR-014 Decision 4). Tick thread; called by the tick pipeline only.</summary>
        /// <param name="currentTick">The tick being run.</param>
        void DispatchTick(uint currentTick);

        /// <summary>Number of requests held for a character; for tests and diagnostics.</summary>
        /// <param name="charId">The character.</param>
        int HeldCount(CharacterID charId);
    }
}
