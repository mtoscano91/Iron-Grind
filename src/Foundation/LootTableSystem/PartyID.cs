using System;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// IL2CPP-safe party identifier. Wraps a <see cref="uint"/> to avoid string-based keys.
    /// </summary>
    /// <remarks>
    /// Owned by the Party System design (design/gdd/party-system.md CR-PS-1). No Party System code
    /// exists yet, so it is declared here as its first consumer (same precedent as <c>ItemID</c>).
    /// <c>0</c> is the uninitialized sentinel and must never appear in stable game state.
    /// </remarks>
    public readonly struct PartyID : IEquatable<PartyID>
    {
        /// <summary>Sentinel value representing no / an unassigned party. Never a valid party.</summary>
        public static readonly PartyID Uninitialized = new PartyID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="PartyID"/> with the specified raw value.</summary>
        public PartyID(uint value)
        {
            _value = value;
        }

        /// <summary>The raw value. Intended for codecs and diagnostics; gameplay code should compare via equality.</summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(PartyID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PartyID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same party.</summary>
        public static bool operator ==(PartyID left, PartyID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different parties.</summary>
        public static bool operator !=(PartyID left, PartyID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"PartyID({_value})";
    }
}
