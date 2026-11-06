using System;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Handles a decoded gateway <c>Evict { requestId, accountId }</c> and queues <c>EvictResult</c> (Authentication
    /// Story 014, ADR-016 Decisions 3 and 4). Tick-thread code; the link thread that decodes and feeds it belongs to the
    /// gateway epic. <c>SessionReleased</c> is never sent for an evicted account: the gateway drops its map entry on
    /// <c>EvictResult</c>.
    /// </summary>
    /// <remarks>
    /// Case order: (1) admitted, client not arrived (a load in flight is dropped so it allocates no slot): entry and token
    /// deleted, result at once, no save; (2) entering slot (no <c>ZoneEntryAck</c>): reclaimed by the CR-ZI-17 leave path
    /// with no save, token deleted, result at once; (3) active session (Connected, Disconnected_SessionActive,
    /// Reconnecting): token invalidated first (CR-TOK-6), then <see cref="SessionEndCoordinator.BeginEviction"/>, the
    /// result is queued when the end completes (save succeeded or failed); (4) unknown: result at once (idempotent).
    /// </remarks>
    public sealed class GatewayEvictHandler
    {
        private readonly AdmittedEntries _admitted;
        private readonly SessionTokenStore _tokens;
        private readonly IGatewayLinkOutbox _outbox;
        private readonly ZoneEntryAdmission _admission;
        private readonly ZoneEntryReclamation _reclamation;
        private readonly ConnectionStateMachine _sessions;
        private readonly SessionEndCoordinator _ends;

        /// <summary>Creates the handler.</summary>
        /// <param name="admitted">The admitted-entries table.</param>
        /// <param name="tokens">The token store.</param>
        /// <param name="outbox">Receives <c>EvictResult</c>.</param>
        /// <param name="admission">Drops a pending load.</param>
        /// <param name="reclamation">Reclaims an entering slot.</param>
        /// <param name="sessions">The connection state machine.</param>
        /// <param name="ends">The session-end coordinator.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public GatewayEvictHandler(AdmittedEntries admitted, SessionTokenStore tokens, IGatewayLinkOutbox outbox,
            ZoneEntryAdmission admission, ZoneEntryReclamation reclamation, ConnectionStateMachine sessions, SessionEndCoordinator ends)
        {
            _admitted = admitted ?? throw new ArgumentNullException(nameof(admitted));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _admission = admission ?? throw new ArgumentNullException(nameof(admission));
            _reclamation = reclamation ?? throw new ArgumentNullException(nameof(reclamation));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _ends = ends ?? throw new ArgumentNullException(nameof(ends));
        }

        /// <summary>Processes one <c>Evict</c> and queues exactly one <c>EvictResult</c>.</summary>
        /// <param name="requestId">Echoed in the result.</param>
        /// <param name="account">The account to evict.</param>
        /// <param name="serverTick">The current server tick.</param>
        public void Handle(uint requestId, AccountID account, uint serverTick)
        {
            if (_admitted.TryEvict(account, serverTick, out CharacterID character))
            {
                _admission.DropPendingFor(character); // a load in flight allocates no slot
                _tokens.InvalidateToken(account.RawValue);
                _outbox.EnqueueEvictResult(requestId);
                return;
            }

            if (_reclamation.ReclaimEnteringForEviction(account))
            {
                _tokens.InvalidateToken(account.RawValue);
                _outbox.EnqueueEvictResult(requestId);
                return;
            }

            if (_sessions.TryGetSessionState(account.RawValue, out _))
            {
                _tokens.InvalidateToken(account.RawValue); // before the save: a Reconnect or handshake during it fails
                _ends.BeginEviction(account.RawValue, () => _outbox.EnqueueEvictResult(requestId));
                return;
            }

            _tokens.InvalidateToken(account.RawValue);
            _outbox.EnqueueEvictResult(requestId);
        }
    }
}
