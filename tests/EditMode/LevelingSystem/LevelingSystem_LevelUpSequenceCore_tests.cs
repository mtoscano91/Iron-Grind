using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 002 (Level-Up Sequence Core — Guard, Increment,
    /// Auto-Alloc, Derived Stats, HP/MP Restore). Composes a real
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, following Story 001's integration-style idiom (never
    /// mocked). Auto-alloc data is supplied via a real <see cref="ClassRegistry"/> populated
    /// with test-local Warrior/Healer <see cref="ClassDefinition"/>s — the forward-dependency
    /// stand-in for the not-yet-built Class System epic.
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_LevelUpSequenceCore_Tests
    {
        private const byte WarriorClassType = 1;
        private const byte HealerClassType = 2;

        private sealed class StatChangeRecorder
        {
            public readonly List<(EntityID entityId, StatID statId)> Firings = new List<(EntityID, StatID)>();
            public void Handle(EntityID entityId, StatID statId) => Firings.Add((entityId, statId));
        }

        /// <summary>
        /// Builds the test-local class registry: Warrior (STR+2/DEX+1/VIT+1, 1 free point/level)
        /// and Healer (VIT+1/INT+2, 2 free points/level) — matching the story's worked examples.
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
        /// Same ADR-010 wiring sequence as Story 001's CreateWiredStats, extended with the
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
        // AC-LS-03 — sequence order (Level -> auto-alloc -> derived stats) and
        // correct tier (x1.2, not x1.0) for a L19->L20 transition.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL19ToL20_SequenceOrderAndTierOnePointTwo()
        {
            // Arrange — Warrior at L19: STR=46, DEX=28, VIT=28, INT=10.
            // Sized to 22, not 21: after L20 is reached, NotifyExperienceCrossedThreshold's CR-2.9
            // loop (added by Story 003, after this test was originally written) always performs one
            // more GetExperienceThreshold(level+1) lookup before stopping — index 21 must exist (a
            // high sentinel) or that lookup throws ArgumentOutOfRangeException. Found and fixed
            // during Story 004's code review (unity-specialist flagged this as a latent regression
            // in already-closed Story 002 work, introduced by Story 003's later loop addition).
            var xpThresholds = new int[22];
            xpThresholds[20] = 300; // level 19 -> 20 threshold
            xpThresholds[21] = 999999; // sentinel — CR-2.9's post-L20 re-check must not cross this
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 19);
            stats.SetBaseStat(entity, StatID.Strength, 46);
            stats.SetBaseStat(entity, StatID.Dexterity, 28);
            stats.SetBaseStat(entity, StatID.Vitality, 28);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            // Act — crosses the L19->L20 threshold immediately.
            stats.AddExperience(entity, 500);

            // Assert — recorder.Firings[0] is (entity, Experience): AddExperience's own XP write
            // fires before NotifyExperienceCrossedThreshold is even called; it precedes the CR-2
            // sequence entirely and is not part of it. The CR-2 sequence itself starts at index 1.
            Assert.AreEqual(14, recorder.Firings.Count,
                "Expected 1 Experience firing + 13 CR-2 sequence firings (Level, 3 auto-alloc, 7 derived stats, CurrentHP, CurrentMP).");
            Assert.AreEqual((entity, StatID.Experience), recorder.Firings[0],
                "AddExperience's own Experience write precedes the CR-2 sequence.");

            var expectedSequence = new[]
            {
                StatID.Level,
                StatID.Strength, StatID.Dexterity, StatID.Vitality,
                StatID.MaxHP, StatID.MaxMP, StatID.AttackPower, StatID.Defense,
                StatID.MagicDefense, StatID.CritChance, StatID.AttackSpeedMultiplier,
                StatID.CurrentHP, StatID.CurrentMP,
            };
            for (int i = 0; i < expectedSequence.Length; i++)
            {
                Assert.AreEqual((entity, expectedSequence[i]), recorder.Firings[i + 1],
                    $"CR-2 sequence firing #{i} must be {expectedSequence[i]} — Level first, then auto-alloc (STR/DEX/VIT), then derived stats in fixed order.");
            }

            bool intelligenceFired = false;
            foreach (var firing in recorder.Firings)
                if (firing.statId == StatID.Intelligence)
                    intelligenceFired = true;
            Assert.IsFalse(intelligenceFired,
                "Intelligence must never fire — Warrior does not auto-allocate it and CR-2.6 only reads it.");

            // Tier proof: AttackPower must reflect x1.2 (127), not x1.0 (106), for the NEW level (20).
            Assert.AreEqual(48, stats.GetBaseStat(entity, StatID.Strength), "STR must be 46+2=48 post auto-alloc.");
            Assert.AreEqual(127, stats.GetBaseStat(entity, StatID.AttackPower),
                "AttackPower = FloorToInt((10+48*2)*1.2) = 127 — proves tier x1.2 was used for the L19->L20 transition, not x1.0 (106).");
        }

        // ---------------------------------------------------------------
        // AC-LS-04 — Warrior L1->L2 auto-alloc order and values; Intelligence untouched.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL1ToL2_AutoAllocOrderAndIntelligenceUntouched()
        {
            // Arrange — Warrior L1, STR=DEX=VIT=INT=10.
            // Sized with a trailing sentinel: after L2 is reached, the CR-2.9 loop (Story 003)
            // always performs one more GetExperienceThreshold(3) lookup before stopping — index 3
            // must exist or that lookup throws ArgumentOutOfRangeException.
            var xpThresholds = new int[] { 0, 0, 200, 999999 }; // level 1 -> index 2; index 3 sentinel
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 1);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            // Act
            stats.AddExperience(entity, 250);

            // Assert — Strength(+2) -> Dexterity(+1) -> Vitality(+1), in that exact order,
            // immediately after the Level write (recorder.Firings[0] is the pre-sequence
            // Experience write; [1] is Level; [2..4] are the auto-alloc writes).
            Assert.AreEqual((entity, StatID.Level), recorder.Firings[1]);
            Assert.AreEqual((entity, StatID.Strength), recorder.Firings[2]);
            Assert.AreEqual((entity, StatID.Dexterity), recorder.Firings[3]);
            Assert.AreEqual((entity, StatID.Vitality), recorder.Firings[4]);

            Assert.AreEqual(12, stats.GetBaseStat(entity, StatID.Strength), "STR must become 10+2=12.");
            Assert.AreEqual(11, stats.GetBaseStat(entity, StatID.Dexterity), "DEX must become 10+1=11.");
            Assert.AreEqual(11, stats.GetBaseStat(entity, StatID.Vitality), "VIT must become 10+1=11.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "INT must remain untouched at 10.");

            foreach (var firing in recorder.Firings)
                Assert.AreNotEqual(StatID.Intelligence, firing.statId,
                    "Intelligence must never fire OnStatChanged — Warrior does not auto-allocate it.");
        }

        // ---------------------------------------------------------------
        // AC-LS-05 — Healer L1->L2 auto-alloc order and values; STR/DEX untouched;
        // heldFreePoints increments by 2.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_HealerL1ToL2_AutoAllocOrderAndHeldFreePointsIncrement()
        {
            // Arrange — Healer L1, VIT=INT=10 (STR/DEX left at default 0 — irrelevant to a Healer).
            // Sized with a trailing sentinel: after L2 is reached, the CR-2.9 loop (Story 003)
            // always performs one more GetExperienceThreshold(3) lookup before stopping — index 3
            // must exist or that lookup throws ArgumentOutOfRangeException.
            var xpThresholds = new int[] { 0, 0, 200, 999999 }; // level 1 -> index 2; index 3 sentinel
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, HealerClassType);

            stats.SetBaseStat(entity, StatID.Level, 1);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must start at 0.");

            // Act
            stats.AddExperience(entity, 250);

            // Assert — Vitality(+1) -> Intelligence(+2), in that exact order, immediately after
            // the Level write ([0] is the pre-sequence Experience write, [1] is Level).
            Assert.AreEqual((entity, StatID.Level), recorder.Firings[1]);
            Assert.AreEqual((entity, StatID.Vitality), recorder.Firings[2]);
            Assert.AreEqual((entity, StatID.Intelligence), recorder.Firings[3]);

            Assert.AreEqual(11, stats.GetBaseStat(entity, StatID.Vitality), "VIT must become 10+1=11.");
            Assert.AreEqual(12, stats.GetBaseStat(entity, StatID.Intelligence), "INT must become 10+2=12.");
            Assert.AreEqual(0, stats.GetBaseStat(entity, StatID.Strength), "STR must remain untouched.");
            Assert.AreEqual(0, stats.GetBaseStat(entity, StatID.Dexterity), "DEX must remain untouched.");

            foreach (var firing in recorder.Firings)
            {
                Assert.AreNotEqual(StatID.Strength, firing.statId, "Strength must never fire — Healer does not auto-allocate it.");
                Assert.AreNotEqual(StatID.Dexterity, firing.statId, "Dexterity must never fire — Healer does not auto-allocate it.");
            }

            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity),
                "heldFreePoints must increment by the Healer's FreePointsPerLevel (2).");
        }

        // ---------------------------------------------------------------
        // AC-LS-06 — Full HP/MP restore uses the freshly-written MaxHP/MaxMP from THIS
        // sequence, not the pre-level-up value (CR-2.6 -> CR-2.7 worked example).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorL9ToL10MidCombat_CurrentHpMpRestoredToFreshMaxValues()
        {
            // Arrange — Warrior at L9, mid-combat: VIT=18 (MaxHP_old=560), CurrentHP=50;
            // INT=10 (MaxMP_old=220), CurrentMP=30.
            // Sized to 12, not 11: after L10 is reached, the CR-2.9 loop (Story 003) always
            // performs one more GetExperienceThreshold(11) lookup before stopping — index 11 must
            // exist (a high sentinel) or that lookup throws ArgumentOutOfRangeException.
            var xpThresholds = new int[12];
            xpThresholds[10] = 300; // level 9 -> 10 threshold
            xpThresholds[11] = 999999; // sentinel — CR-2.9's post-L10 re-check must not cross this
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 9);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 18);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);
            stats.SetBaseStat(entity, StatID.MaxHP, 560);
            stats.SetBaseStat(entity, StatID.MaxMP, 220);
            stats.ApplyRegen(entity, 50f);   // seeds CurrentHP = 50, mid-combat, via the real gameplay path
            stats.ApplyManaRegen(entity, 30f); // seeds CurrentMP = 30

            Assert.AreEqual(50f, stats.GetCurrentHP(entity), "Precondition: CurrentHP must be 50 before leveling.");
            Assert.AreEqual(30f, stats.GetCurrentMP(entity), "Precondition: CurrentMP must be 30 before leveling.");

            // Act — crosses the L9->L10 threshold; Vitality auto-alloc 18->19; tier stays x1.0 (level 10 < 20).
            stats.AddExperience(entity, 500);

            // Assert — MaxHP_new = FloorToInt((200+19*20)*1.0) = 580 (matches the story's worked example).
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality), "VIT must become 18+1=19.");
            Assert.AreEqual(580, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must be recomputed to 580.");
            Assert.AreEqual(580f, stats.GetCurrentHP(entity),
                "CurrentHP must be restored to the FRESH MaxHP=580 — not 50 (pre-level-up) or 560 (stale MaxHP).");

            Assert.AreEqual(220, stats.GetBaseStat(entity, StatID.MaxMP), "MaxMP is unchanged (INT untouched by Warrior auto-alloc).");
            Assert.AreEqual(220f, stats.GetCurrentMP(entity),
                "CurrentMP must be restored to the FRESH MaxMP=220 — not 30 (pre-level-up).");
        }
    }
}
