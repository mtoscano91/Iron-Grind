using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 007 (Respec Two-Phase Commit &amp; Exception
    /// Safety — CR-4.1 / CR-4.4 / CR-4.6). Exercises
    /// <see cref="RespecTwoPhaseCommitCoordinator.ExecuteRespec"/> — this story's own production
    /// seam standing in for the not-yet-built Inventory System's Phase 1/Phase 2 orchestration —
    /// composed with a real <see cref="IronGrind.CharacterStats.CharacterStats"/> and a real
    /// <see cref="LevelingService"/>, following the established
    /// <c>CreateWiredStats</c>/<c>CreateClassRegistry</c> idiom from
    /// <c>LevelingSystem_RespecCommitSequence_tests.cs</c> (Story 006) exactly.
    /// </summary>
    /// <remarks>
    /// <para>Covers AC-LS-18a (combat gate stubbed true — <c>TryApplyRespec</c> never called,
    /// item stays in active inventory, no stats change) and AC-LS-22 (exception injected during
    /// CR-4.4 step 2 — <c>RollbackStatTransaction</c> reverts ALL touched stats to their exact
    /// pre-call values, <c>EndStatTransaction</c> never called, exception re-thrown, the
    /// test-double Inventory-System caller's <c>Release()</c> path is exercised, and no
    /// <c>OnStatChanged</c> fires for any stat touched in the aborted transaction).</para>
    /// <para><b>AC-LS-18b is explicitly NOT covered here</b> — blocked on OQ-LS-3 (Status
    /// Effects GDD has not yet defined "combat-tagged"). Tracked as tech debt per the story's own
    /// Test Evidence section; do not add a test for it until a future Status Effects story
    /// unblocks it.</para>
    /// <para>The AC-LS-22 exception is injected via <see cref="LevelingService.
    /// TestOnly_ThrowDuringRespecStep2"/> — a test-only seam (guarded by
    /// <c>UNITY_INCLUDE_TESTS</c>/<c>DEVELOPMENT_BUILD</c>, matching this project's established
    /// fault-injection pattern, e.g. <c>TransportFaultInjector</c> in the Networking Core epic)
    /// invoked once per primary-attribute write inside the CR-4.4 step 2 loop, letting the test
    /// throw after a specific number of writes.</para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_RespecTwoPhaseCommit_Tests
    {
        private const byte WarriorClassType = 1;

        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WarriorClassType, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            return registry;
        }

        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds, CreateClassRegistry());
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        // ---------------------------------------------------------------
        // Test-double IItemReservation — tracks Consume()/Release() call counts and the
        // resulting state, standing in for the not-yet-built Inventory System's real handle.
        // ---------------------------------------------------------------

        private sealed class MockItemReservation : IItemReservation
        {
            public enum ReservationState { Reserved, Consumed, ActiveInventory }

            public ReservationState State { get; private set; } = ReservationState.Reserved;
            public int ConsumeCallCount { get; private set; }
            public int ReleaseCallCount { get; private set; }

            public void Consume()
            {
                ConsumeCallCount++;
                State = ReservationState.Consumed;
            }

            public void Release()
            {
                ReleaseCallCount++;
                State = ReservationState.ActiveInventory;
            }
        }

        // ---------------------------------------------------------------
        // AC-LS-18a — combat gate stubbed true: TryApplyRespec never called, item never
        // reserved (stays in mock active-inventory state), no stats change.
        // ---------------------------------------------------------------

        [Test]
        public void RespecTwoPhaseCommitCoordinator_CombatGateStubbedTrue_NeverCallsTryApplyRespecItemStaysActive()
        {
            // Arrange — Warrior L10 at floor (STR=28, DEX=19, VIT=19, INT=10).
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 10);
            stats.SetBaseStat(entity, StatID.Strength, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 19);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            bool reserveItemCalled = false;
            bool itemInActiveInventory = true;
            IItemReservation ReserveItem()
            {
                reserveItemCalled = true;
                itemInActiveInventory = false;
                return new MockItemReservation();
            }

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 29,
                [StatID.Dexterity] = 19,
                [StatID.Vitality] = 19,
                [StatID.Intelligence] = 10,
            };

            // Act — AC-LS-18a: combat gate stubbed to return true.
            RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                entity,
                hasCombatTaggedEffect: _ => true,
                reserveItem: ReserveItem,
                levelingService: leveling,
                newTotals: newTotals);

            // Assert — item never reserved, never touched.
            Assert.IsFalse(reserveItemCalled,
                "reserveItem must never be invoked when the combat gate rejects — the item must never be reserved.");
            Assert.IsTrue(itemInActiveInventory,
                "Item must remain in active inventory — never reserved, never consumed.");

            // Assert — TryApplyRespec never called: no stats changed.
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength), "No stats may change — TryApplyRespec must never be called.");
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality));
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence));
            Assert.AreEqual(0, recorder.FiredCount.Count, "No OnStatChanged may fire.");
        }

        // ---------------------------------------------------------------
        // AC-LS-22 — exception injected during CR-4.4 step 2: RollbackStatTransaction reverts
        // ALL touched stats to their exact pre-call values (real value-level reversion, not
        // just proof that Rollback was invoked), EndStatTransaction never called, exception
        // re-thrown, Release() exercised, no OnStatChanged fires for any stat touched in the
        // aborted transaction.
        // ---------------------------------------------------------------

        [Test]
        public void RespecTwoPhaseCommitCoordinator_ExceptionDuringStep2_RollsBackAllTouchedStatsReleasesItemRethrows()
        {
            // Arrange — Warrior L10 at floor (STR=28, DEX=19, VIT=19, INT=10), with derived
            // stats and resource pools also pre-seeded so we can prove they are UNCHANGED
            // (the aborted transaction never reaches RecomputeDerivedStats or the HP/MP
            // reconciliation — the exception fires inside the primary-attribute write loop,
            // CR-4.4 step 2, before either of those run).
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 10);
            stats.SetBaseStat(entity, StatID.Strength, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 19);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);
            // NOTE: MaxHP/MaxMP/AttackPower/Defense/MagicDefense/CritChance/AttackSpeedMultiplier
            // below are ARBITRARY placeholder values, not formula-derived output — they are
            // chosen only to prove non-disturbance (RecomputeDerivedStats never runs before this
            // test's injected exception, since it fires inside the step-2 write loop). Do not
            // mistake these for verified RecomputeDerivedStats formula results; the real formula
            // at STR=28/VIT=19/INT=10, tier 1.0 would yield different numbers (e.g. MaxHP=580,
            // MaxMP=220), which is irrelevant here since that recompute never executes.
            stats.SetBaseStat(entity, StatID.MaxHP, 500);
            stats.SetBaseStat(entity, StatID.MaxMP, 300);
            stats.SetBaseStat(entity, StatID.AttackPower, 66);
            stats.SetBaseStat(entity, StatID.Defense, 33);
            stats.SetBaseStat(entity, StatID.MagicDefense, 4);
            stats.SetBaseStatFloat(entity, StatID.CritChance, 0.0785f);
            stats.SetBaseStatFloat(entity, StatID.AttackSpeedMultiplier, 1.057f);
            stats.SetCurrentHP(entity, 400f);
            stats.SetCurrentMP(entity, 200f);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            var reservation = new MockItemReservation();
            bool reserveItemCalled = false;
            IItemReservation ReserveItem()
            {
                reserveItemCalled = true;
                return reservation;
            }

            // Inject the exception after the 2nd primary-attribute write (Strength, then
            // Dexterity) — Vitality and Intelligence are never reached by the write loop,
            // proving the rollback restores exactly the stats that were touched.
            int writeCount = 0;
            leveling.TestOnly_ThrowDuringRespecStep2 = _ =>
            {
                writeCount++;
                if (writeCount == 2)
                    throw new InvalidOperationException("AC-LS-22 injected test exception during CR-4.4 step 2.");
            };

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 50,
                [StatID.Dexterity] = 40,
                [StatID.Vitality] = 30,
                [StatID.Intelligence] = 25,
            };

            // Act / Assert — the exception propagates out of TryApplyRespec and out of the
            // coordinator (CR-4.1 Phase 2: the Inventory-System-side caller must observe it).
            var thrown = Assert.Throws<InvalidOperationException>(() =>
                RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                    entity,
                    hasCombatTaggedEffect: _ => false,
                    reserveItem: ReserveItem,
                    levelingService: leveling,
                    newTotals: newTotals));
            Assert.AreEqual("AC-LS-22 injected test exception during CR-4.4 step 2.", thrown.Message);

            // Assert — item was reserved (gate cleared), then Released on the exception path.
            Assert.IsTrue(reserveItemCalled, "reserveItem must be called once the combat gate clears.");
            Assert.AreEqual(1, reservation.ReleaseCallCount, "Release() must be called exactly once on the exception path.");
            Assert.AreEqual(0, reservation.ConsumeCallCount, "Consume() must never be called on the exception path.");
            Assert.AreEqual(MockItemReservation.ReservationState.ActiveInventory, reservation.State,
                "Item must be returned to active inventory.");

            // Assert — ALL stats touched by the aborted transaction reverted to their EXACT
            // pre-call values. This is the real proof of the Character Stats Story 007 fix:
            // actual GetBaseStat/GetBaseStatFloat/GetCurrentHP/GetCurrentMP values, not merely
            // that RollbackStatTransaction was invoked.
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength), "Strength must revert to its pre-call value.");
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity), "Dexterity must revert to its pre-call value.");
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality), "Vitality must be unchanged — never reached by the aborted write loop.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "Intelligence must be unchanged — never reached by the aborted write loop.");
            Assert.AreEqual(500, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must be unchanged — RecomputeDerivedStats never ran.");
            Assert.AreEqual(300, stats.GetBaseStat(entity, StatID.MaxMP), "MaxMP must be unchanged — RecomputeDerivedStats never ran.");
            Assert.AreEqual(66, stats.GetBaseStat(entity, StatID.AttackPower), "AttackPower must be unchanged.");
            Assert.AreEqual(33, stats.GetBaseStat(entity, StatID.Defense), "Defense must be unchanged.");
            Assert.AreEqual(4, stats.GetBaseStat(entity, StatID.MagicDefense), "MagicDefense must be unchanged.");
            Assert.AreEqual(0.0785f, stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-5f, "CritChance must be unchanged.");
            Assert.AreEqual(1.057f, stats.GetBaseStatFloat(entity, StatID.AttackSpeedMultiplier), 1e-5f, "AttackSpeedMultiplier must be unchanged.");
            Assert.AreEqual(400f, stats.GetCurrentHP(entity), "CurrentHP must be unchanged — never reached by the aborted transaction.");
            Assert.AreEqual(200f, stats.GetCurrentMP(entity), "CurrentMP must be unchanged — never reached by the aborted transaction.");

            // Assert — heldFreePoints is unchanged (CR-4.2 territory, not touched by CR-4.4 at
            // all). This entity never had free points allocated (no level-ups occurred in this
            // test — only direct SetBaseStat calls), so GetHeldFreePoints' documented default
            // (0, for an entity never present in the internal dictionary) is the correct
            // pre-seeded value to assert against. Future-proofs against a refactor that
            // accidentally moves a heldFreePoints write inside the try block.
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity),
                "heldFreePoints must be unchanged by an aborted respec — TryApplyRespec never touches it (CR-4.2), and this entity never had free points to begin with.");

            // Assert — no OnStatChanged fired for ANY stat touched during the aborted transaction.
            Assert.AreEqual(0, recorder.FiredCount.Count,
                "No OnStatChanged may fire for any stat touched during the aborted transaction.");

            // Assert — EndStatTransaction was never called: the transaction is already closed
            // by RollbackStatTransaction, so a subsequent EndStatTransaction() call throws as
            // if none was ever open.
            Assert.Throws<InvalidOperationException>(() => stats.EndStatTransaction(),
                "EndStatTransaction must throw — RollbackStatTransaction already closed the transaction; EndStatTransaction was never reached on the aborted path.");
        }

        // ---------------------------------------------------------------
        // Success path — no exception: TryApplyRespec succeeds, Consume() is called exactly
        // once, Release() is never called. Proves the unity-specialist-flagged restructure
        // (Consume() moved outside the try block) didn't break the happy path.
        // ---------------------------------------------------------------

        [Test]
        public void RespecTwoPhaseCommitCoordinator_SuccessfulCommit_CallsConsumeExactlyOnceNeverReleases()
        {
            // Arrange — Warrior L10 at floor (STR=28, DEX=19, VIT=19, INT=10).
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 10);
            stats.SetBaseStat(entity, StatID.Strength, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 19);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var reservation = new MockItemReservation();

            // Valid newTotals — at or above the L10 floor (STR>=28, DEX/VIT>=19, INT>=10) — so
            // the commit actually succeeds.
            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 29,
                [StatID.Dexterity] = 19,
                [StatID.Vitality] = 19,
                [StatID.Intelligence] = 10,
            };

            // Act
            RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                entity,
                hasCombatTaggedEffect: _ => false,
                reserveItem: () => reservation,
                levelingService: leveling,
                newTotals: newTotals);

            // Assert
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Strength), "Commit must have succeeded.");
            Assert.AreEqual(1, reservation.ConsumeCallCount, "Consume() must be called exactly once on a successful commit.");
            Assert.AreEqual(0, reservation.ReleaseCallCount, "Release() must never be called on a successful commit.");
            Assert.AreEqual(MockItemReservation.ReservationState.Consumed, reservation.State,
                "Reservation must end in the Consumed state.");
        }

        // ---------------------------------------------------------------
        // EC-LS-22 — nested BeginStatTransaction triggered specifically FROM INSIDE
        // TryApplyRespec (not just at the raw CharacterStats level, which Character Stats'
        // own Story 007 gate test already covers). Reproduced by opening a transaction
        // directly on CharacterStats before calling TryApplyRespec — TryApplyRespec's own
        // BeginStatTransaction() call then throws InvalidOperationException immediately,
        // before any write. This is the cleanest way to trigger this specific nested case
        // without an artificial hook — no new test-only seam needed.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_TransactionAlreadyOpenExternally_RollsBackUnconditionallyAndRethrows()
        {
            // Arrange — Warrior L10 at floor (STR=28, DEX=19, VIT=19, INT=10).
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 10);
            stats.SetBaseStat(entity, StatID.Strength, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 19);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            // Leave a transaction open externally — simulates the EC-LS-22 "programming error
            // leaves a transaction open" scenario. TryApplyRespec's own BeginStatTransaction()
            // call below will hit this already-open transaction and throw.
            stats.BeginStatTransaction();

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 29,
                [StatID.Dexterity] = 19,
                [StatID.Vitality] = 19,
                [StatID.Intelligence] = 10,
            };

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => leveling.TryApplyRespec(entity, newTotals),
                "TryApplyRespec's own BeginStatTransaction() call must throw when a transaction is already open (EC-LS-22).");

            // Assert — no stats changed; the write loop never ran.
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality));
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence));
            Assert.AreEqual(0, recorder.FiredCount.Count, "No OnStatChanged may fire.");

            // Assert — the same unconditional-rollback-and-rethrow catch behavior closed the
            // (externally-opened) transaction: EndStatTransaction() now throws as if none was
            // ever open.
            Assert.Throws<InvalidOperationException>(() => stats.EndStatTransaction(),
                "EndStatTransaction must throw — TryApplyRespec's catch block called RollbackStatTransaction unconditionally, closing even the externally-opened transaction.");
        }

        // ---------------------------------------------------------------
        // EC-LS-23 broadened coverage — exception injected AFTER RecomputeDerivedStats (CR-4.4
        // step 3) instead of during step 2, proving rollback also reverts derived stats that
        // were already recomputed before the failure, not just the primary attributes
        // AC-LS-22's step-2 injection covers.
        // ---------------------------------------------------------------

        [Test]
        public void RespecTwoPhaseCommitCoordinator_ExceptionAfterRecomputeDerivedStats_RollsBackPrimaryAndDerivedStats()
        {
            // Arrange — same seeded baseline as the step-2 test. See that test's NOTE for why
            // the derived-stat seed values are arbitrary placeholders, not formula-derived —
            // same rationale applies here even though RecomputeDerivedStats DOES run in this
            // scenario, because this test only asserts reversion to the ORIGINAL seeded values,
            // never the freshly recomputed ones.
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 10);
            stats.SetBaseStat(entity, StatID.Strength, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 19);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);
            stats.SetBaseStat(entity, StatID.MaxHP, 500);
            stats.SetBaseStat(entity, StatID.MaxMP, 300);
            stats.SetBaseStat(entity, StatID.AttackPower, 66);
            stats.SetBaseStat(entity, StatID.Defense, 33);
            stats.SetBaseStat(entity, StatID.MagicDefense, 4);
            stats.SetBaseStatFloat(entity, StatID.CritChance, 0.0785f);
            stats.SetBaseStatFloat(entity, StatID.AttackSpeedMultiplier, 1.057f);
            stats.SetCurrentHP(entity, 400f);
            stats.SetCurrentMP(entity, 200f);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            leveling.TestOnly_ThrowAfterRecomputeDerivedStats = _ =>
                throw new InvalidOperationException("EC-LS-23 injected test exception after RecomputeDerivedStats.");

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 50,
                [StatID.Dexterity] = 40,
                [StatID.Vitality] = 30,
                [StatID.Intelligence] = 25,
            };

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => leveling.TryApplyRespec(entity, newTotals),
                "Exception must propagate out of TryApplyRespec.");

            // Assert — primary attributes (step 2) reverted.
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality));
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence));

            // Assert — derived stats, ALREADY recomputed (step 3) before the injected
            // exception, also reverted to their original pre-transaction values.
            Assert.AreEqual(500, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must revert even though RecomputeDerivedStats already wrote a new value.");
            Assert.AreEqual(300, stats.GetBaseStat(entity, StatID.MaxMP), "MaxMP must revert even though RecomputeDerivedStats already wrote a new value.");
            Assert.AreEqual(66, stats.GetBaseStat(entity, StatID.AttackPower));
            Assert.AreEqual(33, stats.GetBaseStat(entity, StatID.Defense));
            Assert.AreEqual(4, stats.GetBaseStat(entity, StatID.MagicDefense));
            Assert.AreEqual(0.0785f, stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-5f);
            Assert.AreEqual(1.057f, stats.GetBaseStatFloat(entity, StatID.AttackSpeedMultiplier), 1e-5f);

            // Assert — step 4 (HP/MP reassertion) never ran; CurrentHP/CurrentMP unchanged.
            Assert.AreEqual(400f, stats.GetCurrentHP(entity));
            Assert.AreEqual(200f, stats.GetCurrentMP(entity));

            Assert.AreEqual(0, recorder.FiredCount.Count, "No OnStatChanged may fire.");
            Assert.Throws<InvalidOperationException>(() => stats.EndStatTransaction(),
                "EndStatTransaction must throw — the transaction was already closed by Rollback.");
        }

        // ---------------------------------------------------------------
        // Null-guard tests — ExecuteRespec's four ArgumentNullException checks, each isolated
        // by supplying valid values for every OTHER parameter so the test proves that specific
        // guard fires (not just that some earlier guard happened to fire first).
        // ---------------------------------------------------------------

        private static Dictionary<StatID, int> ValidNewTotals() => new Dictionary<StatID, int>
        {
            [StatID.Strength] = 29,
            [StatID.Dexterity] = 19,
            [StatID.Vitality] = 19,
            [StatID.Intelligence] = 10,
        };

        [Test]
        public void RespecTwoPhaseCommitCoordinator_NullHasCombatTaggedEffect_ThrowsArgumentNullException()
        {
            var xpThresholds = new int[] { 0, 999999 };
            var (_, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                    entity,
                    hasCombatTaggedEffect: null,
                    reserveItem: () => new MockItemReservation(),
                    levelingService: leveling,
                    newTotals: ValidNewTotals()));
            Assert.AreEqual("hasCombatTaggedEffect", ex.ParamName);
        }

        [Test]
        public void RespecTwoPhaseCommitCoordinator_NullReserveItem_ThrowsArgumentNullException()
        {
            var xpThresholds = new int[] { 0, 999999 };
            var (_, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                    entity,
                    hasCombatTaggedEffect: _ => false,
                    reserveItem: null,
                    levelingService: leveling,
                    newTotals: ValidNewTotals()));
            Assert.AreEqual("reserveItem", ex.ParamName);
        }

        [Test]
        public void RespecTwoPhaseCommitCoordinator_NullLevelingService_ThrowsArgumentNullException()
        {
            var entity = CharacterStatsFixture.PlayerEntityId;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                    entity,
                    hasCombatTaggedEffect: _ => false,
                    reserveItem: () => new MockItemReservation(),
                    levelingService: null,
                    newTotals: ValidNewTotals()));
            Assert.AreEqual("levelingService", ex.ParamName);
        }

        [Test]
        public void RespecTwoPhaseCommitCoordinator_NullNewTotals_ThrowsArgumentNullException()
        {
            var xpThresholds = new int[] { 0, 999999 };
            var (_, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;

            var ex = Assert.Throws<ArgumentNullException>(() =>
                RespecTwoPhaseCommitCoordinator.ExecuteRespec(
                    entity,
                    hasCombatTaggedEffect: _ => false,
                    reserveItem: () => new MockItemReservation(),
                    levelingService: leveling,
                    newTotals: null));
            Assert.AreEqual("newTotals", ex.ParamName);
        }
    }
}
