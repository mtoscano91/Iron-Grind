using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="OpenNPCInteraction"/> (4-byte body: <c>uint npcId</c>). No allocation.</summary>
    public static class OpenNPCInteractionCodec
    {
        /// <summary>Writes the 4-byte body at offset 0 of <paramref name="destination"/>; returns 4.</summary>
        /// <param name="destination">At least <see cref="OpenNPCInteraction.BodySize"/> bytes.</param>
        /// <param name="npcId">The NPC to open a session with (raw <see cref="uint"/>, not validated here).</param>
        public static int WriteBody(Span<byte> destination, uint npcId)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), npcId);
            return OpenNPCInteraction.BodySize;
        }

        /// <summary>Attempts to decode the body; <see langword="false"/> if shorter than 4 bytes. Extra bytes ignored.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint npcId)
        {
            if (body.Length < OpenNPCInteraction.BodySize)
            {
                npcId = 0;
                return false;
            }

            npcId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            return true;
        }
    }
}
