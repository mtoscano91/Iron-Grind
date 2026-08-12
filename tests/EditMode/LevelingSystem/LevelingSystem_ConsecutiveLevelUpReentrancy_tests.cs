using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 003 (Consecutive Level-Up &amp; Re-Entrancy
    /// Guards). Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/> with a
    /// real <see cref="LevelingService"/>, following Story 001/002's integration-style idiom
    /// (never mocked). Auto-alloc data is supplied via a real <see cref="ClassRegistry"/>
    /// populated with test-local Warrior/Healer <see cref="ClassDefinition"/>s, matching
    /// <c>LevelingSystem_LevelUpSequenceCore_tests.cs</c>'s established idiom exactly.
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_ConsecutiveLevelUpReentrancy_Tests
    {
        private const byte WarriorClassType = 1;
        private const byte HealerClassType = 2;

        private sealed class LevelUpRecorder
        {
            public readonly List<(EntityID entityId, int newLevel)> Firings = new List<(EntityID, int)>();
            public void Handle(LevelUpEventArgs args) => Firings.Add((args.EntityId, args.NewLevel));
        }

        /// <summary>
        /// Builds the test-local class registry: Warrior (STR+2/DEX+1/VIT+1, 1 free point/level)
        /// and Healer (VIT+1/INT+2, 2 free points/level) — matching Story 002's worked examples.
        /// </summary>
        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WarriorClassType, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            registry.RegisterClass(HealerClassType, new ClassDefinition(
                strengthAutoAlloc: 0,
                dexterityAutoAlloc: 0,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 2,
                freePointsPerLevel: 2));
            return registry;
        }

        /// <summary>
        /// Same ADR-010 wiring sequence as Story 001/002's CreateWiredStats, extended with the
        /// Warrior/Healer class registry this story's CR-2.4 auto-alloc needs.
        /// </summary>
        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds, CreateClassRegistry());
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        // ---------------------------------------------------------------
        // AC-LS-07 — heldFreePoints increments correctly per class through the new CR-2.9 loop
        // wrapper; readable only via GetHeldFreePoints (no StatID exists for it — a structural
        // guarantee, not something a runtime assertion can add to). Likely already satisfied by
        // Story 002's CR-2.4/CR-2.8 bookkeeping inside ExecuteLevelUpSequence (unmodified by
        // this story) — this test confirms it still holds when exercised through the new loop.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_SingleLevelUp_Warrior_HeldFreePointsIncrementsByOne()
        {
            // Arrange — Warrior at L4, heldFreePoints=0.
            var warriorThresholds = new int[] { 0, 0, 0, 0, 0, 200, 999999 }; // index5 = L4->L5; index6 sentinel
            var (warriorStats, warriorLeveling) = CreateWiredStats(warriorThresholds);
            var warrior = CharacterStatsFixture.PlayerEntityId;
            warriorLeveling.RegisterPlayerEntity(warrior);
            warriorLeveling.RegisterPlayerClassType(warrior, WarriorClassType);
            warriorStats.SetBaseStat(warrior, StatID.Level, 4);
            Assert.AreEqual(0, warriorLeveling.GetHeldFreePoints(warrior), "Precondition: Warrior heldFreePoints starts at 0.");

            // Act — Warrior gains exactly one level.
            warriorStats.AddExperience(warrior, 200);

            // Assert
            Assert.AreEqual(5, warriorStats.GetBaseStat(warrior, StatID.Level), "Warrior must reach L5.");
            Assert.AreEqual(1, warriorLeveling.GetHeldFreePoints(warrior),
                "Warrior heldFreePoints must increment by 1 (FreePointsPerLevel), readable only via GetHeldFreePoints.");
        }

        [Test]
        public void LevelingService_SingleLevelUp_Healer_HeldFreePointsIncrementsByTwo()
        {
            // Arrange — Healer at L4, heldFreePoints=0.
            var healerThresholds = new int[] { 0, 0, 0, 0, 0, 200, 999999 };
            var (healerStats, healerLeveling) = CreateWiredStats(healerThresholds);
            var healer = CharacterStatsFixture.PlayerEntityId;
            healerLeveling.RegisterPlayerEntity(healer);
            healerLeveling.RegisterPlayerClassType(healer, HealerClassType);
            healerStats.SetBaseStat(healer, StatID.Level, 4);
            Assert.AreEqual(0, healerLeveling.GetHeldFreePoints(healer), "Precondition: Healer heldFreePoints starts at 0.");

            // Act — Healer gains exactly one level.
            healerStats.AddExperience(healer, 200);

            // Assert
            Assert.AreEqual(5, healerStats.GetBaseStat(healer, StatID.Level), "Healer must reach L5.");
            Assert.AreEqual(2, healerLeveling.GetHeldFreePoints(healer),
                "Healer heldFreePoints must increment by 2 (FreePointsPerLevel), readable only via GetHeldFreePoints.");
        }

        // ---------------------------------------------------------------
        // AC-LS-08 — a single XP grant crossing 3 thresholds (L3->L4->L5->L6) fires OnLevelUp
        // exactly 3 times, in order (4,5,6), and Level already reads 6 at every one of those
        // firings — proving the broadcast is deferred until the entire CR-2.9 loop completes,
        // never fired per-iteration.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_ThreeConsecutiveLevelUps_FiresOnLevelUpThriceInOrderAfterLoopCompletes()
        {
            // Arrange — Warrior at L3. index4=L3->L4, index5=L4->L5, index6=L5->L6, index7 sentinel.
            var xpThresholds = new int[] { 0, 0, 0, 0, 100, 200, 300, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 3);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new LevelUpRecorder();
            var levelDuringFirings = new List<int>();
            void CaptureLevelAtFiring(LevelUpEventArgs args) => levelDuringFirings.Add(stats.GetBaseStat(entity, StatID.Level));
            leveling.OnLevelUp += recorder.Handle;
            leveling.OnLevelUp += CaptureLevelAtFiring;

            // Act — a single grant spanning all 3 thresholds.
            stats.AddExperience(entity, 300);

            // Assert — Level reached the final value (6) before any broadcast fired.
            Assert.AreEqual(6, stats.GetBaseStat(entity, StatID.Level), "Level must reach 6.");

            Assert.AreEqual(3, recorder.Firings.Count, "OnLevelUp must fire exactly 3 times — once per level gained.");
            Assert.AreEqual((entity, 4), recorder.Firings[0], "First OnLevelUp firing must report level 4.");
            Assert.AreEqual((entity, 5), recorder.Firings[1], "Second OnLevelUp firing must report level 5.");
            Assert.AreEqual((entity, 6), recorder.Firings[2], "Third OnLevelUp firing must report level 6.");

            foreach (int levelAtFiring in levelDuringFirings)
                Assert.AreEqual(6, levelAtFiring,
                    "GetBaseStat(Level) must already read 6 at EVERY OnLevelUp firing — proves all broadcasts fire only after the full CR-2.9 loop completes, never mid-iteration.");

            // AC-LS-07 cross-check: heldFreePoints must accumulate across all 3 loop iterations
            // within this single AddExperience call (Warrior +1/level x 3 levels gained = 3),
            // not just the single-iteration case AC-LS-07's own dedicated test already covers.
            Assert.AreEqual(3, leveling.GetHeldFreePoints(entity),
                "heldFreePoints must accumulate across all 3 CR-2.9 loop iterations within one call (1+1+1=3), not just a single grant.");
        }

        // ---------------------------------------------------------------
        // AC-LS-09 — each CR-2.9 loop iteration reads fresh attribute totals written by the
        // PRIOR iteration's own auto-alloc — final MaxHP must match the LAST iteration's
        // totals (L4's Vitality), not a stale earlier value (L3's Vitality).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TwoConsecutiveLevelUps_FinalMaxHpReflectsLastIterationsFreshVitalityTotal()
        {
            // Arrange — Warrior at L2, VIT=10 (Warrior auto-allocs VIT+1/level).
            // index3=L2->L3, index4=L3->L4, index5 sentinel.
            var xpThresholds = new int[] { 0, 0, 0, 100, 200, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 2);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            // Act — a single grant spanning both thresholds (L2->L3->L4).
            stats.AddExperience(entity, 200);

            // Assert — VIT: L2's 10 -> L3's 11 (iteration 1) -> L4's 12 (iteration 2).
            Assert.AreEqual(4, stats.GetBaseStat(entity, StatID.Level), "Level must reach 4.");
            Assert.AreEqual(12, stats.GetBaseStat(entity, StatID.Vitality),
                "VIT must be 10+1+1=12 — both iterations' auto-alloc applied.");

            // MaxHP formula: FloorToInt((200 + VIT*20) * tier). tier=1.0 at L4 (<20).
            // If iteration 2 incorrectly reused iteration 1's stale VIT=11, MaxHP would be 420.
            // The correct, fresh-read value uses iteration 2's own VIT=12 -> MaxHP=440.
            Assert.AreEqual(440, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be computed from the SECOND iteration's own fresh VIT=12 total (440), " +
                "not a stale VIT=11 total carried over from the first iteration (420).");
        }

        // ---------------------------------------------------------------
        // AC-LS-10 — AllocateFreePoint is rejected while a CR-2.9 loop is in progress:
        // heldFreePoints unchanged (by the rejected call), no SetBaseStat, a "system busy"
        // result returned.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_WhileLevelingUpInProgress_RejectsAndLeavesHeldFreePointsUnchangedByThatCall()
        {
            // Arrange — Healer already holds free points from a prior, already-completed
            // level-up (L1->L2, no subscriber attached yet). index2=L1->L2, index3=L2->L3,
            // index4 sentinel.
            var xpThresholds = new int[] { 0, 0, 200, 300, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, HealerClassType);
            stats.SetBaseStat(entity, StatID.Level, 1);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 200); // L1 -> L2, no subscriber attached yet.
            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity),
                "Precondition: Healer holds 2 free points from the L1->L2 level-up.");

            // Now attach a subscriber that attempts AllocateFreePoint mid-sequence, during the
            // SECOND level-up (L2->L3), while _levelingUpInProgress is true.
            bool attempted = false;
            AllocateFreePointResult? capturedResult = null;
            int? heldBeforeAttempt = null;
            int? heldAfterAttempt = null;
            bool? inProgressDuringAttempt = null;

            void Handler(EntityID e, StatID statId)
            {
                if (attempted || statId != StatID.Level) return;
                attempted = true;
                heldBeforeAttempt = leveling.GetHeldFreePoints(entity);
                inProgressDuringAttempt = leveling.IsLevelingUpInProgress;
                capturedResult = leveling.AllocateFreePoint(entity, StatID.Vitality);
                heldAfterAttempt = leveling.GetHeldFreePoints(entity);
            }
            stats.Subscribe(Handler);

            // Act — L2 -> L3 (crosses index3=300; total XP becomes 200+150=350).
            stats.AddExperience(entity, 150);

            // Assert — the busy call was rejected and touched nothing.
            Assert.IsTrue(attempted, "Precondition: the mid-sequence handler must have run.");
            Assert.AreEqual(true, inProgressDuringAttempt, "IsLevelingUpInProgress must be true at the moment of the busy AllocateFreePoint attempt.");
            Assert.AreEqual(AllocateFreePointResult.RejectedSystemBusy, capturedResult, "AllocateFreePoint must return the busy-rejection result.");
            Assert.AreEqual(heldBeforeAttempt, heldAfterAttempt, "heldFreePoints must be unchanged by the rejected call specifically.");
            Assert.AreEqual(2, heldAfterAttempt, "heldFreePoints must still read 2 immediately after the rejected call (unaffected by it).");

            // The rest of the L2->L3 sequence must have proceeded normally, unaffected by the
            // rejected AllocateFreePoint attempt — CR-2.8's own legitimate grant still applies.
            Assert.AreEqual(3, stats.GetBaseStat(entity, StatID.Level), "Level must still reach 3 — the rejected AllocateFreePoint call must not have disrupted the sequence.");
            Assert.AreEqual(4, leveling.GetHeldFreePoints(entity), "heldFreePoints must be 2 (prior) + 2 (this level-up's legitimate CR-2.8 grant) = 4.");
            Assert.IsFalse(leveling.IsLevelingUpInProgress, "IsLevelingUpInProgress must be cleared after the sequence completes.");
        }

        // ---------------------------------------------------------------
        // AC-LS-49 (part a) — regression test proving the GDD's LITERAL EC-LS-10 scenario (a
        // subscriber calling AddExperience from inside an OnStatChanged handler) never reaches
        // LevelingService at all: CharacterStats' own Story 005 `_isFiring` re-entrancy guard
        // (a single flag covering the whole class, not scoped per-stat) throws
        // InvalidOperationException on the re-entrant SetBaseStat(Experience, ...) call. See
        // LevelingService.NotifyExperienceCrossedThreshold's doc comment for the full trace and
        // the documented GDD/implementation discrepancy this resolves.
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_ReentrantAddExperienceFromOnStatChangedHandler_ThrowsInvalidOperationException()
        {
            // Arrange — threshold deliberately unreachable; isolates this test to CharacterStats'
            // own guard, no leveling sequence is ever entered.
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            void Handler(EntityID e, StatID statId)
            {
                if (statId == StatID.Experience)
                    stats.AddExperience(entity, 5); // re-entrant — must throw inside CharacterStats.
            }
            stats.Subscribe(Handler);

            // Act / Assert
            var ex = Assert.Throws<InvalidOperationException>(() => stats.AddExperience(entity, 10));
            StringAssert.Contains("Re-entrance", ex.Message,
                "The exception must come from CharacterStats.IsFiringAndAssert's re-entrance guard.");
        }

        // ---------------------------------------------------------------
        // AC-LS-49 (part b) — the real, reachable re-entrancy path: a subscriber that calls
        // NotifyExperienceCrossedThreshold DIRECTLY (bypassing AddExperience/SetBaseStat, which
        // is what CharacterStats' own guard would otherwise intercept first — see part (a)
        // above). No SetBaseStat(Level, ...) occurs during the re-entrant call itself, and the
        // OUTER call's own CR-2.9 loop continues normally afterward, resolving both levels and
        // firing OnLevelUp for each.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_DirectReentrantNotifyExperienceCrossedThreshold_NoOpsAndOuterLoopContinuesNormally()
        {
            // Arrange — Warrior at L3. index4=L3->L4, index5=L4->L5, index6 sentinel.
            var xpThresholds = new int[] { 0, 0, 0, 0, 100, 200, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 3);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            bool attempted = false;
            bool? inProgressDuringAttempt = null;
            int? levelImmediatelyAfterAttempt = null;

            void Handler(EntityID e, StatID statId)
            {
                if (attempted || statId != StatID.Level) return;
                attempted = true;
                inProgressDuringAttempt = leveling.IsLevelingUpInProgress;
                leveling.NotifyExperienceCrossedThreshold(entity); // direct re-entrant call
                levelImmediatelyAfterAttempt = stats.GetBaseStat(entity, StatID.Level);
            }
            stats.Subscribe(Handler);

            var recorder = new LevelUpRecorder();
            leveling.OnLevelUp += recorder.Handle;

            // Act — a single grant spanning both thresholds (L3->L4->L5).
            stats.AddExperience(entity, 200);

            // Assert — the re-entrant attempt was observed but was a true no-op.
            Assert.IsTrue(attempted, "Precondition: the mid-sequence handler must have run.");
            Assert.AreEqual(true, inProgressDuringAttempt, "IsLevelingUpInProgress must be true at the moment of the re-entrant attempt.");
            Assert.AreEqual(4, levelImmediatelyAfterAttempt,
                "Level must still read 4 (this iteration's own CR-2.2 write) immediately after the re-entrant call returns — " +
                "the re-entrant call must not have advanced Level any further.");
            Assert.AreEqual(2, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must have been entered exactly twice — the OUTER call and the blocked RE-ENTRANT call.");

            // The OUTER call's own CR-2.9 loop must have continued normally, unaffected by the
            // blocked re-entrant attempt, resolving both levels.
            Assert.AreEqual(5, stats.GetBaseStat(entity, StatID.Level), "Level must reach 5 — the outer loop's own second iteration must not have been disrupted.");
            Assert.AreEqual(2, recorder.Firings.Count, "OnLevelUp must fire exactly twice — once per level actually gained by the OUTER call.");
            Assert.AreEqual((entity, 4), recorder.Firings[0]);
            Assert.AreEqual((entity, 5), recorder.Firings[1]);
            Assert.IsFalse(leveling.IsLevelingUpInProgress, "IsLevelingUpInProgress must be cleared after the OUTER call's loop completes.");
        }
    }
}
