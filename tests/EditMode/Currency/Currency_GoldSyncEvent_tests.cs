using System.Collections.Generic;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for Story 005 — the <see cref="CurrencySystem.OnGoldSync"/> Tier 2
    /// broadcast event (ADR-010 Decision 3) fired after every successful
    /// <see cref="CurrencySystem.AddGold"/>/<see cref="CurrencySystem.TrySpendGold"/> mutation.
    /// </summary>
    [TestFixture]
    internal sealed class Currency_GoldSyncEvent_Tests
    {
        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-01: registered character at balance=500, version=0; AddGold(150,
        // MonsterDrop) -> OnGoldSync fires exactly once with CharacterID matching,
        // NewBalance=650, Version=1, Reason=MonsterDrop.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_SuccessfulMutation_FiresOnGoldSyncWithCorrectFields()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            _currency.AddGold(Player, 150u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(1, captured.Count, "OnGoldSync must fire exactly once for a single successful AddGold.");
            Assert.AreEqual(Player, captured[0].CharacterID);
            Assert.AreEqual(650u, captured[0].NewBalance);
            Assert.AreEqual(1u, captured[0].Version, "A freshly registered character starts at version 0; the first successful mutation must produce version 1.");
            Assert.AreEqual(GoldTransactionReason.MonsterDrop, captured[0].Reason);

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-01 (TrySpendGold variant): registered character at balance=500,
        // version=0; TrySpendGold(150, ScrollPurchase) -> OnGoldSync fires exactly
        // once with NewBalance=350, Version=1, Reason=ScrollPurchase.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_SuccessfulMutation_FiresOnGoldSyncWithCorrectFields()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            _currency.TrySpendGold(Player, 150u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(1, captured.Count, "OnGoldSync must fire exactly once for a single successful TrySpendGold.");
            Assert.AreEqual(Player, captured[0].CharacterID);
            Assert.AreEqual(350u, captured[0].NewBalance);
            Assert.AreEqual(1u, captured[0].Version, "A freshly registered character starts at version 0; the first successful mutation must produce version 1.");
            Assert.AreEqual(GoldTransactionReason.ScrollPurchase, captured[0].Reason);

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-02: AddGold(charId, 0, ...) on a registered character ->
        // InvalidAmount -> OnGoldSync does NOT fire.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_ZeroAmount_DoesNotFireOnGoldSync()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            var result = _currency.AddGold(Player, 0u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(GoldMutationError.InvalidAmount, result.Error, "Precondition: this call must be rejected as InvalidAmount.");
            Assert.AreEqual(0, captured.Count, "OnGoldSync must NOT fire when AddGold returns InvalidAmount.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-02: AddGold on an unregistered charId -> CharacterNotFound ->
        // OnGoldSync does NOT fire.
        // -----------------------------------------------------------------------

        [Test]
        public void AddGold_UnregisteredCharacter_DoesNotFireOnGoldSync()
        {
            // Arrange — Player has never been passed to RegisterCharacter.
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            var result = _currency.AddGold(Player, 100u, GoldTransactionReason.MonsterDrop);

            // Assert
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error, "Precondition: this call must be rejected as CharacterNotFound.");
            Assert.AreEqual(0, captured.Count, "OnGoldSync must NOT fire when AddGold returns CharacterNotFound.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-03: TrySpendGold with insufficient balance -> InsufficientFunds
        // -> OnGoldSync does NOT fire.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_InsufficientBalance_DoesNotFireOnGoldSync()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 50u);
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            var result = _currency.TrySpendGold(Player, 100u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(GoldMutationError.InsufficientFunds, result.Error, "Precondition: this call must be rejected as InsufficientFunds.");
            Assert.AreEqual(0, captured.Count, "OnGoldSync must NOT fire when TrySpendGold returns InsufficientFunds.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-03: TrySpendGold(charId, 0, ...) -> InvalidAmount -> OnGoldSync
        // does NOT fire.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_ZeroCost_DoesNotFireOnGoldSync()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 500u);
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            var result = _currency.TrySpendGold(Player, 0u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(GoldMutationError.InvalidAmount, result.Error, "Precondition: this call must be rejected as InvalidAmount.");
            Assert.AreEqual(0, captured.Count, "OnGoldSync must NOT fire when TrySpendGold returns InvalidAmount.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-03: TrySpendGold on an unregistered charId -> CharacterNotFound
        // -> OnGoldSync does NOT fire.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_UnregisteredCharacter_DoesNotFireOnGoldSync()
        {
            // Arrange — Player has never been passed to RegisterCharacter.
            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act
            var result = _currency.TrySpendGold(Player, 100u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.AreEqual(GoldMutationError.CharacterNotFound, result.Error, "Precondition: this call must be rejected as CharacterNotFound.");
            Assert.AreEqual(0, captured.Count, "OnGoldSync must NOT fire when TrySpendGold returns CharacterNotFound.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-04: registered character at version=0; 3 sequential successful
        // mutations (AddGold, TrySpendGold, AddGold) -> captured Versions are
        // exactly [1, 2, 3] in order — no skip, no repeat.
        // -----------------------------------------------------------------------

        [Test]
        public void SequentialSuccessfulMutations_VersionsIncrementMonotonicallyWithNoSkipsOrRepeats()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);
            var capturedVersions = new List<uint>();
            void Recorder(GoldSyncEventArgs args) => capturedVersions.Add(args.Version);
            _currency.OnGoldSync += Recorder;

            // Act
            _currency.AddGold(Player, 100u, GoldTransactionReason.MonsterDrop);
            _currency.TrySpendGold(Player, 50u, GoldTransactionReason.ScrollPurchase);
            _currency.AddGold(Player, 200u, GoldTransactionReason.MonsterDrop);

            // Assert
            CollectionAssert.AreEqual(
                new uint[] { 1u, 2u, 3u },
                capturedVersions,
                "Version must increment by exactly 1 per successful mutation, in call order, with no skips or repeats.");

            _currency.OnGoldSync -= Recorder;
        }

        // -----------------------------------------------------------------------
        // AC-CS-F-01 / AC-CS-F-03 interaction: TrySpendGold's internal retry
        // (Story 004) — a forced ConcurrencyConflict on the first CAS attempt must
        // NOT fire OnGoldSync, and the subsequent successful retry must fire it
        // exactly once with the post-retry balance/version.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_RetriesAfterForcedConcurrencyConflict_FiresOnGoldSyncExactlyOnceForTheSuccessfulRetry()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);
            const uint staleVersion = 1u; // RegisterCharacter resets version to 0 — 1 is deliberately stale.
            var forcedConflict = _currency.TryCompareAndSwapSpend(Player, 100u, GoldTransactionReason.ScrollPurchase, staleVersion);
            Assert.AreEqual(GoldMutationError.ConcurrencyConflict, forcedConflict.Error, "Precondition: the forced conflict must have occurred before the subscriber is attached.");

            var captured = new List<GoldSyncEventArgs>();
            void Recorder(GoldSyncEventArgs args) => captured.Add(args);
            _currency.OnGoldSync += Recorder;

            // Act — a normal TrySpendGold retry now reads the current, correct version and succeeds.
            var result = _currency.TrySpendGold(Player, 100u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsTrue(result.Success, "Precondition: the retry must succeed once it reads the current, correct version.");
            Assert.AreEqual(1, captured.Count, "OnGoldSync must fire exactly once for the successful retry — never for a ConcurrencyConflict attempt.");
            Assert.AreEqual(Player, captured[0].CharacterID);
            Assert.AreEqual(900u, captured[0].NewBalance);
            Assert.AreEqual(1u, captured[0].Version, "The forced conflict never wrote a version bump; the successful retry is the character's first real mutation, so Version must be 1.");
            Assert.AreEqual(GoldTransactionReason.ScrollPurchase, captured[0].Reason);

            _currency.OnGoldSync -= Recorder;
        }
    }
}
