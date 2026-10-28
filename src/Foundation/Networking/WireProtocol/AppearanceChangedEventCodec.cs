using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="AppearanceChangedEvent"/> (5-byte body, little-endian; networking-wire-protocol.md
    /// <c>AppearanceChangedEvent</c>; equipment-system.md CR-EQS-11). No allocation.
    /// </summary>
    public static class AppearanceChangedEventCodec
    {
        /// <summary>Writes the 5-byte body at offset 0 of <paramref name="destination"/>; returns 5.</summary>
        /// <param name="destination">At least <see cref="AppearanceChangedEvent.BodySize"/> bytes.</param>
        /// <param name="entityId">The raw entity id of the character whose gear changed.</param>
        /// <param name="equipmentAppearanceFlags">The new absolute appearance byte (CR-EQS-11).</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than <see cref="AppearanceChangedEvent.BodySize"/> bytes.</exception>
        public static int WriteBody(Span<byte> destination, uint entityId, byte equipmentAppearanceFlags)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), entityId);
            destination[4] = equipmentAppearanceFlags;
            return AppearanceChangedEvent.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (outputs zero) if <paramref name="body"/> is not
        /// exactly <see cref="AppearanceChangedEvent.BodySize"/> bytes. Every flags value is accepted.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint entityId, out byte equipmentAppearanceFlags)
        {
            if (body.Length != AppearanceChangedEvent.BodySize)
            {
                entityId = 0;
                equipmentAppearanceFlags = 0;
                return false;
            }

            entityId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            equipmentAppearanceFlags = body[4];
            return true;
        }
    }
}
