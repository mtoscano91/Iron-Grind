namespace IronGrind.Networking
{
    /// <summary>
    /// Zone instance lifecycle state. <c>zone-instancing.md</c> CR-ZI-2 owns the lifecycle and its
    /// transitions (T-1 to T-7); <c>networking-session.md</c> ST-NET-2 defines the occupancy rules
    /// that drive it. Declared unconditionally — referenced by production zone lifecycle code, not
    /// just the test harness.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for wire compactness and IL2CPP efficiency, matching the
    /// convention already used by <c>GoldTransactionReason</c>. Values are pre-registered here —
    /// do not renumber once referenced by wire-serialized or persisted data.
    /// </remarks>
    public enum ZoneState : byte
    {
        /// <summary>The zone process has started and is loading (T-1); not yet joinable. Leaves via T-2 (Active) or T-3 / T-6 (Closed).</summary>
        Loading = 0,

        /// <summary>The zone is fully simulating and accepts joins (entered via T-2). Leaves via T-4 (Draining), T-6 or T-7 (Closed).</summary>
        Active = 1,

        /// <summary>The zone is winding down after a quit request (entered only via T-4): no new joins, and it never returns to Active. Leaves via T-5 or T-6 (Closed).</summary>
        Draining = 2,

        /// <summary>Terminal: the zone has closed and accepts no traffic; no transition leaves this state.</summary>
        Closed = 3,
    }
}
