namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client notice that the player entered the Dead state. <b>Wire name: <c>DeathStateEntered</c></b>
    /// (networking-wire-protocol.md Death and Respawn; death-and-respawn.md CR-DR-6):
    /// <c>{ uint entityId; byte respawnTimerSeconds; }</c> - 5-byte body, S-to-owner, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE161</c> - readiness decision 2026-11-18 (Death and Respawn Story 005).</remarks>
    public readonly struct DeathStateEntered
    {
        /// <summary>Wire <c>MessageTypeID</c> - see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE161;

        /// <summary>Wire size of the body only: 4 + 1 = 5 bytes.</summary>
        public const int BodySize = 5;
    }
}
