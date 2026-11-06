using System;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Handles a decoded gateway <c>Admit { requestId, accountId, characterId, token[16] }</c> and queues
    /// <c>AdmitResult</c> (Authentication Story 013, ADR-016 Decision 4). Pure tick-thread code; the link thread that
    /// decodes and feeds it belongs to the gateway epic.
    /// </summary>
    /// <remarks>
    /// Check order: zone not Active, then capacity (session count without the account's own existing entry, so a
    /// replacement is not counted twice), then token validity. Every rejection changes nothing.
    /// </remarks>
    public sealed class GatewayAdmitHandler
    {
        private readonly AdmittedEntries _admitted;
        private readonly SessionTokenStore _tokens;
        private readonly IGatewayLinkOutbox _outbox;
        private readonly Func<bool> _acceptsPlayers;
        private readonly Func<int> _sessionCount;
        private readonly int _maxPlayers;

        /// <summary>Creates the handler.</summary>
        /// <param name="admitted">The admitted-entries table.</param>
        /// <param name="tokens">The token store receiving the gateway's token.</param>
        /// <param name="outbox">Receives <c>AdmitResult</c>.</param>
        /// <param name="acceptsPlayers">True while the zone is Active (<c>ZoneLifecycle.AcceptsPlayers</c>).</param>
        /// <param name="sessionCount">The one session count: occupied player slots plus reserved admissions.</param>
        /// <param name="maxPlayers">Player capacity of the zone.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public GatewayAdmitHandler(AdmittedEntries admitted, SessionTokenStore tokens, IGatewayLinkOutbox outbox,
            Func<bool> acceptsPlayers, Func<int> sessionCount, int maxPlayers)
        {
            _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _acceptsPlayers = acceptsPlayers ?? throw new ArgumentNullException(nameof(acceptsPlayers));
            _sessionCount = sessionCount ?? throw new ArgumentNullException(nameof(sessionCount));
            _maxPlayers = maxPlayers;
        }

        /// <summary>Processes one <c>Admit</c> and queues exactly one <c>AdmitResult</c>.</summary>
        /// <param name="requestId">Echoed in the result.</param>
        /// <param name="account">The authenticated account.</param>
        /// <param name="character">The character the account will enter with.</param>
        /// <param name="token">The gateway-generated 16-byte token.</param>
        /// <param name="serverTick">The current server tick.</param>
        public void Handle(uint requestId, AccountID account, CharacterID character, byte[] token, uint serverTick)
        {
            if (!_acceptsPlayers())
            {
                Reject(requestId, AdmitRejectReason.NotActive);
                return;
            }

            int sessions = _sessionCount();
            if (_admitted.IsReserved(account))
            {
                sessions--;
            }

            if (sessions >= _maxPlayers)
            {
                Reject(requestId, AdmitRejectReason.Full);
                return;
            }

            uint expiresAt = serverTick + (uint)_admitted.ConnectingTimeoutTicks;
            if (!_tokens.AdmitToken(account.RawValue, token, expiresAt))
            {
                Reject(requestId, AdmitRejectReason.InvalidToken);
                return;
            }

            _admitted.Admit(account, character, serverTick);
            _outbox.EnqueueAdmitResult(requestId, true, AdmitRejectReason.None);
        }

        private void Reject(uint requestId, AdmitRejectReason reason)
        {
            _outbox.EnqueueAdmitResult(requestId, false, reason);
        }
    }
}
