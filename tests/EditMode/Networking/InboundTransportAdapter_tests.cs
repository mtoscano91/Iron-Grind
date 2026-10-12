using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>One shared, ordered log of calls across the dispatcher fake and the transport fake.</summary>
    internal sealed class TransportAdapterCallLog
    {
        public readonly List<string> Entries = new List<string>();
    }

    /// <summary>One message seen by <see cref="TransportAdapterRecordingIntake"/>: the id and a copy of the bytes.</summary>
    internal sealed class TransportAdapterRecordedMessage
    {
        public readonly uint ClientId;
        public readonly byte[] Bytes;

        public TransportAdapterRecordedMessage(uint clientId, byte[] bytes)
        {
            ClientId = clientId;
            Bytes = bytes;
        }
    }

    /// <summary>An intake that copies each span it is given and returns a configurable value.</summary>
    internal sealed class TransportAdapterRecordingIntake : IInboundMessageIntake
    {
        public readonly List<TransportAdapterRecordedMessage> Calls = new List<TransportAdapterRecordedMessage>();

        public bool Result = true;
        public bool Throw;

        public bool TryAccept(uint clientId, ReadOnlySpan<byte> message)
        {
            if (Throw)
            {
                throw new InvalidOperationException("intake fake");
            }
            Calls.Add(new TransportAdapterRecordedMessage(clientId, message.ToArray()));
            return Result;
        }
    }

    /// <summary>An intake that only counts calls and bytes, so it allocates nothing.</summary>
    internal sealed class TransportAdapterCountingIntake : IInboundMessageIntake
    {
        public int CallCount;
        public long ByteCount;

        public bool TryAccept(uint clientId, ReadOnlySpan<byte> message)
        {
            CallCount++;
            ByteCount += message.Length;
            return true;
        }
    }

    /// <summary>A dispatcher fake that records connection calls in the shared log; the other members do nothing.</summary>
    internal sealed class TransportAdapterFakeDispatcher : IInboundRequestDispatcher
    {
        private readonly TransportAdapterCallLog _log;

        public readonly List<uint> Added = new List<uint>();
        public readonly List<uint> Removed = new List<uint>();

        public bool AddResult = true;
        public bool ThrowOnAdd;
        public bool ThrowOnRemove;

        public TransportAdapterFakeDispatcher(TransportAdapterCallLog log)
        {
            _log = log;
        }

        public bool AddConnection(uint clientId)
        {
            Added.Add(clientId);
            _log.Entries.Add("Add:" + clientId);
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("dispatcher fake");
            }
            return AddResult;
        }

        public void RemoveConnection(uint clientId)
        {
            Removed.Add(clientId);
            _log.Entries.Add("Remove:" + clientId);
            if (ThrowOnRemove)
            {
                throw new InvalidOperationException("dispatcher fake");
            }
        }

        public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

        public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

        public void Seal() { }

        public void DispatchTick(uint currentTick) { }

        public int HeldCount(CharacterID charId) => 0;
    }

    /// <summary>A transport seam that records every disconnect request in the shared log.</summary>
    internal sealed class TransportAdapterFakeTransport : IInboundTransportDisconnector
    {
        private readonly TransportAdapterCallLog _log;

        public readonly List<ulong> Disconnected = new List<ulong>();

        public bool Throw;

        public TransportAdapterFakeTransport(TransportAdapterCallLog log)
        {
            _log = log;
        }

        public void DisconnectClient(ulong transportClientId)
        {
            Disconnected.Add(transportClientId);
            _log.Entries.Add("Disconnect:" + transportClientId);
            if (Throw)
            {
                throw new InvalidOperationException("transport fake");
            }
        }
    }

    /// <summary>
    /// EditMode unit tests for Networking Core Story 038 — the plain C# core
    /// <see cref="InboundTransportAdapter"/> (ADR-014 Decisions 2 and 4): length and id checks, the single
    /// <c>TryAccept</c> call, connection events, server-initiated disconnect, allocation, and source
    /// searches. The NGO shell is not used here (this assembly does not reference NGO).
    /// </summary>
    [TestFixture]
    internal sealed class InboundTransportAdapter_Tests
    {
        private const ulong IdOne = 1UL;
        private const ulong IdMaxUint = uint.MaxValue;
        private const ulong IdAboveUint = (ulong)uint.MaxValue + 1UL;
        private const ulong IdSeven = 7UL;
        private const ulong IdFive = 5UL;
        private const ulong IdNine = 9UL;
        private const uint ClientSeven = 7u;
        private const int MinLength = InboundTransportAdapter.MIN_INBOUND_MESSAGE_BYTES;
        private const int MaxLength = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES;
        private const int ValidLength = 32;
        private const int AllocationMessageCount = 1000;
        private const int WarmUpMessageCount = 10;
        private const int PendingOverflowExtra = 1;
        private const ulong OldestPendingId = 1UL;
        private const string AdapterPrefix = "[InboundTransportAdapter]";
        private const string TransportFolder = "src/ServerLogic/Networking/Transport/";
        private const string CoreFile = "InboundTransportAdapter.cs";
        private const string ShellFile = "NgoInboundTransportShell.cs";
        private const string SourceRoot = "src";

        private static readonly string[] ForbiddenNames =
        {
            "CrossCuttingRpcGuardChain", "InboundRequestHandler", "MessageEnvelopeCodec",
        };

        private static readonly string[] ShellOnlyNames = { "OnUnnamedMessage", "FastBufferReader" };

        private TransportAdapterCallLog _log;
        private TransportAdapterRecordingIntake _intake;
        private TransportAdapterFakeDispatcher _dispatcher;
        private TransportAdapterFakeTransport _transport;
        private InboundTransportAdapter _adapter;

        [SetUp]
        public void SetUp()
        {
            _log = new TransportAdapterCallLog();
            _intake = new TransportAdapterRecordingIntake();
            _dispatcher = new TransportAdapterFakeDispatcher(_log);
            _transport = new TransportAdapterFakeTransport(_log);
            _adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport);
        }

        private static Regex AdapterErrorRegex(string name, ulong transportClientId) =>
            new Regex(Regex.Escape($"{AdapterPrefix} {name}: transportClientId={transportClientId}"));

        private static byte[] Message(int length) =>
            InboundMessageBuilder.Body(length, InboundTestIds.SeedOne);

        // =========================================================================================
        // Length boundaries.
        // =========================================================================================

        [TestCase(0, false)]
        [TestCase(MinLength - 1, false)]
        [TestCase(MinLength, true)]
        [TestCase(MaxLength, true)]
        [TestCase(MaxLength + 1, false)]
        public void test_message_length_boundaries_accept_only_ten_to_max(int length, bool accepted)
        {
            byte[] message = Message(length);

            _adapter.OnMessageReceived(IdOne, message);

            Assert.AreEqual(accepted ? 1 : 0, _intake.Calls.Count);
            if (accepted)
            {
                Assert.AreEqual((uint)IdOne, _intake.Calls[0].ClientId);
                CollectionAssert.AreEqual(message, _intake.Calls[0].Bytes, "Same bytes and length.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_message_every_length_zero_to_max_plus_one_never_throws()
        {
            for (int length = 0; length <= MaxLength + 1; length++)
            {
                byte[] message = Message(length);
                Assert.DoesNotThrow(() => _adapter.OnMessageReceived(IdOne, message), "length " + length);
            }

            Assert.AreEqual(MaxLength - MinLength + 1, _intake.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Id boundaries.
        // =========================================================================================

        [TestCase(IdOne)]
        [TestCase(IdMaxUint)]
        public void test_message_id_that_fits_uint_is_passed_unchanged(ulong transportClientId)
        {
            _adapter.OnMessageReceived(transportClientId, Message(ValidLength));

            Assert.AreEqual(1, _intake.Calls.Count);
            Assert.AreEqual((uint)transportClientId, _intake.Calls[0].ClientId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_message_id_above_uint_is_dropped_with_one_error()
        {
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("MessageDropped", IdAboveUint));

            _adapter.OnMessageReceived(IdAboveUint, Message(ValidLength));

            Assert.AreEqual(0, _intake.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_message_bad_length_with_oversize_id_is_dropped_without_a_log()
        {
            _adapter.OnMessageReceived(IdAboveUint, Message(MinLength - 1));

            Assert.AreEqual(0, _intake.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Intake behaviour.
        // =========================================================================================

        [Test]
        public void test_message_intake_returns_false_does_not_throw_and_is_called_once()
        {
            _intake.Result = false;

            Assert.DoesNotThrow(() => _adapter.OnMessageReceived(IdOne, Message(ValidLength)));

            Assert.AreEqual(1, _intake.Calls.Count);
            Assert.AreEqual(0, _dispatcher.Added.Count + _dispatcher.Removed.Count, "No other call.");
            Assert.AreEqual(0, _transport.Disconnected.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_message_accepted_makes_no_connection_or_transport_call()
        {
            _adapter.OnMessageReceived(IdOne, Message(ValidLength));

            Assert.AreEqual(0, _log.Entries.Count);
        }

        // =========================================================================================
        // Connections.
        // =========================================================================================

        [Test]
        public void test_connect_then_disconnect_calls_add_then_remove_once_each()
        {
            _adapter.OnClientConnected(IdSeven);
            _adapter.OnClientDisconnected(IdSeven);

            CollectionAssert.AreEqual(new[] { "Add:7", "Remove:7" }, _log.Entries);
            Assert.AreEqual(0, _transport.Disconnected.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_id_above_uint_adds_nothing_logs_one_error_and_disconnects_it()
        {
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdAboveUint));

            _adapter.OnClientConnected(IdAboveUint);

            Assert.AreEqual(0, _dispatcher.Added.Count);
            CollectionAssert.AreEqual(new[] { IdAboveUint }, _transport.Disconnected);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_disconnect_event_of_oversize_id_calls_no_remove()
        {
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdAboveUint));
            _adapter.OnClientConnected(IdAboveUint);

            _adapter.OnClientDisconnected(IdAboveUint);
            _adapter.OnClientDisconnected(IdAboveUint);

            Assert.AreEqual(0, _dispatcher.Removed.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_dispatcher_refuses_logs_one_adapter_error_and_disconnects_it()
        {
            _dispatcher.AddResult = false;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdFive));

            _adapter.OnClientConnected(IdFive);

            CollectionAssert.AreEqual(new[] { (uint)IdFive }, _dispatcher.Added);
            CollectionAssert.AreEqual(new[] { IdFive }, _transport.Disconnected);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_disconnect_event_after_refused_connect_calls_remove_once()
        {
            _dispatcher.AddResult = false;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdFive));
            _adapter.OnClientConnected(IdFive);

            _adapter.OnClientDisconnected(IdFive);

            CollectionAssert.AreEqual(new[] { (uint)IdFive }, _dispatcher.Removed,
                "If the id was refused as already known, this is what frees its connection.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_real_dispatcher_refuses_known_id_logs_dispatcher_and_adapter_errors()
        {
            var h = InboundDispatchHarness.CreateReady();
            var adapter = new InboundTransportAdapter(h.Dispatcher, h.Dispatcher, _transport);
            adapter.OnClientConnected(IdOne);
            LogAssert.Expect(LogType.Error, InboundTestIds.AddConnectionFailedRegex((uint)IdOne));
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdOne));

            adapter.OnClientConnected(IdOne);

            CollectionAssert.AreEqual(new[] { IdOne }, _transport.Disconnected);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_real_dispatcher_refused_known_id_is_freed_by_its_disconnect_event()
        {
            var h = InboundDispatchHarness.CreateReady();
            var adapter = new InboundTransportAdapter(h.Dispatcher, h.Dispatcher, _transport);
            adapter.OnClientConnected(IdOne);
            LogAssert.Expect(LogType.Error, InboundTestIds.AddConnectionFailedRegex((uint)IdOne));
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdOne));
            adapter.OnClientConnected(IdOne);

            // The refusal asked the transport to disconnect id 1, which closes the live connection.
            adapter.OnClientDisconnected(IdOne);
            bool addedAgain = h.Dispatcher.AddConnection((uint)IdOne);

            Assert.IsTrue(addedAgain, "The dispatcher kept the connection of a client the transport closed.");
            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Server-initiated disconnect.
        // =========================================================================================

        [Test]
        public void test_server_disconnect_removes_before_asking_the_transport()
        {
            _adapter.Disconnect(ClientSeven);

            CollectionAssert.AreEqual(new[] { "Remove:7", "Disconnect:7" }, _log.Entries);
        }

        [Test]
        public void test_server_disconnect_later_transport_event_adds_no_second_remove()
        {
            _adapter.Disconnect(ClientSeven);

            _adapter.OnClientDisconnected(IdSeven);

            Assert.AreEqual(1, _dispatcher.Removed.Count);
        }

        [Test]
        public void test_server_disconnect_event_inside_the_transport_call_adds_no_second_remove()
        {
            var reentrant = new TransportAdapterReentrantTransport();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, reentrant);
            reentrant.Adapter = adapter;

            adapter.Disconnect(ClientSeven);

            Assert.AreEqual(1, _dispatcher.Removed.Count);
        }

        [Test]
        public void test_server_disconnect_second_transport_event_for_same_id_calls_remove()
        {
            _adapter.Disconnect(ClientSeven);
            _adapter.OnClientDisconnected(IdSeven);

            _adapter.OnClientDisconnected(IdSeven);

            Assert.AreEqual(2, _dispatcher.Removed.Count);
        }

        [Test]
        public void test_connect_after_stale_server_disconnect_clears_the_pending_entry()
        {
            _adapter.Disconnect(ClientSeven);

            _adapter.OnClientConnected(IdSeven);
            _adapter.OnClientDisconnected(IdSeven);

            CollectionAssert.AreEqual(new[] { "Remove:7", "Disconnect:7", "Add:7", "Remove:7" }, _log.Entries);
        }

        [Test]
        public void test_server_disconnect_pending_set_full_overwrites_the_oldest_entry_without_a_log()
        {
            // Ids whose disconnect event never comes: the transport no longer knew them.
            for (uint id = 1; id <= InboundTransportAdapter.PENDING_DISCONNECT_CAPACITY; id++)
            {
                _adapter.Disconnect(id);
            }
            uint overflowId = InboundTransportAdapter.PENDING_DISCONNECT_CAPACITY + (uint)PendingOverflowExtra;
            int removedAfterDisconnects = InboundTransportAdapter.PENDING_DISCONNECT_CAPACITY + 1;

            Assert.DoesNotThrow(() => _adapter.Disconnect(overflowId));
            _adapter.OnClientDisconnected(overflowId);
            int removedAfterNewestEvent = _dispatcher.Removed.Count;
            _adapter.OnClientDisconnected(OldestPendingId + 1UL);
            int removedAfterSecondOldestEvent = _dispatcher.Removed.Count;
            _adapter.OnClientDisconnected(OldestPendingId);

            Assert.AreEqual(removedAfterDisconnects, removedAfterNewestEvent, "The newest id was recorded.");
            Assert.AreEqual(removedAfterDisconnects, removedAfterSecondOldestEvent, "The second oldest id was kept.");
            Assert.AreEqual(removedAfterDisconnects + 1, _dispatcher.Removed.Count,
                "Only the oldest id was overwritten; its event gets a second, harmless RemoveConnection.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_message_after_removal_with_real_dispatcher_is_dropped_and_nothing_dispatches()
        {
            var h = InboundDispatchHarness.CreateReady();
            var adapter = new InboundTransportAdapter(h.Dispatcher, h.Dispatcher, _transport);
            adapter.OnClientConnected(IdOne);
            h.RegisterClientIdentity((uint)IdOne);
            adapter.Disconnect((uint)IdOne);
            byte[] message = InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, 1u,
                InboundDispatchHarness.EntityOf((uint)IdOne).RawValue,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne));

            bool accepted = h.Dispatcher.TryAccept((uint)IdOne, message);
            adapter.OnMessageReceived(IdOne, message);
            h.Dispatcher.DispatchTick(1u);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.Handler.Calls.Count, "Nothing dispatched.");
            CollectionAssert.AreEqual(new[] { IdOne }, _transport.Disconnected);
        }

        [Test]
        public void test_message_before_removal_with_real_dispatcher_is_dispatched()
        {
            var h = InboundDispatchHarness.CreateReady();
            var adapter = new InboundTransportAdapter(h.Dispatcher, h.Dispatcher, _transport);
            adapter.OnClientConnected(IdOne);
            h.RegisterClientIdentity((uint)IdOne);
            byte[] message = InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, 1u,
                InboundDispatchHarness.EntityOf((uint)IdOne).RawValue,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne));

            adapter.OnMessageReceived(IdOne, message);
            h.Dispatcher.DispatchTick(1u);

            Assert.AreEqual(1, h.Handler.Calls.Count);
        }

        // =========================================================================================
        // Exceptions from the intake, the dispatcher and the transport seam.
        // =========================================================================================

        [Test]
        public void test_message_intake_throws_logs_one_error_and_does_not_throw()
        {
            _intake.Throw = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("TryAcceptThrew", IdOne));

            Assert.DoesNotThrow(() => _adapter.OnMessageReceived(IdOne, Message(ValidLength)));

            Assert.AreEqual(0, _log.Entries.Count, "No connection or transport call.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_dispatcher_throws_logs_one_error_and_disconnects_it()
        {
            _dispatcher.ThrowOnAdd = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("AddConnectionThrew", IdFive));

            Assert.DoesNotThrow(() => _adapter.OnClientConnected(IdFive));

            CollectionAssert.AreEqual(new[] { IdFive }, _transport.Disconnected);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_disconnect_event_dispatcher_throws_logs_one_error_and_does_not_throw()
        {
            _adapter.OnClientConnected(IdSeven);
            _dispatcher.ThrowOnRemove = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("RemoveConnectionThrew", IdSeven));

            Assert.DoesNotThrow(() => _adapter.OnClientDisconnected(IdSeven));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_server_disconnect_dispatcher_throws_logs_one_error_and_still_asks_the_transport()
        {
            _dispatcher.ThrowOnRemove = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("RemoveConnectionThrew", IdSeven));

            Assert.DoesNotThrow(() => _adapter.Disconnect(ClientSeven));

            CollectionAssert.AreEqual(new[] { IdSeven }, _transport.Disconnected);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_server_disconnect_transport_throws_logs_one_error_and_keeps_no_pending_entry()
        {
            _transport.Throw = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("DisconnectClientThrew", IdSeven));

            Assert.DoesNotThrow(() => _adapter.Disconnect(ClientSeven));
            _adapter.OnClientDisconnected(IdSeven);

            Assert.AreEqual(2, _dispatcher.Removed.Count, "No pending entry was kept, so the event calls RemoveConnection.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_connect_refused_transport_throws_logs_both_errors_and_does_not_throw()
        {
            _dispatcher.AddResult = false;
            _transport.Throw = true;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdFive));
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("DisconnectClientThrew", IdFive));

            Assert.DoesNotThrow(() => _adapter.OnClientConnected(IdFive));

            LogAssert.NoUnexpectedReceived();
        }

        // =========================================================================================
        // Allocation.
        // =========================================================================================

        [Test]
        public void test_accept_path_allocates_nothing_over_1000_messages()
        {
            var intake = new TransportAdapterCountingIntake();
            var adapter = new InboundTransportAdapter(intake, _dispatcher, _transport);
            byte[] message = Message(ValidLength);
            for (int i = 0; i < WarmUpMessageCount; i++)
            {
                adapter.OnMessageReceived(IdOne, message);
            }

            // The constraint TickLoop_CompletionQueue_tests.cs uses: it counts GC.Alloc samples.
            Assert.That(() =>
            {
                for (int i = 0; i < AllocationMessageCount; i++)
                {
                    adapter.OnMessageReceived(IdOne, message);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(AllocationMessageCount + WarmUpMessageCount, intake.CallCount);
        }

        // =========================================================================================
        // Source searches.
        // =========================================================================================

        [TestCase(CoreFile)]
        [TestCase(ShellFile)]
        public void test_source_has_no_guard_handler_or_envelope_codec_reference(string fileName)
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot(), TransportFolder + fileName));

            foreach (string forbidden in ForbiddenNames)
            {
                StringAssert.DoesNotContain(forbidden, text, fileName);
            }
        }

        [Test]
        public void test_source_ngo_receive_types_appear_only_in_the_shell_file()
        {
            string srcRoot = Path.Combine(RepoRoot(), SourceRoot);
            string shellPath = Path.GetFullPath(Path.Combine(RepoRoot(), TransportFolder + ShellFile));
            string shellText = File.ReadAllText(shellPath);
            foreach (string name in ShellOnlyNames)
            {
                StringAssert.Contains(name, shellText, "The shell file must use " + name);
            }

            foreach (string path in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(path), shellPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = File.ReadAllText(path);
                foreach (string name in ShellOnlyNames)
                {
                    StringAssert.DoesNotContain(name, text, path);
                }
            }
        }

        private static string RepoRoot()
        {
            // Application.dataPath is <repo>/Assets; the sources live in <repo>/src.
            return Directory.GetParent(Application.dataPath).FullName;
        }

        // =========================================================================================
        // Client list (Networking Core Story 039).
        // =========================================================================================

        private OutboxAdapterListenerFake NewListener() => new OutboxAdapterListenerFake(_log);

        [Test]
        public void test_client_list_connect_calls_add_client_once()
        {
            OutboxAdapterListenerFake listener = NewListener();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);

            adapter.OnClientConnected(IdSeven);

            CollectionAssert.AreEqual(new[] { ClientSeven }, listener.Added);
            Assert.AreEqual(0, listener.Removed.Count);
            CollectionAssert.AreEqual(new[] { "Add:7", "ClientAdd:7" }, _log.Entries, "After AddConnection returned.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_client_list_refused_connect_adds_no_client()
        {
            OutboxAdapterListenerFake listener = NewListener();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);
            _dispatcher.AddResult = false;
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdFive));

            adapter.OnClientConnected(IdFive);

            Assert.AreEqual(0, listener.Added.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_client_list_disconnect_event_calls_remove_client_once()
        {
            OutboxAdapterListenerFake listener = NewListener();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);
            adapter.OnClientConnected(IdSeven);

            adapter.OnClientDisconnected(IdSeven);

            CollectionAssert.AreEqual(new[] { ClientSeven }, listener.Removed);
            CollectionAssert.AreEqual(new[] { "Add:7", "ClientAdd:7", "Remove:7", "ClientRemove:7" }, _log.Entries);
        }

        [Test]
        public void test_client_list_server_disconnect_then_its_event_calls_remove_client_once()
        {
            OutboxAdapterListenerFake listener = NewListener();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);
            adapter.OnClientConnected(IdSeven);

            adapter.Disconnect(ClientSeven);
            adapter.OnClientDisconnected(IdSeven);

            CollectionAssert.AreEqual(new[] { ClientSeven }, listener.Removed);
            CollectionAssert.AreEqual(new[] { "Add:7", "ClientAdd:7", "Remove:7", "ClientRemove:7", "Disconnect:7" }, _log.Entries);
        }

        [Test]
        public void test_client_list_throwing_listener_logs_one_error_and_changes_nothing_else()
        {
            OutboxAdapterListenerFake listener = NewListener();
            listener.ThrowOnAdd = true;
            listener.ThrowOnRemove = true;
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ClientListenerThrew", IdSeven));

            Assert.DoesNotThrow(() => adapter.OnClientConnected(IdSeven));

            CollectionAssert.AreEqual(new[] { ClientSeven }, _dispatcher.Added);
            Assert.AreEqual(0, _transport.Disconnected.Count, "A listener exception does not disconnect the client.");
            LogAssert.NoUnexpectedReceived();

            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ClientListenerThrew", IdSeven));
            Assert.DoesNotThrow(() => adapter.OnClientDisconnected(IdSeven));
            CollectionAssert.AreEqual(new[] { ClientSeven }, _dispatcher.Removed);

            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ClientListenerThrew", IdSeven));
            Assert.DoesNotThrow(() => adapter.Disconnect(ClientSeven));
            CollectionAssert.AreEqual(new[] { IdSeven }, _transport.Disconnected, "Disconnect still asks the transport.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_client_list_id_above_uint_makes_no_listener_call()
        {
            OutboxAdapterListenerFake listener = NewListener();
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport, listener);
            LogAssert.Expect(LogType.Error, AdapterErrorRegex("ConnectRefused", IdAboveUint));

            adapter.OnClientConnected(IdAboveUint);
            adapter.OnClientDisconnected(IdAboveUint);

            Assert.AreEqual(0, listener.Added.Count);
            Assert.AreEqual(0, listener.Removed.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_client_list_adapter_is_the_disconnect_requester_of_the_outbox()
        {
            var adapter = new InboundTransportAdapter(_intake, _dispatcher, _transport);

            IClientDisconnectRequester requester = adapter;
            requester.Disconnect(ClientSeven);

            CollectionAssert.AreEqual(new[] { "Remove:7", "Disconnect:7" }, _log.Entries);
        }

        /// <summary>A client listener that records its calls in the shared log and can throw.</summary>
        private sealed class OutboxAdapterListenerFake : IClientConnectionListener
        {
            private readonly TransportAdapterCallLog _log;

            public readonly List<uint> Added = new List<uint>();
            public readonly List<uint> Removed = new List<uint>();

            public bool ThrowOnAdd;
            public bool ThrowOnRemove;

            public OutboxAdapterListenerFake(TransportAdapterCallLog log)
            {
                _log = log;
            }

            public void AddClient(uint clientId)
            {
                Added.Add(clientId);
                _log.Entries.Add("ClientAdd:" + clientId);
                if (ThrowOnAdd)
                {
                    throw new InvalidOperationException("listener fake");
                }
            }

            public void RemoveClient(uint clientId)
            {
                Removed.Add(clientId);
                _log.Entries.Add("ClientRemove:" + clientId);
                if (ThrowOnRemove)
                {
                    throw new InvalidOperationException("listener fake");
                }
            }
        }

        /// <summary>A transport seam whose disconnect raises the transport's disconnect event inside the call.</summary>
        private sealed class TransportAdapterReentrantTransport : IInboundTransportDisconnector
        {
            public InboundTransportAdapter Adapter;

            public void DisconnectClient(ulong transportClientId)
            {
                Adapter.OnClientDisconnected(transportClientId);
            }
        }
    }
}
