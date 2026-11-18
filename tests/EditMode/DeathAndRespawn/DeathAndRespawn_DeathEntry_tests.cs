using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityDiedHandler = IronGrind.CharacterStats.CharacterStats.EntityDiedHandler;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 002: death entry, T-1 (AC-1 to AC-9; CR-DR-1 to CR-DR-3,
    /// CR-DR-5, CR-DR-6, EC-DR-6).
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_DeathEntry_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const long START_TICK = 1000L;
        private const long LATER_TICK = 1005L;
        private const long WIDE_TICK = 4294967296L;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;

        private sealed class FakeServerTicks : IServerTickSource
        {
            public uint Value;

            public uint ServerTickNumber => Value;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        /// <summary>Records the exact delegate instances; removes by reference so a fresh delegate would not match.</summary>
        private sealed class FakeDeathSource : IEntityDeathSource
        {
            private readonly List<EntityDiedHandler> _handlers = new List<EntityDiedHandler>();

            /// <summary>When true, Unsubscribe is recorded but the handler stays registered (reaches the disposed guard).</summary>
            public bool IgnoreUnsubscribe;

            public int SubscribeCalls;
            public int UnsubscribeCalls;
            public EntityDiedHandler LastSubscribed;
            public EntityDiedHandler LastUnsubscribed;

            public int HandlerCount => _handlers.Count;

            public void Subscribe(EntityDiedHandler handler)
            {
                SubscribeCalls++;
                LastSubscribed = handler;
                _handlers.Add(handler);
            }

            public void Unsubscribe(EntityDiedHandler handler)
            {
                UnsubscribeCalls++;
                LastUnsubscribed = handler;
                if (IgnoreUnsubscribe)
                {
                    return;
                }

                for (int i = 0; i < _handlers.Count; i++)
                {
                    if (ReferenceEquals(_handlers[i], handler))
                    {
                        _handlers.RemoveAt(i);
                        return;
                    }
                }
            }

            public void Raise(EntityID entity)
            {
                for (int i = 0; i < _handlers.Count; i++)
                {
                    _handlers[i](entity);
                }
            }
        }

        private sealed class FakeDeathTicks : IDeathTickSource
        {
            public long Value;

            public long CurrentTick => Value;
        }

        private sealed class FakeCombatSink : ICombatProhibitionSink
        {
            public readonly List<(EntityID Entity, bool Prohibited)> Calls = new List<(EntityID, bool)>();

            public void SetCombatProhibited(EntityID entity, bool prohibited)
            {
                Calls.Add((entity, prohibited));
            }
        }

        private sealed class FakeMovement : IMovementSystem
        {
            public readonly List<(EntityID Entity, bool Prohibited)> Calls = new List<(EntityID, bool)>();

            public bool SetProhibited(EntityID id, bool prohibited)
            {
                Calls.Add((id, prohibited));
                return true;
            }

            public bool InjectFacingOverride(EntityID id, Vector3 direction)
            {
                return true;
            }
        }

        private sealed class FakeParty : IPartyMemberStatusSink
        {
            public readonly List<(EntityID Entity, PartyMemberDeathStatus Status)> Calls =
                new List<(EntityID, PartyMemberDeathStatus)>();

            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status)
            {
                Calls.Add((entity, status));
            }
        }

        private sealed class FakeGhostStatus : IGhostStatusSource
        {
            public bool IsGhost(EntityID id) { return false; }
        }

        private sealed class FakeGhostSink : IGhostDeathSink
        {
            public int Calls;

            public void OnGhostDied(EntityID entity) { Calls++; }
        }

        private FakeGhostStatus _notGhost;
        private FakeGhostSink _ghostSink;
        private ZoneSlotPool _pool;
        private FakeServerTicks _serverTicks;
        private FakeDeathSource _source;
        private FakeDeathTicks _ticks;
        private FakeCombatSink _combat;
        private FakeMovement _movement;
        private FakeParty _party;
        private DeathAndRespawnService _service;
        private EntityID _player;
        private EntityID _mob;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues Config => new DeathAndRespawnConfigValues(
            DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT,
            DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT);

        private static long DelayTicks => Config.RespawnDelayTicks;

        [SetUp]
        public void SetUp()
        {
            _serverTicks = new FakeServerTicks();
            _pool = new ZoneSlotPool(Zone, _serverTicks, new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _player = AllocatePlayer(11u);
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out _mob));

            _source = new FakeDeathSource();
            _ticks = new FakeDeathTicks { Value = START_TICK };
            _combat = new FakeCombatSink();
            _movement = new FakeMovement();
            _party = new FakeParty();
            _notGhost = new FakeGhostStatus();
            _ghostSink = new FakeGhostSink();
            _service = new DeathAndRespawnService(Config, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink,
                new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output());
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private EntityID AllocatePlayer(uint raw)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(raw), new AccountID(1000u + raw), raw,
                out _, out EntityID id));
            return id;
        }

        private ZoneSessionState SessionState(EntityID id)
        {
            Assert.IsTrue(_pool.TryGetPlayer(id, out ZonePlayerSlot slot));
            return slot.State;
        }

        private DeathState StateOf(EntityID id)
        {
            Assert.IsTrue(_service.TryGetDeathState(id, out DeathState state));
            return state;
        }

        private void AssertUntouchedAlive(EntityID id)
        {
            DeathState state = StateOf(id);
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
            Assert.AreEqual(0L, state.RespawnTick);
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(id));
            Assert.IsFalse(_service.IsDead(id));
        }

        private void AssertNoSinkCalls()
        {
            Assert.AreEqual(0, _combat.Calls.Count);
            Assert.AreEqual(0, _movement.Calls.Count);
            Assert.AreEqual(0, _party.Calls.Count);
        }

        // ---- Construction ----

        [Test]
        public void test_death_entry_constructor_subscribes_once()
        {
            Assert.AreEqual(1, _source.SubscribeCalls);
            Assert.AreEqual(1, _source.HandlerCount);
        }

        [Test]
        public void test_death_entry_constructor_null_arguments_throw()
        {
            DeathAndRespawnConfigValues c = Config;

            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, null, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, null, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, null, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, null, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, null, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, null, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, null, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, null, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, null, new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), null, new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), null, new RespawnStubs.Status(), new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), null, new RespawnStubs.Output()));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), null));
        }

        [Test]
        public void test_death_entry_constructor_invalid_config_throws_without_subscribing()
        {
            var source = new FakeDeathSource();
            LogAssert.Expect(LogType.Error, new Regex("RESPAWN_DELAY_SECONDS"));
            LogAssert.Expect(LogType.Error, new Regex("RESPAWN_ANCHOR_RETRY_TICKS"));

            Assert.Throws<ArgumentException>(() => new DeathAndRespawnService(default(DeathAndRespawnConfigValues),
                _pool, source, _ticks, _combat, _movement, _party, _notGhost, _ghostSink,
                new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(), new RespawnStubs.Status(), new RespawnStubs.Output()));

            Assert.AreEqual(0, source.SubscribeCalls);
        }

        // ---- AC-1 ----

        [Test]
        public void test_death_entry_alive_player_becomes_dead_with_ticks_and_slot_state()
        {
            // Arrange: the default 7.0 s is 140 ticks at 20 Hz (QA case values).
            Assert.AreEqual(140, Config.RespawnDelayTicks);

            // Act
            _source.Raise(_player);

            // Assert
            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
            Assert.AreEqual(START_TICK, state.DeathTick);
            Assert.AreEqual(1140L, state.RespawnTick);
            Assert.AreEqual(ZoneSessionState.Dead, SessionState(_player));
        }

        [Test]
        public void test_death_entry_default_state_is_alive_with_zero_ticks()
        {
            AssertUntouchedAlive(_player);
        }

        [Test]
        public void test_death_entry_second_player_death_does_not_touch_first_player()
        {
            EntityID second = AllocatePlayer(12u);

            _source.Raise(second);

            AssertUntouchedAlive(_player);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(second).CurrentState);
            Assert.AreEqual(second, _combat.Calls[0].Entity);
            Assert.AreEqual(second, _movement.Calls[0].Entity);
            Assert.AreEqual(second, _party.Calls[0].Entity);

            _ticks.Value = LATER_TICK;
            _source.Raise(_player);

            DeathState first = StateOf(_player);
            Assert.AreEqual(LATER_TICK, first.DeathTick);
            Assert.AreEqual(LATER_TICK + DelayTicks, first.RespawnTick);
            DeathState other = StateOf(second);
            Assert.AreEqual(START_TICK, other.DeathTick);
            Assert.AreEqual(START_TICK + DelayTicks, other.RespawnTick);
        }

        [Test]
        public void test_death_entry_two_players_same_tick_each_get_their_own_state()
        {
            EntityID second = AllocatePlayer(12u);

            _source.Raise(_player);
            _source.Raise(second);

            foreach (EntityID id in new[] { _player, second })
            {
                DeathState state = StateOf(id);
                Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
                Assert.AreEqual(START_TICK, state.DeathTick);
                Assert.AreEqual(START_TICK + DelayTicks, state.RespawnTick);
                Assert.AreEqual(ZoneSessionState.Dead, SessionState(id));
            }

            Assert.AreEqual(2, _combat.Calls.Count);
            Assert.AreEqual(2, _movement.Calls.Count);
            Assert.AreEqual(2, _party.Calls.Count);
        }

        // ---- AC-2 ----

        [Test]
        public void test_death_entry_mob_death_changes_nothing()
        {
            _source.Raise(_mob);

            AssertNoSinkCalls();
            AssertUntouchedAlive(_player);
            Assert.IsFalse(_service.TryGetDeathState(_mob, out _));
            Assert.IsFalse(_service.IsDead(_mob));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_death_entry_unknown_entity_changes_nothing()
        {
            _source.Raise(new EntityID(UNKNOWN_ENTITY_RAW));

            AssertNoSinkCalls();
            AssertUntouchedAlive(_player);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-3 ----

        [Test]
        public void test_death_entry_duplicate_in_dead_is_suppressed()
        {
            _source.Raise(_player);
            _ticks.Value = LATER_TICK;

            _source.Raise(_player);

            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
            Assert.AreEqual(START_TICK, state.DeathTick);
            Assert.AreEqual(START_TICK + DelayTicks, state.RespawnTick);
            Assert.AreEqual(1, _combat.Calls.Count);
            Assert.AreEqual(1, _movement.Calls.Count);
            Assert.AreEqual(1, _party.Calls.Count);
        }

        [Test]
        public void test_death_entry_duplicate_in_respawning_is_suppressed()
        {
            _source.Raise(_player);
            _service.SetStateForTests(_player, DeathStateKind.Respawning);
            _ticks.Value = LATER_TICK;

            _source.Raise(_player);

            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Respawning, state.CurrentState);
            Assert.AreEqual(START_TICK, state.DeathTick);
            Assert.AreEqual(START_TICK + DelayTicks, state.RespawnTick);
            Assert.AreEqual(1, _combat.Calls.Count);
            Assert.AreEqual(1, _movement.Calls.Count);
            Assert.AreEqual(1, _party.Calls.Count);
            Assert.IsTrue(_service.IsDead(_player));
        }

        // ---- AC-4 ----

        [Test]
        public void test_death_entry_bans_combat_and_movement_once()
        {
            _source.Raise(_player);

            Assert.AreEqual(1, _combat.Calls.Count);
            Assert.AreEqual(_player, _combat.Calls[0].Entity);
            Assert.IsTrue(_combat.Calls[0].Prohibited);
            Assert.AreEqual(1, _movement.Calls.Count);
            Assert.AreEqual(_player, _movement.Calls[0].Entity);
            Assert.IsTrue(_movement.Calls[0].Prohibited);
        }

        // ---- AC-5 ----

        [Test]
        public void test_death_entry_reports_dead_to_party_once()
        {
            _source.Raise(_player);

            Assert.AreEqual(1, _party.Calls.Count);
            Assert.AreEqual(_player, _party.Calls[0].Entity);
            Assert.AreEqual(PartyMemberDeathStatus.Dead, _party.Calls[0].Status);
        }

        [Test]
        public void test_death_entry_noop_party_sink_does_not_throw()
        {
            var sink = new NoOpPartyMemberStatusSink();

            Assert.DoesNotThrow(() => sink.SetMemberStatus(_player, PartyMemberDeathStatus.Dead));
        }

        [Test]
        public void test_death_entry_party_status_default_is_online()
        {
            Assert.AreEqual(PartyMemberDeathStatus.Online, default(PartyMemberDeathStatus));
        }

        // ---- AC-6 ----

        [Test]
        public void test_death_entry_tick_beyond_uint_range_does_not_overflow()
        {
            _ticks.Value = WIDE_TICK;

            _source.Raise(_player);

            DeathState state = StateOf(_player);
            Assert.AreEqual(4294967296L, state.DeathTick);
            Assert.AreEqual(4294967436L, state.RespawnTick);
        }

        // ---- AC-7 ----

        [Test]
        public void test_death_entry_server_tick_adapter_widens_uint_max_value()
        {
            _serverTicks.Value = uint.MaxValue;
            var adapter = new ServerTickDeathTickSource(_serverTicks);

            Assert.AreEqual(4294967295L, adapter.CurrentTick);
        }

        [Test]
        public void test_death_entry_server_tick_adapter_null_source_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ServerTickDeathTickSource(null));
        }

        // ---- AC-8 ----

        [Test]
        public void test_death_entry_dispose_unsubscribes_same_handler_and_later_death_changes_nothing()
        {
            _service.Dispose();

            Assert.AreEqual(1, _source.UnsubscribeCalls);
            Assert.AreSame(_source.LastSubscribed, _source.LastUnsubscribed);
            Assert.AreEqual(0, _source.HandlerCount);

            _source.Raise(_player);

            AssertUntouchedAlive(_player);
            AssertNoSinkCalls();
        }

        [Test]
        public void test_death_entry_handler_called_after_dispose_changes_nothing()
        {
            // Arrange: the source keeps the handler, so a stale call reaches the disposed guard.
            _source.IgnoreUnsubscribe = true;
            _service.Dispose();
            Assert.AreEqual(1, _source.HandlerCount);

            // Act
            _source.Raise(_player);

            // Assert
            AssertUntouchedAlive(_player);
            AssertNoSinkCalls();
        }

        [Test]
        public void test_death_entry_dispose_twice_does_not_throw_and_unsubscribes_once()
        {
            _service.Dispose();

            Assert.DoesNotThrow(() => _service.Dispose());
            Assert.AreEqual(1, _source.UnsubscribeCalls);
        }

        // ---- AC-9 ----

        [Test]
        public void test_death_entry_query_reports_dead_player_and_rejects_non_player()
        {
            _source.Raise(_player);

            Assert.IsTrue(_service.IsDead(_player));
            Assert.IsFalse(_service.IsDead(_mob));
            Assert.IsFalse(_service.IsDead(new EntityID(UNKNOWN_ENTITY_RAW)));
            Assert.IsFalse(_service.TryGetDeathState(_mob, out DeathState state));
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
        }
    }
}
