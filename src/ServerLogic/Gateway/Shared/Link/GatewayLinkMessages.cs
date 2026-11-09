using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.Gateway.Link
{
    /// <summary>Zone to gateway (0x01): the zone announces itself and the accounts it currently holds.</summary>
    public readonly struct RegisterZoneMessage
    {
        /// <summary>Creates the message.</summary>
        public RegisterZoneMessage(ZoneID zoneId, byte templateId, ushort udpPort, ushort capacity, AccountID[] liveAccounts)
        {
            ZoneId = zoneId;
            TemplateId = templateId;
            UdpPort = udpPort;
            Capacity = capacity;
            LiveAccounts = liveAccounts;
        }

        /// <summary>The zone instance id.</summary>
        public ZoneID ZoneId { get; }

        /// <summary>The zone template id.</summary>
        public byte TemplateId { get; }

        /// <summary>The zone's UDP game port.</summary>
        public ushort UdpPort { get; }

        /// <summary>Player capacity of the zone.</summary>
        public ushort Capacity { get; }

        /// <summary>Accounts currently live in the zone (never null when decoded; may be empty).</summary>
        public AccountID[] LiveAccounts { get; }
    }

    /// <summary>Zone to gateway (0x02): current player count.</summary>
    public readonly struct PlayerCountMessage
    {
        /// <summary>Creates the message.</summary>
        public PlayerCountMessage(ushort count)
        {
            Count = count;
        }

        /// <summary>Players currently in the zone.</summary>
        public ushort Count { get; }
    }

    /// <summary>Zone to gateway (0x04): an account's session left the zone.</summary>
    public readonly struct SessionReleasedMessage
    {
        /// <summary>Creates the message.</summary>
        public SessionReleasedMessage(AccountID accountId)
        {
            AccountId = accountId;
        }

        /// <summary>The released account.</summary>
        public AccountID AccountId { get; }
    }

    /// <summary>Zone to gateway (0x05): reply to <see cref="AdmitMessage"/>.</summary>
    public readonly struct AdmitResultMessage
    {
        /// <summary>Creates the message.</summary>
        public AdmitResultMessage(uint requestId, bool accepted, AdmitRejectReason reason)
        {
            RequestId = requestId;
            Accepted = accepted;
            Reason = reason;
        }

        /// <summary>The request id echoed from the Admit.</summary>
        public uint RequestId { get; }

        /// <summary>Whether the zone admitted the account.</summary>
        public bool Accepted { get; }

        /// <summary>Rejection reason; <see cref="AdmitRejectReason.None"/> if and only if accepted.</summary>
        public AdmitRejectReason Reason { get; }
    }

    /// <summary>Zone to gateway (0x06): reply to <see cref="EvictMessage"/>.</summary>
    public readonly struct EvictResultMessage
    {
        /// <summary>Creates the message.</summary>
        public EvictResultMessage(uint requestId)
        {
            RequestId = requestId;
        }

        /// <summary>The request id echoed from the Evict.</summary>
        public uint RequestId { get; }
    }

    /// <summary>Zone to gateway (0x07): reply to <see cref="ReconnectMessage"/>.</summary>
    public readonly struct ReconnectResultMessage
    {
        /// <summary>Creates the message. <paramref name="newToken"/> must be 16 bytes (all zeros when rejected).</summary>
        public ReconnectResultMessage(uint requestId, bool accepted, byte[] newToken)
        {
            RequestId = requestId;
            Accepted = accepted;
            NewToken = newToken;
        }

        /// <summary>The request id echoed from the Reconnect.</summary>
        public uint RequestId { get; }

        /// <summary>Whether the reconnect was accepted.</summary>
        public bool Accepted { get; }

        /// <summary>The replacement token (16 bytes); all zeros when rejected.</summary>
        public byte[] NewToken { get; }
    }

    /// <summary>Gateway to zone (0x81): admit an account.</summary>
    public readonly struct AdmitMessage
    {
        /// <summary>Creates the message. <paramref name="token"/> must be 16 bytes.</summary>
        public AdmitMessage(uint requestId, AccountID accountId, CharacterID characterId, byte[] token)
        {
            RequestId = requestId;
            AccountId = accountId;
            CharacterId = characterId;
            Token = token;
        }

        /// <summary>Correlation id echoed by the reply.</summary>
        public uint RequestId { get; }

        /// <summary>The account to admit.</summary>
        public AccountID AccountId { get; }

        /// <summary>The character to admit.</summary>
        public CharacterID CharacterId { get; }

        /// <summary>The zone-entry token (16 bytes).</summary>
        public byte[] Token { get; }
    }

    /// <summary>Gateway to zone (0x82): evict an account.</summary>
    public readonly struct EvictMessage
    {
        /// <summary>Creates the message.</summary>
        public EvictMessage(uint requestId, AccountID accountId)
        {
            RequestId = requestId;
            AccountId = accountId;
        }

        /// <summary>Correlation id echoed by the reply.</summary>
        public uint RequestId { get; }

        /// <summary>The account to evict.</summary>
        public AccountID AccountId { get; }
    }

    /// <summary>Gateway to zone (0x83): reconnect an account.</summary>
    public readonly struct ReconnectMessage
    {
        /// <summary>Creates the message. <paramref name="token"/> must be 16 bytes.</summary>
        public ReconnectMessage(uint requestId, AccountID accountId, byte[] token)
        {
            RequestId = requestId;
            AccountId = accountId;
            Token = token;
        }

        /// <summary>Correlation id echoed by the reply.</summary>
        public uint RequestId { get; }

        /// <summary>The reconnecting account.</summary>
        public AccountID AccountId { get; }

        /// <summary>The token (16 bytes).</summary>
        public byte[] Token { get; }
    }

    /// <summary>
    /// A decoded gateway to zone message (what the zone receives). Read the field matching
    /// <see cref="Type"/>; the others are default.
    /// </summary>
    public readonly struct GatewayToZoneMessage
    {
        internal GatewayToZoneMessage(GatewayLinkMessageType type, AdmitMessage admit, EvictMessage evict, ReconnectMessage reconnect)
        {
            Type = type;
            Admit = admit;
            Evict = evict;
            Reconnect = reconnect;
        }

        /// <summary>Which message this is (Admit, Evict or Reconnect).</summary>
        public GatewayLinkMessageType Type { get; }

        /// <summary>Valid when <see cref="Type"/> is Admit.</summary>
        public AdmitMessage Admit { get; }

        /// <summary>Valid when <see cref="Type"/> is Evict.</summary>
        public EvictMessage Evict { get; }

        /// <summary>Valid when <see cref="Type"/> is Reconnect.</summary>
        public ReconnectMessage Reconnect { get; }
    }

    /// <summary>
    /// A decoded zone to gateway message (what the gateway receives). Read the field matching
    /// <see cref="Type"/>; the others are default. <c>ZoneDraining</c> has no body and no field.
    /// </summary>
    public readonly struct ZoneToGatewayMessage
    {
        internal ZoneToGatewayMessage(
            GatewayLinkMessageType type,
            RegisterZoneMessage registerZone,
            PlayerCountMessage playerCount,
            SessionReleasedMessage sessionReleased,
            AdmitResultMessage admitResult,
            EvictResultMessage evictResult,
            ReconnectResultMessage reconnectResult)
        {
            Type = type;
            RegisterZone = registerZone;
            PlayerCount = playerCount;
            SessionReleased = sessionReleased;
            AdmitResult = admitResult;
            EvictResult = evictResult;
            ReconnectResult = reconnectResult;
        }

        /// <summary>Which message this is (0x01-0x07).</summary>
        public GatewayLinkMessageType Type { get; }

        /// <summary>Valid when <see cref="Type"/> is RegisterZone.</summary>
        public RegisterZoneMessage RegisterZone { get; }

        /// <summary>Valid when <see cref="Type"/> is PlayerCount.</summary>
        public PlayerCountMessage PlayerCount { get; }

        /// <summary>Valid when <see cref="Type"/> is SessionReleased.</summary>
        public SessionReleasedMessage SessionReleased { get; }

        /// <summary>Valid when <see cref="Type"/> is AdmitResult.</summary>
        public AdmitResultMessage AdmitResult { get; }

        /// <summary>Valid when <see cref="Type"/> is EvictResult.</summary>
        public EvictResultMessage EvictResult { get; }

        /// <summary>Valid when <see cref="Type"/> is ReconnectResult.</summary>
        public ReconnectResultMessage ReconnectResult { get; }
    }
}
