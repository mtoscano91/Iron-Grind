using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// One buff applied to one entity (status-effects.md CR-SE-1). Deliberately a mutable struct:
    /// the effect table decrements <see cref="TicksRemaining"/> in place (Story 005). Holds no
    /// heap reference. Public fields are intentional for in-place mutation inside a table.
    /// </summary>
    public struct ActiveEffect
    {
        /// <summary>The buff type.</summary>
        public BuffID BuffId;

        /// <summary>The entity carrying the effect.</summary>
        public EntityID EntityId;

        /// <summary>The entity that applied the effect.</summary>
        public EntityID CasterEntityId;

        /// <summary>Ticks left before the effect expires.</summary>
        public uint TicksRemaining;

        /// <summary>Number of used modifier slots (0 to 8).</summary>
        public byte ModifierCount;

        /// <summary>Modifier slot 0.</summary>
        public StatModifier Modifier0;

        /// <summary>Modifier slot 1.</summary>
        public StatModifier Modifier1;

        /// <summary>Modifier slot 2.</summary>
        public StatModifier Modifier2;

        /// <summary>Modifier slot 3.</summary>
        public StatModifier Modifier3;

        /// <summary>Modifier slot 4.</summary>
        public StatModifier Modifier4;

        /// <summary>Modifier slot 5.</summary>
        public StatModifier Modifier5;

        /// <summary>Modifier slot 6.</summary>
        public StatModifier Modifier6;

        /// <summary>Modifier slot 7.</summary>
        public StatModifier Modifier7;

        /// <summary>HP restored per tick.</summary>
        public float HpRegenPerTick;

        /// <summary>MP restored per tick.</summary>
        public float MpRegenPerTick;

        /// <summary>Creates an effect from a definition; <see cref="TicksRemaining"/> starts at the definition's duration.</summary>
        /// <param name="definition">The authored buff.</param>
        /// <param name="entityId">The entity that carries the effect.</param>
        /// <param name="casterEntityId">The entity that applied it.</param>
        public ActiveEffect(in BuffDefinition definition, EntityID entityId, EntityID casterEntityId)
        {
            BuffId = definition.BuffId;
            EntityId = entityId;
            CasterEntityId = casterEntityId;
            TicksRemaining = definition.DurationTicks;
            ModifierCount = definition.ModifierCount;
            Modifier0 = definition.Modifier0;
            Modifier1 = definition.Modifier1;
            Modifier2 = definition.Modifier2;
            Modifier3 = definition.Modifier3;
            Modifier4 = definition.Modifier4;
            Modifier5 = definition.Modifier5;
            Modifier6 = definition.Modifier6;
            Modifier7 = definition.Modifier7;
            HpRegenPerTick = definition.HpRegenPerTick;
            MpRegenPerTick = definition.MpRegenPerTick;
        }

        /// <summary>Returns the modifier in slot <paramref name="index"/>; slots at or above <see cref="ModifierCount"/> are default.</summary>
        /// <param name="index">Slot index, 0 to 7.</param>
        /// <exception cref="ArgumentOutOfRangeException">Index outside 0-7.</exception>
        public readonly StatModifier GetModifier(int index)
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
