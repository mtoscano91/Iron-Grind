#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Mob Spawning Story 007 AC-3 / QA 2: every point of the shipped starting-zone spawn table passes CR-NAV-13 on the
    /// zone's real baked NavMesh (the definition's <c>BakedNavMesh</c>, added the way the zone loader adds it: no
    /// offset), through the production <see cref="UnityNavMeshQuery"/> and default <see cref="NavigationConfig"/>.
    /// </summary>
    public sealed class MobSpawning_StartingZoneNavMesh_tests
    {
        private const string DEFINITION_PATH = "Assets/data/Zones/ZoneDefinition_StartingZone.asset";

        private NavMeshDataInstance _instance;
        private NavigationConfig _config;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(DEFINITION_PATH);
            Assert.That(definition, Is.Not.Null, "zone definition missing at " + DEFINITION_PATH);
            Assert.That(definition.BakedNavMesh, Is.Not.Null, "the starting zone has no baked NavMesh");
            _instance = NavMesh.AddNavMeshData(definition.BakedNavMesh);
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            yield return null; // let the added mesh settle
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_instance.valid)
            {
                NavMesh.RemoveNavMeshData(_instance);
            }

            Object.DestroyImmediate(_config);
            yield return null;
        }

        [UnityTest]
        public IEnumerator test_startingZoneNavMesh_every_shipped_spawn_point_is_on_the_baked_mesh()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(DEFINITION_PATH);
            IReadOnlyList<SpawnPointEntry> entries = definition.SpawnTable.Entries;
            var inputs = new List<SpawnPointInput>(entries.Count);
            foreach (SpawnPointEntry entry in entries)
            {
                inputs.Add(new SpawnPointInput(entry.SpawnPointID, entry.Position));
            }

            var validOut = new List<SpawnPointInput>(entries.Count);
            var validator = new SpawnPointValidator(new UnityNavMeshQuery(), _config);

            SpawnValidationResult result = validator.Validate(new ZoneID(1u), inputs, validOut);

            Assert.That(result.ShouldAbort, Is.False);
            Assert.That(result.TotalCount, Is.EqualTo(48));
            Assert.That(result.ValidCount, Is.EqualTo(result.TotalCount), "every point is on the mesh (0 excluded)");
            Assert.That(validOut.Count, Is.EqualTo(48));
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }
    }
}
#endif
