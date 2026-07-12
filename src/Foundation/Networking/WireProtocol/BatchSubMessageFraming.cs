using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// The 4-byte per-sub-message framing header (<c>uint16 length</c> + <c>uint16 MessageTypeID</c>)
    /// shared by every R-U/U-U batch sub-message (CR-NET-7.7): <c>[uint16 length][MessageTypeID][payload]</c>.
    /// Factored out of <see cref="RUBatchWriter"/>, <see cref="CycleBroadcastPacketWriter"/>, and
    /// <see cref="PositionPacketWriter"/> because all three packet types share this exact framing.
    /// </summary>
    /// <remarks>
    /// The <c>uint16 length</c> field records the byte count of <c>MessageTypeID + payload</c> —
    /// not including the 2-byte length field itself (CR-NET-7.7). Receivers use this length to
    /// skip past unrecognized sub-message types for forward compatibility; that skip-forward
    /// behavior belongs to a future decode-side story and is not implemented here.
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[4 + bodySize];
    /// int headerBytes = BatchSubMessageFraming.WriteHeader(buffer, (ushort)bodySize, messageTypeId: 0x0301);
    /// // caller now writes `bodySize` body bytes starting at buffer.Slice(headerBytes)
    /// </code>
    /// </example>
    public static class BatchSubMessageFraming
    {
        /// <summary>Wire size of the per-sub-message framing header: 2-byte length prefix + 2-byte MessageTypeID.</summary>
        public const int SubMessageHeaderSize = 4;

        /// <summary>
        /// Writes the 4-byte sub-message header to <paramref name="destination"/> at offset 0:
        /// a <c>uint16</c> length field (<c>2 + bodySize</c>, per CR-NET-7.7's "MessageTypeID + payload"
        /// definition) followed by the 2-byte <paramref name="messageTypeId"/>. Returns
        /// <see cref="SubMessageHeaderSize"/> (4) — the caller writes the body starting at that offset.
        /// </summary>
        /// <example>
        /// <code>
        /// int offset = BatchSubMessageFraming.WriteHeader(buffer, bodySize: 14, messageTypeId: 0x0301);
        /// // offset == 4; write the 14-byte body at buffer.Slice(4)
        /// </code>
        /// </example>
        public static int WriteHeader(Span<byte> destination, ushort bodySize, ushort messageTypeId)
        {
            ushort lengthFieldValue = (ushort)(2 + bodySize);
            BinaryPrimitives.WriteUInt16LittleEndian(destination, lengthFieldValue);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), messageTypeId);
            return SubMessageHeaderSize;
        }

        /// <summary>
        /// Attempts to decode the 4-byte sub-message header from <paramref name="source"/>. Returns
        /// <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="SubMessageHeaderSize"/> (untrusted network data must never throw on a
        /// too-short source — see <see cref="MessageEnvelopeCodec"/>'s documented Write/TryRead
        /// convention, which this mirrors).
        /// </summary>
        /// <param name="source">The buffer positioned at the start of a sub-message.</param>
        /// <param name="bodySize">The decoded body size (the length field's value minus the 2-byte MessageTypeID it includes).</param>
        /// <param name="messageTypeId">The decoded <c>MessageTypeID</c>.</param>
        /// <example>
        /// <code>
        /// if (BatchSubMessageFraming.TryReadHeader(source, out ushort bodySize, out ushort messageTypeId))
        /// {
        ///     ReadOnlySpan&lt;byte&gt; body = source.Slice(BatchSubMessageFraming.SubMessageHeaderSize, bodySize);
        /// }
        /// </code>
        /// </example>
        public static bool TryReadHeader(ReadOnlySpan<byte> source, out ushort bodySize, out ushort messageTypeId)
        {
            if (source.Length < SubMessageHeaderSize)
            {
                bodySize = 0;
                messageTypeId = 0;
                return false;
            }

            ushort lengthFieldValue = BinaryPrimitives.ReadUInt16LittleEndian(source);
            messageTypeId = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(2, 2));
            bodySize = (ushort)(lengthFieldValue - 2);
            return true;
        }
    }
}
