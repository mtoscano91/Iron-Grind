using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// An already-decoded <c>SessionHandshake</c> for zone entry (Zone Instancing Story 009). The wire codec is not
    /// part of the story (schema pending OQ-NC-SER-2); the caller decodes and hands this to
    /// <see cref="ZoneEntryAdmission.BeginEntry"/> on the tick thread.
    /// </summary>
    public readonly struct ZoneEntryRequest
    {
        /// <summary>Creates a request.</summary>
        /// <param name="clientId">The connection the handshake arrived on.</param>
        /// <param name="account">The account the client claims.</param>
        /// <param name="character">The character the client wants to enter with.</param>
        /// <param name="targetZone">The zone the client asked for.</param>
        /// <param name="sessionToken">The presented session token (<c>SessionTokenStore.TOKEN_LENGTH_BYTES</c> bytes); may be null.</param>
        public ZoneEntryRequest(uint clientId, AccountID account, CharacterID character, ZoneID targetZone, byte[] sessionToken)
        {
            ClientId = clientId;
            Account = account;
            Character = character;
            TargetZone = targetZone;
            SessionToken = sessionToken;
        }

        /// <summary>The connection the handshake arrived on.</summary>
        public uint ClientId { get; }

        /// <summary>The account the client claims.</summary>
        public AccountID Account { get; }

        /// <summary>The character the client wants to enter with.</summary>
        public CharacterID Character { get; }

        /// <summary>The zone the client asked for.</summary>
        public ZoneID TargetZone { get; }

        /// <summary>The presented session token; may be null.</summary>
        public byte[] SessionToken { get; }
    }
}
