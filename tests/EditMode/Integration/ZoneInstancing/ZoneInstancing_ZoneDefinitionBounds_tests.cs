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

        [TestCase("min", 0, -250.01f, "x", "-250.01")]
        [TestCase("min", 1, -250.01f, "y", "-250.01")]
        [TestCase("min", 2, -250.01f, "z", "-250.01")]
        [TestCase("max", 0, 250.01f, "x", "250.01")]
        [TestCase("max", 1, 250.01f, "y", "250.01")]
        [TestCase("max", 2, 250.01f, "z", "250.01")]
        public void test_coordinate_beyond_250_is_invalid_and_logs_bounds_exceed_encoding_naming_corner_axis_and_zone(
            string corner, int axisIndex, float value, string axis, string valueText)
        {
            Vector3 min = new Vector3(-50f, -50f, -50f);
            Vector3 max = new Vector3(50f, 50f, 50f);
            if (corner == "min")
            {
                min[axisIndex] = value;
            }
            else
            {
                max[axisIndex] = value;
            }

            var validator = new ZoneDefinitionBoundsValidator(SlotWith(CreateDefinition(min, max)));
            LogAssert.Expect(LogType.Error, new Regex(
                @"^\[ZoneInstancing\] ZoneBoundsExceedEncoding: " + corner + @"\." + axis + " = "
                + Regex.Escape(valueText) + @" .*\(zone ZoneID\(7\)\)$"));

            Assert.IsFalse(validator.Validate(ZONE));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Malformed

        private static Vector3 WithAxis(Vector3 v, int axis, float value)
        {
            v[axis] = value;
            return v;
        }

        private static readonly Vector3 VALID_MIN = new Vector3(-50f, -50f, -50f);
        private static readonly Vector3 VALID_MAX = new Vector3(50f, 50f, 50f);

        [TestCase(0, "x")]
        [TestCase(1, "y")]
        [TestCase(2, "z")]
        public void test_nan_on_min_is_invalid_and_names_the_axis(int axisIndex, string axis)
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                WithAxis(VALID_MIN, axisIndex, float.NaN), VALID_MAX, out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
            StringAssert.EndsWith("axis " + axis, failure);
        }

        [TestCase(0, "x")]
        [TestCase(1, "y")]
        [TestCase(2, "z")]
        public void test_nan_on_max_is_invalid_and_names_the_axis(int axisIndex, string axis)
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                VALID_MIN, WithAxis(VALID_MAX, axisIndex, float.NaN), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
            StringAssert.EndsWith("axis " + axis, failure);
        }

        [Test]
        public void test_positive_infinity_on_max_y_is_invalid_and_names_the_axis()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                VALID_MIN, WithAxis(VALID_MAX, 1, float.PositiveInfinity), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
            StringAssert.EndsWith("axis y", failure);
        }

        [Test]
        public void test_negative_infinity_on_min_z_is_invalid_and_names_the_axis()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                WithAxis(VALID_MIN, 2, float.NegativeInfinity), VALID_MAX, out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
            StringAssert.EndsWith("axis z", failure);
        }

        [TestCase(0, "x")]
        [TestCase(1, "y")]
        [TestCase(2, "z")]
        public void test_min_above_max_is_invalid_and_names_the_axis(int axisIndex, string axis)
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                WithAxis(VALID_MIN, axisIndex, 10f), WithAxis(VALID_MAX, axisIndex, 5f), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
            StringAssert.EndsWith("axis " + axis, failure);
        }

        // ---------------------------------------------------------------- Ordering of the checks

        [Test]
        public void test_nan_together_with_an_out_of_range_coordinate_reports_zone_definition_load_failed()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(float.NaN, -50f, -50f), new Vector3(50f, 50f, 300f), out string failure));
            StringAssert.StartsWith("ZoneDefinitionLoadFailed", failure);
        }

        [Test]
        public void test_min_above_max_with_both_beyond_250_reports_bounds_exceed_encoding()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(300f, -50f, -50f), new Vector3(260f, 50f, 50f), out string failure));
            StringAssert.StartsWith("ZoneBoundsExceedEncoding", failure);
        }

        [Test]
        public void test_min_above_max_with_both_in_range_reports_zone_definition_load_failed()
        {
            Assert.IsFalse(ZoneDefinitionBoundsValidator.AreBoundsValid(
                new Vector3(100f, -50f, -50f), new Vector3(60f, 50f, 50f), out string failure));
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
            LogAssert.Expect(LogType.Error, new Regex(
                @"^\[ZoneInstancing\] ZoneDefinitionLoadFailed: non-finite bound on axis x \(zone ZoneID\(7\)\)$"));

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

        [Test]
        public void test_registry_duplicate_entries_first_complete_entry_wins()
        {
            ZoneDefinition first = CreateValidDefinition();
            ZoneDefinition second = CreateValidDefinition();
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, "FirstScene", first),
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, "SecondScene", second));

            Assert.IsTrue(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));
            Assert.AreEqual("FirstScene", scene);
            Assert.AreSame(first, definition);
        }

        [Test]
        public void test_registry_incomplete_first_entry_does_not_resolve_even_with_a_complete_second_entry()
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, "", CreateValidDefinition()),
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, CreateValidDefinition()));

            Assert.IsFalse(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));
            Assert.IsNull(scene);
            Assert.IsTrue(definition == null);
        }

        [Test]
        public void test_registry_undefined_value_text_and_undefined_entry_never_resolve()
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry((ZoneTemplateID)5, SCENE_NAME, CreateValidDefinition()));

            Assert.IsFalse(registry.TryResolve("5", out _, out _));
            Assert.IsFalse(registry.TryResolve("StartingZone", out _, out _));
        }

        [Test]
        public void test_registry_never_initialised_does_not_resolve()
        {
            var registry = ScriptableObject.CreateInstance<ZoneSceneRegistry>();
            _created.Add(registry);

            Assert.IsFalse(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));
            Assert.IsNull(scene);
            Assert.IsTrue(definition == null);
        }

        [TestCase("StartingZone ")]
        [TestCase(" StartingZone")]
        [TestCase("StartingZone,StartingZone")]
        [TestCase("-1")]
        [TestCase("+0")]
        public void test_registry_rejects_whitespace_padded_signed_and_comma_list_names(string name)
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, CreateValidDefinition()));

            Assert.IsFalse(registry.TryResolve(name, out _, out _));
        }

        [Test]
        public void test_registry_entry_whose_definition_is_for_another_template_does_not_resolve_and_logs_an_error()
        {
            var wrong = ScriptableObject.CreateInstance<ZoneDefinition>();
            wrong.Initialize((ZoneTemplateID)1, VALID_MIN, VALID_MAX, Vector3.zero, false, null);
            _created.Add(wrong);
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, wrong));
            LogAssert.Expect(LogType.Error, new Regex(
                @"^\[ZoneInstancing\] ZoneDefinitionLoadFailed: registry entry StartingZone points at a definition for template 1$"));

            Assert.IsFalse(registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition));
            Assert.IsNull(scene);
            Assert.IsTrue(definition == null);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- Loader

        [Test]
        public void test_loader_before_begin_load_is_not_done()
        {
            var loader = new UnitySceneLoader(null, CreateRegistry(), new ZoneDefinitionSlot());

            Assert.IsFalse(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
        }

        [TestCase(null)]
        [TestCase("")]
        public void test_loader_null_or_empty_template_is_done_not_succeeded_with_one_error(string name)
        {
            var loader = new UnitySceneLoader(null, CreateRegistry(), new ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Log, new Regex("A1-3 before load"));
            LogAssert.Expect(LogType.Error, new Regex("ZoneDefinitionLoadFailed"));

            loader.BeginLoad(name);

            Assert.IsTrue(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_loader_begin_load_clears_a_preset_slot_when_the_template_is_unknown()
        {
            ZoneSceneRegistry registry = CreateRegistry(
                new ZoneSceneRegistry.Entry(ZoneTemplateID.StartingZone, SCENE_NAME, CreateValidDefinition()));
            ZoneDefinitionSlot slot = SlotWith(CreateValidDefinition());
            var loader = new UnitySceneLoader(null, registry, slot);
            LogAssert.Expect(LogType.Log, new Regex("A1-3 before load"));
            LogAssert.Expect(LogType.Error, new Regex("ZoneDefinitionLoadFailed.*'Forest'"));

            loader.BeginLoad("Forest");

            Assert.IsTrue(slot.Current == null);
            LogAssert.NoUnexpectedReceived();
        }

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
            _created.Add(navMesh);
            ZoneDefinition definition = CreateValidDefinition(navMesh);
            var loader = new UnitySceneLoader(null, null, SlotWith(definition));

            _navInstance = loader.AddNavMeshData();

            Assert.IsTrue(_navInstance.valid);
            // NavMeshDataInstance.owner is caller-assigned (not set by AddNavMeshData), so only validity is asserted.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_add_nav_mesh_data_with_an_empty_slot_returns_default_and_logs_the_stand_in_once()
        {
            var loader = new UnitySceneLoader(null, null, new ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Log, STAND_IN_LOG);

            NavMeshDataInstance first = loader.AddNavMeshData();
            NavMeshDataInstance second = loader.AddNavMeshData();

            Assert.IsFalse(first.valid);
            Assert.IsFalse(second.valid);
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
