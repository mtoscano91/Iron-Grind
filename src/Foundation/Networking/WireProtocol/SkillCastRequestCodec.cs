using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="SkillCastRequest"/> (16-byte body, four little-endian <c>uint</c>s). No allocation.
    /// </summary>
    /// <example>
    /// <code>
    /// if (SkillCastRequestCodec.TryReadBody(body, out SkillCastRequest request)) { /* use request */ }
    /// </code>
    /// </example>
    public static class SkillCastRequestCodec
    {
        /// <summary>Writes the 16-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="SkillCastRequest.BodySize"/> bytes.</param>
        /// <param name="message">The request to write.</param>
        /// <returns><see cref="SkillCastRequest.BodySize"/>, always.</returns>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 16 bytes; nothing is written.</exception>
        public static int WriteBody(Span<byte> destination, in SkillCastRequest message)
        {
            if (destination.Length < SkillCastRequest.BodySize)
            {
                throw new ArgumentException("destination must hold at least SkillCastRequest.BodySize (16) bytes.", nameof(destination));
            }

            Span<byte> body = destination.Slice(0, SkillCastRequest.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(0, 4), message.CasterEntityId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(4, 4), message.SkillId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(8, 4), message.TargetEntityId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(12, 4), message.ClientTickNumber);
            return SkillCastRequest.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> unless <paramref name="body"/> is exactly
        /// <see cref="SkillCastRequest.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="message">The decoded request, or default on failure.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SkillCastRequest message)
        {
            if (body.Length != SkillCastRequest.BodySize)
            {
                message = default;
                return false;
            }

            message = new SkillCastRequest(
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12, 4)));
            return true;
        }
    }
}
