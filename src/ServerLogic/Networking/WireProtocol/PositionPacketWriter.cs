using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Serializes one client's per-tick Path 2b-ii Position U-U packet (CR-NET-7.7,
    /// <c>TICK_BATCH_UU = 0x0102</c>): the 12-byte batch header followed by
    /// <see cref="EntityPositionUpdate"/> sub-messages, sorted ascending by <c>EntityID</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Serialization order:</b> entries are sorted ascending by <see cref="EntityPositionUpdate.EntityId"/>
    /// before writing (CR-NET-7.7), via <see cref="Array.Sort{T}(T[], Comparison{T})"/> — never
    /// LINQ's <c>OrderBy</c> (forbidden on the serialization hot path, CR-NET-7.8).
    /// </para>
    /// <para>
    /// <b>Overflow policy:</b> if the sorted set would exceed <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/>,
    /// entries are dropped highest-<c>EntityID</c>-first — i.e. the ascending-sorted list is
    /// truncated from the tail, consistently across all clients (CR-NET-7.7). This is the
    /// opposite of <see cref="CycleBroadcastPacketWriter"/>'s policy: position data self-corrects
    /// on the next tick, so silent truncation (rather than throwing) is the documented behavior
    /// here. A <c>BatchOverflow</c> anomaly is logged (<see cref="Debug.LogWarning"/>) when
    /// truncation occurs.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = new byte[600];
    /// int bytesWritten = PositionPacketWriter.Write(buffer, sequenceNumber: 42u, tickNumber: 1000u, entries: positionEntries, clientIdForLogging: 7u);
    /// </code>
    /// </example>
    public static class PositionPacketWriter
    {
        /// <summary>Batch-header <c>MessageTypeID</c> for the per-tick Position U-U packet (Path 2b-ii, CR-NET-7.7).</summary>
        public const ushort TICK_BATCH_UU = 0x0102;

        /// <summary>
        /// The maximum number of <see cref="EntityPositionUpdate"/> entries that fit in a single
        /// packet: <c>⌊(MAX_MESSAGE_BODY_BYTES − BatchHeaderCodec.HeaderSize) / EntityPositionUpdate.BatchSize⌋</c>
        /// = ⌊500/14⌋ = 35.
        /// </summary>
        public const int MaxEntriesPerPacket = (RUBatchWriter.MAX_MESSAGE_BODY_BYTES - BatchHeaderCodec.HeaderSize) / EntityPositionUpdate.BatchSize;

        /// <summary>
        /// Serializes the Position packet for one client for a single tick into
        /// <paramref name="destination"/> at offset 0.
        /// </summary>
        /// <param name="destination">The destination buffer (e.g. a <see cref="ClientBufferSet.PositionBuffer"/> slot).</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this packet.</param>
        /// <param name="tickNumber">The server tick this packet was authored on.</param>
        /// <param name="entries">Every zone entity's position other than the receiving client, in any order. May be <see langword="null"/> (treated as empty).</param>
        /// <param name="clientIdForLogging">The destination client's ID, included in the anomaly log message for traceability.</param>
        /// <returns>The total number of bytes written to <paramref name="destination"/>.</returns>
        /// <example>
        /// <code>
        /// int bytesWritten = PositionPacketWriter.Write(buffer, 42u, 1000u, entries, clientIdForLogging: 7u);
        /// </code>
        /// </example>
        public static int Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, IReadOnlyList<EntityPositionUpdate> entries, uint clientIdForLogging)
        {
            entries ??= Array.Empty<EntityPositionUpdate>();

            var sorted = new EntityPositionUpdate[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                sorted[i] = entries[i];
            }

            Array.Sort(sorted, CompareByEntityIdAscending);

            int includedCount = sorted.Length;
            if (sorted.Length > MaxEntriesPerPacket)
            {
                int droppedCount = sorted.Length - MaxEntriesPerPacket;
                includedCount = MaxEntriesPerPacket;
                Debug.LogWarning($"[PositionPacketWriter] BatchOverflow: clientId={clientIdForLogging}, tick={tickNumber}, " +
                    $"droppedCount={droppedCount} EntityPositionUpdate entries dropped highest-EntityID-first (post ascending sort).");
            }

            var envelope = new ServerMessageEnvelope(TICK_BATCH_UU, sequenceNumber, tickNumber);
            BatchHeaderCodec.Write(destination, in envelope, (ushort)includedCount);
            int offset = BatchHeaderCodec.HeaderSize;

            for (int i = 0; i < includedCount; i++)
            {
                EntityPositionUpdate entry = sorted[i];
                offset += BatchSubMessageCodec.WriteEntityPositionUpdate(destination.Slice(offset), in entry);
            }

            return offset;
        }

        private static int CompareByEntityIdAscending(EntityPositionUpdate a, EntityPositionUpdate b)
            => a.EntityId.RawValue.CompareTo(b.EntityId.RawValue);
    }
}
