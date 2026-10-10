namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server selection cancel (CR-ENH-6, AC-ENH-6): <c>CancelEnhancement { }</c> — no body, C→S, R-OD.
    /// Fire-and-forget: the server sends no response and changes no state.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range).
    /// </remarks>
    public readonly struct CancelEnhancement
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE104;

        /// <summary>Wire size of the body only: 0 bytes.</summary>
        public const int BodySize = 0;
    }
}
