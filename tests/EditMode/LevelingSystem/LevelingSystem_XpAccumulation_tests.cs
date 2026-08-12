using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 001 (XP Accumulation &amp; Threshold-Crossed
    /// Notification). Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/> with
    /// a real <see cref="LevelingService"/> — <c>AddExperience</c> is already-built code under
    /// test just as much as <see cref="LevelingService"/> is (per the story's Implementation
    /// Notes: this is deliberately integration-style, not a mock of CharacterStats).
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_XpAccumulation_Tests
    {
        private sealed class StatChangeRecorder
        {
            public readonly List<(EntityID entityId, StatID statId)> Firings = new List<(EntityID, StatID)>();
            public void Handle(EntityID entityId, StatID statId) => Firings.Add((entityId, statId));
        }

        /// <summary>
        /// Performs the full ADR-010 wiring sequence required to break the CharacterStats
        /// &lt;-&gt; ILevelingService constructor cycle (see <see cref="LevelingService"/> class
        /// remarks): construct LevelingService, construct CharacterStats with it, then attach
        /// the CharacterStats back-reference.
        /// </summary>
        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds);
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        // ---------------------------------------------------------------
        // AC-LS-01
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_BelowThreshold_UpdatesExperienceFiresOnceNoNotification()
        {
            // Arrange — Warrior at L5, Experience=5000; threshold at index [6] set high enough
            // that 5500 doesn't cross it.
            var xpThresholds = new int[] { 0, 100, 200, 300, 400, 500, 100000 }; // index = level+1
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Level, 5);
            stats.SetBaseStat(entity, StatID.Experience, 5000);

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            // Act
            stats.AddExperience(entity, 500);

            // Assert
            Assert.AreEqual(5500, stats.GetBaseStat(entity, StatID.Experience),
                "Experience must be 5500 after adding 500 to a base of 5000.");
            Assert.AreEqual(1, recorder.Firings.Count,
                "OnStatChanged must fire exactly once for this AddExperience call.");
            Assert.AreEqual((entity, StatID.Experience), recorder.Firings[0],
                "The single OnStatChanged firing must be for (entity, StatID.Experience).");
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must not be called — 5500 has not reached the 100000 threshold.");
        }

        // ---------------------------------------------------------------
        // AC-LS-40 (relaxed — no log assertion; see story Implementation Notes / TD-031)
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_ZeroThenNegativeAmount_ExperienceUnchangedNoEvents()
        {
            // Arrange
            var xpThresholds = new int[] { 0, 100, 200, 300, 400, 500, 100000 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Experience, 3000);

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            // Act
            stats.AddExperience(entity, 0);
            stats.AddExperience(entity, -100);

            // Assert
            Assert.AreEqual(3000, stats.GetBaseStat(entity, StatID.Experience),
                "Experience must remain 3000 — zero and negative amounts must not write.");
            Assert.AreEqual(0, recorder.Firings.Count,
                "OnStatChanged must never fire for zero or negative AddExperience amounts.");
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must never be called for zero or negative amounts.");
        }

        // ---------------------------------------------------------------
        // EC-LS-08
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_MobEntity_NoWriteNoNotification()
        {
            // Arrange — MobEntityId is never registered via RegisterPlayerEntity, so
            // IsPlayerEntity returns false for it.
            var xpThresholds = new int[] { 0, 100, 200, 300, 400, 500, 100000 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var mob = CharacterStatsFixture.MobEntityId;

            var recorder = new StatChangeRecorder();
            stats.Subscribe(recorder.Handle);

            // Act
            stats.AddExperience(mob, 500);

            // Assert
            Assert.AreEqual(0, stats.GetBaseStat(mob, StatID.Experience),
                "Experience must remain 0 — AddExperience must no-op for a mob entity.");
            Assert.AreEqual(0, recorder.Firings.Count,
                "OnStatChanged must never fire for a mob entity.");
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must never be called for a mob entity.");
        }

        // ---------------------------------------------------------------
        // Threshold-crossed observability (story deliverable #3): proves
        // NotifyExperienceCrossedThreshold is reachable, callable, and observable.
        // Story 002 fills in the real level-up sequence this stub hands off to.
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_CrossesThreshold_NotifiesOnceWithEntityId()
        {
            // Arrange — Level 5 -> threshold index [6] = 5400; 5000 + 500 = 5500 crosses it.
            // Sized with a trailing sentinel: after L6 is reached, the CR-2.9 loop (Story 003)
            // always performs one more GetExperienceThreshold(7) lookup before stopping — index 7
            // must exist or that lookup throws ArgumentOutOfRangeException.
            var xpThresholds = new int[] { 0, 100, 200, 300, 400, 500, 5400, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            stats.SetBaseStat(entity, StatID.Level, 5);
            stats.SetBaseStat(entity, StatID.Experience, 5000);

            // Act
            stats.AddExperience(entity, 500);

            // Assert
            Assert.AreEqual(1, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must be called exactly once when the threshold is crossed.");
            Assert.AreEqual(entity, leveling.LastNotifiedEntityId,
                "NotifyExperienceCrossedThreshold must be called with the entity that crossed the threshold.");
        }

        // ---------------------------------------------------------------
        // LevelingService unit-level coverage: indexing formula, AttachCharacterStats
        // precondition, and the player-registry seam.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_GetExperienceThreshold_ReturnsTableEntryAtLevelPlusOne()
        {
            // Arrange
            var xpThresholds = new int[] { 10, 20, 30, 40 }; // indices 0..3
            var leveling = new LevelingService(xpThresholds);
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            var entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.Level, 2); // level 2 -> index 3
            leveling.AttachCharacterStats(stats);

            // Act
            int threshold = leveling.GetExperienceThreshold(entity);

            // Assert
            Assert.AreEqual(40, threshold,
                "GetExperienceThreshold must return xpThresholds[level + 1] (level 2 -> index 3 -> 40).");
        }

        [Test]
        public void LevelingService_GetExperienceThreshold_BeforeAttachCharacterStats_Throws()
        {
            // Arrange
            var leveling = new LevelingService(new int[] { 10, 20 });

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => leveling.GetExperienceThreshold(CharacterStatsFixture.PlayerEntityId),
                "GetExperienceThreshold must throw if called before AttachCharacterStats wires the back-reference.");
        }

        [Test]
        public void LevelingService_RegisterAndUnregisterPlayerEntity_TogglesIsPlayerEntity()
        {
            // Arrange
            var leveling = new LevelingService(new int[] { 0 });
            var entity = CharacterStatsFixture.PlayerEntityId;

            // Act / Assert — unregistered by default
            Assert.IsFalse(leveling.IsPlayerEntity(entity),
                "An entity must not be treated as a player before RegisterPlayerEntity is called.");

            leveling.RegisterPlayerEntity(entity);
            Assert.IsTrue(leveling.IsPlayerEntity(entity),
                "IsPlayerEntity must return true after RegisterPlayerEntity.");

            leveling.UnregisterPlayerEntity(entity);
            Assert.IsFalse(leveling.IsPlayerEntity(entity),
                "IsPlayerEntity must return false again after UnregisterPlayerEntity.");
        }
    }
}
