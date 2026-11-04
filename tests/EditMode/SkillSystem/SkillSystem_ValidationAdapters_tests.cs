using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for the Skill System Story 005 production adapters: CharacterStatsSkillCasterAliveSource,
    /// ZoneSkillTargetSource and ZoneSkillPositionSource (V-0, V-7, V-8 inputs; TR-sk-004).
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_ValidationAdapters_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private uint _nextClient = 11u;
        private const uint ID_UNKNOWN = 9999u;
        private const int HP_ALIVE = 50;
        private const int HP_MAX = 100;
        private const int HP_DEAD = 0;
        private const short WIRE_X = 250;
        private const short WIRE_Z = -150;
        private const short WIRE_Y_LARGE = 20000;
        private const float EXPECTED_X = 2.5f;
        private const float EXPECTED_Z = -1.5f;
        private const short WIRE_MOB_X = 1000;
        private const short WIRE_MOB_Z = 500;
        private const float EXPECTED_MOB_X = 10f;
        private const float EXPECTED_MOB_Z = 5f;
        private const float TOLERANCE = 0.0001f;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class DictionaryZoneState : IZoneEntityStateSource
        {
            public readonly Dictionary<uint, ZonePlayerStateView> Players = new Dictionary<uint, ZonePlayerStateView>();
            public readonly Dictionary<uint, ZoneMobStateView> Mobs = new Dictionary<uint, ZoneMobStateView>();

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view) => Players.TryGetValue(id.RawValue, out view);

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view) => Mobs.TryGetValue(id.RawValue, out view);
        }

        private ZoneSlotPool _pool;
        private DictionaryZoneState _state;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _state = new DictionaryZoneState();
            _nextClient = 11u;
        }

        private EntityID AddPlayer(int hp, short x = 0, short y = 0, short z = 0, bool withState = true)
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u),
                client, out _, out EntityID id));
            if (withState)
            {
                _state.Players[id.RawValue] = new ZonePlayerStateView(x, y, z, 0, 0, 0, 0, 0, hp, HP_MAX, 0, 0, 1, false, 0, 0, "p", 0u);
            }

            return id;
        }

        private EntityID AddMob(int hp, short x = 0, short y = 0, short z = 0, bool withState = true)
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID id));
            if (withState)
            {
                _state.Mobs[id.RawValue] = new ZoneMobStateView(x, y, z, 0, 0, 0, 0, 0, hp, HP_MAX, false, 0);
            }

            return id;
        }

        // ---- CharacterStatsSkillCasterAliveSource ----

        [Test]
        public void test_validation_alive_source_positive_hp_is_alive()
        {
            var stats = CharacterStatsFixture.Create();
            var entity = new EntityID(CharacterStatsFixture.PlayerEntityId.RawValue);
            stats.SetBaseStat(entity, StatID.MaxHP, HP_MAX);
            stats.SetCurrentHP(entity, HP_ALIVE);

            Assert.That(new CharacterStatsSkillCasterAliveSource(stats).IsAlive(entity), Is.True);
        }

        [Test]
        public void test_validation_alive_source_zero_hp_is_dead()
        {
            var stats = CharacterStatsFixture.Create();
            var entity = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(entity, StatID.MaxHP, HP_MAX);
            stats.SetCurrentHP(entity, HP_DEAD);

            Assert.That(new CharacterStatsSkillCasterAliveSource(stats).IsAlive(entity), Is.False);
        }

        [Test]
        public void test_validation_alive_source_unknown_entity_is_dead_because_hp_reads_zero()
        {
            var stats = CharacterStatsFixture.Create();

            Assert.That(new CharacterStatsSkillCasterAliveSource(stats).IsAlive(new EntityID(ID_UNKNOWN)), Is.False);
        }

        [Test]
        public void test_validation_alive_source_null_stats_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsSkillCasterAliveSource(null));
        }

        // ---- ZoneSkillTargetSource ----

        [Test]
        public void test_validation_target_source_player_with_hp_is_alive_player()
        {
            EntityID player = AddPlayer(HP_ALIVE);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(player, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Player));
            Assert.That(alive, Is.True);
        }

        [Test]
        public void test_validation_target_source_player_with_zero_hp_is_dead_player()
        {
            EntityID player = AddPlayer(HP_DEAD);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(player, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Player));
            Assert.That(alive, Is.False);
        }

        [Test]
        public void test_validation_target_source_player_without_state_is_dead_player()
        {
            EntityID player = AddPlayer(HP_ALIVE, withState: false);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(player, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Player));
            Assert.That(alive, Is.False);
        }

        [Test]
        public void test_validation_target_source_mob_with_hp_is_alive_mob()
        {
            EntityID mob = AddMob(HP_ALIVE);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(mob, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Mob));
            Assert.That(alive, Is.True);
        }

        [Test]
        public void test_validation_target_source_mob_with_zero_hp_is_dead_mob()
        {
            EntityID mob = AddMob(HP_DEAD);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(mob, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Mob));
            Assert.That(alive, Is.False);
        }

        [Test]
        public void test_validation_target_source_mob_without_state_is_dead_mob()
        {
            EntityID mob = AddMob(HP_ALIVE, withState: false);

            bool found = new ZoneSkillTargetSource(_pool, _state).TryGetTarget(mob, out SkillTargetKind kind, out bool alive);

            Assert.That(found, Is.True);
            Assert.That(kind, Is.EqualTo(SkillTargetKind.Mob));
            Assert.That(alive, Is.False);
        }

        [Test]
        public void test_validation_target_source_id_in_neither_pool_is_not_found()
        {
            bool found = new ZoneSkillTargetSource(_pool, _state)
                .TryGetTarget(new EntityID(ID_UNKNOWN), out _, out bool alive);

            Assert.That(found, Is.False);
            Assert.That(alive, Is.False);
        }

        [Test]
        public void test_validation_target_source_null_arguments_throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSkillTargetSource(null, _state));
            Assert.Throws<ArgumentNullException>(() => new ZoneSkillTargetSource(_pool, null));
        }

        // ---- ZoneSkillPositionSource ----

        [Test]
        public void test_validation_position_source_player_wire_position_is_divided_by_one_hundred()
        {
            EntityID player = AddPlayer(HP_ALIVE, WIRE_X, 0, WIRE_Z);

            bool found = new ZoneSkillPositionSource(_state).TryGetPositionXZ(player, out float x, out float z);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(EXPECTED_X).Within(TOLERANCE));
            Assert.That(z, Is.EqualTo(EXPECTED_Z).Within(TOLERANCE), "negative coordinates keep their sign");
        }

        [Test]
        public void test_validation_position_source_mob_wire_position_is_divided_by_one_hundred()
        {
            EntityID mob = AddMob(HP_ALIVE, WIRE_MOB_X, 0, WIRE_MOB_Z);

            bool found = new ZoneSkillPositionSource(_state).TryGetPositionXZ(mob, out float x, out float z);

            Assert.That(found, Is.True);
            Assert.That(x, Is.EqualTo(EXPECTED_MOB_X).Within(TOLERANCE));
            Assert.That(z, Is.EqualTo(EXPECTED_MOB_Z).Within(TOLERANCE));
        }

        [Test]
        public void test_validation_position_source_mob_negative_coordinates_keep_their_sign()
        {
            EntityID mob = AddMob(HP_ALIVE, WIRE_Z, 0, WIRE_X);

            new ZoneSkillPositionSource(_state).TryGetPositionXZ(mob, out float x, out float z);

            Assert.That(x, Is.EqualTo(EXPECTED_Z).Within(TOLERANCE));
            Assert.That(z, Is.EqualTo(EXPECTED_X).Within(TOLERANCE));
        }

        [Test]
        public void test_validation_position_source_ignores_y()
        {
            EntityID flat = AddPlayer(HP_ALIVE, WIRE_X, 0, WIRE_Z);
            EntityID high = AddPlayer(HP_ALIVE, WIRE_X, WIRE_Y_LARGE, WIRE_Z);
            var source = new ZoneSkillPositionSource(_state);

            source.TryGetPositionXZ(flat, out float flatX, out float flatZ);
            source.TryGetPositionXZ(high, out float highX, out float highZ);

            Assert.That(highX, Is.EqualTo(flatX));
            Assert.That(highZ, Is.EqualTo(flatZ));
        }

        [Test]
        public void test_validation_position_source_unknown_entity_is_not_found()
        {
            bool found = new ZoneSkillPositionSource(_state).TryGetPositionXZ(new EntityID(ID_UNKNOWN), out float x, out float z);

            Assert.That(found, Is.False);
            Assert.That(x, Is.EqualTo(0f));
            Assert.That(z, Is.EqualTo(0f));
        }

        [Test]
        public void test_validation_position_source_null_state_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSkillPositionSource(null));
        }
    }
}
