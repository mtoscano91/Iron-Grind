using System;

namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Represents a single buff or debuff's contribution to the modifier stack.
    /// <see langword="readonly struct"/> — zero heap allocation on the modifier-stack hot path.
    /// Stored in a fixed-capacity array (capacity 32 per entity).
    /// </summary>
    /// <remarks>
    /// Modifier stack application (AddBuffModifier / RemoveBuffModifier)
    /// is implemented in Story 003. <see cref="IEquatable{T}"/> is implemented here so that
    /// Story 003 can safely use this type as a Dictionary or HashSet key without IL2CPP boxing.
    /// </remarks>
    public readonly struct BuffModifierEntry : IEquatable<BuffModifierEntry>
    {
        /// <summary>Flat stat bonus. Applied before percentage bonuses in the modifier stack.</summary>
        public readonly float FlatBonus;

        /// <summary>
        /// Percentage bonus (e.g., 0.10f = +10%). Within the buff modifier layer,
        /// percentage bonuses are additive — two +10% entries sum to +20%, not +21%.
        /// Applied after all flat bonuses.
        /// </summary>
        public readonly float PctBonus;

        /// <summary>
        /// Remaining duration in server ticks. Zero means the buff has expired.
        /// Tick countdown is managed by Story 003 (modifier lifecycle).
        /// </summary>
        public readonly int DurationTicks;

        /// <summary>The buff type that owns this modifier entry.</summary>
        public readonly BuffID Id;

        /// <summary>Initializes a new <see cref="BuffModifierEntry"/>.</summary>
        public BuffModifierEntry(float flatBonus, float pctBonus, int durationTicks, BuffID id)
        {
            FlatBonus     = flatBonus;
            PctBonus      = pctBonus;
            DurationTicks = durationTicks;
            Id            = id;
        }

        /// <inheritdoc/>
        public bool Equals(BuffModifierEntry other)
            => FlatBonus == other.FlatBonus
            && PctBonus == other.PctBonus
            && DurationTicks == other.DurationTicks
            && Id == other.Id;

        /// <inheritdoc/>
        public override bool Equals(object obj)
            => obj is BuffModifierEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = FlatBonus.GetHashCode();
            h = (h * 397) ^ PctBonus.GetHashCode();
            h = (h * 397) ^ DurationTicks.GetHashCode();
            h = (h * 397) ^ Id.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both entries have identical bonuses, duration, and buff ID.</summary>
        public static bool operator ==(BuffModifierEntry left, BuffModifierEntry right)
            => left.Equals(right);

        /// <summary>Returns true if the entries differ in any field.</summary>
        public static bool operator !=(BuffModifierEntry left, BuffModifierEntry right)
            => !left.Equals(right);
    }
}
