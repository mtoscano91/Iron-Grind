using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The only path from a client message to a game system (ADR-014 Decisions 2 to 5, Networking
    /// Core Story 036). It is both the intake (<see cref="IInboundMessageIntake"/>: report activity,
    /// decode the envelope, drop what is unknown, malformed or stale, hand connection-level messages
    /// to a sink, copy requests into a bounded inbox) and the dispatcher
    /// (<see cref="IInboundRequestDispatcher"/>: on the tick, Pass A releases held requests whose
    /// gate is open, Pass B takes the inbox in arrival order and runs guard chain, character, gate,
    /// handler).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Allocation:</b> every array is created in the constructor or at <see cref="Seal"/>.
    /// <see cref="TryAccept"/> and <see cref="DispatchTick"/> allocate nothing per message or per
    /// request; a string is built only in a branch that logs.
    /// </para>
    /// <para>
    /// <b>Threading:</b> not thread-safe. <see cref="TryAccept"/> runs on the main thread outside the
    /// tick; <see cref="DispatchTick"/> runs on the tick thread; the two never overlap
    /// (ADR-014 Verification Required 1).
    /// </para>
    /// <para>
    /// <b>Inbox capacity:</b> the record array holds 50 x 64 entries. Releasing a slot does not
    /// compact it, so a slot that is removed and re-added several times within one tick interval can
    /// leave dead records behind; once the array is full a further request is dropped as
    /// <c>InboundInboxOverflow</c>. Not reachable without repeated connect/disconnect churn inside
    /// one 50 ms interval.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var dispatcher = new InboundRequestDispatcher(guardChain, gate, directory, activitySink, tickLoop,
    ///     MessageRoutingRegistry.TryGetEntry, isDevelopmentBuild: false);
    /// NetworkingCoreInboundRegistration.Register(dispatcher, setTargetHandler, heartbeatSink);
    /// dispatcher.Seal();
    /// dispatcher.AddConnection(clientId);
    /// bool accepted = dispatcher.TryAccept(clientId, receivedBytes);   // receive callback
    /// dispatcher.DispatchTick(tickLoop.ServerTickNumber);              // the tick
    /// </code>
    /// </example>
    public sealed class InboundRequestDispatcher : IInboundRequestDispatcher, IInboundMessageIntake
    {
        private const int SLOT_COUNT = ZoneBufferPool.MAX_PLAYERS_PER_ZONE;
        private const byte SLOT_FREE = 0;
        private const byte SLOT_LIVE = 1;
        private const byte SLOT_REMOVED = 2;

        private const int HOLD_POOL_SIZE = SLOT_COUNT * TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER;
        // Characters with held requests. Twice the slot count: a removed connection's character can
        // still own a queue (until the slot is released) while its new connection holds one too.
        private const int QUEUE_CAPACITY = SLOT_COUNT * 2;

        private const int ANOMALY_UNKNOWN_TYPE = 0;
        private const int ANOMALY_MALFORMED = 1;
        private const int ANOMALY_INBOX_OVERFLOW = 2;
        private const int ANOMALY_NO_CHARACTER = 3;
        private const int ANOMALY_HELD_OVERFLOW = 4;
        private const int ANOMALY_KIND_COUNT = 5;

        private static readonly string[] AnomalyNames =
        {
            "UnknownInboundMessageType",
            "InboundMessageMalformed",
            "InboundInboxOverflow",
            "InboundRequestWithoutCharacter",
            "HeldRequestOverflow",
        };

        private const string DETAIL_SHORT_ENVELOPE = "message shorter than the 10-byte envelope";
        private const string DETAIL_SHORT_FOR_TYPE = "message shorter than the envelope of its type";
        private const string DETAIL_BODY_TOO_LONG = "body longer than MaxBodyBytes of its type";
        private const string DETAIL_COUNT_BOUND = "connection already has MAX_INBOX_REQUESTS_PER_CONNECTION undispatched requests";
        private const string DETAIL_BYTE_BOUND = "body does not fit in the connection's inbox segment";
        private const string DETAIL_ARRAY_FULL = "inbox record array is full";
        private const string DETAIL_HOLD_FULL = "hold queue is full";

        private struct TypeEntry
        {
            public ushort MessageTypeId;
            public bool IsRequest;
            public bool IsUnreliable;
            public bool CarriesSenderEntityId;
            public ushort MaxBodyBytes;
            public InboundRequestDescriptor Request;
            public InboundRequestHandler Handler;
            public IConnectionMessageSink Sink;
            public int UuIndex;
        }

        private struct InboxRecord
        {
            public int Slot;
            public uint Generation;
            public uint ClientId;
            public int TypeIndex;
            public uint SenderRaw;
            public uint ArrivalTick;
            public int BodyOffset;
            public int BodyLength;
        }

        private readonly CrossCuttingRpcGuardChain _guardChain;
        private readonly ICharacterMutationGate _gate;
        private readonly IConnectionCharacterDirectory _characterDirectory;
        private readonly IConnectionActivitySink _activitySink;
        private readonly IServerTickSource _tickSource;
        private readonly MessageRoutingLookup _routingLookup;
        private readonly bool _isDevelopmentBuild;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        private readonly INetworkTestObserver _observer;
#endif

        // Type table: filled by Register*, frozen by Seal.
        private readonly List<TypeEntry> _registrationList = new List<TypeEntry>();
        private readonly Dictionary<ushort, int> _typeIndexById = new Dictionary<ushort, int>();
        private TypeEntry[] _types = Array.Empty<TypeEntry>();
        private bool _sealed;
        private int _uuTypeCount;

        // Connection slots.
        private readonly byte[] _slotState = new byte[SLOT_COUNT];
        private readonly uint[] _slotClientId = new uint[SLOT_COUNT];
        private readonly uint[] _slotGeneration = new uint[SLOT_COUNT];
        private readonly Dictionary<uint, int> _slotByClient = new Dictionary<uint, int>(SLOT_COUNT);
        private bool _hasRemovedSlots;
        private bool _inPass;

        // Per (slot, U-U type) stale state; sized at Seal.
        private bool[] _uuHasValue = Array.Empty<bool>();
        private uint[] _uuHighest = Array.Empty<uint>();

        // Inbox.
        private readonly InboxRecord[] _inboxRecords =
            new InboxRecord[SLOT_COUNT * InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION];
        private readonly byte[] _arena = new byte[SLOT_COUNT * InboundDispatchConstants.MAX_INBOX_BYTES_PER_CONNECTION];
        private readonly int[] _slotInboxCount = new int[SLOT_COUNT];
        private readonly int[] _slotArenaUsed = new int[SLOT_COUNT];
        private int _inboxCount;

        // Anomaly log flags, per (slot, kind), cleared at the end of every DispatchTick.
        private readonly bool[] _anomalyLogged = new bool[SLOT_COUNT * ANOMALY_KIND_COUNT];
        private int _suppressedAnomalyCount;
        private int _staleDropCount;

        // Hold entry pool (parallel arrays) and its free stack.
        private readonly int[] _entryNext = new int[HOLD_POOL_SIZE];
        private readonly int[] _entrySlot = new int[HOLD_POOL_SIZE];
        private readonly uint[] _entryClientId = new uint[HOLD_POOL_SIZE];
        private readonly uint[] _entrySender = new uint[HOLD_POOL_SIZE];
        private readonly int[] _entryTypeIndex = new int[HOLD_POOL_SIZE];
        private readonly uint[] _entryArrivalTick = new uint[HOLD_POOL_SIZE];
        private readonly int[] _entryBodyLength = new int[HOLD_POOL_SIZE];
        private readonly byte[] _entryBody = new byte[HOLD_POOL_SIZE * InboundDispatchConstants.MAX_HELD_BODY_BYTES];
        private readonly int[] _freeEntries = new int[HOLD_POOL_SIZE];
        private int _freeEntryCount;

        // Hold queues (one per character with held requests) and the ordered list of active ones.
        private readonly uint[] _queueCharacter = new uint[QUEUE_CAPACITY];
        private readonly int[] _queueHead = new int[QUEUE_CAPACITY];
        private readonly int[] _queueTail = new int[QUEUE_CAPACITY];
        private readonly int[] _queueCount = new int[QUEUE_CAPACITY];
        private readonly int[] _freeQueues = new int[QUEUE_CAPACITY];
        private int _freeQueueCount;
        private readonly int[] _activeQueues = new int[QUEUE_CAPACITY];
        private int _activeQueueCount;

        /// <summary>
        /// Creates a dispatcher (ADR-014 Decision 3). Allocates all storage. Null arguments throw
        /// <see cref="ArgumentNullException"/>.
        /// </summary>
        /// <param name="guardChain">The guard chain run in Pass B and queried by Pass A.</param>
        /// <param name="gate">The per-character mutation gate (polled, never subscribed to).</param>
        /// <param name="characterDirectory">Resolves a connection's character.</param>
        /// <param name="activitySink">Told of every message of a known connection.</param>
        /// <param name="tickSource">Source of <c>ArrivalTick</c>.</param>
        /// <param name="routingLookup">Routing table lookup; production passes <c>MessageRoutingRegistry.TryGetEntry</c>.</param>
        /// <param name="isDevelopmentBuild">Selects the AC-MCR-03 behaviour for a type with no client-to-server routing row.</param>
        /// <param name="observer">Test/dev-build observer passed to <c>Evaluate</c>; exists only inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c>.</param>
        /// <example>
        /// <code>
        /// var dispatcher = new InboundRequestDispatcher(guardChain, gate, directory, sink, tickLoop,
        ///     MessageRoutingRegistry.TryGetEntry, isDevelopmentBuild: false);
        /// </code>
        /// </example>
        public InboundRequestDispatcher(
            CrossCuttingRpcGuardChain guardChain,
            ICharacterMutationGate gate,
            IConnectionCharacterDirectory characterDirectory,
            IConnectionActivitySink activitySink,
            IServerTickSource tickSource,
            MessageRoutingLookup routingLookup,
            bool isDevelopmentBuild
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            _guardChain = guardChain ?? throw new ArgumentNullException(nameof(guardChain));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _characterDirectory = characterDirectory ?? throw new ArgumentNullException(nameof(characterDirectory));
            _activitySink = activitySink ?? throw new ArgumentNullException(nameof(activitySink));
            _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));
            _routingLookup = routingLookup ?? throw new ArgumentNullException(nameof(routingLookup));
            _isDevelopmentBuild = isDevelopmentBuild;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            _observer = observer;
#endif

            for (int i = 0; i < HOLD_POOL_SIZE; i++)
            {
                _freeEntries[i] = i;
            }
            _freeEntryCount = HOLD_POOL_SIZE;

            for (int i = 0; i < QUEUE_CAPACITY; i++)
            {
                _freeQueues[i] = i;
                _queueHead[i] = -1;
                _queueTail[i] = -1;
            }
            _freeQueueCount = QUEUE_CAPACITY;
        }

        // ------------------------------------------------------------------ diagnostics

        /// <summary>Number of U-U messages dropped as stale since construction (ADR-014 Decision 2, step 4). Never logged.</summary>
        public int StaleDropCount => _staleDropCount;

        /// <summary>Number of anomaly logs suppressed by the once-per-connection-per-tick-interval bound.</summary>
        public int SuppressedAnomalyCount => _suppressedAnomalyCount;

        /// <summary>Number of unused hold entries; <c>MAX_PLAYERS_PER_ZONE x MAX_HELD_REQUESTS_PER_CHARACTER</c> (800) when nothing is held.</summary>
        public int FreeHoldEntryCount => _freeEntryCount;

        /// <summary>Requests of a live connection currently in the inbox; 0 for an unknown or removed connection.</summary>
        /// <param name="clientId">The connection.</param>
        public int PendingRequestCount(uint clientId)
        {
            return _slotByClient.TryGetValue(clientId, out int slot) && _slotState[slot] == SLOT_LIVE
                ? _slotInboxCount[slot]
                : 0;
        }

        // ------------------------------------------------------------------ registration (Decision 3)

        /// <inheritdoc/>
        /// <remarks>
        /// Throws <see cref="InvalidOperationException"/> after <see cref="Seal"/> or when the type is
        /// already registered; <see cref="ArgumentNullException"/> for a null handler;
        /// <see cref="ArgumentOutOfRangeException"/> for a tag that is not an <see cref="RpcTypeTag"/>
        /// member; <see cref="ArgumentException"/> for a body bound above
        /// <c>MAX_INBOUND_MESSAGE_BYTES</c> less 14, or a held type that is U-U, rate-limited or has
        /// a body above <c>MAX_HELD_BODY_BYTES</c>; <see cref="PendingSchemaDispatchException"/> in a
        /// development build when the type has no client-to-server routing row (in a release build
        /// the registration is skipped and a server error is logged).
        /// </remarks>
        public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
        {
            ThrowIfSealed();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ThrowIfDuplicate(descriptor.MessageTypeId);

            bool rateLimited = CrossCuttingRpcGuardChain.IsRateLimited(descriptor.RpcTypeTag);

            int maxBody = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ClientEntityMessageEnvelope.WireSize;
            if (descriptor.MaxBodyBytes > maxBody)
            {
                throw new ArgumentException(
                    $"MaxBodyBytes {descriptor.MaxBodyBytes} of request type 0x{descriptor.MessageTypeId:X4} exceeds {maxBody} " +
                    "(MAX_INBOUND_MESSAGE_BYTES less the 14-byte envelope).", nameof(descriptor));
            }

            if (!TryResolveChannel(descriptor.MessageTypeId, out bool isUnreliable))
            {
                return;
            }

            if (descriptor.HeldDuringIrreversibleWrite)
            {
                if (isUnreliable)
                {
                    throw new ArgumentException(
                        $"Request type 0x{descriptor.MessageTypeId:X4} is held but travels on the U-U channel.", nameof(descriptor));
                }
                if (rateLimited)
                {
                    throw new ArgumentException(
                        $"Request type 0x{descriptor.MessageTypeId:X4} is held but carries rate-limited tag {descriptor.RpcTypeTag}.", nameof(descriptor));
                }
                if (descriptor.MaxBodyBytes > InboundDispatchConstants.MAX_HELD_BODY_BYTES)
                {
                    throw new ArgumentException(
                        $"Request type 0x{descriptor.MessageTypeId:X4} is held but MaxBodyBytes {descriptor.MaxBodyBytes} exceeds " +
                        $"MAX_HELD_BODY_BYTES {InboundDispatchConstants.MAX_HELD_BODY_BYTES}.", nameof(descriptor));
                }
            }

            _typeIndexById.Add(descriptor.MessageTypeId, _registrationList.Count);
            _registrationList.Add(new TypeEntry
            {
                MessageTypeId = descriptor.MessageTypeId,
                IsRequest = true,
                IsUnreliable = isUnreliable,
                CarriesSenderEntityId = true,
                MaxBodyBytes = descriptor.MaxBodyBytes,
                Request = descriptor,
                Handler = handler,
                UuIndex = -1,
            });
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Throws <see cref="InvalidOperationException"/> after <see cref="Seal"/> or when the type is
        /// already registered; <see cref="ArgumentNullException"/> for a null sink;
        /// <see cref="ArgumentException"/> for a body bound above <c>MAX_INBOUND_MESSAGE_BYTES</c> less
        /// the envelope (14, or 10 without <c>SenderEntityID</c>);
        /// <see cref="PendingSchemaDispatchException"/> as for <see cref="Register"/>.
        /// </remarks>
        public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
        {
            ThrowIfSealed();
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            ThrowIfDuplicate(descriptor.MessageTypeId);

            int envelopeBytes = descriptor.CarriesSenderEntityId ? ClientEntityMessageEnvelope.WireSize : ServerMessageEnvelope.WireSize;
            int maxBody = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - envelopeBytes;
            if (descriptor.MaxBodyBytes > maxBody)
            {
                throw new ArgumentException(
                    $"MaxBodyBytes {descriptor.MaxBodyBytes} of connection-level type 0x{descriptor.MessageTypeId:X4} exceeds {maxBody} " +
                    "(MAX_INBOUND_MESSAGE_BYTES less its envelope).", nameof(descriptor));
            }

            if (!TryResolveChannel(descriptor.MessageTypeId, out bool isUnreliable))
            {
                return;
            }

            _typeIndexById.Add(descriptor.MessageTypeId, _registrationList.Count);
            _registrationList.Add(new TypeEntry
            {
                MessageTypeId = descriptor.MessageTypeId,
                IsRequest = false,
                IsUnreliable = isUnreliable,
                CarriesSenderEntityId = descriptor.CarriesSenderEntityId,
                MaxBodyBytes = descriptor.MaxBodyBytes,
                Sink = sink,
                UuIndex = -1,
            });
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Freezes the type table, gives each U-U type an index and sizes the per-connection stale
        /// state. Calling it again does nothing. <see cref="TryAccept"/> returns false before it.
        /// </remarks>
        public void Seal()
        {
            if (_sealed)
            {
                return;
            }

            _types = _registrationList.ToArray();
            int uuCount = 0;
            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i].IsUnreliable)
                {
                    _types[i].UuIndex = uuCount++;
                }
            }

            _uuTypeCount = uuCount;
            _uuHasValue = new bool[SLOT_COUNT * uuCount];
            _uuHighest = new uint[SLOT_COUNT * uuCount];
            _sealed = true;
        }

        private void ThrowIfSealed()
        {
            if (_sealed)
            {
                throw new InvalidOperationException("InboundRequestDispatcher is sealed; registration after Seal() is not allowed.");
            }
        }

        private void ThrowIfDuplicate(ushort messageTypeId)
        {
            if (_typeIndexById.ContainsKey(messageTypeId))
            {
                throw new InvalidOperationException($"Message type 0x{messageTypeId:X4} is already registered.");
            }
        }

        // Reads the channel from the routing table (Decision 3). False = registration skipped
        // (release build, AC-MCR-03). Throws PendingSchemaDispatchException in a development build.
        private bool TryResolveChannel(ushort messageTypeId, out bool isUnreliable)
        {
            if (_routingLookup(messageTypeId, out MessageRoutingEntry entry) && entry.Direction == MessageDirection.ClientToServer)
            {
                isUnreliable = entry.Channel == NetworkChannel.Unreliable;
                return true;
            }

            isUnreliable = false;
            if (_isDevelopmentBuild)
            {
                throw new PendingSchemaDispatchException(messageTypeId);
            }

            Debug.LogError($"[InboundRequestDispatcher] RegistrationSkipped: messageType=0x{messageTypeId:X4} - no client-to-server " +
                "MessageRoutingRegistry row; every message of this type will be dropped as UnknownInboundMessageType.");
            return false;
        }

        // ------------------------------------------------------------------ connections (Decision 4)

        /// <inheritdoc/>
        /// <remarks>
        /// Outside a pass, first releases the storage of connections removed since the last
        /// <see cref="DispatchTick"/>. Returns false and logs a server error when no slot is free or
        /// when the client already has a slot (live, or removed and not yet released).
        /// </remarks>
        public bool AddConnection(uint clientId)
        {
            if (!_inPass)
            {
                ReleaseRemovedSlots();
            }

            if (_slotByClient.ContainsKey(clientId))
            {
                Debug.LogError($"[InboundRequestDispatcher] AddConnectionFailed: clientId={clientId} - the client is already known.");
                return false;
            }

            int slot = -1;
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (_slotState[i] == SLOT_FREE)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Debug.LogError($"[InboundRequestDispatcher] AddConnectionFailed: clientId={clientId} - no free connection slot " +
                    $"({SLOT_COUNT} connections).");
                return false;
            }

            _slotState[slot] = SLOT_LIVE;
            _slotClientId[slot] = clientId;
            _slotByClient.Add(clientId, slot);
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>Only marks the connection removed; nothing is freed. A call for an unknown or already removed client does nothing.</remarks>
        public void RemoveConnection(uint clientId)
        {
            if (_slotByClient.TryGetValue(clientId, out int slot) && _slotState[slot] == SLOT_LIVE)
            {
                _slotState[slot] = SLOT_REMOVED;
                _hasRemovedSlots = true;
            }
        }

        private void ReleaseRemovedSlots()
        {
            if (!_hasRemovedSlots)
            {
                return;
            }

            for (int slot = 0; slot < SLOT_COUNT; slot++)
            {
                if (_slotState[slot] == SLOT_REMOVED)
                {
                    ReleaseSlot(slot);
                }
            }

            _hasRemovedSlots = false;
        }

        // Clears the slot's inbox state, stale state and anomaly flags, returns its hold entries to
        // the pool, and invalidates its inbox records through the generation counter.
        private void ReleaseSlot(int slot)
        {
            _slotByClient.Remove(_slotClientId[slot]);
            _slotState[slot] = SLOT_FREE;
            _slotClientId[slot] = 0;
            _slotGeneration[slot]++;
            _slotInboxCount[slot] = 0;
            _slotArenaUsed[slot] = 0;

            if (_uuTypeCount > 0)
            {
                Array.Clear(_uuHasValue, slot * _uuTypeCount, _uuTypeCount);
                Array.Clear(_uuHighest, slot * _uuTypeCount, _uuTypeCount);
            }
            Array.Clear(_anomalyLogged, slot * ANOMALY_KIND_COUNT, ANOMALY_KIND_COUNT);

            int a = 0;
            while (a < _activeQueueCount)
            {
                int q = _activeQueues[a];
                int e = _queueHead[q];
                int newHead = -1;
                int newTail = -1;
                int newCount = 0;
                while (e != -1)
                {
                    int next = _entryNext[e];
                    if (_entrySlot[e] == slot)
                    {
                        ReturnEntry(e);
                    }
                    else
                    {
                        _entryNext[e] = -1;
                        if (newHead == -1)
                        {
                            newHead = e;
                        }
                        else
                        {
                            _entryNext[newTail] = e;
                        }
                        newTail = e;
                        newCount++;
                    }
                    e = next;
                }

                _queueHead[q] = newHead;
                _queueTail[q] = newTail;
                _queueCount[q] = newCount;
                if (newCount == 0)
                {
                    RemoveActiveQueueAt(a);
                }
                else
                {
                    a++;
                }
            }
        }

        // ------------------------------------------------------------------ intake (Decision 2)

        /// <inheritdoc/>
        /// <remarks>
        /// Runs the seven steps of ADR-014 Decision 2 in order and never throws: any exception inside
        /// (for example from a sink) is logged as a server error and the message is dropped. Returns
        /// true when the message reached a connection-level sink or was accepted into the inbox.
        /// </remarks>
        public bool TryAccept(uint clientId, ReadOnlySpan<byte> message)
        {
            try
            {
                return TryAcceptCore(clientId, message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InboundRequestDispatcher] TryAcceptFailed: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private bool TryAcceptCore(uint clientId, ReadOnlySpan<byte> message)
        {
            // Step 1: known connections only; report the activity before anything is decoded.
            if (!_sealed)
            {
                return false;
            }
            if (!_slotByClient.TryGetValue(clientId, out int slot) || _slotState[slot] != SLOT_LIVE)
            {
                return false;
            }

            uint arrivalTick = _tickSource.ServerTickNumber;
            _activitySink.OnInboundActivity(clientId, arrivalTick);

            // Step 2: envelope and type table.
            if (!MessageEnvelopeCodec.TryRead(message, out ServerMessageEnvelope envelope))
            {
                LogAnomaly(slot, ANOMALY_MALFORMED, clientId, 0, DETAIL_SHORT_ENVELOPE);
                return false;
            }

            if (!_typeIndexById.TryGetValue(envelope.MessageTypeId, out int typeIndex))
            {
                LogAnomaly(slot, ANOMALY_UNKNOWN_TYPE, clientId, envelope.MessageTypeId, null);
                return false;
            }

            ref TypeEntry type = ref _types[typeIndex];

            // Step 3: the envelope of this type, and the body bound.
            int envelopeBytes = type.CarriesSenderEntityId ? ClientEntityMessageEnvelope.WireSize : ServerMessageEnvelope.WireSize;
            if (message.Length < envelopeBytes)
            {
                LogAnomaly(slot, ANOMALY_MALFORMED, clientId, envelope.MessageTypeId, DETAIL_SHORT_FOR_TYPE);
                return false;
            }

            uint senderRaw = type.CarriesSenderEntityId
                ? BinaryPrimitives.ReadUInt32LittleEndian(message.Slice(ServerMessageEnvelope.WireSize, 4))
                : 0u;
            ReadOnlySpan<byte> body = message.Slice(envelopeBytes);
            if (body.Length > type.MaxBodyBytes)
            {
                LogAnomaly(slot, ANOMALY_MALFORMED, clientId, envelope.MessageTypeId, DETAIL_BODY_TOO_LONG);
                return false;
            }

            // Step 4: stale check, U-U types only, per (connection, type).
            int staleIndex = type.UuIndex >= 0 ? (slot * _uuTypeCount) + type.UuIndex : -1;
            if (staleIndex >= 0
                && _uuHasValue[staleIndex]
                && !StaleDiscardComparer.IsNewerVersion(_uuHighest[staleIndex], envelope.SequenceNumber))
            {
                _staleDropCount++;
                return false;
            }

            // Step 5: connection-level messages go to their sink and stop.
            if (!type.IsRequest)
            {
                AdvanceStale(staleIndex, envelope.SequenceNumber);
                type.Sink.OnConnectionMessage(clientId, envelope.MessageTypeId, new EntityID(senderRaw), body);
                return true;
            }

            // Step 6: inbox bounds.
            if (_slotInboxCount[slot] >= InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION)
            {
                LogAnomaly(slot, ANOMALY_INBOX_OVERFLOW, clientId, envelope.MessageTypeId, DETAIL_COUNT_BOUND);
                return false;
            }
            if (_slotArenaUsed[slot] + body.Length > InboundDispatchConstants.MAX_INBOX_BYTES_PER_CONNECTION)
            {
                LogAnomaly(slot, ANOMALY_INBOX_OVERFLOW, clientId, envelope.MessageTypeId, DETAIL_BYTE_BOUND);
                return false;
            }
            if (_inboxCount >= _inboxRecords.Length)
            {
                LogAnomaly(slot, ANOMALY_INBOX_OVERFLOW, clientId, envelope.MessageTypeId, DETAIL_ARRAY_FULL);
                return false;
            }

            // Step 7: copy the body into the connection's segment and record the request.
            int bodyOffset = (slot * InboundDispatchConstants.MAX_INBOX_BYTES_PER_CONNECTION) + _slotArenaUsed[slot];
            body.CopyTo(new Span<byte>(_arena, bodyOffset, body.Length));
            _slotArenaUsed[slot] += body.Length;
            _slotInboxCount[slot]++;

            _inboxRecords[_inboxCount++] = new InboxRecord
            {
                Slot = slot,
                Generation = _slotGeneration[slot],
                ClientId = clientId,
                TypeIndex = typeIndex,
                SenderRaw = senderRaw,
                ArrivalTick = arrivalTick,
                BodyOffset = bodyOffset,
                BodyLength = body.Length,
            };

            AdvanceStale(staleIndex, envelope.SequenceNumber);
            return true;
        }

        private void AdvanceStale(int staleIndex, uint sequenceNumber)
        {
            if (staleIndex >= 0)
            {
                _uuHasValue[staleIndex] = true;
                _uuHighest[staleIndex] = sequenceNumber;
            }
        }

        // ------------------------------------------------------------------ dispatch (Decision 4)

        /// <inheritdoc/>
        /// <remarks>
        /// Releases the storage of removed connections, runs Pass A (held requests whose gate is
        /// open, in the order each character's first request was held) and Pass B (the inbox in
        /// arrival order), empties the inbox, clears the per-tick anomaly flags and calls
        /// <see cref="CrossCuttingRpcGuardChain.FlushRejectionSummary"/> once. Never throws.
        /// </remarks>
        public void DispatchTick(uint currentTick)
        {
            try
            {
                _inPass = false;
                ReleaseRemovedSlots();
                _inPass = true;

                ReleaseHeldRequests(currentTick);
                DispatchInbox(currentTick);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InboundRequestDispatcher] DispatchTickFailed: tick={currentTick} - {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _inboxCount = 0;
                Array.Clear(_slotInboxCount, 0, _slotInboxCount.Length);
                Array.Clear(_slotArenaUsed, 0, _slotArenaUsed.Length);
                _inPass = false;
            }

            try
            {
                _guardChain.FlushRejectionSummary();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InboundRequestDispatcher] DispatchTickFailed: tick={currentTick} - FlushRejectionSummary: {ex.GetType().Name}: {ex.Message}");
            }

            Array.Clear(_anomalyLogged, 0, _anomalyLogged.Length);
        }

        // Pass A (Decision 4): polls the gate before each request, since a released request may close it.
        private void ReleaseHeldRequests(uint currentTick)
        {
            int a = 0;
            while (a < _activeQueueCount)
            {
                int q = _activeQueues[a];
                CharacterID charId = new CharacterID(_queueCharacter[q]);

                while (_queueCount[q] > 0)
                {
                    int e = _queueHead[q];
                    bool popped = false;
                    bool stop = false;
                    try
                    {
                        if (_gate.IsHeld(charId))
                        {
                            stop = true;
                        }
                        else
                        {
                            PopHead(q);
                            popped = true;

                            uint clientId = _entryClientId[e];
                            EntityID sender = new EntityID(_entrySender[e]);
                            int slot = _entrySlot[e];
                            if (_slotState[slot] == SLOT_LIVE && _guardChain.IsLiveOwner(clientId, sender))
                            {
                                ref TypeEntry type = ref _types[_entryTypeIndex[e]];
                                InboundRequestContext context = new InboundRequestContext(
                                    clientId, sender, charId, type.MessageTypeId, _entryArrivalTick[e], currentTick, true);
                                type.Handler(in context,
                                    new ReadOnlySpan<byte>(_entryBody, e * InboundDispatchConstants.MAX_HELD_BODY_BYTES, _entryBodyLength[e]));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogRequestFailure("Release", _entryClientId[e], _types[_entryTypeIndex[e]].MessageTypeId, ex);
                        if (!popped)
                        {
                            // The gate poll itself threw: nothing is known about the gate, so the
                            // character's requests stay held, in order, and are tried again next tick.
                            stop = true;
                        }
                    }
                    finally
                    {
                        if (popped)
                        {
                            ReturnEntry(e);
                        }
                    }

                    if (stop)
                    {
                        break;
                    }
                }

                if (_queueCount[q] == 0)
                {
                    RemoveActiveQueueAt(a);
                }
                else
                {
                    a++;
                }
            }
        }

        // Pass B (Decision 4): guard chain, character, gate, handler, one try/catch per request.
        private void DispatchInbox(uint currentTick)
        {
            for (int i = 0; i < _inboxCount; i++)
            {
                InboxRecord record = _inboxRecords[i];
                if (_slotState[record.Slot] != SLOT_LIVE || _slotGeneration[record.Slot] != record.Generation)
                {
                    continue;
                }

                ref TypeEntry type = ref _types[record.TypeIndex];
                try
                {
                    EntityID sender = new EntityID(record.SenderRaw);
                    InboundRpcDescriptor guardDescriptor = new InboundRpcDescriptor(
                        record.ClientId, sender, type.Request.RpcTypeTag, currentTick);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    RpcGuardResult guardResult = _guardChain.Evaluate(guardDescriptor, _observer);
#else
                    RpcGuardResult guardResult = _guardChain.Evaluate(guardDescriptor);
#endif
                    if (guardResult != RpcGuardResult.Accepted)
                    {
                        continue;
                    }

                    if (!_characterDirectory.TryGetCharacterId(record.ClientId, out CharacterID charId))
                    {
                        LogAnomaly(record.Slot, ANOMALY_NO_CHARACTER, record.ClientId, type.MessageTypeId, null);
                        continue;
                    }

                    ReadOnlySpan<byte> body = new ReadOnlySpan<byte>(_arena, record.BodyOffset, record.BodyLength);
                    if (type.Request.HeldDuringIrreversibleWrite && (_gate.IsHeld(charId) || HeldCount(charId) > 0))
                    {
                        TryHold(charId, in record, body);
                        continue;
                    }

                    InboundRequestContext context = new InboundRequestContext(
                        record.ClientId, sender, charId, type.MessageTypeId, record.ArrivalTick, currentTick, false);
                    type.Handler(in context, body);
                }
                catch (Exception ex)
                {
                    LogRequestFailure("Dispatch", record.ClientId, type.MessageTypeId, ex);
                }
            }
        }

        // ------------------------------------------------------------------ hold queues (Decision 5)

        /// <inheritdoc/>
        public int HeldCount(CharacterID charId)
        {
            int q = FindQueue(charId.RawValue);
            return q < 0 ? 0 : _queueCount[q];
        }

        private int FindQueue(uint characterRaw)
        {
            for (int a = 0; a < _activeQueueCount; a++)
            {
                int q = _activeQueues[a];
                if (_queueCharacter[q] == characterRaw)
                {
                    return q;
                }
            }
            return -1;
        }

        private bool TryHold(CharacterID charId, in InboxRecord record, ReadOnlySpan<byte> body)
        {
            int q = FindQueue(charId.RawValue);
            bool needsQueue = q < 0;
            if ((!needsQueue && _queueCount[q] >= TickCompletionConstants.MAX_HELD_REQUESTS_PER_CHARACTER)
                || _freeEntryCount == 0
                || (needsQueue && _freeQueueCount == 0))
            {
                LogAnomaly(record.Slot, ANOMALY_HELD_OVERFLOW, record.ClientId, _types[record.TypeIndex].MessageTypeId, DETAIL_HOLD_FULL);
                return false;
            }

            if (needsQueue)
            {
                q = _freeQueues[--_freeQueueCount];
                _queueCharacter[q] = charId.RawValue;
                _queueHead[q] = -1;
                _queueTail[q] = -1;
                _queueCount[q] = 0;
                _activeQueues[_activeQueueCount++] = q;
            }

            int e = _freeEntries[--_freeEntryCount];
            _entryNext[e] = -1;
            _entrySlot[e] = record.Slot;
            _entryClientId[e] = record.ClientId;
            _entrySender[e] = record.SenderRaw;
            _entryTypeIndex[e] = record.TypeIndex;
            _entryArrivalTick[e] = record.ArrivalTick;
            _entryBodyLength[e] = body.Length;
            body.CopyTo(new Span<byte>(_entryBody, e * InboundDispatchConstants.MAX_HELD_BODY_BYTES, body.Length));

            if (_queueCount[q] == 0)
            {
                _queueHead[q] = e;
            }
            else
            {
                _entryNext[_queueTail[q]] = e;
            }
            _queueTail[q] = e;
            _queueCount[q]++;
            return true;
        }

        private void PopHead(int q)
        {
            int e = _queueHead[q];
            _queueHead[q] = _entryNext[e];
            _queueCount[q]--;
            if (_queueCount[q] == 0)
            {
                _queueHead[q] = -1;
                _queueTail[q] = -1;
            }
        }

        private void ReturnEntry(int e)
        {
            _freeEntries[_freeEntryCount++] = e;
        }

        // Removes the active queue at list position a (keeping the order of the others) and frees it.
        private void RemoveActiveQueueAt(int a)
        {
            int q = _activeQueues[a];
            for (int i = a; i < _activeQueueCount - 1; i++)
            {
                _activeQueues[i] = _activeQueues[i + 1];
            }
            _activeQueueCount--;
            _queueCount[q] = 0;
            _queueHead[q] = -1;
            _queueTail[q] = -1;
            _freeQueues[_freeQueueCount++] = q;
        }

        // ------------------------------------------------------------------ logging

        // Anomaly warning, at most once per (connection, kind) per tick interval; the message is
        // built only when it is logged.
        private void LogAnomaly(int slot, int kind, uint clientId, ushort messageTypeId, string detail)
        {
            int flagIndex = (slot * ANOMALY_KIND_COUNT) + kind;
            if (_anomalyLogged[flagIndex])
            {
                _suppressedAnomalyCount++;
                return;
            }

            _anomalyLogged[flagIndex] = true;
            Debug.LogWarning($"[InboundRequestDispatcher] {AnomalyNames[kind]}: clientId={clientId} messageType=0x{messageTypeId:X4}"
                + (detail != null ? " - " + detail : string.Empty));
        }

        private static void LogRequestFailure(string stage, uint clientId, ushort messageTypeId, Exception ex)
        {
            Debug.LogError($"[InboundRequestDispatcher] RequestFailed: clientId={clientId} messageType=0x{messageTypeId:X4} " +
                $"stage={stage} - {ex.GetType().Name}: {ex.Message}");
        }
    }
}
