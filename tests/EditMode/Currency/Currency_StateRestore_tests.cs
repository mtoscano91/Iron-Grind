using System.Collections.Generic;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// <see cref="ICurrencyStateRestore.RestoreCharacter"/> on <see cref="CurrencySystem"/> (Character Persistence
    /// Story 004, CR-CP-3 step 6): a state restore of balance and version that registers an unknown character,
    /// overwrites a known one, raises no <see cref="ICurrencyService.OnGoldSync"/>, and continues the version epoch.
    /// </summary>
    [TestFixture]
    internal sealed class Currency_StateRestore_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private const uint RESTORED_BALANCE = 1250u;
        private const uint RESTORED_VERSION = 9u;

        private CurrencySystem _currency;
        private List<GoldSyncEventArgs> _syncs;

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
            _syncs = new List<GoldSyncEventArgs>();
            _currency.OnGoldSync += _syncs.Add;
        }

        [Test]
        public void RestoreCharacter_UnregisteredCharacter_BecomesRegisteredWithBalanceAndVersion()
        {
            _currency.RestoreCharacter(Player, RESTORED_BALANCE, RESTORED_VERSION);

            Assert.AreEqual(RESTORED_BALANCE, _currency.GetBalance(Player));
            GoldMutationResult add = _currency.AddGold(Player, 10u, GoldTransactionReason.MonsterDrop);
            Assert.IsTrue(add.Success, "a restored character must accept mutations (not CharacterNotFound)");
            Assert.AreEqual(1, _syncs.Count);
            Assert.AreEqual(RESTORED_VERSION + 1u, _syncs[0].Version);
        }

        [Test]
        public void RestoreCharacter_RegisteredCharacter_OverwritesBalanceAndVersion()
        {
            _currency.RegisterCharacter(Player, 500u);
            _currency.AddGold(Player, 5u, GoldTransactionReason.MonsterDrop);
            _syncs.Clear();

            _currency.RestoreCharacter(Player, RESTORED_BALANCE, RESTORED_VERSION);

            Assert.AreEqual(RESTORED_BALANCE, _currency.GetBalance(Player));
            Assert.IsTrue(_currency.TrySpendGold(Player, 250u, GoldTransactionReason.MonsterDrop).Success);
            Assert.AreEqual(RESTORED_VERSION + 1u, _syncs[0].Version);
        }

        [Test]
        public void RestoreCharacter_RaisesNoGoldSync()
        {
            _currency.RestoreCharacter(Player, RESTORED_BALANCE, RESTORED_VERSION);

            Assert.AreEqual(0, _syncs.Count);
        }

        [Test]
        public void RestoreCharacter_SpendCarryingPreRestoreVersion_ReturnsConcurrencyConflict()
        {
            _currency.RestoreCharacter(Player, RESTORED_BALANCE, RESTORED_VERSION);

            GoldMutationResult stale = _currency.TryCompareAndSwapSpend(Player, 250u, GoldTransactionReason.MonsterDrop, 0u);

            Assert.AreEqual(GoldMutationError.ConcurrencyConflict, stale.Error);
            Assert.AreEqual(RESTORED_BALANCE, _currency.GetBalance(Player));

            GoldMutationResult current = _currency.TryCompareAndSwapSpend(Player, 250u, GoldTransactionReason.MonsterDrop, RESTORED_VERSION);

            Assert.IsTrue(current.Success);
            Assert.AreEqual(RESTORED_BALANCE - 250u, _currency.GetBalance(Player));
        }
    }
}
