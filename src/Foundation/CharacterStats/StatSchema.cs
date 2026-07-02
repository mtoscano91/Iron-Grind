namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Classifies StatID values into int-schema or float-schema, provides array-index
    /// arithmetic for float stats, and defines StatMin / StatMax per stat.
    /// </summary>
    /// <remarks>
    /// <para><b>Int-schema stats</b> (Strength – Experience) are stored in the per-entity
    /// <c>int[]</c> array indexed by <c>(int)StatID</c>. The array size is
    /// <c>(int)StatID.Experience + 1 = 12</c>.</para>
    ///
    /// <para><b>Float-schema stats</b> (CritChance – MovementSpeed) are stored in a
    /// separate per-entity <c>float[]</c> array of size <see cref="FloatStatArraySize"/>.
    /// Use <see cref="FloatStatIndex"/> to convert a float-schema StatID to its
    /// float-array index.</para>
    ///
    /// <para>This class is <see langword="internal"/> — it is a storage implementation
    /// detail. External code reads and writes stats exclusively through the public
    /// <see cref="CharacterStats"/> API.</para>
    /// </remarks>
    internal static class StatSchema
    {
        // First float-schema StatID value (= 12).
        private const int FloatStatStart = (int)StatID.CritChance;

        /// <summary>
        /// Number of float-schema stats. Used to size the per-entity float[] array.
        /// Currently 5 (CritChance, CritMultiplier, AttackRange, AttackSpeedMultiplier, MovementSpeed).
        /// </summary>
        public const int FloatStatArraySize = (int)StatID.MovementSpeed - FloatStatStart + 1;

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="statId"/> is a
        /// float-schema stat (CritChance through MovementSpeed).
        /// </summary>
        public static bool IsFloatStat(StatID statId)
            => (int)statId >= FloatStatStart;

        /// <summary>
        /// Converts a float-schema <paramref name="statId"/> to its zero-based index
        /// into the per-entity <c>float[]</c> array.
        /// Calling this with an int-schema StatID is a logic error; the result is negative.
        /// </summary>
        public static int FloatStatIndex(StatID statId)
            => (int)statId - FloatStatStart;

        /// <summary>
        /// Returns the StatMin clamp lower bound for <paramref name="statId"/>.
        /// Applied by GetEffectiveStat / GetEffectiveStatFloat after the full formula.
        /// </summary>
        public static float GetStatMin(StatID statId)
        {
            switch (statId)
            {
                case StatID.AttackPower:           return 1f;
                case StatID.MaxHP:                 return 1f;
                case StatID.Defense:               return 0f;
                case StatID.MaxMP:                 return 0f;
                case StatID.Level:                 return 1f;
                case StatID.Experience:            return 0f;
                case StatID.Strength:              return 1f;
                case StatID.Dexterity:             return 1f;
                case StatID.Vitality:              return 1f;
                case StatID.Intelligence:          return 1f;
                case StatID.CurrentHP:             return 0f;
                case StatID.CurrentMP:             return 0f;
                case StatID.CritChance:            return 0f;
                case StatID.CritMultiplier:        return 1f;
                case StatID.AttackRange:           return 0.5f;
                case StatID.AttackSpeedMultiplier: return 0.5f;
                case StatID.MovementSpeed:         return 0.5f;
                default:                           return 0f;
            }
        }

        /// <summary>
        /// Returns the StatMax clamp upper bound for <paramref name="statId"/>.
        /// Applied by GetEffectiveStat / GetEffectiveStatFloat after the full formula.
        /// </summary>
        public static float GetStatMax(StatID statId)
        {
            switch (statId)
            {
                case StatID.MaxHP:                 return 99999f;
                case StatID.AttackPower:           return 99999f;
                case StatID.Defense:               return 9999f;
                case StatID.MaxMP:                 return 9999f;
                case StatID.Level:                 return 60f;
                case StatID.Experience:            return 999999f;
                case StatID.Strength:              return 999f;
                case StatID.Dexterity:             return 999f;
                case StatID.Vitality:              return 999f;
                case StatID.Intelligence:          return 999f;
                case StatID.CurrentHP:             return 99999f;
                case StatID.CurrentMP:             return 9999f;
                case StatID.CritChance:            return 0.75f;
                case StatID.CritMultiplier:        return 3.0f;
                case StatID.AttackRange:           return 20f;
                case StatID.AttackSpeedMultiplier: return 2.0f;
                case StatID.MovementSpeed:         return 20f;
                default:                           return 99999f;
            }
        }
    }
}
