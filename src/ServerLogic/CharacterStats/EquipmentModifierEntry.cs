using System;

namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Represents a single equipment piece's contribution to the modifier stack.
    /// <see langword="readonly struct"/> — zero heap allocation on the modifier-stack hot path.
    /// Stored in a fixed-capacity array (capacity 16 per entity).
    /// </summary>
    /// <remarks>
    /// Modifier stack application (AddEquipmentModifier / RemoveEquipmentModifier)
    /// is implemented in Story 003. <see cref="IEquatable{T}"/> is implemented here so that
    /// Story 003 can safely use this type as a Dictionary or HashSet key without IL2CPP boxing.
    /// </remarks>
    public readonly struct EquipmentModifierEntry : IEquatable<EquipmentModifierEntry>
    {
        /// <summary>Flat stat bonus. Applied before percentage bonuses in the modifier stack.</summary>
        public readonly float FlatBonus;

        /// <summary>
        /// Percentage bonus (e.g., 0.10f = +10%). Within the equipment modifier layer,
        /// percentage bonuses are additive — two +10% entries sum to +20%, not +21%.
        /// Applied after all flat bonuses.
        /// </summary>
        public readonly float PctBonus;

        /// <summary>The item that owns this modifier entry.</summary>
        public readonly ItemID Id;

        /// <summary>Initializes a new <see cref="EquipmentModifierEntry"/>.</summary>
        public EquipmentModifierEntry(float flatBonus, float pctBonus, ItemID id)
        {
            FlatBonus = flatBonus;
            PctBonus  = pctBonus;
            Id        = id;
        }

        /// <inheritdoc/>
        public bool Equals(EquipmentModifierEntry other)
            => FlatBonus == other.FlatBonus && PctBonus == other.PctBonus && Id == other.Id;

        /// <inheritdoc/>
        public override bool Equals(object obj)
            => obj is EquipmentModifierEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = FlatBonus.GetHashCode();
            h = (h * 397) ^ PctBonus.GetHashCode();
            h = (h * 397) ^ Id.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both entries have identical bonuses and item ID.</summary>
        public static bool operator ==(EquipmentModifierEntry left, EquipmentModifierEntry right)
            => left.Equals(right);

        /// <summary>Returns true if the entries differ in any field.</summary>
        public static bool operator !=(EquipmentModifierEntry left, EquipmentModifierEntry right)
            => !left.Equals(right);
    }
}
