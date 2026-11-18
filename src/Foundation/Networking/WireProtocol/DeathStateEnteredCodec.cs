using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="DeathStateEntered"/> (5-byte body, little-endian; networking-wire-protocol.md
    /// <c>DeathStateEntered</c>). No allocation.
    /// </summary>
    public static class DeathStateEnteredCodec
    {
        /// <summary>Writes the 5-byte body at offset 0 of <paramref name="destination"/>; returns 5.</summary>
        /// <param name="destination">At least <see cref="DeathStateEntered.BodySize"/> bytes.</param>
        /// <param name="entityId">The raw entity id of the dying player.</param>
        /// <param name="respawnTimerSeconds">Whole seconds until the respawn.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too short.</exception>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; body = stackalloc byte[DeathStateEntered.BodySize];
        /// DeathStateEnteredCodec.WriteBody(body, entity.RawValue, 7);
        /// </code>
        /// </example>
        public static int WriteBody(Span<byte> destination, uint entityId, byte respawnTimerSeconds)
        {
            destination = destination.Slice(0, DeathStateEntered.BodySize); // throws before any write when too short
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), entityId);
            destination[4] = respawnTimerSeconds;
            return DeathStateEntered.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (outputs zero) if <paramref name="body"/> is not
        /// exactly <see cref="DeathStateEntered.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="entityId">The decoded raw entity id.</param>
        /// <param name="respawnTimerSeconds">The decoded timer.</param>
        /// <returns>True when the body length is valid.</returns>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint entityId, out byte respawnTimerSeconds)
        {
            if (body.Length != DeathStateEntered.BodySize)
            {
                entityId = 0;
                respawnTimerSeconds = 0;
                return false;
            }

            entityId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            respawnTimerSeconds = body[4];
            return true;
        }
    }
}
