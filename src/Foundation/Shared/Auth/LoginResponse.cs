using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Authentication
{
    /// <summary>
    /// Login response body (gateway to client). Success bodies are 28 bytes, failure bodies 2 bytes.
    /// Intentionally carries no account identifier (AC-AUTH-30).
    /// </summary>
    public readonly struct LoginResponse
    {
        /// <summary>True if the login succeeded.</summary>
        public readonly bool Success;

        /// <summary>Failure reason; <see cref="AuthFailureReason.None"/> on success.</summary>
        public readonly AuthFailureReason ErrorCode;

        /// <summary>
        /// 16-byte session token. Only meaningful (and non-null) when <see cref="Success"/> is true.
        /// The array is held by reference, not copied: changing its contents changes this response.
        /// </summary>
        public readonly byte[] SessionToken;

        /// <summary>The character to enter the world with. Only meaningful on success.</summary>
        public readonly CharacterID CharacterId;

        /// <summary>The zone to connect to. Only meaningful on success.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>Port of the zone server. Only meaningful on success.</summary>
        public readonly ushort ZoneServerPort;

        /// <summary>Creates a response from all fields. Prefer <see cref="ForSuccess"/> / <see cref="ForFailure"/>.</summary>
        /// <param name="success">Whether the login succeeded.</param>
        /// <param name="errorCode">Failure reason.</param>
        /// <param name="sessionToken">16-byte token (success only).</param>
        /// <param name="characterId">Character id (success only).</param>
        /// <param name="zoneId">Zone id (success only).</param>
        /// <param name="zoneServerPort">Zone server port (success only).</param>
        public LoginResponse(
            bool success,
            AuthFailureReason errorCode,
            byte[] sessionToken,
            CharacterID characterId,
            ZoneID zoneId,
            ushort zoneServerPort)
        {
            Success = success;
            ErrorCode = errorCode;
            SessionToken = sessionToken;
            CharacterId = characterId;
            ZoneId = zoneId;
            ZoneServerPort = zoneServerPort;
        }

        /// <summary>Creates a successful response.</summary>
        /// <param name="sessionToken">16-byte session token.</param>
        /// <param name="characterId">Character id.</param>
        /// <param name="zoneId">Zone id.</param>
        /// <param name="zoneServerPort">Zone server port.</param>
        /// <returns>A response with <see cref="Success"/> true.</returns>
        public static LoginResponse ForSuccess(byte[] sessionToken, CharacterID characterId, ZoneID zoneId, ushort zoneServerPort)
        {
            return new LoginResponse(true, AuthFailureReason.None, sessionToken, characterId, zoneId, zoneServerPort);
        }

        /// <summary>Creates a failed response.</summary>
        /// <param name="reason">Failure reason.</param>
        /// <returns>A response with <see cref="Success"/> false.</returns>
        public static LoginResponse ForFailure(AuthFailureReason reason)
        {
            return new LoginResponse(false, reason, null, CharacterID.Invalid, ZoneID.Invalid, 0);
        }
    }
}
