namespace IronGrind.Client.Session
{
    /// <summary>Why a zone entry failed (reported once through <see cref="ClientZoneSession.EntryFailed"/>).</summary>
    public enum ZoneEntryFailureReason
    {
        /// <summary>The initial login was refused or failed.</summary>
        LoginFailed,

        /// <summary>The zone answered <c>ZoneFullResponse</c>; see <see cref="ClientZoneSession.ZoneFullReason"/>.</summary>
        ZoneFull,

        /// <summary>The transport closed (or never connected) before the <c>ZoneEntryAck</c> was sent.</summary>
        ClosedBeforeAck,

        /// <summary>The snapshot announced more fragments than <c>MaxClientSnapshotFragments</c>.</summary>
        SnapshotTooLarge,

        /// <summary>No complete snapshot arrived in time, twice.</summary>
        SnapshotTimeout,

        /// <summary>The transport did not connect within <c>ConnectTimeoutSeconds</c> (initial connection or reconnect).</summary>
        ConnectTimeout
    }
}
