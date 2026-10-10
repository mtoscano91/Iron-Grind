namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to open an NPC session (npc-shop.md CR-SHOP-3, enhancement-system.md CR-ENH-16/17):
    /// <c>OpenNPCInteraction { uint npcId; }</c> — 4-byte body, C→S, R-OD. The server answers with
    /// <see cref="NPCInteractionOpened"/> or <see cref="RejectedNotInTownHub"/>.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE11x</c> NPC sub-block of the R-OD priority range). <c>npcId</c> is a raw
    /// <see cref="uint"/>: no NPC id type exists and the schema states no zero meaning, so no zero guard is applied
    /// here; validating the id is deferred (TD-058 item 5).
    /// </remarks>
    public readonly struct OpenNPCInteraction
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE110;

        /// <summary>Wire size of the body only: 4 bytes.</summary>
        public const int BodySize = 4;
    }
}
