using System;

namespace IronGrind.Currency
{
    /// <summary>
    /// IL2CPP-safe character identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations when keying on character identity in the balance store.
    /// </summary>
    /// <remarks>
    /// Identifies a persistent player account record (used by Currency System, Character
    /// Persistence). Distinct from <c>EntityID</c> (Character Stats GDD), which identifies
    /// any runtime combat entity — players, enemies, and NPCs. Player characters have both;
    /// enemies have only an <c>EntityID</c>. Do not conflate the two — they serve different
    /// identity scopes (GDD Rule 12).
    /// </remarks>
    public readonly struct CharacterID : IEquatable<CharacterID>
    {
        /// <summary>Sentinel value representing no character / an unassigned reference. Reserved — must not be assigned to any character.</summary>
        public static readonly CharacterID Invalid = new CharacterID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="CharacterID"/> with the specified raw value.</summary>
        public CharacterID(uint value)
        {
            _value = value;
        }

        /// <inheritdoc/>
        public bool Equals(CharacterID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CharacterID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same character.</summary>
        public static bool operator ==(CharacterID left, CharacterID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different characters.</summary>
        public static bool operator !=(CharacterID left, CharacterID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"CharacterID({_value})";
    }
}
