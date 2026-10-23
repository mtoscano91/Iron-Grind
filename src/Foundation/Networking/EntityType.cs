namespace IronGrind.Networking
{
    /// <summary>
    /// Discriminator of the <see cref="EntityState"/> tagged union, always the first byte of an entry
    /// (networking-wire-protocol.md, Bulk-Transfer Messages). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// An unknown byte makes <see cref="EntityStateCodec.TryReadBody"/> return <see langword="false"/>: entries carry no
    /// length prefix, so the reader cannot skip to the next entry and the caller stops parsing that payload.
    /// </remarks>
    public enum EntityType : byte
    {
        /// <summary>A player entity (shared header plus player fields).</summary>
        Player = 0,

        /// <summary>A mob entity (shared header plus <c>mobTypeId</c>).</summary>
        Mob = 1,
    }
}
