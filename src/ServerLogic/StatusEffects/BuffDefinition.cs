using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Authored description of a buff or debuff: up to <see cref="StatusEffectsConstants.MAX_STATS_PER_BUFF"/>
    /// stat modifiers, a duration in ticks and per-tick regeneration (status-effects.md CR-SE-1).
    /// Modifiers are eight inline fields rather than an array so the type holds no heap reference.
    /// </summary>
    public readonly struct BuffDefinition
    {
        /// <summary>The buff type.</summary>
        public readonly BuffID BuffId;

        /// <summary>Number of used modifier slots (0 to 8).</summary>
        public readonly byte ModifierCount;

        /// <summary>Modifier slot 0.</summary>
        public readonly StatModifier Modifier0;

        /// <summary>Modifier slot 1.</summary>
        public readonly StatModifier Modifier1;

        /// <summary>Modifier slot 2.</summary>
        public readonly StatModifier Modifier2;

        /// <summary>Modifier slot 3.</summary>
        public readonly StatModifier Modifier3;

        /// <summary>Modifier slot 4.</summary>
        public readonly StatModifier Modifier4;

        /// <summary>Modifier slot 5.</summary>
        public readonly StatModifier Modifier5;

        /// <summary>Modifier slot 6.</summary>
        public readonly StatModifier Modifier6;

        /// <summary>Modifier slot 7.</summary>
        public readonly StatModifier Modifier7;

        /// <summary>Duration in server ticks.</summary>
        public readonly uint DurationTicks;

        /// <summary>HP restored per tick.</summary>
        public readonly float HpRegenPerTick;

        /// <summary>MP restored per tick.</summary>
        public readonly float MpRegenPerTick;

        /// <summary>
        /// Builds a definition. Stats and duration are not validated here (ApplyEffect does that, Story 002).
        /// </summary>
        /// <param name="buffId">The buff type.</param>
        /// <param name="modifiers">Modifiers copied into the inline slots; unused slots stay default.</param>
        /// <param name="durationTicks">Duration in ticks.</param>
        /// <param name="hpRegenPerTick">HP regen per tick.</param>
        /// <param name="mpRegenPerTick">MP regen per tick.</param>
        /// <exception cref="ArgumentException">More than <see cref="StatusEffectsConstants.MAX_STATS_PER_BUFF"/> modifiers.</exception>
        public BuffDefinition(
            BuffID buffId,
            ReadOnlySpan<StatModifier> modifiers,
            uint durationTicks,
            float hpRegenPerTick,
            float mpRegenPerTick)
        {
            if (modifiers.Length > StatusEffectsConstants.MAX_STATS_PER_BUFF)
                throw new ArgumentException("Too many modifiers for one buff.", nameof(modifiers));

            int count = modifiers.Length;
            BuffId = buffId;
            ModifierCount = (byte)count;
            Modifier0 = count > 0 ? modifiers[0] : default;
            Modifier1 = count > 1 ? modifiers[1] : default;
            Modifier2 = count > 2 ? modifiers[2] : default;
            Modifier3 = count > 3 ? modifiers[3] : default;
            Modifier4 = count > 4 ? modifiers[4] : default;
            Modifier5 = count > 5 ? modifiers[5] : default;
            Modifier6 = count > 6 ? modifiers[6] : default;
            Modifier7 = count > 7 ? modifiers[7] : default;
            DurationTicks = durationTicks;
            HpRegenPerTick = hpRegenPerTick;
            MpRegenPerTick = mpRegenPerTick;
        }

        /// <summary>Returns the modifier in slot <paramref name="index"/>; slots at or above <see cref="ModifierCount"/> are default.</summary>
        /// <param name="index">Slot index, 0 to 7.</param>
        /// <exception cref="ArgumentOutOfRangeException">Index outside 0-7.</exception>
        public StatModifier GetModifier(int index)
        {
            switch (index)
            {
                case 0: return Modifier0;
                case 1: return Modifier1;
                case 2: return Modifier2;
                case 3: return Modifier3;
                case 4: return Modifier4;
                case 5: return Modifier5;
                case 6: return Modifier6;
                case 7: return Modifier7;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "Must be in 0-7.");
            }
        }
    }
}
