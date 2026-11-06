using System;
using IronGrind.CharacterPersistence;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The real <see cref="IZoneEntryTokenValidator"/> (Authentication Story 013): finds the admitted entry for the
    /// request's <c>CharacterID</c> (reserved, or kept after T-4) and compares the presented token with
    /// <see cref="SessionTokenStore.TokenMatches"/> (fixed-time). Non-blocking, no allocation per call.
    /// </summary>
    /// <remarks>
    /// A match on a kept entry returns true so <see cref="ZoneEntryAdmission"/>'s pre-check answers
    /// <c>ZoneDoesNotExist</c> (the zone left Active) instead of closing silently.
    /// </remarks>
    public sealed class GatewayZoneEntryTokenValidator : IZoneEntryTokenValidator
    {
        private readonly AdmittedEntries _admitted;
        private readonly SessionTokenStore _tokens;

        /// <summary>Creates the validator.</summary>
        /// <param name="admitted">The admitted-entries table.</param>
        /// <param name="tokens">The token store holding the gateway's tokens.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public GatewayZoneEntryTokenValidator(AdmittedEntries admitted, SessionTokenStore tokens)
        {
            _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        /// <inheritdoc/>
        public bool TryValidate(in ZoneEntryRequest request, out AccountID account)
        {
            if (_admitted.TryGetByCharacter(request.Character, out AccountID found, out _)
                && _tokens.TokenMatches(found.RawValue, request.SessionToken))
            {
                account = found;
                return true;
            }

            account = AccountID.Invalid;
            return false;
        }
    }
}
