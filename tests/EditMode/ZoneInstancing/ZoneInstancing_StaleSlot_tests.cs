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
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// Tests for Zone Instancing Story 011 — <see cref="ZoneEntryReclamation"/> (CR-ZI-8 step 11, CR-ZI-17, F-ZI-2,
    /// EC-ZI-13, EC-ZI-14; AC-ZI-14, AC-ZI-28, AC-ZI-29, AC-ZI-30). Real pool, roster, directory, admission and
    /// <see cref="TickCompletionQueue"/>; fake persistence, outbox, transport close, session sink and party. One shared
    /// call log records the order of side effects. No wall-clock time, no I/O.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_StaleSlot_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint CLIENT_A = 21u;
        private const uint CLIENT_B = 22u;
        private const uint OTHER_CLIENT = 30u;
        private const uint CHARACTER_C = 500u;
        private const uint OTHER_CHARACTER = 600u;
        private const uint START_TICK = 100u;
        private const uint PARTY_RAW = 77u;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick = START_TICK;

            public uint ServerTickNumber => Tick;
        }

        private sealed class RecordingPoolListener : IZoneSlotPoolListener
        {
            public readonly List<int> Occupied = new List<int>();
            public ZoneLifecycle Lifecycle;

            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick)
            {
                Occupied.Add(occupied);
                Lifecycle?.OnOccupiedPlayerSlotsChanged(occupied, serverTick);
            }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeOutbox : IClientMessageOutbox
        {
            private readonly List<string> _log;
            public bool LastFullCapExempt;

            public FakeOutbox(List<string> log) => _log = log;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (messageTypeId == PlayerLeftZone.MessageTypeId)
                {
                    Assert.IsTrue(PlayerLeftZoneCodec.TryReadBody(body, out PlayerLeftZone left));
                    _log.Add("left:" + clientId + ":" + left.EntityId.RawValue + ":" + left.DisconnectType);
                }
                else if (messageTypeId == ZoneFullResponse.MessageTypeId)
                {
                    Assert.IsTrue(ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse full));
                    _log.Add("full:" + clientId + ":" + full.Reason + ":" + full.RetryAfterSeconds);
                    LastFullCapExempt = isCapExempt;
                }
                else
                {
                    Assert.Fail("unexpected message type 0x" + messageTypeId.ToString("X4"));
                }
            }
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            private readonly List<string> _log;

            public bool Throw;

            public FakeDisconnector(List<string> log) => _log = log;

            public void DisconnectClient(ulong transportClientId)
            {
                _log.Add("close:" + transportClientId);
                if (Throw)
                {
                    throw new InvalidOperationException("transport shut down");
                }
            }
        }

        private sealed class FakeSessionSink : IZoneEntryReclaimSessionSink
        {
            private readonly List<string> _log;

            public FakeSessionSink(List<string> log) => _log = log;

            public void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned) =>
                _log.Add("expire:" + clientId + ":" + deleteTokenIfOwned);
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            private readonly List<string> _log;
            public bool InParty;

            public FakeParty(List<string> log) => _log = log;

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = InParty ? new PartyID(PARTY_RAW) : PartyID.Uninitialized;
                return InParty;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId) => _log.Add("partyexit:" + entityId.RawValue);
        }

        private sealed class FakeValidator : IZoneEntryTokenValidator
        {
            public bool IsValid(in ZoneEntryRequest request) => true;
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        /// <summary>Mirrors the state change of ZoneEntryCompletion that matters here: directory entry and pending roster.</summary>
        private sealed class FakeContinuation : IZoneEntryContinuation
        {
            private readonly ZoneSlotPool _pool;
            private readonly ZoneConnectionCharacterDirectory _directory;
            private readonly ZoneTickRoster _roster;
            public readonly List<EntityID> Admitted = new List<EntityID>();

            public FakeContinuation(ZoneSlotPool pool, ZoneConnectionCharacterDirectory directory, ZoneTickRoster roster)
            {
                _pool = pool;
                _directory = directory;
                _roster = roster;
            }

            public void OnPlayerAdmitted(uint clientId, byte slot, EntityID entityId)
            {
                Assert.IsTrue(_pool.TryGetPlayer(entityId, out ZonePlayerSlot player));
                _directory.Add(clientId, player.Character);
                _roster.AddPending(entityId);
                Admitted.Add(entityId);
            }
        }

        private sealed class FakePersistence : ICharacterPersistence
        {
            private readonly List<string> _log;
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();
            public int SaveSessionCalls;

            public FakePersistence(List<string> log) => _log = log;

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                _log.Add("load:" + id.RawValue);
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

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) =>
                throw new NotSupportedException();
        }

        private sealed class FakeLifecycleListener : IZoneLifecycleListener
        {
            public readonly List<ZoneCloseTransition> Closed = new List<ZoneCloseTransition>();

            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick) => Closed.Add(transition);
        }

        /// <summary>Records connection-level registrations; every other member is a no-op.</summary>
        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<ConnectionMessageDescriptor> ConnectionLevel = new List<ConnectionMessageDescriptor>();
            public readonly List<IConnectionMessageSink> Sinks = new List<IConnectionMessageSink>();
            public int RequestRegistrations;

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) => RequestRegistrations++;

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
            {
                ConnectionLevel.Add(descriptor);
                Sinks.Add(sink);
            }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private List<string> _log;
        private FakeTicks _ticks;
        private RecordingPoolListener _poolListener;
        private FakeParty _party;
        private FakePersistence _persistence;
        private ZoneSlotPool _pool;
        private ZoneConnectionCharacterDirectory _directory;
        private ZoneTickRoster _roster;
        private TickCompletionQueue _queue;
        private FakeContinuation _continuation;
        private ZoneEntryReclamation _reclamation;
        private ZoneEntryAdmission _admission;
        private FakeOutbox _outbox;
        private FakeDisconnector _disconnector;
        private FakeLifecycleListener _lifecycleListener;
        private ZoneLifecycle _lifecycle;
        private uint _nextCharacter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _ticks = new FakeTicks();
            _poolListener = new RecordingPoolListener();
            _party = new FakeParty(_log);
            _persistence = new FakePersistence(_log);
            _pool = new ZoneSlotPool(Zone, _ticks, _poolListener);
            _directory = new ZoneConnectionCharacterDirectory();
            _roster = new ZoneTickRoster(_pool, _ticks);
            _queue = new TickCompletionQueue(5);
            _continuation = new FakeContinuation(_pool, _directory, _roster);
            _lifecycleListener = new FakeLifecycleListener();
            _lifecycle = new ZoneLifecycle(Zone, _lifecycleListener, 0u);
            _lifecycle.MarkActive(0u);
            _outbox = new FakeOutbox(_log);
            _disconnector = new FakeDisconnector(_log);
            _reclamation = new ZoneEntryReclamation(Zone, _pool, _directory, _roster, _party, _outbox,
                _disconnector, new FakeSessionSink(_log), _ticks);
            _admission = new ZoneEntryAdmission(Zone, _pool, _lifecycle, _persistence, new FakeApplier(), _queue,
                new FakeValidator(), _outbox, _continuation, _directory, reclamation: _reclamation);
            _nextCharacter = 1000u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private static Regex Rx(string pattern) => new Regex(pattern);

        private void BeginHandshake(uint clientId, uint characterRaw)
        {
            var request = new ZoneEntryRequest(clientId, new AccountID(characterRaw + 5000u), new CharacterID(characterRaw), Zone, new byte[16]);
            _admission.BeginEntry(in request);
        }

        private void CompleteLoad(int index, bool success = true)
        {
            var result = success
                ? new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord())
                : new CharacterLoadResult(CharacterLoadCode.CharacterNotFound, ZoneID.Invalid);
            _persistence.Loads[index].SetResult(result);
            _queue.Drain(_ticks.Tick);
        }

        // Full admission of a new character on a connection; returns the slot's EntityID.
        private EntityID Admit(uint clientId, uint characterRaw)
        {
            int loadIndex = _persistence.Loads.Count;
            BeginHandshake(clientId, characterRaw);
            CompleteLoad(loadIndex);
            return _continuation.Admitted[_continuation.Admitted.Count - 1];
        }

        // The bystander is acked so that only the slot under test can stall.
        private EntityID AdmitOther()
        {
            EntityID id = Admit(OTHER_CLIENT, OTHER_CHARACTER);
            LogAssert.Expect(UnityEngine.LogType.Log, Rx("ZoneEntryAck from client " + OTHER_CLIENT));
            SendAck(OTHER_CLIENT);
            _reclamation.OnTick(START_TICK);
            return id;
        }

        private void SendAck(uint clientId, uint zoneRaw = ZONE_RAW, uint version = 1u)
        {
            var buffer = new byte[ZoneEntryAck.BodySize];
            ZoneEntryAckCodec.WriteBody(buffer, new ZoneEntryAck(new ZoneID(zoneRaw), version));
            _reclamation.OnConnectionMessage(clientId, ZoneEntryAck.MessageTypeId, EntityID.Invalid, buffer);
        }

        private uint TimeoutTick => START_TICK + _reclamation.StallTicks;

        // ----- AC-ZI-29: entering phase and ack --------------------------------------------------

        [Test]
        public void test_ack_for_the_zone_marks_the_slot_acked_and_the_stall_timer_no_longer_reclaims_it()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            Assert.IsTrue(_reclamation.IsEntering(id));

            SendAck(CLIENT_A);
            _reclamation.OnTick(START_TICK + 1u);
            _reclamation.OnTick(TimeoutTick + 100u);

            Assert.IsFalse(_reclamation.IsEntering(id));
            Assert.IsTrue(_reclamation.IsAcked(CLIENT_A));
            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _), "an acked slot is never stall-reclaimed");
            Assert.IsEmpty(_log.FindAll(e => e.StartsWith("full:") || e.StartsWith("close:") || e.StartsWith("expire:")));
        }

        [Test]
        public void test_entry_steps_do_not_wait_for_the_ack_the_slot_is_entering_and_admitted()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);

            Assert.IsTrue(_reclamation.IsEntering(id));
            Assert.AreEqual(1, _reclamation.EnteringCount);
            Assert.IsTrue(_directory.TryGetCharacterId(CLIENT_A, out _));
            Assert.AreEqual(1, _roster.PendingCount);
        }

        [Test]
        public void test_ack_from_a_connection_with_no_slot_three_times_logs_exactly_one_warning_and_changes_nothing()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("UnexpectedZoneEntryAck"));

            SendAck(CLIENT_B);
            SendAck(CLIENT_B);
            SendAck(CLIENT_B);
            _reclamation.OnTick(START_TICK + 1u);

            Assert.IsTrue(_reclamation.IsEntering(id));
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ack_naming_another_zone_changes_nothing_and_warns_once()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("UnexpectedZoneEntryAck"));

            SendAck(CLIENT_A, zoneRaw: 99u);
            SendAck(CLIENT_A, zoneRaw: 99u);
            _reclamation.OnTick(START_TICK + 1u);

            Assert.IsTrue(_reclamation.IsEntering(id), "the slot is still entering");
            Assert.IsFalse(_reclamation.IsAcked(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_second_ack_from_an_acked_slot_changes_nothing_and_warns_once()
        {
            Admit(CLIENT_A, CHARACTER_C);
            SendAck(CLIENT_A);
            _reclamation.OnTick(START_TICK + 1u);
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("UnexpectedZoneEntryAck"));

            SendAck(CLIENT_A);
            SendAck(CLIENT_A);
            _reclamation.OnTick(START_TICK + 2u);

            Assert.IsTrue(_reclamation.IsAcked(CLIENT_A));
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_ack_from_a_reclaimed_slot_changes_nothing_and_warns_once()
        {
            Admit(CLIENT_A, CHARACTER_C);
            _reclamation.OnTick(TimeoutTick);
            _log.Clear();
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("UnexpectedZoneEntryAck"));

            SendAck(CLIENT_A);
            SendAck(CLIENT_A);
            _reclamation.OnTick(TimeoutTick + 1u);

            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.IsEmpty(_log, "nothing is sent, closed or expired a second time");
            Assert.IsFalse(_directory.TryGetCharacterId(CLIENT_A, out _), "the dispatcher drops the connection's requests (no character)");
            Assert.AreEqual(0, _reclamation.EnteringCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_malformed_ack_body_is_dropped_with_one_warning_and_changes_nothing()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("ZoneEntryAckMalformed"));

            _reclamation.OnConnectionMessage(CLIENT_A, ZoneEntryAck.MessageTypeId, EntityID.Invalid, new byte[ZoneEntryAck.BodySize - 1]);
            _reclamation.OnTick(START_TICK + 1u);

            Assert.IsTrue(_reclamation.IsEntering(id));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- AC-ZI-14: case (a) stall ----------------------------------------------------------

        [Test]
        public void test_stall_timeout_runs_leave_path_then_zone_full_other_then_close_then_session_sink()
        {
            AdmitOther();
            _roster.ActivatePending(START_TICK + 1u);
            EntityID stalled = Admit(CLIENT_A, CHARACTER_C);
            _roster.ActivatePending(START_TICK + 2u);
            Assert.IsTrue(_roster.IsInTickLoop(stalled));
            _log.Clear();
            int before = _pool.OccupiedPlayerCount;

            _reclamation.OnTick(TimeoutTick);

            Assert.AreEqual(new List<string>
            {
                "left:" + OTHER_CLIENT + ":" + stalled.RawValue + ":Timeout",
                "full:" + CLIENT_A + ":Other:0",
                "close:" + CLIENT_A,
                "expire:" + CLIENT_A + ":True",
            }, _log);
            Assert.IsFalse(_roster.IsInTickLoop(stalled));
            Assert.IsFalse(_pool.TryGetPlayerSlot(stalled, out _));
            Assert.AreEqual(before - 1, _pool.OccupiedPlayerCount);
            Assert.IsFalse(_directory.TryGetCharacterId(CLIENT_A, out _));
            Assert.IsFalse(_directory.TryGetClientId(new CharacterID(CHARACTER_C), out _));
            Assert.AreEqual(0, _persistence.SaveSessionCalls, "reclamation never saves");
            Assert.AreEqual(0, _reclamation.EnteringCount);
            Assert.IsTrue(_outbox.LastFullCapExempt, "the reply must not be deferred past the close");
        }

        [Test]
        public void test_stall_of_a_solo_player_makes_no_party_call()
        {
            // Arrange
            AdmitOther();
            Admit(CLIENT_A, CHARACTER_C);
            _party.InParty = false;
            _log.Clear();

            // Act
            _reclamation.OnTick(TimeoutTick);

            // Assert
            Assert.IsEmpty(_log.FindAll(e => e.StartsWith("partyexit:")));
            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("expire:")).Count);
        }

        [Test]
        public void test_stall_with_a_throwing_transport_close_still_expires_the_session_and_reclaims_the_other_stalled_slot()
        {
            // Arrange
            EntityID first = Admit(CLIENT_A, CHARACTER_C);
            EntityID second = Admit(CLIENT_B, CHARACTER_C + 1u);
            _disconnector.Throw = true;
            _log.Clear();
            LogAssert.Expect(UnityEngine.LogType.Error, Rx("closing client " + CLIENT_B));
            LogAssert.Expect(UnityEngine.LogType.Error, Rx("closing client " + CLIENT_A));

            // Act
            _reclamation.OnTick(TimeoutTick);

            // Assert
            Assert.IsFalse(_pool.TryGetPlayerSlot(first, out _));
            Assert.IsFalse(_pool.TryGetPlayerSlot(second, out _));
            Assert.Contains("expire:" + CLIENT_A + ":True", _log);
            Assert.Contains("expire:" + CLIENT_B + ":True", _log);
            Assert.AreEqual(0, _reclamation.EnteringCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ack_recorded_before_the_timeout_tick_is_applied_before_the_scan_of_that_tick()
        {
            // Arrange
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();
            LogAssert.Expect(UnityEngine.LogType.Log, Rx("ZoneEntryAck from client " + CLIENT_A));

            // Act
            SendAck(CLIENT_A);
            _reclamation.OnTick(TimeoutTick);

            // Assert
            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _));
            Assert.IsTrue(_reclamation.IsAcked(CLIENT_A));
            Assert.IsEmpty(_log);
        }

        [Test]
        public void test_stall_timeout_is_35_seconds_times_the_tick_rate_by_default()
        {
            Assert.AreEqual(35, ZoneEntryReclamation.ZONE_ENTRY_STALL_TIMEOUT_SECONDS);
            Assert.AreEqual((uint)(35 * ServerTickLoop.TICK_RATE_HZ), _reclamation.StallTicks);
        }

        [Test]
        public void test_one_tick_before_the_timeout_nothing_is_reclaimed()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            _reclamation.OnTick(TimeoutTick - 1u);

            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _));
            Assert.IsEmpty(_log);
        }

        [Test]
        public void test_ack_one_tick_before_the_timeout_prevents_the_whole_reclamation()
        {
            AdmitOther();
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            SendAck(CLIENT_A);
            _reclamation.OnTick(TimeoutTick - 1u);
            _reclamation.OnTick(TimeoutTick);

            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _));
            Assert.IsEmpty(_log);
            Assert.IsTrue(_directory.TryGetCharacterId(CLIENT_A, out _));
        }

        [Test]
        public void test_stall_before_step_10_removes_the_entity_from_pending_and_it_is_never_activated()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            Assert.AreEqual(1, _roster.PendingCount);

            _reclamation.OnTick(TimeoutTick);
            _roster.ActivatePending(TimeoutTick + 1u);

            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(0, _roster.ActiveCount);
            Assert.IsFalse(_roster.IsInTickLoop(id));
        }

        [Test]
        public void test_last_entering_slot_reclaimed_raises_an_occupancy_change_to_zero()
        {
            Admit(CLIENT_A, CHARACTER_C);
            _poolListener.Occupied.Clear();

            _reclamation.OnTick(TimeoutTick);

            Assert.AreEqual(new List<int> { 0 }, _poolListener.Occupied, "the lifecycle idle timer is fed by this change (Story 001)");
        }

        [Test]
        public void test_last_entering_slot_reclaimed_in_an_active_zone_starts_the_idle_timer_and_the_zone_closes_by_t7()
        {
            // Arrange
            _poolListener.Lifecycle = _lifecycle;
            Admit(CLIENT_A, CHARACTER_C);
            _ticks.Tick = TimeoutTick;
            uint idleTicks = (uint)(ZoneLifecycle.ZONE_IDLE_CLOSE_SECONDS * ServerTickLoop.TICK_RATE_HZ);

            // Act
            _reclamation.OnTick(TimeoutTick);
            _lifecycle.OnTick(TimeoutTick + 1u);
            ZoneState afterOneTick = _lifecycle.State;
            _lifecycle.OnTick(TimeoutTick + idleTicks);

            // Assert
            Assert.AreEqual(ZoneState.Active, afterOneTick, "the zone stays Active while the idle timer runs");
            Assert.AreEqual(ZoneState.Closed, _lifecycle.State);
            Assert.AreEqual(new List<ZoneCloseTransition> { ZoneCloseTransition.Idle }, _lifecycleListener.Closed);
        }

        [Test]
        public void test_register_adds_one_connection_level_row_for_zone_entry_ack_with_the_service_as_sink()
        {
            // Arrange
            var dispatcher = new RecordingDispatcher();

            // Act
            ZoneEntryReclamation.Register(dispatcher, _reclamation);

            // Assert
            Assert.AreEqual(1, dispatcher.ConnectionLevel.Count);
            Assert.AreEqual(ZoneEntryAck.MessageTypeId, dispatcher.ConnectionLevel[0].MessageTypeId);
            Assert.AreEqual(ZoneEntryAck.BodySize, dispatcher.ConnectionLevel[0].MaxBodyBytes);
            Assert.IsFalse(dispatcher.ConnectionLevel[0].CarriesSenderEntityId);
            Assert.AreSame(_reclamation, dispatcher.Sinks[0]);
            Assert.AreEqual(0, dispatcher.RequestRegistrations);
            Assert.Throws<ArgumentNullException>(() => ZoneEntryReclamation.Register(null, _reclamation));
            Assert.Throws<ArgumentNullException>(() => ZoneEntryReclamation.Register(dispatcher, null));
        }

        [Test]
        public void test_constructor_rejects_a_stall_timeout_outside_31_to_60_and_accepts_the_bounds()
        {
            var outbox = new FakeOutbox(_log);
            Assert.Throws<ArgumentOutOfRangeException>(() => NewReclamation(30));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewReclamation(61));
            Assert.AreEqual(31u * 20u, NewReclamation(31, 20).StallTicks);
            Assert.AreEqual(60u * 20u, NewReclamation(60, 20).StallTicks);
            Assert.Throws<ArgumentNullException>(() => new ZoneEntryReclamation(Zone, _pool, _directory, _roster, _party, outbox,
                null, new FakeSessionSink(_log), _ticks));
        }

        private ZoneEntryReclamation NewReclamation(int seconds, int tickRate = 20) =>
            new ZoneEntryReclamation(Zone, _pool, _directory, _roster, _party, new FakeOutbox(_log), new FakeDisconnector(_log),
                new FakeSessionSink(_log), _ticks, seconds, tickRate);

        // ----- leave path ------------------------------------------------------------------------

        [Test]
        public void test_leave_path_notifies_party_exit_once_when_the_character_is_in_a_formal_party()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _party.InParty = true;
            _log.Clear();

            Assert.IsTrue(_reclamation.LeaveZone(id, DisconnectType.Graceful));

            Assert.AreEqual(new List<string> { "partyexit:" + id.RawValue }, _log);
        }

        [Test]
        public void test_leave_path_for_a_solo_player_makes_no_party_call()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            Assert.IsTrue(_reclamation.LeaveZone(id, DisconnectType.Graceful));

            Assert.IsEmpty(_log);
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
        }

        [Test]
        public void test_leave_path_on_an_already_freed_slot_is_a_no_op_with_no_second_message()
        {
            AdmitOther();
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _party.InParty = true;
            Assert.IsTrue(_reclamation.LeaveZone(id, DisconnectType.Timeout));
            _log.Clear();

            Assert.IsFalse(_reclamation.LeaveZone(id, DisconnectType.Timeout));
            Assert.IsFalse(_reclamation.LeaveZone(new EntityID(9999u), DisconnectType.Timeout));

            Assert.IsEmpty(_log);
        }

        [Test]
        public void test_player_left_zone_goes_to_other_connected_clients_never_to_the_leaver()
        {
            AdmitOther();
            Admit(CLIENT_B, CHARACTER_C + 1u);
            EntityID leaver = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            _reclamation.LeaveZone(leaver, DisconnectType.Graceful);

            Assert.AreEqual(2, _log.Count);
            Assert.Contains("left:" + OTHER_CLIENT + ":" + leaver.RawValue + ":Graceful", _log);
            Assert.Contains("left:" + CLIENT_B + ":" + leaver.RawValue + ":Graceful", _log);
        }

        // ----- AC-ZI-28: case (b) ----------------------------------------------------------------

        [Test]
        public void test_duplicate_handshake_reclaims_the_entering_slot_before_load_character_and_admits_the_new_connection()
        {
            AdmitOther();
            EntityID oldSlot = Admit(CLIENT_A, CHARACTER_C);
            _party.InParty = true;
            int countBefore = _pool.OccupiedPlayerCount;
            _log.Clear();

            int loadIndex = _persistence.Loads.Count;
            BeginHandshake(CLIENT_B, CHARACTER_C);

            Assert.AreEqual(new List<string>
            {
                "left:" + OTHER_CLIENT + ":" + oldSlot.RawValue + ":Timeout",
                "partyexit:" + oldSlot.RawValue,
                "close:" + CLIENT_A,
                "expire:" + CLIENT_A + ":False",
                "load:" + CHARACTER_C,
            }, _log, "the old slot is reclaimed, the old connection closed with no message, then LoadCharacter starts");
            Assert.IsFalse(_pool.TryGetPlayerSlot(oldSlot, out _));

            CompleteLoad(loadIndex);

            Assert.AreEqual(countBefore, _pool.OccupiedPlayerCount);
            Assert.IsTrue(_directory.TryGetClientId(new CharacterID(CHARACTER_C), out uint holder));
            Assert.AreEqual(CLIENT_B, holder);
            int slotsOfC = 0;
            IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(Zone);
            for (int i = 0; i < players.Count; i++)
            {
                _pool.TryGetPlayer(players[i], out ZonePlayerSlot slot);
                if (slot.Character == new CharacterID(CHARACTER_C))
                {
                    slotsOfC++;
                }
            }

            Assert.AreEqual(1, slotsOfC, "the character holds exactly one slot");
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
        }

        [Test]
        public void test_duplicate_handshake_with_a_failed_load_replies_load_character_failed_and_leaves_no_slot()
        {
            AdmitOther();
            Admit(CLIENT_A, CHARACTER_C);
            int countBefore = _pool.OccupiedPlayerCount;
            int loadIndex = _persistence.Loads.Count;
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("LoadCharacter failed"));

            BeginHandshake(CLIENT_B, CHARACTER_C);
            _log.Clear();
            CompleteLoad(loadIndex, success: false);

            Assert.AreEqual(1, _log.Count, "only the rejection reply follows: " + string.Join(",", _log));
            Assert.AreEqual("full:" + CLIENT_B + ":LoadCharacterFailed:0", _log[0]);
            Assert.AreEqual(countBefore - 1, _pool.OccupiedPlayerCount, "the old slot stays reclaimed");
            Assert.IsFalse(_directory.TryGetClientId(new CharacterID(CHARACTER_C), out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_handshake_for_a_character_with_an_acked_slot_is_ignored_with_one_warning_and_no_second_slot()
        {
            EntityID acked = Admit(CLIENT_A, CHARACTER_C);
            LogAssert.Expect(UnityEngine.LogType.Log, Rx("ZoneEntryAck from client " + CLIENT_A));
            SendAck(CLIENT_A);
            _reclamation.OnTick(START_TICK + 1u);
            _log.Clear();
            int loadsBefore = _persistence.Loads.Count;
            LogAssert.Expect(UnityEngine.LogType.Warning, Rx("EntryHandshakeCharacterAlreadyInZone"));

            BeginHandshake(CLIENT_B, CHARACTER_C);
            BeginHandshake(CLIENT_B, CHARACTER_C);

            Assert.IsTrue(_pool.TryGetPlayerSlot(acked, out _), "not reclaimed");
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
            Assert.AreEqual(loadsBefore, _persistence.Loads.Count, "no load for the second connection");
            Assert.IsEmpty(_log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_handshake_for_a_different_character_does_not_touch_an_entering_slot()
        {
            EntityID entering = Admit(CLIENT_A, CHARACTER_C);

            Admit(CLIENT_B, CHARACTER_C + 1u);

            Assert.IsTrue(_reclamation.IsEntering(entering));
            Assert.AreEqual(2, _pool.OccupiedPlayerCount);
        }

        // ----- AC-ZI-30: case (c) ----------------------------------------------------------------

        [Test]
        public void test_transport_drop_of_an_entering_slot_reclaims_on_the_next_tick_sends_nothing_and_expires_the_session()
        {
            AdmitOther();
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _party.InParty = true;
            _log.Clear();

            _admission.RemoveClient(CLIENT_A);

            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _), "nothing is mutated inside the transport callback");
            Assert.IsEmpty(_log);

            _reclamation.OnTick(START_TICK + 1u);

            Assert.IsFalse(_pool.TryGetPlayerSlot(id, out _));
            Assert.IsFalse(_directory.TryGetCharacterId(CLIENT_A, out _));
            Assert.AreEqual(new List<string>
            {
                "left:" + OTHER_CLIENT + ":" + id.RawValue + ":Timeout",
                "partyexit:" + id.RawValue,
                "expire:" + CLIENT_A + ":True",
            }, _log, "no ZoneFullResponse, no close");
            Assert.AreEqual(0, _persistence.SaveSessionCalls);
        }

        [Test]
        public void test_transport_drop_of_an_acked_slot_is_not_handled_here()
        {
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            SendAck(CLIENT_A);
            _reclamation.OnTick(START_TICK + 1u);
            _log.Clear();

            _admission.RemoveClient(CLIENT_A);
            _reclamation.OnTick(START_TICK + 2u);
            _reclamation.OnTick(TimeoutTick + 10u);

            Assert.IsTrue(_pool.TryGetPlayerSlot(id, out _));
            Assert.IsEmpty(_log);
        }

        [Test]
        public void test_drop_then_stall_for_the_same_slot_runs_the_leave_path_only_once()
        {
            AdmitOther();
            EntityID id = Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            _admission.RemoveClient(CLIENT_A);
            _reclamation.OnTick(START_TICK + 1u);
            int afterDrop = _log.Count;
            _reclamation.OnTick(TimeoutTick);

            Assert.AreEqual(afterDrop, _log.Count, "the stall scan finds nothing");
            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("left:")).Count);
            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("expire:")).Count);
            Assert.IsFalse(_pool.TryGetPlayerSlot(id, out _));
        }

        [Test]
        public void test_drop_and_stall_in_the_same_tick_run_the_leave_path_only_once_and_send_no_message()
        {
            AdmitOther();
            Admit(CLIENT_A, CHARACTER_C);
            _log.Clear();

            _admission.RemoveClient(CLIENT_A);
            _reclamation.OnTick(TimeoutTick);

            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("left:")).Count);
            Assert.AreEqual(0, _log.FindAll(e => e.StartsWith("full:")).Count);
            Assert.AreEqual(1, _log.FindAll(e => e.StartsWith("expire:")).Count);
        }

        // ----- directory reverse map -------------------------------------------------------------

        [Test]
        public void test_directory_reverse_lookup_follows_add_replace_and_remove()
        {
            var directory = new ZoneConnectionCharacterDirectory();
            var first = new CharacterID(5u);
            var second = new CharacterID(6u);

            directory.Add(CLIENT_A, first);
            Assert.IsTrue(directory.TryGetClientId(first, out uint found));
            Assert.AreEqual(CLIENT_A, found);

            directory.Add(CLIENT_A, second);
            Assert.IsFalse(directory.TryGetClientId(first, out _), "the replaced character no longer maps");
            Assert.IsTrue(directory.TryGetClientId(second, out found));
            Assert.AreEqual(CLIENT_A, found);

            Assert.IsTrue(directory.Remove(CLIENT_A));
            Assert.IsFalse(directory.TryGetClientId(second, out _));
        }

        [Test]
        public void test_directory_remove_of_an_old_connection_keeps_the_new_holder_of_the_character()
        {
            var directory = new ZoneConnectionCharacterDirectory();
            var character = new CharacterID(5u);
            directory.Add(CLIENT_A, character);
            directory.Add(CLIENT_B, character);

            directory.Remove(CLIENT_A);

            Assert.IsTrue(directory.TryGetClientId(character, out uint holder));
            Assert.AreEqual(CLIENT_B, holder);
        }

        // ----- stand-in --------------------------------------------------------------------------

        [Test]
        public void test_logging_session_sink_warns_once_per_instance()
        {
            var sink = new LoggingZoneEntryReclaimSessionSink();
            LogAssert.Expect(UnityEngine.LogType.Warning, LoggingZoneEntryReclaimSessionSink.NOT_IMPLEMENTED_MESSAGE);

            sink.ExpireEnteringSession(1u, true);
            sink.ExpireEnteringSession(2u, false);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_descriptor_is_connection_level_for_the_ack_with_an_8_byte_body()
        {
            Assert.AreEqual(ZoneEntryAck.MessageTypeId, ZoneEntryReclamation.Descriptor.MessageTypeId);
            Assert.AreEqual(ZoneEntryAck.BodySize, ZoneEntryReclamation.Descriptor.MaxBodyBytes);
        }
    }
}
