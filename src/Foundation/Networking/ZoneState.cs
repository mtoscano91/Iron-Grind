namespace IronGrind.Networking
{
    /// <summary>
    /// ST-NET-2 zone instance lifecycle state (<c>networking-test-harness.md</c> / <c>networking-core.md</c>).
    /// Declared unconditionally — referenced by production zone lifecycle code in later
    /// Networking Core / Zone Instancing stories, not just the test harness.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for wire compactness and IL2CPP efficiency, matching the
    /// convention already used by <c>GoldTransactionReason</c>. Values are pre-registered here —
    /// do not renumber once referenced by wire-serialized or persisted data.
    /// </remarks>
    public enum ZoneState : byte
    {
        /// <summary>No players present; the instance may be eligible for teardown.</summary>
        Empty = 0,

        /// <summary>At least one player present; the instance is fully simulating.</summary>
        Active = 1,

        /// <summary>The instance is closing — no new joins are accepted, existing players are being moved out.</summary>
        Draining = 2,

        /// <summary>The instance has fully torn down and no longer accepts any traffic.</summary>
        Closed = 3,
    }
}
