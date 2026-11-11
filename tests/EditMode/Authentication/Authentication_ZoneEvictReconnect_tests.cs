using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Authentication
{
    /// <summary>
    /// Authentication Story 014: zone-side <c>Evict</c> and <c>Reconnect</c> handlers (AC-AUTH-27 b, c, e; AC-AUTH-33;
    /// EC-AUTH-6, EC-AUTH-7, EC-CP-4). Driven through a real <see cref="ZoneInstancingWiring"/> with fake persistence
    /// whose saves are completed by hand, so each case controls when a save finishes. No socket, no real I/O.
    /// </summary>
    [TestFixture]
    internal sealed class Authentication_ZoneEvictReconnect_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const uint ACCOUNT_1 = 1u;
        private const uint ACCOUNT_2 = 2u;
        private const uint ACCOUNT_3 = 3u;
        private const uint ACCOUNT_4 = 4u;
        private const uint ACCOUNT_5 = 5u;
        private const uint CHARACTER_1 = 77u;
        private const uint CHARACTER_2 = 78u;
        private const uint CHARACTER_4 = 80u;
        private const uint CHARACTER_5 = 81u;
        private const uint CLIENT_4 = 14u;
        private const uint CLIENT_5 = 15u;
        private const int REAUTH_LIMIT = 3;
        private const uint EXPIRY_TICK = 6100u;
        private const uint HEARTBEAT_TICKS = 10u;
        private static readonly TimeSpan TEARDOWN_DRAIN_TIMEOUT = TimeSpan.FromMilliseconds(200);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static AccountID Acct(uint raw) => new AccountID(raw);

        private static byte[] Token(byte seed)
        {
            var token = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];
            for (int i = 0; i < token.Length; i++)
            {
                token[i] = (byte)(seed + i);
            }

            return token;
        }

        // ----- fakes ----------------------------------------------------------------------------

        private sealed class RecordingLink : IGatewayLinkOutbox
        {
            public readonly List<uint> EvictResults = new List<uint>();
            public readonly List<(uint RequestId, bool Accepted, byte[] NewToken)> ReconnectResults = new List<(uint, bool, byte[])>();
            public readonly List<AccountID> Released = new List<AccountID>();

            public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason) { }

            public void EnqueueSessionReleased(AccountID account) => Released.Add(account);

            public void EnqueueEvictResult(uint requestId) => EvictResults.Add(requestId);

            public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken) =>
                ReconnectResults.Add((requestId, accepted, newToken));

            public void EnqueuePlayerCount(ushort count) { }

            public void EnqueueZoneDraining() { }
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Now;

            public uint ServerTickNumber => Now;
        }

        private sealed class FakeClientOutbox : IClientMessageOutbox
        {
            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt) { }
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
            public readonly List<ulong> Disconnected = new List<ulong>();

            public void DisconnectClient(ulong transportClientId) => Disconnected.Add(transportClientId);
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        // Loads and saves are completed by hand.
        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public readonly List<CharacterID> LoadIds = new List<CharacterID>();
            public readonly List<(CharacterID Id, SessionEndReason Reason, TaskCompletionSource<CharacterSaveResult> Source)> Saves =
                new List<(CharacterID, SessionEndReason, TaskCompletionSource<CharacterSaveResult>)>();

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                var source = new TaskCompletionSource<CharacterLoadResult>();
                Loads.Add(source);
                LoadIds.Add(id);
                return source.Task;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
            {
                var source = new TaskCompletionSource<CharacterSaveResult>();
                Saves.Add((id, reason, source));
                return source.Task;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                SaveSession(id, reason, ct);

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        // ----- fixture state ----------------------------------------------------------------------

        private RecordingLink _link;
        private FakeTicks _ticks;
        private FakeDisconnector _disconnector;
        private FakePersistence _persistence;
        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private NetworkTestObserver _observer;
        private ZoneInstancingWiring _wiring;

        [SetUp]
        public void SetUp()
        {
            _link = new RecordingLink();
            _ticks = new FakeTicks();
            _disconnector = new FakeDisconnector();
            _persistence = new FakePersistence();
            _queue = new TickCompletionQueue();
            _gate = new CharacterMutationGate();
            _observer = new NetworkTestObserver();
            _wiring = null;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            _queue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);
        }

        private void Build(int idleCloseSeconds = 600)
        {
            var options = new ZoneInstancingWiringOptions
            {
                TickRateHz = HZ,
                GatewayOutbox = _link,
                Equipment = new FakeEquipmentService(),
                ReauthFailureLimit = REAUTH_LIMIT,
                IdleCloseSeconds = idleCloseSeconds,
            };
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, new FakeDispatcher(), new FakeClientOutbox(), _persistence, _persistence,
                new FakeApplier(), _gate, new RecordingInventory(), _disconnector, _ => { }, options);
            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
            _wiring.SessionEnds.TestObserver = _observer;
        }

        private void Drain(uint tick)
        {
            _ticks.Now = tick;
            _queue.Drain(tick);
        }

        // A Connected session with a stored token.
        private void SeedConnected(uint account, uint character, byte tokenSeed)
        {
            _wiring.Sessions.EnterConnecting(account, 0u);
            _wiring.Sessions.CompleteAuthSuccess(account, character, 0u);
            Assert.IsTrue(_wiring.TokenStore.AdmitToken(account, Token(tokenSeed), EXPIRY_TICK));
        }

        // Disconnected_SessionActive via the heartbeat sweep (expiry tick left at 0).
        private void SeedGhost(uint account, uint character, byte tokenSeed)
        {
            SeedConnected(account, character, tokenSeed);
            _wiring.Sessions.EvaluateTimeouts(1000u, HEARTBEAT_TICKS, 100000u);
            AssertState(account, SessionState.Disconnected_SessionActive);
        }

        private void AssertState(uint account, SessionState expected)
        {
            Assert.IsTrue(_wiring.Sessions.TryGetSessionState(account, out SessionState actual));
            Assert.AreEqual(expected, actual);
        }

        private void Evict(uint requestId, uint account, uint tick = 1u)
        {
            _ticks.Now = tick;
            _wiring.GatewayEvict.Handle(requestId, Acct(account), tick);
        }

        private void Reconnect(uint requestId, uint account, byte[] token, uint tick = 1u)
        {
            _ticks.Now = tick;
            _wiring.GatewayReconnect.Handle(requestId, Acct(account), token, tick);
        }

        private void CompleteSave(int index, CharacterSaveResult result)
        {
            _persistence.Saves[index].Source.SetResult(result);
        }

        // ----- AC-1: Evict a connected player (AC-AUTH-27 b, c) ----------------------------------------

        [Test]
        public void Evict_ConnectedSession_ResultQueuedOnlyAfterSaveThenSessionClosedAndTokenGone()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);

            // Act
            Evict(9u, ACCOUNT_1);
            Drain(2u);
            Drain(3u);
            Assert.AreEqual(0, _link.EvictResults.Count, "no result while the save is pending");
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(4u);

            // Assert
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.AreEqual(1, _persistence.Saves.Count);
            Assert.AreEqual(SessionEndReason.Evicted, _persistence.Saves[0].Reason);
            Assert.AreEqual(new CharacterID(CHARACTER_1), _persistence.Saves[0].Id);
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_1));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1), "no ghost entry, no reconnect window");
            CollectionAssert.AreEqual(new[] { (ACCOUNT_1, CHARACTER_1) }, _observer.SessionInvalidatedByStealCalls);
            CollectionAssert.AreEqual(new[] { (CHARACTER_1, PersistenceWriteReason.SessionSteal) }, _observer.PersistenceWriteCompletedCalls);
            CollectionAssert.AreEqual(
                new[] { (ACCOUNT_1, SessionState.Connected, SessionState.Disconnected_SessionExpired, "GatewayEvict") },
                _observer.SessionStateTransitionedCalls);
            Assert.AreEqual(0, _link.Released.Count, "SessionReleased is never sent for an evicted account");
        }

        [Test]
        public void Evict_ConnectedSession_TokenIsInvalidatedBeforeTheSaveCompletes()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);

            // Act
            Evict(9u, ACCOUNT_1);

            // Assert: a Reconnect or handshake during the save already fails.
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_1));
            AssertState(ACCOUNT_1, SessionState.Connected);

            CompleteSave(0, CharacterSaveResult.Success); // finish the save so teardown finds no task in flight
        }

        // ----- AC-2: save fails (AC-AUTH-27 e) ----------------------------------------------------------

        [Test]
        public void Evict_SaveFails_ResultStillSentAndSessionStillClosed()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            Evict(9u, ACCOUNT_1);

            // Act: the critical persistence log is written by the save itself (not faked here).
            CompleteSave(0, CharacterSaveResult.DatabaseError);
            Drain(2u);

            // Assert
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
            Assert.AreEqual(1, _observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual(1, _persistence.Saves.Count);
        }

        // ----- AC-3: not arrived / unknown ----------------------------------------------------------------

        [Test]
        public void Evict_AdmittedNotArrived_DeletesEntryAndTokenWithoutSaveOrSessionReleased()
        {
            // Arrange
            Build();
            _wiring.GatewayAdmit.Handle(1u, Acct(ACCOUNT_2), new CharacterID(CHARACTER_2), Token(2), 1u);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount);

            // Act
            Evict(5u, ACCOUNT_2);

            // Assert
            CollectionAssert.AreEqual(new[] { 5u }, _link.EvictResults);
            Assert.AreEqual(0, _persistence.Saves.Count);
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_2));
            Assert.IsFalse(_wiring.Admitted.TryGetByCharacter(new CharacterID(CHARACTER_2), out _, out _));
            Assert.AreEqual(0, _wiring.Admitted.ReservedCount);
            Assert.AreEqual(0, _link.Released.Count);
        }

        [Test]
        public void Evict_UnknownAccount_ResultAtOnceWithNoSave()
        {
            // Arrange
            Build();

            // Act
            Evict(6u, ACCOUNT_3);
            Evict(7u, ACCOUNT_3);

            // Assert
            CollectionAssert.AreEqual(new[] { 6u, 7u }, _link.EvictResults);
            Assert.AreEqual(0, _persistence.Saves.Count);
        }

        // ----- AC-3b: load in flight / entering slot ------------------------------------------------------

        [Test]
        public void Evict_LoadInFlight_DropsPendingEntrySoLoadAllocatesNoSlot()
        {
            // Arrange: account 4 admitted, handshake accepted, LoadCharacter pending.
            Build();
            _wiring.GatewayAdmit.Handle(1u, Acct(ACCOUNT_4), new CharacterID(CHARACTER_4), Token(4), 1u);
            _wiring.Admission.BeginEntry(new ZoneEntryRequest(CLIENT_4, new CharacterID(CHARACTER_4), Zone, Token(4)));
            Assert.IsTrue(_wiring.Admission.IsLoadPending(new CharacterID(CHARACTER_4)));

            // Act
            Evict(8u, ACCOUNT_4);
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            Drain(2u);

            // Assert
            CollectionAssert.AreEqual(new[] { 8u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Admission.IsLoadPending(new CharacterID(CHARACTER_4)));
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount, "the dropped load allocated no slot");
            Assert.AreEqual(0, _persistence.Saves.Count);
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_4));
            Assert.AreEqual(0, _wiring.Admitted.EntryCount);
            Assert.AreEqual(0, _link.Released.Count);
        }

        [Test]
        public void Evict_EnteringSlot_ReclaimsSlotWithoutSaveAndDeletesToken()
        {
            // Arrange: account 5 holds an allocated slot and has sent no ZoneEntryAck.
            Build();
            _wiring.GatewayAdmit.Handle(1u, Acct(ACCOUNT_5), new CharacterID(CHARACTER_5), Token(5), 1u);
            _wiring.Admission.BeginEntry(new ZoneEntryRequest(CLIENT_5, new CharacterID(CHARACTER_5), Zone, Token(5)));
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            Drain(2u);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _wiring.Reclamation.EnteringCount);

            // Act
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Evict(9u, ACCOUNT_5, 3u);

            // Assert
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount, "slot reclaimed");
            Assert.AreEqual(0, _wiring.Reclamation.EnteringCount);
            Assert.AreEqual(0, _persistence.Saves.Count);
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_5));
            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)CLIENT_5);
            Assert.AreEqual(0, _link.Released.Count);
        }

        // ----- AC-3c: ghost / Reconnecting ------------------------------------------------------------------

        [Test]
        public void Evict_GhostSession_SavesOnceThenEndsWithGatewayEvictTrigger()
        {
            // Arrange
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);

            // Act
            Evict(9u, ACCOUNT_1);
            Assert.AreEqual(0, _link.EvictResults.Count);
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);

            // Assert
            Assert.AreEqual(1, _persistence.Saves.Count);
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_1));
            CollectionAssert.Contains(_observer.SessionStateTransitionedCalls,
                (ACCOUNT_1, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired, "GatewayEvict"));
        }

        [Test]
        public void Evict_ReconnectingSession_SavesOnceThenEndsWithGatewayEvictTrigger()
        {
            // Arrange
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);
            _wiring.Sessions.EnterReconnecting(ACCOUNT_1, EXPIRY_TICK);
            AssertState(ACCOUNT_1, SessionState.Reconnecting);

            // Act
            Evict(9u, ACCOUNT_1);
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);

            // Assert
            Assert.AreEqual(1, _persistence.Saves.Count);
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
            CollectionAssert.Contains(_observer.SessionStateTransitionedCalls,
                (ACCOUNT_1, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "GatewayEvict"));
        }

        // ----- AC-3d: end already in flight (EC-CP-4) ----------------------------------------------------------

        [Test]
        public void Evict_SecondEvictWhileFirstPending_OneSaveAndBothRequestIdsAnswered()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            Evict(9u, ACCOUNT_1);

            // Act
            Evict(10u, ACCOUNT_1);
            Assert.AreEqual(1, _persistence.Saves.Count);
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);

            // Assert
            CollectionAssert.AreEqual(new[] { 9u, 10u }, _link.EvictResults);
            Assert.AreEqual(1, _persistence.Saves.Count);
        }

        [Test]
        public void Evict_CleanLogoutInFlightAndSucceeds_NoSecondSaveAndResultWhenLogoutEnds()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            _wiring.SessionEnds.BeginCleanLogout(ACCOUNT_1, _ => { });

            // Act
            Evict(9u, ACCOUNT_1);
            Assert.AreEqual(0, _link.EvictResults.Count);
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);

            // Assert
            Assert.AreEqual(1, _persistence.Saves.Count, "one save per end");
            Assert.AreEqual(SessionEndReason.CleanLogout, _persistence.Saves[0].Reason);
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
        }

        [Test]
        public void Evict_CleanLogoutInFlightAndFails_EvictionSaveStartsThenResultAfterIt()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            _wiring.SessionEnds.BeginCleanLogout(ACCOUNT_1, _ => { });
            Evict(9u, ACCOUNT_1);

            // Act: the logout save fails, the session is still Connected, so the eviction save starts now.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            CompleteSave(0, CharacterSaveResult.DatabaseError);
            Drain(2u);

            // Assert
            Assert.AreEqual(2, _persistence.Saves.Count);
            Assert.AreEqual(SessionEndReason.Evicted, _persistence.Saves[1].Reason);
            Assert.AreEqual(0, _link.EvictResults.Count);
            CompleteSave(1, CharacterSaveResult.Success);
            Drain(3u);
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
        }

        [Test]
        public void Evict_MutationGateHeld_SaveWaitsForGateThenRuns()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            _gate.Close(new CharacterID(CHARACTER_1));

            // Act
            Evict(9u, ACCOUNT_1);
            Assert.AreEqual(0, _persistence.Saves.Count, "waits on the gate");
            Assert.AreEqual(0, _link.EvictResults.Count);
            _gate.Open(new CharacterID(CHARACTER_1));

            // Assert
            Assert.AreEqual(1, _persistence.Saves.Count);
            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);
            CollectionAssert.AreEqual(new[] { 9u }, _link.EvictResults);
        }

        // ----- AC-4: Reconnect -------------------------------------------------------------------------------------

        [Test]
        public void Reconnect_ValidToken_RotatesTokenAndLeavesStateAndExpiryUnchanged()
        {
            // Arrange: ghost with expiry tick E (reached through the existing re-auth path, limit well above the count).
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);
            _wiring.Sessions.EnterReconnecting(ACCOUNT_1, EXPIRY_TICK);
            _wiring.Sessions.RecordFailedReAuthAttempt(ACCOUNT_1, 100, _ => { });
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);

            // Act
            Reconnect(3u, ACCOUNT_1, Token(1));

            // Assert
            Assert.AreEqual(1, _link.ReconnectResults.Count);
            Assert.AreEqual(3u, _link.ReconnectResults[0].RequestId);
            Assert.IsTrue(_link.ReconnectResults[0].Accepted);
            Assert.IsNotNull(_link.ReconnectResults[0].NewToken);
            CollectionAssert.AreNotEqual(Token(1), _link.ReconnectResults[0].NewToken);
            Assert.IsTrue(_wiring.TokenStore.TokenMatches(ACCOUNT_1, _link.ReconnectResults[0].NewToken));
            Assert.IsFalse(_wiring.TokenStore.TokenMatches(ACCOUNT_1, Token(1)));
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);
            Assert.IsTrue(_wiring.Sessions.TryGetSessionExpiryTick(ACCOUNT_1, out uint expiry));
            Assert.AreEqual(EXPIRY_TICK, expiry);
        }

        [Test]
        public void Reconnect_WrongTokenUpToLimit_FirstFailuresReturnToGhostThenLimitEndsSessionThroughCoordinator()
        {
            // Arrange
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);

            // Act / Assert: failures below the limit.
            Reconnect(1u, ACCOUNT_1, Token(99));
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);
            Reconnect(2u, ACCOUNT_1, Token(99));
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_1));
            Assert.AreEqual(0, _persistence.Saves.Count);

            // The failure that reaches the limit.
            Reconnect(3u, ACCOUNT_1, Token(99));
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_1), "token deleted at the limit");
            Assert.AreEqual(1, _persistence.Saves.Count, "one async save through the coordinator");
            Assert.AreEqual(3, _link.ReconnectResults.Count);
            Assert.IsTrue(_link.ReconnectResults.TrueForAll(r => !r.Accepted && r.NewToken == null), "rejected at once, each");
            Assert.IsTrue(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1), "released only after the save");

            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
            CollectionAssert.Contains(_observer.SessionStateTransitionedCalls,
                (ACCOUNT_1, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, "ReauthLimitExceeded"));
            CollectionAssert.Contains(_observer.ReAuthAttemptFailedCalls, (ACCOUNT_1, 3, 0), "the limiting attempt is reported at the end");
        }

        [Test]
        public void Reconnect_ConnectedAccountWithWrongToken_RejectedAndNothingCounted()
        {
            // Arrange
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);

            // Act
            for (uint i = 0; i < REAUTH_LIMIT + 2; i++)
            {
                Reconnect(i, ACCOUNT_1, Token(99));
            }

            // Assert
            Assert.AreEqual(REAUTH_LIMIT + 2, _link.ReconnectResults.Count);
            Assert.IsTrue(_link.ReconnectResults.TrueForAll(r => !r.Accepted));
            AssertState(ACCOUNT_1, SessionState.Connected);
            Assert.IsTrue(_wiring.Sessions.TryGetReauthFailureCount(ACCOUNT_1, out int failures));
            Assert.AreEqual(0, failures);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_1));
            Assert.AreEqual(0, _persistence.Saves.Count);
        }

        [Test]
        public void Reconnect_WrongTokenUpToLimitWhileTtlEndInFlight_RejectedNothingCountedAndTtlEndReleasesSession()
        {
            // Arrange: ghost whose TTL (expiry tick 0) has passed; the TTL save is in flight.
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);
            Assert.IsTrue(_wiring.SessionEnds.EvaluateSessionTTL(ACCOUNT_1, 1u, 0u));
            Assert.IsTrue(_wiring.SessionEnds.IsEndInFlight(ACCOUNT_1));

            // Act
            for (uint i = 0; i < REAUTH_LIMIT; i++)
            {
                Reconnect(i, ACCOUNT_1, Token(99));
            }

            // Assert: nothing moved; the TTL end still releases the session.
            Assert.IsTrue(_link.ReconnectResults.TrueForAll(r => !r.Accepted));
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);
            Assert.IsTrue(_wiring.Sessions.TryGetReauthFailureCount(ACCOUNT_1, out int failures));
            Assert.AreEqual(0, failures);
            Assert.AreEqual(1, _persistence.Saves.Count, "only the TTL save");

            CompleteSave(0, CharacterSaveResult.Success);
            Drain(2u);
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT_1));
        }

        [Test]
        public void Reconnect_ValidTokenWhileTtlEndInFlight_RejectedAndTokenNotRotated()
        {
            // Arrange
            Build();
            SeedGhost(ACCOUNT_1, CHARACTER_1, 1);
            Assert.IsTrue(_wiring.SessionEnds.EvaluateSessionTTL(ACCOUNT_1, 1u, 0u));

            // Act
            Reconnect(5u, ACCOUNT_1, Token(1));

            // Assert
            Assert.AreEqual(1, _link.ReconnectResults.Count);
            Assert.IsFalse(_link.ReconnectResults[0].Accepted);
            Assert.IsTrue(_wiring.TokenStore.TokenMatches(ACCOUNT_1, Token(1)), "not rotated");
            AssertState(ACCOUNT_1, SessionState.Disconnected_SessionActive);

            CompleteSave(0, CharacterSaveResult.Success); // finish the save so teardown finds no task in flight
            Drain(2u);
        }

        [Test]
        public void Reconnect_UnknownAccount_RejectedAtOnce()
        {
            // Arrange
            Build();

            // Act
            Reconnect(4u, ACCOUNT_3, Token(1));

            // Assert
            Assert.AreEqual(1, _link.ReconnectResults.Count);
            Assert.IsFalse(_link.ReconnectResults[0].Accepted);
        }

        [Test]
        public void Reconnect_ConstructorRejectsNonPositiveLimit()
        {
            // Arrange
            Build();

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new GatewayReconnectHandler(
                _wiring.Sessions, _wiring.TokenStore, _wiring.SessionEnds, _link, 0));
        }

        // ----- AC-5: AC-AUTH-33 ban does not kick ----------------------------------------------------------------------

        [Test]
        public void Ban_ConnectedSessionFor100Ticks_StaysConnectedWithNoDisconnectEvent()
        {
            // Arrange: the zone has no ban input, so "banning" the account in the database changes nothing it can see.
            Build();
            SeedConnected(ACCOUNT_1, CHARACTER_1, 1);
            _wiring.Sessions.RecordInboundActivity(ACCOUNT_1, 0u);

            // Act: 100 ticks (5 s at 20 Hz).
            for (uint tick = 1u; tick <= 100u; tick++)
            {
                _ticks.Now = tick;
                _wiring.Run(tick, 0f);
            }

            // Assert
            AssertState(ACCOUNT_1, SessionState.Connected);
            Assert.AreEqual(0, _observer.SessionStateTransitionedCalls.Count);
            Assert.AreEqual(0, _persistence.Saves.Count);
            Assert.AreEqual(0, _disconnector.Disconnected.Count);
        }
    }
}
