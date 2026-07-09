using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for Story 003 — the <see cref="GoldMutationError.CharacterNotFound"/>
    /// and <see cref="GoldMutationError.InvalidAmount"/> guard clauses added to
    /// <see cref="CurrencySystem.AddGold"/> and <see cref="CurrencySystem.TrySpendGold"/>, the
    /// <see cref="CurrencySystem.RegisterCharacter"/> registration seam, and the derived
    /// Empty/Normal/AtCap balance state machine. That state machine is NOT a stored field — it
    /// is a pure classification of <see cref="CurrencySystem.GetBalance"/>'s return value, so
    /// these tests are plain before/after assertions on <c>GetBalance()</c>.
    /// </summary>
    [TestFixture]
    internal sealed class Currency_GuardsAndStateMachine_Tests
    {
        private const uint GOLD_CAP = 9_999_999u;

        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // AC-CS-B-01: AddGold(charId, 0) on a registered character with balance=500
        // -> InvalidAmount. No write. GetBalance == 500 (unchanged).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_ZeroAmountOnRegisteredCharacter_ReturnsInvalidAmountAndLeavesBalanceUnchanged()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);

            // Act
            var result = _currency.AddGold(Player, 0u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.InvalidAmount, result.Error);
            Assert.AreEqual(500u, result.NewBalance);
            Assert.AreEqual(500u, result.PreviousBalance);
            Assert.AreEqual(500u, _currency.GetBalance(Player), "Balance must be unchanged by a rejected zero-amount AddGold.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-B-02: TrySpendGold(charId, 0) on a registered character with balance=500
        // -> InvalidAmount. No write. GetBalance == 500 (unchanged).
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_ZeroCostOnRegisteredCharacter_ReturnsInvalidAmountAndLeavesBalanceUnchanged()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);

            // Act
            var result = _currency.TrySpendGold(Player, 0u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.InvalidAmount, result.Error);
            Assert.AreEqual(500u, result.NewBalance);
            Assert.AreEqual(500u, result.PreviousBalance);
            Assert.AreEqual(500u, _currency.GetBalance(Player), "Balance must be unchanged by a rejected zero-cost TrySpendGold.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-B-03: AddGold/TrySpendGold with a never-registered CharacterID ->
        // CharacterNotFound. No write. (Both methods tested separately.)
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_UnregisteredCharacter_ReturnsCharacterNotFoundAndDoesNotWrite()
        {
            // Arrange — Player has never been passed to RegisterCharacter.

            // Act
            var result = _currency.AddGold(Player, 100u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error);
            Assert.AreEqual(0u, result.NewBalance);
            Assert.AreEqual(0u, result.PreviousBalance);
            Assert.AreEqual(0u, _currency.GetBalance(Player), "An unregistered character must remain unwritten after a rejected AddGold.");
        }

        [Test]
        public void TrySpendGold_UnregisteredCharacter_ReturnsCharacterNotFoundAndDoesNotWrite()
        {
            // Arrange — Player has never been passed to RegisterCharacter.

            // Act
            var result = _currency.TrySpendGold(Player, 100u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error);
            Assert.AreEqual(0u, result.NewBalance);
            Assert.AreEqual(0u, result.PreviousBalance);
            Assert.AreEqual(0u, _currency.GetBalance(Player), "An unregistered character must remain unwritten after a rejected TrySpendGold.");
        }

        // -----------------------------------------------------------------------
        // Guard order proof: when BOTH guard conditions apply simultaneously
        // (unregistered character AND zero amount/cost), CharacterNotFound must
        // win — it is checked first per the story's Implementation Notes. Without
        // this test, silently swapping the guard order would not be caught by any
        // other test in this file.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_UnregisteredCharacterWithZeroAmount_ReturnsCharacterNotFoundNotInvalidAmount()
        {
            // Arrange — Player is both unregistered AND amount is zero; if the guards
            // were swapped, this would return InvalidAmount instead.

            // Act
            var result = _currency.AddGold(Player, 0u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error, "CharacterNotFound must be checked before InvalidAmount.");
        }

        [Test]
        public void TrySpendGold_UnregisteredCharacterWithZeroCost_ReturnsCharacterNotFoundNotInvalidAmount()
        {
            // Arrange — Player is both unregistered AND cost is zero; if the guards
            // were swapped, this would return InvalidAmount instead.

            // Act
            var result = _currency.TrySpendGold(Player, 0u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error, "CharacterNotFound must be checked before InvalidAmount.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-01: balance=0 (Empty), AddGold(1) -> balance becomes 1 (Normal:
        // 0 < 1 < GOLD_CAP).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_FromEmptyStateAddOne_TransitionsToNormalState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            Assert.AreEqual(0u, _currency.GetBalance(Player), "Precondition: balance must be Empty (0).");

            // Act
            _currency.AddGold(Player, 1u, GoldTransactionReason.MonsterDrop);

            // Assert
            uint balance = _currency.GetBalance(Player);
            Assert.AreEqual(1u, balance);
            Assert.IsTrue(balance > 0u && balance < GOLD_CAP, "Balance must be in the Normal range (0 < balance < GOLD_CAP).");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-02: balance=GOLD_CAP-1 (Normal), AddGold(1) -> balance becomes
        // GOLD_CAP (AtCap).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_FromNormalStateNearCap_TransitionsToAtCapState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, GOLD_CAP - 1u);

            // Act
            _currency.AddGold(Player, 1u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(GOLD_CAP, _currency.GetBalance(Player), "Balance must reach exactly GOLD_CAP (AtCap).");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-03: balance=GOLD_CAP (AtCap), TrySpendGold(1) -> balance becomes
        // GOLD_CAP-1 (Normal).
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_FromAtCapStateSpendOne_TransitionsToNormalState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, GOLD_CAP);

            // Act
            _currency.TrySpendGold(Player, 1u, GoldTransactionReason.ScrollPurchase);

            // Assert
            uint balance = _currency.GetBalance(Player);
            Assert.AreEqual(GOLD_CAP - 1u, balance);
            Assert.IsTrue(balance > 0u && balance < GOLD_CAP, "Balance must be in the Normal range (0 < balance < GOLD_CAP).");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-04: balance=1 (Normal), TrySpendGold(1) -> balance becomes 0 (Empty).
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_FromNormalStateSpendAll_TransitionsToEmptyState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1u);

            // Act
            _currency.TrySpendGold(Player, 1u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(0u, _currency.GetBalance(Player), "Balance must reach exactly 0 (Empty).");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-05: balance=GOLD_CAP (AtCap), AddGold(500) -> balance remains
        // exactly GOLD_CAP (no change; cap-clamp per F-CS-1, already implemented in
        // Story 001).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_FromAtCapState_RemainsAtCapAfterFurtherAddition()
        {
            // Arrange
            _currency.RegisterCharacter(Player, GOLD_CAP);

            // Act
            var result = _currency.AddGold(Player, 500u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.IsTrue(result.Success, "A cap-clamped AddGold must still report Success=true.");
            Assert.AreEqual(GOLD_CAP, result.NewBalance);
            Assert.AreEqual(GOLD_CAP, _currency.GetBalance(Player), "Balance must remain exactly GOLD_CAP — no change beyond the cap.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-06: balance=0 (Empty), AddGold(GOLD_CAP) -> Success=true,
        // NewBalance=GOLD_CAP; GetBalance == GOLD_CAP (Empty -> AtCap directly).
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_FromEmptyStateAddFullCap_TransitionsDirectlyToAtCapState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);

            // Act
            var result = _currency.AddGold(Player, GOLD_CAP, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(GOLD_CAP, result.NewBalance);
            Assert.AreEqual(GOLD_CAP, _currency.GetBalance(Player), "Balance must transition directly from Empty to AtCap.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-H-07: balance=GOLD_CAP (AtCap), TrySpendGold(GOLD_CAP) ->
        // Success=true, NewBalance=0; GetBalance == 0 (AtCap -> Empty directly).
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_FromAtCapStateSpendFullCap_TransitionsDirectlyToEmptyState()
        {
            // Arrange
            _currency.RegisterCharacter(Player, GOLD_CAP);

            // Act
            var result = _currency.TrySpendGold(Player, GOLD_CAP, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0u, result.NewBalance);
            Assert.AreEqual(0u, _currency.GetBalance(Player), "Balance must transition directly from AtCap to Empty.");
        }
    }
}
