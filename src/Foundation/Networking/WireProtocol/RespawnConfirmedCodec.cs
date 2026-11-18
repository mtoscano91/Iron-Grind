using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="RespawnConfirmed"/> (4-byte body, little-endian; networking-wire-protocol.md
    /// <c>RespawnConfirmed</c>). No allocation.
    /// </summary>
    public static class RespawnConfirmedCodec
    {
        /// <summary>Writes the 4-byte body at offset 0 of <paramref name="destination"/>; returns 4.</summary>
        /// <param name="destination">At least <see cref="RespawnConfirmed.BodySize"/> bytes.</param>
        /// <param name="entityId">The raw entity id of the respawned player.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too short.</exception>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; body = stackalloc byte[RespawnConfirmed.BodySize];
        /// RespawnConfirmedCodec.WriteBody(body, entity.RawValue);
        /// </code>
        /// </example>
        public static int WriteBody(Span<byte> destination, uint entityId)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), entityId);
            return RespawnConfirmed.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (outputs zero) if <paramref name="body"/> is not
        /// exactly <see cref="RespawnConfirmed.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="entityId">The decoded raw entity id.</param>
        /// <returns>True when the body length is valid.</returns>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint entityId)
        {
            if (body.Length != RespawnConfirmed.BodySize)
            {
                entityId = 0;
                return false;
            }

            entityId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            return true;
        }
    }
}
