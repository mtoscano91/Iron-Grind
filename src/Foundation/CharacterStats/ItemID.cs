using System;

namespace IronGrind.CharacterStats
{
    /// <summary>
    /// IL2CPP-safe item identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations when keying on item identity inside modifier arrays.
    /// </summary>
    public readonly struct ItemID : IEquatable<ItemID>
    {
        /// <summary>Sentinel value representing no item / an unassigned equipment slot.</summary>
        public static readonly ItemID None = new ItemID(0u);

        /// <summary>
        /// Sentinel value used by the Item Database to represent an unregistered or reserved item ID.
        /// Both <see cref="None"/> and <see cref="Invalid"/> map to <c>uint(0)</c>. <see cref="None"/>
        /// is the CharacterStats vocabulary for "empty slot"; <see cref="Invalid"/> is the Item Database
        /// vocabulary for "no item / unregistered". They are aliases — <c>None == Invalid</c> is
        /// <c>true</c>. <see cref="IronGrind.ItemDatabase.ItemDatabase.GetItem"/> returns <c>null</c>
        /// silently (no error) when passed this value.
        /// </summary>
        public static readonly ItemID Invalid = new ItemID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="ItemID"/> with the specified raw value.</summary>
        public ItemID(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// The raw wire-format value. Intended for use by wire codecs only (e.g.
        /// <c>IronGrind.Networking.WireIdCodec</c>) — gameplay code should treat
        /// <see cref="ItemID"/> as opaque and compare via equality, not this value.
        /// </summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(ItemID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same item instance.</summary>
        public static bool operator ==(ItemID left, ItemID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different item instances.</summary>
        public static bool operator !=(ItemID left, ItemID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ItemID({_value})";
    }
}
