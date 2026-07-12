using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Serializes one client's per-tick Path 2b-i CycleBroadcast U-U packet (CR-NET-7.7,
    /// <c>TICK_BATCH_UU_CYCLE = 0x0103</c>): the 12-byte batch header followed by one
    /// <see cref="CycleTimerBroadcast"/> sub-message per zone entity other than the receiving
    /// client.
    /// </summary>
    /// <remarks>
    /// <b>Never dropped:</b> <see cref="CycleTimerBroadcast"/> drives the Rhythm Mastery core
    /// gameplay pillar and must never be truncated. At <see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/>
    /// = 50, this packet never overflows: header(12) + 49 × <see cref="CycleTimerBroadcast.BatchSize"/>(10)
    /// = 502 bytes ≤ <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/> (512). If the caller
    /// supplies enough entries to exceed the budget anyway (a <c>MAX_PLAYERS_PER_ZONE</c>
    /// misconfiguration per the GDD's own tuning-knob cross-check, not a runtime network
    /// condition), this method does <i>not</i> silently truncate — silently dropping any
    /// <see cref="CycleTimerBroadcast"/> entry would violate the "never dropped" invariant, and
    /// truncating without signaling would hide a real configuration bug. It logs a critical
    /// anomaly (<see cref="Debug.LogError"/>) and throws <see cref="InvalidOperationException"/>
    /// instead — mirroring the zero-write-guard precedent in <see cref="WireIdCodec"/> (a
    /// caller/config sizing bug, not a recoverable condition).
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = new byte[600];
    /// int bytesWritten = CycleBroadcastPacketWriter.Write(buffer, sequenceNumber: 42u, tickNumber: 1000u, entries: cycleEntries, clientIdForLogging: 7u);
    /// </code>
    /// </example>
    public static class CycleBroadcastPacketWriter
    {
        /// <summary>Batch-header <c>MessageTypeID</c> for the per-tick CycleBroadcast U-U packet (Path 2b-i, CR-NET-7.7).</summary>
        public const ushort TICK_BATCH_UU_CYCLE = 0x0103;

        /// <summary>
        /// Serializes the CycleBroadcast packet for one client for a single tick into
        /// <paramref name="destination"/> at offset 0.
        /// </summary>
        /// <param name="destination">The destination buffer (e.g. a <see cref="ClientBufferSet.CycleBroadcastBuffer"/> slot).</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this packet.</param>
        /// <param name="tickNumber">The server tick this packet was authored on.</param>
        /// <param name="entries">Every zone entity's cycle timer other than the receiving client. May be <see langword="null"/> (treated as empty).</param>
        /// <param name="clientIdForLogging">The destination client's ID, included in the critical-anomaly log message for traceability.</param>
        /// <returns>The total number of bytes written to <paramref name="destination"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="entries"/> would produce a packet exceeding
        /// <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/> — see class remarks.
        /// </exception>
        /// <example>
        /// <code>
        /// int bytesWritten = CycleBroadcastPacketWriter.Write(buffer, 42u, 1000u, entries, clientIdForLogging: 7u);
        /// </code>
        /// </example>
        public static int Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, IReadOnlyList<CycleTimerBroadcast> entries, uint clientIdForLogging)
        {
            entries ??= Array.Empty<CycleTimerBroadcast>();
            int totalBytes = BatchHeaderCodec.HeaderSize + (entries.Count * CycleTimerBroadcast.BatchSize);

            if (totalBytes > RUBatchWriter.MAX_MESSAGE_BODY_BYTES)
            {
                Debug.LogError($"[CycleBroadcastPacketWriter] CycleBroadcastNeverDroppedOverflow (CRITICAL): clientId={clientIdForLogging}, " +
                    $"tick={tickNumber}, entries.Count={entries.Count} would produce {totalBytes} bytes, exceeding " +
                    $"MAX_MESSAGE_BODY_BYTES ({RUBatchWriter.MAX_MESSAGE_BODY_BYTES}) — CycleTimerBroadcast must never be dropped or truncated; " +
                    "this indicates a MAX_PLAYERS_PER_ZONE misconfiguration, not a runtime network condition.");
                throw new InvalidOperationException(
                    $"CycleBroadcastNeverDroppedOverflow: {entries.Count} entries would produce {totalBytes} bytes, " +
                    $"exceeding MAX_MESSAGE_BODY_BYTES ({RUBatchWriter.MAX_MESSAGE_BODY_BYTES}).");
            }

            var envelope = new ServerMessageEnvelope(TICK_BATCH_UU_CYCLE, sequenceNumber, tickNumber);
            BatchHeaderCodec.Write(destination, in envelope, (ushort)entries.Count);
            int offset = BatchHeaderCodec.HeaderSize;

            for (int i = 0; i < entries.Count; i++)
            {
                CycleTimerBroadcast entry = entries[i];
                offset += BatchSubMessageCodec.WriteCycleTimerBroadcast(destination.Slice(offset), in entry);
            }

            return offset;
        }
    }
}
