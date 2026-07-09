namespace IronGrind.Networking
{
    /// <summary>
    /// ST-NET-1 session lifecycle state (<c>networking-test-harness.md</c> / <c>networking-session.md</c>).
    /// Declared unconditionally — referenced by production session-management code in later
    /// Networking Core stories, not just the test harness.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for wire compactness and IL2CPP efficiency, matching the
    /// convention already used by <c>GoldTransactionReason</c>. Values are pre-registered here —
    /// do not renumber once referenced by wire-serialized or persisted data.
    /// </remarks>
    public enum SessionState : byte
    {
        /// <summary>Connection established, authentication in progress.</summary>
        Connecting = 0,

        /// <summary>Authenticated and fully joined — the normal steady-state.</summary>
        Connected = 1,

        /// <summary>Transport dropped, but the session is still held open pending reconnection.</summary>
        Disconnected_SessionActive = 2,

        /// <summary>A dropped session's client has reconnected and is re-authenticating.</summary>
        Reconnecting = 3,

        /// <summary>The session's TTL expired while disconnected — the session is no longer recoverable.</summary>
        Disconnected_SessionExpired = 4,
    }
}
