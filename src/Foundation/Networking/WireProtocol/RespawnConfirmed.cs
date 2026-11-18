namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client confirmation that the player respawned. <b>Wire name: <c>RespawnConfirmed</c></b>
    /// (networking-wire-protocol.md Death and Respawn; death-and-respawn.md CR-DR-13): <c>{ uint entityId; }</c> -
    /// 4-byte body, S-to-owner, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE163</c> - readiness decision 2026-11-18 (Death and Respawn Story 005).</remarks>
    public readonly struct RespawnConfirmed
    {
        /// <summary>Wire <c>MessageTypeID</c> - see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE163;

        /// <summary>Wire size of the body only: 4 bytes.</summary>
        public const int BodySize = 4;
    }
}
