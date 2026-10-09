namespace IronGrind.Networking
{
    /// <summary>
    /// Looks up the routing row of a message type. Production passes
    /// <see cref="MessageRoutingRegistry.TryGetEntry"/>; tests pass a fake table, because the static
    /// registry has only two client-to-server rows (Story 036 Implementation Notes).
    /// </summary>
    /// <example>
    /// <code>
    /// MessageRoutingLookup lookup = MessageRoutingRegistry.TryGetEntry;
    /// </code>
    /// </example>
    public delegate bool MessageRoutingLookup(ushort messageTypeId, out MessageRoutingEntry entry);
}
