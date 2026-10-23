namespace IronGrind.Networking
{
    /// <summary>
    /// Wire-transmitted reason a zone join was rejected, carried on <see cref="ZoneFullResponse"/>
    /// (networking-wire-protocol.md, Zone Instancing Messages; zone-instancing.md CR-ZI-7). Backed by
    /// <see cref="byte"/> like <see cref="DisconnectReason"/>. Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The valid wire values are the gap-and-outlier set <c>{0, 1, 2, 255}</c>. An unknown byte is decoded as
    /// <see cref="Other"/> with an anomaly log by <see cref="WireEnumCodec.DecodeJoinRejectedReason"/>
    /// (CR-NET-7.4); the message is not dropped.
    /// </para>
    /// <para>Never use <see cref="System.Enum.IsDefined(System.Type, object)"/> for validation (forbidden on IL2CPP, CR-NET-7.4).</para>
    /// </remarks>
    public enum JoinRejectedReason : byte
    {
        /// <summary>The target zone has no free player slot.</summary>
        ZoneFull = 0,

        /// <summary>The requested zone id does not name a zone this server hosts.</summary>
        ZoneDoesNotExist = 1,

        /// <summary>Loading the character from persistence failed.</summary>
        LoadCharacterFailed = 2,

        /// <summary>Catch-all / substitution fallback for an unrecognized wire byte.</summary>
        Other = 255,
    }
}
