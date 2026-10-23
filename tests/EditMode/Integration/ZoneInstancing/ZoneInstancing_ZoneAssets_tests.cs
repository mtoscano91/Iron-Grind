#if UNITY_EDITOR
using System.Linq;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// Asset integrity checks for the authored Zone Instancing Story 004 assets: the registry, the StartingZone
    /// definition and the server scene's reference to the registry. Reads the real project assets (read-only).
    /// </summary>
    [TestFixture]
    [Category("AssetIntegrity")]
    internal sealed class ZoneInstancing_ZoneAssets_Tests
    {
        private const string REGISTRY_PATH = "Assets/data/Zones/ZoneSceneRegistry.asset";
        private const string SERVER_SCENE_PATH = "Assets/Scenes/Server/ZoneServer.unity";

        [Test]
        public void test_registry_asset_exists()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ZoneSceneRegistry>(REGISTRY_PATH));
        }

        [Test]
        public void test_registry_asset_resolves_starting_zone_to_the_empty_template_scene_and_a_definition()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ZoneSceneRegistry>(REGISTRY_PATH);
            Assert.IsNotNull(registry);

            Assert.IsTrue(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));

            Assert.AreEqual("ZoneTemplate_Empty", scene);
            Assert.IsNotNull(definition);
        }

        [Test]
        public void test_starting_zone_definition_asset_has_valid_plus_minus_50_bounds_and_no_nav_mesh()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ZoneSceneRegistry>(REGISTRY_PATH);
            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.TryResolve("StartingZone", out _, out ZoneDefinition definition));

            Assert.AreEqual(ZoneTemplateID.StartingZone, definition.TemplateId);
            Assert.AreEqual(new Vector3(-50f, -50f, -50f), definition.WalkableBoundsMin);
            Assert.AreEqual(new Vector3(50f, 50f, 50f), definition.WalkableBoundsMax);
            Assert.IsTrue(ZoneDefinitionBoundsValidator.AreBoundsValid(
                definition.WalkableBoundsMin, definition.WalkableBoundsMax, out string failure), failure);
            Assert.IsFalse(definition.TownRespawnPointAuthored);
            Assert.IsTrue(definition.BakedNavMesh == null);
        }

        [Test]
        public void test_zone_server_scene_references_the_registry_asset()
        {
            string[] dependencies = AssetDatabase.GetDependencies(SERVER_SCENE_PATH, false);

            Assert.IsTrue(dependencies.Contains(REGISTRY_PATH), "ZoneServer.unity does not reference " + REGISTRY_PATH);
        }
    }
}
#endif
