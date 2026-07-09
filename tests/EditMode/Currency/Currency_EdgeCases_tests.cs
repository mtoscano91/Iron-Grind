using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for Story 006 edge cases — the <see cref="CurrencySystem.TransferGold"/>
    /// stub (AC-CS-E-02) and the compensating refund sequence (AC-CS-E-03, ADR-001 Decision 3).
    /// </summary>
    [TestFixture]
    internal sealed class Currency_EdgeCases_Tests
    {
        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // AC-CS-E-02: TransferGold(fromId, toId, 100) -> Error=NotImplemented.
        // No write to either balance. GetBalance for both characters unchanged.
        // -----------------------------------------------------------------------

        [Test]
        public void TransferGold_TwoRegisteredCharacters_ReturnsNotImplementedAndLeavesBothBalancesUnchanged()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.RegisterCharacter(OtherPlayer, 0u);
            _currency.AddGold(Player, 500u, GoldTransactionReason.MonsterDrop);
            _currency.AddGold(OtherPlayer, 300u, GoldTransactionReason.MonsterDrop);

            // Act
            var result = _currency.TransferGold(Player, OtherPlayer, 100u);

            // Assert
            Assert.AreEqual(GoldMutationError.NotImplemented, result.Error);
            Assert.AreEqual(500u, _currency.GetBalance(Player), "TransferGold must not touch the sender's balance — it is a stub.");
            Assert.AreEqual(300u, _currency.GetBalance(OtherPlayer), "TransferGold must not touch the recipient's balance — it is a stub.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-E-03: TrySpendGold(400, ScrollPurchase) succeeds (balance=800), then
        // AddGold(400, CompensatingRefund) simulates a compensating refund after a
        // downstream failure. Assert Error=None and Success=true on the refund call
        // itself, and GetBalance returns 1,200 (fully restored, no overflow/clamp artifact).
        // -----------------------------------------------------------------------

        [Test]
        public void CompensatingRefund_AfterSuccessfulSpend_RestoresOriginalBalanceExactly()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 0u);
            _currency.AddGold(Player, 1_200u, GoldTransactionReason.MonsterDrop);

            var spendResult = _currency.TrySpendGold(Player, 400u, GoldTransactionReason.ScrollPurchase);
            Assert.IsTrue(spendResult.Success, "Precondition: the initial spend must succeed before simulating a downstream failure.");
            Assert.AreEqual(800u, _currency.GetBalance(Player), "Precondition: balance must be 800 after the spend, before the compensating refund.");

            // Act — simulate a downstream failure (e.g. item grant) after the successful debit,
            // triggering a compensating refund per ADR-001 Decision 3.
            var refundResult = _currency.AddGold(Player, 400u, GoldTransactionReason.CompensatingRefund);

            // Assert
            Assert.AreEqual(GoldMutationError.None, refundResult.Error, "The compensating refund call itself must report no error.");
            Assert.IsTrue(refundResult.Success, "The compensating refund call itself must report Success=true.");
            Assert.AreEqual(1_200u, _currency.GetBalance(Player), "Balance must be fully restored to its pre-spend value with no overflow or clamp artifact.");
        }
    }
}
