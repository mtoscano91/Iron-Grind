using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 009 — <see cref="ZoneEntryAdmission"/> (CR-ZI-7, CR-ZI-8 steps 3-5,
    /// EC-ZI-1, EC-ZI-4, AC-ZI-4, AC-ZI-27). Real pool, lifecycle and <see cref="TickCompletionQueue"/>; fake
    /// persistence (completed by hand through <see cref="TaskCompletionSource{TResult}"/>), applier, outbox and
    /// continuation. No wall-clock time, no I/O.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_EntryAdmission_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint OTHER_ZONE_RAW = 99u;
        private const uint CLIENT_A = 42u;
        private const uint CLIENT_B = 43u;
        private const int WATCHDOG_TICKS = 5;
        private const uint TIMEOUT_MARGIN_TICKS = 5u;
        private const int TOKEN_LENGTH = SessionTokenStore.TOKEN_LENGTH_BYTES;
        private const byte CUSTOM_RETRY_SECONDS = 7;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeLifecycleListener : IZoneLifecycleListener
        {
            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick) { }
        }

        private sealed class FakeOutbox : IClientMessageOutbox
        {
            public readonly List<KeyValuePair<uint, ZoneFullResponse>> Sent = new List<KeyValuePair<uint, ZoneFullResponse>>();

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Assert.AreEqual(ZoneFullResponse.MessageTypeId, messageTypeId);
                Assert.IsFalse(isCapExempt, "admission replies count against the outbox cap");
                Assert.IsTrue(ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse message));
                Sent.Add(new KeyValuePair<uint, ZoneFullResponse>(clientId, message));
            }
        }

        /// <summary>Delegates to a real queue, records the cancellation sources and can be told to throw on Track.</summary>
        private sealed class RecordingQueue : ITickCompletionQueue
        {
            private readonly TickCompletionQueue _inner;
            public readonly List<CancellationTokenSource> Tracked = new List<CancellationTokenSource>();
            public bool ThrowOnTrack;

            public RecordingQueue(TickCompletionQueue inner) => _inner = inner;

            public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
            {
                if (ThrowOnTrack)
                {
                    throw new InvalidOperationException("track failed");
                }

                Tracked.Add(cancellation);
                _inner.Track(task, cancellation, onCompletedOnTick);
            }

            public void Drain(uint currentTick) => _inner.Drain(currentTick);

            public int InFlightCount => _inner.InFlightCount;

            public void DrainOnShutdown(TimeSpan timeout) => _inner.DrainOnShutdown(timeout);
        }

        private sealed class FakePersistence : ICharacterPersistence
        {
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public bool ThrowOnLoad;
            public int SaveSessionCalls;
            public int OtherCalls;

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException("load failed synchronously");
                }

                var source = new TaskCompletionSource<CharacterLoadResult>();
                Loads.Add(source);
                Tokens.Add(ct);
                return source.Task;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
            {
                SaveSessionCalls++;
                return Task.FromResult(CharacterSaveResult.Success);
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct)
            {
                SaveSessionCalls++;
                return Task.FromResult(CharacterSaveResult.Success);
            }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                OtherCalls++;
                return Task.FromResult(CharacterSaveResult.Success);
            }

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct)
            {
                OtherCalls++;
                return Task.CompletedTask;
            }

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode Result = CharacterLoadCode.Success;
            public bool ThrowOnApply;
            public int Calls;
            public EntityID LastEntity;

            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
            {
                Calls++;
                LastEntity = entityId;
                if (ThrowOnApply)
                {
                    throw new InvalidOperationException("apply failed");
                }

                return Result;
            }
        }

        private sealed class FakeValidator : IZoneEntryTokenValidator
        {
            public bool Accept = true;
            public int Calls;
            public ZoneEntryRequest LastRequest;

            public bool IsValid(in ZoneEntryRequest request)
            {
                Calls++;
                LastRequest = request;
                return Accept;
            }
        }

        private sealed class FakeContinuation : IZoneEntryContinuation
        {
            public readonly List<(uint Client, byte Slot, EntityID Entity)> Admitted = new List<(uint, byte, EntityID)>();

            public void OnPlayerAdmitted(uint clientId, byte slot, EntityID entityId) => Admitted.Add((clientId, slot, entityId));
        }

        private FakePersistence _persistence;
        private FakeApplier _applier;
        private FakeValidator _validator;
        private FakeContinuation _continuation;
        private ZoneConnectionCharacterDirectory _directory;
        private FakeOutbox _outbox;
        private ZoneSlotPool _pool;
        private ZoneLifecycle _lifecycle;
        private RecordingQueue _queue;
        private ZoneEntryAdmission _svc;
        private uint _nextCharacter;
        private uint _tick;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _persistence = new FakePersistence();
            _applier = new FakeApplier();
            _validator = new FakeValidator();
            _continuation = new FakeContinuation();
            _directory = new ZoneConnectionCharacterDirectory();
            _outbox = new FakeOutbox();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener());
            _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            _lifecycle.MarkActive(0u);
            _queue = new RecordingQueue(new TickCompletionQueue(WATCHDOG_TICKS));
            _svc = Build(_pool);
            _nextCharacter = 1000u;
            _tick = 0u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private ZoneEntryAdmission Build(ZoneSlotPool pool, byte retryAfterSeconds = ZoneEntryAdmission.ZONE_FULL_RETRY_AFTER_SECONDS) =>
            new ZoneEntryAdmission(Zone, pool, _lifecycle, _persistence, _applier, _queue, _validator, _outbox, _continuation, _directory, retryAfterSeconds);

        private ZoneEntryRequest Request(uint clientId, uint zoneRaw = ZONE_RAW)
        {
            uint n = _nextCharacter++;
            return new ZoneEntryRequest(clientId, new AccountID(n + 5000u), new CharacterID(n), new ZoneID(zoneRaw), new byte[TOKEN_LENGTH]);
        }

        private EntityID AddPlayer(uint clientId)
        {
            uint n = _nextCharacter++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u), clientId, out _, out EntityID id));
            return id;
        }

        private void FillTo(int count)
        {
            while (_pool.OccupiedPlayerCount < count)
            {
                AddPlayer(1u);
            }
        }

        private static CharacterLoadResult Loaded() => new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord());

        private void Drain()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private static Regex Rx(string pattern) => new Regex(pattern);

        private void AssertLastReply(uint clientId, JoinRejectedReason reason, byte retry, uint zoneRaw = ZONE_RAW)
        {
            Assert.IsNotEmpty(_outbox.Sent);
            KeyValuePair<uint, ZoneFullResponse> last = _outbox.Sent[_outbox.Sent.Count - 1];
            Assert.AreEqual(clientId, last.Key);
            Assert.AreEqual(new ZoneFullResponse(new ZoneID(zoneRaw), reason, retry), last.Value);
        }

        private static void AssertDisposed(CancellationTokenSource cts) =>
            Assert.Throws<ObjectDisposedException>(() => { CancellationToken ignored = cts.Token; });

        // ----- pre-check order ------------------------------------------------------------------

        [Test]
        public void test_wrong_zone_replies_zone_does_not_exist_with_sent_zone_and_starts_no_load()
        {
            var request = Request(CLIENT_A, OTHER_ZONE_RAW);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneDoesNotExist, 0, OTHER_ZONE_RAW);
            Assert.AreEqual(0, _persistence.Loads.Count);
            Assert.AreEqual(0, _validator.Calls, "the zone check runs before the token check");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_rejecting_validator_replies_other_zero_starts_no_load_and_logs_once_per_instance()
        {
            var rejecting = new RejectingZoneEntryTokenValidator();
            var svc = new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence, _applier, _queue, rejecting, _outbox, _continuation, _directory);
            LogAssert.Expect(LogType.Warning, RejectingZoneEntryTokenValidator.NOT_IMPLEMENTED_MESSAGE);
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_B);

            svc.BeginEntry(in first);
            svc.BeginEntry(in second);

            Assert.AreEqual(2, _outbox.Sent.Count);
            Assert.AreEqual(CLIENT_A, _outbox.Sent[0].Key);
            Assert.AreEqual(new ZoneFullResponse(Zone, JoinRejectedReason.Other, 0), _outbox.Sent[0].Value);
            AssertLastReply(CLIENT_B, JoinRejectedReason.Other, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_rejected_token_replies_other_and_keeps_zone_state_untouched()
        {
            _validator.Accept = false;
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.Other, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validator_receives_the_full_request_once_on_success()
        {
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            Assert.AreEqual(1, _validator.Calls);
            Assert.AreEqual(request.ClientId, _validator.LastRequest.ClientId);
            Assert.AreEqual(request.Account, _validator.LastRequest.Account);
            Assert.AreEqual(request.Character, _validator.LastRequest.Character);
            Assert.AreEqual(request.TargetZone, _validator.LastRequest.TargetZone);
            Assert.AreSame(request.SessionToken, _validator.LastRequest.SessionToken);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_precheck_zone_loading_replies_zone_does_not_exist_and_starts_no_load()
        {
            _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            _svc = Build(_pool);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneDoesNotExist, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_precheck_zone_full_replies_zone_full_with_retry_and_starts_no_load()
        {
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneFull, ZoneEntryAdmission.ZONE_FULL_RETRY_AFTER_SECONDS);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_precheck_zone_full_carries_the_configured_retry_after_seconds()
        {
            _svc = Build(_pool, CUSTOM_RETRY_SECONDS);
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneFull, CUSTOM_RETRY_SECONDS);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pending_client_with_other_zone_handshake_is_ignored_as_duplicate_without_reply()
        {
            var first = Request(CLIENT_A);
            var wrongZone = Request(CLIENT_A, OTHER_ZONE_RAW);
            _svc.BeginEntry(in first);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));

            _svc.BeginEntry(in wrongZone);

            Assert.IsEmpty(_outbox.Sent, "duplicate beats wrong zone");
            Assert.AreEqual(1, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_rejected_token_beats_zone_loading_precheck()
        {
            _validator.Accept = false;
            _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            _svc = Build(_pool);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.Other, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_rejected_token_beats_zone_full_precheck()
        {
            _validator.Accept = false;
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.Other, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_draining_zone_at_cap_replies_zone_does_not_exist_not_zone_full()
        {
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            _lifecycle.OnOccupiedPlayerSlotsChanged(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, 0u);
            _lifecycle.RequestQuit(1u);
            Assert.AreEqual(ZoneState.Draining, _lifecycle.State);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneDoesNotExist, 0);
            Assert.AreEqual(0, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- load outcome ---------------------------------------------------------------------

        [Test]
        public void test_load_started_without_reply_and_pending_until_drained()
        {
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            Assert.AreEqual(1, _persistence.Loads.Count);
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_load_failure_code_replies_load_character_failed_and_allocates_no_slot()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.CharacterNotFound, ZoneID.Invalid));
            LogAssert.Expect(LogType.Warning, Rx("LoadCharacter failed"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_success_code_with_null_record_replies_load_character_failed_and_logs_null_record()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, null));
            LogAssert.Expect(LogType.Warning, Rx("NullRecord"));

            Drain();

            Assert.AreEqual(1, _outbox.Sent.Count);
            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_faulted_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetException(new InvalidOperationException("db"));
            LogAssert.Expect(LogType.Warning, Rx("LoadCharacter failed"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_canceled_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetCanceled();
            LogAssert.Expect(LogType.Warning, Rx("LoadCharacter failed"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_timed_out_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _queue.Drain(1u);
            LogAssert.Expect(LogType.Error, Rx("watchdog"));
            LogAssert.Expect(LogType.Warning, Rx("LoadCharacter failed"));

            _queue.Drain(1u + WATCHDOG_TICKS + TIMEOUT_MARGIN_TICKS);

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- cancellation source lifetime -----------------------------------------------------

        [Test]
        public void test_token_given_to_load_belongs_to_the_tracked_source()
        {
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            Assert.AreEqual(1, _queue.Tracked.Count);
            Assert.AreEqual(_queue.Tracked[0].Token, _persistence.Tokens[0]);
            Assert.IsFalse(_persistence.Tokens[0].IsCancellationRequested);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_source_is_disposed_after_a_successful_callback()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            AssertDisposed(_queue.Tracked[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_source_is_disposed_after_a_failed_load_callback()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetException(new InvalidOperationException("db"));
            LogAssert.Expect(LogType.Warning, Rx("LoadCharacter failed"));

            Drain();

            AssertDisposed(_queue.Tracked[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_source_is_disposed_after_a_dropped_connection_callback()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _svc.RemoveClient(CLIENT_A);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            AssertDisposed(_queue.Tracked[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_load_character_throwing_synchronously_rethrows_cleans_up_and_allows_a_fresh_entry()
        {
            _persistence.ThrowOnLoad = true;
            var request = Request(CLIENT_A);

            Assert.Throws<InvalidOperationException>(() => _svc.BeginEntry(in request));

            Assert.AreEqual(0, _svc.PendingCount);
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _queue.Tracked.Count);
            _persistence.ThrowOnLoad = false;
            var retry = Request(CLIENT_A);

            _svc.BeginEntry(in retry);

            Assert.AreEqual(1, _persistence.Loads.Count, "not treated as a duplicate");
            Assert.AreEqual(1, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_track_throwing_rethrows_cleans_up_cancels_but_does_not_dispose_the_source()
        {
            _queue.ThrowOnTrack = true;
            var request = Request(CLIENT_A);

            Assert.Throws<InvalidOperationException>(() => _svc.BeginEntry(in request));

            Assert.AreEqual(0, _svc.PendingCount);
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(1, _persistence.Tokens.Count);
            Assert.IsTrue(_persistence.Tokens[0].IsCancellationRequested);
            Assert.DoesNotThrow(() => { bool ignored = _persistence.Tokens[0].IsCancellationRequested; });
            LogAssert.NoUnexpectedReceived();
        }

        // ----- re-check -------------------------------------------------------------------------

        [Test]
        public void test_zone_draining_while_load_pending_replies_zone_does_not_exist_with_no_slot_and_no_save()
        {
            AddPlayer(1u);
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _lifecycle.OnOccupiedPlayerSlotsChanged(1, 0u);
            _lifecycle.RequestQuit(1u);
            Assert.AreEqual(ZoneState.Draining, _lifecycle.State);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneDoesNotExist, 0);
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
            Assert.IsEmpty(_continuation.Admitted);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_two_loads_complete_in_one_drain_at_49_of_50_admit_one_and_reject_one_with_zone_full()
        {
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE - 1);
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_B);
            _svc.BeginEntry(in first);
            _svc.BeginEntry(in second);
            Assert.AreEqual(2, _persistence.Loads.Count, "both pass the pre-check at 49 / 50");
            _persistence.Loads[0].SetResult(Loaded());
            _persistence.Loads[1].SetResult(Loaded());

            Drain();

            // A is admitted first only because the queue delivers completions in tracking order.
            Assert.AreEqual(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_A, _continuation.Admitted[0].Client);
            Assert.AreEqual(1, _outbox.Sent.Count);
            AssertLastReply(CLIENT_B, JoinRejectedReason.ZoneFull, ZoneEntryAdmission.ZONE_FULL_RETRY_AFTER_SECONDS);
            Assert.AreEqual(1, _applier.Calls, "EC-ZI-4: the losing record is discarded");
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_recheck_zone_full_carries_the_configured_retry_after_seconds()
        {
            _svc = Build(_pool, CUSTOM_RETRY_SECONDS);
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE - 1);
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_B);
            _svc.BeginEntry(in first);
            _svc.BeginEntry(in second);
            _persistence.Loads[0].SetResult(Loaded());
            _persistence.Loads[1].SetResult(Loaded());

            Drain();

            AssertLastReply(CLIENT_B, JoinRejectedReason.ZoneFull, CUSTOM_RETRY_SECONDS);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_entity_counter_exhausted_replies_other_and_logs_error()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), firstEntityId: uint.MaxValue);
            _svc = Build(pool);
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(1u), 1u, out _, out _));
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());
            LogAssert.Expect(LogType.Error, Rx("EntityIDCounterExhausted"));
            LogAssert.Expect(LogType.Error, Rx("TryAllocatePlayer failed below the cap"));

            Drain();

            Assert.AreEqual(1, _outbox.Sent.Count);
            AssertLastReply(CLIENT_A, JoinRejectedReason.Other, 0);
            Assert.AreEqual(1, pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            Assert.IsEmpty(_continuation.Admitted);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- apply / success ------------------------------------------------------------------

        [Test]
        public void test_apply_fails_releases_slot_replies_load_character_failed_and_does_not_hand_off()
        {
            _applier.Result = CharacterLoadCode.InventoryNotRestored;
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());
            LogAssert.Expect(LogType.Warning, Rx("ApplyLoadedRecord returned"));

            Drain();

            Assert.AreEqual(1, _applier.Calls);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.IsEmpty(_continuation.Admitted);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
            Assert.IsFalse(_pool.TryGetPlayer(_applier.LastEntity, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_throws_releases_slot_logs_error_and_replies_load_character_failed()
        {
            _applier.ThrowOnApply = true;
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryAdmission\] ApplyLoadedRecord threw"));

            Drain();

            Assert.AreEqual(1, _applier.Calls);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _outbox.Sent.Count);
            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.IsEmpty(_continuation.Admitted);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_success_writes_connected_slot_applies_record_and_calls_continuation_once()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _continuation.Admitted.Count);
            var admitted = _continuation.Admitted[0];
            Assert.AreEqual(CLIENT_A, admitted.Client);
            Assert.AreEqual(_applier.LastEntity, admitted.Entity);
            Assert.IsTrue(_pool.TryGetPlayer(admitted.Entity, out ZonePlayerSlot slot));
            Assert.AreEqual(request.Character, slot.Character);
            Assert.AreEqual(request.Account, slot.Account);
            Assert.AreEqual(CLIENT_A, slot.ClientId);
            Assert.AreEqual(ZoneSessionState.Connected, slot.State);
            Assert.IsTrue(_pool.TryGetPlayerSlot(admitted.Entity, out byte index));
            Assert.AreEqual(index, admitted.Slot);
            Assert.AreEqual(1, _applier.Calls);
            Assert.AreEqual(1, _validator.Calls);
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- duplicate / drop -----------------------------------------------------------------

        [Test]
        public void test_duplicate_handshake_while_pending_is_ignored_with_one_warning_and_one_load()
        {
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_A);
            var third = Request(CLIENT_A);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));

            _svc.BeginEntry(in first);
            _svc.BeginEntry(in second);
            _svc.BeginEntry(in third);

            Assert.AreEqual(1, _persistence.Loads.Count);
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.IsEmpty(_outbox.Sent);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_warning_is_emitted_again_after_the_load_completed()
        {
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));
            var first = Request(CLIENT_A);
            _svc.BeginEntry(in first);
            _svc.BeginEntry(in first);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();
            var second = Request(CLIENT_A);
            _svc.BeginEntry(in second);

            _svc.BeginEntry(in second);

            Assert.AreEqual(2, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_warning_is_emitted_again_after_remove_client()
        {
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));
            var first = Request(CLIENT_A);
            _svc.BeginEntry(in first);
            _svc.BeginEntry(in first);
            _svc.RemoveClient(CLIENT_A);
            _svc.AddClient(CLIENT_A);
            var second = Request(CLIENT_A);
            _svc.BeginEntry(in second);

            _svc.BeginEntry(in second);

            Assert.AreEqual(2, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connection_drop_while_pending_sends_nothing_allocates_nothing_and_skips_apply()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _svc.RemoveClient(CLIENT_A);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            Assert.IsEmpty(_continuation.Admitted);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stale_callback_completing_first_leaves_the_fresh_entry_pending_and_untouched()
        {
            var stale = Request(CLIENT_A);
            _svc.BeginEntry(in stale);
            _svc.RemoveClient(CLIENT_A);
            _svc.AddClient(CLIENT_A);
            var fresh = Request(CLIENT_A);
            _svc.BeginEntry(in fresh);
            Assert.AreEqual(2, _persistence.Loads.Count);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            Assert.AreEqual(1, _svc.PendingCount, "the stale callback must not clear the fresh pending entry");
            Assert.IsEmpty(_outbox.Sent);
            Assert.IsEmpty(_continuation.Admitted);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeDuplicate"));
            var again = Request(CLIENT_A);
            _svc.BeginEntry(in again);
            Assert.AreEqual(2, _persistence.Loads.Count, "still pending, so a further handshake is a duplicate");
            _persistence.Loads[1].SetResult(Loaded());

            Drain();

            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.IsTrue(_pool.TryGetPlayer(_continuation.Admitted[0].Entity, out ZonePlayerSlot slot));
            Assert.AreEqual(fresh.Character, slot.Character);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_stale_callback_completing_after_the_fresh_one_sends_and_allocates_nothing()
        {
            var stale = Request(CLIENT_A);
            _svc.BeginEntry(in stale);
            _svc.RemoveClient(CLIENT_A);
            _svc.AddClient(CLIENT_A);
            var fresh = Request(CLIENT_A);
            _svc.BeginEntry(in fresh);
            _persistence.Loads[1].SetResult(Loaded());
            Drain();
            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(1, _applier.Calls);
            _persistence.Loads[0].SetResult(Loaded());

            Drain();

            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _applier.Calls);
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- pending count --------------------------------------------------------------------

        [Test]
        public void test_pending_count_drops_to_zero_right_after_remove_client_before_drain()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            Assert.AreEqual(1, _svc.PendingCount);

            _svc.RemoveClient(CLIENT_A);

            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pending_count_counts_two_clients_and_ignores_removal_of_an_unknown_client()
        {
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_B);
            _svc.BeginEntry(in first);
            _svc.BeginEntry(in second);
            Assert.AreEqual(2, _svc.PendingCount);

            Assert.DoesNotThrow(() => _svc.RemoveClient(9999u));

            Assert.AreEqual(2, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructor_rejects_invalid_zone_and_null_arguments()
        {
            Assert.Throws<ArgumentException>(() => new ZoneEntryAdmission(ZoneID.Invalid, _pool, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, _continuation, _directory));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryAdmission(Zone, null, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, _continuation, _directory));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, null, _directory));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, _continuation, null));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- second handshake on an admitted connection (Story 010) ---------------------------

        [Test]
        public void test_second_handshake_on_admitted_connection_is_ignored_with_one_warning_no_reply_no_load()
        {
            var first = Request(CLIENT_A);
            _svc.BeginEntry(in first);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();
            _directory.Add(CLIENT_A, first.Character);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeAlreadyAdmitted"));
            var second = Request(CLIENT_A);
            var third = Request(CLIENT_A);

            _svc.BeginEntry(in second);
            _svc.BeginEntry(in third);

            Assert.AreEqual(1, _persistence.Loads.Count, "no further load");
            Assert.IsEmpty(_outbox.Sent, "no reply");
            Assert.AreEqual(0, _svc.PendingCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_handshake_after_remove_client_and_directory_remove_is_a_fresh_entry_and_rewarns_on_next_admit()
        {
            var first = Request(CLIENT_A);
            _svc.BeginEntry(in first);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();
            _directory.Add(CLIENT_A, first.Character);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeAlreadyAdmitted"));
            var ignored = Request(CLIENT_A);
            _svc.BeginEntry(in ignored);
            _svc.RemoveClient(CLIENT_A);
            _directory.Remove(CLIENT_A);
            var fresh = Request(CLIENT_A);

            _svc.BeginEntry(in fresh);

            Assert.AreEqual(2, _persistence.Loads.Count, "a fresh entry starts a load");
            Assert.AreEqual(1, _svc.PendingCount);
            _directory.Add(CLIENT_A, fresh.Character);
            LogAssert.Expect(LogType.Warning, Rx("EntryHandshakeAlreadyAdmitted"));
            _svc.RemoveClient(CLIENT_A);
            _directory.Add(CLIENT_A, fresh.Character);
            var again = Request(CLIENT_A);
            _svc.BeginEntry(in again);
            Assert.AreEqual(2, _persistence.Loads.Count);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
