using System;
using System.Buffers.Binary;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EntityRespawned"/> (10-byte body, little-endian; networking-wire-protocol.md
    /// <c>EntityRespawned</c>). No allocation. The position is clamped to +-327.67 m per axis by
    /// <see cref="WireFixedPointCodec.EncodePosition"/>, which logs one aggregated warning when it clamps.
    /// </summary>
    public static class EntityRespawnedCodec
    {
        /// <summary>Writes the 10-byte body at offset 0 of <paramref name="destination"/>; returns 10.</summary>
        /// <param name="destination">At least <see cref="EntityRespawned.BodySize"/> bytes.</param>
        /// <param name="entityId">The raw entity id of the respawned player.</param>
        /// <param name="position">The authoritative respawn position.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too short.</exception>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; body = stackalloc byte[EntityRespawned.BodySize];
        /// EntityRespawnedCodec.WriteBody(body, entity.RawValue, new Vector3(12.75f, 0f, -85.23f));
        /// </code>
        /// </example>
        public static int WriteBody(Span<byte> destination, uint entityId, Vector3 position)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), entityId);
            WireFixedPointCodec.EncodePosition(destination.Slice(4, WireFixedPointCodec.PositionWireSize), position);
            return EntityRespawned.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (outputs zero) if <paramref name="body"/> is not
        /// exactly <see cref="EntityRespawned.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="entityId">The decoded raw entity id.</param>
        /// <param name="position">The decoded position (centimetre precision).</param>
        /// <returns>True when the body length is valid.</returns>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint entityId, out Vector3 position)
        {
            if (body.Length != EntityRespawned.BodySize)
            {
                entityId = 0;
                position = default;
                return false;
            }

            entityId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            position = WireFixedPointCodec.DecodePosition(body.Slice(4, WireFixedPointCodec.PositionWireSize));
            return true;
        }
    }
}
