using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 014 — <see cref="ZoneInstancingWiring"/> (AC-ZI-21 to AC-ZI-25, slot pool
    /// to lifecycle, exit-code table, T-4 broadcast, Draining rejection, tick order, stand-in logs). Real systems, fake
    /// persistence, outbox, dispatcher and sinks; ticks are advanced by hand and no database or wall clock is involved.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_Wiring_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const int IDLE_SECONDS = 60;
        private const uint IDLE_TICKS = IDLE_SECONDS * HZ;
        private const int GRACE_SECONDS = 30;
        private const uint GRACE_TICKS = GRACE_SECONDS * HZ;
        private const int LOAD_TIMEOUT_SECONDS = 10;
        private const uint LOAD_TIMEOUT_TICKS = LOAD_TIMEOUT_SECONDS * HZ;
        private const uint ACCOUNT_OFFSET = 900u;
        private const uint CONNECT_TICK = 100u;
        private const uint HEARTBEAT_TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TIMEOUT_TICKS = 60u;
        private const uint CONNECTING_TIMEOUT_TICKS = 200u;
        private const uint NEW_CLIENT = 77u;
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan TEARDOWN_DRAIN_TIMEOUT = TimeSpan.FromMilliseconds(200);

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
            public readonly List<(uint Client, ushort Type, byte[] Body, bool CapExempt)> Sent =
                new List<(uint, ushort, byte[], bool)>();

            public Action<uint, ushort> OnEnqueue;
            public uint ThrowForClient = uint.MaxValue;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (clientId == ThrowForClient)
                {
                    throw new InvalidOperationException("outbox failed for client");
                }

                OnEnqueue?.Invoke(clientId, messageTypeId);
                Sent.Add((clientId, messageTypeId, body.ToArray(), isCapExempt));
            }

            public List<(uint Client, ZoneSessionEnded Message)> Ended()
            {
                var result = new List<(uint, ZoneSessionEnded)>();
                foreach ((uint client, ushort type, byte[] body, bool _) in Sent)
                {
                    if (type == ZoneSessionEnded.MessageTypeId && ZoneSessionEndedCodec.TryReadBody(body, out ZoneSessionEnded m))
                    {
                        result.Add((client, m));
                    }
                }

                return result;
            }

            public int EndedFor(uint client)
            {
                int n = 0;
                foreach ((uint c, ZoneSessionEnded _) in Ended())
                {
                    if (c == client)
                    {
                        n++;
                    }
                }

                return n;
            }

            public List<(uint Client, ZoneFullResponse Message)> Full()
            {
                var result = new List<(uint, ZoneFullResponse)>();
                foreach ((uint client, ushort type, byte[] body, bool _) in Sent)
                {
                    if (type == ZoneFullResponse.MessageTypeId && ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse m))
                    {
                        result.Add((client, m));
                    }
                }

                return result;
            }
        }

        private sealed class FakeDispatcher : IInboundRequestDispatcher
        {
            public int ConnectionLevelCount;
            public bool Sealed;
            public readonly List<IConnectionMessageSink> Sinks = new List<IConnectionMessageSink>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
            {
                ConnectionLevelCount++;
                Sinks.Add(sink);
            }

            public void Seal() => Sealed = true;

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public void DisconnectClient(ulong transportClientId) { }
        }

        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly List<(CharacterID Id, SessionEndReason Reason)> SaveCalls = new List<(CharacterID, SessionEndReason)>();
            public int LoadCalls;

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

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                LoadCalls++;
                throw new NotSupportedException();
            }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class AcceptAllValidator : IZoneEntryTokenValidator
        {
            public bool IsValid(in ZoneEntryRequest request) => true;
        }

        private sealed class RecordingTeardownSink : IZoneTeardownSessionSink
        {
            public readonly List<CharacterID> GhostsSaved = new List<CharacterID>();
            public readonly List<uint> ConnectedExpired = new List<uint>();

            public Task<CharacterSaveResult> SaveGhostSnapshotAndExpire(CharacterID characterId, uint clientId)
            {
                GhostsSaved.Add(characterId);
                return Task.FromResult(CharacterSaveResult.Success);
            }

            public void ExpireConnectedSession(uint clientId) => ConnectedExpired.Add(clientId);
        }

        private sealed class RecordingGhosts : IZoneGhostSessionSource
        {
            public readonly List<(uint Account, uint Expiry)> Items = new List<(uint, uint)>();
            public Action OnEnumerate;

            public int Count
            {
                get
                {
                    OnEnumerate?.Invoke();
                    return Items.Count;
                }
            }

            public void Get(int index, out uint accountId, out uint sessionExpiryTick)
            {
                accountId = Items[index].Account;
                sessionExpiryTick = Items[index].Expiry;
            }
        }

        private FakeTicks _ticks;
        private FakeOutbox _outbox;
        private FakeDispatcher _dispatcher;
        private FakePersistence _persistence;
        private RecordingTeardownSink _sink;
        private RecordingGhosts _ghosts;
        private TickCompletionQueue _queue;
        private ZoneInstancingWiring _wiring;
        private readonly List<(ZoneExitCode Code, uint Tick)> _exits = new List<(ZoneExitCode, uint)>();
        private bool _teardownThrows;
        private uint _nextCharacter;
        private readonly List<uint> _transitionTicks = new List<uint>();
        private int _tracked;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        private NetworkTestObserver _observer;
#endif

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _outbox = new FakeOutbox();
            _dispatcher = new FakeDispatcher();
            _persistence = new FakePersistence();
            _sink = new RecordingTeardownSink();
            _ghosts = new RecordingGhosts();
            _queue = new TickCompletionQueue();
            _exits.Clear();
            _teardownThrows = false;
            _nextCharacter = 1000u;
            _transitionTicks.Clear();
            _tracked = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _queue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);
        }

        // ----- helpers --------------------------------------------------------------------------

        private ZoneInstancingWiring Build(Action<ZoneInstancingWiringOptions> configure = null, bool activate = true)
        {
            var options = new ZoneInstancingWiringOptions
            {
                IdleCloseSeconds = IDLE_SECONDS,
                DrainGraceSeconds = GRACE_SECONDS,
                LoadTimeoutSeconds = LOAD_TIMEOUT_SECONDS,
                TickRateHz = HZ,
                Validator = new AcceptAllValidator(),
                TeardownSessionSink = _sink,
                Ghosts = _ghosts,
            };
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            _observer = new NetworkTestObserver();
            options.Observer = _observer;
#endif
            configure?.Invoke(options);
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, _dispatcher, _outbox, _persistence, _persistence,
                new FakeApplier(), new CharacterMutationGate(), new RecordingInventory(), new FakeDisconnector(),
                code =>
                {
                    if (_teardownThrows)
                    {
                        throw new InvalidOperationException("teardown request failed");
                    }

                    _exits.Add((code, _ticks.Now));
                }, options);
            if (activate)
            {
                _ticks.Now = 0u;
                _wiring.MarkActive(Zone);
                Track(0u);
            }

            return _wiring;
        }

        private P AddPlayer(uint client, ZoneSessionState state = ZoneSessionState.Connected)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, character, new AccountID(n + 5000u), client, out _, out EntityID id));
            _wiring.Pool.SetSessionState(id, state);
            _wiring.Directory.Add(client, character);
            _wiring.TickRoster.AddPending(id);
            return new P { Entity = id, Character = character, Client = client, Account = client + ACCOUNT_OFFSET };
        }

        private void ConnectAccount(P p, uint tick)
        {
            _wiring.Sessions.EnterConnecting(p.Account, tick);
            _wiring.Sessions.CompleteAuthSuccess(p.Account, p.Character.RawValue, tick);
        }

        /// <summary>A ghost: connected at <see cref="CONNECT_TICK"/>, then timed out by the heartbeat.</summary>
        private P AddGhost(uint client)
        {
            P p = AddPlayer(client);
            ConnectAccount(p, CONNECT_TICK);
            _wiring.Sessions.EvaluateTimeouts(HEARTBEAT_TIMEOUT_TICK, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS);
            Assert.IsTrue(_wiring.Sessions.TryGetSessionState(p.Account, out SessionState state));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, state);
            _wiring.Pool.SetSessionState(p.Entity, ZoneSessionState.Disconnected_SessionActive);
            return p;
        }

        private P AddConnected(uint client, uint tick = HEARTBEAT_TIMEOUT_TICK)
        {
            P p = AddPlayer(client);
            ConnectAccount(p, tick);
            return p;
        }

        private void RunTick(uint tick)
        {
            _ticks.Now = tick;
            _wiring.Run(tick, 0f);
            Track(tick);
        }

        private void DrainQueue(uint tick)
        {
            _ticks.Now = tick;
            _queue.Drain(tick);
            Track(tick);
        }

        /// <summary>Stamps every lifecycle transition reported since the last call with the tick of the action that caused it.</summary>
        private void Track(uint tick)
        {
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            while (_tracked < _observer.ZoneStateTransitionedCalls.Count)
            {
                _transitionTicks.Add(tick);
                _tracked++;
            }
#endif
        }

        /// <summary>
        /// Asserts the exact lifecycle transition sequence with the tick of each. The observer reports states, not the
        /// ZoneCloseTransition value, so Idle / Drained / Emergency are told apart by path (Draining or not) and by
        /// the tick and occupancy asserted by the caller.
        /// </summary>
        private void AssertTransitions(params (ZoneState From, ZoneState To, uint Tick)[] expected)
        {
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            var actual = _observer.ZoneStateTransitionedCalls;
            Assert.AreEqual(expected.Length, actual.Count, "transition count");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].From, actual[i].fromState, "from #" + i);
                Assert.AreEqual(expected[i].To, actual[i].toState, "to #" + i);
                Assert.AreEqual(expected[i].Tick, _transitionTicks[i], "tick #" + i);
            }
#else
            Assert.Inconclusive("INetworkTestObserver is compiled out");
#endif
        }

        private static List<string> CaptureLogs(Action act)
        {
            var messages = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type) => messages.Add(condition);
            Application.logMessageReceived += OnLog;
            try
            {
                act();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            return messages;
        }

        private static int CountContaining(List<string> messages, string marker)
        {
            int n = 0;
            foreach (string m in messages)
            {
                if (m.Contains(marker))
                {
                    n++;
                }
            }

            return n;
        }

        private ZoneEntryRequest Request(uint client)
        {
            uint n = _nextCharacter++;
            return new ZoneEntryRequest(client, new AccountID(n + 5000u), new CharacterID(n), Zone, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES]);
        }

        private void AssertExit(ZoneExitCode code, uint tick)
        {
            Assert.AreEqual(1, _exits.Count, "exactly one teardown request");
            Assert.AreEqual(code, _exits[0].Code);
            Assert.AreEqual(tick, _exits[0].Tick);
        }

        // ----- construction and slot pool -> lifecycle --------------------------------------------

        [Test]
        public void test_wiring_construction_registers_ack_and_snapshot_sinks_unsealed()
        {
            Build();

            Assert.AreEqual(2, _dispatcher.ConnectionLevelCount);
            Assert.IsFalse(_dispatcher.Sealed, "the composition seals, not the wiring");
        }

        [Test]
        public void test_wiring_constructor_rejects_null_teardown_callback()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneInstancingWiring(Zone, _ticks, _queue, _dispatcher, _outbox,
                _persistence, _persistence, new FakeApplier(), new CharacterMutationGate(), new RecordingInventory(),
                new FakeDisconnector(), null));
        }

        [Test]
        public void test_wiring_mark_active_moves_lifecycle_to_active()
        {
            Build(activate: false);
            Assert.AreEqual(ZoneState.Loading, _wiring.Lifecycle.State);

            _ticks.Now = 5u;
            _wiring.MarkActive(Zone);

            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State);
        }

        [Test]
        public void test_wiring_slot_occupancy_is_forwarded_to_the_lifecycle()
        {
            Build();

            AddPlayer(1u);
            AddPlayer(2u);

            Assert.AreEqual(2, _wiring.Lifecycle.OccupiedPlayerSlots);
        }

        // ----- AC-ZI-21 ---------------------------------------------------------------------------

        [Test]
        public void test_ac_zi_21_idle_zone_closes_clean_after_idle_ticks_from_last_release()
        {
            Build();
            P a = AddPlayer(1u);
            _ticks.Now = 100u;

            Assert.IsTrue(_wiring.PlayerExit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful));
            RunTick(100u + IDLE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State);
            RunTick(100u + IDLE_TICKS);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Clean, 100u + IDLE_TICKS);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Closed, 100u + IDLE_TICKS));
        }

        [Test]
        public void test_ac_zi_21_slot_allocated_one_tick_before_the_deadline_cancels_the_close()
        {
            Build();
            P a = AddPlayer(1u);
            _ticks.Now = 100u;
            _wiring.PlayerExit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful);

            _ticks.Now = 100u + IDLE_TICKS - 1u;
            AddPlayer(2u);
            RunTick(100u + IDLE_TICKS);

            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State);
            Assert.AreEqual(0, _exits.Count);
        }

        // ----- AC-ZI-22 ---------------------------------------------------------------------------

        [Test]
        public void test_ac_zi_22_quit_notifies_connected_only_rejects_handshake_and_closes_drained_on_last_release()
        {
            Build();
            P ghost = AddGhost(1u);
            P a = AddConnected(2u);
            P b = AddConnected(3u);
            Assert.AreEqual(3, _wiring.Pool.OccupiedPlayerCount);

            _wiring.RequestQuit();
            RunTick(400u);

            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            Assert.AreEqual(2, _outbox.Ended().Count, "ZoneSessionEnded to the two Connected clients only");
            Assert.AreEqual(1, _outbox.EndedFor(a.Client));
            Assert.AreEqual(1, _outbox.EndedFor(b.Client));
            Assert.AreEqual(0, _outbox.EndedFor(ghost.Client));
            foreach ((uint _, ZoneSessionEnded message) in _outbox.Ended())
            {
                Assert.AreEqual(Zone, message.ZoneId);
                Assert.AreEqual(DisconnectReason.ZoneClosed, message.Reason);
                Assert.AreEqual(GRACE_SECONDS, message.GracePeriodSeconds);
            }

            _wiring.Admission.BeginEntry(Request(NEW_CLIENT));
            Assert.AreEqual(1, _outbox.Full().Count);
            Assert.AreEqual(NEW_CLIENT, _outbox.Full()[0].Client);
            Assert.AreEqual(JoinRejectedReason.ZoneDoesNotExist, _outbox.Full()[0].Message.Reason);
            Assert.AreEqual(3, _wiring.Pool.OccupiedPlayerCount, "no slot for the new handshake");

            Assert.IsTrue(_wiring.SessionEnds.BeginCleanLogout(a.Account, r => { }));
            Assert.IsTrue(_wiring.SessionEnds.BeginCleanLogout(b.Account, r => { }));
            DrainQueue(410u);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            Assert.AreEqual(0, _exits.Count);

            Assert.IsTrue(_wiring.SessionEnds.EvaluateSessionTTL(ghost.Account, 420u, 420u));
            DrainQueue(420u);

            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Clean, 420u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 400u),
                (ZoneState.Draining, ZoneState.Closed, 420u));
        }

        // ----- AC-ZI-23 ---------------------------------------------------------------------------

        [Test]
        public void test_ac_zi_23_grace_expiry_closes_drained_and_close_steps_end_connected_and_ghost_once()
        {
            Build();
            P ghost = AddGhost(1u);
            P connected = AddConnected(2u);
            _ghosts.Items.Add((ghost.Account, 1000u + 250u * (uint)HZ));
            _wiring.RequestQuit();
            RunTick(1000u);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);

            RunTick(1000u + GRACE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            Assert.AreEqual(2, _wiring.Pool.OccupiedPlayerCount, "ghost and connected slots still held one tick before grace expiry");
            Assert.AreEqual(0, _sink.ConnectedExpired.Count, "no session expired before the close steps run");
            Assert.AreEqual(0, _persistence.SaveCalls.Count);
            RunTick(1000u + GRACE_TICKS);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(2, _wiring.Pool.OccupiedPlayerCount, "closed by the grace deadline, not by occupancy");
            AssertExit(ZoneExitCode.Clean, 1000u + GRACE_TICKS);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 1000u),
                (ZoneState.Draining, ZoneState.Closed, 1000u + GRACE_TICKS));

            _wiring.RunCloseSteps();
            _queue.DrainOnShutdown(DRAIN_TIMEOUT);

            CollectionAssert.AreEqual(new[] { ghost.Character }, _sink.GhostsSaved, "ghost snapshot saved exactly once");
            Assert.AreEqual(1, _persistence.SaveCalls.Count, "no SaveSession for the ghost");
            Assert.AreEqual(connected.Character, _persistence.SaveCalls[0].Id);
            Assert.AreEqual(SessionEndReason.ZoneClosed, _persistence.SaveCalls[0].Reason);
            CollectionAssert.AreEqual(new[] { connected.Client }, _sink.ConnectedExpired, "Connected session expired once");
            Assert.AreEqual(1, _outbox.EndedFor(connected.Client), "no second ZoneSessionEnded");
            Assert.AreEqual(0, _outbox.EndedFor(ghost.Client));
        }

        // ----- AC-ZI-24 ---------------------------------------------------------------------------

        [Test]
        public void test_ac_zi_24_second_quit_while_draining_closes_clean_and_saves_every_slot()
        {
            Build();
            P a = AddConnected(2u);
            P b = AddConnected(3u);
            _wiring.RequestQuit();
            RunTick(100u);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);

            _wiring.RequestQuit();
            RunTick(110u);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(2, _wiring.Pool.OccupiedPlayerCount, "Emergency: slots still occupied and grace not expired");
            AssertExit(ZoneExitCode.Clean, 110u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 100u),
                (ZoneState.Draining, ZoneState.Closed, 110u));

            _wiring.RunCloseSteps();
            _queue.DrainOnShutdown(DRAIN_TIMEOUT);

            Assert.AreEqual(2, _persistence.SaveCalls.Count, "every occupied slot saved");
            foreach ((CharacterID _, SessionEndReason reason) in _persistence.SaveCalls)
            {
                Assert.AreEqual(SessionEndReason.ZoneClosed, reason);
            }

            CollectionAssert.AreEquivalent(new[] { a.Character, b.Character }, new[] { _persistence.SaveCalls[0].Id, _persistence.SaveCalls[1].Id });
            Assert.AreEqual(1, _outbox.EndedFor(a.Client), "no second ZoneSessionEnded");
            Assert.AreEqual(1, _outbox.EndedFor(b.Client));
        }

        [Test]
        public void test_ac_zi_24_two_quits_before_one_tick_apply_in_order_and_close_that_tick()
        {
            Build();
            AddConnected(2u);
            _wiring.RequestQuit();
            _wiring.RequestQuit();
            Assert.AreEqual(2, _wiring.PendingQuitCount);

            RunTick(100u);

            Assert.AreEqual(0, _wiring.PendingQuitCount);
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount, "Emergency, not Drained: the slot is still held");
            AssertExit(ZoneExitCode.Clean, 100u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 100u),
                (ZoneState.Draining, ZoneState.Closed, 100u));
        }

        // ----- AC-ZI-25 ---------------------------------------------------------------------------

        [Test]
        public void test_ac_zi_25_quit_with_no_slot_closes_on_that_tick_without_a_notice()
        {
            Build();

            _wiring.RequestQuit();
            RunTick(50u);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Clean, 50u);
            Assert.AreEqual(0, _outbox.Ended().Count);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 50u),
                (ZoneState.Draining, ZoneState.Closed, 50u));
        }

        // ----- Transition log (Story 045 rerun evidence) -----------------------------------------

        [Test]
        public void test_wiring_log_transitions_on_logs_active_draining_and_closed_with_ticks()
        {
            // Arrange
            LogAssert.Expect(LogType.Log, @"[ZoneInstancingWiring] ZoneID(7) Active at tick 0");
            LogAssert.Expect(LogType.Log, @"[ZoneInstancingWiring] ZoneID(7) Draining at tick 50");
            LogAssert.Expect(LogType.Log, @"[ZoneInstancingWiring] ZoneID(7) Closed(Drained) at tick 50");
            Build(o => o.LogTransitions = true);

            // Act
            _wiring.RequestQuit();
            RunTick(50u);

            // Assert
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Clean, 50u);
        }

        [Test]
        public void test_wiring_log_transitions_off_by_default_logs_no_transition()
        {
            // Arrange
            int transitionLines = 0;
            Application.LogCallback count = (message, _, __) =>
            {
                if (message.StartsWith("[ZoneInstancingWiring] ZoneID(", StringComparison.Ordinal))
                {
                    transitionLines++;
                }
            };
            Application.logMessageReceived += count;
            try
            {
                Build();

                // Act
                _wiring.RequestQuit();
                RunTick(50u);
            }
            finally
            {
                Application.logMessageReceived -= count;
            }

            // Assert
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(0, transitionLines);
        }

        // ----- Exhaustion and exit-code table ----------------------------------------------------

        [Test]
        public void test_entity_id_exhaustion_closes_emergency_and_requests_fatal_teardown()
        {
            Build(o => o.FirstEntityId = uint.MaxValue);
            AddPlayer(1u);
            _ticks.Now = 42u;
            LogAssert.Expect(LogType.Error, new Regex("EntityIDCounterExhausted"));

            bool allocated = _wiring.Pool.TryAllocatePlayer(Zone, new CharacterID(5000u), new AccountID(5001u), 2u, out _, out _);

            Track(42u);

            Assert.IsFalse(allocated);
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Fatal, 42u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Closed, 42u));
        }

        [Test]
        public void test_request_fatal_closes_emergency_with_fatal_exit_code()
        {
            Build();

            _ticks.Now = 10u;
            _wiring.RequestFatal(10u);
            Track(10u);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Fatal, 10u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Closed, 10u));
        }

        [Test]
        public void test_request_fatal_while_draining_closes_with_fatal_exit_code()
        {
            Build();
            AddConnected(2u);
            _wiring.RequestQuit();
            RunTick(10u);

            _ticks.Now = 11u;
            _wiring.RequestFatal(11u);
            Track(11u);

            AssertExit(ZoneExitCode.Fatal, 11u);
            AssertTransitions((ZoneState.Loading, ZoneState.Active, 0u), (ZoneState.Active, ZoneState.Draining, 10u),
                (ZoneState.Draining, ZoneState.Closed, 11u));
        }

        [Test]
        public void test_quit_while_loading_closes_emergency_with_clean_exit_code()
        {
            Build(activate: false);

            _ticks.Now = 5u;
            _wiring.RequestQuitNow(5u);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            AssertExit(ZoneExitCode.Clean, 5u);
        }

        [Test]
        public void test_load_timeout_closes_load_failed_without_a_teardown_request()
        {
            Build(activate: false);

            _wiring.OnLoadingTick(LOAD_TIMEOUT_TICKS - 1u);
            Assert.AreEqual(ZoneState.Loading, _wiring.Lifecycle.State);
            _wiring.OnLoadingTick(LOAD_TIMEOUT_TICKS);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(0, _exits.Count);
        }

        [Test]
        public void test_invalid_bounds_closes_load_failed_without_a_teardown_request()
        {
            Build(activate: false);

            _wiring.MarkClosedInvalidBounds(Zone);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            Assert.AreEqual(0, _exits.Count);
        }

        [Test]
        public void test_close_listener_never_throws_when_the_teardown_request_throws()
        {
            Build();
            _teardownThrows = true;
            LogAssert.Expect(LogType.Error, new Regex(@"\[ZoneInstancingWiring\].*OnClosed"));

            Assert.DoesNotThrow(() => _wiring.RequestFatal(1u));

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
        }

        // ----- T-4 broadcast and drain log ---------------------------------------------------------

        [Test]
        public void test_drain_notice_log_records_connected_clients_only()
        {
            Build();
            P ghost = AddGhost(1u);
            P connected = AddConnected(2u);

            _wiring.RequestQuit();
            RunTick(400u);

            Assert.IsTrue(_wiring.WasNotifiedAtDrain(connected.Client));
            Assert.IsFalse(_wiring.WasNotifiedAtDrain(ghost.Client));
            Assert.IsFalse(_wiring.WasNotifiedAtDrain(NEW_CLIENT));
        }

        [Test]
        public void test_drain_notice_is_cap_exempt()
        {
            Build();
            AddConnected(2u);

            _wiring.RequestQuit();
            RunTick(400u);

            Assert.AreEqual(1, _outbox.Sent.FindAll(s => s.Type == ZoneSessionEnded.MessageTypeId && s.CapExempt).Count);
        }

        [Test]
        public void test_new_handshake_while_draining_gets_zone_does_not_exist_and_starts_no_load()
        {
            Build();
            AddConnected(2u);
            _wiring.RequestQuit();
            RunTick(400u);

            _wiring.Admission.BeginEntry(Request(NEW_CLIENT));

            Assert.AreEqual(1, _outbox.Full().Count);
            Assert.AreEqual(JoinRejectedReason.ZoneDoesNotExist, _outbox.Full()[0].Message.Reason);
            Assert.AreEqual(Zone, _outbox.Full()[0].Message.ZoneId);
            Assert.AreEqual(0, _persistence.LoadCalls);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
        }

        // ----- Tick order --------------------------------------------------------------------------

        [Test]
        public void test_tick_order_pending_quit_is_applied_before_the_ghost_ttl_step()
        {
            Build();
            AddConnected(2u);
            ZoneState atTtl = ZoneState.Closed;
            _ghosts.OnEnumerate = () => atTtl = _wiring.Lifecycle.State;

            _wiring.RequestQuit();
            RunTick(10u);

            Assert.AreEqual(ZoneState.Draining, atTtl);
        }

        [Test]
        public void test_tick_order_roster_is_activated_before_the_ghost_ttl_step()
        {
            Build();
            _ticks.Now = 5u;
            P p = AddPlayer(2u);
            bool activeAtTtl = false;
            _ghosts.OnEnumerate = () => activeAtTtl = _wiring.TickRoster.IsInTickLoop(p.Entity);

            RunTick(6u);

            Assert.IsTrue(activeAtTtl);
        }

        [Test]
        public void test_tick_order_reclamation_runs_before_the_ghost_ttl_step()
        {
            Build();
            _ticks.Now = 5u;
            P p = AddPlayer(2u);
            _wiring.Reclamation.RegisterEntering(p.Client, p.Entity);
            int enteringAtTtl = -1;
            _ghosts.OnEnumerate = () => enteringAtTtl = _wiring.Reclamation.EnteringCount;

            RunTick(5u + _wiring.Reclamation.StallTicks);

            Assert.AreEqual(0, enteringAtTtl, "the stalled slot was reclaimed before the TTL step");
        }

        [Test]
        public void test_tick_order_lifecycle_tick_runs_after_the_ghost_ttl_step()
        {
            Build();
            ZoneState atTtl = ZoneState.Closed;
            _ghosts.OnEnumerate = () => atTtl = _wiring.Lifecycle.State;

            RunTick(IDLE_TICKS);

            Assert.AreEqual(ZoneState.Active, atTtl);
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
        }

        [Test]
        public void test_tick_step_evaluates_the_session_ttl_of_listed_ghosts()
        {
            Build();
            P ghost = AddGhost(1u);
            _ghosts.Items.Add((ghost.Account, 500u));

            RunTick(499u);
            Assert.AreEqual(0, _persistence.SaveCalls.Count);
            RunTick(500u);

            Assert.AreEqual(1, _persistence.SaveCalls.Count);
            Assert.AreEqual(ghost.Character, _persistence.SaveCalls[0].Id);
            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _persistence.SaveCalls[0].Reason);
        }

        // ----- Stand-ins ---------------------------------------------------------------------------

        [Test]
        public void test_default_ghost_source_and_entity_state_source_each_log_once()
        {
            int ghostLogs = 0;
            int stateLogs = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition.Contains("ghost session TTL enumeration not implemented"))
                {
                    ghostLogs++;
                }

                if (condition.Contains("zone entity state source not implemented"))
                {
                    stateLogs++;
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                Build(o => o.Ghosts = null);
                RunTick(1u);
                RunTick(2u);
                var source = new LoggingZoneEntityStateSource();
                Assert.IsFalse(source.TryGetPlayerState(new EntityID(1u), out _));
                Assert.IsFalse(source.TryGetMobState(new EntityID(2u), out _));
                Assert.IsFalse(source.TryGetPlayerState(new EntityID(3u), out _));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.AreEqual(1, ghostLogs);
            Assert.AreEqual(1, stateLogs);
        }

        // ----- Drain notice isolation ---------------------------------------------------------------

        [Test]
        public void test_drain_notice_failure_for_one_client_does_not_stop_the_next_client()
        {
            Build();
            P first = AddConnected(2u);
            P second = AddConnected(3u);
            _outbox.ThrowForClient = first.Client;
            LogAssert.Expect(LogType.Error, new Regex(@"\[ZoneInstancingWiring\] drain notice for .* failed"));

            _wiring.RequestQuit();
            RunTick(400u);

            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            Assert.AreEqual(0, _outbox.EndedFor(first.Client));
            Assert.AreEqual(1, _outbox.EndedFor(second.Client));
            Assert.IsTrue(_wiring.WasNotifiedAtDrain(second.Client));
            Assert.IsFalse(_wiring.WasNotifiedAtDrain(first.Client), "a failed enqueue is not recorded as notified");
        }

        // ----- Full tick order -----------------------------------------------------------------------

        [Test]
        public void test_tick_order_full_sequence_quit_roster_reclamation_ghosts_lifecycle_snapshots()
        {
            Build();
            _ticks.Now = 5u;
            P a = AddConnected(2u, 5u);
            P entering = AddPlayer(3u);
            _wiring.Reclamation.RegisterEntering(entering.Client, entering.Entity);
            var order = new List<string>();
            _ghosts.OnEnumerate = () => order.Add("ghost:" + _wiring.Lifecycle.State + ":" + _wiring.TickRoster.IsInTickLoop(a.Entity)
                + ":" + _wiring.Reclamation.EnteringCount);
            // The snapshot requester's own replies only (fragments while Draining, ZoneFullResponse once Closed),
            // once per serve; the T-4 drain notice and reclamation's PlayerLeftZone for the stalled player are not serves.
            _outbox.OnEnqueue = (client, type) =>
            {
                if (client == a.Client && type != ZoneSessionEnded.MessageTypeId && type != PlayerLeftZone.MessageTypeId
                    && (order.Count == 0 || !order[order.Count - 1].StartsWith("snapshot:", StringComparison.Ordinal)))
                {
                    order.Add("snapshot:" + _wiring.Lifecycle.State);
                }
            };
            ZoneSnapshotRequestService sink = SnapshotSink();
            uint tickA = 5u + _wiring.Reclamation.StallTicks;
            uint tickB = tickA + GRACE_TICKS;

            _wiring.RequestQuit();
            SendSnapshotRequest(sink, a);
            RunTick(tickA);
            SendSnapshotRequest(sink, a);
            RunTick(tickB);

            CollectionAssert.AreEqual(new[]
            {
                "ghost:Draining:True:0",
                "snapshot:Draining",
                "ghost:Draining:True:0",
                "snapshot:Closed",
            }, order, "pending quit, roster, reclamation before ghosts; lifecycle tick between ghosts and the snapshot serve");
        }

        private ZoneSnapshotRequestService SnapshotSink()
        {
            foreach (IConnectionMessageSink s in _dispatcher.Sinks)
            {
                if (s is ZoneSnapshotRequestService service)
                {
                    return service;
                }
            }

            Assert.Fail("snapshot request sink not registered");
            return null;
        }

        private static void SendSnapshotRequest(ZoneSnapshotRequestService sink, P p)
        {
            byte[] body = new byte[ZoneSnapshotRequest.BodySize];
            ZoneSnapshotRequestCodec.WriteBody(body, new ZoneSnapshotRequest(Zone, 0u));
            sink.OnConnectionMessage(p.Client, ZoneSnapshotRequest.MessageTypeId, p.Entity, body);
        }

        // ----- Tuning knobs --------------------------------------------------------------------------

        [Test]
        public void test_options_defaults_equal_the_owner_constants()
        {
            var options = new ZoneInstancingWiringOptions();

            Assert.AreEqual(ZoneLifecycle.ZONE_IDLE_CLOSE_SECONDS, options.IdleCloseSeconds);
            Assert.AreEqual(ZoneLifecycle.ZONE_DRAIN_GRACE_SECONDS, options.DrainGraceSeconds);
            Assert.AreEqual(ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS, options.LoadTimeoutSeconds);
            Assert.AreEqual(ServerTickLoop.TICK_RATE_HZ, options.TickRateHz);
            Assert.AreEqual(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, options.MaxPlayers);
            Assert.AreEqual(ZoneSlotPool.MAX_MOBS_PER_ZONE, options.MaxMobs);
        }

        // ----- No allocation per tick ---------------------------------------------------------------

        [Test]
        public void test_run_allocates_nothing_per_tick_with_an_empty_ghost_source()
        {
            Build();
            AddConnected(2u);

            long delta = MeasureRunAllocation(400u);

            Assert.AreEqual(0L, delta, "bytes allocated over 200 Run calls");
        }

        [Test]
        public void test_run_allocates_nothing_per_tick_with_a_connected_player_and_a_listed_ghost()
        {
            Build();
            P ghost = AddGhost(1u);
            AddConnected(2u);
            _ghosts.Items.Add((ghost.Account, 1000000u));

            long delta = MeasureRunAllocation(400u);

            Assert.AreEqual(0L, delta, "bytes allocated over 200 Run calls");
            Assert.AreEqual(0, _persistence.SaveCalls.Count, "the listed ghost did not expire");
        }

        private long MeasureRunAllocation(uint startTick)
        {
            uint tick = startTick;
            for (int i = 0; i < 50; i++)
            {
                _ticks.Now = tick;
                _wiring.Run(tick, 0f);
                tick++;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                _ticks.Now = tick;
                _wiring.Run(tick, 0f);
                tick++;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        // ----- Stand-in logs -------------------------------------------------------------------------

        [Test]
        public void test_default_stand_ins_each_log_once_over_a_driven_lifecycle()
        {
            _ticks.Now = 0u;
            List<string> logs = CaptureLogs(() =>
            {
                Build(o =>
                {
                    o.Validator = null;
                    o.TeardownSessionSink = null;
                    o.Ghosts = null;
                });
                _ticks.Now = 5u;
                P a = AddConnected(2u, 5u);
                P entering = AddPlayer(3u);
                _wiring.Reclamation.RegisterEntering(entering.Client, entering.Entity);
                _wiring.Admission.BeginEntry(Request(NEW_CLIENT));
                SendSnapshotRequest(SnapshotSink(), a);
                RunTick(5u + _wiring.Reclamation.StallTicks);
                RunTick(5u + _wiring.Reclamation.StallTicks + 1u);
                _wiring.RequestQuit();
                RunTick(5u + _wiring.Reclamation.StallTicks + 2u);
                _wiring.RequestQuit();
                RunTick(5u + _wiring.Reclamation.StallTicks + 3u);
                _wiring.RunCloseSteps();
                _queue.DrainOnShutdown(DRAIN_TIMEOUT);
            });

            Assert.AreEqual(1, CountContaining(logs, "ghost session TTL enumeration not implemented"), "ghost source");
            Assert.AreEqual(1, CountContaining(logs, RejectingZoneEntryTokenValidator.NOT_IMPLEMENTED_MESSAGE), "token validator");
            Assert.AreEqual(1, CountContaining(logs, LoggingZoneEntryReclaimSessionSink.NOT_IMPLEMENTED_MESSAGE), "reclaim session sink");
            Assert.AreEqual(1, CountContaining(logs, "zone entity state source not implemented"), "entity state source");
            Assert.AreEqual(1, CountContaining(logs, NoPartyZonePresence.NOT_IMPLEMENTED_MESSAGE), "party");
            Assert.AreEqual(1, CountContaining(logs, "mob spawn timers not implemented"), "mob spawn timers");
            Assert.AreEqual(1, CountContaining(logs, "teardown session sink (TD-079) not implemented"), "teardown session sink");
            // LoggingZoneEntryResponder is unreachable here: SessionReady needs a completed entry and the fake persistence cannot load a character.
            Assert.AreEqual(0, CountContaining(logs, LoggingZoneEntryResponder.NOT_IMPLEMENTED_MESSAGE), "responder is unreached");
        }

        [Test]
        public void test_stand_in_inventory_logs_once_on_unregister_and_throws_on_other_members()
        {
            var inventory = new StandInZoneInventoryService();
            var id = new CharacterID(1u);

            List<string> logs = CaptureLogs(() =>
            {
                inventory.UnregisterCharacter(id);
                inventory.UnregisterCharacter(id);
            });

            Assert.AreEqual(1, CountContaining(logs, "inventory service not implemented"));
            Assert.Throws<NotSupportedException>(() => inventory.RegisterCharacter(id));
            Assert.Throws<NotSupportedException>(() => inventory.IsFull(id));
            Assert.Throws<NotSupportedException>(() => inventory.FilledSlots(id));
            Assert.Throws<NotSupportedException>(() => inventory.GetSlot(id, 0));
        }
    }
}
