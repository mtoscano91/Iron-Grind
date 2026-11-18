using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityDiedHandler = IronGrind.CharacterStats.CharacterStats.EntityDiedHandler;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 007: disconnect, reconnect, release and repeat death while dead
    /// (EC-DR-1, EC-DR-3, TR-dr-007). The service runs with the real pool, ghost status adapter, vitals writer, message
    /// emitter and outbox. The snapshot, zone close and RespawnTick consumers are covered in their own fixtures
    /// (<c>ZoneInstancing_SnapshotAssembly_tests</c>, <c>ZoneInstancing_Teardown_tests</c>,
    /// <c>ZoneEntityStateSource_tests</c>); AC-5 is in <c>CharacterPersistence_SaveSession_tests</c>.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_DisconnectTeardown_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint CLIENT_P = 11u;
        private const uint CLIENT_Q = 12u;
        private const uint CLIENT_NEW = 13u;
        private const uint RECONNECT_CLIENT = 99u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const long DEATH_TICK = 1000L;
        private const long DISCONNECT_TICK = 1010L;
        private const long RECONNECT_TICK = 1050L;
        private const int PLAYER_MAX_HP = 500;
        private const int PLAYER_MAX_MP = 200;

        private static readonly Vector3 TownPoint = new Vector3(12f, 0f, -7f);

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

        private sealed class RecordingGhostSink : IGhostDeathSink
        {
            public readonly List<EntityID> Calls = new List<EntityID>();

            public void OnGhostDied(EntityID entity) { Calls.Add(entity); }
        }

        private sealed class NoCombat : ICombatProhibitionSink
        {
            public void SetCombatProhibited(EntityID entity, bool prohibited) { }
        }

        private sealed class NoMovement : IMovementSystem
        {
            public bool SetProhibited(EntityID id, bool prohibited) { return true; }

            public bool InjectFacingOverride(EntityID id, Vector3 direction) { return true; }
        }

        private sealed class RecordingParty : IPartyMemberStatusSink
        {
            public readonly Dictionary<EntityID, PartyMemberDeathStatus> Last =
                new Dictionary<EntityID, PartyMemberDeathStatus>();

            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status) { Last[entity] = status; }
        }

        private sealed class RecordingTeleport : IMovementTeleport
        {
            public readonly Dictionary<EntityID, Vector3> Last = new Dictionary<EntityID, Vector3>();

            public bool TrySetAuthoritativePosition(EntityID id, Vector3 position)
            {
                Last[id] = position;
                return true;
            }
        }

        // Real vitals writer plus a hook that runs first, inside T-3 while the state is Respawning.
        private sealed class ProbeVitals : IRespawnVitalsWriter
        {
            private readonly IRespawnVitalsWriter _inner;
            public Action<EntityID> DuringRespawn;

            public ProbeVitals(IRespawnVitalsWriter inner) { _inner = inner; }

            public void RestoreToFull(EntityID entity)
            {
                DuringRespawn?.Invoke(entity);
                _inner.RestoreToFull(entity);
            }
        }

        // Replies Vector3.zero (unresolved) while ZeroReplies > 0, then the town point.
        private sealed class ScriptedPoints : ITownRespawnPointProvider
        {
            public int ZeroReplies;

            public Vector3 GetTownRespawnPoint(ZoneID zone)
            {
                if (ZeroReplies > 0)
                {
                    ZeroReplies--;
                    return Vector3.zero;
                }

                return TownPoint;
            }
        }

        private sealed class FixedPositions : IZonePlayerPositionReader
        {
            public bool TryGetPosition(EntityID id, out Vector3 position)
            {
                position = Vector3.zero;
                return true;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<uint> Clients = new List<uint>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Clients.Add(clientId);
            }

            public int CountFor(uint client)
            {
                int n = 0;
                for (int i = 0; i < Clients.Count; i++)
                {
                    if (Clients[i] == client)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private ZoneSlotPool _pool;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private FakeDeathSource _source;
        private FakeDeathTicks _ticks;
        private ZoneSlotGhostStatusSource _ghosts;
        private RecordingGhostSink _ghostSink;
        private RecordingParty _party;
        private RecordingTeleport _teleport;
        private ProbeVitals _vitals;
        private ScriptedPoints _points;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private DeathAndRespawnService _service;
        private EntityID _p;
        private EntityID _q;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues Config => DeathAndRespawnConfigValues.Defaults;

        private static int DelayTicks => Config.RespawnDelayTicks;

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeServerTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _stats = CharacterStatsFixture.Create();
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _p = AddPlayer(CLIENT_P);
            _q = AddPlayer(CLIENT_Q);
            _source = new FakeDeathSource();
            _ticks = new FakeDeathTicks { Value = DEATH_TICK };
            _ghosts = new ZoneSlotGhostStatusSource(_pool);
            _ghostSink = new RecordingGhostSink();
            _party = new RecordingParty();
            _teleport = new RecordingTeleport();
            _points = new ScriptedPoints();
            _vitals = new ProbeVitals(new CharacterStatsRespawnVitalsWriter(_stats));
            _service = new DeathAndRespawnService(Config, _pool, _source, _ticks, new NoCombat(), new NoMovement(),
                _party, _ghosts, _ghostSink, _points, _vitals, _teleport,
                new RespawnStubs.Status(), new DeathMessageEmitter(_pool, _outbox));
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET),
                client, out _, out EntityID id));
            _outbox.AddClient(client);
            _stats.SetBaseStat(id, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetBaseStat(id, StatID.MaxMP, PLAYER_MAX_MP);
            _stats.SetCurrentHP(id, PLAYER_MAX_HP);
            _stats.SetCurrentMP(id, PLAYER_MAX_MP);
            return id;
        }

        private ZoneSessionState SlotState(EntityID id)
        {
            Assert.IsTrue(_pool.TryGetPlayer(id, out ZonePlayerSlot slot));
            return slot.State;
        }

        // P's HP drops to 0 and the death is raised at DEATH_TICK.
        private void KillP()
        {
            _stats.SetCurrentHP(_p, 0f);
            _stats.SetCurrentMP(_p, 0f);
            _ticks.Value = DEATH_TICK;
            _source.Raise(_p);
        }

        // Transport disconnect: the slot becomes a ghost and the client leaves the outbox.
        private void DisconnectP()
        {
            Assert.IsTrue(_pool.SetSessionState(_p, ZoneSessionState.Disconnected_SessionActive));
            _outbox.RemoveClient(CLIENT_P);
        }

        private void TickTo(long tick)
        {
            _ticks.Value = tick;
            _service.Tick(tick);
        }

        // ---- AC-1 DeathState is the source of dead-ness ----

        [Test]
        public void test_disconnect_teardown_death_does_not_write_slot_state()
        {
            // Act
            KillP();

            // Assert
            Assert.IsTrue(_service.IsDead(_p));
            Assert.AreEqual(ZoneSessionState.Connected, SlotState(_p));
        }

        [Test]
        public void test_disconnect_teardown_dead_then_disconnected_is_dead_and_ghost()
        {
            // Arrange
            KillP();

            // Act
            DisconnectP();

            // Assert
            Assert.IsTrue(_service.IsDead(_p));
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, SlotState(_p));
            Assert.IsTrue(_ghosts.IsGhost(_p));
            Assert.IsTrue(_service.TryGetRespawnTick(_p, out long respawnTick));
            Assert.AreEqual(DEATH_TICK + DelayTicks, respawnTick);
        }

        [Test]
        public void test_disconnect_teardown_reader_alive_or_unknown_has_no_respawn_tick()
        {
            // Arrange: Q is alive; EntityID.Invalid holds no slot.

            // Act
            bool aliveDead = _service.IsDead(_q);
            bool aliveFound = _service.TryGetRespawnTick(_q, out long aliveTick);
            bool unknownDead = _service.IsDead(EntityID.Invalid);
            bool unknownFound = _service.TryGetRespawnTick(EntityID.Invalid, out long unknownTick);

            // Assert
            Assert.IsFalse(aliveDead);
            Assert.IsFalse(aliveFound);
            Assert.AreEqual(0L, aliveTick);
            Assert.IsFalse(unknownDead);
            Assert.IsFalse(unknownFound);
            Assert.AreEqual(0L, unknownTick);
        }

        [Test]
        public void test_disconnect_teardown_reader_reports_dead_while_respawning()
        {
            // Arrange: probe the reader from inside T-3, where the state is Respawning.
            KillP();
            bool? deadDuringRespawn = null;
            bool? tickFoundDuringRespawn = null;
            _vitals.DuringRespawn = id =>
            {
                deadDuringRespawn = _service.IsDead(id);
                tickFoundDuringRespawn = _service.TryGetRespawnTick(id, out _);
            };

            // Act
            TickTo(DEATH_TICK + DelayTicks);

            // Assert
            Assert.AreEqual(true, deadDuringRespawn);
            Assert.AreEqual(true, tickFoundDuringRespawn);
            Assert.IsFalse(_service.IsDead(_p), "Alive once T-3 finished");
        }

        [Test]
        public void test_disconnect_teardown_aggro_source_skips_dead_connected_player_and_keeps_alive_one()
        {
            // Arrange
            KillP();
            var candidates = new ZoneAggroCandidateSource(Zone, _pool, new FixedPositions(), _service);
            var into = new List<(EntityID Id, Vector3 Position)>();

            // Act
            candidates.CopyPlayers(into);

            // Assert
            Assert.AreEqual(ZoneSessionState.Connected, SlotState(_p));
            Assert.AreEqual(1, into.Count);
            Assert.AreEqual(_q, into[0].Id);
            Assert.IsFalse(candidates.TryGetPlayerPosition(_p, out _));
            Assert.IsTrue(candidates.TryGetPlayerPosition(_q, out _));
        }

        [Test]
        public void test_disconnect_teardown_aggro_source_without_reader_offers_connected_player()
        {
            // Arrange
            KillP();

            // Act
            var candidates = new ZoneAggroCandidateSource(Zone, _pool, new FixedPositions());

            // Assert

            Assert.IsTrue(candidates.TryGetPlayerPosition(_p, out _));
        }

        // ---- AC-2 timer runs while disconnected (EC-DR-1) ----

        [Test]
        public void test_disconnect_teardown_respawn_runs_while_disconnected_and_slot_stays_disconnected()
        {
            // Arrange: die 1000, disconnect 1010, no reconnect.
            KillP();
            _ticks.Value = DISCONNECT_TICK;
            DisconnectP();
            _outbox.Flush(1u);
            _seam.Clients.Clear();
            TickTo(DEATH_TICK + DelayTicks - 1);
            Assert.IsTrue(_service.IsDead(_p));

            // Act
            TickTo(DEATH_TICK + DelayTicks);
            _outbox.Flush(2u);

            // Assert
            Assert.IsFalse(_service.IsDead(_p));
            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_p));
            Assert.AreEqual(PLAYER_MAX_MP, _stats.GetCurrentMP(_p));
            Assert.AreEqual(TownPoint, _teleport.Last[_p]);
            Assert.AreEqual(PartyMemberDeathStatus.Online, _party.Last[_p]);
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, SlotState(_p));
            Assert.AreEqual(0, _seam.CountFor(CLIENT_P), "nothing is sent to a disconnected client");
            Assert.Greater(_seam.CountFor(CLIENT_Q), 0, "control: the zone-wide respawn message still reaches Q");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-3 reconnect (EC-DR-1; snapshot fields covered in ZoneInstancing_SnapshotAssembly_tests) ----

        [Test]
        public void test_disconnect_teardown_reconnect_while_dead_keeps_respawn_tick_and_rebinds_connected()
        {
            // Arrange: die 1000, disconnect 1010.
            KillP();
            _ticks.Value = DISCONNECT_TICK;
            DisconnectP();
            TickTo(RECONNECT_TICK);

            // Act
            Assert.IsTrue(_pool.RebindClient(_p, RECONNECT_CLIENT));

            // Assert
            Assert.AreEqual(ZoneSessionState.Connected, SlotState(_p));
            Assert.IsTrue(_service.TryGetRespawnTick(_p, out long respawnTick));
            Assert.AreEqual(90L, respawnTick - RECONNECT_TICK);
        }

        [Test]
        public void test_disconnect_teardown_reconnect_after_respawn_is_alive_with_full_hp()
        {
            // Arrange
            KillP();
            _ticks.Value = DISCONNECT_TICK;
            DisconnectP();
            TickTo(DEATH_TICK + DelayTicks);

            // Act
            Assert.IsTrue(_pool.RebindClient(_p, RECONNECT_CLIENT));

            // Assert
            Assert.IsFalse(_service.IsDead(_p));
            Assert.IsFalse(_service.TryGetRespawnTick(_p, out long respawnTick));
            Assert.AreEqual(0L, respawnTick);
            Assert.AreEqual(PLAYER_MAX_HP, _stats.GetCurrentHP(_p));
            Assert.AreEqual(ZoneSessionState.Connected, SlotState(_p));
        }

        // ---- AC-6 release clears state ----

        [Test]
        public void test_disconnect_teardown_release_resets_state_and_new_occupant_is_alive_and_never_respawned()
        {
            // Arrange: P dies and burns two retry attempts on an unresolved town point.
            _points.ZeroReplies = 2;
            KillP();
            TickTo(DEATH_TICK + DelayTicks);
            TickTo(DEATH_TICK + DelayTicks + 1);
            Assert.IsTrue(_service.IsDead(_p));
            Assert.IsTrue(_pool.TryGetPlayerSlot(_p, out byte oldSlot));
            _pool.SetReleaseListener(_service);

            // Act
            Assert.IsTrue(_pool.ReleasePlayer(_p));
            EntityID newcomer = AddPlayer(CLIENT_NEW);

            // Assert
            Assert.IsTrue(_pool.TryGetPlayerSlot(newcomer, out byte newSlot));
            Assert.AreEqual(oldSlot, newSlot, "the newcomer reuses the released slot");
            Assert.IsFalse(_service.IsDead(newcomer));
            Assert.IsTrue(_service.TryGetDeathState(newcomer, out DeathState state));
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.DeathTick);
            Assert.AreEqual(0L, state.RespawnTick);
            TickTo(DEATH_TICK + DelayTicks + 2);
            Assert.IsFalse(_teleport.Last.ContainsKey(newcomer), "the newcomer is never respawned");
        }

        [Test]
        public void test_disconnect_teardown_release_resets_retry_counter()
        {
            // Arrange: P burns RespawnAnchorRetryTicks - 1 attempts, is released; the newcomer dies and needs a full
            // fresh budget before the critical origin fallback.
            _points.ZeroReplies = int.MaxValue;
            KillP();
            int retries = Config.RespawnAnchorRetryTicks;
            for (int i = 0; i < retries - 1; i++)
            {
                TickTo(DEATH_TICK + DelayTicks + i);
            }

            _pool.SetReleaseListener(_service);
            Assert.IsTrue(_pool.ReleasePlayer(_p));
            EntityID newcomer = AddPlayer(CLIENT_NEW);
            long death2 = DEATH_TICK + DelayTicks + retries;
            _ticks.Value = death2;
            _source.Raise(newcomer);

            // Act: one tick at its respawn tick — with a stale counter this would hit the fallback at once.
            TickTo(death2 + DelayTicks);

            // Assert
            Assert.IsTrue(_service.IsDead(newcomer));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_disconnect_teardown_before_release_unknown_id_changes_nothing()
        {
            // Arrange
            KillP();

            // Act
            _service.BeforeRelease(new EntityID(9999u));

            // Assert
            Assert.IsTrue(_service.IsDead(_p));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-7 second death while Dead is dropped (Story 003 AC-6 revised) ----

        [Test]
        public void test_disconnect_teardown_second_death_while_dead_and_disconnected_is_dropped()
        {
            // Arrange
            KillP();
            DeathState before;
            Assert.IsTrue(_service.TryGetDeathState(_p, out before));
            _ticks.Value = DISCONNECT_TICK;
            DisconnectP();
            _outbox.Flush(1u);
            _seam.Clients.Clear();

            // Act
            _source.Raise(_p);
            _outbox.Flush(2u);

            // Assert
            Assert.AreEqual(0, _ghostSink.Calls.Count);
            Assert.IsTrue(_service.TryGetDeathState(_p, out DeathState after));
            Assert.AreEqual(DeathStateKind.Dead, after.CurrentState);
            Assert.AreEqual(before.DeathTick, after.DeathTick);
            Assert.AreEqual(before.RespawnTick, after.RespawnTick);
            Assert.AreEqual(0, _seam.Clients.Count, "no second EntityDied");
        }

        [Test]
        public void test_disconnect_teardown_death_while_respawning_and_disconnected_is_dropped()
        {
            // Arrange: P dies, disconnects (a ghost), and a death is raised from inside T-3 (Respawning).
            KillP();
            _ticks.Value = DISCONNECT_TICK;
            DisconnectP();
            Assert.IsTrue(_ghosts.IsGhost(_p));
            _vitals.DuringRespawn = id => _source.Raise(id);

            // Act
            TickTo(DEATH_TICK + DelayTicks);

            // Assert
            Assert.AreEqual(0, _ghostSink.Calls.Count);
            Assert.IsTrue(_service.TryGetDeathState(_p, out DeathState state));
            Assert.AreEqual(DeathStateKind.Alive, state.CurrentState);
            Assert.AreEqual(0L, state.RespawnTick);
        }

        [Test]
        public void test_disconnect_teardown_alive_ghost_death_still_calls_sink_once()
        {
            // Arrange
            DisconnectP();

            // Act
            _source.Raise(_p);

            // Assert
            Assert.AreEqual(1, _ghostSink.Calls.Count);
            Assert.AreEqual(_p, _ghostSink.Calls[0]);
            Assert.IsFalse(_service.IsDead(_p));
        }
    }
}
