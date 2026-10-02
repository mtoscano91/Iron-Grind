using System;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// IL2CPP-safe mob type identifier. Wraps a <see cref="uint"/> to avoid string-based keys.
    /// </summary>
    /// <remarks>
    /// Owned by the Enemy AI design (design/gdd/enemy-ai.md). No Enemy AI code exists yet, so it
    /// is declared here as its first consumer (same precedent as <c>ItemID</c>). Move it to the
    /// Enemy AI module when that exists.
    /// </remarks>
    public readonly struct MobTypeID : IEquatable<MobTypeID>
    {
        /// <summary>Sentinel value representing no / an unassigned mob type. Never a valid table key.</summary>
        public static readonly MobTypeID Invalid = new MobTypeID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="MobTypeID"/> with the specified raw value.</summary>
        public MobTypeID(uint value)
        {
            _value = value;
        }

        /// <summary>The raw value. Intended for codecs and diagnostics; gameplay code should compare via equality.</summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(MobTypeID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MobTypeID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same mob type.</summary>
        public static bool operator ==(MobTypeID left, MobTypeID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different mob types.</summary>
        public static bool operator !=(MobTypeID left, MobTypeID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"MobTypeID({_value})";
    }
}
