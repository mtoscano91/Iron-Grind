using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 006 (Respec Core Commit Sequence — CR-4.4).
    /// Composes a real <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, following Stories 001-005's integration-style idiom
    /// (never mocked). Follows the established <c>CreateWiredStats</c>/<c>CreateClassRegistry</c>
    /// idiom from <c>LevelingSystem_FreePointAllocation_tests.cs</c> exactly.
    /// </summary>
    /// <remarks>
    /// Most preconditions here set Level and the 4 primary attributes directly via
    /// <c>SetBaseStat</c> rather than driving a real level-up through <c>AddExperience</c> —
    /// unlike Story 005's tests, <c>TryApplyRespec</c> itself never calls
    /// <c>NotifyExperienceCrossedThreshold</c>, so there is no XP mechanic to exercise as part
    /// of this method's own contract. The one exception is
    /// <see cref="LevelingService_TryApplyRespec_ValidRequest_LeavesHeldFreePointsUnchanged"/>,
    /// which needs a real <c>heldFreePoints</c> balance (no public setter exists) and therefore
    /// does drive real level-ups — sized with the trailing sentinel entry one index past the
    /// target level, per this epic's recurring CR-2.9 test-setup gotcha (see Story 005).
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_RespecCommitSequence_Tests
    {
        private const byte WarriorClassType = 1;
        private const byte HealerClassType = 2;

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

        private static (IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling) CreateWiredStats(
            IReadOnlyList<int> xpThresholds)
        {
            var leveling = new LevelingService(xpThresholds, CreateClassRegistry());
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            return (stats, leveling);
        }

        // ---------------------------------------------------------------
        // AC-LS-17 — BeginStatTransaction precedes all writes; every OnStatChanged this
        // method writes — including CritChance/AttackSpeedMultiplier via SetBaseStatFloat,
        // now that it is transaction-aware too (Story 006 Fix B: SetBaseStatFloat previously
        // fired immediately mid-transaction, unlike SetBaseStat/SetCurrentHP/SetCurrentMP;
        // TryApplyRespec was the first caller to ever open a transaction around
        // RecomputeDerivedStats, which is what surfaced the gap) — fires exactly once, batched
        // into the single EndStatTransaction pass.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_ValidRequest_DefersAllStatChangedEventsToSingleEndTransactionPass()
        {
            // Arrange — Warrior L10, starting totals AT the L10 floor (STR=28, DEX=19, VIT=19,
            // INT=10). Strength floor = 10+(10-1)*2 = 28.
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

            // Ordering proof: with SetBaseStatFloat now transaction-aware (Fix B), CritChance
            // and AttackSpeedMultiplier — the two stats that used to fire immediately, ahead of
            // everything else, before this fix — must already be at their fresh recomputed
            // values by the time the very first OnStatChanged fires at all, proving they are
            // now part of the SAME deferred batch as every other stat this method writes.
            bool firstFireSeen = false;
            float critChanceAtFirstFire = 0f;
            float attackSpeedAtFirstFire = 0f;
            void HandleFirstFireSnapshot(EntityID e, StatID s)
            {
                if (firstFireSeen) return;
                firstFireSeen = true;
                critChanceAtFirstFire = stats.GetBaseStatFloat(entity, StatID.CritChance);
                attackSpeedAtFirstFire = stats.GetBaseStatFloat(entity, StatID.AttackSpeedMultiplier);
            }
            stats.Subscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)HandleFirstFireSnapshot);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 29,
                [StatID.Dexterity] = 19,
                [StatID.Vitality] = 19,
                [StatID.Intelligence] = 10,
            };

            // Act
            leveling.TryApplyRespec(entity, newTotals);

            // Assert — final values.
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Strength));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality));
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence));
            // AttackPower = FloorToInt((10+29*2)*1.0) = 68 at tier x1.0 (Level 10 < 20).
            Assert.AreEqual(68, stats.GetBaseStat(entity, StatID.AttackPower));
            // CritChance = 0.05 + (19*0.0015*1.0) = 0.0785; AttackSpeedMultiplier = 1.0 + (19*0.003*1.0) = 1.057.
            Assert.AreEqual(0.0785f, stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-5f);
            Assert.AreEqual(1.057f, stats.GetBaseStatFloat(entity, StatID.AttackSpeedMultiplier), 1e-5f);

            // Assert — ordering: CritChance/AttackSpeedMultiplier already carried these final
            // values at the moment the very first OnStatChanged fired at all — not fired
            // individually, ahead of the rest, mid-call (see arrange comment above).
            Assert.IsTrue(firstFireSeen, "At least one OnStatChanged must have fired.");
            Assert.AreEqual(0.0785f, critChanceAtFirstFire, 1e-5f);
            Assert.AreEqual(1.057f, attackSpeedAtFirstFire, 1e-5f);

            // Assert — every stat this method writes fires OnStatChanged exactly once, all
            // batched into the single EndStatTransaction() pass (dedup + single pass).
            Assert.AreEqual(1, recorder.FiredCount[StatID.Strength]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.Dexterity]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.Vitality]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.Intelligence]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.MaxHP]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.MaxMP]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.AttackPower]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.Defense]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.MagicDefense]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.CritChance]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.AttackSpeedMultiplier]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.CurrentHP]);
            Assert.AreEqual(1, recorder.FiredCount[StatID.CurrentMP]);
        }

        // ---------------------------------------------------------------
        // AC-LS-19 — heldFreePoints is UNCHANGED by a respec commit; only the primary-attribute
        // totals move. heldFreePoints=6 reached via 6 real level-ups from an L14 floor baseline
        // (Warrior 1 free point/level). Sentinel-array pattern: xpThresholds sized one index
        // past the target level (index 21), per this epic's recurring test-setup gotcha.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_ValidRequest_LeavesHeldFreePointsUnchanged()
        {
            // Arrange — Warrior L14 baseline AT the L14 floor (STR=36, DEX=23, VIT=23, INT=10).
            // Experience stays pinned at 50 across the whole CR-2.9 loop (never decremented per
            // level), and the threshold check re-reads xpThresholds[levelBefore+1] fresh each
            // iteration — since xpThresholds[15..20] are all 50, the loop satisfies the
            // threshold at levelBefore = 14,15,16,17,18,19: SIX level-ups (L14->L20), not five.
            // This lands exactly on the L20 floor (STR=48 = 36+6x2, DEX=29, VIT=29, INT=10) and
            // grants heldFreePoints=6 (1/level x 6 level-ups). index15..20 = level-up
            // thresholds, index21 = trailing sentinel one past the target level (CR-2.9's
            // post-L20 re-check must not cross it).
            var xpThresholds = new int[22];
            xpThresholds[15] = 50;
            xpThresholds[16] = 50;
            xpThresholds[17] = 50;
            xpThresholds[18] = 50;
            xpThresholds[19] = 50;
            xpThresholds[20] = 50;
            xpThresholds[21] = 999999;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 14);
            stats.SetBaseStat(entity, StatID.Strength, 36);
            stats.SetBaseStat(entity, StatID.Dexterity, 23);
            stats.SetBaseStat(entity, StatID.Vitality, 23);
            stats.SetBaseStat(entity, StatID.Intelligence, 10);

            stats.AddExperience(entity, 50); // L14 -> L20 (6 level-ups — see arrange comment above).

            Assert.AreEqual(20, stats.GetBaseStat(entity, StatID.Level), "Precondition: Level must reach 20.");
            Assert.AreEqual(48, stats.GetBaseStat(entity, StatID.Strength), "Precondition: STR must land exactly on the L20 floor (48).");
            Assert.AreEqual(6, leveling.GetHeldFreePoints(entity), "Precondition: heldFreePoints must be 6 after 6 level-ups (Warrior 1/level).");

            // Act — reallocate: raise Strength above its floor, leave the rest at floor.
            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 50,
                [StatID.Dexterity] = 29,
                [StatID.Vitality] = 29,
                [StatID.Intelligence] = 10,
            };
            leveling.TryApplyRespec(entity, newTotals);

            // Assert
            Assert.AreEqual(6, leveling.GetHeldFreePoints(entity), "heldFreePoints must be UNCHANGED by the respec commit — still 6.");
            Assert.AreEqual(50, stats.GetBaseStat(entity, StatID.Strength), "Strength must reflect the new total.");
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Dexterity), "Dexterity must be unchanged.");
            Assert.AreEqual(29, stats.GetBaseStat(entity, StatID.Vitality), "Vitality must be unchanged.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence), "Intelligence must be unchanged.");
        }

        // ---------------------------------------------------------------
        // AC-LS-20 — a below-floor newTotals submission throws BEFORE BeginStatTransaction is
        // ever called: no writes, no transaction left open.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_BelowFloorSubmission_ThrowsBeforeOpeningTransactionWithNoWrites()
        {
            // Arrange — Warrior L10, at floor exactly (STR=28, DEX=19, VIT=19, INT=10). Strength
            // floor at L10 = 10+(10-1)*2 = 28, matching the story's worked example exactly.
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

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 27, // one below the L10 floor of 28.
                [StatID.Dexterity] = 19,
                [StatID.Vitality] = 19,
                [StatID.Intelligence] = 10,
            };

            // Act / Assert
            Assert.Throws<ArgumentException>(() => leveling.TryApplyRespec(entity, newTotals),
                "A below-floor submission must throw before any transaction is opened.");

            // Assert — no writes occurred anywhere.
            Assert.AreEqual(28, stats.GetBaseStat(entity, StatID.Strength), "Strength must be unchanged — the floor guard must fire before any write.");
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Dexterity));
            Assert.AreEqual(19, stats.GetBaseStat(entity, StatID.Vitality));
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Intelligence));

            // Assert — no OnStatChanged fired at all.
            Assert.AreEqual(0, recorder.FiredCount.Count, "No OnStatChanged may fire — no BeginStatTransaction call ever happened.");

            // Assert — no transaction was left open: EndStatTransaction must throw as if none
            // was ever begun, proving BeginStatTransaction was never reached.
            Assert.Throws<InvalidOperationException>(() => stats.EndStatTransaction(),
                "No transaction should have been opened — EndStatTransaction must throw as if none was ever begun.");
        }

        // ---------------------------------------------------------------
        // AC-LS-21 — respec lowering MaxMP below CurrentMP clamps CurrentMP within the same
        // transaction window, one deferred OnStatChanged pass.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_LoweringMaxMPBelowCurrentMP_ClampsCurrentMPInSingleDeferredPass()
        {
            // Arrange — Healer L20. INT floor = 10+(20-1)*2 = 48. Pre-respec: MaxMP baseline set
            // high (2000) so the CurrentMP=850 setup write below is not itself clamped;
            // CurrentMP will only be clamped by TryApplyRespec's own reconciliation once MaxMP
            // is recomputed from the new Intelligence total (48) at tier x1.2:
            // floor((100+48*12)*1.2) = floor(811.2) = 811.
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, HealerClassType);
            stats.SetBaseStat(entity, StatID.Level, 20);
            stats.SetBaseStat(entity, StatID.Strength, 10);
            stats.SetBaseStat(entity, StatID.Dexterity, 10);
            stats.SetBaseStat(entity, StatID.Vitality, 29);
            stats.SetBaseStat(entity, StatID.Intelligence, 60); // pre-respec value, overwritten below.
            stats.SetBaseStat(entity, StatID.MaxMP, 2000);
            stats.SetCurrentMP(entity, 850f);

            Assert.AreEqual(850f, stats.GetCurrentMP(entity), "Precondition: CurrentMP must be 850.");

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 10,
                [StatID.Dexterity] = 10,
                [StatID.Vitality] = 29,
                [StatID.Intelligence] = 48, // exactly the L20 floor.
            };

            // Act
            leveling.TryApplyRespec(entity, newTotals);

            // Assert — MaxMP recomputed from the new Intelligence total; CurrentMP clamped down
            // to it by the same call, never raised.
            Assert.AreEqual(811, stats.GetBaseStat(entity, StatID.MaxMP), "MaxMP must be recomputed from the new Intelligence total.");
            Assert.AreEqual(811f, stats.GetCurrentMP(entity), "CurrentMP must clamp down to the new MaxMP within the same respec call.");

            // Assert — one deferred pass: MaxMP and CurrentMP each fire OnStatChanged exactly once.
            Assert.AreEqual(1, recorder.FiredCount[StatID.MaxMP], "OnStatChanged(MaxMP) must fire exactly once.");
            Assert.AreEqual(1, recorder.FiredCount[StatID.CurrentMP], "OnStatChanged(CurrentMP) must fire exactly once — one deferred pass, not two separate ones.");
        }

        // ---------------------------------------------------------------
        // AC-LS-52 — respec lowering MaxHP below CurrentHP clamps CurrentHP the same way;
        // OnEntityDied does NOT fire (schema clamp, not a damage/death event).
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_TryApplyRespec_LoweringMaxHPBelowCurrentHP_ClampsCurrentHPWithoutFiringEntityDied()
        {
            // Arrange — Warrior L20. Vitality floor = 10+(20-1)*1 = 29. Pre-respec: MaxHP
            // baseline set high (5000) so the CurrentHP=950 setup write below is not itself
            // clamped; CurrentHP will only be clamped by TryApplyRespec's own reconciliation once
            // MaxHP is recomputed from the new Vitality total (29) at tier x1.2:
            // floor((200+29*20)*1.2) = floor(936.0) = 936.
            var xpThresholds = new int[] { 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 20);
            stats.SetBaseStat(entity, StatID.Strength, 48);
            stats.SetBaseStat(entity, StatID.Dexterity, 29);
            stats.SetBaseStat(entity, StatID.Vitality, 10); // pre-respec value, overwritten below.
            stats.SetBaseStat(entity, StatID.Intelligence, 10);
            stats.SetBaseStat(entity, StatID.MaxHP, 5000);
            stats.SetCurrentHP(entity, 950f);

            Assert.AreEqual(950f, stats.GetCurrentHP(entity), "Precondition: CurrentHP must be 950.");

            bool entityDiedFired = false;
            void HandleEntityDied(EntityID e) => entityDiedFired = true;
            stats.Subscribe((IronGrind.CharacterStats.CharacterStats.EntityDiedHandler)HandleEntityDied);

            var recorder = new StatEventRecorder();
            recorder.Subscribe(stats);

            var newTotals = new Dictionary<StatID, int>
            {
                [StatID.Strength] = 48,
                [StatID.Dexterity] = 29,
                [StatID.Vitality] = 29, // exactly the L20 floor.
                [StatID.Intelligence] = 10,
            };

            // Act
            leveling.TryApplyRespec(entity, newTotals);

            // Assert — MaxHP recomputed from the new Vitality total; CurrentHP clamped down
            // within the same call, never raised. OnEntityDied does not fire.
            Assert.AreEqual(936, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must be recomputed from the new Vitality total.");
            Assert.AreEqual(936f, stats.GetCurrentHP(entity), "CurrentHP must clamp down to the new MaxHP within the same respec call.");
            Assert.IsFalse(entityDiedFired, "OnEntityDied must NOT fire — a respec-driven MaxHP reduction is a schema clamp, not a death event.");

            // Assert — one deferred pass: MaxHP and CurrentHP each fire OnStatChanged exactly once.
            Assert.AreEqual(1, recorder.FiredCount[StatID.MaxHP], "OnStatChanged(MaxHP) must fire exactly once.");
            Assert.AreEqual(1, recorder.FiredCount[StatID.CurrentHP], "OnStatChanged(CurrentHP) must fire exactly once — one deferred pass, not two separate ones.");
        }
    }
}
