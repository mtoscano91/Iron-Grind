namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Identifies a character stat slot. Backed by <see cref="byte"/> to keep enum
    /// comparisons cheap on iOS IL2CPP and to minimise array-index arithmetic cost.
    /// The 256-value ceiling (0–255) is sufficient for all planned stats.
    /// </summary>
    /// <remarks>
    /// <para>When adding a new <see cref="StatID"/> value, prefer appending to the end of its
    /// schema block. <c>MagicDefense</c> (value 7) is inserted mid-block rather than appended —
    /// see its own doc comment for why that was still safe (every array-size constant in this
    /// file is computed from enum member references, never a hardcoded literal).</para>
    ///
    /// <para><b>Int-schema stats</b> (Strength – Experience, values 0–12) are stored in the
    /// per-entity <c>int[]</c> array indexed by <c>(int)StatID</c>.</para>
    ///
    /// <para><b>Float-schema stats</b> (CritChance – MovementSpeed, values 13–17) are stored
    /// in a separate per-entity <c>float[]</c> via <c>FloatStatValues</c>. Their enum values
    /// must never be used as indices into the int array. Use
    /// <see cref="StatSchema.IsFloatStat"/> and <see cref="StatSchema.FloatStatIndex"/>.</para>
    ///
    /// <para>INVARIANT: <c>Experience</c> must remain the highest-valued
    /// <b>int-schema</b> StatID member. <c>MovementSpeed</c> must remain the highest-valued
    /// float-schema StatID member. Adding a new stat in either schema: append to the
    /// corresponding block (or insert carefully, per the <c>MagicDefense</c> precedent) and
    /// update <see cref="StatSchema"/> constants accordingly.</para>
    /// </remarks>
    public enum StatID : byte
    {
        // -----------------------------------------------------------------------
        // Player primary stats — player-only.
        // These fields are never written for mob entities; GetBaseStat returns 0.
        // -----------------------------------------------------------------------

        /// <summary>Base strength. Drives the melee Attack Power formula.</summary>
        Strength     = 0,

        /// <summary>Base dexterity. Drives critical-chance and attack-speed formulas.</summary>
        Dexterity    = 1,

        /// <summary>Base vitality. Drives the MaxHP formula (F-3).</summary>
        Vitality     = 2,

        /// <summary>Base intelligence. Drives the MaxMP formula (F-4).</summary>
        Intelligence = 3,

        // -----------------------------------------------------------------------
        // Derived stats — valid for both player and mob entities.
        // -----------------------------------------------------------------------

        /// <summary>Maximum hit-points ceiling. Derived from Vitality (player) or written directly (mob).</summary>
        MaxHP        = 4,

        /// <summary>Base attack damage before reduction. Derived from Strength (player) or written directly (mob).</summary>
        AttackPower  = 5,

        /// <summary>Incoming damage reduction. Derived from equipment and class, or written directly (mob).</summary>
        Defense      = 6,

        /// <summary>
        /// Elemental damage mitigation (fire/cold/lightning/poison share one value, F-7). Derived
        /// from Intelligence (player) or written directly (mob). Added post-Character-Stats-Story-007
        /// (Leveling System Story 002) — the GDD always required this stat (character-stats.md F-7,
        /// range [0, 9999]) but it was never added to this enum. Inserted here, not appended at the
        /// end, to keep the "Derived stats" grouping coherent; every array size in this file
        /// (<c>StatArraySize</c>, <c>StatSchema.FloatStatArraySize</c>/<c>FloatStatStart</c>) is
        /// computed from enum member references, not hardcoded literals, so this insertion safely
        /// renumbers every member below it with no other code changes required.
        /// </summary>
        MagicDefense = 7,

        // -----------------------------------------------------------------------
        // Resource pools — valid for both player and mob entities.
        // Mobs have CurrentHP managed by ApplyDamage / ApplyRegen (Story 004).
        // MaxMP and CurrentMP are never written for mobs; GetBaseStat returns 0.
        // -----------------------------------------------------------------------

        /// <summary>Current hit points. Written by ApplyDamage / ApplyRegen (Story 004). Valid for mobs.</summary>
        CurrentHP    = 8,

        /// <summary>Maximum mana ceiling. Derived from Intelligence.</summary>
        MaxMP        = 9,

        /// <summary>Current mana. Written by ConsumeMana / ApplyManaRegen (Story 004).</summary>
        CurrentMP    = 10,

        // -----------------------------------------------------------------------
        // Progression stats — player-only for Experience; Level valid for mobs.
        // Level is written for mobs by the mob data table (mob level scaling).
        // Experience is never written for mobs; GetBaseStat returns 0.
        // -----------------------------------------------------------------------

        /// <summary>Character level (1–60). Written by the Leveling System. Valid for mobs (mob level scaling).</summary>
        Level        = 11,

        /// <summary>Accumulated experience points. Written by AddExperience (Story 006).</summary>
        Experience   = 12,

        // -----------------------------------------------------------------------
        // Float-schema stats (Story 002+).
        // Stored in FloatStatValues, NOT in the int[] stat array.
        // Use StatSchema.IsFloatStat() / StatSchema.FloatStatIndex() to work with these.
        // INVARIANT: Experience (= 12) must remain the highest int-schema value.
        // INVARIANT: MovementSpeed must remain the highest float-schema value.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Probability of landing a critical hit (0.0–0.75). Float-schema.
        /// StatMax = 0.75 (AC-03). Use GetEffectiveStatFloat / GetBaseStatFloat.
        /// </summary>
        CritChance            = 13,

        /// <summary>
        /// Damage multiplier applied on a critical hit (1.0–3.0). Float-schema.
        /// Use GetEffectiveStatFloat / GetBaseStatFloat.
        /// </summary>
        CritMultiplier        = 14,

        /// <summary>
        /// Maximum attack reach in world units (0.5–20.0). Float-schema.
        /// Use GetEffectiveStatFloat / GetBaseStatFloat.
        /// </summary>
        AttackRange           = 15,

        /// <summary>
        /// Multiplier applied to base attack cadence (0.5–2.0). Float-schema.
        /// StatMin = 0.5, StatMax = 2.0 (AC-28a/b). Use GetEffectiveStatFloat / GetBaseStatFloat.
        /// </summary>
        AttackSpeedMultiplier = 16,

        /// <summary>
        /// Character movement speed in world units per second (0.5–20.0). Float-schema.
        /// Use GetEffectiveStatFloat / GetBaseStatFloat.
        /// </summary>
        MovementSpeed         = 17,
    }
}
