using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// TD-078 (Zone Instancing Story 014): a second handshake for a character whose load is in flight on another
    /// connection drops the first pending entry and closes that connection with no message. Real pool, lifecycle,
    /// <see cref="TickCompletionQueue"/> and <see cref="ZoneEntryReclamation"/>; fake persistence (completed by hand),
    /// applier, outbox, disconnector and continuation.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_EntryAdmissionDuplicateLoad_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_A = 42u;
        private const uint CLIENT_B = 43u;
        private const uint CHARACTER_RAW = 1000u;
        private const uint OTHER_CHARACTER_RAW = 1001u;
        private const int WATCHDOG_TICKS = 5;
        private const int TOKEN_LENGTH = SessionTokenStore.TOKEN_LENGTH_BYTES;
        private static readonly Regex SUPERSEDED = new Regex("EntryHandshakeSupersededInFlight");

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
            public int Count;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt) => Count++;
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public readonly List<ulong> Closed = new List<ulong>();

            public void DisconnectClient(ulong transportClientId) => Closed.Add(transportClientId);
        }

        private sealed class NoopSink : IZoneEntryReclaimSessionSink
        {
            public void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned) { }
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = PartyID.Uninitialized;
                return false;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId) { }
        }

        private sealed class FakeValidator : IZoneEntryTokenValidator
        {
            public bool IsValid(in ZoneEntryRequest request) => true;
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class FakeContinuation : IZoneEntryContinuation
        {
            public readonly List<(uint Client, byte Slot, EntityID Entity)> Admitted = new List<(uint, byte, EntityID)>();

            public void OnPlayerAdmitted(uint clientId, byte slot, EntityID entityId) => Admitted.Add((clientId, slot, entityId));
        }

        private sealed class FakePersistence : ICharacterPersistence
        {
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
            public int SaveSessionCalls;
            public bool ThrowOnLoad;

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException("load failed");
                }

                Tokens.Add(ct);
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

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private FakePersistence _persistence;
        private FakeContinuation _continuation;
        private FakeOutbox _outbox;
        private FakeDisconnector _disconnector;
        private ZoneConnectionCharacterDirectory _directory;
        private ZoneSlotPool _pool;
        private ZoneLifecycle _lifecycle;
        private TickCompletionQueue _queue;
        private ZoneEntryAdmission _svc;
        private uint _tick;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            var ticks = new FakeTicks();
            _persistence = new FakePersistence();
            _continuation = new FakeContinuation();
            _outbox = new FakeOutbox();
            _disconnector = new FakeDisconnector();
            _directory = new ZoneConnectionCharacterDirectory();
            _pool = new ZoneSlotPool(Zone, ticks, new FakePoolListener());
            var lifecycle = _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            lifecycle.MarkActive(0u);
            _queue = new TickCompletionQueue(WATCHDOG_TICKS);
            var roster = new ZoneTickRoster(_pool, ticks);
            var reclamation = new ZoneEntryReclamation(Zone, _pool, _directory, roster, new FakeParty(), _outbox,
                _disconnector, new NoopSink(), ticks);
            _svc = new ZoneEntryAdmission(Zone, _pool, lifecycle, _persistence, new FakeApplier(), _queue, new FakeValidator(),
                _outbox, _continuation, _directory, reclamation: reclamation);
            _tick = 0u;
        }

        [TearDown]
        public void TearDown()
        {
            // Loads a test left pending would raise the queue's in-flight-at-shutdown alert: finish them first.
            foreach (TaskCompletionSource<CharacterLoadResult> load in _persistence.Loads)
            {
                load.TrySetResult(Loaded());
            }

            _queue.DrainOnShutdown(TimeSpan.FromMilliseconds(200));
        }

        private static ZoneEntryRequest Request(uint clientId, uint characterRaw) =>
            new ZoneEntryRequest(clientId, new AccountID(characterRaw + 5000u), new CharacterID(characterRaw), Zone, new byte[TOKEN_LENGTH]);

        private static CharacterLoadResult Loaded() => new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord());

        private void Drain()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private void Begin(uint clientId, uint characterRaw)
        {
            var request = Request(clientId, characterRaw);
            _svc.BeginEntry(in request);
        }

        [Test]
        public void test_duplicate_load_second_handshake_closes_first_ignores_its_completion_and_admits_second()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, SUPERSEDED);

            Begin(CLIENT_B, CHARACTER_RAW);

            CollectionAssert.AreEqual(new ulong[] { CLIENT_A }, _disconnector.Closed);
            Assert.AreEqual(0, _outbox.Count, "the first connection is closed with no message");
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.AreEqual(1, _svc.PendingCharacterCount);
            Assert.AreEqual(2, _persistence.Loads.Count);

            _persistence.Loads[0].SetResult(Loaded());
            Drain();

            Assert.AreEqual(0, _pool.OccupiedPlayerCount, "the dropped load allocates nothing");
            Assert.AreEqual(0, _continuation.Admitted.Count);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _outbox.Count);
            Assert.AreEqual(1, _svc.PendingCount, "the second load is still pending");
            Assert.AreEqual(1, _svc.PendingCharacterCount, "the dropped completion leaves the second entry's key alone");

            _persistence.Loads[1].SetResult(Loaded());
            Drain();

            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_B, _continuation.Admitted[0].Client);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_second_completes_before_dropped_first_character_ends_with_one_slot()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, SUPERSEDED);
            Begin(CLIENT_B, CHARACTER_RAW);

            _persistence.Loads[1].SetResult(Loaded());
            Drain();
            _persistence.Loads[0].SetResult(Loaded());
            Drain();

            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_B, _continuation.Admitted[0].Client);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_dropped_first_load_fails_later_nothing_happens()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, SUPERSEDED);
            Begin(CLIENT_B, CHARACTER_RAW);

            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.CharacterNotFound, Zone, null));
            Drain();

            Assert.AreEqual(0, _outbox.Count, "no LoadCharacterFailed reply to the dropped connection");
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
            Assert.AreEqual(1, _svc.PendingCount, "the second entry is untouched");
            Assert.AreEqual(1, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_different_characters_in_flight_are_both_admitted()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            Begin(CLIENT_B, OTHER_CHARACTER_RAW);

            Assert.AreEqual(2, _svc.PendingCount);
            Assert.IsEmpty(_disconnector.Closed);

            _persistence.Loads[0].SetResult(Loaded());
            _persistence.Loads[1].SetResult(Loaded());
            Drain();

            Assert.AreEqual(2, _pool.OccupiedPlayerCount);
            Assert.AreEqual(2, _continuation.Admitted.Count);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_after_first_completed_and_holds_slot_case_b_reclaims_unchanged()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();
            Assert.AreEqual(1, _continuation.Admitted.Count);
            _directory.Add(CLIENT_A, new CharacterID(CHARACTER_RAW));

            Begin(CLIENT_B, CHARACTER_RAW);

            CollectionAssert.AreEqual(new ulong[] { CLIENT_A }, _disconnector.Closed, "closed once, by reclamation case (b)");
            Assert.AreEqual(0, _pool.OccupiedPlayerCount, "the entering slot was reclaimed");
            Assert.AreEqual(1, _svc.PendingCount);

            _persistence.Loads[1].SetResult(Loaded());
            Drain();

            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(CLIENT_B, _continuation.Admitted[1].Client);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_pending_map_empty_after_all_completions_and_removals()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, SUPERSEDED);
            Begin(CLIENT_B, CHARACTER_RAW);
            Begin(CLIENT_A, OTHER_CHARACTER_RAW);

            _svc.RemoveClient(CLIENT_A);
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.AreEqual(1, _svc.PendingCharacterCount, "RemoveClient dropped the other character's key too");

            _persistence.Loads[0].SetResult(Loaded());
            _persistence.Loads[1].SetResult(Loaded());
            _persistence.Loads[2].SetResult(Loaded());
            Drain();

            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            Assert.AreEqual(1, _pool.OccupiedPlayerCount, "only the surviving handshake was admitted");

            // The character key is gone too: the same character may enter again without a superseded warning.
            _svc.RemoveClient(CLIENT_B);
            Begin(CLIENT_A, CHARACTER_RAW);
            Assert.IsEmpty(_disconnector.Closed.GetRange(1, _disconnector.Closed.Count - 1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_remove_client_while_pending_clears_both_keys()
        {
            Begin(CLIENT_A, CHARACTER_RAW);

            _svc.RemoveClient(CLIENT_A);

            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();
            Assert.AreEqual(0, _pool.OccupiedPlayerCount, "the dropped completion allocates nothing");
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_load_character_throwing_synchronously_leaves_no_pending_key()
        {
            _persistence.ThrowOnLoad = true;
            var request = Request(CLIENT_A, CHARACTER_RAW);

            Assert.Throws<InvalidOperationException>(() => _svc.BeginEntry(in request));

            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            Assert.AreEqual(0, _outbox.Count, "no reply to the client");
        }

        [Test]
        public void test_duplicate_load_same_client_resends_same_character_uses_the_duplicate_rule_without_closing()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, new Regex("EntryHandshakeDuplicate"));

            Begin(CLIENT_A, CHARACTER_RAW);

            Assert.IsEmpty(_disconnector.Closed, "no self-close");
            Assert.AreEqual(1, _persistence.Loads.Count, "the resend starts no second load");
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.AreEqual(1, _svc.PendingCharacterCount);
            Assert.IsFalse(_persistence.Tokens[0].IsCancellationRequested);

            _persistence.Loads[0].SetResult(Loaded());
            Drain();

            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_A, _continuation.Admitted[0].Client);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_superseding_handshake_rejected_by_zone_full_keeps_the_first_entry_and_it_completes()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            var fillers = new List<EntityID>();
            for (int i = 0; i < _pool.MaxPlayers; i++)
            {
                Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(2000u + (uint)i), new AccountID(9000u + (uint)i),
                    500u + (uint)i, out _, out EntityID filler));
                fillers.Add(filler);
            }

            Begin(CLIENT_B, CHARACTER_RAW);

            Assert.AreEqual(1, _outbox.Count, "client B got the ZoneFull reply");
            Assert.IsEmpty(_disconnector.Closed, "client A is not closed");
            Assert.AreEqual(1, _persistence.Loads.Count, "no second load");
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.AreEqual(1, _svc.PendingCharacterCount);
            Assert.IsFalse(_persistence.Tokens[0].IsCancellationRequested, "the first load is not cancelled");

            _pool.ReleasePlayer(fillers[0]);
            _persistence.Loads[0].SetResult(Loaded());
            Drain();

            Assert.AreEqual(1, _continuation.Admitted.Count);
            Assert.AreEqual(CLIENT_A, _continuation.Admitted[0].Client);
            Assert.AreEqual(0, _svc.PendingCount);
            Assert.AreEqual(0, _svc.PendingCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_duplicate_load_superseding_handshake_rejected_while_draining_keeps_the_first_entry_open()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            _lifecycle.RequestQuit(1u);

            Begin(CLIENT_B, CHARACTER_RAW);

            Assert.AreEqual(1, _outbox.Count, "client B got the ZoneDoesNotExist reply");
            Assert.IsEmpty(_disconnector.Closed, "client A is not closed");
            Assert.AreEqual(1, _persistence.Loads.Count);
            Assert.AreEqual(1, _svc.PendingCount);
            Assert.AreEqual(1, _svc.PendingCharacterCount);
            Assert.IsFalse(_persistence.Tokens[0].IsCancellationRequested);
        }

        [Test]
        public void test_duplicate_load_superseded_entrys_token_is_cancelled_and_the_new_one_is_not()
        {
            Begin(CLIENT_A, CHARACTER_RAW);
            LogAssert.Expect(LogType.Warning, SUPERSEDED);

            Begin(CLIENT_B, CHARACTER_RAW);

            Assert.IsTrue(_persistence.Tokens[0].IsCancellationRequested);
            Assert.IsFalse(_persistence.Tokens[1].IsCancellationRequested);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
