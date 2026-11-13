using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode unit tests for Enemy AI Story 001: mob definition, validator and registry
    /// (CR-AI-7, EC-AI-4, EC-AI-12, F-AI-1, AC-AI-7, AC-AI-8).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_Definition_Tests
    {
        private static MobDefinition ValidDefinition(uint id)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(id),
                displayName: "Test Mob",
                baseMaxHP: 100,
                baseAttackPower: 10,
                baseDefense: 5,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: 10,
                mobTier: MobTier.Common,
                enragedChance: 0.07f,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: 1);
        }

        // Copy of ValidDefinition(1) with one field replaced; field names match the MobDefinition properties.
        private static MobDefinition With(string field, double value)
        {
            float f = (float)value;
            int n = (int)value;
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u),
                displayName: "Test Mob",
                baseMaxHP: field == "BaseMaxHP" ? n : 100,
                baseAttackPower: field == "BaseAttackPower" ? n : 10,
                baseDefense: field == "BaseDefense" ? n : 5,
                attackRange: field == "AttackRange" ? f : 2f,
                moveSpeed: field == "MoveSpeed" ? f : 3.5f,
                aggroRange: field == "AggroRange" ? f : 8f,
                leashRange: field == "LeashRange" ? f : 20f,
                attackWindUpTicks: field == "AttackWindUpTicks" ? n : 12,
                attackRecoveryTicks: field == "AttackRecoveryTicks" ? n : 6,
                attackCooldownTicks: field == "AttackCooldownTicks" ? n : 10,
                killXP: field == "KillXP" ? n : 10,
                mobTier: field == "MobTier" ? (MobTier)n : MobTier.Common,
                enragedChance: field == "EnragedChance" ? f : 0.07f,
                enragedMaxHPMultiplier: field == "EnragedMaxHPMultiplier" ? f : 1.5f,
                enragedAttackPowerMultiplier: field == "EnragedAttackPowerMultiplier" ? f : 1.3f,
                enragedXPMultiplier: field == "EnragedXPMultiplier" ? f : 1.5f,
                enragedLootQualityBonusTier: field == "EnragedLootQualityBonusTier" ? n : 1);
        }

        private static MobDefinition WithTimings(int windUp, int recovery, int cooldown)
        {
            return new MobDefinition(
                new MobTypeID(1u), "Test Mob", 100, 10, 5, 2f, 3.5f, 8f, 20f,
                windUp, recovery, cooldown, 10, MobTier.Common, 0.07f, 1.5f, 1.3f, 1.5f, 1);
        }

        private static IReadOnlyList<MobDefinition> One(MobDefinition def)
        {
            return new List<MobDefinition> { def };
        }

        private static void AssertOneIssueForField(List<MobDefinitionValidationIssue> issues, string field)
        {
            Assert.AreEqual(1, issues.Count, "expected exactly one issue");
            StringAssert.StartsWith(field + ":", issues[0].Message);
        }

        // -----------------------------------------------------------------------
        // AC-1 (AC-AI-7): wind-up minimum
        // -----------------------------------------------------------------------

        [Test]
        public void test_windUp_11_oneIssueAndRefusedWithOneError()
        {
            // Arrange
            MobDefinition def = With("AttackWindUpTicks", 11);
            LogAssert.Expect(LogType.Error, new Regex(@"MobDefinition refused: MobTypeID\(1\) AttackWindUpTicks"));

            // Act
            var issues = MobDefinitionValidator.Validate(def);
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            AssertOneIssueForField(issues, "AttackWindUpTicks");
            Assert.IsNull(registry.GetDefinition(new MobTypeID(1u)));
            Assert.AreEqual(0, registry.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_windUp_12_acceptedAndRetrievable()
        {
            // Arrange
            MobDefinition def = With("AttackWindUpTicks", 12);

            // Act
            var issues = MobDefinitionValidator.Validate(def);
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.AreEqual(0, issues.Count);
            Assert.AreSame(def, registry.GetDefinition(new MobTypeID(1u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_minWindUpTicks_derivedFromTickRate_is12At20Hz()
        {
            // Arrange / Act / Assert
            Assert.AreEqual(12, MobDefinitionValidator.MIN_WIND_UP_TICKS);
        }

        // -----------------------------------------------------------------------
        // AC-2 (AC-AI-8, EC-AI-4, EC-AI-12): leash gap
        // -----------------------------------------------------------------------

        [TestCase(0f, false)]
        [TestCase(1.99f, false)]
        [TestCase(2.0f, true)]
        public void test_leashGap_offsetFromAggro_acceptedOnlyAtTwoMetres(float offset, bool accepted)
        {
            // Arrange (AggroRange is 8 in the factory)
            MobDefinition def = With("LeashRange", 8.0 + offset);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            if (accepted)
            {
                Assert.AreEqual(0, issues.Count);
            }
            else
            {
                AssertOneIssueForField(issues, "LeashRange");
            }
        }

        // -----------------------------------------------------------------------
        // AC-3: one refusal per rule
        // -----------------------------------------------------------------------

        [TestCase("MoveSpeed", 0.0)]
        [TestCase("AggroRange", 0.0)]
        [TestCase("AttackRange", 0.49)]
        [TestCase("BaseMaxHP", 0.0)]
        [TestCase("BaseAttackPower", 0.0)]
        [TestCase("BaseDefense", -1.0)]
        [TestCase("AttackRecoveryTicks", -1.0)]
        [TestCase("KillXP", 0.0)]
        [TestCase("EnragedChance", -0.01)]
        [TestCase("EnragedChance", 1.01)]
        [TestCase("EnragedLootQualityBonusTier", -1.0)]
        [TestCase("EnragedLootQualityBonusTier", 3.0)]
        [TestCase("EnragedMaxHPMultiplier", 0.99)]
        [TestCase("EnragedMaxHPMultiplier", 3.01)]
        [TestCase("EnragedAttackPowerMultiplier", 0.99)]
        [TestCase("EnragedAttackPowerMultiplier", 2.01)]
        [TestCase("EnragedXPMultiplier", 0.99)]
        [TestCase("EnragedXPMultiplier", 3.01)]
        public void test_ruleViolation_exactlyOneIssueForThatField(string field, double value)
        {
            // Arrange
            MobDefinition def = With(field, value);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            AssertOneIssueForField(issues, field);
        }

        [TestCase("AttackRange", double.NaN)]
        [TestCase("AttackRange", double.PositiveInfinity)]
        [TestCase("MoveSpeed", double.NaN)]
        [TestCase("MoveSpeed", double.PositiveInfinity)]
        [TestCase("AggroRange", double.NaN)]
        [TestCase("AggroRange", double.PositiveInfinity)]
        [TestCase("LeashRange", double.NaN)]
        [TestCase("LeashRange", double.PositiveInfinity)]
        [TestCase("EnragedChance", double.NaN)]
        [TestCase("EnragedChance", double.PositiveInfinity)]
        [TestCase("EnragedMaxHPMultiplier", double.NaN)]
        [TestCase("EnragedMaxHPMultiplier", double.PositiveInfinity)]
        [TestCase("EnragedAttackPowerMultiplier", double.NaN)]
        [TestCase("EnragedAttackPowerMultiplier", double.PositiveInfinity)]
        [TestCase("EnragedXPMultiplier", double.NaN)]
        [TestCase("EnragedXPMultiplier", double.PositiveInfinity)]
        [TestCase("AttackRange", double.NegativeInfinity)]
        [TestCase("MoveSpeed", double.NegativeInfinity)]
        [TestCase("AggroRange", double.NegativeInfinity)]
        [TestCase("LeashRange", double.NegativeInfinity)]
        [TestCase("EnragedChance", double.NegativeInfinity)]
        [TestCase("EnragedMaxHPMultiplier", double.NegativeInfinity)]
        [TestCase("EnragedAttackPowerMultiplier", double.NegativeInfinity)]
        [TestCase("EnragedXPMultiplier", double.NegativeInfinity)]
        public void test_nonFiniteFloat_exactlyOneIssueForThatField(string field, double value)
        {
            // Arrange
            MobDefinition def = With(field, value);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            AssertOneIssueForField(issues, field);
        }

        [TestCase("AttackRange", 0.5)]
        [TestCase("BaseDefense", 0.0)]
        [TestCase("AttackRecoveryTicks", 0.0)]
        [TestCase("EnragedChance", 0.0)]
        [TestCase("EnragedChance", 1.0)]
        [TestCase("EnragedMaxHPMultiplier", 1.0)]
        [TestCase("EnragedMaxHPMultiplier", 3.0)]
        [TestCase("EnragedAttackPowerMultiplier", 1.0)]
        [TestCase("EnragedAttackPowerMultiplier", 2.0)]
        [TestCase("EnragedXPMultiplier", 1.0)]
        [TestCase("EnragedXPMultiplier", 3.0)]
        [TestCase("EnragedLootQualityBonusTier", 0.0)]
        [TestCase("EnragedLootQualityBonusTier", 2.0)]
        public void test_boundaryValue_accepted(string field, double value)
        {
            // Arrange
            MobDefinition def = With(field, value);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void test_invalidMobTypeId_oneIssueAndRefused()
        {
            // Arrange
            MobDefinition def = ValidDefinition(0u);
            LogAssert.Expect(LogType.Error, new Regex(@"MobDefinition refused: MobTypeID\(0\) MobTypeID"));

            // Act
            var issues = MobDefinitionValidator.Validate(def);
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            AssertOneIssueForField(issues, "MobTypeID");
            Assert.AreEqual(0, registry.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_nullDefinition_oneIssueAndRefused()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(0\) Definition: definition is null"));

            // Act
            var issues = MobDefinitionValidator.Validate(null);
            var registry = MobDefinitionRegistry.Build(new List<MobDefinition> { null });

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(0, registry.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_twoViolations_twoIssuesAndOneErrorLog()
        {
            // Arrange
            MobDefinition def = new MobDefinition(
                new MobTypeID(1u), "Test Mob", 0, 10, 5, 2f, 0f, 8f, 20f,
                12, 6, 10, 10, MobTier.Common, 0.07f, 1.5f, 1.3f, 1.5f, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"MobDefinition refused: MobTypeID\(1\) .*BaseMaxHP.*MoveSpeed|MobDefinition refused: MobTypeID\(1\) .*MoveSpeed.*BaseMaxHP"));

            // Act
            var issues = MobDefinitionValidator.Validate(def);
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.AreEqual(2, issues.Count);
            Assert.AreEqual(0, registry.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // AC-4: duplicate id
        // -----------------------------------------------------------------------

        [Test]
        public void test_duplicateMobTypeId_firstKeptSecondRefusedWithOneError()
        {
            // Arrange
            MobDefinition first = ValidDefinition(7u);
            MobDefinition second = ValidDefinition(7u);
            LogAssert.Expect(LogType.Error, new Regex(@"MobDefinition refused: MobTypeID\(7\) MobTypeID: duplicate"));
            var issuesOut = new List<MobDefinitionValidationIssue>();

            // Act
            var registry = MobDefinitionRegistry.Build(new List<MobDefinition> { first, second }, issuesOut);

            // Assert
            Assert.AreEqual(1, registry.Count);
            Assert.AreSame(first, registry.GetDefinition(new MobTypeID(7u)));
            Assert.AreEqual(1, issuesOut.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_twoDistinctValidDefinitions_bothRegistered()
        {
            // Arrange
            MobDefinition a = ValidDefinition(1u);
            MobDefinition b = ValidDefinition(2u);

            // Act
            var registry = MobDefinitionRegistry.Build(new List<MobDefinition> { a, b });

            // Assert
            Assert.AreEqual(2, registry.Count);
            Assert.AreSame(a, registry.GetDefinition(new MobTypeID(1u)));
            Assert.AreSame(b, registry.GetDefinition(new MobTypeID(2u)));
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // AC-5: unknown id
        // -----------------------------------------------------------------------

        [Test]
        public void test_getDefinition_unknownAndInvalidId_returnNull()
        {
            // Arrange
            var registry = MobDefinitionRegistry.Build(One(ValidDefinition(1u)));

            // Act / Assert
            Assert.IsNull(registry.GetDefinition(new MobTypeID(99u)));
            Assert.IsNull(registry.GetDefinition(MobTypeID.Invalid));
        }

        // -----------------------------------------------------------------------
        // AC-6: cooldown recommendation
        // -----------------------------------------------------------------------

        [Test]
        public void test_cooldown_9_acceptedWithOneWarning()
        {
            // Arrange
            MobDefinition def = With("AttackCooldownTicks", 9);
            LogAssert.Expect(LogType.Warning, new Regex(@"MobDefinition MobTypeID\(1\): AttackCooldownTicks 9"));

            // Act
            var issues = MobDefinitionValidator.Validate(def);
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.AreEqual(0, issues.Count);
            Assert.AreSame(def, registry.GetDefinition(new MobTypeID(1u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cooldown_10_acceptedWithoutWarning()
        {
            // Arrange
            MobDefinition def = With("AttackCooldownTicks", 10);

            // Act
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.IsFalse(MobDefinitionValidator.HasCooldownWarning(def));
            Assert.AreSame(def, registry.GetDefinition(new MobTypeID(1u)));
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // AC-7 (F-AI-1, AC-AI-20a): effective attack interval
        // -----------------------------------------------------------------------

        [TestCase(12, 6, 10, 22)]
        [TestCase(12, 6, 4, 18)]
        [TestCase(12, 0, 10, 22)]
        [TestCase(12, 6, 6, 18)]
        public void test_effectiveAttackIntervalTicks_matchesFormula(int windUp, int recovery, int cooldown, int expected)
        {
            // Arrange
            MobDefinition def = WithTimings(windUp, recovery, cooldown);

            // Act
            int interval = MobDefinition.EffectiveAttackIntervalTicks(def);

            // Assert
            Assert.AreEqual(expected, interval);
        }

        // -----------------------------------------------------------------------
        // Code-review additions
        // -----------------------------------------------------------------------

        [Test]
        public void test_constructor_all_fields_round_trip_through_properties()
        {
            // Arrange / Act
            var def = new MobDefinition(
                mobTypeID: new MobTypeID(7u),
                displayName: "Orc",
                baseMaxHP: 101,
                baseAttackPower: 102,
                baseDefense: 103,
                attackRange: 2.5f,
                moveSpeed: 3.25f,
                aggroRange: 8.5f,
                leashRange: 21.5f,
                attackWindUpTicks: 13,
                attackRecoveryTicks: 7,
                attackCooldownTicks: 11,
                killXP: 104,
                mobTier: MobTier.Rare,
                enragedChance: 0.25f,
                enragedMaxHPMultiplier: 1.4f,
                enragedAttackPowerMultiplier: 1.6f,
                enragedXPMultiplier: 1.8f,
                enragedLootQualityBonusTier: 2);

            // Assert
            Assert.AreEqual(new MobTypeID(7u), def.MobTypeID);
            Assert.AreEqual("Orc", def.DisplayName);
            Assert.AreEqual(101, def.BaseMaxHP);
            Assert.AreEqual(102, def.BaseAttackPower);
            Assert.AreEqual(103, def.BaseDefense);
            Assert.AreEqual(2.5f, def.AttackRange);
            Assert.AreEqual(3.25f, def.MoveSpeed);
            Assert.AreEqual(8.5f, def.AggroRange);
            Assert.AreEqual(21.5f, def.LeashRange);
            Assert.AreEqual(13, def.AttackWindUpTicks);
            Assert.AreEqual(7, def.AttackRecoveryTicks);
            Assert.AreEqual(11, def.AttackCooldownTicks);
            Assert.AreEqual(104, def.KillXP);
            Assert.AreEqual(MobTier.Rare, def.MobTier);
            Assert.AreEqual(0.25f, def.EnragedChance);
            Assert.AreEqual(1.4f, def.EnragedMaxHPMultiplier);
            Assert.AreEqual(1.6f, def.EnragedAttackPowerMultiplier);
            Assert.AreEqual(1.8f, def.EnragedXPMultiplier);
            Assert.AreEqual(2, def.EnragedLootQualityBonusTier);
        }

        [Test]
        public void test_build_fatal_definition_with_short_cooldown_logs_one_error_and_no_warning()
        {
            // Arrange
            MobDefinition def = new MobDefinition(
                new MobTypeID(1u), "Test Mob", 0, 10, 5, 2f, 3.5f, 8f, 20f,
                12, 6, 9, 10, MobTier.Common, 0.07f, 1.5f, 1.3f, 1.5f, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"refused.*BaseMaxHP"));

            // Act
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.AreEqual(0, registry.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_build_null_list_returns_empty_registry_without_logging()
        {
            // Arrange
            var issuesOut = new List<MobDefinitionValidationIssue>();

            // Act
            MobDefinitionRegistry withoutOut = null;
            MobDefinitionRegistry withOut = null;
            Assert.DoesNotThrow(() => withoutOut = MobDefinitionRegistry.Build(null, null));
            Assert.DoesNotThrow(() => withOut = MobDefinitionRegistry.Build(null, issuesOut));

            // Assert
            Assert.AreEqual(0, withoutOut.Count);
            Assert.AreEqual(0, withOut.Count);
            Assert.AreEqual(0, issuesOut.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_build_issues_out_appends_every_issue_with_field_and_type_id()
        {
            // Arrange
            var sentinel = new MobDefinitionValidationIssue(new MobTypeID(99u), "Sentinel: pre-existing.");
            var issuesOut = new List<MobDefinitionValidationIssue> { sentinel };
            var broken = new MobDefinition(
                new MobTypeID(2u), "Test Mob", 0, 10, 5, 2f, 0f, 8f, 20f,
                12, 6, 10, 10, MobTier.Common, 0.07f, 1.5f, 1.3f, 1.5f, 1);
            var input = new List<MobDefinition> { ValidDefinition(1u), broken, ValidDefinition(1u) };
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(2\) "));
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(1\) MobTypeID: duplicate"));

            // Act
            var registry = MobDefinitionRegistry.Build(input, issuesOut);

            // Assert
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(4, issuesOut.Count);
            Assert.AreEqual(sentinel.Message, issuesOut[0].Message);
            Assert.AreEqual(new MobTypeID(2u), issuesOut[1].MobTypeId);
            StringAssert.StartsWith("BaseMaxHP:", issuesOut[1].Message);
            Assert.AreEqual(new MobTypeID(2u), issuesOut[2].MobTypeId);
            StringAssert.StartsWith("MoveSpeed:", issuesOut[2].Message);
            Assert.AreEqual(new MobTypeID(1u), issuesOut[3].MobTypeId);
            StringAssert.StartsWith("MobTypeID:", issuesOut[3].Message);
            StringAssert.Contains("duplicate", issuesOut[3].Message);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("BaseMaxHP", 0.0)]
        [TestCase("MoveSpeed", 0.0)]
        [TestCase("AttackRange", 0.49)]
        [TestCase("EnragedChance", 1.01)]
        [TestCase("MoveSpeed", double.NaN)]
        [TestCase("MobTier", 99.0)]
        public void test_build_rule_violation_refused_with_one_error_log(string field, double value)
        {
            // Arrange
            MobDefinition def = With(field, value);
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(1\) " + field + ":"));

            // Act
            var registry = MobDefinitionRegistry.Build(One(def));

            // Assert
            Assert.AreEqual(0, registry.Count);
            Assert.IsNull(registry.GetDefinition(new MobTypeID(1u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_build_three_same_id_definitions_first_kept_two_errors()
        {
            // Arrange
            MobDefinition first = ValidDefinition(7u);
            var input = new List<MobDefinition> { first, ValidDefinition(7u), ValidDefinition(7u) };
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(7\) MobTypeID: duplicate"));
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(7\) MobTypeID: duplicate"));
            var issuesOut = new List<MobDefinitionValidationIssue>();

            // Act
            var registry = MobDefinitionRegistry.Build(input, issuesOut);

            // Assert
            Assert.AreEqual(1, registry.Count);
            Assert.AreSame(first, registry.GetDefinition(new MobTypeID(7u)));
            Assert.AreEqual(2, issuesOut.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_build_null_element_between_valid_definitions_others_registered()
        {
            // Arrange
            MobDefinition a = ValidDefinition(1u);
            MobDefinition b = ValidDefinition(2u);
            var input = new List<MobDefinition> { a, null, b };
            LogAssert.Expect(LogType.Error, new Regex(@"refused: MobTypeID\(0\) Definition: definition is null"));

            // Act
            var registry = MobDefinitionRegistry.Build(input);

            // Assert
            Assert.AreEqual(2, registry.Count);
            Assert.AreSame(a, registry.GetDefinition(new MobTypeID(1u)));
            Assert.AreSame(b, registry.GetDefinition(new MobTypeID(2u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_values_just_above_zero_accepted()
        {
            // Arrange
            var def = new MobDefinition(
                new MobTypeID(1u), "Test Mob", 1, 1, 5, 2f, 0.01f, 0.01f, 20f,
                12, 6, 10, 1, MobTier.Common, 0.07f, 1.5f, 1.3f, 1.5f, 1);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void test_mob_definition_is_sealed_class_without_loot_members()
        {
            // Arrange
            Type type = typeof(MobDefinition);
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            // Act
            var names = new List<string>();
            foreach (MemberInfo member in type.GetMembers(all))
            {
                names.Add(member.Name);
            }

            // Assert
            Assert.IsTrue(type.IsClass);
            Assert.IsTrue(type.IsSealed);
            CollectionAssert.DoesNotContain(names, "LootTableRef");
            CollectionAssert.DoesNotContain(names, "GoldMin");
            CollectionAssert.DoesNotContain(names, "GoldMax");
        }

        [Test]
        public void test_mob_tier_values_are_zero_to_three_in_order()
        {
            // Arrange / Act / Assert
            Assert.AreEqual(0, (int)MobTier.Common);
            Assert.AreEqual(1, (int)MobTier.Uncommon);
            Assert.AreEqual(2, (int)MobTier.Rare);
            Assert.AreEqual(3, (int)MobTier.Elite);
        }

        [TestCase(99)]
        [TestCase(-1)]
        public void test_validate_undefined_mob_tier_one_issue(int tier)
        {
            // Arrange
            MobDefinition def = With("MobTier", tier);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            AssertOneIssueForField(issues, "MobTier");
        }

        [Test]
        public void test_validate_elite_mob_tier_boundary_accepted()
        {
            // Arrange
            MobDefinition def = With("MobTier", 3);

            // Act
            var issues = MobDefinitionValidator.Validate(def);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void test_has_cooldown_warning_9_true_10_false_null_false()
        {
            // Arrange / Act / Assert
            Assert.IsTrue(MobDefinitionValidator.HasCooldownWarning(With("AttackCooldownTicks", 9)));
            Assert.IsFalse(MobDefinitionValidator.HasCooldownWarning(With("AttackCooldownTicks", 10)));
            Assert.IsFalse(MobDefinitionValidator.HasCooldownWarning(null));
        }
    }
}
