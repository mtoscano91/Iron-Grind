using System;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// IL2CPP-safe ground item identifier. Wraps a <see cref="uint"/>. Allocated from a per-service
    /// sequential counter starting at 1, scoped to the zone session (design/registry/entities.yaml).
    /// </summary>
    /// <remarks>
    /// <c>0</c> is <see cref="Invalid"/> and is never handed out for a real ground item.
    /// </remarks>
    public readonly struct GroundItemID : IEquatable<GroundItemID>
    {
        /// <summary>Sentinel value representing no ground item. Never a valid ground item.</summary>
        public static readonly GroundItemID Invalid = new GroundItemID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="GroundItemID"/> with the specified raw value.</summary>
        public GroundItemID(uint value)
        {
            _value = value;
        }

        /// <summary>The raw value. Intended for codecs and diagnostics; gameplay code should compare via equality.</summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(GroundItemID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GroundItemID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same ground item.</summary>
        public static bool operator ==(GroundItemID left, GroundItemID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different ground items.</summary>
        public static bool operator !=(GroundItemID left, GroundItemID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"GroundItemID({_value})";
    }
}
