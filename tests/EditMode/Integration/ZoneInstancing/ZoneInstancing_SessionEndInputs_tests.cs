using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 016 (TD-101): the zone-side session end inputs. Real
    /// <see cref="ZoneInstancingWiring"/>, <see cref="ConnectionStateMachine"/>, <see cref="RecordingConnectionActivitySink"/>
    /// and <see cref="SessionMachineGhostSource"/> over fake persistence / outbox / party; ticks are advanced by hand.
    /// AC-1..AC-6, AC-8 and AC-9 live here; AC-7 and AC-10 are composition tests (<c>ZoneServer_CompositionSessionEnd_tests</c>).
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_SessionEndInputs_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const uint HEARTBEAT_TIMEOUT_TICKS = 10u * HZ;
        private const uint CONNECTING_TIMEOUT_TICKS = 30u * HZ;
        private const uint SESSION_TTL_TICKS = 300u * HZ;
        private const uint ACCOUNT_OFFSET = 900u;
        private const uint CLIENT_A = 1u;
        private const uint CLIENT_B = 2u;
        private const uint CLIENT_C = 3u;
        private const uint OBSERVER = 4u;
        private const uint SECOND_BASE = 60u * HZ;
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromMilliseconds(200);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private struct P
        {
            public EntityID Entity;
            public CharacterID Character;
            public uint Client;
            public uint Account;
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Now;

            public uint ServerTickNumber => Now;
        }

        private sealed class FakeOutbox : IClientMessageOutbox
        {
            public readonly List<(uint Client, ushort Type)> Sent = new List<(uint, ushort)>();

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Sent.Add((clientId, messageTypeId));
            }

            public int CountFor(uint client, ushort type)
            {
                int n = 0;
                foreach ((uint c, ushort t) in Sent)
                {
                    if (c == client && t == type)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private sealed class FakeDispatcher : IInboundRequestDispatcher
        {
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public void DisconnectClient(ulong transportClientId) { }
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            public int Exits;

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = new PartyID(1u);
                return true;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId)
            {
                Exits++;
            }
        }

        private sealed class NoOpSink : IConnectionMessageSink
        {
            public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body) { }
        }

        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly List<(CharacterID Id, SessionEndReason Reason)> SaveCalls = new List<(CharacterID, SessionEndReason)>();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => Save(id, reason);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => Save(id, reason);

            private Task<CharacterSaveResult> Save(CharacterID id, SessionEndReason reason)
            {
                SaveCalls.Add((id, reason));
                return Task.FromResult(CharacterSaveResult.Success);
            }

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private FakeTicks _ticks;
        private FakeOutbox _outbox;
        private FakePersistence _persistence;
        private FakeParty _party;
        private TickCompletionQueue _queue;
        private RecordingConnectionActivitySink _sink;
        private ConnectionStateMachine _machine;
        private ZoneInstancingWiring _wiring;
        private uint _nextCharacter;

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _outbox = new FakeOutbox();
            _persistence = new FakePersistence();
            _party = new FakeParty();
            _queue = new TickCompletionQueue();
            _sink = new RecordingConnectionActivitySink(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            _machine = new ConnectionStateMachine();
            _nextCharacter = 1000u;
            UseGhostSource(new SessionMachineGhostSource(_machine, ZoneSlotPool.MAX_PLAYERS_PER_ZONE));
        }

        /// <summary>Replaces <c>_wiring</c> with a fresh active one that reads ghosts from <paramref name="ghosts"/>.</summary>
        private void UseGhostSource(IZoneGhostSessionSource ghosts)
        {
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, new FakeDispatcher(), _outbox, _persistence, _persistence,
                new FakeApplier(), new CharacterMutationGate(), new RecordingInventory(), new FakeDisconnector(), _ => { },
                new ZoneInstancingWiringOptions
                {
                    TickRateHz = HZ,
                    Equipment = new FakeEquipmentService(),
                    Party = _party,
                    Sessions = _machine,
                    ActivitySink = _sink,
                    Ghosts = ghosts,
                });
            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
        }

        private sealed class RecordingGhosts : IZoneGhostSessionSource
        {
            private readonly SessionMachineGhostSource _inner;
            private readonly ConnectionStateMachine _machine;
            private readonly uint _account;
            public readonly List<(SessionState State, int Count)> Refreshes = new List<(SessionState, int)>();

            public RecordingGhosts(ConnectionStateMachine machine, uint account)
            {
                _machine = machine;
                _account = account;
                _inner = new SessionMachineGhostSource(machine, 4);
            }

            public int Count => _inner.Count;

            public void Refresh()
            {
                _inner.Refresh();
                _machine.TryGetSessionState(_account, out SessionState state);
                Refreshes.Add((state, _inner.Count));
            }

            public void Get(int index, out uint accountId, out uint sessionExpiryTick) => _inner.Get(index, out accountId, out sessionExpiryTick);
        }

        private void Reconnect(P p, uint tick)
        {
            Assert.IsTrue(_machine.TryGetSessionExpiryTick(p.Account, out uint expiry));
            _machine.EnterReconnecting(p.Account, expiry);
            var currency = new CurrencySystem();
            currency.RegisterCharacter(p.Character, 0u);
            _machine.CompleteReAuthSuccess(p.Account, tick, currency,
                queryGoldDebitedPurchases: _ => Array.Empty<IronGrind.Networking.PendingPurchaseRecord>(),
                markPurchaseRefunded: _ => { },
                queryRespecReservationStatus: _ => RespecReservationStatus.NoActiveReservation,
                representRespecPhase2: _ => { },
                releaseRespecReservationAndNotify: _ => { },
                handshakeData: new SessionHandshakeData(false, 1u, 1, 1, 1, 0, 1));
        }

        private int SavesWithReason(SessionEndReason reason)
        {
            int n = 0;
            foreach ((CharacterID _, SessionEndReason r) in _persistence.SaveCalls)
            {
                if (r == reason)
                {
                    n++;
                }
            }

            return n;
        }

        [TearDown]
        public void TearDown()
        {
            _queue.DrainOnShutdown(DRAIN_TIMEOUT);
        }

        // ----- helpers --------------------------------------------------------------------------

        private P AddConnected(uint client, uint tick)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            uint account = client + ACCOUNT_OFFSET;
            Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, character, new AccountID(account), client, out _, out EntityID id));
            _wiring.Pool.SetSessionState(id, ZoneSessionState.Connected);
            _wiring.Directory.Add(client, character);
            _wiring.TickRoster.AddPending(id);
            _machine.EnterConnecting(account, tick);
            _machine.CompleteAuthSuccess(account, n, tick);
            return new P { Entity = id, Character = character, Client = client, Account = account };
        }

        /// <summary>One server tick: the receive callback first (<paramref name="receive"/>), then the wiring step and the queue drain.</summary>
        private void RunTick(uint tick, Action<uint> receive = null)
        {
            _ticks.Now = tick;
            receive?.Invoke(tick);
            _wiring.Run(tick, 0f);
            _queue.Drain(tick);
        }

        private SessionState StateOf(uint account)
        {
            Assert.IsTrue(_machine.TryGetSessionState(account, out SessionState state), "session registered");
            return state;
        }

        // ----- AC-1 -----------------------------------------------------------------------------

        [Test]
        public void test_activity_every_five_seconds_keeps_a_connected_client_connected_over_sixty_seconds()
        {
            P p = AddConnected(CLIENT_A, 0u);

            for (uint t = 1u; t <= SECOND_BASE; t++)
            {
                RunTick(t, tick =>
                {
                    if (tick % (5u * HZ) == 0u)
                    {
                        _sink.OnInboundActivity(CLIENT_A, tick);
                    }
                });
            }

            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
        }

        [Test]
        public void test_activity_from_a_client_without_a_slot_is_ignored_and_the_sink_is_emptied()
        {
            P p = AddConnected(CLIENT_A, 0u);

            RunTick(1u, tick => _sink.OnInboundActivity(999u, tick));

            Assert.AreEqual(0, _sink.Count, "the drain empties the sink");
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
        }

        [Test]
        public void test_the_latest_recorded_tick_per_client_is_what_the_session_machine_receives()
        {
            P p = AddConnected(CLIENT_A, 0u);

            _sink.OnInboundActivity(CLIENT_A, 150u);
            _sink.OnInboundActivity(CLIENT_A, 190u);
            RunTick(190u);
            RunTick(389u); // 190 + 199: still inside the window of the latest tick
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
            RunTick(390u); // 190 + 200
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(p.Account));
        }

        // ----- AC-2 -----------------------------------------------------------------------------

        [Test]
        public void test_heartbeat_only_client_through_the_real_dispatcher_stays_connected_and_logs_no_anomaly()
        {
            P p = AddConnected(CLIENT_A, 0u);
            var dispatcher = new InboundRequestDispatcher(new CrossCuttingRpcGuardChain(), new CharacterMutationGate(),
                new InboundDictionaryDirectory(), _sink, _ticks, MessageRoutingRegistry.TryGetEntry, false);
            NetworkingCoreInboundRegistration.RegisterHeartbeat(dispatcher, new NoOpSink());
            dispatcher.Seal();
            Assert.IsTrue(dispatcher.AddConnection(CLIENT_A));
            var message = new byte[HeartbeatMessage.WireSize];
            var logs = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Log)
                {
                    logs.Add(condition);
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                for (uint t = 1u; t <= SECOND_BASE; t++)
                {
                    _ticks.Now = t;
                    if (t % (3u * HZ) == 0u)
                    {
                        MessageEnvelopeCodec.Write(message, HeartbeatMessage.CreateEnvelope(t, t));
                        Assert.IsTrue(dispatcher.TryAccept(CLIENT_A, message));
                    }

                    _wiring.Run(t, 0f);
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
            Assert.AreEqual(0, logs.Count, "no ANOMALY_UNKNOWN_TYPE or other warning: " + string.Join(" | ", logs));
        }

        [Test]
        public void test_register_heartbeat_registers_the_connection_level_descriptor_of_the_core_registration()
        {
            var dispatcher = new CapturingDispatcher();

            NetworkingCoreInboundRegistration.RegisterHeartbeat(dispatcher, new NoOpSink());

            Assert.AreEqual(1, dispatcher.Descriptors.Count);
            Assert.AreEqual(HeartbeatMessage.MessageTypeId, dispatcher.Descriptors[0].MessageTypeId);
            Assert.AreEqual(0, dispatcher.Descriptors[0].MaxBodyBytes);
            Assert.IsFalse(dispatcher.Descriptors[0].CarriesSenderEntityId);
        }

        private sealed class CapturingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<ConnectionMessageDescriptor> Descriptors = new List<ConnectionMessageDescriptor>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) => Descriptors.Add(descriptor);

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        // ----- AC-3 / AC-4 ----------------------------------------------------------------------

        [Test]
        public void test_silent_client_is_ghosted_at_tick_200_not_199_with_expiry_tick_plus_6000_and_nothing_ends()
        {
            P p = AddConnected(CLIENT_A, 0u);
            AddConnected(OBSERVER, 0u); // a live neighbour (kept alive below) that would receive any PlayerLeftZone

            for (uint t = 1u; t <= HEARTBEAT_TIMEOUT_TICKS - 1u; t++)
            {
                RunTick(t, tick => _sink.OnInboundActivity(OBSERVER, tick));
            }

            Assert.AreEqual(SessionState.Connected, StateOf(p.Account), "tick 199");
            RunTick(HEARTBEAT_TIMEOUT_TICKS, tick => _sink.OnInboundActivity(OBSERVER, tick));

            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(p.Account), "tick 200");
            Assert.IsTrue(_machine.TryGetSessionExpiryTick(p.Account, out uint expiry));
            Assert.AreEqual(HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS, expiry);
            Assert.AreEqual(6000u, SESSION_TTL_TICKS);
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(p.Entity, out _), "slot and entity stay");
            Assert.AreEqual(0, _persistence.SaveCalls.Count, "no save");
            Assert.AreEqual(0, _party.Exits, "no NotifyZoneExit");
            Assert.AreEqual(0, _outbox.CountFor(OBSERVER, PlayerLeftZone.MessageTypeId), "no PlayerLeftZone");
        }

        [Test]
        public void test_transport_close_changes_nothing_and_the_client_is_ghosted_ten_seconds_after_its_last_activity()
        {
            P p = AddConnected(CLIENT_A, 0u);
            RunTick(50u, tick => _sink.OnInboundActivity(CLIENT_A, tick));
            // The transport closes here: no input reaches the session machine (the client simply stops producing activity).
            RunTick(60u);
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account), "no transition at close");

            RunTick(50u + HEARTBEAT_TIMEOUT_TICKS - 1u);
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
            RunTick(50u + HEARTBEAT_TIMEOUT_TICKS);

            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(p.Account));
        }

        // ----- AC-5 -----------------------------------------------------------------------------

        [Test]
        public void test_two_ghosts_expiring_in_the_same_tick_are_both_ended_and_a_later_ghost_is_untouched()
        {
            P a = AddConnected(CLIENT_A, 0u);
            P b = AddConnected(CLIENT_B, 0u);
            P c = AddConnected(CLIENT_C, 100u);
            P observer = AddConnected(OBSERVER, 0u);
            uint expiryAB = HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS;

            for (uint t = 1u; t < expiryAB; t++)
            {
                RunTick(t, tick =>
                {
                    if (tick % 100u == 0u)
                    {
                        _sink.OnInboundActivity(OBSERVER, tick);
                    }
                });
            }

            Assert.AreEqual(0, _persistence.SaveCalls.Count, "nothing ends before the expiry tick");
            RunTick(expiryAB, tick => _sink.OnInboundActivity(OBSERVER, tick));

            Assert.AreEqual(2, _persistence.SaveCalls.Count, "two saves");
            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _persistence.SaveCalls[0].Reason);
            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _persistence.SaveCalls[1].Reason);
            CollectionAssert.AreEquivalent(new[] { a.Character, b.Character },
                new[] { _persistence.SaveCalls[0].Id, _persistence.SaveCalls[1].Id });
            Assert.AreEqual(2, _outbox.CountFor(observer.Client, PlayerLeftZone.MessageTypeId), "two PlayerLeftZone");
            Assert.AreEqual(2, _party.Exits, "two NotifyZoneExit");
            Assert.IsFalse(_wiring.Pool.TryGetPlayer(a.Entity, out _));
            Assert.IsFalse(_wiring.Pool.TryGetPlayer(b.Entity, out _));
            Assert.IsFalse(_machine.TryGetSessionState(a.Account, out _), "session removed");
            Assert.IsFalse(_machine.TryGetSessionState(b.Account, out _), "session removed");
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(c.Entity, out _), "later ghost untouched");
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(c.Account));
        }

        // ----- AC-6 -----------------------------------------------------------------------------

        [Test]
        public void test_connecting_session_with_no_admission_is_removed_after_thirty_seconds()
        {
            uint account = 5000u;
            _machine.EnterConnecting(account, 0u);

            RunTick(CONNECTING_TIMEOUT_TICKS - 1u);
            Assert.AreEqual(SessionState.Connecting, StateOf(account), "tick 599");
            RunTick(CONNECTING_TIMEOUT_TICKS);

            Assert.IsFalse(_machine.TryGetSessionState(account, out _), "removed at tick 600");
        }

        [Test]
        public void test_a_character_load_taking_the_watchdog_duration_is_admitted_with_a_connected_session()
        {
            uint account = 5001u;
            _machine.EnterConnecting(account, 0u);

            uint watchdog = (uint)TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS;
            RunTick(watchdog);
            Assert.AreEqual(SessionState.Connecting, StateOf(account), "still Connecting when the load ends");
            _machine.CompleteAuthSuccess(account, 77u, watchdog);

            Assert.AreEqual(SessionState.Connected, StateOf(account));
        }

        // ----- AC-8 -----------------------------------------------------------------------------

        [Test]
        public void test_activity_recorded_in_the_receive_callback_of_the_timeout_tick_prevents_that_ticks_timeout()
        {
            P p = AddConnected(CLIENT_A, 0u);
            for (uint t = 1u; t < HEARTBEAT_TIMEOUT_TICKS; t++)
            {
                RunTick(t);
            }

            RunTick(HEARTBEAT_TIMEOUT_TICKS, tick => _sink.OnInboundActivity(CLIENT_A, tick));

            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
        }

        [Test]
        public void test_a_session_ghosted_this_tick_is_listed_this_tick_but_not_ended()
        {
            // Arrange: the wiring under test reads a recording source that logs the session state at each Refresh.
            P p = AddConnected(CLIENT_A, 0u);
            var recording = new RecordingGhosts(_machine, p.Account);
            UseGhostSource(recording);

            // Act
            for (uint t = 1u; t <= HEARTBEAT_TIMEOUT_TICKS; t++)
            {
                RunTick(t);
            }

            // Assert: the wiring refreshed AFTER EvaluateTimeouts, so the tick-200 refresh already saw the new ghost.
            Assert.AreEqual((int)HEARTBEAT_TIMEOUT_TICKS, recording.Refreshes.Count, "one Refresh per Run");
            Assert.AreEqual((SessionState.Connected, 0), recording.Refreshes[(int)HEARTBEAT_TIMEOUT_TICKS - 2], "tick 199");
            Assert.AreEqual((SessionState.Disconnected_SessionActive, 1), recording.Refreshes[(int)HEARTBEAT_TIMEOUT_TICKS - 1], "tick 200");
            recording.Get(0, out uint account, out uint expiry);
            Assert.AreEqual(p.Account, account);
            Assert.AreEqual(HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS, expiry);
            Assert.AreEqual(0, _persistence.SaveCalls.Count, "listed but not ended this tick");
        }

        // ----- ghost lifecycle before expiry ----------------------------------------------------

        [Test]
        public void test_ghost_that_reconnects_before_expiry_is_not_ended_at_the_old_expiry_tick()
        {
            P p = AddConnected(CLIENT_A, 0u);
            for (uint t = 1u; t <= HEARTBEAT_TIMEOUT_TICKS; t++)
            {
                RunTick(t);
            }

            uint oldExpiry = HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS;
            RunTick(250u);
            Reconnect(p, 250u);

            for (uint t = 251u; t <= oldExpiry + 1u; t++)
            {
                RunTick(t, tick =>
                {
                    if (tick % 100u == 0u)
                    {
                        _sink.OnInboundActivity(CLIENT_A, tick);
                    }
                });
            }

            Assert.AreEqual(0, _persistence.SaveCalls.Count, "no save");
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(p.Entity, out _), "slot present");
        }

        [Test]
        public void test_ghost_that_is_evicted_before_expiry_is_not_ended_again_at_the_old_expiry_tick()
        {
            P p = AddConnected(CLIENT_A, 0u);
            for (uint t = 1u; t <= HEARTBEAT_TIMEOUT_TICKS; t++)
            {
                RunTick(t);
            }

            uint oldExpiry = HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS;
            int evicted = 0;
            RunTick(250u);
            _wiring.SessionEnds.BeginEviction(p.Account, () => evicted++);
            for (uint t = 251u; t <= oldExpiry + 1u; t++)
            {
                RunTick(t);
            }

            Assert.AreEqual(1, evicted, "eviction finished once");
            Assert.AreEqual(0, SavesWithReason(SessionEndReason.SessionTTLExpiry), "no TTL end at the old expiry");
            Assert.AreEqual(1, _persistence.SaveCalls.Count, "only the eviction save");
            Assert.IsFalse(_machine.TryGetSessionState(p.Account, out _), "session removed");
            Assert.IsFalse(_wiring.Pool.TryGetPlayer(p.Entity, out _), "slot released");
        }

        [Test]
        public void test_reconnected_ghost_is_connected_for_the_full_heartbeat_window_and_ghosted_again_without_activity()
        {
            P p = AddConnected(CLIENT_A, 0u);
            for (uint t = 1u; t <= HEARTBEAT_TIMEOUT_TICKS; t++)
            {
                RunTick(t);
            }

            const uint T = 250u;
            RunTick(T);
            Reconnect(p, T);

            RunTick(T + 1u);
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account), "T+1");
            RunTick(T + HEARTBEAT_TIMEOUT_TICKS - 1u);
            Assert.AreEqual(SessionState.Connected, StateOf(p.Account), "T+199");
            RunTick(T + HEARTBEAT_TIMEOUT_TICKS);
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(p.Account), "T+200");
        }

        [Test]
        public void test_collect_session_active_ghosts_clears_the_lists_and_lists_only_disconnected_session_active()
        {
            var machine = new ConnectionStateMachine();
            machine.EnterConnecting(1u, 0u);                                         // Connecting
            machine.EnterConnecting(3u, 0u);
            machine.CompleteAuthSuccess(3u, 30u, 0u);                                // -> Reconnecting below
            machine.EnterConnecting(4u, 0u);
            machine.CompleteAuthSuccess(4u, 40u, 0u);                                // -> ghost
            machine.EvaluateTimeouts(1u, 1u, 1000u);                                 // 3 and 4 ghosted, 1 still Connecting
            machine.EnterReconnecting(3u, 777u);                                     // Reconnecting
            machine.EnterConnecting(2u, 1u);
            machine.CompleteAuthSuccess(2u, 20u, 1u);                                // Connected
            var accounts = new List<uint> { 99u, 98u, 97u };
            var expiries = new List<uint> { 1u, 2u, 3u };

            machine.CollectSessionActiveGhosts(accounts, expiries);

            CollectionAssert.AreEqual(new[] { 4u }, accounts);
            Assert.AreEqual(1, expiries.Count);
            Assert.IsTrue(machine.TryGetSessionExpiryTick(4u, out uint expiry));
            Assert.AreEqual(expiry, expiries[0]);
        }

        // ----- activity from ghosts and from clients without a slot -----------------------------

        [Test]
        public void test_activity_from_a_ghosts_client_does_not_bring_it_back()
        {
            P p = AddConnected(CLIENT_A, 0u);
            uint expiry = HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS;
            for (uint t = 1u; t < expiry - 1u; t++)
            {
                RunTick(t);
            }

            RunTick(expiry - 1u, tick => _sink.OnInboundActivity(CLIENT_A, tick));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(p.Account), "state unchanged by activity");
            Assert.AreEqual(0, _persistence.SaveCalls.Count);
            RunTick(expiry, tick => _sink.OnInboundActivity(CLIENT_A, tick));

            Assert.AreEqual(1, SavesWithReason(SessionEndReason.SessionTTLExpiry), "ended at the expiry tick");
            Assert.IsFalse(_machine.TryGetSessionState(p.Account, out _));
        }

        [Test]
        public void test_activity_from_a_client_whose_slot_was_released_is_ignored()
        {
            P p = AddConnected(CLIENT_A, 0u);
            Assert.IsTrue(_wiring.Pool.ReleasePlayer(p.Entity));
            _sink.OnInboundActivity(CLIENT_A, 5u);

            Assert.DoesNotThrow(() => RunTick(5u));

            Assert.AreEqual(SessionState.Connected, StateOf(p.Account));
            Assert.AreEqual(0, _sink.Count);
        }

        [Test]
        public void test_activity_from_two_clients_where_only_one_has_a_slot_updates_only_that_one()
        {
            P a = AddConnected(CLIENT_A, 0u);
            P b = AddConnected(CLIENT_B, 0u);
            Assert.IsTrue(_wiring.Pool.ReleasePlayer(b.Entity));

            RunTick(100u, tick =>
            {
                _sink.OnInboundActivity(CLIENT_A, tick);
                _sink.OnInboundActivity(CLIENT_B, tick);
            });
            RunTick(HEARTBEAT_TIMEOUT_TICKS);

            Assert.AreEqual(SessionState.Connected, StateOf(a.Account), "A's activity counted");
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(b.Account), "B's activity was ignored");
            Assert.AreEqual(0, _sink.Count);
        }

        // ----- ghost source capacity ------------------------------------------------------------

        [Test]
        public void test_ghost_source_with_more_ghosts_than_initial_capacity_lists_all()
        {
            var machine = new ConnectionStateMachine();
            machine.EnterConnecting(1u, 0u);
            machine.CompleteAuthSuccess(1u, 10u, 0u);
            machine.EvaluateTimeouts(10u, 10u, 1000u);
            machine.EnterConnecting(2u, 5u);
            machine.CompleteAuthSuccess(2u, 20u, 5u);
            machine.EvaluateTimeouts(15u, 10u, 1000u);
            machine.EnterConnecting(3u, 10u);
            machine.CompleteAuthSuccess(3u, 30u, 10u);
            machine.EvaluateTimeouts(20u, 10u, 1000u);
            var source = new SessionMachineGhostSource(machine, 1);

            source.Refresh();

            Assert.AreEqual(3, source.Count);
            var seen = new Dictionary<uint, uint>();
            for (int i = 0; i < source.Count; i++)
            {
                source.Get(i, out uint account, out uint expiry);
                seen[account] = expiry;
            }

            Assert.AreEqual(SESSION_TTL_TICKS + 10u, seen[1u]);
            Assert.AreEqual(SESSION_TTL_TICKS + 15u, seen[2u]);
            Assert.AreEqual(SESSION_TTL_TICKS + 20u, seen[3u]);
        }

        [Test]
        public void test_three_ghosts_expiring_in_the_same_tick_are_all_ended_with_a_capacity_one_source()
        {
            UseGhostSource(new SessionMachineGhostSource(_machine, 1));
            AddConnected(CLIENT_A, 0u);
            AddConnected(CLIENT_B, 0u);
            AddConnected(CLIENT_C, 0u);
            uint expiry = HEARTBEAT_TIMEOUT_TICKS + SESSION_TTL_TICKS;
            for (uint t = 1u; t < expiry; t++)
            {
                RunTick(t);
            }

            Assert.AreEqual(0, _persistence.SaveCalls.Count, "nothing ends before the expiry tick");
            RunTick(expiry);

            Assert.AreEqual(3, SavesWithReason(SessionEndReason.SessionTTLExpiry));
        }

        // ----- AC-9 -----------------------------------------------------------------------------

        [Test]
        public void test_the_activity_sink_record_and_take_allocate_nothing_in_steady_state()
        {
            _sink.OnInboundActivity(5u, 1u);
            _sink.TryTake(5u, out _);

            Assert.That(() =>
            {
                _sink.OnInboundActivity(5u, 2u);
                _sink.TryTake(5u, out _);
                _sink.Clear();
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_evaluate_timeouts_and_ghost_enumeration_allocate_nothing_in_steady_state()
        {
            AddConnected(CLIENT_A, 0u);
            AddConnected(CLIENT_B, 0u);
            AddConnected(CLIENT_C, 0u);
            _machine.EvaluateTimeouts(HEARTBEAT_TIMEOUT_TICKS, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS); // all three ghosted
            AddConnected(OBSERVER, HEARTBEAT_TIMEOUT_TICKS); // a Connected session in the evaluated set
            var ghosts = new SessionMachineGhostSource(_machine, 8);
            ghosts.Refresh();
            Assert.AreEqual(3, ghosts.Count);

            Assert.That(() => { _machine.EvaluateTimeouts(HEARTBEAT_TIMEOUT_TICKS + 1u, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS); },
                Is.Not.AllocatingGCMemory());
            Assert.That(() =>
            {
                ghosts.Refresh();
                for (int i = 0; i < ghosts.Count; i++)
                {
                    ghosts.Get(i, out _, out _);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_run_with_ghosts_connecting_and_pending_activity_allocates_nothing()
        {
            AddConnected(CLIENT_B, 0u);
            AddConnected(CLIENT_C, 0u);
            _machine.EvaluateTimeouts(HEARTBEAT_TIMEOUT_TICKS, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS); // B and C ghosted (unexpired)
            AddConnected(CLIENT_A, HEARTBEAT_TIMEOUT_TICKS);                                                       // Connected, with activity
            _machine.EnterConnecting(5002u, HEARTBEAT_TIMEOUT_TICKS);                                              // Connecting
            uint tick = HEARTBEAT_TIMEOUT_TICKS;
            for (int i = 0; i < 3; i++)
            {
                tick++;
                RunTick(tick, t => _sink.OnInboundActivity(CLIENT_A, t));
            }

            tick++;
            _sink.OnInboundActivity(CLIENT_A, tick);
            _ticks.Now = tick;
            uint runTick = tick;

            Assert.That(() => { _wiring.Run(runTick, 0f); }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, _sink.Count);
        }

        [Test]
        public void test_the_activity_drain_allocates_nothing_in_steady_state()
        {
            AddConnected(CLIENT_A, 0u);
            AddConnected(CLIENT_B, 0u);
            for (uint t = 1u; t <= 3u; t++)
            {
                RunTick(t, tick => _sink.OnInboundActivity(CLIENT_A, tick));
            }

            _sink.OnInboundActivity(CLIENT_A, 4u);
            _sink.OnInboundActivity(CLIENT_B, 4u);
            // Drain happens inside Run; the whole Zone Instancing step must stay allocation-free at steady state.
            _ticks.Now = 4u;
            Assert.That(() => { _wiring.Run(4u, 0f); }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, _sink.Count);
        }
    }
}
