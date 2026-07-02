using NUnit.Framework;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// EditMode unit tests for the CharacterStats container — Story 001 acceptance criteria.
    /// AC-26 (GetBaseStat == GetEffectiveStat for Experience) is DEFERRED to Story 002
    /// because GetEffectiveStat is not implemented until that story.
    /// </summary>
    [TestFixture]
    public class CharacterStats_Container_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _player;
        private EntityID _mob;
        private EntityID _playerB;

        [SetUp]
        public void SetUp()
        {
            _stats   = CharacterStatsFixture.Create();
            _player  = CharacterStatsFixture.PlayerEntityId;
            _mob     = CharacterStatsFixture.MobEntityId;
            _playerB = CharacterStatsFixture.PlayerBEntityId;
        }

        // -------------------------------------------------------------------
        // AC-23: SetBaseStat / GetBaseStat round-trip — exact value stored
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetAndGetBaseStat_MaxHP_ReturnsExactValue()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 5520);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(5520, result, "GetBaseStat must return the exact value written by SetBaseStat.");
        }

        [Test]
        public void CharacterStats_SetBaseStat_OverwriteExistingValue_ReturnsNewValue()
        {
            // Arrange — write high value first, then overwrite with lower value
            _stats.SetBaseStat(_player, StatID.MaxHP, 5520);

            // Act
            _stats.SetBaseStat(_player, StatID.MaxHP, 400);
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(400, result, "SetBaseStat must overwrite the previous value, not accumulate.");
        }

        // -------------------------------------------------------------------
        // AC-25: SetBaseStat stores int exactly — no re-rounding
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetBaseStat_Defense394_ReturnsExact394()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.Defense, 394);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.Defense);

            // Assert — 395 would indicate the system re-rounds on store or retrieve
            Assert.AreEqual(394, result, "Expected 394 exactly. A return of 395 indicates re-rounding.");
        }

        [Test]
        public void CharacterStats_SetBaseStat_EdgeIntValues_EachStoredExactly()
        {
            // Act & Assert — 393
            _stats.SetBaseStat(_player, StatID.Defense, 393);
            Assert.AreEqual(393, _stats.GetBaseStat(_player, StatID.Defense), "393 must be stored exactly.");

            // Act & Assert — 0
            _stats.SetBaseStat(_player, StatID.Defense, 0);
            Assert.AreEqual(0, _stats.GetBaseStat(_player, StatID.Defense), "0 must be stored exactly.");
        }

        // -------------------------------------------------------------------
        // AC-32: Persistence load path — no formula re-evaluation on SetBaseStat
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetBaseStat_PersistenceRestore_ReturnsStoredValueNotFormulaResult()
        {
            // Arrange — simulate a persistence restore of MaxHP = 2940.
            // The F-3 formula result for VIT=10 would be 200, not 2940.
            // SetBaseStat must store 2940 without invoking any formula.
            _stats.SetBaseStat(_player, StatID.MaxHP, 2940);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(2940, result,
                "SetBaseStat must not re-evaluate formulas. A return of 200 indicates formula evaluation.");
        }

        [Test]
        public void CharacterStats_SetBaseStat_MultipleCallsSameStat_OnlyLastValueKept()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.SetBaseStat(_player, StatID.MaxHP, 2000);
            _stats.SetBaseStat(_player, StatID.MaxHP, 2940);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(2940, result, "Only the last SetBaseStat call's value should be retained.");
        }

        // -------------------------------------------------------------------
        // AC-21 (adapted): Player-only stats return 0 on mob entity via GetBaseStat
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_GetBaseStat_MobEntity_PlayerOnlyStatsReturnZero()
        {
            // Arrange — mob has derived stats written (simulating a mob data-table load),
            // but no player-only fields are ever written for mobs.
            _stats.SetBaseStat(_mob, StatID.MaxHP,      500);
            _stats.SetBaseStat(_mob, StatID.AttackPower, 75);
            _stats.SetBaseStat(_mob, StatID.Defense,     30);

            // Act & Assert — all 7 player-only fields must return 0; no exception thrown
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.Strength),
                "Strength is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.Dexterity),
                "Dexterity is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.Vitality),
                "Vitality is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.Intelligence),
                "Intelligence is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.MaxMP),
                "MaxMP is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.CurrentMP),
                "CurrentMP is player-only; mob must return 0.");
            Assert.AreEqual(0, _stats.GetBaseStat(_mob, StatID.Experience),
                "Experience is player-only; mob must return 0.");
        }

        // -------------------------------------------------------------------
        // AC-30 (GetBaseStat portion): Mob stat round-trip
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetAndGetBaseStat_MobEntity_AttackPowerRoundTrips()
        {
            // Arrange
            _stats.SetBaseStat(_mob, StatID.AttackPower, 75);

            // Act
            int result = _stats.GetBaseStat(_mob, StatID.AttackPower);

            // Assert — raw base value; no formula applied
            Assert.AreEqual(75, result, "Mob AttackPower must round-trip with no formula applied.");
        }

        [Test]
        public void CharacterStats_SetAndGetBaseStat_MobEntity_MaxHPRoundTrips()
        {
            // Arrange
            _stats.SetBaseStat(_mob, StatID.MaxHP, 800);

            // Act
            int result = _stats.GetBaseStat(_mob, StatID.MaxHP);

            // Assert
            Assert.AreEqual(800, result, "Mob MaxHP must round-trip identically to player MaxHP.");
        }

        // -------------------------------------------------------------------
        // StatMax non-enforcement: SetBaseStat does not clamp at StatMax
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetBaseStat_ValueExceedingStatMax_StoredWithoutClamping()
        {
            // Arrange — StatMax(MaxMP) = 9999 per GDD; 10016 exceeds it.
            _stats.SetBaseStat(_player, StatID.MaxMP, 10016);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxMP);

            // Assert — 9999 would indicate SetBaseStat incorrectly applied StatMax clamping
            Assert.AreEqual(10016, result,
                "SetBaseStat must not clamp at StatMax. Clamping is the Leveling System's responsibility.");
        }

        [Test]
        public void CharacterStats_SetBaseStat_ZeroValue_StoredWithoutStatMinEnforcement()
        {
            // Arrange — StatMin(MaxHP) = 1 per GDD; write 0 to verify no StatMin enforcement on write.
            _stats.SetBaseStat(_player, StatID.MaxHP, 100);
            _stats.SetBaseStat(_player, StatID.MaxHP, 0);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(0, result,
                "SetBaseStat must not enforce StatMin. 0 must be stored exactly as written.");
        }

        [Test]
        public void CharacterStats_SetBaseStat_NegativeValue_StoredWithoutLowerBoundEnforcement()
        {
            // Arrange — SetBaseStat enforces no lower bound of any kind.
            // If GetEffectiveStat or a future StatMin guard were incorrectly applied in
            // SetBaseStat, this write would be clamped and the assertion would catch it.
            _stats.SetBaseStat(_player, StatID.MaxHP, -50);

            // Act
            int result = _stats.GetBaseStat(_player, StatID.MaxHP);

            // Assert
            Assert.AreEqual(-50, result,
                "SetBaseStat must store negative values exactly as written. Clamping is the caller's responsibility.");
        }

        // -------------------------------------------------------------------
        // Container isolation: two entities have independent stat arrays
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_TwoEntities_StatContainersAreIndependent()
        {
            // Arrange
            _stats.SetBaseStat(_player,  StatID.MaxHP, 500);
            _stats.SetBaseStat(_playerB, StatID.MaxHP, 200);

            // Act & Assert
            Assert.AreEqual(500, _stats.GetBaseStat(_player,  StatID.MaxHP),
                "PlayerA MaxHP must not be affected by playerB write.");
            Assert.AreEqual(200, _stats.GetBaseStat(_playerB, StatID.MaxHP),
                "PlayerB MaxHP must be 200.");
        }

        [Test]
        public void CharacterStats_TwoEntities_WriteOrderDoesNotAffectIsolation()
        {
            // Arrange — write B before A (reversed order)
            _stats.SetBaseStat(_playerB, StatID.MaxHP, 200);
            _stats.SetBaseStat(_player,  StatID.MaxHP, 500);

            // Act & Assert — write order must not affect either entity's stored value
            Assert.AreEqual(500, _stats.GetBaseStat(_player,  StatID.MaxHP),
                "PlayerA MaxHP must be 500 regardless of write order.");
            Assert.AreEqual(200, _stats.GetBaseStat(_playerB, StatID.MaxHP),
                "PlayerB MaxHP must be 200 regardless of write order.");
        }
    }
}
