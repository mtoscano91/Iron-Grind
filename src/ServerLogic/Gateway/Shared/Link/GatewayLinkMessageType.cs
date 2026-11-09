namespace IronGrind.ServerLogic.Gateway.Link
{
    /// <summary>
    /// Message type byte of a gateway link frame (Gateway Story 001, ADR-016 Decision 3). The high bit marks
    /// gateway to zone messages (0x81-0x83); zone to gateway messages are 0x01-0x07.
    /// </summary>
    public enum GatewayLinkMessageType : byte
    {
        /// <summary>Zone to gateway: zone announces itself and its live accounts (11 + 4n body bytes).</summary>
        RegisterZone = 0x01,

        /// <summary>Zone to gateway: current player count (2 body bytes).</summary>
        PlayerCount = 0x02,

        /// <summary>Zone to gateway: the zone is draining (empty body).</summary>
        ZoneDraining = 0x03,

        /// <summary>Zone to gateway: an account's session left the zone (4 body bytes).</summary>
        SessionReleased = 0x04,

        /// <summary>Zone to gateway: reply to Admit (6 body bytes).</summary>
        AdmitResult = 0x05,

        /// <summary>Zone to gateway: reply to Evict (4 body bytes).</summary>
        EvictResult = 0x06,

        /// <summary>Zone to gateway: reply to Reconnect (21 body bytes).</summary>
        ReconnectResult = 0x07,

        /// <summary>Gateway to zone: admit an account with a zone-entry token (28 body bytes).</summary>
        Admit = 0x81,

        /// <summary>Gateway to zone: evict an account (8 body bytes).</summary>
        Evict = 0x82,

        /// <summary>Gateway to zone: reconnect an account (24 body bytes).</summary>
        Reconnect = 0x83,
    }
}
