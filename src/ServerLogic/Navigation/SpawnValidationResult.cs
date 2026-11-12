namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>Outcome of <see cref="SpawnPointValidator.Validate"/> (CR-NAV-13).</summary>
    public readonly struct SpawnValidationResult
    {
        /// <summary>Creates a result.</summary>
        /// <param name="validCount">Spawn points that sit on the NavMesh.</param>
        /// <param name="totalCount">Spawn points declared.</param>
        /// <param name="shouldAbort">True when the valid share is below MIN_VALID_SPAWN_FRACTION.</param>
        public SpawnValidationResult(int validCount, int totalCount, bool shouldAbort)
        {
            ValidCount = validCount;
            TotalCount = totalCount;
            ShouldAbort = shouldAbort;
        }

        /// <summary>Spawn points that sit on the NavMesh.</summary>
        public int ValidCount { get; }

        /// <summary>Spawn points declared.</summary>
        public int TotalCount { get; }

        /// <summary>
        /// True when the zone cannot serve (CR-NAV-13). The validator only computes this; the caller aborts zone
        /// initialisation.
        /// </summary>
        public bool ShouldAbort { get; }
    }
}
