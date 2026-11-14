namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Result of <see cref="MobSpawnSeeder.Seed"/>: whether the spawn is Enraged and the two values
    /// downstream stories consume (F-AI-E-1, EC-AI-11).
    /// </summary>
    public readonly struct MobSpawnVariant
    {
        /// <summary>Initializes a variant.</summary>
        /// <param name="isEnraged">True when the spawn roll succeeded.</param>
        /// <param name="effectiveKillXP">Kill XP after the Enraged multiplier (Story 012).</param>
        /// <param name="tierShift">Loot quality tier shift (Story 011).</param>
        public MobSpawnVariant(bool isEnraged, int effectiveKillXP, int tierShift)
        {
            IsEnraged = isEnraged;
            EffectiveKillXP = effectiveKillXP;
            TierShift = tierShift;
        }

        /// <summary>True when this spawn is Enraged.</summary>
        public bool IsEnraged { get; }

        /// <summary>
        /// Kill XP: <c>RoundToInt(KillXP * EnragedXPMultiplier)</c> when Enraged, else <c>KillXP</c>.
        /// Consumed by Story 012.
        /// </summary>
        public int EffectiveKillXP { get; }

        /// <summary>
        /// Loot quality tier shift: <c>EnragedLootQualityBonusTier</c> when Enraged, else 0. A property of the
        /// mob, independent of any damage record (EC-AI-11). Consumed by Story 011.
        /// </summary>
        public int TierShift { get; }
    }
}
