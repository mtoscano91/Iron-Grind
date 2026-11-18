namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-all-zone-clients notice that a player respawned. <b>Wire name: <c>EntityRespawned</c></b>
    /// (networking-wire-protocol.md Death and Respawn; death-and-respawn.md CR-DR-13):
    /// <c>{ uint entityId; short posX, posY, posZ; }</c> (position as <c>WireFixedPointCodec</c> x100) - 10-byte body,
    /// S-to-ALL (this zone's clients), R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE162</c> - readiness decision 2026-11-18 (Death and Respawn Story 005).</remarks>
    public readonly struct EntityRespawned
    {
        /// <summary>Wire <c>MessageTypeID</c> - see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE162;

        /// <summary>Wire size of the body only: 4 + 6 = 10 bytes.</summary>
        public const int BodySize = 10;
    }
}
