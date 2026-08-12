using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 005 (Free Point Allocation — CR-3). Composes a
    /// real <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, following Stories 001-003's integration-style idiom (never
    /// mocked). Since <c>heldFreePoints</c> has no public setter, tests that need a nonzero
    /// starting balance drive it through a real level-up via <c>AddExperience</c> (Warrior grants
    /// 1 free point/level, Healer grants 2), matching the Story 002/003 test idiom exactly —
    /// including the trailing-sentinel entry one index past the target level in every
    /// <c>xpThresholds</c> array that triggers a real level-up (the CR-2.9 loop always performs
    /// one more <c>GetExperienceThreshold</c> lookup after a successful level-up).
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_FreePointAllocation_Tests
    {
        private const byte WarriorClassType = 1;
        private const byte HealerClassType = 2;

        /// <summary>
        /// Builds the test-local class registry: Warrior (STR+2/DEX+1/VIT+1, 1 free point/level)
        /// and Healer (VIT+1/INT+2, 2 free points/level) — matching Stories 002/003's worked
        /// examples exactly.
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
        /// Same ADR-010 wiring sequence as Stories 001-003's CreateWiredStats, extended with the
        /// Warrior/Healer class registry the level-up setup steps below need.
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
        // AC-LS-11 — valid allocation: heldFreePoints decrements, target stat +1, F-3-F-9
        // re-derived at current tier, CurrentHP/CurrentMP untouched.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_ValidAllocation_DecrementsHeldPointsIncrementsStatAndRecomputesAttackPower()
        {
            // Arrange — Warrior L8, STR=24. Two real level-ups (L8->L9->L10) via Warrior
            // auto-alloc (STR+2/level) reach STR=28, heldFreePoints=2 (1/level x 2), matching the
            // story's worked example precondition. index9=L8->L9, index10=L9->L10, index11
            // sentinel (CR-2.9's post-L10 re-check must not cross it).
            var xpThresholds = new int[12];
            xpThresholds[9] = 100;
            xpThresholds[10] = 200;
            xpThresholds[11] = 999999;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 8);
            stats.SetBaseStat(entity, StatID.Strength, 24);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 200); // L8 -> L9 -> L10.

            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach 10.");
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength), "Precondition: STR must be 24+2+2=28.");
            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 1+1=2.");

            float hpBefore = stats.GetCurrentHP(entity);
            float mpBefore = stats.GetCurrentMP(entity);

            // Act
            var result = leveling.AllocateFreePoint(entity, StatID.Strength);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.Success, result, "A valid allocation must succeed.");
            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "heldFreePoints must decrement from 2 to 1.");
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Strength), "Strength must increment by 1 (28->29).");

            // AttackPower = FloorToInt((10+29*2)*1.0) = 68 — tier x1.0 at Level 10 (<20).
            Assert.AreEqual(68, stats.GetBaseStat(entity, StatID.AttackPower),
                "AttackPower must be recomputed from the NEW Strength=29 total at tier x1.0.");

            Assert.AreEqual(hpBefore, stats.GetCurrentHP(entity), "CurrentHP must be untouched by a free-point spend.");
            Assert.AreEqual(mpBefore, stats.GetCurrentMP(entity), "CurrentMP must be untouched by a free-point spend.");
        }

        // ---------------------------------------------------------------
        // AC-LS-12 — heldFreePoints == 0 -> Guard 1 rejects; no write.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_NoHeldFreePoints_RejectsWithNoWrite()
        {
            // Arrange — fresh entity, no level-up ever triggered, heldFreePoints defaults to 0.
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 5);
            stats.SetBaseStat(entity, StatID.Strength, 10);

            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must start at 0.");

            // Act
            var result = leveling.AllocateFreePoint(entity, StatID.Strength);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.RejectedNoFreePoints, result, "Guard 1 must reject when heldFreePoints==0.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must remain 0 — a rejected call must not go negative or otherwise change.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Strength), "Strength must be untouched — no write on rejection.");
        }

        // ---------------------------------------------------------------
        // AC-LS-13 — invalid StatID (e.g. MaxHP) -> Guard 2 rejects BEFORE decrement;
        // heldFreePoints unchanged.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_InvalidStatId_RejectsBeforeDecrementLeavingHeldPointsUnchanged()
        {
            // Arrange — Healer L1->L2 (single real level-up) grants heldFreePoints=2 in one shot.
            // index2=L1->L2, index3 sentinel.
            var xpThresholds = new int[] { 0, 0, 200, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, HealerClassType);
            stats.SetBaseStat(entity, StatID.Level, 1);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 200); // L1 -> L2.

            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity), "Precondition: Healer heldFreePoints must be 2 after L1->L2.");
            int maxHpBefore = stats.GetBaseStat(entity, StatID.MaxHP);

            // Act — MaxHP is a derived stat, not one of the four allocatable primary stats.
            var result = leveling.AllocateFreePoint(entity, StatID.MaxHP);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.RejectedInvalidStat, result, "Guard 2 must reject an invalid target StatID.");
            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity), "heldFreePoints must be unchanged — Guard 2 fires before the decrement.");
            Assert.AreEqual(maxHpBefore, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP itself must be unchanged — no recompute occurs on rejection.");
        }

        // ---------------------------------------------------------------
        // AC-LS-14 — a free-point spend never restores CurrentHP/CurrentMP, even when
        // MaxHP/MaxMP increases via the F-3/F-4 recompute.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_SpendOnVitalityMidCombat_IncreasesMaxHpButLeavesCurrentHpUntouched()
        {
            // Arrange — Warrior L5->L6 (single real level-up, VIT+1/level) grants heldFreePoints=1
            // and (via CR-2.7) fully restores HP/MP to fresh Max. index6=L5->L6, index7 sentinel.
            var xpThresholds = new int[] { 0, 0, 0, 0, 0, 0, 300, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 5);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 300); // L5 -> L6.

            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 1 after the single level-up.");

            // Simulate mid-combat: reduce CurrentHP/CurrentMP below their post-level-up Max values.
            stats.ApplyDamage(entity, 200f);
            stats.ConsumeMana(entity, 50f);
            float hpMidCombat = stats.GetCurrentHP(entity);
            float mpMidCombat = stats.GetCurrentMP(entity);
            int maxHpBefore = stats.GetBaseStat(entity, StatID.MaxHP);

            // Act — spend the held point on Vitality, which increases MaxHP via the F-3 recompute.
            var result = leveling.AllocateFreePoint(entity, StatID.Vitality);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.Success, result);
            int maxHpAfter = stats.GetBaseStat(entity, StatID.MaxHP);
            Assert.Greater(maxHpAfter, maxHpBefore, "MaxHP must increase — Vitality went up and tier is unchanged.");

            Assert.AreEqual(hpMidCombat, stats.GetCurrentHP(entity),
                "CurrentHP must stay at the mid-combat value — a free-point spend must never restore it, unlike a level-up.");
            Assert.AreEqual(mpMidCombat, stats.GetCurrentMP(entity),
                "CurrentMP must stay at the mid-combat value — a free-point spend must never restore it, unlike a level-up.");
        }

        // ---------------------------------------------------------------
        // AC-LS-15 — last free point spent: counter reaches exactly 0, never -1; the next call
        // immediately hits Guard 1.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_LastPointSpent_CounterReachesExactlyZeroThenNextCallRejects()
        {
            // Arrange — Warrior single level-up (L9->L10) grants heldFreePoints=1.
            var xpThresholds = new int[12];
            xpThresholds[10] = 300;
            xpThresholds[11] = 999999;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 9);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 300); // L9 -> L10.

            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 1.");

            // Act — spend the single held point.
            var firstResult = leveling.AllocateFreePoint(entity, StatID.Dexterity);

            // Assert — exactly 0, not -1.
            Assert.AreEqual(AllocateFreePointResult.Success, firstResult);
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must reach exactly 0 after spending the last point.");

            // Act — a second, immediate call must hit Guard 1.
            var secondResult = leveling.AllocateFreePoint(entity, StatID.Dexterity);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.RejectedNoFreePoints, secondResult, "The immediate next call must be rejected by Guard 1.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "heldFreePoints must remain exactly 0 — never negative.");
        }

        // ---------------------------------------------------------------
        // AC-LS-16 — LevelTierMultiplier used during the recompute is derived on-demand from
        // GetBaseStat(Level) at spend time, no stored/cached multiplier.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_AtLevel20_RecomputeUsesFreshTierOnePointTwo()
        {
            // Arrange — Warrior L19->L20 (single real level-up, matching Story 002's AC-LS-03
            // worked example exactly: STR 46->48). Sized to 22, with index21 sentinel (CR-2.9's
            // post-L20 re-check must not cross it).
            var xpThresholds = new int[22];
            xpThresholds[20] = 300;
            xpThresholds[21] = 999999;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 19);
            stats.SetBaseStat(entity, StatID.Strength, 46);
            stats.SetBaseStat(entity, StatID.Dexterity, 28);
            stats.SetBaseStat(entity, StatID.Vitality, 28);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 500); // L19 -> L20.

            Assert.AreEqual(20, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach 20.");
            Assert.AreEqual(48, stats.GetBaseStat(entity, StatID.Strength), "Precondition: STR must be 46+2=48 post auto-alloc.");
            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 1 after the single level-up.");

            // Act — spend the point on Strength; tier at Level 20 is x1.2 (not x1.0).
            var result = leveling.AllocateFreePoint(entity, StatID.Strength);

            // Assert
            Assert.AreEqual(AllocateFreePointResult.Success, result);
            Assert.AreEqual(49, stats.GetBaseStat(entity, StatID.Strength), "Strength must increment by 1 (48->49).");

            // AttackPower = FloorToInt((10+49*2)*1.2) = FloorToInt(129.6) = 129 — proves the x1.2
            // tier (Level 20) was used, derived fresh from GetBaseStat(Level) at spend time, not a
            // stale x1.0 tier or any stored/cached multiplier field.
            Assert.AreEqual(129, stats.GetBaseStat(entity, StatID.AttackPower),
                "AttackPower must reflect tier x1.2 (129), proving the tier multiplier is derived on-demand from the current Level.");
        }
    }
}
