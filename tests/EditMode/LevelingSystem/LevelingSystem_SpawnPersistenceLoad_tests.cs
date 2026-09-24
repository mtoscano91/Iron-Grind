using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using UnityEngine.TestTools;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 009 (Spawn Initialization &amp; Persistence Load
    /// — CR-6). Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, following the established <c>CreateWiredStats</c>/
    /// <c>CreateClassRegistry</c> idiom from <c>LevelingSystem_LevelCapBehavior_tests.cs</c>
    /// (Story 008) exactly.
    /// </summary>
    /// <remarks>
    /// Covers AC-LS-27 (<see cref="LevelingService.InitializeAtL1"/> spawn sequence), AC-LS-28
    /// (persistence-load path fires zero level-up/threshold events and performs no recompute),
    /// AC-LS-29 (<c>heldFreePoints</c> save/load round-trip is lossless and not derivable from
    /// <c>GetBaseStat</c>), AC-LS-30 (corrupted <c>heldFreePoints</c> clamps, EC-LS-36), and
    /// AC-LS-51 (corrupted <c>Level</c> clamps, EC-LS-38).
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_SpawnPersistenceLoad_Tests
    {
        private const byte WarriorClassType = 1;

        /// <summary>
        /// Warrior: STR+2/DEX+1/VIT+1/INT+0 auto-alloc, 1 free point/level — matching this
        /// epic's established worked example exactly (see
        /// <c>LevelingSystem_LevelCapBehavior_tests.cs</c>/<c>LevelingSystem_FreePointAllocation_tests.cs</c>).
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
        // AC-LS-27 — InitializeAtL1: Level=1, all primary attributes=10, Experience=0,
        // heldFreePoints=0, F-3-F-9 computed at x1.0. Zero OnLevelUp / zero
        // NotifyExperienceCrossedThreshold firings.
        //
        // Hand-derived F-3-F-9 at STR=DEX=VIT=INT=10, tier=1.0 (see RecomputeDerivedStats):
        //   MaxHP               = FloorToInt((200 + 10*20) * 1.0) = FloorToInt(400.0)  = 400
        //   MaxMP                = Min(FloorToInt((100 + 10*12) * 1.0), 9999) = FloorToInt(220.0) = 220
        //   AttackPower          = FloorToInt((10 + 10*2) * 1.0)  = FloorToInt(30.0)   = 30
        //   Defense              = FloorToInt((5 + 10*1.5) * 1.0) = FloorToInt(20.0)   = 20
        //   MagicDefense         = FloorToInt(10*0.4 * 1.0)       = FloorToInt(4.0)    = 4
        //   CritChance           = 0.05 + (10*0.0015*1.0)         = 0.05 + 0.015       = 0.065
        //   AttackSpeedMultiplier= 1.0 + (10*0.003*1.0)           = 1.0 + 0.03         = 1.03
        // ---------------------------------------------------------------

        [Test]
        public void InitializeAtL1_FreshEntity_SetsLevelOneAttributesTenAndDerivedStatsAtTierOnePointZero()
        {
            // Arrange
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            int levelUpCount = 0;
            leveling.OnLevelUp += _ => levelUpCount++;

            // Act
            leveling.InitializeAtL1(entity, WarriorClassType);

            // Assert — CR-6.2 steps 1-2, 5.
            Assert.AreEqual(1, stats.GetBaseStat(entity, StatID.Level), "Level must be 1.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Strength), "Strength must be 10.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Dexterity), "Dexterity must be 10.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Vitality), "Vitality must be 10.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "Intelligence must be 10.");
            Assert.AreEqual(0, stats.GetBaseStat(entity, StatID.Experience), "Experience must be 0.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must be 0.");

            // Assert — CR-6.2 step 3 (F-3-F-9 at tier x1.0, hand-derived above).
            Assert.AreEqual(400, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must be 400.");
            Assert.AreEqual(220, stats.GetBaseStat(entity, StatID.MaxMP), "MaxMP must be 220.");
            Assert.AreEqual(30, stats.GetBaseStat(entity, StatID.AttackPower), "AttackPower must be 30.");
            Assert.AreEqual(20, stats.GetBaseStat(entity, StatID.Defense), "Defense must be 20.");
            Assert.AreEqual(4, stats.GetBaseStat(entity, StatID.MagicDefense), "MagicDefense must be 4.");
            Assert.AreEqual(0.065f, stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-5f, "CritChance must be 0.065.");
            Assert.AreEqual(1.03f, stats.GetBaseStatFloat(entity, StatID.AttackSpeedMultiplier), 1e-5f, "AttackSpeedMultiplier must be 1.03.");

            // Assert — CR-6.2 step 4 (HP/MP fully restored to the freshly written Max values).
            Assert.AreEqual(400f, stats.GetCurrentHP(entity), "CurrentHP must be set to MaxHP.");
            Assert.AreEqual(220f, stats.GetCurrentMP(entity), "CurrentMP must be set to MaxMP.");

            // Assert — CR-6.1: not a level-up.
            Assert.AreEqual(0, levelUpCount, "OnLevelUp must never fire during spawn initialization.");
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must never be called during spawn initialization.");
        }

        // ---------------------------------------------------------------
        // AC-LS-28 — RestoreLevelingState on a L35 Warrior restored via SetBaseStat (simulating
        // Character Persistence): zero events, no recompute, heldFreePoints readable as 7.
        // ---------------------------------------------------------------

        [Test]
        public void RestoreLevelingState_L35WarriorRestoredViaSetBaseStat_NoEventsNoRecomputeHeldFreePointsReadableAsSeven()
        {
            // Arrange — simulate Character Persistence: SetBaseStat calls directly, NOT
            // InitializeAtL1. Derived stats are set to arbitrary sentinel values to prove no
            // recompute occurs.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 35);
            stats.SetBaseStat(entity, StatID.Strength, 58);
            stats.SetBaseStat(entity, StatID.Dexterity, 44);
            stats.SetBaseStat(entity, StatID.Vitality, 44);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);
            stats.SetBaseStat(entity, StatID.MaxHP, 12345); // sentinel — must be unchanged by RestoreLevelingState.
            stats.SetBaseStat(entity, StatID.AttackPower, 99999); // sentinel.

            int levelUpCount = 0;
            leveling.OnLevelUp += _ => levelUpCount++;

            // Act
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 7));

            // Assert — zero events.
            Assert.AreEqual(0, levelUpCount, "OnLevelUp must never fire during a persistence load.");
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must never be called during a persistence load.");

            // Assert — no recompute: the sentinel derived-stat values are untouched.
            Assert.AreEqual(12345, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must be untouched — no F-3-F-9 recompute on load.");
            Assert.AreEqual(99999, stats.GetBaseStat(entity, StatID.AttackPower), "AttackPower must be untouched — no F-3-F-9 recompute on load.");

            // Assert — heldFreePoints readable as 7.
            Assert.AreEqual(7, leveling.GetHeldFreePoints(entity), "heldFreePoints must be exactly the restored value, 7.");

            // Assert — Level itself is untouched (35 is within [1,60], no clamp).
            Assert.AreEqual(35, stats.GetBaseStat(entity, StatID.Level), "Level must remain 35 — no clamp needed.");
        }

        // ---------------------------------------------------------------
        // AC-LS-29 — heldFreePoints save/load round-trip is lossless via GetLevelingState/
        // RestoreLevelingState, and is not derivable from GetBaseStat (no StatID.heldFreePoints
        // exists).
        // ---------------------------------------------------------------

        [Test]
        public void GetLevelingState_ThenRestoreLevelingState_HealerL30HeldFreePointsFourteen_RoundTripsLosslessly()
        {
            // Arrange — a Healer-like Warrior-registry entity (class identity is irrelevant to
            // this AC; only the heldFreePoints round-trip is under test) at L30 with
            // heldFreePoints=14 (within a plausible max for some class — round-trip fidelity
            // does not depend on the corruption-clamp logic exercised by AC-LS-30).
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (sourceStats, sourceLeveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            sourceLeveling.RegisterPlayerEntity(entity);
            sourceLeveling.RegisterPlayerClassType(entity, WarriorClassType);
            sourceStats.SetBaseStat(entity, StatID.Level, 30);
            sourceLeveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 14));

            // Act — save.
            LevelingStateSnapshot saved = sourceLeveling.GetLevelingState(entity);

            // Assert — not derivable from GetBaseStat: no StatID represents heldFreePoints, so
            // the only way to read it is through GetHeldFreePoints/GetLevelingState.
            Assert.AreEqual(14, saved.HeldFreePoints, "GetLevelingState must capture the exact heldFreePoints value.");

            // Assert — AC-LS-29's non-derivability clause, proven executably (code-review
            // suggestion) rather than left as a comment: no StatID enum member represents
            // heldFreePoints, so GetBaseStat is structurally incapable of exposing it. Fails
            // immediately if a future change ever added such a member.
            foreach (string statIdName in System.Enum.GetNames(typeof(StatID)))
            {
                StringAssert.DoesNotContain("freepoint", statIdName.ToLowerInvariant(),
                    $"StatID.{statIdName} must not represent heldFreePoints — AC-LS-29 requires it stay unrecoverable via GetBaseStat.");
            }

            // Act — load into a second, independent LevelingService/CharacterStats pair
            // (simulating a fresh session), with Level restored first via SetBaseStat (as
            // Character Persistence would do).
            var (targetStats, targetLeveling) = CreateWiredStats(xpThresholds);
            var targetEntity = CharacterStatsFixture.PlayerEntityId;
            targetLeveling.RegisterPlayerEntity(targetEntity);
            targetLeveling.RegisterPlayerClassType(targetEntity, WarriorClassType);
            targetStats.SetBaseStat(targetEntity, StatID.Level, 30);

            targetLeveling.RestoreLevelingState(targetEntity, saved);

            // Assert — lossless round-trip.
            Assert.AreEqual(14, targetLeveling.GetHeldFreePoints(targetEntity),
                "RestoreLevelingState must reproduce the exact saved heldFreePoints value, 14.");
        }

        // ---------------------------------------------------------------
        // AC-LS-30 — corrupted heldFreePoints on load: 500 (above L60 Warrior max of 59) clamps
        // to 59; -5 clamps to 0. Error logged in each case (EC-LS-36).
        // ---------------------------------------------------------------

        [Test]
        public void RestoreLevelingState_CorruptedHeldFreePointsAboveMax_ClampsToFiftyNineAndLogsError()
        {
            // Arrange — L60 Warrior: max = (60-1)*1 = 59.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*heldFreePoints=500.*clamped to 59"));

            // Act
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 500));

            // Assert
            Assert.AreEqual(59, leveling.GetHeldFreePoints(entity), "heldFreePoints must clamp to the L60 Warrior max, 59.");
        }

        [Test]
        public void RestoreLevelingState_CorruptedHeldFreePointsBelowZero_ClampsToZeroAndLogsError()
        {
            // Arrange
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*heldFreePoints=-5.*clamped to 0"));

            // Act
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: -5));

            // Assert
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must clamp to 0, never go negative.");
        }

        // ---------------------------------------------------------------
        // AC-LS-51 — corrupted Level on load: Level=0 clamps to 1, Level=70 clamps to 60. Error
        // logged in each case (EC-LS-38); no IndexOutOfRangeException; LevelTierMultiplier/AtCap
        // state derive correctly from the clamped value (proven via GetExperienceThreshold,
        // reusing the same code path AC-LS-25's at-cap sentinel test already proves).
        // ---------------------------------------------------------------

        [Test]
        public void RestoreLevelingState_CorruptedLevelZero_ClampsToOneAndLogsError()
        {
            // Arrange — a 62-entry table so GetExperienceThreshold(Level=1) safely resolves
            // index 2, proving ordinary (non-sentinel) derivation works post-clamp.
            var xpThresholds = new int[62];
            xpThresholds[2] = 150; // L1 -> L2 threshold.
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 0); // corrupted.

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*Level=0.*clamped to 1"));

            // Act
            Assert.DoesNotThrow(() => leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 0)),
                "RestoreLevelingState must not throw for a corrupted Level=0.");

            // Assert — clamp applied.
            Assert.AreEqual(1, stats.GetBaseStat(entity, StatID.Level), "Level must clamp to 1.");

            // Assert — derivation correctness: an ordinary (non-sentinel) threshold resolves
            // without exception, proving LevelTierMultiplier/AtCap state derive correctly from
            // the clamped Level=1 (same GetExperienceThreshold code path as AC-LS-25).
            int threshold = 0;
            Assert.DoesNotThrow(() => threshold = leveling.GetExperienceThreshold(entity),
                "GetExperienceThreshold must not throw at the clamped Level=1.");
            Assert.AreEqual(150, threshold, "GetExperienceThreshold at the clamped Level=1 must return the ordinary xpThresholds[2] entry, not the cap sentinel.");
        }

        [Test]
        public void RestoreLevelingState_CorruptedLevelSeventy_ClampsToSixtyAndLogsError()
        {
            // Arrange — 62-entry table with the CR-5.3 sentinel at index 61, matching Story 008's
            // AC-LS-25 worked example exactly.
            var xpThresholds = new int[62];
            xpThresholds[61] = int.MaxValue; // CR-5.3 sentinel.
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 70); // corrupted.

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*Level=70.*clamped to 60"));

            // Act
            Assert.DoesNotThrow(() => leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 0)),
                "RestoreLevelingState must not throw for a corrupted Level=70 — no IndexOutOfRangeException.");

            // Assert — clamp applied.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must clamp to 60.");

            // Assert — AtCap state derives correctly: GetExperienceThreshold at the clamped
            // Level=60 must return the CR-5.3 sentinel without throwing, exactly matching
            // AC-LS-25's dedicated at-cap sentinel test — proving LevelTierMultiplier/AtCap
            // derivation is correct via the same code path, not new logic.
            int threshold = 0;
            Assert.DoesNotThrow(() => threshold = leveling.GetExperienceThreshold(entity),
                "GetExperienceThreshold must not throw at the clamped Level=60.");
            Assert.AreEqual(int.MaxValue, threshold,
                "GetExperienceThreshold at the clamped Level=60 must return the CR-5.3 AtCap sentinel, proving AtCap state derives correctly from the clamped value.");
        }

        // ---------------------------------------------------------------
        // Code-review suggestions (qa-tester, Story 009): exact-boundary clamp cases were
        // untested — AC-LS-30/AC-LS-51's tests only exercised overshoot/undershoot values, never
        // the exact legal boundary. These pin the clamp comparison operators (< / >, not <= / >=)
        // against an off-by-one regression: a legal boundary value must produce NO clamp and NO
        // logged error.
        // ---------------------------------------------------------------

        [Test]
        public void RestoreLevelingState_LevelExactlyOne_NoClampNoError()
        {
            // Arrange — Level=1 is the lowest legal value; no clamp, no log.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 1);

            // Act — no LogAssert.Expect: an unexpected Debug.LogError fails this test by default.
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 0));

            // Assert — untouched.
            Assert.AreEqual(1, stats.GetBaseStat(entity, StatID.Level), "Level=1 is already legal — must not be rewritten.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints=0 at Level=1 (max=0) is already legal.");
        }

        [Test]
        public void RestoreLevelingState_HeldFreePointsExactlyZero_NoClampNoError()
        {
            // Arrange — Level=30 Warrior, heldFreePoints=0 is the lowest legal value.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 30);

            // Act — no LogAssert.Expect.
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 0));

            // Assert
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints=0 is always legal — must not clamp or log.");
        }

        [Test]
        public void RestoreLevelingState_HeldFreePointsExactlyAtMax_NoClampNoError()
        {
            // Arrange — L60 Warrior: max = (60-1)*1 = 59, the exact legal ceiling.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);

            // Act — no LogAssert.Expect.
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 59));

            // Assert
            Assert.AreEqual(59, leveling.GetHeldFreePoints(entity), "heldFreePoints=59 (the exact L60 Warrior max) is legal — must not clamp or log.");
        }

        [Test]
        public void RestoreLevelingState_UnregisteredClassType_HeldFreePointsClampsToZeroFallback()
        {
            // Arrange — entity registered as a player but RegisterPlayerClassType is deliberately
            // never called (simulating the doc-comment's flagged precondition gap): GetClassType
            // defaults to 0, which this registry never registered a ClassDefinition for, so
            // TryGetClass returns false and def falls back to a zero-valued struct
            // (FreePointsPerLevel=0) — the same fallback ExecuteLevelUpSequence/TryApplyRespec use.
            var xpThresholds = new int[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            // Deliberately no leveling.RegisterPlayerClassType(entity, ...) call.
            stats.SetBaseStat(entity, StatID.Level, 30);

            // maxHeld = (30-1) * 0 (unregistered fallback) = 0 — any positive value clamps to 0.
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*heldFreePoints=5.*clamped to 0"));

            // Act
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 5));

            // Assert
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity),
                "An unregistered classType must fall back to FreePointsPerLevel=0, clamping any positive heldFreePoints to 0.");
        }
    }
}
