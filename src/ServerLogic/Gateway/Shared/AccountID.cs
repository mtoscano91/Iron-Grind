using System;
using System.Collections.Generic;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// IL2CPP-safe account identifier. Wraps a <see cref="uint"/> to avoid string-based
    /// GC allocations when keying on account identity.
    /// </summary>
    /// <remarks>
    /// Identifies the owning account of a persistent character (Character Persistence CR-CP-1).
    /// Shaped like <c>IronGrind.Currency.CharacterID</c>.
    /// Lives in the server-only shared folder (<c>src/ServerLogic/Gateway/Shared/</c>, ADR-016 Decision 6):
    /// it is compiled by Unity's server assembly and by the gateway, and must never be sent to a client
    /// (Authentication CR-AUTH-2). Use <see cref="AccountIDComparer"/> for Dictionary keys (IL2CPP-safe).
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

        /// <summary>
        /// Allocation-free equality comparer for <see cref="AccountID"/> Dictionary/HashSet keys
        /// (CR-AUTH-2: IL2CPP-safe keys; avoids the default comparer path).
        /// </summary>
        /// <example><c>var map = new Dictionary&lt;AccountID, int&gt;(AccountID.AccountIDComparer.Instance);</c></example>
        public sealed class AccountIDComparer : IEqualityComparer<AccountID>
        {
            /// <summary>Shared stateless instance.</summary>
            public static readonly AccountIDComparer Instance = new AccountIDComparer();

            private AccountIDComparer() { }

            /// <summary>Returns true if both identifiers have the same <see cref="RawValue"/>.</summary>
            public bool Equals(AccountID x, AccountID y) => x.RawValue == y.RawValue;

            /// <summary>Returns the hash code of the raw value.</summary>
            public int GetHashCode(AccountID obj) => obj.RawValue.GetHashCode();
        }
    }
}
