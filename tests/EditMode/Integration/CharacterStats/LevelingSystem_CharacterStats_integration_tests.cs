using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.Integration.CharacterStats
{
    /// <summary>
    /// EditMode integration tests for Character Stats Story 008 (Integration — Leveling System
    /// &#8596; Character Stats: Spawn Path, Tier Transitions, MaxMP Ceiling). Composes a real
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/> with a real
    /// <see cref="LevelingService"/>, wired via <see cref="LevelingService.AttachCharacterStats"/>
    /// — no mocks for either principal system, following the established idiom of
    /// <c>LevelingSystem_TierTransitionRecompute_tests.cs</c> and
    /// <c>LevelingSystem_SpawnPersistenceLoad_tests.cs</c>.
    /// </summary>
    /// <remarks>
    /// <para>Covers GDD <c>design/gdd/character-stats.md</c> AC-31 (L1 spawn path via
    /// <see cref="LevelingService.InitializeAtL1"/>, tier &#215;1.0), AC-27a/b/c (Warrior Tank tier
    /// transitions at L20/L40/L60, &#215;1.2/&#215;1.5/&#215;2.0), and AC-34 (MaxMP F-4 ceiling: the
    /// Leveling System clamps to 9,999 before <c>SetBaseStat</c>; <c>CharacterStats</c> itself does
    /// not enforce <c>StatMax</c>).</para>
    /// <para><b>Overlap note:</b> Leveling System epic tests AC-LS-27/38/53/54/43
    /// (<c>LevelingSystem_SpawnPersistenceLoad_tests.cs</c> /
    /// <c>LevelingSystem_TierTransitionRecompute_tests.cs</c>) already exercise real
    /// <c>CharacterStats</c> collaboration for the plain-Warrior path (auto-alloc only, no
    /// free-point spend). This file's distinct value is the "Warrior Tank" build's free-point
    /// spend path (1 auto-alloc + 1 player-spent free point = 2 VIT/level, per the story's build
    /// note), the exact GDD AC-27/31/34 worked values, and the explicit proof that
    /// <c>CharacterStats.SetBaseStat</c> never clamps MaxMP — only the Leveling System does.</para>
    /// <para>Deterministic: no randomness, no I/O, no wall-clock dependence. Each test builds its
    /// own fresh <c>CharacterStats</c>/<c>LevelingService</c> pair via <see cref="CreateWiredStats"/>
    /// — no shared mutable state between tests.</para>
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_CharacterStats_Integration_Tests
    {
        private const byte WarriorClassType = 1;

        /// <summary>
        /// Warrior Tank class data: STR+2/DEX+1/VIT+1 auto-alloc per level, 1 free point/level —
        /// the same registry data used by <c>LevelingSystem_TierTransitionRecompute_tests.cs</c>
        /// and <c>LevelingSystem_SpawnPersistenceLoad_tests.cs</c>. The story's "Warrior Tank"
        /// build spends every free point on Vitality, yielding VIT+2/level (1 auto + 1 free) —
        /// the class registry itself does not encode that spend choice; it is applied per-test via
        /// explicit <see cref="LevelingService.AllocateFreePoint"/> calls.
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
        /// Same ADR-010 wiring sequence as the Leveling System epic's established
        /// <c>CreateWiredStats</c> helper: real <see cref="IronGrind.CharacterStats.CharacterStats"/>
        /// + real <see cref="LevelingService"/>, connected via
        /// <see cref="LevelingService.AttachCharacterStats"/> after both exist.
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
        // AC-31 — L1 spawn path (InitializeAtL1): MaxHP initialized with LevelTierMultiplier
        // x1.0. Edge case: CharacterStats reports no MaxHP value before the spawn sequence fires.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_InitializeAtL1_WarriorSpawn_MaxHpFourHundredAtTierOnePointZeroWithNoPriorValue()
        {
            // Arrange.
            var xpThresholds = new[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);

            // Edge case — before spawn, CharacterStats has never had any stat written for this
            // entity. GetBaseStat's documented "unset" sentinel is 0 (CharacterStats.cs:
            // "Returns 0 if the entity is unknown or the stat has never been set." — there is no
            // separate has-value flag or nullable API), so a return of 0 here IS "no MaxHP value".
            Assert.AreEqual(0, stats.GetBaseStat(entity, StatID.MaxHP),
                "Before InitializeAtL1 runs, GetBaseStat(MaxHP) must return CharacterStats' unset sentinel, 0 — the entity has never had any stat written.");

            // Act — stub Class System step: triggers the Leveling System's spawn sequence
            // (CR-6.1/CR-6.2), tier pinned literally to x1.0, all primary attributes = 10.
            leveling.InitializeAtL1(entity, WarriorClassType);

            // Assert — AC-31: MaxHP = FloorToInt((200+10*20)*1.0) = 400. A return of 200 (the
            // formula's base term with no tier multiplier applied) is the documented failure value.
            Assert.AreEqual(1, stats.GetBaseStat(entity, StatID.Level), "Precondition: spawn sets Level=1.");
            Assert.AreEqual(10, stats.GetBaseStat(entity, StatID.Vitality), "Precondition: spawn sets Vitality=10.");
            Assert.AreEqual(400, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+10*20)*1.0)=400 immediately after spawn (AC-31). A return of 200 (tier x1.0 never applied — bare formula base) is the documented failure value.");
        }

        // ---------------------------------------------------------------
        // AC-27a — Warrior Tank L19->L20: auto-alloc VIT->47 (tier x1.2) -> MaxHP 1368, then
        // AllocateFreePoint(Vitality) -> VIT 48 -> MaxHP 1392.
        //
        // Setup: VIT=46 at L19 = 10 base + 18 level-ups x 2 VIT (1 auto + 1 free, Warrior Tank
        // build note). Only Level and Vitality are seeded in the AC-27 tests — MaxHP (F-3)
        // depends only on Vitality, so STR/DEX/INT are left unset and play no part in the
        // assertions.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorTankL19ToL20_MaxHpReaches1368ThenFreePointSpendReaches1392AtTierOnePointTwo()
        {
            // Arrange.
            // Sized to 22, not 21: after L20 is reached, NotifyExperienceCrossedThreshold's CR-2.9
            // loop (Leveling System Story 003) always performs one more
            // GetExperienceThreshold(level+1) lookup before stopping — index 21 must exist (a high
            // sentinel) or that lookup throws ArgumentOutOfRangeException, even though no further
            // level-up is expected to fire (same sizing rationale as
            // LevelingSystem_TierTransitionRecompute_tests.cs's AC-LS-38 test).
            var xpThresholds = new int[22];
            xpThresholds[20] = 300; // level 19 -> 20 threshold.
            xpThresholds[21] = 999999; // sentinel — CR-2.9's post-L20 re-check must not cross this.
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 19);
            stats.SetBaseStat(entity, StatID.Vitality, 46);

            // Act — crosses the L19->L20 threshold; Warrior auto-alloc fires: VIT 46->47.
            stats.AddExperience(entity, 300);

            // Assert — AC-27a first half: post-auto-alloc VIT=47; MaxHP =
            // FloorToInt((200+47*20)*1.2) = 1368. Failure values ruled out: 1140 (tier x1.0 still
            // applied instead of the new x1.2 tier), 1344 (VIT=46 used — auto-alloc did not fire
            // before the recompute, an ordering violation).
            Assert.AreEqual(20, stats.GetBaseStat(entity, StatID.Level), "Level must reach 20.");
            Assert.AreEqual(47, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 46+1=47 post-auto-alloc.");
            Assert.AreEqual(1368, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+47*20)*1.2)=1368 (F-3, AC-27a). Failure values: 1140 (tier x1.0 not upgraded to x1.2), 1344 (VIT=46 — auto-alloc fired after, not before, the recompute).");

            // Act — the Warrior Tank build's free-point spend: the level-up granted 1 held free
            // point (Warrior FreePointsPerLevel=1); spend it on Vitality.

            // Assert — AC-27a second half: VIT=48; MaxHP = FloorToInt((200+48*20)*1.2) = 1392.
            // Failure value 1160 corresponds to tier x1.0 still applied instead of x1.2.
            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: the L20 level-up must grant exactly 1 held free point (Warrior FreePointsPerLevel=1).");
            var result = leveling.AllocateFreePoint(entity, StatID.Vitality);
            Assert.AreEqual(AllocateFreePointResult.Success, result, "AllocateFreePoint(Vitality) must succeed — the L20 level-up granted exactly 1 held free point.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "The spend must decrement heldFreePoints 1->0 (CR-3.3) — a non-decrementing spend would let the same point be spent twice.");
            Assert.AreEqual(48, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 47+1=48 after the free-point spend.");
            Assert.AreEqual(1392, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+48*20)*1.2)=1392 (F-3, AC-27a free-point spend). Failure value 1160 corresponds to tier x1.0 never having been upgraded to x1.2.");
        }

        // ---------------------------------------------------------------
        // AC-27b — Warrior Tank L39->L40: ordering-regression detector. Auto-alloc MUST fire
        // (VIT->87) BEFORE the tier x1.5 recompute; then AllocateFreePoint(Vitality) -> VIT 88 ->
        // MaxHP 2940. Edge case: pre-level-up MaxHP is recorded to make the tier spike explicit.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorTankL39ToL40_MaxHpReaches2910ThenFreePointSpendReaches2940AtTierOnePointFiveOrderingProof()
        {
            // Arrange.
            // Sized to 42, not 41 — same CR-2.9 post-level-up re-check reasoning as the L19->L20
            // test above (index 41 must exist as a high sentinel).
            var xpThresholds = new int[42];
            xpThresholds[40] = 300; // level 39 -> 40 threshold.
            xpThresholds[41] = 999999; // sentinel — CR-2.9's post-L40 re-check must not cross this.
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 39);
            stats.SetBaseStat(entity, StatID.Vitality, 86);

            // Edge case — record the "L39 end" MaxHP explicitly (F-3 at tier x1.2, the tier
            // Level 39 falls under, VIT=86): FloorToInt((200+86*20)*1.2) = 2304. Set directly
            // since no level-up event has fired yet to populate MaxHP naturally — this makes the
            // pre-level-up baseline explicit so the assertion below can prove the tier spike is
            // visible, rather than merely asserting the post-level-up value in isolation.
            stats.SetBaseStat(entity, StatID.MaxHP, 2304);

            // Act — crosses the L39->L40 threshold. Ordering contract under test: Warrior
            // auto-alloc (VIT 86->87) MUST be applied BEFORE F-3 is recomputed at the new x1.5
            // tier — never the reverse.
            stats.AddExperience(entity, 300);

            // Assert — AC-27b first half / ordering-regression detector: post-auto-alloc VIT=87;
            // MaxHP = FloorToInt((200+87*20)*1.5) = 2910, a clear jump from the L39 baseline of
            // 2304 (tier spike visible). A return of 2880 (FloorToInt((200+86*20)*1.5) — VIT=86
            // used, i.e. auto-alloc fired AFTER the recompute) is the ordering-regression failure
            // value; 1940 (tier x1.0) and 2328 (tier x1.2) are also failure values.
            Assert.AreEqual(40, stats.GetBaseStat(entity, StatID.Level), "Level must reach 40.");
            Assert.AreEqual(87, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 86+1=87 post-auto-alloc.");
            Assert.AreEqual(2910, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+87*20)*1.5)=2910 (F-3, AC-27b), up from the pre-level-up L39 baseline of 2304 — the tier spike is visible. A return of 2880 (VIT=86 — auto-alloc fired AFTER the recompute) is an ORDERING REGRESSION. 1940 (tier x1.0) or 2328 (tier x1.2) are also failure values.");

            // Act — free-point spend.

            // Assert — AC-27b second half: VIT=88; MaxHP = FloorToInt((200+88*20)*1.5) = 2940.
            // Failure values 1960 (x1.0) / 2352 (x1.2) correspond to a stale tier.
            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: the L40 level-up must grant exactly 1 held free point (Warrior FreePointsPerLevel=1).");
            var result = leveling.AllocateFreePoint(entity, StatID.Vitality);
            Assert.AreEqual(AllocateFreePointResult.Success, result, "AllocateFreePoint(Vitality) must succeed — the L40 level-up granted exactly 1 held free point.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "The spend must decrement heldFreePoints 1->0 (CR-3.3) — a non-decrementing spend would let the same point be spent twice.");
            Assert.AreEqual(88, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 87+1=88 after the free-point spend.");
            Assert.AreEqual(2940, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+88*20)*1.5)=2940 (F-3, AC-27b free-point spend). Failure values: 1960 (tier x1.0), 2352 (tier x1.2).");
        }

        // ---------------------------------------------------------------
        // AC-27c — Warrior Tank L59->L60: final tier milestone x2.0. VIT->127 -> MaxHP 5480, then
        // AllocateFreePoint(Vitality) -> VIT 128 -> MaxHP 5520. Edge case: Level=60 is the cap —
        // a further AddExperience call must not trigger another level-up.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_LevelUp_WarriorTankL59ToL60_MaxHpReaches5480ThenFreePointSpendReaches5520AndFurtherXpDoesNotLevelUp()
        {
            // Arrange. xpThresholds[60] is both the L59->L60 crossing threshold AND the CR-2.2a
            // clamp target — matches the sizing already established by
            // LevelingSystem_TierTransitionRecompute_tests.cs's AC-LS-54 test. No extra sentinel
            // is needed beyond index 60: once Level reaches 60, CR-2.9's loop guard
            // ("if (levelBefore >= 60) break;") stops BEFORE any further GetExperienceThreshold
            // lookup, unlike the L20/L40 cases above.
            var xpThresholds = new int[61];
            xpThresholds[60] = 5000;
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);

            stats.SetBaseStat(entity, StatID.Level, 59);
            stats.SetBaseStat(entity, StatID.Vitality, 126);

            // Act — crosses the L59->L60 threshold exactly (0 + 5000 == xpThresholds[60]).
            stats.AddExperience(entity, 5000);

            // Assert — AC-27c first half: post-auto-alloc VIT=127; MaxHP =
            // FloorToInt((200+127*20)*2.0) = 5480. Failure values: 5440
            // (FloorToInt((200+126*20)*2.0) — VIT=126, ordering violation) and 4110 (tier x1.5).
            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must reach the cap, 60.");
            Assert.AreEqual(5000, stats.GetBaseStat(entity, StatID.Experience), "CR-2.2a must clamp Experience to exactly xpThresholds[60]=5000.");
            Assert.AreEqual(127, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 126+1=127 post-auto-alloc.");
            Assert.AreEqual(5480, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+127*20)*2.0)=5480 (F-3, AC-27c). Failure values: 5440 (VIT=126 — ordering violation), 4110 (tier x1.5 instead of x2.0).");

            // Act — free-point spend.

            // Assert — AC-27c second half: VIT=128; MaxHP = FloorToInt((200+128*20)*2.0) = 5520.
            // Failure values: 2760 (tier x1.0), 4140 (tier x1.5).
            Assert.AreEqual(1, leveling.GetHeldFreePoints(entity), "Precondition: the L60 level-up must grant exactly 1 held free point (Warrior FreePointsPerLevel=1).");
            var result = leveling.AllocateFreePoint(entity, StatID.Vitality);
            Assert.AreEqual(AllocateFreePointResult.Success, result, "AllocateFreePoint(Vitality) must succeed — the L60 level-up granted exactly 1 held free point.");
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "The spend must decrement heldFreePoints 1->0 (CR-3.3) — a non-decrementing spend would let the same point be spent twice.");
            Assert.AreEqual(128, stats.GetBaseStat(entity, StatID.Vitality), "VIT must be 127+1=128 after the free-point spend.");
            Assert.AreEqual(5520, stats.GetBaseStat(entity, StatID.MaxHP),
                "MaxHP must be FloorToInt((200+128*20)*2.0)=5520 (F-3, AC-27c free-point spend). Failure values: 2760 (tier x1.0), 4140 (tier x1.5).");

            // Edge case — Level is already at the cap (60). AddExperience's CR-5.2 at-cap guard
            // ("if (GetBaseStat(Level) == 60) return;") must short-circuit BEFORE any Experience
            // write or threshold check — no further level-up, no state change at all.
            stats.AddExperience(entity, 1000);

            Assert.AreEqual(60, stats.GetBaseStat(entity, StatID.Level), "Level must remain 60 — AddExperience's CR-5.2 at-cap guard must prevent any further level-up.");
            Assert.AreEqual(5000, stats.GetBaseStat(entity, StatID.Experience), "Experience must remain exactly 5000 — the at-cap guard fires before any Experience write occurs.");
            Assert.AreEqual(5520, stats.GetBaseStat(entity, StatID.MaxHP), "MaxHP must remain 5520 — no recompute occurs when no level-up fires.");
        }

        // ---------------------------------------------------------------
        // AC-34 — MaxMP F-4 ceiling: the Leveling System clamps min(raw, 9999) before calling
        // SetBaseStat(MaxMP); CharacterStats.SetBaseStat itself does NOT enforce StatMax.
        //
        // Cleanest recompute path chosen: rather than reaching INT=409/500 via a long chain of
        // level-ups (Level is already pinned at the L60 cap, so no further level-up can fire to
        // trigger a recompute), INT is driven via a short, real AllocateFreePoint(Intelligence)
        // sequence — each call increments INT by exactly 1 and triggers a real F-3-F-9 recompute
        // at the current (x2.0) tier, using the same production code path AC-27a/b/c exercise.
        // heldFreePoints is seeded directly via RestoreLevelingState (CR-6.3's real load-path API)
        // rather than granted through level-ups, since this AC is only about the MaxMP ceiling,
        // not the leveling sequence itself.
        // ---------------------------------------------------------------

        [Test]
        public void LevelingService_AllocateFreePoint_IntelligenceAtL60_MaxMpClampedTo9999WhileCharacterStatsItselfDoesNotClamp()
        {
            // Arrange — pin Level=60 (tier x2.0) directly; no level-up sequence needed for this AC.
            var xpThresholds = new[] { 0, 0, 999999 };
            var (stats, leveling) = CreateWiredStats(xpThresholds);
            var entity = CharacterStatsFixture.PlayerEntityId;
            leveling.RegisterPlayerEntity(entity);
            leveling.RegisterPlayerClassType(entity, WarriorClassType);
            stats.SetBaseStat(entity, StatID.Level, 60);

            // Seed 3 held free points directly via the real CR-6.3 load-path API — L60 Warrior
            // max = (60-1)*1 = 59, so 3 is well within range and RestoreLevelingState neither
            // clamps nor logs an error here.
            leveling.RestoreLevelingState(entity, new LevelingStateSnapshot(heldFreePoints: 3));
            Assert.AreEqual(3, leveling.GetHeldFreePoints(entity), "Precondition: RestoreLevelingState must seed exactly 3 held free points (within the L60 Warrior max of 59 — no clamp).");

            stats.SetBaseStat(entity, StatID.Intelligence, 407);

            // Act 1 — INT 407->408 via a real AllocateFreePoint spend.
            var result1 = leveling.AllocateFreePoint(entity, StatID.Intelligence);

            // Assert — edge case: INT=408 -> F-4 raw = FloorToInt((100+408*12)*2.0) = 9992, which
            // does NOT exceed the 9,999 ceiling, so no clamp is needed — GetBaseStat(MaxMP) must
            // read the raw value exactly.
            Assert.AreEqual(AllocateFreePointResult.Success, result1, "First Intelligence spend must succeed — 3 held free points were seeded.");
            Assert.AreEqual(408, stats.GetBaseStat(entity, StatID.Intelligence), "INT must be 407+1=408.");
            Assert.AreEqual(9992, stats.GetBaseStat(entity, StatID.MaxMP),
                "MaxMP must be FloorToInt((100+408*12)*2.0)=9992 (F-4) — below the 9,999 ceiling, so no clamp fires (edge case: the clamp must not fire when it isn't needed).");

            // Act 2 — INT 408->409 via a second real AllocateFreePoint spend.
            var result2 = leveling.AllocateFreePoint(entity, StatID.Intelligence);

            // Assert — AC-34 first half: F-4 raw = FloorToInt((100+409*12)*2.0) = 10016, which
            // DOES exceed the ceiling; the Leveling System must clamp to 9999 before calling
            // SetBaseStat. GetBaseStat(MaxMP) must read the CLAMPED value, never the raw 10016.
            Assert.AreEqual(AllocateFreePointResult.Success, result2, "Second Intelligence spend must succeed.");
            Assert.AreEqual(409, stats.GetBaseStat(entity, StatID.Intelligence), "INT must be 408+1=409.");
            Assert.AreEqual(9999, stats.GetBaseStat(entity, StatID.MaxMP),
                "MaxMP must read the CLAMPED value 9999, not the raw FloorToInt((100+409*12)*2.0)=10016 (F-4, AC-34) — the Leveling System owns the write-side ceiling.");

            // Act 3 — drive INT to 500 (direct overwrite to 499, matching the AC-34 "INT->500"
            // scenario, then one more real spend to 500) to prove the clamp holds even for a
            // much larger raw overshoot.
            stats.SetBaseStat(entity, StatID.Intelligence, 499);
            var result3 = leveling.AllocateFreePoint(entity, StatID.Intelligence);

            // Assert — AC-34 second half: F-4 raw = FloorToInt((100+500*12)*2.0) = 12200, clamped
            // to 9999.
            Assert.AreEqual(AllocateFreePointResult.Success, result3, "Third Intelligence spend must succeed — the third seeded held free point.");
            Assert.AreEqual(500, stats.GetBaseStat(entity, StatID.Intelligence), "INT must be 499+1=500.");
            Assert.AreEqual(9999, stats.GetBaseStat(entity, StatID.MaxMP),
                "MaxMP must read the CLAMPED value 9999, not the raw FloorToInt((100+500*12)*2.0)=12200 (F-4, AC-34) — the same 9,999 ceiling holds for a much larger raw overshoot.");

            // Held-point bookkeeping — all 3 seeded points are now spent, so a 4th spend must be
            // rejected by Guard 1 with no write (INT and MaxMP unchanged). Catches a spend path
            // that fails to decrement heldFreePoints.
            Assert.AreEqual(0, leveling.GetHeldFreePoints(entity), "All 3 seeded free points must be consumed after 3 successful spends.");
            var result4 = leveling.AllocateFreePoint(entity, StatID.Intelligence);
            Assert.AreEqual(AllocateFreePointResult.RejectedNoFreePoints, result4, "A 4th spend with 0 held free points must be rejected (Guard 1, CR-3).");
            Assert.AreEqual(500, stats.GetBaseStat(entity, StatID.Intelligence), "A rejected spend must not write INT.");
            Assert.AreEqual(9999, stats.GetBaseStat(entity, StatID.MaxMP), "A rejected spend must not trigger a recompute or change MaxMP.");

            // Act 4 — direct path, bypassing the Leveling System entirely.
            stats.SetBaseStat(entity, StatID.MaxMP, 10016);

            // Assert — proves CharacterStats.SetBaseStat itself enforces no StatMax: it stores
            // exactly what is written, unclamped. The 9999 ceiling above is therefore entirely
            // owned by the Leveling System's RecomputeDerivedStats, not by CharacterStats.
            Assert.AreEqual(10016, stats.GetBaseStat(entity, StatID.MaxMP),
                "A direct SetBaseStat(MaxMP, 10016) call (bypassing the Leveling System) must store 10016 exactly, unclamped — CharacterStats.SetBaseStat does not enforce StatMax; only the Leveling System's write-side clamp does (AC-34).");
        }
    }
}
