using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encode/decode for the 12-byte batch-packet header shared by all three per-tick batch types
    /// (<see cref="RUBatchWriter"/> / <c>TICK_BATCH_RU</c>, <see cref="CycleBroadcastPacketWriter"/> /
    /// <c>TICK_BATCH_UU_CYCLE</c>, <see cref="PositionPacketWriter"/> / <c>TICK_BATCH_UU</c>).
    /// </summary>
    /// <remarks>
    /// The batch header extends the standard 10-byte <see cref="ServerMessageEnvelope"/> (CR-NET-7.1)
    /// with an additional 2-byte <c>uint16</c> sub-message count field appended immediately after —
    /// exclusive to message types in the <c>0x0100–0x01FF</c> batch-header range (CR-NET-7.7).
    /// </remarks>
    /// <example>
    /// <code>
    /// var envelope = new ServerMessageEnvelope(RUBatchWriter.TICK_BATCH_RU, sequenceNumber: 42u, serverTickNumber: 1000u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[BatchHeaderCodec.HeaderSize];
    /// BatchHeaderCodec.Write(buffer, in envelope, subMessageCount: 3);
    /// </code>
    /// </example>
    public static class BatchHeaderCodec
    {
        /// <summary>Total wire size of a batch header: the 10-byte base envelope plus the 2-byte sub-message count (CR-NET-7.7).</summary>
        public const int HeaderSize = ServerMessageEnvelope.WireSize + 2;

        /// <summary>
        /// Writes the 12-byte batch header (10-byte envelope + 2-byte sub-message count) to
        /// <paramref name="destination"/> at offset 0.
        /// </summary>
        /// <example>
        /// <code>
        /// BatchHeaderCodec.Write(buffer, in envelope, subMessageCount: 3);
        /// </code>
        /// </example>
        public static void Write(Span<byte> destination, in ServerMessageEnvelope envelope, ushort subMessageCount)
        {
            MessageEnvelopeCodec.Write(destination, in envelope);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(ServerMessageEnvelope.WireSize, 2), subMessageCount);
        }

        /// <summary>
        /// Attempts to decode a 12-byte batch header from <paramref name="source"/>. Returns
        /// <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="HeaderSize"/> (untrusted network data must never throw on a too-short source).
        /// </summary>
        /// <example>
        /// <code>
        /// if (BatchHeaderCodec.TryRead(receivedBytes, out ServerMessageEnvelope envelope, out ushort subMessageCount))
        /// {
        ///     ReadOnlySpan&lt;byte&gt; firstSubMessage = receivedBytes.Slice(BatchHeaderCodec.HeaderSize);
        /// }
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ServerMessageEnvelope envelope, out ushort subMessageCount)
        {
            if (source.Length < HeaderSize || !MessageEnvelopeCodec.TryRead(source, out envelope))
            {
                envelope = default;
                subMessageCount = 0;
                return false;
            }

            subMessageCount = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(ServerMessageEnvelope.WireSize, 2));
            return true;
        }
    }
}
