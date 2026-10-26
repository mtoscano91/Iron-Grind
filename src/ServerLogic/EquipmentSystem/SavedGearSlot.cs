#nullable enable

using System;
using IronGrind.CharacterStats;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// One persisted gear slot: the <c>{item_id, enhancement_level}</c> pair of ADR-006's
    /// <c>gear_slots</c> JSONB (Equipment System Story 007, CR-EQS-15). Equipment-owned so the
    /// dependency stays Persistence → Equipment; Character Persistence converts from its own record.
    /// </summary>
    /// <remarks>An empty slot is <see cref="Empty"/>: <see cref="ItemID.Invalid"/> with level 0.
    /// <c>IsTransitioning</c> is never part of it (CR-EQS-15).</remarks>
    public readonly struct SavedGearSlot : IEquatable<SavedGearSlot>
    {
        /// <summary>The empty saved slot: <see cref="ItemID.Invalid"/>, level 0.</summary>
        public static readonly SavedGearSlot Empty = new SavedGearSlot(ItemID.Invalid, 0);

        /// <summary>Creates a saved slot.</summary>
        /// <param name="itemId">The item, or <see cref="ItemID.Invalid"/> for an empty slot.</param>
        /// <param name="enhancementLevel">The item's enhancement level; 0 for an empty slot.</param>
        public SavedGearSlot(ItemID itemId, byte enhancementLevel)
        {
            ItemId = itemId;
            EnhancementLevel = enhancementLevel;
        }

        /// <summary>The saved item, or <see cref="ItemID.Invalid"/> when the slot is empty.</summary>
        public ItemID ItemId { get; }

        /// <summary>The saved enhancement level.</summary>
        public byte EnhancementLevel { get; }

        /// <summary>Whether both fields are equal.</summary>
        /// <param name="other">The slot to compare with.</param>
        public bool Equals(SavedGearSlot other) => ItemId == other.ItemId && EnhancementLevel == other.EnhancementLevel;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SavedGearSlot other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ItemId.GetHashCode() * 31 + EnhancementLevel;

        /// <summary>Value equality.</summary>
        public static bool operator ==(SavedGearSlot left, SavedGearSlot right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(SavedGearSlot left, SavedGearSlot right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"SavedGearSlot({ItemId}, +{EnhancementLevel})";
    }
}
