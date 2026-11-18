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
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityDiedHandler = IronGrind.CharacterStats.CharacterStats.EntityDiedHandler;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// No-op seam implementations for fixtures that do not exercise the respawn (Death and Respawn Story 004).
    /// </summary>
    internal static class RespawnStubs
    {
        /// <summary>Returns a fixed non-zero town point.</summary>
        internal sealed class Points : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return new Vector3(1f, 0f, 1f); }
        }

        /// <summary>Does nothing.</summary>
        internal sealed class Vitals : IRespawnVitalsWriter
        {
            public void RestoreToFull(EntityID entity) { }
        }

        /// <summary>Always succeeds.</summary>
        internal sealed class Teleport : IMovementTeleport
        {
            public bool TrySetAuthoritativePosition(EntityID id, Vector3 position) { return true; }
        }

        /// <summary>Does nothing.</summary>
        internal sealed class Status : IStatusEffectsRespawnSink
        {
            public void OnEntityRespawned(EntityID entity) { }
        }
    }

    /// <summary>
    /// EditMode tests for Death and Respawn Story 004: respawn sequence T-2 / T-3 (AC-1 to AC-10; TR-dr-004,
    /// TR-dr-009), plus the production vitals writer against a real CharacterStats.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_Respawn_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const long START_TICK = 1000L;
        private const long BIG_TICK = 4294967296L;
        private const int RETRY_BUDGET = 40;
        private const int DELAY_TICKS = 140;

        private static readonly Vector3 TownPoint = new Vector3(12f, 0f, -7f);

        private enum Kind
        {
            CombatBan, CombatLift, MoveBan, MoveLift, PartyDead, PartyOnline, Vitals, Teleport, Status
        }

        private readonly struct Call
        {
            public readonly Kind K;
            public readonly EntityID Entity;
            public readonly Vector3 Point;

            public Call(Kind k, EntityID entity, Vector3 point = default)
            {
                K = k;
                Entity = entity;
                Point = point;
            }
        }

        private sealed class CallLog
        {
            public readonly List<Call> Calls = new List<Call>(256);

            public int Count(Kind k)
            {
                int n = 0;
                for (int i = 0; i < Calls.Count; i++)
                {
                    if (Calls[i].K == k)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

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

            public void Subscribe(EntityDiedHandler handler) { _handlers.Add(handler); }

            public void Unsubscribe(EntityDiedHandler handler) { _handlers.Remove(handler); }

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

        private sealed class NotGhost : IGhostStatusSource
        {
            public bool IsGhost(EntityID id) { return false; }
        }

        private sealed class NoGhostSink : IGhostDeathSink
        {
            public void OnGhostDied(EntityID entity) { }
        }

        private sealed class LogCombat : ICombatProhibitionSink
        {
            private readonly CallLog _log;
            public LogCombat(CallLog log) { _log = log; }

            public void SetCombatProhibited(EntityID entity, bool prohibited)
            {
                _log.Calls.Add(new Call(prohibited ? Kind.CombatBan : Kind.CombatLift, entity));
            }
        }

        private sealed class LogMovement : IMovementSystem
        {
            private readonly CallLog _log;
            public LogMovement(CallLog log) { _log = log; }

            public bool SetProhibited(EntityID id, bool prohibited)
            {
                _log.Calls.Add(new Call(prohibited ? Kind.MoveBan : Kind.MoveLift, id));
                return true;
            }

            public bool InjectFacingOverride(EntityID id, Vector3 direction) { return true; }
        }

        private sealed class LogParty : IPartyMemberStatusSink
        {
            private readonly CallLog _log;
            public LogParty(CallLog log) { _log = log; }

            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status)
            {
                _log.Calls.Add(new Call(status == PartyMemberDeathStatus.Online ? Kind.PartyOnline : Kind.PartyDead,
                    entity));
            }
        }

        private sealed class LogVitals : IRespawnVitalsWriter
        {
            private readonly CallLog _log;
            public Action<EntityID> OnRestore;
            public Func<EntityID, bool> ThrowWhen;
            public LogVitals(CallLog log) { _log = log; }

            public void RestoreToFull(EntityID entity)
            {
                _log.Calls.Add(new Call(Kind.Vitals, entity));
                OnRestore?.Invoke(entity);
                if (ThrowWhen != null && ThrowWhen(entity))
                {
                    throw new InvalidOperationException("vitals failure");
                }
            }
        }

        private sealed class LogTeleport : IMovementTeleport
        {
            private readonly CallLog _log;
            public bool Result = true;
            public Vector3 LastPoint;
            public LogTeleport(CallLog log) { _log = log; }

            public bool TrySetAuthoritativePosition(EntityID id, Vector3 position)
            {
                _log.Calls.Add(new Call(Kind.Teleport, id, position));
                LastPoint = position;
                return Result;
            }
        }

        private sealed class LogStatus : IStatusEffectsRespawnSink
        {
            private readonly CallLog _log;
            public LogStatus(CallLog log) { _log = log; }

            public void OnEntityRespawned(EntityID entity) { _log.Calls.Add(new Call(Kind.Status, entity)); }
        }

        // Returns Vector3.zero for the first ZeroReplies calls, then Point. Never logs.
        private sealed class ScriptedPoints : ITownRespawnPointProvider
        {
            public int ZeroReplies;
            public readonly Queue<bool> Script = new Queue<bool>(); // true = reply zero; consumed before ZeroReplies
            public Vector3 Point = TownPoint;
            public int Calls;
            public ZoneID LastZone;

            public Vector3 GetTownRespawnPoint(ZoneID zone)
            {
                Calls++;
                LastZone = zone;
                if (Script.Count > 0)
                {
                    return Script.Dequeue() ? Vector3.zero : Point;
                }

                if (ZeroReplies > 0)
                {
                    ZeroReplies--;
                    return Vector3.zero;
                }

                return Point;
            }
        }

        private CallLog _log;
        private ZoneSlotPool _pool;
        private FakeDeathSource _source;
        private FakeDeathTicks _ticks;
        private ScriptedPoints _points;
        private LogVitals _vitals;
        private LogTeleport _teleport;
        private DeathAndRespawnService _service;
        private EntityID _player;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues Config => new DeathAndRespawnConfigValues(
            DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT, RETRY_BUDGET);

        [SetUp]
        public void SetUp()
        {
            _log = new CallLog();
            _pool = new ZoneSlotPool(Zone, new FakeServerTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _player = AllocatePlayer(11u);
            _source = new FakeDeathSource();
            _ticks = new FakeDeathTicks { Value = START_TICK };
            _points = new ScriptedPoints();
            _vitals = new LogVitals(_log);
            _teleport = new LogTeleport(_log);
            _service = Build(new LogStatus(_log));
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private DeathAndRespawnService Build(IStatusEffectsRespawnSink status)
        {
            return new DeathAndRespawnService(Config, _pool, _source, _ticks, new LogCombat(_log),
                new LogMovement(_log), new LogParty(_log), new NotGhost(), new NoGhostSink(), _points, _vitals,
                _teleport, status);
        }

        private EntityID AllocatePlayer(uint raw)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(raw), new AccountID(1000u + raw), raw,
                out _, out EntityID id));
            return id;
        }

        private DeathState StateOf(EntityID id)
        {
            Assert.IsTrue(_service.TryGetDeathState(id, out DeathState state));
            return state;
        }

        private ZoneSessionState SessionState(EntityID id)
        {
            Assert.IsTrue(_pool.TryGetPlayer(id, out ZonePlayerSlot slot));
            return slot.State;
        }

        private void Die()
        {
            _source.Raise(_player);
            _log.Calls.Clear();
        }

        // ---- AC-1 ----

        [Test]
        public void test_respawn_fires_on_respawn_tick_exactly_not_one_before()
        {
            Die();

            _service.Tick(START_TICK + DELAY_TICKS - 1);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            Assert.AreEqual(0, _log.Calls.Count);

            _service.Tick(START_TICK + DELAY_TICKS);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(1, _log.Count(Kind.Vitals));
        }

        [Test]
        public void test_respawn_works_beyond_2_pow_32_ticks()
        {
            _ticks.Value = BIG_TICK;
            Die();

            _service.Tick(BIG_TICK + DELAY_TICKS - 1);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);

            _service.Tick(BIG_TICK + DELAY_TICKS);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
        }

        [Test]
        public void test_respawn_tick_on_alive_player_makes_no_calls()
        {
            _service.Tick(START_TICK);
            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(0, _log.Calls.Count);
            Assert.AreEqual(0, _points.Calls);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
        }

        // ---- AC-2 ----

        [Test]
        public void test_respawn_provider_zero_three_times_then_point_respawns_on_fourth_attempt()
        {
            _points.ZeroReplies = 3;
            Die();
            long t = START_TICK + DELAY_TICKS;

            for (int i = 0; i < 3; i++)
            {
                _service.Tick(t + i);
                Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
                Assert.AreEqual(0, _log.Calls.Count);
            }

            _service.Tick(t + 3);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(TownPoint, _teleport.LastPoint);
            Assert.AreEqual(Zone, _points.LastZone);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_respawn_provider_always_zero_falls_back_on_budget_attempt_with_one_critical_log()
        {
            _points.ZeroReplies = int.MaxValue;
            Die();
            long t = START_TICK + DELAY_TICKS;

            for (int i = 0; i < RETRY_BUDGET - 1; i++)
            {
                _service.Tick(t + i);
            }

            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            Assert.AreEqual(0, _log.Calls.Count);

            LogAssert.Expect(LogType.Error, new Regex("zone " + Regex.Escape(Zone.ToString()) + ".*entity "
                + Regex.Escape(_player.ToString()), RegexOptions.Singleline));
            _service.Tick(t + RETRY_BUDGET - 1);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(Vector3.zero, _teleport.LastPoint);
            Assert.AreEqual(1, _log.Count(Kind.Teleport));
        }

        [Test]
        public void test_respawn_retry_counter_is_reset_after_a_respawn()
        {
            _points.ZeroReplies = 3;
            Die();
            long t = START_TICK + DELAY_TICKS;
            for (int i = 0; i <= 3; i++)
            {
                _service.Tick(t + i);
            }

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            _points.ZeroReplies = int.MaxValue;
            long second = t + 10;
            _ticks.Value = second;
            Die();
            long due = second + DELAY_TICKS;

            for (int i = 0; i < RETRY_BUDGET - 1; i++)
            {
                _service.Tick(due + i);
            }

            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            LogAssert.Expect(LogType.Error, new Regex("critical"));
            _service.Tick(due + RETRY_BUDGET - 1);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
        }

        // ---- AC-3 / AC-4 ----

        [Test]
        public void test_respawn_calls_vitals_once_and_teleport_once_with_resolved_point()
        {
            Die();

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(1, _log.Count(Kind.Vitals));
            Assert.AreEqual(1, _log.Count(Kind.Teleport));
            Assert.AreEqual(_player, _log.Calls[0].Entity);
            Assert.AreEqual(TownPoint, _teleport.LastPoint);
        }

        [Test]
        public void test_respawn_teleport_false_logs_error_and_rest_of_sequence_runs()
        {
            _teleport.Result = false;
            Die();
            LogAssert.Expect(LogType.Error, new Regex("position set for " + Regex.Escape(_player.ToString())));

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(1, _log.Count(Kind.CombatLift));
            Assert.AreEqual(1, _log.Count(Kind.MoveLift));
            Assert.AreEqual(1, _log.Count(Kind.Status));
            Assert.AreEqual(1, _log.Count(Kind.PartyOnline));
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
        }

        // ---- AC-5 / AC-6 / AC-7 ----

        [Test]
        public void test_respawn_lifts_bans_notifies_status_effects_and_party_once_each()
        {
            Die();

            _service.Tick(START_TICK + DELAY_TICKS);

            for (int i = 0; i < _log.Calls.Count; i++)
            {
                Assert.AreEqual(_player, _log.Calls[i].Entity, "entity of call " + i);
            }

            Assert.AreEqual(1, _log.Count(Kind.CombatLift));
            Assert.AreEqual(1, _log.Count(Kind.MoveLift));
            Assert.AreEqual(1, _log.Count(Kind.Status));
            Assert.AreEqual(1, _log.Count(Kind.PartyOnline));
            Assert.AreEqual(0, _log.Count(Kind.CombatBan));
            Assert.AreEqual(0, _log.Count(Kind.PartyDead));
        }

        [Test]
        public void test_respawn_real_status_effects_service_accepts_effects_after_respawn()
        {
            _service.Dispose();
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            stats.SetBaseStat(_player, StatID.AttackPower, 20);
            stats.SetBaseStat(_player, StatID.Defense, 10);
            stats.SetBaseStat(_player, StatID.MaxHP, 400);
            stats.SetBaseStat(_player, StatID.MaxMP, 80);
            var slots = new DictionaryEntitySlotIndex();
            slots.Add(_player, 0);
            var effects = new StatusEffectsService(new CharacterStatsBuffModifierLayer(stats),
                new CharacterStatsResourceSink(stats), slots, _source);
            _service = Build(effects);
            var def = new BuffDefinition((BuffID)1u,
                new[] { new StatModifier(StatID.AttackPower, 10f, 0f) }, 100u, 0f, 0f);
            try
            {
                Die();
                Assert.AreEqual(ApplyResult.InvalidTarget, effects.ApplyEffect(_player, _player, def));

                _service.Tick(START_TICK + DELAY_TICKS);

                Assert.AreEqual(ApplyResult.Success, effects.ApplyEffect(_player, _player, def));
            }
            finally
            {
                effects.Dispose();
            }
        }

        // ---- AC-8 ----

        [Test]
        public void test_respawn_ends_alive_with_zeroed_ticks_and_slot_connected()
        {
            Die();
            Assert.AreEqual(ZoneSessionState.Dead, SessionState(_player));

            _service.Tick(START_TICK + DELAY_TICKS);

            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
            Assert.AreEqual(0L, state.RespawnTick);
            Assert.IsFalse(_service.IsDead(_player));
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(_player));
        }

        [Test]
        public void test_respawn_disconnected_session_active_slot_is_left_unchanged()
        {
            Die();
            Assert.IsTrue(_pool.SetSessionState(_player, ZoneSessionState.Disconnected_SessionActive));

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, SessionState(_player));
        }

        [Test]
        public void test_respawn_death_raised_during_t3_is_dropped_and_player_ends_alive()
        {
            _vitals.OnRestore = e => _source.Raise(e);
            Die();

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(0, _log.Count(Kind.CombatBan));
            Assert.AreEqual(0, _log.Count(Kind.PartyDead));
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(_player));
        }

        [Test]
        public void test_respawn_death_on_next_tick_is_a_fresh_death_with_new_respawn_tick()
        {
            Die();
            long respawnTick = START_TICK + DELAY_TICKS;
            _service.Tick(respawnTick);

            _ticks.Value = respawnTick + 1;
            _source.Raise(_player);

            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
            Assert.AreEqual(respawnTick + 1, state.DeathTick);
            Assert.AreEqual(respawnTick + 1 + DELAY_TICKS, state.RespawnTick);
            Assert.AreEqual(1, _log.Count(Kind.CombatBan));
        }

        // ---- AC-9 ----

        [Test]
        public void test_respawn_side_effects_run_in_gdd_order()
        {
            Die();

            _service.Tick(START_TICK + DELAY_TICKS);

            var expected = new[]
            {
                Kind.Vitals, Kind.Teleport, Kind.CombatLift, Kind.MoveLift, Kind.Status, Kind.PartyOnline
            };
            Assert.AreEqual(expected.Length, _log.Calls.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], _log.Calls[i].K, "call " + i);
                Assert.AreEqual(_player, _log.Calls[i].Entity, "entity of call " + i);
            }

            Assert.AreEqual(TownPoint, _log.Calls[1].Point);
        }

        // ---- AC-10 ----

        [Test]
        public void test_respawn_tick_allocates_zero_bytes_after_warm_up()
        {
            long t = START_TICK;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                _ticks.Value = t;
                _source.Raise(_player);
                _service.Tick(t + DELAY_TICKS);
                t += DELAY_TICKS + 5;
            }

            _log.Calls.Clear();
            _ticks.Value = t;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _source.Raise(_player);
            _service.Tick(t + DELAY_TICKS - 1);
            _service.Tick(t + DELAY_TICKS);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(1, _log.Count(Kind.Vitals));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Exception safety, multiple players, teardown ----

        private static Regex StepFailure(string step, EntityID id)
        {
            return new Regex("respawn step " + step + " for " + Regex.Escape(id.ToString()) + " threw");
        }

        [Test]
        public void test_respawn_throwing_vitals_is_logged_and_rest_of_sequence_still_runs()
        {
            _vitals.ThrowWhen = e => true;
            Die();
            LogAssert.Expect(LogType.Error, StepFailure("vitals", _player));

            Assert.DoesNotThrow(() => _service.Tick(START_TICK + DELAY_TICKS));

            Assert.AreEqual(1, _log.Count(Kind.Teleport));
            Assert.AreEqual(1, _log.Count(Kind.CombatLift));
            Assert.AreEqual(1, _log.Count(Kind.MoveLift));
            Assert.AreEqual(1, _log.Count(Kind.Status));
            Assert.AreEqual(1, _log.Count(Kind.PartyOnline));
            DeathState state = StateOf(_player);
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.RespawnTick);
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(_player));
        }

        [Test]
        public void test_respawn_throwing_seam_for_one_player_does_not_stop_another_player()
        {
            EntityID other = AllocatePlayer(12u);
            _vitals.ThrowWhen = e => e.Equals(_player);
            _source.Raise(_player);
            _source.Raise(other);
            _log.Calls.Clear();
            LogAssert.Expect(LogType.Error, StepFailure("vitals", _player));

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(other).CurrentState);
            Assert.AreEqual(2, _log.Count(Kind.PartyOnline));
            Assert.AreEqual(2, _log.Count(Kind.CombatLift));
            Assert.AreEqual(ZoneSessionState.Connected, SessionState(other));
        }

        [Test]
        public void test_respawn_two_players_dying_the_same_tick_both_respawn_in_one_tick()
        {
            EntityID other = AllocatePlayer(12u);
            _source.Raise(_player);
            _source.Raise(other);
            _log.Calls.Clear();

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(other).CurrentState);
            Assert.AreEqual(2, _log.Count(Kind.Vitals));
            Assert.AreEqual(2, _log.Count(Kind.Teleport));
            Assert.AreEqual(2, _log.Count(Kind.PartyOnline));
            var seen = new HashSet<EntityID>();
            for (int i = 0; i < _log.Calls.Count; i++)
            {
                if (_log.Calls[i].K == Kind.Status)
                {
                    seen.Add(_log.Calls[i].Entity);
                }
            }

            CollectionAssert.AreEquivalent(new[] { _player, other }, seen);
        }

        [Test]
        public void test_respawn_retrying_player_stays_dead_with_own_counter_while_other_respawns()
        {
            EntityID other = AllocatePlayer(12u);
            _source.Raise(_player);
            _source.Raise(other);
            _log.Calls.Clear();
            _points.Script.Enqueue(true);   // first player in slot order: unresolved
            _points.Script.Enqueue(false);  // second player: resolved
            long t = START_TICK + DELAY_TICKS;

            _service.Tick(t);

            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(other).CurrentState);
            Assert.AreEqual(1, _log.Count(Kind.Vitals));
            Assert.AreEqual(other, _log.Calls[0].Entity);

            // The retrying player has used 1 attempt; 38 more still leave it Dead; the 40th attempt falls back.
            _points.ZeroReplies = int.MaxValue;
            for (int i = 1; i < RETRY_BUDGET - 1; i++)
            {
                _service.Tick(t + i);
            }

            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
            LogAssert.Expect(LogType.Error, new Regex("critical"));
            _service.Tick(t + RETRY_BUDGET - 1);
            Assert.AreEqual(DeathStateKind.Alive, StateOf(_player).CurrentState);
            Assert.AreEqual(2, _log.Count(Kind.Vitals));
        }

        [Test]
        public void test_respawn_tick_after_dispose_makes_no_calls_and_player_stays_dead()
        {
            Die();
            _service.Dispose();

            _service.Tick(START_TICK + DELAY_TICKS);

            Assert.AreEqual(0, _log.Calls.Count);
            Assert.AreEqual(0, _points.Calls);
            Assert.AreEqual(DeathStateKind.Dead, StateOf(_player).CurrentState);
        }

        [Test]
        public void test_respawn_slot_released_while_dead_tick_does_not_throw_and_calls_nothing()
        {
            Die();
            Assert.IsTrue(_pool.ReleasePlayer(_player));

            Assert.DoesNotThrow(() => _service.Tick(START_TICK + DELAY_TICKS));

            Assert.AreEqual(0, _log.Calls.Count);
            Assert.AreEqual(0, _points.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Construction ----

        [Test]
        public void test_respawn_constructor_null_new_arguments_throw()
        {
            var combat = new LogCombat(_log);
            var movement = new LogMovement(_log);
            var party = new LogParty(_log);
            var status = new LogStatus(_log);
            DeathAndRespawnConfigValues c = Config;

            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, combat,
                movement, party, new NotGhost(), new NoGhostSink(), null, _vitals, _teleport, status));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, combat,
                movement, party, new NotGhost(), new NoGhostSink(), _points, null, _teleport, status));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, combat,
                movement, party, new NotGhost(), new NoGhostSink(), _points, _vitals, null, status));
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(c, _pool, _source, _ticks, combat,
                movement, party, new NotGhost(), new NoGhostSink(), _points, _vitals, _teleport, null));
        }

        // ---- Production vitals writer ----

        [Test]
        public void test_respawn_vitals_writer_restores_hp_and_mp_to_effective_max()
        {
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            EntityID id = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(id, StatID.MaxHP, 250);
            stats.SetBaseStat(id, StatID.MaxMP, 80);
            stats.SetCurrentHP(id, 0f);
            stats.SetCurrentMP(id, 13f);
            var writer = new CharacterStatsRespawnVitalsWriter(stats);

            writer.RestoreToFull(id);

            Assert.AreEqual(250f, stats.GetCurrentHP(id), 0.001f);
            Assert.AreEqual(80f, stats.GetCurrentMP(id), 0.001f);
        }

        [Test]
        public void test_respawn_vitals_writer_uses_effective_max_including_modifiers()
        {
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            EntityID id = CharacterStatsFixture.PlayerEntityId;
            stats.SetBaseStat(id, StatID.MaxHP, 250);
            stats.SetBaseStat(id, StatID.MaxMP, 80);
            stats.AddBuffModifier(id, StatID.MaxHP, new BuffModifierEntry(50f, 0f, 10, (BuffID)1u));
            stats.AddBuffModifier(id, StatID.MaxMP, new BuffModifierEntry(20f, 0f, 10, (BuffID)2u));
            stats.SetCurrentHP(id, 1f);
            stats.SetCurrentMP(id, 1f);
            var writer = new CharacterStatsRespawnVitalsWriter(stats);

            writer.RestoreToFull(id);

            Assert.AreEqual(300f, stats.GetCurrentHP(id), 0.001f);
            Assert.AreEqual(100f, stats.GetCurrentMP(id), 0.001f);
        }

        [Test]
        public void test_respawn_vitals_writer_null_stats_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsRespawnVitalsWriter(null));
        }
    }
}
