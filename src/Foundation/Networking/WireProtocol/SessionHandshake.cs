using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server zone entry handshake:
    /// <c>SessionHandshake { CharacterID characterId; ZoneID targetZoneId; byte[16] sessionToken; }</c> - 24-byte body,
    /// C-to-S, R-OD, connection-level (ADR-014 Decision 1): 10-byte envelope only, no <c>SenderEntityID</c>, never an
    /// <c>AccountID</c> (ADR-016 Decision 4). Gateway Story 012; quick spec <c>session-handshake-schema-2026-11-11.md</c>.
    /// </summary>
    /// <remarks>
    /// The zone resolves the account from the token store; the client never sends it. A malformed body (wrong length,
    /// <c>characterId == 0</c>, all-zero token) closes the connection with no message (CR-TOK-9); that rule lives in the
    /// zone-side intake, not in the codec.
    /// </remarks>
    /// <example>
    /// <code>
    /// var message = new SessionHandshake(new CharacterID(501u), new ZoneID(7u), token16);
    /// int size = SessionHandshakeCodec.WriteBody(buffer, in message); // 24
    /// </code>
    /// </example>
    public readonly struct SessionHandshake
    {
        /// <summary>Wire <c>MessageTypeID</c> (R-OD range 0xE000-0xEFFF; next free id of the zone entry block).</summary>
        public const ushort MessageTypeId = 0xE136;

        /// <summary>Length of the session token in bytes.</summary>
        public const int TOKEN_LENGTH = 16;

        /// <summary>Wire size of the body: 4 + 4 + 16 = 24 bytes.</summary>
        public const int BodySize = 24;

        /// <summary>The character the client wants to enter with (never 0 on a valid handshake).</summary>
        public readonly CharacterID CharacterId;

        /// <summary>The zone the client asks for (the <c>ZoneID</c> from <c>LoginResponse</c>).</summary>
        public readonly ZoneID TargetZoneId;

        /// <summary>The raw 16-byte session token. Decoding hands over a fresh array.</summary>
        public readonly byte[] SessionToken;

        /// <summary>Initializes a new <see cref="SessionHandshake"/>.</summary>
        /// <param name="characterId">The character.</param>
        /// <param name="targetZoneId">The target zone.</param>
        /// <param name="sessionToken">The 16-byte token (stored by reference).</param>
        public SessionHandshake(CharacterID characterId, ZoneID targetZoneId, byte[] sessionToken)
        {
            CharacterId = characterId;
            TargetZoneId = targetZoneId;
            SessionToken = sessionToken;
        }

        /// <inheritdoc/>
        public override string ToString() => $"SessionHandshake(CharacterId={CharacterId}, TargetZoneId={TargetZoneId})";
    }
}
