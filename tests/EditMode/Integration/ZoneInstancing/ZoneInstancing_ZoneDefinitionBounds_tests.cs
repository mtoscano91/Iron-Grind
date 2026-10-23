using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode integration tests for Zone Instancing Story 004 — <see cref="ZoneDefinition"/>,
    /// <see cref="ZoneSceneRegistry"/>, <see cref="ZoneDefinitionBoundsValidator"/> (CR-ZI-10, AC-ZI-5) and the
    /// loader's registry lookup and NavMesh step. Crafted assets are created in memory and destroyed in TearDown.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_ZoneDefinitionBounds_Tests
    {
        private const string SCENE_NAME = "ZoneTemplate_Empty";
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly Regex NO_DEFINITION_LOG = new Regex(@"\[ZoneInstancing\] ZoneDefinitionLoadFailed: no zone definition loaded");
        private const string STAND_IN_LOG = "[StandIn] no zone NavMesh data";

        private readonly List<Object> _created = new List<Object>();
        private NavMeshDataInstance _navInstance;

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _navInstance = default(NavMeshDataInstance);
        }

        [TearDown]
        public void TearDown()
        {
            if (_navInstance.valid)
            {
                NavMesh.RemoveNavMeshData(_navInstance);
            }

            foreach (Object created in _created)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _created.Clear();
        }

        private ZoneDefinition CreateDefinition(Vector3 min, Vector3 max, NavMeshData navMesh = null)
        {
            var definition = ScriptableObject.CreateInstance<ZoneDefinition>();
            definition.Initialize(ZoneTemplateID.StartingZone, min, max, Vector3.zero, false, navMesh);
            _created.Add(definition);
            return definition;
        }

        private ZoneDefinition CreateValidDefinition(NavMeshData navMesh = null)
        {
            return CreateDefinition(new Vector3(-50f, -50f, -50f), new Vector3(50f, 50f, 50f), navMesh);
        }

        private ZoneSceneRegistry CreateRegistry(params ZoneSceneRegistry.Entry[] entries)
        {
            var registry = ScriptableObject.CreateInstance<ZoneSceneRegistry>();
            registry.Initialize(entries);
            _created.Add(registry);
            return registry;
        }

        private static ZoneDefinitionSlot SlotWith(ZoneDefinition definition)
        {
            var slot = new ZoneDefinitionSlot();
            slot.Set(definition);
            return slot;
        }

        private static Vector3 Max(float x, float y, float z) => new Vector3(x, y, z);

        // ---------------------------------------------------------------- AC-ZI-5 (composition S6, real validator)

        private static SuRig BuildRigWithRealValidator(ZoneDefinitionSlot slot, out ZoneServerComposition comp)
        {
            // SuRig fixes its bounds seam to a fake, so the composition is built here over the same fakes.
            var rig = new SuRig();
            var backend = new SuBackend(rig.Log, rig.Plan);
            comp = new ZoneServerComposition(
                rig.App, rig.Net, new SuNav(rig.Log, rig.Plan), new ZsClose(rig.Log), rig.Scene,
                new ZoneDefinitionBoundsValidator(slot), new SuStatic(rig.Log, rig.Plan), new SuStateOwner(rig.Log),
                rig.Gateway, rig.Config, backend, () => rig.Now);
            backend.Comp = comp;
            return rig;
        }

        [Test]
        public void test_s6_definition_with_max_x_251_closes_invalid_bounds_once_and_never_activates()
        {
            ZoneDefinition definition = CreateDefinition(new Vector3(-50f, -50f, -50f), Max(251f, 50f, 50f));
            SuRig rig = BuildRigWithRealValidator(SlotWith(definition), out ZoneServerComposition comp);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneInstancing\] ZoneBoundsExceedEncoding: max\.x = 251 "));
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S6: StepReturnedFalse");

            SuRig.Drive(comp.Start(SuRig.ARGS));

            Assert.AreEqual(1, rig.Log.FindAll(s => s == "S6:MarkClosedInvalidBounds").Count);
            CollectionAssert.DoesNotContain(rig.Log, "S9:MarkActive");
            CollectionAssert.DoesNotContain(rig.Log, "S10:Register");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_s6_definition_with_valid_bounds_reaches_mark_active_and_register()
        {
            ZoneDefinition definition = CreateValidDefinition();
            SuRig rig = BuildRigWithRealValidator(SlotWith(definition), out ZoneServerComposition comp);

            SuRig.Drive(comp.Start(SuRig.ARGS));

            CollectionAssert.Contains(rig.Log, "S9:MarkActive");
            CollectionAssert.Contains(rig.Log, "S10:Register");
            CollectionAssert.DoesNotContain(rig.Log, "S6:MarkClosedInvalidBounds");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Boundary

        [Test]
        public void test_bounds_exactly_plus_minus_250_on_every_axis_are_valid_and_log_nothing()
        {
            var validator = new ZoneDefinitionBoundsValidator(SlotWith(
                CreateDefinition(new Vector3(-250f, -250f, -250f), new Vector3(250f, 250f, 250f))));

            Assert.IsTrue(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_max_half_extent_constant_is_250()
        {
            Assert.AreEqual(250f, ZoneDefinitionBoundsValidator.MAX_WALKABLE_HALF_EXTENT_M);
        }

        [Test]
        public void test_min_z_below_minus_250_is_invalid_and_logs_bounds_exceed_encoding_naming_z()
        {
            var validator = new ZoneDefinitionBoundsValidator(SlotWith(
                CreateDefinition(new Vector3(-50f, -50f, -250.01f), new Vector3(50f, 50f, 50f))));
            LogAssert.Expect(LogType.Error, new Regex(@"ZoneBoundsExceedEncoding: min\.z = -250\.0"));

            Assert.IsFalse(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_max_y_above_250_is_invalid_and_logs_bounds_exceed_encoding_naming_y()
        {
            var validator = new ZoneDefinitionBoundsValidator(SlotWith(
                CreateDefinition(new Vector3(-50f, -50f, -50f), new Vector3(50f, 250.01f, 50f))));
            LogAssert.Expect(LogType.Error, new Regex(@"ZoneBoundsExceedEncoding: max\.y = 250\.0"));

            Assert.IsFalse(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Malformed

        [Test]
        public void test_nan_coordinate_is_invalid()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(float.NaN, -50f, -50f), new Vector3(50f, 50f, 50f), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
        }

        [Test]
        public void test_positive_infinity_coordinate_is_invalid()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(-50f, -50f, -50f), new Vector3(50f, float.PositiveInfinity, 50f), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
        }

        [Test]
        public void test_min_x_above_max_x_is_invalid()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(10f, -50f, -50f), new Vector3(5f, 50f, 50f), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
        }

        [Test]
        public void test_min_equal_to_max_on_every_axis_is_valid()
        {
            Assert.IsTrue(ZoneDefinitionBoundsValidator.AreBoundsValid(Vector3.zero, Vector3.zero, out string failure));
            Assert.IsNull(failure);
        }

        [Test]
        public void test_malformed_bounds_through_validate_return_false_and_log_one_error()
        {
            var validator = new ZoneDefinitionBoundsValidator(SlotWith(
                CreateDefinition(new Vector3(float.NaN, 0f, 0f), new Vector3(1f, 1f, 1f))));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ZoneInstancing\] ZoneDefinitionLoadFailed: "));

            Assert.IsFalse(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_empty_slot_is_invalid_and_logs_zone_definition_load_failed()
        {
            var validator = new ZoneDefinitionBoundsValidator(new ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Error, NO_DEFINITION_LOG);

            Assert.IsFalse(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validator_constructor_rejects_a_null_slot()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ZoneDefinitionBoundsValidator(null));
        }

        // ---------------------------------------------------------------- Registry

        [TestCase("Forest")]
        [TestCase("0")]
        [TestCase("startingzone")]
        [TestCase("")]
        [TestCase(null)]
        public void test_registry_rejects_unknown_numeric_wrong_case_and_empty_names(string name)
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, CreateValidDefinition()));

            Assert.IsFalse(registry.TryResolve(name, out string scene, out ZoneDefinition definition));
            Assert.IsNull(scene);
            Assert.IsTrue(definition == null);
        }

        [Test]
        public void test_registry_resolves_starting_zone_to_its_scene_name_and_definition()
        {
            ZoneDefinition expected = CreateValidDefinition();
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, expected));

            Assert.IsTrue(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));
            Assert.AreEqual(SCENE_NAME, scene);
            Assert.AreSame(expected, definition);
        }

        [Test]
        public void test_registry_known_template_without_an_entry_does_not_resolve()
        {
            ZoneSceneRegistry registry = CreateRegistry();

            Assert.IsFalse(registry.TryResolve("StartingZone", out _, out _));
        }

        [Test]
        public void test_registry_entry_with_empty_scene_name_or_null_definition_does_not_resolve()
        {
            ZoneSceneRegistry noScene = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, "", CreateValidDefinition()));
            ZoneSceneRegistry noDefinition = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, null));

            Assert.IsFalse(noScene.TryResolve("StartingZone", out _, out _));
            Assert.IsFalse(noDefinition.TryResolve("StartingZone", out _, out _));
        }

        // ---------------------------------------------------------------- Loader

        [TestCase("Forest")]
        [TestCase("0")]
        public void test_loader_unknown_template_is_done_not_succeeded_with_one_error_and_an_empty_slot(string name)
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, CreateValidDefinition()));
            var slot = new ZoneDefinitionSlot();
            var loader = new UnitySceneLoader(null, registry, slot);
            LogAssert.Expect(LogType.Log, new Regex("A1-3 before load"));
            LogAssert.Expect(LogType.Error, new Regex("ZoneDefinitionLoadFailed.*'" + name + "'"));

            loader.BeginLoad(name);

            Assert.IsTrue(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
            Assert.IsTrue(slot.Current == null);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_known_template_without_an_entry_is_done_not_succeeded()
        {
            var slot = new ZoneDefinitionSlot();
            var loader = new UnitySceneLoader(null, CreateRegistry(), slot);
            LogAssert.Expect(LogType.Log, new Regex("A1-3 before load"));
            LogAssert.Expect(LogType.Error, new Regex("ZoneDefinitionLoadFailed.*'StartingZone'"));

            loader.BeginLoad("StartingZone");

            Assert.IsTrue(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_null_registry_is_done_not_succeeded()
        {
            var loader = new UnitySceneLoader(null, null, new ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Log, new Regex("A1-3 before load"));
            LogAssert.Expect(LogType.Error, new Regex("ZoneDefinitionLoadFailed"));

            loader.BeginLoad("StartingZone");

            Assert.IsTrue(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_constructor_rejects_a_null_slot()
        {
            Assert.Throws<System.ArgumentNullException>(() => new UnitySceneLoader(null, null, null));
        }

        // ---------------------------------------------------------------- NavMesh

        // The story's EditMode engine check: NavMesh.AddNavMeshData(new NavMeshData()) returns a valid instance.
        [Test]
        public void test_add_nav_mesh_data_with_a_baked_nav_mesh_returns_a_valid_instance()
        {
            var navMesh = new NavMeshData();
            ZoneDefinition definition = CreateValidDefinition(navMesh);
            var loader = new UnitySceneLoader(null, null, SlotWith(definition));

            _navInstance = loader.AddNavMeshData();

            Assert.IsTrue(_navInstance.valid);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_add_nav_mesh_data_without_a_baked_nav_mesh_returns_default_and_logs_the_stand_in_once()
        {
            var loader = new UnitySceneLoader(null, null, SlotWith(CreateValidDefinition()));
            LogAssert.Expect(LogType.Log, STAND_IN_LOG);

            NavMeshDataInstance first = loader.AddNavMeshData();
            NavMeshDataInstance second = loader.AddNavMeshData();

            Assert.IsFalse(first.valid);
            Assert.IsFalse(second.valid);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
