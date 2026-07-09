using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for <see cref="CurrencySystem.AddGold"/> — Story 001 acceptance
    /// criteria. Covers the F-CS-1 cap-safe addition formula and its overflow-safety guarantee.
    /// </summary>
    [TestFixture]
    internal sealed class Currency_AddGold_Tests
    {
        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // GetBalance default — documented but not covered by a numbered AC.
        // -----------------------------------------------------------------------

        [Test]
        public void GetBalance_CharacterNeverTouched_ReturnsZero()
        {
            // Arrange — Player has never been passed to AddGold.

            // Act
            uint balance = _currency.GetBalance(Player);

            // Assert
            Assert.AreEqual(0u, balance, "GetBalance must return 0 for a character that has never received gold.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-A-01: AddGold(150) on balance=500 -> GetBalance returns 650.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_NormalAddition_BalanceIncreasesByAmount()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 500u, GoldTransactionReason.MonsterDrop);

            // Act
            _currency.AddGold(Player, 150u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(650u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-A-04: AddGold(200) on balance=9,999,900 -> Success, NewBalance=9,999,999,
        // Error=None, 101g discarded, cap detected via result.NewBalance == GOLD_CAP.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_ExceedsCap_ClampsToGoldCapAndDiscardsExcess()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 9_999_900u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.AddGold(Player, 200u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.IsTrue(result.Success, "A cap-clamped AddGold must still report Success=true.");
            Assert.AreEqual(9_999_999u, result.NewBalance);
            Assert.AreEqual(GoldMutationError.None, result.Error, "Cap-clamp must not produce an error code — callers detect it from NewBalance == GOLD_CAP.");
            Assert.AreEqual(9_999_999u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-A-05: AddGold(GOLD_CAP) on balance=1 -> GetBalance returns 9,999,999.
        // NewBalance never exceeds GOLD_CAP regardless of input magnitude.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_AmountEqualsGoldCap_NeverExceedsCap()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 1u, GoldTransactionReason.MonsterDrop);

            // Act
            _currency.AddGold(Player, 9_999_999u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(9_999_999u, _currency.GetBalance(Player));
        }

        [Test]
        public void AddGold_UIntMaxValueAmount_ClampsToGoldCapWithNoOverflow()
        {
            // Arrange — an even larger input than GOLD_CAP itself, to prove the clamp
            // guard holds regardless of how large `amount` is, not just at GOLD_CAP.
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 1u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.AddGold(Player, uint.MaxValue, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(9_999_999u, result.NewBalance, "Must clamp to GOLD_CAP exactly, never overflow-wrap to a small number.");
            Assert.AreEqual(9_999_999u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-C-01: AddGold(2) on balance=GOLD_CAP-1 (9,999,998) -> GetBalance returns
        // 9,999,999. Must NOT return 0 or any value < GOLD_CAP (no uint wrap).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_NearCapBoundary_DoesNotWrapAround()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 9_999_998u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.AddGold(Player, 2u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(9_999_999u, result.NewBalance);
            Assert.GreaterOrEqual(result.NewBalance, 9_999_999u, "Must not wrap to a value below GOLD_CAP.");
            Assert.AreEqual(9_999_999u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-C-02: AddGold(GOLD_CAP) on balance=0 -> GetBalance returns exactly
        // 9,999,999 (not overflow, not less).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_FromZeroBalance_ReachesGoldCapExactly()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);

            // Act
            var result = _currency.AddGold(Player, 9_999_999u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(9_999_999u, result.NewBalance);
            Assert.AreEqual(9_999_999u, _currency.GetBalance(Player));
        }
    }
}
