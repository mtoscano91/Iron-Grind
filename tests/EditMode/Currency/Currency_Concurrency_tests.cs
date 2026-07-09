using System.Linq;
using System.Threading.Tasks;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Currency
{
    /// <summary>
    /// EditMode unit tests for Story 004 — thread-safe concurrent mutation of
    /// <see cref="CurrencySystem.AddGold"/> and <see cref="CurrencySystem.TrySpendGold"/>.
    /// </summary>
    /// <remarks>
    /// Per <c>.claude/rules/test-standards.md</c>, tests must be deterministic and must not rely
    /// on real thread-scheduling timing to produce a specific error code. This file therefore
    /// splits into two kinds of test:
    /// <list type="bullet">
    /// <item>AC-CS-D-01 and AC-CS-J-01 spin up real concurrent <see cref="Task.Run(System.Action)"/>
    /// calls via <see cref="Task.WhenAll(Task[])"/> and assert only deterministic, invariant
    /// final-state properties (final balance, and for J-01 the sorted set of returned
    /// <c>NewBalance</c> values) — never which specific call "won" the race.</item>
    /// <item>AC-CS-D-02 exercises the deterministic internal seam
    /// <see cref="CurrencySystem.TryCompareAndSwapSpend"/> directly with a deliberately stale
    /// expected version to force <see cref="GoldMutationError.ConcurrencyConflict"/> on demand —
    /// no real race, no <see cref="Task.Run(System.Action)"/> needed.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    internal sealed class Currency_Concurrency_Tests
    {
        private CurrencySystem _currency;
        private static readonly CharacterID Player = new CharacterID(1001u);

        [SetUp]
        public void SetUp()
        {
            _currency = new CurrencySystem();
        }

        // -----------------------------------------------------------------------
        // AC-CS-D-01: Two concurrent TrySpendGold(charId, 600) calls on a character
        // with balance=800 (combined cost 1,200 > 800): exactly one call returns
        // Success with NewBalance=200; the other returns InsufficientFunds or
        // ConcurrencyConflict. Final GetBalance returns exactly 200. Balance never
        // negative, never double-debited.
        // -----------------------------------------------------------------------

        [Test]
        public async Task TrySpendGold_TwoConcurrentSpendsExceedingBalance_ExactlyOneSucceedsAndFinalBalanceIsCorrect()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 800u);

            // Act — real concurrency; only the final, invariant state is asserted below.
            Task<GoldMutationResult> spendA = Task.Run(() => _currency.TrySpendGold(Player, 600u, GoldTransactionReason.ScrollPurchase));
            Task<GoldMutationResult> spendB = Task.Run(() => _currency.TrySpendGold(Player, 600u, GoldTransactionReason.ScrollPurchase));
            GoldMutationResult[] results = await Task.WhenAll(spendA, spendB);

            // Assert
            GoldMutationResult[] successes = results.Where(r => r.Success).ToArray();
            GoldMutationResult[] failures = results.Where(r => !r.Success).ToArray();

            Assert.AreEqual(1, successes.Length, "Exactly one of the two concurrent spends must succeed — combined cost exceeds the starting balance.");
            Assert.AreEqual(1, failures.Length, "Exactly one of the two concurrent spends must fail.");
            Assert.AreEqual(200u, successes[0].NewBalance, "The winning spend must leave NewBalance=200 (800 - 600).");
            Assert.IsTrue(
                failures[0].Error == GoldMutationError.InsufficientFunds || failures[0].Error == GoldMutationError.ConcurrencyConflict,
                $"The losing spend must fail with InsufficientFunds or ConcurrencyConflict, got {failures[0].Error}.");
            Assert.AreEqual(200u, _currency.GetBalance(Player), "Final balance must be exactly 200 — never negative, never double-debited.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-D-02 (part 1): TryCompareAndSwapSpend called directly with a
        // deliberately stale expectedVersion deterministically forces
        // ConcurrencyConflict — balance and version are left unchanged.
        // -----------------------------------------------------------------------

        [Test]
        public void TryCompareAndSwapSpend_StaleExpectedVersion_ReturnsConcurrencyConflictAndLeavesBalanceUnchanged()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);
            const uint staleVersion = 1u; // RegisterCharacter resets version to 0 — 1 is deliberately stale.

            // Act
            GoldMutationResult result = _currency.TryCompareAndSwapSpend(Player, 100u, GoldTransactionReason.ScrollPurchase, staleVersion);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GoldMutationError.ConcurrencyConflict, result.Error);
            Assert.AreEqual(1000u, _currency.GetBalance(Player), "A forced ConcurrencyConflict must not write to the balance store.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-D-02 (part 2): after a forced ConcurrencyConflict, a normal
        // TrySpendGold retry on the same character with sufficient balance
        // succeeds — it reads the current, correct version rather than the stale
        // one used above.
        // -----------------------------------------------------------------------

        [Test]
        public void TrySpendGold_AfterForcedConcurrencyConflict_RetrySucceedsWithCorrectBalance()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);
            const uint staleVersion = 1u;
            GoldMutationResult forcedConflict = _currency.TryCompareAndSwapSpend(Player, 100u, GoldTransactionReason.ScrollPurchase, staleVersion);
            Assert.AreEqual(GoldMutationError.ConcurrencyConflict, forcedConflict.Error, "Precondition: the forced conflict must have occurred.");

            // Act
            GoldMutationResult result = _currency.TrySpendGold(Player, 100u, GoldTransactionReason.ScrollPurchase);

            // Assert
            Assert.IsTrue(result.Success, "A normal TrySpendGold retry must succeed once it reads the current, correct version.");
            Assert.AreEqual(900u, result.NewBalance);
            Assert.AreEqual(900u, _currency.GetBalance(Player));
        }

        // -----------------------------------------------------------------------
        // AC-CS-J-01: 4 concurrent AddGold(charId, 100, MonsterDrop) calls on a
        // character with balance=1000: after all 4 complete, GetBalance returns
        // exactly 1400 (no lost updates — must not be 1100/1200/1300).
        // -----------------------------------------------------------------------

        [Test]
        public async Task AddGold_FourConcurrentAdds_NoLostUpdatesAndFinalBalanceIsCorrect()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);

            // Act
            Task<GoldMutationResult>[] tasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => _currency.AddGold(Player, 100u, GoldTransactionReason.MonsterDrop)))
                .ToArray();
            GoldMutationResult[] results = await Task.WhenAll(tasks);

            // Assert
            Assert.IsTrue(results.All(r => r.Success), "All 4 concurrent AddGold calls must succeed — AddGold never rejects on conflict.");
            Assert.AreEqual(1400u, _currency.GetBalance(Player), "Final balance must be exactly 1400 — a value of 1100/1200/1300 indicates a lost update.");
        }

        // -----------------------------------------------------------------------
        // AC-CS-J-01 edge case: the 4 returned NewBalance values, sorted, must be
        // exactly {1100, 1200, 1300, 1400} — a stronger check than just the final
        // balance, confirming no two concurrent calls observed the same
        // pre-mutation balance (which would indicate a lost update masked by
        // coincidental final-value correctness).
        // -----------------------------------------------------------------------

        [Test]
        public async Task AddGold_FourConcurrentAdds_ReturnedNewBalancesAreDistinctAndSequential()
        {
            // Arrange
            _currency.RegisterCharacter(Player, 1000u);

            // Act
            Task<GoldMutationResult>[] tasks = Enumerable.Range(0, 4)
                .Select(_ => Task.Run(() => _currency.AddGold(Player, 100u, GoldTransactionReason.MonsterDrop)))
                .ToArray();
            GoldMutationResult[] results = await Task.WhenAll(tasks);

            // Assert
            uint[] sortedNewBalances = results.Select(r => r.NewBalance).OrderBy(b => b).ToArray();
            CollectionAssert.AreEqual(
                new uint[] { 1100u, 1200u, 1300u, 1400u },
                sortedNewBalances,
                "Each concurrent AddGold call must observe a distinct pre-mutation balance — no two calls may read the same balance.");
        }
    }
}
