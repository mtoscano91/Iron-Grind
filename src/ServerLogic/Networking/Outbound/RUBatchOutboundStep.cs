using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The per-tick Path 2a R-U batch step (Hit Detection Story 003): collects the sub-messages systems queue
    /// during a tick through <see cref="Enqueue"/>, and in <see cref="Flush"/> sends each client one
    /// <see cref="RUBatchWriter"/> packet on <see cref="NetworkChannel.ReliableUnordered"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tick thread only; not thread-safe. <b>Storage.</b> The constructor builds <c>maxClients</c> per-client states,
    /// each with a byte arena of <see cref="RU_ARENA_BYTES"/>, a pending list reserved to
    /// <see cref="RU_MAX_PENDING_MESSAGES"/> and a <see cref="ClientBufferSet"/>. A state is taken on a client's
    /// first <see cref="Enqueue"/> of the tick and given back by <see cref="Flush"/>; the arena resets each flush, so
    /// there is no compaction. <see cref="Enqueue"/> and <see cref="Flush"/> allocate nothing on the success path
    /// (<see cref="RUBatchWriter.Write"/> allocates only when it logs an overflow).
    /// </para>
    /// <para>
    /// <b>Never throws.</b> <see cref="Enqueue"/> refuses <see cref="RUBatchCategory.DamageEvent"/>,
    /// <see cref="RUBatchCategory.GoldSyncEvent"/>, an out-of-range category and a body over
    /// <see cref="RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes"/> with one error, so <see cref="RUBatchWriter.Write"/>
    /// cannot throw on the flush. A client unknown to the outbox is dropped silently (a ghost player is routine).
    /// An empty state pool, a full entry table or a full arena drops the message with at most one warning per tick
    /// for the whole step. An <see cref="Enqueue"/> from inside the send seam during <see cref="Flush"/> is dropped
    /// with one error.
    /// </para>
    /// <para>
    /// <b>Disconnect.</b> This class is not an <c>IClientConnectionListener</c> (the transport takes one listener, the
    /// outbox) and holds nothing between ticks. Pending state lives one tick, so a client that left is found at
    /// <see cref="Enqueue"/> or at <see cref="Flush"/> through <see cref="ClientMessageOutbox.TryGetSequenceCounter"/>;
    /// its messages are discarded without a send. Teardown flushes the outbox only; pending messages are discarded.
    /// </para>
    /// <para>
    /// <b>Empty batch.</b> When one category overflows alone, <see cref="RUBatchWriter.Write"/> evicts all of it and
    /// returns a 12-byte batch with no sub-messages. The sequence number is already issued, so that packet is sent.
    /// The sequence number comes from the outbox's counter, so the one counter per connection (CCR-1) holds.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var step = new RUBatchOutboundStep(sendSeam, outbox, maxClients: 32);
    /// step.Enqueue(7u, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
    /// step.Flush(currentTick);   // after the outbox flush, once per tick
    /// </code>
    /// </example>
    public sealed class RUBatchOutboundStep : IRUBatchCollector, IZoneOutboundStep
    {
        /// <summary>Most body bytes one client may have pending at once (a structural wire limit).</summary>
        public const int RU_ARENA_BYTES = 4096;

        /// <summary>Most sub-messages one client may have pending at once (a structural wire limit).</summary>
        public const int RU_MAX_PENDING_MESSAGES = 128;

        private const string LOG_PREFIX = "[RUBatchOutboundStep]";

        private sealed class State
        {
            public readonly byte[] Arena = new byte[RU_ARENA_BYTES];
            public readonly List<PendingSubMessage> Pending = new List<PendingSubMessage>(RU_MAX_PENDING_MESSAGES);
            public readonly ClientBufferSet Buffers = new ClientBufferSet();
            public int Tail;
            public uint ClientId;
        }

        private readonly IClientMessageSendSeam _seam;
        private readonly ClientMessageOutbox _outbox;
        private readonly State[] _free;
        private readonly State[] _active;
        private readonly Dictionary<uint, State> _byClient;
        private int _freeCount;
        private int _activeCount;
        private bool _warnedFull;
        private bool _flushing;

        /// <summary>Creates the step and pre-allocates every client state.</summary>
        /// <param name="seam">The send seam (the same instance the outbox uses).</param>
        /// <param name="outbox">Provides each client's sequence counter (CCR-1) and tells whether the client exists.</param>
        /// <param name="maxClients">How many clients may have messages pending in one tick (the zone's player capacity).</param>
        /// <exception cref="ArgumentNullException"><paramref name="seam"/> or <paramref name="outbox"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxClients"/> is below 1.</exception>
        public RUBatchOutboundStep(IClientMessageSendSeam seam, ClientMessageOutbox outbox, int maxClients)
        {
            _seam = seam ?? throw new ArgumentNullException(nameof(seam));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            if (maxClients < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxClients), maxClients, "maxClients must be at least 1.");
            }

            _free = new State[maxClients];
            _active = new State[maxClients];
            _byClient = new Dictionary<uint, State>(maxClients);
            for (int i = 0; i < maxClients; i++)
            {
                _free[i] = new State();
            }

            _freeCount = maxClients;
        }

        /// <inheritdoc />
        public void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            try
            {
                EnqueueCore(clientId, category, messageTypeId, body);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} EnqueueThrew: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// The R-U step of the Outbound phase: sends each client with pending messages one batch, then clears all
        /// pending state. A client gone from the outbox is skipped; an exception is logged per client and does not
        /// stop other clients or the clearing.
        /// </summary>
        /// <param name="currentTick">The tick number written into each batch.</param>
        public void Flush(uint currentTick)
        {
            _warnedFull = false;
            if (_activeCount == 0)
            {
                return;
            }

            _flushing = true;
            try
            {
                for (int i = 0; i < _activeCount; i++)
                {
                    SendClient(_active[i], currentTick);
                }
            }
            finally
            {
                _flushing = false;
                for (int i = 0; i < _activeCount; i++)
                {
                    State state = _active[i];
                    state.Pending.Clear();
                    state.Tail = 0;
                    _free[_freeCount] = state;
                    _freeCount++;
                    _active[i] = null;
                }

                _activeCount = 0;
                _byClient.Clear();
            }
        }

        private void SendClient(State state, uint currentTick)
        {
            if (!_outbox.TryGetSequenceCounter(state.ClientId, out ConnectionSequenceCounter counter))
            {
                return;
            }

            try
            {
                uint sequence = counter.IssueNext();
                byte[] buffer = state.Buffers.RUBatchBuffer;
                int bytes = RUBatchWriter.Write(new Span<byte>(buffer), sequence, currentTick, null, null, state.Pending, state.ClientId);
                _seam.Send(state.ClientId, new ReadOnlySpan<byte>(buffer, 0, bytes), NetworkChannel.ReliableUnordered);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} SendThrew: clientId={state.ClientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void EnqueueCore(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            if (_flushing)
            {
                // Called from inside the send seam: the end of the flush would clear this message anyway.
                Debug.LogError($"{LOG_PREFIX} EnqueueDuringFlush: clientId={clientId} - type 0x{messageTypeId:X4} not queued.");
                return;
            }

            int length = body.Length;
            if (category == RUBatchCategory.DamageEvent
                || category == RUBatchCategory.GoldSyncEvent
                || (int)category > (int)RUBatchCategory.HitCheckResult
                || length > RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes)
            {
                Debug.LogError($"{LOG_PREFIX} MessageRefused: clientId={clientId} - category {(int)category}, type 0x{messageTypeId:X4}, {length} bytes; "
                    + $"only the eight opaque categories are accepted and a body is at most {RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes} bytes; not queued.");
                return;
            }

            if (!_outbox.TryGetSequenceCounter(clientId, out _))
            {
                return;
            }

            if (!_byClient.TryGetValue(clientId, out State state))
            {
                if (_freeCount == 0)
                {
                    WarnFull(clientId, messageTypeId, "no free client state");
                    return;
                }

                _freeCount--;
                state = _free[_freeCount];
                _free[_freeCount] = null;
                state.ClientId = clientId;
                state.Tail = 0;
                _byClient.Add(clientId, state);
                _active[_activeCount] = state;
                _activeCount++;
            }

            if (state.Pending.Count >= RU_MAX_PENDING_MESSAGES)
            {
                WarnFull(clientId, messageTypeId, "entry table full");
                return;
            }

            if (length > RU_ARENA_BYTES - state.Tail)
            {
                WarnFull(clientId, messageTypeId, "arena full");
                return;
            }

            int offset = state.Tail;
            body.CopyTo(new Span<byte>(state.Arena, offset, length));
            state.Tail = offset + length;
            state.Pending.Add(new PendingSubMessage(category, messageTypeId, new ReadOnlyMemory<byte>(state.Arena, offset, length)));
        }

        // At most one warning per tick for the whole step.
        private void WarnFull(uint clientId, ushort messageTypeId, string reason)
        {
            if (_warnedFull)
            {
                return;
            }

            _warnedFull = true;
            Debug.LogWarning($"{LOG_PREFIX} BatchFull: clientId={clientId} - type 0x{messageTypeId:X4} dropped ({reason}); further drops this tick are not logged.");
        }
    }
}
