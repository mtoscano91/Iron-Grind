using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// EditMode unit tests for Skill System Story 004 - cast intake (CR-SK-1, CR-SK-2, V-1, V-2, EC-SK-11,
    /// AC-SK-04, AC-SK-43; TR-sk-004). "No SkillCastResult" is tested as "nothing stored"; the result type is Story 009.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_CastIntake_Tests
    {
        private const int MAX_PLAYERS = 50;
        private const int MAX_PLAYERS_MIN = 1;
        private const int MAX_PLAYERS_MAX = 255;
        private const int SLOT_A = 0;
        private const int SLOT_B = 1;
        private const int MOB_SLOT = MAX_PLAYERS;
        private const int NEGATIVE_SLOT = -1;
        private const int TOLERANCE = 5;
        private const uint TOLERANCE_TICKS = 5u;
        private const uint AHEAD_TICKS = 3u;
        private const uint SERVER_TICK = 100;
        private const uint STALE_CLIENT_TICK = SERVER_TICK - TOLERANCE_TICKS - 5u;
        private const uint CLIENT_ID = 7;
        private const uint ID_A = 1001;
        private const uint ID_B = 1002;
        private const uint ID_MOB = 2001;
        private const uint ID_UNMAPPED = 3001;
        private const uint ID_NEGATIVE_SLOT = 4001;
        private const uint ID_OTHER = 1999;
        private const uint SKILL_FIRST = 11;
        private const uint SKILL_SECOND = 12;
        private const uint SKILL_THIRD = 13;
        private const uint TARGET = 2001;
        private const uint TARGET_SECOND = 2002;
        private const uint NEXT_TICK = SERVER_TICK + 1;
        private const uint FIRST_TICK_NUMBER = 0u;
        private const uint WRAPPED_STALE_CLIENT_TICK = uint.MaxValue - TOLERANCE_TICKS;   // server 0 is 6 ticks behind (tolerance + 1)
        private const int SHORT_BODY = 15;
        private const int LONG_BODY = 17;
        private const int EMPTY_BODY = 0;
        private const ushort EXPECTED_MESSAGE_TYPE_ID = 0x0308;
        private const int EXPECTED_BODY_SIZE = 16;
        private const uint CODEC_CASTER = 0x01020304u;
        private const uint CODEC_SKILL = 0x05060708u;
        private const uint CODEC_TARGET = 0x090A0B0Cu;
        private const uint CODEC_TICK = 0x0D0E0F10u;
        private const uint DISPATCH_CLIENT = 1u;
        private const uint DISPATCH_SEQUENCE = 1u;
        private const uint DISPATCH_SEQUENCE_SECOND = 2u;
        private const string V1_TEXT = "SECURITY V-1";
        private const string V2_TEXT = "SECURITY V-2";
        private const string MALFORMED_TEXT = "InboundMessageMalformed";
        private const string NO_SLOT_TEXT = "no player slot";
        private const string HANDLER_TEXT = "[SkillCastRequestHandler]";
        private const string DISPATCHER_MALFORMED_TEXT = "[InboundRequestDispatcher] InboundMessageMalformed";

        private static readonly byte[] CODEC_BYTES_LE =
        {
            0x04, 0x03, 0x02, 0x01, 0x08, 0x07, 0x06, 0x05, 0x0C, 0x0B, 0x0A, 0x09, 0x10, 0x0F, 0x0E, 0x0D,
        };

        private static readonly EntityID PLAYER_A = new EntityID(ID_A);
        private static readonly EntityID PLAYER_B = new EntityID(ID_B);

        private FakeSlotIndex _slots;
        private FakeTick _tick;
        private SkillCastIntake _intake;
        private SkillCastRequestHandler _handler;
        private List<string> _warnings;

        private sealed class FakeSlotIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(uint id, int slot) { _map[id] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) => _map.TryGetValue(id.RawValue, out index);
        }

        private sealed class FakeTick : IServerTickSource
        {
            public uint Value;

            public uint ServerTickNumber => Value;
        }

        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<InboundRequestDescriptor> Descriptors = new List<InboundRequestDescriptor>();
            public readonly List<InboundRequestHandler> Handlers = new List<InboundRequestHandler>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
            {
                Descriptors.Add(descriptor);
                Handlers.Add(handler);
            }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        [SetUp]
        public void SetUp()
        {
            _warnings = new List<string>();
            Application.logMessageReceived += OnLogMessage;
            _slots = new FakeSlotIndex();
            _slots.Map(ID_A, SLOT_A);
            _slots.Map(ID_B, SLOT_B);
            _slots.Map(ID_MOB, MOB_SLOT);
            _slots.Map(ID_NEGATIVE_SLOT, NEGATIVE_SLOT);
            _tick = new FakeTick { Value = SERVER_TICK };
            _intake = new SkillCastIntake(_slots, MAX_PLAYERS);
            _handler = new SkillCastRequestHandler(_intake, _tick, TOLERANCE);
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLogMessage;
            _warnings = null;
            _slots = null;
            _tick = null;
            _intake = null;
            _handler = null;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                _warnings.Add(condition);
            }
        }

        private int CountWarnings(string fragment = null)
        {
            int count = 0;
            foreach (string warning in _warnings)
            {
                if (fragment == null || warning.Contains(fragment))
                {
                    count++;
                }
            }

            return count;
        }

        private static void ExpectWarning(string pattern) => LogAssert.Expect(LogType.Warning, new Regex(pattern));

        private static byte[] Encode(in SkillCastRequest request)
        {
            var bytes = new byte[SkillCastRequest.BodySize];
            SkillCastRequestCodec.WriteBody(bytes, in request);
            return bytes;
        }

        private static InboundRequestContext Context(uint sender, uint tick)
            => new InboundRequestContext(CLIENT_ID, new EntityID(sender), default, SkillCastRequest.MessageTypeId, tick, tick, false);

        private void Send(uint sender, in SkillCastRequest request)
        {
            byte[] body = Encode(in request);
            InboundRequestContext context = Context(sender, _tick.Value);
            _handler.Handle(in context, body);
        }

        private void SendRaw(uint sender, byte[] body)
        {
            InboundRequestContext context = Context(sender, _tick.Value);
            _handler.Handle(in context, body);
        }

        private void AssertNothingStored()
        {
            for (int i = 0; i < _intake.Capacity; i++)
            {
                Assert.IsFalse(_intake.TryGetPending(i, out _), "slot " + i);
            }
        }

        // ---- Codec ----

        [Test]
        public void test_castintake_codec_roundtrip_preserves_all_four_fields()
        {
            var request = new SkillCastRequest(CODEC_CASTER, CODEC_SKILL, CODEC_TARGET, CODEC_TICK);
            var buffer = new byte[SkillCastRequest.BodySize];

            int written = SkillCastRequestCodec.WriteBody(buffer, in request);
            bool ok = SkillCastRequestCodec.TryReadBody(buffer, out SkillCastRequest decoded);

            Assert.AreEqual(EXPECTED_BODY_SIZE, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(CODEC_CASTER, decoded.CasterEntityId);
            Assert.AreEqual(CODEC_SKILL, decoded.SkillId);
            Assert.AreEqual(CODEC_TARGET, decoded.TargetEntityId);
            Assert.AreEqual(CODEC_TICK, decoded.ClientTickNumber);
            Assert.AreEqual(CODEC_BYTES_LE, buffer, "little-endian field order");
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void test_castintake_codec_roundtrip_extreme_values_in_all_fields(uint value)
        {
            var request = new SkillCastRequest(value, value, value, value);
            var buffer = new byte[SkillCastRequest.BodySize];

            SkillCastRequestCodec.WriteBody(buffer, in request);
            bool ok = SkillCastRequestCodec.TryReadBody(buffer, out SkillCastRequest decoded);

            Assert.IsTrue(ok);
            Assert.AreEqual(value, decoded.CasterEntityId);
            Assert.AreEqual(value, decoded.SkillId);
            Assert.AreEqual(value, decoded.TargetEntityId);
            Assert.AreEqual(value, decoded.ClientTickNumber);
        }

        [TestCase(EMPTY_BODY)]
        [TestCase(SHORT_BODY)]
        [TestCase(LONG_BODY)]
        public void test_castintake_codec_wrong_length_is_rejected(int length)
        {
            bool ok = SkillCastRequestCodec.TryReadBody(new byte[length], out SkillCastRequest decoded);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(SkillCastRequest).CasterEntityId, decoded.CasterEntityId);
        }

        [Test]
        public void test_castintake_codec_write_into_short_destination_throws_and_writes_nothing()
        {
            var request = new SkillCastRequest(CODEC_CASTER, CODEC_SKILL, CODEC_TARGET, CODEC_TICK);
            var destination = new byte[SHORT_BODY];

            Assert.Throws<ArgumentException>(() => SkillCastRequestCodec.WriteBody(destination, in request));

            Assert.AreEqual(new byte[SHORT_BODY], destination);
        }

        [Test]
        public void test_castintake_message_type_id_and_body_size_match_wire_spec()
        {
            Assert.AreEqual(EXPECTED_MESSAGE_TYPE_ID, SkillCastRequest.MessageTypeId);
            Assert.AreEqual(EXPECTED_BODY_SIZE, SkillCastRequest.BodySize);
        }

        // ---- Registration ----

        [Test]
        public void test_castintake_register_skill_input_registers_the_expected_descriptor()
        {
            var dispatcher = new RecordingDispatcher();

            NetworkingCoreInboundRegistration.RegisterSkillInput(dispatcher, _handler);

            Assert.AreEqual(1, dispatcher.Descriptors.Count);
            InboundRequestDescriptor descriptor = dispatcher.Descriptors[0];
            Assert.AreEqual(EXPECTED_MESSAGE_TYPE_ID, descriptor.MessageTypeId);
            Assert.AreEqual(RpcTypeTag.NotifySkillUsed, descriptor.RpcTypeTag);
            Assert.IsFalse(descriptor.HeldDuringIrreversibleWrite);
            Assert.AreEqual(EXPECTED_BODY_SIZE, descriptor.MaxBodyBytes);
            Assert.IsNotNull(dispatcher.Handlers[0]);
        }

        [Test]
        public void test_castintake_register_skill_input_rejects_null_arguments()
        {
            Assert.Throws<ArgumentNullException>(() => NetworkingCoreInboundRegistration.RegisterSkillInput(null, _handler));
            Assert.Throws<ArgumentNullException>(() => NetworkingCoreInboundRegistration.RegisterSkillInput(new RecordingDispatcher(), null));
        }

        [Test]
        public void test_castintake_real_dispatcher_delivers_16_byte_body_and_not_17_byte_body()
        {
            var harness = new InboundDispatchHarness(false, null, MessageRoutingRegistry.TryGetEntry);
            uint sender = InboundDispatchHarness.EntityOf(DISPATCH_CLIENT).RawValue;
            _slots.Map(sender, SLOT_A);
            var handler = new SkillCastRequestHandler(_intake, harness.TickSource, TOLERANCE);
            NetworkingCoreInboundRegistration.RegisterSkillInput(harness.Dispatcher, handler);
            harness.Dispatcher.Seal();
            harness.AddClient(DISPATCH_CLIENT);
            harness.TickSource.Tick = SERVER_TICK;
            var longBody = new byte[LONG_BODY];
            SkillCastRequestCodec.WriteBody(longBody, new SkillCastRequest(sender, SKILL_FIRST, TARGET, SERVER_TICK));
            harness.SendRequest(DISPATCH_CLIENT, SkillCastRequest.MessageTypeId, DISPATCH_SEQUENCE, longBody);
            harness.Dispatcher.DispatchTick(SERVER_TICK);

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _), "17-byte body must not reach the handler");
            Assert.AreEqual(1, CountWarnings(DISPATCHER_MALFORMED_TEXT), "the dispatcher drops the oversize body with one warning");
            Assert.AreEqual(0, CountWarnings(HANDLER_TEXT), "rejected before the handler");

            harness.SendRequest(DISPATCH_CLIENT, SkillCastRequest.MessageTypeId, DISPATCH_SEQUENCE_SECOND,
                Encode(new SkillCastRequest(sender, SKILL_FIRST, TARGET, SERVER_TICK)));
            harness.Dispatcher.DispatchTick(SERVER_TICK);

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast cast), "16-byte body reaches the handler");
            Assert.AreEqual(SKILL_FIRST, cast.Request.SkillId);
        }

        // ---- V-1 ----

        [Test]
        public void test_castintake_v1_caster_mismatch_stores_nothing_and_logs_security_warning()
        {
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(V1_TEXT));
        }

        [Test]
        public void test_castintake_v1_mapped_victim_is_not_written_for_a_spoofed_caster()
        {
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_B, SKILL_FIRST, TARGET, SERVER_TICK));

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _), "sender slot");
            Assert.IsFalse(_intake.TryGetPending(SLOT_B, out _), "victim slot");
        }

        [Test]
        public void test_castintake_v1_repeated_mismatches_in_one_tick_log_once()
        {
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_SECOND, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_THIRD, TARGET, SERVER_TICK));

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _));
            Assert.AreEqual(1, CountWarnings(V1_TEXT));
        }

        [Test]
        public void test_castintake_v1_before_v2_a_spoofed_and_stale_request_logs_only_v1()
        {
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, STALE_CLIENT_TICK));

            Assert.AreEqual(1, CountWarnings(V1_TEXT));
            Assert.AreEqual(0, CountWarnings(V2_TEXT));
            AssertNothingStored();
        }

        // ---- V-2 ----

        [TestCase(SERVER_TICK, SERVER_TICK - TOLERANCE_TICKS, true, TestName = "test_castintake_v2_at_tolerance_is_accepted")]
        [TestCase(SERVER_TICK, SERVER_TICK - TOLERANCE_TICKS - 1u, false, TestName = "test_castintake_v2_tolerance_plus_one_is_dropped_with_log")]
        [TestCase(SERVER_TICK, SERVER_TICK + AHEAD_TICKS, true, TestName = "test_castintake_v2_client_ahead_of_server_is_accepted")]
        [TestCase(2u, uint.MaxValue, true, TestName = "test_castintake_v2_server_2_client_max_is_accepted")]
        [TestCase(10u, uint.MaxValue, false, TestName = "test_castintake_v2_server_10_client_max_is_dropped_with_log")]
        public void test_castintake_v2_staleness_boundary(uint serverTick, uint clientTick, bool accepted)
        {
            _tick.Value = serverTick;
            if (!accepted)
            {
                ExpectWarning(V2_TEXT);
            }

            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, clientTick));

            Assert.AreEqual(accepted, _intake.TryGetPending(SLOT_A, out PendingSkillCast cast));
            Assert.AreEqual(accepted ? 0 : 1, CountWarnings());
            if (accepted)
            {
                Assert.AreEqual(clientTick, cast.Request.ClientTickNumber);
            }
        }

        [Test]
        public void test_castintake_v2_tolerance_zero_accepts_equal_tick_and_drops_one_behind()
        {
            var handler = new SkillCastRequestHandler(_intake, _tick, 0);
            InboundRequestContext context = Context(ID_A, SERVER_TICK);

            handler.Handle(in context, Encode(new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK)));
            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out _), "client == server accepted");

            _intake.ClearAll();
            ExpectWarning(V2_TEXT);
            handler.Handle(in context, Encode(new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK - 1u)));

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _), "server - 1 dropped");
        }

        [Test]
        public void test_castintake_v2_tolerance_int_max_never_drops_and_does_not_throw()
        {
            var handler = new SkillCastRequestHandler(_intake, _tick, int.MaxValue);
            InboundRequestContext context = Context(ID_A, SERVER_TICK);
            byte[] body = Encode(new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, FIRST_TICK_NUMBER));

            Assert.DoesNotThrow(() => handler.Handle(in context, body));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out _));
        }

        // ---- Log throttle ----

        [Test]
        public void test_castintake_throttle_v1_logs_again_on_a_later_tick()
        {
            ExpectWarning(V1_TEXT);
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));
            _tick.Value = NEXT_TICK;
            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, NEXT_TICK));

            Assert.AreEqual(2, CountWarnings(V1_TEXT));
        }

        [Test]
        public void test_castintake_throttle_v2_logs_again_on_a_later_tick()
        {
            ExpectWarning(V2_TEXT);
            ExpectWarning(V2_TEXT);

            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, STALE_CLIENT_TICK));
            _tick.Value = NEXT_TICK;
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, STALE_CLIENT_TICK));

            Assert.AreEqual(2, CountWarnings(V2_TEXT));
        }

        [Test]
        public void test_castintake_throttle_v1_first_log_at_tick_zero_is_emitted_once()
        {
            _tick.Value = FIRST_TICK_NUMBER;
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, FIRST_TICK_NUMBER));
            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_SECOND, TARGET, FIRST_TICK_NUMBER));

            Assert.AreEqual(1, CountWarnings(V1_TEXT));
        }

        [Test]
        public void test_castintake_throttle_v2_first_log_at_tick_zero_is_emitted_once()
        {
            _tick.Value = FIRST_TICK_NUMBER;
            ExpectWarning(V2_TEXT);

            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, WRAPPED_STALE_CLIENT_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_SECOND, TARGET, WRAPPED_STALE_CLIENT_TICK));

            Assert.AreEqual(1, CountWarnings(V2_TEXT));
        }

        [Test]
        public void test_castintake_throttle_v1_and_v2_in_the_same_tick_do_not_suppress_each_other()
        {
            ExpectWarning(V1_TEXT);
            ExpectWarning(V2_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, STALE_CLIENT_TICK));

            Assert.AreEqual(1, CountWarnings(V1_TEXT));
            Assert.AreEqual(1, CountWarnings(V2_TEXT));
            Assert.AreEqual(2, CountWarnings());
        }

        [Test]
        public void test_castintake_throttle_malformed_and_v1_in_the_same_tick_do_not_suppress_each_other()
        {
            ExpectWarning(MALFORMED_TEXT);
            ExpectWarning(V1_TEXT);

            SendRaw(ID_A, new byte[SHORT_BODY]);
            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));

            Assert.AreEqual(1, CountWarnings(MALFORMED_TEXT));
            Assert.AreEqual(1, CountWarnings(V1_TEXT));
            Assert.AreEqual(2, CountWarnings());
        }

        [Test]
        public void test_castintake_throttle_repeated_malformed_bodies_in_one_tick_log_once()
        {
            ExpectWarning(MALFORMED_TEXT);

            SendRaw(ID_A, new byte[SHORT_BODY]);
            SendRaw(ID_A, new byte[LONG_BODY]);
            SendRaw(ID_A, new byte[EMPTY_BODY]);

            Assert.AreEqual(1, CountWarnings(MALFORMED_TEXT));
            AssertNothingStored();
        }

        [Test]
        public void test_castintake_throttle_repeated_no_slot_drops_in_one_tick_log_once()
        {
            ExpectWarning(NO_SLOT_TEXT);

            _intake.Submit(new EntityID(ID_UNMAPPED), new SkillCastRequest(ID_UNMAPPED, SKILL_FIRST, TARGET, SERVER_TICK));
            _intake.Submit(new EntityID(ID_UNMAPPED), new SkillCastRequest(ID_UNMAPPED, SKILL_SECOND, TARGET, SERVER_TICK));
            _intake.Submit(new EntityID(ID_MOB), new SkillCastRequest(ID_MOB, SKILL_THIRD, TARGET, SERVER_TICK));

            Assert.AreEqual(1, CountWarnings(NO_SLOT_TEXT));
            AssertNothingStored();
        }

        [Test]
        public void test_castintake_throttle_no_slot_logs_again_after_clear_all()
        {
            ExpectWarning(NO_SLOT_TEXT);
            ExpectWarning(NO_SLOT_TEXT);

            _intake.Submit(new EntityID(ID_UNMAPPED), new SkillCastRequest(ID_UNMAPPED, SKILL_FIRST, TARGET, SERVER_TICK));
            _intake.ClearAll();
            _intake.Submit(new EntityID(ID_UNMAPPED), new SkillCastRequest(ID_UNMAPPED, SKILL_FIRST, TARGET, SERVER_TICK));

            Assert.AreEqual(2, CountWarnings(NO_SLOT_TEXT));
        }

        // ---- A dropped request does not block the next one ----

        [Test]
        public void test_castintake_spoofed_request_first_then_valid_request_stores_the_valid_one()
        {
            ExpectWarning(V1_TEXT);

            Send(ID_A, new SkillCastRequest(ID_OTHER, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_SECOND, TARGET, SERVER_TICK));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast cast));
            Assert.AreEqual(SKILL_SECOND, cast.Request.SkillId);
        }

        [Test]
        public void test_castintake_stale_request_first_then_valid_request_stores_the_valid_one()
        {
            ExpectWarning(V2_TEXT);

            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, STALE_CLIENT_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_SECOND, TARGET, SERVER_TICK));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast cast));
            Assert.AreEqual(SKILL_SECOND, cast.Request.SkillId);
        }

        // ---- First wins / clear / two entities ----

        [Test]
        public void test_castintake_three_requests_one_tick_only_first_is_visible_with_all_fields()
        {
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_SECOND, TARGET_SECOND, SERVER_TICK + AHEAD_TICKS));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_THIRD, TARGET_SECOND, SERVER_TICK - TOLERANCE_TICKS));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast cast));
            Assert.AreEqual(PLAYER_A, cast.Caster);
            Assert.AreEqual(ID_A, cast.Request.CasterEntityId);
            Assert.AreEqual(SKILL_FIRST, cast.Request.SkillId);
            Assert.AreEqual(TARGET, cast.Request.TargetEntityId);
            Assert.AreEqual(SERVER_TICK, cast.Request.ClientTickNumber);
            Assert.AreEqual(0, CountWarnings());
        }

        [Test]
        public void test_castintake_clear_empties_slot_and_next_tick_request_is_stored()
        {
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_SECOND, TARGET, SERVER_TICK));

            _intake.ClearAll();

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _), "discarded request is not carried over");

            _tick.Value = NEXT_TICK;
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_THIRD, TARGET, NEXT_TICK));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast cast));
            Assert.AreEqual(SKILL_THIRD, cast.Request.SkillId);
        }

        [Test]
        public void test_castintake_clear_all_on_an_empty_intake_does_not_throw()
        {
            Assert.DoesNotThrow(() => _intake.ClearAll());
            AssertNothingStored();
        }

        [Test]
        public void test_castintake_two_entities_same_tick_both_stored()
        {
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK));
            Send(ID_B, new SkillCastRequest(ID_B, SKILL_SECOND, TARGET, SERVER_TICK));

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out PendingSkillCast castA));
            Assert.IsTrue(_intake.TryGetPending(SLOT_B, out PendingSkillCast castB));
            Assert.AreEqual(SKILL_FIRST, castA.Request.SkillId);
            Assert.AreEqual(SKILL_SECOND, castB.Request.SkillId);
            Assert.AreEqual(PLAYER_B, castB.Caster);
        }

        [TestCase(NEGATIVE_SLOT)]
        [TestCase(MAX_PLAYERS)]
        [TestCase(int.MaxValue)]
        public void test_castintake_try_get_pending_out_of_range_returns_false_and_default(int slot)
        {
            Send(ID_A, new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK));

            bool found = _intake.TryGetPending(slot, out PendingSkillCast cast);

            Assert.IsFalse(found);
            Assert.AreEqual(default(EntityID), cast.Caster);
            Assert.AreEqual(0u, cast.Request.SkillId);
            Assert.AreEqual(MAX_PLAYERS, _intake.Capacity);
        }

        // ---- Malformed / unmapped ----

        [Test]
        public void test_castintake_malformed_short_body_warns_stores_nothing_and_does_not_throw()
        {
            ExpectWarning(MALFORMED_TEXT);
            InboundRequestContext context = Context(ID_A, SERVER_TICK);
            var shortBody = new byte[SHORT_BODY];

            Assert.DoesNotThrow(() => _handler.Handle(in context, shortBody));

            Assert.IsFalse(_intake.TryGetPending(SLOT_A, out _));
            Assert.AreEqual(1, CountWarnings(MALFORMED_TEXT));
        }

        [Test]
        public void test_castintake_malformed_17_byte_body_warns_and_stores_nothing()
        {
            ExpectWarning(MALFORMED_TEXT);

            SendRaw(ID_A, new byte[LONG_BODY]);

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(MALFORMED_TEXT));
        }

        [Test]
        public void test_castintake_unmapped_entity_is_dropped_with_warning()
        {
            ExpectWarning(NO_SLOT_TEXT);

            _intake.Submit(new EntityID(ID_UNMAPPED), new SkillCastRequest(ID_UNMAPPED, SKILL_FIRST, TARGET, SERVER_TICK));

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(NO_SLOT_TEXT));
        }

        [Test]
        public void test_castintake_default_entity_id_unmapped_is_dropped_with_warning_and_does_not_throw()
        {
            ExpectWarning(NO_SLOT_TEXT);
            var request = new SkillCastRequest(0u, SKILL_FIRST, TARGET, SERVER_TICK);

            Assert.DoesNotThrow(() => _intake.Submit(default, in request));

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(NO_SLOT_TEXT));
        }

        [Test]
        public void test_castintake_mob_index_is_dropped_with_warning()
        {
            ExpectWarning(NO_SLOT_TEXT);

            _intake.Submit(new EntityID(ID_MOB), new SkillCastRequest(ID_MOB, SKILL_FIRST, TARGET, SERVER_TICK));

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(NO_SLOT_TEXT));
        }

        [Test]
        public void test_castintake_slot_mapped_to_negative_one_is_dropped_with_warning()
        {
            ExpectWarning(NO_SLOT_TEXT);

            _intake.Submit(new EntityID(ID_NEGATIVE_SLOT), new SkillCastRequest(ID_NEGATIVE_SLOT, SKILL_FIRST, TARGET, SERVER_TICK));

            AssertNothingStored();
            Assert.AreEqual(1, CountWarnings(NO_SLOT_TEXT));
        }

        // ---- Constructors ----

        [Test]
        public void test_castintake_constructor_rejects_out_of_range_arguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SkillCastIntake(_slots, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SkillCastIntake(_slots, 256));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SkillCastRequestHandler(_intake, _tick, -1));
            Assert.Throws<ArgumentNullException>(() => new SkillCastRequestHandler(null, _tick, TOLERANCE));
        }

        [Test]
        public void test_castintake_constructor_rejects_null_slot_index_and_null_tick_source()
        {
            Assert.Throws<ArgumentNullException>(() => new SkillCastIntake(null, MAX_PLAYERS));
            Assert.Throws<ArgumentNullException>(() => new SkillCastRequestHandler(_intake, null, TOLERANCE));
        }

        [TestCase(MAX_PLAYERS_MIN)]
        [TestCase(MAX_PLAYERS_MAX)]
        public void test_castintake_constructor_accepts_the_capacity_limits(int maxPlayers)
        {
            SkillCastIntake intake = null;

            Assert.DoesNotThrow(() => intake = new SkillCastIntake(_slots, maxPlayers));

            Assert.AreEqual(maxPlayers, intake.Capacity);
        }

        // ---- No allocation ----

        [Test]
        public void test_castintake_submit_does_not_allocate()
        {
            var request = new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK);
            _intake.Submit(PLAYER_A, in request);   // warm-up outside the measured block
            _intake.ClearAll();

            Assert.That(() =>
            {
                _intake.Submit(PLAYER_A, in request);
                _intake.Submit(PLAYER_A, in request);   // first-wins discard path
                _intake.TryGetPending(SLOT_A, out _);
                _intake.ClearAll();
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, CountWarnings());
        }

        [Test]
        public void test_castintake_handler_accept_path_does_not_allocate()
        {
            byte[] body = Encode(new SkillCastRequest(ID_A, SKILL_FIRST, TARGET, SERVER_TICK));
            InboundRequestContext warmContext = Context(ID_A, SERVER_TICK);
            _handler.Handle(in warmContext, body);   // warm-up outside the measured block
            _intake.ClearAll();

            Assert.That(() =>
            {
                InboundRequestContext context = Context(ID_A, SERVER_TICK);
                _handler.Handle(in context, body);
            }, Is.Not.AllocatingGCMemory());

            Assert.IsTrue(_intake.TryGetPending(SLOT_A, out _));
            Assert.AreEqual(0, CountWarnings());
        }
    }
}
