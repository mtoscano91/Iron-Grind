namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server voluntary NPC session close (npc-shop.md CR-SHOP-4): <c>CloseNPCInteraction { }</c> — no body,
    /// C→S, R-OD. Fire-and-forget; the server sends no response.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE11x</c> NPC sub-block of the R-OD priority range).
    /// </remarks>
    public readonly struct CloseNPCInteraction
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE111;

        /// <summary>Wire size of the body only: 0 bytes.</summary>
        public const int BodySize = 0;
    }
}
