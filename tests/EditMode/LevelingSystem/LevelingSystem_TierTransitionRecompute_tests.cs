using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 004 (Tier Transition From-Scratch Recompute &amp;
    /// Raw-Write Ceilings). Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/>
    /// with a real <see cref="LevelingService"/>, following Story 001/002/003's integration-style
    /// idiom (never mocked). Auto-alloc data is supplied via a real <see cref="ClassRegistry"/>
    /// populated with the same test-local Warrior <see cref="ClassDefinition"/> Story 002/003 use,
    /// matching <c>LevelingSystem_LevelUpSequenceCore_tests.cs</c>'s established idiom exactly.
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_TierTransitionRecompute_Tests
    {
        private const byte WarriorClassType = 1;

        /// <summary>
        /// Builds the test-local class registry: Warrior only (STR+2/DEX+1/VIT+1, 1 free
        /// point/level) — matching Story 002/003's worked examples. AC-LS-42/43 deliberately do
        /// NOT register a class for their entity so CR-2.4 auto-alloc silently no-ops, leaving
        /// their injected stat-ceiling values (DEX=467, INT=420) exactly as injected through the
        /// recompute.
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

        /// <summary>
        /// Same ADR-010 wiring sequence as Story 001/002/003's CreateWiredStats.
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
        // AC-LS-38 — L19->L20 tier transition: MaxHP computed from scratch with post-auto-alloc
        // totals x1.2, not as a delta from the L19 value.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL19ToL20_MaxHpComputedFromScratchAtTierOnePointTwo()
        {
            // Arrange — Warrior at L19: STR=46, VIT=28, DEX=28 pre-auto-alloc (AC-LS-38 worked example).
            // Sized to 22, not 21: after L20 is reached, NotifyExperienceCrossedThreshold's CR-2.9
            // loop (Story 003) always performs one more GetExperienceThreshold(level+1) lookup
            // before stopping — index 21 must exist (a high sentinel) or that lookup throws
            // ArgumentOutOfRangeException, even though no further level-up is expected to fire.
            var xpThresholds = new int[22];
            xpThresholds[20] = 300; // level 19 -> 20 threshold
            xpThresholds[21] = 999999; // sentinel — CR-2.9's post-L20 re-check must not cross this
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 19);
            stats.SetBaseStat(entity, StatID.Strength, 46);
            stats.SetBaseStat(entity, StatID.Vitality, 28);
            stats.SetBaseStat(entity, StatID.Dexterity, 28);

            // Act — crosses the L19->L20 threshold; Warrior auto-alloc: STR+2, DEX+1, VIT+1.
            stats.AddExperience(entity, 300);

            // Assert — post-auto-alloc VIT=29; MaxHP = FloorToInt((200+29*20)*1.2) = 936,
            // computed from scratch (fresh GetBaseStat reads inside CR-2.5/CR-2.6), never as a
            // delta from the pre-level-up MaxHP value.
            Assert.AreEqual(20, stats.GetBaseStat(entity, StatID.Level), "Level must reach 20.");
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 28+1=29 post-auto-alloc.");
            Assert.AreEqual(936, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+29*20)*1.2)=936 — from-scratch recompute at the L20 tier boundary (x1.2).");
        }

        // ---------------------------------------------------------------
        // AC-LS-53 — L39->L40 tier transition: same from-scratch proof at x1.5.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL39ToL40_MaxHpComputedFromScratchAtTierOnePointFive()
        {
            // Arrange — Warrior at L39: STR=86, VIT=48, DEX=48 pre-auto-alloc (AC-LS-53 worked example).
            // Sized to 42, not 41 — same CR-2.9 post-level-up re-check reasoning as AC-LS-38 above.
            var xpThresholds = new int[42];
            xpThresholds[40] = 300; // level 39 -> 40 threshold
            xpThresholds[41] = 999999; // sentinel — CR-2.9's post-L40 re-check must not cross this
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 39);
            stats.SetBaseStat(entity, StatID.Strength, 86);
            stats.SetBaseStat(entity, StatID.Vitality, 48);
            stats.SetBaseStat(entity, StatID.Dexterity, 48);

            // Act — crosses the L39->L40 threshold; Warrior auto-alloc: STR+2, DEX+1, VIT+1.
            stats.AddExperience(entity, 300);

            // Assert — post-auto-alloc VIT=49; MaxHP = FloorToInt((200+49*20)*1.5) = 1770,
            // computed from scratch at this specific tier boundary (x1.5), independently of the
            // L20 boundary's x1.2 proof above — the two transitions are not interchangeable.
            Assert.AreEqual(40, stats.GetBaseStat(entity, StatID.Level), "Level must reach 40.");
            Assert.AreEqual(49, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 48+1=49 post-auto-alloc.");
            Assert.AreEqual(1770, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+49*20)*1.5)=1770 — from-scratch recompute at the L40 tier boundary (x1.5).");
        }

        // ---------------------------------------------------------------
        // AC-LS-54 — L59->L60 tier transition: CR-2.2a XP clamp fires (Experience ==
        // XpThreshold[60] exactly, overshoot absorbed) before the x2.0 from-scratch recompute.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL59ToL60_XpClampFiresBeforeFromScratchRecomputeAtTierTwoPointZero()
        {
            // Arrange — Warrior at L59: STR=126, VIT=68, DEX=68 (AC-LS-54 worked example).
            // xpThresholds[60] is both the crossing threshold AND the CR-2.2a clamp target.
            var xpThresholds = new int[61];
            xpThresholds[60] = 5000;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 59);
            stats.SetBaseStat(entity, StatID.Strength, 126);
            stats.SetBaseStat(entity, StatID.Vitality, 68);
            stats.SetBaseStat(entity, StatID.Dexterity, 68);

            // Act — a single grant that OVERSHOOTS the L60 threshold by 300 XP (5300 > 5000).
            stats.AddExperience(entity, 5300);

            // Assert — CR-2.2a must clamp the overshoot to exactly xpThresholds[60], absorbing
            // it within this same level-up call, before any further step (auto-alloc,
            // derived-stat recompute) runs.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must reach the cap, 60.");
            Assert.AreEqual(5000, stats.GetBaseStat(entity, StatID.Experience),
                "CR-2.2a must clamp Experience to exactly xpThresholds[60]=5000 — the 300 XP overshoot is absorbed, not retained.");

            // Post-auto-alloc VIT=69; MaxHP = FloorToInt((200+69*20)*2.0) = 3160, computed from
            // scratch AFTER the CR-2.2a clamp (not before it, and not as a delta from L59's MaxHP).
            Assert.AreEqual(69, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 68+1=69 post-auto-alloc.");
            Assert.AreEqual(3160, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+69*20)*2.0)=3160 — from-scratch recompute at the L60 tier boundary (x2.0), after the XP clamp.");
        }

        // ---------------------------------------------------------------
        // AC-LS-42 — CritChance/AttackSpeedMultiplier are written RAW (unclamped) via
        // SetBaseStatFloat even when the formula output exceeds GetEffectiveStatFloat's
        // query-time clamp ceiling. Uses stat injection (DEX=467, unreachable in normal play).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_DexInjected467_CritChanceWrittenRawAboveEffectiveCap()
        {
            // Arrange — DEX injected to 467 via direct SetBaseStat (test-only seam; unreachable
            // in normal play under current MVP class stat caps — see AC-LS-42/EC-LS-27). No
            // class is registered for this entity, so CR-2.4 auto-alloc silently no-ops,
            // leaving DEX at exactly 467 through the recompute (a registered class's DEX
            // auto-alloc would otherwise perturb this exact injected value by +1).
            var xpThresholds = new int[61];
            xpThresholds[60] = 5000;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Level, 59);
            stats.SetBaseStat(entity, StatID.Dexterity, 467);

            // Act — trigger a recompute via a real level-up call (L59->L60, tier x2.0 — the
            // tier this AC's worked example uses to reach the injected value's formula output).
            stats.AddExperience(entity, 5000);

            // Assert — CritChance is written RAW (unclamped): 0.05 + (467*0.0015*2.0) = 1.451f.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach 60.");
            Assert.AreEqual(467, stats.GetBaseStat(entity, StatID.Dexterity),
                "Precondition: DEX must remain exactly 467 — no class registered, so CR-2.4 auto-alloc must not have perturbed it.");
            Assert.AreEqual(1.451f, stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-5f,
                "GetBaseStatFloat must return the RAW formula output 1.451f — the Leveling System never pre-clamps CritChance/AttackSpeedMultiplier (EC-LS-27).");
            Assert.AreEqual(0.75f, stats.GetEffectiveStatFloat(entity, StatID.CritChance), 1e-5f,
                "GetEffectiveStatFloat must clamp to StatMax=0.75 at QUERY time only — the raw base value on disk is unaffected.");
        }

        // ---------------------------------------------------------------
        // AC-LS-43 — MaxMP IS clamped to <=9,999 by the Leveling System before SetBaseStat —
        // the one derived stat with a hard write-side ceiling. Uses stat injection (INT=420,
        // unreachable in normal play).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_IntInjected420_MaxMpClampedTo9999AtWriteTime()
        {
            // Arrange — INT injected to 420 via direct SetBaseStat (test-only seam; unreachable
            // in normal play — max INT for Healer is 246, see AC-LS-43/EC-LS-27). No class
            // registered, so CR-2.4 auto-alloc silently no-ops, leaving INT at exactly 420
            // through the recompute.
            var xpThresholds = new int[61];
            xpThresholds[60] = 5000;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Level, 59);
            stats.SetBaseStat(entity, StatID.Intelligence, 420);

            // Act — trigger a recompute via a real level-up call (L59->L60, tier x2.0).
            stats.AddExperience(entity, 5000);

            // Assert — raw F-4 output FloorToInt((100+420*12)*2.0)=10280 exceeds the 9,999
            // schema ceiling; the Leveling System clamps min(raw,9999) BEFORE calling
            // SetBaseStat — contrast with CritChance/AttackSpeedMultiplier (AC-LS-42), which
            // are never pre-clamped.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach 60.");
            Assert.AreEqual(420, stats.GetBaseStat(entity, StatID.Intelligence),
                "Precondition: INT must remain exactly 420 — no class registered, so CR-2.4 auto-alloc must not have perturbed it.");
            Assert.AreEqual(9999, stats.GetBaseStat(entity, StatID.MaxMP),
                "GetBaseStat(MaxMP) must read the CLAMPED value 9999, not the raw FloorToInt((100+420*12)*2.0)=10280 — the one derived stat with a write-side hard ceiling (contrast AC-LS-42).");
        }
    }
}
