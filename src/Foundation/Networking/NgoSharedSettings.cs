namespace IronGrind.Networking
{
    /// <summary>
    /// The Netcode for GameObjects <c>NetworkConfig</c> values that the zone server and the client must agree on:
    /// NGO compares a hash of the config on connect, so one differing field fails every connect with
    /// "NetworkConfig mismatch" (Authentication Story 017 run). Lives in Foundation because that is the only assembly
    /// both sides may reference (ADR-012): Foundation, Client and the dev harness cannot see ServerLogic, and
    /// Foundation and Client do not reference NGO, so this holds plain values and each side applies them to its own
    /// <c>NetworkConfig</c>. A test pins the values against <c>ServerTickLoop.TICK_RATE_HZ</c>,
    /// <c>ClientMovementController.CLIENT_TICK_RATE_HZ</c> and the NGO defaults.
    /// </summary>
    public static class NgoSharedSettings
    {
        /// <summary>NGO network tick rate in Hz. Must equal <c>ServerTickLoop.TICK_RATE_HZ</c>.</summary>
        public const uint TICK_RATE = 20u;

        /// <summary>NGO connection approval. Off: the zone admits through its own entry token.</summary>
        public const bool CONNECTION_APPROVAL = false;

        /// <summary>NGO scene management. Off on both sides.</summary>
        public const bool ENABLE_SCENE_MANAGEMENT = false;

        /// <summary>
        /// NGO <c>ForceSamePrefabs</c>. True is NGO's default and the zone leaves it untouched; the flag is part of the
        /// config hash, so a side that changes it alone cannot connect.
        /// </summary>
        public const bool FORCE_SAME_PREFABS = true;
    }
}
