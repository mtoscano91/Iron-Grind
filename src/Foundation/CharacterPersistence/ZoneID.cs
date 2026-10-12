using System;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// IL2CPP-safe zone identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations when keying on zone identity.
    /// </summary>
    /// <remarks>
    /// Used for a character's last zone (Character Persistence CR-CP-2, CR-CP-9) and zone
    /// transition messages. Shaped like <c>IronGrind.Currency.CharacterID</c>.
    /// </remarks>
    public readonly struct ZoneID : IEquatable<ZoneID>
    {
        /// <summary>
        /// Sentinel value representing no zone. As a character's last zone it is the first-login
        /// sentinel of CR-CP-9 ("no last zone"). Must not be assigned to any real zone.
        /// </summary>
        public static readonly ZoneID Invalid = new ZoneID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="ZoneID"/> with the specified raw value.</summary>
        public ZoneID(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// The raw wire-format value. Intended for wire codecs and the persistence layer only;
        /// gameplay code should treat <see cref="ZoneID"/> as opaque and compare via equality.
        /// </summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(ZoneID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same zone.</summary>
        public static bool operator ==(ZoneID left, ZoneID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different zones.</summary>
        public static bool operator !=(ZoneID left, ZoneID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneID({_value})";
    }
}
