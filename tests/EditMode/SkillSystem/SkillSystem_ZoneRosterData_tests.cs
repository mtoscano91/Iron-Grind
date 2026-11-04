#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.SkillSystemTests
{
    /// <summary>
    /// Skill System Story 013: the zone server loads the roster and class data at startup S2
    /// (<see cref="ZoneClassSkillDataLoader"/>; ADR-009 Amendment 1 S2; class-system.md CD-10, AC-CS-18) and S5 builds
    /// Leveling and the Skill System over it. Loader checks over in-memory assets, the real Story 012 assets, the
    /// composition over the real <see cref="ZoneServerComposition"/> (<see cref="WiRig"/>), and the server scene.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_ZoneRosterData_Tests
    {
        private const string SKILL_DATABASE_PATH = "Assets/data/Skills/SkillDatabase.asset";
        private const string CLASS_DATABASE_PATH = "Assets/data/Classes/ClassDatabase.asset";
        private const string SERVER_SCENE_PATH = "Assets/Scenes/Server/ZoneServer.unity";
        private const string S2_FAILED = "[ZoneServer] startup failed at S2: StepReturnedFalse";

        private const uint WARRIOR_FIRST_SKILL = 101u;
        private const uint HEALER_FIRST_SKILL = 201u;
        private const uint UNKNOWN_SKILL = 999u;
        private const int SKILLS = ZoneClassSkillDataLoader.SKILLS_PER_CLASS;

        private const uint CLIENT_ONE = 11u;
        private const uint ACCOUNT_OFFSET = 1000u;

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private readonly List<WiRig> _rigs = new List<WiRig>();

        [TearDown]
        public void TearDown()
        {
            foreach (WiRig rig in _rigs)
            {
                DisposeSkillTeardown(rig);
            }

            _rigs.Clear();
            foreach (UnityEngine.Object obj in _created)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }

            _created.Clear();
        }

        // ---- Fixtures ----

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

        private static uint[] SkillIds(uint first)
        {
            var ids = new uint[SKILLS];
            for (int i = 0; i < SKILLS; i++)
            {
                ids[i] = first + (uint)i;
            }

            return ids;
        }

        // A roster holding the Warrior ids 101-110, plus any extra ids.
        private SkillDatabase FakeSkills(params uint[] extra)
        {
            var defs = new List<SkillDefinition>();
            foreach (uint id in SkillIds(WARRIOR_FIRST_SKILL))
            {
                defs.Add(new SkillDefinition { ID = id, UnlockLevel = 1 });
            }

            foreach (uint id in extra)
            {
                defs.Add(new SkillDefinition { ID = id, UnlockLevel = 1 });
            }

            var db = ScriptableObject.CreateInstance<SkillDatabase>();
            db.SetForTesting(defs.ToArray());
            _created.Add(db);
            return db;
        }

        // A valid Warrior entry (2/1/1/0 + 1 = 5, skills 101-110), the Story 012 shape.
        private static ClassDefinitionEntry ValidWarrior()
        {
            return new ClassDefinitionEntry
            {
                ClassType = ClassType.Warrior,
                StrengthAutoAlloc = 2,
                DexterityAutoAlloc = 1,
                VitalityAutoAlloc = 1,
                IntelligenceAutoAlloc = 0,
                FreePointsPerLevel = 1,
                SkillList = SkillIds(WARRIOR_FIRST_SKILL),
            };
        }

        private ClassDatabase FakeClasses(params ClassDefinitionEntry[] entries)
        {
            var db = ScriptableObject.CreateInstance<ClassDatabase>();
            db.SetForTesting(entries);
            _created.Add(db);
            return db;
        }

        private sealed class CountingLoader : IZoneStaticDataLoader
        {
            public int Calls;
            public bool Result = true;
            public bool LoadAndValidate() { Calls++; return Result; }
        }

        // The full RunTeardown would log a SaveSession error once players are admitted (no persistence in the rig), so
        // this disposes only the registered SkillSystemTeardown, as SkillSystem_Composition_Tests does.
        private static void DisposeSkillTeardown(WiRig rig)
        {
            FieldInfo field = typeof(ZoneServerComposition).GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "_disposables list");
            var disposables = (List<IDisposable>)field.GetValue(rig.Comp);
            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                if (disposables[i] is SkillSystemTeardown teardown)
                {
                    teardown.Dispose();
                }
            }
        }

        private WiRig StartRig(IZoneStaticDataLoader loader)
        {
            var rig = new WiRig(staticDataLoader: loader);
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            _rigs.Add(rig);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        // ---- AC-7: the real assets pass ----

        [Test]
        public void test_loader_real_story_012_assets_pass_and_fill_the_registry()
        {
            var rest = new CountingLoader();
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), rest);

            Assert.IsTrue(loader.LoadAndValidate());

            Assert.AreEqual(1, rest.Calls, "the remaining static data ran after the class and skill data");
            Assert.AreSame(RealSkills(), loader.LoadedSkillDatabase);
            Assert.IsNotNull(loader.LoadedClassRegistry);
        }

        // ---- AC-2: Warrior and Healer from ClassDatabase ----

        [TestCase(ClassType.Warrior, WARRIOR_FIRST_SKILL)]
        [TestCase(ClassType.Healer, HEALER_FIRST_SKILL)]
        public void test_loader_registry_returns_the_class_and_its_skills_in_authored_order(ClassType type, uint first)
        {
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), null);
            Assert.IsTrue(loader.LoadAndValidate());

            Assert.IsTrue(loader.LoadedClassRegistry.TryGetClass((byte)type, out ClassDefinition _));
            IReadOnlyList<SkillID> skills = loader.LoadedClassRegistry.GetClassSkills((byte)type);
            Assert.AreEqual(SKILLS, skills.Count);
            for (int i = 0; i < SKILLS; i++)
            {
                Assert.AreEqual(first + (uint)i, skills[i].Value, "slot " + i);
            }
        }

        // ---- AC-4: missing asset ----

        [Test]
        public void test_loader_null_skill_database_fails_with_one_error()
        {
            var rest = new CountingLoader();
            var loader = new ZoneClassSkillDataLoader(null, RealClasses(), rest);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: SkillDatabase missing");

            Assert.IsFalse(loader.LoadAndValidate());

            Assert.AreEqual(0, rest.Calls, "nothing else loads after a failure");
            Assert.IsNull(loader.LoadedClassRegistry);
            Assert.IsNull(loader.LoadedSkillDatabase);
        }

        [Test]
        public void test_loader_null_class_database_fails_with_one_error()
        {
            var loader = new ZoneClassSkillDataLoader(RealSkills(), null, null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase missing");

            Assert.IsFalse(loader.LoadAndValidate());
            Assert.IsNull(loader.LoadedClassRegistry);
        }

        [Test]
        public void test_loader_class_database_without_classes_fails()
        {
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase has no classes");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_failing_rest_loader_fails_s2()
        {
            var rest = new CountingLoader { Result = false };
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), rest);

            Assert.IsFalse(loader.LoadAndValidate());
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNull(loader.LoadedClassRegistry, "nothing is exposed when S2 fails");
            Assert.IsNull(loader.LoadedSkillDatabase);
        }

        [Test]
        public void test_loader_null_class_entries_fail_as_no_classes()
        {
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(null), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase has no classes");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        // ---- AC-5: CD-10 checks ----

        [Test]
        public void test_loader_valid_fake_warrior_passes()
        {
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(ValidWarrior()), null);

            Assert.IsTrue(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_duplicate_class_type_fails_naming_the_entry()
        {
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(ValidWarrior(), ValidWarrior()), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 1 (Warrior): duplicate class type");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_undefined_class_type_fails_naming_the_raw_value()
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.ClassType = (ClassType)99;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (99): undefined class type");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        // Each field above the [0, 2] bound, with free points lowered so the sum stays 5: the per-field check is not
        // covered by the sum check (CD-10).
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void test_loader_auto_allocation_above_two_fails_even_with_sum_five(int field)
        {
            var entry = new ClassDefinitionEntry
            {
                ClassType = ClassType.Warrior,
                StrengthAutoAlloc = field == 0 ? 3 : 0,
                DexterityAutoAlloc = field == 1 ? 3 : 0,
                VitalityAutoAlloc = field == 2 ? 3 : 0,
                IntelligenceAutoAlloc = field == 3 ? 3 : 0,
                FreePointsPerLevel = 2,
                SkillList = SkillIds(WARRIOR_FIRST_SKILL),
            };
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): auto-allocation outside [0, 2]");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_negative_auto_allocation_fails()
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.IntelligenceAutoAlloc = -1;
            entry.FreePointsPerLevel = 2;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): auto-allocation outside [0, 2]");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [TestCase(0)]
        [TestCase(2)]
        public void test_loader_points_per_level_not_five_fails(int freePoints)
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.FreePointsPerLevel = freePoints; // 4 + free: 4 or 6
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): auto-allocation plus free points is not 5");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        // ---- AC-6: skill list cross-check ----

        [Test]
        public void test_loader_skill_id_missing_from_the_roster_fails_naming_the_slot()
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.SkillList = SkillIds(WARRIOR_FIRST_SKILL);
            entry.SkillList[4] = UNKNOWN_SKILL;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): skill list slot 4 not in SkillDatabase");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_skill_list_repeating_a_skill_fails_naming_the_slot()
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.SkillList[7] = WARRIOR_FIRST_SKILL;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): skill list slot 7 repeats an earlier skill");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [TestCase(0)]
        [TestCase(SKILLS - 1)]
        [TestCase(SKILLS + 1)]
        public void test_loader_skill_list_without_ten_skills_fails(int count)
        {
            ClassDefinitionEntry entry = ValidWarrior();
            var ids = new uint[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = i < SKILLS ? WARRIOR_FIRST_SKILL + (uint)i : UNKNOWN_SKILL;
            }

            entry.SkillList = ids;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(UNKNOWN_SKILL), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): skill list does not have 10 skills");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        [Test]
        public void test_loader_null_skill_list_fails()
        {
            ClassDefinitionEntry entry = ValidWarrior();
            entry.SkillList = null;
            var loader = new ZoneClassSkillDataLoader(FakeSkills(), FakeClasses(entry), null);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: ClassDatabase entry 0 (Warrior): skill list does not have 10 skills");

            Assert.IsFalse(loader.LoadAndValidate());
        }

        // ---- AC-1: the composition uses the loader's data ----

        [Test]
        public void test_composition_with_loader_builds_leveling_and_skill_over_the_loaded_data()
        {
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), null);

            WiRig rig = StartRig(loader);

            Assert.AreSame(loader.LoadedClassRegistry, rig.Comp.ClassRegistryInUse);
            Assert.IsTrue(rig.Comp.ClassRegistryInUse.TryGetClass((byte)ClassType.Warrior, out ClassDefinition _));
            Assert.IsTrue(rig.Comp.ClassRegistryInUse.TryGetClass((byte)ClassType.Healer, out ClassDefinition _));
            Assert.IsNotNull(rig.Comp.SkillService);
            Assert.IsNotNull(rig.Comp.ZoneLeveling);
        }

        // ---- AC-3: overrides win ----

        [Test]
        public void test_composition_overrides_win_over_the_loader()
        {
            var loader = new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), null);
            var registry = new ClassRegistry();
            var rig = new WiRig(staticDataLoader: loader);
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.SkillDatabaseOverride = FakeSkills();
            _rigs.Add(rig);

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreSame(registry, rig.Comp.ClassRegistryInUse);
            Assert.IsFalse(rig.Comp.ClassRegistryInUse.TryGetClass((byte)ClassType.Healer, out ClassDefinition _));
        }

        // A loader that offers the data type but holds none: S5 builds on the silent empty fallbacks, no exception.
        private sealed class EmptyDataLoader : IZoneStaticDataLoader, IZoneClassSkillData
        {
            public ClassRegistry LoadedClassRegistry => null;
            public SkillDatabase LoadedSkillDatabase => null;
            public bool LoadAndValidate() { return true; }
        }

        [Test]
        public void test_composition_loader_without_data_uses_the_empty_fallbacks()
        {
            WiRig rig = StartRig(new EmptyDataLoader());

            Assert.IsNotNull(rig.Comp.ClassRegistryInUse);
            Assert.IsFalse(rig.Comp.ClassRegistryInUse.TryGetClass((byte)ClassType.Warrior, out ClassDefinition _));
            Assert.IsNotNull(rig.Comp.SkillService);
        }

        // ---- AC-4: S2 failure in the composition ----

        [Test]
        public void test_composition_missing_asset_fails_s2_builds_nothing_and_quits_1()
        {
            var loader = new ZoneClassSkillDataLoader(null, RealClasses(), null);
            var rig = new WiRig(staticDataLoader: loader);
            LogAssert.Expect(LogType.Error, "[ZoneServer] S2: SkillDatabase missing");
            LogAssert.Expect(LogType.Error, S2_FAILED);

            rig.RunAll();

            Assert.IsFalse(rig.Observed.Contains(nameof(SkillSystemService)), "no S5 construction");
            Assert.IsNull(rig.Comp.ClassRegistryInUse);
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
        }

        // ---- AC-9: an admitted player's snapshot carries the class's 10 ids ----

        [TestCase(ClassType.Warrior, WARRIOR_FIRST_SKILL)]
        [TestCase(ClassType.Healer, HEALER_FIRST_SKILL)]
        public void test_composition_admitted_player_snapshot_has_the_loaded_class_skill_ids(ClassType type, uint first)
        {
            WiRig rig = StartRig(new ZoneClassSkillDataLoader(RealSkills(), RealClasses(), null));
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(CLIENT_ONE), new AccountID(CLIENT_ONE + ACCOUNT_OFFSET),
                CLIENT_ONE, out byte slot, out EntityID id));
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(CLIENT_ONE));
            rig.Net.ClientListener.AddClient(CLIENT_ONE);
            rig.Comp.GuardChain.RegisterEntityOwnership(CLIENT_ONE, id);
            rig.Comp.GuardChain.MarkSessionReady(CLIENT_ONE);
            rig.Comp.ZoneInstancing.Directory.Add(CLIENT_ONE, new CharacterID(CLIENT_ONE));
            // The class type comes from the record, through the zone's own Persistence applier (Character Persistence Story 018).
            CharacterRecord record = CharacterStubDefaults.CreateRecord(new AccountID(CLIENT_ONE + ACCOUNT_OFFSET), new CharacterID(CLIENT_ONE), (byte)type);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Comp.PersistenceService.ApplyLoadedRecord(id, record));
            rig.Comp.ZoneInstancing.EntryCompletion.OnPlayerAdmitted(CLIENT_ONE, slot, id);
            LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));

            rig.Net.FireTick();

            Assert.IsTrue(TryDecodeSnapshot(rig, CLIENT_ONE, out SkillCooldownSnapshot snapshot), "snapshot was sent");
            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                Assert.AreEqual(new SkillCooldownEntry(first + (uint)i, 0u), snapshot.GetEntry(i), "entry " + i);
            }
        }

        private static bool TryDecodeSnapshot(WiRig rig, uint client, out SkillCooldownSnapshot snapshot)
        {
            snapshot = default;
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to != client || !MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope envelope)
                    || envelope.MessageTypeId != SkillCooldownSnapshot.MessageTypeId)
                {
                    continue;
                }

                return SkillCooldownSnapshotCodec.TryReadBody(data.AsSpan(ServerMessageEnvelope.WireSize), out snapshot);
            }

            return false;
        }

        // ---- AC-8: the server scene references both assets ----

        // Opens the server scene additively (no Awake runs in edit mode) and reads the host's two fields, so a swapped
        // pair or a reference on another component fails. The scene is closed again whatever happens.
        [TestCase("_skillDatabase", SKILL_DATABASE_PATH)]
        [TestCase("_classDatabase", CLASS_DATABASE_PATH)]
        public void test_server_scene_host_field_references_the_asset(string field, string assetPath)
        {
            Scene scene = EditorSceneManager.OpenScene(SERVER_SCENE_PATH, OpenSceneMode.Additive);
            try
            {
                ZoneServerHost host = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    host = host != null ? host : root.GetComponentInChildren<ZoneServerHost>(true);
                }

                Assert.IsNotNull(host, "ZoneServerHost in " + SERVER_SCENE_PATH);
                SerializedProperty property = new SerializedObject(host).FindProperty(field);
                Assert.IsNotNull(property, field);
                Assert.AreSame(AssetDatabase.LoadMainAssetAtPath(assetPath), property.objectReferenceValue);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
#endif
