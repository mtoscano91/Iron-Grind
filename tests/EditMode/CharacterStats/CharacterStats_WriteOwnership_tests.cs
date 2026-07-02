using System.Collections.Generic;
using IronGrind.CharacterStats;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// Story 006 — Write Ownership: ILevelingService injection, mob guard, write-locked stat rejection.
    ///
    /// Covers:
    ///   AC-14  AddExperience is a no-op for mob entities; ILevelingService receives zero calls.
    ///   NEW AC AddExperience accumulates XP for player entities and fires exactly one
    ///          notification when the accumulated total reaches or exceeds the threshold.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterStats_WriteOwnership_Tests
    {
        // -----------------------------------------------------------------------
        // Mock ILevelingService — private to this file, not shared via TestHelpers.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Configurable mock: treats PlayerEntityId as a player, all others as mobs.
        /// Records every call to NotifyExperienceCrossedThreshold so tests can assert
        /// exact call counts and which entity IDs were notified.
        /// </summary>
        private sealed class MockLevelingService : ILevelingService
        {
            private readonly int _threshold;

            /// <summary>Total number of calls received by NotifyExperienceCrossedThreshold.</summary>
            public int NotificationCount { get; private set; }

            /// <summary>Ordered list of entity IDs passed to NotifyExperienceCrossedThreshold.</summary>
            public List<EntityID> NotifiedEntities { get; } = new List<EntityID>();

            /// <param name="threshold">XP threshold at which a level-up notification fires. Defaults to 1500.</param>
            public MockLevelingService(int threshold = 1500)
            {
                _threshold = threshold;
            }

            public bool IsPlayerEntity(EntityID entityId)
                => entityId == CharacterStatsFixture.PlayerEntityId;

            public int GetExperienceThreshold(EntityID entityId) => _threshold;

            public void NotifyExperienceCrossedThreshold(EntityID entityId)
            {
                NotificationCount++;
                NotifiedEntities.Add(entityId);
            }
        }

        // -----------------------------------------------------------------------
        // AC-14: AddExperience is a no-op for mob entities.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_MobEntity_IsNoOp()
        {
            // Arrange
            var mock  = new MockLevelingService();
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.MobEntityId, 500);

            // Assert
            Assert.AreEqual(0, stats.GetBaseStat(CharacterStatsFixture.MobEntityId, StatID.Experience),
                "Experience must remain 0 for mob entities.");
            Assert.AreEqual(0, mock.NotificationCount,
                "ILevelingService must receive zero notification calls for a mob entity.");
        }

        [Test]
        public void CharacterStats_AddExperience_MobEntity_ZeroAmount_IsNoOp()
        {
            // Arrange
            var mock  = new MockLevelingService();
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.MobEntityId, 0);

            // Assert
            Assert.AreEqual(0, stats.GetBaseStat(CharacterStatsFixture.MobEntityId, StatID.Experience),
                "Experience must remain 0 when zero XP is awarded to a mob entity.");
            Assert.AreEqual(0, mock.NotificationCount,
                "ILevelingService must receive zero notification calls.");
        }

        [Test]
        public void CharacterStats_AddExperience_MobEntity_LargeAmount_IsNoOp()
        {
            // Arrange
            var mock  = new MockLevelingService();
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.MobEntityId, int.MaxValue);

            // Assert
            Assert.AreEqual(0, stats.GetBaseStat(CharacterStatsFixture.MobEntityId, StatID.Experience),
                "Experience must remain 0 for mob entities even with a very large XP amount.");
            Assert.AreEqual(0, mock.NotificationCount,
                "ILevelingService must receive zero notification calls.");
        }

        [Test]
        public void CharacterStats_AddExperience_PlayerEntity_ZeroAmount_IsNoOp()
        {
            // Arrange
            var mock  = new MockLevelingService();
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 0);

            // Assert
            Assert.AreEqual(0, stats.GetBaseStat(CharacterStatsFixture.PlayerEntityId, StatID.Experience),
                "Experience must remain 0 when zero XP is awarded to a player entity.");
            Assert.AreEqual(0, mock.NotificationCount,
                "ILevelingService must receive zero notification calls when amount is 0.");
        }

        // -----------------------------------------------------------------------
        // NEW AC: AddExperience accumulates XP and notifies when threshold is crossed.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_PlayerEntity_BelowThreshold_AccumulatesNoNotification()
        {
            // Arrange — threshold is 1500; adding 1000 stays below it.
            var mock  = new MockLevelingService(threshold: 1500);
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 1000);

            // Assert
            Assert.AreEqual(1000, stats.GetBaseStat(CharacterStatsFixture.PlayerEntityId, StatID.Experience),
                "Experience must be 1000 after a single below-threshold award.");
            Assert.AreEqual(0, mock.NotificationCount,
                "ILevelingService must receive zero notification calls while below threshold.");
        }

        [Test]
        public void CharacterStats_AddExperience_PlayerEntity_CrossesThreshold_NotifiesOnce()
        {
            // Arrange — threshold is 1500; first call takes XP to 1000 (below), second to 1600 (crosses).
            var mock  = new MockLevelingService(threshold: 1500);
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 1000);

            // Precondition: still below threshold, no notification yet.
            Assert.AreEqual(0, mock.NotificationCount, "No notification expected before threshold is crossed.");

            // Act
            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 600);

            // Assert
            Assert.AreEqual(1600, stats.GetBaseStat(CharacterStatsFixture.PlayerEntityId, StatID.Experience),
                "Experience must be 1600 after the two awards.");
            Assert.AreEqual(1, mock.NotificationCount,
                "ILevelingService must receive exactly one notification call when the threshold is first crossed.");
            Assert.AreEqual(CharacterStatsFixture.PlayerEntityId, mock.NotifiedEntities[0],
                "The notified entity ID must be the player entity.");
        }

        [Test]
        public void CharacterStats_AddExperience_PlayerEntity_Accumulates_NotOverwrites()
        {
            // Arrange — two consecutive awards; verify XP is summed, not overwritten.
            // Threshold is int.MaxValue so no notification fires (not the focus of this test).
            var mock  = new MockLevelingService(threshold: int.MaxValue);
            var stats = CharacterStatsFixture.CreateWithLeveling(mock);

            // Act
            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 300);
            stats.AddExperience(CharacterStatsFixture.PlayerEntityId, 700);

            // Assert
            Assert.AreEqual(1000, stats.GetBaseStat(CharacterStatsFixture.PlayerEntityId, StatID.Experience),
                "Consecutive AddExperience calls must accumulate (300 + 700 = 1000), not overwrite.");
            Assert.AreEqual(0, mock.NotificationCount,
                "No notification expected when accumulated XP stays below int.MaxValue threshold.");
        }
    }
}
