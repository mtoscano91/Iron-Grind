using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Zone-to-gateway link messages queued from the tick (Authentication Story 013, ADR-016 Decision 4). Implementations
    /// only enqueue; the gateway epic's link thread drains the queue later. Called on the tick thread; must not block.
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
    }
}
