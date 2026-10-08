namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Serialization payload for the Leveling-System-owned per-entity state that Character
    /// Persistence cannot derive from <c>CharacterStats</c> alone (CR-6.3). Today this is only
    /// <c>heldFreePoints</c> — there is no <c>StatID</c> for it and it is not recoverable from
    /// <c>GetBaseStat</c> (AC-LS-29). Returned by <see cref="LevelingService.GetLevelingState"/>
    /// (save) and consumed by <see cref="LevelingService.RestoreLevelingState"/> (load).
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> per this codebase's small-data-payload idiom — see
    /// <see cref="LevelUpEventArgs"/>. <c>LevelTierMultiplier</c> is deliberately NOT part of
    /// this snapshot: CR-6.3 requires it always be derived on demand from
    /// <c>GetBaseStat(Level)</c>, never stored on save or load.
    /// </remarks>
    public readonly struct LevelingStateSnapshot
    {
        /// <summary>
        /// Number of free (player-allocatable) stat points held for the entity at the time this
        /// snapshot was taken. See <see cref="LevelingService.GetHeldFreePoints"/>.
        /// </summary>
        public readonly int HeldFreePoints;

        /// <summary>Constructs a new <see cref="LevelingStateSnapshot"/>.</summary>
        public LevelingStateSnapshot(int heldFreePoints)
        {
            HeldFreePoints = heldFreePoints;
        }
    }
}
