namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-all-zone-clients notice that a player character died. <b>Wire name: <c>EntityDied</c></b>
    /// (networking-wire-protocol.md Death and Respawn; death-and-respawn.md CR-DR-6, CR-DR-7): <c>{ uint entityId; }</c>
    /// - 4-byte body, S-to-ALL (this zone's clients, the dying player included; networking-relevance-filter.md RFR-7),
    /// R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE160</c> - readiness decision 2026-11-18 (Death and Respawn Story 005).</remarks>
    public readonly struct EntityDied
    {
        /// <summary>Wire <c>MessageTypeID</c> - see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE160;

        /// <summary>Wire size of the body only: 4 bytes.</summary>
        public const int BodySize = 4;
    }
}
