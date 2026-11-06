using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Authentication
{
    /// <summary>Authentication Story 001: AccountID value semantics and IL2CPP-safe comparer (TR-auth-002, CR-AUTH-2).</summary>
    [TestFixture]
    internal sealed class Authentication_AccountID_Tests
    {
        private const uint RESERVED_VALUE = 0u;
        private const uint FIRST_VALUE = 1u;
        private const uint OTHER_VALUE = 2u;
        private const uint KEY_VALUE = 7u;
        private const int STORED_VALUE = 42;

        [Test]
        public void test_zero_value_equals_invalid_via_comparer_and_operator()
        {
            var zero = new AccountID(RESERVED_VALUE);

            Assert.IsTrue(AccountID.AccountIDComparer.Instance.Equals(zero, AccountID.Invalid));
            Assert.IsTrue(zero == AccountID.Invalid);
        }

        [Test]
        public void test_nonzero_value_not_equal_to_invalid()
        {
            var id = new AccountID(FIRST_VALUE);

            Assert.IsFalse(AccountID.AccountIDComparer.Instance.Equals(id, AccountID.Invalid));
            Assert.IsTrue(id != AccountID.Invalid);
        }

        [Test]
        public void test_same_value_equal_with_equal_hash_codes()
        {
            var a = new AccountID(KEY_VALUE);
            var b = new AccountID(KEY_VALUE);
            var comparer = AccountID.AccountIDComparer.Instance;

            Assert.IsTrue(comparer.Equals(a, b));
            Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b));
        }

        [Test]
        public void test_different_values_not_equal()
        {
            var a = new AccountID(FIRST_VALUE);
            var b = new AccountID(OTHER_VALUE);

            Assert.IsFalse(AccountID.AccountIDComparer.Instance.Equals(a, b));
        }

        [Test]
        public void test_max_uint_round_trips_through_raw_value()
        {
            var id = new AccountID(uint.MaxValue);

            Assert.AreEqual(uint.MaxValue, id.RawValue);
        }

        [Test]
        public void test_dictionary_with_comparer_finds_key_via_fresh_instance()
        {
            var map = new Dictionary<AccountID, int>(AccountID.AccountIDComparer.Instance)
            {
                [new AccountID(KEY_VALUE)] = STORED_VALUE
            };

            Assert.IsTrue(map.TryGetValue(new AccountID(KEY_VALUE), out int found));
            Assert.AreEqual(STORED_VALUE, found);
        }

        [Test]
        public void test_default_value_equals_invalid_via_comparer()
        {
            var unassigned = default(AccountID);

            Assert.IsTrue(AccountID.AccountIDComparer.Instance.Equals(unassigned, AccountID.Invalid));
        }

        [TestCase(RESERVED_VALUE)]
        [TestCase(KEY_VALUE)]
        [TestCase(uint.MaxValue)]
        public void test_comparer_hash_matches_struct_hash(uint raw)
        {
            var id = new AccountID(raw);

            int comparerHash = AccountID.AccountIDComparer.Instance.GetHashCode(id);

            Assert.AreEqual(id.GetHashCode(), comparerHash);
        }

        [Test]
        public void test_comparer_instance_is_singleton()
        {
            Assert.AreSame(AccountID.AccountIDComparer.Instance, AccountID.AccountIDComparer.Instance);
        }
    }
}
