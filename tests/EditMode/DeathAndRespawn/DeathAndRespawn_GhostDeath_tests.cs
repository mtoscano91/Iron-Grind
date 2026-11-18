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
    /// EditMode tests for Death and Respawn Story 003: ghost death branch (AC-1 to AC-6; CR-DR-Ghost, TR-dr-003).
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_GhostDeath_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const long START_TICK = 1000L;
        private const long LATER_TICK = 1005L;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;

        private sealed class FakeServerTicks : IServerTickSource
        {
            public uint ServerTickNumber => 0u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeDeathSource : IEntityDeathSource
        {
            private readonly List<EntityDiedHandler> _handlers = new List<EntityDiedHandler>();

            public void Subscribe(EntityDiedHandler handler)
            {
                _handlers.Add(handler);
            }

            public void Unsubscribe(EntityDiedHandler handler)
            {
                _handlers.Remove(handler);
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
            public int Calls;

            public void SetCombatProhibited(EntityID entity, bool prohibited)
            {
                Calls++;
            }
        }

        private sealed class FakeMovement : IMovementSystem
        {
            public int Calls;

            public bool SetProhibited(EntityID id, bool prohibited)
            {
                Calls++;
                return true;
            }

            public bool InjectFacingOverride(EntityID id, Vector3 direction)
            {
                return true;
            }
        }

        private sealed class FakeParty : IPartyMemberStatusSink
        {
            public int Calls;

            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status)
            {
                Calls++;
            }
        }

        private sealed class RecordingGhostSink : IGhostDeathSink
        {
            public readonly List<EntityID> Calls = new List<EntityID>();
            public bool Throw;

            public void OnGhostDied(EntityID entity)
            {
                Calls.Add(entity);
                if (Throw)
                {
                    throw new InvalidOperationException("sink failure");
                }
            }
        }

        private ZoneSlotPool _pool;
        private FakeDeathSource _source;
        private FakeDeathTicks _ticks;
        private FakeCombatSink _combat;
        private FakeMovement _movement;
        private FakeParty _party;
        private RecordingGhostSink _sink;
        private ZoneSlotGhostStatusSource _ghosts;
        private DeathAndRespawnService _service;
        private EntityID _player;
        private EntityID _mob;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues Config => new DeathAndRespawnConfigValues(
            DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT,
            DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT);

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeServerTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _player = AllocatePlayer(11u);
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out _mob));

            _source = new FakeDeathSource();
            _ticks = new FakeDeathTicks { Value = START_TICK };
            _combat = new FakeCombatSink();
            _movement = new FakeMovement();
            _party = new FakeParty();
            _sink = new RecordingGhostSink();
            _ghosts = new ZoneSlotGhostStatusSource(_pool);
            _service = new DeathAndRespawnService(Config, _pool, _source, _ticks, _combat, _movement, _party,
                _ghosts, _sink, new RespawnStubs.Points(), new RespawnStubs.Vitals(), new RespawnStubs.Teleport(),
                new RespawnStubs.Status(), new RespawnStubs.Output());
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

        private void MakeGhost(EntityID id)
        {
            Assert.IsTrue(_pool.SetSessionState(id, ZoneSessionState.Disconnected_SessionActive));
        }

        private void AssertNoSideEffectCalls()
        {
            Assert.AreEqual(0, _combat.Calls);
            Assert.AreEqual(0, _movement.Calls);
            Assert.AreEqual(0, _party.Calls);
        }

        private static Regex HandOffError(EntityID id)
        {
            return new Regex(Regex.Escape("ghost death hand-off for " + id + " threw"));
        }

        // ---- AC-1 ----

        // Story 007 AC-7 reversed the order: duplicate suppression runs before the ghost check.
        [Test]
        public void test_ghost_death_dedup_runs_before_ghost_check()
        {
            // Arrange
            _service.SetStateForTests(_player, DeathStateKind.Dead);
            MakeGhost(_player);

            // Act
            _source.Raise(_player);

            // Assert
            Assert.AreEqual(0, _sink.Calls.Count);
            AssertNoSideEffectCalls();
            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
            Assert.AreEqual(0L, state.RespawnTick);
        }

        // ---- AC-2 ----

        [Test]
        public void test_ghost_death_calls_sink_once_and_leaves_state_alive_and_slot_unchanged()
        {
            MakeGhost(_player);

            _source.Raise(_player);

            Assert.AreEqual(1, _sink.Calls.Count);
            Assert.AreEqual(_player, _sink.Calls[0]);
            Assert.IsFalse(_service.IsDead(_player));
            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
            Assert.AreEqual(0L, state.RespawnTick);
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, SessionState(_player));
        }

        // ---- AC-3 ----

        [Test]
        public void test_ghost_death_makes_no_combat_movement_or_party_calls()
        {
            MakeGhost(_player);

            _source.Raise(_player);

            // DeathStateEntered / EntityDied are not observable here; Story 005 asserts them against the outbox.
            AssertNoSideEffectCalls();
        }

        // ---- AC-4 ----

        [Test]
        public void test_ghost_death_connected_player_follows_normal_death_and_sink_not_called()
        {
            _source.Raise(_player);

            Assert.AreEqual(0, _sink.Calls.Count);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(_player));
            Assert.AreEqual(1, _combat.Calls);
            Assert.AreEqual(1, _movement.Calls);
            Assert.AreEqual(1, _party.Calls);
        }

        [Test]
        public void test_ghost_death_mob_death_does_not_call_sink()
        {
            _source.Raise(_mob);

            Assert.AreEqual(0, _sink.Calls.Count);
            AssertNoSideEffectCalls();
        }

        [Test]
        public void test_ghost_death_ghost_and_connected_same_tick_only_ghost_reaches_sink()
        {
            EntityID connected = AllocatePlayer(12u);
            MakeGhost(_player);

            _source.Raise(_player);
            _source.Raise(connected);

            CollectionAssert.AreEqual(new[] { _player }, _sink.Calls);
            Assert.IsFalse(_service.IsDead(_player));
            Assert.AreEqual(DeathStateKind.Dead, StateOf(connected).CurrentState);
            Assert.AreEqual(START_TICK, StateOf(connected).DeathTick);
            Assert.AreEqual(1, _combat.Calls);
            Assert.AreEqual(1, _movement.Calls);
            Assert.AreEqual(1, _party.Calls);
        }

        // ---- Adapter ----

        [Test]
        public void test_ghost_death_adapter_connected_player_is_not_ghost()
        {
            Assert.IsFalse(_ghosts.IsGhost(_player));
        }

        [Test]
        public void test_ghost_death_adapter_disconnected_session_active_player_is_ghost()
        {
            MakeGhost(_player);

            Assert.IsTrue(_ghosts.IsGhost(_player));
        }

        [Test]
        public void test_ghost_death_adapter_dead_or_respawning_slot_is_not_ghost()
        {
            Assert.IsTrue(_pool.SetSessionState(_player, ZoneSessionState.Dead));
            Assert.IsFalse(_ghosts.IsGhost(_player));

            Assert.IsTrue(_pool.SetSessionState(_player, ZoneSessionState.Respawning));
            Assert.IsFalse(_ghosts.IsGhost(_player));
        }

        [Test]
        public void test_ghost_death_adapter_unknown_id_is_not_ghost()
        {
            Assert.IsFalse(_ghosts.IsGhost(new EntityID(UNKNOWN_ENTITY_RAW)));
        }

        [Test]
        public void test_ghost_death_adapter_mob_id_is_not_ghost()
        {
            Assert.IsFalse(_ghosts.IsGhost(_mob));
        }

        [Test]
        public void test_ghost_death_adapter_null_pool_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSlotGhostStatusSource(null));
        }

        // ---- AC-5 ----

        [Test]
        public void test_ghost_death_sink_throwing_is_logged_and_does_not_escape()
        {
            MakeGhost(_player);
            _sink.Throw = true;
            LogAssert.Expect(LogType.Error, HandOffError(_player));

            Assert.DoesNotThrow(() => _source.Raise(_player));

            Assert.AreEqual(1, _sink.Calls.Count);
            Assert.IsFalse(_service.IsDead(_player));
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            AssertNoSideEffectCalls();
        }

        [Test]
        public void test_ghost_death_sink_throwing_then_other_player_dies_normally()
        {
            EntityID connected = AllocatePlayer(12u);
            MakeGhost(_player);
            _sink.Throw = true;
            LogAssert.Expect(LogType.Error, HandOffError(_player));
            _source.Raise(_player);

            _source.Raise(connected);

            Assert.AreEqual(DeathStateKind.Dead, StateOf(connected).CurrentState);
            Assert.AreEqual(1, _combat.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-6 (revised by Story 007 AC-7: duplicate suppression runs before the ghost check) ----

        [Test]
        public void test_ghost_death_dead_then_disconnected_second_death_is_dropped()
        {
            // Arrange
            _source.Raise(_player);
            DeathState before = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Dead, before.CurrentState);
            Assert.IsTrue(_pool.SetSessionState(_player, ZoneSessionState.Disconnected_SessionActive));
            Assert.IsTrue(_ghosts.IsGhost(_player));
            _ticks.Value = LATER_TICK;

            // Act
            _source.Raise(_player);

            // Assert
            Assert.AreEqual(0, _sink.Calls.Count);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            DeathState after = StateOf(_player);
            Assert.AreEqual(before.DeathTick, after.DeathTick);
            Assert.AreEqual(before.RespawnTick, after.RespawnTick);
            Assert.AreEqual(1, _combat.Calls);
            Assert.AreEqual(1, _movement.Calls);
            Assert.AreEqual(1, _party.Calls);
        }

        // ---- Disposed ----

        [Test]
        public void test_ghost_death_after_dispose_does_not_call_sink()
        {
            MakeGhost(_player);
            _service.Dispose();

            _source.Raise(_player);

            Assert.AreEqual(0, _sink.Calls.Count);
        }
    }
}
