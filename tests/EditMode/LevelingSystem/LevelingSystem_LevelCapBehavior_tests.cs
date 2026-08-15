using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 008 (Level Cap Behavior — CR-5). Composes a real
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, following the established
    /// <c>CreateWiredStats</c>/<c>CreateClassRegistry</c> idiom from
    /// <c>LevelingSystem_RespecTwoPhaseCommit_tests.cs</c> (Story 007) exactly, and the shared
    /// <see cref="StatEventRecorder"/> helper as the "zero writes occurred" proxy — every
    /// non-transactional <c>SetBaseStat</c> call synchronously fires <c>OnStatChanged</c>, so zero
    /// recorded firings is equivalent to zero <c>SetBaseStat</c> calls (same idiom
    /// <c>LevelingSystem_XpAccumulation_tests.cs</c>, Story 001, established for AC-LS-40).
    /// </summary>
    /// <remarks>
    /// <para>Covers AC-LS-23 (<c>AddExperience</c> at-cap no-op, Story 008's own new production
    /// guard), AC-LS-24 (CR-2.1 at-cap guard inside
    /// <see cref="LevelingService.NotifyExperienceCrossedThreshold"/>'s CR-2.9 loop, already
    /// implemented since Story 002/003 — this is its dedicated cap-boundary test), AC-LS-25
    /// (<see cref="LevelingService.GetExperienceThreshold"/>'s CR-5.3 sentinel at index 61,
    /// tested directly since no current production caller reaches it at Level 60 — see the
    /// method's own doc comment for why), and AC-LS-26 (<see cref="LevelingService.
    /// AllocateFreePoint"/> remains spendable at L60, already implemented since Story 005, no
    /// Level-based restriction anywhere in its guard chain).</para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_LevelCapBehavior_Tests
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

        /// <summary>
        /// Small test-local XP threshold table (index = level+1), NOT Story 010's real
        /// economy-signed-off table (Blocked on OQ-LS-7, irrelevant here — see the story's own
        /// Dependencies note). Sized to 62 entries with the CR-5.3 sentinel at index 61 =
        /// <see cref="int.MaxValue"/> (AC-LS-25). Entries [58]/[59]/[60] (L57-&gt;L58-&gt;L59-&gt;
        /// L60) let AC-LS-26 drive <c>heldFreePoints</c> to a nonzero value via 3 real consecutive
        /// level-ups — <c>heldFreePoints</c> has no public setter, so this is the established
        /// idiom (see <c>LevelingSystem_FreePointAllocation_tests.cs</c>, Story 005) for seeding
        /// it. [60] = 300 also doubles as "XpThreshold[60]" for AC-LS-23's exact-cap-value
        /// precondition.
        /// </summary>
        private static int[] BuildXpThresholdsWithCapSentinel()
        {
            var t = new int[62];
            t[58] = 100;          // L57 -> L58
            t[59] = 200;          // L58 -> L59
            t[60] = 300;          // L59 -> L60 ("XpThreshold[60]" in CR-5.2's wording)
            t[61] = int.MaxValue; // CR-5.3 sentinel.
            return t;
        }

        // ---------------------------------------------------------------
        // AC-LS-23 — AddExperience at Experience == XpThreshold[60] exactly, Level == 60: no
        // write, no OnStatChanged, no threshold check, zero SetBaseStat calls (spy-verified via
        // StatEventRecorder — see class remarks for why zero firings proves zero SetBaseStat
        // calls).
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_AtLevelCap_NoWriteNoEventsNoThresholdCheck()
        {
            // Arrange — Level 60, Experience already exactly XpThreshold[60] (300).
            var xpThresholds = BuildXpThresholdsWithCapSentinel();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);
            stats.SetBaseStat(entity, StatID.Experience, 300);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            // Act
            stats.AddExperience(entity, 500);

            // Assert — no write: Experience is unchanged.
            Assert.AreEqual(300, stats.GetBaseStat(entity, StatID.Experience),
                "Experience must remain exactly XpThreshold[60] (300) — XP beyond the cap must be discarded.");

            // Assert — no OnStatChanged fired for ANY stat (proxy for zero SetBaseStat calls).
            Assert.AreEqual(0, recorder.FiredCount.Count,
                "No OnStatChanged may fire — AddExperience must be a complete no-op at the level cap.");

            // Assert — no threshold check, no notify.
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "NotifyExperienceCrossedThreshold must never be called — the at-cap guard must fire before the threshold check.");
        }

        // ---------------------------------------------------------------
        // AC-LS-24 — CR-2.1 at-cap guard aborts NotifyExperienceCrossedThreshold's CR-2.9 loop
        // when Level == 60: Level never reaches 61, no auto-alloc, no recompute, no OnLevelUp
        // broadcast. Already-implemented guard (Story 002/003) — this is its dedicated test,
        // called directly (not via AddExperience) per the story's own worked example.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_NotifyExperienceCrossedThreshold_AtLevelCap_AbortsNoLevelUpNoBroadcast()
        {
            // Arrange — Level 60 with a large Experience value (irrelevant to the outcome — the
            // loop's own "levelBefore >= 60" break fires before any threshold/Experience read).
            var xpThresholds = BuildXpThresholdsWithCapSentinel();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);
            stats.SetBaseStat(entity, StatID.Experience, 999_999_999);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            int levelUpBroadcastCount = 0;
            leveling.OnLevelUp += _ => levelUpBroadcastCount++;

            // Act — called directly, not via AddExperience (which would itself now no-op per
            // AC-LS-23's new guard before ever reaching this method).
            leveling.NotifyExperienceCrossedThreshold(entity);

            // Assert — Level never reaches 61.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level),
                "Level must remain exactly 60 — the at-cap guard must abort before any Level write.");

            // Assert — no downstream writes at all (no auto-alloc, no F-3-F-9 recompute, no
            // HP/MP restore) — proxy via zero OnStatChanged firings for ANY stat.
            Assert.AreEqual(0, recorder.FiredCount.Count,
                "No OnStatChanged may fire — the sequence must abort before ExecuteLevelUpSequence performs any write.");

            // Assert — no OnLevelUp broadcast.
            Assert.AreEqual(0, levelUpBroadcastCount,
                "OnLevelUp must never fire — no level was gained.");

            // Assert — the call itself is still observable (bookkeeping counter increments
            // unconditionally at the top of the method, before the guard) but resolved to nothing.
            Assert.AreEqual(1, leveling.NotifyExperienceCrossedThresholdCallCount,
                "The call itself must still be recorded — only its downstream effects are guarded.");
        }

        // ---------------------------------------------------------------
        // AC-LS-25 — CR-5.3 sentinel: GetExperienceThreshold at Level 60 returns
        // XpThreshold[61] = int.MaxValue without throwing ArgumentOutOfRangeException. Tested
        // directly against GetExperienceThreshold itself, NOT through AddExperience/
        // NotifyExperienceCrossedThreshold — neither current production caller ever reaches this
        // at Level 60 (AddExperience's new AC-LS-23 guard and NotifyExperienceCrossedThreshold's
        // own "levelBefore >= 60" break both short-circuit first). This is a forward-looking
        // safety contract (e.g. for Story 013's HUD), not something provably "used" by an
        // existing call path today.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_GetExperienceThreshold_AtLevelCap_ReturnsSentinelWithoutThrowing()
        {
            // Arrange
            var xpThresholds = BuildXpThresholdsWithCapSentinel();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            stats.SetBaseStat(entity, StatID.Level, 60);

            // Act
            int threshold = 0;
            Assert.DoesNotThrow(() => threshold = leveling.GetExperienceThreshold(entity),
                "GetExperienceThreshold must not throw at Level 60 — index 61 must be safely resolvable via the CR-5.3 sentinel.");

            // Assert — the sentinel value itself.
            Assert.AreEqual(int.MaxValue, threshold,
                "GetExperienceThreshold at Level 60 must return the CR-5.3 sentinel, XpThreshold[61] = int.MaxValue.");

            // Assert — the array's own shape, per AC-LS-25's explicit array-level requirement.
            Assert.GreaterOrEqual(xpThresholds.Length, 62,
                "The xpThresholds array must have at least 62 entries (CR-5.3).");
            Assert.AreEqual(int.MaxValue, xpThresholds[61],
                "xpThresholds[61] must be the CR-5.3 sentinel, int.MaxValue.");
        }

        // ---------------------------------------------------------------
        // AC-LS-26 — heldFreePoints remains spendable at L60: allocation proceeds normally,
        // F-3-F-9 recompute uses the x2.0 tier. Already-implemented guard chain (Story 005) — no
        // Level-based restriction anywhere in it. heldFreePoints has no public setter, so it is
        // driven to 3 via 3 real consecutive level-ups (L57->L58->L59->L60), matching the
        // established idiom from LevelingSystem_FreePointAllocation_tests.cs (Story 005).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_AtLevelCap_SucceedsUsesTierTwoPointZero()
        {
            // Arrange — Warrior L57, baseline STR=10/DEX=10/VIT=10/INT=10. A single
            // AddExperience(300) call drives 3 consecutive real level-ups (L57->58->59->60) via
            // the CR-2.9 loop: [58]=100, [59]=200, [60]=300 are each crossed by the same
            // cumulative Experience=300, landing exactly at the cap (the loop's own
            // "levelBefore >= 60" break then stops it — no further GetExperienceThreshold(61)
            // lookup occurs on this path, unlike AC-LS-25's direct call above).
            var xpThresholds = BuildXpThresholdsWithCapSentinel();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 57);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 10);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 300); // L57 -> L58 -> L59 -> L60.

            // Preconditions — 3 Warrior level-ups: STR+2/level, DEX+1/level, VIT+1/level, INT+0.
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach the cap, 60.");
            Assert.AreEqual(16, stats.GetBaseStat(entity, StatID.Strength), "Precondition: STR must be 10+2*3=16.");
            Assert.AreEqual(13, stats.GetBaseStat(entity, StatID.Dexterity), "Precondition: DEX must be 10+1*3=13.");
            Assert.AreEqual(13, stats.GetBaseStat(entity, StatID.Vitality), "Precondition: VIT must be 10+1*3=13.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "Precondition: INT must be unchanged, 10 (Warrior has 0 auto-alloc).");
            Assert.AreEqual(3, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 1/level * 3 levels = 3.");

            // AttackPower after the 3rd level-up (L59->L60) was recomputed at tier x2.0 (the NEW
            // level, 60, per GetLevelTierMultiplier): floor((10+16*2)*2.0) = floor(84.0) = 84.
            Assert.AreEqual(84, stats.GetBaseStat(entity, StatID.AttackPower),
                "Precondition: AttackPower must already reflect tier x2.0 after the L59->L60 transition.");

            // Act — spend one held point on Strength.
            var result = leveling.AllocateFreePoint(entity, StatID.Strength);

            // Assert — allocation succeeds normally at the cap; no Level-based rejection exists.
            Assert.AreEqual(AllocateFreePointResult.Success, result,
                "AllocateFreePoint must succeed at Level 60 — there is no Level-based restriction in its guard chain.");
            Assert.AreEqual(2, leveling.GetHeldFreePoints(entity), "heldFreePoints must decrement from 3 to 2.");
            Assert.AreEqual(17, stats.GetBaseStat(entity, StatID.Strength), "Strength must increment by 1 (16->17).");

            // Assert — F-3-F-9 recompute uses GetLevelTierMultiplier(60) == 2.0, NOT the x1.5
            // tier a level below the cap would use. AttackPower = floor((10+17*2)*2.0) = 88,
            // clearly distinguishable from the x1.5 result (floor((10+17*2)*1.5) = 66) — proving
            // the x2.0 tier was actually applied, not merely that some recompute ran.
            Assert.AreEqual(88, stats.GetBaseStat(entity, StatID.AttackPower),
                "AttackPower must be recomputed at tier x2.0 (88) — distinct from the x1.5 result (66) this would be if the wrong tier were used.");
        }

        // ---------------------------------------------------------------
        // Regression coverage for AC-LS-23's own new guard (code-review suggestion): the
        // Level==60 early-return sits in the hot path of every AddExperience call at every
        // level 1-59. Every other Leveling System story's tests call AddExperience below the
        // cap and would fail if this guard misfired, but this story introduces the guard and
        // should pin its own boundary directly rather than relying on incidental coverage.
        // ---------------------------------------------------------------

        [Test]
        public void CharacterStats_AddExperience_BelowLevelCap_StillProcessesNormally()
        {
            // Arrange — Level 59, one below the cap; the new guard must not fire here.
            var xpThresholds = BuildXpThresholdsWithCapSentinel();
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 59);
            stats.SetBaseStat(entity, StatID.Experience, 0);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            // Act — a small grant that does not cross xpThresholds[60]=300, isolating the write
            // itself from any level-up side effects.
            stats.AddExperience(entity, 50);

            // Assert — the write happened: Experience increased, exactly one OnStatChanged fired.
            Assert.AreEqual(50, stats.GetBaseStat(entity, StatID.Experience),
                "AddExperience must still write normally at Level 59 — the new AC-LS-23 guard must only fire at exactly Level 60.");
            Assert.AreEqual(1, recorder.FiredCount.Count, "Exactly one stat (Experience) must have changed.");
            Assert.IsTrue(recorder.FiredCount.ContainsKey(StatID.Experience));
            Assert.AreEqual(0, leveling.NotifyExperienceCrossedThresholdCallCount,
                "50 XP does not cross xpThresholds[60]=300 from Level 59 — no threshold crossing expected.");
        }
    }
}
