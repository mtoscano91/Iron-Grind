namespace IronGrind.Networking
{
    /// <summary>
    /// Wire-transmitted classification of how a client left a zone (CR-NET-7.9), carried on the
    /// <c>PlayerLeftZone</c> message. Backed by <see cref="byte"/> for wire compactness and
    /// IL2CPP efficiency, matching the convention used by <see cref="SessionState"/>/<see cref="ZoneState"/>.
    /// </summary>
    /// <remarks>
    /// Receivers must range-check the received byte before casting — never
    /// <see cref="System.Enum.IsDefined(System.Type, object)"/> (forbidden on IL2CPP, CR-NET-7.4).
    /// See <see cref="WireEnumCodec.DecodeDisconnectType"/> for the
    /// guarded decode path: an out-of-range byte substitutes <see cref="Timeout"/> and logs an
    /// anomaly — the entity is still despawned rather than the message being dropped.
    /// </remarks>
    public enum DisconnectType : byte
    {
        /// <summary>The player sent an explicit logout.</summary>
        Graceful = 0,

        /// <summary>Heartbeat timeout fired — the session TTL may still be active.</summary>
        Timeout = 1,

        /// <summary>The player is moving to a different zone.</summary>
        ZoneTransfer = 2,
    }
}
