using System;
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
    /// EditMode unit tests for Damage Calculation Story 002 - elemental weapon lookup (Steps 4-5),
    /// elemental mitigation (F-DC-2), the pre-crit sum and the elemental fields of the result.
    /// Every case has no crit (crit is Story 003).
    /// </summary>
    [TestFixture]
    internal sealed class DamageCalculation_Elemental_Tests
    {
        private const uint ATTACKER_RAW_ID = 1001u;
        private const uint TARGET_RAW_ID = 2001u;
        private const int TARGET_SETUP_MAX_HP = 99999; // StatSchema MaxHP ceiling
        private const uint WEAPON_RAW_ITEM_ID = 701u;
        private const uint MISSING_RAW_ITEM_ID = 799u;
        private const int MAX_DEFENSE = 9999;
        private const int MAGIC_DEFENSE_SAMPLE = 8;
        private const int BASE_ELEMENTAL_50 = 50;
        private const int LEVEL_ZERO = 0;
        private const byte ENHANCED_LEVEL = 5;
        private const float DEFAULT_MIN_DAMAGE_FRACTION = 0.05f;
        private const float DEFAULT_K_MAGIC = 200f;
        private const float DEFAULT_MIN_ELEMENTAL_FRACTION = 0.10f;
        private const int DEFAULT_MAX_BASE_DAMAGE = 99999;
        private const float CUSTOM_K_MAGIC = 100f;
        private const int CUSTOM_K_MAGIC_MAGIC_DEFENSE = 100;
        private const float CUSTOM_MIN_ELEMENTAL_FRACTION = 0.5f;
        private const int CONTRACT_LEVEL = 7;
        private const int CONTRACT_BONUS_RETURNED = 80;
        private const int DEFENSE_394 = 394;
        private const int BASE_DAMAGE_100 = 100;
        private const int BASE_DAMAGE_768 = 768;
        private const int PHYSICAL_374 = 374;
        private const int ELEMENTAL_48 = 48;

        private static readonly EntityID Attacker = new EntityID(ATTACKER_RAW_ID);
        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly ItemID WeaponId = new ItemID(WEAPON_RAW_ITEM_ID);

        private IronGrind.CharacterStats.CharacterStats _stats;
        private FakeEquippedWeaponQuery _weapons;
        private CountingItemDatabase _items;
        private List<ItemDefinition> _createdDefinitions;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.Create();
            // Story 004: keep the target alive and out of reach so no case logs the dead-entity error or
            // flips IsKill. MaxHP is set first because SetCurrentHP clamps to the effective MaxHP.
            _stats.SetBaseStat(Target, StatID.MaxHP, TARGET_SETUP_MAX_HP);
            _stats.SetCurrentHP(Target, TARGET_SETUP_MAX_HP);
            _weapons = new FakeEquippedWeaponQuery();
            _items = new CountingItemDatabase();
            _createdDefinitions = new List<ItemDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _createdDefinitions)
                UnityEngine.Object.DestroyImmediate(definition);
            _createdDefinitions.Clear();
        }

        // ---------- helpers ----------

        /// <summary>The single place the calculator is built; defaults to the real bonus provider.</summary>
        private DamageCalculator CreateCalculator(
            IEnhancementBonusProvider bonuses = null, DamageCalculationConfig config = null)
        {
            return new DamageCalculator(
                _stats,
                _weapons,
                _items,
                bonuses ?? new EnhancementBonusProvider(EnhancementConfig.Default),
                config ?? DamageCalculationConfig.Default);
        }

        /// <summary>Registers a weapon with the given element and equips it on the attacker.</summary>
        private void GivenWeapon(
            ElementType element, int baseElementalDamage, GearTier tier = GearTier.Bronze, byte level = LEVEL_ZERO)
        {
            ItemDefinition weapon = ItemDefinitionBuilder.Build(
                WEAPON_RAW_ITEM_ID,
                "TestWeapon",
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, tier, elementType: element, elementalDamage: baseElementalDamage));
            _createdDefinitions.Add(weapon);
            _items.Add(weapon);
            _weapons.WeaponId = WeaponId;
            _weapons.EnhancementLevel = level;
        }

        private void GivenTargetDefenses(int defense, int magicDefense)
        {
            _stats.SetBaseStat(Target, StatID.Defense, defense);
            _stats.SetBaseStat(Target, StatID.MagicDefense, magicDefense);
        }

        private static void AssertNoElemental(DamageResult result)
        {
            Assert.AreEqual(0, result.ElementalDamage);
            Assert.IsFalse(result.HasElementalContribution);
        }

        // ---------- elemental mitigation (F-DC-2) ----------

        [Test]
        public void DamageCalculation_Calculate_Fire50MagicDefense8_ElementalDamage48() // AC-DC-F-04
        {
            // Arrange - 50 x max(0.10, 1 - 8/208) = 48.08, floored to 48
            GivenTargetDefenses(0, MAGIC_DEFENSE_SAMPLE);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(ELEMENTAL_48, result.ElementalDamage);
            Assert.IsTrue(result.HasElementalContribution);
        }

        [Test]
        public void DamageCalculation_Calculate_Base100MagicDefense9999_MinElementalFractionGives10() // AC-DC-F-05
        {
            // Arrange - 100 x max(0.10, 1 - 9999/10199 = 0.0196) = 10
            GivenTargetDefenses(0, MAX_DEFENSE);
            GivenWeapon(ElementType.Fire, 100);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(10, result.ElementalDamage);
            Assert.IsTrue(result.HasElementalContribution);
        }

        [Test]
        public void DamageCalculation_Calculate_NoWeaponEquipped_NoElementalAndDatabaseNotTouched() // AC-DC-F-06
        {
            // Arrange - the weapon query returns ItemID.Invalid by default
            GivenTargetDefenses(0, MAGIC_DEFENSE_SAMPLE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertNoElemental(result);
            Assert.AreEqual(0, _items.GetItemCallCount);
            Assert.AreEqual(0, _items.TryGetItemCallCount);
        }

        [Test]
        public void DamageCalculation_Calculate_Base768Defense394Fire50_PhysicalElementalAndFinalSplit() // AC-DC-F-08, AC-DC-F-12
        {
            // Arrange - physical 768 - 394 = 374; elemental 48.08; sum 422.08 floors to 422
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(ELEMENTAL_48, result.ElementalDamage);
            Assert.AreEqual(422, result.FinalDamage);
            Assert.IsFalse(result.IsCrit);
        }

        [Test]
        public void DamageCalculation_Calculate_FractionalComponents_FinalSumsBeforeFlooring() // AC-DC-F-12b
        {
            // Arrange - physical: max(7492 x 0.05, 7492 - 9999) = 374.6
            //           elemental: 486 x max(0.10, 1 - 9999/10199) = 486 x 0.10 = 48.6
            //           sum 423.2 floors to 423, while the floored parts give 374 + 48 = 422
            GivenTargetDefenses(MAX_DEFENSE, MAX_DEFENSE);
            GivenWeapon(ElementType.Fire, 486);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(7492, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(423, result.FinalDamage);
            Assert.AreEqual(374, result.PhysicalDamage);
            Assert.AreEqual(48, result.ElementalDamage);
            Assert.AreEqual(422, result.PhysicalDamage + result.ElementalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_CustomKMagic_UsesConfigValue() // K_MAGIC from config
        {
            // Arrange - KMagic 100: 60 x max(0.10, 1 - 100/200) = 30 (the default 200 would give 40)
            GivenTargetDefenses(0, CUSTOM_K_MAGIC_MAGIC_DEFENSE);
            GivenWeapon(ElementType.Fire, 60);
            var config = new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, CUSTOM_K_MAGIC, DEFAULT_MIN_ELEMENTAL_FRACTION, DEFAULT_MAX_BASE_DAMAGE);
            DamageCalculator calculator = CreateCalculator(config: config);

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(30, result.ElementalDamage);
            Assert.AreEqual(BASE_DAMAGE_100 + 30, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_CustomMinElementalFraction_UsesConfigValue() // MIN_ELEMENTAL_FRACTION from config
        {
            // Arrange - floor 0.5: 100 x max(0.5, 0.0196) = 50 (the default 0.10 would give 10)
            GivenTargetDefenses(0, MAX_DEFENSE);
            GivenWeapon(ElementType.Fire, 100);
            var config = new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, DEFAULT_K_MAGIC, CUSTOM_MIN_ELEMENTAL_FRACTION, DEFAULT_MAX_BASE_DAMAGE);
            DamageCalculator calculator = CreateCalculator(config: config);

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(50, result.ElementalDamage);
            Assert.AreEqual(BASE_DAMAGE_100 + 50, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_TargetWithoutStatRecord_MagicDefenseZero()
        {
            // Arrange - the target has no stat record: Defense 0 and MagicDefense 0
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(BASE_ELEMENTAL_50, result.ElementalDamage);
            Assert.AreEqual(BASE_DAMAGE_100 + BASE_ELEMENTAL_50, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_RealProviderAtPlus5_ElementalUsesEnhancedBonus() // F-ENH-2 end to end
        {
            // Arrange - Bronze Fire 50 at +5: 50 + 5 x ELEMENTAL_BONUS_PER_LEVEL_BRONZE
            int expectedBonus = BASE_ELEMENTAL_50
                + ENHANCED_LEVEL * EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_BRONZE;
            GivenTargetDefenses(0, 0);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50, GearTier.Bronze, ENHANCED_LEVEL);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.Greater(expectedBonus, BASE_ELEMENTAL_50);
            Assert.AreEqual(expectedBonus, result.ElementalDamage);
            Assert.AreEqual(BASE_DAMAGE_100 + expectedBonus, result.FinalDamage);
        }

        // ---------- edge cases ----------

        [Test]
        public void DamageCalculation_Calculate_ProviderReturnsNegative_NoElementalContribution()
        {
            // Arrange - a negative bonus counts as 0 and never reduces physical damage
            GivenTargetDefenses(0, 0);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            var provider = new RecordingBonusProvider { ElementalBonusToReturn = -5 };
            DamageCalculator calculator = CreateCalculator(provider);

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertNoElemental(result);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_DatabaseReturnsTrueWithNullItem_PhysicalReturnedAndErrorLogged()
        {
            // Arrange - the database reports the id as found but hands back no definition
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            _items.AddFoundButNull(WeaponId);
            _weapons.WeaponId = WeaponId;
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("missing from the Item Database or is not equipment"));

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(PHYSICAL_374, result.FinalDamage);
            AssertNoElemental(result);
            Assert.AreEqual(0, _weapons.EnhancementLevelCallCount);
        }

        [Test]
        public void DamageCalculation_Calculate_FireWeaponWithBaseZero_NoElementalAndNoError() // AC-DC-E-04
        {
            // Arrange
            GivenTargetDefenses(0, MAGIC_DEFENSE_SAMPLE);
            GivenWeapon(ElementType.Fire, 0);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertNoElemental(result);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DamageCalculation_Calculate_Fire9MagicDefense9999_ZeroDamageButContributionTrue() // AC-DC-E-06
        {
            // Arrange - 9 x 0.10 = 0.9: floors to 0 but is above zero
            GivenTargetDefenses(0, MAX_DEFENSE);
            GivenWeapon(ElementType.Fire, 9);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(0, result.ElementalDamage);
            Assert.IsTrue(result.HasElementalContribution);
        }

        [Test]
        public void DamageCalculation_Calculate_NonElementalWeapon_NoElementalAndNothingElseConsulted() // Non-elemental weapon
        {
            // Arrange
            GivenTargetDefenses(0, MAGIC_DEFENSE_SAMPLE);
            GivenWeapon(ElementType.None, BASE_ELEMENTAL_50);
            var provider = new RecordingBonusProvider { ElementalBonusToReturn = CONTRACT_BONUS_RETURNED };
            DamageCalculator calculator = CreateCalculator(provider);

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertNoElemental(result);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
            Assert.AreEqual(0, provider.ElementalCallCount);
            Assert.AreEqual(0, _weapons.EnhancementLevelCallCount);
        }

        [Test]
        public void DamageCalculation_Calculate_ElementalWeapon_ProviderReceivesWeaponDataOnceAndDrivesDamage() // Bonus provider contract
        {
            // Arrange
            GivenTargetDefenses(0, 0);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50, GearTier.Steel, CONTRACT_LEVEL);
            var provider = new RecordingBonusProvider { ElementalBonusToReturn = CONTRACT_BONUS_RETURNED };
            DamageCalculator calculator = CreateCalculator(provider);

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(1, _weapons.WeaponIdCallCount);
            Assert.AreEqual(1, _weapons.EnhancementLevelCallCount);
            Assert.AreEqual(Attacker, _weapons.LastWeaponIdEntity);
            Assert.AreEqual(Attacker, _weapons.LastEnhancementLevelEntity);
            Assert.AreEqual(1, provider.ElementalCallCount);
            Assert.AreEqual(CONTRACT_LEVEL, provider.LastLevel);
            Assert.AreEqual(BASE_ELEMENTAL_50, provider.LastBaseElementalDamage);
            Assert.AreEqual(GearTier.Steel, provider.LastGearTier);
            Assert.IsTrue(provider.LastIsWeapon);
            Assert.AreEqual(CONTRACT_BONUS_RETURNED, result.ElementalDamage);
            Assert.AreEqual(BASE_DAMAGE_100 + CONTRACT_BONUS_RETURNED, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_ProviderThrows_ExceptionPropagates() // Provider propagation
        {
            // Arrange
            GivenTargetDefenses(0, 0);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50, GearTier.Bronze, CONTRACT_LEVEL);
            var provider = new RecordingBonusProvider
            {
                ExceptionToThrow = new ArgumentOutOfRangeException("level")
            };
            DamageCalculator calculator = CreateCalculator(provider);

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => calculator.Calculate(BASE_DAMAGE_100, Attacker, Target, DamageContext.PhysicalAuto));
        }

        [Test]
        public void DamageCalculation_Calculate_WeaponNotInDatabase_PhysicalReturnedAndErrorLogged() // Item missing
        {
            // Arrange - the weapon query names an id the database does not know
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            _weapons.WeaponId = new ItemID(MISSING_RAW_ITEM_ID);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("missing from the Item Database or is not equipment"));

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(PHYSICAL_374, result.FinalDamage);
            AssertNoElemental(result);
            Assert.AreEqual(0, _weapons.EnhancementLevelCallCount);
        }

        [Test]
        public void DamageCalculation_Calculate_WeaponIsNotEquipment_PhysicalReturnedAndErrorLogged() // Item not equipment
        {
            // Arrange - a consumable (no equipment data) registered under the equipped id
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            ItemDefinition consumable = ItemDefinitionBuilder.Build(
                WEAPON_RAW_ITEM_ID, "NotAWeapon", ItemCategory.Consumable);
            _createdDefinitions.Add(consumable);
            _items.Add(consumable);
            _weapons.WeaponId = WeaponId;
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("missing from the Item Database or is not equipment"));

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(PHYSICAL_374, result.FinalDamage);
            AssertNoElemental(result);
            Assert.AreEqual(0, _weapons.EnhancementLevelCallCount);
        }

        // ---------- physical results unchanged ----------

        [Test]
        public void DamageCalculation_Calculate_NoWeapon_PhysicalResultUnchanged() // Physical unaffected
        {
            // Arrange
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(PHYSICAL_374, result.FinalDamage);
            AssertNoElemental(result);
        }

        // ---------- constructor null checks ----------

        [Test]
        public void DamageCalculator_Constructor_NullWeapons_Throws()
        {
            // Arrange / Act / Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new DamageCalculator(
                _stats, null, _items, new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default));
            Assert.AreEqual("weapons", ex.ParamName);
        }

        [Test]
        public void DamageCalculator_Constructor_NullItems_Throws()
        {
            // Arrange / Act / Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new DamageCalculator(
                _stats, _weapons, null, new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default));
            Assert.AreEqual("items", ex.ParamName);
        }

        [Test]
        public void DamageCalculator_Constructor_NullBonuses_Throws()
        {
            // Arrange / Act / Assert
            var ex = Assert.Throws<ArgumentNullException>(() => new DamageCalculator(
                _stats, _weapons, _items, null, DamageCalculationConfig.Default));
            Assert.AreEqual("bonuses", ex.ParamName);
        }
    }
}
