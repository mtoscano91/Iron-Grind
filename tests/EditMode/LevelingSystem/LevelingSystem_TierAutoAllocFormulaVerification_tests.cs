using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 011 (Tier Multiplier &amp; Auto-Alloc Formula
    /// Verification — F-LS-3, F-LS-4). Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/>
    /// with a real <see cref="LevelingService"/>, following the established
    /// <c>CreateWiredStats</c>/<c>CreateClassRegistry</c> idiom from
    /// <c>LevelingSystem_LevelCapBehavior_tests.cs</c> (Story 008) and
    /// <c>LevelingSystem_FreePointAllocation_tests.cs</c> (Story 005) exactly.
    /// </summary>
    /// <remarks>
    /// <para>Covers AC-LS-34 (<see cref="LevelingService.GetLevelTierMultiplier"/> boundary
    /// values at L1/19/20/39/40/59/60, tested directly against the now-<c>internal</c> lookup
    /// method — see that method's own doc comment for why it was widened from <c>private</c> for
    /// this story), AC-LS-35 (Warrior L60 auto-alloc snapshot), and AC-LS-36 (Healer L60
    /// auto-alloc snapshot) — both driven via a single <c>AddExperience</c> call that cascades
    /// through 59 consecutive real level-ups (Story 002/003's CR-2.9 loop), with zero free-point
    /// spend, per the story's own Implementation Notes.</para>
    /// <para>Out of scope (per the story): the tier-transition-specific from-scratch recompute
    /// proof at each individual boundary is Story 004's job; the real <c>XpThreshold</c> table is
    /// Story 010's job (still Blocked) — the array built here is test-local only, matching every
    /// other story in this epic.</para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_TierAutoAllocFormulaVerification_Tests
    {
        private const byte WarriorClassType = 1;
        private const byte HealerClassType = 2;

        /// <summary>
        /// Builds the test-local class registry: Warrior (STR+2/DEX+1/VIT+1/INT+0, 1 free
        /// point/level) and Healer (STR+0/DEX+0/VIT+1/INT+2, 2 free points/level) — matching this
        /// epic's established worked-example values exactly (see
        /// <c>LevelingSystem_FreePointAllocation_tests.cs</c>, Story 005).
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
        /// Same ADR-010 wiring sequence as every other Leveling System story's tests.
        /// </summary>
        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds, CreateClassRegistry());
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        /// <summary>
        /// Test-local XP threshold table (index = level+1) that makes every threshold from
        /// L1 up to L60 trivially met by <c>Experience == 1</c>: indices [2]..[60] are all 0,
        /// so the CR-2.9 loop's <c>currentXp &lt; threshold</c> check (<c>1 &lt; 0</c>) is always
        /// false and the loop runs uninterrupted until its own <c>levelBefore &gt;= 60</c> break
        /// fires — 59 consecutive level-ups from a single <c>AddExperience(entity, 1)</c> call.
        /// Index [61] is set to the established CR-5.3 sentinel (<see cref="int.MaxValue"/>,
        /// Story 008) purely for this epic's defensive-sentinel convention — this cascade's own
        /// at-cap break means index 61 is never actually probed on this path.
        /// </summary>
        private static int[] BuildAllZeroXpThresholdsToCap()
        {
            var t = new int[62];
            for (int level = 2; level <= 60; level++)
                t[level] = 0;
            t[61] = int.MaxValue; // CR-5.3 sentinel convention — never actually reached here.
            return t;
        }

        // ---------------------------------------------------------------
        // AC-LS-34 — LevelTierMultiplier boundary values, tested directly against the pure
        // lookup function itself (no CharacterStats, no LevelingService instance needed), in
        // isolation from any level-up sequence.
        // ---------------------------------------------------------------

        [Test]
        public void GetLevelTierMultiplier_AllTierBoundaries_ReturnExactMultipliers()
        {
            Assert.AreEqual(1.0f, LevelingService.GetLevelTierMultiplier(1), "L1 must be tier x1.0.");
            Assert.AreEqual(1.0f, LevelingService.GetLevelTierMultiplier(19), "L19 must still be tier x1.0 (upper edge of the first band).");
            Assert.AreEqual(1.2f, LevelingService.GetLevelTierMultiplier(20), "L20 must be tier x1.2 (lower edge of the second band).");
            Assert.AreEqual(1.2f, LevelingService.GetLevelTierMultiplier(39), "L39 must still be tier x1.2 (upper edge of the second band).");
            Assert.AreEqual(1.5f, LevelingService.GetLevelTierMultiplier(40), "L40 must be tier x1.5 (lower edge of the third band).");
            Assert.AreEqual(1.5f, LevelingService.GetLevelTierMultiplier(59), "L59 must still be tier x1.5 (upper edge of the third band).");
            Assert.AreEqual(2.0f, LevelingService.GetLevelTierMultiplier(60), "L60 must be tier x2.0 (the cap tier).");
        }

        // ---------------------------------------------------------------
        // Code-review suggestion (qa-tester, Story 011): out-of-range inputs are not covered by
        // AC-LS-34's literal 7-value list, but every caller elsewhere in this codebase relies on
        // Level always being pre-clamped to [1,60] (see RestoreLevelingState, Story 009). This
        // test makes that assumption explicit: the lookup's simple >= chain degrades gracefully
        // outside the documented range rather than throwing, matching its unguarded/stateless
        // design (see the method's own doc comment).
        // ---------------------------------------------------------------

        [Test]
        public void GetLevelTierMultiplier_OutOfDocumentedRange_DegradesGracefully()
        {
            Assert.AreEqual(1.0f, LevelingService.GetLevelTierMultiplier(0), "Below the documented [1,60] range must fall through to the lowest tier, x1.0, not throw.");
            Assert.AreEqual(2.0f, LevelingService.GetLevelTierMultiplier(61), "Above the documented [1,60] range must still resolve to the cap tier, x2.0, not throw.");
        }

        // ---------------------------------------------------------------
        // AC-LS-35 — Warrior L60 auto-alloc snapshot: 59 real consecutive level-ups from L1,
        // driven by a SINGLE AddExperience call, zero free-point spend.
        // ---------------------------------------------------------------

        [Test]
        public void Warrior_DrivenFromL1ToL60ViaSingleAddExperienceCall_MatchesF_LS_4Snapshot()
        {
            // Arrange
            var xpThresholds = BuildAllZeroXpThresholdsToCap();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.InitializeAtL1(entity, WarriorClassType);

            // Act — a single AddExperience(1) call cascades through the full CR-2.9 loop: every
            // threshold from index [2] through [60] is 0, so "currentXp(1) < threshold(0)" is
            // always false, and the loop runs uninterrupted until its own "levelBefore >= 60"
            // break fires — 59 consecutive level-ups total.
            Assert.DoesNotThrow(() => stats.AddExperience(entity, 1),
                "A single AddExperience call driving 59 consecutive level-ups through Stories 002/003/009's machinery must not throw.");

            // Assert — sanity check the cascade actually reached the cap and stopped there.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must reach exactly 60 — the cascade must not stop early or run past the cap.");

            // Assert — F-LS-4 worked example: STR = 10 + 59*2 = 128, DEX = 10 + 59*1 = 69,
            // VIT = 10 + 59*1 = 69, INT = 10 (untouched, Warrior has 0 INT auto-alloc).
            Assert.AreEqual(128, stats.GetBaseStat(entity, StatID.Strength), "STR must be 10 + 59*2 = 128.");
            Assert.AreEqual(69, stats.GetBaseStat(entity, StatID.Dexterity), "DEX must be 10 + 59*1 = 69.");
            Assert.AreEqual(69, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 10 + 59*1 = 69.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "INT must remain 10 — Warrior has 0 INT auto-alloc.");

            // Assert — heldFreePoints accumulated with zero spend: 59 levels * 1/level = 59.
            Assert.AreEqual(59, leveling.GetHeldFreePoints(entity), "heldFreePoints must be 59*1=59 with zero free-point spend.");
        }

        // ---------------------------------------------------------------
        // AC-LS-36 — Healer L60 auto-alloc snapshot: same mechanism, different class definition.
        // ---------------------------------------------------------------

        [Test]
        public void Healer_DrivenFromL1ToL60ViaSingleAddExperienceCall_MatchesF_LS_4Snapshot()
        {
            // Arrange
            var xpThresholds = BuildAllZeroXpThresholdsToCap();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.InitializeAtL1(entity, HealerClassType);

            // Act — same single-call cascade mechanism as the Warrior test above.
            Assert.DoesNotThrow(() => stats.AddExperience(entity, 1),
                "A single AddExperience call driving 59 consecutive level-ups through Stories 002/003/009's machinery must not throw.");

            // Assert — sanity check the cascade actually reached the cap and stopped there.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must reach exactly 60 — the cascade must not stop early or run past the cap.");

            // Assert — F-LS-4 worked example: STR = DEX = 10 (untouched, Healer has 0 STR/DEX
            // auto-alloc), VIT = 10 + 59*1 = 69, INT = 10 + 59*2 = 128.
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Strength), "STR must remain 10 — Healer has 0 STR auto-alloc.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Dexterity), "DEX must remain 10 — Healer has 0 DEX auto-alloc.");
            Assert.AreEqual(69, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 10 + 59*1 = 69.");
            Assert.AreEqual(128, stats.GetBaseStat(entity, StatID.Intelligence), "INT must be 10 + 59*2 = 128.");

            // Assert — heldFreePoints accumulated with zero spend: 59 levels * 2/level = 118.
            Assert.AreEqual(118, leveling.GetHeldFreePoints(entity), "heldFreePoints must be 59*2=118 with zero free-point spend.");
        }
    }
}
