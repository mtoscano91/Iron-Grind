using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.DamageCalculation
{
    /// <summary>
    /// EditMode unit tests for Damage Calculation Story 004 - kill detection (Step 10), the dead-entity
    /// guard and the "report only" rule: the resolver never changes HP, awards XP or fires an event.
    /// Every case has no crit (crit is Story 003).
    /// </summary>
    [TestFixture]
    internal sealed class DamageCalculation_KillDetection_Tests
    {
        private const uint ATTACKER_RAW_ID = 1001u;
        private const uint TARGET_RAW_ID = 2001u;
        private const uint WEAPON_RAW_ITEM_ID = 701u;
        private const int TARGET_MAX_HP = 99999; // StatSchema MaxHP ceiling
        private const int ATTACKER_START_XP = 250;
        private const int BASE_ELEMENTAL_50 = 50;
        private const int MAGIC_DEFENSE_SAMPLE = 8;
        private const int DEFENSE_394 = 394;
        private const int DEFENSE_900 = 900;
        private const int LEVEL_BELOW_CAP = 1; // AddExperience is a no-op at the level cap
        private const string DEAD_ENTITY_ERROR = "dead-entity guard";

        private static readonly EntityID Attacker = new EntityID(ATTACKER_RAW_ID);
        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly ItemID WeaponId = new ItemID(WEAPON_RAW_ITEM_ID);

        private IronGrind.CharacterStats.CharacterStats _stats;
        private IronGrind.CharacterStats.CharacterStats.EntityDiedHandler _diedHandler;
        private FakeEquippedWeaponQuery _weapons;
        private CountingItemDatabase _items;
        private List<ItemDefinition> _createdDefinitions;
        private int _diedCount;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.CreateWithLeveling(new AllPlayersLevelingService());
            _weapons = new FakeEquippedWeaponQuery();
            _items = new CountingItemDatabase();
            _createdDefinitions = new List<ItemDefinition>();
            _diedCount = 0;
            _diedHandler = OnEntityDied;
            _stats.Subscribe(_diedHandler);
        }

        [TearDown]
        public void TearDown()
        {
            _stats?.Unsubscribe(_diedHandler);
            foreach (ItemDefinition definition in _createdDefinitions)
                UnityEngine.Object.DestroyImmediate(definition);
            _createdDefinitions.Clear();
        }

        // ---------- helpers ----------

        private void OnEntityDied(EntityID entityId)
        {
            _diedCount++;
        }

        /// <summary>The single place the calculator is built.</summary>
        private DamageCalculator CreateCalculator()
        {
            return new DamageCalculator(
                _stats,
                _weapons,
                _items,
                new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default);
        }

        /// <summary>Gives the target a MaxHP ceiling first, then the requested current HP (SetCurrentHP clamps).</summary>
        private void GivenTargetHp(float hp)
        {
            _stats.SetBaseStat(Target, StatID.MaxHP, TARGET_MAX_HP);
            _stats.SetCurrentHP(Target, hp);
        }

        private void GivenTargetDefenses(int defense, int magicDefense)
        {
            _stats.SetBaseStat(Target, StatID.Defense, defense);
            _stats.SetBaseStat(Target, StatID.MagicDefense, magicDefense);
        }

        /// <summary>
        /// Puts the attacker in a state where AddExperience would really add: a player (see SetUp),
        /// below the level cap, with a known Experience value.
        /// </summary>
        private void GivenAttackerExperience()
        {
            _stats.SetBaseStat(Attacker, StatID.Level, LEVEL_BELOW_CAP);
            _stats.SetBaseStat(Attacker, StatID.Experience, ATTACKER_START_XP);
        }

        /// <summary>Registers a Bronze weapon with the given element at +0 and equips it on the attacker.</summary>
        private void GivenWeapon(ElementType element, int baseElementalDamage)
        {
            ItemDefinition weapon = ItemDefinitionBuilder.Build(
                WEAPON_RAW_ITEM_ID,
                "TestWeapon",
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: element, elementalDamage: baseElementalDamage));
            _createdDefinitions.Add(weapon);
            _items.Add(weapon);
            _weapons.WeaponId = WeaponId;
            _weapons.EnhancementLevel = 0;
        }

        private DamageResult Hit(DamageCalculator calculator, int baseDamage)
        {
            return calculator.Calculate(baseDamage, Attacker, Target, DamageContext.PhysicalAuto);
        }

        // ---------- fixture controls: the side-effect assertions below can fail ----------

        [Test]
        public void DamageCalculation_StatsFixture_AddExperienceOnAttacker_IncreasesExperience()
        {
            // Arrange
            GivenAttackerExperience();

            // Act
            _stats.AddExperience(Attacker, 10);

            // Assert
            Assert.AreEqual(ATTACKER_START_XP + 10, _stats.GetBaseStat(Attacker, StatID.Experience));
        }

        [Test]
        public void DamageCalculation_StatsFixture_ApplyDamageKillingTarget_FiresEntityDiedOnce()
        {
            // Arrange
            GivenTargetHp(100f);

            // Act
            _stats.ApplyDamage(Target, 100f);

            // Assert
            Assert.AreEqual(1, _diedCount);
        }

        // ---------- kill threshold ----------

        [Test]
        public void DamageCalculation_Calculate_DamageEqualsHp_KillWithoutAwardOrEvent() // AC-DC-K-01
        {
            // Arrange
            GivenTargetHp(100f);
            GivenAttackerExperience();
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.AreEqual(100, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
            Assert.AreEqual(100f, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(0, _diedCount);
        }

        [Test]
        public void DamageCalculation_Calculate_DamageBelowHp_NoKill() // AC-DC-K-02
        {
            // Arrange
            GivenTargetHp(101f);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.AreEqual(100, result.FinalDamage);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_KillScenario_HpAndAttackerExperienceUnchanged() // AC-DC-K-03
        {
            // Arrange
            GivenTargetHp(100f);
            GivenAttackerExperience();
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 500);

            // Assert
            Assert.IsTrue(result.IsKill);
            Assert.AreEqual(100f, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(0, _diedCount);
        }

        [Test]
        public void DamageCalculation_Calculate_Hp50Damage50_KillAtEquality() // AC-DC-K-04
        {
            // Arrange
            GivenTargetHp(50f);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 50);

            // Assert
            Assert.AreEqual(50, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_FractionalHpAboveDamage_NoKill() // AC-DC-K-05
        {
            // Arrange - 100.5 HP: an int-floored HP read (100) would wrongly report a kill
            GivenTargetHp(100.5f);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.AreEqual(100, result.FinalDamage);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_FractionalHpBelowOne_KillWithFloorDamage() // AC-DC-K-07
        {
            // Arrange - Defense 1 on base 1 floors the final damage to 1
            GivenTargetDefenses(1, 0);
            GivenTargetHp(0.1f);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 1);

            // Assert
            Assert.AreEqual(1, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }

        // ---------- dead-entity guard ----------

        [Test]
        public void DamageCalculation_Calculate_TargetAtZeroHp_NoKillErrorLoggedDamageStillComputed() // AC-DC-K-06
        {
            // Arrange
            GivenTargetHp(0f);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex(DEAD_ENTITY_ERROR));

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual(100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_TargetWithNoHpRecord_BehavesLikeZeroHp() // AC-DC-K-06 edge
        {
            // Arrange - the target never had HP set: GetCurrentHP reads 0
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex(DEAD_ENTITY_ERROR));

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual(100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_TargetHpIsNaN_NoKillAndErrorLogged()
        {
            // Arrange - a corrupted HP pool must not pass the guard silently
            GivenTargetHp(float.NaN);
            Assume.That(float.IsNaN(_stats.GetCurrentHP(Target)), "CharacterStats no longer stores a NaN HP.");
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex(DEAD_ENTITY_ERROR));

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual(100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_TargetAtZeroHp_HpUnchangedAndNoEvent()
        {
            // Arrange
            GivenTargetHp(0f);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex(DEAD_ENTITY_ERROR));

            // Act
            Hit(calculator, 100);

            // Assert
            Assert.AreEqual(0f, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(0, _diedCount);
        }

        [Test]
        public void DamageCalculation_Calculate_RejectedCallOnZeroHpTarget_LogsOnlyRejection()
        {
            // Arrange - the rejection returns before Step 10, so the dead-entity guard is not reached
            GivenTargetHp(0f);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at least 1"));

            // Act
            DamageResult result = Hit(calculator, 0);

            // Assert
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual(0, result.FinalDamage);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- integration with the other steps ----------

        [Test]
        public void DamageCalculation_Calculate_Base768Defense394Fire50_AllFieldsAndNoKill() // AC-DC-I-03
        {
            // Arrange - physical 374, elemental 48.08, sum 422.08 floors to 422; HP 1000 survives
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            GivenTargetHp(1000f);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 768);

            // Assert
            Assert.AreEqual(374, result.PhysicalDamage);
            Assert.AreEqual(48, result.ElementalDamage);
            Assert.AreEqual(422, result.FinalDamage);
            Assert.IsFalse(result.IsCrit);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_Base768Defense900_FinalDamage38AndNoKill() // AC-DC-I-05
        {
            // Arrange - max(768 x 0.05 = 38.4, 768 - 900) floors to 38
            GivenTargetDefenses(DEFENSE_900, 0);
            GivenTargetHp(1000f);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 768);

            // Assert
            Assert.AreEqual(38, result.FinalDamage);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_Base500Hp100_KillReportedWithoutSideEffects() // AC-DC-I-06 (resolver half)
        {
            // Arrange
            GivenTargetDefenses(0, 0);
            GivenTargetHp(100f);
            GivenAttackerExperience();
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 500);

            // Assert
            Assert.IsTrue(result.IsKill);
            Assert.AreEqual(500, result.FinalDamage);
            Assert.AreEqual(100f, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(0, _diedCount);
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Attacker, StatID.Experience));
        }

        [Test]
        public void DamageCalculation_Calculate_ElementalDamageCompletesTheKill_KillTrue()
        {
            // Arrange - physical 100 alone would leave 40 HP; physical 100 + elemental 50 = 150 kills
            GivenTargetDefenses(0, 0);
            GivenTargetHp(140f);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = Hit(calculator, 100);

            // Assert
            Assert.AreEqual(100, result.PhysicalDamage);
            Assert.AreEqual(150, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }
    }
}
