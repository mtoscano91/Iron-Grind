using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 003 - unlock on level-up and SkillUnlockNotification
    /// (CR-SK-21, AC-SK-11, AC-SK-14, AC-SK-45, AC-SK-46; TR-sk-003). AC-SK-49 is Story 011.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_Unlock_Tests
    {
        private const byte CLASS_FULL = 0;
        private const uint ID_BASE = 100;
        private const int MAX_PLAYERS = 50;
        private const int SLOT_A = 0;
        private const int SLOT_B = 1;
        private const int LEVEL_MIN = 1;
        private const int LEVEL_MAX = 60;
        private const int UNLOCK_LEVEL = 5;
        private const int LEVEL_BEFORE_UNLOCK = 4;
        private const int LEVEL_WITHOUT_SKILL = 3;
        private const int LEVEL_LATER = 6;
        private const int LEVEL_SECOND_STEP = 2;
        private const int SHORT_BODY = 3;
        private const int LONG_BODY = 5;
        private const uint CODEC_SKILL_VALUE = 0x01020304u;
        private const ushort EXPECTED_MESSAGE_TYPE_ID = 0xE151;
        private const int EXPECTED_BODY_SIZE = 4;
        private static readonly int[] UNLOCK_LEVELS = { 1, 1, 5, 15, 20, 25, 35, 42, 50, 58 };
        private static readonly byte[] CODEC_BYTES_LE = { 0x04, 0x03, 0x02, 0x01 };

        private static readonly EntityID PLAYER_A = new EntityID(1001);
        private static readonly EntityID PLAYER_B = new EntityID(1002);
        private static readonly EntityID MOB = new EntityID(2001);
        private static readonly EntityID UNKNOWN = new EntityID(3001);

        private SkillDatabase _database;
        private ClassRegistry _registry;
        private FakeLevels _levels;
        private FakeSlotIndex _slots;
        private RecordingUnlockOutput _output;
        private SkillSystemService _service;

        private sealed class FakeLevels : ISkillLevelSource
        {
            private readonly Dictionary<uint, int> _levels = new Dictionary<uint, int>();

            public void Set(EntityID id, int level) { _levels[id.RawValue] = level; }

            public int GetLevel(EntityID entity) => _levels.TryGetValue(entity.RawValue, out int l) ? l : 0;
        }

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
        }

        private sealed class RecordingUnlockOutput : ISkillUnlockOutput
        {
            public readonly List<(EntityID Owner, SkillID Skill)> Sent = new List<(EntityID, SkillID)>();

            public void SendUnlock(EntityID owner, SkillID skillId) { Sent.Add((owner, skillId)); }
        }

        private sealed class ThrowingUnlockOutput : ISkillUnlockOutput
        {
            public void SendUnlock(EntityID owner, SkillID skillId) { throw new InvalidOperationException("output-failure"); }
        }

        // Records, for each send, whether the skill in every slot was unlocked at that moment (slots 0 and 1).
        private sealed class StoreReadingOutput : ISkillUnlockOutput
        {
            public SkillSystemService Service;
            public readonly List<bool> UnlockedAtSend = new List<bool>();

            public void SendUnlock(EntityID owner, SkillID skillId)
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    Service.TryGetSkill(owner, slot, out SkillInstance skill);
                    UnlockedAtSend.Add(skill.IsUnlocked);
                }
            }
        }

        private sealed class FakeBroadcaster : ILevelingEventBroadcaster
        {
            private Action<LevelUpEventArgs> _handlers;

            public event Action<LevelUpEventArgs> OnLevelUp
            {
                add { _handlers += value; }
                remove { _handlers -= value; }
            }

            public Delegate[] Handlers => _handlers == null ? Array.Empty<Delegate>() : _handlers.GetInvocationList();

            public void Raise(EntityID entity, int level) { _handlers?.Invoke(new LevelUpEventArgs(entity, level)); }
        }

        [SetUp]
        public void SetUp()
        {
            _registry = new ClassRegistry();
            _levels = new FakeLevels();
            _slots = new FakeSlotIndex();
            _output = new RecordingUnlockOutput();
            _slots.Map(PLAYER_A, SLOT_A);
            _slots.Map(MOB, MAX_PLAYERS); // mob indices start at the player capacity
            _levels.Set(PLAYER_A, LEVEL_MIN);
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

        private static uint IdAt(int index) => ID_BASE + (uint)index;

        // Registers CLASS_FULL with one skill per unlock level, but only the first definedCount have definitions.
        private void Build(int[] unlockLevels, int definedCount)
        {
            var ids = new uint[unlockLevels.Length];
            var defs = new SkillDefinition[definedCount];
            for (int i = 0; i < unlockLevels.Length; i++)
            {
                ids[i] = IdAt(i);
                if (i < definedCount)
                {
                    defs[i] = new SkillDefinition { ID = ids[i], UnlockLevel = unlockLevels[i] };
                }
            }

            _registry.RegisterClass(CLASS_FULL, new ClassDefinition(0, 0, 0, 0, 0, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);
            _service = new SkillSystemService(_registry, _database, _levels, _slots, _output, MAX_PLAYERS);
        }

        private void BuildAndSpawn(int[] unlockLevels, int spawnLevel)
        {
            Build(unlockLevels, unlockLevels.Length);
            _levels.Set(PLAYER_A, spawnLevel);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);
        }

        private bool IsUnlocked(int slot)
        {
            Assert.That(_service.TryGetSkill(PLAYER_A, slot, out SkillInstance skill), Is.True);
            return skill.IsUnlocked;
        }

        private void AssertSent(params uint[] expectedIds)
        {
            Assert.That(_output.Sent.Count, Is.EqualTo(expectedIds.Length));
            for (int i = 0; i < expectedIds.Length; i++)
            {
                Assert.That(_output.Sent[i].Skill, Is.EqualTo(new SkillID(expectedIds[i])), "notification " + i);
                Assert.That(_output.Sent[i].Owner, Is.EqualTo(PLAYER_A), "recipient " + i);
            }
        }

        // ---- AC-SK-11 ----

        [Test]
        public void test_unlock_on_level_up_at_unlock_level_sets_unlocked_before_return()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_BEFORE_UNLOCK);
            Assert.That(IsUnlocked(0), Is.False);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(IsUnlocked(0), Is.True);
        }

        // ---- AC-SK-14 ----

        [Test]
        public void test_unlock_on_level_up_two_through_sixty_never_reverts_unlocked()
        {
            BuildAndSpawn(UNLOCK_LEVELS, LEVEL_MIN);
            var wasUnlocked = new bool[UNLOCK_LEVELS.Length];
            for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
            {
                wasUnlocked[i] = IsUnlocked(i);
            }

            for (int level = LEVEL_SECOND_STEP; level <= LEVEL_MAX; level++)
            {
                _service.OnLevelUp(PLAYER_A, level);

                for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
                {
                    bool now = IsUnlocked(i);
                    Assert.That(wasUnlocked[i] && !now, Is.False, "slot " + i + " reverted at level " + level);
                    wasUnlocked[i] = now;
                }
            }

            for (int i = 0; i < UNLOCK_LEVELS.Length; i++)
            {
                Assert.That(wasUnlocked[i], Is.True, "slot " + i + " locked after level " + LEVEL_MAX);
            }
        }

        // ---- AC-SK-45 ----

        [Test]
        public void test_unlock_on_level_up_one_unlock_sends_one_notification_to_owner()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_BEFORE_UNLOCK);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            AssertSent(IdAt(0));
        }

        // ---- AC-SK-46 ----

        [Test]
        public void test_unlock_on_level_up_no_threshold_crossed_sends_nothing()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, LEVEL_WITHOUT_SKILL);

            Assert.That(_output.Sent, Is.Empty);
            Assert.That(IsUnlocked(0), Is.False);
        }

        // ---- Multi-level, same level, already unlocked ----

        [Test]
        public void test_unlock_on_level_up_multi_level_gain_sends_notifications_in_level_order()
        {
            BuildAndSpawn(new[] { LEVEL_BEFORE_UNLOCK, LEVEL_LATER }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, LEVEL_BEFORE_UNLOCK);
            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);
            _service.OnLevelUp(PLAYER_A, LEVEL_LATER);

            AssertSent(IdAt(0), IdAt(1));
            Assert.That(IsUnlocked(0), Is.True);
            Assert.That(IsUnlocked(1), Is.True);
        }

        [Test]
        public void test_unlock_on_level_up_two_skills_same_level_unlock_both_with_two_notifications()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL, UNLOCK_LEVEL }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(IsUnlocked(0), Is.True);
            Assert.That(IsUnlocked(1), Is.True);
            AssertSent(IdAt(0), IdAt(1));
        }

        [Test]
        public void test_unlock_on_level_up_skill_already_unlocked_at_spawn_sends_nothing()
        {
            BuildAndSpawn(new[] { LEVEL_MIN }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, LEVEL_MIN);

            Assert.That(_output.Sent, Is.Empty);
            Assert.That(IsUnlocked(0), Is.True);
        }

        [Test]
        public void test_unlock_on_level_up_same_level_twice_notifies_once()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);
            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            AssertSent(IdAt(0));
        }

        // ---- Unknown entity / mob / despawned ----

        [Test]
        public void test_unlock_on_level_up_unknown_entity_is_ignored()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            Assert.DoesNotThrow(() => _service.OnLevelUp(UNKNOWN, UNLOCK_LEVEL));

            Assert.That(_output.Sent, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unlock_on_level_up_mob_entity_is_ignored()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            Assert.DoesNotThrow(() => _service.OnLevelUp(MOB, UNLOCK_LEVEL));

            Assert.That(_output.Sent, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unlock_on_level_up_after_despawn_is_ignored()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            _service.OnEntityDespawned(PLAYER_A);

            Assert.DoesNotThrow(() => _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL));

            Assert.That(_output.Sent, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Missing definition ----

        [Test]
        public void test_unlock_on_level_up_missing_definition_stays_locked_without_notification_or_log()
        {
            Build(new[] { UNLOCK_LEVEL, UNLOCK_LEVEL }, 1); // second skill has no definition
            LogAssert.Expect(LogType.Error, new Regex("no SkillDatabase definition")); // the spawn-time error only
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            for (int level = LEVEL_MIN; level <= LEVEL_MAX; level++)
            {
                _service.OnLevelUp(PLAYER_A, level);
            }

            Assert.That(IsUnlocked(1), Is.False);
            AssertSent(IdAt(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Subscription ----

        [Test]
        public void test_unlock_subscribe_then_raise_unlocks_and_unsubscribe_stops_it()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL, LEVEL_LATER }, LEVEL_MIN);
            var broadcaster = new FakeBroadcaster();

            _service.Subscribe(broadcaster);
            broadcaster.Raise(PLAYER_A, UNLOCK_LEVEL);
            _service.Unsubscribe(broadcaster);
            broadcaster.Raise(PLAYER_A, LEVEL_LATER);

            AssertSent(IdAt(0));
            Assert.That(IsUnlocked(1), Is.False);
            Assert.That(broadcaster.Handlers, Is.Empty);
        }

        [Test]
        public void test_unlock_subscribe_handler_is_a_named_method_not_a_lambda()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            var broadcaster = new FakeBroadcaster();

            _service.Subscribe(broadcaster);

            Assert.That(broadcaster.Handlers.Length, Is.EqualTo(1));
            Assert.That(broadcaster.Handlers[0].Method.Name, Does.Not.Contain("<"));
        }

        [Test]
        public void test_unlock_subscribe_and_unsubscribe_null_broadcaster_throw()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            Assert.Throws<ArgumentNullException>(() => _service.Subscribe(null));
            Assert.Throws<ArgumentNullException>(() => _service.Unsubscribe(null));
        }

        [Test]
        public void test_unlock_constructor_null_unlock_output_throws()
        {
            Build(new[] { UNLOCK_LEVEL }, 1);

            Assert.Throws<ArgumentNullException>(
                () => new SkillSystemService(_registry, _database, _levels, _slots, null, MAX_PLAYERS));
        }

        // ---- Subscription guard ----

        [Test]
        public void test_unlock_subscribe_twice_throws_and_level_up_sends_one_notice()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            var broadcaster = new FakeBroadcaster();
            _service.Subscribe(broadcaster);

            Assert.Throws<InvalidOperationException>(() => _service.Subscribe(broadcaster));
            Assert.Throws<InvalidOperationException>(() => _service.Subscribe(new FakeBroadcaster()));
            broadcaster.Raise(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(broadcaster.Handlers.Length, Is.EqualTo(1));
            AssertSent(IdAt(0));
        }

        [Test]
        public void test_unlock_unsubscribe_without_subscribe_is_a_noop()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            var subscribed = new FakeBroadcaster();
            var other = new FakeBroadcaster();

            Assert.DoesNotThrow(() => _service.Unsubscribe(other));
            _service.Subscribe(subscribed);
            Assert.DoesNotThrow(() => _service.Unsubscribe(other));
            subscribed.Raise(PLAYER_A, UNLOCK_LEVEL);

            AssertSent(IdAt(0));
        }

        [Test]
        public void test_unlock_subscribe_unsubscribe_subscribe_again_works()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            var broadcaster = new FakeBroadcaster();

            _service.Subscribe(broadcaster);
            _service.Unsubscribe(broadcaster);
            Assert.DoesNotThrow(() => _service.Subscribe(broadcaster));
            broadcaster.Raise(PLAYER_A, UNLOCK_LEVEL);

            AssertSent(IdAt(0));
        }

        // ---- Two-pass unlock ----

        [Test]
        public void test_unlock_throwing_output_still_unlocks_every_slot_of_the_level()
        {
            var throwing = new ThrowingUnlockOutput();
            Build(new[] { UNLOCK_LEVEL, UNLOCK_LEVEL }, 2);
            _service = new SkillSystemService(_registry, _database, _levels, _slots, throwing, MAX_PLAYERS);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            Assert.Throws<InvalidOperationException>(() => _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL));

            Assert.That(IsUnlocked(0), Is.True);
            Assert.That(IsUnlocked(1), Is.True);
        }

        [Test]
        public void test_unlock_output_sees_every_slot_unlocked_before_the_first_send()
        {
            var reading = new StoreReadingOutput();
            Build(new[] { UNLOCK_LEVEL, UNLOCK_LEVEL }, 2);
            _service = new SkillSystemService(_registry, _database, _levels, _slots, reading, MAX_PLAYERS);
            reading.Service = _service;
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(reading.UnlockedAtSend, Is.EqualTo(new[] { true, true, true, true }));
        }

        // ---- Edge levels and isolation ----

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(LEVEL_MAX + 1)]
        [TestCase(int.MaxValue)]
        public void test_unlock_on_level_up_level_outside_range_unlocks_nothing(int level)
        {
            BuildAndSpawn(UNLOCK_LEVELS, LEVEL_MIN);
            var before = new bool[UNLOCK_LEVELS.Length];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = IsUnlocked(i);
            }

            _service.OnLevelUp(PLAYER_A, level);

            Assert.That(_output.Sent, Is.Empty);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(IsUnlocked(i), Is.EqualTo(before[i]), "slot " + i);
            }
        }

        [Test]
        public void test_unlock_on_level_up_skipped_level_leaves_the_skipped_skill_locked()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);

            _service.OnLevelUp(PLAYER_A, LEVEL_LATER);

            Assert.That(IsUnlocked(0), Is.False);
            Assert.That(_output.Sent, Is.Empty);
        }

        [Test]
        public void test_unlock_on_level_up_one_player_does_not_unlock_another_players_skills()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            _slots.Map(PLAYER_B, SLOT_B);
            _levels.Set(PLAYER_B, LEVEL_MIN);
            _service.OnEntitySpawned(PLAYER_B, ClassType.Warrior);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(_service.TryGetSkill(PLAYER_B, 0, out SkillInstance other), Is.True);
            Assert.That(other.IsUnlocked, Is.False);
            AssertSent(IdAt(0));
        }

        [Test]
        public void test_unlock_respawn_at_level_one_then_level_five_sends_a_second_notice()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);
            _service.OnEntityDespawned(PLAYER_A);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            AssertSent(IdAt(0), IdAt(0));
        }

        [Test]
        public void test_unlock_respawn_at_unlock_level_then_level_up_sends_no_notice()
        {
            BuildAndSpawn(new[] { UNLOCK_LEVEL }, LEVEL_MIN);
            _service.OnEntityDespawned(PLAYER_A);
            _levels.Set(PLAYER_A, UNLOCK_LEVEL);
            _service.OnEntitySpawned(PLAYER_A, ClassType.Warrior);

            _service.OnLevelUp(PLAYER_A, UNLOCK_LEVEL);

            Assert.That(_output.Sent, Is.Empty);
        }

        // ---- Codec ----

        [Test]
        public void test_unlock_codec_message_type_id_and_body_size_match_wire_spec()
        {
            Assert.That(SkillUnlockNotification.MessageTypeId, Is.EqualTo(EXPECTED_MESSAGE_TYPE_ID));
            Assert.That(SkillUnlockNotification.BodySize, Is.EqualTo(EXPECTED_BODY_SIZE));
        }

        [Test]
        public void test_unlock_codec_round_trip_writes_little_endian_and_reads_back()
        {
            var message = new SkillUnlockNotification(new SkillID(CODEC_SKILL_VALUE));
            var buffer = new byte[SkillUnlockNotification.BodySize];

            int written = SkillUnlockNotificationCodec.WriteBody(buffer, in message);
            bool ok = SkillUnlockNotificationCodec.TryReadBody(buffer, out SkillUnlockNotification read);

            Assert.That(written, Is.EqualTo(SkillUnlockNotification.BodySize));
            Assert.That(buffer, Is.EqualTo(CODEC_BYTES_LE));
            Assert.That(ok, Is.True);
            Assert.That(read, Is.EqualTo(message));
        }

        [TestCase(0u)]
        [TestCase(CODEC_SKILL_VALUE)]
        [TestCase(uint.MaxValue)]
        public void test_unlock_codec_round_trip_preserves_boundary_ids(uint value)
        {
            var message = new SkillUnlockNotification(new SkillID(value));
            var buffer = new byte[SkillUnlockNotification.BodySize];

            SkillUnlockNotificationCodec.WriteBody(buffer, in message);
            bool ok = SkillUnlockNotificationCodec.TryReadBody(buffer, out SkillUnlockNotification read);

            Assert.That(ok, Is.True);
            Assert.That(read.SkillId.Value, Is.EqualTo(value));
        }

        [TestCase(SHORT_BODY)]
        [TestCase(LONG_BODY)]
        public void test_unlock_codec_wrong_length_body_is_rejected(int length)
        {
            bool ok = SkillUnlockNotificationCodec.TryReadBody(new byte[length], out SkillUnlockNotification read);

            Assert.That(ok, Is.False);
            Assert.That(read, Is.EqualTo(default(SkillUnlockNotification)));
        }

        [Test]
        public void test_unlock_routing_registry_has_owner_only_reliable_ordered_row()
        {
            Assert.That(MessageRoutingRegistry.TryGetEntry(SkillUnlockNotification.MessageTypeId, out MessageRoutingEntry entry), Is.True);
            Assert.That(entry.Channel, Is.EqualTo(NetworkChannel.ReliableOrdered));
            Assert.That(entry.Direction, Is.EqualTo(MessageDirection.ServerToOwningClient));
        }
    }
}
