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
    /// <para>
    /// A match on a kept entry returns true so <see cref="ZoneEntryAdmission"/>'s pre-check answers
    /// <c>ZoneDoesNotExist</c> (the zone left Active) instead of closing silently.
    /// </para>
    /// <para>
    /// Token path (<c>networking-session-token.md</c> CR-TOK-9): with a session machine supplied, <see cref="TryValidateReconnect"/>
    /// resolves the account from the ghost session (<see cref="SessionState.Disconnected_SessionActive"/>) that plays the character and
    /// compares the token the same way. It changes no state and counts no failure.
    /// </para>
    /// </remarks>
    public sealed class GatewayZoneEntryTokenValidator : IZoneEntryTokenValidator, IZoneReconnectTokenValidator
    {
        private readonly AdmittedEntries _admitted;
        private readonly SessionTokenStore _tokens;
        private readonly ConnectionStateMachine _sessions;

        /// <summary>Creates the validator.</summary>
        /// <param name="admitted">The admitted-entries table.</param>
        /// <param name="tokens">The token store holding the gateway's tokens.</param>
        /// <param name="sessions">The session machine the token path resolves ghosts from; null disables the token path.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public GatewayZoneEntryTokenValidator(AdmittedEntries admitted, SessionTokenStore tokens, ConnectionStateMachine sessions = null)
        {
            _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _sessions = sessions;
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

        /// <inheritdoc/>
        public bool TryValidateReconnect(in ZoneEntryRequest request, out AccountID account)
        {
            if (_sessions != null
                && _sessions.TryGetAccountByCharacter(request.Character.RawValue, out uint raw)
                && _sessions.TryGetSessionState(raw, out SessionState state)
                && state == SessionState.Disconnected_SessionActive
                && _tokens.TokenMatches(raw, request.SessionToken))
            {
                account = new AccountID(raw);
                return true;
            }

            account = AccountID.Invalid;
            return false;
        }
    }
}
