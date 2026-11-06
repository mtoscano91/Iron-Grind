using System;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Handles a decoded gateway <c>Reconnect { requestId, accountId, token }</c> and queues <c>ReconnectResult</c>
    /// (Authentication Story 014, ADR-016 Decision 3, CR-TOK-4/5). Tick-thread code.
    /// </summary>
    /// <remarks>
    /// Only a session in <see cref="SessionState.Disconnected_SessionActive"/> is validated. Success rotates the token
    /// (expiry = the session's current expiry tick) and leaves the state unchanged until the handshake. Failure runs
    /// <see cref="ConnectionStateMachine.EnterReconnecting"/> and the existing failure count; the failure that reaches
    /// <c>REAUTH_FAILURE_LIMIT</c> deletes the token and ends the session through
    /// <see cref="SessionEndCoordinator.BeginReauthExhaustion"/> (async save, never the synchronous persist delegate),
    /// and that last failure is not counted through <see cref="ConnectionStateMachine.RecordFailedReAuthAttempt"/>
    /// (which would remove the session before the save). Any other state, no session, or a session whose end is already
    /// in flight (<see cref="SessionEndCoordinator.IsEndInFlight"/> — e.g. a TTL save; moving it to Reconnecting would make
    /// that end's completion re-check fail and leak the session) answers <c>accepted:false</c> and counts nothing.
    /// A <see cref="SessionState.Connected"/> account is rejected even with the correct token (story decision: no rule
    /// defines a second client's handshake against a live Connected session).
    /// </remarks>
    public sealed class GatewayReconnectHandler
    {
        private static readonly Action<uint> NoOpPersist = _ => { };

        private readonly ConnectionStateMachine _sessions;
        private readonly SessionTokenStore _tokens;
        private readonly SessionEndCoordinator _ends;
        private readonly IGatewayLinkOutbox _outbox;
        private readonly int _reauthFailureLimit;

        /// <summary>Creates the handler.</summary>
        /// <param name="sessions">The connection state machine.</param>
        /// <param name="tokens">The token store.</param>
        /// <param name="ends">The session-end coordinator.</param>
        /// <param name="outbox">Receives <c>ReconnectResult</c>.</param>
        /// <param name="reauthFailureLimit"><c>REAUTH_FAILURE_LIMIT</c>; must be positive.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="reauthFailureLimit"/> is not positive.</exception>
        public GatewayReconnectHandler(ConnectionStateMachine sessions, SessionTokenStore tokens, SessionEndCoordinator ends,
            IGatewayLinkOutbox outbox, int reauthFailureLimit)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _ends = ends ?? throw new ArgumentNullException(nameof(ends));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            if (reauthFailureLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reauthFailureLimit), $"reauthFailureLimit must be positive (was {reauthFailureLimit}).");
            }

            _reauthFailureLimit = reauthFailureLimit;
        }

        /// <summary>Processes one <c>Reconnect</c> and queues exactly one <c>ReconnectResult</c>, at once.</summary>
        /// <param name="requestId">Echoed in the result.</param>
        /// <param name="account">The reconnecting account.</param>
        /// <param name="token">The 16-byte token the client presented.</param>
        /// <param name="serverTick">The current server tick.</param>
        public void Handle(uint requestId, AccountID account, byte[] token, uint serverTick)
        {
            uint id = account.RawValue;
            if (!_sessions.TryGetSessionState(id, out SessionState state) || state != SessionState.Disconnected_SessionActive
                || !_sessions.TryGetSessionExpiryTick(id, out uint expiry)
                || _ends.IsEndInFlight(id)) // the end in flight releases the session; nothing is rotated or counted
            {
                _outbox.EnqueueReconnectResult(requestId, false, null);
                return;
            }

            if (token != null && _tokens.TryValidateAndRotate(id, token, expiry, out byte[] newToken, out _))
            {
                _outbox.EnqueueReconnectResult(requestId, true, newToken);
                return;
            }

            _sessions.EnterReconnecting(id, expiry);
            _sessions.TryGetReauthFailureCount(id, out int failures);
            if (failures + 1 >= _reauthFailureLimit)
            {
                _tokens.InvalidateToken(id);
                _ends.BeginReauthExhaustion(id);
            }
            else
            {
                _sessions.RecordFailedReAuthAttempt(id, _reauthFailureLimit, NoOpPersist);
            }

            _outbox.EnqueueReconnectResult(requestId, false, null);
        }
    }
}
