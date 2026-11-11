using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// An already-decoded <c>SessionHandshake</c> for zone entry (Zone Instancing Story 009). The wire format is fixed
    /// (0xE136, 24-byte body, Gateway Story 012); <see cref="SessionHandshakeIntake"/> decodes it in the receive
    /// callback and hands this to <see cref="ZoneEntryAdmission.BeginEntry"/> on the tick thread. It carries no account: the client never sends
    /// one (CR-TOK-8), <see cref="IZoneEntryTokenValidator.TryValidate"/> resolves it.
    /// </summary>
    public readonly struct ZoneEntryRequest
    {
        /// <summary>Creates a request.</summary>
        /// <param name="clientId">The connection the handshake arrived on.</param>
        /// <param name="character">The character the client wants to enter with.</param>
        /// <param name="targetZone">The zone the client asked for.</param>
        /// <param name="sessionToken">The presented session token (<c>SessionTokenStore.TOKEN_LENGTH_BYTES</c> bytes); may be null.</param>
        public ZoneEntryRequest(uint clientId, CharacterID character, ZoneID targetZone, byte[] sessionToken)
        {
            ClientId = clientId;
            Character = character;
            TargetZone = targetZone;
            SessionToken = sessionToken;
        }

        /// <summary>The connection the handshake arrived on.</summary>
        public uint ClientId { get; }

        /// <summary>The character the client wants to enter with.</summary>
        public CharacterID Character { get; }

        /// <summary>The zone the client asked for.</summary>
        public ZoneID TargetZone { get; }

        /// <summary>The presented session token; may be null. Validators must not retain or mutate it; the decoder hands over an array it will not reuse.</summary>
        public byte[] SessionToken { get; }
    }
}
