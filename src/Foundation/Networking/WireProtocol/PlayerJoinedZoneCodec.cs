using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="PlayerJoinedZone"/> (16 to 40 bytes, little-endian). The only allocation is the
    /// decoded name. Zone Instancing Story 006.
    /// </summary>
    public static class PlayerJoinedZoneCodec
    {
        /// <summary>
        /// Writes the body at offset 0 and returns the bytes written (16 to 40). The name is cut to 24 UTF-8 bytes at a
        /// character boundary. Throws <see cref="InvalidOperationException"/> (via <see cref="WireIdCodec"/>, CR-NET-7.3)
        /// for <see cref="EntityID.Invalid"/>, and <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="PlayerJoinedZone.MaxBodySize"/>.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in PlayerJoinedZone message)
        {
            destination = destination.Slice(0, PlayerJoinedZone.MaxBodySize);
            WireIdCodec.SerializeEntityId(destination, message.EntityId, PlayerJoinedZone.MessageTypeId, nameof(message.EntityId));
            int offset = WireStringCodec.WriteString(destination, WireIdCodec.EntityIdWireSize, message.CharacterName, PlayerJoinedZone.MaxNameBytes);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), message.Level);
            offset += 4;
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset, 2), message.PosX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset + 2, 2), message.PosY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset + 4, 2), message.PosZ);
            return offset + 6;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (default message) if the body is truncated or the
        /// name length exceeds 24. Trailing bytes are ignored. Never throws.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out PlayerJoinedZone message)
        {
            message = default;

            if (body.Length < PlayerJoinedZone.MinBodySize
                || !WireStringCodec.TryReadString(body, WireIdCodec.EntityIdWireSize, PlayerJoinedZone.MaxNameBytes, out string name, out int offset)
                || body.Length - offset < 4 + 6)
            {
                return false;
            }

            message = new PlayerJoinedZone(
                WireIdCodec.DeserializeEntityId(body),
                name,
                BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, 4)),
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(offset + 4, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(offset + 6, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(offset + 8, 2)));
            return true;
        }
    }
}
