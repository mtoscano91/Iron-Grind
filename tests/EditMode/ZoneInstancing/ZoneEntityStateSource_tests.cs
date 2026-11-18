#nullable enable
#pragma warning disable CS0067 // the fake equipment service never raises its event
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EquipmentSystem;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.DeathAndRespawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode tests for Zone Instancing Story 015 (TD-100): <see cref="ZoneEntityStateSource"/> over a real
    /// <see cref="ZoneSlotPool"/> and <see cref="ZoneMobRoster"/> with fake stats, name, equipment and combat views,
    /// and <see cref="ForwardingZoneEntityStateSource"/>.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneEntityStateSource_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const string NAME = "Brakka";
        private const byte FLAGS = 5;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public readonly Dictionary<EntityID, float> Hp = new Dictionary<EntityID, float>();
            public readonly Dictionary<EntityID, float> Mp = new Dictionary<EntityID, float>();
            public readonly Dictionary<(EntityID, StatID), int> Stats = new Dictionary<(EntityID, StatID), int>();

            public float GetCurrentHP(EntityID entityId) => Hp.TryGetValue(entityId, out float v) ? v : 0f;

            public float GetCurrentMP(EntityID entityId) => Mp.TryGetValue(entityId, out float v) ? v : 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => Stats.TryGetValue((entityId, statId), out int v) ? v : 0;
        }

        private sealed class FakeNames : ICharacterNameLookup
        {
            public readonly Dictionary<CharacterID, string> Names = new Dictionary<CharacterID, string>();

            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                if (Names.TryGetValue(id, out string? found))
                {
                    name = found;
                    return true;
                }

                name = string.Empty;
                return false;
            }
        }

        private sealed class FakeCombat : IZoneEntityCombatReader
        {
            public readonly Dictionary<EntityID, AutoAttackState> States = new Dictionary<EntityID, AutoAttackState>();
            public int ActionTimer;
            public int CycleTicks;

            public AutoAttackState GetState(EntityID entityId) => States.TryGetValue(entityId, out AutoAttackState s) ? s : AutoAttackState.Idle;

            public int GetActionTimer(EntityID entityId) => ActionTimer;

            public int GetCycleTicks(EntityID entityId) => CycleTicks;
        }

        private sealed class FakeEquipment : IEquipmentService
        {
            public byte Flags = FLAGS;

            public event Action<EquipmentAppearanceChangedEventArgs>? OnAppearanceChanged;

            public byte GetAppearanceFlags(CharacterID charId) => Flags;

            public void RegisterCharacter(CharacterID charId, EntityID entityId) => throw new NotSupportedException();

            public void UnregisterCharacter(CharacterID charId) => throw new NotSupportedException();

            public EquipmentSlotState GetEquipmentSlotState(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public byte GetSlotEnhancementLevel(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public bool IsSlotTransitioning(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public ItemID GetEquippedWeaponItemID(CharacterID charId) => throw new NotSupportedException();

            public byte GetEquippedWeaponEnhancementLevel(CharacterID charId) => throw new NotSupportedException();

            public EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex) => throw new NotSupportedException();

            public EquipResult Unequip(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public MergeResult RequestMerge(CharacterID charId, int slotIdx1, int slotIdx2, int slotIdx3) => throw new NotSupportedException();

            public GearRestoreResult RestoreSlots(CharacterID charId, ReadOnlySpan<SavedGearSlot> slots) => throw new NotSupportedException();

            public SavedGearSlot[] ExportSlots(CharacterID charId) => throw new NotSupportedException();
        }

        private sealed class FalseSource : IZoneEntityStateSource
        {
            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = default;
                return false;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return false;
            }
        }

        private sealed class StubSource : IZoneEntityStateSource
        {
            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, false, 0, 0, "x", 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return true;
            }
        }

        private ZoneSlotPool _pool = null!;
        private ZoneMobRoster _roster = null!;
        private FakeStats _stats = null!;
        private FakeNames _names = null!;
        private FakeCombat _combat = null!;
        private FakeEquipment _equipment = null!;
        private ZoneEntityStateSource _source = null!;
        private uint _nextCharacter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            var ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, ticks, new FakeListener());
            _roster = new ZoneMobRoster(_pool, ticks);
            _stats = new FakeStats();
            _names = new FakeNames();
            _combat = new FakeCombat();
            _equipment = new FakeEquipment();
            _source = new ZoneEntityStateSource(_pool, _roster, _stats, _names, _equipment, _combat);
            _nextCharacter = 1000u;
        }

        private EntityID AddPlayer(string? name = NAME)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, character, new AccountID(n + 5000u), n + 9000u, out _, out EntityID id));
            if (name != null)
            {
                _names.Names[character] = name;
            }

            return id;
        }

        private EntityID AddMob()
        {
            EntityID id = _roster.TryAddMob(Zone, new MobTypeID(5u), Vector3.zero);
            Assert.AreNotEqual(EntityID.Invalid, id);
            return id;
        }

        // ----- construction ---------------------------------------------------------------------

        [Test]
        public void test_constructor_null_dependency_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(null!, _roster, _stats, _names, _equipment, _combat));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(_pool, null!, _stats, _names, _equipment, _combat));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(_pool, _roster, null!, _names, _equipment, _combat));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(_pool, _roster, _stats, null!, _equipment, _combat));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(_pool, _roster, _stats, _names, null!, _combat));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntityStateSource(_pool, _roster, _stats, _names, _equipment, null!));
        }

        // ----- AC-1 players ---------------------------------------------------------------------

        [Test]
        public void test_player_state_carries_name_level_hp_mp_and_appearance_flags()
        {
            EntityID id = AddPlayer();
            _stats.Hp[id] = 80f;
            _stats.Mp[id] = 30f;
            _stats.Stats[(id, StatID.MaxHP)] = 100;
            _stats.Stats[(id, StatID.MaxMP)] = 40;
            _stats.Stats[(id, StatID.Level)] = 7;

            bool found = _source.TryGetPlayerState(id, out ZonePlayerStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual(NAME, view.CharacterName);
            Assert.AreEqual(7, view.Level);
            Assert.AreEqual(80, view.CurrentHp);
            Assert.AreEqual(100, view.MaxHp);
            Assert.AreEqual(30, view.CurrentMp);
            Assert.AreEqual(40, view.MaxMp);
            Assert.AreEqual(FLAGS, view.EquipmentAppearanceFlags);
            Assert.AreEqual((short)0, view.PosX);
            Assert.AreEqual((short)0, view.FacingAngle);
        }

        [Test]
        public void test_player_state_through_movement_decorator_keeps_name_and_level()
        {
            EntityID id = AddPlayer();
            _stats.Stats[(id, StatID.Level)] = 7;
            var decorated = new MovementZoneEntityStateSource(_source);

            bool found = decorated.TryGetPlayerState(id, out ZonePlayerStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual(NAME, view.CharacterName);
            Assert.AreEqual(7, view.Level);
        }

        // ----- AC-2 nameless player -------------------------------------------------------------

        [Test]
        public void test_player_without_a_name_returns_false()
        {
            EntityID id = AddPlayer(name: null);

            bool found = _source.TryGetPlayerState(id, out ZonePlayerStateView view);

            Assert.IsFalse(found);
            Assert.IsNull(view.CharacterName);
        }

        [Test]
        public void test_player_with_an_empty_name_returns_false()
        {
            EntityID id = AddPlayer(name: string.Empty);

            Assert.IsFalse(_source.TryGetPlayerState(id, out _));
        }

        [Test]
        public void test_player_whose_lookup_returns_a_null_name_returns_false()
        {
            EntityID id = AddPlayer(name: null);
            Assert.IsTrue(_pool.TryGetPlayer(id, out ZonePlayerSlot slot));
            _names.Names[slot.Character] = null!;

            Assert.IsFalse(_source.TryGetPlayerState(id, out _));
        }

        [Test]
        public void test_released_player_and_removed_mob_return_false()
        {
            // Arrange
            EntityID player = AddPlayer();
            EntityID mob = AddMob();
            Assert.IsTrue(_pool.ReleasePlayer(player));
            _roster.RemoveMob(Zone, mob);

            // Act / Assert
            Assert.IsFalse(_source.TryGetPlayerState(player, out _));
            Assert.IsFalse(_source.TryGetMobState(mob, out _));
        }

        // ----- AC-3 isInCombat ------------------------------------------------------------------
        // The mob cases drive a fake combat reader: the real AutoAttackCombatSystem resolves player slots only, so in
        // production a mob always reads Idle (TD-102). These cases pin the rule, not production mob behaviour.

        [TestCase(AutoAttackState.Idle, false)]
        [TestCase(AutoAttackState.CombatActive, true)]
        [TestCase(AutoAttackState.OutOfRange, true)]
        [TestCase(AutoAttackState.Prohibited, false)]
        public void test_player_is_in_combat_follows_auto_attack_state(AutoAttackState state, bool expected)
        {
            EntityID id = AddPlayer();
            _combat.States[id] = state;

            Assert.IsTrue(_source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual(expected, view.IsInCombat);
        }

        [TestCase(AutoAttackState.Idle, false)]
        [TestCase(AutoAttackState.CombatActive, true)]
        [TestCase(AutoAttackState.OutOfRange, true)]
        [TestCase(AutoAttackState.Prohibited, false)]
        public void test_mob_is_in_combat_follows_auto_attack_state(AutoAttackState state, bool expected)
        {
            EntityID id = AddMob();
            _combat.States[id] = state;

            Assert.IsTrue(_source.TryGetMobState(id, out ZoneMobStateView view));

            Assert.AreEqual(expected, view.IsInCombat);
        }

        // ----- AC-4 rounding, cycle timer, respawn ----------------------------------------------

        [Test]
        public void test_player_current_hp_is_rounded_down()
        {
            EntityID id = AddPlayer();
            _stats.Hp[id] = 79.6f;
            _stats.Mp[id] = 12.9f;

            Assert.IsTrue(_source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual(79, view.CurrentHp);
            Assert.AreEqual(12, view.CurrentMp);
        }

        [Test]
        public void test_player_cycle_timer_equals_normalized_action_timer_and_respawn_tick_is_zero()
        {
            EntityID id = AddPlayer();
            _combat.ActionTimer = 5;
            _combat.CycleTicks = 20;

            Assert.IsTrue(_source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual(CycleTimerBroadcastStep.NormalizeCycleTimer(5, 20), view.CycleTimer);
            Assert.AreNotEqual((ushort)0, view.CycleTimer);
            Assert.AreEqual(0u, view.RespawnTick);
        }

        // ----- Death and Respawn Story 007 AC-3: RespawnTick from the death reader ------------

        private ZoneEntityStateSource SourceWith(FakePlayerDeathStateReader deaths) =>
            new ZoneEntityStateSource(_pool, _roster, _stats, _names, _equipment, _combat, null, deaths);

        [Test]
        public void test_player_respawn_tick_dead_player_is_read_from_death_reader()
        {
            // Arrange
            EntityID id = AddPlayer();
            var deaths = new FakePlayerDeathStateReader();
            deaths.SetDead(id, 1140L);

            // Act
            Assert.IsTrue(SourceWith(deaths).TryGetPlayerState(id, out ZonePlayerStateView view));

            // Assert
            Assert.AreEqual(1140u, view.RespawnTick);
        }

        [Test]
        public void test_player_respawn_tick_alive_player_with_reader_is_zero()
        {
            // Arrange
            EntityID id = AddPlayer();
            ZoneEntityStateSource source = SourceWith(new FakePlayerDeathStateReader());

            // Act
            Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView view));

            // Assert
            Assert.AreEqual(0u, view.RespawnTick);
        }

        [Test]
        public void test_player_respawn_tick_without_reader_is_zero_even_if_dead_elsewhere()
        {
            // Arrange: the default source (no reader) cannot see a death recorded in an unconnected reader.
            EntityID id = AddPlayer();
            new FakePlayerDeathStateReader().SetDead(id, 1140L);

            // Act
            Assert.IsTrue(_source.TryGetPlayerState(id, out ZonePlayerStateView view));

            // Assert
            Assert.AreEqual(0u, view.RespawnTick);
        }

        [TestCase(0L)]
        [TestCase(-5L)]
        public void test_player_respawn_tick_not_positive_is_zero_and_silent(long respawnTick)
        {
            // Arrange
            EntityID id = AddPlayer();
            var deaths = new FakePlayerDeathStateReader();
            deaths.SetDead(id, respawnTick);

            // Act
            Assert.IsTrue(SourceWith(deaths).TryGetPlayerState(id, out ZonePlayerStateView view));

            // Assert
            Assert.AreEqual(0u, view.RespawnTick);
            LogAssert.NoUnexpectedReceived();
        }

        // Real DeathAndRespawnService as the reader: the death it records reaches the view, and the respawn clears it.
        [Test]
        public void test_player_respawn_tick_real_death_service_dead_then_respawned()
        {
            // Arrange
            EntityID id = AddPlayer();
            var deaths = new RealDeathRig(_pool);
            var source = new ZoneEntityStateSource(_pool, _roster, _stats, _names, _equipment, _combat,
                null, deaths.Service);
            long respawnAt = RealDeathRig.DEATH_TICK + DeathAndRespawnConfigValues.Defaults.RespawnDelayTicks;

            try
            {
                // Act + Assert: dead
                deaths.Kill(id);
                Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView dead));
                Assert.AreEqual((uint)respawnAt, dead.RespawnTick);

                // Act + Assert: respawned
                deaths.Service.Tick(respawnAt);
                Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView alive));
                Assert.AreEqual(0u, alive.RespawnTick);
            }
            finally
            {
                deaths.Service.Dispose();
            }
        }

        [Test]
        public void test_player_respawn_tick_above_uint_max_is_clamped_and_logs_one_error()
        {
            // Arrange
            EntityID id = AddPlayer();
            var deaths = new FakePlayerDeathStateReader();
            deaths.SetDead(id, (long)uint.MaxValue + 1L);
            LogAssert.Expect(LogType.Error, new Regex(@"RespawnTick 4294967296 .* clamped to 4294967295"));

            // Act
            Assert.IsTrue(SourceWith(deaths).TryGetPlayerState(id, out ZonePlayerStateView view));

            // Assert
            Assert.AreEqual(uint.MaxValue, view.RespawnTick);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_player_respawn_tick_exactly_uint_max_is_not_clamped()
        {
            EntityID id = AddPlayer();
            var deaths = new FakePlayerDeathStateReader();
            deaths.SetDead(id, uint.MaxValue);

            Assert.IsTrue(SourceWith(deaths).TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual(uint.MaxValue, view.RespawnTick);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0, 0)]
        [TestCase(25, 20)]
        public void test_player_cycle_timer_at_edge_inputs_equals_the_normalizer(int actionTimer, int cycleTicks)
        {
            EntityID id = AddPlayer();
            _combat.ActionTimer = actionTimer;
            _combat.CycleTicks = cycleTicks;

            Assert.IsTrue(_source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual(CycleTimerBroadcastStep.NormalizeCycleTimer(actionTimer, cycleTicks), view.CycleTimer);
        }

        // Fake combat reader (see the AC-3 note): production mobs read CycleTimer 0 today.
        [Test]
        public void test_mob_cycle_timer_equals_normalized_action_timer()
        {
            EntityID id = AddMob();
            _combat.ActionTimer = 10;
            _combat.CycleTicks = 40;

            Assert.IsTrue(_source.TryGetMobState(id, out ZoneMobStateView view));

            Assert.AreEqual(CycleTimerBroadcastStep.NormalizeCycleTimer(10, 40), view.CycleTimer);
        }

        // ----- AC-5 mobs ------------------------------------------------------------------------

        [Test]
        public void test_mob_state_carries_current_and_max_hp_rounded_down()
        {
            EntityID id = AddMob();
            _stats.Hp[id] = 45.9f;
            _stats.Stats[(id, StatID.MaxHP)] = 60;

            bool found = _source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual(45, view.CurrentHp);
            Assert.AreEqual(60, view.MaxHp);
        }

        [Test]
        public void test_unknown_entity_returns_false_from_both_methods()
        {
            var unknown = new EntityID(9999u);

            Assert.IsFalse(_source.TryGetPlayerState(unknown, out _));
            Assert.IsFalse(_source.TryGetMobState(unknown, out _));
        }

        [Test]
        public void test_mob_entity_is_not_a_player_and_player_entity_is_not_a_mob()
        {
            EntityID player = AddPlayer();
            EntityID mob = AddMob();

            Assert.IsFalse(_source.TryGetPlayerState(mob, out _));
            Assert.IsFalse(_source.TryGetMobState(player, out _));
        }

        // ----- AC-7 allocation ------------------------------------------------------------------

        [Test]
        public void test_player_and_mob_state_reads_allocate_nothing()
        {
            EntityID player = AddPlayer();
            EntityID mob = AddMob();
            _source.TryGetPlayerState(player, out _);
            _source.TryGetMobState(mob, out _);

            Assert.That(() => { _source.TryGetPlayerState(player, out _); }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { _source.TryGetMobState(mob, out _); }, Is.Not.AllocatingGCMemory());
        }

        // ----- forwarding source ----------------------------------------------------------------

        [Test]
        public void test_forwarding_source_returns_false_until_bound()
        {
            var forwarder = new ForwardingZoneEntityStateSource();

            Assert.IsFalse(forwarder.TryGetPlayerState(new EntityID(1u), out _));
            Assert.IsFalse(forwarder.TryGetMobState(new EntityID(1u), out _));
        }

        [Test]
        public void test_forwarding_source_forwards_after_bind()
        {
            var forwarder = new ForwardingZoneEntityStateSource();
            Assert.IsFalse(forwarder.IsBound);
            forwarder.Bind(new StubSource());

            Assert.IsTrue(forwarder.IsBound);
            Assert.IsTrue(forwarder.TryGetPlayerState(new EntityID(1u), out ZonePlayerStateView view));
            Assert.AreEqual("x", view.CharacterName);
            Assert.IsTrue(forwarder.TryGetMobState(new EntityID(1u), out _));
        }

        [Test]
        public void test_forwarding_source_passes_a_false_target_result_through()
        {
            var forwarder = new ForwardingZoneEntityStateSource();
            forwarder.Bind(new FalseSource());

            Assert.IsFalse(forwarder.TryGetPlayerState(new EntityID(1u), out _));
            Assert.IsFalse(forwarder.TryGetMobState(new EntityID(1u), out _));
        }

        [Test]
        public void test_forwarding_source_second_bind_throws_invalid_operation()
        {
            var forwarder = new ForwardingZoneEntityStateSource();
            forwarder.Bind(new StubSource());

            Assert.Throws<InvalidOperationException>(() => forwarder.Bind(new StubSource()));
        }

        [Test]
        public void test_forwarding_source_bind_null_throws_argument_null()
        {
            var forwarder = new ForwardingZoneEntityStateSource();

            Assert.Throws<ArgumentNullException>(() => forwarder.Bind(null!));
        }
    }
}
