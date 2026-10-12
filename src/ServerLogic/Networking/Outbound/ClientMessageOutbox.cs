using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The production <see cref="IClientMessageOutbox"/> (Networking Core Story 039): per-client queueing of
    /// priority-path messages under the CR-NET-7.7 cap, the CR-NET-7.1 server envelope, and the send through
    /// <see cref="IClientMessageSendSeam"/>. <see cref="Enqueue"/> copies the body into the client's arena;
    /// <see cref="Flush"/> (the Outbound step of the tick) writes the envelope, issues the sequence number and
    /// sends, at most <see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/> non-exempt messages per client per tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Storage.</b> Everything a client needs is allocated in <see cref="AddClient"/>: a byte arena of
    /// <see cref="OUTBOX_ARENA_BYTES"/>, an entry table of <see cref="OUTBOX_MAX_QUEUED_MESSAGES"/>, a
    /// <see cref="PriorityPathQueue{T}"/> of entry indices, and a <see cref="ConnectionSequenceCounter"/>.
    /// <see cref="Enqueue"/> and <see cref="Flush"/> allocate nothing on the success path. Bytes of sent messages
    /// are given back: the arena is reset when the client's queue is empty and compacted (live entries moved to
    /// the front, in offset order) when a body does not fit at the tail but would fit after compaction.
    /// </para>
    /// <para>
    /// <b>Never throws.</b> <see cref="Enqueue"/> and <see cref="Flush"/> log instead. A message type outside
    /// 0xE000-0xEFFF or without a <see cref="MessageRoutingRegistry"/> row, a body above
    /// <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/> is refused with one error, in development and release
    /// builds alike. A full queue refuses the message, and on the first refusal of that client logs one error
    /// and requests its disconnect through <see cref="SetDisconnectRequester"/>; without a requester the error
    /// says so, and later refusals of the same client are silent. A removed or unknown client gets at most one
    /// warning per tick. An exception from the routing lookup is logged as one error.
    /// </para>
    /// <para>
    /// <b>Limits to know.</b> NGO disconnects a client whose reliable send queue overflows and the outbox cannot
    /// prevent it; the disconnect arrives through the transport adapter like any other. Exempt messages are never
    /// deferred, so more than 8 exempt messages in one tick are all sent that tick; the outbox does not police the
    /// flag. This class is the priority path only: the batch-path story must draw its sequence numbers from
    /// <see cref="TryGetSequenceCounter"/> so that CCR-1's one counter per connection holds.
    /// </para>
    /// <para>
    /// Tick thread only; not thread-safe. <see cref="AddClient"/> and <see cref="RemoveClient"/> are called by the
    /// transport adapter outside the tick. <see cref="RemoveClient"/> is safe to call from inside
    /// <see cref="Enqueue"/> (through the disconnect requester) and from inside the send seam during
    /// <see cref="Flush"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var outbox = new ClientMessageOutbox(sendSeam);
    /// outbox.AddClient(7u);
    /// outbox.Enqueue(7u, SelfDamageEvent.MessageTypeId, body, isCapExempt: false);
    /// outbox.Flush(currentTick: 100u);   // sends envelope + body to client 7
    /// </code>
    /// </example>
    public sealed class ClientMessageOutbox : IClientMessageOutbox, IClientConnectionListener, IZoneOutboundStep
    {
        /// <summary>Most messages one client may have queued at once (queued and deferred).</summary>
        public const int OUTBOX_MAX_QUEUED_MESSAGES = 128;

        /// <summary>Most body bytes one client may have queued at once.</summary>
        public const int OUTBOX_ARENA_BYTES = 16384;

        /// <summary>First message type id of the priority range (R-OD, 0xE000-0xEFFF).</summary>
        public const ushort PRIORITY_RANGE_FIRST = 0xE000;

        /// <summary>Last message type id of the priority range.</summary>
        public const ushort PRIORITY_RANGE_LAST = 0xEFFF;

        private const string LOG_PREFIX = "[ClientMessageOutbox]";
        private const int MAX_TRACKED_WARNINGS = 64;
        private const int SEND_BUFFER_BYTES = ServerMessageEnvelope.WireSize + RUBatchWriter.MAX_MESSAGE_BODY_BYTES;

        private sealed class ClientState
        {
            public readonly uint ClientId;
            public readonly byte[] Arena = new byte[OUTBOX_ARENA_BYTES];
            public readonly int[] EntryOffset = new int[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly int[] EntryLength = new int[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly ushort[] EntryTypeId = new ushort[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly NetworkChannel[] EntryChannel = new NetworkChannel[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly bool[] EntryLive = new bool[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly int[] FreeIndices = new int[OUTBOX_MAX_QUEUED_MESSAGES];
            public readonly PriorityPathQueue<int> Queue = new PriorityPathQueue<int>();
            public readonly List<QueuedMessage<int>> FlushList = new List<QueuedMessage<int>>(OUTBOX_MAX_QUEUED_MESSAGES);
            public readonly ConnectionSequenceCounter Counter = new ConnectionSequenceCounter();
            public int FreeCount;
            public int Tail;
            public int LiveCount;
            public int LiveBytes;
            public bool DisconnectRequested;
            public bool IsRemoved;

            public ClientState(uint clientId)
            {
                ClientId = clientId;
                for (int i = 0; i < OUTBOX_MAX_QUEUED_MESSAGES; i++)
                {
                    FreeIndices[i] = OUTBOX_MAX_QUEUED_MESSAGES - 1 - i;
                }
                FreeCount = OUTBOX_MAX_QUEUED_MESSAGES;
                Queue.Reserve(OUTBOX_MAX_QUEUED_MESSAGES);
            }
        }

        private readonly IClientMessageSendSeam _sendSeam;
        private readonly MessageRoutingLookup _routingLookup;
        private readonly Dictionary<uint, ClientState> _clientsById = new Dictionary<uint, ClientState>();
        private readonly List<ClientState> _clients = new List<ClientState>();
        private readonly byte[] _sendBuffer = new byte[SEND_BUFFER_BYTES];
        private readonly int[] _compactScratch = new int[OUTBOX_MAX_QUEUED_MESSAGES];
        private readonly uint[] _warnedIds = new uint[MAX_TRACKED_WARNINGS];
        private IClientDisconnectRequester _disconnectRequester;
        private int _warnedCount;
        private bool _flushing;
        private bool _listDirty;

        /// <summary>Creates the outbox. No client exists until <see cref="AddClient"/>.</summary>
        /// <param name="sendSeam">Receives each finished message; not null.</param>
        /// <param name="routingLookup">
        /// The routing table lookup that gives each message its channel; null means
        /// <see cref="MessageRoutingRegistry.TryGetEntry"/>. Tests pass a fake table.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="sendSeam"/> is null.</exception>
        public ClientMessageOutbox(IClientMessageSendSeam sendSeam, MessageRoutingLookup routingLookup = null)
        {
            _sendSeam = sendSeam ?? throw new ArgumentNullException(nameof(sendSeam));
            _routingLookup = routingLookup ?? MessageRoutingRegistry.TryGetEntry;
        }

        /// <summary>
        /// Sets the seam asked to disconnect a client whose queue overflows. Called once after construction
        /// (it breaks the cycle with the transport adapter, which needs the outbox as its listener). Null means none.
        /// </summary>
        /// <param name="requester">The disconnect seam, or null.</param>
        public void SetDisconnectRequester(IClientDisconnectRequester requester)
        {
            _disconnectRequester = requester;
        }

        /// <summary>Allocates the client's storage. Adding a known id does nothing.</summary>
        /// <param name="clientId">The client's connection id.</param>
        public void AddClient(uint clientId)
        {
            if (_clientsById.ContainsKey(clientId))
            {
                return;
            }

            var state = new ClientState(clientId);
            _clientsById.Add(clientId, state);
            _clients.Add(state);
        }

        /// <summary>
        /// Discards the client's queue and storage. An unknown id does nothing. Safe to call from inside
        /// <see cref="Enqueue"/> and from inside the send seam during <see cref="Flush"/>.
        /// </summary>
        /// <param name="clientId">The client's connection id.</param>
        public void RemoveClient(uint clientId)
        {
            if (!_clientsById.TryGetValue(clientId, out ClientState state))
            {
                return;
            }

            _clientsById.Remove(clientId);
            state.IsRemoved = true;
            if (_flushing)
            {
                // The flush loop is walking the list; it drops removed states when it finishes.
                _listDirty = true;
                return;
            }

            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_clients[i], state))
                {
                    _clients.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Queues a message for a client; copies <paramref name="body"/> before it returns. Never throws.
        /// Refusals (unknown client, bad type, oversize body, full queue) are logged and queue nothing.
        /// </summary>
        /// <param name="clientId">The connection that receives the message.</param>
        /// <param name="messageTypeId">The wire message type; must be in 0xE000-0xEFFF and have a routing row.</param>
        /// <param name="body">The encoded body, at most 512 bytes; valid only during the call.</param>
        /// <param name="isCapExempt">True when the message is exempt from the priority-path cap.</param>
        public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
        {
            try
            {
                EnqueueCore(clientId, messageTypeId, body, isCapExempt);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} EnqueueThrew: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// The Outbound step: for every client, sends this tick's messages (exempt first, then up to 8 non-exempt)
        /// in order, each as the 10-byte envelope plus the body. The sequence number is issued here, in send order.
        /// A throwing send seam is logged once per client per flush and does not stop other clients or the
        /// client's remaining messages.
        /// </summary>
        /// <param name="currentTick">The tick number written into each envelope.</param>
        public void Flush(uint currentTick)
        {
            if (_flushing)
            {
                return;
            }

            _flushing = true;
            _warnedCount = 0;
            try
            {
                int count = _clients.Count;
                for (int i = 0; i < count; i++)
                {
                    ClientState state = _clients[i];
                    if (state.IsRemoved)
                    {
                        continue;
                    }

                    try
                    {
                        FlushClient(state, currentTick);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"{LOG_PREFIX} FlushThrew: clientId={state.ClientId} - {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            finally
            {
                _flushing = false;
                DropRemovedClients();
            }
        }

        // Clients removed while the flush was walking the list.
        private void DropRemovedClients()
        {
            if (!_listDirty)
            {
                return;
            }

            _listDirty = false;
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if (_clients[i].IsRemoved)
                {
                    _clients.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// The client's sequence counter (CCR-1: one per connection). The later batch-path story must draw its
        /// numbers from this instance so every message the server sends a client shares one counter.
        /// </summary>
        /// <param name="clientId">The client's connection id.</param>
        /// <param name="counter">The counter, or null when the client is unknown.</param>
        /// <returns>True when the client exists.</returns>
        public bool TryGetSequenceCounter(uint clientId, out ConnectionSequenceCounter counter)
        {
            if (_clientsById.TryGetValue(clientId, out ClientState state))
            {
                counter = state.Counter;
                return true;
            }

            counter = null;
            return false;
        }

        private void EnqueueCore(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
        {
            if (!_clientsById.TryGetValue(clientId, out ClientState state))
            {
                WarnUnknownClient(clientId, messageTypeId);
                return;
            }

            int length = body.Length;
            if (!TryAcceptMessage(clientId, messageTypeId, length, out NetworkChannel channel))
            {
                return;
            }

            if (!TryMakeRoom(state, length))
            {
                RejectFull(clientId, state, messageTypeId, length);
                // The disconnect request may have removed the client: nothing of it is touched after this.
                return;
            }

            state.FreeCount--;
            int index = state.FreeIndices[state.FreeCount];
            body.CopyTo(new Span<byte>(state.Arena, state.Tail, length));
            state.EntryOffset[index] = state.Tail;
            state.EntryLength[index] = length;
            state.EntryTypeId[index] = messageTypeId;
            state.EntryChannel[index] = channel;
            state.EntryLive[index] = true;
            state.Tail += length;
            state.LiveBytes += length;
            state.LiveCount++;
            state.Queue.Enqueue(index, isCapExempt);
        }

        // The checks that do not depend on the client's queue: priority range, routing row, body size.
        private bool TryAcceptMessage(uint clientId, ushort messageTypeId, int length, out NetworkChannel channel)
        {
            channel = default;
            if (messageTypeId < PRIORITY_RANGE_FIRST || messageTypeId > PRIORITY_RANGE_LAST)
            {
                Debug.LogError($"{LOG_PREFIX} MessageTypeRejected: clientId={clientId} - type 0x{messageTypeId:X4} is outside the priority range 0xE000-0xEFFF; not sent.");
                return false;
            }

            if (!_routingLookup(messageTypeId, out MessageRoutingEntry row))
            {
                Debug.LogError($"{LOG_PREFIX} MessageTypeRejected: clientId={clientId} - type 0x{messageTypeId:X4} has no routing row; not sent.");
                return false;
            }

            if (length > RUBatchWriter.MAX_MESSAGE_BODY_BYTES)
            {
                Debug.LogError($"{LOG_PREFIX} BodyTooLarge: clientId={clientId} - type 0x{messageTypeId:X4} body is {length} bytes, the most is {RUBatchWriter.MAX_MESSAGE_BODY_BYTES}; not sent.");
                return false;
            }

            channel = row.Channel;
            return true;
        }

        // True when an entry and `length` bytes at the tail are available, compacting the arena if that frees them.
        private bool TryMakeRoom(ClientState state, int length)
        {
            if (state.LiveCount == 0)
            {
                state.Tail = 0;
            }

            if (state.LiveCount >= OUTBOX_MAX_QUEUED_MESSAGES)
            {
                return false;
            }

            if (length <= OUTBOX_ARENA_BYTES - state.Tail)
            {
                return true;
            }

            if (state.LiveBytes + length > OUTBOX_ARENA_BYTES)
            {
                return false;
            }

            Compact(state);
            return true;
        }

        // Logs and requests the disconnect on the first refusal only; later refusals of the same client are silent.
        private void RejectFull(uint clientId, ClientState state, ushort messageTypeId, int length)
        {
            if (state.DisconnectRequested)
            {
                return;
            }

            state.DisconnectRequested = true;
            IClientDisconnectRequester requester = _disconnectRequester;

            string outcome = requester == null
                ? "no disconnect requester is set, so the client is not disconnected"
                : "disconnect requested";
            Debug.LogError($"{LOG_PREFIX} QueueFull: clientId={clientId} - type 0x{messageTypeId:X4}, {length} bytes not queued; "
                + $"{state.LiveCount} messages and {state.LiveBytes} bytes queued; {outcome}.");

            if (requester == null)
            {
                return;
            }

            try
            {
                // May re-enter RemoveClient.
                requester.Disconnect(clientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} DisconnectRequestThrew: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Moves the live entries to the front of the arena, keeping their offset order. Insertion sort on a
        // fixed scratch array: at most 128 entries, and it only runs when the tail is full.
        private void Compact(ClientState state)
        {
            int n = CollectLiveEntriesByOffset(state);
            int cursor = 0;
            for (int k = 0; k < n; k++)
            {
                int index = _compactScratch[k];
                int length = state.EntryLength[index];
                int offset = state.EntryOffset[index];
                if (offset != cursor)
                {
                    // Buffer.BlockCopy handles overlapping ranges; the move is always toward the front.
                    Buffer.BlockCopy(state.Arena, offset, state.Arena, cursor, length);
                    state.EntryOffset[index] = cursor;
                }
                cursor += length;
            }

            state.Tail = cursor;
        }

        // Fills _compactScratch with the live entry indices in offset order and returns how many there are.
        private int CollectLiveEntriesByOffset(ClientState state)
        {
            int n = 0;
            for (int i = 0; i < OUTBOX_MAX_QUEUED_MESSAGES; i++)
            {
                if (state.EntryLive[i])
                {
                    _compactScratch[n] = i;
                    n++;
                }
            }

            for (int i = 1; i < n; i++)
            {
                int item = _compactScratch[i];
                int offset = state.EntryOffset[item];
                int j = i - 1;
                while (j >= 0 && state.EntryOffset[_compactScratch[j]] > offset)
                {
                    _compactScratch[j + 1] = _compactScratch[j];
                    j--;
                }
                _compactScratch[j + 1] = item;
            }

            return n;
        }

        private void FlushClient(ClientState state, uint currentTick)
        {
            state.Queue.Flush(currentTick, state.FlushList);
            List<QueuedMessage<int>> list = state.FlushList;
            bool failureLogged = false;
            int next = 0;

            try
            {
                while (next < list.Count)
                {
                    int index = list[next].Payload;
                    next++;
                    SendEntry(state, index, currentTick, ref failureLogged);
                    if (state.IsRemoved)
                    {
                        // Removed from inside the send: the rest of its messages are discarded with it.
                        return;
                    }
                }
            }
            finally
            {
                // Entries are left only after an exception or a removal: the queue no longer holds them, so
                // their space is given back.
                for (int i = next; i < list.Count; i++)
                {
                    ReleaseEntry(state, list[i].Payload);
                }

                list.Clear();
                if (state.LiveCount == 0)
                {
                    state.Tail = 0;
                }
            }
        }

        private void SendEntry(ClientState state, int index, uint currentTick, ref bool failureLogged)
        {
            int length = state.EntryLength[index];
            int offset = state.EntryOffset[index];
            ushort messageTypeId = state.EntryTypeId[index];
            NetworkChannel channel = state.EntryChannel[index];

            // Released before anything else, so the state is consistent if the seam re-enters the outbox
            // or a step below throws. The bytes stay in the arena until the copy two lines down.
            ReleaseEntry(state, index);

            var envelope = new ServerMessageEnvelope(messageTypeId, state.Counter.IssueNext(), currentTick);
            MessageEnvelopeCodec.Write(new Span<byte>(_sendBuffer, 0, ServerMessageEnvelope.WireSize), in envelope);
            Buffer.BlockCopy(state.Arena, offset, _sendBuffer, ServerMessageEnvelope.WireSize, length);

            try
            {
                _sendSeam.Send(state.ClientId, new ReadOnlySpan<byte>(_sendBuffer, 0, ServerMessageEnvelope.WireSize + length), channel);
            }
            catch (Exception ex)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    Debug.LogError($"{LOG_PREFIX} SendThrew: clientId={state.ClientId} - {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void ReleaseEntry(ClientState state, int index)
        {
            state.EntryLive[index] = false;
            state.LiveBytes -= state.EntryLength[index];
            state.LiveCount--;
            state.FreeIndices[state.FreeCount] = index;
            state.FreeCount++;
        }

        private void WarnUnknownClient(uint clientId, ushort messageTypeId)
        {
            for (int i = 0; i < _warnedCount; i++)
            {
                if (_warnedIds[i] == clientId)
                {
                    return;
                }
            }

            if (_warnedCount >= MAX_TRACKED_WARNINGS)
            {
                return;
            }

            _warnedIds[_warnedCount] = clientId;
            _warnedCount++;
            Debug.LogWarning($"{LOG_PREFIX} UnknownClient: clientId={clientId} - type 0x{messageTypeId:X4} dropped; the client was never added or has been removed.");
        }
    }
}
