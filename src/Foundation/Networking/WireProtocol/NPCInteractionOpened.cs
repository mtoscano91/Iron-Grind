namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client confirmation that an NPC session is active (npc-shop.md CR-SHOP-3):
    /// <c>NPCInteractionOpened { }</c> — no body, S→C, R-OD. The client starts its <c>SESSION_TTL_SECONDS</c> countdown
    /// on receipt.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE11x</c> NPC sub-block of the R-OD priority range). Not cap-exempt.
    /// </remarks>
    public readonly struct NPCInteractionOpened
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE112;

        /// <summary>Wire size of the body only: 0 bytes.</summary>
        public const int BodySize = 0;
    }
}
