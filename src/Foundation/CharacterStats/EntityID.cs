using System;

namespace IronGrind.CharacterStats
{
    /// <summary>
    /// IL2CPP-safe entity identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations on hot paths under iOS IL2CPP.
    /// </summary>
    /// <remarks>
    /// Never use <see langword="string"/> for entity identity. String dictionary keys
    /// allocate on every lookup under IL2CPP AOT. This struct's <see cref="GetHashCode"/>
    /// delegates to <see cref="uint.GetHashCode"/>, which is allocation-free.
    /// </remarks>
    public readonly struct EntityID : IEquatable<EntityID>
    {
        /// <summary>Sentinel value representing an unassigned or invalid entity.</summary>
        public static readonly EntityID Invalid = new EntityID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="EntityID"/> with the specified raw value.</summary>
        public EntityID(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// The raw wire-format value. Intended for use by wire codecs only (e.g.
        /// <c>IronGrind.Networking.WireIdCodec</c>) — gameplay code should treat
        /// <see cref="EntityID"/> as opaque and compare via equality, not this value.
        /// </summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(EntityID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EntityID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same entity.</summary>
        public static bool operator ==(EntityID left, EntityID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different entities.</summary>
        public static bool operator !=(EntityID left, EntityID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"EntityID({_value})";
    }
}
