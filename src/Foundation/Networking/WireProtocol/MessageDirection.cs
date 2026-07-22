namespace IronGrind.Networking
{
    /// <summary>
    /// The CCR-3 direction codes (<c>networking-channel-contract.md</c>) — who a message flows
    /// between at the application level.
    /// </summary>
    /// <example>
    /// <code>
    /// MessageDirection direction = MessageDirection.ServerToOwningClient; // GoldSyncEvent (CCR-3)
    /// </code>
    /// </example>
    public enum MessageDirection : byte
    {
        /// <summary>C→S — client to server.</summary>
        ClientToServer = 0,

        /// <summary>S→C — server to the one owning/affected client only.</summary>
        ServerToOwningClient = 1,

        /// <summary>S→ALL — server to all zone clients.</summary>
        ServerToAllZoneClients = 2,

        /// <summary>S→RELEVANT — server to clients for whom the entity is in their relevance set.</summary>
        ServerToRelevantClients = 3,

        /// <summary>S→PARTY — server to all current party members (resolves to S→C for solo players).</summary>
        ServerToParty = 4,
    }
}
