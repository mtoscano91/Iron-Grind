namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Per-class level-up data: which primary attributes are auto-allocated on level-up
    /// (CR-2.4) and how many free stat points are granted per level (CR-2.8).
    /// </summary>
    /// <remarks>
    /// <para><b>Forward-dependency stand-in.</b> The real Class System epic (GDD Complete,
    /// no concrete implementation yet in this codebase — confirmed via full-repo grep) will
    /// eventually own class data. This struct is the smallest shape the Leveling System's
    /// CR-2.4/CR-2.8 sequence needs; see <see cref="IClassRegistry"/> and
    /// <see cref="ClassRegistry"/> for the accompanying lookup mock.</para>
    /// <para>Auto-alloc increments are read in the fixed CR-2.4 iteration order
    /// {Strength, Dexterity, Vitality, Intelligence} — see
    /// <see cref="LevelingService"/>'s private auto-alloc order table.</para>
    /// </remarks>
    public readonly struct ClassDefinition
    {
        /// <summary>Strength gained automatically on every level-up. 0 = not auto-allocated for this class.</summary>
        public readonly int StrengthAutoAlloc;

        /// <summary>Dexterity gained automatically on every level-up. 0 = not auto-allocated for this class.</summary>
        public readonly int DexterityAutoAlloc;

        /// <summary>Vitality gained automatically on every level-up. 0 = not auto-allocated for this class.</summary>
        public readonly int VitalityAutoAlloc;

        /// <summary>Intelligence gained automatically on every level-up. 0 = not auto-allocated for this class.</summary>
        public readonly int IntelligenceAutoAlloc;

        /// <summary>Free (player-allocatable) stat points granted per level-up (CR-2.8).</summary>
        public readonly int FreePointsPerLevel;

        public ClassDefinition(
            int strengthAutoAlloc,
            int dexterityAutoAlloc,
            int vitalityAutoAlloc,
            int intelligenceAutoAlloc,
            int freePointsPerLevel)
        {
            StrengthAutoAlloc = strengthAutoAlloc;
            DexterityAutoAlloc = dexterityAutoAlloc;
            VitalityAutoAlloc = vitalityAutoAlloc;
            IntelligenceAutoAlloc = intelligenceAutoAlloc;
            FreePointsPerLevel = freePointsPerLevel;
        }
    }
}
