using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Savable form of one <see cref="StatModifier"/> inside <see cref="BuffDefinitionData"/> (Skill System
    /// Story 012). <see cref="StatModifier"/> is a readonly struct, which Unity does not serialize.
    /// </summary>
    [Serializable]
    public struct BuffModifierData
    {
        /// <summary>Stat the modifier applies to.</summary>
        public StatID StatId;

        /// <summary>Flat bonus; negative for a debuff.</summary>
        public float FlatBonus;

        /// <summary>Percentage bonus as a fraction (0.20 = +20%, character-stats.md F-1); negative for a debuff.</summary>
        public float PctBonus;

        /// <summary>Creates a modifier entry.</summary>
        /// <param name="statId">Stat the modifier applies to.</param>
        /// <param name="flatBonus">Flat bonus.</param>
        /// <param name="pctBonus">Percentage bonus as a fraction.</param>
        public BuffModifierData(StatID statId, float flatBonus, float pctBonus)
        {
            StatId = statId;
            FlatBonus = flatBonus;
            PctBonus = pctBonus;
        }

        /// <summary>The runtime modifier.</summary>
        /// <returns>A <see cref="StatModifier"/> with the same values.</returns>
        public StatModifier ToStatModifier() => new StatModifier(StatId, FlatBonus, PctBonus);
    }
}
