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
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 013 — <see cref="ZonePlayerExit.ReleasePlayer"/> (CR-ZI-14 exit,
    /// AC-ZI-13, EC-ZI-11). Real pool, directory, roster, <see cref="ZoneEntryReclamation"/>, <see cref="ZoneLifecycle"/>,
    /// <see cref="ConnectionStateMachine"/>, <see cref="SessionEndCoordinator"/>, <see cref="ZoneCloseSteps"/> and
    /// <see cref="TickCompletionQueue"/>; fake persistence (controllable <see cref="TaskCompletionSource{TResult}"/> saves),
    /// party, outbox and stand-ins. No database.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_PlayerExit_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint PARTY_RAW = 77u;
        private const uint WATCHER_CLIENT = 99u;
        private const uint WATCHER2_CLIENT = 98u;
        private const uint ACCOUNT_OFFSET = 900u;
        private const uint UNKNOWN_CHARACTER_RAW = 424242u;
        private const uint CONNECT_TICK = 100u;
        private const uint TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TIMEOUT_TICKS = 60u;
        private const uint CONNECTING_TIMEOUT_TICKS = 200u;
        private const uint EXPIRY_TICK = 500u;
        private const uint QUIT_TICK = CONNECT_TICK;
        private const uint DRAINED_RELEASE_TICK = 150u;
        private const int HZ = 20;
        private const int IDLE_SECONDS = 60;
        private const uint IDLE_TICKS = IDLE_SECONDS * HZ;
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
            public uint Now = CONNECT_TICK;

            public uint ServerTickNumber => Now;
        }

        private sealed class ForwardingPoolListener : IZoneSlotPoolListener
        {
            public ZoneLifecycle Lifecycle;

            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) => Lifecycle?.OnOccupiedPlayerSlotsChanged(occupied, serverTick);

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeLifecycleListener : IZoneLifecycleListener
        {
            public readonly List<(ZoneCloseTransition Transition, uint Tick)> Closed = new List<(ZoneCloseTransition, uint)>();

            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick) => Closed.Add((transition, serverTick));
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public void DisconnectClient(ulong transportClientId) { }
        }

        private sealed class NoopReclaimSink : IZoneEntryReclaimSessionSink
        {
            public void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned) { }
        }

        /// <summary>Records instead of asserting: <c>LeaveZone</c> swallows exceptions thrown inside the outbox.</summary>
        private sealed class FakeOutbox : IClientMessageOutbox
        {
            public readonly List<(uint Client, PlayerLeftZone Message)> Left = new List<(uint, PlayerLeftZone)>();
            public int DecodeFailures;
            public int OtherCount;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (messageTypeId != PlayerLeftZone.MessageTypeId)
                {
                    OtherCount++;
                    return;
                }

                if (!PlayerLeftZoneCodec.TryReadBody(body, out PlayerLeftZone message))
                {
                    DecodeFailures++;
                    return;
                }

                Left.Add((clientId, message));
            }

            public int LeftFor(uint client, EntityID leaving)
            {
                int n = 0;
                foreach ((uint c, PlayerLeftZone m) in Left)
                {
                    if (c == client && m.EntityId == leaving)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>> Sources =
                new Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>>();
            public readonly List<(CharacterID Id, SessionEndReason Reason)> Calls = new List<(CharacterID, SessionEndReason)>();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => Record(id, reason);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => Record(id, reason);

            private Task<CharacterSaveResult> Record(CharacterID id, SessionEndReason reason)
            {
                Calls.Add((id, reason));
                var source = new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                Sources[id] = source;
                return source.Task;
            }

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            private readonly List<string> _events;

            public readonly HashSet<CharacterID> InParty = new HashSet<CharacterID>();
            public readonly List<(PartyID Party, EntityID Entity)> Exits = new List<(PartyID, EntityID)>();

            public FakeParty(List<string> events)
            {
                _events = events;
            }

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                bool inParty = InParty.Contains(character);
                party = inParty ? new PartyID(PARTY_RAW) : PartyID.Uninitialized;
                return inParty;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId)
            {
                _events.Add(ExitEvent(entityId));
                Exits.Add((party, entityId));
            }

            public int ExitsFor(EntityID entity)
            {
                int n = 0;
                foreach ((PartyID _, EntityID e) in Exits)
                {
                    if (e == entity)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private sealed class NoopTimers : IZoneMobSpawnTimers
        {
            public void CancelAllSpawnTimers(ZoneID zone) { }
        }

        private sealed class NoopSink : IZoneTeardownSessionSink
        {
            public Task<CharacterSaveResult> SaveGhostSnapshotAndExpire(CharacterID characterId, uint clientId) =>
                Task.FromResult(CharacterSaveResult.Success);

            public void ExpireConnectedSession(uint clientId) { }
        }

        private sealed class NoDrainLog : IZoneDrainNoticeLog
        {
            public bool WasNotifiedAtDrain(uint clientId) => false;
        }

        private static string ExitEvent(EntityID entity) => "exit:" + entity;

        private static string SaveEvent(CharacterID character) => "save-complete:" + character.RawValue;

        private List<string> _events;
        private FakeTicks _ticks;
        private ForwardingPoolListener _poolListener;
        private FakeLifecycleListener _lifecycleListener;
        private FakeOutbox _outbox;
        private FakeParty _party;
        private FakePersistence _endPersistence;
        private FakePersistence _closePersistence;
        private ZoneSlotPool _pool;
        private ZoneConnectionCharacterDirectory _directory;
        private ZoneTickRoster _roster;
        private ZoneEntryReclamation _reclamation;
        private ZonePlayerExit _exit;
        private TickCompletionQueue _endQueue;
        private TickCompletionQueue _closeQueue;
        private ConnectionStateMachine _sm;
        private SessionEndCoordinator _ends;
        private uint _nextCharacter;
        private uint _endTick;
        private readonly List<bool> _releaseResults = new List<bool>();

        [SetUp]
        public void SetUp()
        {
            _events = new List<string>();
            _ticks = new FakeTicks();
            _poolListener = new ForwardingPoolListener();
            _lifecycleListener = new FakeLifecycleListener();
            _outbox = new FakeOutbox();
            _party = new FakeParty(_events);
            _endPersistence = new FakePersistence();
            _closePersistence = new FakePersistence();
            _pool = new ZoneSlotPool(Zone, _ticks, _poolListener);
            _directory = new ZoneConnectionCharacterDirectory();
            _roster = new ZoneTickRoster(_pool, _ticks);
            _reclamation = new ZoneEntryReclamation(Zone, _pool, _directory, _roster, _party, _outbox, new FakeDisconnector(), new NoopReclaimSink(), _ticks);
            _exit = new ZonePlayerExit(_pool, _reclamation);
            _endQueue = new TickCompletionQueue();
            _closeQueue = new TickCompletionQueue();
            _sm = new ConnectionStateMachine();
            _nextCharacter = 1000u;
            _endTick = 0u;
            _releaseResults.Clear();
            _ends = new SessionEndCoordinator(_endPersistence, _endPersistence, _endQueue, new CharacterMutationGate(), _sm,
                new RecordingInventory(), entity => { }, characterId => { },
                (characterId, type) => _releaseResults.Add(_exit.ReleasePlayer(characterId, type)));
        }

        [TearDown]
        public void TearDown()
        {
            // No continuation outlives a test: finish every pending save, then deliver what is left.
            foreach (TaskCompletionSource<CharacterSaveResult> source in _endPersistence.Sources.Values)
            {
                source.TrySetResult(CharacterSaveResult.Success);
            }

            foreach (TaskCompletionSource<CharacterSaveResult> source in _closePersistence.Sources.Values)
            {
                source.TrySetResult(CharacterSaveResult.Success);
            }

            _endQueue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);
            _closeQueue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);

            // Checked last so a failure still leaves no save pending.
            Assert.AreEqual(0, _outbox.DecodeFailures, "an outbox message failed to decode");
        }

        // ----- helpers --------------------------------------------------------------------------

        private P AddPlayer(uint client, bool inParty = true)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, character, new AccountID(n + 5000u), client, out _, out EntityID id));
            _pool.SetSessionState(id, ZoneSessionState.Connected);
            _directory.Add(client, character);
            _roster.AddPending(id);
            if (inParty)
            {
                _party.InParty.Add(character);
            }

            return new P { Entity = id, Character = character, Client = client, Account = client + ACCOUNT_OFFSET };
        }

        private void ConnectAccount(P p)
        {
            _sm.EnterConnecting(p.Account, CONNECT_TICK);
            _sm.CompleteAuthSuccess(p.Account, p.Character.RawValue, CONNECT_TICK);
            AssertSessionState(p, SessionState.Connected);
        }

        private void DisconnectByHeartbeat(P p)
        {
            _sm.EvaluateTimeouts(TIMEOUT_TICK, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS);
            AssertSessionState(p, SessionState.Disconnected_SessionActive);
        }

        private void AssertSessionState(P p, SessionState expected)
        {
            Assert.IsTrue(_sm.TryGetSessionState(p.Account, out SessionState actual));
            Assert.AreEqual(expected, actual);
        }

        /// <summary>The slot, the directory entry and the roster entry of the player are gone.</summary>
        private void AssertFullyReleased(P p)
        {
            Assert.IsFalse(_pool.TryGetPlayer(p.Entity, out _), "slot");
            Assert.IsFalse(_directory.TryGetCharacterId(p.Client, out _), "directory entry");
            Assert.IsFalse(_roster.IsInTickLoop(p.Entity), "roster (active)");
            Assert.AreEqual(_pool.OccupiedPlayerCount, _roster.PendingCount + _roster.ActiveCount,
                "every remaining occupied slot is on the roster and the released one is not");
        }

        private void AssertNothingReleasedYet(P p)
        {
            Assert.AreEqual(0, _party.ExitsFor(p.Entity), "no NotifyZoneExit");
            Assert.AreEqual(0, _outbox.Left.Count, "no PlayerLeftZone");
            Assert.AreEqual(0, _releaseResults.Count, "ReleasePlayer not called");
            Assert.IsTrue(_pool.TryGetPlayer(p.Entity, out _), "slot kept");
        }

        private void CompleteEndSave(P p, CharacterSaveResult result = CharacterSaveResult.Success)
        {
            _events.Add(SaveEvent(p.Character));
            _endPersistence.Sources[p.Character].SetResult(result);
        }

        private void DrainEnds() => _endQueue.Drain(++_endTick);

        private ZoneCloseSteps Steps() =>
            new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, _closePersistence, _closeQueue, _party, new NoopTimers(),
                new NoopSink(), new NoDrainLog(), _outbox, Zone);

        private void CompleteCloseSave(P p) => _closePersistence.Sources[p.Character].SetResult(CharacterSaveResult.Success);

        private void DrainClose() => _closeQueue.DrainOnShutdown(DRAIN_TIMEOUT);

        private ZoneLifecycle BuildLifecycle()
        {
            var lifecycle = new ZoneLifecycle(Zone, _lifecycleListener, 0u, idleCloseSeconds: IDLE_SECONDS, tickRateHz: HZ);
            _poolListener.Lifecycle = lifecycle;
            lifecycle.MarkActive(0u);
            return lifecycle;
        }

        // ----- AC-ZI-13: logout and TTL through the real coordinator ----------------------------

        [Test]
        public void test_logout_notifies_once_after_the_save_and_drain_with_one_graceful_left()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);

            Assert.IsTrue(_ends.BeginCleanLogout(leaver.Account, r => { }));

            Assert.AreEqual(1, _endPersistence.Calls.Count);
            Assert.AreEqual(SessionEndReason.CleanLogout, _endPersistence.Calls[0].Reason);
            AssertNothingReleasedYet(leaver);

            CompleteEndSave(leaver);
            CollectionAssert.AreEqual(new[] { SaveEvent(leaver.Character) }, _events);
            AssertNothingReleasedYet(leaver);

            DrainEnds();

            CollectionAssert.AreEqual(new[] { SaveEvent(leaver.Character), ExitEvent(leaver.Entity) }, _events, "save completes, then the exit");
            Assert.AreEqual(1, _party.ExitsFor(leaver.Entity));
            Assert.AreEqual(new PartyID(PARTY_RAW), _party.Exits[0].Party);
            Assert.AreEqual(1, _outbox.LeftFor(watcher.Client, leaver.Entity));
            Assert.AreEqual(1, _outbox.Left.Count);
            Assert.AreEqual(DisconnectType.Graceful, _outbox.Left[0].Message.DisconnectType);
            CollectionAssert.AreEqual(new[] { true }, _releaseResults);
            AssertFullyReleased(leaver);
        }

        [Test]
        public void test_ttl_expiry_notifies_once_after_the_save_and_drain_with_one_timeout_left()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P ghost = AddPlayer(1u);
            ConnectAccount(ghost);
            DisconnectByHeartbeat(ghost);

            Assert.IsTrue(_ends.EvaluateSessionTTL(ghost.Account, EXPIRY_TICK, EXPIRY_TICK));

            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _endPersistence.Calls[0].Reason);
            AssertNothingReleasedYet(ghost);

            CompleteEndSave(ghost);
            AssertNothingReleasedYet(ghost);
            DrainEnds();

            CollectionAssert.AreEqual(new[] { SaveEvent(ghost.Character), ExitEvent(ghost.Entity) }, _events, "save completes, then the exit");
            Assert.AreEqual(1, _party.ExitsFor(ghost.Entity));
            Assert.AreEqual(1, _outbox.LeftFor(watcher.Client, ghost.Entity));
            Assert.AreEqual(1, _outbox.Left.Count);
            Assert.AreEqual(DisconnectType.Timeout, _outbox.Left[0].Message.DisconnectType);
            CollectionAssert.AreEqual(new[] { true }, _releaseResults);
            AssertFullyReleased(ghost);
        }

        [TestCase(CharacterSaveResult.CharacterNotFound)]
        [TestCase(CharacterSaveResult.ConcurrencyConflict)]
        [TestCase(CharacterSaveResult.DatabaseError)]
        public void test_failed_logout_save_keeps_the_slot_and_the_session_connected(CharacterSaveResult result)
        {
            AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);
            Assert.IsTrue(_ends.BeginCleanLogout(leaver.Account, r => { }));

            CompleteEndSave(leaver, result);
            DrainEnds();

            AssertNothingReleasedYet(leaver);
            Assert.AreEqual(0, _party.Exits.Count);
            AssertSessionState(leaver, SessionState.Connected);
        }

        [Test]
        public void test_faulted_logout_save_keeps_the_slot_and_the_session_connected()
        {
            AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);
            Assert.IsTrue(_ends.BeginCleanLogout(leaver.Account, r => { }));

            _endPersistence.Sources[leaver.Character].SetException(new InvalidOperationException("db down"));
            DrainEnds();

            AssertNothingReleasedYet(leaver);
            AssertSessionState(leaver, SessionState.Connected);
        }

        [Test]
        public void test_cancelled_logout_save_keeps_the_slot_and_the_session_connected()
        {
            AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);
            Assert.IsTrue(_ends.BeginCleanLogout(leaver.Account, r => { }));

            _endPersistence.Sources[leaver.Character].SetCanceled();
            DrainEnds();

            AssertNothingReleasedYet(leaver);
            AssertSessionState(leaver, SessionState.Connected);
        }

        [Test]
        public void test_failed_ttl_save_still_releases_and_notifies_once()
        {
            AddPlayer(WATCHER_CLIENT);
            P ghost = AddPlayer(1u);
            ConnectAccount(ghost);
            DisconnectByHeartbeat(ghost);
            Assert.IsTrue(_ends.EvaluateSessionTTL(ghost.Account, EXPIRY_TICK, EXPIRY_TICK));

            CompleteEndSave(ghost, CharacterSaveResult.DatabaseError);
            DrainEnds();

            Assert.AreEqual(1, _party.ExitsFor(ghost.Entity));
            Assert.AreEqual(1, _outbox.Left.Count);
            Assert.AreEqual(DisconnectType.Timeout, _outbox.Left[0].Message.DisconnectType);
            CollectionAssert.AreEqual(new[] { true }, _releaseResults);
            AssertFullyReleased(ghost);
        }

        // ----- AC-ZI-13: teardown and reclamation -----------------------------------------------

        [Test]
        public void test_teardown_close_steps_and_drain_notify_exactly_once()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);

            Steps().RunCloseSteps();
            Assert.AreEqual(0, _party.Exits.Count, "no notification before the saves complete");
            CompleteCloseSave(a);
            CompleteCloseSave(b);
            DrainClose();

            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
            Assert.AreEqual(1, _party.ExitsFor(b.Entity));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_entry_stall_reclamation_notifies_exactly_once()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P stalled = AddPlayer(1u);
            _reclamation.RegisterEntering(stalled.Client, stalled.Entity);

            _reclamation.OnTick(_ticks.Now + _reclamation.StallTicks);
            int leftAfterReclaim = _outbox.Left.Count;

            Assert.AreEqual(1, _party.ExitsFor(stalled.Entity));
            Assert.AreEqual(1, _outbox.LeftFor(watcher.Client, stalled.Entity));
            Assert.IsFalse(_pool.TryGetPlayer(stalled.Entity, out _));
            Assert.IsFalse(_exit.ReleasePlayer(stalled.Character.RawValue, DisconnectType.Timeout));
            Assert.AreEqual(1, _party.ExitsFor(stalled.Entity));
            Assert.AreEqual(leftAfterReclaim, _outbox.Left.Count, "the late release sent nothing");
        }

        [Test]
        public void test_stall_reclamation_and_teardown_in_one_tick_notify_exactly_once()
        {
            P stalled = AddPlayer(1u);
            P live = AddPlayer(2u);
            _reclamation.RegisterEntering(stalled.Client, stalled.Entity);

            _reclamation.OnTick(_ticks.Now + _reclamation.StallTicks);
            Steps().RunCloseSteps();
            CompleteCloseSave(live);
            DrainClose();

            Assert.AreEqual(1, _party.ExitsFor(stalled.Entity));
            Assert.AreEqual(1, _party.ExitsFor(live.Entity));
            Assert.AreEqual(1, _closePersistence.Calls.Count, "the reclaimed slot is not saved by the close steps");
        }

        [Test]
        public void test_logout_save_in_flight_at_close_steps_notifies_exactly_once()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);
            Assert.IsTrue(_ends.BeginCleanLogout(leaver.Account, r => { }));

            Steps().RunCloseSteps();
            CompleteCloseSave(leaver);
            CompleteCloseSave(watcher);
            DrainClose();
            Assert.AreEqual(1, _party.ExitsFor(leaver.Entity));

            CompleteEndSave(leaver);
            DrainEnds();

            Assert.AreEqual(1, _party.ExitsFor(leaver.Entity), "the late ReleasePlayer is a no-op");
            CollectionAssert.AreEqual(new[] { false }, _releaseResults);
        }

        // ----- EC-ZI-11, solo, recipients ---------------------------------------------------------

        [Test]
        public void test_solo_player_release_sends_no_party_notice_and_frees_the_slot()
        {
            AddPlayer(WATCHER_CLIENT);
            P solo = AddPlayer(1u, inParty: false);

            bool freed = _exit.ReleasePlayer(solo.Character.RawValue, DisconnectType.Graceful);

            Assert.IsTrue(freed);
            Assert.AreEqual(0, _party.Exits.Count);
            Assert.AreEqual(1, _outbox.Left.Count);
            AssertFullyReleased(solo);
        }

        [Test]
        public void test_double_release_notifies_once_and_the_second_call_returns_false()
        {
            AddPlayer(WATCHER_CLIENT);
            P a = AddPlayer(1u);

            bool first = _exit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful);
            bool second = _exit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
            Assert.AreEqual(1, _outbox.Left.Count);
            AssertFullyReleased(a);
        }

        [Test]
        public void test_release_sends_player_left_zone_to_each_other_client_and_not_to_the_leaver()
        {
            P watcherA = AddPlayer(WATCHER_CLIENT);
            P watcherB = AddPlayer(WATCHER2_CLIENT);
            P leaver = AddPlayer(1u);

            Assert.IsTrue(_exit.ReleasePlayer(leaver.Character.RawValue, DisconnectType.Graceful));

            Assert.AreEqual(2, _outbox.Left.Count);
            Assert.AreEqual(1, _outbox.LeftFor(watcherA.Client, leaver.Entity));
            Assert.AreEqual(1, _outbox.LeftFor(watcherB.Client, leaver.Entity));
            foreach ((uint client, PlayerLeftZone message) in _outbox.Left)
            {
                Assert.AreNotEqual(leaver.Client, client);
                Assert.AreEqual(leaver.Entity, message.EntityId);
            }

            Assert.AreEqual(0, _outbox.OtherCount);
        }

        [Test]
        public void test_release_of_a_character_without_a_slot_changes_nothing()
        {
            AddPlayer(WATCHER_CLIENT);
            P a = AddPlayer(1u);

            bool freed = _exit.ReleasePlayer(UNKNOWN_CHARACTER_RAW, DisconnectType.Timeout);

            Assert.IsFalse(freed);
            Assert.AreEqual(2, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _outbox.Left.Count);
            Assert.AreEqual(0, _party.Exits.Count);
            Assert.IsTrue(_pool.TryGetPlayer(a.Entity, out _));
        }

        [Test]
        public void test_release_of_character_id_zero_changes_nothing()
        {
            AddPlayer(WATCHER_CLIENT);
            AddPlayer(1u);

            bool freed = _exit.ReleasePlayer(0u, DisconnectType.Timeout);

            Assert.IsFalse(freed);
            Assert.AreEqual(2, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _outbox.Left.Count);
        }

        [Test]
        public void test_release_frees_only_the_slot_of_the_named_character()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);

            Assert.IsTrue(_exit.ReleasePlayer(b.Character.RawValue, DisconnectType.Graceful));

            Assert.IsTrue(_pool.TryGetPlayer(a.Entity, out _));
            Assert.IsFalse(_pool.TryGetPlayer(b.Entity, out _));
            Assert.AreEqual(0, _party.ExitsFor(a.Entity));
        }

        // ----- Lifecycle --------------------------------------------------------------------------

        [Test]
        public void test_last_release_while_draining_closes_drained_on_that_tick()
        {
            ZoneLifecycle lifecycle = BuildLifecycle();
            P a = AddPlayer(1u);
            lifecycle.RequestQuit(QUIT_TICK);
            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            _ticks.Now = DRAINED_RELEASE_TICK;

            Assert.IsTrue(_exit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful));

            Assert.AreEqual(1, _lifecycleListener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Drained, DRAINED_RELEASE_TICK), _lifecycleListener.Closed[0]);
        }

        [Test]
        public void test_release_of_one_of_two_slots_while_draining_does_not_close()
        {
            ZoneLifecycle lifecycle = BuildLifecycle();
            P a = AddPlayer(1u);
            AddPlayer(2u);
            lifecycle.RequestQuit(QUIT_TICK);
            _ticks.Now = DRAINED_RELEASE_TICK;

            Assert.IsTrue(_exit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful));

            Assert.AreEqual(ZoneState.Draining, lifecycle.State);
            Assert.AreEqual(0, _lifecycleListener.Closed.Count);
        }

        [Test]
        public void test_last_release_while_active_starts_the_idle_timer()
        {
            ZoneLifecycle lifecycle = BuildLifecycle();
            P a = AddPlayer(1u);
            _ticks.Now = CONNECT_TICK;

            Assert.IsTrue(_exit.ReleasePlayer(a.Character.RawValue, DisconnectType.Graceful));
            lifecycle.OnTick(CONNECT_TICK + IDLE_TICKS - 1u);
            Assert.AreEqual(ZoneState.Active, lifecycle.State);
            lifecycle.OnTick(CONNECT_TICK + IDLE_TICKS);

            Assert.AreEqual(ZoneState.Closed, lifecycle.State);
            Assert.AreEqual(1, _lifecycleListener.Closed.Count);
            Assert.AreEqual((ZoneCloseTransition.Idle, CONNECT_TICK + IDLE_TICKS), _lifecycleListener.Closed[0]);
        }

        // ----- One PlayerLeftZone through the state machine --------------------------------------

        [Test]
        public void test_explicit_disconnect_with_noop_remove_sends_one_graceful_left()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P leaver = AddPlayer(1u);
            ConnectAccount(leaver);

            _sm.ProcessExplicitDisconnect(leaver.Account, characterId => { }, characterId => { },
                (characterId, type) => _exit.ReleasePlayer(characterId, type));

            Assert.AreEqual(1, _outbox.Left.Count);
            Assert.AreEqual(1, _outbox.LeftFor(watcher.Client, leaver.Entity));
            Assert.AreEqual(DisconnectType.Graceful, _outbox.Left[0].Message.DisconnectType);
            Assert.AreEqual(1, _party.ExitsFor(leaver.Entity));
            Assert.IsFalse(_pool.TryGetPlayer(leaver.Entity, out _));
        }

        [Test]
        public void test_ttl_expiry_with_noop_remove_sends_one_timeout_left()
        {
            P watcher = AddPlayer(WATCHER_CLIENT);
            P ghost = AddPlayer(1u);
            ConnectAccount(ghost);
            DisconnectByHeartbeat(ghost);

            _sm.CompleteSessionActiveTTLExpiry(ghost.Account, EXPIRY_TICK, EXPIRY_TICK, characterId => { }, characterId => { },
                (characterId, type) => _exit.ReleasePlayer(characterId, type));

            Assert.AreEqual(1, _outbox.Left.Count);
            Assert.AreEqual(1, _outbox.LeftFor(watcher.Client, ghost.Entity));
            Assert.AreEqual(DisconnectType.Timeout, _outbox.Left[0].Message.DisconnectType);
            Assert.AreEqual(1, _party.ExitsFor(ghost.Entity));
        }

        [Test]
        public void test_constructor_rejects_null_arguments()
        {
            Assert.Throws<ArgumentNullException>(() => new ZonePlayerExit(null, _reclamation));
            Assert.Throws<ArgumentNullException>(() => new ZonePlayerExit(_pool, null));
        }
    }
}
