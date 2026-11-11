using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Gateway
{
    /// <summary>
    /// Gateway Story 012 (TD-095): the zone-side client entry path. A real <see cref="ZoneInstancingWiring"/> with fake
    /// persistence; handshake bodies are fed to the intake the way the dispatcher's receive callback does, and ticks are
    /// advanced by hand. Nothing here sleeps or opens a socket.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneClientEntry_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const int CAPACITY = 50;
        private const uint CLIENT = 3001u;
        private const uint CHARACTER = 501u;
        private const uint ACCOUNT = 1001u;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        // Bytes 0x01..0x10.
        private static byte[] Token()
        {
            var token = new byte[SessionHandshake.TOKEN_LENGTH];
            for (int i = 0; i < token.Length; i++)
            {
                token[i] = (byte)(i + 1);
            }

            return token;
        }

        private static byte[] Body(uint character, uint zone, byte[] token)
        {
            var body = new byte[SessionHandshake.BodySize];
            SessionHandshakeCodec.WriteBody(body, new SessionHandshake(new CharacterID(character), new ZoneID(zone), token));
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
        }

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<ConnectionMessageDescriptor> ConnectionLevel = new List<ConnectionMessageDescriptor>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) => ConnectionLevel.Add(descriptor);

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

        private sealed class ConfigurableApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode Result = CharacterLoadCode.Success;
            public bool Throw;

            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
            {
                if (Throw)
                {
                    throw new InvalidOperationException("Test applier failure.");
                }

                return Result;
            }
        }

        private sealed class SpyValidator : IZoneEntryTokenValidator
        {
            public int Calls;
            public ZoneEntryRequest Last;

            public bool TryValidate(in ZoneEntryRequest request, out AccountID account)
            {
                Calls++;
                Last = request;
                account = new AccountID(request.Character.RawValue + 5000u);
                return true;
            }
        }

        private sealed class FakeChannel : IGatewayLinkChannel
        {
            public readonly List<byte[]> Frames = new List<byte[]>();

            public int LinkUpCount { get; set; } = 1;

            public bool IsConnected { get; set; } = true;

            public void Start(IPEndPoint endpoint, Func<RegisterZoneMessage> registration) { }

            public void Enqueue(byte[] frame)
            {
                if (IsConnected)
                {
                    Frames.Add(frame);
                }
            }

            public bool TryDequeue(out GatewayToZoneMessage message)
            {
                message = default;
                return false;
            }

            public void Stop() { }
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

        // ----- fixture state ----------------------------------------------------------------------

        private FakeTicks _ticks;
        private RecordingOutbox _outbox;
        private RecordingDispatcher _dispatcher;
        private RecordingDisconnector _disconnector;
        private SpyPersistence _persistence;
        private TickCompletionQueue _queue;
        private ZoneInstancingWiring _wiring;
        private FakeChannel _channel;
        private GatewayLinkPump _pump;
        private ConfigurableApplier _applier;

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks();
            _outbox = new RecordingOutbox();
            _dispatcher = new RecordingDispatcher();
            _disconnector = new RecordingDisconnector();
            _persistence = new SpyPersistence();
            _queue = new TickCompletionQueue();
            _wiring = null;
            _channel = null;
            _pump = null;
            _applier = new ConfigurableApplier();
        }

        [TearDown]
        public void TearDown()
        {
            // Loads a test left pending are cancelled first: DrainOnShutdown logs a critical alert (an unexpected
            // LogError, failing the test) for any tracked task still incomplete at shutdown.
            foreach (TaskCompletionSource<CharacterLoadResult> load in _persistence.Loads)
            {
                load.TrySetCanceled();
            }

            _queue.DrainOnShutdown(TimeSpan.FromMilliseconds(200));
        }

        private void Build(IZoneEntryTokenValidator validator = null, int maxPlayers = CAPACITY, bool withLink = false)
        {
            var options = new ZoneInstancingWiringOptions
            {
                TickRateHz = HZ,
                MaxPlayers = maxPlayers,
                Validator = validator,
                Equipment = new FakeEquipmentService(),
            };
            if (withLink)
            {
                _channel = new FakeChannel();
                options.GatewayOutbox = new GatewayLinkOutbox(_channel);
            }

            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, _dispatcher, _outbox, _persistence, _persistence,
                _applier, new CharacterMutationGate(), new RecordingInventory(), _disconnector, _ => { }, options);
            if (withLink)
            {
                _pump = new GatewayLinkPump(_channel, (IGatewayLinkOutbox)options.GatewayOutbox, _wiring);
            }

            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
        }

        private void Send(uint clientId, byte[] body)
        {
            _wiring.HandshakeIntake.OnConnectionMessage(clientId, SessionHandshake.MessageTypeId, EntityID.Invalid, body);
        }

        private void RunTick()
        {
            _ticks.Now++;
            _queue.Drain(_ticks.Now);
            _wiring.Run(_ticks.Now, 0f);
            _pump?.Run(_ticks.Now, 0f);
        }

        private void Admit(uint account, uint character, byte[] token)
        {
            _wiring.GatewayAdmit.Handle(1u, new AccountID(account), new CharacterID(character), token, _ticks.Now);
        }

        private void CompleteLoad(int index, CharacterLoadResult result)
        {
            _persistence.Loads[index].SetResult(result);
        }

        private static CharacterLoadResult Loaded() => new CharacterLoadResult(CharacterLoadCode.Success, Zone, new CharacterRecord());

        private bool IsConnected(uint account) =>
            _wiring.Sessions.TryGetSessionState(account, out SessionState state) && state == SessionState.Connected;

        private SessionState StateOf(uint account)
        {
            Assert.IsTrue(_wiring.Sessions.TryGetSessionState(account, out SessionState state));
            return state;
        }

        // ----- AC-1: registration -----------------------------------------------------------------

        [Test]
        public void test_session_handshake_registry_row_is_priority_client_to_server_and_neighbours_are_unchanged()
        {
            Assert.AreEqual((ushort)0xE136, SessionHandshake.MessageTypeId);
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(0xE136, out MessageRoutingEntry entry));
            Assert.AreEqual(MessageDirection.ClientToServer, entry.Direction);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);

            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(0xE131, out MessageRoutingEntry snapshotRequest));
            Assert.AreEqual(MessageDirection.ClientToServer, snapshotRequest.Direction);
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(0xE134, out MessageRoutingEntry ack));
            Assert.AreEqual(MessageDirection.ClientToServer, ack.Direction);
            Assert.AreEqual((ushort)0xE134, ZoneEntryAck.MessageTypeId);
        }

        [Test]
        public void test_session_handshake_descriptor_passes_every_body_length_to_the_sink_and_has_no_sender_entity_id_and_is_registered_by_the_wiring()
        {
            ConnectionMessageDescriptor descriptor = SessionHandshakeIntake.Descriptor;

            // The dispatcher drops (does not close) a body above its type's bound, so the bound must let a 25-byte
            // or longer handshake through to the sink, which closes it.
            Assert.AreEqual((ushort)0xE136, descriptor.MessageTypeId);
            Assert.AreEqual((ushort)(InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ServerMessageEnvelope.WireSize), descriptor.MaxBodyBytes);
            Assert.Greater(descriptor.MaxBodyBytes, SessionHandshake.BodySize);
            Assert.IsFalse(descriptor.CarriesSenderEntityId);
            Build();
            Assert.IsTrue(_dispatcher.ConnectionLevel.Exists(d => d.MessageTypeId == 0xE136 && d.MaxBodyBytes == SessionHandshakeIntake.MAX_ACCEPTED_BODY_BYTES && !d.CarriesSenderEntityId));
            Assert.IsTrue(_dispatcher.ConnectionLevel.Exists(d => d.MessageTypeId == ZoneEntryAck.MessageTypeId && d.MaxBodyBytes == ZoneEntryAck.BodySize));
        }

        [Test]
        public void test_session_handshake_codec_round_trips_character_zone_and_token()
        {
            byte[] token = Token();
            var buffer = new byte[SessionHandshake.BodySize];

            int size = SessionHandshakeCodec.WriteBody(buffer, new SessionHandshake(new CharacterID(CHARACTER), Zone, token));
            bool ok = SessionHandshakeCodec.TryReadBody(buffer, out SessionHandshake decoded);

            Assert.AreEqual(24, size);
            Assert.IsTrue(ok);
            Assert.AreEqual(new CharacterID(CHARACTER), decoded.CharacterId);
            Assert.AreEqual(Zone, decoded.TargetZoneId);
            CollectionAssert.AreEqual(token, decoded.SessionToken);
            Assert.AreEqual(CHARACTER, BitConverter.ToUInt32(buffer, 0));
            Assert.AreEqual(ZONE_RAW, BitConverter.ToUInt32(buffer, 4));
            var tokenBytes = new byte[SessionHandshake.TOKEN_LENGTH];
            Array.Copy(buffer, 8, tokenBytes, 0, tokenBytes.Length);
            CollectionAssert.AreEqual(token, tokenBytes);
            Assert.IsFalse(SessionHandshakeCodec.TryReadBody(new byte[23], out _));
            Assert.IsFalse(SessionHandshakeCodec.TryReadBody(new byte[25], out _));
        }

        [Test]
        public void test_session_handshake_codec_write_body_with_a_15_byte_token_throws()
        {
            var buffer = new byte[SessionHandshake.BodySize];
            var message = new SessionHandshake(new CharacterID(CHARACTER), Zone, new byte[SessionHandshake.TOKEN_LENGTH - 1]);

            Assert.Throws<ArgumentOutOfRangeException>(() => SessionHandshakeCodec.WriteBody(buffer, message));
        }

        // ----- AC-2: decode -----------------------------------------------------------------------

        [Test]
        public void test_handshake_body_reaches_begin_entry_once_with_the_same_values_and_the_sending_client()
        {
            var spy = new SpyValidator();
            Build(spy);
            byte[] token = Token();
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            Assert.AreEqual(0, spy.Calls, "the callback only records");

            RunTick();
            RunTick();

            Assert.AreEqual(1, spy.Calls);
            Assert.AreEqual(CLIENT, spy.Last.ClientId);
            Assert.AreEqual(new CharacterID(CHARACTER), spy.Last.Character);
            Assert.AreEqual(Zone, spy.Last.TargetZone);
            CollectionAssert.AreEqual(token, spy.Last.SessionToken);
            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
            Assert.AreEqual(new CharacterID(CHARACTER), _persistence.LoadedCharacters[0]);
        }

        // ----- AC-3: malformed / wrong zone ---------------------------------------------------------

        private void AssertClosedWithNoMessage(SpyValidator spy)
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionHandshakeMalformed"));
            RunTick();

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _outbox.Sent.Count, "a malformed handshake gets no message");
            Assert.AreEqual(0, spy.Calls, "BeginEntry is not called");
            Assert.AreEqual(0, _persistence.LoadedCharacters.Count);
        }

        [Test]
        public void test_handshake_of_23_bytes_closes_the_connection_with_no_message_and_no_begin_entry()
        {
            var spy = new SpyValidator();
            Build(spy);

            Send(CLIENT, new byte[23]);

            AssertClosedWithNoMessage(spy);
        }

        [Test]
        public void test_handshake_of_25_bytes_closes_the_connection_with_no_message_and_no_begin_entry()
        {
            var spy = new SpyValidator();
            Build(spy);
            byte[] body = new byte[25];
            Body(CHARACTER, ZONE_RAW, Token()).CopyTo(body, 0);

            Send(CLIENT, body);

            AssertClosedWithNoMessage(spy);
        }

        [Test]
        public void test_handshake_with_character_zero_closes_the_connection_with_no_message_and_no_begin_entry()
        {
            var spy = new SpyValidator();
            Build(spy);

            Send(CLIENT, Body(0u, ZONE_RAW, Token()));

            AssertClosedWithNoMessage(spy);
        }

        [Test]
        public void test_handshake_with_an_all_zero_token_closes_the_connection_with_no_message_and_no_begin_entry()
        {
            var spy = new SpyValidator();
            Build(spy);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, new byte[SessionHandshake.TOKEN_LENGTH]));

            AssertClosedWithNoMessage(spy);
        }

        [Test]
        public void test_well_formed_handshake_for_another_zone_gets_zone_does_not_exist_with_the_sent_zone_and_no_load()
        {
            var spy = new SpyValidator();
            Build(spy);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW + 1u, Token()));
            RunTick();

            Assert.AreEqual(1, _outbox.Replies.Count);
            Assert.AreEqual(CLIENT, _outbox.Replies[0].Key);
            Assert.AreEqual(new ZoneFullResponse(new ZoneID(ZONE_RAW + 1u), JoinRejectedReason.ZoneDoesNotExist, 0), _outbox.Replies[0].Value);
            Assert.AreEqual(0, spy.Calls, "the zone check runs before the token check");
            Assert.AreEqual(0, _persistence.LoadedCharacters.Count);
            Assert.AreEqual(0, _disconnector.Disconnected.Count);
        }

        // ----- AC-4: shared token store -------------------------------------------------------------

        [Test]
        public void test_token_admitted_through_the_admit_handler_validates_a_handshake_through_the_intake()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
            Assert.AreEqual(0, _disconnector.Disconnected.Count);
        }

        [Test]
        public void test_same_character_with_a_different_token_is_closed_and_starts_no_load()
        {
            Build();
            Admit(ACCOUNT, CHARACTER, Token());
            byte[] other = Token();
            other[0] ^= 0xFF;

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, other));
            RunTick();

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _persistence.LoadedCharacters.Count);
            Assert.AreEqual(0, _outbox.Sent.Count);
        }

        // ----- AC-5: token check ----------------------------------------------------------------------

        [Test]
        public void test_wrong_token_byte_closes_with_no_message_and_the_admitted_token_loads_the_character_once()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            byte[] flipped = Token();
            flipped[7] ^= 0x01;

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, flipped));
            RunTick();

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(0, _outbox.Sent.Count);
            Assert.AreEqual(0, _persistence.LoadedCharacters.Count);

            Send(CLIENT + 1u, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            Assert.AreEqual(1, _persistence.LoadedCharacters.Count);
            Assert.AreEqual(new CharacterID(CHARACTER), _persistence.LoadedCharacters[0]);
        }

        // ----- AC-6: session states -----------------------------------------------------------------

        [Test]
        public void test_session_is_connecting_while_the_load_is_pending_and_connected_after_admission()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();
            Assert.AreEqual(SessionState.Connecting, StateOf(ACCOUNT));

            CompleteLoad(0, Loaded());
            RunTick();

            Assert.AreEqual(SessionState.Connected, StateOf(ACCOUNT));
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
            Assert.IsTrue(_wiring.Sessions.TryGetCharacterId(ACCOUNT, out uint connectedCharacter));
            Assert.AreEqual(CHARACTER, connectedCharacter);
        }

        [Test]
        public void test_session_is_not_connected_after_a_failed_load()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();
            Assert.AreEqual(SessionState.Connecting, StateOf(ACCOUNT));

            CompleteLoad(0, new CharacterLoadResult(CharacterLoadCode.DatabaseError, ZoneID.Invalid));
            RunTick();

            Assert.IsFalse(IsConnected(ACCOUNT));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _outbox.Replies.Count);
            Assert.AreEqual(new ZoneFullResponse(Zone, JoinRejectedReason.LoadCharacterFailed, 0), _outbox.Replies[0].Value);
        }

        [Test]
        public void test_session_is_not_connected_when_the_connection_drops_while_the_load_is_pending()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            _wiring.Admission.RemoveClient(CLIENT);
            CompleteLoad(0, Loaded());
            RunTick();

            Assert.IsFalse(IsConnected(ACCOUNT));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount);
        }

        // Not covered here: the registered character name ("Brakka") on the session registry and EntityState.characterName.
        // Neither source is reachable in an EditMode wiring rig (the real CharacterPersistenceService and the Movement
        // state source need the composition); the AC-AUTH-21 (e) check stays with the composition-level evidence.

        // ----- failure triggers leave no Connecting session ------------------------------------------

        [Test]
        public void test_recheck_refused_when_the_zone_fills_during_the_load_leaves_no_session()
        {
            Build(maxPlayers: 2);
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();
            Assert.AreEqual(SessionState.Connecting, StateOf(ACCOUNT));
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, new CharacterID(2000u + (uint)i), new AccountID(1000u + (uint)i),
                    3000u + (uint)i, out _, out _));
            }

            CompleteLoad(0, Loaded());
            RunTick();

            Assert.IsFalse(IsConnected(ACCOUNT));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(2, _wiring.Pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_superseded_entry_closes_the_first_connection_and_the_second_ends_connected()
        {
            Build();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("EntryHandshakeSupersededInFlight"));
            Send(CLIENT + 1u, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
            Assert.AreEqual(SessionState.Connecting, StateOf(ACCOUNT));
            Assert.AreEqual(2, _persistence.Loads.Count);

            CompleteLoad(1, Loaded());
            RunTick();

            Assert.AreEqual(SessionState.Connected, StateOf(ACCOUNT));
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_apply_loaded_record_failure_code_leaves_no_session_and_no_slot_and_replies_load_failed()
        {
            Build();
            _applier.Result = CharacterLoadCode.DatabaseError;
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("ApplyLoadedRecord returned"));
            CompleteLoad(0, Loaded());
            RunTick();

            Assert.IsFalse(IsConnected(ACCOUNT));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _outbox.Replies.Count);
            Assert.AreEqual(new ZoneFullResponse(Zone, JoinRejectedReason.LoadCharacterFailed, 0), _outbox.Replies[0].Value);
        }

        [Test]
        public void test_apply_loaded_record_throwing_leaves_no_session_and_no_slot_and_replies_load_failed()
        {
            Build();
            _applier.Throw = true;
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("ApplyLoadedRecord threw"));
            CompleteLoad(0, Loaded());
            RunTick();

            Assert.IsFalse(IsConnected(ACCOUNT));
            Assert.IsFalse(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(0, _wiring.Pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _outbox.Replies.Count);
            Assert.AreEqual(new ZoneFullResponse(Zone, JoinRejectedReason.LoadCharacterFailed, 0), _outbox.Replies[0].Value);
        }

        // ----- guard: a session the zone did not create is left alone -----------------------------------

        private const uint FOREIGN_CHARACTER = 999u;

        private void RegisterForeignSession()
        {
            _wiring.Sessions.EnterConnecting(ACCOUNT, _ticks.Now);
            _wiring.Sessions.CompleteAuthSuccess(ACCOUNT, FOREIGN_CHARACTER, _ticks.Now);
        }

        [Test]
        public void test_existing_session_keeps_its_state_and_character_after_the_entry_completes()
        {
            Build();
            RegisterForeignSession();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionAlreadyRegistered"));
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            CompleteLoad(0, Loaded());
            RunTick();

            Assert.AreEqual(SessionState.Connected, StateOf(ACCOUNT));
            Assert.IsTrue(_wiring.Sessions.TryGetCharacterId(ACCOUNT, out uint character));
            Assert.AreEqual(FOREIGN_CHARACTER, character);
            Assert.AreEqual(1, _wiring.Pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_failed_load_does_not_remove_an_existing_session()
        {
            Build();
            RegisterForeignSession();
            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionAlreadyRegistered"));
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();

            CompleteLoad(0, new CharacterLoadResult(CharacterLoadCode.DatabaseError, ZoneID.Invalid));
            RunTick();

            Assert.IsTrue(_wiring.Sessions.IsAccountRegistered(ACCOUNT));
            Assert.AreEqual(SessionState.Connected, StateOf(ACCOUNT));
            Assert.IsTrue(_wiring.Sessions.TryGetCharacterId(ACCOUNT, out uint character));
            Assert.AreEqual(FOREIGN_CHARACTER, character);
        }

        // ----- real dispatcher ----------------------------------------------------------------------

        // A real InboundRequestDispatcher with the intake registered and sealed, fed whole frames through TryAccept.
        private SessionHandshakeIntake BuildRealDispatcherIntake(out IronGrind.Tests.EditMode.Networking.InboundDispatchHarness harness)
        {
            Build();
            harness = new IronGrind.Tests.EditMode.Networking.InboundDispatchHarness();
            harness.Routing.AddRow(SessionHandshake.MessageTypeId, "SessionHandshake", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            var intake = new SessionHandshakeIntake(_wiring.Admission, _disconnector, 4);
            SessionHandshakeIntake.Register(harness.Dispatcher, intake);
            harness.Dispatcher.Seal();
            harness.AddClient(CLIENT);
            return intake;
        }

        [Test]
        public void test_real_dispatcher_passes_a_25_byte_handshake_to_the_intake_which_closes_the_client()
        {
            SessionHandshakeIntake intake = BuildRealDispatcherIntake(out var harness);

            harness.SendConnectionLevel(CLIENT, SessionHandshake.MessageTypeId, 1u, new byte[25]);

            Assert.AreEqual(1, intake.PendingCount);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionHandshakeMalformed"));
            intake.Drain(1u);
            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
        }

        [Test]
        public void test_real_dispatcher_passes_the_longest_allowed_handshake_body_to_the_intake_which_closes_the_client()
        {
            SessionHandshakeIntake intake = BuildRealDispatcherIntake(out var harness);

            harness.SendConnectionLevel(CLIENT, SessionHandshake.MessageTypeId, 1u, new byte[SessionHandshakeIntake.MAX_ACCEPTED_BODY_BYTES]);

            Assert.AreEqual(1, intake.PendingCount);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionHandshakeMalformed"));
            intake.Drain(1u);
            CollectionAssert.AreEqual(new ulong[] { CLIENT }, _disconnector.Disconnected);
        }

        [Test]
        public void test_real_dispatcher_drops_a_handshake_body_one_byte_over_the_bound_before_the_intake()
        {
            SessionHandshakeIntake intake = BuildRealDispatcherIntake(out var harness);

            harness.SendConnectionLevel(CLIENT, SessionHandshake.MessageTypeId, 1u, new byte[SessionHandshakeIntake.MAX_ACCEPTED_BODY_BYTES + 1]);

            Assert.AreEqual(0, intake.PendingCount);
        }

        // ----- Intake -------------------------------------------------------------------------------

        [Test]
        public void test_remove_client_before_the_drain_discards_the_recorded_handshake()
        {
            var spy = new SpyValidator();
            Build(spy);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, Token()));
            Send(CLIENT + 1u, new byte[3]);
            Assert.AreEqual(2, _wiring.HandshakeIntake.PendingCount);

            _wiring.HandshakeIntake.RemoveClient(CLIENT);
            _wiring.HandshakeIntake.RemoveClient(CLIENT + 1u);
            RunTick();

            Assert.AreEqual(0, _wiring.HandshakeIntake.PendingCount);
            Assert.AreEqual(0, spy.Calls);
            Assert.AreEqual(0, _disconnector.Disconnected.Count);
            Assert.AreEqual(0, _persistence.LoadedCharacters.Count);
        }

        [Test]
        public void test_intake_queue_is_bounded_and_counts_the_dropped_handshakes()
        {
            var spy = new SpyValidator();
            Build(spy, maxPlayers: 2);
            int capacity = _wiring.HandshakeIntake.Capacity;
            Assert.AreEqual(4, capacity);

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionHandshakeOverflow"));
            for (int i = 0; i < capacity + 3; i++)
            {
                Send(CLIENT + (uint)i, Body(CHARACTER + (uint)i, ZONE_RAW, Token()));
            }

            Assert.AreEqual(capacity, _wiring.HandshakeIntake.PendingCount);
            Assert.AreEqual(3, _wiring.HandshakeIntake.OverflowDroppedCount);

            RunTick();

            Assert.AreEqual(0, _wiring.HandshakeIntake.PendingCount);
            Assert.AreEqual(capacity, spy.Calls);
        }

        private sealed class ThrowOnFirstValidator : IZoneEntryTokenValidator
        {
            private int _calls;

            public bool TryValidate(in ZoneEntryRequest request, out AccountID account)
            {
                _calls++;
                if (_calls == 1)
                {
                    throw new InvalidOperationException("Test validator failure.");
                }

                account = new AccountID(request.Character.RawValue + 5000u);
                return true;
            }
        }

        [Test]
        public void test_drain_continues_with_the_next_handshake_when_begin_entry_throws()
        {
            Build(new ThrowOnFirstValidator());
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, Token()));
            Send(CLIENT + 1u, Body(CHARACTER + 1u, ZONE_RAW, Token()));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("BeginEntry failed for client"));

            RunTick();

            CollectionAssert.AreEqual(new[] { new CharacterID(CHARACTER + 1u) }, _persistence.LoadedCharacters);
        }

        [Test]
        public void test_second_handshake_from_the_same_client_in_one_tick_is_ignored_and_counted_and_the_client_may_send_again_after_the_drain()
        {
            var spy = new SpyValidator();
            Build(spy);
            Send(CLIENT, Body(CHARACTER, ZONE_RAW, Token()));
            Send(CLIENT, Body(CHARACTER + 1u, ZONE_RAW, Token()));

            Assert.AreEqual(1, _wiring.HandshakeIntake.PendingCount);
            Assert.AreEqual(1, _wiring.HandshakeIntake.DuplicateIgnoredCount);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("SessionHandshakeDuplicate"));
            RunTick();
            Assert.AreEqual(1, spy.Calls);
            Assert.AreEqual(new CharacterID(CHARACTER), spy.Last.Character);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, Token()));
            Assert.AreEqual(1, _wiring.HandshakeIntake.PendingCount);
            Assert.AreEqual(1, _wiring.HandshakeIntake.DuplicateIgnoredCount);
        }

        [Test]
        public void test_intake_constructor_rejects_null_arguments_and_clamps_capacity_zero_to_one()
        {
            Build();

            Assert.Throws<ArgumentNullException>(() => new SessionHandshakeIntake(null, _disconnector, 4));
            Assert.Throws<ArgumentNullException>(() => new SessionHandshakeIntake(_wiring.Admission, null, 4));
            var intake = new SessionHandshakeIntake(_wiring.Admission, _disconnector, 0);
            Assert.AreEqual(1, intake.Capacity);
            intake.OnConnectionMessage(CLIENT, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(CHARACTER, ZONE_RAW, Token()));
            intake.OnConnectionMessage(CLIENT + 1u, SessionHandshake.MessageTypeId, EntityID.Invalid, Body(CHARACTER, ZONE_RAW, Token()));
            Assert.AreEqual(1, intake.PendingCount);
            Assert.AreEqual(1, intake.OverflowDroppedCount);
        }

        [Test]
        public void test_handshakes_run_in_arrival_order()
        {
            var spy = new SpyValidator();
            Build(spy);

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, Token()));
            Send(CLIENT + 1u, Body(CHARACTER + 1u, ZONE_RAW, Token()));
            RunTick();

            CollectionAssert.AreEqual(
                new[] { new CharacterID(CHARACTER), new CharacterID(CHARACTER + 1u) }, _persistence.LoadedCharacters);
        }

        // ----- TD-099 ---------------------------------------------------------------------------------

        [Test]
        public void test_real_handshake_that_turns_an_admitted_entry_into_a_slot_sends_no_player_count()
        {
            Build(withLink: true);
            _pump.Run(0u, 0f);
            for (int i = 0; i < CAPACITY - 2; i++)
            {
                Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, new CharacterID(2000u + (uint)i), new AccountID(1000u + (uint)i),
                    3000u + (uint)i, out _, out _));
            }

            byte[] token = Token();
            Admit(ACCOUNT, CHARACTER, token);
            RunTick();
            Assert.AreEqual(CAPACITY - 1, _wiring.SessionCount);
            _channel.Frames.Clear();

            Send(CLIENT, Body(CHARACTER, ZONE_RAW, token));
            RunTick();
            CompleteLoad(0, Loaded());
            RunTick();
            RunTick();

            Assert.AreEqual(SessionState.Connected, StateOf(ACCOUNT));
            Assert.AreEqual(CAPACITY - 1, _wiring.SessionCount, "the reservation became the slot");
            Assert.AreEqual(0, _channel.Frames.Count, "no PlayerCount frame");
        }
    }
}
