using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// <see cref="IGatewayLinkOutbox"/> for tests only (and the wiring's fallback when no outbox is injected): logs each
    /// message in the Editor and development builds, does nothing otherwise. The running zone composition passes the
    /// real <c>GatewayLinkOutbox</c> (Gateway Story 011).
    /// </summary>
    public sealed class LoggingGatewayLinkOutbox : IGatewayLinkOutbox
    {
        private const string LOG_PREFIX = "[GatewayLinkOutbox]";

        /// <inheritdoc/>
        public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} AdmitResult(requestId={requestId}, accepted={accepted}, reason={reason}) not sent: gateway link not implemented.");
#endif
        }

        /// <inheritdoc/>
        public void EnqueueSessionReleased(AccountID account)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} SessionReleased({account}) not sent: gateway link not implemented.");
#endif
        }

        /// <inheritdoc/>
        public void EnqueueEvictResult(uint requestId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} EvictResult(requestId={requestId}) not sent: gateway link not implemented.");
#endif
        }

        /// <inheritdoc/>
        public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} ReconnectResult(requestId={requestId}, accepted={accepted}) not sent: gateway link not implemented.");
#endif
        }

        /// <inheritdoc/>
        public void EnqueuePlayerCount(ushort count)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} PlayerCount({count}) not sent: logging outbox.");
#endif
        }

        /// <inheritdoc/>
        public void EnqueueZoneDraining()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"{LOG_PREFIX} ZoneDraining not sent: logging outbox.");
#endif
        }
    }
}
