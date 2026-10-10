namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-all-connected-clients announcement of a +8 to +9 success (CR-ENH-14, CR-ENH-15 step 8):
    /// <c>ServerBroadcast_Enhancement9 { string playerName; string itemName; }</c> — variable body of 4 to 52 bytes,
    /// S→ALL, R-OD. Each string is a <c>ushort</c> UTF-8 byte count followed by that many UTF-8 bytes, at most
    /// <see cref="MaxStringBytes"/> bytes, cut at a character boundary. <b>Not</b> exempt from the priority-path cap.
    /// Sent by Enhancement Story 015; the codec is written in Story 010 (AC-NC-41).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range).
    /// </para>
    /// <para>
    /// <b>Scope:</b> the channel contract codes this message <c>S→ALL</c> (all connected clients, every zone), and its
    /// routing row uses <see cref="MessageDirection.ServerToAllZoneClients"/>, which mirrors the five CCR-3 codes.
    /// The audience is every connected client in every zone, not only the sender's zone (CCR-3 qualifier). The sender
    /// (Enhancement Story 015) must not scope it to one zone.
    /// </para>
    /// </remarks>
    public readonly struct ServerBroadcast_Enhancement9
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE103;

        /// <summary>Maximum UTF-8 byte length of each string, excluding its length prefix.</summary>
        public const int MaxStringBytes = 24;

        /// <summary>Width of each string's length prefix: a <see cref="ushort"/>, 2 bytes.</summary>
        public const int LengthPrefixSize = 2;

        /// <summary>Minimum body size: two empty strings, 2 + 2 = 4 bytes.</summary>
        public const int MinBodySize = 2 * LengthPrefixSize;

        /// <summary>Maximum body size: two full strings, 2 × (2 + 24) = 52 bytes.</summary>
        public const int MaxBodySize = 2 * (LengthPrefixSize + MaxStringBytes);
    }
}
