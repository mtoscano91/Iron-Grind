using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode tests for Enemy AI Story 002: <see cref="EnemyAIConfigValues"/> clamping, <see cref="MobRoster"/>,
    /// <see cref="ZoneMobDataLoader"/> and the shipped <c>Assets/data/Mobs/MobRoster.asset</c> (AC-1 to AC-4).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_Config_Tests
    {
        private const string ROSTER_ASSET_PATH = "Assets/data/Mobs/MobRoster.asset";
        private const float TOLERANCE = 1e-5f;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private sealed class FakeLoader : IZoneStaticDataLoader
        {
            private readonly bool _result;

            public FakeLoader(bool result)
            {
                _result = result;
            }

            public int Calls { get; private set; }

            public bool LoadAndValidate()
            {
                Calls++;
                return _result;
            }
        }

        private sealed class FakeDataLoader : IZoneStaticDataLoader, IZoneClassSkillData, IZoneItemData
        {
            public bool Result = true;
            public ClassRegistry ClassRegistry;
            public SkillDatabase SkillDatabase;
            public IItemDatabase ItemDatabase;

            public ClassRegistry LoadedClassRegistry => ClassRegistry;

            public SkillDatabase LoadedSkillDatabase => SkillDatabase;

            public IItemDatabase LoadedItemDatabase => ItemDatabase;

            public bool LoadAndValidate()
            {
                return Result;
            }
        }

        private sealed class FakeItemOnlyLoader : IZoneStaticDataLoader, IZoneItemData
        {
            public IItemDatabase LoadedItemDatabase { get; } = new IronGrind.ItemDatabase.ItemDatabase();

            public bool LoadAndValidate()
            {
                return true;
            }
        }

        private FakeDataLoader NewFakeDataLoader()
        {
            SkillDatabase skills = ScriptableObject.CreateInstance<SkillDatabase>();
            _created.Add(skills);
            return new FakeDataLoader
            {
                ClassRegistry = new ClassRegistry(),
                SkillDatabase = skills,
                ItemDatabase = new IronGrind.ItemDatabase.ItemDatabase()
            };
        }

        private static MobRosterEntry ValidEntry(uint id)
        {
            return new MobRosterEntry
            {
                MobTypeId = id,
                DisplayName = "Test Mob",
                MobTier = MobTier.Common,
                BaseMaxHP = 100,
                BaseAttackPower = 10,
                BaseDefense = 5,
                AttackRange = 2f,
                MoveSpeed = 3.5f,
                AggroRange = 8f,
                LeashRange = 20f,
                AttackWindUpTicks = 12,
                AttackRecoveryTicks = 6,
                AttackCooldownTicks = 10,
                KillXP = 10
            };
        }

        private MobRoster Roster(params MobRosterEntry[] entries)
        {
            MobRoster roster = ScriptableObject.CreateInstance<MobRoster>();
            _created.Add(roster);
            roster.SetEntriesForTesting(entries);
            return roster;
        }

        private EnemyAIConfig Config(int scan, float pursuit, float arrival, int linger)
        {
            EnemyAIConfig config = ScriptableObject.CreateInstance<EnemyAIConfig>();
            _created.Add(config);
            config.SetForTesting(scan, pursuit, arrival, linger);
            return config;
        }

        // ---------- AC-1 config ----------

        [Test]
        public void test_config_defaults_match_gdd()
        {
            EnemyAIConfigValues values = EnemyAIConfigValues.Defaults;

            Assert.AreEqual(5, values.AggroScanIntervalTicks);
            Assert.AreEqual(1.0f, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(0.5f, values.ReturnArrivalThreshold, TOLERANCE);
            Assert.AreEqual(30, values.DeathLingerTicks);
        }

        [Test]
        public void test_config_fresh_asset_equals_defaults_without_warning()
        {
            EnemyAIConfig asset = ScriptableObject.CreateInstance<EnemyAIConfig>();
            _created.Add(asset);
            EnemyAIConfigValues defaults = EnemyAIConfigValues.Defaults;

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(asset);

            Assert.AreEqual(defaults.AggroScanIntervalTicks, values.AggroScanIntervalTicks);
            Assert.AreEqual(defaults.PursuitRepositionThreshold, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(defaults.ReturnArrivalThreshold, values.ReturnArrivalThreshold, TOLERANCE);
            Assert.AreEqual(defaults.DeathLingerTicks, values.DeathLingerTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(1, 0.5f, 0.25f, 10)]
        [TestCase(10, 3.0f, 2.0f, 100)]
        [TestCase(3, 2.0f, 1.0f, 50)]
        public void test_config_values_at_min_max_and_inside_kept_exactly_without_warning(
            int scan, float pursuit, float arrival, int linger)
        {
            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(scan, pursuit, arrival, linger));

            Assert.AreEqual(scan, values.AggroScanIntervalTicks);
            Assert.AreEqual(pursuit, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(arrival, values.ReturnArrivalThreshold, TOLERANCE);
            Assert.AreEqual(linger, values.DeathLingerTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_low_side_clamp_logs_exact_warning_message()
        {
            LogAssert.Expect(LogType.Warning,
                "[EnemyAIConfig] AGGRO_SCAN_INTERVAL_TICKS 0 is outside [1, 10]; using 1");

            EnemyAIConfigValues.FromAsset(Config(0, 1.0f, 0.5f, 30));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_non_finite_logs_exact_warning_message()
        {
            LogAssert.Expect(LogType.Warning,
                "[EnemyAIConfig] PURSUIT_REPOSITION_THRESHOLD is not finite; using default 1");

            EnemyAIConfigValues.FromAsset(Config(5, float.NaN, 0.5f, 30));

            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0, 1)]
        [TestCase(11, 10)]
        [TestCase(int.MinValue, 1)]
        [TestCase(int.MaxValue, 10)]
        public void test_config_aggro_scan_interval_out_of_range_clamped_with_one_warning(int input, int expected)
        {
            LogAssert.Expect(LogType.Warning, new Regex("AGGRO_SCAN_INTERVAL_TICKS"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(input, 1.0f, 0.5f, 30));

            Assert.AreEqual(expected, values.AggroScanIntervalTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0.4f, 0.5f)]
        [TestCase(3.1f, 3.0f)]
        [TestCase(-1f, 0.5f)]
        public void test_config_pursuit_threshold_out_of_range_clamped_with_one_warning(float input, float expected)
        {
            LogAssert.Expect(LogType.Warning, new Regex("PURSUIT_REPOSITION_THRESHOLD"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(5, input, 0.5f, 30));

            Assert.AreEqual(expected, values.PursuitRepositionThreshold, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0.2f, 0.25f)]
        [TestCase(2.5f, 2.0f)]
        [TestCase(-1f, 0.25f)]
        public void test_config_arrival_threshold_out_of_range_clamped_with_one_warning(float input, float expected)
        {
            LogAssert.Expect(LogType.Warning, new Regex("RETURN_ARRIVAL_THRESHOLD"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(5, 1.0f, input, 30));

            Assert.AreEqual(expected, values.ReturnArrivalThreshold, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(9, 10)]
        [TestCase(101, 100)]
        [TestCase(int.MinValue, 10)]
        [TestCase(int.MaxValue, 100)]
        public void test_config_death_linger_out_of_range_clamped_with_one_warning(int input, int expected)
        {
            LogAssert.Expect(LogType.Warning, new Regex("DEATH_LINGER_TICKS"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(5, 1.0f, 0.5f, input));

            Assert.AreEqual(expected, values.DeathLingerTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_several_knobs_out_of_range_one_warning_each()
        {
            LogAssert.Expect(LogType.Warning, new Regex("AGGRO_SCAN_INTERVAL_TICKS"));
            LogAssert.Expect(LogType.Warning, new Regex("PURSUIT_REPOSITION_THRESHOLD"));
            LogAssert.Expect(LogType.Warning, new Regex("RETURN_ARRIVAL_THRESHOLD"));
            LogAssert.Expect(LogType.Warning, new Regex("DEATH_LINGER_TICKS"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(0, 9f, 0f, 1000));

            Assert.AreEqual(1, values.AggroScanIntervalTicks);
            Assert.AreEqual(3.0f, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(0.25f, values.ReturnArrivalThreshold, TOLERANCE);
            Assert.AreEqual(100, values.DeathLingerTicks);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_config_non_finite_pursuit_falls_back_to_default_with_one_warning_other_kept(float input)
        {
            LogAssert.Expect(LogType.Warning, new Regex("PURSUIT_REPOSITION_THRESHOLD"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(5, input, 0.75f, 30));

            Assert.AreEqual(1.0f, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(0.75f, values.ReturnArrivalThreshold, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_config_non_finite_arrival_falls_back_to_default_with_one_warning_other_kept(float input)
        {
            LogAssert.Expect(LogType.Warning, new Regex("RETURN_ARRIVAL_THRESHOLD"));

            EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(Config(5, 2.0f, input, 30));

            Assert.AreEqual(2.0f, values.PursuitRepositionThreshold, TOLERANCE);
            Assert.AreEqual(0.5f, values.ReturnArrivalThreshold, TOLERANCE);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_config_null_asset_throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => EnemyAIConfigValues.FromAsset(null));
        }

        // ---------- AC-2 loader ----------

        [Test]
        public void test_loader_valid_and_invalid_entry_registers_valid_refuses_invalid_returns_true()
        {
            MobRosterEntry invalid = ValidEntry(2);
            invalid.BaseMaxHP = 0;
            LogAssert.Expect(LogType.Error, new Regex("^MobDefinition refused"));
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1), invalid), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.IsNotNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(1u)));
            Assert.IsNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(2u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_all_entries_invalid_returns_false_with_null_registry()
        {
            MobRosterEntry invalid = ValidEntry(1);
            invalid.MoveSpeed = 0f;
            LogAssert.Expect(LogType.Error, new Regex("^MobDefinition refused"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
            var loader = new ZoneMobDataLoader(Roster(invalid), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
        }

        [Test]
        public void test_loader_empty_roster_returns_false_with_null_registry()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
            var loader = new ZoneMobDataLoader(Roster(), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
        }

        [Test]
        public void test_loader_null_roster_returns_false_with_null_registry()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
            var loader = new ZoneMobDataLoader(null, null);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
        }

        [Test]
        public void test_loader_only_null_row_returns_false_with_warning_and_no_valid_definition_error()
        {
            LogAssert.Expect(LogType.Warning, "[MobRoster] row 0 is null; skipped");
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: MobRoster has no valid mob definition");
            var loader = new ZoneMobDataLoader(Roster(new MobRosterEntry[] { null }), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
        }

        [Test]
        public void test_loader_null_row_and_valid_row_returns_true_with_one_warning_and_one_definition()
        {
            LogAssert.Expect(LogType.Warning, "[MobRoster] row 0 is null; skipped");
            var loader = new ZoneMobDataLoader(Roster(null, ValidEntry(1)), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.AreEqual(1, ((MobDefinitionRegistry)loader.LoadedMobRegistry).Count);
            Assert.IsNotNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(1u)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_duplicate_mob_type_keeps_first_definition()
        {
            MobRosterEntry first = ValidEntry(1);
            first.DisplayName = "First";
            MobRosterEntry second = ValidEntry(1);
            second.DisplayName = "Second";
            LogAssert.Expect(LogType.Error, new Regex("^MobDefinition refused"));
            var loader = new ZoneMobDataLoader(Roster(first, second), null);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.AreEqual("First", loader.LoadedMobRegistry.GetDefinition(new MobTypeID(1u)).DisplayName);
        }

        // ---------- AC-3 shipped asset ----------

#if UNITY_EDITOR
        [Test]
        public void test_shipped_roster_builds_mob_types_1_to_4_without_error_or_warning()
        {
            var roster = UnityEditor.AssetDatabase.LoadAssetAtPath<MobRoster>(ROSTER_ASSET_PATH);
            Assert.IsNotNull(roster, "MobRoster asset missing at " + ROSTER_ASSET_PATH);
            var loader = new ZoneMobDataLoader(roster, null);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            for (uint id = 1; id <= 4; id++)
            {
                Assert.IsNotNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(id)), "MobTypeID " + id);
            }

            Assert.IsNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(5u)));
            Assert.AreEqual(4, roster.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(1u, "Bandicoot", MobTier.Common, 55, 28, 2, 1.5f, 3.5f, 6f, 12f, 12, 6, 10, 20)]
        [TestCase(2u, "Wild Bandicoot", MobTier.Uncommon, 120, 40, 6, 1.5f, 3.8f, 7f, 14f, 14, 6, 12, 45)]
        [TestCase(3u, "Bandicoot Scavenger", MobTier.Rare, 260, 60, 12, 1.8f, 4.0f, 8f, 16f, 16, 8, 14, 100)]
        [TestCase(4u, "Giant Bandicoot", MobTier.Elite, 600, 95, 20, 2.5f, 3.6f, 9f, 18f, 18, 10, 16, 250)]
        public void test_shipped_roster_row_fields_equal_story_table(
            uint id, string name, MobTier tier, int hp, int ap, int def, float range, float speed, float aggro,
            float leash, int windUp, int recovery, int cooldown, int xp)
        {
            var roster = UnityEditor.AssetDatabase.LoadAssetAtPath<MobRoster>(ROSTER_ASSET_PATH);
            var loader = new ZoneMobDataLoader(roster, null);
            Assert.IsTrue(loader.LoadAndValidate());

            MobDefinition d = loader.LoadedMobRegistry.GetDefinition(new MobTypeID(id));

            Assert.AreEqual(new MobTypeID(id), d.MobTypeID);
            Assert.AreEqual(name, d.DisplayName);
            Assert.AreEqual(tier, d.MobTier);
            Assert.AreEqual(hp, d.BaseMaxHP);
            Assert.AreEqual(ap, d.BaseAttackPower);
            Assert.AreEqual(def, d.BaseDefense);
            Assert.AreEqual(range, d.AttackRange, TOLERANCE);
            Assert.AreEqual(speed, d.MoveSpeed, TOLERANCE);
            Assert.AreEqual(aggro, d.AggroRange, TOLERANCE);
            Assert.AreEqual(leash, d.LeashRange, TOLERANCE);
            Assert.AreEqual(windUp, d.AttackWindUpTicks);
            Assert.AreEqual(recovery, d.AttackRecoveryTicks);
            Assert.AreEqual(cooldown, d.AttackCooldownTicks);
            Assert.AreEqual(xp, d.KillXP);
            Assert.AreEqual(0.07f, d.EnragedChance, TOLERANCE);
            Assert.AreEqual(1.5f, d.EnragedMaxHPMultiplier, TOLERANCE);
            Assert.AreEqual(1.3f, d.EnragedAttackPowerMultiplier, TOLERANCE);
            Assert.AreEqual(1.5f, d.EnragedXPMultiplier, TOLERANCE);
            Assert.AreEqual(1, d.EnragedLootQualityBonusTier);
        }
#endif

        // ---------- AC-4 chain ----------

        [Test]
        public void test_chain_rest_fails_returns_false_with_null_registry()
        {
            var rest = new FakeLoader(false);
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1)), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNull(loader.LoadedMobRegistry);
        }

        [Test]
        public void test_chain_roster_fails_returns_false_and_leaves_no_registry()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
            var rest = new FakeLoader(true);
            var loader = new ZoneMobDataLoader(Roster(), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
            Assert.AreEqual(0, rest.Calls);
        }

        [Test]
        public void test_chain_both_pass_returns_true_with_registry()
        {
            var rest = new FakeLoader(true);
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1)), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNotNull(loader.LoadedMobRegistry);
        }

        // A null roster cannot be reached after a successful load (the roster is a readonly constructor argument), so
        // that path is covered by test_loader_null_roster_returns_false_with_null_registry, which starts empty.
        [TestCase("roster_emptied")]
        [TestCase("roster_all_invalid")]
        [TestCase("roster_only_null_row")]
        [TestCase("rest_fails")]
        public void test_chain_failed_reload_clears_mob_registry_and_forwarded_data(string failure)
        {
            FakeDataLoader rest = NewFakeDataLoader();
            MobRoster roster = Roster(ValidEntry(1));
            var loader = new ZoneMobDataLoader(roster, rest);
            Assert.IsTrue(loader.LoadAndValidate());
            Assert.IsNotNull(loader.LoadedMobRegistry);
            Assert.IsNotNull(loader.LoadedClassRegistry);
            Assert.IsNotNull(loader.LoadedSkillDatabase);
            Assert.IsNotNull(loader.LoadedItemDatabase);

            switch (failure)
            {
                case "roster_emptied":
                    roster.SetEntriesForTesting(new MobRosterEntry[0]);
                    LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
                    break;
                case "roster_all_invalid":
                    MobRosterEntry invalid = ValidEntry(1);
                    invalid.BaseMaxHP = 0;
                    roster.SetEntriesForTesting(new[] { invalid });
                    LogAssert.Expect(LogType.Error, new Regex("^MobDefinition refused"));
                    LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
                    break;
                case "roster_only_null_row":
                    roster.SetEntriesForTesting(new MobRosterEntry[] { null });
                    LogAssert.Expect(LogType.Warning, "[MobRoster] row 0 is null; skipped");
                    LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
                    break;
                default:
                    rest.Result = false;
                    break;
            }

            bool ok = loader.LoadAndValidate();

            Assert.IsFalse(ok);
            Assert.IsNull(loader.LoadedMobRegistry);
            Assert.IsNull(loader.LoadedClassRegistry);
            Assert.IsNull(loader.LoadedSkillDatabase);
            Assert.IsNull(loader.LoadedItemDatabase);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_chain_reload_success_after_failure_repopulates_mob_registry_and_forwarded_data()
        {
            FakeDataLoader rest = NewFakeDataLoader();
            MobRoster roster = Roster(ValidEntry(1));
            var loader = new ZoneMobDataLoader(roster, rest);
            Assert.IsTrue(loader.LoadAndValidate());
            roster.SetEntriesForTesting(new MobRosterEntry[0]);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: "));
            Assert.IsFalse(loader.LoadAndValidate());
            roster.SetEntriesForTesting(new[] { ValidEntry(1) });

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.IsNotNull(loader.LoadedMobRegistry.GetDefinition(new MobTypeID(1u)));
            Assert.AreSame(rest.ClassRegistry, loader.LoadedClassRegistry);
            Assert.AreSame(rest.SkillDatabase, loader.LoadedSkillDatabase);
            Assert.AreSame(rest.ItemDatabase, loader.LoadedItemDatabase);
        }

        [Test]
        public void test_chain_rest_with_all_data_interfaces_forwards_same_instances()
        {
            FakeDataLoader rest = NewFakeDataLoader();
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1)), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.AreSame(rest.ClassRegistry, loader.LoadedClassRegistry);
            Assert.AreSame(rest.SkillDatabase, loader.LoadedSkillDatabase);
            Assert.AreSame(rest.ItemDatabase, loader.LoadedItemDatabase);
        }

        [Test]
        public void test_chain_rest_without_data_interfaces_leaves_forwarded_data_null()
        {
            var rest = new FakeLoader(true);
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1)), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.IsNull(loader.LoadedClassRegistry);
            Assert.IsNull(loader.LoadedSkillDatabase);
            Assert.IsNull(loader.LoadedItemDatabase);
        }

        [Test]
        public void test_chain_rest_with_only_item_interface_forwards_item_data_only()
        {
            var rest = new FakeItemOnlyLoader();
            var loader = new ZoneMobDataLoader(Roster(ValidEntry(1)), rest);

            bool ok = loader.LoadAndValidate();

            Assert.IsTrue(ok);
            Assert.IsNull(loader.LoadedClassRegistry);
            Assert.IsNull(loader.LoadedSkillDatabase);
            Assert.AreSame(rest.LoadedItemDatabase, loader.LoadedItemDatabase);
        }
    }
}
