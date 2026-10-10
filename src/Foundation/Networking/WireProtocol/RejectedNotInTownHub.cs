namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client rejection of <see cref="OpenNPCInteraction"/> when the character is not in the town hub
    /// zone (npc-shop.md CR-SHOP-3 step 2): <c>RejectedNotInTownHub { }</c> — no body, S→C, R-OD. Carries no
    /// <c>requestId</c>.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE11x</c> NPC sub-block of the R-OD priority range). Not cap-exempt.
    /// </remarks>
    public readonly struct RejectedNotInTownHub
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE113;

        /// <summary>Wire size of the body only: 0 bytes.</summary>
        public const int BodySize = 0;
    }
}
