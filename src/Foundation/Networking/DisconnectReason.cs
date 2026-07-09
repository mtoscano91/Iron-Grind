namespace IronGrind.Networking
{
    /// <summary>
    /// Wire-transmitted reason a session was ended (CR-NET-7.9), carried on the
    /// <c>ZoneSessionEnded</c> message. Backed by <see cref="byte"/> for wire compactness and
    /// IL2CPP efficiency, matching the convention used by <see cref="SessionState"/>/<see cref="ZoneState"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="DamageType"/>/<see cref="DisconnectType"/>, this enum's valid wire values
    /// are <b>not</b> a contiguous range — they are the gap-and-outlier set
    /// <c>{0, 1, 2, 3, 255}</c>. A naive <c>rawByte &gt; maxDeclaredValue</c> check is wrong for
    /// this type; see <see cref="WireEnumCodec.DecodeDisconnectReason"/>,
    /// which validates via an explicit switch over exactly these 5 values. An out-of-range byte
    /// substitutes <see cref="Other"/> and logs an anomaly rather than dropping the message.
    /// </para>
    /// <para>Never use <see cref="System.Enum.IsDefined(System.Type, object)"/> for validation (forbidden on IL2CPP, CR-NET-7.4).</para>
    /// </remarks>
    public enum DisconnectReason : byte
    {
        /// <summary>The zone instance is shutting down.</summary>
        ZoneClosed = 0,

        /// <summary>Server maintenance.</summary>
        ServerShutdown = 1,

        /// <summary>An administrator kicked the session.</summary>
        AdminKick = 2,

        /// <summary>
        /// Reconnect rejected — the ghost entity died during the disconnect period (CGS-5/CGS-6).
        /// Triggers the client's respawn flow, not the reconnect flow.
        /// </summary>
        GhostDeath = 3,

        /// <summary>Catch-all / substitution fallback for an unrecognized wire byte.</summary>
        Other = 255,
    }
}
