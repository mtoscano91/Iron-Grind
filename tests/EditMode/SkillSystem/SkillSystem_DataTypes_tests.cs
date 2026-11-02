using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.LevelingSystem;
using IronGrind.SkillSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 001 - data types, class skill lists, SkillDatabase lookup and
    /// SkillSystemConfig clamping (AC-SK-33, AC-SK-35, EC-CS-8).
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_DataTypes_Tests
    {
        private const byte CLASS_WARRIOR = 0;
        private const byte CLASS_UNKNOWN = 200;
        private const uint SKILL_A = 3;
        private const uint SKILL_B = 7;
        private const uint SKILL_UNKNOWN = 99;
        private const float MULTIPLIER_ABOVE = 3.0f;
        private const float MULTIPLIER_BELOW = 0.1f;
        private const float MULTIPLIER_IN_RANGE = 1.5f;
        private const int TOLERANCE_ABOVE = 20;
        private const int TOLERANCE_BELOW = 1;
        private const int TOLERANCE_IN_RANGE = 7;

        private SkillDatabase _database;
        private SkillSystemConfig _config;

        [TearDown]
        public void TearDown()
        {
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }

            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
                _config = null;
            }
        }

        private static ClassRegistry RegistryWith(uint[] skillList)
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, skillList));
            return registry;
        }

        private static SkillDefinition Skill(uint id, string name)
        {
            return new SkillDefinition { ID = id, DisplayName = name };
        }

        private SkillDatabase DatabaseWith(params SkillDefinition[] skills)
        {
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(skills);
            return _database;
        }

        // ---- AC-SK-33: SkillID ----

        [Test]
        public void test_skillid_type_is_value_type()
        {
            Assert.That(typeof(SkillID).IsValueType, Is.True);
        }

        [Test]
        public void test_skillid_has_exactly_one_instance_field_of_type_uint()
        {
            FieldInfo[] fields = typeof(SkillID).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(fields.Length, Is.EqualTo(1));
            Assert.That(fields[0].FieldType, Is.EqualTo(typeof(uint)));
            Assert.That(fields[0].IsInitOnly, Is.True);
        }

        [Test]
        public void test_skillid_is_marked_readonly_struct()
        {
            Assert.That(typeof(SkillID).IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute)), Is.True);
        }

        [Test]
        public void test_skillid_invalid_has_value_zero_and_is_not_valid()
        {
            Assert.That(SkillID.Invalid.Value, Is.EqualTo(0u));
            Assert.That(SkillID.Invalid.IsValid, Is.False);
            Assert.That(new SkillID(0), Is.EqualTo(SkillID.Invalid));
            Assert.That(new SkillID(SKILL_A).IsValid, Is.True);
        }

        [Test]
        public void test_skillid_equality_and_hash_are_by_value()
        {
            var a = new SkillID(SKILL_A);
            var b = new SkillID(SKILL_A);
            var c = new SkillID(SKILL_B);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)c), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo(b.ToString()));
        }

        // ---- AC-SK-35: reflection ----

        [Test]
        public void test_classdefinition_skilllist_field_type_is_uint_array()
        {
            FieldInfo field = typeof(ClassDefinition).GetField("SkillList");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(uint[])));
        }

        [Test]
        public void test_iclassregistry_getclassskills_returns_ireadonlylist_of_skillid()
        {
            MethodInfo method = typeof(IClassRegistry).GetMethod("GetClassSkills");

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(IReadOnlyList<SkillID>)));
        }

        // ---- Empty list ----

        [Test]
        public void test_getclassskills_null_skilllist_returns_empty_not_null()
        {
            IReadOnlyList<SkillID> skills = RegistryWith(null).GetClassSkills(CLASS_WARRIOR);

            Assert.That(skills, Is.Not.Null);
            Assert.That(skills.Count, Is.EqualTo(0));
        }

        [Test]
        public void test_getclassskills_empty_array_returns_empty_not_null()
        {
            IReadOnlyList<SkillID> skills = RegistryWith(new uint[0]).GetClassSkills(CLASS_WARRIOR);

            Assert.That(skills, Is.Not.Null);
            Assert.That(skills.Count, Is.EqualTo(0));
        }

        [Test]
        public void test_getclassskills_unknown_class_returns_empty_not_null()
        {
            IReadOnlyList<SkillID> skills = RegistryWith(new[] { SKILL_A }).GetClassSkills(CLASS_UNKNOWN);

            Assert.That(skills, Is.Not.Null);
            Assert.That(skills.Count, Is.EqualTo(0));
        }

        // ---- Wrapping ----

        [Test]
        public void test_getclassskills_wraps_raw_values_in_order()
        {
            IReadOnlyList<SkillID> skills = RegistryWith(new[] { SKILL_A, SKILL_B }).GetClassSkills(CLASS_WARRIOR);

            Assert.That(skills.Count, Is.EqualTo(2));
            Assert.That(skills[0], Is.EqualTo(new SkillID(SKILL_A)));
            Assert.That(skills[1], Is.EqualTo(new SkillID(SKILL_B)));
        }

        [Test]
        public void test_getclassskills_repeated_calls_return_same_cached_instance()
        {
            ClassRegistry registry = RegistryWith(new[] { SKILL_A, SKILL_B });

            Assert.That(registry.GetClassSkills(CLASS_WARRIOR), Is.SameAs(registry.GetClassSkills(CLASS_WARRIOR)));
        }

        [Test]
        public void test_getclassskills_second_call_does_not_allocate()
        {
            ClassRegistry registry = RegistryWith(new[] { SKILL_A, SKILL_B });
            registry.GetClassSkills(CLASS_WARRIOR);

            Assert.That(() => { registry.GetClassSkills(CLASS_WARRIOR); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_getclassskills_caller_array_mutation_after_register_has_no_effect()
        {
            uint[] raw = { SKILL_A, SKILL_B };
            ClassRegistry registry = RegistryWith(raw);

            raw[0] = SKILL_UNKNOWN;

            Assert.That(registry.GetClassSkills(CLASS_WARRIOR)[0], Is.EqualTo(new SkillID(SKILL_A)));
        }

        [Test]
        public void test_getclassskills_reregister_with_null_list_returns_empty()
        {
            ClassRegistry registry = RegistryWith(new[] { SKILL_A, SKILL_B });

            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, null));

            Assert.That(registry.GetClassSkills(CLASS_WARRIOR).Count, Is.EqualTo(0));
        }

        [Test]
        public void test_getclassskills_reregister_with_new_list_replaces_old_list()
        {
            ClassRegistry registry = RegistryWith(new[] { SKILL_A });

            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, new[] { SKILL_B }));

            IReadOnlyList<SkillID> skills = registry.GetClassSkills(CLASS_WARRIOR);
            Assert.That(skills.Count, Is.EqualTo(1));
            Assert.That(skills[0], Is.EqualTo(new SkillID(SKILL_B)));
        }

        // ---- Enums ----

        [TestCase(ClassType.Warrior, 0)]
        [TestCase(ClassType.Healer, 1)]
        public void test_classtype_member_has_expected_value(ClassType member, int expected)
        {
            Assert.That((int)(byte)member, Is.EqualTo(expected));
        }

        [Test]
        public void test_classtype_underlying_type_is_byte()
        {
            Assert.That(Enum.GetUnderlyingType(typeof(ClassType)), Is.EqualTo(typeof(byte)));
        }

        [TestCase(TargetConstraint.SingleHostile, 0)]
        [TestCase(TargetConstraint.SingleFriendly, 1)]
        [TestCase(TargetConstraint.Self, 2)]
        public void test_targetconstraint_member_has_expected_value(TargetConstraint member, int expected)
        {
            Assert.That((int)(byte)member, Is.EqualTo(expected));
        }

        [Test]
        public void test_targetconstraint_underlying_type_is_byte()
        {
            Assert.That(Enum.GetUnderlyingType(typeof(TargetConstraint)), Is.EqualTo(typeof(byte)));
        }

        [TestCase(SkillEffectType.None, 0)]
        [TestCase(SkillEffectType.PhysicalDamage, 1)]
        [TestCase(SkillEffectType.MagicalDamage, 2)]
        [TestCase(SkillEffectType.InstantHeal, 4)]
        [TestCase(SkillEffectType.ApplyBuff, 8)]
        [TestCase(SkillEffectType.ApplyDebuff, 16)]
        public void test_skilleffecttype_member_has_expected_value(SkillEffectType member, int expected)
        {
            Assert.That((int)(byte)member, Is.EqualTo(expected));
        }

        [Test]
        public void test_skilleffecttype_underlying_type_is_byte_and_flags_attribute_present()
        {
            Assert.That(Enum.GetUnderlyingType(typeof(SkillEffectType)), Is.EqualTo(typeof(byte)));
            Assert.That(typeof(SkillEffectType).IsDefined(typeof(FlagsAttribute), false), Is.True);
        }

        [TestCase(SkillCastRejectionCode.InvalidSkill, 1)]
        [TestCase(SkillCastRejectionCode.SkillLocked, 2)]
        [TestCase(SkillCastRejectionCode.OnCooldown, 3)]
        [TestCase(SkillCastRejectionCode.InvalidTarget, 4)]
        [TestCase(SkillCastRejectionCode.OutOfRange, 5)]
        [TestCase(SkillCastRejectionCode.InsufficientMana, 6)]
        [TestCase(SkillCastRejectionCode.ServerError, 7)]
        [TestCase(SkillCastRejectionCode.Silenced, 8)]
        [TestCase(SkillCastRejectionCode.CasterNotAlive, 9)]
        public void test_skillcastrejectioncode_member_has_expected_value(SkillCastRejectionCode member, int expected)
        {
            Assert.That((int)(byte)member, Is.EqualTo(expected));
        }

        [Test]
        public void test_skillcastrejectioncode_underlying_type_is_byte_and_zero_is_not_defined()
        {
            Assert.That(Enum.GetUnderlyingType(typeof(SkillCastRejectionCode)), Is.EqualTo(typeof(byte)));
            Assert.That(Enum.IsDefined(typeof(SkillCastRejectionCode), (byte)0), Is.False);
        }

        [TestCase(DamageStat.AttackPower, 0)]
        [TestCase(DamageStat.Intelligence, 1)]
        [TestCase(DamageStat.Strength, 2)]
        public void test_damagestat_member_has_expected_value(DamageStat member, int expected)
        {
            Assert.That((int)(byte)member, Is.EqualTo(expected));
        }

        [Test]
        public void test_damagestat_underlying_type_is_byte()
        {
            Assert.That(Enum.GetUnderlyingType(typeof(DamageStat)), Is.EqualTo(typeof(byte)));
        }

        // ---- SkillDefinition ----

        [Test]
        public void test_skilldefinition_skillid_property_wraps_raw_id()
        {
            SkillDefinition skill = Skill(SKILL_A, "A");

            Assert.That(skill.SkillId, Is.EqualTo(new SkillID(SKILL_A)));
        }

        // Schema table (skill-system.md SkillDefinition Schema); ID is raw uint (Unity serialization, story notes).
        [TestCase("ID", typeof(uint))]
        [TestCase("DisplayName", typeof(string))]
        [TestCase("ClassType", typeof(ClassType))]
        [TestCase("UnlockLevel", typeof(int))]
        [TestCase("ManaCost", typeof(int))]
        [TestCase("CooldownTicks", typeof(int))]
        [TestCase("RangeUnits", typeof(float))]
        [TestCase("TargetConstraint", typeof(TargetConstraint))]
        [TestCase("SkillEffectType", typeof(SkillEffectType))]
        [TestCase("DamageMultiplier", typeof(float))]
        [TestCase("DamageBaseStat", typeof(DamageStat))]
        [TestCase("DamageContext", typeof(IronGrind.DamageCalculation.DamageContext))]
        [TestCase("HealMultiplier", typeof(float))]
        [TestCase("BuffID", typeof(IronGrind.CharacterStats.BuffID))]
        [TestCase("BuffDefinition", typeof(IronGrind.StatusEffects.BuffDefinition))]
        public void test_skilldefinition_schema_field_is_public_field_of_expected_type(string name, Type expected)
        {
            FieldInfo field = typeof(SkillDefinition).GetField(name, BindingFlags.Instance | BindingFlags.Public);

            Assert.That(field, Is.Not.Null, name + " must be a public instance field");
            Assert.That(field.FieldType, Is.EqualTo(expected));
        }

        // ---- SkillDatabase ----

        [Test]
        public void test_skilldatabase_known_id_returns_true_with_matching_definition()
        {
            SkillDatabase db = DatabaseWith(Skill(SKILL_A, "A"), Skill(SKILL_B, "B"));

            bool found = db.TryGetDefinition(new SkillID(SKILL_B), out SkillDefinition definition);

            Assert.That(found, Is.True);
            Assert.That(definition.ID, Is.EqualTo(SKILL_B));
            Assert.That(definition.DisplayName, Is.EqualTo("B"));
        }

        [Test]
        public void test_skilldatabase_invalid_id_returns_false()
        {
            SkillDatabase db = DatabaseWith(Skill(SKILL_A, "A"));

            Assert.That(db.TryGetDefinition(SkillID.Invalid, out _), Is.False);
        }

        [Test]
        public void test_skilldatabase_unknown_id_returns_false()
        {
            SkillDatabase db = DatabaseWith(Skill(SKILL_A, "A"));

            Assert.That(db.TryGetDefinition(new SkillID(SKILL_UNKNOWN), out _), Is.False);
        }

        [Test]
        public void test_skilldatabase_duplicate_ids_log_error_and_first_wins()
        {
            SkillDatabase db = DatabaseWith(Skill(SKILL_A, "First"), Skill(SKILL_A, "Second"));
            LogAssert.Expect(LogType.Error, new Regex("duplicate skill ID"));

            bool found = db.TryGetDefinition(new SkillID(SKILL_A), out SkillDefinition definition);

            Assert.That(found, Is.True);
            Assert.That(definition.DisplayName, Is.EqualTo("First"));
        }

        [Test]
        public void test_skilldatabase_entry_with_id_zero_logs_error_and_is_skipped()
        {
            SkillDatabase db = DatabaseWith(Skill(0, "Zero"), Skill(SKILL_A, "A"));
            LogAssert.Expect(LogType.Error, new Regex("reserved ID 0"));

            Assert.That(db.TryGetDefinition(new SkillID(SKILL_A), out _), Is.True);
            Assert.That(db.TryGetDefinition(SkillID.Invalid, out _), Is.False);
        }

        [Test]
        public void test_skilldatabase_null_skills_returns_false()
        {
            SkillDatabase db = DatabaseWith(null);

            Assert.That(db.TryGetDefinition(new SkillID(SKILL_A), out _), Is.False);
        }

        [Test]
        public void test_skilldatabase_replacing_skills_after_lookup_rebuilds_lookup()
        {
            SkillDatabase db = DatabaseWith(Skill(SKILL_A, "A"));
            Assert.That(db.TryGetDefinition(new SkillID(SKILL_A), out _), Is.True);

            db.SetForTesting(new[] { Skill(SKILL_B, "B") });

            Assert.That(db.TryGetDefinition(new SkillID(SKILL_A), out _), Is.False);
            Assert.That(db.TryGetDefinition(new SkillID(SKILL_B), out _), Is.True);
        }

        // ---- SkillSystemConfig ----

        [Test]
        public void test_config_default_values_are_one_and_five()
        {
            Assert.That(SkillSystemConfigValues.Default.ManaCostMultiplier, Is.EqualTo(1.0f));
            Assert.That(SkillSystemConfigValues.Default.CastStaleTickTolerance, Is.EqualTo(5));
        }

        [Test]
        public void test_config_fresh_asset_holds_defaults_without_warnings()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();

            SkillSystemConfigValues values = SkillSystemConfigValues.FromAsset(_config);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(1.0f));
            Assert.That(values.CastStaleTickTolerance, Is.EqualTo(5));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_multiplier_above_max_clamps_to_max_with_warning()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();
            _config.SetForTesting(MULTIPLIER_ABOVE, TOLERANCE_IN_RANGE);
            LogAssert.Expect(LogType.Warning, new Regex("MANA_COST_MULTIPLIER"));

            SkillSystemConfigValues values = SkillSystemConfigValues.FromAsset(_config);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(2.0f));
            Assert.That(values.CastStaleTickTolerance, Is.EqualTo(TOLERANCE_IN_RANGE));
        }

        [Test]
        public void test_config_multiplier_below_min_clamps_to_min_with_warning()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();
            _config.SetForTesting(MULTIPLIER_BELOW, TOLERANCE_IN_RANGE);
            LogAssert.Expect(LogType.Warning, new Regex("MANA_COST_MULTIPLIER"));

            Assert.That(SkillSystemConfigValues.FromAsset(_config).ManaCostMultiplier, Is.EqualTo(0.5f));
        }

        [Test]
        public void test_config_tolerance_below_min_clamps_to_min_with_warning()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();
            _config.SetForTesting(MULTIPLIER_IN_RANGE, TOLERANCE_BELOW);
            LogAssert.Expect(LogType.Warning, new Regex("CAST_STALE_TICK_TOLERANCE"));

            Assert.That(SkillSystemConfigValues.FromAsset(_config).CastStaleTickTolerance, Is.EqualTo(2));
        }

        [Test]
        public void test_config_tolerance_above_max_clamps_to_max_with_warning()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();
            _config.SetForTesting(MULTIPLIER_IN_RANGE, TOLERANCE_ABOVE);
            LogAssert.Expect(LogType.Warning, new Regex("CAST_STALE_TICK_TOLERANCE"));

            Assert.That(SkillSystemConfigValues.FromAsset(_config).CastStaleTickTolerance, Is.EqualTo(10));
        }

        [Test]
        public void test_config_in_range_values_are_unchanged_without_warnings()
        {
            _config = ScriptableObject.CreateInstance<SkillSystemConfig>();
            _config.SetForTesting(MULTIPLIER_IN_RANGE, TOLERANCE_IN_RANGE);

            SkillSystemConfigValues values = SkillSystemConfigValues.FromAsset(_config);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(MULTIPLIER_IN_RANGE));
            Assert.That(values.CastStaleTickTolerance, Is.EqualTo(TOLERANCE_IN_RANGE));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_nan_multiplier_uses_default_with_warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("MANA_COST_MULTIPLIER is NaN"));

            SkillSystemConfigValues values = SkillSystemConfigValues.Create(float.NaN, TOLERANCE_IN_RANGE);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(SkillSystemConfigValues.MANA_COST_MULTIPLIER_DEFAULT));
        }

        // Boundary values: the safe ranges are inclusive, so each bound is kept and logs nothing.
        [TestCase(0.5f, 2)]
        [TestCase(2.0f, 10)]
        public void test_config_values_at_bounds_are_unchanged_without_warnings(float multiplier, int tolerance)
        {
            SkillSystemConfigValues values = SkillSystemConfigValues.Create(multiplier, tolerance);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(multiplier));
            Assert.That(values.CastStaleTickTolerance, Is.EqualTo(tolerance));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_create_with_both_knobs_out_of_range_clamps_both_with_two_warnings()
        {
            LogAssert.Expect(LogType.Warning, new Regex("MANA_COST_MULTIPLIER"));
            LogAssert.Expect(LogType.Warning, new Regex("CAST_STALE_TICK_TOLERANCE"));

            SkillSystemConfigValues values = SkillSystemConfigValues.Create(MULTIPLIER_ABOVE, TOLERANCE_ABOVE);

            Assert.That(values.ManaCostMultiplier, Is.EqualTo(2.0f));
            Assert.That(values.CastStaleTickTolerance, Is.EqualTo(10));
        }

        [Test]
        public void test_config_null_asset_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => SkillSystemConfigValues.FromAsset(null));
        }
    }
}
