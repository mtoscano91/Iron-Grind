using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Named constants shared by the Story 036 tests (<c>InboundDispatch_*_tests.cs</c>): fake message
    /// type ids and sizes, client, entity and character ids, and the descriptors of the fake types.
    /// </summary>
    internal static class InboundTestIds
    {
        // Fake types of the story. HELD_ROD, PLAIN_ROD and CHAT are R-OD; UU_A, UU_B and CONN_UU are U-U.
        public const ushort TypeHeldRod = 0xE101;
        public const ushort TypePlainRod = 0xE102;
        public const ushort TypeChat = 0xE103;
        public const ushort TypeUuA = 0x0F01;
        public const ushort TypeUuB = 0x0F02;
        public const ushort TypeConnUu = 0x0F03;
        public const ushort TypeServerToClient = 0x0F04;   // routing row of the wrong direction
        public const ushort TypeNoRow = 0x0F05;            // no routing row at all
        public const ushort TypeUnregistered = 0x0F06;     // has a row, never registered on the dispatcher

        public const ushort HeldRodBodySize = 10;
        public const ushort PlainRodBodySize = 12;
        public const ushort ChatBodySize = 386;
        public const ushort UuBodySize = 8;
        public const ushort ConnUuBodySize = 8;

        public const uint ClientOne = 1u;
        public const uint ClientTwo = 2u;
        public const uint ClientThree = 3u;
        public const uint ClientNew = 51u;
        public const int ZoneClientCount = ZoneBufferPool.MAX_PLAYERS_PER_ZONE;

        public const uint EntityBase = 100u;
        public const uint CharacterBase = 200u;
        public const uint UnknownEntityRaw = 9999u;
        public const uint EnvelopeTick = 0u;

        public const byte SeedOne = 1;
        public const byte SeedTwo = 2;
        public const byte SeedThree = 3;
        public const byte SeedFour = 4;

        public const string DispatcherLogPrefix = "[InboundRequestDispatcher]";
        public const string GuardLogPrefix = "[CrossCuttingRpcGuardChain]";
        public const string SetTargetHandlerLogPrefix = "[SetTargetRequestHandler]";

        /// <summary>Longest body of a request type that fits MAX_INBOUND_MESSAGE_BYTES (386).</summary>
        public const int MaxRequestBodyBytes =
            InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ClientEntityMessageEnvelope.WireSize;

        public static InboundRequestDescriptor HeldRod() =>
            new InboundRequestDescriptor(TypeHeldRod, RpcTypeTag.SetTarget, true, HeldRodBodySize);

        public static InboundRequestDescriptor PlainRod() =>
            new InboundRequestDescriptor(TypePlainRod, RpcTypeTag.SetTarget, false, PlainRodBodySize);

        public static InboundRequestDescriptor Chat() =>
            new InboundRequestDescriptor(TypeChat, RpcTypeTag.SetTarget, false, ChatBodySize);

        public static InboundRequestDescriptor UuA() =>
            new InboundRequestDescriptor(TypeUuA, RpcTypeTag.SetTarget, false, UuBodySize);

        public static InboundRequestDescriptor UuB() =>
            new InboundRequestDescriptor(TypeUuB, RpcTypeTag.NotifySkillUsed, false, UuBodySize);

        public static ConnectionMessageDescriptor ConnUu() =>
            new ConnectionMessageDescriptor(TypeConnUu, ConnUuBodySize, false);

        public static Regex RequestFailedRegex(uint clientId, ushort typeId, string stage) =>
            new Regex(Regex.Escape($"{DispatcherLogPrefix} RequestFailed: clientId={clientId} messageType=0x{typeId:X4} stage={stage}"));

        public static Regex AddConnectionFailedRegex(uint clientId) =>
            new Regex(Regex.Escape($"{DispatcherLogPrefix} AddConnectionFailed: clientId={clientId}"));

        public static Regex TryAcceptFailedRegex(uint clientId) =>
            new Regex(Regex.Escape($"{DispatcherLogPrefix} TryAcceptFailed: clientId={clientId}"));

        public static Regex RegistrationSkippedRegex(ushort typeId) =>
            new Regex(Regex.Escape($"{DispatcherLogPrefix} RegistrationSkipped: messageType=0x{typeId:X4}"));
    }

    /// <summary>One recorded call of <see cref="InboundRecordingActivitySink"/>.</summary>
    internal readonly struct InboundActivityCall
    {
        public readonly uint ClientId;
        public readonly uint ServerTick;

        public InboundActivityCall(uint clientId, uint serverTick)
        {
            ClientId = clientId;
            ServerTick = serverTick;
        }
    }

    /// <summary>Records every <c>OnInboundActivity</c> call.</summary>
    internal sealed class InboundRecordingActivitySink : IConnectionActivitySink
    {
        public readonly List<InboundActivityCall> Calls = new List<InboundActivityCall>();

        public void OnInboundActivity(uint clientId, uint serverTick)
        {
            Calls.Add(new InboundActivityCall(clientId, serverTick));
        }
    }

    /// <summary>One recorded connection-level message; the body is a copy.</summary>
    internal sealed class InboundRecordedConnectionMessage
    {
        public readonly uint ClientId;
        public readonly ushort MessageTypeId;
        public readonly EntityID SenderEntityId;
        public readonly byte[] Body;

        public InboundRecordedConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, byte[] body)
        {
            ClientId = clientId;
            MessageTypeId = messageTypeId;
            SenderEntityId = senderEntityId;
            Body = body;
        }
    }

    /// <summary>Records every connection-level message; copies the body inside the call.</summary>
    internal sealed class InboundRecordingConnectionSink : IConnectionMessageSink
    {
        public readonly List<InboundRecordedConnectionMessage> Calls = new List<InboundRecordedConnectionMessage>();

        public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body)
        {
            Calls.Add(new InboundRecordedConnectionMessage(clientId, messageTypeId, senderEntityId, body.ToArray()));
        }
    }

    /// <summary>A connection-level sink that throws on every message.</summary>
    internal sealed class InboundThrowingConnectionSink : IConnectionMessageSink
    {
        public void OnConnectionMessage(uint clientId, ushort messageTypeId, EntityID senderEntityId, ReadOnlySpan<byte> body)
        {
            throw new InvalidOperationException("Test sink failure.");
        }
    }

    /// <summary>A tick source whose value the test sets.</summary>
    internal sealed class InboundSettableTickSource : IServerTickSource
    {
        public uint Tick;

        public uint ServerTickNumber => Tick;
    }

    /// <summary>A directory backed by a dictionary; can be told to throw for a client.</summary>
    internal sealed class InboundDictionaryDirectory : IConnectionCharacterDirectory
    {
        private readonly Dictionary<uint, CharacterID> _characters = new Dictionary<uint, CharacterID>();

        public readonly HashSet<uint> ThrowForClients = new HashSet<uint>();

        public void Set(uint clientId, CharacterID characterId)
        {
            _characters[clientId] = characterId;
        }

        public void Remove(uint clientId)
        {
            _characters.Remove(clientId);
        }

        public bool TryGetCharacterId(uint clientId, out CharacterID charId)
        {
            if (ThrowForClients.Contains(clientId))
            {
                throw new InvalidOperationException("Test directory failure for client " + clientId);
            }

            return _characters.TryGetValue(clientId, out charId);
        }
    }

    /// <summary>
    /// Wraps the real <see cref="CharacterMutationGate"/>; <see cref="IsHeldFailuresRemaining"/> makes the
    /// next that many <c>IsHeld</c> calls throw.
    /// </summary>
    internal sealed class InboundFaultableGate : ICharacterMutationGate
    {
        private readonly CharacterMutationGate _inner = new CharacterMutationGate();

        public int IsHeldFailuresRemaining;

        public event Action<CharacterID> OnGateOpened
        {
            add { _inner.OnGateOpened += value; }
            remove { _inner.OnGateOpened -= value; }
        }

        public bool IsHeld(CharacterID charId)
        {
            if (IsHeldFailuresRemaining > 0)
            {
                IsHeldFailuresRemaining--;
                throw new InvalidOperationException("Test gate failure.");
            }

            return _inner.IsHeld(charId);
        }

        public void Close(CharacterID charId) => _inner.Close(charId);

        public void Open(CharacterID charId) => _inner.Open(charId);
    }

    /// <summary>The fake routing table: a lookup method passed as the <see cref="MessageRoutingLookup"/>.</summary>
    internal sealed class InboundFakeRoutingTable
    {
        private readonly Dictionary<ushort, MessageRoutingEntry> _rows = new Dictionary<ushort, MessageRoutingEntry>();

        public InboundFakeRoutingTable()
        {
            AddRow(InboundTestIds.TypeHeldRod, "HeldRod", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypePlainRod, "PlainRod", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypeChat, "Chat", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypeUuA, "UuA", NetworkChannel.Unreliable, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypeUuB, "UuB", NetworkChannel.Unreliable, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypeConnUu, "ConnUu", NetworkChannel.Unreliable, MessageDirection.ClientToServer);
            AddRow(HeartbeatMessage.MessageTypeId, "Heartbeat", NetworkChannel.Unreliable, MessageDirection.ClientToServer);
            AddRow(InboundTestIds.TypeServerToClient, "ServerToClient", NetworkChannel.ReliableOrdered, MessageDirection.ServerToOwningClient);
            AddRow(InboundTestIds.TypeUnregistered, "Unregistered", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
        }

        public void AddRow(ushort messageTypeId, string name, NetworkChannel channel, MessageDirection direction)
        {
            _rows[messageTypeId] = new MessageRoutingEntry(messageTypeId, name, DesignPillar.EarnedPower, channel,
                direction, MessageDeliveryContext.PriorityPath, false, "Test row.");
        }

        public bool TryGetEntry(ushort messageTypeId, out MessageRoutingEntry entry)
        {
            return _rows.TryGetValue(messageTypeId, out entry);
        }
    }

    /// <summary>One request seen by <see cref="InboundRecordingHandler"/>: the context and a copy of the body.</summary>
    internal sealed class InboundRecordedRequest
    {
        public readonly InboundRequestContext Context;
        public readonly byte[] Body;

        public InboundRecordedRequest(InboundRequestContext context, byte[] body)
        {
            Context = context;
            Body = body;
        }
    }

    /// <summary>
    /// A request handler that copies the context and the body. <see cref="Behaviour"/>, when set, runs after
    /// the call is recorded (and may throw, close a gate, remove a connection).
    /// </summary>
    internal sealed class InboundRecordingHandler
    {
        public readonly List<InboundRecordedRequest> Calls = new List<InboundRecordedRequest>();

        public Action<InboundRequestContext> Behaviour;

        public void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            Calls.Add(new InboundRecordedRequest(context, body.ToArray()));
            Behaviour?.Invoke(context);
        }
    }

    /// <summary>Builds wire messages: envelope, optional <c>SenderEntityID</c>, body.</summary>
    internal static class InboundMessageBuilder
    {
        /// <summary>A 14-byte-envelope message (a request).</summary>
        public static byte[] Request(ushort messageTypeId, uint sequence, uint senderRaw, byte[] body)
        {
            var message = new byte[ClientEntityMessageEnvelope.WireSize + body.Length];
            MessageEnvelopeCodec.Write(message,
                new ClientEntityMessageEnvelope(messageTypeId, sequence, InboundTestIds.EnvelopeTick, senderRaw));
            body.CopyTo(message, ClientEntityMessageEnvelope.WireSize);
            return message;
        }

        /// <summary>A 10-byte-envelope message (connection level, no sender).</summary>
        public static byte[] ConnectionLevel(ushort messageTypeId, uint sequence, byte[] body)
        {
            var message = new byte[ServerMessageEnvelope.WireSize + body.Length];
            MessageEnvelopeCodec.Write(message,
                new ServerMessageEnvelope(messageTypeId, sequence, InboundTestIds.EnvelopeTick));
            body.CopyTo(message, ServerMessageEnvelope.WireSize);
            return message;
        }

        /// <summary>A body of <paramref name="length"/> bytes: seed, seed + 1, ...</summary>
        public static byte[] Body(int length, byte seed)
        {
            var body = new byte[length];
            for (int i = 0; i < length; i++)
            {
                body[i] = (byte)(seed + i);
            }
            return body;
        }
    }

    /// <summary>
    /// Counts log messages whose text starts with a prefix. Needed because <c>LogAssert</c> does not fail
    /// on an unexpected warning and so cannot prove that one was suppressed.
    /// </summary>
    internal sealed class InboundLogCapture : IDisposable
    {
        private readonly string _prefix;
        private readonly List<KeyValuePair<LogType, string>> _entries = new List<KeyValuePair<LogType, string>>();

        public InboundLogCapture(string prefix)
        {
            _prefix = prefix;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (condition.StartsWith(_prefix, StringComparison.Ordinal))
            {
                _entries.Add(new KeyValuePair<LogType, string>(type, condition));
            }
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        /// <summary>Number of warnings, optionally only those containing <paramref name="fragment"/>.</summary>
        public int CountWarnings(string fragment = null)
        {
            int count = 0;
            foreach (KeyValuePair<LogType, string> entry in _entries)
            {
                if (entry.Key == LogType.Warning
                    && (fragment == null || entry.Value.Contains(fragment)))
                {
                    count++;
                }
            }
            return count;
        }

        public string WarningAt(int index)
        {
            int seen = 0;
            foreach (KeyValuePair<LogType, string> entry in _entries)
            {
                if (entry.Key == LogType.Warning)
                {
                    if (seen == index)
                    {
                        return entry.Value;
                    }
                    seen++;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Builds a dispatcher with the real guard chain and mutation gate and the fakes above. Each test
    /// builds its own, so no state is shared. Client <c>k</c> owns entity <c>EntityBase + k</c> and plays
    /// character <c>CharacterBase + k</c>.
    /// </summary>
    internal sealed class InboundDispatchHarness
    {
        public readonly CrossCuttingRpcGuardChain GuardChain = new CrossCuttingRpcGuardChain();
        public readonly ICharacterMutationGate Gate;
        public readonly InboundDictionaryDirectory Directory = new InboundDictionaryDirectory();
        public readonly InboundRecordingActivitySink Activity = new InboundRecordingActivitySink();
        public readonly InboundSettableTickSource TickSource = new InboundSettableTickSource();
        public readonly InboundFakeRoutingTable Routing = new InboundFakeRoutingTable();
        public readonly InboundRecordingHandler Handler = new InboundRecordingHandler();
        public readonly InboundRecordingConnectionSink ConnectionSink = new InboundRecordingConnectionSink();
        public readonly InboundRequestDispatcher Dispatcher;

        public InboundDispatchHarness(bool isDevelopmentBuild = false, ICharacterMutationGate gate = null,
            MessageRoutingLookup routingLookup = null)
        {
            Gate = gate ?? new CharacterMutationGate();
            Dispatcher = new InboundRequestDispatcher(GuardChain, Gate, Directory, Activity, TickSource,
                routingLookup ?? Routing.TryGetEntry, isDevelopmentBuild);
        }

        public static EntityID EntityOf(uint clientId) => new EntityID(InboundTestIds.EntityBase + clientId);

        public static CharacterID CharacterOf(uint clientId) => new CharacterID(InboundTestIds.CharacterBase + clientId);

        /// <summary>Registers the six fake types (not sealed).</summary>
        public void RegisterStandardTypes()
        {
            Dispatcher.Register(InboundTestIds.HeldRod(), Handler.Handle);
            Dispatcher.Register(InboundTestIds.PlainRod(), Handler.Handle);
            Dispatcher.Register(InboundTestIds.Chat(), Handler.Handle);
            Dispatcher.Register(InboundTestIds.UuA(), Handler.Handle);
            Dispatcher.Register(InboundTestIds.UuB(), Handler.Handle);
            Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), ConnectionSink);
        }

        /// <summary>A sealed dispatcher with the standard types and the given clients added.</summary>
        public static InboundDispatchHarness CreateReady(params uint[] clientIds)
        {
            var harness = new InboundDispatchHarness();
            harness.RegisterStandardTypes();
            harness.Dispatcher.Seal();
            foreach (uint clientId in clientIds)
            {
                harness.AddClient(clientId);
            }
            return harness;
        }

        /// <summary>Guard-chain ownership, session-ready and a directory entry for a client.</summary>
        public void RegisterClientIdentity(uint clientId)
        {
            GuardChain.RegisterEntityOwnership(clientId, EntityOf(clientId));
            GuardChain.MarkSessionReady(clientId);
            Directory.Set(clientId, CharacterOf(clientId));
        }

        /// <summary>AddConnection plus <see cref="RegisterClientIdentity"/>.</summary>
        public void AddClient(uint clientId)
        {
            if (!Dispatcher.AddConnection(clientId))
            {
                throw new InvalidOperationException("Harness could not add client " + clientId);
            }
            RegisterClientIdentity(clientId);
        }

        /// <summary>Adds <paramref name="count"/> clients with consecutive ids from <paramref name="firstClientId"/>.</summary>
        public void AddClients(uint firstClientId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                AddClient(firstClientId + (uint)i);
            }
        }

        public bool SendRequest(uint clientId, ushort messageTypeId, uint sequence, byte[] body)
        {
            return SendRequestFrom(clientId, messageTypeId, sequence, EntityOf(clientId).RawValue, body);
        }

        public bool SendRequestFrom(uint clientId, ushort messageTypeId, uint sequence, uint senderRaw, byte[] body)
        {
            byte[] message = InboundMessageBuilder.Request(messageTypeId, sequence, senderRaw, body);
            return Dispatcher.TryAccept(clientId, message);
        }

        public bool SendConnectionLevel(uint clientId, ushort messageTypeId, uint sequence, byte[] body)
        {
            byte[] message = InboundMessageBuilder.ConnectionLevel(messageTypeId, sequence, body);
            return Dispatcher.TryAccept(clientId, message);
        }
    }
}
