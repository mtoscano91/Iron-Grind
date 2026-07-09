using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for <see cref="CurrencySystem.TrySpendGold"/> — Story 002 acceptance
    /// criteria. Covers the F-CS-2 spend guard formula and its atomicity guarantee (no partial
    /// spend, no write on failure).
    /// </summary>
    [TestFixture]
    internal sealed class Currency_TrySpendGold_Tests
    {
        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // AC-CS-A-02: TrySpendGold(400) on balance=1,200 -> Success, NewBalance=800.
        // GetBalance returns 800.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_SufficientBalance_DebitsFullCostAndReturnsSuccess()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 1_200u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.TrySpendGold(Player, 400u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(800u, result.NewBalance);
            Assert.AreEqual(GoldMutationError.None, result.Error);
            Assert.AreEqual(800u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-A-03: TrySpendGold(400) on balance=300 -> InsufficientFunds.
        // GetBalance still returns 300 (unchanged) — proves no partial spend / no write.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_InsufficientBalance_ReturnsInsufficientFundsAndLeavesBalanceUnchanged()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 300u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.TrySpendGold(Player, 400u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.InsufficientFunds, result.Error);
            Assert.AreEqual(300u, result.NewBalance, "NewBalance must equal the unchanged balance on failure, not the would-be debited value.");
            Assert.AreEqual(300u, _currency.GetBalance(Player), "GetBalance must prove the balance was NOT mutated on a failed spend.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-E-01: TrySpendGold(1) on balance=0 -> InsufficientFunds. No write.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_ZeroBalanceSpendOne_ReturnsInsufficientFundsWithNoWrite()
        {
            // Arrange — Player is registered with balance 0, but never passed to AddGold.
            _currency.RegisterCharacter(Player, 0u);

            // Act
            var result = _currency.TrySpendGold(Player, 1u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.InsufficientFunds, result.Error);
            Assert.AreEqual(0u, result.NewBalance);
            Assert.AreEqual(0u, _currency.GetBalance(Player), "GetBalance must prove no dictionary entry was written for a never-touched character.");
        }

        // -----------------------------------------------------------------------
        // Boundary case (implied by F-CS-2's >= guard): cost == balance exactly ->
        // must succeed, NewBalance == 0.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_CostEqualsBalanceExactly_SucceedsWithZeroNewBalance()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 400u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.TrySpendGold(Player, 400u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsTrue(result.Success, "cost == balance is the boundary of the >= guard and must succeed.");
            Assert.AreEqual(0u, result.NewBalance);
            Assert.AreEqual(GoldMutationError.None, result.Error);
            Assert.AreEqual(0u, _currency.GetBalance(Player));
        }
    }
}
