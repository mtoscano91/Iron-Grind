using System;
using System.Collections.Generic;
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
                Assert.IsTrue(ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse message));
                Sent.Add(new KeyValuePair<uint, ZoneFullResponse>(clientId, message));
            }
        }

        private sealed class FakePersistence : ICharacterPersistence
        {
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public int SaveSessionCalls;
            public int OtherCalls;

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                var source = new TaskCompletionSource<CharacterLoadResult>();
                Loads.Add(source);
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

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct)
            {
                OtherCalls++;
                throw new NotSupportedException();
            }

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct)
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
            public int Calls;
            public EntityID LastEntity;

            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
            {
                Calls++;
                LastEntity = entityId;
                return Result;
            }
        }

        private sealed class FakeValidator : IZoneEntryTokenValidator
        {
            public bool Accept = true;
            public int Calls;

            public bool IsValid(in ZoneEntryRequest request)
            {
                Calls++;
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
        private FakeOutbox _outbox;
        private ZoneSlotPool _pool;
        private ZoneLifecycle _lifecycle;
        private TickCompletionQueue _queue;
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
            _outbox = new FakeOutbox();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener());
            _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            _lifecycle.MarkActive(0u);
            _queue = new TickCompletionQueue(WATCHDOG_TICKS);
            _svc = Build(_pool);
            _nextCharacter = 1000u;
            _tick = 0u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private ZoneEntryAdmission Build(ZoneSlotPool pool) =>
            new ZoneEntryAdmission(Zone, pool, _lifecycle, _persistence, _applier, _queue, _validator, _outbox, _continuation);

        private ZoneEntryRequest Request(uint clientId, uint zoneRaw = ZONE_RAW)
        {
            uint n = _nextCharacter++;
            return new ZoneEntryRequest(clientId, new AccountID(n + 5000u), new CharacterID(n), new ZoneID(zoneRaw), new byte[16]);
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

        private void AssertLastReply(uint clientId, JoinRejectedReason reason, byte retry, uint zoneRaw = ZONE_RAW)
        {
            Assert.IsNotEmpty(_outbox.Sent);
            KeyValuePair<uint, ZoneFullResponse> last = _outbox.Sent[_outbox.Sent.Count - 1];
            Assert.AreEqual(clientId, last.Key);
            Assert.AreEqual(new ZoneFullResponse(new ZoneID(zoneRaw), reason, retry), last.Value);
        }

        // ----- pre-check order ------------------------------------------------------------------

        [Test]
        public void test_wrong_zone_replies_zone_does_not_exist_with_sent_zone_and_starts_no_load()
        {
            var request = Request(CLIENT_A, OTHER_ZONE_RAW);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneDoesNotExist, 0, OTHER_ZONE_RAW);
            Assert.AreEqual(0, _persistence.Loads.Count);
            Assert.AreEqual(0, _validator.Calls, "the zone check runs before the token check");
        }

        [Test]
        public void test_rejecting_validator_replies_other_zero_starts_no_load_and_logs_once_per_instance()
        {
            var rejecting = new RejectingZoneEntryTokenValidator();
            var svc = new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence, _applier, _queue, rejecting, _outbox, _continuation);
            LogAssert.Expect(LogType.Warning, RejectingZoneEntryTokenValidator.NOT_IMPLEMENTED_MESSAGE);
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_B);

            svc.BeginEntry(in first);
            svc.BeginEntry(in second);

            Assert.AreEqual(2, _outbox.Sent.Count);
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
        }

        [Test]
        public void test_precheck_zone_full_replies_zone_full_with_retry_and_starts_no_load()
        {
            FillTo(ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            var request = Request(CLIENT_A);

            _svc.BeginEntry(in request);

            AssertLastReply(CLIENT_A, JoinRejectedReason.ZoneFull, ZoneEntryAdmission.ZONE_FULL_RETRY_AFTER_SECONDS);
            Assert.AreEqual(0, _persistence.Loads.Count);
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
        }

        [Test]
        public void test_load_failure_code_replies_load_character_failed_and_allocates_no_slot()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.CharacterNotFound, ZoneID.Invalid));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("LoadCharacter failed"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
            Assert.AreEqual(0, _svc.PendingCount);
        }

        [Test]
        public void test_faulted_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetException(new InvalidOperationException("db"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("LoadCharacter failed"));
            LogAssert.ignoreFailingMessages = false;

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_canceled_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetCanceled();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("LoadCharacter failed"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_timed_out_task_replies_load_character_failed()
        {
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _queue.Drain(1u);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("watchdog"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("LoadCharacter failed"));
            LogAssert.ignoreFailingMessages = false;

            _queue.Drain(1u + WATCHDOG_TICKS + 5u);

            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
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

            Assert.AreEqual(ZoneSlotPool.MAX_PLAYERS_PER_ZONE, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_A, _continuation.Admitted[0].Client);
            Assert.AreEqual(1, _outbox.Sent.Count);
            AssertLastReply(CLIENT_B, JoinRejectedReason.ZoneFull, ZoneEntryAdmission.ZONE_FULL_RETRY_AFTER_SECONDS);
            Assert.AreEqual(1, _applier.Calls, "EC-ZI-4: the losing record is discarded");
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _persistence.OtherCalls);
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
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("EntityIDCounterExhausted"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("TryAllocatePlayer failed below the cap"));

            Drain();

            AssertLastReply(CLIENT_A, JoinRejectedReason.Other, 0);
            Assert.AreEqual(1, pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _applier.Calls);
        }

        // ----- apply / success ------------------------------------------------------------------

        [Test]
        public void test_apply_fails_releases_slot_replies_load_character_failed_and_does_not_hand_off()
        {
            _applier.Result = CharacterLoadCode.InventoryNotRestored;
            var request = Request(CLIENT_A);
            _svc.BeginEntry(in request);
            _persistence.Loads[0].SetResult(Loaded());
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ApplyLoadedRecord returned"));

            Drain();

            Assert.AreEqual(1, _applier.Calls);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            AssertLastReply(CLIENT_A, JoinRejectedReason.LoadCharacterFailed, 0);
            Assert.IsEmpty(_continuation.Admitted);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.IsFalse(_pool.TryGetPlayer(_applier.LastEntity, out _));
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
            Assert.IsEmpty(_outbox.Sent);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
        }

        // ----- duplicate / drop -----------------------------------------------------------------

        [Test]
        public void test_duplicate_handshake_while_pending_is_ignored_with_one_warning_and_one_load()
        {
            var first = Request(CLIENT_A);
            var second = Request(CLIENT_A);
            var third = Request(CLIENT_A);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("EntryHandshakeDuplicate"));

            _svc.BeginEntry(in first);
            _svc.BeginEntry(in second);
            _svc.BeginEntry(in third);

            Assert.AreEqual(1, _persistence.Loads.Count);
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.IsEmpty(_outbox.Sent);
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
        }

        [Test]
        public void test_fresh_entry_after_drop_and_add_client_is_admitted_and_stale_callback_does_not_interfere()
        {
            var stale = Request(CLIENT_A);
            _svc.BeginEntry(in stale);
            _svc.RemoveClient(CLIENT_A);
            _svc.AddClient(CLIENT_A);
            var fresh = Request(CLIENT_A);
            _svc.BeginEntry(in fresh);
            Assert.AreEqual(2, _persistence.Loads.Count);
            _persistence.Loads[0].SetResult(Loaded());
            _persistence.Loads[1].SetResult(Loaded());

            Drain();

            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.IsTrue(_pool.TryGetPlayer(_continuation.Admitted[0].Entity, out ZonePlayerSlot slot));
            Assert.AreEqual(fresh.Character, slot.Character);
            Assert.AreEqual(0, _svc.PendingCount);
        }

        [Test]
        public void test_constructor_rejects_invalid_zone_and_null_arguments()
        {
            Assert.Throws<ArgumentException>(() => new ZoneEntryAdmission(ZoneID.Invalid, _pool, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, _continuation));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryAdmission(Zone, null, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, _continuation));
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence,
                _applier, _queue, _validator, _outbox, null));
        }
    }
}
