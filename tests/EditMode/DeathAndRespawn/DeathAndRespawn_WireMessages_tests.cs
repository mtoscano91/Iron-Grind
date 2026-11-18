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
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 005, service side: <see cref="DeathAndRespawnService"/> calls
    /// <see cref="IDeathMessageOutput"/> at T-1 and T-3 (AC-2 to AC-5), with a recording fake.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_WireMessages_Service_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const long START_TICK = 1000L;
        private const int DELAY_TICKS = 140;
        private const int RETRY_BUDGET = 40;
        private const uint UNKNOWN_ENTITY_RAW = 999999u;

        private static readonly Vector3 TownPoint = new Vector3(12f, 0f, -7f);

        private readonly struct DeathCall
        {
            public readonly EntityID Entity;
            public readonly byte Seconds;
            public readonly long Tick;
            public readonly DeathStateKind Kind;
            public readonly ZoneSessionState Slot;
            public readonly bool CombatBanned;
            public readonly bool MovementBanned;

            public DeathCall(EntityID entity, byte seconds, long tick, DeathStateKind kind, ZoneSessionState slot,
                bool combatBanned, bool movementBanned)
            {
                Entity = entity;
                Seconds = seconds;
                Tick = tick;
                Kind = kind;
                Slot = slot;
                CombatBanned = combatBanned;
                MovementBanned = movementBanned;
            }
        }

        private sealed class RecordingOutput : IDeathMessageOutput
        {
            public readonly List<DeathCall> Deaths = new List<DeathCall>();
            public readonly List<(EntityID Entity, Vector3 Position, long Tick, ZoneSessionState State)> Respawns =
                new List<(EntityID, Vector3, long, ZoneSessionState)>();
            public Func<long> TickNow;
            public Func<EntityID, ZoneSessionState> StateOf;
            public Func<EntityID, DeathStateKind> KindOf;
            public Func<EntityID, bool> CombatBanned;
            public Func<EntityID, bool> MovementBanned;

            public void SendDeath(EntityID entity, byte respawnTimerSeconds)
            {
                Deaths.Add(new DeathCall(entity, respawnTimerSeconds, TickNow(), KindOf(entity), StateOf(entity),
                    CombatBanned(entity), MovementBanned(entity)));
            }

            public void SendRespawn(EntityID entity, Vector3 position)
            {
                Respawns.Add((entity, position, TickNow(), StateOf(entity)));
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

        private sealed class FakeGhosts : IGhostStatusSource
        {
            public readonly HashSet<EntityID> Ghosts = new HashSet<EntityID>();

            public bool IsGhost(EntityID id) { return Ghosts.Contains(id); }
        }

        private sealed class NoGhostSink : IGhostDeathSink
        {
            public void OnGhostDied(EntityID entity) { }
        }

        private sealed class TrackingCombat : ICombatProhibitionSink
        {
            public readonly HashSet<EntityID> Banned = new HashSet<EntityID>();

            public void SetCombatProhibited(EntityID entity, bool prohibited)
            {
                if (prohibited) { Banned.Add(entity); } else { Banned.Remove(entity); }
            }
        }

        private sealed class TrackingMovement : IMovementSystem
        {
            public readonly HashSet<EntityID> Banned = new HashSet<EntityID>();

            public bool SetProhibited(EntityID id, bool prohibited)
            {
                if (prohibited) { Banned.Add(id); } else { Banned.Remove(id); }
                return true;
            }

            public bool InjectFacingOverride(EntityID id, Vector3 direction) { return true; }
        }

        private sealed class NoParty : IPartyMemberStatusSink
        {
            public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status) { }
        }

        private sealed class ScriptedPoints : ITownRespawnPointProvider
        {
            public int ZeroReplies;
            public Vector3 Point = TownPoint;

            public Vector3 GetTownRespawnPoint(ZoneID zone)
            {
                if (ZeroReplies > 0)
                {
                    ZeroReplies--;
                    return Vector3.zero;
                }

                return Point;
            }
        }

        private sealed class ThrowingVitals : IRespawnVitalsWriter
        {
            public void RestoreToFull(EntityID entity) { throw new InvalidOperationException("vitals failure"); }
        }

        private sealed class RecordingTeleport : IMovementTeleport
        {
            public bool Throw;
            public int Calls;
            public Vector3 LastPosition;

            public bool TrySetAuthoritativePosition(EntityID id, Vector3 position)
            {
                Calls++;
                LastPosition = position;
                if (Throw)
                {
                    throw new InvalidOperationException("teleport failure");
                }

                return true;
            }
        }

        private ZoneSlotPool _pool;
        private FakeDeathSource _source;
        private FakeDeathTicks _ticks;
        private FakeGhosts _ghosts;
        private ScriptedPoints _points;
        private TrackingCombat _combat;
        private TrackingMovement _movement;
        private RecordingTeleport _teleport;
        private RecordingOutput _output;
        private DeathAndRespawnService _service;
        private EntityID _player;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static DeathAndRespawnConfigValues ConfigOf(float seconds)
        {
            return new DeathAndRespawnConfigValues(seconds, RETRY_BUDGET);
        }

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeServerTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _player = AllocatePlayer(11u);
            _source = new FakeDeathSource();
            _ticks = new FakeDeathTicks { Value = START_TICK };
            _ghosts = new FakeGhosts();
            _points = new ScriptedPoints();
            _combat = new TrackingCombat();
            _movement = new TrackingMovement();
            _teleport = new RecordingTeleport();
            _output = new RecordingOutput
            {
                TickNow = () => _ticks.Value,
                StateOf = SessionStateOf,
                KindOf = id =>
                {
                    Assert.IsTrue(_service.TryGetDeathState(id, out DeathState state));
                    return state.CurrentState;
                },
                CombatBanned = id => _combat.Banned.Contains(id),
                MovementBanned = id => _movement.Banned.Contains(id)
            };
            _service = Build(ConfigOf(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT), new RespawnStubs.Vitals());
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private DeathAndRespawnService Build(DeathAndRespawnConfigValues config, IRespawnVitalsWriter vitals)
        {
            return new DeathAndRespawnService(config, _pool, _source, _ticks, _combat, _movement,
                new NoParty(), _ghosts, new NoGhostSink(), _points, vitals, _teleport,
                new RespawnStubs.Status(), _output);
        }

        private EntityID AllocatePlayer(uint raw)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(raw), new AccountID(1000u + raw), raw,
                out _, out EntityID id));
            return id;
        }

        private ZoneSessionState SessionStateOf(EntityID id)
        {
            Assert.IsTrue(_pool.TryGetPlayer(id, out ZonePlayerSlot slot));
            return slot.State;
        }

        // Advances the death tick source and runs one respawn step, so recorded ticks are real.
        private void TickAt(long tick)
        {
            _ticks.Value = tick;
            _service.Tick(tick);
        }

        // ---- AC-2 ----

        [Test]
        public void test_wire_messages_service_null_output_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnService(
                ConfigOf(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT), _pool, _source, _ticks,
                _combat, _movement, new NoParty(), _ghosts, new NoGhostSink(), _points,
                new RespawnStubs.Vitals(), _teleport, new RespawnStubs.Status(), null));
        }

        [TestCase(7.0f, (byte)7)]
        [TestCase(5.4f, (byte)5)]
        [TestCase(9.6f, (byte)10)]
        public void test_wire_messages_service_timer_seconds_is_rounded_config_delay(float seconds, byte expected)
        {
            _service.Dispose();
            _service = Build(ConfigOf(seconds), new RespawnStubs.Vitals());

            _source.Raise(_player);

            Assert.AreEqual(1, _output.Deaths.Count);
            Assert.AreEqual(expected, _output.Deaths[0].Seconds);
        }

        // ---- AC-3 ----

        [Test]
        public void test_wire_messages_service_player_death_sends_death_once_in_death_tick()
        {
            _source.Raise(_player);

            Assert.AreEqual(1, _output.Deaths.Count);
            Assert.AreEqual(_player, _output.Deaths[0].Entity);
            Assert.AreEqual((byte)7, _output.Deaths[0].Seconds);
            Assert.AreEqual(START_TICK, _output.Deaths[0].Tick);
            Assert.AreEqual(0, _output.Respawns.Count);
        }

        [Test]
        public void test_wire_messages_service_send_death_runs_after_state_slot_and_bans()
        {
            _source.Raise(_player);

            Assert.AreEqual(1, _output.Deaths.Count);
            Assert.AreEqual(DeathStateKind.Dead, _output.Deaths[0].Kind);
            Assert.AreEqual(ZoneSessionState.Dead, _output.Deaths[0].Slot);
            Assert.IsTrue(_output.Deaths[0].CombatBanned);
            Assert.IsTrue(_output.Deaths[0].MovementBanned);
        }

        [Test]
        public void test_wire_messages_service_duplicate_death_sends_only_once()
        {
            _source.Raise(_player);
            _source.Raise(_player);

            Assert.AreEqual(1, _output.Deaths.Count);
        }

        [Test]
        public void test_wire_messages_service_duplicate_death_while_respawning_sends_nothing_more()
        {
            _source.Raise(_player);
            _service.SetStateForTests(_player, DeathStateKind.Respawning);
            _source.Raise(_player);

            Assert.AreEqual(1, _output.Deaths.Count);
        }

        [Test]
        public void test_wire_messages_service_unknown_id_sends_nothing()
        {
            _source.Raise(new EntityID(UNKNOWN_ENTITY_RAW));

            Assert.AreEqual(0, _output.Deaths.Count);
            Assert.AreEqual(0, _output.Respawns.Count);
        }

        [Test]
        public void test_wire_messages_service_allocated_mob_death_sends_nothing()
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID mob));

            _source.Raise(mob);
            TickAt(START_TICK + DELAY_TICKS);

            Assert.AreEqual(0, _output.Deaths.Count);
            Assert.AreEqual(0, _output.Respawns.Count);
        }

        [Test]
        public void test_wire_messages_service_two_players_dying_in_one_tick_send_two_distinct_deaths()
        {
            EntityID second = AllocatePlayer(12u);

            _source.Raise(_player);
            _source.Raise(second);

            Assert.AreEqual(2, _output.Deaths.Count);
            Assert.AreEqual(_player, _output.Deaths[0].Entity);
            Assert.AreEqual(second, _output.Deaths[1].Entity);
            Assert.AreNotEqual(_output.Deaths[0].Entity, _output.Deaths[1].Entity);
        }

        [Test]
        public void test_wire_messages_service_death_after_dispose_sends_nothing()
        {
            _service.Dispose();

            _source.Raise(_player);

            Assert.AreEqual(0, _output.Deaths.Count);
        }

        // ---- AC-4 ----

        [Test]
        public void test_wire_messages_service_respawn_tick_sends_respawn_once_after_slot_is_connected()
        {
            _source.Raise(_player);
            TickAt(START_TICK + DELAY_TICKS - 1);
            Assert.AreEqual(0, _output.Respawns.Count);

            TickAt(START_TICK + DELAY_TICKS);
            TickAt(START_TICK + DELAY_TICKS + 1);

            Assert.AreEqual(1, _output.Respawns.Count);
            Assert.AreEqual(_player, _output.Respawns[0].Entity);
            Assert.AreEqual(TownPoint, _output.Respawns[0].Position);
            Assert.AreEqual(START_TICK + DELAY_TICKS, _output.Respawns[0].Tick);
            Assert.AreEqual(ZoneSessionState.Connected, _output.Respawns[0].State);
            // S2: the position sent is the one the teleport received.
            Assert.AreEqual(_teleport.LastPosition, _output.Respawns[0].Position);
        }

        [Test]
        public void test_wire_messages_service_vitals_seam_throws_respawn_still_sent_once_with_slot_connected()
        {
            _service.Dispose();
            _service = Build(ConfigOf(DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT), new ThrowingVitals());
            _source.Raise(_player);

            LogAssert.Expect(LogType.Error, new Regex("respawn step vitals"));
            TickAt(START_TICK + DELAY_TICKS);

            Assert.AreEqual(1, _output.Respawns.Count);
            Assert.AreEqual(TownPoint, _output.Respawns[0].Position);
            Assert.AreEqual(ZoneSessionState.Connected, _output.Respawns[0].State);
            Assert.AreEqual(1, _output.Deaths.Count);
        }

        [Test]
        public void test_wire_messages_service_teleport_seam_throws_respawn_still_sent_once()
        {
            _teleport.Throw = true;
            _source.Raise(_player);

            LogAssert.Expect(LogType.Error, new Regex("respawn step teleport"));
            TickAt(START_TICK + DELAY_TICKS);

            Assert.AreEqual(1, _teleport.Calls);
            Assert.AreEqual(1, _output.Respawns.Count);
            Assert.AreEqual(TownPoint, _output.Respawns[0].Position);
            Assert.AreEqual(ZoneSessionState.Connected, _output.Respawns[0].State);
            Assert.AreEqual(1, _output.Deaths.Count);
        }

        [Test]
        public void test_wire_messages_service_anchor_retrying_sends_no_respawn()
        {
            _points.ZeroReplies = 3;
            _source.Raise(_player);

            for (int i = 0; i < 3; i++)
            {
                TickAt(START_TICK + DELAY_TICKS + i);
            }

            Assert.AreEqual(0, _output.Respawns.Count);
            Assert.AreEqual(1, _output.Deaths.Count);
        }

        [Test]
        public void test_wire_messages_service_anchor_unresolved_twice_then_resolved_sends_one_respawn_at_resolve_tick()
        {
            _points.ZeroReplies = 2;
            _source.Raise(_player);
            long due = START_TICK + DELAY_TICKS;

            TickAt(due);
            TickAt(due + 1);
            Assert.AreEqual(0, _output.Respawns.Count);

            TickAt(due + 2);
            TickAt(due + 3);

            Assert.AreEqual(1, _output.Respawns.Count);
            Assert.AreEqual(_player, _output.Respawns[0].Entity);
            Assert.AreEqual(TownPoint, _output.Respawns[0].Position);
            Assert.AreEqual(due + 2, _output.Respawns[0].Tick);
        }

        [Test]
        public void test_wire_messages_service_anchor_exhausted_sends_one_respawn_at_origin_and_no_more()
        {
            _points.ZeroReplies = int.MaxValue;
            _source.Raise(_player);
            long due = START_TICK + DELAY_TICKS;

            for (int i = 0; i < RETRY_BUDGET - 1; i++)
            {
                TickAt(due + i);
            }

            Assert.AreEqual(0, _output.Respawns.Count);
            LogAssert.Expect(LogType.Error, new Regex("critical"));
            TickAt(due + RETRY_BUDGET - 1);

            Assert.AreEqual(1, _output.Respawns.Count);
            Assert.AreEqual(Vector3.zero, _output.Respawns[0].Position);
            Assert.AreEqual(due + RETRY_BUDGET - 1, _output.Respawns[0].Tick);

            for (int i = 1; i <= 3; i++)
            {
                TickAt(due + RETRY_BUDGET - 1 + i);
            }

            Assert.AreEqual(1, _output.Respawns.Count);
        }

        // ---- AC-5 ----

        [Test]
        public void test_wire_messages_service_ghost_death_sends_no_death_and_no_respawn()
        {
            _ghosts.Ghosts.Add(_player);

            _source.Raise(_player);
            TickAt(START_TICK + DELAY_TICKS);
            TickAt(START_TICK + DELAY_TICKS * 3);

            Assert.AreEqual(0, _output.Deaths.Count);
            Assert.AreEqual(0, _output.Respawns.Count);
        }
    }

    /// <summary>
    /// EditMode tests for Death and Respawn Story 005, emitter side: <see cref="DeathMessageEmitter"/> over the real
    /// <see cref="ZoneSlotPool"/> and <see cref="ClientMessageOutbox"/> with a recording send seam (AC-6 to AC-9).
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_WireMessages_Emitter_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_P = 11u;
        private const uint CLIENT_Q = 12u;
        private const uint CLIENT_R = 13u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint TICK = 4242u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint UNMAPPED_ENTITY_RAW = 999999u;

        private static readonly Vector3 Position = new Vector3(12.75f, 3.5f, -85.23f);

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;

            public Sent(uint clientId, byte[] data)
            {
                ClientId = clientId;
                Data = data;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();
            public bool Throw;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                if (Throw)
                {
                    throw new InvalidOperationException("send failure");
                }

                Packets.Add(new Sent(clientId, message.ToArray()));
            }
        }

        private ZoneSlotPool _pool;
        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private DeathMessageEmitter _emitter;
        private EntityID _p;
        private EntityID _q;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_P);
            _outbox.AddClient(CLIENT_Q);
            _outbox.AddClient(CLIENT_R);
            _p = AddPlayer(CLIENT_P);
            _q = AddPlayer(CLIENT_Q);
            AddPlayer(CLIENT_R);
            _emitter = new DeathMessageEmitter(_pool, _outbox);
        }

        private EntityID AddPlayer(uint client)
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET),
                client, out _, out EntityID id));
            return id;
        }

        private void Flush()
        {
            _outbox.Flush(TICK);
        }

        // Message type ids received by one client, in send order.
        private List<ushort> TypesFor(uint client)
        {
            var types = new List<ushort>();
            foreach (Sent packet in _seam.Packets)
            {
                if (packet.ClientId != client)
                {
                    continue;
                }

                Assert.That(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope), Is.True);
                types.Add(envelope.MessageTypeId);
            }

            return types;
        }

        // Body of the nth message of the given type sent to a client.
        private byte[] BodyOf(uint client, ushort typeId, int nth = 0)
        {
            int seen = 0;
            foreach (Sent packet in _seam.Packets)
            {
                if (packet.ClientId != client)
                {
                    continue;
                }

                Assert.That(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope), Is.True);
                if (envelope.MessageTypeId == typeId && seen++ == nth)
                {
                    return packet.Data.AsSpan(ServerMessageEnvelope.WireSize).ToArray();
                }
            }

            Assert.Fail("no message " + typeId.ToString("X4") + " for client " + client);
            return null;
        }

        // ---- AC-6 ----

        [Test]
        public void test_wire_messages_emitter_send_death_owner_gets_state_then_died_others_only_died()
        {
            _emitter.SendDeath(_p, 7);
            Flush();

            Assert.AreEqual(4, _seam.Packets.Count);
            CollectionAssert.AreEqual(new[] { DeathStateEntered.MessageTypeId, EntityDied.MessageTypeId }, TypesFor(CLIENT_P));
            CollectionAssert.AreEqual(new[] { EntityDied.MessageTypeId }, TypesFor(CLIENT_Q));
            CollectionAssert.AreEqual(new[] { EntityDied.MessageTypeId }, TypesFor(CLIENT_R));

            Assert.That(DeathStateEnteredCodec.TryReadBody(BodyOf(CLIENT_P, DeathStateEntered.MessageTypeId),
                out uint stateEntity, out byte seconds), Is.True);
            Assert.AreEqual(_p.RawValue, stateEntity);
            Assert.AreEqual((byte)7, seconds);
            foreach (uint client in new[] { CLIENT_P, CLIENT_Q, CLIENT_R })
            {
                Assert.That(EntityDiedCodec.TryReadBody(BodyOf(client, EntityDied.MessageTypeId), out uint diedEntity), Is.True);
                Assert.AreEqual(_p.RawValue, diedEntity);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_wire_messages_emitter_send_respawn_zone_gets_respawned_owner_also_confirmed_after()
        {
            _emitter.SendRespawn(_p, Position);
            Flush();

            Assert.AreEqual(4, _seam.Packets.Count);
            CollectionAssert.AreEqual(new[] { EntityRespawned.MessageTypeId, RespawnConfirmed.MessageTypeId }, TypesFor(CLIENT_P));
            CollectionAssert.AreEqual(new[] { EntityRespawned.MessageTypeId }, TypesFor(CLIENT_Q));
            CollectionAssert.AreEqual(new[] { EntityRespawned.MessageTypeId }, TypesFor(CLIENT_R));

            foreach (uint client in new[] { CLIENT_P, CLIENT_Q, CLIENT_R })
            {
                Assert.That(EntityRespawnedCodec.TryReadBody(BodyOf(client, EntityRespawned.MessageTypeId),
                    out uint entity, out Vector3 position), Is.True);
                Assert.AreEqual(_p.RawValue, entity);
                Assert.AreEqual(Position.x, position.x, 0.005f);
                Assert.AreEqual(Position.y, position.y, 0.005f);
                Assert.AreEqual(Position.z, position.z, 0.005f);
            }

            Assert.That(RespawnConfirmedCodec.TryReadBody(BodyOf(CLIENT_P, RespawnConfirmed.MessageTypeId), out uint confirmed), Is.True);
            Assert.AreEqual(_p.RawValue, confirmed);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_wire_messages_emitter_two_deaths_each_owner_gets_own_state_and_all_get_both_died()
        {
            _emitter.SendDeath(_p, 7);
            _emitter.SendDeath(_q, 7);
            Flush();

            Assert.That(DeathStateEnteredCodec.TryReadBody(BodyOf(CLIENT_P, DeathStateEntered.MessageTypeId),
                out uint pState, out _), Is.True);
            Assert.That(DeathStateEnteredCodec.TryReadBody(BodyOf(CLIENT_Q, DeathStateEntered.MessageTypeId),
                out uint qState, out _), Is.True);
            Assert.AreEqual(_p.RawValue, pState);
            Assert.AreEqual(_q.RawValue, qState);
            Assert.IsEmpty(TypesFor(CLIENT_R).FindAll(t => t == DeathStateEntered.MessageTypeId));

            foreach (uint client in new[] { CLIENT_P, CLIENT_Q, CLIENT_R })
            {
                Assert.That(EntityDiedCodec.TryReadBody(BodyOf(client, EntityDied.MessageTypeId, 0), out uint first), Is.True);
                Assert.That(EntityDiedCodec.TryReadBody(BodyOf(client, EntityDied.MessageTypeId, 1), out uint second), Is.True);
                Assert.AreEqual(_p.RawValue, first);
                Assert.AreEqual(_q.RawValue, second);
            }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-7 ----

        [Test]
        public void test_wire_messages_emitter_owner_removed_from_outbox_others_get_died_and_no_log()
        {
            _outbox.RemoveClient(CLIENT_P);

            _emitter.SendDeath(_p, 7);
            Flush();

            Assert.IsEmpty(TypesFor(CLIENT_P));
            CollectionAssert.AreEqual(new[] { EntityDied.MessageTypeId }, TypesFor(CLIENT_Q));
            CollectionAssert.AreEqual(new[] { EntityDied.MessageTypeId }, TypesFor(CLIENT_R));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_wire_messages_emitter_owner_removed_from_outbox_respawn_skips_owner_and_logs_nothing()
        {
            _outbox.RemoveClient(CLIENT_P);

            _emitter.SendRespawn(_p, Position);
            Flush();

            Assert.IsEmpty(TypesFor(CLIENT_P));
            CollectionAssert.AreEqual(new[] { EntityRespawned.MessageTypeId }, TypesFor(CLIENT_Q));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_wire_messages_emitter_entity_without_pool_slot_sends_zone_wide_only_and_no_log()
        {
            var unmapped = new EntityID(UNMAPPED_ENTITY_RAW);

            _emitter.SendDeath(unmapped, 7);
            _emitter.SendRespawn(unmapped, Position);
            Flush();

            foreach (uint client in new[] { CLIENT_P, CLIENT_Q, CLIENT_R })
            {
                CollectionAssert.AreEqual(new[] { EntityDied.MessageTypeId, EntityRespawned.MessageTypeId }, TypesFor(client));
            }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-8 ----

        [Test]
        public void test_wire_messages_emitter_outbox_contains_send_faults_and_emitter_never_throws_or_logs()
        {
            // The outbox buffers in Enqueue and logs a send fault once per client at Flush; the emitter's own
            // catch is defensive and must stay silent (no [DeathMessageEmitter] log).
            _seam.Throw = true;

            Assert.DoesNotThrow(() => _emitter.SendDeath(_p, 7));
            Assert.DoesNotThrow(() => _emitter.SendRespawn(_p, Position));

            LogAssert.Expect(LogType.Error, new Regex(@"\[ClientMessageOutbox\] SendThrew: clientId=11 "));
            LogAssert.Expect(LogType.Error, new Regex(@"\[ClientMessageOutbox\] SendThrew: clientId=12 "));
            LogAssert.Expect(LogType.Error, new Regex(@"\[ClientMessageOutbox\] SendThrew: clientId=13 "));
            Assert.DoesNotThrow(Flush);

            Assert.IsEmpty(_seam.Packets);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_wire_messages_emitter_constructor_null_argument_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new DeathMessageEmitter(null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new DeathMessageEmitter(_pool, null));
        }

        // ---- AC-9 ----

        [Test]
        public void test_wire_messages_emitter_respawn_position_out_of_range_is_clamped_warned_and_still_sent()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodePosition"));
            _emitter.SendRespawn(_p, new Vector3(400f, 0f, 0f));
            Flush();

            foreach (uint client in new[] { CLIENT_P, CLIENT_Q, CLIENT_R })
            {
                Assert.That(EntityRespawnedCodec.TryReadBody(BodyOf(client, EntityRespawned.MessageTypeId),
                    out _, out Vector3 position), Is.True);
                Assert.AreEqual(327.67f, position.x, 0.005f);
            }

            LogAssert.NoUnexpectedReceived();
        }
    }
}
