using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.Gateway.Link;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The real <see cref="IGatewayLinkOutbox"/> (Gateway Story 011, ADR-016 Decision 3): encodes each zone-to-gateway
    /// message with <see cref="GatewayLinkCodec"/> and enqueues the frame on the link channel. Tick thread only; never
    /// blocks and never touches a socket. Frames enqueued while the link is down are dropped by the channel.
    /// </summary>
    /// <remarks>
    /// Frames are best-effort at shutdown: teardown step 2a stops the link without flushing, so a <c>ZoneDraining</c> or
    /// <c>SessionReleased</c> queued just before a close may not reach the gateway. The gateway treats the lost link as
    /// the zone gone (no more routing to it; its account-map entries end after the link-loss grace), which covers it.
    /// </remarks>
    internal sealed class GatewayLinkOutbox : IGatewayLinkOutbox
    {
        private static readonly byte[] ZERO_TOKEN = new byte[GatewayLinkCodec.TOKEN_LENGTH];

        // The ZoneDraining frame has no body, so one immutable instance serves every send.
        private static readonly byte[] ZONE_DRAINING_FRAME = GatewayLinkCodec.EncodeZoneDraining();

        private readonly IGatewayLinkChannel _channel;

        /// <summary>Creates the outbox over a link channel.</summary>
        /// <param name="channel">The channel receiving the frames; not null.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="channel"/> is null.</exception>
        public GatewayLinkOutbox(IGatewayLinkChannel channel)
        {
            _channel = channel ?? throw new System.ArgumentNullException(nameof(channel));
        }

        /// <inheritdoc/>
        public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason)
        {
            _channel.Enqueue(GatewayLinkCodec.Encode(new AdmitResultMessage(requestId, accepted, reason)));
        }

        /// <inheritdoc/>
        public void EnqueueSessionReleased(AccountID account)
        {
            _channel.Enqueue(GatewayLinkCodec.Encode(new SessionReleasedMessage(account)));
        }

        /// <inheritdoc/>
        public void EnqueueEvictResult(uint requestId)
        {
            _channel.Enqueue(GatewayLinkCodec.Encode(new EvictResultMessage(requestId)));
        }

        /// <inheritdoc/>
        public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken)
        {
            // A rejected result carries 16 zero bytes (codec contract), whatever the caller passed.
            _channel.Enqueue(GatewayLinkCodec.Encode(new ReconnectResultMessage(requestId, accepted, accepted ? newToken : ZERO_TOKEN)));
        }

        /// <inheritdoc/>
        public void EnqueuePlayerCount(ushort count)
        {
            _channel.Enqueue(GatewayLinkCodec.Encode(new PlayerCountMessage(count)));
        }

        /// <inheritdoc/>
        public void EnqueueZoneDraining()
        {
            _channel.Enqueue(ZONE_DRAINING_FRAME);
        }
    }
}
