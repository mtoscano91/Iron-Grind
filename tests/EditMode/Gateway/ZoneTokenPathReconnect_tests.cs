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
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Gateway
{
    /// <summary>
    /// Authentication Story 017 (AC-6): the zone-side token-path reconnect handshake (<c>networking-session-token.md</c> CR-TOK-9,
    /// <c>zone-instancing.md</c> CR-ZI-8 token path). A real <see cref="ZoneInstancingWiring"/> over fakes; ticks are advanced by hand and
    /// nothing sleeps or opens a socket.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneTokenPathReconnect_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const uint CLIENT = 3001u;
        private const uint NEW_CLIENT = 3002u;
        private const uint CHARACTER = 501u;
        private const uint ACCOUNT = 1001u;
        private const int GHOST_AFTER_TICKS = 260;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static byte[] Token(byte first)
        {
            var token = new byte[SessionHandshake.TOKEN_LENGTH];
            for (int i = 0; i < token.Length; i++)
            {
                token[i] = (byte)(first + i);
            }

            return token;
        }

        private static byte[] Body(byte[] token)
        {
            var body = new byte[SessionHandshake.BodySize];
            SessionHandshakeCodec.WriteBody(body, new SessionHandshake(new CharacterID(CHARACTER), Zone, token));
            return body;
        }

        // ----- fakes ----------------------------------------------------------------------------

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Now;

            public uint ServerTickNumber => Now;
        }

        private sealed class RecordingOutbox : IClientMessageOutbox
        {
            public readonly List<KeyValuePair<uint, ushort>> Sent = new List<KeyValuePair<uint, ushort>>();
            public readonly List<KeyValuePair<uint, ZoneFullResponse>> Replies = new List<KeyValuePair<uint, ZoneFullResponse>>();

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Sent.Add(new KeyValuePair<uint, ushort>(clientId, messageTypeId));
                if (messageTypeId == ZoneFullResponse.MessageTypeId && ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse reply))
                {
                    Replies.Add(new KeyValuePair<uint, ZoneFullResponse>(clientId, reply));
                }
            }

            public int CountTo(uint clientId, ushort messageTypeId)
            {
                int count = 0;
                for (int i = 0; i < Sent.Count; i++)
                {
                    if (Sent[i].Key == clientId && Sent[i].Value == messageTypeId)
                    {
                        count++;
                    }
                }

                return count;
            }

            public int CountOfType(ushort messageTypeId)
            {
                int count = 0;
                for (int i = 0; i < Sent.Count; i++)
                {
                    if (Sent[i].Value == messageTypeId)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private sealed class RecordingDisconnector : IInboundTransportDisconnector
        {
            public readonly List<ulong> Disconnected = new List<ulong>();

            public void DisconnectClient(ulong transportClientId) => Disconnected.Add(transportClientId);
        }

        private sealed class ToggleResponder : IZoneEntryResponder
        {
            public bool ThrowOnReady;

            public void SendSessionReady(uint clientId)
            {
                if (ThrowOnReady)
                {
                    throw new InvalidOperationException("SessionReady send failed");
                }
            }

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId) { }
        }

        private sealed class AcceptingApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class RecordingGatewayOutbox : IGatewayLinkOutbox
        {
            public bool LastReconnectAccepted;
            public byte[] LastReconnectToken;

            public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason) { }

            public void EnqueueSessionReleased(AccountID account) { }

            public void EnqueueEvictResult(uint requestId) { }

            public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken)
            {
                LastReconnectAccepted = accepted;
                LastReconnectToken = newToken;
            }

            public void EnqueuePlayerCount(ushort count) { }

            public void EnqueueZoneDraining() { }
        }

        private sealed class SpyPersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public readonly List<CharacterID> LoadedCharacters = new List<CharacterID>();
            public readonly List<TaskCompletionSource<CharacterLoadResult>> Loads = new List<TaskCompletionSource<CharacterLoadResult>>();

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
            {
                LoadedCharacters.Add(id);
                var source = new TaskCompletionSource<CharacterLoadResult>();
                Loads.Add(source);
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

        // ----- fixture ----------------------------------------------------------------------------

        private FakeTicks _ticks;
        private RecordingOutbox _outbox;
        private RecordingDisconnector _disconnector;
        private SpyPersistence _persistence;
        private TickCompletionQueue _queue;
        private RecordingGatewayOutbox _gatewayOutbox;
        private ToggleResponder _responder;
        private ZoneInstancingWiring _wiring;
        private byte[] _token0;

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _outbox = new RecordingOutbox();
            _disconnector = new RecordingDisconnector();
            _persistence = new SpyPersistence();
            _queue = new TickCompletionQueue();
            _gatewayOutbox = new RecordingGatewayOutbox();
            _responder = new ToggleResponder();
            _token0 = Token(0x01);
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, new RecordingDispatcher(), _outbox, _persistence, _persistence,
                new AcceptingApplier(), new CharacterMutationGate(), new RecordingInventory(), _disconnector, _ => { },
                new ZoneInstancingWiringOptions { TickRateHz = HZ, Equipment = new FakeEquipmentService(), GatewayOutbox = _gatewayOutbox, Responder = _responder });
            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (TaskCompletionSource<CharacterLoadResult> load in _persistence.Loads)
            {
                load.TrySetCanceled();
            }

            _queue.DrainOnShutdown(TimeSpan.FromMilliseconds(200));
        }

        private void RunTick()
        {
            _ticks.Now++;
            _queue.Drain(_ticks.Now);
            _wiring.Run(_ticks.Now, 0f);
        }

        private void Handshake(uint clientId, byte[] token)
        {
            _wiring.HandshakeIntake.OnConnectionMessage(clientId, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(token));
            RunTick();
            RunTick();
        }

        private void Ack(uint clientId)
        {
            var buffer = new byte[ZoneEntryAck.BodySize];
            ZoneEntryAckCodec.WriteBody(buffer, new ZoneEntryAck(Zone, 1u));
            _wiring.Reclamation.OnConnectionMessage(clientId, ZoneEntryAck.MessageTypeId, EntityID.Invalid, buffer);
            RunTick();
        }

        private SessionState StateOf()
        {
            Assert.IsTrue(_wiring.Sessions.TryGetSessionState(ACCOUNT, out SessionState state));
            return state;
        }

        // Fresh entry on CLIENT, acked, then the transport drops and the heartbeat timeout makes the session a ghost.
        private EntityID EnterAckAndGhost(bool removeOldClient = true)
        {
            _wiring.GatewayAdmit.Handle(1u, new AccountID(ACCOUNT), new CharacterID(CHARACTER), _token0, _ticks.Now);
            _wiring.HandshakeIntake.OnConnectionMessage(CLIENT, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(_token0));
            RunTick();
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            RunTick();
            Assert.AreEqual(SessionState.Connected, StateOf());
            Ack(CLIENT);
            Assert.IsTrue(_wiring.Reclamation.IsAcked(CLIENT));
            Assert.IsTrue(_wiring.Pool.TryFindPlayerByCharacter(new CharacterID(CHARACTER), out EntityID entity));

            if (removeOldClient)
            {
                _wiring.Admission.RemoveClient(CLIENT);
                _wiring.HandshakeIntake.RemoveClient(CLIENT);
            }

            for (int i = 0; i < GHOST_AFTER_TICKS && StateOf() != SessionState.Disconnected_SessionActive; i++)
            {
                RunTick();
            }

            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            return entity;
        }

        // Gateway Reconnect: rotates the token and answers with the new one.
        private byte[] RotateThroughGateway()
        {
            _wiring.GatewayReconnect.Handle(9u, new AccountID(ACCOUNT), _token0, _ticks.Now);
            Assert.IsTrue(_gatewayOutbox.LastReconnectAccepted);
            Assert.IsNotNull(_gatewayOutbox.LastReconnectToken);
            return _gatewayOutbox.LastReconnectToken;
        }

        private ZoneEntryRequest Request(uint client, byte[] token) =>
            new ZoneEntryRequest(client, new CharacterID(CHARACTER), Zone, token);

        private GatewayZoneEntryTokenValidator Validator() =>
            new GatewayZoneEntryTokenValidator(_wiring.Admitted, _wiring.TokenStore, _wiring.Sessions);

        // ----- validator ----------------------------------------------------------------------------

        [Test]
        public void test_token_path_validator_ghost_with_the_rotated_token_is_accepted_as_reconnect()
        {
            EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            ZoneEntryRequest request = Request(NEW_CLIENT, rotated);

            bool reconnect = Validator().TryValidateReconnect(in request, out AccountID account);
            bool fresh = Validator().TryValidate(in request, out _);

            Assert.IsTrue(reconnect);
            Assert.AreEqual(new AccountID(ACCOUNT), account);
            Assert.IsFalse(fresh, "no admitted entry: the fresh path does not match");
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf(), "validating changes no state");
        }

        [Test]
        public void test_token_path_validator_ghost_with_a_wrong_token_is_rejected_and_changes_nothing()
        {
            EnterAckAndGhost();
            byte[] wrong = Token(0x55);
            ZoneEntryRequest request = Request(NEW_CLIENT, wrong);
            _wiring.Sessions.TryGetReauthFailureCount(ACCOUNT, out int before);

            bool reconnect = Validator().TryValidateReconnect(in request, out AccountID account);

            Assert.IsFalse(reconnect);
            Assert.AreEqual(AccountID.Invalid, account);
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            _wiring.Sessions.TryGetReauthFailureCount(ACCOUNT, out int after);
            Assert.AreEqual(before, after);
        }

        [Test]
        public void test_token_path_validator_unknown_character_is_rejected()
        {
            ZoneEntryRequest request = Request(NEW_CLIENT, _token0);

            Assert.IsFalse(Validator().TryValidateReconnect(in request, out AccountID account));
            Assert.AreEqual(AccountID.Invalid, account);
        }

        [Test]
        public void test_token_path_validator_connected_session_with_the_right_token_is_not_a_reconnect()
        {
            _wiring.GatewayAdmit.Handle(1u, new AccountID(ACCOUNT), new CharacterID(CHARACTER), _token0, _ticks.Now);
            _wiring.HandshakeIntake.OnConnectionMessage(CLIENT, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(_token0));
            RunTick();
            _persistence.Loads[0].SetResult(new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord()));
            RunTick();
            ZoneEntryRequest request = Request(NEW_CLIENT, _token0);

            Assert.AreEqual(SessionState.Connected, StateOf());
            Assert.IsFalse(Validator().TryValidateReconnect(in request, out _));
        }

        [Test]
        public void test_token_path_validator_fresh_admitted_entry_still_validates_through_try_validate()
        {
            _wiring.GatewayAdmit.Handle(1u, new AccountID(ACCOUNT), new CharacterID(CHARACTER), _token0, _ticks.Now);
            ZoneEntryRequest request = Request(CLIENT, _token0);

            Assert.IsTrue(Validator().TryValidate(in request, out AccountID account));
            Assert.AreEqual(new AccountID(ACCOUNT), account);
            Assert.IsFalse(Validator().TryValidateReconnect(in request, out _), "an admitted entry is not a ghost");
        }

        // ----- admission / wiring -------------------------------------------------------------------

        [Test]
        public void test_token_path_reconnect_keeps_the_entity_rebinds_the_slot_and_connects_without_loading()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            int joinedBefore = _outbox.CountOfType(PlayerJoinedZone.MessageTypeId);
            int snapshotsBefore = _outbox.CountTo(NEW_CLIENT, ZoneStateSnapshotFragment.MessageTypeId);

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(SessionState.Connected, StateOf());
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count, "the token path never calls LoadCharacter");
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.IsTrue(_wiring.Pool.TryFindPlayerByCharacter(new CharacterID(CHARACTER), out EntityID after));
            Assert.AreEqual(entity, after, "the ghost's EntityID stays");
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot));
            Assert.AreEqual(NEW_CLIENT, slot.ClientId);
            Assert.AreEqual(ZoneSessionState.Connected, slot.State);
            Assert.IsTrue(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out CharacterID mapped));
            Assert.AreEqual(new CharacterID(CHARACTER), mapped);
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(CLIENT, out _), "the old connection no longer maps");
            Assert.Greater(_outbox.CountTo(NEW_CLIENT, ZoneStateSnapshotFragment.MessageTypeId), snapshotsBefore, "snapshot fragments go to the new client");
            Assert.AreEqual(joinedBefore, _outbox.CountOfType(PlayerJoinedZone.MessageTypeId), "no PlayerJoinedZone for a reconnect");
            Assert.IsTrue(_wiring.Reclamation.IsEntering(entity), "the slot is entering again until the new ack");
            Assert.IsTrue(_wiring.TokenStore.TokenMatches(ACCOUNT, rotated), "the token is not rotated again");
        }

        [Test]
        public void test_token_path_reconnect_accepts_the_zone_entry_ack_from_the_new_client_only()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);

            Ack(NEW_CLIENT);

            Assert.IsTrue(_wiring.Reclamation.IsAcked(NEW_CLIENT));
            Assert.IsFalse(_wiring.Reclamation.IsAcked(CLIENT));
            Assert.IsFalse(_wiring.Reclamation.IsEntering(entity));
        }

        [Test]
        public void test_token_path_reconnect_with_the_old_token_after_rotation_closes_silently_and_stays_a_ghost()
        {
            EnterAckAndGhost();
            RotateThroughGateway();
            int sentBefore = _outbox.Sent.Count;
            _disconnector.Disconnected.Clear();

            Handshake(NEW_CLIENT, _token0);

            CollectionAssert.AreEqual(new ulong[] { NEW_CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(sentBefore, _outbox.Sent.Count, "no message to the rejected connection");
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
            _wiring.Sessions.TryGetReauthFailureCount(ACCOUNT, out int failures);
            Assert.AreEqual(0, failures, "a rejected handshake does not count toward REAUTH_FAILURE_LIMIT");
        }

        [Test]
        public void test_token_path_reconnect_when_the_zone_is_not_active_answers_zone_does_not_exist_and_closes()
        {
            EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            _wiring.RequestQuit();
            RunTick();
            Assert.IsFalse(_wiring.Lifecycle.AcceptsPlayers);
            _disconnector.Disconnected.Clear();
            _outbox.Replies.Clear();

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(1, _outbox.Replies.Count);
            Assert.AreEqual(NEW_CLIENT, _outbox.Replies[0].Key);
            Assert.AreEqual(new ZoneFullResponse(Zone, JoinRejectedReason.ZoneDoesNotExist, 0), _outbox.Replies[0].Value);
            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)NEW_CLIENT);
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
        }

        // ----- reconnect edge cases (code-review fixes) -----------------------------------------------

        private const uint THIRD_CLIENT = 3003u;

        private static string Rejected(uint client) => "EntryTokenRejected: client " + client + " ";

        [Test]
        public void test_token_path_reconnect_late_old_client_removal_after_rebind_does_not_touch_the_rebound_session()
        {
            EntityID entity = EnterAckAndGhost(removeOldClient: false);
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);

            _wiring.Admission.RemoveClient(CLIENT); // the transport callback of the connection the zone closed
            _wiring.HandshakeIntake.RemoveClient(CLIENT);
            RunTick();
            RunTick();

            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot));
            Assert.AreEqual(NEW_CLIENT, slot.ClientId);
            Assert.AreEqual(SessionState.Connected, StateOf());
            Assert.IsTrue(_wiring.Reclamation.IsEntering(entity));
            Assert.IsTrue(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_token_path_reconnect_zone_entry_ack_from_the_old_client_after_rebind_is_ignored()
        {
            EntityID entity = EnterAckAndGhost(removeOldClient: false);
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);
            LogAssert.Expect(LogType.Warning, new Regex("UnexpectedZoneEntryAck: client " + CLIENT + " "));

            Ack(CLIENT);

            Assert.IsFalse(_wiring.Reclamation.IsAcked(CLIENT));
            Assert.IsTrue(_wiring.Reclamation.IsEntering(entity), "only the new connection's ack settles the slot");
        }

        [Test]
        public void test_token_path_reconnect_second_handshake_from_the_new_client_is_ignored_with_one_warning()
        {
            EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);
            int snapshots = _outbox.CountTo(NEW_CLIENT, ZoneStateSnapshotFragment.MessageTypeId);
            LogAssert.Expect(LogType.Warning, new Regex("EntryHandshakeAlreadyAdmitted: client " + NEW_CLIENT + " "));

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(SessionState.Connected, StateOf());
            Assert.AreEqual(snapshots, _outbox.CountTo(NEW_CLIENT, ZoneStateSnapshotFragment.MessageTypeId), "no second snapshot");
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
        }

        [Test]
        public void test_token_path_reconnect_twice_ghost_reconnect_ghost_reconnect_keeps_the_entity()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);
            Ack(NEW_CLIENT);
            _wiring.Admission.RemoveClient(NEW_CLIENT);
            _wiring.HandshakeIntake.RemoveClient(NEW_CLIENT);
            for (int i = 0; i < GHOST_AFTER_TICKS && StateOf() != SessionState.Disconnected_SessionActive; i++)
            {
                RunTick();
            }

            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            _wiring.GatewayReconnect.Handle(11u, new AccountID(ACCOUNT), rotated, _ticks.Now);
            Assert.IsTrue(_gatewayOutbox.LastReconnectAccepted);
            byte[] rotatedAgain = _gatewayOutbox.LastReconnectToken;

            Handshake(THIRD_CLIENT, rotatedAgain);

            Assert.AreEqual(SessionState.Connected, StateOf());
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot));
            Assert.AreEqual(THIRD_CLIENT, slot.ClientId);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
            Assert.IsTrue(_wiring.Directory.TryGetCharacterId(THIRD_CLIENT, out _));
        }

        [Test]
        public void test_token_path_reconnect_while_the_session_end_is_in_flight_closes_the_client_and_rebinds_nothing()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            _wiring.SessionEnds.EvaluateSessionTTL(ACCOUNT, _ticks.Now, 0u); // TTL end starts; the queue is not drained yet
            _disconnector.Disconnected.Clear();

            _wiring.HandshakeIntake.OnConnectionMessage(NEW_CLIENT, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(rotated));
            _wiring.HandshakeIntake.Drain(_ticks.Now);

            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)NEW_CLIENT);
            Assert.AreEqual(0, _outbox.CountTo(NEW_CLIENT, ZoneStateSnapshotFragment.MessageTypeId));
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
            Assert.AreNotEqual(SessionState.Connected, StateOf());
            if (_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot))
            {
                Assert.AreEqual(CLIENT, slot.ClientId);
            }
        }

        [Test]
        public void test_token_path_reconnect_without_a_slot_warns_closes_and_stays_a_ghost()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Assert.IsTrue(_wiring.Pool.ReleasePlayer(entity));
            _disconnector.Disconnected.Clear();
            LogAssert.Expect(LogType.Warning, new Regex("ReconnectWithoutSlot"));

            Handshake(NEW_CLIENT, rotated);

            CollectionAssert.AreEqual(new ulong[] { NEW_CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
        }

        [Test]
        public void test_token_path_reconnect_continuation_failure_reverts_to_ghost_with_the_original_expiry_and_closes_the_client()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Assert.IsTrue(_wiring.Sessions.TryGetSessionExpiryTick(ACCOUNT, out uint expiryBefore));
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot before));
            _disconnector.Disconnected.Clear();
            _responder.ThrowOnReady = true;
            LogAssert.Expect(LogType.Error, new Regex("reconnect of client " + NEW_CLIENT + " "));

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            Assert.IsTrue(_wiring.Sessions.TryGetSessionExpiryTick(ACCOUNT, out uint expiryAfter));
            Assert.AreEqual(expiryBefore, expiryAfter);
            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)NEW_CLIENT);
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot after));
            Assert.AreEqual(CLIENT, after.ClientId);
            // The slot is marked a ghost on revert, even when the original ghosting left it Connected (pool slots are never set to
            // Disconnected_SessionActive on heartbeat ghosting; tech debt recorded at Story 017 /story-done).
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, after.State);
            Assert.IsFalse(_wiring.Reclamation.IsEntering(entity));
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
            Assert.IsTrue(_wiring.Directory.TryGetCharacterId(CLIENT, out _), "the ghost's mapping is restored");

            _responder.ThrowOnReady = false; // the ghost can still reconnect afterwards
            Handshake(THIRD_CLIENT, rotated);
            Assert.AreEqual(SessionState.Connected, StateOf());
        }

        [Test]
        public void test_token_path_reconnect_stall_timer_reverts_a_rebound_slot_to_ghost_instead_of_leaving_the_zone()
        {
            EntityID entity = EnterAckAndGhost();
            byte[] rotated = RotateThroughGateway();
            Handshake(NEW_CLIENT, rotated);
            Assert.IsTrue(_wiring.Reclamation.IsEntering(entity));
            _disconnector.Disconnected.Clear();
            int leftBefore = _outbox.CountOfType(PlayerLeftZone.MessageTypeId);

            for (uint i = 0; i < _wiring.Reclamation.StallTicks + 2u; i++)
            {
                RunTick();
            }

            Assert.IsFalse(_wiring.Reclamation.IsEntering(entity));
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount, "the slot stays: the session-end path owns the save");
            Assert.IsTrue(_wiring.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot));
            Assert.AreEqual(CLIENT, slot.ClientId);
            Assert.AreEqual(ZoneSessionState.Disconnected_SessionActive, slot.State);
            Assert.AreEqual(SessionState.Disconnected_SessionActive, StateOf());
            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)NEW_CLIENT);
            Assert.IsTrue(_wiring.Directory.TryGetCharacterId(CLIENT, out _));
            Assert.IsFalse(_wiring.Directory.TryGetCharacterId(NEW_CLIENT, out _));
            Assert.AreEqual(leftBefore, _outbox.CountOfType(PlayerLeftZone.MessageTypeId));
        }

        [Test]
        public void test_token_path_reconnect_does_not_disconnect_an_old_client_the_transport_already_removed()
        {
            EnterAckAndGhost(removeOldClient: true);
            byte[] rotated = RotateThroughGateway();
            _disconnector.Disconnected.Clear();

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(SessionState.Connected, StateOf());
            CollectionAssert.DoesNotContain(_disconnector.Disconnected, (ulong)CLIENT);
        }

        [Test]
        public void test_token_path_reconnect_closes_an_old_client_that_is_still_live()
        {
            EnterAckAndGhost(removeOldClient: false);
            byte[] rotated = RotateThroughGateway();
            _disconnector.Disconnected.Clear();

            Handshake(NEW_CLIENT, rotated);

            Assert.AreEqual(SessionState.Connected, StateOf());
            CollectionAssert.Contains(_disconnector.Disconnected, (ulong)CLIENT);
        }

        [Test]
        public void test_entry_token_rejected_is_logged_once_per_connection()
        {
            // Counted directly: LogAssert.NoUnexpectedReceived would also trip on once-per-domain stand-in logs (test-order dependent).
            int newClientWarnings = 0;
            int thirdClientWarnings = 0;
            void Count(string message, string stackTrace, LogType type)
            {
                if (type != LogType.Warning)
                {
                    return;
                }

                if (message.Contains(Rejected(NEW_CLIENT)))
                {
                    newClientWarnings++;
                }
                else if (message.Contains(Rejected(THIRD_CLIENT)))
                {
                    thirdClientWarnings++;
                }
            }

            LogAssert.Expect(LogType.Warning, new Regex(Rejected(NEW_CLIENT)));
            LogAssert.Expect(LogType.Warning, new Regex(Rejected(THIRD_CLIENT)));
            Application.logMessageReceived += Count;
            try
            {
                Handshake(NEW_CLIENT, Token(0x77));
                Handshake(NEW_CLIENT, Token(0x78)); // same connection: no second warning
                Handshake(THIRD_CLIENT, Token(0x79));
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }

            Assert.AreEqual(1, newClientWarnings, "one warning for the first connection despite two bad handshakes");
            Assert.AreEqual(1, thirdClientWarnings, "a different connection gets its own warning");
        }
    }
}
