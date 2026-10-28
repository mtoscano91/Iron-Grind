using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// One stat change carried by a buff: a flat bonus and a percentage bonus to a single stat
    /// (status-effects.md CR-SE-1). Value type with no references.
    /// </summary>
    public readonly struct StatModifier
    {
        /// <summary>The stat this modifier changes.</summary>
        public readonly StatID StatId;

        /// <summary>Flat amount added to the stat.</summary>
        public readonly float FlatBonus;

        /// <summary>Percentage amount applied to the stat (fraction, e.g. 0.1 = +10%).</summary>
        public readonly float PctBonus;

        /// <summary>Builds a modifier. No validation is done here (Story 002 validates).</summary>
        /// <param name="statId">The stat changed.</param>
        /// <param name="flatBonus">Flat bonus.</param>
        /// <param name="pctBonus">Percentage bonus.</param>
        public StatModifier(StatID statId, float flatBonus, float pctBonus)
        {
            StatId = statId;
            FlatBonus = flatBonus;
            PctBonus = pctBonus;
        }
    }
}
