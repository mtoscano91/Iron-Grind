using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The per-tick Position U-U broadcast (Movement Story 009, CR-MOV-6 step 6; wire protocol Path 2b-ii). Every
    /// occupied player slot registered in Movement is a receiver; its packet holds an <see cref="EntityPositionUpdate"/>
    /// for every other registered player, sorted ascending by EntityID, and is sent unreliably.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tick thread only. Allocates nothing after construction: one <see cref="ClientBufferSet"/> per player slot and one
    /// scratch array are built in the constructor; the sort is an insertion sort over at most MAX_PLAYERS_PER_ZONE entries.
    /// A receiver with no other registered player gets no packet; a client unknown to the outbox is skipped silently.
    /// </para>
    /// <para>
    /// <b>Overflow.</b> When any receiver drops entries (more than <see cref="PositionPacketWriter.MaxEntriesPerPacket"/>
    /// others) one <c>BatchOverflow</c> warning is logged when the overflow starts; the flag clears on the first tick
    /// without drops. A throwing send seam logs one error per client per flush and the other clients are still sent.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var step = new PositionBroadcastStep(movement, pool, sendSeam, outbox);
    /// step.Flush(currentTick);   // after the outbox flush, once per tick
    /// </code>
    /// </example>
    public sealed class PositionBroadcastStep : IZoneOutboundStep
    {
        private const string LOG_PREFIX = "[PositionBroadcastStep]";

        private readonly MovementService _movement;
        private readonly ZoneSlotPool _pool;
        private readonly IClientMessageSendSeam _seam;
        private readonly ClientMessageOutbox _outbox;
        private readonly ClientBufferSet[] _buffers;
        private readonly EntityPositionUpdate[] _scratch;
        private readonly EntityID[] _receiverIds;
        private readonly uint[] _receiverClients;
        private readonly int[] _receiverSlots;
        private bool _overflowing;

        /// <summary>Creates the step and pre-allocates every buffer.</summary>
        /// <param name="movement">Source of the committed positions.</param>
        /// <param name="pool">Slot pool: who is in the zone and which client id they use.</param>
        /// <param name="seam">The send seam (the same instance the outbox uses).</param>
        /// <param name="outbox">Provides each client's sequence counter (CCR-1).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public PositionBroadcastStep(MovementService movement, ZoneSlotPool pool, IClientMessageSendSeam seam,
            ClientMessageOutbox outbox)
        {
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _seam = seam ?? throw new ArgumentNullException(nameof(seam));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

            int slots = pool.MaxPlayers;
            _buffers = new ClientBufferSet[slots];
            for (int i = 0; i < slots; i++)
            {
                _buffers[i] = new ClientBufferSet();
            }

            _scratch = new EntityPositionUpdate[slots];
            _receiverIds = new EntityID[slots];
            _receiverClients = new uint[slots];
            _receiverSlots = new int[slots];
        }

        /// <summary>True while a BatchOverflow is active (some receiver dropped entries on the last flush).</summary>
        public bool IsOverflowing => _overflowing;

        /// <summary>Builds and sends this tick's position packets.</summary>
        /// <param name="currentTick">The tick number written into each packet.</param>
        public void Flush(uint currentTick)
        {
            int count = CollectSorted();
            int maxDropped = 0;
            if (count > 1)
            {
                for (int i = 0; i < count; i++)
                {
                    int dropped = SendTo(i, count, currentTick);
                    if (dropped > maxDropped)
                    {
                        maxDropped = dropped;
                    }
                }
            }

            UpdateOverflow(currentTick, maxDropped);
        }

        // Fills the scratch and receiver arrays with every registered player, sorted ascending by EntityID. A ghost
        // (disconnected, slot held) is still collected: others keep seeing it, and it receives nothing itself because
        // the outbox no longer knows its client.
        private int CollectSorted()
        {
            int count = 0;
            int slots = _pool.MaxPlayers;
            for (int slot = 0; slot < slots; slot++)
            {
                if (!_pool.TryGetPlayerAtSlot(slot, out EntityID id, out uint clientId)
                    || !_movement.TryGetPosition(id, out Vector3 position))
                {
                    continue;
                }

                var entry = new EntityPositionUpdate(id, ToWire(position.x), ToWire(position.y), ToWire(position.z));
                // Insertion sort ascending by EntityID, keeping the receiver arrays parallel.
                int j = count - 1;
                while (j >= 0 && _scratch[j].EntityId.RawValue > id.RawValue)
                {
                    _scratch[j + 1] = _scratch[j];
                    _receiverIds[j + 1] = _receiverIds[j];
                    _receiverClients[j + 1] = _receiverClients[j];
                    _receiverSlots[j + 1] = _receiverSlots[j];
                    j--;
                }

                _scratch[j + 1] = entry;
                _receiverIds[j + 1] = id;
                _receiverClients[j + 1] = clientId;
                _receiverSlots[j + 1] = slot;
                count++;
            }

            return count;
        }

        private int SendTo(int index, int count, uint currentTick)
        {
            uint clientId = _receiverClients[index];
            if (!_outbox.TryGetSequenceCounter(clientId, out ConnectionSequenceCounter counter))
            {
                return 0;
            }

            uint sequence = counter.IssueNext();
            byte[] buffer = _buffers[_receiverSlots[index]].PositionBuffer;
            int bytes = PositionPacketWriter.Write(new Span<byte>(buffer), sequence, currentTick, _scratch, count,
                _receiverIds[index], out int droppedCount);
            try
            {
                _seam.Send(clientId, new ReadOnlySpan<byte>(buffer, 0, bytes), NetworkChannel.Unreliable);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} SendThrew: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }

            return droppedCount;
        }

        private void UpdateOverflow(uint currentTick, int maxDropped)
        {
            if (maxDropped <= 0)
            {
                _overflowing = false;
                return;
            }

            if (_overflowing)
            {
                return;
            }

            _overflowing = true;
            Debug.LogWarning($"{LOG_PREFIX} BatchOverflow: tick={currentTick}, droppedCount={maxDropped}");
        }

        // Same encoding as MovementZoneEntityStateSource: clamp to the wire range, then round(m x 100).
        private static short ToWire(float meters)
        {
            float clamped = Mathf.Clamp(meters, -WireFixedPointCodec.MaxPositionMeters, WireFixedPointCodec.MaxPositionMeters);
            return (short)Mathf.RoundToInt(clamped * 100f);
        }
    }
}
