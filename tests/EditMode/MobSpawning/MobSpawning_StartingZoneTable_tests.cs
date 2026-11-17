#if UNITY_EDITOR
using System.Collections.Generic;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MobSpawning
{
    /// <summary>
    /// Mob Spawning Story 007: the shipped starting-zone spawn table. Checks the asset reference (AC-1), the Story 001
    /// validator and the authoring margin (AC-2), the CR-MS-2 layout sized for this zone (AC-4) and the tier clearance
    /// (AC-5). The NavMesh check (AC-3) is in PlayMode (<c>MobSpawning_StartingZoneNavMesh_tests</c>). Each layout test
    /// writes its measurements to the test output for the Completion Notes.
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_StartingZoneTable_Tests
    {
        private const string DEFINITION_PATH = "Assets/data/Zones/ZoneDefinition_StartingZone.asset";
        private const string ROSTER_PATH = "Assets/data/Mobs/MobRoster.asset";

        // Readiness 2026-11-16 (AC-2, AC-4): authoring margin, point count and per-tier split.
        private const float MAX_ABS_XZ = 47f;
        private const int TOTAL_POINTS = 48;
        private const float CLUSTER_RADIUS = 15f;
        private const int CLUSTER_NEIGHBOURS = 3;
        private const float COMMON_SHARE_MIN = 0.35f;
        private const float COMMON_SHARE_MAX = 0.45f;

        private static readonly int[] TIER_COUNTS = { 20, 14, 9, 5 }; // Common, Uncommon, Rare, Elite

        private ZoneDefinition _definition;
        private ZoneSpawnTable _table;
        private IMobDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _definition = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(DEFINITION_PATH);
            Assert.IsNotNull(_definition, "zone definition missing at " + DEFINITION_PATH);
            _table = _definition.SpawnTable;
            Assert.IsNotNull(_table, "ZoneDefinition_StartingZone has no spawn table");
            var roster = AssetDatabase.LoadAssetAtPath<MobRoster>(ROSTER_PATH);
            Assert.IsNotNull(roster, "roster missing at " + ROSTER_PATH);
            var loader = new ZoneMobDataLoader(roster, null);
            Assert.IsTrue(loader.LoadAndValidate());
            _registry = loader.LoadedMobRegistry;
        }

        private MobTier TierOf(SpawnPointEntry entry) => _registry.GetDefinition(entry.MobTypeID).MobTier;

        private List<Vector3> PositionsOf(MobTier tier)
        {
            var list = new List<Vector3>();
            foreach (SpawnPointEntry entry in _table.Entries)
            {
                if (TierOf(entry) == tier)
                {
                    list.Add(entry.Position);
                }
            }

            return list;
        }

        private static float MinDistance(List<Vector3> a, List<Vector3> b)
        {
            float min = float.MaxValue;
            foreach (Vector3 p in a)
            {
                foreach (Vector3 q in b)
                {
                    min = Mathf.Min(min, Vector3.Distance(p, q));
                }
            }

            return min;
        }

        // ---- QA 1: AC-1, AC-2 ----

        [Test]
        public void test_startingZoneTable_asset_is_the_definitions_table_for_the_starting_zone()
        {
            Assert.AreEqual("ZoneSpawnTable_StartingZone", _table.name);
            Assert.AreEqual(ZoneTemplateID.StartingZone, _table.TemplateId);
            Assert.AreEqual(_definition.TemplateId, _table.TemplateId);
        }

        [Test]
        public void test_startingZoneTable_passes_the_table_validator_with_no_log()
        {
            bool ok = ZoneSpawnTableValidator.Validate(_table, _definition.TemplateId, _definition.WalkableBoundsMin,
                _definition.WalkableBoundsMax, _registry);

            Assert.IsTrue(ok);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_startingZoneTable_every_point_is_on_the_floor_inside_the_margin_with_no_override()
        {
            foreach (SpawnPointEntry entry in _table.Entries)
            {
                string id = "point " + entry.SpawnPointID;
                Assert.AreEqual(0f, entry.Position.y, id + " y");
                Assert.LessOrEqual(Mathf.Abs(entry.Position.x), MAX_ABS_XZ, id + " x");
                Assert.LessOrEqual(Mathf.Abs(entry.Position.z), MAX_ABS_XZ, id + " z");
                Assert.AreEqual(0, entry.RespawnDelayOverrideTicks, id + " override");
            }
        }

        // ---- QA 3: AC-4 ----

        [Test]
        public void test_startingZoneTable_has_48_points_split_20_14_9_5_with_a_common_share_of_35_to_45_percent()
        {
            Assert.AreEqual(TOTAL_POINTS, _table.Entries.Count);
            for (int tier = 0; tier < TIER_COUNTS.Length; tier++)
            {
                Assert.AreEqual(TIER_COUNTS[tier], PositionsOf((MobTier)tier).Count, ((MobTier)tier).ToString());
            }

            float share = PositionsOf(MobTier.Common).Count / (float)_table.Entries.Count;
            Assert.That(share, Is.InRange(COMMON_SHARE_MIN, COMMON_SHARE_MAX));
            TestContext.WriteLine($"points {_table.Entries.Count}; Common share {share:P1}");
        }

        [Test]
        public void test_startingZoneTable_mean_distance_from_the_origin_strictly_increases_common_to_elite()
        {
            float previous = -1f;
            for (int tier = 0; tier < TIER_COUNTS.Length; tier++)
            {
                List<Vector3> points = PositionsOf((MobTier)tier);
                float sum = 0f;
                foreach (Vector3 p in points)
                {
                    sum += p.magnitude;
                }

                float mean = sum / points.Count;
                TestContext.WriteLine($"{(MobTier)tier}: {points.Count} points, mean distance from origin {mean:F2} m");
                Assert.Greater(mean, previous, ((MobTier)tier) + " must be farther than the previous tier");
                previous = mean;
            }
        }

        [Test]
        public void test_startingZoneTable_every_point_has_at_least_3_others_within_15_m()
        {
            IReadOnlyList<SpawnPointEntry> entries = _table.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                int neighbours = 0;
                for (int j = 0; j < entries.Count; j++)
                {
                    if (i != j && Vector3.Distance(entries[i].Position, entries[j].Position) <= CLUSTER_RADIUS)
                    {
                        neighbours++;
                    }
                }

                Assert.GreaterOrEqual(neighbours, CLUSTER_NEIGHBOURS, "point " + entries[i].SpawnPointID);
            }
        }

        // ---- QA 4: AC-5 ----

        [TestCase(MobTier.Rare)]
        [TestCase(MobTier.Elite)]
        public void test_startingZoneTable_common_points_are_farther_than_the_aggro_range_from_the_tier(MobTier tier)
        {
            float aggro = 0f;
            foreach (SpawnPointEntry entry in _table.Entries)
            {
                if (TierOf(entry) == tier)
                {
                    aggro = Mathf.Max(aggro, _registry.GetDefinition(entry.MobTypeID).AggroRange);
                }
            }

            float min = MinDistance(PositionsOf(MobTier.Common), PositionsOf(tier));

            TestContext.WriteLine($"min Common to {tier}: {min:F2} m (AggroRange {aggro} m)");
            Assert.Greater(min, aggro);
        }
    }
}
#endif
