using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Savable form of a <see cref="BuffDefinition"/>, authored on <see cref="SkillDefinition.BuffData"/>
    /// (Skill System Story 012). <see cref="BuffDefinition"/> is a readonly struct, which Unity does not serialize;
    /// <see cref="SkillDatabase"/> builds the runtime definition from this once, when it builds its lookup.
    /// </summary>
    /// <remarks>An entry with <see cref="DurationTicks"/> 0, no modifiers and no regen counts as "not authored".</remarks>
    [Serializable]
    public struct BuffDefinitionData
    {
        /// <summary>Duration in ticks, in [1, 6000] (status-effects.md F-SE-1).</summary>
        public uint DurationTicks;

        /// <summary>HP restored per tick; 0 = none.</summary>
        public float HpRegenPerTick;

        /// <summary>MP restored per tick; 0 = none.</summary>
        public float MpRegenPerTick;

        /// <summary>Stat modifiers, at most <see cref="StatusEffectsConstants.MAX_STATS_PER_BUFF"/>, one per stat.</summary>
        public BuffModifierData[] Modifiers;

        /// <summary>True when anything is authored (a duration, a modifier or a regen value).</summary>
        public bool IsAuthored => DurationTicks > 0 || (Modifiers != null && Modifiers.Length > 0)
            || HpRegenPerTick != 0f || MpRegenPerTick != 0f;

        /// <summary>
        /// Checks the data against the Status Effects rules: duration in [1, 6000], at most
        /// <see cref="StatusEffectsConstants.MAX_STATS_PER_BUFF"/> modifiers, no stat twice (EC: InvalidStat),
        /// and at least one modifier or a regen value (NoOpDefinition).
        /// </summary>
        /// <param name="error">Why the data is invalid; null when valid.</param>
        /// <returns>True when valid.</returns>
        public bool TryValidate(out string error)
        {
            if (DurationTicks < 1 || DurationTicks > MAX_DURATION_TICKS)
            {
                error = "duration " + DurationTicks + " is outside [1, " + MAX_DURATION_TICKS + "]";
                return false;
            }

            int count = Modifiers == null ? 0 : Modifiers.Length;
            if (count > StatusEffectsConstants.MAX_STATS_PER_BUFF)
            {
                error = count + " modifiers, more than " + StatusEffectsConstants.MAX_STATS_PER_BUFF;
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (Modifiers[i].StatId == Modifiers[j].StatId)
                    {
                        error = "stat " + Modifiers[i].StatId + " appears twice";
                        return false;
                    }
                }
            }

            if (count == 0 && HpRegenPerTick == 0f && MpRegenPerTick == 0f)
            {
                error = "no modifier and no regen";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Builds the runtime definition. Call <see cref="TryValidate"/> first.</summary>
        /// <param name="buffId">The skill's buff id.</param>
        /// <returns>The runtime <see cref="BuffDefinition"/>.</returns>
        /// <exception cref="ArgumentException">More than <see cref="StatusEffectsConstants.MAX_STATS_PER_BUFF"/> modifiers.</exception>
        public BuffDefinition ToBuffDefinition(BuffID buffId)
        {
            int count = Modifiers == null ? 0 : Modifiers.Length;
            Span<StatModifier> modifiers = stackalloc StatModifier[StatusEffectsConstants.MAX_STATS_PER_BUFF];
            if (count > modifiers.Length)
            {
                throw new ArgumentException("Too many modifiers for one buff.", nameof(Modifiers));
            }

            for (int i = 0; i < count; i++)
            {
                modifiers[i] = Modifiers[i].ToStatModifier();
            }

            return new BuffDefinition(buffId, modifiers.Slice(0, count), DurationTicks, HpRegenPerTick, MpRegenPerTick);
        }

        private const uint MAX_DURATION_TICKS = 6000;
    }
}
