using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Default <see cref="IGatewayLinkOutbox"/> until the gateway epic supplies the real link: logs each message in the
    /// Editor and development builds, does nothing otherwise.
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
    }
}
