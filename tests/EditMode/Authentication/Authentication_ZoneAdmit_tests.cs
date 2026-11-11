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
    /// Authentication Story 013: the zone admits a gateway token (AC-AUTH-21 a, c, d, e; CR-AUTH-11 steps 4 and 6;
    /// AC-ZI-35). Token store additions, admitted entries, the Admit handler and the validator are driven directly;
    /// the handshake cases run through a real <see cref="ZoneInstancingWiring"/> with fake persistence. Ticks are
    /// advanced by hand; tokens are fixed bytes.
    /// </summary>
    [TestFixture]
    internal sealed class Authentication_ZoneAdmit_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const int CONNECTING_SECONDS = 5;
        private const int CONNECTING_TICKS = CONNECTING_SECONDS * HZ;
        private const uint ACCOUNT_A = 1u;
        private const uint ACCOUNT_B = 2u;
        private const uint CHARACTER_A = 77u;
        private const uint CHARACTER_A2 = 78u;
        private const uint CHARACTER_B = 79u;
        private const uint CLIENT = 9u;
        private const int CAPACITY = 2;
        private const string VISIBLE_NAME = "Grim_Iron7";
        private const string LOGIN_NAME = "alice_login";
        private static readonly TimeSpan TEARDOWN_DRAIN_TIMEOUT = TimeSpan.FromMilliseconds(200);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

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

        private sealed class RecordingGatewayLinkOutbox : IGatewayLinkOutbox
        {
            public readonly List<(uint RequestId, bool Accepted, AdmitRejectReason Reason)> Results =
                new List<(uint, bool, AdmitRejectReason)>();

            public readonly List<AccountID> Released = new List<AccountID>();

            public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason) =>
                Results.Add((requestId, accepted, reason));

            public void EnqueueSessionReleased(AccountID account) => Released.Add(account);

            public void EnqueueEvictResult(uint requestId) { }

            public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken) { }

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
            public readonly List<(uint Client, ZoneFullResponse Message)> Full = new List<(uint, ZoneFullResponse)>();

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (messageTypeId == ZoneFullResponse.MessageTypeId && ZoneFullResponseCodec.TryReadBody(body.ToArray(), out ZoneFullResponse message))
                {
                    Full.Add((clientId, message));
                }
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
            public readonly List<ulong> Disconnected = new List<ulong>();

            public void DisconnectClient(ulong transportClientId) => Disconnected.Add(transportClientId);
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterRecord LastRecord;
            public EntityID LastEntity;

            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
            {
                LastRecord = record;
                LastEntity = entityId;
                return CharacterLoadCode.Success;
            }
        }

        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public readonly List<(CharacterID Id, AccountID Account)> LoadArgs = new List<(CharacterID, AccountID)>();

            public int LoadCalls => LoadArgs.Count;

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                var source = new TaskCompletionSource<CharacterLoadResult>();
                Loads.Add(source);
                LoadArgs.Add((id, ownerAccountID));
                return source.Task;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

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

        private RecordingGatewayLinkOutbox _link;
        private FakeTicks _ticks;
        private FakeClientOutbox _clientOutbox;
        private FakeDisconnector _disconnector;
        private FakePersistence _persistence;
        private FakeApplier _applier;
        private TickCompletionQueue _queue;
        private ZoneInstancingWiring _wiring;

        [SetUp]
        public void SetUp()
        {
            _link = new RecordingGatewayLinkOutbox();
            _ticks = new FakeTicks();
            _clientOutbox = new FakeClientOutbox();
            _disconnector = new FakeDisconnector();
            _persistence = new FakePersistence();
            _applier = new FakeApplier();
            _queue = new TickCompletionQueue();
            _wiring = null;
        }

        [TearDown]
        public void TearDown()
        {
            _queue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);
        }

        private ZoneInstancingWiring Build(Action<ZoneInstancingWiringOptions> configure = null)
        {
            var options = new ZoneInstancingWiringOptions
            {
                TickRateHz = HZ,
                ConnectingTimeoutSeconds = CONNECTING_SECONDS,
                GatewayOutbox = _link,
                Equipment = new FakeEquipmentService(),
            };
            configure?.Invoke(options);
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, new FakeDispatcher(), _clientOutbox, _persistence, _persistence,
                _applier, new CharacterMutationGate(), new RecordingInventory(), _disconnector, _ => { }, options);
            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
            return _wiring;
        }

        private void RunTick(uint tick)
        {
            _ticks.Now = tick;
            _wiring.Run(tick, 0f);
        }

        private void Admit(uint requestId, uint account, uint character, byte[] token, uint tick)
        {
            _ticks.Now = tick;
            _wiring.GatewayAdmit.Handle(requestId, new AccountID(account), new CharacterID(character), token, tick);
        }

        private void Handshake(uint client, uint character, byte[] token)
        {
            var request = new ZoneEntryRequest(client, new CharacterID(character), Zone, token);
            _wiring.Admission.BeginEntry(in request);
        }

        // ----- AC-1: token store -----------------------------------------------------------------

        [Test]
        public void test_admit_token_rejects_null_short_long_and_zero_tokens_leaving_store_unchanged()
        {
            var store = new SessionTokenStore();

            Assert.IsFalse(store.AdmitToken(ACCOUNT_A, null, 10u));
            Assert.IsFalse(store.AdmitToken(ACCOUNT_A, new byte[15], 10u));
            Assert.IsFalse(store.AdmitToken(ACCOUNT_A, new byte[17], 10u));
            Assert.IsFalse(store.AdmitToken(ACCOUNT_A, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], 10u));
            Assert.IsFalse(store.IsTokenActive(ACCOUNT_A));

            Assert.IsTrue(store.AdmitToken(ACCOUNT_A, Token(1), 10u));
            Assert.IsTrue(store.IsTokenActive(ACCOUNT_A));
        }

        [Test]
        public void test_admit_token_stores_a_copy_so_mutating_the_callers_array_changes_nothing()
        {
            var store = new SessionTokenStore();
            byte[] token = Token(1);

            store.AdmitToken(ACCOUNT_A, token, 10u);
            token[0] ^= 0xFF;

            Assert.IsFalse(store.TokenMatches(ACCOUNT_A, token));
            Assert.IsTrue(store.TokenMatches(ACCOUNT_A, Token(1)));
        }

        [Test]
        public void test_token_matches_true_for_stored_token_and_false_for_other_null_short_and_unknown()
        {
            var store = new SessionTokenStore();
            store.AdmitToken(ACCOUNT_A, Token(1), 10u);

            Assert.IsTrue(store.TokenMatches(ACCOUNT_A, Token(1)));
            Assert.IsTrue(store.TokenMatches(ACCOUNT_A, Token(1)), "no rotation: still matches");
            Assert.IsFalse(store.TokenMatches(ACCOUNT_A, Token(2)));
            Assert.IsFalse(store.TokenMatches(ACCOUNT_A, null));
            Assert.IsFalse(store.TokenMatches(ACCOUNT_A, new byte[15]));
            Assert.IsFalse(store.TokenMatches(ACCOUNT_B, Token(1)));
        }

        // ----- AC-2: Admit handler -----------------------------------------------------------------

        private sealed class HandlerRig
        {
            public readonly SessionTokenStore Store = new SessionTokenStore();
            public readonly RecordingGatewayLinkOutbox Link = new RecordingGatewayLinkOutbox();
            public readonly AdmittedEntries Entries;
            public readonly GatewayAdmitHandler Handler;
            public bool Active = true;
            public int Occupied;

            public HandlerRig()
            {
                Entries = new AdmittedEntries(Store, Link, CONNECTING_TICKS);
                Handler = new GatewayAdmitHandler(Entries, Store, Link, () => Active, () => Occupied + Entries.ReservedCount, CAPACITY);
            }

            public void Handle(uint requestId, uint account, uint character, byte[] token, uint tick) =>
                Handler.Handle(requestId, new AccountID(account), new CharacterID(character), token, tick);
        }

        [Test]
        public void test_admit_accepts_then_rejects_full_counting_occupied_plus_reserved_and_echoes_request_id()
        {
            var rig = new HandlerRig { Occupied = 1 };

            rig.Handle(5u, ACCOUNT_A, CHARACTER_A, Token(1), 10u);
            rig.Handle(6u, ACCOUNT_B, CHARACTER_B, Token(2), 11u);

            Assert.AreEqual((5u, true, AdmitRejectReason.None), rig.Link.Results[0]);
            Assert.AreEqual((6u, false, AdmitRejectReason.Full), rig.Link.Results[1]);
            Assert.AreEqual(1, rig.Entries.ReservedCount);
            Assert.IsTrue(rig.Store.IsTokenActive(ACCOUNT_A));
            Assert.IsFalse(rig.Store.IsTokenActive(ACCOUNT_B), "a rejected Admit stores nothing");
            Assert.IsFalse(rig.Entries.TryGetByCharacter(new CharacterID(CHARACTER_B), out _, out _));
        }

        [Test]
        public void test_admit_rejects_not_active_and_changes_nothing()
        {
            var rig = new HandlerRig { Active = false };

            rig.Handle(7u, ACCOUNT_A, CHARACTER_A, Token(1), 10u);

            Assert.AreEqual((7u, false, AdmitRejectReason.NotActive), rig.Link.Results[0]);
            Assert.AreEqual(0, rig.Entries.ReservedCount);
            Assert.IsFalse(rig.Store.IsTokenActive(ACCOUNT_A));
        }

        [Test]
        public void test_admit_rejects_all_zero_token_as_invalid_token_and_changes_nothing()
        {
            var rig = new HandlerRig();

            rig.Handle(8u, ACCOUNT_A, CHARACTER_A, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], 10u);

            Assert.AreEqual((8u, false, AdmitRejectReason.InvalidToken), rig.Link.Results[0]);
            Assert.AreEqual(0, rig.Entries.ReservedCount);
            Assert.IsFalse(rig.Store.IsTokenActive(ACCOUNT_A));
            Assert.IsFalse(rig.Entries.TryGetByCharacter(new CharacterID(CHARACTER_A), out _, out _));
        }

        [Test]
        public void test_admit_replacement_at_capacity_is_accepted_without_counting_the_old_entry()
        {
            var rig = new HandlerRig { Occupied = 1 };
            rig.Handle(1u, ACCOUNT_A, CHARACTER_A, Token(1), 10u);

            rig.Handle(2u, ACCOUNT_A, CHARACTER_A2, Token(2), 50u);

            Assert.AreEqual((2u, true, AdmitRejectReason.None), rig.Link.Results[1]);
            Assert.AreEqual(1, rig.Entries.ReservedCount, "reserved count unchanged");
            Assert.IsTrue(rig.Entries.TryGetByCharacter(new CharacterID(CHARACTER_A2), out AccountID account, out bool reserved));
            Assert.AreEqual(new AccountID(ACCOUNT_A), account);
            Assert.IsTrue(reserved);
            Assert.IsFalse(rig.Entries.TryGetByCharacter(new CharacterID(CHARACTER_A), out _, out _));
            Assert.IsTrue(rig.Store.TokenMatches(ACCOUNT_A, Token(2)));
            Assert.IsFalse(rig.Store.TokenMatches(ACCOUNT_A, Token(1)));

            rig.Entries.OnTick(50u + CONNECTING_TICKS - 1u);
            Assert.AreEqual(1, rig.Entries.ReservedCount, "timeout counts from the second tick");
            rig.Entries.OnTick(50u + CONNECTING_TICKS);
            Assert.AreEqual(0, rig.Entries.ReservedCount);
            CollectionAssert.AreEqual(new[] { new AccountID(ACCOUNT_A) }, rig.Link.Released);
        }

        [Test]
        public void test_admitted_entries_constructor_rejects_non_positive_timeout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AdmittedEntries(new SessionTokenStore(), new RecordingGatewayLinkOutbox(), 0));
        }

        // ----- AC-3: handshake at the admission seam ------------------------------------------------

        [Test]
        public void test_handshake_with_admitted_token_loads_once_with_admitted_account_then_entry_goes_and_token_stays()
        {
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);
            Assert.IsTrue(_wiring.TokenStore.TokenMatches(ACCOUNT_A, Token(1)));
            Assert.AreEqual(1, _wiring.SessionCount);

            Handshake(CLIENT, CHARACTER_A, Token(1));

            Assert.AreEqual(1, _persistence.LoadCalls);
            Assert.AreEqual((new CharacterID(CHARACTER_A), new AccountID(ACCOUNT_A)), _persistence.LoadArgs[0]);

            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            _ticks.Now = 2u;
            _queue.Drain(2u);

            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.IsFalse(_wiring.Admitted.TryGetByCharacter(new CharacterID(CHARACTER_A), out _, out _), "entry gone");
            Assert.AreEqual(0, _wiring.Admitted.ReservedCount);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_A), "token stays");
            Assert.AreEqual(1, _wiring.SessionCount, "session count unchanged");

            RunTick(1u + CONNECTING_TICKS + 5u);
            Assert.AreEqual(0, _link.Released.Count, "the timeout can no longer fire for that account");
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_A));
            Assert.AreEqual(0, _disconnector.Disconnected.Count);
        }

        [Test]
        public void test_handshake_with_wrong_token_closes_without_reply_or_load()
        {
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);

            Handshake(CLIENT, CHARACTER_A, Token(2));

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _clientOutbox.Full.Count);
            Assert.AreEqual(0, _persistence.LoadCalls);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount, "no state change");
        }

        [Test]
        public void test_handshake_with_unknown_character_closes_without_reply_or_load()
        {
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);

            Handshake(CLIENT, CHARACTER_A2, Token(1));

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _clientOutbox.Full.Count);
            Assert.AreEqual(0, _persistence.LoadCalls);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount);
        }

        // ----- AC-3b: idle timer ---------------------------------------------------------------------

        [Test]
        public void test_accepted_admit_disarms_the_idle_timer_and_a_timed_out_entry_rearms_it()
        {
            const int idleSeconds = 10;
            const uint idleTicks = idleSeconds * HZ;
            const int connectingSeconds = 15;
            const uint connectingTicks = connectingSeconds * HZ;
            Build(o =>
            {
                o.IdleCloseSeconds = idleSeconds;
                o.ConnectingTimeoutSeconds = connectingSeconds;
            });
            // MarkActive at tick 0 with no occupied slot armed the idle timer: deadline = idleTicks.

            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 10u);
            RunTick(idleTicks + 10u);
            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State, "disarmed by the accepted Admit");

            RunTick(10u + connectingTicks - 1u);
            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount);

            uint timeoutTick = 10u + connectingTicks;
            RunTick(timeoutTick);
            Assert.AreEqual(0, _wiring.Admitted.ReservedCount);
            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State, "re-armed from the timeout tick, not closed yet");

            RunTick(timeoutTick + idleTicks - 1u);
            Assert.AreEqual(ZoneState.Active, _wiring.Lifecycle.State);
            RunTick(timeoutTick + idleTicks);
            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
        }

        // ----- AC-3c: T-4 keeps the token ---------------------------------------------------------------

        [Test]
        public void test_leaving_active_releases_reservation_keeps_token_answers_late_handshake_and_drops_token_at_timeout()
        {
            Build();
            // One occupied player keeps the zone in Draining instead of closing at once.
            Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, new CharacterID(500u), new AccountID(500u), 3u, out _, out _));
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 10u);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount);

            _wiring.RequestQuit();
            RunTick(11u);

            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            CollectionAssert.AreEqual(new[] { new AccountID(ACCOUNT_A) }, _link.Released);
            Assert.AreEqual(0, _wiring.Admitted.ReservedCount);
            Assert.IsTrue(_wiring.TokenStore.TokenMatches(ACCOUNT_A, Token(1)), "token kept");

            _ticks.Now = 12u;
            Handshake(CLIENT, CHARACTER_A, Token(1));

            Assert.AreEqual(1, _clientOutbox.Full.Count);
            Assert.AreEqual(CLIENT, _clientOutbox.Full[0].Client);
            Assert.AreEqual(JoinRejectedReason.ZoneDoesNotExist, _clientOutbox.Full[0].Message.Reason);
            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _persistence.LoadCalls);

            RunTick(10u + CONNECTING_TICKS - 1u);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_A));
            RunTick(10u + CONNECTING_TICKS);
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_A), "token dropped at its own timeout");
            Assert.IsFalse(_wiring.Admitted.TryGetByCharacter(new CharacterID(CHARACTER_A), out _, out _));
            Assert.AreEqual(1, _link.Released.Count, "no second SessionReleased");
        }

        // ----- AC-4: visible name ------------------------------------------------------------------------

        [Test]
        public void test_visible_name_comes_from_the_character_record_and_the_login_name_never_reaches_the_zone()
        {
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);
            Handshake(CLIENT, CHARACTER_A, Token(1));
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone,
                new CharacterRecord { CharacterName = VISIBLE_NAME }));

            _ticks.Now = 2u;
            _queue.Drain(2u);

            // The registry / EntityState path needs the session and Movement epics' wiring (Movement's state source),
            // which a wiring test does not build; what the zone applies at admission is the record's name.
            Assert.IsNotNull(_applier.LastRecord);
            Assert.AreEqual(VISIBLE_NAME, _applier.LastRecord.CharacterName);
            Assert.AreNotEqual(LOGIN_NAME, _applier.LastRecord.CharacterName);
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(_applier.LastEntity, out ZonePlayerSlot slot));
            Assert.AreEqual(new CharacterID(CHARACTER_A), slot.Character);
            StringAssert.DoesNotContain(LOGIN_NAME, slot.ToString());
            StringAssert.DoesNotContain(LOGIN_NAME, new AccountID(ACCOUNT_A).ToString());
        }

        // ----- AC-5: admission timeout ---------------------------------------------------------------------

        [Test]
        public void test_unused_admission_times_out_dropping_entry_and_token_and_queues_exactly_one_session_released()
        {
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 5u);

            RunTick(5u + CONNECTING_TICKS - 1u);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_A));
            Assert.AreEqual(0, _link.Released.Count);

            RunTick(5u + CONNECTING_TICKS);
            RunTick(5u + CONNECTING_TICKS + 50u);

            Assert.IsFalse(_wiring.Admitted.TryGetByCharacter(new CharacterID(CHARACTER_A), out _, out _));
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_A));
            Assert.AreEqual(0, _wiring.Admitted.ReservedCount);
            CollectionAssert.AreEqual(new[] { new AccountID(ACCOUNT_A) }, _link.Released);
        }

        // ----- Code review 2026-11-06 ---------------------------------------------------------------------

        [Test]
        public void test_admission_timeout_during_an_inflight_load_does_not_drop_the_entry_and_the_slot_keeps_its_token()
        {
            // Arrange: admitted, handshake accepted, load still running when the deadline passes.
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);
            Handshake(CLIENT, CHARACTER_A, Token(1));
            Assert.IsTrue(_wiring.Admission.IsLoadPending(new CharacterID(CHARACTER_A)));

            // Act: the deadline passes mid-load, then the load completes.
            RunTick(1u + CONNECTING_TICKS + 5u);
            Assert.IsTrue(_wiring.Admitted.IsReserved(new AccountID(ACCOUNT_A)), "entry kept while its load runs");
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            _ticks.Now = 1u + CONNECTING_TICKS + 6u;
            _queue.Drain(_ticks.Now);
            RunTick(1u + CONNECTING_TICKS + 7u);

            // Assert: the player holds a slot with a live token, and the gateway was never told the session ended.
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.IsTrue(_wiring.TokenStore.IsTokenActive(ACCOUNT_A));
            Assert.AreEqual(0, _wiring.Admitted.EntryCount);
            Assert.AreEqual(0, _link.Released.Count);
        }

        [Test]
        public void test_admitting_a_character_held_by_another_account_releases_the_older_entry_and_its_token()
        {
            // Arrange
            Build();
            Admit(1u, ACCOUNT_A, CHARACTER_A, Token(1), 1u);

            // Act
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("the older entry is released"));
            Admit(2u, ACCOUNT_B, CHARACTER_A, Token(2), 2u);

            // Assert
            Assert.IsFalse(_wiring.TokenStore.IsTokenActive(ACCOUNT_A), "older token invalidated");
            CollectionAssert.AreEqual(new[] { new AccountID(ACCOUNT_A) }, _link.Released);
            Assert.IsTrue(_wiring.Admitted.TryGetByCharacter(new CharacterID(CHARACTER_A), out AccountID holder, out _));
            Assert.AreEqual(new AccountID(ACCOUNT_B), holder);
            Assert.AreEqual(1, _wiring.Admitted.ReservedCount);
        }

        [Test]
        public void test_set_reservation_sink_twice_throws()
        {
            // Arrange: the wiring already set the sink.
            Build();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _wiring.Admission.SetReservationSink(_wiring.Admitted));
            Assert.Throws<ArgumentNullException>(() => _wiring.Admission.SetReservationSink(null));
        }
    }
}
