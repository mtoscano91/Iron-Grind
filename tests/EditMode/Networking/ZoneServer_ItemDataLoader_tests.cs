#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Character Persistence Story 019, criterion 1: the S2 <see cref="ZoneItemDataLoader"/> (list, null entry, duplicate
    /// id and invalid record checks, then a ready database), its forwarding through <see cref="ZoneClassSkillDataLoader"/>,
    /// and the server scene's host holding every item asset under <c>Assets/data/items/</c>.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneServer_ItemDataLoader_Tests
    {
        private const string ITEMS_FOLDER = "Assets/data/items";
        private const string SERVER_SCENE_PATH = "Assets/Scenes/Server/ZoneServer.unity";
        private const string SKILL_DATABASE_PATH = "Assets/data/Skills/SkillDatabase.asset";
        private const string CLASS_DATABASE_PATH = "Assets/data/Classes/ClassDatabase.asset";
        private const int MVP_ITEM_COUNT = 38;

        private MvpItemFixture _fixture;
        private readonly List<Object> _created = new List<Object>();

        private sealed class CountingLoader : IZoneStaticDataLoader
        {
            public int Calls;
            public bool Result = true;
            public bool LoadAndValidate() { Calls++; return Result; }
        }

        [SetUp]
        public void SetUp()
        {
            _fixture = new MvpItemFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
            foreach (Object obj in _created)
            {
                Object.DestroyImmediate(obj);
            }

            _created.Clear();
        }

        private static List<ItemDefinition> AssetsOnDisk()
        {
            var items = new List<ItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ITEMS_FOLDER }))
            {
                items.Add(AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            }

            return items;
        }

        private void AssertFails(IReadOnlyList<ItemDefinition> items, string reasonPattern)
        {
            var rest = new CountingLoader();
            var loader = new ZoneItemDataLoader(items, rest);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneServer\] S2: " + reasonPattern));

            Assert.IsFalse(loader.LoadAndValidate());

            Assert.IsNull(loader.LoadedItemDatabase, "no database exposed");
            Assert.AreEqual(0, rest.Calls, "the remaining static data does not run");
        }

        // ---------------------------------------------------------------- the loader

        [Test]
        public void test_item_loader_mvp_records_pass_and_expose_a_ready_database()
        {
            var rest = new CountingLoader();
            var loader = new ZoneItemDataLoader(_fixture.Items, rest);

            Assert.IsTrue(loader.LoadAndValidate());

            Assert.AreEqual(1, rest.Calls, "the remaining static data ran after the items");
            Assert.IsNotNull(loader.LoadedItemDatabase);
            Assert.IsTrue(loader.LoadedItemDatabase.IsReady);
            foreach (ItemDefinition item in _fixture.Items)
            {
                Assert.AreSame(item, loader.LoadedItemDatabase.GetItem(item.ItemId), item.DisplayName);
            }
        }

        [Test]
        public void test_item_loader_assets_on_disk_pass()
        {
            List<ItemDefinition> items = AssetsOnDisk();
            Assert.AreEqual(MVP_ITEM_COUNT, items.Count, "ItemDefinition assets under " + ITEMS_FOLDER);

            Assert.IsTrue(ZoneItemDataLoader.TryBuild(items, out IronGrind.ItemDatabase.ItemDatabase database, out string reason), reason);
            Assert.IsTrue(database.IsReady);
        }

        [Test]
        public void test_item_loader_null_list_fails_s2()
        {
            AssertFails(null, "item list missing");
        }

        [Test]
        public void test_item_loader_empty_list_fails_s2()
        {
            AssertFails(new List<ItemDefinition>(), "item list is empty");
        }

        [Test]
        public void test_item_loader_null_entry_fails_s2_naming_its_index()
        {
            var items = new List<ItemDefinition>(_fixture.Items);
            items.Insert(3, null);

            AssertFails(items, "item list entry 3 is null");
        }

        [Test]
        public void test_item_loader_duplicate_item_id_fails_s2()
        {
            var items = new List<ItemDefinition>(_fixture.Items) { _fixture.Items[0] };

            AssertFails(items, "item validation failed: Duplicate ItemID");
        }

        [Test]
        public void test_item_loader_invalid_record_fails_s2()
        {
            var broken = ScriptableObject.CreateInstance<ItemDefinition>();
            _created.Add(broken);
            broken.SetForTesting(ItemID.Invalid, "Broken", ItemCategory.Consumable, 1, false, 1);
            var items = new List<ItemDefinition>(_fixture.Items) { broken };

            AssertFails(items, @"item validation failed: .*ItemID\(0\)");
        }

        [Test]
        public void test_item_loader_rest_failure_exposes_no_database()
        {
            var rest = new CountingLoader { Result = false };
            var loader = new ZoneItemDataLoader(_fixture.Items, rest);

            Assert.IsFalse(loader.LoadAndValidate());

            Assert.AreEqual(1, rest.Calls);
            Assert.IsNull(loader.LoadedItemDatabase);
        }

        // ---------------------------------------------------------------- forwarding through the class / skill loader

        private static SkillDatabase RealSkills()
        {
            var db = AssetDatabase.LoadAssetAtPath<SkillDatabase>(SKILL_DATABASE_PATH);
            Assert.IsNotNull(db, SKILL_DATABASE_PATH);
            return db;
        }

        private static ClassDatabase RealClasses()
        {
            var db = AssetDatabase.LoadAssetAtPath<ClassDatabase>(CLASS_DATABASE_PATH);
            Assert.IsNotNull(db, CLASS_DATABASE_PATH);
            return db;
        }

        [Test]
        public void test_class_skill_loader_forwards_the_item_database_of_its_rest_loader()
        {
            var items = new ZoneItemDataLoader(_fixture.Items, null);
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), items);

            Assert.IsTrue(loader.LoadAndValidate());

            Assert.IsNotNull(items.LoadedItemDatabase);
            Assert.AreSame(items.LoadedItemDatabase, loader.LoadedItemDatabase);
        }

        [Test]
        public void test_class_skill_loader_without_an_item_rest_loader_has_no_item_database()
        {
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), new CountingLoader());

            Assert.IsTrue(loader.LoadAndValidate());

            Assert.IsNull(loader.LoadedItemDatabase);
        }

        [Test]
        public void test_item_loader_failed_reload_clears_the_earlier_database()
        {
            var rest = new CountingLoader();
            var loader = new ZoneItemDataLoader(_fixture.Items, rest);
            Assert.IsTrue(loader.LoadAndValidate());
            Assert.IsNotNull(loader.LoadedItemDatabase);
            rest.Result = false;

            Assert.IsFalse(loader.LoadAndValidate());

            Assert.IsNull(loader.LoadedItemDatabase, "no stale database after a failed reload");
        }

        // ---------------------------------------------------------------- the server scene

        // Opens the server scene additively (no Awake runs in edit mode) and reads the host's _items, which must hold
        // exactly the ItemDefinition assets under Assets/data/items/ (same set, no nulls, no repeats).
        [Test]
        public void test_server_scene_host_items_hold_every_item_asset()
        {
            var expected = new HashSet<Object>(AssetsOnDisk());
            Assert.AreEqual(MVP_ITEM_COUNT, expected.Count);

            Scene scene = EditorSceneManager.OpenScene(SERVER_SCENE_PATH, OpenSceneMode.Additive);
            try
            {
                ZoneServerHost host = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    host = host != null ? host : root.GetComponentInChildren<ZoneServerHost>(true);
                }

                Assert.IsNotNull(host, "ZoneServerHost in " + SERVER_SCENE_PATH);
                SerializedProperty property = new SerializedObject(host).FindProperty("_items");
                Assert.IsNotNull(property, "_items");
                Assert.AreEqual(MVP_ITEM_COUNT, property.arraySize);

                var actual = new HashSet<Object>();
                for (int i = 0; i < property.arraySize; i++)
                {
                    Object item = property.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.IsNotNull(item, "_items[" + i + "]");
                    Assert.IsTrue(actual.Add(item), "_items[" + i + "] repeats an earlier entry");
                }

                Assert.IsTrue(expected.SetEquals(actual), "_items is not the set of item assets");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
#endif
