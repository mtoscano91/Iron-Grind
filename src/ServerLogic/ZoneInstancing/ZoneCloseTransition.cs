namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Which lifecycle transition closed the zone (<c>zone-instancing.md</c> CR-ZI-2). The numeric
    /// values are the GDD's transition numbers (T-3, T-5, T-6, T-7), so a log line or a test failure
    /// can be matched to the States and Transitions table directly.
    /// </summary>
    /// <remarks>
    /// T-1 (process start) and T-2 (<c>Loading → Active</c>) and T-4 (<c>Active → Draining</c>) do not
    /// close the zone and therefore have no value here. In-process only; not on the wire.
    /// </remarks>
    public enum ZoneCloseTransition : byte
    {
        /// <summary>T-3: <c>Loading → Closed</c> — init failure or <c>ZONE_LOAD_TIMEOUT_SECONDS</c> elapsed.</summary>
        LoadFailed = 3,

        /// <summary>T-5: <c>Draining → Closed</c> — last occupied player slot released, or the drain grace expired.</summary>
        Drained = 5,

        /// <summary>T-6: any state → <c>Closed</c> — fatal error, <c>EntityIDCounterExhausted</c>, second quit, or quit while Loading.</summary>
        Emergency = 6,

        /// <summary>T-7: <c>Active → Closed</c> — no occupied player slot for <c>ZONE_IDLE_CLOSE_SECONDS</c>.</summary>
        Idle = 7,
    }
}
