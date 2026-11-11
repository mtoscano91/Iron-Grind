using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Zone-to-gateway link messages queued from the tick (Authentication Story 013, ADR-016 Decision 4). Implementations
    /// only enqueue; the link thread drains the queue later. Called on the tick thread; must not block. The running zone
    /// uses <c>GatewayLinkOutbox</c> (Gateway Story 011); <see cref="LoggingGatewayLinkOutbox"/> is for tests only.
    /// </summary>
    public interface IGatewayLinkOutbox
    {
        /// <summary>Queues <c>AdmitResult { requestId, accepted, reason }</c>.</summary>
        /// <param name="requestId">The <c>requestId</c> of the <c>Admit</c> being answered, echoed unchanged.</param>
        /// <param name="accepted">True when the zone accepted the admission.</param>
        /// <param name="reason"><see cref="AdmitRejectReason.None"/> when accepted.</param>
        void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason);

        /// <summary>Queues <c>SessionReleased { accountId }</c>: the zone no longer holds a reservation for the account.</summary>
        /// <param name="account">The account whose reservation ended without a handshake.</param>
        void EnqueueSessionReleased(AccountID account);

        /// <summary>Queues <c>EvictResult { requestId }</c> (Authentication Story 014): the account's session no longer exists in the zone.</summary>
        /// <param name="requestId">The <c>requestId</c> of the <c>Evict</c> being answered, echoed unchanged.</param>
        void EnqueueEvictResult(uint requestId);

        /// <summary>Queues <c>ReconnectResult { requestId, accepted, newToken }</c> (Authentication Story 014).</summary>
        /// <param name="requestId">The <c>requestId</c> of the <c>Reconnect</c> being answered, echoed unchanged.</param>
        /// <param name="accepted">True when the presented token was valid and was rotated.</param>
        /// <param name="newToken">The rotated token when accepted; null otherwise.</param>
        void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken);

        /// <summary>Queues <c>PlayerCount { count }</c> (Gateway Story 011): occupied player slots plus reserved admissions.</summary>
        /// <param name="count">The zone's session count.</param>
        void EnqueuePlayerCount(ushort count);

        /// <summary>Queues <c>ZoneDraining</c> (Gateway Story 011): the zone left Active and takes no further admissions.</summary>
        void EnqueueZoneDraining();
    }
}
