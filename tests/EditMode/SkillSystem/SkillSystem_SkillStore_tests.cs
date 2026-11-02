using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 002 - per-entity skill store, spawn and despawn
    /// (CR-SK-20, EC-SK-12, AC-SK-10, AC-SK-13, AC-SK-47, AC-SK-48).
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_SkillStore_Tests
    {
        private const byte CLASS_FULL = 0;
        private const byte CLASS_EMPTY = 1;
        private const uint ID_BASE = 100;
        private const int FIRST_MOB_INDEX = 50;
        private const int SLOT_A = 0;
        private const int SLOT_B = 1;
        private const int SLOT_C = 2;
        private const int MAX_PLAYERS = FIRST_MOB_INDEX; // mob indices start at the player capacity
        private const int SMALL_CAPACITY = 2;
        private const int COOLDOWN_SLOT = 2;
        private const int FOUND_SLOT = 3;
        private const uint ABSENT_OFFSET = 99;
        private const int SHORT_COUNT = 2;
        private const int LEVEL_MAX = 60;
        private const uint COOLDOWN_TICK = 777;
        private const int OVERSIZED_COUNT = 11;
        private static readonly int[] UNLOCK_LEVELS = { 1, 1, 5, 15, 20, 25, 35, 42, 50, 58 };

        private static readonly EntityID PLAYER_A = new EntityID(1001);
        private static readonly EntityID PLAYER_B = new EntityID(1002);
        private static readonly EntityID MOB = new EntityID(2001);
        private static readonly EntityID UNKNOWN = new EntityID(3001);

        private SkillDatabase _database;
        private ClassRegistry _registry;
        private FakeLevels _levels;
        private FakeSlotIndex _slots;
        private SkillSystemService _service;

        private sealed class FakeLevels : ISkillLevelSource
        {
            private readonly Dictionary<uint, int> _levels = new Dictionary<uint, int>();

            public void Set(EntityID id, int level) { _levels[id.RawValue] = level; }

            public int Reads { get; private set; }

            public int GetLevel(EntityID entity)
            {
                Reads++;
                return _levels.TryGetValue(entity.RawValue, out int l) ? l : 0;
            }
        }

        private sealed class NoOpUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { }
        }

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public void Unmap(EntityID id) { _map.Remove(id.RawValue); }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
        }

        [SetUp]
        public void SetUp()
        {
            _registry = new ClassRegistry();
            _levels = new FakeLevels();
            _slots = new FakeSlotIndex();
            _slots.Map(PLAYER_A, SLOT_A);
            _slots.Map(PLAYER_B, SLOT_B);
            _slots.Map(MOB, FIRST_MOB_INDEX);
            _registry.RegisterClass(CLASS_EMPTY, new ClassDefinition(0, 0, 0, 0, 0, null));
            _levels.Set(PLAYER_A, 1);
            _levels.Set(PLAYER_B, 1);
        }

        [TearDown]
        public void TearDown()
        {
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        // Registers CLASS_FULL with one skill per unlock level (ids ID_BASE + index) and builds the service.
        private void BuildFullClass(int[] unlockLevels)
        {
            var ids = new uint[unlockLevels.Length];
            var defs = new SkillDefinition[unlockLevels.Length];
            for (int i = 0; i < unlockLevels.Length; i++)
            {
                ids[i] = ID_BASE + (uint)i;
                defs[i] = new SkillDefinition { ID = ids[i], UnlockLevel = unlockLevels[i] };
            }

            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, ids));
            BuildService(defs);
        }

        private void BuildService(SkillDefinition[] defs)
        {
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);
            _service = new SkillSystemService(_registry, _database, _levels, _slots, new NoOpUnlockOutput(), MAX_PLAYERS);
        }

        private static int[] Repeat(int value, int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = value;
            }

            return result;
        }

        // ---- AC-SK-10 ----

        [TestCase(4, false)]
        [TestCase(5, true)]
        public void test_spawn_unlock_level_five_at_boundary_levels_sets_unlocked(int level, bool expected)
        {
            BuildFullClass(new[] { 5 });
            _levels.Set(PLAYER_A, level);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.TryGetSkill(PLAYER_A, 0, out SkillInstance skill), Is.True);
            Assert.That(skill.IsUnlocked, Is.EqualTo(expected));
        }

        // ---- AC-SK-13 / AC-SK-47 ----

        [Test]
        public void test_spawn_ten_skill_class_at_level_one_unlocks_only_level_one_skills()
        {
            BuildFullClass(UNLOCK_LEVELS);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(UNLOCK_LEVELS.Length));
            for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
            {
                Assert.That(_service.TryGetSkill(PLAYER_A, i, out SkillInstance skill), Is.True);
                Assert.That(skill.IsUnlocked, Is.EqualTo(UNLOCK_LEVELS[i] <= 1), "slot " + i);
            }
        }

        [Test]
        public void test_spawn_slot_order_matches_getclassskills_order()
        {
            BuildFullClass(UNLOCK_LEVELS);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            IReadOnlyList<SkillID> expected = _registry.GetClassSkills(CLASS_FULL);
            for (int i = 0; i < expected.Count; i++)
            {
                _service.TryGetSkill(PLAYER_A, i, out SkillInstance skill);
                Assert.That(skill.SkillID, Is.EqualTo(expected[i]), "slot " + i);
            }
        }

        [Test]
        public void test_spawn_at_max_level_unlocks_every_slot()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _levels.Set(PLAYER_A, LEVEL_MAX);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
            {
                _service.TryGetSkill(PLAYER_A, i, out SkillInstance skill);
                Assert.That(skill.IsUnlocked, Is.True, "slot " + i);
            }
        }

        [Test]
        public void test_spawn_class_with_eleven_skills_keeps_first_ten_and_logs_error()
        {
            BuildFullClass(Repeat(1, OVERSIZED_COUNT));
            LogAssert.Expect(LogType.Error, new Regex("only the first 10"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(SkillSystemService.MAX_SKILLS_PER_ENTITY));
        }

        // ---- AC-SK-48 ----

        [Test]
        public void test_spawn_empty_class_logs_warning_and_gives_zero_slots()
        {
            BuildFullClass(UNLOCK_LEVELS);
            LogAssert.Expect(LogType.Warning, new Regex("EC-SK-12"));

            Assert.DoesNotThrow(() => _service.OnEntitySpawned(PLAYER_A, ClassType.Healer));

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
            Assert.That(_service.TryGetSkill(PLAYER_A, 0, out _), Is.False);
        }

        // ---- Cooldowns ----

        [Test]
        public void test_spawn_all_cooldown_expiry_ticks_are_zero()
        {
            BuildFullClass(UNLOCK_LEVELS);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
            {
                _service.TryGetSkill(PLAYER_A, i, out SkillInstance skill);
                Assert.That(skill.CooldownExpiryTick, Is.EqualTo(0u), "slot " + i);
            }
        }

        [Test]
        public void test_despawn_then_respawn_same_id_resets_cooldown()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, COOLDOWN_SLOT, COOLDOWN_TICK), Is.True);

            _service.OnEntityDespawned(PLAYER_A);
            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.TryGetSkill(PLAYER_A, COOLDOWN_SLOT, out SkillInstance skill);
            Assert.That(skill.CooldownExpiryTick, Is.EqualTo(0u));
        }

        [Test]
        public void test_despawn_when_slot_index_forgot_entity_still_releases_store()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _slots.Unmap(PLAYER_A);

            _service.OnEntityDespawned(PLAYER_A);
            _slots.Map(PLAYER_A, SLOT_A);

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
        }

        [Test]
        public void test_despawn_unknown_entity_is_noop_and_leaves_others_untouched()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.DoesNotThrow(() => _service.OnEntityDespawned(UNKNOWN));

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(UNLOCK_LEVELS.Length));
        }

        // ---- Missing definition ----

        [Test]
        public void test_spawn_class_skill_without_definition_stays_locked_and_logs_one_error()
        {
            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, new[] { ID_BASE, ID_BASE + 1 }));
            BuildService(new[] { new SkillDefinition { ID = ID_BASE, UnlockLevel = 1 } });
            _levels.Set(PLAYER_A, LEVEL_MAX);
            LogAssert.Expect(LogType.Error, new Regex("no SkillDatabase definition"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.TryGetSkill(PLAYER_A, 0, out SkillInstance known);
            _service.TryGetSkill(PLAYER_A, 1, out SkillInstance missing);
            Assert.That(known.IsUnlocked, Is.True);
            Assert.That(missing.SkillID, Is.EqualTo(new SkillID(ID_BASE + 1)));
            Assert.That(missing.IsUnlocked, Is.False);
        }

        // ---- Spawn twice ----

        [Test]
        public void test_spawn_twice_without_despawn_warns_once_rebuilds_and_leaves_other_record_untouched()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _service.OnEntitySpawned(PLAYER_B, ClassType.Warrior);
            _service.SetCooldownForTesting(PLAYER_A, 0, COOLDOWN_TICK);
            _service.SetCooldownForTesting(PLAYER_B, 0, COOLDOWN_TICK);
            LogAssert.Expect(LogType.Warning, new Regex("spawn without despawn.*same entity|same entity.*spawn without despawn"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.TryGetSkill(PLAYER_A, 0, out SkillInstance a);
            _service.TryGetSkill(PLAYER_B, 0, out SkillInstance b);
            Assert.That(a.CooldownExpiryTick, Is.EqualTo(0u));
            Assert.That(b.CooldownExpiryTick, Is.EqualTo(COOLDOWN_TICK));
            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(UNLOCK_LEVELS.Length));
        }

        // ---- Players only ----

        [Test]
        public void test_spawn_mob_index_logs_warning_and_creates_no_record()
        {
            BuildFullClass(UNLOCK_LEVELS);
            LogAssert.Expect(LogType.Warning, new Regex("no player slot"));

            _service.OnEntitySpawned(MOB, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(MOB), Is.EqualTo(0));
        }

        [Test]
        public void test_spawn_unknown_entity_logs_warning_and_creates_no_record()
        {
            BuildFullClass(UNLOCK_LEVELS);
            LogAssert.Expect(LogType.Warning, new Regex("no player slot"));

            _service.OnEntitySpawned(UNKNOWN, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(UNKNOWN), Is.EqualTo(0));
        }

        // ---- Reused slot ----

        [Test]
        public void test_reused_slot_does_not_leak_previous_entity_skills()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _slots.Unmap(PLAYER_A);
            _slots.Map(PLAYER_B, SLOT_A);
            LogAssert.Expect(LogType.Warning, new Regex("different entity"));

            _service.OnEntitySpawned(PLAYER_B, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
            Assert.That(_service.TryGetSkill(PLAYER_A, 0, out _), Is.False);
            Assert.That(_service.GetSkillCount(PLAYER_B), Is.EqualTo(UNLOCK_LEVELS.Length));
        }

        // ---- Lookup ----

        [Test]
        public void test_tryfindslot_returns_slot_of_skill_and_false_for_absent_skill()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.TryFindSlot(PLAYER_A, new SkillID(ID_BASE + FOUND_SLOT), out int slot), Is.True);
            Assert.That(slot, Is.EqualTo(FOUND_SLOT));
            Assert.That(_service.TryFindSlot(PLAYER_A, new SkillID(ID_BASE + ABSENT_OFFSET), out _), Is.False);
            Assert.That(_service.TryFindSlot(PLAYER_B, new SkillID(ID_BASE), out _), Is.False);
        }

        [TestCase(-1)]
        [TestCase(SkillSystemService.MAX_SKILLS_PER_ENTITY)]
        public void test_tryget_skill_slot_outside_range_returns_false(int slot)
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.TryGetSkill(PLAYER_A, slot, out _), Is.False);
        }

        // ---- Constructor ----

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void test_constructor_null_argument_throws(int nullIndex)
        {
            BuildFullClass(UNLOCK_LEVELS);
            IClassRegistry registry = nullIndex == 0 ? null : _registry;
            ISkillDefinitionSource definitions = nullIndex == 1 ? null : _database;
            ISkillLevelSource levels = nullIndex == 2 ? null : _levels;
            IEntitySlotIndex slots = nullIndex == 3 ? null : _slots;

            Assert.Throws<ArgumentNullException>(() => new SkillSystemService(registry, definitions, levels, slots, new NoOpUnlockOutput(), MAX_PLAYERS));
        }

        // ---- OnLevelUp stub ----

        [Test]
        public void test_onlevelup_stub_does_not_throw_and_changes_nothing()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.DoesNotThrow(() => _service.OnLevelUp(PLAYER_A, LEVEL_MAX));

            _service.TryGetSkill(PLAYER_A, UNLOCK_LEVELS.Length - 1, out SkillInstance skill);
            Assert.That(skill.IsUnlocked, Is.False);
        }

        [Test]
        public void test_spawn_on_new_slot_without_despawn_releases_stale_record()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _slots.Map(PLAYER_A, SLOT_C);
            LogAssert.Expect(LogType.Warning, new Regex("still held record"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _slots.Unmap(PLAYER_A);
            _service.OnEntityDespawned(PLAYER_A);

            // Without the release, the owner scan would still find the stale record at SLOT_A.
            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
        }

        // ---- Capacity ----

        [TestCase(0)]
        [TestCase(256)]
        public void test_constructor_max_players_outside_range_throws(int maxPlayers)
        {
            BuildFullClass(UNLOCK_LEVELS);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SkillSystemService(_registry, _database, _levels, _slots, new NoOpUnlockOutput(), maxPlayers));
        }

        [Test]
        public void test_spawn_index_at_small_capacity_is_refused_as_mob()
        {
            BuildFullClass(UNLOCK_LEVELS);
            var service = new SkillSystemService(_registry, _database, _levels, _slots, new NoOpUnlockOutput(), SMALL_CAPACITY);
            _slots.Map(PLAYER_A, SMALL_CAPACITY);
            LogAssert.Expect(LogType.Warning, new Regex("no player slot"));

            service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(service.GetSkillCount(PLAYER_A), Is.EqualTo(0));
        }

        // ---- Re-spawn, level read, logs ----

        [Test]
        public void test_respawn_with_shorter_class_list_drops_extra_slots()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, new[] { ID_BASE, ID_BASE + 1 }));
            LogAssert.Expect(LogType.Warning, new Regex("same entity"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.GetSkillCount(PLAYER_A), Is.EqualTo(SHORT_COUNT));
            Assert.That(_service.TryGetSkill(PLAYER_A, SHORT_COUNT, out _), Is.False);
        }

        [Test]
        public void test_spawn_reads_level_once()
        {
            BuildFullClass(UNLOCK_LEVELS);

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_levels.Reads, Is.EqualTo(1));
        }

        [Test]
        public void test_spawn_empty_class_or_mob_does_not_read_level()
        {
            BuildFullClass(UNLOCK_LEVELS);
            LogAssert.Expect(LogType.Warning, new Regex("EC-SK-12"));
            LogAssert.Expect(LogType.Warning, new Regex("no player slot"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Healer);
            _service.OnEntitySpawned(MOB, ClassType.Warrior);

            Assert.That(_levels.Reads, Is.EqualTo(0));
        }

        [Test]
        public void test_spawn_two_missing_definitions_logs_two_errors()
        {
            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, new[] { ID_BASE, ID_BASE + 1 }));
            BuildService(new SkillDefinition[0]);
            LogAssert.Expect(LogType.Error, new Regex("no SkillDatabase definition; slot 0"));
            LogAssert.Expect(LogType.Error, new Regex("no SkillDatabase definition; slot 1"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_spawn_at_level_zero_logs_warning_and_locks_every_slot()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _levels.Set(PLAYER_A, 0);
            LogAssert.Expect(LogType.Warning, new Regex("not registered in CharacterStats"));

            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.TryGetSkill(PLAYER_A, 0, out SkillInstance first);
            Assert.That(first.IsUnlocked, Is.False);
        }

        [Test]
        public void test_setcooldown_without_record_or_valid_slot_returns_false()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.That(_service.SetCooldownForTesting(PLAYER_B, 0, COOLDOWN_TICK), Is.False);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, -1, COOLDOWN_TICK), Is.False);
            Assert.That(_service.SetCooldownForTesting(PLAYER_A, UNLOCK_LEVELS.Length, COOLDOWN_TICK), Is.False);
        }

        // ---- Allocation ----

        [Test]
        public void test_second_spawn_after_warmup_does_not_allocate()
        {
            BuildFullClass(UNLOCK_LEVELS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
            _service.OnEntityDespawned(PLAYER_A);

            Assert.That(() => { _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior); }, Is.Not.AllocatingGCMemory());
        }
    }
}
