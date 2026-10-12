using System;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// IL2CPP-safe account identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations when keying on account identity.
    /// </summary>
    /// <remarks>
    /// Identifies the owning account of a persistent character (Character Persistence CR-CP-1).
    /// Shaped like <c>IronGrind.Currency.CharacterID</c>.
    /// </remarks>
    public readonly struct AccountID : IEquatable<AccountID>
    {
        /// <summary>Sentinel value representing no account / an unassigned reference. Reserved - must not be assigned to any account.</summary>
        public static readonly AccountID Invalid = new AccountID(0u);

        private readonly uint _value;

        /// <summary>Initializes a new <see cref="AccountID"/> with the specified raw value.</summary>
        public AccountID(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// The raw wire-format value. Intended for wire codecs and the persistence layer only;
        /// gameplay code should treat <see cref="AccountID"/> as opaque and compare via equality.
        /// </summary>
        public uint RawValue => _value;

        /// <inheritdoc/>
        public bool Equals(AccountID other) => _value == other._value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AccountID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Returns true if both identifiers refer to the same account.</summary>
        public static bool operator ==(AccountID left, AccountID right) => left.Equals(right);

        /// <summary>Returns true if the identifiers refer to different accounts.</summary>
        public static bool operator !=(AccountID left, AccountID right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"AccountID({_value})";
    }
}
